"""Signed one-time leak-token collector (feature: closed-loop leak protocol).

Closes ROADMAP P0-5. Uses Ed25519 asymmetric signatures (CF-01) so attestations
are third-party-verifiable — a user can present the signed attestation + public
key to r/AskNetSec without revealing a shared secret.

  1. /api/leak/token (paid) issues an unguessable one-time token bound to the
     caller's subject and persisted.
  2. The authoritative DNS server records which resolver forwarded the token
     lookup (dns_authoritative.observe -> here).
  3. /api/leak/verify (paid) returns the observed resolvers together with an
     Ed25519 signature the caller can verify with the collector's public key.
"""

import base64
import json
import os
import secrets
import time

from . import config, db, logging_util, resolver

try:
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey  # type: ignore[unresolved-import]
    from cryptography.hazmat.primitives.serialization import (  # type: ignore[unresolved-import]
        Encoding,
        PublicFormat,
        load_der_private_key,
    )

    _HAS_CRYPTO = True
except ImportError:
    _HAS_CRYPTO = False


# ── Collector Ed25519 key management ──────────────────────────────────────
# The collector signs leak attestations with a dedicated Ed25519 keypair
# (independent of the licensing HMAC secret).  Set SYXX_COLLECTOR_PRIVATE_KEY
# to a base64-encoded DER **PKCS#8** Ed25519 private key — what
# load_der_private_key() accepts, i.e. ~48 bytes, NOT the bare 32-byte seed.
# Generate one with _GENERATE_HINT below.  If unset, production fails closed.
#
# This comment used to say "DER ... (32 seed bytes)", which is self-contradictory
# and cost a production operator several rounds: 32 random bytes base64'd looks
# plausible, is accepted by the presence check, and then dies inside OpenSSL with
# "not enough data". State the format the loader actually parses, once, here.

_COLLECTOR_PRIV_KEY = None
_COLLECTOR_PUB_KEY_RAW = None
_COLLECTOR_KEY_ID = None
# A114: the actual time this key was loaded/generated, NOT the current time.
# Returned by /api/pubkey so callers can see when the key was activated, not when
# they happened to fetch it.
_COLLECTOR_KEY_ACTIVATED_AT = None
# A051: tracks whether _ensure_collector_key has been attempted and whether it
# succeeded, so repeated calls don't re-raise the same RuntimeError on every
# request (which the global handler turns into a vague 500 "internal error").
_COLLECTOR_KEY_INITIALIZED = False
_COLLECTOR_KEY_ERROR = None

# A copy-pasteable generator. An error that names a format without showing how to
# produce it is how an operator ends up pasting a bare seed.
_GENERATE_HINT = (
    "Generate one with:  python3 -c 'import base64;"
    "from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey as K;"
    "from cryptography.hazmat.primitives.serialization import Encoding,PrivateFormat,"
    "NoEncryption as N;"
    "print(base64.b64encode(K.generate().private_bytes(Encoding.DER,PrivateFormat.PKCS8,"
    "N())).decode())'"
)


def collector_key_error(env_key):
    """Return why `env_key` is unusable as the collector key, or None if it loads.

    Exists so the boot preflight can PARSE the key rather than merely observe that the
    variable is non-empty. A presence check reports ready on a key that 500s every
    request — the exact false-clean this codebase exists to refuse.
    """
    if not _HAS_CRYPTO:
        return "the 'cryptography' library is not installed — install: pip install cryptography"
    if not env_key:
        return "unset"
    try:
        raw = base64.b64decode(env_key, validate=True)
    except Exception:  # noqa: BLE001 — boundary handler: any exception here must become a reported result, not a crash
        return "not valid base64. " + _GENERATE_HINT
    if len(raw) == 32:
        # The specific mistake the old message invited. Name it exactly.
        return "looks like a bare 32-byte Ed25519 seed, not a DER PKCS#8 key. " + _GENERATE_HINT
    try:
        load_der_private_key(raw, password=None)
    except Exception as exc:  # noqa: BLE001 — boundary handler: any exception here must become a reported result, not a crash
        return f"does not parse as a DER PKCS#8 private key ({exc}). {_GENERATE_HINT}"
    return None


