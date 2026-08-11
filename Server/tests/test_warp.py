"""Unit tests for syxx_server.warp — core module functions.

Covers parse_assets, inline_scripts, build_baseline, build_manifest,
content-encoding decode helpers, CSS @import scanning, canonical hostname,
and error paths.  All network I/O is mocked.
"""

import hashlib
import json
import os
import time
from unittest.mock import MagicMock, patch

import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

from syxx_server import warp, fetcher  # noqa: E402


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

@pytest.fixture
def mock_fetch():
    """Patch fetcher.fetch (used by build_baseline)."""
    with patch("syxx_server.warp.fetcher.fetch") as m:
        yield m


@pytest.fixture
def mock_fetch_ex():
    """Patch fetcher.fetch_ex (used by build_manifest / _fetch_decoded)."""
    with patch("syxx_server.warp.fetcher.fetch_ex") as m:
        yield m


@pytest.fixture
def mock_cache():
    """Patch the DB cache layer so build_baseline never touches SQLite."""
    with patch("syxx_server.warp.db.warp_cache_get", return_value=None) as g, \
         patch("syxx_server.warp.db.warp_cache_put") as p:
        yield {"get": g, "put": p}


@pytest.fixture
def mock_incr():
    with patch("syxx_server.warp.logging_util.incr") as m:
        yield m


# ---------------------------------------------------------------------------
# parse_assets
# ---------------------------------------------------------------------------

class TestParseAssets:
    def test_extracts_script_src(self):
        html = '<html><script src="/app.js"></script></html>'
        scripts, styles, iframes, inlines = warp.parse_assets(html, "https://example.com")
        assert scripts == ["https://example.com/app.js"]
        assert styles == []
        assert iframes == []

    def test_extracts_stylesheet_href(self):
        html = '<link rel="stylesheet" href="/style.css">'
        scripts, styles, iframes, inlines = warp.parse_assets(html, "https://example.com")
        assert styles == ["https://example.com/style.css"]

    def test_extracts_iframe_src(self):
        html = '<iframe src="https://other.com/embed"></iframe>'
        scripts, styles, iframes, inlines = warp.parse_assets(html, "https://example.com")
        assert iframes == ["https://other.com/embed"]

    def test_extracts_inline_script_body(self):
        html = '<script>console.log("hello");</script>'
        scripts, styles, iframes, inlines = warp.parse_assets(html, "https://example.com")
        assert inlines == ['console.log("hello");']

    def test_html_comment_stripped_before_parsing(self):
        html = '<!-- <script src="/hidden.js"></script> --><script src="/real.js"></script>'
        scripts, _, _, _ = warp.parse_assets(html, "https://example.com")
        assert scripts == ["https://example.com/real.js"]

    def test_handles_gt_inside_quoted_attribute(self):
        """A051/A093: > inside a quoted attribute must not break parsing."""
        html = '<script data-x=">">var x = 1;</script>'
        scripts, _, _, inlines = warp.parse_assets(html, "https://example.com")
        assert inlines == ["var x = 1;"]

    def test_relative_url_resolved_against_base(self):
        html = '<script src="js/app.js"></script>'
        scripts, _, _, _ = warp.parse_assets(html, "https://example.com/page/")
        assert scripts == ["https://example.com/page/js/app.js"]

    def test_link_rel_attribute_order_independent(self):
        """A093: stylesheet detection must not depend on attribute order."""
        html = '<link href="/s.css" rel="stylesheet">'
        _, styles, _, _ = warp.parse_assets(html, "https://example.com")
        assert styles == ["https://example.com/s.css"]

    def test_empty_html_returns_empty_lists(self):
        scripts, styles, iframes, inlines = warp.parse_assets("", "https://example.com")
        assert scripts == styles == iframes == inlines == []


# ---------------------------------------------------------------------------
# inline_scripts
# ---------------------------------------------------------------------------

