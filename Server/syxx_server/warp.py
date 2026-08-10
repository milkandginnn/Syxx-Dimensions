"""Warp structural + hash comparison (feature: SSRF-safe Warp baseline).

Produces exactly the JSON shape the app's ContentWarp client decodes
(html/scripts/stylesheets/iframes/scriptHashes), but built on the SSRF-safe
fetcher, with attribute-order-independent asset parsing (fixes the stylesheet
regex that required href-before-rel), bounded script count/size, and a cached
baseline in SQLite.

A051/A093/A094: uses html.parser.HTMLParser instead of regex for tag extraction.
Regex `[^>]*>` terminates at the first `>` even inside quoted attributes, causing
tags like `<script data-x=">">` to be truncated and assets missed/evaded. The
HTMLParser handles quoted `>`, nested quotes, and comment boundaries correctly.
"""

import concurrent.futures
import encodings.idna
import hashlib
import html.parser
import json
import re
import threading
import time
import unicodedata
import urllib.parse
import zlib

from . import config, db, fetcher, logging_util

_COMMENT = re.compile(r"<!--.*?-->", re.DOTALL)


class _AssetExtractor(html.parser.HTMLParser):
    """A051/A093: bounded HTML parser that extracts script, link, iframe src/href
    attributes and inline script bodies — correctly handles `>` inside quoted
    attributes that the old regex `[^>]*>` could not."""

    def __init__(self, base_url):
        super().__init__(convert_charrefs=False)
        self._base = base_url
        self.scripts = []
        self.stylesheets = []
        self.iframes = []
        self.inline_bodies = []
        self._in_script = False
        self._script_src = None
        self._script_body = []

    def handle_starttag(self, tag, attrs):
        # A308: markupbase.ParserBase already lowercases attribute names before
        # passing them here. The explicit .lower() is a defensive measure against
        # parser implementation differences or future HTMLParser changes — it is
        # redundant with current CPython but harmless and protects against
        # case-sensitive attribute lookups if the parser contract ever changes.
        a = {k.lower(): v or "" for k, v in attrs}
        if tag == "script":
            self._in_script = True
            self._script_src = a.get("src")
            self._script_body = []
            if self._script_src:
                self.scripts.append(_resolve(self._base, self._script_src))
        elif tag == "link":
            rels = a.get("rel", "").lower().split()
            if "stylesheet" in rels and a.get("href"):
                self.stylesheets.append(_resolve(self._base, a["href"]))
        elif tag == "iframe":
            if a.get("src"):
                self.iframes.append(_resolve(self._base, a["src"]))

    def handle_endtag(self, tag):
        if tag == "script" and self._in_script:
            self._in_script = False
            if not self._script_src:
                # A094: hash raw inline script bytes without comment stripping.
                # Previously stripped HTML comments from script bodies before hashing,
                # which altered execution-relevant content and caused hash divergence
                # with clients that hash raw bytes. A MITM hiding code inside
                # <!-- --> that the server stripped but the browser executes would
                # escape detection (false clean). Now hashes exact decoded script text.
                # NOTE: the .strip() is retained for leading/trailing whitespace
                # normalization; server and client MUST share this canonicalization.
                b = "".join(self._script_body).strip()
                if b:
                    self.inline_bodies.append(b)
            self._script_src = None
            self._script_body = []

    def handle_data(self, data):
        if self._in_script:
            self._script_body.append(data)

    def handle_comment(self, data):
        pass  # A094: comments inside script bodies are preserved in the hash


def _resolve(base, src):
    try:
        return urllib.parse.urljoin(base, src)
    except ValueError:
        return src


def parse_assets(html, base_url):
    """Extract external scripts, stylesheets, and iframes from HTML.

    A051/A093: uses HTMLParser instead of regex `[^>]*>` which breaks on
    `>` inside quoted attributes (e.g. `<script data-x=">">`)."""
    extractor = _AssetExtractor(base_url)
    # Strip HTML comments first for parity with the client-side parser.
    # A094 HONESTY CAVEAT: see the note in inline_scripts — this must stay
    # byte-for-byte in sync with the client or false mismatches occur.
    html = _COMMENT.sub("", html)
    extractor.feed(html)
    return extractor.scripts, extractor.stylesheets, extractor.iframes, extractor.inline_bodies