def _ensure_collector_key():
    """Initialise the collector Ed25519 keypair (idempotent).

    Returns True on success, False when the key is unavailable.  The error
    message is stored in ``_COLLECTOR_KEY_ERROR`` for callers to surface as a
    structured response instead of an unhandled 500.

    A051: previously raised RuntimeError on failure, which the global HTTP
    handler caught and flattened to ``{"error": "internal error"}`` — hiding
    the actual diagnostic (missing key, bad format, wrong algorithm) from
    both the operator's logs-at-a-glance and the client.  Now caches the
    outcome so repeated requests get the same answer without re-raising.
    """
    global _COLLECTOR_PRIV_KEY, _COLLECTOR_PUB_KEY_RAW, _COLLECTOR_KEY_ID
    global _COLLECTOR_KEY_ACTIVATED_AT, _COLLECTOR_KEY_INITIALIZED, _COLLECTOR_KEY_ERROR

    # A051: return cached result — don't re-attempt (or re-raise) on every request.
    if _COLLECTOR_KEY_INITIALIZED:
        return _COLLECTOR_KEY_ERROR is None

    if not _HAS_CRYPTO:
        _COLLECTOR_KEY_ERROR = "cryptography library required for Ed25519 leak signing. Install: pip install cryptography"
        _COLLECTOR_KEY_INITIALIZED = True
        return False
    env_key = os.environ.get("SYXX_COLLECTOR_PRIVATE_KEY")
    if env_key:
        # A provisioned-but-wrong key used to surface as a raw OpenSSL "not enough data"
        # on every request, naming neither the variable nor the fix.
        why = collector_key_error(env_key)
        if why:
            _COLLECTOR_KEY_ERROR = f"SYXX_COLLECTOR_PRIVATE_KEY {why}"
            _COLLECTOR_KEY_INITIALIZED = True
            return False
        _COLLECTOR_PRIV_KEY = load_der_private_key(base64.b64decode(env_key), password=None)
        # A113: validate the loaded key is actually Ed25519. A wrong-algorithm DER
        # key (e.g. RSA, ECDSA) passes load_der_private_key but fails later at
        # public_bytes(Raw, Raw) with an opaque OpenSSL error. Fail immediately
        # with a clear diagnostic instead.
        if not isinstance(_COLLECTOR_PRIV_KEY, Ed25519PrivateKey):
            actual_type = type(_COLLECTOR_PRIV_KEY).__name__
            _COLLECTOR_PRIV_KEY = None
            _COLLECTOR_KEY_ERROR = (
                f"SYXX_COLLECTOR_PRIVATE_KEY is {actual_type}, not Ed25519. The collector "
                f"signs leak attestations with Ed25519 only. {_GENERATE_HINT}"
            )
            _COLLECTOR_KEY_INITIALIZED = True
            return False
    elif os.environ.get("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY") == "1":
        # Dev/test escape hatch ONLY. An ephemeral key means attestations do not
        # survive a restart and anyone could mint a self-consistent "collector
        # attestation" against this throwaway key — never acceptable in production.
        _COLLECTOR_PRIV_KEY = Ed25519PrivateKey.generate()
        logging_util.warn(
            "collector_ed25519_key",
            msg="SYXX_COLLECTOR_PRIVATE_KEY not set; generated ephemeral key "
            "(dev/test only — attestations will not survive server restart)",
        )
    else:
        # Fail closed (A022): refuse to sign with an ephemeral key in production. A
        # per-process key would silently invalidate every attestation after a restart
        # and offers no trust anchor a client could pin.
        _COLLECTOR_KEY_ERROR = (
            "SYXX_COLLECTOR_PRIVATE_KEY is not set. The collector refuses to start "
            "without a stable Ed25519 signing key: an ephemeral key makes leak "
            "attestations unverifiable after a restart and forgeable by anyone. "
            "Set it to a base64-encoded DER PKCS#8 Ed25519 private key. "
            + _GENERATE_HINT
            + "  (SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY=1 is dev/test ONLY.)"
        )
        _COLLECTOR_KEY_INITIALIZED = True
        return False
    pub = _COLLECTOR_PRIV_KEY.public_key()
    _COLLECTOR_PUB_KEY_RAW = pub.public_bytes(Encoding.Raw, PublicFormat.Raw)
    _COLLECTOR_KEY_ID = base64.b64encode(_COLLECTOR_PUB_KEY_RAW[:8]).decode("ascii")
    # A114: record the actual activation time of this key instance, not the current
    # fetch time. For a configured key this is the server start time (the key was
    # already generated externally); for an ephemeral key it is now.
    _COLLECTOR_KEY_ACTIVATED_AT = int(time.time())
    _COLLECTOR_KEY_INITIALIZED = True
    _COLLECTOR_KEY_ERROR = None
    return True


