# Implementation Plan: 10 Free/Lite Web Server Features

**Created:** 2026-07-26
**Scope:** Server/syxx_server/ (Python HTTP server) + Server/index.html (frontend)
**Status:** Plan (not yet implemented, pending User approval)

---

## Executive Summary

Ten free-tier DNS investigation features for the Syxx web interface. All features
leverage the existing `resolver.py` DNS client infrastructure. The critical first step
is extending `_format_rdata()` to handle all common record types, after which most
features are independent and can proceed in parallel.

**All frontend "free" command outputs are currently FAKE** (the `rr()` function at
index.html:591 generates deterministic fake records from a hash; the `fakeIP()` function
at index.html:590 fabricates IPs). Connecting real backends is the primary value of
this plan and resolves the AGENTS.md 5 violation (no mocked production output).

---

## Dependency Graph

```
Phase 1: _format_rdata extension (SHARED FOUNDATION)
  |
  +---> Phase 2a: DNS Lookup (Feature 1)
  |       |
  |       +---> Phase 2b: CAA Record Inspector (Feature 8) -- free with Feature 1
  |       +---> Phase 2c: Reverse DNS / PTR (Feature 9)    -- free with Feature 1
  |       +---> Phase 2d: DNS Benchmark (Feature 10)       -- independent, small
  |
  +---> Phase 3a: WHOIS Lookup (Feature 4)        -- independent module
  +---> Phase 3b: SPF/DMARC/DKIM Checker (Feature 5) -- needs Phase 1 TXT formatting
  +---> Phase 3c: Subdomain Enumeration (Feature 6)    -- independent module
  +---> Phase 3d: DNS Propagation Checker (Feature 7)  -- needs config expansion
  |
  +---> Phase 4: DNSSEC Chain Validator (Feature 3) -- largest, needs cryptography lib
  |
  +---> Phase 5: Frontend Integration (all features wired to index.html)

Feature 2 (DNS Leak Test): ALREADY DONE. No work required.
```

---

## Phase 1: Shared Foundation -- _format_rdata Extension

### Problem

`resolver.py:161-175` -- `_format_rdata()` only handles 4 record types:

- A (type 1): `socket.inet_ntop(AF_INET, rdata)`
- AAAA (type 28): `socket.inet_ntop(AF_INET6, rdata)`
- NS (type 2) / CNAME (type 5): `_read_name(full, rdata_offset)`

All other types return `rdata.hex()`, producing unreadable hex for MX, TXT, SOA,
CAA, PTR, SRV, and every other record type.

### What to Change

**File:** `Server/syxx_server/resolver.py`
**Location:** Replace lines 161-175 (the entire `_format_rdata` function body)

### Record Type Parsers to Add

Each parser is a conditional branch inside `_format_rdata`. Wire format references
are from the relevant RFCs.

| Type | Number | Wire Format | Return Value |
|------|--------|-------------|-------------|
| MX | 15 | preference(2) + exchange(name) | `"10 mail.example.com"` |
| TXT | 16 | one or more `<len><string>` chunks | `"v=spf1 include:_spf.google.com ~all"` (joined) |
| SOA | 6 | mname(name) + rname(name) + serial(4) + refresh(4) + retry(4) + expire(4) + minimum(4) | `"ns1.example.com. hostmaster.example.com. 2026072101 7200 3600 1209600 3600"` |
| CAA | 257 | flags(1) + tag_len(1) + tag(var) + value(var) | `"0 issue \"letsencrypt.org\""` |
| PTR | 12 | ptrdname(name) | `"mail.example.com"` |
| SRV | 33 | priority(2) + weight(2) + port(2) + target(name) | `"10 60 5060 sip.example.com"` |

### Exact Implementation

```python
def _format_rdata(rtype, rdata, full, rdata_offset):
    # A record (type 1)
    if rtype == TYPE_A and len(rdata) == 4:
        return socket.inet_ntop(socket.AF_INET, rdata)
    # AAAA record (type 28)
    if rtype == TYPE_AAAA and len(rdata) == 16:
        return socket.inet_ntop(socket.AF_INET6, rdata)
    # NS / CNAME (types 2, 5) -- name-compressed
    if rtype in (TYPE_NS, 5):
        try:
            name, _ = _read_name(full, rdata_offset)
            return name
        except Exception:
            return rdata.hex()
    # PTR (type 12) -- same wire format as NS/CNAME (a single compressed name)
    if rtype == 12:
        try:
            name, _ = _read_name(full, rdata_offset)
            return name
        except Exception:
            return rdata.hex()
    # MX (type 15) -- preference(2) + exchange(name)
    if rtype == 15 and len(rdata) >= 3:
        pref = struct.unpack(">H", rdata[:2])[0]
        try:
            exchange, _ = _read_name(full, rdata_offset + 2)
            return "%d %s" % (pref, exchange)
        except Exception:
            return rdata.hex()
    # TXT (type 16) -- one or more <length><string> chunks
    if rtype == 16:
        parts = []
        off = 0
        while off < len(rdata):
            slen = rdata[off]
            off += 1
            if off + slen > len(rdata):
                break
            parts.append(rdata[off:off + slen].decode("utf-8", "replace"))
            off += slen
        return " ".join('"' + p + '"' for p in parts) if parts else rdata.hex()
    # SOA (type 6) -- mname(name) + rname(name) + serial(4) + refresh(4) + retry(4) + expire(4) + minimum(4)
    if rtype == 6:
        try:
            mname, off = _read_name(full, rdata_offset)
            rname, off = _read_name(full, off)
            if off + 20 <= len(full):
                serial, refresh, retry, expire, minimum = struct.unpack(">IIIII", full[off:off + 20])
                return "%s. %s. %d %d %d %d %d" % (mname, rname, serial, refresh, retry, expire, minimum)
        except Exception:
            pass
        return rdata.hex()
    # CAA (type 257) -- flags(1) + tag_len(1) + tag(tag_len) + value(rest)
    if rtype == 257 and len(rdata) >= 3:
        flags = rdata[0]
        tag_len = rdata[1]
        if 2 + tag_len <= len(rdata):
            tag = rdata[2:2 + tag_len].decode("ascii", "replace")
            value = rdata[2 + tag_len:].decode("utf-8", "replace")
            return '%d %s "%s"' % (flags, tag, value)
        return rdata.hex()
    # SRV (type 33) -- priority(2) + weight(2) + port(2) + target(name)
    if rtype == 33 and len(rdata) >= 7:
        priority, weight, port = struct.unpack(">HHH", rdata[:6])
        try:
            target, _ = _read_name(full, rdata_offset + 6)
            return "%d %d %d %s" % (priority, weight, port, target)
        except Exception:
            return rdata.hex()
    # Fallback: hex for unrecognized types
    return rdata.hex()
```

### Also Update _TYPE_NAMES

**File:** `Server/syxx_server/resolver.py`
**Location:** Line 38

Add PTR (12), MX (15), TXT (16), SRV (33) to the type name mapping:

```python
_TYPE_NAMES = {1: "A", 2: "NS", 5: "CNAME", 6: "SOA", 12: "PTR", 15: "MX",
               16: "TXT", 28: "AAAA", 33: "SRV", 46: "RRSIG", 48: "DNSKEY", 257: "CAA"}
```

### Testing

1. Unit test: construct known wire-format bytes for each type, verify `_format_rdata` output
2. Integration test: `resolver.query("google.com", 15, "1.1.1.1")` (MX query) should return
   records with human-readable preference + exchange, not hex
3. Failure case: truncated rdata for each type should fall back to hex, not crash

### Dependencies

None. This is the foundation.

---

## Phase 2: Quick Wins (S Effort)

### Feature 1: DNS Lookup