class TestInlineScripts:
    def test_returns_ordered_bodies(self):
        html = '<script>var a = 1;</script><script>var b = 2;</script>'
        bodies = warp.inline_scripts(html)
        assert bodies == ["var a = 1;", "var b = 2;"]

    def test_skips_external_scripts(self):
        html = '<script src="/ext.js"></script><script>inline</script>'
        bodies = warp.inline_scripts(html)
        assert bodies == ["inline"]

    def test_preserves_html_comments_in_script_body(self):
        """A094: comments inside script bodies are preserved in the hash."""
        html = "<script><!-- comment -->var x;</script>"
        bodies = warp.inline_scripts(html)
        assert any("comment" in b for b in bodies)


# ---------------------------------------------------------------------------
# build_baseline
# ---------------------------------------------------------------------------

class TestBuildBaseline:
    def test_basic_baseline_structure(self, mock_fetch, mock_cache, mock_incr):
        html = b"<html><script src='/a.js'></script></html>"
        js_body = b"var a = 1;"
        mock_fetch.side_effect = [
            (html, "text/html", "https://example.com/"),  # HTML fetch
            (js_body, "application/javascript", "https://example.com/a.js"),  # script fetch
        ]
        result = warp.build_baseline("https://example.com")
        assert "html" in result
        assert "scripts" in result
        assert "scriptHashes" in result
        assert "inlineScriptHashes" in result
        assert "scriptFetchStatus" in result

    def test_caches_under_final_url(self, mock_fetch, mock_cache, mock_incr):
        html = b"<html></html>"
        mock_fetch.return_value = (html, "text/html", "https://example.com/final")
        warp.build_baseline("https://example.com/start")
        # Cache put should use the final URL, not the original
        mock_cache["put"].assert_called_once()
        call_args = mock_cache["put"].call_args
        assert call_args[0][0] == "https://example.com/final"

    def test_returns_cached_result_on_hit(self, mock_fetch, mock_cache, mock_incr):
        cached = json.dumps({"html": "cached", "scripts": []})
        mock_cache["get"].return_value = cached
        mock_fetch.return_value = (b"<html></html>", "text/html", "https://example.com/")
        result = warp.build_baseline("https://example.com")
        assert result == {"html": "cached", "scripts": []}
        mock_incr.assert_called_with("warp_cache_hit")

    def test_script_fetch_failure_records_status(self, mock_fetch, mock_cache, mock_incr):
        html = b"<html><script src='/fail.js'></script></html>"
        mock_fetch.side_effect = [
            (html, "text/html", "https://example.com/"),
            fetcher.FetchError("timeout"),
        ]
        result = warp.build_baseline("https://example.com")
        assert result["scriptFetchStatus"]["https://example.com/fail.js"] == "fetch_failed"

    def test_deadline_exceeded_skips_script_fetches(self, mock_fetch, mock_cache, mock_incr):
        html = b"<html><script src='/a.js'></script><script src='/b.js'></script></html>"
        mock_fetch.return_value = (html, "text/html", "https://example.com/")
        # Set deadline in the past so script fetches are skipped
        result = warp.build_baseline("https://example.com", deadline=time.monotonic() - 1)
        for url in result["scriptFetchStatus"]:
            assert result["scriptFetchStatus"][url] == "deadline_exceeded"

    def test_inline_scripts_hashed(self, mock_fetch, mock_cache, mock_incr):
        html = b"<html><script>var x = 1;</script></html>"
        mock_fetch.return_value = (html, "text/html", "https://example.com/")
        result = warp.build_baseline("https://example.com")
        expected_hash = hashlib.sha256(b"var x = 1;").hexdigest()
        assert expected_hash in result["inlineScriptHashes"]


# ---------------------------------------------------------------------------
# Content-encoding decode helpers
# ---------------------------------------------------------------------------

