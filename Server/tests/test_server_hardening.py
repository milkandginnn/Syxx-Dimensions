"""Focused regression tests for server hardening deltas A001-A018."""

import io
import os
import time
import unittest

import conftest

_DB_PATH = conftest.SYXX_TEST_DB_PATH

os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")

from syxx_server import app, config, db, fetcher, licensing, ratelimit, resolver  # noqa: E402

config.DB_PATH = _DB_PATH
db._initialized = False
db.init()


class BodyReadTests(unittest.TestCase):
    def test_short_post_body_is_explicit_incomplete(self):
        h = object.__new__(app.Handler)
        h.headers = {"Content-Length": "8"}
        h.rfile = io.BytesIO(b"abc")
        self.assertIs(h._body(), app._BODY_INCOMPLETE)

    def test_oversized_post_body_is_explicit_too_large(self):
        h = object.__new__(app.Handler)
        h.headers = {"Content-Length": str((1 << 20) + 1)}
        h.rfile = io.BytesIO(b"")
        self.assertIs(h._body(), app._BODY_TOO_LARGE)


class RateLimitTests(unittest.TestCase):
    def tearDown(self):
        with ratelimit._lock:
            ratelimit._buckets.clear()
            ratelimit._last_prune_ms = 0

    def test_prune_removes_only_stale_buckets(self):
        with ratelimit._lock:
            ratelimit._buckets.clear()
            ratelimit._buckets["old"] = (1000, 1)
            ratelimit._buckets["fresh"] = (1000, 200_000)
            ratelimit._prune(200_000)
            self.assertNotIn("old", ratelimit._buckets)
            self.assertIn("fresh", ratelimit._buckets)


class LicensingTests(unittest.TestCase):
    def test_malformed_bearer_base64_is_unauthenticated_not_exception(self):
        self.assertIsNone(licensing.verify_session("not-base64!.signature"))

    def test_query_string_license_key_is_ignored(self):
        seen = []
        orig = licensing.validate_license
        licensing.validate_license = lambda key: seen.append(key) or (None, None)
        try:
            ent, err = licensing.entitlement_for({}, {"key": ["LICENSE-SECRET"]})
        finally:
            licensing.validate_license = orig
        self.assertIsNone(ent)
        self.assertIsNone(err)
        self.assertEqual(seen, [])


class ResolverAuthoritativeTests(unittest.TestCase):
    def setUp(self):
        self._orig_query_sections = resolver._query_sections
        self._orig_zone_signed = resolver._zone_is_signed

    def tearDown(self):
        resolver._query_sections = self._orig_query_sections
        resolver._zone_is_signed = self._orig_zone_signed

    def test_terminal_answer_without_aa_is_inconclusive(self):
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

        resolver._query_sections = _sections
        resolver._zone_is_signed = lambda zone, server, timeout: True
        result = resolver.resolve_authoritative("example.com", resolver.TYPE_A, max_depth=1)
        self.assertEqual(result["conclusion"], "inconclusive")
        self.assertIsNone(result["authoritative"])


class FetcherTests(unittest.TestCase):
    def test_truncated_response_raises_explicit_error(self):
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

        orig_resolve = fetcher._resolve_public
        orig_http = fetcher.http.client.HTTPConnection
        fetcher._resolve_public = lambda host: "93.184.216.34"
        fetcher.http.client.HTTPConnection = Conn
        try:
            with self.assertRaises(fetcher.TruncatedError):
                fetcher._one_request("http://example.com/", timeout=1, max_bytes=5)
        finally:
            fetcher._resolve_public = orig_resolve
            fetcher.http.client.HTTPConnection = orig_http


class TrialAnchorRetentionTests(unittest.TestCase):
    def test_purge_removes_old_trial_anchors(self):
        db.get_or_create_trial_anchor("old-fp", 1)
        db.purge_expired(now=int(time.time()) + 400 * 86400)
        conn = db.connect()
        try:
            row = conn.execute("SELECT * FROM trial_anchors WHERE fingerprint='old-fp'").fetchone()
        finally:
            conn.close()
        self.assertIsNone(row)


