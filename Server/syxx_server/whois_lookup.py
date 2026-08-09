"""WHOIS / RDAP lookup.

RDAP (RFC 9083) is the modern structured replacement for WHOIS. It returns JSON
over HTTPS. For TLDs where RDAP is not yet available, falls back to raw WHOIS
(TCP port 43) with a simple text parser.

SSRF-safe: only queries known RDAP bootstrap servers and well-known WHOIS servers.
Never follows redirects to private IPs.
"""

import http.client
import json
import socket
import ssl
import threading
import time
import urllib.parse

from .fetcher import _PinnedHTTPSConnection, _resolve_public

# ---------- IANA RDAP Bootstrap Cache ----------
# The bootstrap maps TLDs to RDAP server base URLs. Fetched once at startup and
# refreshed every 24 hours. Never fetched per-request.

_BOOTSTRAP_URL = "https://data.iana.org/rdap/dns.json"
_BOOTSTRAP_TTL = 86400  # 24 hours
_bootstrap_cache: dict[str, float | dict[str, object] | None] = {"data": None, "fetched_at": 0.0}
_bootstrap_lock = threading.Lock()


def _fetch_bootstrap():
    """Fetch (or return cached) IANA RDAP bootstrap. Thread-safe."""
    with _bootstrap_lock:
        now = time.monotonic()
        if _bootstrap_cache["data"] and (now - float(_bootstrap_cache["fetched_at"])) < _BOOTSTRAP_TTL:  # type: ignore[invalid-argument-type]
            return _bootstrap_cache["data"]
    # Fetch outside lock to avoid holding it during network I/O
    try:
        parsed = urllib.parse.urlparse(_BOOTSTRAP_URL)
        ctx = ssl.create_default_context()
        conn = http.client.HTTPSConnection(parsed.hostname or "", timeout=10, context=ctx)  # type: ignore[invalid-argument-type]
        conn.request("GET", parsed.path)
        resp = conn.getresponse()
        if resp.status != 200:
            return _bootstrap_cache.get("data")
        body = json.loads(resp.read().decode("utf-8"))
        with _bootstrap_lock:
            _bootstrap_cache["data"] = body
            _bootstrap_cache["fetched_at"] = time.monotonic()
        return body
    except Exception:
        return _bootstrap_cache.get("data")


def _rdap_base_for_tld(tld):
    """Look up the RDAP base URL for a TLD from the IANA bootstrap."""
    bootstrap = _fetch_bootstrap()
    if not bootstrap:
        return None
    # Bootstrap format: {"services": [["tld1","tld2"], ["https://rdap.server/"]], ...}
    for entry in bootstrap.get("services", []):
        if len(entry) == 2 and tld.lower() in [t.lower() for t in entry[0]]:
            urls = entry[1]
            return urls[0] if urls else None
    return None


# ---------- TLD Extraction ----------

# SM-035: expanded two-part TLD set. The original list missed many common
# multi-part TLDs (co.in, co.kr, com.cn, etc.) causing incorrect TLD extraction.
_TWO_PART_TLDS = {
    "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "net.uk",
    "co.jp", "or.jp", "ne.jp", "ac.jp",
    "com.au", "net.au", "org.au", "edu.au", "gov.au",
    "co.nz", "net.nz", "org.nz", "govt.nz",
    "com.br", "net.br", "org.br", "gov.br",
    "co.za", "org.za", "web.za",
    "com.mx", "net.mx", "org.mx",
    "com.ar", "net.ar", "org.ar",
    "co.in", "net.in", "org.in", "gen.in", "firm.in", "ind.in",
    "co.kr", "or.kr", "ne.kr",
    "com.cn", "net.cn", "org.cn",
    "com.tw", "net.tw", "org.tw",
    "co.th", "or.th", "in.th",
    "com.sg", "net.sg", "org.sg",
    "com.hk", "net.hk", "org.hk",
    "co.il", "org.il", "net.il",
    "com.tr", "net.tr", "org.tr",
}


