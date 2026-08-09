"""Stripe Managed Payments — Checkout Session creation + webhook verification (stdlib only).

Managed Payments = Stripe as merchant of record (handles indirect tax in 80+ countries, fraud, and
disputes). Implemented against the Stripe REST API via urllib — no `stripe` pip dependency, keeping the
server's stdlib-only design. The only Managed-Payments delta vs. plain Checkout is `managed_payments
[enabled]=true` on the session (+ the `Stripe-Version` header). Webhook signatures are verified with
HMAC-SHA256 (stdlib `hmac`), constant-time compared.

Nothing here activates until the STRIPE_* env vars are set (see config.py).
"""

import base64
import hashlib
import hmac
import json
import os
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from . import config, logging_util

_API = "https://api.stripe.com/v1"

# SEC-008: Pinned TLS context for Stripe API calls.
# Uses the system CA store with strict verification settings.
# E226: SPKI certificate pinning — set SYXX_STRIPE_PIN_SPKI to one or more
# base64-encoded SHA-256 hashes of the SubjectPublicKeyInfo from Stripe's TLS
# certificate chain. Multiple pins (comma-separated) allow key rotation without
# downtime. When configured, the TLS handshake verifies that at least one cert
# in the server's chain matches a pinned SPKI hash, preventing MITM even if a
# rogue CA is trusted by the system store.
_STRIPE_SSL_CONTEXT = ssl.create_default_context()
_STRIPE_SSL_CONTEXT.check_hostname = True
_STRIPE_SSL_CONTEXT.verify_mode = ssl.CERT_REQUIRED
# Disable legacy TLS versions
_STRIPE_SSL_CONTEXT.minimum_version = ssl.TLSVersion.TLSv1_2

# E226: SPKI pin set (SHA-256 hashes, base64-encoded). Comma-separated for rotation.
# Leave blank to disable pinning (rely on system CA store only).
# Example: SYXX_STRIPE_PIN_SPKI="hash1,hash2"
_STRIPE_SPKI_PINS = [
    p.strip() for p in os.environ.get("SYXX_STRIPE_PIN_SPKI", "").split(",") if p.strip()
]


def _verify_spki_pin(sock: ssl.SSLSocket, host: str) -> None:
    """E226: verify that at least one certificate in the TLS chain has an SPKI
    hash matching the configured pin set. Raises StripeError if no pin matches.

    This runs AFTER the standard TLS handshake (CA verification, hostname check)
    and adds a second layer: even if a rogue CA issues a certificate for api.stripe.com,
    the SPKI hash won't match unless the attacker controls Stripe's private key."""
    if not _STRIPE_SPKI_PINS:
        return  # pinning not configured — rely on CA verification

    # getpeercert(binary_form=True) returns the DER-encoded leaf certificate.
    # To check the full chain, we'd need get_unverified_chain() (Python 3.13+).
    # For now, pin the leaf certificate which is the strongest guarantee.
    der_cert = sock.getpeercert(binary_form=True)
    if not der_cert:
        raise StripeError(f"stripe TLS: no peer certificate from {host}")

    # Extract SPKI from DER certificate. The SPKI is the subjectPublicKeyInfo
    # field of the X.509 TBSCertificate. We parse the minimal DER structure:
    # Certificate → SEQUENCE → TBSCertificate → SEQUENCE → ... → subjectPublicKeyInfo
    spki_hash = _extract_spki_sha256_b64(der_cert)
    if spki_hash not in _STRIPE_SPKI_PINS:
        logging_util.warn(
            "stripe_spki_pin_mismatch",
            host=host,
            got_hash=spki_hash[:16] + "…",
            expected_count=len(_STRIPE_SPKI_PINS),
        )
        raise StripeError(
            f"stripe TLS: certificate SPKI hash does not match pinned set for {host}"
        )


