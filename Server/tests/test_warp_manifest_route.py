"""WARP_MANIFEST_V1 route — /api/warp/manifest gating, validation, and error ladder.

Mirrors the discipline of the other warp routes: Pro-gate + rate-limit, url + nonce shape
validation, and the honest except-ladder (WarpInconclusive → 502, BlockedError → 400,
FetchError → 502). Also covers /api/warp/raw?raw=1 (byte-exact octet-stream + header) and the
deprecation stub at /api/warp/assembly.
"""

import os
import unittest
from unittest.mock import patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import app, fetcher, warp  # noqa: E402

BASE = "https://example.test/"


class _Double:
    def __init__(self, *, require_ok=True, rate_ok=True):
        self.sent = {}
        self.audits = []
        self._require_ok = require_ok
        self._rate = rate_ok

    def _require(self, query, enforce):
        if self._require_ok:
            return None, True
        self.sent = {"status": 402}
        return None, False

    def _rate_ok(self, ent):
        if self._rate:
            return True
        self.sent = {"status": 429}
        return False

    def _client_ip(self):
        return "203.0.113.9"

    def _send(self, status, body, content_type="application/json", extra_headers=None):
        self.sent = {"status": status, "body": body, "ctype": content_type, "headers": extra_headers or {}}

    def _json_error(self, status, msg, extra=None):
        self.sent = {"status": status, "error": msg, "extra": extra or {}}


def _net(mapping):
    def fx(url, timeout=None, max_bytes=None, max_redirects=None):
        if url not in mapping:
            raise fetcher.FetchError(f"no route to {url}")
        return mapping[url]

    return fx


def _run(route, query, *, double=None, fetch_ex=None):
    d = double or _Double()
    with patch("syxx_server.db.audit", lambda *a, **k: d.audits.append(a)):
        if fetch_ex is not None:
            with patch("syxx_server.fetcher.fetch_ex", side_effect=fetch_ex):
                route(d, query)
        else:
            route(d, query)
    return d


class WarpManifestRouteTests(unittest.TestCase):
    def _ok_net(self):
        return {
            BASE: (b"<html><script src='/a.js'></script></html>", "text/html", "", BASE),
            BASE + "a.js": (b"x", "application/javascript", "", BASE + "a.js"),
        }

    def test_missing_url_is_400(self):
        d = _run(app.Handler._route_warp_manifest, {})
        self.assertEqual(d.sent["status"], 400)

    def test_valid_request_returns_the_signed_envelope(self):
        d = _run(app.Handler._route_warp_manifest, {"url": [BASE]}, fetch_ex=_net(self._ok_net()))
        self.assertEqual(d.sent["status"], 200)
        self.assertEqual(set(d.sent["body"]), {"manifest_b64", "signature_ed25519", "key_id"})
        # audited under the manifest path.
        self.assertTrue(any(a[1] == "/api/warp/manifest" for a in d.audits))

    def test_invalid_nonce_is_400_before_any_fetch(self):
        calls = []

        def fx(url, **k):
            calls.append(url)
            return b"<html></html>", "text/html", "", BASE

        d = _run(app.Handler._route_warp_manifest, {"url": [BASE], "nonce": ["bad nonce!"]}, fetch_ex=fx)
        self.assertEqual(d.sent["status"], 400)
        self.assertEqual(calls, [])  # rejected before touching the network

    def test_overlong_nonce_is_400(self):
        d = _run(app.Handler._route_warp_manifest, {"url": [BASE], "nonce": ["a" * 65]}, fetch_ex=_net(self._ok_net()))
        self.assertEqual(d.sent["status"], 400)

    def test_valid_nonce_shapes_are_accepted(self):
        d = _run(
            app.Handler._route_warp_manifest, {"url": [BASE], "nonce": ["A-z0_9" * 2]}, fetch_ex=_net(self._ok_net())
        )
        self.assertEqual(d.sent["status"], 200)

    def test_pro_gate_blocks_unentitled(self):
        d = _Double(require_ok=False)
        _run(app.Handler._route_warp_manifest, {"url": [BASE]}, double=d, fetch_ex=_net(self._ok_net()))
        self.assertEqual(d.sent["status"], 402)

    def test_rate_limit_blocks(self):
        d = _Double(rate_ok=False)
        _run(app.Handler._route_warp_manifest, {"url": [BASE]}, double=d, fetch_ex=_net(self._ok_net()))
        self.assertEqual(d.sent["status"], 429)

    def test_html_unfetchable_is_502(self):
        def fx(url, **k):
            raise fetcher.FetchError("upstream status 503")

        d = _run(app.Handler._route_warp_manifest, {"url": [BASE]}, fetch_ex=fx)
        self.assertEqual(d.sent["status"], 502)

    def test_blocked_target_is_400(self):
        def fx(url, **k):
            raise fetcher.BlockedError("blocked non-public address")

        d = _run(app.Handler._route_warp_manifest, {"url": [BASE]}, fetch_ex=fx)
        self.assertEqual(d.sent["status"], 400)