def _canon(obj):
    """Canonical JSON encoding for deterministic hashing (sorted keys, no whitespace)."""
    return json.dumps(obj, separators=(",", ":"), sort_keys=True).encode("utf-8")


def collector_public_key():
    """Return the base64-encoded Ed25519 public key, or None if unavailable (A051)."""
    if not _ensure_collector_key():
        return None
    return base64.b64encode(_COLLECTOR_PUB_KEY_RAW).decode("ascii")  # type: ignore[invalid-argument-type]


def collector_key_id():
    """Return the short key id (base64 of first 8 pubkey bytes), or None if unavailable (A051)."""
    if not _ensure_collector_key():
        return None
    return _COLLECTOR_KEY_ID


def collector_warp_key_id():
    """WARP_MANIFEST_V1 key id: lowercase hex of the RAW 32-byte Ed25519 public key.
    Deliberately distinct from collector_key_id() (base64 of the first 8 pubkey bytes,
    fixed by the leak-attestation protocol). Both derive from the same collector key.
    Returns None if the key is unavailable (A051)."""
    if not _ensure_collector_key():
        return None
    return _COLLECTOR_PUB_KEY_RAW.hex()


def collector_key_activated_at():
    """A114: return the timestamp when this key instance was activated (loaded/generated).
    Returns None if the key is unavailable (A051)."""
    if not _ensure_collector_key():
        return None
    return _COLLECTOR_KEY_ACTIVATED_AT


def collector_key_error_message():
    """A051: return the stored error message for the collector key, or None if the key is OK.
    Callers (e.g. /api/pubkey handler) can use this to build a structured error response
    instead of getting a vague 500 from the global exception handler."""
    _ensure_collector_key()
    return _COLLECTOR_KEY_ERROR


def sign_content(content):
    """D041: sign arbitrary content (bytes or str) with the collector's Ed25519 private key.
    Returns a base64-encoded signature, or None if the collector key is unavailable (A051).
    Used to sign What's-New HTML so the client can verify content integrity before rendering.
    If content is a string, it is encoded as UTF-8 before signing."""
    if not _ensure_collector_key():
        return None
    if isinstance(content, str):
        content = content.encode("utf-8")
    signature = _COLLECTOR_PRIV_KEY.sign(content)  # type: ignore[unresolved-attribute]
    return base64.b64encode(signature).decode("ascii")


def issue_token(subject, zone=None, origin="ip"):
    """Issue a leak token. `origin` is 'license' for paid-route tokens or 'ip' for
    free-route tokens. A133: the origin is stored on the token row and checked on
    read so the free route cannot serve paid tokens."""
    zone = zone if zone in config.LEAK_ZONES else config.LEAK_ZONES[0]
    label = base64.b32encode(secrets.token_bytes(16)).decode("ascii").rstrip("=").lower()
    db.leak_token_create(label, subject, zone, config.LEAK_TOKEN_TTL, origin=origin)
    logging_util.incr("leak_tokens_issued")
    return {
        "token": label,
        "zone": zone,
        "subdomain": f"{label}.{zone}",
        "expiresAt": int(time.time()) + config.LEAK_TOKEN_TTL,
    }


