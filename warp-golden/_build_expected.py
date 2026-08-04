#!/usr/bin/env python3
"""Deterministically (re)build the WARP_ASSEMBLY_V1 golden-vector corpus.

This script is the reproducible source for every ``inputs/`` body, every
``expected.blob``, and every ``expected.sha256`` under ``warp-golden/``. It has
no third-party dependencies (Python stdlib only) so any harness's CI can run it.

Contract implemented here is frozen by ``docs/warp-assembly-v1-spec.md``:

  * version line   : b"WARP_ASSEMBLY_V1\\n"
  * per-file seam  : b"\\n===== WARP <CLASS> <ABSOLUTE_URL> =====\\n"
  * template seam  : b"\\n===== WARP TEMPLATE <n> =====\\n"   (fallback only)
  * blob           : version_line + for each file: seam + raw_bytes
  * order          : HTML, then JS (document order), then CSS (document order)
  * hash           : SHA-256(blob), lowercase hex

Bytes are never normalized: no charset decoding, no trimming, no CRLF/BOM
rewriting. Delivery transfer-encoding (gzip/br/deflate) is decoded before
concatenation, so the same underlying bytes produce the same blob regardless of
how they were delivered.

Run:  python3 warp-golden/_build_expected.py
"""

from __future__ import annotations

import gzip
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent

VERSION_LINE = b"WARP_ASSEMBLY_V1\n"


def seam(cls: str, url: str) -> bytes:
    """The fixed per-file seam header, emitted as UTF-8 bytes."""
    return f"\n===== WARP {cls} {url} =====\n".encode("utf-8")


def template_seam(n: int) -> bytes:
    """The fallback static-subtree seam header, emitted as UTF-8 bytes."""
    return f"\n===== WARP TEMPLATE {n} =====\n".encode("utf-8")


def build_blob(files: list[tuple[str, str, bytes]]) -> bytes:
    """files = ordered list of (class, absolute_url, raw_bytes)."""
    blob = bytearray(VERSION_LINE)
    for cls, url, raw in files:
        blob += seam(cls, url)
        blob += raw
    return bytes(blob)


def build_fallback_blob(
    subtrees: list[bytes], js: list[tuple[str, bytes]]
) -> bytes:
    """DEGRADED-mode blob: template/slot subtrees (document order) then JS.

    subtrees = ordered inner-content byte ranges of <template>/<slot>.
    js       = ordered list of (absolute_url, raw_bytes). CSS is omitted.
    """
    blob = bytearray(VERSION_LINE)
    for n, sub in enumerate(subtrees):
        blob += template_seam(n)
        blob += sub
    for url, raw in js:
        blob += seam("JS", url)
        blob += raw
    return bytes(blob)


def write_case(
    name: str,
    inputs: dict[str, bytes],
    blob: bytes,
    *,
    extra: dict[str, bytes] | None = None,
) -> str:
    case_dir = ROOT / name
    in_dir = case_dir / "inputs"
    in_dir.mkdir(parents=True, exist_ok=True)
    for fname, body in inputs.items():
        (in_dir / fname).write_bytes(body)
    if extra:
        for fname, body in extra.items():
            (in_dir / fname).write_bytes(body)
    (case_dir / "expected.blob").write_bytes(blob)
    digest = hashlib.sha256(blob).hexdigest()
    (case_dir / "expected.sha256").write_text(digest + "\n")
    return digest


# --------------------------------------------------------------------------
# Case bodies. LF line endings unless a case deliberately exercises CRLF/BOM.
# --------------------------------------------------------------------------

BASE = "https://example.test/"


def case_001() -> str:
    html = (
        b"<!doctype html>\n<html>\n<head><title>Single Script</title></head>\n"
        b"<body>\n<h1>Hello</h1>\n<script src=\"app.js\"></script>\n"
        b"</body>\n</html>\n"
    )
    app_js = b'console.log("app.js loaded");\n'
    blob = build_blob(
        [
            ("HTML", BASE, html),
            ("JS", BASE + "app.js", app_js),
        ]
    )
    return write_case(
        "001_single_script",
        {"index.html": html, "app.js": app_js},
        blob,
    )


def case_002() -> str:
    html = (
        b"<!doctype html>\n<html>\n<head>\n"
        b'<script src="a.js"></script>\n'
        b'<link rel="stylesheet" href="a.css">\n'
        b'<script src="b.js"></script>\n'
        b'<link rel="stylesheet" href="b.css">\n'
        b"</head>\n<body><p>Interleaved</p></body>\n</html>\n"
    )
    a_js = b'console.log("a.js");\n'
    b_js = b'console.log("b.js");\n'
    a_css = b"body{color:#0a0;}\n"
    b_css = b"p{margin:0;}\n"
    # JS in document order (a, b), then CSS in document order (a, b).
    blob = build_blob(
        [
            ("HTML", BASE, html),
            ("JS", BASE + "a.js", a_js),
            ("JS", BASE + "b.js", b_js),
            ("CSS", BASE + "a.css", a_css),
            ("CSS", BASE + "b.css", b_css),
        ]
    )
    return write_case(
        "002_interleaved",
        {
            "index.html": html,
            "a.js": a_js,
            "b.js": b_js,
            "a.css": a_css,
            "b.css": b_css,
        },
        blob,
    )


