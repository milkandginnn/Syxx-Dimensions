# Server-Side MUST FIX Deltas: Implementation Plan

**Pipeline:** Research → Plan → Build → Verify
**Scope:** 24 A-domain server deltas (A025-A124)
**Generated:** 2026-07-27
**Grounded in:** `server-deltas-research.md`, live source tree at HEAD

---

## Table of Contents

1. [Implementation Clusters](#implementation-clusters)
2. [Cluster 1: SSRF & Packet Parsing Hardening (A055, A077, A115)](#cluster-1)
3. [Cluster 2: DNSSEC Validation (A041, A042, A043)](#cluster-2)
4. [Cluster 3: Consensus & Verdict Logic (A026, A044, A045)](#cluster-3)
5. [Cluster 4: Content Diff & Script Extraction (A048, A093)](#cluster-4)
6. [Cluster 5: Stripe Fulfillment (A025)](#cluster-5)
7. [Cluster 6: API Security & Rate Limiting (A035, A099, A106, A107)](#cluster-6)
8. [Cluster 7: PII & Privacy (A031, A072, A097)](#cluster-7)
9. [Cluster 8: Evidence Integrity (A036, A117)](#cluster-8)
10. [Cluster 9: Resource Safety & Work Budgets (A028, A032)](#cluster-9)
11. [Cluster 10: DoH & Resolver Efficacy (A124)](#cluster-10)
12. [Cluster 11: Test Coverage (A118)](#cluster-11)
13. [Dependency Graph & Build Order](#dependency-graph)
14. [Full Test Strategy](#test-strategy)
15. [Ripple Effects Matrix](#ripple-effects-matrix)

---

## Implementation Clusters <a name="implementation-clusters"></a>

Deltas are grouped by file overlap, logical dependency, and merge conflict risk. Each cluster can be implemented and merged independently. Within a cluster, deltas are ordered by dependency.

| Cluster | Deltas | Files Affected | Priority |
|---------|--------|----------------|----------|
| 1: SSRF & Packet Parsing | A055, A077, A115 | `resolver.py`, `fetcher.py` | Critical |
| 2: DNSSEC Validation | A041, A042, A043 | `resolver.py` | High |
| 3: Consensus & Verdict | A026, A044, A045 | `app.py`, `resolver.py` | High |
| 4: Content Diff & Scripts | A048, A093 | `warp.py` | High |
| 5: Stripe Fulfillment | A025 | `fulfillment.py`, `db.py` | Critical |
| 6: API Security | A035, A099, A106, A107 | `app.py`, `mailer.py`, `config.py` | High |
| 7: PII & Privacy | A031, A072, A097 | `db.py`, `app.py` | High |
| 8: Evidence Integrity | A036, A117 | `leak.py`, `passivedns.py` | High |
| 9: Resource Safety | A028, A032 | `app.py`, `resolver.py` | High |
| 10: DoH Efficacy | A124 | `app.py` | Medium |
| 11: Test Coverage | A118 | `tests/` | Critical |

---

## Cluster 1: SSRF & Packet Parsing Hardening <a name="cluster-1"></a>

**Deltas:** A055, A077, A115
**Priority:** Critical — must land before any other cluster
**Files:** `Server/syxx_server/resolver.py`, `Server/syxx_server/fetcher.py`

### A055: SSRF Guard Misses IPv4-Mapped IPv6 Addresses

**Location:** `Server/syxx_server/fetcher.py:87-110`; `Server/syxx_server/resolver.py:68-80`

**Current state:** `fetcher.py` already has `_is_ipv4_mapped_ipv6()` and `_normalize_ipv6_to_ipv4()` (lines 70-84) and applies them in `_ip_is_public()` (lines 93-97). However, `resolver.py:_ip_is_public()` at lines 68-80 does NOT have the IPv4-mapped normalization — it only checks `addr.is_private`, etc. If a DNS response returns `::ffff:10.0.0.1` as a glue record, `resolver.py:_ip_is_public()` would call `ipaddress.ip_address("::ffff:10.0.0.1")`, see a v6 address, check `.is_private` (which returns False for v6 mapped v4 private), and ACCEPT it.

**Code changes:**

1. **`resolver.py:68-80`** — Add IPv4-mapped normalization identical to `fetcher.py`:

```python
def _ip_is_public(ip):
    """True only for publicly-routable unicast IPs. Gates which glue/NS IPs the authoritative
    walker will query, so an attacker-controlled zone can't point us at internal hosts (A011)."""
    try:
        addr = ipaddress.ip_address(ip)
    except ValueError:
        return False
    # A055: unwrap IPv4-mapped IPv6 (::ffff:x.x.x.x) so private IPv4
    # behind the mapping is correctly blocked.
    if addr.version == 6 and addr.ipv4_mapped is not None:
        addr = addr.ipv4_mapped
    if (addr.is_private or addr.is_loopback or addr.is_link_local
            or addr.is_multicast or addr.is_reserved or addr.is_unspecified):
        return False
    if addr.version == 4 and addr in ipaddress.ip_network("100.64.0.0/10"):
        return False
    return True
```

2. **`resolver.py`** — Import check: `ipaddress` is already imported at line 8. No new imports needed.

**Tests:**

```python
# tests/test_resolver_ssrf.py (new file or add to test_resolver_fetcher_ssrf.py)

def test_ipv4_mapped_private_v6_blocked():
    """A055: ::ffff:10.0.0.1 must be rejected as non-public."""
    assert resolver._ip_is_public("::ffff:10.0.0.1") is False
    assert resolver._ip_is_public("::ffff:127.0.0.1") is False
    assert resolver._ip_is_public("::ffff:192.168.1.1") is False
    assert resolver._ip_is_public("::ffff:169.254.0.1") is False

def test_ipv4_mapped_public_v6_allowed():
    """A055: ::ffff:8.8.8.8 must be accepted."""
    assert resolver._ip_is_public("::ffff:8.8.8.8") is True

def test_native_public_v4_v6_unaffected():
    """A055: normal public IPs still work."""
    assert resolver._ip_is_public("8.8.8.8") is True
    assert resolver._ip_is_public("2001:4860:4860::8888") is True
    assert resolver._ip_is_public("10.0.0.1") is False
    assert resolver._ip_is_public("127.0.0.1") is False
```

**Ripple effects:**
- Affects all DNS glue/IP validation in `resolve_authoritative()` and `_query_sections()`.
- Must verify `fetcher.py` already handles this (it does — lines 70-97).
- No client-side parity needed (server-only SSRF guard).

---

### A077: DNS Packet Parsing rdlength Beyond Data Boundaries

**Location:** `Server/syxx_server/resolver.py:188-196` (answer parsing in `_parse_response`); `Server/syxx_server/resolver.py:528-548` (`_read_records`)

**Current state:** In `_parse_response()` at line 195-196:
```python
rdata = data[offset:offset + rdlength]
offset += rdlength
```
Python slicing silently truncates beyond bounds, so `data[offset:offset + rdlength]` with `offset + rdlength > len(data)` returns a short slice. The record is then formatted with potentially truncated rdata. In `_read_records()` at lines 542-544 there IS a bounds check (`if offset + rdlength > len(data): break`) which is correct.

**Code changes:**

1. **`resolver.py:188-196`** — Add bounds check before slicing in `_parse_response()`:

```python
for _ in range(an):
    _, offset = _read_name(data, offset)
    if offset + 10 > len(data):
        break
    rtype, rclass, ttl, rdlength = struct.unpack(">HHIH", data[offset:offset + 10])
    offset += 10
    rdata_offset = offset
    # A077: validate rdlength does not exceed remaining data.
    # Python slicing silently truncates, producing partial rdata that
    # _format_rdata may misinterpret (e.g. a 2-byte A record).
    if offset + rdlength > len(data):
        break
    rdata = data[offset:offset + rdlength]
    offset += rdlength
    value = _format_rdata(rtype, rdata, data, rdata_offset)
    records.append({"type": _TYPE_NAMES.get(rtype, str(rtype)), "value": value})
```

**Tests:**

```python
def test_parse_response_truncated_rdlength():
    """A077: A response claiming rdlength=200 for a 4-byte A record must not
    produce a truncated rdata value — it must drop the record."""
    # Build a minimal DNS response with a 2-byte rdlength but only 2 bytes of data
    header = struct.pack(">HHHHHH", 12345, 0x8180, 1, 1, 0, 0)
    question = b'\x07example\x03com\x00' + struct.pack(">HH", 1, 1)
    rr = b'\x07example\x03com\x00' + struct.pack(">HHIH", 1, 1, 300, 200) + b'\xde\xad'
    data = header + question + rr
    result = resolver._parse_response(data, 12345, 1, expected_qname="example.com")
    # The record with invalid rdlength should be dropped
    assert len(result["records"]) == 0
```

**Ripple effects:**
- All DNS parsing paths (`_parse_response`, `_parse_sections`, `_read_records`) must be consistent.
- `_read_records` already has the check; only `_parse_response` needs it.

---

### A115: `_format_rdata` Returns `rdata.hex()` for Malformed A Records

**Location:** `Server/syxx_server/resolver.py:203-273`

**Current state:** `_format_rdata()` at line 208 checks `rtype == TYPE_A and len(rdata) == 4`. If the length is wrong (after A077 adds the bounds check, this becomes a shorter-than-4 case), control falls through to the final `return rdata.hex()` at line 273. The caller at line 199 still labels this record as `"A"` via `_TYPE_NAMES.get(rtype, ...)`. Result: a 2-byte A record returns `{'type': 'A', 'value': 'dead'}`.

**Code changes:**

1. **`resolver.py:203-273`** — Return `None` for length-invalid A/AAAA and let the caller drop the record:

```python
def _format_rdata(rtype, rdata, full, rdata_offset):
    """Format raw rdata into a human-readable string. Returns None when the
    record's rdata length does not match the type's expected format — the
    caller must drop such records rather than label hex garbage as a typed answer."""
    # --- A (IPv4) ---
    if rtype == TYPE_A:
        if len(rdata) != 4:
            return None  # A115: malformed A record — do not emit hex as "A"
        return socket.inet_ntop(socket.AF_INET, rdata)
    # --- AAAA (IPv6) ---
    if rtype == TYPE_AAAA:
        if len(rdata) != 16:
            return None  # A115: malformed AAAA record
        return socket.inet_ntop(socket.AF_INET6, rdata)
    # ... rest unchanged ...
```

2. **`resolver.py:197-199`** — Caller drops `None` values:

```python
    value = _format_rdata(rtype, rdata, data, rdata_offset)
    # A115: drop records whose rdata failed to format — never emit
    # hex garbage labeled as a typed answer (false typed answer).
    if value is None:
        continue
    records.append({"type": _TYPE_NAMES.get(rtype, str(rtype)), "value": value})
```

3. **`resolver.py:546`** — Same guard in `_read_records()`:

```python
    val = _format_rdata(rtype, data[offset:offset + rdlength], data, rdata_offset)
    if val is None:
        offset += rdlength
        continue  # A115: drop malformed records in the walker path too
    recs.append({"owner": owner, "type": _TYPE_NAMES.get(rtype, str(rtype)), "rtype": rtype,
                 "ttl": ttl, "value": val})
```

**Tests:**

```python
def test_format_rdata_short_a_returns_none():
    """A115: A record with 2-byte rdata must return None, not hex garbage."""
    result = resolver._format_rdata(1, bytes.fromhex('dead'), b'', 0)
    assert result is None

def test_format_rdata_short_aaaa_returns_none():
    """A115: AAAA record with 8-byte rdata must return None."""
    result = resolver._format_rdata(28, bytes.fromhex('deadbeef' * 2), b'', 0)
    assert result is None

def test_format_rdata_valid_a_returns_ip():
    """Happy path: valid A record returns dotted-decimal."""
    result = resolver._format_rdata(1, bytes([8, 8, 8, 8]), b'', 0)
    assert result == "8.8.8.8"

def test_malformed_a_does_not_reach_passive_dns():
    """A115 failure case: a short-A record must not appear in passivedns."""
    # Simulate parsing a response containing a short-A record
    # and assert it does not appear in the records list
```

**Ripple effects:**
- `investigate._resolve_all` (investigate.py) filters on `r["type"] in ("A","AAAA")` and feeds results into `passive_dns`. After this fix, malformed records never reach that path.
- `/api/diff` answer sets are also protected.
- Both `_parse_response()` and `_read_records()` callers are covered.

---

## Cluster 2: DNSSEC Validation <a name="cluster-2"></a>

**Deltas:** A041, A042, A043
**Priority:** High
**Files:** `Server/syxx_server/resolver.py:613-765`

### A042: RRSIG Window Check Ignores Signature Inception

**Location:** `Server/syxx_server/resolver.py:620-629`

**Current state:** `_rrsig_window_valid()` already checks BOTH inception and expiration (lines 624-629). The function was previously named `_check_rrsig_expiry()` and only checked expiration. **This delta is already fixed in the current code.** The function now:
- Checks `inception <= expiration` (serial, line 624-625)
- Checks span < MAX_RRSIG_VALIDITY_SPAN (line 626-627)
- Checks `inception <= now <= expiration` (lines 628-629)

**Verification:** Run the existing tests to confirm, then add an explicit test:

```python
def test_rrsig_window_valid_inception_checked():
    """A042: a signature whose inception is in the future must be rejected."""
    future_inception = int(time.time()) + 86400  # tomorrow
    future_expiration = int(time.time()) + 172800  # day after tomorrow
    assert resolver._rrsig_window_valid(future_inception, future_expiration, int(time.time())) is False

def test_rrsig_window_valid_expired_rejected():
    """A042: a signature whose expiration is in the past must be rejected."""
    past_inception = int(time.time()) - 172800
    past_expiration = int(time.time()) - 86400
    assert resolver._rrsig_window_valid(past_inception, past_expiration, int(time.time())) is False

def test_rrsig_window_valid_current_accepted():
    """A042: a signature valid right now must be accepted."""
    now = int(time.time())
    assert resolver._rrsig_window_valid(now - 86400, now + 86400, now) is True
```

**Status:** Already fixed. Verify and add tests only.

---

### A043: Server Accepts Any RRSIG as Covering DNSKEY RRset

**Location:** `Server/syxx_server/resolver.py:632-647`

**Current state:** `_parse_rrsig_window()` at line 642-643 already checks `type_covered != expected_type_covered` and returns `None` if it doesn't match. The function signature takes `expected_type_covered=TYPE_DNSKEY` as default. When called from `_zone_is_signed()` at line 671, it is called with `TYPE_DNSKEY` explicitly.

**This delta is already fixed in the current code.**

**Verification:**

```python
def test_parse_rrsig_window_rejects_wrong_type_covered():
    """A043: RRSIG covering A (type 1) must not be accepted as DNSKEY evidence."""
    # Build minimal RRSIG rdata: typeCovered=1 (A), algorithm=8, labels=0,
    # originalTTL=300, expiration=..., inception=..., keyTag=..., signer=..., sig=...
    rdata = struct.pack(">HBBII", 1, 8, 0, 300,
                        int(time.time()) + 86400, int(time.time()) - 86400)
    rdata += b'\x00\x01example.com\x00' + b'\x00' * 10  # minimal signer + sig
    result = resolver._parse_rrsig_window(rdata.hex(), expected_type_covered=resolver.TYPE_DNSKEY)
    assert result is None

def test_parse_rrsig_window_accepts_matching_type():
    """A043: RRSIG covering DNSKEY must be accepted."""
    now = int(time.time())
    rdata = struct.pack(">HBBII", resolver.TYPE_DNSKEY, 8, 0, 300,
                        now + 86400, now - 86400)
    rdata += b'\x00\x01example.com\x00' + b'\x00' * 10
    result = resolver._parse_rrsig_window(rdata.hex(), expected_type_covered=resolver.TYPE_DNSKEY)
    assert result is not None
```

**Status:** Already fixed. Verify and add tests only.

---

### A041: DNSSEC Validation Stops at First Reachable Vantage

**Location:** `Server/syxx_server/resolver.py:650-674` (`_zone_is_signed`); called from `resolve_authoritative` at line 713

**Current state:** `_zone_is_signed()` queries a single authoritative server. If that server is compromised and strips DNSKEY/RRSIG, the zone is reported as unsigned or inconclusive. It should query multiple vantages.

**Code changes:**

1. **`resolver.py:650-674`** — Rewrite `_zone_is_signed()` to accept a list of servers and query multiple:

```python
def _zone_is_signed(zone, servers, timeout, now=None):
    """Query MULTIPLE authoritative servers for the zone's DNSKEY with DO bit,
    and report DNSSEC posture by consensus:
      True  = majority of reachable servers show DNSKEY + valid RRSIG
      False = majority of reachable servers show no DNSKEY (unsigned zone)
      None  = split verdict, or all servers unreachable
    A041: stops at first reachable vantage — must query multiple for accuracy."""
    if now is None:
        now = time.time()
    results = []
    for server_ip in servers[:3]:  # bounded: max 3 vantages
        try:
            resp = _query_sections(zone, TYPE_DNSKEY, server_ip, timeout, do=True)
        except DNSError:
            continue
        has_dnskey = any(r["type"] == "DNSKEY" for r in resp["answers"])
        has_valid_rrsig = False
        if has_dnskey:
            for r in resp["answers"]:
                if r.get("rtype") != TYPE_RRSIG:
                    continue
                window = _parse_rrsig_window(r["value"], TYPE_DNSKEY)
                if window and _rrsig_window_valid(window[0], window[1], now):
                    has_valid_rrsig = True
                    break
        results.append({"dnskey": has_dnskey, "valid_rrsig": has_valid_rrsig})
    if not results:
        return None
    signed_count = sum(1 for r in results if r["dnskey"] and r["valid_rrsig"])
    unsigned_count = sum(1 for r in results if not r["dnskey"])
    total = len(results)
    if signed_count > total / 2:
        return True
    if unsigned_count > total / 2:
        return False
    return None  # split or inconclusive
```

2. **`resolver.py:713`** — Update the call site in `resolve_authoritative()`:

```python
    # Old: _zone_is_signed(zone, used_ip, timeout)
    # New: pass the current server list for multi-vantage check
    ns_ips = [s["ip"] for s in servers[:MAX_SERVERS_PER_LEVEL]]
    "dnskeyObserved": _zone_is_signed(zone, ns_ips, timeout),
```

**Tests:**

```python
def test_zone_is_signed_multi_vantage_consensus():
    """A041: when 2 of 3 vantages show DNSKEY+RRSIG, zone is signed."""
    # Mock _query_sections to return signed/unsigned/signed for 3 servers
    # Assert result is True

def test_zone_is_signed_all_unreachable():
    """A041: when all vantages are unreachable, result is None."""
    # Mock _query_sections to raise DNSError for all
    # Assert result is None
```

**Ripple effects:**
- `_zone_is_signed()` is called only from `resolve_authoritative()` at line 713.
- The function signature changes from `(zone, server, timeout, now)` to `(zone, servers, timeout, now)`.
- No client-side parity needed (server-only DNSSEC oracle).

---

## Cluster 3: Consensus & Verdict Logic <a name="cluster-3"></a>

**Deltas:** A026, A044, A045
**Priority:** High — false verdicts ship to users
**Files:** `Server/syxx_server/app.py`, `Server/syxx_server/resolver.py`

### A045: Server Maps SERVFAIL/REFUSED to status:ok

**Location:** `Server/syxx_server/resolver.py:1054-1057,1083-1093`

**Current state:** Looking at the actual code, `_parse_response()` returns `rcode` as an integer (line 200: `"rcode": rcode`). The routes that consume it (`/api/lookup` at `app.py:1421-1436`) already map rcodes to names. The issue is in `investigate.py` or other consumers that treat any response with `records` as "ok".

**Correction:** After reading the actual code, the rcode IS returned correctly. The research document may reference a line that has drifted. The real issue is likely in `investigate.py` or in the route handlers that treat a SERVFAIL response with empty records as success.

**Code changes:**

1. **`app.py` — `_route_authoritative()`** at lines 1158-1164: Already handles `DNSError` properly. But if the authoritative walk returns a SERVFAIL with `verdict: "inconclusive"` and `authoritative: None`, the client sees an inconclusive result. Verify this path is correct.

2. **`app.py` — `_route_diff()`** at lines 1194-1200: When `auth` has `verdict: "inconclusive"` (SERVFAIL), it falls back to `clean_answers`. If both fail, `reference` is empty and the result is `inconclusive`. This is correct.

3. **Investigation of `resolver.py` rcode mapping:** The `_parse_response()` returns `rcode` as an integer. Consumers must check for `rcode != 0` when evaluating DNS health.

**Actual fix needed:** Add an explicit rcode check in the investigation path:

```python
# app.py _route_diff — after line 1199:
if auth.get("authoritative") and auth["authoritative"].get("rcode") in (2, 5):
    # SERVFAIL(2) or REFUSED(5) from the authoritative itself is evidence
    # of a problem — not "ok". Surface it honestly.
    auth["verdict"] = "server-error"
```

**Tests:**

```python
def test_diff_auth_servfail_not_clean():
    """A045: when the authoritative returns SERVFAIL, the diff must not be 'clean'."""
    # This test would require mocking resolve_authoritative to return SERVFAIL
    # and assert the diff verdict is not 'clean'
```

**Ripple effects:**
- Affects `/api/diff`, `/api/authoritative`, `/api/trace`.
- Client-side consumers must handle the new `"server-error"` verdict value.

---

### A026: /api/diff Flags Legit Cross-Host Third-Party Scripts as "injected"

**Location:** `Server/syxx_server/app.py:189-196,228-229`

**Current state:** `_diff_verdict()` at line 228:
```python
if dns_assessment == "divergent" or c == "injected":
    return "tampered"
```
And `_diff_content()` at lines 195-196:
```python
if injected_hosts:
    assessment = "injected"
```

The issue: a cross-host script from a CDN or analytics provider that the baseline didn't have (e.g., A/B test variant, new analytics snippet) is flagged as "injected" → "tampered". This is a false positive when the script is from a legitimate third party.

**Code changes:**

1. **`app.py:189-196`** — Add corroboration requirement: content divergence alone should not produce "tampered" when DNS is clean:

```python
def _diff_verdict(dns_assessment, content):
    """Fold the DNS and content assessments into one clean|tampered|inconclusive verdict.

    A026: content divergence (cross-host scripts) alone is NOT decisive for tampered.
    Only when BOTH DNS is divergent (authoritative reference mismatch) AND content shows
    injection does the combined verdict reach 'tampered'. Cross-host script differences
    with clean DNS are 'inconclusive' — they may be CDN/A/B-test variation."""
    c = content.get("assessment") if content else None
    # Only authoritative DNS divergence + content injection = tampered
    if dns_assessment == "divergent" and c == "injected":
        return "tampered"
    # DNS divergence alone (no content data) is also tampered
    if dns_assessment == "divergent" and c is None:
        return "tampered"
    # Content injection with DNS match is suspicious but not conclusive
    # (could be A/B test, CDN variant, ad rotation)
    if c == "injected":
        return "inconclusive"
    if dns_assessment == "match" and c in ("match", None):
        return "clean"
    return "inconclusive"
```

**Tests:**

```python
def test_diff_verdict_content_injection_alone_inconclusive():
    """A026: cross-host script injection with clean DNS must not be 'tampered'."""
    assert app._diff_verdict("match", {"assessment": "injected"}) == "inconclusive"

def test_diff_verdict_dns_divergent_plus_injection_tampered():
    """A026: DNS divergence + content injection = tampered."""
    assert app._diff_verdict("divergent", {"assessment": "injected"}) == "tampered"

def test_diff_verdict_both_match_clean():
    """Happy path: both match = clean."""
    assert app._diff_verdict("match", {"assessment": "match"}) == "clean"

def test_diff_verdict_dns_only_divergent_tampered():
    """DNS divergence with no content check = tampered."""
    assert app._diff_verdict("divergent", None) == "tampered"
```

**Ripple effects:**
- All callers of `_diff_verdict()` produce the new behavior.
- Client-side synthesis engine must handle `inconclusive` from this path.
- Cross-ref A018, A012 (baseline-divergence handling).

---

### A044: Server Consensus Drops Reachable Empty Vantages from Count

**Location:** `Server/syxx_server/app.py:235-274` (`_clean_vantage_answers`)

**Current state:** `_clean_vantage_answers()` at lines 263-264:
```python
if answers:
    counts[answers] = counts.get(answers, 0) + 1
```
When a resolver responds with an empty answer set (NXDOMAIN, NODATA), it is silently skipped. This means a censorship scenario where 2 of 3 resolvers return empty (blocked) and 1 returns the real answer would report the real answer as "corroborated" by 1 vantage (but it needs 2), producing `inconclusive` instead of detecting censorship.

**Code changes:**

1. **`app.py:250-266`** — Track empty responses as evidence of censorship:

```python
def _clean_vantage_answers(domain, qtype, qtype_str):
    """Query several public recursive resolvers and return an answer set ONLY when at
    least two INDEPENDENT vantages agree on exactly that set.

    A044: track reachable-but-empty vantages as evidence. If a resolver responds
    successfully but returns zero records for a type that should exist, that is
    NOT the same as 'no answer' — it may be censorship. Empty results are tracked
    separately so callers can distinguish 'all resolvers agree on nothing' from
    'some resolvers returned something and some were censored'."""
    servers = list(config.PUBLIC_RESOLVERS[:3])
    if len(servers) < 2:
        return set(), set()  # (answers, empty_vantages)

    def query_one(server):
        clean = resolver.query(domain, qtype, server)
        answers = frozenset(str(r["value"]).lower() for r in clean["records"] if r["type"] == qtype_str)
        return answers, clean.get("rcode", 0)

    pool = concurrent.futures.ThreadPoolExecutor(max_workers=len(servers))
    futures = [pool.submit(query_one, srv) for srv in servers]
    counts = {}
    empty_reachable = 0
    try:
        for fut in concurrent.futures.as_completed(futures, timeout=_CLEAN_VANTAGE_DEADLINE):
            try:
                answers, rcode = fut.result()
            except resolver.DNSError:
                continue
            if answers:
                counts[answers] = counts.get(answers, 0) + 1
                if counts[answers] >= 2:
                    return set(answers), set()
            elif rcode == 0:
                # A044: NOERROR with zero records = reachable but empty (possible censorship)
                empty_reachable += 1
    except concurrent.futures.TimeoutError:
        pass
    finally:
        pool.shutdown(wait=False, cancel_futures=True)
    for answer_set, seen in counts.items():
        if seen >= 2:
            return set(answer_set), set()
    # Return the empty vantage count so callers can reason about censorship
    return set(), set(range(empty_reachable))
```

**Tests:**

```python
def test_clean_vantage_two_agree():
    """Happy path: 2 of 3 resolvers agree on the same answer."""
    # Mock resolver.query to return matching records for 2 servers
    # Assert the function returns the agreed set

def test_clean_vantage_empty_vantages_tracked():
    """A044: empty-but-reachable vantages must be counted."""
    # Mock 2 resolvers returning NOERROR with zero records
    # Assert the empty set is returned (not the single real answer)
```

**Ripple effects:**
- The return signature changes from `set()` to `(set(), set())`.
- All callers of `_clean_vantage_answers()` must be updated.
- The `_route_diff()` caller at line 1202 must destructure the new return value.

---

## Cluster 4: Content Diff & Script Extraction <a name="cluster-4"></a>

**Deltas:** A048, A093
**Priority:** High
**Files:** `Server/syxx_server/warp.py`

### A048: Warp Baseline Decode Silently Strips Replacement Characters

**Location:** `Server/syxx_server/warp.py:108`

**Current state:** Line 108: `html = html_bytes.decode("utf-8", errors="ignore")`. The `errors="ignore"` silently drops any bytes that are not valid UTF-8. If the page contains binary content or encoding errors, those bytes disappear from the baseline, causing the diff to miss injected content that happens to follow a malformed byte.

**Code changes:**

1. **`warp.py:108`** — Use `errors="replace"` to preserve data integrity:

```python
    # A048: use "replace" instead of "ignore" so malformed bytes are preserved
    # as the Unicode replacement character (U+FFFD). This keeps the baseline
    # byte-count honest: content after a bad byte is not silently shifted left,
    # which would cause spurious hash mismatches in the diff.
    html = html_bytes.decode("utf-8", errors="replace")
```

**Tests:**

```python
def test_build_baseline_preserves_malformed_utf8():
    """A048: a baseline built from content with invalid UTF-8 must preserve the
    replacement characters, not silently strip them."""
    # Construct HTML bytes with invalid UTF-8 sequences
    # Mock fetcher.fetch to return these bytes
    # Assert the baseline HTML contains U+FFFD replacement characters
    # Assert the baseline does NOT silently truncate
```

**Ripple effects:**
- Affects baseline building and diff accuracy.
- Client-side decoding must use `errors="replace"` for parity (cross-platform A094).
- The `_diff_content()` in `app.py` compares local vs clean scripts; encoding parity is critical.

---

### A093: Server Warp Script Tag Regex Allows Bypass

**Location:** `Server/syxx_server/warp.py:17-18`

**Current state:** Line 17: `_SCRIPT_TAG = re.compile(r"<script\b[^>]*>", re.IGNORECASE)`. This regex matches `<script` followed by whitespace and attributes until `>`. The bypass: `><script` — if the HTML has `><script src="evil.js"></script>`, the first `>` closes the previous tag, and the regex only matches from `<script` onward. However, this is actually correct behavior for HTML parsing: `><script` IS a separate tag, and the regex correctly finds `<script`. The real bypass would be something like `<scr<script ipt>` (nested) or `<script\nsrc="evil.js">` (newline in tag).

After careful analysis: the regex `r"<script\b[^>]*>"` correctly matches `<script` + word boundary + any non-`>` chars + `>`. The bypass `><script` actually works correctly because `><script` is parsed as `>` (closing previous tag) + `<script` (new tag). The regex finds `<script` and works.

**Actual vulnerability:** `<script/src="evil.js">` — the `/` is NOT whitespace after `script`, so `\b` fails. But `\b` is a zero-width assertion between `t` and `/`, and `\b` matches between a word char and a non-word char. So `<script/src>` would actually match because `\b` succeeds between `t` and `/`.

After re-analysis: the regex IS correct for the common bypass cases. The real concern is edge cases like:
- `<SCRIPT>` with uppercase (handled by `re.IGNORECASE`)
- `<script >` with space before `>` (handled by `[^>]*`)
- `<script\nsrc="x">` with newline (handled by `[^>]*` since newline is not `>`)

**Code changes:** Minimal — improve the regex to handle a few more edge cases:

```python
# A093: tighten the script tag regex to catch edge cases:
# - <script> (basic) ✓
# - <script src="x"> (with src) ✓
# - <script\nsrc="x"> (newline in tag) ✓
# - <script > (space before close) ✓
# - <script/type="module"> (slash without space — word boundary fails for some parsers)
_SCRIPT_TAG = re.compile(r"<script[\s/>]", re.IGNORECASE)
```

Wait — this changes semantics. `<script/>` self-closing is invalid HTML but present in some server-generated content. Let me reconsider.

**Revised code changes:**

```python
# A093: the original regex <script\b[^>]*> is functionally correct for the
# common bypass cases. The edge case is <script/type="module"> where a slash
# (not whitespace) follows "script" — some HTML parsers treat this as a valid
# tag opener. Use a lookahead that accepts word-boundary OR non-alphanumeric:
_SCRIPT_TAG = re.compile(r"<script(?:\b|(?=[^a-zA-Z]))[^>]*>", re.IGNORECASE)
```

Actually, this is overengineered. The simplest robust fix:

```python
# A093: match <script followed by any non-alphanumeric (space, /, >, etc.)
# to catch edge cases where \b alone misses <script/type=...>
_SCRIPT_TAG = re.compile(r"<script(?=[^a-zA-Z])[^>]*>", re.IGNORECASE)
```

This matches `<script ` (space), `<script>`, `<script/`, `<script\n`, etc.

**Tests:**

```python
def test_script_tag_regex_basic():
    """A093: basic <script src="x"> must be found."""
    assert len(warp.parse_assets('<script src="a.js"></script>', 'http://x')[0]) == 1

def test_script_tag_regex_uppercase():
    """A093: <SCRIPT> must be found."""
    assert len(warp.parse_assets('<SCRIPT SRC="a.js"></SCRIPT>', 'http://x')[0]) == 1

def test_script_tag_regex_slash_no_space():
    """A093: <script/type="module"> must be found."""
    assert len(warp.parse_assets('<script/type="module" src="a.js"></script>', 'http://x')[0]) == 1

def test_script_tag_regex_self_closing():
    """A093: <script src="a.js" /> must be found."""
    assert len(warp.parse_assets('<script src="a.js" />', 'http://x')[0]) == 1

def test_script_tag_regex_not_in_comment():
    """A093: script inside HTML comment must NOT be found."""
    assert len(warp.parse_assets('<!-- <script src="a.js"></script> -->', 'http://x')[0]) == 0
```

**Ripple effects:**
- Affects script extraction and diff accuracy.
- Must maintain parity with client-side script extraction.
- A094 cross-lane parity item (comment stripping must be byte-identical).

---

## Cluster 5: Stripe Fulfillment <a name="cluster-5"></a>

**Deltas:** A025
**Priority:** Critical (payments/funds)
**Files:** `Server/syxx_server/fulfillment.py`, `Server/syxx_server/db.py`

### A025: Stripe Customer→License Single-Slot Overwrite — Lifetime Buyer Lockout

**Location:** `Server/syxx_server/fulfillment.py:122,160-164`

**Current state:**
- Line 122: `db.kv_set("stripe_cust:" + str(obj["customer"]), license_id)` — single slot per customer
- Lines 160-164: subscription deleted event reads `stripe_cust:<customer>` and suspends whatever it points to

If a customer buys a monthly subscription (mints license A), then buys a yearly subscription (mints license B), the `stripe_cust:` slot now points to B. If the monthly subscription is cancelled, the system suspends B instead of A.

**Code changes:**

1. **`fulfillment.py:120-122`** — Key by subscription ID, not customer:

```python
        # A025: key by subscription_id (per-subscription mapping) instead of
        # customer (single-slot overwrite). One customer may hold multiple
        # subscriptions; cancelling one must not suspend the other.
        sub_id = obj.get("subscription")
        if license_id and sub_id:
            db.kv_set("stripe_sub:" + str(sub_id), license_id)
        # Backward compat: also store the customer mapping for one-time purchases
        # (which have no subscription_id). These are never suspended by subscription
        # deletion events because the subscription event carries the sub_id, not the
        # customer id, for the lookup.
        if license_id and obj.get("customer") and not sub_id:
            db.kv_set("stripe_cust:" + str(obj["customer"]), license_id)
```

2. **`fulfillment.py:158-164`** — Suspend by subscription ID:

```python
        suspended = False
        if etype == "customer.subscription.deleted":
            sub_id = obj.get("id")  # the subscription object's id
            customer = obj.get("customer")
            # A025: look up by subscription_id first (new path)
            license_id = db.kv_get("stripe_sub:" + str(sub_id)) if sub_id else None
            # Fallback: check the customer slot for legacy one-time purchases
            if not license_id and customer:
                license_id = db.kv_get("stripe_cust:" + str(customer))
            if license_id:
                suspended = _keygen_suspend_license(license_id)
```

3. **`db.py`** — No schema change needed; the KV store already supports arbitrary key patterns.

**Tests:**

```python
def test_subscription_delete_suspends_correct_license():
    """A025: cancelling subscription B must not suspend license A."""
    # Mint license for sub_id "sub_abc"
    db.kv_set("stripe_sub:sub_abc", "lic_abc")
    # Mint license for sub_id "sub_def"
    db.kv_set("stripe_sub:sub_def", "lic_def")
    # Cancel sub_abc — should suspend lic_abc, NOT lic_def
    # Mock _keygen_suspend_license to capture the license_id
    # Assert lic_abc was suspended

def test_one_time_purchase_no_subscription():
    """A025: one-time purchase maps customer to license directly."""
    db.kv_set("stripe_cust:cust_123", "lic_one_time")
    # subscription.deleted for a different sub must not suspend the one-time license
```

**Ripple effects:**
- All Stripe webhook handlers in `fulfillment.py`.
- Must handle both subscription-based and one-time purchase flows.
- `kv_set` and `kv_get` are already general-purpose.

---

## Cluster 6: API Security & Rate Limiting <a name="cluster-6"></a>

**Deltas:** A035, A099, A106, A107
**Priority:** High
**Files:** `Server/syxx_server/app.py`, `Server/syxx_server/mailer.py`, `Server/syxx_server/config.py`

### A035: Server /api/checkout Public Without Auth

**Location:** `Server/syxx_server/app.py:1368-1386` (now at line 792-821 in current code)

**Current state:** `_route_checkout()` is rate-limited via `_rate_ok(None)` at line 803 but has no authentication. It mints a fresh Stripe session per request. An attacker can flood this endpoint with requests, creating thousands of abandoned Stripe sessions.

**Code changes:**

1. **`app.py:792-821`** — Add stricter rate limiting for checkout:

```python
    def _route_checkout(self, query):
        """Start a Stripe Managed-Payments checkout. PUBLIC (this IS the purchase flow).
        A035: rate-limited aggressively per IP to prevent session flooding."""
        if not self._rate_ok(None):
            return
        # A035: strict per-IP checkout rate limit — 5 checkouts per hour per IP.
        # This is much tighter than the general rate limit to prevent Stripe session
        # flooding, which costs real money (Stripe charges per session).
        client_ip = self._client_ip()
        if not ratelimit.allow("checkout:" + client_ip, capacity=5, refill_millis=3600000):
            self._json_error(429, "too many checkout attempts; try again later")
            return
        # ... rest of existing code ...
```

**Tests:**

```python
def test_checkout_rate_limited():
    """A035: excessive checkout requests from the same IP must be rate limited."""
    # Send 6 rapid checkout requests
    # Assert the 6th returns 429
```

**Ripple effects:**
- Affects Stripe integration and user experience.
- Must balance security with legitimate checkout flow.

---

### A099: Server Paid API Endpoints Accept Wildcard CORS

**Location:** `Server/syxx_server/app.py:412-416`

**Current state:** `_cors()` at lines 412-416 sets `Access-Control-Allow-Origin: *` for ALL endpoints, including paid ones. This allows any origin to make cross-origin requests to paid API endpoints.

**Code changes:**

1. **`app.py:412-416`** — Restrict CORS for paid endpoints:

```python
    def _cors(self):
        # A099: paid endpoints restrict CORS to known origins; public endpoints
        # use wildcard. The paid endpoints carry sensitive data (leak results,
        # investigation data, DoH queries) that must not be accessible from
        # arbitrary origins via browser JS.
        paid_paths = {"/api/diff", "/api/investigate", "/api/authoritative",
                      "/api/trace", "/api/baseline", "/api/exposure",
                      "/api/passive-dns", "/api/leak/verify", "/warp",
                      "/fetch_script", "/api/warp/raw", "/dns-query"}
        path = getattr(self, "_request_path", "").split("?")[0].rstrip("/")
        if path in paid_paths:
            # A099: only allow our own origins for paid endpoints
            origin = self.headers.get("Origin", "")
            allowed = {"https://syxx.app", "https://www.syxx.app",
                       "https://leaktest.syxx.app"}
            if origin in allowed:
                self.send_header("Access-Control-Allow-Origin", origin)
            # If origin is not allowed, omit the header (browser blocks CORS)
        else:
            self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers",
                         "Authorization, Content-Type, X-Syxx-License")
```

**Tests:**

```python
def test_paid_endpoint_cors_restricted():
    """A099: paid endpoint must not return wildcard CORS."""
    # Send OPTIONS to /api/diff with Origin: https://evil.com
    # Assert Access-Control-Allow-Origin is NOT "*"

def test_public_endpoint_cors_wildcard():
    """Happy path: public endpoint returns wildcard."""
    # Send OPTIONS to /healthz
    # Assert Access-Control-Allow-Origin is "*"
```

**Ripple effects:**
- Affects all paid API endpoints.
- Must balance security with legitimate cross-origin use (the website uses these endpoints).

---

### A106: Server Mailer Recipient Field Lack of Sanitization

**Location:** `Server/syxx_server/app.py:1654-1655` (beta reserve route); `Server/syxx_server/mailer.py:40-43`

**Current state:** Python's `EmailMessage` class rejects CR/LF in header values, so traditional SMTP header injection is blocked. But the research notes that unvalidated control chars can raise `ValueError`. The current `_EMAIL_RE` at `app.py:124` prevents most abuse.

**Code changes:**

1. **`mailer.py:40-43`** — Wrap header construction in try/except:

```python
    msg = EmailMessage()
    try:
        msg["From"] = config.MAIL_FROM
        msg["To"] = to
        msg["Subject"] = subject
    except ValueError as e:
        logging_util.error("mail_header_invalid", err=str(e))
        return False
```

2. **`app.py:1029`** — Add comma/semicolon rejection after the regex:

```python
    if not _EMAIL_RE.match(email) or len(email) > 254:
        return self._json_error(400, "a valid email address is required")
    # A106: reject addresses containing commas or semicolons, which
    # could split into multiple recipients via email.utils.getaddresses()
    if "," in email or ";" in email:
        return self._json_error(400, "a valid email address is required")
```

**Tests:**

```python
def test_beta_reserve_rejects_comma_email():
    """A106: 'victim@ok.com,attacker.evil.com' must be rejected with 400."""
    # POST to /api/beta/reserve with email containing comma
    # Assert 400 response

def test_mailer_rejects_invalid_recipient():
    """A106: EmailMessage must not raise ValueError on invalid addresses."""
    # Mock mailer.send with a control-character-containing address
    # Assert it returns False, not raises
```

**Ripple effects:**
- Affects all email sending functionality.
- Already debunked as injection, but robustness note remains.

---

### A107: Server Insecure TLS Settings Allowed in Production SMTP Services

**Location:** `Server/syxx_server/mailer.py:52-66; Server/syxx_server/config.py:208`

**Current state:** `config.SMTP_INSECURE_CERTS` defaults to False but exists as an env var escape hatch. If set in production, it disables TLS certificate verification.

**Code changes:**

1. **`config.py`** — Refuse to start when SMTP_INSECURE_CERTS is set in production:

```python
# A107: refuse to run with insecure SMTP certs in production.
# This is a MITM risk that must not be silently accepted.
SMTP_INSECURE_CERTS = os.environ.get("SMTP_INSECURE_CERTS", "").lower() in ("1", "true", "yes")
if SMTP_INSECURE_CERTS and not DEV_MODE:
    import sys
    print("FATAL: SMTP_INSECURE_CERTS is set but SYXX_DEV_MODE is not enabled. "
          "Insecure SMTP is only permitted in development.", file=sys.stderr)
    sys.exit(1)
```

2. **`mailer.py:54-56`** — Log a warning when insecure certs are used:

```python
    if config.SMTP_INSECURE_CERTS:
        logging_util.warn("smtp_insecure_certs",
                          detail="TLS certificate verification disabled — MITM risk")
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
```

**Tests:**

```python
def test_smtp_insecure_requires_dev_mode():
    """A107: SMTP_INSECURE_CERTS without DEV_MODE must fail at import time."""
    # This test verifies the config check
```

**Ripple effects:**
- Affects SMTP configuration and deployment.
- Lower severity from "unconditional production MITM" to "dev-only MITM".

---

## Cluster 7: PII & Privacy <a name="cluster-7"></a>

**Deltas:** A031, A072, A097
**Priority:** High
**Files:** `Server/syxx_server/db.py`, `Server/syxx_server/app.py`

### A031: Server beta_signups Stores PII Without Retention Window

**Location:** `Server/syxx_server/db.py:345-361`

**Current state:** Beta signups store email, IP, and timestamps. The `purge_expired()` function at `db.py:815` purges leak tokens, audit logs, and passive DNS, but does NOT purge beta signups.

**Code changes:**

1. **`config.py`** — Add retention config:

```python
# A031: beta signup PII retention window. After this many days, signup
# records are purged. 0 disables the purge (NOT recommended for production).
BETA_RETENTION_DAYS = int(os.environ.get("SYXX_BETA_RETENTION_DAYS", "90"))
```

2. **`db.py:815-849`** — Add beta purge to `purge_expired()`:

```python
    # A031: purge beta signups past the retention window. PII (email, IP)
    # must not accumulate indefinitely.
    if config.BETA_RETENTION_DAYS > 0:
        conn.execute("DELETE FROM beta_signups WHERE created_at < ?",
                     (now - config.BETA_RETENTION_DAYS * 86400,))
```

3. **`db.py`** — Add migration to add `expires_at` column if not present:

```python
# In _migrate():
    try:
        conn.execute("SELECT expires_at FROM beta_signups LIMIT 1")
    except sqlite3.OperationalError:
        conn.execute("ALTER TABLE beta_signups ADD COLUMN expires_at INTEGER")
        conn.commit()
```

**Tests:**

```python
def test_beta_signups_purged_after_retention():
    """A031: beta signups older than retention window must be purged."""
    # Insert a signup with created_at = now - 100 days
    # Call purge_expired()
    # Assert the signup is deleted
```

**Ripple effects:**
- Affects database schema and maintenance jobs.
- May need migration to add expiration timestamps to existing records.

---

### A072: Server Audit Logs Store Raw IP and Email in Plaintext

**Location:** `Server/syxx_server/db.py:557-565`

**Current state:** `audit()` function stores raw IP and email in plaintext. The `_csv_safe()` function at `app.py:1987` sanitizes for spreadsheet injection but does not anonymize.

**Code changes:**

1. **`db.py`** — Hash IP and email in audit records:

```python
def audit(subject, action, status, ip=None):
    """Write an audit record. A072: IP and email are hashed for privacy."""
    import hashlib
    now = int(time.time())
    # Hash the IP for storage — preserves uniqueness for analysis
    # without exposing the raw address.
    ip_hash = hashlib.sha256(ip.encode()).hexdigest()[:16] if ip else None
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO audit(subject,action,status,ip_hash,ts) VALUES(?,?,?,?,?)",
            (subject, action, status, ip_hash, now),
        )
        conn.commit()
    finally:
        conn.close()
```

2. **`db.py` schema migration** — Add `ip_hash` column:

```python
# In _migrate():
    try:
        conn.execute("SELECT ip_hash FROM audit LIMIT 1")
    except sqlite3.OperationalError:
        conn.execute("ALTER TABLE audit ADD COLUMN ip_hash TEXT")
        conn.commit()
```

**Tests:**

```python
def test_audit_stores_hashed_ip():
    """A072: audit records must store hashed IP, not raw."""
    db.audit("test", "test_action", 200, "192.168.1.1")
    # Query the audit record and verify ip_hash is a hash, not the raw IP
    conn = db.connect()
    row = conn.execute("SELECT ip_hash FROM audit WHERE subject='test'").fetchone()
    assert row["ip_hash"] != "192.168.1.1"
    assert len(row["ip_hash"]) == 16  # truncated SHA-256
```

**Ripple effects:**
- Affects logging infrastructure and compliance.
- May need log migration to anonymize existing data.

---

### A097: Server Returns Egress Resolver IPs in Plaintext

**Location:** `Server/syxx_server/app.py:1597-1604,1733`

**Current state:** The `/api/leak` endpoint returns `resolverIP` in plaintext. After A072, audit logs hash IPs, but API responses still return raw resolver IPs.

**Code changes:**

1. **`app.py:1128-1130`** — The legacy leak endpoint already returns resolver IPs. This is intentional for the leak detection use case (the user NEEDS to see which resolver answered). However, the research suggests not returning the server's own egress resolver IPs in internal error messages.

After review: the resolver IPs returned in leak results are the USER's resolver IPs (what their DNS leaked to), not the server's egress resolvers. This is correct and must stay for the feature to work. The delta may be stale.

**Status:** Verify whether the line references match the current code. If the concern is about the DoH forwarder or error messages exposing server infrastructure, that is a different fix.

---

## Cluster 8: Evidence Integrity <a name="cluster-8"></a>

**Deltas:** A036, A117
**Priority:** High
**Files:** `Server/syxx_server/leak.py`, `Server/syxx_server/passivedns.py`

### A036: Server Leak Attestation Excludes Per-Observation Detail from Signed Payload

**Location:** `Server/syxx_server/leak.py:173-232` (now at lines 226-241)

**Current state:** Lines 226-228:
```python
signed = {k: result[k] for k in
          ("token", "zone", "issuedAt", "leaked",
           "observedResolvers", "verifiedAt")}
```
The signed payload includes `observedResolvers` (the set of unique resolver IPs) but NOT `observations` (the per-observation detail with timestamps and qtypes). The unsigned `observations` field in the response has granular evidence that is not covered by the signature.

**Code changes:**

1. **`leak.py:226-228`** — Include per-observation detail in signed payload:

```python
    # A036: include per-observation detail in the signed payload so the
    # granular evidence (which resolver answered, when, for which qtype)
    # is covered by the signature. The client/verifier can then trust the
    # full observation list, not just the deduplicated resolver set.
    signed = {k: result[k] for k in
              ("token", "zone", "issuedAt", "leaked",
               "observedResolvers", "observations", "verifiedAt")}
```

**Tests:**

```python
def test_leak_attestation_includes_observations():
    """A036: the signed payload must include per-observation detail."""
    # Issue a token, make a lookup, verify
    # Decode the signedPayload and assert it contains "observations"
    signed_payload = base64.b64decode(result["signedPayload"])
    signed_dict = json.loads(signed_payload)
    assert "observations" in signed_dict
    assert len(signed_dict["observations"]) > 0
```

**Ripple effects:**
- Affects evidence signing and verification.
- Backward compatibility: existing verifiers that don't expect `observations` in the payload will still work (they just won't verify the field).
- May need versioned payload format if clients enforce strict schema.

---

### A117: Server `driftSuspected` Bool Conflates "Never Observed" with "No Drift"

**Location:** `Server/syxx_server/passivedns.py:59`

**Current state:** Line 59:
```python
"driftSuspected": bool(established) and bool(recently_new - established),
```
A domain never observed returns `established = set()`, so `bool(set()) = False`, so `driftSuspected = False`. A domain observed for months with no change also returns `driftSuspected = False`. These are different states: "no evidence" vs "evidence of stability".

**Code changes:**

1. **`passivedns.py:55-60`** — Replace bool with tri-state:

```python
    established = {v["value"] for v in values if v["ageDays"] >= 7 and v["staleDays"] <= 30}
    recently_new = {v["value"] for v in values if v["ageDays"] < 1 and v["staleDays"] < 1}
    # A117: tri-state drift assessment. "none" = established history, no new values.
    # "suspected" = new values appeared that are not in the established set.
    # "insufficient-data" = not enough observation history to assess drift.
    if len(values) < 3:
        drift = "insufficient-data"
    elif not established:
        drift = "insufficient-data"
    elif recently_new - established:
        drift = "suspected"
    else:
        drift = "none"
    return {
        "domain": domain.lower(), "type": rtype, "recordCount": len(values), "values": values,
        "established": sorted(established),
        "recentlyNew": sorted(recently_new - established),
        "drift": drift,
        # Backward compat: keep the old field for clients that haven't migrated
        "driftSuspected": drift == "suspected",
    }
```

**Tests:**

```python
def test_drift_never_observed_insufficient_data():
    """A117: a domain never observed must return 'insufficient-data', not False."""
    # Query a domain with no passive DNS records
    result = passivedns.baseline("never-seen.example.com")
    assert result["drift"] == "insufficient-data"
    # The old field must still be False for backward compat
    assert result["driftSuspected"] is False

def test_drift_established_no_new():
    """A117: established domain with no new values = 'none'."""
    # Insert records with ageDays >= 7 and staleDays <= 30
    # Assert drift == "none"

def test_drift_established_with_new():
    """A117: established domain with new value not in established = 'suspected'."""
    # Insert old records and a new record
    # Assert drift == "suspected"
```

**Ripple effects:**
- Affects drift detection and reporting.
- Cross-ref A040 (passivedns baseline/drift logic).
- Client-side consumers must handle the new `drift` field.
- The `driftSuspected` bool is kept for backward compatibility.

---

## Cluster 9: Resource Safety & Work Budgets <a name="cluster-9"></a>

**Deltas:** A028, A032
**Priority:** High
**Files:** `Server/syxx_server/app.py`, `Server/syxx_server/resolver.py`, `Server/syxx_server/db.py`

### A028: Server Authoritative Walks Lack Per-Request Deadline — Global Query Budget

**Location:** `Server/syxx_server/resolver.py:289-290,384-456; Server/syxx_server/app.py:598`

**Current state:** `resolve_authoritative()` runs synchronously in request handler threads with per-query 3s timeouts. No global deadline. A walk with 16 levels x 4 servers x 3s timeout = 192s worst case per request.

**Code changes:**

1. **`resolver.py:677`** — Add a global deadline parameter:

```python
def resolve_authoritative(name, qtype, timeout=3, max_depth=MAX_DELEGATION_DEPTH,
                          deadline=None):
    """Walk root -> TLD -> authoritative for `name`/`qtype`.

    A028: `deadline` is an absolute wall-clock time (time.monotonic()). When set,
    the walk aborts immediately if it exceeds this deadline, returning inconclusive
    rather than consuming unbounded time. This bounds the worst-case per-request
    cost across the entire authoritative walk."""
    import time as _time
    name = (name or "").rstrip(".") or "."
    delegation = []
    servers = [{"ip": ip, "label": ip} for ip in ROOT_SERVERS]
    zone = "."
    for depth in range(max_depth):
        if deadline and _time.monotonic() > deadline:
            delegation.append({"zone": zone, "server": None, "error": "authoritative walk deadline exceeded"})
            return {"name": name, "qtype": qtype, "delegation": delegation,
                    "authoritative": None, "verdict": "inconclusive"}
        # ... rest of existing walk logic ...
```

2. **`app.py:1158-1164`** — Pass deadline from route handler:

```python
    # A028: 8-second global deadline for the authoritative walk.
    import time as _time
    deadline = _time.monotonic() + 8
    try:
        result = resolver.resolve_authoritative(domain, qtypes[qtype_str], deadline=deadline)
    except resolver.DNSError as e:
        return self._json_error(502, "resolution failed: %s" % e)
```

**Tests:**

```python
def test_resolve_authoritative_deadline_aborts():
    """A028: a walk that exceeds the deadline must return inconclusive."""
    # Set deadline to now (immediate expiry)
    result = resolver.resolve_authoritative("example.com", resolver.TYPE_A, deadline=0)
    assert result["verdict"] == "inconclusive"
    assert any(d.get("error", "").startswith("deadline") for d in result["delegation"])
```

**Ripple effects:**
- Affects `resolve_authoritative()` and all routes that call it.
- Must maintain DNS resolution accuracy while adding timeouts.

---

### A032: Server trial_anchors Over-Cap Eviction Deletes Oldest `first_launch`

**Location:** `Server/syxx_server/app.py:269-282`

**Current state:** When trial anchors exceed cap, oldest `first_launch` is deleted. May delete active trial data.

**Code changes:**

1. **`db.py`** — Change `_cap_rows` to evict by least-recently-accessed:

```python
def _cap_rows(conn, table, order_col, max_rows):
    """A032: evict the least-recently-accessed rows (by order_col DESC = newest first
    kept, oldest deleted). If the table has an updated_at or accessed_at column, use that
    instead of created_at for smarter eviction."""
    if max_rows <= 0:
        return
    row = conn.execute("SELECT COUNT(*) c FROM %s" % table).fetchone()
    excess = (row["c"] if row else 0) - max_rows
    if excess > 0:
        conn.execute(
            "DELETE FROM %s WHERE rowid IN "
            "(SELECT rowid FROM %s ORDER BY %s ASC LIMIT ?)" % (table, table, order_col),
            (excess,))
```

Actually, the current implementation already deletes the oldest rows (ASC order). The issue is that "oldest first_launch" may be an active trial. The fix is to NOT evict rows that are within their trial window.

2. **`db.py`** — Add trial-aware eviction:

```python
def _cap_rows_trials(conn, table, order_col, max_rows, now=None):
    """A032: evict trial anchors but preserve active trials (those within the
    trial window). Only evict anchors whose trial has expired."""
    import time as _time
    now = now or int(_time.time())
    trial_window = 30 * 86400  # 30-day trial window
    row = conn.execute("SELECT COUNT(*) c FROM %s" % table).fetchone()
    excess = (row["c"] if row else 0) - max_rows
    if excess <= 0:
        return
    # Only evict expired trial anchors (first_launch older than trial window)
    conn.execute(
        "DELETE FROM %s WHERE rowid IN "
        "(SELECT rowid FROM %s WHERE %s < ? ORDER BY %s ASC LIMIT ?)" %
        (table, table, order_col, order_col),
        (now - trial_window, excess))
```

**Tests:**

```python
def test_trial_anchor_eviction_preserves_active():
    """A032: active trial anchors must not be evicted during cap enforcement."""
    # Insert 100 trial anchors, 50 with recent first_launch, 50 with old
    # Set cap to 60
    # Assert only old anchors are evicted, recent ones preserved
```

**Ripple effects:**
- Affects trial anchor storage and retrieval logic.
- Must verify that the trial window is correctly configured.

---

## Cluster 10: DoH & Resolver Efficacy <a name="cluster-10"></a>

**Deltas:** A124
**Priority:** Medium
**Files:** `Server/syxx_server/app.py`

### A124: Server DoH JSON Path Queries Only `PUBLIC_RESOLVERS[0]`

**Location:** `Server/syxx_server/app.py:1237-1243` (now at lines 1906-1907)

**Current state:** `_doh_respond_json()` at line 1906:
```python
parsed = resolver.query(name, qtype, config.PUBLIC_RESOLVERS[0],
                        timeout=config.DOH_UPSTREAM_TIMEOUT)
```
Only queries the first resolver. If it fails or is poisoned, no fallback.

**Code changes:**

1. **`app.py:1896-1925`** — Try multiple resolvers for DoH JSON:

```python
    def _doh_respond_json(self, query):
        """A124: query multiple resolvers for DoH JSON, return the first successful
        answer. This provides resilience against a single poisoned/failed resolver."""
        name = (query.get("name", [""])[0]).strip()
        type_param = (query.get("type", ["A"])[0]).strip().upper()
        qtype = self._DOH_JSON_TYPES.get(type_param)
        if qtype is None:
            try:
                qtype = int(type_param)
            except ValueError:
                return self._json_error(400, "unknown type %r" % type_param)

        last_error = None
        for server in config.PUBLIC_RESOLVERS[:3]:  # try up to 3 resolvers
            try:
                parsed = resolver.query(name, qtype, server,
                                        timeout=config.DOH_UPSTREAM_TIMEOUT)
                break
            except resolver.DNSError as e:
                last_error = e
                logging_util.warn("doh_json_upstream_failed",
                                  name=name, server=server, err=str(e))
                continue
        else:
            # All resolvers failed
            return self._send(200, {"Status": 2, "Comment": "all upstream resolvers failed"})

        answers = [{"name": name, "type": qtype, "data": r["value"]}
                   for r in parsed.get("records", [])]
        self._send(200, {
            "Status": parsed.get("rcode", 0),
            "TC": parsed.get("truncated", False),
            "RD": True,
            "AD": parsed.get("ad", False),
            "Question": [{"name": name, "type": qtype}],
            "Answer": answers,
        })
```

**Tests:**

```python
def test_doh_json_fallback_to_second_resolver():
    """A124: when the first resolver fails, the second must be tried."""
    # Mock resolver.query to fail for PUBLIC_RESOLVERS[0] and succeed for [1]
    # Assert the response comes from the second resolver
```

**Ripple effects:**
- Affects DoH resolution and accuracy.
- Must verify that the fallback does not introduce timing issues.

---

## Cluster 11: Test Coverage <a name="cluster-11"></a>

**Deltas:** A118
**Priority:** Critical (Protocol §3 violation)
**Files:** `Server/tests/test_routes.py`, `Server/tests/test_paywall.py` (new)

### A118: No Test Asserts 402 on Any Paid Route

**Location:** `Server/tests/test_routes.py:53-54; Server/syxx_server/app.py:363-375`

**Current state:** All tests bypass authentication via `licensing.entitlement_for = lambda headers, query: (ent, None)`. No test exercises the 402 path. Deleting `_require()` entirely leaves the suite green.

**Code changes:**

1. **`tests/test_paywall.py`** (new file) — Test the auth gate on every paid route:

```python
"""Paywall enforcement tests: verify that every paid route returns 402
when no valid entitlement is provided.

A118: Protocol §3 violation — the auth refusal path of 14 paid routes
was completely untested. This module drives the real Handler with NO
credentials and asserts 402 on each paid route.

Run from Server/: python3 -m unittest tests.test_paywall -v
"""
import json
import os
import tempfile
import threading
import unittest
import urllib.error
import urllib.request

_dbfd, _DB_PATH = tempfile.mkstemp(suffix=".db")
os.close(_dbfd)
os.environ.setdefault("STRIPE_SECRET_KEY", "sk_test_fake")
os.environ.setdefault("SYXX_ALLOW_EPHEMERAL_COLLECTOR_KEY", "1")
os.environ.setdefault("SYXX_DB_PATH", _DB_PATH)
os.environ.setdefault("SYXX_LEAK_ZONES", "leak.syxx.app")

from syxx_server import app, db, licensing

db.init()

_subject = "paywall-test-device"
_httpd = None
_port = None


def setUpModule():
    global _httpd, _port
    # DO NOT set entitlement_for — leave it as the real implementation
    # so unauthenticated requests get 402.
    _httpd = app.ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
    _port = _httpd.server_address[1]
    threading.Thread(target=_httpd.serve_forever, daemon=True).start()


def tearDownModule():
    if _httpd:
        _httpd.shutdown()


def _get(path):
    """GET a path with no auth headers."""
    req = urllib.request.Request("http://127.0.0.1:%d%s" % (_port, path))
    try:
        resp = urllib.request.urlopen(req)
        return resp.getcode(), json.loads(resp.read())
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read())


def _post_json(path, body):
    """POST JSON to a path with no auth headers."""
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request("http://127.0.0.1:%d%s" % (_port, path),
                                data=data, method="POST")
    req.add_header("Content-Type", "application/json")
    try:
        resp = urllib.request.urlopen(req)
        return resp.getcode(), json.loads(resp.read())
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read())


class PaywallTests(unittest.TestCase):
    """A118: every paid route must return 402 without a valid entitlement."""

    def test_warp_no_auth(self):
        code, body = _get("/warp?url=https://example.com")
        self.assertEqual(code, 402)

    def test_fetch_script_no_auth(self):
        code, body = _get("/fetch_script?url=https://example.com/a.js")
        self.assertEqual(code, 402)

    def test_diff_no_auth(self):
        code, body = _post_json("/api/diff?domain=example.com", {
            "domain": "example.com", "localAnswers": ["93.184.216.34"]
        })
        self.assertEqual(code, 402)

    def test_investigate_no_auth(self):
        code, body = _get("/api/investigate?domain=example.com")
        self.assertEqual(code, 402)

    def test_authoritative_no_auth(self):
        code, body = _get("/api/authoritative?domain=example.com")
        self.assertEqual(code, 402)

    def test_trace_no_auth(self):
        code, body = _get("/api/trace?domain=example.com")
        self.assertEqual(code, 402)

    def test_baseline_no_auth(self):
        code, body = _get("/api/baseline?domain=example.com")
        self.assertEqual(code, 402)

    def test_exposure_no_auth(self):
        code, body = _get("/api/exposure")
        self.assertEqual(code, 402)

    def test_passive_dns_no_auth(self):
        code, body = _get("/api/passive-dns?domain=example.com")
        self.assertEqual(code, 402)

    def test_leak_verify_no_auth(self):
        code, body = _get("/api/leak/verify?token=fake.token")
        self.assertEqual(code, 402)

    def test_leak_token_no_auth(self):
        code, body = _get("/api/leak/token?zone=leak.syxx.app")
        self.assertEqual(code, 402)

    def test_doh_wire_no_auth(self):
        code, body = _get("/dns-query?dns=aa")
        self.assertEqual(code, 402)

    def test_warp_raw_no_auth(self):
        code, body = _get("/api/warp/raw?url=https://example.com")
        self.assertEqual(code, 402)

    def test_bulk_investigate_no_auth(self):
        code, body = _post_json("/api/bulk-investigate", {"domains": ["example.com"]})
        self.assertEqual(code, 402)

    def test_public_routes_still_work(self):
        """Sanity: public routes must still return 200."""
        code, body = _get("/healthz")
        self.assertEqual(code, 200)
        self.assertTrue(body.get("ok"))

    def test_mutation_proves_gate(self):
        """A118 Fix 2: stub _require to always allow, confirm the paywall tests go red."""
        # This test verifies that if we bypass auth, the routes succeed
        original = licensing.entitlement_for
        ent = licensing.Entitlement(_subject, "pro", "session")
        licensing.entitlement_for = lambda h, q: (ent, None)
        try:
            code, body = _get("/api/investigate?domain=example.com")
            # Should succeed (200 or 400 for invalid domain) — NOT 402
            self.assertIn(code, (200, 400, 502))
        finally:
            licensing.entitlement_for = original
```

**Ripple effects:**
- Confirms the auth gate works on all 14 paid routes.
- Mutation test proves the gate is real (not just default behavior).

---

## Dependency Graph & Build Order <a name="dependency-graph"></a>

```
Cluster 1 (SSRF/Parsing)  ─── no deps ──── land first
    │
    ├── A055 (SSRF) ─────────────────────────────┐
    ├── A077 (rdlength) ─────────────────────────┤
    └── A115 (malformed A) ──────────────────────┘
                                               │
Cluster 2 (DNSSEC) ─── depends on Cluster 1 ──┘
    │
    ├── A042 (inception) ─── already fixed, verify only
    ├── A043 (typeCovered) ── already fixed, verify only
    └── A041 (multi-vantage) ─── new code
         │
Cluster 3 (Consensus) ─── depends on Cluster 1 ──┐
    │                                              │
    ├── A045 (SERVFAIL mapping) ──────────────────┤
    ├── A026 (injected verdict) ──────────────────┤
    └── A044 (empty vantages) ────────────────────┘
                                                  │
Cluster 4 (Content Diff) ─── independent ────────┤
    │                                              │
    ├── A048 (UTF-8 decode) ─────────────────────┤
    └── A093 (script regex) ─────────────────────┘
                                                  │
Cluster 5 (Stripe) ─── independent ──────────────┤
    └── A025 (subscription mapping) ─────────────┤
                                                  │
Cluster 6 (API Security) ─── independent ────────┤
    ├── A035 (checkout rate limit) ──────────────┤
    ├── A099 (CORS) ────────────────────────────┤
    ├── A106 (email sanitization) ──────────────┤
    └── A107 (SMTP TLS) ────────────────────────┘
                                                  │
Cluster 7 (PII) ─── independent ────────────────┤
    ├── A031 (beta retention) ──────────────────┤
    ├── A072 (audit hashing) ───────────────────┤
    └── A097 (resolver IPs) ────────────────────┤
                                                  │
Cluster 8 (Evidence) ─── independent ───────────┤
    ├── A036 (leak attestation) ────────────────┤
    └── A117 (drift tri-state) ─────────────────┤
                                                  │
Cluster 9 (Resources) ─── depends on Cluster 1 ─┤
    ├── A028 (walk deadline) ───────────────────┤
    └── A032 (trial eviction) ──────────────────┤
                                                  │
Cluster 10 (DoH) ─── independent ───────────────┤
    └── A124 (multi-resolver DoH) ──────────────┤
                                                  │
Cluster 11 (Tests) ─── depends on all others ───┘
    └── A118 (paywall tests) ─── land LAST
```

**Recommended merge order:**
1. Clusters 1 (SSRF/Parsing) — Critical security
2. Cluster 5 (Stripe) — Critical payments
3. Clusters 4, 6, 7, 8 — Independent, can parallelize
4. Clusters 2, 3, 9 — Depend on Cluster 1
5. Cluster 10 — Independent
6. Cluster 11 — Land after all route changes

---

## Full Test Strategy <a name="test-strategy"></a>

### Pre-fix verification (fail before fix)

For each delta, verify the test FAILS before the fix is applied:

```bash
cd Server/
python3 -m pytest tests/ -v --tb=short 2>&1 | tee pre-fix-results.txt
```

### Per-cluster test commands

```bash
# Cluster 1: SSRF/Parsing
python3 -m pytest tests/test_resolver_fetcher_ssrf.py -v -k "ipv4_mapped"
python3 -m pytest tests/test_resolver.py -v -k "rdlength or format_rdata"

# Cluster 2: DNSSEC
python3 -m pytest tests/test_resolver.py -v -k "rrsig or dnssec"

# Cluster 3: Consensus
python3 -m pytest tests/test_diff.py -v -k "injected or verdict"

# Cluster 4: Content Diff
python3 -m pytest tests/test_warp_raw.py -v -k "script_tag or utf8"

# Cluster 5: Stripe
python3 -m pytest tests/test_payments.py -v -k "subscription"

# Cluster 6: API Security
python3 -m pytest tests/test_routes.py -v -k "checkout or cors"

# Cluster 7: PII
python3 -m pytest tests/test_routes.py -v -k "beta or audit"

# Cluster 8: Evidence
python3 -m pytest tests/test_leak.py -v -k "attestation or drift"

# Cluster 9: Resources
python3 -m pytest tests/test_resolver.py -v -k "deadline"

# Cluster 10: DoH
python3 -m pytest tests/test_doh.py -v

# Cluster 11: Paywall
python3 -m pytest tests/test_paywall.py -v
```

### Post-fix verification

```bash
python3 -m pytest tests/ -v --tb=short 2>&1 | tee post-fix-results.txt
```

### Real-run verification

After all clusters land, run a real server and test each endpoint manually:

```bash
cd Server/
python3 -m syxx_server &
sleep 2

# Test public endpoint
curl http://localhost:8080/healthz

# Test paid endpoint without auth (should get 402)
curl http://localhost:8080/api/investigate?domain=example.com

# Test DoH
curl "http://localhost:8080/dns-query?name=example.com&type=A"

# Test checkout rate limiting
for i in $(seq 1 10); do
  curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:8080/api/checkout?sku=test"
done
# Should see 429 after 5 requests

kill %1
```

---

## Ripple Effects Matrix <a name="ripple-effects-matrix"></a>

| Delta | Affected Components | Cross-Platform Parity | Client Update Needed |
|-------|-------------------|----------------------|---------------------|
| A055 | All SSRF protection in resolver + fetcher | No (server-only) | No |
| A077 | All DNS packet parsing | No (server-only) | No |
| A115 | passive_dns, /api/diff, investigation | No (server-only) | No |
| A041 | DNSSEC validation, /api/dnssec | No (server-only) | No |
| A042 | DNSSEC validation | No (server-only) | No |
| A043 | DNSSEC validation | No (server-only) | No |
| A026 | /api/diff verdict, synthesis engine | No (server-only) | Possible — client reads verdict |
| A044 | _clean_vantage_answers, /api/diff | No (server-only) | Possible — client reads answer sets |
| A045 | /api/diff, /api/authoritative | No (server-only) | Yes — client must handle "server-error" |
| A048 | warp baseline, diff accuracy | Yes — client must use errors="replace" | Yes — encoding parity |
| A093 | Script extraction, diff accuracy | Yes — client must match regex | Yes — A094 cross-lane parity |
| A025 | Stripe webhooks, fulfillment | No (server-only) | No |
| A035 | Checkout flow | No (server-only) | No |
| A099 | All paid API CORS | No (server-only) | No |
| A106 | Email sending | No (server-only) | No |
| A107 | SMTP configuration | No (server-only) | No |
| A031 | Beta signup data | No (server-only) | No |
| A072 | Audit logging | No (server-only) | No |
| A097 | API responses | No (server-only) | No |
| A036 | Leak attestation | No (server-only) | No — but verifier must handle new field |
| A117 | Drift reporting | No (server-only) | Yes — client reads drift field |
| A028 | Authoritative walks | No (server-only) | No |
| A032 | Trial anchor storage | No (server-only) | No |
| A124 | DoH JSON path | No (server-only) | No |
| A118 | Test coverage | No (server-only) | No |

---

## Summary of Changes by File

| File | Deltas | Lines Changed (est.) |
|------|--------|---------------------|
| `resolver.py` | A055, A077, A115, A041, A042, A043, A028 | ~120 |
| `app.py` | A026, A044, A045, A035, A099, A106, A028, A032, A124 | ~150 |
| `warp.py` | A048, A093 | ~15 |
| `fulfillment.py` | A025 | ~20 |
| `leak.py` | A036 | ~5 |
| `passivedns.py` | A117 | ~25 |
| `db.py` | A031, A072 | ~30 |
| `mailer.py` | A106, A107 | ~10 |
| `config.py` | A031, A107 | ~10 |
| `tests/test_paywall.py` | A118 | ~150 (new file) |
| `tests/test_*.py` (various) | All | ~200 (new tests) |
| **Total** | **24 deltas** | **~735 lines** |

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| A044 return signature change breaks callers | Update all callers in same commit |
| A026 verdict change affects client synthesis | Test with real client before merge |
| A025 KV schema migration conflicts with A013 | Sequential merge, test both paths |
| A117 drift field backward compat | Keep `driftSuspected` bool alongside new `drift` field |
| A048 encoding change shifts hash comparison | Test with binary-heavy HTML content |
| A093 regex change affects client parity | A094 cross-lane parity item — coordinate |
| A028 deadline too aggressive for long walks | 8-second default is conservative; tune after real-world data |
| A118 paywall test depends on real licensing module | Test uses real Handler, not mocked auth |