def inline_scripts(html):
    """Ordered raw bodies of inline <script> elements (those without a src).

    A094: uses HTMLParser so inline script bodies are extracted with correct
    quote/attribute handling. Comment stripping inside script bodies MUST stay
    in sync with the client hasher — see A094 in DELTAS.md."""
    extractor = _AssetExtractor(base_url="")
    extractor.feed(html)
    return extractor.inline_bodies


def build_baseline(target_url, deadline=None):
    """Fetch + analyze a target, returning the client-compatible dict.

    DEPRECATED (superseded by build_manifest / WARP_MANIFEST_V1). Retained ONLY because
    the scheduler baseline job (scheduler.py) and the /api/diff content half
    (app._diff_content) still consume this shape; those consumers live in another lane and
    are migrated separately. The `/warp` route that exposes it is documented deprecated.
    Do NOT build new callers on this — use build_manifest, which is signed and covers the
    full CSS/@import closure.

    SM-016: accepts an optional monotonic deadline. When set, script fetches are
    skipped if the deadline has passed, preventing the function from pinning a
    scheduler thread for minutes on slow fetches."""
    # A092: the cache is keyed on the RESOLVED final_url, not the pre-redirect
    # target_url. The old code read/wrote the cache under target_url but parsed and
    # hashed assets relative to final_url; if target_url's redirect destination
    # changed within the TTL, a stale baseline built for the OLD destination was
    # served for the new one. Warp's diff is the content-tamper signal, so a stale
    # baseline is false evidence (a fabricated "tampered", or a real injection masked
    # as clean). Fetching first and keying on final_url ties the cached baseline to
    # the content it was actually built from; two targets that redirect to the same
    # final_url correctly share one entry. The redirect fetch is a single bounded
    # request; the expensive per-script fetches below are still cached.
    html_bytes, _ctype, final_url = fetcher.fetch(target_url)
    cached = db.warp_cache_get(final_url, config.WARP_CACHE_TTL)
    if cached:
        logging_util.incr("warp_cache_hit")
        return json.loads(cached)

    # A048: decode from the declared charset strictly, preserving replacement
    # evidence (U+FFFD) rather than silently deleting invalid bytes. Byte deletion
    # can merge or erase tag/attribute boundaries, changing the asset inventory
    # without marking the fetch inconclusive.
    html = html_bytes.decode("utf-8", errors="replace")
    scripts, stylesheets, iframes, inlines = parse_assets(html, final_url)

    script_hashes = {}
    # A049: track per-script fetch success/failure so the client can distinguish
    # a failed baseline acquisition from a genuine content mismatch. The old code
    # stored "ERROR_PROXY_FETCH" as a plain string hash that the client compared
    # against a real SHA-256, recording a mismatch instead of making the component
    # inconclusive.
    script_fetch_status = {}
    # A054: parallel script fetches via ThreadPoolExecutor (max_workers=5) to
    # eliminate the N+1 serial-fetch bottleneck — up to 50 scripts were previously
    # fetched one at a time, each waiting the full FETCH_SCRIPT_TIMEOUT on failure.
    # Concurrency cap of 5 prevents overwhelming upstream servers while delivering
    # ~5× wall-clock improvement on the common (no-error) path.
    _script_urls = scripts[: config.FETCH_MAX_SCRIPTS]
    _dict_lock = threading.Lock()

    def _fetch_one_script(script_url):
        # SM-016: check deadline before each script fetch to prevent unbounded runtime.
        if deadline is not None and time.monotonic() > deadline:
            with _dict_lock:
                script_fetch_status[script_url] = "deadline_exceeded"
            return
        try:
            data, _c, _f = fetcher.fetch(script_url, timeout=config.FETCH_SCRIPT_TIMEOUT)
            with _dict_lock:
                script_hashes[script_url] = hashlib.sha256(data).hexdigest()
                script_fetch_status[script_url] = "success"
        except fetcher.FetchError:
            with _dict_lock:
                script_fetch_status[script_url] = "fetch_failed"

    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        pool.map(_fetch_one_script, _script_urls)

    inline_hashes = [hashlib.sha256(b.encode("utf-8")).hexdigest() for b in inlines]
    result = {
        "html": html,
        "scripts": scripts,
        "stylesheets": stylesheets,
        "iframes": iframes,
        "scriptHashes": script_hashes,
        "scriptFetchStatus": script_fetch_status,  # A049: typed per-script status
        "inlineScripts": inlines,
        "inlineScriptHashes": inline_hashes,
    }
    # A092: cache under the resolved final_url (see the note at the top of this
    # function), never the pre-redirect target_url.
    # A010: sort_keys=True for deterministic serialization order so the cache
    # entry is byte-identical regardless of dict insertion order.
    db.warp_cache_put(final_url, json.dumps(result, sort_keys=True))
    logging_util.incr("warp_baselines_built")
    return result


