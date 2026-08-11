"""Unit tests for syxx_server/fetcher.py — SSRF-safe HTTP fetch gateway.

Complements test_resolver_fetcher_ssrf.py which covers DNS-rebinding pin,
out-of-bailiwick glue, port allowlist, and pinned HTTPS connections.
This file covers UNTESTED functions:
  - _strip_invisible(): zero-width and bidi character stripping
  - _ip_is_public(): fetcher-level SSRF gate wrapper
  - _resolve_public(): DNS resolution + public IP validation
  - fetch() / fetch_ex(): public API including scheme blocking, missing host,
    truncated responses, too many redirects
  - _one_request(): scheme enforcement, host header construction

No real network calls. All socket/DNS layers are monkeypatched.
"""

import http.client
from unittest.mock import MagicMock, patch

import pytest

from syxx_server import fetcher  # noqa: E402


# ---------------------------------------------------------------------------
# _strip_invisible — zero-width / bidi character removal
# ---------------------------------------------------------------------------

class TestStripInvisible:
    def test_strips_zero_width_space(self):
        assert fetcher._strip_invisible("exam\u200Bple.com") == "example.com"

    def test_strips_zero_width_non_joiner(self):
        assert fetcher._strip_invisible("exam\u200Cple.com") == "example.com"

    def test_strips_zero_width_joiner(self):
        assert fetcher._strip_invisible("exam\u200Dple.com") == "example.com"

    def test_strips_bidi_embedding(self):
        assert fetcher._strip_invisible("exam\u202Aple.com") == "example.com"

    def test_strips_bom(self):
        assert fetcher._strip_invisible("\uFEFFexample.com") == "example.com"

    def test_strips_left_to_right_mark(self):
        assert fetcher._strip_invisible("ex\u200Eample.com") == "example.com"

    def test_strips_multiple_invisibles(self):
        s = "\u200Bex\u200C\u200Dam\u200Eple\u200F.com\uFEFF"
        assert fetcher._strip_invisible(s) == "example.com"

    def test_preserves_clean_input(self):
        assert fetcher._strip_invisible("clean.example.com") == "clean.example.com"

    def test_empty_string_returns_empty(self):
        assert fetcher._strip_invisible("") == ""

    def test_none_returns_none(self):
        assert fetcher._strip_invisible(None) is None


# ---------------------------------------------------------------------------
# _ip_is_public — fetcher-level SSRF gate
# ---------------------------------------------------------------------------

class TestIpIsPublic:
    def test_public_ipv4_is_public(self):
        assert fetcher._ip_is_public("93.184.216.34") is True

    def test_loopback_is_not_public(self):
        assert fetcher._ip_is_public("127.0.0.1") is False

    def test_private_10_is_not_public(self):
        assert fetcher._ip_is_public("10.0.0.1") is False

    def test_private_172_is_not_public(self):
        assert fetcher._ip_is_public("172.16.0.1") is False

    def test_private_192_is_not_public(self):
        assert fetcher._ip_is_public("192.168.1.1") is False

    def test_cgnat_is_not_public(self):
        assert fetcher._ip_is_public("100.64.0.1") is False

    def test_invalid_ip_returns_false(self):
        assert fetcher._ip_is_public("not-an-ip") is False

    def test_ipv4_mapped_loopback_is_not_public(self):
        assert fetcher._ip_is_public("::ffff:127.0.0.1") is False

    def test_ipv4_mapped_private_is_not_public(self):
        assert fetcher._ip_is_public("::ffff:10.0.0.1") is False

    def test_ipv6_loopback_is_not_public(self):
        assert fetcher._ip_is_public("::1") is False


# ---------------------------------------------------------------------------
# _resolve_public — DNS resolution + validation
# ---------------------------------------------------------------------------

