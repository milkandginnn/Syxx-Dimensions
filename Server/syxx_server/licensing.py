"""Licensing and entitlement (feature: Pro-subscription authorization).

This is the paywall. A request is entitled only if it presents either:
  * a valid Keygen license key (the SAME key the app validates), or
  * a valid offline/admin license key, or
  * a server-minted session token (issued after a license validated).

Session tokens are stateless HMAC-signed blobs so verifying them needs no round
trip to Keygen. The website obtains one via /api/auth/activate and attaches it
to every live query, so a visitor with no license can never run a paid search.

E018 — Key algorithm summary (cross-platform):
  * SERVER (this file): Ed25519 — verifies Keygen.sh response signatures via
    the Keygen-Signature header.  The public key is a 32-byte Ed25519 key
    (hex-encoded, from config.KEYGEN_PUBLIC_KEY).  Requires the `cryptography`
    library.
  * macOS CLIENT (LicenseManager.swift): P-256 (NIST secp256r1) ECDSA —
    verifies Keygen.sh response signatures using CryptoKit's P256.Signing API.
    The public key is a 91-byte SubjectPublicKeyInfo DER blob (hex-encoded).
  * WINDOWS CLIENT (LicenseManager.cs): P-256 (NIST secp256r1) ECDSA —
    verifies Keygen.sh response signatures using .NET's ECDsa with
    DSASignatureFormat.Rfc3279DerSequence.  Same DER key format as macOS.

  The server and clients use DIFFERENT signature algorithms because Keygen.sh
  offers both Ed25519 (preferred for server-to-server, higher performance) and
  P-256/ECDSA (for client SDKs using platform-native crypto like CryptoKit and
  .NET ECDsa).  Both provide equivalent security against forged responses.

A021 (Fix 1): validate-key responses from Keygen are cryptographically verified
using Ed25519 response signatures (Keygen-Signature header) when
config.KEYGEN_PUBLIC_KEY is set.  A MITM with a forged body is denied even over
compromised TLS.
"""

import base64
import datetime
import email.utils
import hashlib
import hmac
import json
import os
import re
import ssl
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

from . import config, db, logging_util

# SEC-008: Pinned TLS context for Keygen API calls.
# Strict verification with TLS 1.2+ minimum.
_KEYGEN_SSL_CONTEXT = ssl.create_default_context()
_KEYGEN_SSL_CONTEXT.check_hostname = True
_KEYGEN_SSL_CONTEXT.verify_mode = ssl.CERT_REQUIRED
_KEYGEN_SSL_CONTEXT.minimum_version = ssl.TLSVersion.TLSv1_2

# A056: a validate-key response is trusted only if its Date header is within this
# many seconds of the server clock. The Ed25519 signature covers the Date header, so
# without this bound a captured, validly-signed response (e.g. from when the key was
# still active) could be replayed forever to bypass expiry - the very anti-MITM
# signature check would be partially defeated. +/- 5 minutes tolerates clock drift.
_KEYGEN_DATE_SKEW_SECONDS = 300

# E045: Clock-skew tolerance for session token expiry verification.
# Wall clock (time.time()) is intentional here — session tokens must survive server
# restarts and be verified against an absolute timestamp embedded at mint time.
# time.monotonic() is only usable for intra-process intervals and must NOT be used.
# This constant provides tolerance for NTP adjustments or minor clock drift between
# the minting host and the verifying host.
_SESSION_CLOCK_SKEW_TOLERANCE = 30  # seconds of tolerance for clock drift

# S11: how often to re-log the "unprovisioned, denying every key" warning. Denying a
# valid key with NO signal at all is the worst of both worlds — the customer is locked
# out and the operator has nothing to search for. preflight says it once at boot, which
# can be days stale by the time a customer complains; this says it again while the
# denials are actually happening. Throttled so a caller spraying bogus keys cannot use
# it to flood the log (at most one line per interval, whatever the request rate).
_UNPROVISIONED_WARN_INTERVAL = 300
_unprovisioned_warned_at = 0.0

# Ed25519 response signature verification (A021 Fix 1).  The Keygen API signs
# every successful (2xx–3xx) response with the account's Ed25519 private key.
# When KEYGEN_PUBLIC_KEY is configured, we verify that signature before trusting
# meta.valid to detect forged responses from a compromised CA or substituted
# trust anchor.
try:
    from cryptography.exceptions import InvalidSignature  # type: ignore[unresolved-import]
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey  # type: ignore[unresolved-import]

    _HAS_ED25519 = True