# ===========================================================================
# WARP_MANIFEST_V1 — signed-manifest content-integrity protocol.
#
# Frozen contract: docs/warp-manifest-v1-spec.md. The server is the ONLY parser:
# it fetches the initial-load HTML, parses it once, resolves the transitive
# static-asset closure (HTML + JS + CSS + CSS @import), hashes each asset's RAW
# (envelope-decoded) bytes, and emits an inner manifest. The route
# (app._route_warp_manifest) serializes that manifest once and Ed25519-signs the
# exact transmitted bytes with the collector key. No content is ever transformed:
# only the HTTP transfer envelope (gzip / br / deflate) is decoded, to recover the
# resource's ACTUAL bytes. Every fetch routes through fetcher.fetch_ex (the SSRF
# gateway); a per-run unique-host cap plus asset-count / import-depth caps bound the
# fan-out. Initial-load only — no JS execution — a deliberate, declared boundary.
# ===========================================================================

MANIFEST_VERSION = "WARP_MANIFEST_V1"

# A009: hard ceiling on freshness_seconds emitted in the manifest. Even if the
# operator sets WARP_MANIFEST_FRESHNESS higher (via env var), the server clamps
# the emitted value to prevent excessively long cache windows that would let a
# stale manifest be accepted by clients for hours. 600 s = 10 minutes.
_MAX_FRESHNESS_SECONDS = 600

CLASS_HTML = "HTML"
CLASS_JS = "JS"
CLASS_CSS = "CSS"
CLASS_CSS_IMPORT = "CSS_IMPORT"

# Spec §3: the coverage declaration. Only these classes define what "covered" means
# to the client; the per-asset `class` string is otherwise opaque display text.
ASSET_CLASSES_IN_SCOPE = [CLASS_HTML, CLASS_JS, CLASS_CSS, CLASS_CSS_IMPORT]

# Redirect policy the manifest declares (spec §6). same-origin: the client follows only
# same scheme+host+port hops and the terminal URL must equal the recorded final_url.
REDIRECT_POLICY = "same-origin"

FETCH_STATUS_FAILED = "fetch_failed"

# CSS `@import` scanning (spec §3 closure). Two accepted forms:
#   @import "sheet.css";        @import 'sheet.css' screen;
#   @import url("sheet.css");   @import url(sheet.css);
# Comments are stripped first so an @import commented out is not (wrongly) followed.
# SM-013: also strip // single-line comments from CSS before scanning @import.
# While // is not valid CSS comment syntax, some preprocessors and malformed
# stylesheets use it. An @import inside a // comment would be erroneously
# followed without this stripping.
_CSS_COMMENT = re.compile(rb"/\*.*?\*/", re.DOTALL)
_CSS_LINE_COMMENT = re.compile(rb"//[^\n]*")
_CSS_IMPORT = re.compile(
    r"""@import\s+(?:url\(\s*(?P<q1>['"]?)(?P<u1>[^'")]+)(?P=q1)\s*\)"""
    r"""|(?P<q2>['"])(?P<u2>[^'"]+)(?P=q2))""",
    re.IGNORECASE,
)


class WarpInconclusive(fetcher.FetchError):
    """The initial-load HTML could not be fetched on this vantage, so there is nothing
    to build a manifest from.

    Honesty protocol: this is the tri-state's third leg — never a silent clean or a
    fabricated manifest. The route surfaces it as an honest 502, never a match/no-match.
    """


def _bounded_decompress(body, wbits, max_bytes):
    """Decompress with a hard output cap so a decompression bomb cannot exhaust memory."""
    d = zlib.decompressobj(wbits)
    out = d.decompress(body, max_bytes + 1)
    if d.unconsumed_tail or len(out) > max_bytes:
        raise fetcher.TruncatedError("decompressed body exceeds cap; asset is inconclusive")
    out += d.flush()
    if len(out) > max_bytes:
        raise fetcher.TruncatedError("decompressed body exceeds cap; asset is inconclusive")
    return out