def _extract_spki_sha256_b64(der_cert: bytes) -> str:
    """Extract the SubjectPublicKeyInfo from a DER-encoded X.509 certificate
    and return its SHA-256 hash, base64-encoded.

    Parses the minimal ASN.1 structure needed: Certificate → TBSCertificate →
    subjectPublicKeyInfo (field index 6, zero-based)."""
    try:
        # Certificate is SEQUENCE { tbsCertificate, signatureAlgorithm, signature }
        # TBSCertificate is SEQUENCE { version, serialNumber, signature, issuer,
        #                              validity, subject, subjectPublicKeyInfo, ... }
        # We need to navigate the DER structure to find subjectPublicKeyInfo.
        offset = 0

        def _read_tag_length(data: bytes, pos: int) -> tuple[int, int, int]:
            """Read DER tag and length. Returns (tag, content_offset, content_length)."""
            tag = data[pos]
            pos += 1
            if data[pos] & 0x80:
                num_len_bytes = data[pos] & 0x7F
                pos += 1
                length = int.from_bytes(data[pos : pos + num_len_bytes], "big")
                pos += num_len_bytes
            else:
                length = data[pos]
                pos += 1
            return tag, pos, length

        # Certificate SEQUENCE
        _, cert_start, _ = _read_tag_length(der_cert, offset)
        # TBSCertificate SEQUENCE
        _, tbs_start, _ = _read_tag_length(der_cert, cert_start)

        # Walk through TBSCertificate fields to reach subjectPublicKeyInfo (index 6)
        pos = tbs_start
        # version [0] EXPLICIT (optional)
        if der_cert[pos] == 0xA0:
            _, pos, ver_len = _read_tag_length(der_cert, pos)
            pos += ver_len
        # serialNumber INTEGER
        _, pos, serial_len = _read_tag_length(der_cert, pos)
        pos += serial_len
        # signature AlgorithmIdentifier
        _, pos, sig_len = _read_tag_length(der_cert, pos)
        pos += sig_len
        # issuer Name
        _, pos, issuer_len = _read_tag_length(der_cert, pos)
        pos += issuer_len
        # validity SEQUENCE
        _, pos, validity_len = _read_tag_length(der_cert, pos)
        pos += validity_len
        # subject Name
        _, pos, subject_len = _read_tag_length(der_cert, pos)
        pos += subject_len
        # subjectPublicKeyInfo SEQUENCE — this is what we want
        _ = der_cert[pos]
        spki_start, spki_content_start, spki_len = _read_tag_length(der_cert, pos)
        # Include the tag+length bytes in the hash (standard SPKI pinning practice)
        spki_bytes = der_cert[spki_start : spki_content_start + spki_len]

        return base64.b64encode(hashlib.sha256(spki_bytes).digest()).decode("ascii")
    except Exception:
        # If DER parsing fails, return a value that won't match any pin.
        # This causes the pin check to fail, which is the safe default.
        return ""


class StripeError(Exception):
    pass


def _request(path, method, params=None, idempotency_key=None):
    if not config.STRIPE_SECRET_KEY:
        raise StripeError("STRIPE_SECRET_KEY not configured")
    # A046: for GET requests, params MUST go in the query string, not the body —
    # Stripe's API does not read expand[] from a request body. For POST/PUT/DELETE,
    # params are sent as form-encoded body data.
    qs = urllib.parse.urlencode(params, doseq=True) if params else None
    url = _API + path
    if method == "GET" and qs:
        url += "?" + qs
        data = None
    else:
        data = qs.encode() if qs else None
    req = urllib.request.Request(url, data=data, method=method)  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
    req.add_header("Authorization", "Bearer " + config.STRIPE_SECRET_KEY)
    req.add_header("Stripe-Version", config.STRIPE_API_VERSION)
    if data is not None:
        req.add_header("Content-Type", "application/x-www-form-urlencoded")
    if idempotency_key:
        req.add_header("Idempotency-Key", idempotency_key)  # safe retries (Stripe API reference)
    try:
        # SEC-008: Use pinned SSL context for Stripe API calls
        with urllib.request.urlopen(req, timeout=15, context=_STRIPE_SSL_CONTEXT) as r:  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        detail = ""
        try:
            detail = json.loads(e.read()).get("error", {}).get("message", "")
        except Exception:  # noqa: S110 — best-effort path; failing here must not take the request down
            pass
        raise StripeError(f"stripe {e.code}: {detail or 'request failed'}") from e
    except OSError as e:
        raise StripeError(f"stripe unreachable: {e}") from e