except ImportError:
    _HAS_ED25519 = False

# Semaphore to cap concurrent Keygen validation requests so a deluge of
# activation attempts doesn't exhaust system resources (SRV-019).
_keygen_sem = threading.Semaphore(config.KEYGEN_MAX_WORKERS)

_secret = None


def secret():
    """Lazy-load the session signing secret (HMAC key for session tokens and attestations)."""
    # SECURITY NOTE (E035): The session signing secret is loaded from db.get_or_create_secret(),
    # which reads/writes the 'session_secret' key in the SQLite kv table as a hex string in
    # plaintext. To encrypt the secret at rest, set SYXX_DB_ENCRYPTION_KEY and implement
    # kv_set_encrypted / kv_get_encrypted helpers in db.py using AES-GCM keyed from that env var.
    # Until then, the secret is protected only by filesystem permissions (0600) on the database.
    global _secret
    if _secret is None:
        _secret = db.get_or_create_secret()
    return _secret


class Entitlement:
    """Resolved entitlement for a subject — plan tier and its provenance."""

    def __init__(self, subject, plan, source):
        self.subject = subject  # opaque id (license key hash or session subject)
        self.plan = plan
        self.source = source  # 'keygen' | 'offline' | 'session'

    def as_dict(self):
        """Serialise to a JSON-friendly dict for API responses."""
        return {"subject": self.subject, "plan": self.plan, "source": self.source}


def _subject_id(key):
    """Derive a stable opaque subject identifier from a license key."""
    return "lic_" + hashlib.sha256(key.encode("utf-8")).hexdigest()[:16]


# --- Session tokens (stateless, HMAC) --------------------------------------


def _sign(payload_bytes):
    """HMAC-SHA256 sign *payload_bytes* with the session secret."""
    return hmac.new(secret(), payload_bytes, hashlib.sha256).digest()


def mint_session(subject, plan, ttl=None):
    """Mint a stateless HMAC-signed session token for *subject* with the given *plan*."""
    ttl = ttl if ttl is not None else config.SESSION_TTL
    payload = {"sub": subject, "plan": plan, "exp": int(time.time()) + ttl}
    raw = json.dumps(payload, separators=(",", ":"), sort_keys=True).encode("utf-8")
    body = base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")
    sig = base64.urlsafe_b64encode(_sign(raw)).decode("ascii").rstrip("=")
    return body + "." + sig, payload["exp"]


def verify_session(token):
    """Verify and decode a session token; return the payload dict or None on any failure."""
    try:
        body, sig = token.split(".", 1)
    except ValueError:
        return None
    try:
        raw = base64.urlsafe_b64decode(body + "=" * (-len(body) % 4))
    except (ValueError, TypeError):
        return None
    expected = base64.urlsafe_b64encode(_sign(raw)).decode("ascii").rstrip("=")
    if not hmac.compare_digest(sig, expected):
        return None
    try:
        payload = json.loads(raw)
        exp = int(payload.get("exp", 0))
    except (TypeError, ValueError):
        return None
    # E045: accept tokens that are up to _SESSION_CLOCK_SKEW_TOLERANCE seconds expired,
    # to handle NTP adjustments and minor clock drift. Wall clock is intentional for
    # cross-restart validity; tolerance handles clock skew without weakening revocation.
    if exp < int(time.time()) - _SESSION_CLOCK_SKEW_TOLERANCE:
        return None
    return Entitlement(payload.get("sub", ""), payload.get("plan", "pro"), "session")


# --- Keygen response signature verification (A021 Fix 1) --------------------


def _warn_unprovisioned():
    """S11: log the unprovisioned-and-denying state, at most once per interval.

    Returns True when it actually logged, so a test can observe the throttle."""
    global _unprovisioned_warned_at
    now = time.monotonic()
    if now - _unprovisioned_warned_at < _UNPROVISIONED_WARN_INTERVAL:
        return False
    _unprovisioned_warned_at = now
    logging_util.warn(
        "keygen_public_key_unset_denying",
        msg="SYXX_KEYGEN_PUBLIC_KEY is not set: the Keygen response signature cannot be "
        "verified, so EVERY license validation is denied — including valid keys. "
        "Set it in /etc/syxx/syxx.env and restart. This is fail-closed by design "
        "(D040/A021); it is not a transient error and it will not self-heal.",
    )
    return True


