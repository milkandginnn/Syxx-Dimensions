"""Tests for syxx_server.seo — SEO metadata injection for public HTML pages.

Covers tag generation, HTML escaping, idempotent injection, JSON-LD
structured data, and page title lookup.
"""

import os

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")

import pytest

from syxx_server import seo


class TestEsc:
    """SM-046: attribute values must be fully escaped."""

    def test_ampersand(self):
        assert "&amp;" in seo._esc("foo&bar")

    def test_double_quote(self):
        assert "&quot;" in seo._esc('say "hello"')

    def test_single_quote(self):
        assert "&#39;" in seo._esc("it's")

    def test_angle_brackets(self):
        assert "&lt;" in seo._esc("<script>")
        assert "&gt;" in seo._esc("<script>")


class TestMetaTag:
    def test_name_attr(self):
        tag = seo._meta_tag("description", "A DNS tool")
        assert 'name="description"' in tag
        assert 'content="A DNS tool"' in tag

    def test_property_attr(self):
        tag = seo._meta_tag("og:title", "Syxx", attr="property")
        assert 'property="og:title"' in tag


class TestLinkCanonical:
    def test_relative_path(self):
        tag = seo._link_canonical("/leak-test")
        assert f'href="{seo.CANONICAL_ORIGIN}/leak-test"' in tag

    def test_absolute_url(self):
        tag = seo._link_canonical("https://syxx.app/docs")
        assert 'href="https://syxx.app/docs"' in tag


class TestBuildTags:
    def test_empty_meta(self):
        assert seo._build_tags({}) == ""

    def test_description_and_keywords(self):
        tags = seo._build_tags({"description": "test desc", "keywords": "a,b"})
        assert 'name="description"' in tags
        assert 'name="keywords"' in tags

    def test_og_tags(self):
        tags = seo._build_tags(
            {
                "og_title": "Syxx",
                "og_description": "DNS tool",
                "og_type": "website",
            }
        )
        assert 'property="og:title"' in tags
        assert 'property="og:description"' in tags
        assert 'property="og:type"' in tags

    def test_twitter_tags(self):
        tags = seo._build_tags({"twitter_card": "summary", "twitter_site": "@syxx"})
        assert 'name="twitter:card"' in tags
        assert 'name="twitter:site"' in tags

    def test_json_ld_injection(self):
        tags = seo._build_tags({"json_ld": '{"@type":"SoftwareApplication"}'})
        assert "application/ld+json" in tags

    def test_json_ld_escapes_script_tags(self):
        """A200: prevent </script> injection via price fields."""
        malicious = '{"price":"</script><script>alert(1)</script>"}'
        tags = seo._build_tags({"json_ld": malicious})
        assert "<script>" not in tags.replace('<script type="application/ld+json">', "")

    def test_robots_directive(self):
        tags = seo._build_tags({"robots": "noindex, nofollow"})
        assert 'name="robots"' in tags
        assert "noindex" in tags


class TestInjectMetaTags:
    def test_unknown_path_returns_unchanged(self):
        html = "<html><head></head><body>hi</body></html>"
        assert seo.inject_meta_tags(html, "/nonexistent") == html

    def test_landing_page_injects_tags(self):
        html = "<html><head>\n</head><body>hi</body></html>"
        result = seo.inject_meta_tags(html, "/")
        assert "Syxx Dimensions" in result
        assert 'name="description"' in result
        assert 'rel="canonical"' in result

    def test_idempotent_replaces_existing(self):
        html = '<html><head>\n<meta name="description" content="old description">\n</head><body></body></html>'
        result = seo.inject_meta_tags(html, "/")
        assert "old description" not in result
        assert 'name="description"' in result

    def test_leak_test_page(self):
        html = "<html><head>\n</head><body>test</body></html>"
        result = seo.inject_meta_tags(html, "/leak-test")
        assert "DNS Leak Test" in result

    def test_no_head_tag_prepends(self):
        html = "<html><body>no head</body></html>"
        result = seo.inject_meta_tags(html, "/")
        assert "Syxx Dimensions" in result

    def test_privacy_has_noindex(self):
        html = "<html><head>\n</head><body></body></html>"
        result = seo.inject_meta_tags(html, "/privacy")
        assert "noindex" in result


class TestPageTitle:
    def test_known_page(self):
        title = seo.page_title("/")
        assert title is not None
        assert "Syxx" in title

    def test_unknown_page(self):
        assert seo.page_title("/nonexistent") is None

    def test_leak_test_title(self):
        title = seo.page_title("/leak-test")
        assert title is not None
        assert "Leak" in title


class TestPageMetaIntegrity:
    def test_all_pages_have_titles(self):
        for path, meta in seo.PAGE_META.items():
            assert "title" in meta, f"{path} missing title"

    def test_all_pages_have_canonical(self):
        for path, meta in seo.PAGE_META.items():
            assert "canonical" in meta, f"{path} missing canonical"

    def test_canonical_origin_is_https(self):
        assert seo.CANONICAL_ORIGIN.startswith("https://")

    def test_json_ld_valid_json(self):
        """The landing page JSON-LD must be valid JSON."""
        import json

        meta = seo.PAGE_META["/"]
        if "json_ld" in meta:
            parsed = json.loads(meta["json_ld"])
            assert parsed["@type"] == "SoftwareApplication"


class TestPriceEscaping:
    """A062: env-driven SEO price values are HTML-escaped before injection into
    served pages, so an operator value like `3.95<script>...</script>` cannot
    inject markup."""

    class _Capture:
        def __init__(self):
            self.sent = {}

        def _send(self, status, body, *a, **k):
            self.sent = {"status": status, "body": body}

        def _json_error(self, status, msg, *a, **k):
            self.sent = {"status": status, "error": msg}

    def test_price_env_values_are_html_escaped(self, tmp_path):
        from syxx_server import app as _app
        from syxx_server import config as _cfg

        (tmp_path / "index.html").write_text("<html><body>$3.95 $19.95</body></html>", encoding="utf-8")
        orig_dir = _cfg.STATIC_DIR
        orig_monthly = _cfg.SEO_PRICE_MONTHLY
        orig_yearly = _cfg.SEO_PRICE_YEARLY
        _cfg.STATIC_DIR = str(tmp_path)
        _cfg.SEO_PRICE_MONTHLY = "3.95<script>alert(1)</script>"
        _cfg.SEO_PRICE_YEARLY = '19.95" onmouseover="x'
        try:
            d = self._Capture()
            _app.Handler._serve_static(d, "/")
            body = d.sent["body"]
            body_text = body.decode("utf-8", "replace")
            assert "<script>alert(1)</script>" not in body_text
            assert "&lt;script&gt;alert(1)&lt;/script&gt;" in body_text
            assert "&quot; onmouseover=&quot;x" in body_text
        finally:
            _cfg.STATIC_DIR = orig_dir
            _cfg.SEO_PRICE_MONTHLY = orig_monthly
            _cfg.SEO_PRICE_YEARLY = orig_yearly