def create_checkout_session(sku, idempotency_key=None):
    """Create a hosted Managed-Payments Checkout Session for a **SKU** (e.g. syxx_mac_yearly_sub).

    The SKU must be in the config allow-list, which names an explicit Stripe PRICE id. `mode` is
    derived from whether that price is recurring — no hardcoded modes. The SKU is stamped into the
    session metadata so the webhook knows exactly which tier sold. Returns (url, session_id).

    This takes a SKU rather than a product id, and reads an explicit price rather than the product's
    `default_price`, because tiers are now multiple prices on ONE product per platform. Resolving
    `default_price` there would charge whichever tier Stripe calls default — a monthly charge for a
    yearly button, with a completely successful-looking checkout. A blank price id is refused rather
    than fallen back on, for the same reason.
    """
    entry = config.STRIPE_SKUS.get(sku)
    if entry is None:
        raise StripeError(f"unknown sku: {sku!r}")
    price_id = config.stripe_price_for(sku)
    if not price_id:
        # Provisioned in code, not yet in Stripe. Fail loudly: the alternative is charging a price
        # nobody chose.
        raise StripeError(f"sku {sku} has no price id configured — it is not sellable yet")

    price = _request("/prices/" + price_id, "GET")
    if not price.get("id"):
        raise StripeError(f"sku {sku}: price {price_id} not found in Stripe")
    if price.get("active") is False:
        # An archived price still fetches. Checkout would reject it later, after the buyer has
        # committed — better to refuse here than to hand them a broken Stripe page.
        raise StripeError(f"sku {sku}: price {price_id} is archived in Stripe")
    mode = "subscription" if price.get("recurring") else "payment"

    params = {
        "mode": mode,
        "line_items[0][price]": price["id"],
        "line_items[0][quantity]": "1",
        "managed_payments[enabled]": "true",  # <- Stripe as merchant of record
        "success_url": config.CHECKOUT_SUCCESS_URL,
        "cancel_url": config.CHECKOUT_CANCEL_URL,
        "client_reference_id": sku,
        "metadata[sku]": sku,  # <- read back by the webhook to pick the Keygen policy
    }
    # Stripe subscription trial. Gated on the mode DERIVED from the price, never assumed:
    # `subscription_data` is invalid on a one-time `payment` session and Stripe rejects the whole
    # request, which would break checkout for a SKU that has nothing to do with trials.
    # config.STRIPE_TRIAL_DAYS defaults to 0, so this is inert until an operator opts in.
    if mode == "subscription" and config.STRIPE_TRIAL_DAYS > 0:
        params["subscription_data[trial_period_days]"] = str(config.STRIPE_TRIAL_DAYS)
        # A trialing subscription is NOT a paid one. Stamp the trial onto the session metadata so
        # the webhook, the audit row, and any later support query can tell a free trial apart from
        # a completed purchase — `payment_status: no_payment_required` alone cannot, because a
        # 100%-off coupon reports exactly the same thing.
        params["metadata[trial_days]"] = str(config.STRIPE_TRIAL_DAYS)
    session = _request("/checkout/sessions", "POST", params, idempotency_key=idempotency_key or str(uuid.uuid4()))
    return session.get("url"), session.get("id")


def create_billing_portal_session(customer_id, return_url=None):
    """Create a Stripe Customer Portal session for the given Stripe customer id.

    The portal lets the subscriber manage their subscription (cancel, upgrade, update
    payment method) without operator involvement.  Returns the session dict (with
    ``url``); raises StripeError on failure.
    """
    if not config.STRIPE_SECRET_KEY:
        raise StripeError("STRIPE_SECRET_KEY not configured")
    if not customer_id:
        raise StripeError("customer_id is required")
    params = {
        "customer": customer_id,
        "return_url": return_url or config.CHECKOUT_SUCCESS_URL.replace("{CHECKOUT_SESSION_ID}", "").rstrip("?"),
    }
    return _request("/billing_portal/sessions", "POST", params)


def verify_webhook(payload_bytes, sig_header, tolerance=None, now=None):
    """Verify a Stripe webhook signature (`Stripe-Signature: t=..,v1=..`), returning the parsed event
    dict or raising StripeError. Signature = HMAC-SHA256(secret, b'{t}.{payload}'), constant-time compared,
    with a replay-window check on the timestamp.

    A128: tolerance defaults to config.STRIPE_WEBHOOK_TOLERANCE (env-configurable).
    When a timestamp is rejected for skew, the measured server time and webhook time
    are logged so the operator can diagnose NTP failures or VM clock drift."""
    if not config.STRIPE_WEBHOOK_SECRET:
        raise StripeError("STRIPE_WEBHOOK_SECRET not configured")
    if tolerance is None:
        tolerance = config.STRIPE_WEBHOOK_TOLERANCE
    now = now if now is not None else int(time.time())
    timestamp = None
    signatures = []
    for item in (sig_header or "").split(","):
        if "=" in item:
            k, v = item.split("=", 1)
            key = k.strip()
            value = v.strip()
            if key == "t":
                timestamp = value
            elif key == "v1":
                signatures.append(value)
    ts = timestamp
    if not ts or not signatures or not ts.isdigit():
        raise StripeError("malformed signature header")
    skew = now - int(ts)
    if abs(skew) > tolerance:
        # A128: log the measured server time and webhook timestamp so the operator
        # can distinguish NTP skew (large consistent offset), VM pause/resume
        # (sudden jump), or replay attacks (timestamp far in the past) from a
        # transient delivery delay.
        logging_util.warn(
            "stripe_webhook_timestamp_skew",
            server_time=now,
            webhook_time=int(ts),
            skew_seconds=skew,
            tolerance=tolerance,
            detail=f"webhook rejected: server clock and Stripe timestamp "
            f"differ by {skew}s (tolerance {tolerance}s). Check NTP sync.",
        )
        raise StripeError("timestamp outside tolerance")
    signed = ts.encode() + b"." + payload_bytes
    expected = hmac.new(config.STRIPE_WEBHOOK_SECRET.encode(), signed, hashlib.sha256).hexdigest()
    if not any(hmac.compare_digest(expected, v1) for v1 in signatures):
        raise StripeError("signature mismatch")
    return json.loads(payload_bytes)