# Sentinel: preflight checks passed, proceed with verification.
_PROCEED = object()


def _key_signature_preflight():
    """Check config and crypto availability for Keygen signature verification.

    D040/A021: fail CLOSED in production when the public key or crypto library
    is absent. Returns _PROCEED to continue, True (dev skip), or None (cannot verify).
    """
    if not config.KEYGEN_PUBLIC_KEY:
        if os.environ.get("SYXX_DEV_MODE") != "1":
            _warn_unprovisioned()
            return None
        return True
    if not _HAS_ED25519:
        if os.environ.get("SYXX_DEV_MODE") != "1":
            logging_util.warn(
                "keygen_ed25519_unavailable_missing_key", msg="cryptography library not available; licensing denied"
            )
            return None
        logging_util.warn(
            "keygen_ed25519_unavailable",
            msg="cryptography library not available; signature verification skipped (dev mode)",
        )
        return True
    return _PROCEED


def _validate_keygen_date(date_header):
    """Parse and validate the Keygen response Date header.

    A056: rejects stale or far-future dates to prevent replay attacks.
    Returns epoch seconds on success, None on failure (already logged).
    """
    try:
        parsed_dt = email.utils.parsedate_to_datetime(date_header)
        if parsed_dt is None:
            raise ValueError("unparseable Date header")
        if parsed_dt.tzinfo is None:
            parsed_dt = parsed_dt.replace(tzinfo=datetime.UTC)
        resp_epoch = parsed_dt.timestamp()
    except (TypeError, ValueError, OverflowError):
        logging_util.warn("keygen_signature_bad_date")
        return None
    if abs(time.time() - resp_epoch) > _KEYGEN_DATE_SKEW_SECONDS:
        logging_util.warn(
            "keygen_signature_stale_date",
            detail="Date header outside allowed clock skew (possible replay of an old signed response)",
        )
        return None
    return resp_epoch


def _extract_signature_bytes(sig_header):
    """Parse the Keygen-Signature header and decode the base64 signature.

    Returns the raw signature bytes, or None on failure (already logged).
    """
    sig_params = {}
    for m in re.finditer(r'''([^=]+)="([^"]*)"''', sig_header):
        key = m.group(1).strip().lstrip(",").strip()
        sig_params[key] = m.group(2)
    enc_sig = sig_params.get("signature", "")
    if not enc_sig:
        logging_util.warn("keygen_signature_param_missing")
        return None
    try:
        return base64.b64decode(enc_sig)
    except Exception:
        logging_util.warn("keygen_signature_bad_base64")
        return None


def _build_signing_string(body_str, date_header, method, url):
    """Build the RFC http-signatures signing data string.

    Computes SHA-256 digest of the body and constructs the canonical signing
    string from (request-target), host, date, and digest.
    """
    digest_bytes = base64.b64encode(hashlib.sha256(body_str.encode("utf-8")).digest())
    enc_digest = digest_bytes.decode("ascii")
    parsed = urllib.parse.urlparse(url)
    host = parsed.hostname or "api.keygen.sh"
    return (
        f"(request-target): {method} {parsed.path}\nhost: {host}\ndate: {date_header}\ndigest: sha-256={enc_digest}"
    )


def _verify_ed25519(signature_bytes, signing_data):
    """Verify an Ed25519 signature against the configured Keygen public key.

    Returns True (verified), False (forgery), or None (crypto error).
    """
    try:
        public_key_bytes = bytes.fromhex(config.KEYGEN_PUBLIC_KEY)
        public_key = Ed25519PublicKey.from_public_bytes(public_key_bytes)
        public_key.verify(signature_bytes, signing_data.encode("utf-8"))
        return True
    except InvalidSignature:
        logging_util.warn(
            "keygen_signature_invalid", detail="Ed25519 signature verification failed – possible forged response"
        )
        return False
    except Exception as e:
        logging_util.warn("keygen_signature_error", err=str(e))
        return None