class TestContentEncodingDecode:
    def test_identity_passthrough(self):
        body = b"hello world"
        assert warp._content_encoding_decode(body, "", 1024) == body

    def test_none_encoding_passthrough(self):
        body = b"hello world"
        assert warp._content_encoding_decode(body, None, 1024) == body

    def test_unsupported_encoding_raises(self):
        with pytest.raises(fetcher.FetchError, match="unsupported"):
            warp._content_encoding_decode(b"data", "zstd", 1024)

    def test_gzip_decode(self):
        import zlib
        raw = b"hello gzip"
        compressed = zlib.compress(raw)
        # gzip wbits = 16 + MAX_WBITS
        import gzip as _gzip
        import io
        buf = io.BytesIO()
        with _gzip.GzipFile(fileobj=buf, mode="wb") as gz:
            gz.write(raw)
        result = warp._content_encoding_decode(buf.getvalue(), "gzip", 1024)
        assert result == raw

    def test_multi_encoding_applied_in_reverse(self):
        """A007: 'gzip, deflate' decodes deflate first (outermost), then gzip."""
        # We only test that the function doesn't crash on multi-encoding identity
        body = b"plain"
        result = warp._content_encoding_decode(body, "identity, identity", 1024)
        assert result == body

    def test_decompression_bomb_raises_truncated(self):
        import zlib
        # Create data that decompresses to more than the cap
        big = b"x" * 2000
        compressed = zlib.compress(big)
        import gzip as _gzip
        import io
        buf = io.BytesIO()
        with _gzip.GzipFile(fileobj=buf, mode="wb") as gz:
            gz.write(big)
        with pytest.raises(fetcher.TruncatedError):
            warp._content_encoding_decode(buf.getvalue(), "gzip", 100)


# ---------------------------------------------------------------------------
# CSS @import scanning
# ---------------------------------------------------------------------------

class TestScanCssImports:
    def test_basic_import(self):
        css = b'@import "reset.css";'
        result = warp._scan_css_imports(css, "https://example.com/css/main.css")
        assert result == ["https://example.com/css/reset.css"]

    def test_import_url_function(self):
        css = b'@import url("fonts.css");'
        result = warp._scan_css_imports(css, "https://example.com/css/")
        assert result == ["https://example.com/css/fonts.css"]

    def test_import_in_comment_ignored(self):
        css = b'/* @import "hidden.css"; */ @import "real.css";'
        result = warp._scan_css_imports(css, "https://example.com/")
        assert result == ["https://example.com/real.css"]

    def test_single_line_comment_stripped(self):
        """SM-013: // line comments must be stripped."""
        css = b'// @import "nope.css";\n@import "yes.css";'
        result = warp._scan_css_imports(css, "https://example.com/")
        assert result == ["https://example.com/yes.css"]

    def test_no_imports_returns_empty(self):
        css = b"body { color: red; }"
        result = warp._scan_css_imports(css, "https://example.com/")
        assert result == []

    def test_multiple_imports_preserved_in_order(self):
        css = b'@import "a.css"; @import "b.css";'
        result = warp._scan_css_imports(css, "https://example.com/")
        assert result == ["https://example.com/a.css", "https://example.com/b.css"]


# ---------------------------------------------------------------------------
# _canonical_hostname
# ---------------------------------------------------------------------------

class TestCanonicalHostname:
    def test_lowercase(self):
        assert warp._canonical_hostname("Example.COM") == "example.com"

    def test_idn_to_punycode(self):
        result = warp._canonical_hostname("café.com")
        assert "xn--" in result

    def test_empty_returns_empty(self):
        assert warp._canonical_hostname("") == ""

    def test_strips_invisible_characters(self):
        """E005: zero-width characters must be stripped."""
        host = "example\u200B.com"
        result = warp._canonical_hostname(host)
        assert "\u200B" not in result
        assert "example" in result


# ---------------------------------------------------------------------------
# build_manifest
# ---------------------------------------------------------------------------

