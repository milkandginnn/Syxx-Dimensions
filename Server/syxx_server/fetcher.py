"""SSRF-safe outbound fetch gateway (feature: SSRF-safe fetch).

Hardens the Warp proxy against the original server's unrestricted urlopen, which
accepted file://, internal IPs, and cloud-metadata endpoints. Controls:
  * scheme allowlist (http/https only) -> kills file://, ftp://, data://
  * DNS resolve + block private/loopback/link-local/reserved/CGNAT/multicast
  * manual redirect following, re-validating every hop (no redirect-to-internal)
  * response size cap (streamed, not read-all) and hard timeout

Residual: a determined attacker could still race DNS between our check and the
socket connect (rebinding). We pin the connection to the vetted IP to close that
window for the common case.
"""

import http.client
import ipaddress
import socket
import ssl
import urllib.parse

from . import config, logging_util, netutil
from .netutil import PinnedHTTPSConnection as _PinnedHTTPSConnection

if config.FETCH_ALLOW_PRIVATE:
    logging_util.warn(
        "fetcher_ssrf_disabled",
        detail="SYXX_FETCH_ALLOW_PRIVATE=True: private-IP block is disabled. "
        "This is a security risk intended for testing only.",
    )


class FetchError(Exception):
    pass


class BlockedError(FetchError):
    pass


class TruncatedError(FetchError):
    pass


# A033: destination-port allowlist. Without this, /warp, /fetch_script, and /api/diff
# fetch an arbitrary public IP:port, turning the server into a blind port scanner /
# cross-protocol probe. Only the standard web ports are permitted.
_ALLOWED_PORTS = frozenset({80, 443})


def _ip_is_public(ip):
    """SSRF gate for outbound fetches. Delegates to the shared netutil gate (which
    keeps the WS8-FIX IPv4-mapped IPv6 normalization: ::ffff:127.0.0.1 is loopback,
    not public). FETCH_ALLOW_PRIVATE remains the testing-only escape hatch; an
    unparseable address is still rejected even with it set."""
    if config.FETCH_ALLOW_PRIVATE:
        try:
            ipaddress.ip_address(ip)
        except ValueError:
            return False
        return True
    return netutil.ip_is_public(ip)


def _resolve_public(host):
    """Return a vetted IP for host, or raise BlockedError if any resolved
    address is non-public (defensive: reject if ANY answer is private)."""
    try:
        infos = socket.getaddrinfo(host, None, proto=socket.IPPROTO_TCP)
    except socket.gaierror as e:
        raise FetchError(f"dns resolution failed: {e}") from e
    ips = {i[4][0] for i in infos}
    if not ips:
        raise FetchError("no addresses for host")
    for ip in ips:
        if not _ip_is_public(ip):
            raise BlockedError(f"blocked non-public address {ip} for {host}")
    # Prefer IPv4 for the pinned connection.
    for ip in ips:
        if ipaddress.ip_address(ip).version == 4:
            return ip
    return next(iter(ips))


import time as _time  # noqa: E402 — import placed after runtime setup above it, deliberately


def _strip_invisible(s):
    """E005: Strip zero-width and bidi control characters from hostnames.
    
    These invisible characters (U+200B-U+200F, U+202A-U+202E, U+FEFF) can be
    embedded in hostnames to create visually identical but distinct domains
    that bypass SSRF validation or consume separate budget slots."""
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