class SSRFBlockTests(unittest.TestCase):
    """fetcher.py exists to stop SSRF (A011); prove it actually rejects non-public targets.
    Goes RED if `_ip_is_public` is broken to allow a private/metadata IP, or the
    reject-if-ANY-answer-private rebinding guard is dropped."""

    def test_private_loopback_linklocal_cgnat_metadata_are_not_public(self):
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
            self.assertFalse(fetcher._ip_is_public(ip), f"{ip} must be rejected as non-public (SSRF)")

    def test_ordinary_public_ips_are_public(self):
        for ip in ("93.184.216.34", "8.8.8.8", "2606:4700:4700::1111"):
            self.assertTrue(fetcher._ip_is_public(ip), f"{ip} must be accepted")

    def test_resolve_public_raises_when_host_maps_to_metadata_ip(self):
        orig = fetcher.socket.getaddrinfo
        fetcher.socket.getaddrinfo = lambda *a, **k: [(fetcher.socket.AF_INET, 0, 0, "", ("169.254.169.254", 0))]
        try:
            with self.assertRaises(fetcher.BlockedError):
                fetcher._resolve_public("metadata.internal")
        finally:
            fetcher.socket.getaddrinfo = orig

    def test_resolve_public_rejects_if_any_answer_is_private(self):
        # DNS-rebinding / split answer: one public + one private must STILL block.
        orig = fetcher.socket.getaddrinfo
        fetcher.socket.getaddrinfo = lambda *a, **k: [
            (fetcher.socket.AF_INET, 0, 0, "", ("93.184.216.34", 0)),
            (fetcher.socket.AF_INET, 0, 0, "", ("127.0.0.1", 0)),
        ]
        try:
            with self.assertRaises(fetcher.BlockedError):
                fetcher._resolve_public("rebind.evil")
        finally:
            fetcher.socket.getaddrinfo = orig


class RateLimitDenialTests(unittest.TestCase):
    """ratelimit.allow() is the abuse/DoS control (A001) — prove it actually DENIES a
    drained bucket. Goes RED if allow() is made to always return True."""

    def tearDown(self):
        with ratelimit._lock:
            ratelimit._buckets.clear()
            ratelimit._last_prune_ms = 0

    def test_bucket_denies_after_capacity_is_exhausted(self):
        results = [ratelimit.allow("sub:burst", capacity=3, refill_millis=0) for _ in range(5)]
        self.assertEqual(results[:3], [True, True, True], "first `capacity` requests must be allowed")
        self.assertEqual(results[3:], [False, False], "requests past capacity MUST be denied (allow() -> False)")

    def test_tokens_refill_after_elapsed_time_reenables(self):
        for _ in range(3):
            ratelimit.allow("sub:refill", capacity=3, refill_millis=1000)
        self.assertFalse(ratelimit.allow("sub:refill", capacity=3, refill_millis=1000))
        with ratelimit._lock:  # backdate last-refill so tokens accrue
            tokens, last = ratelimit._buckets["sub:refill"]
            ratelimit._buckets["sub:refill"] = (tokens, last - 2000)
        self.assertTrue(ratelimit.allow("sub:refill", capacity=3, refill_millis=1000))


class SessionForgeryTests(unittest.TestCase):
    """A well-formed session token whose HMAC the server did not produce must be rejected —
    the paywall/entitlement cannot be minted by an attacker; expiry is enforced. Complements
    the (narrow) malformed-base64 test, which dies before hmac.compare_digest is ever reached."""

    def test_wellformed_token_with_forged_signature_is_rejected(self):
        import base64 as _b64
        import json as _json
        import time as _time

        payload = {"sub": "attacker", "plan": "pro", "exp": int(_time.time()) + 10_000}
        raw = _json.dumps(payload, separators=(",", ":"), sort_keys=True).encode()
        body = _b64.urlsafe_b64encode(raw).decode().rstrip("=")
        forged = body + "." + _b64.urlsafe_b64encode(b"not-the-real-mac").decode().rstrip("=")
        self.assertIsNone(
            licensing.verify_session(forged), "forged-signature session token accepted -> paywall/evidence bypass"
        )

    def test_valid_minted_token_is_accepted(self):
        token, _exp = licensing.mint_session("legit", "pro")
        ent = licensing.verify_session(token)
        self.assertIsNotNone(ent)
        self.assertEqual(ent.subject, "legit")
        self.assertEqual(ent.plan, "pro")

    def test_expired_token_is_rejected(self):
        token, _exp = licensing.mint_session("legit", "pro", ttl=-1)
        self.assertIsNone(licensing.verify_session(token), "expired session token accepted")


if __name__ == "__main__":
    unittest.main()
