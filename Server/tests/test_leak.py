"""Tests for the leak collector's Ed25519 attestation path (/api/leak/verify).

Regression guard for two cold-start defects that shipped because `leak.verify()`
had NO test: (1) `.sign()` ran before the collector key was created, and (2) the
public-key global was misnamed so `collector_public_key()` raised NameError. Both
were deterministic — every cold verify() 500'd — yet invisible to unit tests that
never exercised the signing path. This test drives issue -> observe -> verify and
INDEPENDENTLY verifies the emitted signature, so the path can't silently break again.

Skipped when `cryptography` isn't installed (leak signing genuinely requires it).
Run from Server/:  python3 -m unittest tests.test_leak -v
"""

import base64
import os

# A214: use the shared temp DB from conftest.py (loaded by pytest before any test module).
# The old pattern (creating a per-module temp DB and setting SYXX_DB_PATH after import)
# was dead code: conftest.py already set the env var and config.DB_PATH was already bound.
import conftest
import pytest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
# Dev/test escape (A022): allow an ephemeral collector key so the attestation path
# runs under test without a provisioned SYXX_COLLECTOR_PRIVATE_KEY. Production fails
# closed without this flag.
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ["SYXX_LEAK_ZONES"] = "leak.syxx.app"

from syxx_server import config, db, leak  # noqa: E402

# A214: force DB_PATH to the conftest-managed temp DB and re-init so tables exist.
config.DB_PATH = _DB_PATH
db._initialized = False
db.init()

_CRYPTO_OR_SKIP = pytest.mark.skipif(not leak._HAS_CRYPTO, reason="cryptography not installed")


@_CRYPTO_OR_SKIP
def test_cold_verify_signs_and_signature_validates():
    from cryptography.exceptions import InvalidSignature
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

    subject = "device-abc123"
    issued = leak.issue_token(subject)
    leak.observe(issued["token"], "8.8.8.8", 1)
    leak.observe(issued["token"], "1.1.1.1", 1)

    # This is the call that used to 500 on a cold process.
    result, label = leak.verify(issued["subdomain"], subject)
    assert result is not None, "cold verify() returned no attestation"
    assert result["queriesObserved"]
    assert sorted(result["observedResolvers"]) == ["1.1.1.1", "8.8.8.8"]

    # Independent verification: the emitted signature must validate...
    pub = Ed25519PublicKey.from_public_bytes(base64.b64decode(result["publicKey"]))
    payload = base64.b64decode(result["signedPayload"])
    sig = base64.urlsafe_b64decode(result["signature"] + "=" * (-len(result["signature"]) % 4))
    pub.verify(sig, payload)  # raises on failure

    # ...the SIGNED payload must bind the SAME facts the attestation DISPLAYS. Otherwise a
    # regression could sign an empty/forged resolver set while displaying the real one
    # ("lying attestation") and still validate. Assert field-equality, not just a valid sig.
    import json as _json

    signed_obj = _json.loads(payload)
    assert signed_obj["observedResolvers"] == result["observedResolvers"], (
        "signed payload resolvers must equal the displayed resolvers (lying attestation)"
    )
    assert signed_obj["queriesObserved"] == result["queriesObserved"]
    assert signed_obj["token"] == result["token"]
    assert signed_obj["zone"] == result["zone"]

    # ...and a tampered payload must be rejected.
    tampered = payload.replace(b'"queriesObserved":true', b'"queriesObserved":fals')
    assert tampered != payload
    with pytest.raises(InvalidSignature):
        pub.verify(sig, tampered)


@_CRYPTO_OR_SKIP
def test_verify_rejects_wrong_subject():
    issued = leak.issue_token("owner-1")
    result, err = leak.verify(issued["subdomain"], "someone-else")
    assert result is None
    assert "does not belong" in err


def test_collector_fails_closed_without_a_provisioned_key():
    """A022/A051 failure case: with NO provisioned key and NO dev escape, the collector must
    refuse to start — never silently mint a forgeable ephemeral key. A051 changed from
    RuntimeError to return-bool pattern; the fail-closed contract is unchanged."""
    if not leak._HAS_CRYPTO:
        pytest.skip("cryptography not installed")
    saved_key = leak._COLLECTOR_PRIV_KEY
    saved_escape = os.environ.get("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY")
    saved_priv = os.environ.get("SYXX_COLLECTOR_PRIVATE_KEY")
    saved_init = getattr(leak, '_COLLECTOR_KEY_INITIALIZED', False)
    saved_err = getattr(leak, '_COLLECTOR_KEY_ERROR', None)
    try:
        leak._COLLECTOR_PRIV_KEY = None  # force re-derivation
        leak._COLLECTOR_KEY_INITIALIZED = False  # reset A051 cache
        leak._COLLECTOR_KEY_ERROR = None
        os.environ.pop("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", None)
        os.environ.pop("SYXX_COLLECTOR_PRIVATE_KEY", None)
        # A051: _ensure_collector_key now returns False instead of raising
        result = leak._ensure_collector_key()
        assert result is False, "fail-closed must return False when no key is provisioned"
        assert leak._COLLECTOR_PRIV_KEY is None, "fail-closed must not leave a silently-minted key behind"
    finally:
        if saved_escape is not None:
            os.environ["SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY"] = saved_escape
        if saved_priv is not None:
            os.environ["SYXX_COLLECTOR_PRIVATE_KEY"] = saved_priv
        leak._COLLECTOR_PRIV_KEY = saved_key  # restore for other tests
        leak._COLLECTOR_KEY_INITIALIZED = saved_init
        leak._COLLECTOR_KEY_ERROR = saved_err


