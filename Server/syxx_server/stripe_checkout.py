"""Stripe Managed Payments — Checkout Session creation + webhook verification (stdlib only).

Managed Payments = Stripe as merchant of record (handles indirect tax in 80+ countries, fraud, and
disputes). Implemented against the Stripe REST API via urllib — no `stripe` pip dependency, keeping the
server's stdlib-only design. The only Managed-Payments delta vs. plain Checkout is `managed_payments
[enabled]=true` on the session (+ the `Stripe-Version` header). Webhook signatures are verified with
HMAC-SHA256 (stdlib `hmac`), constant-time compared.

Nothing here activates until the STRIPE_* env vars are set (see config.py).
"""

import hashlib
import hmac
import json
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

from . import config, logging_util

_API = "https://api.stripe.com/v1"


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
        with urllib.request.urlopen(req, timeout=15) as r:  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
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