def _verify_validate_key_response(body_str, sig_header, digest_header, date_header, method, url):
    """Verify the Ed25519 response signature from Keygen's validate-key endpoint.

    Uses the Ed25519 public key from config.KEYGEN_PUBLIC_KEY (hex-encoded).

    A130: returns a tri-state to distinguish verified/forgery/cannot-verify:
      - True  : signature verified successfully
      - False : forgery detected (InvalidSignature)
      - None  : could not verify (missing config, infrastructure failure, etc.)

    The caller treats None as a transient error (503) and False as a denial (402).
    In dev mode (SYXX_DEV_MODE=1), missing key or crypto lib returns True (skip).
    """
    preflight = _key_signature_preflight()
    if preflight is not _PROCEED:
        return preflight
    if not sig_header or not digest_header or not date_header:
        logging_util.warn("keygen_signature_missing_headers")
        return False
    if _validate_keygen_date(date_header) is None:
        return None
    signature_bytes = _extract_signature_bytes(sig_header)
    if signature_bytes is None:
        return None
    signing_data = _build_signing_string(body_str, date_header, method, url)
    return _verify_ed25519(signature_bytes, signing_data)


# --- License validation ----------------------------------------------------


def _validate_offline(key):
    """D053: validate offline license keys. The static OFFLINE_LICENSE_KEYS set is trusted
    (it's loaded from environment variables at startup, not from the database). The database
    fallback now requires an HMAC signature to prevent unauthorized DB inserts from granting
    entitlement. A DB row without a valid HMAC is rejected as untrusted."""
    if key in config.OFFLINE_LICENSE_KEYS:
        return "pro"
    row = db.license_lookup(key)
    if row and row.get("status") == "active":
        exp = row.get("expires_at")
        if exp is None or int(exp) > int(time.time()):
            # D053: verify the HMAC signature on the DB row. The HMAC is computed over
            # key + status + expires_at + plan using the server secret. An unauthorized
            # DB insert cannot forge this HMAC without knowing the secret.
            stored_hmac = row.get("hmac")
            if not stored_hmac:
                logging_util.warn(
                    "offline_license_missing_hmac", msg="DB license row has no HMAC signature — rejecting as untrusted"
                )
                return None
            expected_hmac = _offline_license_hmac(key, row.get("status"), exp, row.get("plan", "pro"))
            if not hmac.compare_digest(stored_hmac, expected_hmac):
                logging_util.warn(
                    "offline_license_invalid_hmac",
                    msg="DB license row HMAC mismatch — rejecting as tampered or unauthorized",
                )
                return None
            return row.get("plan", "pro")
    return None


def _offline_license_hmac(key, status, expires_at, plan):
    """Compute HMAC-SHA256 over the license row fields using the server secret.
    The HMAC covers key + status + expires_at + plan so an attacker cannot modify
    any field without invalidating the signature."""
    payload = f"{key}|{status}|{expires_at}|{plan}".encode()
    return hmac.new(secret(), payload, hashlib.sha256).hexdigest()