def _extract_tld(domain):
    """Extract the effective TLD from a domain name."""
    labels = domain.rstrip(".").lower().split(".")
    if len(labels) < 2:
        return labels[-1] if labels else ""
    two_part = ".".join(labels[-2:])
    if two_part in _TWO_PART_TLDS:
        return two_part
    return labels[-1]


# ---------- RDAP Lookup ----------

_MAX_REDIRECTS = 3


def _redirect_target_is_safe(url):
    """SD-67 / A161: Validate that a redirect target does not point to a private/reserved IP.

    Resolves the redirect target hostname and rejects any address that is not
    globally routable unicast. Uses the shared netutil.ip_is_public gate (not
    ipaddress.is_global) so NAT64, CGNAT, and IPv4-mapped IPv6 are excluded
    consistently with the rest of the codebase.

    Returns the resolved public IP string on success, or False on failure.
    The caller MUST use this IP for the connection (A161: pinning prevents
    DNS-rebinding between the check and the connect).
    """
    parsed = urllib.parse.urlparse(url)
    hostname = parsed.hostname
    if not hostname:
        return False
    # Reject non-HTTPS redirects (already done in the caller, but defense-in-depth).
    if parsed.scheme != "https":
        return False
    # A161: use the shared netutil gate via fetcher._resolve_public so the
    # resolved IP is pinned for the connection and the public-IP check uses
    # the same gate as all other outbound paths.
    try:
        ip = _resolve_public(hostname)
        return ip  # returns the vetted IP string
    except Exception:
        return False


def _rdap_query(domain, timeout=10):
    """Query RDAP for domain registration data. Returns parsed dict or None.

    A161: every hop (including the first) is validated and pinned. The resolved
    IP is used for the socket connection (no re-resolution / DNS-rebinding), and
    the hostname is used for SNI and certificate validation. Non-HTTPS base URLs
    are refused outright.
    """
    tld = _extract_tld(domain)
    base_url = _rdap_base_for_tld(tld)
    if not base_url:
        return None

    url = base_url.rstrip("/") + "/domain/" + domain
    # SM-036: maximum RDAP response body size.
    _MAX_RDAP_BYTES = 2 * 1024 * 1024  # 2 MiB — RDAP responses are typically < 100 KiB
    # SM-040: track visited URLs to detect redirect loops beyond _MAX_REDIRECTS.
    visited_urls = set()
    for _ in range(_MAX_REDIRECTS):
        try:
            parsed = urllib.parse.urlparse(url)
            # A161: refuse non-HTTPS RDAP URLs (bootstrap-supplied or redirect).
            if parsed.scheme != "https":
                return None
            hostname = parsed.hostname
            if not hostname:
                return None
            # A161: resolve + validate + pin for EVERY hop including the first.
            # _resolve_public rejects non-public IPs and returns the vetted IP.
            pin_ip = _resolve_public(hostname)
            port = parsed.port or 443
            ctx = ssl.create_default_context()
            conn = _PinnedHTTPSConnection(pin_ip, port, hostname, timeout, ctx)
            path = parsed.path or "/domain/" + domain
            if parsed.query:
                path += "?" + parsed.query
            conn.request("GET", path, headers={"Accept": "application/rdap+json"})
            resp = conn.getresponse()
            if resp.status in (301, 302, 303, 307, 308):
                location = resp.getheader("Location")
                if not location:
                    return None
                # SSRF guard: only follow redirects to https on known RDAP hosts
                redir = urllib.parse.urlparse(location)
                if redir.scheme != "https":
                    return None
                # SM-038: _redirect_target_is_safe resolves the hostname — use the
                # result. The next iteration MUST use the same resolved IP, not
                # re-resolve (TOCTOU: the DNS answer can change between the check
                # and the connect, allowing DNS-rebinding).
                safe_ip = _redirect_target_is_safe(location)
                if not safe_ip:
                    return None
                # SM-040: detect redirect loops.
                if location in visited_urls:
                    return None
                visited_urls.add(location)
                url = location
                continue
            if resp.status != 200:
                return None
            # SM-036: bounded read to prevent memory exhaustion from oversized RDAP responses.
            body = resp.read(_MAX_RDAP_BYTES + 1)
            if len(body) > _MAX_RDAP_BYTES:
                return None  # response too large
            data = json.loads(body.decode("utf-8"))
            return _parse_rdap(data)
        except Exception:
            return None
    return None