def _one_request(url, timeout, max_bytes, deadline=None):
    parts = urllib.parse.urlsplit(url)
    if parts.scheme not in ("http", "https"):
        raise BlockedError(f"scheme not allowed: {parts.scheme!r}")
    host = parts.hostname
    if not host:
        raise BlockedError("missing host")
    # E005: strip zero-width and bidi control characters from hostname
    host = _strip_invisible(host)
    port = parts.port or (443 if parts.scheme == "https" else 80)
    # A033: reject non-web ports. This runs on every hop (fetch() re-enters here per
    # redirect), so an http->https:PORT redirect cannot bypass a first-request-only check.
    if port not in _ALLOWED_PORTS:
        raise BlockedError(f"port not allowed: {port}")
    ip = _resolve_public(host)

    # A164: derive per-hop timeout from the remaining deadline budget so the
    # redirect loop does not reset the clock on each hop.
    if deadline is not None:
        remaining = deadline - _time.monotonic()
        if remaining <= 0:
            raise FetchError("aggregate fetch deadline exceeded")
        hop_timeout = min(timeout, remaining)
    else:
        hop_timeout = timeout

    path = parts.path or "/"
    if parts.query:
        path += "?" + parts.query

    # Pin the socket to the vetted IP; keep the original Host header for vhosts
    # and TLS SNI so certificate validation still matches the real hostname.
    if parts.scheme == "https":
        # A067: dial the vetted IP without any second DNS lookup (no rebind window).
        ctx = ssl.create_default_context()
        conn = _PinnedHTTPSConnection(ip, port, host, hop_timeout, ctx)
    else:
        conn = http.client.HTTPConnection(ip, port, timeout=hop_timeout)

    # Host header from the parsed hostname (+ non-default port) — NOT parts.netloc,
    # which carries any user:pass@ userinfo (credential leak to the target) and is a
    # CRLF-injection surface. hostname is already normalized and separator-free.
    default_port = 443 if parts.scheme == "https" else 80
    host_header = host if port == default_port else f"{host}:{port}"

    try:
        conn.putrequest("GET", path, skip_host=True, skip_accept_encoding=True)
        conn.putheader("Host", host_header)
        conn.putheader("User-Agent", "SyxxWarpProxy/2.0")
        conn.putheader("Accept", "*/*")
        conn.putheader("Connection", "close")
        conn.endheaders()
        resp = conn.getresponse()
        status = resp.status
        location = resp.getheader("Location")
        ctype = resp.getheader("Content-Type", "")
        # Warp raw-concatenation needs the transfer envelope so callers can un-gzip/deflate
        # and recover the resource's ACTUAL bytes (spec §2 envelope-decode-only). Surfaced
        # additively; fetch()'s public 3-tuple is unchanged.
        cenc = resp.getheader("Content-Encoding", "") or ""
        # A164: read body in bounded chunks with deadline checking to defeat
        # slow-drip thread pinning (one byte every N seconds holding the thread
        # indefinitely). A single resp.read(n) blocks until n bytes arrive,
        # resetting the per-recv clock on each chunk.
        _CHUNK = 65536
        chunks = []
        total = 0
        while True:
            if deadline is not None and _time.monotonic() > deadline:
                raise FetchError("aggregate fetch deadline exceeded during read")
            chunk = resp.read(_CHUNK)
            if not chunk:
                break
            total += len(chunk)
            if total > max_bytes:
                raise TruncatedError("baseline truncated; diff is inconclusive")
            chunks.append(chunk)
        body = b"".join(chunks)
        return status, location, ctype, body, cenc
    finally:
        conn.close()


def _fetch_loop(url, timeout, max_bytes, max_redirects):
    """Shared SSRF-safe redirect loop. Returns (body, content_type, content_encoding, final_url).

    A164: computes an aggregate deadline from the per-request timeout so the
    redirect loop and body reads share a single wall-clock budget. This
    prevents a slow-drip target from pinning a handler thread indefinitely by
    emitting one byte every N seconds.
    """
    # A164: aggregate deadline = timeout × (max_redirects + 1) so the entire
    # fetch including all hops is bounded.
    deadline = _time.monotonic() + timeout * (max_redirects + 1)

    current = url
    for _ in range(max_redirects + 1):
        status, location, ctype, body, cenc = _one_request(current, timeout, max_bytes, deadline=deadline)
        if status in (301, 302, 303, 307, 308) and location:
            current = urllib.parse.urljoin(current, location)  # re-validated next loop
            continue
        if status >= 400:
            raise FetchError(f"upstream status {status}")
        return body, ctype, cenc, current
    raise FetchError("too many redirects")


def fetch(url, timeout=None, max_bytes=None, max_redirects=None):
    """Fetch a URL safely, following (and re-validating) redirects.

    Returns (content_bytes, content_type, final_url). Raises FetchError/BlockedError.
    """
    timeout = config.FETCH_TIMEOUT if timeout is None else timeout
    max_bytes = config.FETCH_MAX_BYTES if max_bytes is None else max_bytes
    max_redirects = config.FETCH_MAX_REDIRECTS if max_redirects is None else max_redirects
    body, ctype, _cenc, final = _fetch_loop(url, timeout, max_bytes, max_redirects)
    return body, ctype, final


def fetch_ex(url, timeout=None, max_bytes=None, max_redirects=None):
    """Additive variant of fetch() that also surfaces the transfer Content-Encoding.

    Returns (content_bytes, content_type, content_encoding, final_url), where content_bytes
    are the RAW transfer bytes (still gzip/br/deflate-encoded if the origin sent them —
    envelope decoding is the caller's responsibility per WARP_ASSEMBLY_V1 §2). Same SSRF
    machinery and caps as fetch(); no second code path. Raises FetchError/BlockedError.
    """
    timeout = config.FETCH_TIMEOUT if timeout is None else timeout
    max_bytes = config.FETCH_MAX_BYTES if max_bytes is None else max_bytes
    max_redirects = config.FETCH_MAX_REDIRECTS if max_redirects is None else max_redirects
    return _fetch_loop(url, timeout, max_bytes, max_redirects)
