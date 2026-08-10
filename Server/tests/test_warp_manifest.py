"""WARP_MANIFEST_V1 producer test — warp.build_manifest closure + raw-byte hashing.

Drives warp.build_manifest end-to-end with fetcher.fetch_ex mocked into a per-test
"network", so the SSRF gateway is stubbed but the enumerate → fetch → envelope-decode →
hash path runs exactly as in production. The load-bearing honesty properties:

  * the manifest covers the full transitive HTML + JS + CSS + CSS@import closure;
  * each asset's sha256 is over its RAW (envelope-decoded) bytes — gzip/br/identity of the
    same underlying bytes hash identically (spec §8);
  * an asset that will not fetch is recorded fetch_status:"fetch_failed", NEVER dropped
    (spec §3 → forces the client to INCONCLUSIVE, never a clean pass);
  * unfetchable HTML is WarpInconclusive, an SSRF-blocked target propagates BlockedError,
    and the per-run unique-host cap bounds the fan-out.
"""

import gzip
import hashlib
import os
from unittest.mock import patch

import brotli
import pytest

os.environ.setdefault("STRIPE_SECRET_KEY", "")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
from syxx_server import config, fetcher, warp  # noqa: E402

BASE = "https://example.test/"


def _net(mapping):
    """Build a fetch_ex double from {url: (raw_bytes, ctype, content_encoding, final_url)}."""

    def fetch_ex(url, timeout=None, max_bytes=None, max_redirects=None):
        if url not in mapping:
            raise fetcher.FetchError(f"no route to {url}")
        return mapping[url]

    return fetch_ex


def _asset(manifest, url):
    for a in manifest["assets"]:
        if a["url"] == url:
            return a
    return None


def test_transitive_closure_html_js_css_and_css_import():
    html = (
        b"<html><head>"
        b"<script src='/a.js'></script>"
        b"<link rel=stylesheet href='/s.css'>"
        b"<link rel=modulepreload href='/m.js'>"
        b"<link rel=preload as=script href='/p.js'>"
        b"</head></html>"
    )
    css = b'@import "/nested.css"; body{color:red}'
    nested = b".n{color:blue}"
    net = {
        BASE: (html, "text/html", "", BASE),
        BASE + "a.js": (b"console.log(1)", "application/javascript", "", BASE + "a.js"),
        BASE + "m.js": (b"export const x=1", "application/javascript", "", BASE + "m.js"),
        BASE + "p.js": (b"//preload", "application/javascript", "", BASE + "p.js"),
        BASE + "s.css": (css, "text/css", "", BASE + "s.css"),
        BASE + "nested.css": (nested, "text/css", "", BASE + "nested.css"),
    }
    with patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)):
        m = warp.build_manifest(BASE, nonce="abc-123_XYZ")

    assert m["warp_manifest_version"] == "WARP_MANIFEST_V1"
    assert m["origin_url"] == BASE
    assert m["final_url"] == BASE
    assert m["asset_classes_in_scope"] == ["HTML", "JS", "CSS", "CSS_IMPORT"]
    assert m["redirect_policy"] == "same-origin"
    assert m["allowed_redirects"] == []
    assert m["nonce"] == "abc-123_XYZ"
    assert m["freshness_seconds"] == config.WARP_MANIFEST_FRESHNESS

    # HTML hash is over the raw HTML bytes.
    assert m["html_sha256"] == hashlib.sha256(html).hexdigest()

    # Every declared asset is present, in the right class, plus the nested @import.
    classes = {a["url"]: a["class"] for a in m["assets"]}
    assert classes[BASE + "a.js"] == "JS"
    assert classes[BASE + "m.js"] == "JS"  # modulepreload → JS
    assert classes[BASE + "p.js"] == "JS"  # preload as=script → JS
    assert classes[BASE + "s.css"] == "CSS"
    assert classes[BASE + "nested.css"] == "CSS_IMPORT"


def test_per_asset_hash_is_over_raw_bytes():
    html = b"<html><script src='/a.js'></script></html>"
    body = b"const secret = 42;\n"
    net = {BASE: (html, "text/html", "", BASE), BASE + "a.js": (body, "application/javascript", "", BASE + "a.js")}
    with patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)):
        m = warp.build_manifest(BASE)
    a = _asset(m, BASE + "a.js")
    assert a["sha256"] == hashlib.sha256(body).hexdigest()
    assert a["bytes"] == len(body)
    assert a["final_url"] == BASE + "a.js"


def test_gzip_br_identity_hash_the_same():
    """Envelope-decode proof (spec §8): identity, gzip, and br delivery of the SAME
    underlying bytes yield the SAME sha256 — the hash is over the decoded bytes."""
    html = b"<html><script src='/a.js'></script></html>"
    body = b"function f(){ return 'hello'; }\n"
    expected = hashlib.sha256(body).hexdigest()

    def run(encoding, delivered):
        net = {
            BASE: (html, "text/html", "", BASE),
            BASE + "a.js": (delivered, "application/javascript", encoding, BASE + "a.js"),
        }
        with patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)):
            return warp.build_manifest(BASE)

    ident = run("", body)
    gz = run("gzip", gzip.compress(body, mtime=0))
    br = run("br", brotli.compress(body))
    assert _asset(ident, BASE + "a.js")["sha256"] == expected
    assert _asset(gz, BASE + "a.js")["sha256"] == expected
    assert _asset(br, BASE + "a.js")["sha256"] == expected