**Effort:** Small
**Dependencies:** Phase 1 (_format_rdata)
**New file:** None (route handler only)

#### API Route

**File:** `Server/syxx_server/app.py`
**Route:** `GET /api/lookup`
**Auth:** Free-tier (session required, no Pro entitlement enforced). Same pattern as
`_route_broadcast()` -- require `_rate_ok(None)` but do NOT call `_require()`.
Alternatively, accept an optional entitlement for audit logging but do not gate on it.

**Decision: Free-tier means rate-limited per IP, no entitlement required.** This matches
the public utility nature of DNS lookups and drives traffic/awareness. If the operator
prefers gating behind activation, change `_rate_ok(None)` to `_rate_ok(ent)` with a
lenient `_entitlement(query)` check.

**Add route in `_dispatch_get()` at ~line 594** (insert before `return self._json_error(404, "not found")`):

```python
if path == "/api/lookup":
    return self._route_lookup(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `domain` | Yes | string | Domain name to look up |
| `type` | No | string | Record type: A, AAAA, MX, TXT, NS, CNAME, SOA, CAA, PTR, SRV. Default: A |
| `server` | No | string | Resolver IP to query. Default: first PUBLIC_RESOLVER |

**Response Schema:**

```json
{
  "domain": "example.com",
  "type": "A",
  "server": "1.1.1.1",
  "rcode": 0,
  "rcodeName": "NOERROR",
  "ad": true,
  "records": [
    {"type": "A", "value": "93.184.216.34"}
  ],
  "truncated": false
}
```

**Route Handler:**

```python
def _route_lookup(self, query):
    """Free-tier DNS lookup: query a resolver for any record type. Rate-limited per IP,
    no entitlement required. Uses the existing resolver.query() infrastructure."""
    if not self._rate_ok(None):
        return
    domain = (query.get("domain", [""])[0]).strip()
    if not domain or len(domain) > 253:
        return self._json_error(400, "missing or invalid domain")
    # Validate domain format (reuse investigate._valid_domain pattern)
    if not investigate._valid_domain(domain):
        return self._json_error(400, "invalid domain format")

    type_str = (query.get("type", ["A"])[0]).strip().upper()
    type_map = {"A": 1, "AAAA": 28, "NS": 2, "CNAME": 5, "SOA": 6, "MX": 15,
                "TXT": 16, "CAA": 257, "PTR": 12, "SRV": 33}
    qtype = type_map.get(type_str)
    if qtype is None:
        return self._json_error(400, "unsupported record type")

    server = (query.get("server", [""])[0]).strip()
    if not server:
        server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
    # SSRF guard: only allow querying known public resolvers or PUBLIC_RESOLVERS
    if server not in config.PUBLIC_RESOLVERS:
        try:
            addr = ipaddress.ip_address(server)
            if not resolver._ip_is_public(str(addr)):
                return self._json_error(400, "resolver must be a public IP")
        except ValueError:
            return self._json_error(400, "invalid resolver address")

    rcode_names = {0: "NOERROR", 1: "FORMERR", 2: "SERVFAIL", 3: "NXDOMAIN",
                   4: "NOTIMP", 5: "REFUSED"}
    try:
        result = resolver.query(domain, qtype, server, timeout=config.INVESTIGATE_TIMEOUT)
    except resolver.DNSError as e:
        return self._json_error(502, "DNS query failed: %s" % e)

    logging_util.incr("lookups")
    self._send(200, {
        "domain": domain,
        "type": type_str,
        "server": server,
        "rcode": result.get("rcode", -1),
        "rcodeName": rcode_names.get(result.get("rcode", -1), "UNKNOWN"),
        "ad": result.get("ad", False),
        "records": result.get("records", []),
        "truncated": result.get("truncated", False),
    })
```

#### Testing

- Happy path: `GET /api/lookup?domain=google.com&type=A` returns real A records
- Failure: `GET /api/lookup?domain=nonexistent-xyz123.test&type=A` returns NXDOMAIN (rcode 3)
- Failure: `GET /api/lookup?domain=google.com&server=127.0.0.1` returns 400 (private IP)
- Failure: rate limit exceeded returns 429

---

### Feature 8: CAA Record Inspector

**Effort:** Small (free with Feature 1)
**Dependencies:** Phase 1 + Feature 1

This feature is **already fully implemented by Phase 1 + Feature 1**. Once
`_format_rdata` handles CAA (type 257) and the `/api/lookup` route accepts
`type=CAA`, the CAA inspector works automatically.

**No new code required.** The frontend just needs a button that calls:

```
GET /api/lookup?domain=example.com&type=CAA
```

#### Response example

```json
{
  "domain": "example.com",
  "type": "CAA",
  "records": [
    {"type": "CAA", "value": "0 issue \"letsencrypt.org\""},
    {"type": "CAA", "value": "0 issuewild \";\""}
  ]
}
```

#### Testing

- `GET /api/lookup?domain=letsencrypt.org&type=CAA` should return CAA records with
  human-readable flags/tag/value, not hex
- Domain with no CAA records should return empty records array with rcode 0

---

### Feature 9: Reverse DNS (PTR)

**Effort:** Small (free with Feature 1 + small helper)
**Dependencies:** Phase 1 + Feature 1

#### Additional Code Needed

**File:** `Server/syxx_server/resolver.py`
**Location:** Add a new helper function after `_format_rdata` (~line 195)

```python
def ip_to_arpa(ip_str):
    """Convert an IP address to its reverse-DNS arpa name.
    IPv4: 192.0.2.1 -> 1.2.0.192.in-addr.arpa
    IPv6: 2001:db8::1 -> 1.0.0.0...8.b.d.0.1.0.0.2.ip6.arpa
    Raises DNSError for invalid addresses."""
    try:
        addr = ipaddress.ip_address(ip_str)
    except ValueError:
        raise DNSError("invalid IP address: %s" % ip_str)
    if addr.version == 4:
        octets = ip_str.split(".")
        return ".".join(reversed(octets)) + ".in-addr.arpa"
    else:
        # Expand to full 32-char hex, reverse nibbles, dot-separate
        hex_full = addr.exploded.replace(":", "")
        return ".".join(reversed(hex_full)) + ".ip6.arpa"