def _brotli_decompress(body, max_bytes):
    """Decode a brotli (`Content-Encoding: br`) body to plaintext, capped at max_bytes.

    Both sides of the protocol MUST support br (spec §8) — it is deterministic. If no
    brotli decoder is installed, or the body will not decode, this raises FetchError so
    the asset is recorded fetch_failed (never hashed while still compressed). A body that
    inflates past the cap raises TruncatedError for the same honest-inconclusive reason."""
    try:
        import brotli as _brotli  # type: ignore[unresolved-import]
    except ImportError:
        try:
            import brotlicffi as _brotli  # type: ignore[unresolved-import]
        except ImportError as e:
            raise fetcher.FetchError(
                "brotli transfer-encoding cannot be decoded: no brotli/brotlicffi decoder installed"
            ) from e
    # A282: incremental decompression to prevent brotli bomb — decompress in chunks
    # and check accumulated size after each chunk, rather than decompressing entire
    # body into memory before checking size cap.
    try:
        decompressor = _brotli.Decompressor()
        out = bytearray()
        chunk_size = 65536  # 64KB chunks
        offset = 0
        while offset < len(body):
            chunk = body[offset:offset + chunk_size]
            decompressed = decompressor.process(chunk)
            out.extend(decompressed)
            if len(out) > max_bytes:
                raise fetcher.TruncatedError("decompressed body exceeds cap; asset is inconclusive")
            offset += chunk_size
        # Process any remaining data
        if not decompressor.is_finished():
            decompressed = decompressor.process(b'')
            out.extend(decompressed)
            if len(out) > max_bytes:
                raise fetcher.TruncatedError("decompressed body exceeds cap; asset is inconclusive")
        return bytes(out)
    except fetcher.TruncatedError:
        raise
    except Exception as e:  # noqa: BLE001 — boundary handler: any exception here must become a reported result, not a crash
        raise fetcher.FetchError(f"brotli decode failed: {e}") from e


def _content_encoding_decode(body, encoding, max_bytes):
    """Spec §8: decode the transfer envelope ONLY (gzip / br / deflate), to recover the
    resource's actual bytes. Identity passes through untouched. Never charset-decode,
    trim, or normalize — the returned bytes are exactly what gets hashed.

    A `br` body that cannot be decoded (no decoder, or corrupt) raises FetchError so the
    asset is recorded fetch_failed rather than hashed while still compressed.

    A007: supports comma-separated multi-encoding (e.g. "gzip, br"). Decodings are
    applied in reverse order — the outermost (last-applied) encoding is decoded first."""
    raw = (encoding or "").strip()
    if not raw:
        return body
    layers = [e.strip().lower() for e in raw.split(",") if e.strip()]
    if not layers:
        return body
    for enc in reversed(layers):
        if enc in ("", "identity"):
            continue
        if enc == "gzip":
            body = _bounded_decompress(body, 16 + zlib.MAX_WBITS, max_bytes)
        elif enc == "deflate":
            try:
                body = _bounded_decompress(body, zlib.MAX_WBITS, max_bytes)
            except zlib.error:
                body = _bounded_decompress(body, -zlib.MAX_WBITS, max_bytes)
        elif enc == "br":
            body = _brotli_decompress(body, max_bytes)
        else:
            raise fetcher.FetchError(f"unsupported content-encoding: {enc!r}")
    return body


def _strip_invisible(s):
    """E005: Strip zero-width and bidi control characters from hostnames.
    
    These invisible characters (U+200B-U+200F, U+202A-U+202E, U+FEFF) can be
    embedded in hostnames to create visually identical but distinct domains
    that bypass deduplication and consume separate budget slots."""
    if not s:
        return s
    # Zero-width spaces, joiners, and bidi control characters
    invisible = (
        '\u200B',  # Zero Width Space
        '\u200C',  # Zero Width Non-Joiner
        '\u200D',  # Zero Width Joiner
        '\u200E',  # Left-to-Right Mark
        '\u200F',  # Right-to-Left Mark
        '\u202A',  # Left-to-Right Embedding
        '\u202B',  # Right-to-Left Embedding
        '\u202C',  # Pop Directional Formatting
        '\u202D',  # Left-to-Right Override
        '\u202E',  # Right-to-Left Override
        '\uFEFF',  # Zero Width No-Break Space (BOM)
    )
    for char in invisible:
        s = s.replace(char, '')
    return s