def _parse_rdap(data):
    """Extract registrar, dates, nameservers, status from RDAP JSON response."""
    result = {"source": "rdap"}

    # Events (dates)
    for event in data.get("events", []):
        action = event.get("eventAction", "")
        date = event.get("eventDate", "")
        if not date:
            continue
        # Truncate to YYYY-MM-DD
        date = date[:10]
        if action == "registration":
            result["created"] = date
        elif action == "expiration":
            result["expires"] = date
        elif action == "last changed":
            result["updated"] = date

    # Status
    result["status"] = data.get("status", [])

    # Nameservers
    ns_list = []
    for ns in data.get("nameservers", []):
        name = ns.get("ldhName", "")
        if name:
            ns_list.append(name)
    result["nameservers"] = ns_list

    # Registrar (from entities)
    for entity in data.get("entities", []):
        roles = entity.get("roles", [])
        if "registrar" in roles:
            # Look for the fn (formatted name) vcard
            vcard = entity.get("vcardArray", [None, []])
            if len(vcard) == 2:
                for item in vcard[1]:
                    if len(item) >= 4 and item[0] == "fn":
                        result["registrar"] = item[3]
                        break
            # Also check the publicIds for registrar IANA ID
            for pid in entity.get("publicIds", []):
                if pid.get("type") == "IANA Registrar ID":
                    result["registrarId"] = pid.get("identifier")
            break

    # Raw RDAP JSON for debugging
    result["raw"] = json.dumps(data, indent=2)[:4000]

    return result


# ---------- WHOIS Fallback ----------

# Well-known WHOIS servers by TLD
_WHOIS_SERVERS = {
    "com": "whois.verisign-grs.com",
    "net": "whois.verisign-grs.com",
    "org": "whois.pir.org",
    "info": "whois.afilias.net",
    "io": "whois.nic.io",
    "dev": "whois.nic.google",
    "app": "whois.nic.google",
    "xyz": "whois.nic.xyz",
    "uk": "whois.nic.uk",
    "de": "whois.denic.de",
    "au": "whois.auda.org.au",
    "ca": "whois.cira.ca",
}


def _whois_fallback(domain, timeout=10):
    """Raw TCP/43 WHOIS query as fallback. Returns raw text string or None.

    A170: bounded read — caps accumulated response at 64 KiB and enforces a
    monotonic deadline across the read loop. WHOIS runs unauthenticated over
    cleartext TCP/43, so a hostile or MITM'd server could otherwise exhaust
    memory or pin the thread indefinitely by dripping data.
    """
    _MAX_WHOIS_BYTES = 65536  # 64 KiB — WHOIS responses are typically < 4 KiB
    tld = _extract_tld(domain)
    server = _WHOIS_SERVERS.get(tld, "whois.iana.org")
    try:
        deadline = time.monotonic() + timeout
        sock = socket.create_connection((server, 43), timeout=timeout)
        sock.sendall((domain + "\r\n").encode("utf-8"))
        chunks = []
        total = 0
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                sock.close()
                return None  # deadline exceeded — abort rather than hang
            sock.settimeout(min(remaining, timeout))
            data = sock.recv(4096)
            if not data:
                break
            total += len(data)
            if total > _MAX_WHOIS_BYTES:
                sock.close()
                return None  # response too large — abort rather than exhaust memory
            chunks.append(data)
        sock.close()
        raw = b"".join(chunks).decode("utf-8", "replace")
        return _parse_whois_text(raw, domain)
    except Exception:
        return None