def _validate_keygen(key):
    """Validate a license key against the Keygen API; return status string, None, or ``__transient__``."""
    if not config.KEYGEN_ACCOUNT_ID:
        return None
    # Cap concurrent Keygen HTTP requests so a deluge doesn't exhaust resources (SRV-019).
    if not _keygen_sem.acquire(timeout=config.KEYGEN_TIMEOUT):
        return "__transient__"
    url = None
    data = None
    try:
        # Keygen's by-key validation route is validate-key; plain .../actions/validate is not a
        # real endpoint (returns 404 NOT_FOUND), which made every license read as invalid.
        # The base URL is configurable so test deployments can point at mock servers (SRV-023).
        base = config.KEYGEN_BASE_URL or f"https://api.keygen.sh/v1/accounts/{config.KEYGEN_ACCOUNT_ID}"
        url = base.rstrip("/") + "/licenses/actions/validate-key"
        req_body = json.dumps({"meta": {"key": key}}).encode("utf-8")
        req = urllib.request.Request(url, data=req_body, method="POST")  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
        req.add_header("Content-Type", "application/vnd.api+json")
        req.add_header("Accept", "application/vnd.api+json")
        # SEC-008: Use pinned SSL context for Keygen API calls
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT, context=_KEYGEN_SSL_CONTEXT) as resp:  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
            raw_body = resp.read()
            body_str = raw_body.decode("utf-8", errors="ignore")
            data = json.loads(body_str)
            sig_header = resp.headers.get("Keygen-Signature", "")
            digest_header = resp.headers.get("Digest", "")
            date_header = resp.headers.get("Date", "")

        # A021 Fix 1 + A130: verify the response Ed25519 signature before trusting
        # meta.valid. Tri-state: True=verified, False=forgery, None=cannot verify.
        sig_result = _verify_validate_key_response(
            body_str,
            sig_header,
            digest_header,
            date_header,
            "post",
            url,
        )
        if sig_result is True:
            pass  # signature verified, proceed
        elif sig_result is False:
            return None  # forgery detected — deny
        else:
            return "__transient__"  # cannot verify — 503, not 402
    except urllib.error.HTTPError as e:
        # 401/404 => definitively not a valid key; other codes => transient.
        if e.code in (401, 404):
            return None
        logging_util.warn("keygen_http_error", code=e.code)
        return "__transient__"
    except Exception as e:  # network/timeout: distinguish from 'invalid'
        logging_util.warn("keygen_network_error", err=str(e))
        return "__transient__"
    finally:
        _keygen_sem.release()
    # A125: guard against non-dict JSON responses. If Keygen returns a non-dict
    # body (array, string, number), `(data or {}).get(...)` raises AttributeError
    # because a non-empty list/string is truthy but has no .get(). Treat a non-dict
    # response as invalid rather than crashing into a 500.
    if not isinstance(data, dict):
        logging_util.warn("keygen_non_dict_response", detail=f"expected dict, got {type(data).__name__}")
        return None
    meta = data.get("meta", {})
    if not isinstance(meta, dict) or not meta.get("valid"):
        return None
    # S12: remember the Keygen license resource id for this key's subject. The
    # scheduler's daily status sweep must query /licenses/<id>, and the subject
    # hash is not a Keygen id; without this stored mapping the sweep 404s and
    # would revoke the API keys of every Keygen-licensed subject.
    lic_id = (data.get("data") or {}).get("id")
    if lic_id:
        try:
            db.keygen_license_id_set(_subject_id(key), str(lic_id))
        except Exception as e:
            # Storage failure must not turn a valid key into a denial; the sweep
            # treats a missing stored id as unknown, never as revoked.
            logging_util.warn("keygen_license_id_store_failed", err=str(e))
    return "pro"


def validate_license(key):
    """Return (Entitlement|None, error_str|None).

    error_str is set only for transient failures (so callers can return 503
    rather than 402, distinguishing 'try again' from 'you did not pay').
    """
    if not key or len(key) < 8:
        return None, None
    plan = _validate_offline(key)
    if plan:
        return Entitlement(_subject_id(key), plan, "offline"), None
    result = _validate_keygen(key)
    if result == "__transient__":
        return None, "license validation service unavailable"
    if result:
        return Entitlement(_subject_id(key), result, "keygen"), None
    return None, None


# --- Keygen license status check (for API key sync) -----------------------


