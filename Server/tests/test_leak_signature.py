"""A203/A215: test the actual production signature-encoding path via leak.verify().

The original tests signed a hardcoded byte string and verified it against the
public key — but never exercised `_canon()`, the real signing call in
`leak.verify()`, or the base64 encoding that ships in the attestation response.
Both tests would have passed even if the production code path was completely
broken, because the test created its own payload, signature, and verification
from scratch.

A215: the standard-base64 contract MUST be asserted on the production output
(result["signature"] from leak.verify()), not on a signature the test creates
itself. Otherwise a drift from b64encode to urlsafe_b64encode in leak.py goes
undetected while this test stays green.

This module tests the real encoding pipeline:
  1. `_canon()` produces deterministic canonical JSON
  2. `leak.verify()` produces a signature that the public key can verify
  3. The base64 output is standard (padded, not url-safe) — asserted on the
     PRODUCTION output, not a test-computed value

Run from the Server/ dir:

    python3 -m pytest tests/test_leak_signature.py -v
"""

import base64
import json
import os

# A214: use the shared temp DB from conftest.py.
import conftest  # noqa: F401
import pytest

os.environ["SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY"] = "1"
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import config, db, leak  # noqa: E402

config.DB_PATH = conftest.SYXX_TEST_DB_PATH
db._initialized = False
db.init()

_CRYPTO_OR_SKIP = pytest.mark.skipif(not leak._HAS_CRYPTO, reason="cryptography not installed")


@_CRYPTO_OR_SKIP
def test_canon_produces_deterministic_json():
    """A203: _canon must produce sorted, compact JSON — this is what gets signed."""
    obj = {"b": 2, "a": 1, "c": [3, 2, 1]}
    result = leak._canon(obj)
    expected = json.dumps(obj, separators=(",", ":"), sort_keys=True).encode("utf-8")
    assert result == expected
    assert leak._canon(obj) == leak._canon(obj)


@_CRYPTO_OR_SKIP
def test_verify_signature_is_standard_base64_and_verifies():
    """A215: assert on the PRODUCTION signature from leak.verify(), not on a
    test-computed value. The standard-base64 contract (D057) has zero coverage
    unless we assert on result["signature"] itself."""
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

    subject = "sig-test-device"
    issued = leak.issue_token(subject)
    leak.observe(issued["token"], "8.8.8.8", 1)
    leak.observe(issued["token"], "1.1.1.1", 1)

    result, label = leak.verify(issued["subdomain"], subject)
    assert result is not None, "verify() returned no attestation"
    assert "signature" in result

    sig_b64 = result["signature"]

    # A215: the signature MUST be standard base64 (padded, not url-safe).
    # If leak.py drifts to urlsafe_b64encode, these assertions go RED.
    assert len(sig_b64) % 4 == 0, "standard base64 must be padded (len % 4 == 0)"
    assert "-" not in sig_b64, "standard base64 uses '+' not '-' (url-safe)"
    assert "_" not in sig_b64, "standard base64 uses '/' not '_' (url-safe)"

    # Round-trip: decode with STRICT standard b64decode (not urlsafe_b64decode)
    # and verify with the public key. This would fail if encoding drifted.
    sig_raw = base64.b64decode(sig_b64)
    pub = Ed25519PublicKey.from_public_bytes(base64.b64decode(result["publicKey"]))
    payload = base64.b64decode(result["signedPayload"])
    pub.verify(sig_raw, payload)  # raises InvalidSignature on failure


@_CRYPTO_OR_SKIP
def test_collector_public_key_is_consistent():
    """A203: collector_public_key() and key_id() must return stable values
    within a single process — the attestation response includes both."""
    pub_b64 = leak.collector_public_key()
    key_id = leak.collector_key_id()

    assert len(pub_b64) > 0
    assert len(key_id) > 0
    assert pub_b64 == leak.collector_public_key()
    assert key_id == leak.collector_key_id()

    pub_raw = base64.b64decode(pub_b64)
    expected_key_id = base64.b64encode(pub_raw[:8]).decode("ascii")
    assert key_id == expected_key_id