def observe(label, resolver_ip, qtype):
    """Called by the DNS server for any lookup under a leak zone."""
    label = label.lower()
    rec = db.leak_token_get(label)
    if rec is None:
        # Never-issued token: write nothing. Otherwise anyone on the internet can drive
        # unbounded DB writes and poison the passive-DNS index with attacker-chosen domains
        # (which feed drift results) via `dig <random>.leak.syxx.app`. (A009)
        return
    # A034: leak_observation_add dedups on (token, resolver_ip, qtype), so a replayed
    # cleartext lookup of a known token cannot append unbounded rows.
    db.leak_observation_add(label, resolver_ip, qtype)
    # A023: do NOT mirror the submitter's resolver IP into the cross-readable
    # passive_dns index. That IP is PII (it deanonymizes whose DNS leaked and to
    # where), it survived the one-time consume(), and it was readable by any entitled
    # caller of /api/passive-dns with no ownership check. The observation already
    # lives in the ownership-gated leak_observations table (returned only to the
    # token's owner via verify()), so the passive mirror added exposure, not value.
    logging_util.incr("leak_observations")


def _ptr_lookup(ip):
    """Best-effort PTR lookup for a resolver IP. Returns the hostname string,
    "no-ptr" if the lookup succeeded but no PTR record exists, or "lookup-failed"
    if the query itself failed. A201 Fix 1: distinguishes the three real outcomes
    so a blank cell is not read as "we looked and found nothing" when we never looked.

    NOTE: PTR records are attacker-influenced text controlled by the resolver's
    operator. Callers must NOT inject the result into HTML via innerHTML without
    escaping (use textContent or HTML-escape first).
    """
    try:
        arpa = resolver.ip_to_arpa(ip)
    except (resolver.DNSError, ValueError):
        return "lookup-failed"
    server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
    try:
        result = resolver.query(arpa, resolver.TYPE_PTR, server, timeout=config.INVESTIGATE_TIMEOUT)
    except (resolver.DNSError, Exception):
        return "lookup-failed"
    ptr_records = [r["value"] for r in result.get("records", []) if r["type"] == "PTR"]
    return ptr_records[0] if ptr_records else "no-ptr"


def _enrich_observations_with_hostnames(observations):
    """A201 Fix 1: attach PTR hostname to each observation. Lookups are cached
    per unique resolver_ip so the same IP is not queried twice."""
    hostname_cache = {}
    for obs in observations:
        ip = obs.get("resolver_ip", "")
        if ip not in hostname_cache:
            hostname_cache[ip] = _ptr_lookup(ip)
        obs["hostname"] = hostname_cache[ip]
    return observations