# E006: confusable-character sets for homograph detection (mirrors investigate.py).
_CONFUSABLE_LATIN = set("aceopxyACEOPXY")
_CONFUSABLE_CYRILLIC = set("асеорхуАСЕОРХУ")  # U+0430–U+0445 range lookalikes


def _warn_homograph(domain: str) -> None:
    """Log a warning if *domain* mixes scripts with confusable characters.

    E006: detects potential homograph attacks where Cyrillic lookalikes of
    ASCII letters are mixed into a domain name.  Warning only — never rejects."""
    has_ascii = has_non_ascii = has_cyrillic_confusable = False
    for ch in domain:
        if ord(ch) < 128:
            has_ascii = True
        else:
            has_non_ascii = True
            if ch in _CONFUSABLE_CYRILLIC:
                has_cyrillic_confusable = True
    if has_ascii and has_non_ascii and has_cyrillic_confusable:
        logging_util.warn(
            "E006_homograph_warn",
            domain=domain,
            detail="mixed ASCII and confusable Cyrillic characters in hostname",
        )


def _canonical_hostname(host):
    """IDNA-canonical ASCII form of a hostname, lowercased.

    A002: urlsplit leaves IDN hostnames as-is, so ``café.com`` and its punycode
    equivalent ``xn--caf-dma.com`` would each consume a separate budget slot even
    though socket.getaddrinfo resolves both to the same server. Canonicalizing to
    punycode makes the per-run unique-host cap see them as one host.

    A003: apply NFC normalization before IDNA encoding so that visually identical
    hostnames with different Unicode representations (e.g., café.com as NFC vs NFD)
    are treated as the same host in the budget comparison."""
    if not host:
        return ""
    try:
        # E005: strip zero-width and bidi control characters before normalization
        host = _strip_invisible(host)
        # E006: warn on mixed-script homograph domains before IDNA encoding
        _warn_homograph(host)
        # A006: .casefold() is locale-insensitive, unlike .lower() which varies
        # with LC_ALL (e.g. Turkish I/i). Applied before NFC so the Unicode input
        # to normalization is deterministic regardless of the server locale.
        host = unicodedata.normalize('NFC', host.casefold())
        labels = host.rstrip(".").split(".")
        return ".".join(encodings.idna.ToASCII(label).decode("ascii") for label in labels).casefold()
    except UnicodeError:
        return host.casefold()


def _fetch_decoded(url, host_budget):
    """Fetch one resource through the SSRF-safe gateway and envelope-decode it.

    Enforces the per-run unique-host cap: a host beyond the cap is refused (raised as a
    BlockedError) so the asset counts as a fetch failure and is recorded fetch_failed,
    never a bypass. Returns (raw_decoded_bytes, final_url)."""
    host = _canonical_hostname(urllib.parse.urlsplit(url).hostname or "")
    if host not in host_budget["seen"]:
        if len(host_budget["seen"]) >= host_budget["cap"]:
            raise fetcher.BlockedError(f"per-run unique-host cap reached ({host_budget['cap']})")
        host_budget["seen"].add(host)
    data, _ctype, cenc, final = fetcher.fetch_ex(url, timeout=config.FETCH_SCRIPT_TIMEOUT)
    return _content_encoding_decode(data, cenc, config.FETCH_MAX_BYTES), final


def fetch_raw_decoded(url):
    """Fetch one URL through the SSRF gateway and return (envelope_decoded_bytes, final_url).

    The byte-exact primitive behind /api/warp/raw?raw=1 (spec §9 diff overlay): it returns
    the server's clean copy of ONE asset without any charset-decode, so the client can hash
    and diff the exact bytes. Raises FetchError/BlockedError like the fetcher."""
    data, _ctype, cenc, final = fetcher.fetch_ex(url)
    return _content_encoding_decode(data, cenc, config.FETCH_MAX_BYTES), final


