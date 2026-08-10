"""Focused regression tests for server hardening deltas A001-A018."""

import io
import os
import time

import conftest
import pytest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import app, config, db, fetcher, licensing, ratelimit, resolver  # noqa: E402

config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


@pytest.fixture(autouse=True)
def _clear_ratelimit_buckets():
    yield
    with ratelimit._lock:
        ratelimit._buckets.clear()
        ratelimit._last_prune_ms = 0


def test_short_post_body_is_explicit_incomplete():
    h = object.__new__(app.Handler)
    h.headers = {"Content-Length": "8"}
    h.rfile = io.BytesIO(b"abc")
    assert h._body() is app._BODY_INCOMPLETE


def test_oversized_post_body_is_explicit_too_large():
    h = object.__new__(app.Handler)
    h.headers = {"Content-Length": str((1 << 20) + 1)}
    h.rfile = io.BytesIO(b"")
    assert h._body() is app._BODY_TOO_LARGE


def test_prune_removes_only_stale_buckets():
    with ratelimit._lock:
        ratelimit._buckets.clear()
        ratelimit._buckets["old"] = (1000, 1)
        ratelimit._buckets["fresh"] = (1000, 200_000)
        ratelimit._prune(200_000)
        assert "old" not in ratelimit._buckets
        assert "fresh" in ratelimit._buckets


def test_malformed_bearer_base64_is_unauthenticated_not_exception():
    assert licensing.verify_session("not-base64!.signature") is None


def test_query_string_license_key_is_ignored(monkeypatch):
    seen = []
    monkeypatch.setattr(licensing, "validate_license", lambda key: seen.append(key) or (None, None))
    ent, err = licensing.entitlement_for({}, {"key": ["LICENSE-SECRET"]})
    assert ent is None
    assert err is None
    assert seen == []


def test_terminal_answer_without_aa_is_inconclusive(monkeypatch):
    # Production passes do=True (DNSSEC OK); accept **kw so the fixture stays compatible.
    def _sections(name, qtype, server, timeout=3, **_kw):
        return {
            "rcode": 0,
            "aa": False,
            "truncated": False,
            "answers": [{"owner": name, "type": "A", "rtype": resolver.TYPE_A, "ttl": 60, "value": "203.0.113.10"}],
            "authority": [],
            "additional": [],
        }

    monkeypatch.setattr(resolver, "_query_sections", _sections)
    monkeypatch.setattr(resolver, "_zone_is_signed", lambda zone, server, timeout: True)
    result = resolver.resolve_authoritative("example.com", resolver.TYPE_A, max_depth=1)
    assert result["conclusion"] == "inconclusive"
    assert result["authoritative"] is None


def test_truncated_response_raises_explicit_error(monkeypatch):
    class Resp:
        status = 200

        def getheader(self, _name, default=None):
            return default

        def read(self, _n):
            return b"abcdef"

    class Conn:
        host = None

        def __init__(self, *a, **k):
            pass

        def putrequest(self, *a, **k):
            pass

        def putheader(self, *a, **k):
            pass

        def endheaders(self):
            pass

        def getresponse(self):
            return Resp()

        def close(self):
            pass

    monkeypatch.setattr(fetcher, "_resolve_public", lambda host: "93.184.216.34")
    monkeypatch.setattr(fetcher.http.client, "HTTPConnection", Conn)
    with pytest.raises(fetcher.TruncatedError):
        fetcher._one_request("http://example.com/", timeout=1, max_bytes=5)


def test_purge_removes_old_trial_anchors():
    db.get_or_create_trial_anchor("old-fp", 1)
    db.purge_expired(now=int(time.time()) + 400 * 86400)
    conn = db.connect()
    try:
        row = conn.execute("SELECT * FROM trial_anchors WHERE fingerprint='old-fp'").fetchone()
    finally:
        conn.close()
    assert row is None