```

#### API Route

The `/api/lookup` route from Feature 1 already handles PTR queries. However, a
convenience wrapper is useful so the user enters an IP instead of an arpa name:

**File:** `Server/syxx_server/app.py`
**Add route in `_dispatch_get()` alongside Feature 1:**

```python
if path == "/api/reverse-dns":
    return self._route_reverse_dns(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `ip` | Yes | string | IPv4 or IPv6 address to reverse-resolve |

**Response Schema:**

```json
{
  "ip": "8.8.8.8",
  "arpa": "8.8.8.8.in-addr.arpa",
  "ptr": "dns.google",
  "rcode": 0
}
```

**Route Handler:**

```python
def _route_reverse_dns(self, query):
    """Free-tier reverse DNS lookup. Converts IP to arpa name and queries PTR."""
    if not self._rate_ok(None):
        return
    ip_str = (query.get("ip", [""])[0]).strip()
    if not ip_str:
        return self._json_error(400, "missing ip parameter")
    try:
        arpa = resolver.ip_to_arpa(ip_str)
    except resolver.DNSError as e:
        return self._json_error(400, str(e))

    server = config.PUBLIC_RESOLVERS[0] if config.PUBLIC_RESOLVERS else "1.1.1.1"
    try:
        result = resolver.query(arpa, 12, server, timeout=config.INVESTIGATE_TIMEOUT)
    except resolver.DNSError as e:
        return self._json_error(502, "reverse DNS query failed: %s" % e)

    ptr_records = [r["value"] for r in result.get("records", []) if r["type"] == "PTR"]
    logging_util.incr("reverse_dns_lookups")
    self._send(200, {
        "ip": ip_str,
        "arpa": arpa,
        "ptr": ptr_records[0] if ptr_records else None,
        "allPtr": ptr_records,
        "rcode": result.get("rcode", -1),
    })
```

#### Testing

- `GET /api/reverse-dns?ip=8.8.8.8` should return `dns.google`
- `GET /api/reverse-dns?ip=127.0.0.1` should work (loopback PTR) or return NXDOMAIN
- Failure: `GET /api/reverse-dns?ip=not-an-ip` returns 400
- Failure: IPv6 address `2001:4860:4860::8888` should produce correct arpa name

---

### Feature 10: DNS Benchmark

**Effort:** Small
**Dependencies:** None (uses existing `resolver.query()` + `time.monotonic()`)

#### API Route

**File:** `Server/syxx_server/app.py`
**Add route in `_dispatch_get()`:**

```python
if path == "/api/benchmark":
    return self._route_benchmark(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `domain` | Yes | string | Domain to resolve for benchmarking |
| `type` | No | string | Record type. Default: A |
| `rounds` | No | int | Number of rounds per resolver. Default: 3, max: 10 |

**Response Schema:**

```json
{
  "domain": "example.com",
  "type": "A",
  "rounds": 3,
  "resolvers": [
    {
      "server": "1.1.1.1",
      "label": "Cloudflare",
      "results": [
        {"time_ms": 12.3, "rcode": 0, "recordCount": 2},
        {"time_ms": 11.8, "rcode": 0, "recordCount": 2},
        {"time_ms": 12.1, "rcode": 0, "recordCount": 2}
      ],
      "avg_ms": 12.07,
      "min_ms": 11.8,
      "max_ms": 12.3,
      "failures": 0
    }
  ],
  "fastest": "1.1.1.1",
  "slowest": "9.9.9.9"
}
```

**Route Handler (~80 lines):**

```python
def _route_benchmark(self, query):
    """Free-tier DNS benchmark: time resolution across public resolvers."""
    if not self._rate_ok(None):
        return
    domain = (query.get("domain", [""])[0]).strip()
    if not domain or not investigate._valid_domain(domain):
        return self._json_error(400, "missing or invalid domain")

    type_str = (query.get("type", ["A"])[0]).strip().upper()
    type_map = {"A": 1, "AAAA": 28, "NS": 2, "MX": 15, "TXT": 16, "CNAME": 5}
    qtype = type_map.get(type_str, 1)

    try:
        rounds = min(10, max(1, int(query.get("rounds", ["3"])[0])))
    except (ValueError, TypeError):
        rounds = 3

    # Resolver labels for display
    resolver_labels = {
        "1.1.1.1": "Cloudflare", "8.8.8.8": "Google", "9.9.9.9": "Quad9",
        "208.67.222.222": "OpenDNS", "64.6.64.6": "Verisign",
    }

    servers = config.PUBLIC_RESOLVERS[:6]  # cap at 6 resolvers
    results_by_server = {}

    for server in servers:
        entries = []
        for _ in range(rounds):
            t0 = time.monotonic()
            try:
                result = resolver.query(domain, qtype, server, timeout=5)
                elapsed = (time.monotonic() - t0) * 1000
                entries.append({
                    "time_ms": round(elapsed, 2),
                    "rcode": result.get("rcode", -1),
                    "recordCount": len(result.get("records", [])),
                })
            except resolver.DNSError as e:
                elapsed = (time.monotonic() - t0) * 1000
                entries.append({
                    "time_ms": round(elapsed, 2),
                    "rcode": -1,
                    "error": str(e),
                    "recordCount": 0,
                })
        successes = [e for e in entries if e["rcode"] >= 0]
        results_by_server[server] = {
            "server": server,
            "label": resolver_labels.get(server, server),
            "results": entries,
            "avg_ms": round(sum(e["time_ms"] for e in successes) / len(successes), 2) if successes else None,
            "min_ms": round(min(e["time_ms"] for e in successes), 2) if successes else None,
            "max_ms": round(max(e["time_ms"] for e in successes), 2) if successes else None,
            "failures": len(entries) - len(successes),
        }

    resolver_list = list(results_by_server.values())
    ranked = sorted([r for r in resolver_list if r["avg_ms"] is not None], key=lambda r: r["avg_ms"])
    logging_util.incr("benchmarks")
    self._send(200, {
        "domain": domain,
        "type": type_str,
        "rounds": rounds,
        "resolvers": resolver_list,
        "fastest": ranked[0]["server"] if ranked else None,
        "slowest": ranked[-1]["server"] if ranked else None,
    })
```

#### Testing

- `GET /api/benchmark?domain=google.com` should return timing data for all PUBLIC_RESOLVERS
- Verify `avg_ms` is mathematically correct from the individual round results
- Failure: domain that times out should show failures count > 0, avg_ms null
- Failure: rate limiting returns 429

---

## Phase 3: Medium Effort Features

### Feature 4: WHOIS Lookup

**Effort:** Medium
**Dependencies:** None (independent module)

#### New Module

**File:** `Server/syxx_server/whois_lookup.py` (~200 lines)

Uses RDAP (Registration Data Access Protocol, RFC 9083) as primary, with raw
WHOIS as fallback. RDAP returns structured JSON; WHOIS returns unstructured text.

**Dependencies:** Python stdlib only (`http.client`, `json`, `ssl`). The `cryptography`
library is NOT needed for this feature.

```python
"""WHOIS / RDAP lookup.

RDAP (RFC 9083) is the modern structured replacement for WHOIS. It returns JSON
over HTTPS. For TLDs where RDAP is not yet available, falls back to raw WHOIS
(TCP port 43) with a simple text parser.

SSRF-safe: only queries known RDAP bootstrap servers and well-known WHOIS servers.
Never follows redirects to private IPs.
"""
```

**Key functions:**

```python
def lookup(domain, timeout=5):
    """Look up WHOIS/RDAP data for a domain. Returns structured dict.
    
    1. Extract TLD from domain
    2. Query IANA RDAP bootstrap (https://data.iana.org/rdap/dns.json)
    3. Query the RDAP server for the domain
    4. If RDAP fails, fall back to raw WHOIS on port 43
    """

def _extract_tld(domain):
    """Extract the effective TLD (last label for most, last two for .co.uk etc.)."""

def _rdap_query(domain, timeout):
    """Query RDAP for domain registration data. Returns parsed dict or None."""

def _whois_fallback(domain, timeout):
    """Raw TCP/43 WHOIS query as fallback. Returns raw text string."""

def _parse_rdap(data):
    """Extract registrar, dates, nameservers, status from RDAP JSON."""
```

**Response Schema (from route):**

```json
{
  "domain": "example.com",
  "source": "rdap",
  "registrar": "MarkMonitor Inc.",
  "created": "1995-08-14",
  "updated": "2024-08-02",
  "expires": "2025-08-13",
  "status": ["clientDeleteProhibited", "clientTransferProhibited"],
  "nameservers": ["ns1.example.com", "ns2.example.com"],
  "raw": "... raw RDAP or WHOIS text ..."
}
```

**SSRF guards:**

- RDAP bootstrap URL is hardcoded to `data.iana.org` (public, well-known)
- RDAP server URLs come from the IANA bootstrap (trusted source)
- WHOIS fallback connects only to well-known WHOIS servers (IANA-registered)
- Never connect to user-supplied hostnames/IPs
- All HTTPS connections validate certificates
- Redirect following is bounded (max 3, same as fetcher.py)

#### API Route

**File:** `Server/syxx_server/app.py`
**Add import at line 37** (alongside other module imports):

```python
from . import (broadcast, config, db, fetcher, fulfillment, investigate, leak, licensing,
               listeners, logging_util, mailer, passivedns, ratelimit, resolver,
               stripe_checkout, warp, whois_lookup)
```

**Add route in `_dispatch_get()`:**

```python
if path == "/api/whois":
    return self._route_whois(query)
```

**Route Handler:**

```python
def _route_whois(self, query):
    """Free-tier WHOIS/RDAP lookup."""
    if not self._rate_ok(None):
        return
    domain = (query.get("domain", [""])[0]).strip()
    if not domain or not investigate._valid_domain(domain) or len(domain) > 253:
        return self._json_error(400, "invalid or missing domain")
    try:
        result = whois_lookup.lookup(domain)
    except Exception as e:
        logging_util.error("whois_failed", domain=domain, err=str(e))
        return self._json_error(502, "WHOIS lookup failed: %s" % e)
    logging_util.incr("whois_lookups")
    self._send(200, result)
```

#### Testing

- `GET /api/whois?domain=google.com` should return registrar, dates, nameservers
- Failure: `.test` TLD should return a clear error (no RDAP/WHOIS for test TLDs)
- Failure: network timeout should return 502, not hang
- Verify the response `source` field accurately reflects whether RDAP or WHOIS was used

---

### Feature 5: SPF/DMARC/DKIM Checker

**Effort:** Medium
**Dependencies:** Phase 1 (TXT record formatting from `_format_rdata`)

#### New Module

**File:** `Server/syxx_server/email_auth.py` (~200 lines)

```python
"""Email authentication record checker (SPF, DMARC, DKIM).

Queries DNS TXT records for SPF, DMARC, and (optionally) DKIM and parses
the results into structured verdicts.

SPF: TXT record at the domain
DMARC: TXT record at _dmarc.<domain>
DKIM: TXT record at <selector>._domainkey.<domain> (selector required)
"""
```

**Key functions:**

```python
def check_email_auth(domain, dkim_selector=None, server=None, timeout=3):
    """Check SPF, DMARC, and optionally DKIM for a domain.
    
    Returns: {
        "spf": {"present": bool, "record": str, "mechanisms": [...], "verdict": "pass|fail|none|neutral"},
        "dmarc": {"present": bool, "record": str, "policy": str, "verdict": "pass|fail|none"},
        "dkim": {"present": bool, "record": str, "selector": str, "verdict": "pass|fail|skip"}
    }
    """

def _parse_spf(txt_records):
    """Find and parse the SPF record from TXT records. Validates 'v=spf1' prefix."""

def _parse_dmarc(txt_records):
    """Parse DMARC record. Extracts policy (p=), rua, ruf, pct."""

def _parse_dkim(txt_records):
    """Parse DKIM record. Extracts key type (k=), public key (p=)."""

def _spf_verdict(mechanisms):
    """Evaluate SPF configuration quality.
    - 'all' at end: good
    - '+all': dangerous (allows everything)
    - missing 'all': incomplete
    - no SPF record: none
    """
```

**SPF verdict logic:**

- `pass`: SPF record present, ends with `-all` or `~all`
- `neutral`: SPF record present but ends with `?all` or has no `all` mechanism
- `fail`: SPF record has `+all` (permits any sender)
- `none`: No SPF record found

**DMARC verdict logic:**

- `pass`: DMARC record present with `p=reject` or `p=quarantine`
- `neutral`: DMARC record present with `p=none`
- `fail`: DMARC record with `p=none` and no `rua` reporting address
- `none`: No DMARC record found

#### API Route

**File:** `Server/syxx_server/app.py`
**Add import:** `from . import email_auth`
**Add route in `_dispatch_get()`:**

```python
if path == "/api/email-auth":
    return self._route_email_auth(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `domain` | Yes | string | Domain to check |
| `dkim_selector` | No | string | DKIM selector (e.g., "google", "default"). If omitted, DKIM is skipped |

**Response Schema:**

```json
{
  "domain": "example.com",
  "spf": {
    "present": true,
    "record": "v=spf1 include:_spf.google.com ~all",
    "mechanisms": ["include:_spf.google.com", "~all"],
    "verdict": "pass"
  },
  "dmarc": {
    "present": true,
    "record": "v=DMARC1; p=reject; rua=mailto:dmarc@example.com",
    "policy": "reject",
    "verdict": "pass"
  },
  "dkim": {
    "present": true,
    "selector": "google",
    "record": "v=DKIM1; k=rsa; p=MIGfMA...",
    "verdict": "pass"
  }
}
```

#### Testing

- `GET /api/email-auth?domain=google.com` should find SPF, DMARC, and DKIM records
- `GET /api/email-auth?domain=google.com&dkim_selector=google` should return DKIM data
- Failure: domain with no email records should return `present: false` for each, not crash
- Failure: `+all` SPF should produce `verdict: fail`, not `pass`

---

### Feature 6: Subdomain Enumeration

**Effort:** Medium
**Dependencies:** None (independent module, uses external APIs)

#### New Module

**File:** `Server/syxx_server/subdomain_enum.py` (~200 lines)

```python
"""Passive subdomain enumeration via CT logs and public DNS.

Uses Certificate Transparency (CT) logs via crt.sh as the primary source.
CT logs are append-only public records of every TLS certificate issued,
making them a reliable source of known subdomains.

This is PASSIVE enumeration only -- no brute-force, no zone transfers,
no aggressive scanning. Only queries public, well-known APIs.
"""
```

**Key functions:**

```python
def enumerate_subdomains(domain, timeout=10, max_results=100):
    """Enumerate subdomains via CT logs + passive DNS.
    
    Returns: {
        "domain": "example.com",
        "source": "ct-logs",
        "subdomains": ["www.example.com", "mail.example.com", ...],
        "count": 42,
        "truncated": false
    }
    """

def _query_crtsh(domain, timeout):
    """Query crt.sh CT log API for certificates matching the domain.
    URL: https://crt.sh/?q=%.domain&output=json
    Extracts unique common names and SAN entries.
    """

def _resolve_subdomains(subdomains, server, timeout):
    """Batch-resolve a list of subdomains to verify they exist.
    Returns dict of subdomain -> [IPs] or subdomain -> None (NXDOMAIN).
    Uses concurrent.futures with bounded parallelism.
    """
```

**SSRF guards:**

- crt.sh API is the only external endpoint queried
- No user-supplied URLs are fetched
- Resolution uses only PUBLIC_RESOLVERS
- `max_results` cap prevents excessive queries

#### API Route

**File:** `Server/syxx_server/app.py`
**Add import:** `from . import subdomain_enum`
**Add route in `_dispatch_get()`:**

```python
if path == "/api/subdomains":
    return self._route_subdomains(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `domain` | Yes | string | Root domain to enumerate subdomains for |
| `resolve` | No | bool | Whether to DNS-resolve each found subdomain. Default: true |
| `max` | No | int | Maximum results. Default: 100, max: 500 |

**Response Schema:**

```json
{
  "domain": "example.com",
  "source": "ct-logs",
  "subdomains": [
    {"name": "www.example.com", "ips": ["93.184.216.34"]},
    {"name": "mail.example.com", "ips": ["93.184.216.35"]},
    {"name": "staging.example.com", "ips": null}
  ],
  "count": 3,
  "truncated": false,
  "resolveTime_ms": 1200
}
```

#### Testing

- `GET /api/subdomains?domain=google.com` should return real subdomains from CT logs
- Failure: crt.sh timeout should return partial results with `truncated: true`
- Failure: domain with no certificates should return empty subdomains, not error
- Verify no user-supplied URLs are fetched (SSRF test)

---

### Feature 7: DNS Propagation Checker

**Effort:** Medium
**Dependencies:** Phase 1 (for readable record formatting); config expansion

#### Config Expansion

**File:** `Server/syxx_server/config.py`
**Location:** After `PUBLIC_RESOLVERS` (line ~280)

Add a new config list with geo metadata for propagation checking:

```python
# Propagation resolvers: extended list with geographic metadata for the propagation
# checker. Each entry has an IP, a label, and a region. Used by /api/propagation.
PROPAGATION_RESOLVERS = [
    {"ip": "1.1.1.1", "label": "Cloudflare", "region": "Global (Anycast)"},
    {"ip": "8.8.8.8", "label": "Google", "region": "Global (Anycast)"},
    {"ip": "9.9.9.9", "label": "Quad9", "region": "Global (Anycast)"},
    {"ip": "208.67.222.222", "label": "OpenDNS", "region": "Global (Anycast)"},
    {"ip": "64.6.64.6", "label": "Verisign", "region": "US"},
    {"ip": "156.154.70.1", "label": "Neustar", "region": "US"},
    {"ip": "156.154.71.1", "label": "Neustar", "region": "US"},
    {"ip": "199.85.126.10", "label": "Norton", "region": "Global (Anycast)"},
    {"ip": "185.228.168.9", "label": "CleanBrowsing", "region": "EU"},
    {"ip": "94.140.14.14", "label": "AdGuard", "region": "Global (Anycast)"},
    {"ip": "76.76.19.19", "label": "Alternate DNS", "region": "US"},
    {"ip": "101.6.6.6", "label": "DNS.SB", "region": "Asia"},
    {"ip": "119.29.29.29", "label": "DNSPod", "region": "China"},
    {"ip": "223.5.5.5", "label": "AliDNS", "region": "China"},
    {"ip": "168.126.63.1", "label": "KT (Korea Telecom)", "region": "South Korea"},
    {"ip": "203.248.252.2", "label": "LG U+", "region": "South Korea"},
    {"ip": "196.1.114.50", "label": "Afrihost", "region": "Africa"},
    {"ip": "200.221.11.100", "label": "UOL", "region": "Brazil"},
]

# DoH vantages for propagation (in addition to the base DOH_VANTAGES)
PROPAGATION_DOH = [
    {"label": "cloudflare-doh", "ip": "1.1.1.1", "sni": "cloudflare-dns.com",
     "path": "/dns-query", "region": "Global (Anycast)"},
    {"label": "google-doh", "ip": "8.8.8.8", "sni": "dns.google",
     "path": "/resolve", "region": "Global (Anycast)"},
    {"label": "quad9-doh", "ip": "9.9.9.9", "sni": "dns.quad9.net",
     "path": "/dns-query", "region": "Global (Anycast)"},
]
```

Note: These can also be overridden via `SYXX_PROPAGATION_RESOLVERS` env var using
the existing `_list` helper if a simpler format is preferred. The JSON-structured
approach above is clearer but requires a new `_json_list` helper or env parsing.

**Alternative (simpler):** Keep the resolver list as a Python list literal and add
the `_json_list` helper:

```python
def _json_list(name, default):
    v = os.environ.get(name)
    if not v:
        return list(default)
    try:
        parsed = json.loads(v)
        if isinstance(parsed, list):
            return parsed
    except (json.JSONDecodeError, TypeError):
        pass
    return list(default)
```

#### API Route

**File:** `Server/syxx_server/app.py`
**Add route in `_dispatch_get()`:**

```python
if path == "/api/propagation":
    return self._route_propagation(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `domain` | Yes | string | Domain to check propagation for |
| `type` | No | string | Record type. Default: A |

**Response Schema:**

```json
{
  "domain": "example.com",
  "type": "A",
  "resolvers": [
    {
      "server": "1.1.1.1",
      "label": "Cloudflare",
      "region": "Global (Anycast)",
      "status": "ok",
      "records": [{"type": "A", "value": "93.184.216.34"}],
      "time_ms": 15
    },
    {
      "server": "223.5.5.5",
      "label": "AliDNS",
      "region": "China",
      "status": "error",
      "error": "timeout",
      "time_ms": 5000
    }
  ],
  "consensus": "consistent",
  "uniqueValues": ["93.184.216.34"],
  "totalResolvers": 18,
  "successCount": 15,
  "propagationPct": 83.3
}
```

**Route Handler:**

```python
def _route_propagation(self, query):
    """Free-tier DNS propagation checker: query many global resolvers concurrently."""
    if not self._rate_ok(None):
        return
    domain = (query.get("domain", [""])[0]).strip()
    if not domain or not investigate._valid_domain(domain) or len(domain) > 253:
        return self._json_error(400, "invalid or missing domain")

    type_str = (query.get("type", ["A"])[0]).strip().upper()
    type_map = {"A": 1, "AAAA": 28, "NS": 2, "MX": 15, "TXT": 16, "CNAME": 5}
    qtype = type_map.get(type_str)
    if qtype is None:
        return self._json_error(400, "unsupported record type")

    resolvers = config.PROPAGATION_RESOLVERS
    # Concurrent fan-out with deadline
    deadline = time.monotonic() + 10  # 10 second total budget

    def query_one(entry):
        t0 = time.monotonic()
        try:
            result = resolver.query(domain, qtype, entry["ip"], timeout=4)
            elapsed = (time.monotonic() - t0) * 1000
            records = result.get("records", [])
            return {
                "server": entry["ip"], "label": entry["label"], "region": entry["region"],
                "status": "ok", "records": records, "time_ms": round(elapsed, 1),
            }
        except resolver.DNSError as e:
            elapsed = (time.monotonic() - t0) * 1000
            return {
                "server": entry["ip"], "label": entry["label"], "region": entry["region"],
                "status": "error", "error": str(e), "time_ms": round(elapsed, 1),
            }

    pool = concurrent.futures.ThreadPoolExecutor(max_workers=min(12, len(resolvers)))
    futures = [pool.submit(query_one, r) for r in resolvers]
    results = []
    try:
        for fut in concurrent.futures.as_completed(futures, timeout=12):
            results.append(fut.result())
    except concurrent.futures.TimeoutError:
        pass
    finally:
        pool.shutdown(wait=False, cancel_futures=True)

    # Compute consensus
    value_sets = []
    for r in results:
        if r["status"] == "ok":
            vals = frozenset(rec["value"] for rec in r.get("records", []))
            if vals:
                value_sets.append(vals)
    unique_values = sorted(set().union(*value_sets)) if value_sets else []
    success_count = sum(1 for r in results if r["status"] == "ok")

    if not value_sets:
        consensus = "inconclusive"
    elif all(s == value_sets[0] for s in value_sets):
        consensus = "consistent"
    else:
        consensus = "divergent"

    propagation_pct = round((success_count / len(resolvers)) * 100, 1) if resolvers else 0
    logging_util.incr("propagation_checks")
    self._send(200, {
        "domain": domain, "type": type_str,
        "resolvers": results,
        "consensus": consensus,
        "uniqueValues": unique_values,
        "totalResolvers": len(resolvers),
        "successCount": success_count,
        "propagationPct": propagation_pct,
    })
```

#### Testing

- `GET /api/propagation?domain=google.com` should return results from 15+ resolvers
- Verify `propagationPct` is computed correctly from success count
- Failure: domain that doesn't exist should show `consensus: consistent` (all get NXDOMAIN)
- Failure: network timeout on some resolvers should show those as `error` status, not hang

---

## Phase 4: Large Effort Feature

### Feature 3: DNSSEC Chain Validator

**Effort:** Large (~300-400 lines new module)
**Dependencies:** Phase 1 (_format_rdata for DNSKEY/RRSIG parsing)

#### Overview

Full DNSSEC chain validation walks the delegation chain from root to the target
zone, validating at each step:

1. DS record in parent zone references a DNSKEY in the child zone
2. DNSKEY in child zone is covered by a valid RRSIG signed by the parent's DS-referenced key
3. The answer RRSIGs are signed by a DNSKEY in the same zone

This requires **cryptographic signature verification** using the `cryptography`
Python library (ECDSA with P-256/SHA-256 and RSA/SHA-256 are the dominant algorithms).

#### Dependency Check

The `cryptography` library must be installed:

```bash
pip install cryptography
```

Add to `Server/requirements.txt` (or equivalent):

```
cryptography>=41.0
```

#### New Module

**File:** `Server/syxx_server/dnssec_validator.py` (~350 lines)

```python
"""DNSSEC chain validator.

Validates the full DNSSEC chain of trust from the root zone to a target domain:
  Root DNSKEY -> Root DS (for TLD) -> TLD DNSKEY -> TLD DS (for domain) -> Domain DNSKEY -> Answer RRSIG

Each step verifies:
  1. DS record digest matches the child's DNSKEY
  2. RRSIG over the DNSKEY RRset is valid (cryptographic verification)
  3. RRSIG over the answer RRset is valid

Uses the `cryptography` library for signature verification. Only RSASHA256 (alg 8)
and ECDSAP256SHA256 (alg 13) are supported; other algorithms return 'unsupported'.

HONESTY: if any step cannot be verified (unsupported algorithm, missing records,
network failure), that step is marked 'inconclusive'. The chain is only 'valid'
when EVERY step passes cryptographic verification.
"""
```

**Key functions:**

```python
def validate_chain(domain, qtype=1, timeout=3):
    """Validate the full DNSSEC chain for a domain.
    
    Returns: {
        "domain": "example.com",
        "chain": [
            {"zone": ".", "step": "root-dnskey", "status": "valid", "detail": "..."},
            {"zone": "com.", "step": "ds-match", "status": "valid", "detail": "..."},
            {"zone": "com.", "step": "dnskey-rrsig", "status": "valid", "detail": "..."},
            {"zone": "example.com.", "step": "ds-match", "status": "valid", "detail": "..."},
            {"zone": "example.com.", "step": "dnskey-rrsig", "status": "valid", "detail": "..."},
            {"zone": "example.com.", "step": "answer-rrsig", "status": "valid", "detail": "..."},
        ],
        "verdict": "valid",  # "valid" | "invalid" | "unsigned" | "inconclusive"
        "algorithm": "ECDSAP256SHA256",
        "signed": True
    }
    """

def _verify_ds_dnskey(ds_rdata, dnskey_rdata):
    """Verify a DS record's digest matches a DNSKEY record.
    DS RDATA: keytag(2) + algorithm(1) + digest_type(1) + digest(var)
    DNSKEY RDATA: flags(2) + protocol(1) + algorithm(1) + key(var)
    
    Digest type 1 = SHA-1, type 2 = SHA-256, type 4 = SHA-384.
    """

def _verify_rrsig(dnskey_rdata, rrsig_rdata, signed_data, owner_name, rrtype):
    """Cryptographically verify an RRSIG against a DNSKEY.
    Supports:
      Algorithm 8: RSASHA256 (RSA with SHA-256)
      Algorithm 13: ECDSAP256SHA256 (ECDSA P-256 with SHA-256)
    Returns: True (valid), False (invalid), None (unsupported/error)
    """

def _build_rrsig_signed_data(rrsig_rdata, rrset_wire, owner_name):
    """Construct the data that was signed per RFC 4034 section 3.1.8.1.
    signed_data = RRSIG_RDATA_without_signature + canonical RRset
    """

def _parse_dnskey_rdata(rdata):
    """Parse DNSKEY rdata into flags, protocol, algorithm, public_key bytes."""

def _parse_ds_rdata(rdata):
    """Parse DS rdata into keytag, algorithm, digest_type, digest bytes."""

def _parse_rrsig_rdata(rdata):
    """Parse RRSIG rdata into type_covered, algorithm, labels, original_ttl,
    expiration, inception, keytag, signer_name, signature."""
```

**Cryptographic verification using `cryptography`:**

```python
from cryptography.hazmat.primitives.asymmetric import ec, rsa, padding, utils
from cryptography.hazmat.primitives import hashes, serialization

def _verify_ecdsa_p256(public_key_bytes, signature_bytes, data_bytes):
    """Verify ECDSA P-256 SHA-256 signature."""
    try:
        # DNSKEY stores raw EC point (uncompressed: 0x04 + X + Y, 64 bytes for P-256)
        public_key = ec.EllipticCurvePublicNumbers(
            x=int.from_bytes(public_key_bytes[:32], 'big'),
            y=int.from_bytes(public_key_bytes[32:], 'big'),
            curve=ec.SECP256R1()
        ).public_key()
        public_key.verify(signature_bytes, data_bytes, ec.ECDSA(hashes.SHA256()))
        return True
    except Exception:
        return False

def _verify_rsa_sha256(public_key_bytes, signature_bytes, data_bytes):
    """Verify RSA SHA-256 signature."""
    try:
        public_key = serialization.load_der_public_key(public_key_bytes)
        public_key.verify(signature_bytes, data_bytes,
                          padding.PKCS1v15(), hashes.SHA256())
        return True
    except Exception:
        return False
```

#### API Route

**File:** `Server/syxx_server/app.py`
**Add import:** `from . import dnssec_validator`
**Add route in `_dispatch_get()`:**

```python
if path == "/api/dnssec":
    return self._route_dnssec(query)
```

**Request Parameters:**

| Param | Required | Type | Description |
|-------|----------|------|-------------|
| `domain` | Yes | string | Domain to validate DNSSEC chain for |

**Response Schema:**

```json
{
  "domain": "example.com",
  "verdict": "valid",
  "signed": true,
  "algorithm": "ECDSAP256SHA256",
  "chain": [
    {
      "zone": ".",
      "step": "root-dnskey",
      "status": "valid",
      "algorithm": "RSASHA256",
      "keyTag": 20326
    },
    {
      "zone": "com.",
      "step": "ds-match",
      "status": "valid",
      "digestType": "SHA-256"
    },
    {
      "zone": "example.com.",
      "step": "answer-rrsig",
      "status": "valid",
      "keyTag": 12345,
      "inception": "2026-07-01T00:00:00Z",
      "expiration": "2026-08-01T00:00:00Z"
    }
  ],
  "errors": []
}
```

**Route Handler:**

```python
def _route_dnssec(self, query):
    """Free-tier DNSSEC chain validator."""
    if not self._rate_ok(None):
        return
    domain = (query.get("domain", [""])[0]).strip()
    if not domain or not investigate._valid_domain(domain) or len(domain) > 253:
        return self._json_error(400, "invalid or missing domain")
    try:
        result = dnssec_validator.validate_chain(domain)
    except Exception as e:
        logging_util.error("dnssec_validation_failed", domain=domain, err=str(e))
        return self._json_error(502, "DNSSEC validation failed: %s" % e)
    logging_util.incr("dnssec_validations")
    self._send(200, result)
```

#### Testing

- `GET /api/dnssec?domain=cloudflare.com` should return `verdict: valid` (known DNSSEC-signed domain)
- `GET /api/dnssec?domain=example.com` should return `verdict: unsigned` or `valid` depending on current state
- Failure: unsigned domain should return `verdict: unsigned`, not `valid`
- Failure: domain with expired RRSIG should return `verdict: invalid`
- Failure: unsupported algorithm should return `status: inconclusive` for that step
- Failure: network timeout should return `verdict: inconclusive`, not `valid`

#### Risk Assessment

This is the highest-risk feature:

- Cryptographic code must be correct; a bug that returns `valid` on an invalid chain
  is a false-secure verdict (AGENTS.md 1, 5)
- The `cryptography` library is an additional dependency
- Wire-format construction for signed data must be byte-exact
- RRSIG window validation must use RFC 1982 serial arithmetic (already implemented
  in `resolver.py:_rrsig_window_valid`)

---

## Phase 5: Frontend Integration

### Problem Statement

ALL frontend "free" command outputs in index.html are currently FAKE:

- `rr()` at line 591: generates deterministic fake DNS records
- `fakeIP()` at line 590: generates deterministic fake IPs
- `nslookup` at line 648: hardcoded `"Server: 1.1.1.1"` + fake records
- `dig` at line 649: hardcoded `"DiG 9.18"` + fake records + fake query time
- `whois` at line 651: hardcoded `"MarkMonitor Inc."`, fake dates, fake status
- `dnssec` at line 652: hardcoded `"alg 13, ECDSAP256SHA256"`, fake chain validation
- `conclude` at line 647: hardcoded `"8% confidence"` verdict

This violates AGENTS.md 5: "FAKE `[SUCCESS]` / SECURE / TAMPER MESSAGES IN A REAL
CODE PATH ARE A LIE."

### Changes Required

**File:** `Server/index.html`

#### Step 1: Remove fake helpers

**Delete or replace:**

- `fakeIP()` function (line 590)
- `rr()` function (line 591)

These are replaced by real API calls. Keep `hshex()` (line 532) if used elsewhere.

#### Step 2: Add API helper function

Add a new helper for making API calls to free-tier endpoints:

```javascript
async function freeAPI(path, params) {
    const url = new URL(path, window.location.origin);
    Object.entries(params || {}).forEach(([k, v]) => {
        if (v !== undefined && v !== null && v !== '') url.searchParams.set(k, v);
    });
    const resp = await fetch(url.toString());
    if (!resp.ok) {
        const err = await resp.json().catch(() => ({error: 'request failed'}));
        throw new Error(err.error || `HTTP ${resp.status}`);
    }
    return resp.json();
}
```

#### Step 3: Wire each command to real API calls

**Replace the switch block at lines 643-657 with real API calls:**

##### nslookup (replaces line 648)

```javascript
case 'nslookup': {
    try {
        const data = await freeAPI('/api/lookup', {domain: d, type: ty});
        let out = `Server: ${data.server}\n\nNon-authoritative answer:\n`;
        data.records.forEach(r => {
            out += `${data.domain}.\t300\tIN\t${r.type}\t${r.value}\n`;
        });
        if (!data.records.length) out += `(no ${ty} records found)\n`;
        out += `\n;; rcode: ${data.rcodeName}`;
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### dig (replaces line 649)

```javascript
case 'dig': {
    try {
        const data = await freeAPI('/api/lookup', {domain: d, type: ty});
        let out = `; <<>> Syxx DiG <<>> ${d} ${ty}\n`;
        out += `;; ->>HEADER<<- opcode: QUERY, status: ${data.rcodeName}\n`;
        out += `;; ANSWER SECTION:\n`;
        data.records.forEach(r => {
            out += `${data.domain}.\t300\tIN\t${r.type}\t${r.value}\n`;
        });
        out += `\n;; Query time: (server-side)\n`;
        out += `;; SERVER: ${data.server}#53\n`;
        out += `;; AD flag: ${data.ad ? 'set' : 'not set'}`;
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### whois (replaces line 651)

```javascript
case 'whois': {
    try {
        const data = await freeAPI('/api/whois', {domain: d});
        let out = `Domain: ${data.domain}\n`;
        out += `Registrar: ${data.registrar || 'N/A'}\n`;
        out += `Created: ${data.created || 'N/A'}\n`;
        out += `Updated: ${data.updated || 'N/A'}\n`;
        out += `Expires: ${data.expires || 'N/A'}\n`;
        out += `Status: ${(data.status || []).join(', ') || 'N/A'}\n`;
        out += `Nameservers: ${(data.nameservers || []).join(', ') || 'N/A'}\n`;
        out += `Source: ${data.source}`;
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### dnssec (replaces line 652)

```javascript
case 'dnssec': {
    try {
        const data = await freeAPI('/api/dnssec', {domain: d});
        let out = `DNSSEC Validation: ${data.domain}\n`;
        out += `Verdict: ${data.verdict.toUpperCase()}\n`;
        out += `Signed: ${data.signed ? 'Yes' : 'No'}\n`;
        if (data.algorithm) out += `Algorithm: ${data.algorithm}\n`;
        out += `\nChain:\n`;
        (data.chain || []).forEach(step => {
            out += `  ${step.zone} [${step.step}]: ${step.status}`;
            if (step.detail) out += ` - ${step.detail}`;
            out += `\n`;
        });
        if (data.errors && data.errors.length) {
            out += `\nErrors:\n`;
            data.errors.forEach(e => out += `  ! ${e}\n`);
        }
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### conclude (replaces line 647)

Remove the hardcoded verdict. Replace with an aggregation of the actual results
from the session's prior commands, or mark as "not yet computed":

```javascript
case 'conclude': {
    mrun(typed, 'Use the investigation tools above to gather evidence.\n' +
         'Run prophet, mirror, and warp to build a complete picture.\n' +
         'A conclusion requires real evidence from multiple vantage points.');
    break;
}
```

#### Step 4: Add new command buttons

The following features need new buttons added to the DNS tools command palette:

**Location:** index.html, inside the command palette grid (after line 218)

Add buttons for: `spf`, `subdomains`, `propagation`, `reverse-dns`, `benchmark`

```html
<span class="cmd" style="--c:teal" onclick="runCmd('spf')">spf</span>
<span class="cmd" style="--c:teal" onclick="runCmd('subdomains')">subdomains</span>
<span class="cmd" style="--c:teal" onclick="runCmd('propagation')">propagation</span>
<span class="cmd" style="--c:teal" onclick="runCmd('reverse-dns')">reverse-dns</span>
<span class="cmd" style="--c:teal" onclick="runCmd('benchmark')">benchmark</span>
```

#### Step 5: Wire new command handlers

Add cases for new commands in the switch block:

##### spf

```javascript
case 'spf': {
    try {
        const data = await freeAPI('/api/email-auth', {domain: d});
        let out = `Email Authentication: ${data.domain}\n\n`;
        out += `SPF: ${data.spf.verdict.toUpperCase()}\n`;
        if (data.spf.record) out += `  Record: ${data.spf.record}\n`;
        out += `\nDMARC: ${data.dmarc.verdict.toUpperCase()}\n`;
        if (data.dmarc.record) out += `  Record: ${data.dmarc.record}\n`;
        out += `  Policy: ${data.dmarc.policy || 'N/A'}\n`;
        if (data.dkim.present !== undefined) {
            out += `\nDKIM: ${data.dkim.verdict.toUpperCase()}\n`;
            if (data.dkim.selector) out += `  Selector: ${data.dkim.selector}\n`;
        }
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### subdomains

```javascript
case 'subdomains': {
    try {
        const data = await freeAPI('/api/subdomains', {domain: d, resolve: 'true'});
        let out = `Subdomain Enumeration: ${data.domain}\n`;
        out += `Source: ${data.source} | Found: ${data.count}`;
        if (data.truncated) out += ' (truncated)';
        out += '\n\n';
        (data.subdomains || []).slice(0, 50).forEach(sub => {
            const ips = sub.ips ? sub.ips.join(', ') : '(no resolution)';
            out += `  ${sub.name} -> ${ips}\n`;
        });
        if ((data.subdomains || []).length > 50)
            out += `  ... and ${data.subdomains.length - 50} more\n`;
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### propagation

```javascript
case 'propagation': {
    try {
        const data = await freeAPI('/api/propagation', {domain: d, type: ty});
        let out = `DNS Propagation: ${data.domain} (${data.type})\n`;
        out += `Consensus: ${data.consensus} | ${data.successCount}/${data.totalResolvers} resolvers (${data.propagationPct}%)\n`;
        out += `Values: ${data.uniqueValues.join(', ') || 'none'}\n\n`;
        (data.resolvers || []).forEach(r => {
            const vals = r.records ? r.records.map(x => x.value).join(', ') : r.error || 'no data';
            const icon = r.status === 'ok' ? '+' : '-';
            out += `  [${icon}] ${r.label} (${r.region}): ${vals} [${r.time_ms}ms]\n`;
        });
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### reverse-dns

```javascript
case 'reverse-dns': {
    const ip = d;  // user enters IP in the domain field for this command
    try {
        const data = await freeAPI('/api/reverse-dns', {ip: ip});
        let out = `Reverse DNS: ${data.ip}\n`;
        out += `ARPA: ${data.arpa}\n`;
        out += `PTR: ${data.ptr || '(no PTR record found)'}\n`;
        if (data.allPtr && data.allPtr.length > 1) {
            out += `All PTR records:\n`;
            data.allPtr.forEach(p => out += `  ${p}\n`);
        }
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

##### benchmark

```javascript
case 'benchmark': {
    try {
        const data = await freeAPI('/api/benchmark', {domain: d, type: ty, rounds: '3'});
        let out = `DNS Benchmark: ${data.domain} (${data.type})\n`;
        out += `Fastest: ${data.fastest || 'N/A'} | Slowest: ${data.slowest || 'N/A'}\n\n`;
        (data.resolvers || []).forEach(r => {
            const avg = r.avg_ms !== null ? `${r.avg_ms}ms` : 'FAIL';
            out += `  ${r.label}: avg ${avg}`;
            if (r.min_ms !== null) out += ` (min ${r.min_ms}ms, max ${r.max_ms}ms)`;
            if (r.failures) out += ` [${r.failures} failures]`;
            out += '\n';
        });
        mrun(typed, out);
    } catch(e) { mrun(typed, `error: ${e.message}`); }
    break;
}
```

#### Step 6: Make runCmd async

The `runCmd()` function at line 595 must be made `async` to support `await freeAPI()`:

```javascript
// Change from:
function runCmd(name) {
// To:
async function runCmd(name) {
```

And the command switch block must use `await` for all API calls (already shown above).

---

## Implementation Sequence (Recommended Order)

| Step | Phase | Feature | Estimated Lines | Dependencies |
|------|-------|---------|----------------|--------------|
| 1 | 1 | _format_rdata extension | ~50 modified | None |
| 2 | 2a | DNS Lookup route | ~50 new | Step 1 |
| 3 | 2b | CAA Inspector | 0 (free with Step 2) | Step 2 |
| 4 | 2c | Reverse DNS route + helper | ~50 new | Steps 1, 2 |
| 5 | 2d | DNS Benchmark route | ~80 new | Step 1 |
| 6 | 3a | WHOIS module + route | ~200 + ~20 new | None |
| 7 | 3b | Email Auth module + route | ~200 + ~20 new | Step 1 |
| 8 | 3c | Subdomain Enum module + route | ~200 + ~20 new | None |
| 9 | 3d | Propagation config + route | ~50 config + ~80 route | Step 1 |
| 10 | 4 | DNSSEC Validator module + route | ~350 + ~20 new | Step 1, cryptography |
| 11 | 5 | Frontend: remove fakes, wire all | ~200 modified | Steps 2-10 |

**Total estimated new code:** ~1,370 lines across 5 new modules + route handlers
**Total estimated modified code:** ~300 lines in resolver.py, app.py, config.py, index.html

### Parallelization Opportunities

Steps 6, 7, 8 can proceed in parallel (independent modules).
Steps 2, 5 can proceed in parallel after Step 1 is complete.
Step 10 (DNSSEC) should be last due to complexity and crypto risk.

---

## File Change Summary

| File | Type | Changes |
|------|------|---------|
| `Server/syxx_server/resolver.py` | Modify | Extend `_format_rdata` (lines 161-175), update `_TYPE_NAMES` (line 38), add `ip_to_arpa()` |
| `Server/syxx_server/app.py` | Modify | Add 8 new route handlers + imports + dispatch entries |
| `Server/syxx_server/config.py` | Modify | Add `PROPAGATION_RESOLVERS` config, optionally `_json_list` helper |
| `Server/syxx_server/whois_lookup.py` | Create | New module (~200 lines) |
| `Server/syxx_server/email_auth.py` | Create | New module (~200 lines) |
| `Server/syxx_server/subdomain_enum.py` | Create | New module (~200 lines) |
| `Server/syxx_server/dnssec_validator.py` | Create | New module (~350 lines) |
| `Server/index.html` | Modify | Remove fakes, add API helper, wire all commands, add buttons |
| `Server/requirements.txt` | Modify | Add `cryptography>=41.0` |

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| DNSSEC crypto bug returns false-valid | Medium | Critical | Extensive test cases with known-valid and known-invalid chains; never return `valid` on error |
| crt.sh API changes/breaks | Low | Low | Graceful degradation (empty results), timeout handling |
| WHOIS rate limiting by registries | Medium | Medium | Cache results, rate-limit per-domain, RDAP preferred |
| `_format_rdata` bug corrupts existing queries | Low | High | Existing record types (A, AAAA, NS, CNAME) are unchanged; new branches are additive |
| Propagation resolver list becomes stale | Low | Low | Config-driven, env-overridable, easy to update |
| Frontend async conversion breaks existing commands | Low | Medium | Test prophet/mirror/warp still work after async conversion |
| `cryptography` library not installed on prod | Medium | High | Add to requirements.txt, add preflight check in dnssec_validator.py |

---

## Verification Plan

### Per-Feature Integration Tests

Each feature must pass a real-world test before being marked complete:

1. **DNS Lookup:** `curl /api/lookup?domain=google.com&type=MX` returns real MX records
2. **CAA:** `curl /api/lookup?domain=letsencrypt.org&type=CAA` returns real CAA records
3. **Reverse DNS:** `curl /api/reverse-dns?ip=8.8.8.8` returns `dns.google`
4. **Benchmark:** `curl /api/benchmark?domain=google.com` returns timing for 3+ resolvers
5. **WHOIS:** `curl /api/whois?domain=google.com` returns real registrar data
6. **Email Auth:** `curl /api/email-auth?domain=google.com` returns real SPF/DMARC records
7. **Subdomains:** `curl /api/subdomains?domain=github.com` returns real subdomains from CT logs
8. **Propagation:** `curl /api/propagation?domain=google.com` returns results from 10+ resolvers
9. **DNSSEC:** `curl /api/dnssec?domain=cloudflare.com` returns `verdict: valid` with chain

### AGENTS.md Pre-Commit Gate

Before marking any feature complete, verify:

1. **Evidence first:** No user-facing conclusion renders before all required data is collected. (All routes query real DNS/WHOIS/RDAP before returning.)
2. **Real run:** Show a real curl command proving the feature works on real input.
3. **Math check:** No confidence/probability scores that give high confidence on empty input. (Benchmark averages are straightforward arithmetic.)
4. **Mocks:** No fake success/secure/tamper messages. (The `rr()` and `fakeIP()` functions are deleted.)
5. **Scope:** No machinery on top of untrustworthy signals.
6. **Honesty:** DNSSEC returns `inconclusive` on any unverified step, not `valid`.