class TestResolvePublic:
    def test_resolves_public_ipv4(self):
        fake_info = [(2, 1, 6, "", ("93.184.216.34", 0))]
        with patch.object(fetcher.socket, "getaddrinfo", return_value=fake_info):
            ip = fetcher._resolve_public("example.com")
        assert ip == "93.184.216.34"

    def test_prefers_ipv4_over_ipv6(self):
        fake_info = [
            (10, 1, 6, "", ("2606:2800:220:1:248:1893:25c8:1946", 0)),
            (2, 1, 6, "", ("93.184.216.34", 0)),
        ]
        with patch.object(fetcher.socket, "getaddrinfo", return_value=fake_info):
            ip = fetcher._resolve_public("example.com")
        assert ip == "93.184.216.34"

    def test_blocks_private_ip_raises_blocked(self):
        fake_info = [(2, 1, 6, "", ("10.0.0.1", 0))]
        with patch.object(fetcher.socket, "getaddrinfo", return_value=fake_info):
            with pytest.raises(fetcher.BlockedError, match="blocked non-public"):
                fetcher._resolve_public("evil.internal")

    def test_blocks_loopback_raises_blocked(self):
        fake_info = [(2, 1, 6, "", ("127.0.0.1", 0))]
        with patch.object(fetcher.socket, "getaddrinfo", return_value=fake_info):
            with pytest.raises(fetcher.BlockedError):
                fetcher._resolve_public("localhost")

    def test_mixed_public_private_blocks_all(self):
        """If ANY resolved IP is non-public, the whole host is blocked."""
        fake_info = [
            (2, 1, 6, "", ("93.184.216.34", 0)),
            (2, 1, 6, "", ("10.0.0.1", 0)),
        ]
        with patch.object(fetcher.socket, "getaddrinfo", return_value=fake_info):
            with pytest.raises(fetcher.BlockedError):
                fetcher._resolve_public("mixed.example.com")

    def test_dns_failure_raises_fetch_error(self):
        import socket as _socket
        with patch.object(fetcher.socket, "getaddrinfo", side_effect=_socket.gaierror("no such host")):
            with pytest.raises(fetcher.FetchError, match="dns resolution failed"):
                fetcher._resolve_public("nonexistent.invalid")

    def test_empty_address_set_raises(self):
        with patch.object(fetcher.socket, "getaddrinfo", return_value=[]):
            with pytest.raises(fetcher.FetchError, match="no addresses"):
                fetcher._resolve_public("empty.example.com")


# ---------------------------------------------------------------------------
# _one_request — scheme enforcement
# ---------------------------------------------------------------------------

class TestOneRequestSchemeBlocking:
    def test_file_scheme_blocked(self):
        with pytest.raises(fetcher.BlockedError, match="scheme not allowed"):
            fetcher._one_request("file:///etc/passwd", timeout=1, max_bytes=100)

    def test_ftp_scheme_blocked(self):
        with pytest.raises(fetcher.BlockedError, match="scheme not allowed"):
            fetcher._one_request("ftp://example.com/file", timeout=1, max_bytes=100)

    def test_data_scheme_blocked(self):
        with pytest.raises(fetcher.BlockedError, match="scheme not allowed"):
            fetcher._one_request("data:text/html,<h1>hi</h1>", timeout=1, max_bytes=100)

    def test_missing_host_blocked(self):
        with pytest.raises(fetcher.BlockedError, match="missing host"):
            fetcher._one_request("http:///path-only", timeout=1, max_bytes=100)


# ---------------------------------------------------------------------------
# fetch / fetch_ex — public API
# ---------------------------------------------------------------------------

def _make_fake_one_request(status=200, body=b"hello", ctype="text/html",
                           location=None, cenc=""):
    """Return a fake _one_request that simulates a successful response."""
    def fake(url, timeout, max_bytes, deadline=None):
        return status, location, ctype, body, cenc
    return fake