def test_private_loopback_linklocal_cgnat_metadata_are_not_public():
    for ip in (
        "127.0.0.1",
        "10.0.0.53",
        "169.254.169.254",
        "192.168.1.1",
        "172.16.0.1",
        "100.64.0.1",
        "0.0.0.0",
        "::1",
        "fd00::1",
        "fe80::1",
    ):
        assert not fetcher._ip_is_public(ip), f"{ip} must be rejected as non-public (SSRF)"


def test_ordinary_public_ips_are_public():
    for ip in ("93.184.216.34", "8.8.8.8", "2606:4700:4700::1111"):
        assert fetcher._ip_is_public(ip), f"{ip} must be accepted"


def test_resolve_public_raises_when_host_maps_to_metadata_ip(monkeypatch):
    monkeypatch.setattr(
        fetcher.socket, "getaddrinfo", lambda *a, **k: [(fetcher.socket.AF_INET, 0, 0, "", ("169.254.169.254", 0))]
    )
    with pytest.raises(fetcher.BlockedError):
        fetcher._resolve_public("metadata.internal")


def test_resolve_public_rejects_if_any_answer_is_private(monkeypatch):
    # DNS-rebinding / split answer: one public + one private must STILL block.
    monkeypatch.setattr(
        fetcher.socket,
        "getaddrinfo",
        lambda *a, **k: [
            (fetcher.socket.AF_INET, 0, 0, "", ("93.184.216.34", 0)),
            (fetcher.socket.AF_INET, 0, 0, "", ("127.0.0.1", 0)),
        ],
    )
    with pytest.raises(fetcher.BlockedError):
        fetcher._resolve_public("rebind.evil")


def test_bucket_denies_after_capacity_is_exhausted():
    results = [ratelimit.allow("sub:burst", capacity=3, refill_millis=0) for _ in range(5)]
    assert results[:3] == [True, True, True], "first `capacity` requests must be allowed"
    assert results[3:] == [False, False], "requests past capacity MUST be denied (allow() -> False)"


def test_tokens_refill_after_elapsed_time_reenables():
    for _ in range(3):
        ratelimit.allow("sub:refill", capacity=3, refill_millis=1000)
    assert not ratelimit.allow("sub:refill", capacity=3, refill_millis=1000)
    with ratelimit._lock:  # backdate last-refill so tokens accrue
        tokens, last = ratelimit._buckets["sub:refill"]
        ratelimit._buckets["sub:refill"] = (tokens, last - 2000)
    assert ratelimit.allow("sub:refill", capacity=3, refill_millis=1000)


def test_wellformed_token_with_forged_signature_is_rejected():
    import base64 as _b64
    import json as _json
    import time as _time

    payload = {"sub": "attacker", "plan": "pro", "exp": int(_time.time()) + 10_000}
    raw = _json.dumps(payload, separators=(",", ":"), sort_keys=True).encode()
    body = _b64.urlsafe_b64encode(raw).decode().rstrip("=")
    forged = body + "." + _b64.urlsafe_b64encode(b"not-the-real-mac").decode().rstrip("=")
    assert licensing.verify_session(forged) is None, (
        "forged-signature session token accepted -> paywall/evidence bypass"
    )


def test_valid_minted_token_is_accepted():
    token, _exp = licensing.mint_session("legit", "pro")
    ent = licensing.verify_session(token)
    assert ent is not None
    assert ent.subject == "legit"
    assert ent.plan == "pro"


def test_expired_token_is_rejected():
    # E045: token must be expired beyond _SESSION_CLOCK_SKEW_TOLERANCE (30 s) to be
    # rejected. ttl=-1 produced a 1-second-expired token which now falls within the
    # skew window and is intentionally accepted. Use ttl=-60 (1 minute ago) to exceed
    # the 30-second tolerance and confirm that genuinely stale tokens are still denied.
    token, _exp = licensing.mint_session("legit", "pro", ttl=-60)
    assert licensing.verify_session(token) is None, "expired session token accepted"