class WarpRawByteModeTests(unittest.TestCase):
    def test_raw1_returns_octet_stream_with_sha_header(self):
        body = b"\x00\x01\x02\xff not utf-8 \xfe"
        d = _Double()

        def fx(url, timeout=None, max_bytes=None, max_redirects=None):
            return body, "application/octet-stream", "", "https://example.com/x"

        with patch("syxx_server.db.audit", lambda *a, **k: None):
            with patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
                app.Handler._route_warp_raw(d, {"url": ["https://example.com/x"], "raw": ["1"]})
        self.assertEqual(d.sent["status"], 200)
        self.assertEqual(d.sent["ctype"], "application/octet-stream")
        self.assertEqual(d.sent["body"], body)  # exact bytes, no utf-8-replace
        import hashlib

        self.assertEqual(d.sent["headers"]["X-Syxx-Warp-Sha256"], hashlib.sha256(body).hexdigest())

    def test_raw1_envelope_decodes_before_hashing(self):
        import gzip

        plain = b"body{color:red}"
        d = _Double()

        def fx(url, timeout=None, max_bytes=None, max_redirects=None):
            return gzip.compress(plain, mtime=0), "text/css", "gzip", "https://example.com/s.css"

        with patch("syxx_server.db.audit", lambda *a, **k: None):
            with patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
                app.Handler._route_warp_raw(d, {"url": ["https://example.com/s.css"], "raw": ["1"]})
        self.assertEqual(d.sent["body"], plain)  # decoded, not the gzip envelope
        import hashlib

        self.assertEqual(d.sent["headers"]["X-Syxx-Warp-Sha256"], hashlib.sha256(plain).hexdigest())

    def test_raw1_blocked_is_400(self):
        d = _Double()

        def fx(url, **k):
            raise fetcher.BlockedError("blocked")

        with patch("syxx_server.db.audit", lambda *a, **k: None):
            with patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
                app.Handler._route_warp_raw(d, {"url": ["http://169.254.169.254/"], "raw": ["1"]})
        self.assertEqual(d.sent["status"], 400)


class WarpAssemblyDeprecationTests(unittest.TestCase):
    def test_assembly_route_is_gone_410_with_successor(self):
        d = _Double()
        with patch("syxx_server.db.audit", lambda *a, **k: None):
            app.Handler._route_warp_assembly(d, {"url": [BASE]})
        self.assertEqual(d.sent["status"], 410)
        self.assertEqual(d.sent["extra"]["successor"], "/api/warp/manifest")

    def test_assembler_machinery_is_deleted(self):
        for gone in (
            "assemble",
            "build_full_blob",
            "build_degraded_blob",
            "_extract_static_subtrees",
            "_AssemblyParser",
        ):
            self.assertFalse(hasattr(warp, gone), f"{gone} must be retired")


if __name__ == "__main__":
    unittest.main()