def test_undecodable_br_asset_is_fetch_failed_not_hashed_compressed():
    """A br body that will not decode must be recorded fetch_failed, never hashed while
    still compressed (spec §8 / honesty spine — a compressed-byte hash is false evidence)."""
    html = b"<html><script src='/a.js'></script></html>"
    net = {
        BASE: (html, "text/html", "", BASE),
        BASE + "a.js": (b"not-valid-brotli-bytes", "application/javascript", "br", BASE + "a.js"),
    }
    with patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)):
        m = warp.build_manifest(BASE)
    a = _asset(m, BASE + "a.js")
    assert a.get("fetch_status") == "fetch_failed"
    assert "sha256" not in a


def test_fetch_failed_asset_is_recorded_never_dropped():
    html = b"<html><script src='/gone.js'></script><script src='/ok.js'></script></html>"
    net = {
        BASE: (html, "text/html", "", BASE),
        BASE + "ok.js": (b"ok", "application/javascript", "", BASE + "ok.js"),
    }
    # /gone.js is absent from the network → FetchError → recorded fetch_failed.
    with patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)):
        m = warp.build_manifest(BASE)
    gone = _asset(m, BASE + "gone.js")
    assert gone is not None, "the failed asset must be recorded, not dropped"
    assert gone["fetch_status"] == "fetch_failed"
    assert "sha256" not in gone
    assert _asset(m, BASE + "ok.js")["sha256"] == hashlib.sha256(b"ok").hexdigest()


def test_timestamp_is_integer_milliseconds():
    net = {BASE: (b"<html></html>", "text/html", "", BASE)}
    with patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)):
        m = warp.build_manifest(BASE)
    assert isinstance(m["timestamp"], int)
    # A ms-since-epoch value is ~1.7e12 in 2026; a seconds value would be ~1.7e9.
    assert m["timestamp"] > 1_000_000_000_000
    assert m["nonce"] is None  # no nonce passed → null


def test_html_unfetchable_is_inconclusive_not_a_fabricated_manifest():
    def fx(url, **k):
        raise fetcher.FetchError("upstream status 503")

    with patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
        with pytest.raises(warp.WarpInconclusive):
            warp.build_manifest(BASE)


def test_blocked_target_propagates_as_blocked_not_inconclusive():
    def fx(url, **k):
        raise fetcher.BlockedError("blocked non-public address")

    with patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
        with pytest.raises(fetcher.BlockedError):
            warp.build_manifest(BASE)


def test_unique_host_cap_refuses_cross_origin_and_records_fetch_failed():
    """With a host cap of 1, a cross-origin asset is refused (never fetched) and recorded
    fetch_failed — the SSRF fan-out is bounded and the gap is honest, not silent."""
    html = b"<html><script src='https://cdn.example.com/x.js'></script></html>"
    contacted = []

    def fx(url, timeout=None, max_bytes=None, max_redirects=None):
        contacted.append(url)
        if url == BASE:
            return html, "text/html", "", BASE
        return b"x", "application/javascript", "", url

    with patch.object(config, "FETCH_MAX_UNIQUE_HOSTS", 1), patch("syxx_server.fetcher.fetch_ex", side_effect=fx):
        m = warp.build_manifest(BASE)
    assert "https://cdn.example.com/x.js" not in contacted
    cross = _asset(m, "https://cdn.example.com/x.js")
    assert cross["fetch_status"] == "fetch_failed"


def test_css_import_depth_is_bounded():
    """A self-referential @import chain terminates at WARP_CSS_IMPORT_MAX_DEPTH rather than
    looping forever; the visited-set also dedups a cycle."""
    html = b"<html><link rel=stylesheet href='/0.css'></html>"
    net = {BASE: (html, "text/html", "", BASE)}
    # 0.css → 1.css → 2.css → ... each imports the next.
    for i in range(10):
        net[BASE + f"{i}.css"] = (f'@import "/{i + 1}.css";'.encode(), "text/css", "", BASE + f"{i}.css")
    with (
        patch.object(config, "WARP_CSS_IMPORT_MAX_DEPTH", 3),
        patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)),
    ):
        m = warp.build_manifest(BASE)
    css_urls = [a["url"] for a in m["assets"]]
    # depth 0 = 0.css, then imports at depth 1,2,3 → 1.css,2.css,3.css. 4.css is not followed.
    assert BASE + "3.css" in css_urls
    assert BASE + "4.css" not in css_urls


def test_asset_count_cap_bounds_enumeration():
    scripts = "".join(f"<script src='/s{i}.js'></script>" for i in range(10))
    html = ("<html>" + scripts + "</html>").encode()
    net = {BASE: (html, "text/html", "", BASE)}
    for i in range(10):
        net[BASE + f"s{i}.js"] = (b"x", "application/javascript", "", BASE + f"s{i}.js")
    with (
        patch.object(config, "WARP_MANIFEST_MAX_ASSETS", 4),
        patch("syxx_server.fetcher.fetch_ex", side_effect=_net(net)),
    ):
        m = warp.build_manifest(BASE)
    assert len(m["assets"]) <= 4