def check_keygen_license_status(subject):
    """Check if a Keygen license is still valid by querying the Keygen admin API.

    Returns True if valid, False if revoked/suspended/expired, None on transient
    error or when no Keygen license id is stored for the subject (unknown).
    Used by the periodic API key sync job to revoke keys for invalidated licenses.
    """
    if not config.KEYGEN_ADMIN_TOKEN or not config.KEYGEN_ACCOUNT_ID:
        return None  # cannot check without admin credentials
    # S12: the subject is a local key hash (lic_<sha256[:16]>), NOT a Keygen
    # resource id. The old code queried /licenses/<subject>, which 404s for every
    # real subject; 404 read as 'invalid' and the sweep revoked ALL keys. Query
    # the license id stored at activation instead, and treat a missing stored id
    # as unknown (None) so the sweep skips the subject rather than revoking.
    license_id = db.keygen_license_id_get(subject)
    if not license_id:
        logging_util.warn("keygen_status_no_stored_license_id", subject=subject)
        return None
    base = config.KEYGEN_BASE_URL or f"https://api.keygen.sh/v1/accounts/{config.KEYGEN_ACCOUNT_ID}"
    url = base.rstrip("/") + "/licenses/{}".format(urllib.parse.quote(license_id, safe=""))
    req = urllib.request.Request(url, method="GET")  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
    req.add_header("Authorization", f"Bearer {config.KEYGEN_ADMIN_TOKEN}")
    req.add_header("Accept", "application/vnd.api+json")
    if not _keygen_sem.acquire(timeout=config.KEYGEN_TIMEOUT):
        return None
    try:
        # SEC-008: Use pinned SSL context for Keygen API calls
        with urllib.request.urlopen(req, timeout=config.KEYGEN_TIMEOUT, context=_KEYGEN_SSL_CONTEXT) as resp:  # noqa: S310 — URL is operator config (KEYGEN_BASE_URL / Stripe), never user input
            data = json.loads(resp.read().decode("utf-8", errors="ignore"))
        lic = data.get("data", {})
        attrs = lic.get("attributes", {})
        status = attrs.get("status", "")
        # A179: explicit allowlist/denylist instead of `status == "ACTIVE"`.
        # An unrecognised status (including empty string from schema drift or a
        # newly added Keygen status) is missing data, not grounds for revocation.
        # Treating unknown as False collapsed the tri-state and let a Keygen API
        # change revoke every subject's API keys in a single sweep.
        _VALID_STATUSES = {"ACTIVE"}
        _INVALID_STATUSES = {"EXPIRED", "SUSPENDED", "REVOKED", "BANNED"}
        if status in _VALID_STATUSES:
            return True
        if status in _INVALID_STATUSES:
            return False
        logging_util.warn(
            "keygen_status_unrecognised",
            status=status,
            subject=subject,
            detail="Keygen returned a status not in the known "
            "allowlist or denylist — treating as unknown "
            "(no revocation) rather than collapsing to False.",
        )
        return None
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return False  # license not found = invalid
        logging_util.warn("keygen_status_check_error", code=e.code, subject=subject)
        return None  # transient
    except Exception as e:
        logging_util.warn("keygen_status_check_network_error", err=str(e), subject=subject)
        return None  # transient
    finally:
        _keygen_sem.release()


# --- Request-level entitlement resolution ----------------------------------


def _extract_session_token(cookie_header: str) -> str | None:
    """Extract the syxx_session token value from a Cookie header string."""
    for part in cookie_header.split(";"):
        stripped = part.strip()
        if stripped.startswith("syxx_session="):
            token = stripped[13:].strip()
            if token:
                return token
    return None


def entitlement_for(headers, query):
    """Resolve an Entitlement from a request, or None.

    Order: API key (X-Syxx-API-Key), then Bearer session token, then cookie
    session token (SEC-004/HT-005), then X-Syxx-License.
    API keys take precedence because they indicate explicit programmatic access
    with dedicated rate limits and audit trails.
    License keys are not accepted in query strings because URLs are commonly
    written to proxy logs, browser history, and referrer metadata.

    SEC-004/HT-005: Session tokens are now also accepted via httpOnly cookies
    (syxx_session cookie). The cookie is the primary delivery mechanism after
    activation — it cannot be stolen by XSS. The Authorization: Bearer header
    remains supported for backward compatibility and native clients.
    """
    # 1. API key auth: "X-Syxx-API-Key: syxx_..." header (or "apikey " prefix in Authorization)
    api_key = headers.get("X-Syxx-API-Key", "")
    if not api_key:
        auth = headers.get("Authorization", "")
        if auth.lower().startswith("apikey "):
            api_key = auth[7:].strip()
    if api_key:
        import hashlib

        # A021: db is already imported at module level (line 33), so this
        # deferred import was redundant. Removed the unnecessary `from . import db`.
        key_hash = hashlib.sha256(api_key.encode()).hexdigest()
        row = db.api_key_lookup(key_hash)
        if row:
            db.api_key_touch(row["id"])
            return Entitlement(row["subject"], "pro", "api-key"), None

    # 2. Bearer session token
    auth = headers.get("Authorization", "")
    if auth.lower().startswith("bearer "):
        ent = verify_session(auth[7:].strip())
        if ent:
            return ent, None

    # SEC-004/HT-005: Session token from httpOnly cookie
    cookie_header = headers.get("Cookie", "")
    if cookie_header:
        token = _extract_session_token(cookie_header)
        if token:
            ent = verify_session(token)
            if ent:
                return ent, None

    # 3. License key header
    key = headers.get("X-Syxx-License")
    if key:
        return validate_license(key.strip())
    return None, None
