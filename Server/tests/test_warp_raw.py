"""Manual-Warp server endpoint (/api/warp/raw) — the clean-path copy for a side-by-side diff.

The honesty rules here are quieter than a result surface but real: (1) it returns the SERVER's
copy verbatim and labels the vantage, so a client can compare against its own local fetch — it must
NOT slip in a "match"/"secure" judgement, because the comparison is the user's to make and the
server never saw the user's network; (2) binary content is refused, not returned as undiffable
bytes that would render as garbage and look like a "difference".
"""

import os
import unittest
from unittest.mock import patch

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import app, fetcher  # noqa: E402


class _Double:
    """Captures the route's output without an HTTP server. ENFORCE_NATIVE is bypassed by making
    _require return an anonymous-ok, and rate/audit/client-ip are stubbed."""

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


class WarpRawTests(unittest.TestCase):
    def _run(self, url, *, fetch_return=None, fetch_error=None):
        d = _Double()
        with patch("syxx_server.fetcher.fetch", side_effect=fetch_error, return_value=fetch_return) as f:
            if fetch_error is not None:
                f.side_effect = fetch_error
            with patch("syxx_server.db.audit", lambda *a, **k: None):
                app.Handler._route_warp_raw(d, {"url": [url]})
        return d.sent

    def test_returns_the_servers_copy_verbatim_with_a_labeled_vantage(self):
        html = b"<html><body>hello</body></html>"
        sent = self._run("https://example.com", fetch_return=(html, "text/html; charset=utf-8", "https://example.com/"))
        self.assertEqual(sent["status"], 200)
        b = sent["body"]
        self.assertEqual(b["content"], html.decode())  # verbatim, not re-rendered
        self.assertEqual(b["vantage"], "server-clean-path")  # vantage is explicit
        self.assertNotIn("match", b)  # no result
        self.assertNotIn("secure", b)
        # sha256 lets the client quick-compare without a full diff.
        import hashlib

        self.assertEqual(b["sha256"], hashlib.sha256(html).hexdigest())

    def test_a_redirect_is_surfaced_as_a_different_final_url(self):
        sent = self._run("http://example.com", fetch_return=(b"<p>ok</p>", "text/html", "https://example.com/landing"))
        self.assertEqual(sent["body"]["requestedUrl"], "http://example.com")
        self.assertEqual(sent["body"]["finalUrl"], "https://example.com/landing")

    def test_binary_content_is_refused_not_returned_as_garbage(self):
        sent = self._run(
            "https://example.com/logo.png", fetch_return=(b"\x89PNG\r\n", "image/png", "https://example.com/logo.png")
        )
        self.assertEqual(sent["status"], 415)

    def test_a_blocked_target_is_400_not_a_fake_empty_page(self):
        sent = self._run("http://169.254.169.254/", fetch_error=fetcher.BlockedError("blocked non-public address"))
        self.assertEqual(sent["status"], 400)

    def test_an_upstream_failure_is_502_not_200_empty(self):
        sent = self._run("https://example.com", fetch_error=fetcher.FetchError("upstream status 503"))
        self.assertEqual(sent["status"], 502)

    def test_missing_url_is_400(self):
        d = _Double()
        app.Handler._route_warp_raw(d, {})
        self.assertEqual(d.sent["status"], 400)


if __name__ == "__main__":
    unittest.main()