def case_003() -> str:
    html = (
        b"<!doctype html>\n<html>\n<head>\n"
        b'<link rel="modulepreload" href="mod.js">\n'
        b'<script src="main.js"></script>\n'
        b'<link rel="preload" as="script" href="pre.js">\n'
        b"</head>\n<body><p>Modulepreload</p></body>\n</html>\n"
    )
    mod_js = b'export const mod = "mod.js";\n'
    main_js = b'console.log("main.js");\n'
    pre_js = b'console.log("pre.js");\n'
    # modulepreload + preload-as-script are JS; document order: mod, main, pre.
    blob = build_blob(
        [
            ("HTML", BASE, html),
            ("JS", BASE + "mod.js", mod_js),
            ("JS", BASE + "main.js", main_js),
            ("JS", BASE + "pre.js", pre_js),
        ]
    )
    return write_case(
        "003_modulepreload",
        {
            "index.html": html,
            "mod.js": mod_js,
            "main.js": main_js,
            "pre.js": pre_js,
        },
        blob,
    )


def case_004() -> str:
    html = (
        b"<!doctype html>\n<html>\n<head>\n"
        b'<script src="app.js"></script>\n'
        b'<script src="https://cdn.example.com/x.js"></script>\n'
        b"</head>\n<body><p>Cross-origin</p></body>\n</html>\n"
    )
    app_js = b'console.log("local app.js");\n'
    x_js = b'console.log("cross-origin x.js");\n'
    # Cross-origin asset IS included; seam carries its absolute URL verbatim.
    blob = build_blob(
        [
            ("HTML", BASE, html),
            ("JS", BASE + "app.js", app_js),
            ("JS", "https://cdn.example.com/x.js", x_js),
        ]
    )
    return write_case(
        "004_cross_origin",
        # cross-origin body stored under a filesystem-safe name; the seam URL
        # is the real absolute URL, documented in the README manifest.
        {"index.html": html, "app.js": app_js, "cdn.example.com__x.js": x_js},
        blob,
    )


def case_005() -> str:
    html = (
        b"<!doctype html>\n<html>\n<head><title>Gzip vs Identity</title></head>\n"
        b'<body>\n<script src="app.js"></script>\n</body>\n</html>\n'
    )
    app_js = b'console.log("delivered gzipped OR identity - same bytes");\n'
    # The blob is built from DECODED bytes. Delivery encoding is irrelevant:
    # gzip(app_js) and identity app_js decode to the same bytes -> same blob.
    blob = build_blob(
        [
            ("HTML", BASE, html),
            ("JS", BASE + "app.js", app_js),
        ]
    )
    # Illustrative artifact: a gzip envelope of app.js (mtime pinned to 0 for
    # reproducibility). It is NOT read by loaders; it proves the decode rule.
    app_js_gz = gzip.compress(app_js, mtime=0)
    return write_case(
        "005_gzip_identity",
        {"index.html": html, "app.js": app_js},
        blob,
        extra={"app.js.gz": app_js_gz},
    )


def case_006() -> str:
    template_inner = b'<li class="item">ROW</li>'
    slot_inner = b"DEFAULT SLOT CONTENT"
    html = (
        b"<!doctype html>\n<html>\n<head>\n"
        b'<link rel="stylesheet" href="style.css">\n'
        b'<script src="app.js"></script>\n'
        b"</head>\n<body>\n"
        b'<template id="row">' + template_inner + b"</template>\n"
        b'<slot name="fallback">' + slot_inner + b"</slot>\n"
        b"</body>\n</html>\n"
    )
    # Sanity: extracted subtrees must be exact substrings of the delivered HTML.
    assert template_inner in html
    assert slot_inner in html
    app_js = b'console.log("app.js in degraded mode");\n'
    style_css = b"body{font:16px system-ui;}\n"
    # DEGRADED: template(0) + slot(1) subtrees, then JS. CSS omitted.
    blob = build_fallback_blob(
        subtrees=[template_inner, slot_inner],
        js=[(BASE + "app.js", app_js)],
    )
    return write_case(
        "006_template_slot_fallback",
        {"index.html": html, "app.js": app_js, "style.css": style_css},
        blob,
    )


def case_007() -> str:
    bom = b"\xef\xbb\xbf"  # UTF-8 BOM
    html = (
        bom
        + b"<!doctype html>\r\n<html>\r\n<head>"
        b'<script src="app.js"></script></head>\r\n'
        b"<body><p>CRLF and BOM</p></body>\r\n</html>\r\n"
    )
    # Asset body also carries a leading BOM and CRLF line endings; every byte
    # must survive into the blob unchanged (proof we never normalize).
    app_js = bom + b'console.log("crlf");\r\nconsole.log("bom");\r\n'
    blob = build_blob(
        [
            ("HTML", BASE, html),
            ("JS", BASE + "app.js", app_js),
        ]
    )
    return write_case(
        "007_crlf_bom_passthrough",
        {"index.html": html, "app.js": app_js},
        blob,
    )


def main() -> None:
    results = {
        "001_single_script": case_001(),
        "002_interleaved": case_002(),
        "003_modulepreload": case_003(),
        "004_cross_origin": case_004(),
        "005_gzip_identity": case_005(),
        "006_template_slot_fallback": case_006(),
        "007_crlf_bom_passthrough": case_007(),
    }
    width = max(len(k) for k in results)
    for name, digest in results.items():
        print(f"{name.ljust(width)}  {digest}")


if __name__ == "__main__":
    main()
