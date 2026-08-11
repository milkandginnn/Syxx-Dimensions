"""WARP_MANIFEST_V1 signature round-trip — the authenticated wire envelope (spec §2).

The producer serializes the inner manifest ONCE, base64s it into `manifest_b64`, and signs
the base64-DECODED bytes with the collector Ed25519 key. This suite proves the envelope a
client actually receives is verifiable exactly as spec §4 mandates:

  1. base64-decode `manifest_b64` → B (standard base64, not url-safe);
  2. Ed25519-verify `signature_ed25519` over B with the /api/pubkey key — BEFORE any JSON parse;
  3. a one-byte tamper of B (or of the signature) fails verification (→ client TAMPERING_DETECTED);
  4. the nonce the client sent is echoed inside the signed bytes (replay binding);
  5. `key_id` is the lowercase hex of the raw 32-byte public key (the pinned-key label).

The route is driven through a lightweight double (no HTTP server); fetcher.fetch_ex is mocked
so warp.build_manifest runs for real end-to-end.
"""

import base64
import json
import os
from unittest.mock import patch

import pytest
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import app, fetcher, leak  # noqa: E402

BASE = "https://example.test/"


class _Double:
    """Captures the route's output without an HTTP server. Pro-gate is bypassed (anonymous-ok);
    rate/audit/client-ip are stubbed."""

    def __init__(self):
        self.sent = {}

    def _require(self, query, enforce):
        return None, True

    def _rate_ok(self, ent):
        return True

    def _client_ip(self):
        return "203.0.113.9"

    def _send(self, status, body, *a, **k):
        self.sent = {"status": status, "body": body}

    def _json_error(self, status, msg, *a, **k):
        self.sent = {"status": status, "error": msg}


def _run_manifest(query, network):
    def fx(url, timeout=None, max_bytes=None, max_redirects=None):
        if url not in network:
            raise fetcher.FetchError(f"no route to {url}")
        return network[url]

    d = _Double()
    with patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
        with patch("syxx_server.db.audit", lambda *a, **k: None):
            app.Handler._route_warp_manifest(d, query)
    return d.sent


def _pubkey():
    """The raw Ed25519 public key object a client pins / fetches from /api/pubkey."""
    return Ed25519PublicKey.from_public_bytes(base64.b64decode(leak.collector_public_key()))


@pytest.fixture
def _html_js_net():
    html = b"<html><script src='/a.js'></script></html>"
    return {
        BASE: (html, "text/html", "", BASE),
        BASE + "a.js": (b"console.log(1)", "application/javascript", "", BASE + "a.js"),
    }


def test_detached_signature_verifies_over_the_base64_decoded_manifest(_html_js_net):
    sent = _run_manifest({"url": [BASE], "nonce": ["nonce-01"]}, _html_js_net)
    assert sent["status"] == 200
    env = sent["body"]
    assert set(env) == {"manifest_b64", "signature_ed25519", "key_id"}

    manifest_bytes = base64.b64decode(env["manifest_b64"])  # standard base64
    signature = base64.b64decode(env["signature_ed25519"])
    # Verify BEFORE parsing the JSON — exactly the client's mandated order.
    _pubkey().verify(signature, manifest_bytes)

    manifest = json.loads(manifest_bytes)
    assert manifest["warp_manifest_version"] == "WARP_MANIFEST_V1"
    assert manifest["nonce"] == "nonce-01"  # echoed inside the signed bytes


def test_one_byte_tamper_of_the_manifest_fails_verification(_html_js_net):
    sent = _run_manifest({"url": [BASE]}, _html_js_net)
    env = sent["body"]
    manifest_bytes = bytearray(base64.b64decode(env["manifest_b64"]))
    signature = base64.b64decode(env["signature_ed25519"])
    # Flip one byte in the middle of the manifest → signature must no longer verify.
    manifest_bytes[len(manifest_bytes) // 2] ^= 0x01
    with pytest.raises(InvalidSignature):
        _pubkey().verify(signature, bytes(manifest_bytes))


def test_one_byte_tamper_of_the_signature_fails_verification(_html_js_net):
    sent = _run_manifest({"url": [BASE]}, _html_js_net)
    env = sent["body"]
    manifest_bytes = base64.b64decode(env["manifest_b64"])
    sig = bytearray(base64.b64decode(env["signature_ed25519"]))
    sig[0] ^= 0x01
    with pytest.raises(InvalidSignature):
        _pubkey().verify(bytes(sig), manifest_bytes)


def test_manifest_b64_is_standard_base64_not_urlsafe(_html_js_net):
    sent = _run_manifest({"url": [BASE]}, _html_js_net)
    env = sent["body"]
    # Standard base64 decodes; and a strict-validate round-trips to the same string.
    raw = base64.b64decode(env["manifest_b64"], validate=True)
    assert base64.b64encode(raw).decode("ascii") == env["manifest_b64"]


def test_key_id_is_raw_pubkey_hex_matching_api_pubkey(_html_js_net):
    """The emitted key_id is the lowercase hex of the raw 32-byte public key served by
    /api/pubkey — the label the corpus, client pinning, and client verifier all share."""
    sent = _run_manifest({"url": [BASE]}, _html_js_net)
    env = sent["body"]
    raw_pub = base64.b64decode(leak.collector_public_key())  # == /api/pubkey publicKey
    assert len(raw_pub) == 32
    assert env["key_id"] == raw_pub.hex()
    assert env["key_id"] == env["key_id"].lower()


def test_nonce_is_null_in_signed_bytes_when_client_sends_none(_html_js_net):
    sent = _run_manifest({"url": [BASE]}, _html_js_net)
    manifest = json.loads(base64.b64decode(sent["body"]["manifest_b64"]))
    assert manifest["nonce"] is None