@_CRYPTO_OR_SKIP
def test_empty_collector_is_inconclusive_and_UNSIGNED():
    """A024: zero observations must NEVER be signed as clean."""
    subject = "device-empty-collector"
    issued = leak.issue_token(subject)
    # Deliberately observe NOTHING — the collector path never fired.
    result, label = leak.verify(issued["subdomain"], subject)

    assert result is not None, "an unexercised collector must still return an honest result"
    assert result["queriesObserved"] is None, "zero observations must be queriesObserved=None, never False"
    assert result["inconclusive"], "zero observations must be explicitly inconclusive"
    assert result["observedResolvers"] == []
    # The load-bearing assertion: no signature. A signed clean reads as trustworthy to a client.
    assert "signature" not in result, "a clean result on zero evidence must NOT carry an Ed25519 signature"
    assert "signedPayload" not in result
    # And the token must NOT be consumed, so a late query can still be attested.
    assert label is None, "an inconclusive result must not consume the token"


@_CRYPTO_OR_SKIP
def test_never_issued_token_writes_nothing():
    """A009: observing a token the server never issued must be a no-op.

    Otherwise anyone on the internet can drive unbounded DB writes and poison the passive-DNS
    index that feeds drift results, via `dig <random>.leak.syxx.app`.
    """
    leak.observe("neverissuedlabel", "203.0.113.9", 1)
    assert db.leak_observations("neverissuedlabel") == [], "a never-issued token must record no observations"


@_CRYPTO_OR_SKIP
def test_a_real_observation_still_signs():
    """Mutation guard: the A024 refusal must be specific to the EMPTY case — a genuine
    observation still produces a signed attestation. Without this, 'never sign anything'
    would pass the test above."""
    subject = "device-real-observation"
    issued = leak.issue_token(subject)
    leak.observe(issued["token"], "8.8.8.8", 1)
    result, label = leak.verify(issued["subdomain"], subject)

    assert result["queriesObserved"]
    assert "signature" in result, "a result backed by real evidence must be signed"


@_CRYPTO_OR_SKIP
def test_c005_cryptography_version_patches_cve_2026_69249():
    """C005 regression: CVE-2026-69249 in cryptography <49.0.0 could expose Ed25519
    private key material through the signing API. The fix is upgrading to >=49.0.0
    (already done by A032 in requirements.txt). This test guards against downgrade."""
    import cryptography
    from packaging.version import Version

    installed = Version(cryptography.__version__)
    assert installed >= Version("49.0.0"), (
        f"C005: cryptography {cryptography.__version__} is vulnerable to CVE-2026-69249 "
        f"(Ed25519 private key exposure via signing side-channel). "
        f"Upgrade to >=49.0.0 per requirements.txt."
    )


@_CRYPTO_OR_SKIP
def test_c005_ed25519_signature_does_not_leak_private_key():
    """C005 regression: verify that Ed25519 signing via leak.sign_content() does not
    expose private key material in the signature output, public key, or any returned
    field. This is the exact attack surface CVE-2026-69249 exploited in older versions."""
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

    # Sign content through the leak module's own signing path
    test_payload = "C005 regression: attestation integrity check"
    sig_b64 = leak.sign_content(test_payload)
    assert sig_b64 is not None, "sign_content must succeed with ephemeral key"

    pub_b64 = leak.collector_public_key()
    assert pub_b64 is not None

    # The signature must be exactly 64 bytes (Ed25519 standard) — not longer
    # (which could indicate private key material appended to the signature).
    sig_bytes = base64.b64decode(sig_b64)
    assert len(sig_bytes) == 64, (
        f"Ed25519 signature must be exactly 64 bytes, got {len(sig_bytes)}. "
        f"A longer signature could indicate private key material leakage (CVE-2026-69249)."
    )

    # The public key must be exactly 32 bytes (raw Ed25519 public key).
    pub_bytes = base64.b64decode(pub_b64)
    assert len(pub_bytes) == 32, (
        f"Ed25519 public key must be exactly 32 bytes, got {len(pub_bytes)}."
    )

    # Independent verification: the signature validates with the public key
    pub = Ed25519PublicKey.from_public_bytes(pub_bytes)
    pub.verify(sig_bytes, test_payload.encode("utf-8"))  # raises on failure

    # The private key bytes must NOT appear anywhere in the signature or public key output
    priv_key = leak._COLLECTOR_PRIV_KEY
    if priv_key is not None:
        from cryptography.hazmat.primitives.serialization import Encoding, PrivateFormat, NoEncryption
        priv_der = priv_key.private_bytes(Encoding.DER, PrivateFormat.PKCS8, NoEncryption())
        # The raw 32-byte seed is embedded in the DER encoding — extract it
        # (PKCS#8 Ed25519 DER is 48 bytes, last 32 are the seed)
        seed = priv_der[-32:]
        assert seed not in (sig_bytes, pub_bytes), (
            "Private key seed must not appear in signature or public key output"
        )