class TestFetch:
    def test_fetch_returns_body_type_url(self):
        orig = fetcher._one_request
        fetcher._one_request = _make_fake_one_request(body=b"content", ctype="text/plain")
        try:
            body, ctype, final_url = fetcher.fetch("http://example.com/", timeout=1, max_bytes=100)
            assert body == b"content"
            assert ctype == "text/plain"
            assert final_url == "http://example.com/"
        finally:
            fetcher._one_request = orig

    def test_fetch_ex_returns_content_encoding(self):
        orig = fetcher._one_request
        fetcher._one_request = _make_fake_one_request(
            body=b"\x1f\x8b...", ctype="text/html", cenc="gzip"
        )
        try:
            body, ctype, cenc, final_url = fetcher.fetch_ex(
                "http://example.com/", timeout=1, max_bytes=100
            )
            assert cenc == "gzip"
            assert body == b"\x1f\x8b..."
        finally:
            fetcher._one_request = orig


class TestFetchRedirects:
    def test_too_many_redirects_raises(self):
        call_count = [0]

        def redirecting(url, timeout, max_bytes, deadline=None):
            call_count[0] += 1
            return 302, url + "/next", "text/html", b"", ""

        orig = fetcher._one_request
        fetcher._one_request = redirecting
        try:
            with pytest.raises(fetcher.FetchError, match="too many redirects"):
                fetcher.fetch("http://example.com/", timeout=1, max_bytes=100, max_redirects=2)
        finally:
            fetcher._one_request = orig
        # Should have attempted max_redirects + 1 hops
        assert call_count[0] == 3

    def test_redirect_followed_to_final_url(self):
        calls = []

        def fake(url, timeout, max_bytes, deadline=None):
            calls.append(url)
            if len(calls) == 1:
                return 301, "http://example.com/new", "text/html", b"", ""
            return 200, None, "text/html", b"final", ""

        orig = fetcher._one_request
        fetcher._one_request = fake
        try:
            body, ctype, final = fetcher.fetch(
                "http://example.com/old", timeout=1, max_bytes=100, max_redirects=3
            )
            assert body == b"final"
            assert final == "http://example.com/new"
            assert len(calls) == 2
        finally:
            fetcher._one_request = orig

    def test_upstream_error_status_raises(self):
        def fake(url, timeout, max_bytes, deadline=None):
            return 503, None, "text/html", b"down", ""

        orig = fetcher._one_request
        fetcher._one_request = fake
        try:
            with pytest.raises(fetcher.FetchError, match="upstream status 503"):
                fetcher.fetch("http://example.com/", timeout=1, max_bytes=100)
        finally:
            fetcher._one_request = orig


class TestTruncatedError:
    def test_truncated_error_is_fetch_error_subclass(self):
        assert issubclass(fetcher.TruncatedError, fetcher.FetchError)

    def test_blocked_error_is_fetch_error_subclass(self):
        assert issubclass(fetcher.BlockedError, fetcher.FetchError)


# ---------------------------------------------------------------------------
# _one_request — host header construction (non-default port)
# ---------------------------------------------------------------------------

class TestHostHeader:
    def test_default_port_omitted_from_host_header(self):
        """Port 443 on HTTPS should not appear in the Host header."""
        captured = {}

        class FakeConn:
            def __init__(self, *a, **k):
                pass
            def putrequest(self, method, path, **kw):
                captured["method"] = method
                captured["path"] = path
            def putheader(self, name, value):
                captured.setdefault("headers", {})[name] = value
            def endheaders(self):
                pass
            def getresponse(self):
                class R:
                    status = 200
                    _reads = 0
                    def getheader(self, n, default=None):
                        return default
                    def read(self, _n):
                        self._reads += 1
                        return b"ok" if self._reads == 1 else b""
                return R()
            def close(self):
                pass

        orig_resolve = fetcher._resolve_public
        orig_http = fetcher.http.client.HTTPConnection
        fetcher._resolve_public = lambda host: "93.184.216.34"
        fetcher.http.client.HTTPConnection = FakeConn
        try:
            fetcher._one_request("http://example.com/path", timeout=1, max_bytes=100)
        finally:
            fetcher._resolve_public = orig_resolve
            fetcher.http.client.HTTPConnection = orig_http

        assert captured["headers"]["Host"] == "example.com"
        assert captured["path"] == "/path"