def verify(token, subject, allow_any_subject=False):
    """Return a signed attestation of observed resolvers for a token.

    Does NOT consume the token — the caller must call consume() after the HTTP
    response has been sent, to avoid consuming a token whose attestation the
    client never received (SRV-050).

    When allow_any_subject=True, the subject (client IP) check is skipped.
    This is used for the free public leak test (/api/leaktest/check) where
    the caller's IP may change mid-test (VPN toggle, dual-stack IPv4/IPv6).

    A111: atomically claims the token before reading observations, so concurrent
    verifies cannot both sign receipts for the same token. If the claim fails
    (another verifier already claimed it), returns None with "already verified".
    """
    label = token.split(".")[0].lower()
    rec = db.leak_token_get(label)
    if not rec:
        return None, "unknown or expired token"
    if not allow_any_subject and rec["subject"] != subject:
        return None, "token does not belong to caller"
    # A111: atomic claim — only one verifier can proceed past this point.
    if not db.leak_token_claim(label):
        return None, "token already verified or being verified"
    obs = db.leak_observations(label)
    # A201 Fix 1: enrich observations with PTR hostnames so the client's
    # "Hostname" table column is populated from the server rather than
    # rendering a permanent "—" for every row.
    if obs:
        obs = _enrich_observations_with_hostnames(obs)
    resolvers = sorted({o["resolver_ip"] for o in obs})
    if not obs:
        # A024: ZERO collector observations is NOT evidence of "no leak" - it is
        # indistinguishable from a collector path that was blocked, censored, or
        # never exercised. Refuse to Ed25519-sign a clean `leaked:false` with no
        # liveness proof (a signed clean would read as trustworthy to a client and
        # to r/AskNetSec). Emit an honest inconclusive with NO signature, and return
        # label=None so the caller (app.py) does NOT consume the token - the caller
        # may retry within the token's TTL if a query arrives late.
        subject_bound = not allow_any_subject
        tier = "paid" if subject_bound else "free"
        inconclusive = {
            "token": label,
            "zone": rec["zone"],
            "issuedAt": rec["issued_at"],
            "queriesObserved": None,
            # A176: emit both keys for backward compatibility. Desktop clients
            # (macOS/Windows) still deserialize `leaked`; without it they record
            # every attestation as inconclusive. Drop only after measured upgrade.
            "leaked": None,
            # A178: binding fields inside the seal (consistent with normal path).
            "subjectBound": subject_bound,
            "tier": tier,
            "resolverCount": 0,
            "observedResolvers": [],
            "observations": [],
            "inconclusive": True,
            "verifiedAt": int(time.time()),
        }
        # A111: release the claim so the client can retry once observations arrive.
        db.leak_token_release_claim(label)
        return inconclusive, None
    # A178: the signed set must carry subjectBound and tier so a verifier can
    # read from the signed bytes alone whether ownership was checked and whether
    # the attestation is free-tier (anonymous) or paid (subject-bound). Without
    # these, a free attestation and a paid one are byte-identical under the
    # signature, so a third party can present another user's free attestation
    # as if the collector attested *their* resolvers.
    subject_bound = not allow_any_subject
    tier = "paid" if subject_bound else "free"
    result = {
        "token": label,
        "zone": rec["zone"],
        "issuedAt": rec["issued_at"],
        "queriesObserved": len(obs) > 0,
        # A176: emit both keys for backward compatibility. Desktop clients
        # (macOS/Windows) still deserialize `leaked`; without it they record
        # every attestation as inconclusive. Drop only after measured upgrade.
        "leaked": len(obs) > 0,
        "resolverCount": len(resolvers),
        "observedResolvers": resolvers,
        # A178: binding fields inside the seal.
        "subjectBound": subject_bound,
        "tier": tier,
        # A036: observations are NOT covered by the signature. Mark them explicitly
        # as advisory/unattested so callers do not treat per-observation detail
        # (resolver_ip, qtype, timestamps) as if it were attested by the collector key.
        # The signed fields (observedResolvers, resolverCount) are attested; the
        # per-observation breakdown is not.
        "observations": obs,
        "observationsAttested": False,
        "verifiedAt": int(time.time()),
    }
    signed = {
        k: result[k]
        for k in (
            "token",
            "zone",
            "issuedAt",
            "queriesObserved",
            "leaked",
            "resolverCount",
            "observedResolvers",
            "subjectBound",
            "tier",
            "verifiedAt",
        )
    }
    # Ensure the collector key exists BEFORE signing. It was previously created only
    # lazily inside collector_public_key()/collector_key_id() below, so on a cold
    # process this sign() ran with _COLLECTOR_PRIV_KEY == None — a deterministic
    # AttributeError that made /api/leak/verify never return an attestation.
    # A051: if the key is unavailable, release the claim so the client can retry
    # later, and return a structured error instead of crashing mid-signature.
    if not _ensure_collector_key():
        db.leak_token_release_claim(label)
        return None, f"collector key unavailable: {_COLLECTOR_KEY_ERROR}"
    signed_payload = _canon(signed)
    result["signature"] = base64.b64encode(_COLLECTOR_PRIV_KEY.sign(signed_payload)).decode("ascii")  # type: ignore[unresolved-attribute]
    result["publicKey"] = collector_public_key()
    result["keyId"] = collector_key_id()
    result["signedPayload"] = base64.b64encode(signed_payload).decode("ascii")
    return result, label


def consume(label):
    """Delete a token and its observations after a successful verify response.
    Must be called by the HTTP handler after _send() succeeds (SRV-050)."""
    db.leak_token_consume(label)