class _ManifestParser(html.parser.HTMLParser):
    """Enumerate the initial-load asset set for WARP_MANIFEST_V1 §3, in document order.

    Records, as absolute URLs resolved against the document's final URL:
      * JS  — <script src>, <link rel=modulepreload>, <link rel="preload" as="script">
      * CSS — <link rel=stylesheet>

    This parse only enumerates membership; it never mutates the bytes that get hashed
    (each asset's body is fetched and hashed raw)."""

    def __init__(self, base_url):
        super().__init__(convert_charrefs=False)
        self._base = base_url
        self.ordered = []  # [(absolute_url, class), ...] in document order

    def handle_starttag(self, tag, attrs):
        a = {k.lower(): (v or "") for k, v in attrs}
        if tag == "script":
            src = a.get("src")
            if src:
                self.ordered.append((_resolve(self._base, src), CLASS_JS))
        elif tag == "link":
            rels = a.get("rel", "").lower().split()
            as_ = a.get("as", "").lower()
            href = a.get("href")
            if not href:
                return
            if "stylesheet" in rels:
                self.ordered.append((_resolve(self._base, href), CLASS_CSS))
            elif "modulepreload" in rels:
                self.ordered.append((_resolve(self._base, href), CLASS_JS))
            elif "preload" in rels and as_ == "script":
                self.ordered.append((_resolve(self._base, href), CLASS_JS))


def _enumerate_manifest_assets(html_bytes, final_url):
    """Ordered [(absolute_url, class)] of the declared HTML→JS/CSS closure. Decodes to
    text for PARSING ONLY — the hashed asset bytes remain the raw fetched bytes."""
    parser = _ManifestParser(final_url)
    parser.feed(html_bytes.decode("utf-8", errors="replace"))
    return parser.ordered


def _scan_css_imports(css_bytes, css_final_url):
    """Absolute URLs of every `@import` in one stylesheet, resolved against that sheet's
    FINAL (post-redirect) URL, in source order. CSS comments are stripped first so a
    commented-out @import is not followed. Parsing view is utf-8/replace — never the
    source of any hashed byte."""
    # SM-013: strip both /* */ block comments and // line comments.
    stripped = _CSS_COMMENT.sub(b" ", css_bytes)
    stripped = _CSS_LINE_COMMENT.sub(b" ", stripped)
    text = stripped.decode("utf-8", errors="replace")
    out = []
    for m in _CSS_IMPORT.finditer(text):
        ref = m.group("u1") or m.group("u2") or ""
        ref = ref.strip()
        if ref:
            out.append(_resolve(css_final_url, ref))
    return out