def _parse_whois_text(raw, domain):
    """Parse raw WHOIS text into structured fields.

    A170: detect standard negative responses ("No match", "NOT FOUND", etc.)
    and return an explicit not-found result instead of an empty-fields dict
    that is indistinguishable from a domain that genuinely has no nameservers.
    """
    # A170: detect negative/not-found responses before attempting field parsing.
    # WHOIS servers return various forms of "not found" for unregistered domains;
    # without this check, a non-existent domain produces a populated-looking dict
    # with empty nameservers/status, collapsing the RDAP-vs-WHOIS-vs-nothing
    # tri-state.
    _NEGATIVE_PATTERNS = (
        "no match for",
        "not found",
        "no data found",
        "domain not found",
        "no entries found",
        "status: free",
        "is available",
        "no information available",
    )
    # SM-037: normalize line endings — WHOIS responses use \r\n from TCP/43,
    # but the raw text may have mixed endings after decode.
    raw = raw.replace("\r\n", "\n").replace("\r", "\n")
    # SM-039: check patterns at line start, not as substrings. A domain name
    # containing "no match for" would false-positive on a substring check.
    raw_lower = raw.lower()
    for pattern in _NEGATIVE_PATTERNS:
        for line in raw_lower.split("\n"):
            if line.strip().startswith(pattern):
                return {"source": "whois", "result": "not-found", "raw": raw[:4000]}

    result = {"source": "whois", "raw": raw[:4000]}

    field_map = {
        "registrar": ["Registrar:", "Registrar Name:", "registrar:"],
        "created": ["Creation Date:", "Created:", "Registration Date:", "created:"],
        "expires": ["Registry Expiry Date:", "Expiration Date:", "Expiry Date:", "expires:"],
        "updated": ["Updated Date:", "Last Modified:", "updated:"],
    }

    for field, patterns in field_map.items():
        for line in raw.split("\n"):
            line_stripped = line.strip()
            for pattern in patterns:
                if line_stripped.startswith(pattern):
                    value = line_stripped[len(pattern) :].strip()
                    if value and field not in result:
                        result[field] = value[:10] if field != "registrar" else value
                    break

    # Nameservers
    ns_list = []
    for line in raw.split("\n"):
        line_stripped = line.strip().lower()
        if line_stripped.startswith("name server:"):
            ns = line_stripped.split(":", 1)[1].strip()
            if ns and ns not in ns_list:
                ns_list.append(ns)
    result["nameservers"] = ns_list

    # Status
    statuses = []
    for line in raw.split("\n"):
        line_stripped = line.strip()
        if line_stripped.startswith("Domain Status:"):
            status = line_stripped.split(":", 1)[1].strip()
            # Remove the URL part if present (e.g., "clientTransferProhibited https://...")
            status = status.split(" ")[0]
            if status and status not in statuses:
                statuses.append(status)
    result["status"] = statuses

    return result


# ---------- Public API ----------


def lookup(domain, timeout=10):
    """Look up WHOIS/RDAP data for a domain.

    1. Try RDAP first (structured JSON from IANA bootstrap)
    2. Fall back to raw WHOIS on port 43
    3. Return structured dict with whatever data was found

    Returns dict with at minimum: source, domain.
    Raises ValueError on invalid domain.
    """
    domain = domain.rstrip(".").lower()
    if not domain or "." not in domain:
        raise ValueError(f"invalid domain: {domain}")
    # A170 / SD-99: reject CR/LF in domain before it reaches the WHOIS command.
    # WHOIS sends `domain\r\n` over TCP/43; a domain containing CR or LF allows
    # injection of arbitrary WHOIS commands to the server.
    if "\r" in domain or "\n" in domain:
        raise ValueError("invalid domain: contains CR/LF characters")

    # Try RDAP first
    result = _rdap_query(domain, timeout=timeout)
    if result:
        result["domain"] = domain
        return result

    # Fall back to WHOIS
    result = _whois_fallback(domain, timeout=timeout)
    if result:
        result["domain"] = domain
        # A170: a not-found result is a valid, conclusive answer — return it
        # rather than falling through to the generic "no data available".
        return result

    return {"domain": domain, "source": "none", "error": "no data available"}
