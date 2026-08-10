"""Manual-Warp server endpoint (/api/warp/raw) — the clean-path copy for a side-by-side diff.

The honesty rules here are quieter than a result surface but real: (1) it returns the SERVER's
copy verbatim and labels the vantage, so a client can compare against its own local fetch — it must
NOT slip in a "match"/"secure" judgement, because the comparison is the user's to make and the
server never saw the user's network; (2) binary content is refused, not returned as undiffable
bytes that would render as garbage and look like a "difference".
"""

import hashlib
import os
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


def _run(url, *, fetch_return=None, fetch_error=None):
    d = _Double()
    with patch("syxx_server.fetcher.fetch", side_effect=fetch_error, return_value=fetch_return) as f:
        if fetch_error is not None:
            f.side_effect = fetch_error
        with patch("syxx_server.db.audit", lambda *a, **k: None):
            app.Handler._route_warp_raw(d, {"url": [url]})
    return d.sent


def test_returns_the_servers_copy_verbatim_with_a_labeled_vantage():
    html = b"<html><body>hello</body></html>"
    sent = _run("https://example.com", fetch_return=(html, "text/html; charset=utf-8", "https://example.com/"))
    assert sent["status"] == 200
    b = sent["body"]
    assert b["content"] == html.decode()  # verbatim, not re-rendered
    assert b["vantage"] == "server-clean-path"  # vantage is explicit
    assert "match" not in b  # no result
    assert "secure" not in b
    # sha256 lets the client quick-compare without a full diff.
    assert b["sha256"] == hashlib.sha256(html).hexdigest()


def test_a_redirect_is_surfaced_as_a_different_final_url():
    sent = _run("http://example.com", fetch_return=(b"<p>ok</p>", "text/html", "https://example.com/landing"))
    assert sent["body"]["requestedUrl"] == "http://example.com"
    assert sent["body"]["finalUrl"] == "https://example.com/landing"


def test_binary_content_is_refused_not_returned_as_garbage():
    sent = _run(
        "https://example.com/logo.png", fetch_return=(b"\x89PNG\r\n", "image/png", "https://example.com/logo.png")
    )
    assert sent["status"] == 415


def test_a_blocked_target_is_400_not_a_fake_empty_page():
    sent = _run("http://169.254.169.254/", fetch_error=fetcher.BlockedError("blocked non-public address"))
    assert sent["status"] == 400


def test_an_upstream_failure_is_502_not_200_empty():
    sent = _run("https://example.com", fetch_error=fetcher.FetchError("upstream status 503"))
    assert sent["status"] == 502


def test_missing_url_is_400():
    d = _Double()
    app.Handler._route_warp_raw(d, {})
    assert d.sent["status"] == 400


def test_overlong_url_is_400():
    """A064: URLs longer than 2048 chars are rejected before any fetch."""
    d = _Double()
    app.Handler._route_warp_raw(d, {"url": ["https://example.com/" + "a" * 2100]})
    assert d.sent["status"] == 400


def test_blocked_error_body_does_not_leak_internal_detail():
    """A078: the client body must not echo resolved IPs / internal hostnames from
    the exception; the full error is logged server-side only."""
    sent = _run(
        "http://169.254.169.254/",
        fetch_error=fetcher.BlockedError("blocked non-public address 169.254.169.254 for metadata"),
    )
    assert sent["status"] == 400
    assert "169.254.169.254" not in sent.get("error", "")
    assert "blocked" in sent.get("error", "")


def test_fetch_error_body_does_not_leak_internal_detail():
    """A078: FetchError detail (DNS/socket internals) is redacted from the body."""
    sent = _run(
        "https://example.com",
        fetch_error=fetcher.FetchError("dns resolution failed: [Errno -2] Name or service not known"),
    )
    assert sent["status"] == 502
    assert "Name or service not known" not in sent.get("error", "")
    assert "fetch failed" in sent.get("error", "")