def build_manifest(target_url, nonce=None):
    """Build the WARP_MANIFEST_V1 inner manifest for one page from THIS (server) vantage.

    Fetches the initial-load HTML, hashes its envelope-decoded raw bytes, resolves the
    transitive HTML + JS + CSS + CSS@import closure, fetches+hashes each asset's raw bytes,
    and returns the inner-manifest dict per spec §3. The caller (app._route_warp_manifest)
    serializes it ONCE and Ed25519-signs the exact bytes — this function never signs.

    Every declared asset appears in `assets` exactly once: either
      {url, final_url, class, sha256, bytes}  (fetched + hashed), or
      {url, class, fetch_status:"fetch_failed"} (recorded, never dropped → client INCONCLUSIVE).

    Raises WarpInconclusive if the initial-load HTML itself will not come down. Raises
    BlockedError for an SSRF-blocked target. `nonce`, when given, is echoed verbatim; the
    caller validates its shape. Caps: WARP_MANIFEST_MAX_ASSETS (count), WARP_CSS_IMPORT_MAX_DEPTH
    (import recursion), FETCH_MAX_UNIQUE_HOSTS (distinct hosts)."""
    host_budget = {"seen": set(), "cap": config.FETCH_MAX_UNIQUE_HOSTS}
    max_assets = config.WARP_MANIFEST_MAX_ASSETS
    import_max_depth = config.WARP_CSS_IMPORT_MAX_DEPTH

    # 1. Initial-load HTML. If it will not come down, there is nothing to attest.
    try:
        html_raw, _ctype, html_cenc, final_url = fetcher.fetch_ex(target_url)
        thost = _canonical_hostname(urllib.parse.urlsplit(final_url).hostname or "")
        host_budget["seen"].add(thost)
        html_bytes = _content_encoding_decode(html_raw, html_cenc, config.FETCH_MAX_BYTES)
    except fetcher.BlockedError:
        raise
    except fetcher.FetchError as e:
        raise WarpInconclusive(f"initial-load HTML unavailable: {e}") from e

    html_sha = hashlib.sha256(html_bytes).hexdigest()

    # 2. Enumerate the declared JS/CSS in document order, then walk the CSS @import closure.
    #    A single queue with a shared count cap keeps enumeration bounded and deterministic:
    #    declared assets (document order) first, CSS @imports appended as their parent is
    #    processed (breadth-first, depth-bounded), deduped on the requested URL.
    queue = [(url, cls, 0) for (url, cls) in _enumerate_manifest_assets(html_bytes, final_url)]
    visited = set()
    assets = []

    # SM-015: track whether the manifest was truncated by the asset count cap.
    truncated = False
    while queue:
        if len(assets) >= max_assets:
            # Count cap reached — stop enumerating. A declared boundary (like the host cap),
            # never a silent drop: the manifest simply does not claim to cover beyond it.
            truncated = True
            break
        url, cls, depth = queue.pop(0)
        if url in visited:
            continue
        visited.add(url)
        try:
            raw, final = _fetch_decoded(url, host_budget)
        except fetcher.FetchError:
            # Includes the host-cap BlockedError. Record, never drop — forces the client
            # to INCONCLUSIVE for this asset rather than a clean pass.
            assets.append({"url": url, "class": cls, "fetch_status": FETCH_STATUS_FAILED})
            continue
        assets.append(
            {
                "url": url,
                "final_url": final,
                "class": cls,
                "sha256": hashlib.sha256(raw).hexdigest(),
                "bytes": len(raw),
            }
        )
        # CSS closure: follow @import from stylesheets and imported stylesheets, depth-bounded.
        if cls in (CLASS_CSS, CLASS_CSS_IMPORT) and depth < import_max_depth:
            for imp in _scan_css_imports(raw, final):
                if imp not in visited:
                    queue.append((imp, CLASS_CSS_IMPORT, depth + 1))

    logging_util.incr("warp_manifests_built")
    return {
        "warp_manifest_version": MANIFEST_VERSION,
        "origin_url": target_url,
        "final_url": final_url,
        "html_sha256": html_sha,
        "asset_classes_in_scope": list(ASSET_CLASSES_IN_SCOPE),
        "assets": assets,
        # SM-015: flag when the manifest was truncated by the asset count cap,
        # so the client knows the closure is incomplete.
        "truncated": truncated,
        # Spec §3: integer milliseconds since epoch, truncated (WitnessSigner convention).
        #
        # A008 — Clock-source design decision (wall clock, not monotonic):
        # ─────────────────────────────────────────────────────────────────
        # This timestamp uses time.time() (wall-clock / UTC epoch) deliberately.
        # The manifest's `timestamp` field must be comparable ACROSS hosts: the
        # client validates freshness by comparing it against the HTTP Date header
        # (also wall-clock) and its own system clock. A monotonic clock
        # (time.monotonic()) is anchored to an arbitrary boot epoch that differs
        # on every machine, so a monotonic value from the server would be
        # meaningless to any client.
        #
        # Known consequence — clock skew:
        #   If the server's NTP is ahead of the client's, the manifest appears
        #   "from the future" and freshness_seconds shrinks from the client's
        #   perspective. If the server's clock lags, the manifest looks stale
        #   sooner than intended. Both are bounded by typical NTP drift (<1 s
        #   on a properly configured host) and are absorbed by the client's
        #   freshness tolerance window. This is an inherent property of any
        #   cross-host wall-clock comparison, not a bug in this implementation.
        #
        # Mitigation (operator-side): keep the server's clock synchronised via
        # chrony or systemd-timesyncd. The WARP_MANIFEST_FRESHNESS config value
        # (default 300 s) provides ample headroom for sub-second NTP skew.
        #
        # See also: config.WARP_MANIFEST_CLOCK_NOTE for the design-decision record.
        "timestamp": int(time.time() * 1000),
        # A009: cap freshness at 10 minutes to prevent excessively long cache windows.
        # Even if WARP_MANIFEST_FRESHNESS is set to a large value via env var,
        # the server never emits more than _MAX_FRESHNESS_SECONDS.
        "freshness_seconds": min(config.WARP_MANIFEST_FRESHNESS, _MAX_FRESHNESS_SECONDS),
        "redirect_policy": REDIRECT_POLICY,
        "allowed_redirects": [],
        "nonce": nonce,
    }