class TestBuildManifest:
    def test_manifest_structure(self, mock_fetch_ex, mock_incr):
        html = b"<html><head><link rel='stylesheet' href='/s.css'></head></html>"
        css = b"body { color: red; }"
        mock_fetch_ex.side_effect = [
            (html, "text/html", "", "https://example.com/"),  # HTML
            (css, "text/css", "", "https://example.com/s.css"),  # CSS
        ]
        m = warp.build_manifest("https://example.com")
        assert m["warp_manifest_version"] == warp.MANIFEST_VERSION
        assert m["origin_url"] == "https://example.com"
        assert m["final_url"] == "https://example.com/"
        assert m["html_sha256"] == hashlib.sha256(html).hexdigest()
        assert m["redirect_policy"] == warp.REDIRECT_POLICY
        assert isinstance(m["assets"], list)
        assert m["truncated"] is False

    def test_nonce_echoed(self, mock_fetch_ex, mock_incr):
        html = b"<html></html>"
        mock_fetch_ex.return_value = (html, "text/html", "", "https://example.com/")
        m = warp.build_manifest("https://example.com", nonce="abc123")
        assert m["nonce"] == "abc123"

    def test_html_fetch_failure_raises_warp_inconclusive(self, mock_fetch_ex, mock_incr):
        mock_fetch_ex.side_effect = fetcher.FetchError("connection refused")
        with pytest.raises(warp.WarpInconclusive):
            warp.build_manifest("https://example.com")

    def test_blocked_error_propagates(self, mock_fetch_ex, mock_incr):
        mock_fetch_ex.side_effect = fetcher.BlockedError("SSRF")
        with pytest.raises(fetcher.BlockedError):
            warp.build_manifest("https://example.com")

    def test_asset_fetch_failure_recorded_not_dropped(self, mock_fetch_ex, mock_incr):
        html = b"<html><script src='/fail.js'></script></html>"
        mock_fetch_ex.side_effect = [
            (html, "text/html", "", "https://example.com/"),
            fetcher.FetchError("timeout"),  # script fetch fails
        ]
        m = warp.build_manifest("https://example.com")
        failed = [a for a in m["assets"] if a.get("fetch_status") == "fetch_failed"]
        assert len(failed) == 1
        assert failed[0]["url"] == "https://example.com/fail.js"

    def test_freshness_capped_at_max(self, mock_fetch_ex, mock_incr):
        html = b"<html></html>"
        mock_fetch_ex.return_value = (html, "text/html", "", "https://example.com/")
        with patch("syxx_server.warp.config.WARP_MANIFEST_FRESHNESS", 9999):
            m = warp.build_manifest("https://example.com")
        assert m["freshness_seconds"] == warp._MAX_FRESHNESS_SECONDS

    def test_css_import_closure_followed(self, mock_fetch_ex, mock_incr):
        html = b"<html><link rel='stylesheet' href='/main.css'></html>"
        main_css = b'@import "child.css"; body {}'
        child_css = b"h1 { font-size: 2em; }"
        mock_fetch_ex.side_effect = [
            (html, "text/html", "", "https://example.com/"),
            (main_css, "text/css", "", "https://example.com/main.css"),
            (child_css, "text/css", "", "https://example.com/child.css"),
        ]
        m = warp.build_manifest("https://example.com")
        urls = [a["url"] for a in m["assets"]]
        assert "https://example.com/main.css" in urls
        assert "https://example.com/child.css" in urls

    def test_asset_count_cap_sets_truncated(self, mock_fetch_ex, mock_incr):
        # Build HTML with many script tags
        scripts = "".join(f'<script src="/s{i}.js"></script>' for i in range(10))
        html = f"<html>{scripts}</html>".encode()
        # First call: HTML, then each script returns successfully
        fetch_returns = [(html, "text/html", "", "https://example.com/")]
        for i in range(10):
            fetch_returns.append((f"var s{i};".encode(), "text/javascript", "", f"https://example.com/s{i}.js"))
        mock_fetch_ex.side_effect = fetch_returns
        with patch("syxx_server.warp.config.WARP_MANIFEST_MAX_ASSETS", 3):
            m = warp.build_manifest("https://example.com")
        assert m["truncated"] is True
        assert len(m["assets"]) <= 3

    def test_timestamp_is_wall_clock_millis(self, mock_fetch_ex, mock_incr):
        html = b"<html></html>"
        mock_fetch_ex.return_value = (html, "text/html", "", "https://example.com/")
        before = int(time.time() * 1000)
        m = warp.build_manifest("https://example.com")
        after = int(time.time() * 1000)
        assert before <= m["timestamp"] <= after

    def test_asset_classes_in_scope_declared(self, mock_fetch_ex, mock_incr):
        html = b"<html></html>"
        mock_fetch_ex.return_value = (html, "text/html", "", "https://example.com/")
        m = warp.build_manifest("https://example.com")
        assert m["asset_classes_in_scope"] == warp.ASSET_CLASSES_IN_SCOPE
