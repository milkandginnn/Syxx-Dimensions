# Syxx Server — Implementation Plan

**Date:** 2026-07-27
**Source:** 111 deltas in `SERVER_DELTAS.md` (SD-01 through SD-111, minus 2 rejected)
**Status:** Research complete. Ready for implementation.

---

## Triage Summary

Of the 111 catalogued deltas:

| Status | Count | Deltas |
|--------|-------|--------|
| **Already fixed** | 12 | SD-01, SD-02, SD-03, SD-11, SD-16, SD-17, SD-25, SD-40, SD-51, SD-52, SD-57, SD-65 (rejected) |
| **By design** | 1 | SD-79 (rejected) |
| **Needs implementation** | 98 | All remaining |
| **Partially fixed** | 3 | SD-15, SD-53, SD-56 |

**Net implementation scope: 98 deltas across 9 phases.**

---

## Phase 1 — Foundation (shared utilities + infrastructure prerequisites)

These modules are prerequisites for multiple later phases. Build and test them first.

### 1.1 Create `syxx_server/netutil.py` — Shared IP validation

**Deltas resolved:** SD-07, SD-04 (as side effect)
**Enables:** SD-67, SD-68

**What:** Consolidate the two `_ip_is_public()` implementations into one hardened shared module.

```python
# syxx_server/netutil.py
"""Shared network utility functions — IP validation, SSRF guards.

The hardened version from fetcher.py (which handles IPv4-mapped IPv6)
is canonical. resolver.py's version is missing IPv4-mapped IPv6 handling (SD-07).
"""
import ipaddress

def ip_is_public(ip):
    """True only for publicly-routable unicast IPs.
    
    Handles IPv4-mapped IPv6 (::ffff:127.0.0.1 → normalize to v4 then check),
    NAT64 prefix (64:ff9b::/96), CGNAT (100.64.0.0/10), and all standard
    private/loopback/link-local/multicast/reserved ranges.
    """
    try:
        addr = ipaddress.ip_address(ip)
    except ValueError:
        return False
    # IPv4-mapped IPv6: normalize to v4 before checking
    if addr.version == 6 and addr.ipv4_mapped:
        addr = addr.ipv4_mapped
    # NAT64 well-known prefix
    if addr.version == 6 and addr in ipaddress.ip_network("64:ff9b::/96"):
        return False
    if (addr.is_private or addr.is_loopback or addr.is_link_local
            or addr.is_multicast or addr.is_reserved or addr.is_unspecified):
        return False
    # CGNAT (RFC 6598)
    if addr.version == 4 and addr in ipaddress.ip_network("100.64.0.0/10"):
        return False
    return True
```

**Files changed:**
| File | Change |
|------|--------|
| `syxx_server/netutil.py` | **NEW** — shared `ip_is_public()` |
| `syxx_server/resolver.py` | Replace `_ip_is_public()` with `from .netutil import ip_is_public` |
| `syxx_server/fetcher.py` | Replace `_ip_is_public()` with `from .netutil import ip_is_public`; keep `FETCH_ALLOW_PRIVATE` escape hatch as a wrapper |
| `syxx_server/firewall_test.py` | Replace local `_ip_is_public()` with import |

**Tests:** Add `tests/test_netutil.py` with IPv4-mapped IPv6 bypass cases, NAT64, CGNAT, all standard ranges.

---

### 1.2 Create `syxx_server/deadline.py` — Shared deadline utility

**Deltas resolved:** Foundation for SD-109, SD-78, SD-82/83
**Enables:** Phase 4 (thread-exhaustion fixes)

**What:** A shared `run_with_deadline()` function that all long-running modules use.

```python
# syxx_server/deadline.py
"""Bounded concurrent execution with wall-clock deadline.

Shared utility for all modules that need to run N tasks in parallel
with an overall time budget (subdomain_enum, firewall_test, lookalike).
"""
import concurrent.futures
import time

def run_with_deadline(items, fn, *, deadline, max_workers=10, default_result=None):
    """Execute fn(item) for each item using a bounded thread pool,
    honouring an absolute wall-clock deadline (monotonic timestamp).

    Returns (results, timed_out) where:
    - results: list of (item, result_or_exception) for completed tasks
    - timed_out: True if any tasks were cancelled due to deadline
    """
    results = []
    timed_out = False

    with concurrent.futures.ThreadPoolExecutor(max_workers=max_workers) as pool:
        future_map = {pool.submit(fn, item): item for item in items}
        remaining = max(deadline - time.monotonic(), 0.01)

        try:
            for fut in concurrent.futures.as_completed(future_map, timeout=remaining):
                item = future_map[fut]
                try:
                    results.append((item, fut.result()))
                except Exception as exc:
                    results.append((item, exc))
                if time.monotonic() >= deadline:
                    break
        except concurrent.futures.TimeoutError:
            timed_out = True

        # Cancel pending futures
        for f in future_map:
            if not f.done():
                f.cancel()
                timed_out = True
                results.append((future_map[f], default_result))

    return results, timed_out
```

**Files changed:**
| File | Change |
|------|--------|
| `syxx_server/deadline.py` | **NEW** |

**Tests:** Add `tests/test_deadline.py` — happy path, deadline hit, exception in task, empty input.

---

### 1.3 Add `config.py:validate()` — Configuration validation

**Deltas resolved:** SD-59, SD-64
**Files:** `syxx_server/config.py`, `server.py`

**What:** Add a `validate()` function that warns about contradictory or silently-fallen-back config at startup. Change `HTTP_HOST` default from `0.0.0.0` to `127.0.0.1`.

**Changes:**
- `config.py`: Change `HTTP_HOST = _s("SYXX_HTTP_HOST", "127.0.0.1")` (was `"0.0.0.0"`)
- `config.py`: Add `validate()` returning list of warning strings
- `server.py`: Call `config.validate()` after `db.init()`, log warnings
- `scripts/deploy.sh`: Add `Environment=SYXX_HTTP_HOST=127.0.0.1` to the systemd unit (explicit is better than relying on defaults)

**Tests:** Unit test `validate()` with contradictory TLS_CERT/TLS_KEY, bad port string, public bind.

---

## Phase 2 — High-Severity Security Fixes

### 2.1 RDAP redirect SSRF + response size caps

**Deltas resolved:** SD-67, SD-68, SD-80
**Files:** `syxx_server/whois_lookup.py`
**Depends on:** Phase 1.1 (netutil.py)

**Changes:**

**SD-67** — In `_rdap_query()`, after parsing redirect URL, resolve hostname and validate IP:
```python
redir = urllib.parse.urlparse(location)
if redir.scheme != "https" or not redir.hostname:
    return None
try:
    for info in socket.getaddrinfo(redir.hostname, None, proto=socket.IPPROTO_TCP):
        if not netutil.ip_is_public(info[4][0]):
            return None
except socket.gaierror:
    return None
```

**SD-68** — In `_whois_fallback()`, add size cap:
```python
MAX_WHOIS_RESPONSE = 64 * 1024  # 64 KiB
sock.settimeout(timeout)
total = 0
while total < MAX_WHOIS_RESPONSE:
    data = sock.recv(4096)
    if not data: break
    chunks.append(data)
    total += len(data)
```

Also cap RDAP response: `body = resp.read(1 << 20 + 1)` with size check.

**SD-80** — Reject non-HTTPS bootstrap URLs:
```python
if not base_url.startswith("https://"):
    return None
```

**Tests:** `tests/test_whois.py` — mock RDAP redirect to 169.254.169.254 (must reject), mock WHOIS server sending 128KB (must truncate/reject), mock bootstrap with http:// URL (must reject).

---

### 2.2 DoT query privacy leak

**Deltas resolved:** SD-66
**Files:** `syxx_server/dot_listener.py`

**Change:** In `_Handler._answer()`, pass a sentinel instead of raw client IP for non-allowlisted DoT queries:
```python
if leak_hit is not None:
    # DoT clients chose encrypted transport for privacy — do not store their raw IP.
    leak.observe(leak_hit[0], "dot:encrypted", leak_hit[1])
```

**Tests:** Extend `tests/test_leak.py` — verify DoT path never stores a real IP address.

---

### 2.3 Audit log PII hashing

**Deltas resolved:** SD-14
**Files:** `syxx_server/app.py`, `syxx_server/db.py`

**Change:** Hash the client IP before passing to `db.audit()`. Add a helper:
```python
def _client_ip_hash(self):
    """SHA-256 truncated to 16 hex chars — preserves correlation, removes PII."""
    import hashlib
    return hashlib.sha256(self._client_ip().encode()).hexdigest()[:16]
```

Replace all `db.audit(..., self._client_ip())` calls with `db.audit(..., self._client_ip_hash())`.

**Tests:** `tests/test_privacy.py` — grep audit log output for raw IP patterns, verify none found.

---

### 2.4 Beta signups PII + retention

**Deltas resolved:** SD-54
**Files:** `syxx_server/db.py`
**Requires:** Schema migration

**Changes:**
1. Hash email before storing: `email_hash = hashlib.sha256(email.encode()).hexdigest()`
2. Drop IP from INSERT (store `None`)
3. Add `beta_signups` purge to `purge_expired()` with 180-day retention
4. Update `beta_all()` to return hashes instead of raw emails
5. Update `beta_reserve()` to hash the email before INSERT

**Migration:** The `email` column is the PRIMARY KEY. Re-hash existing rows in a one-time migration:
```sql
-- One-time migration: hash existing emails
UPDATE beta_signups SET email = lower(hex(sha256(email))), ip = NULL;
```
Note: SQLite lacks `sha256()`. Use Python to read all rows, hash emails, and rewrite.

**Tests:** Verify `beta_reserve` stores hash, not raw email. Verify `purge_expired` deletes old signups.

---

### 2.5 Keygen metadata PII removal

**Deltas resolved:** SD-24
**Files:** `syxx_server/fulfillment.py`

**Change:** Remove email from Keygen metadata:
```python
"attributes": {}  # was: {"metadata": {"email": email}} if email else {}
```

**Tests:** Extend `test_payments.py` — assert `_keygen_create_license` is called without email in the payload.

---

### 2.6 Google Fonts self-hosting

**Deltas resolved:** SD-90
**Files:** `index.html`, `leak-test.html`, `leaktest.html`, `purchase-success.html`, `purchase-cancel.html`, `whats-new-mac.html`, `whats-new-win.html`, `docs/api.html`, `docs/help.html`, `assets/` directory

**Changes:**
1. Download WOFF2 files for: Cinzel (500, 600, 700), EB Garamond (400, 500, italic 400, 500), Quicksand, JetBrains Mono, Fira Code
2. Place in `assets/fonts/`
3. Add `@font-face` declarations in each HTML file's `<style>` block
4. Remove `<link rel="preconnect" href="https://fonts.googleapis.com">` and the Google Fonts `<link>` from all 9 HTML files

**Tests:** Grep-based CI check: `grep -r "fonts.googleapis" *.html docs/*.html` must return empty.

---

## Phase 3 — Thread-Exhaustion DoS Fixes

**Depends on:** Phase 1.2 (deadline.py)

### 3.1 Subdomain enumeration deadline

**Deltas resolved:** SD-109
**Files:** `syxx_server/subdomain_enum.py`
**Severity:** Highest — free-tier route, 10-minute pin

**Changes:**
1. Add `import time` and `from .deadline import run_with_deadline`
2. Add `deadline` parameter to `enumerate()` (default: `time.monotonic() + 30`)
3. Replace `_resolve_subdomains()` sequential loop with `run_with_deadline()`:
   ```python
   results, timed_out = run_with_deadline(
       subdomains, _resolve_one,
       deadline=deadline, max_workers=10, default_result=None
   )
   ```
4. Set `truncated = truncated or timed_out`

**Tests:** Mock `resolver.query` to sleep 2s, set deadline to 1s, verify partial results + `truncated=True`.

---

### 3.2 Firewall test deadline + parallelism

**Deltas resolved:** SD-78
**Files:** `syxx_server/firewall_test.py`

**Changes:**
1. Add `import time` and `from .deadline import run_with_deadline`
2. Add `deadline` parameter to `test()` (default: `time.monotonic() + 30`)
3. Parallelize per-domain queries: submit user + 2 clean queries per domain to pool
4. If deadline hit, set `assessment = "inconclusive"`

**Tests:** Mock resolver to sleep, verify deadline respected, partial results returned.

---

### 3.3 Lookalike scan deadline + parallelism

**Deltas resolved:** SD-82, SD-83
**Files:** `syxx_server/lookalike.py`

**Changes:**
1. Add `import time` and `from .deadline import run_with_deadline`
2. Add `deadline` parameter to `scan()` (default: `time.monotonic() + 60`)
3. Replace sequential `_check_resolution` loop with `run_with_deadline()`
4. Add `max_perm = max(1, min(int(max_permutations), 500))` lower-bound guard (closes SD-88 variant)

**Tests:** Mock resolver to sleep, verify deadline respected.

---

### 3.4 Bulk investigate TimeoutError fix

**Deltas resolved:** SD-69
**Files:** `syxx_server/bulk_investigate.py`

**Change:** Wrap `as_completed` loop in try/except:
```python
try:
    for fut in concurrent.futures.as_completed(future_map, timeout=max(deadline - time.monotonic(), 0.01)):
        ...
except concurrent.futures.TimeoutError:
    pass  # post-loop cancellation handles remaining futures
```

**Tests:** `tests/test_bulk_investigate.py` — submit 50 domains with slow mock, verify timeout errors for pending items, no crash.

---

### 3.5 Scheduler API key sync offload

**Deltas resolved:** SD-110
**Files:** `syxx_server/scheduler.py`

**Change:** Run `_sync_api_keys()` in a separate thread:
```python
def _loop():
    ...
    if now - _last_api_key_sync >= sync_interval_sec:
        _last_api_key_sync = now
        threading.Thread(target=_sync_api_keys_safe, daemon=True, name="api-key-sync").start()

def _sync_api_keys_safe():
    try:
        _sync_api_keys()
    except Exception as exc:
        logging_util.error("api_key_sync_error", err=str(exc))
```

**Tests:** Verify `_tick()` runs concurrently with `_sync_api_keys()`.

---

## Phase 4 — Correctness Fixes (batch by module)

### 4.1 Email auth fixes (batch)

**Deltas resolved:** SD-86, SD-111
**Files:** `syxx_server/email_auth.py`
**New tests:** `tests/test_email_auth.py`

**SD-86** — Fix SPF "all" match:
```python
def _extract_spf_all(record):
    for part in reversed(record.split()):
        stripped = part.lower().lstrip("+-~?")
        if stripped == "all":
            return part
    return None
```

**SD-111** — Validate DMARC rua:
```python
has_rua = rua is not None and "mailto:" in rua.lower()
```

**Tests:** SPF record with `include:sendgridall` must not match. DMARC with `rua=garbage` must not pass.

---

### 4.2 Rate limiter prune fix

**Deltas resolved:** SD-12
**Files:** `syxx_server/ratelimit.py`
**New tests:** `tests/test_ratelimit.py`

**Change:** Store `_touched_at` wall-clock timestamp alongside bucket:
```python
# Bucket: (tokens_ms, last_credited, touched_at)
def allow(key, *, capacity, refill_millis):
    now_ms = _now_ms()
    with _lock:
        _maybe_prune(now_ms)
        entry = _buckets.get(key)
        if entry is None:
            _buckets[key] = (capacity * 1000, now_ms, now_ms)
            return True
        tokens_ms, last, _ = entry
        ...
        _buckets[key] = (new_tokens, new_last, now_ms)  # update touched_at
```

Prune uses `touched_at` instead of `last`:
```python
stale = [key for key, (_, _, touched) in _buckets.items() if touched < stale_before]
```

**Tests:** Fast-polling attacker must be rate-limited even after prune interval.

---

### 4.3 App.py input validation fixes (batch)

**Deltas resolved:** SD-87, SD-88, SD-89, SD-91, SD-92
**Files:** `syxx_server/app.py`

**SD-87** — Metrics cardinality:
```python
_KNOWN_METRIC_PATHS = frozenset({"root", "healthz", "readyz", "metrics", "api_stats",
    "warp", "fetch_script", "api_investigate", "api_authoritative", ...})
metric_key = norm.replace("/", "_").lstrip("_") or "root"
if metric_key not in _KNOWN_METRIC_PATHS:
    metric_key = "other"
logging_util.incr(f"http_endpoint_{metric_key}")
```

**SD-88** — API key TTL lower bound:
```python
try:
    ttl_raw = int(payload.get("ttlDays", 90))
except (ValueError, TypeError):
    return self._json_error(400, "ttlDays must be a number")
if ttl_raw < 1:
    return self._json_error(400, "ttlDays must be at least 1")
ttl = min(ttl_raw, 365)
```

**SD-89** — Already handled by SD-88's try/except pattern; apply same to `_route_lookalike` maxPermutations.

**SD-91** — DoH domain validation:
```python
name = (query.get("name", [""])[0]).strip()
if not name or not investigate.valid_domain(name) or len(name) > 253:
    return self._json_error(400, "invalid or missing domain name")
```

**SD-92** — fetch_script content type validation:
```python
data, ctype, _final = fetcher.fetch(target, timeout=config.FETCH_SCRIPT_TIMEOUT)
if not (ctype and "javascript" in ctype.lower()):
    return self._json_error(415, "upstream content is not JavaScript")
self._send(200, data, ctype)
```

---

### 4.4 Webhook idempotency staleness check

**Deltas resolved:** SD-10
**Files:** `syxx_server/fulfillment.py`

**Change:** Add staleness check for stuck "processing" state:
```python
if not db.kv_insert_once(evt_key, "processing"):
    existing = db.kv_get(evt_key)
    if existing in ("minted", "processing", "minted-undelivered"):
        # Check staleness: if "processing" for > 5 minutes, assume crash
        if existing == "processing":
            created = db.kv_created_at(evt_key)
            if created and (time.time() - created) > 300:
                db.kv_set(evt_key, "failed")  # re-enter below
                # fall through to re-entry
            else:
                return "duplicate"
        else:
            return "duplicate"
```

Requires `db.kv_created_at()` — add to `db.py` (simple SELECT on `kv.created_at`).

---

## Phase 5 — DNSSEC + DNS Fixes

### 5.1 Quick wins (localized changes)

**Deltas resolved:** SD-71, SD-72, SD-94, SD-05, SD-18
**Files:** `syxx_server/dnssec_validator.py`, `syxx_server/resolver.py`
**New tests:** `tests/test_dnssec_validator.py`

**SD-71** — KSK protocol validation in `_ksk()`:
```python
def _ksk(rs):
    candidates = [r for r in rs if _pdk(r["rd"]) and _pdk(r["rd"])[1] == 3
                  and not (_pdk(r["rd"])[0] & 0x80)]  # not revoked
    for r in candidates:
        if _pdk(r["rd"])[0] & 1: return r  # SEP bit
    return candidates[0] if candidates else None
```

**SD-72** — TCP length cap (2 lines):
```python
# In dnssec_validator._q() and resolver._query_tcp():
(ln,) = struct.unpack(">H", _recv_exact(ts, 2))
if ln > 16384:
    raise DNSError("TCP response too large (%d bytes)" % ln)
```

**SD-94** — RRSIG signer name check in `_vsig()`:
```python
# Add zone parameter to _vsig, add check:
if sg["sg"].rstrip(".").lower() != zone.rstrip(".").lower():
    continue
```

**SD-05** — Explicit ephemeral port bind:
```python
# Before sock.connect() in resolver.query(), _query_sections(), forward_wire():
sock.bind(('', 0))
```

**SD-18** — Underscore label IDNA fix in `_encode_name()` and `_nw()`:
```python
try:
    encoded = label.encode("idna") if not label.startswith("_") else label.encode("ascii")
except (UnicodeError, UnicodeDecodeError):
    encoded = label.encode("ascii", "replace")
```

---

### 5.2 DNSSEC aggregate time budget

**Deltas resolved:** SD-68
**Files:** `syxx_server/dnssec_validator.py`

**Change:** Add `deadline` parameter to `validate_chain()`, thread remaining time through `_sq()`, `_q()`, `_find()`:
```python
def validate_chain(domain, qtype=1, timeout=5, deadline=None):
    if deadline is None:
        deadline = time.monotonic() + timeout * 3
    ...
    # Before each query:
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        return _r(domain, "inconclusive", ...)  # chain timed out
    per_query_to = min(timeout, max(remaining, 0.5))
```

---

### 5.3 NSEC denial-of-existence verification

**Deltas resolved:** SD-73
**Files:** `syxx_server/dnssec_validator.py`
**Complexity:** High (~50 lines of new NSEC parsing)

**Change:** When DS query returns empty answer, check authority section for NSEC records. If present, verify the queried name falls within the NSEC window (owner ≤ query < next_owner). If verified, record "authenticated denial" instead of "inconclusive."

NSEC3 is significantly more complex (iterative hashing with salt) — defer to follow-up; record as "inconclusive" for NSEC3.

**Tests:** Mock response with NSEC record covering queried name → verify "authenticated denial". Mock response without NSEC → verify "inconclusive".

---

## Phase 6 — Infrastructure & Deployment

### 6.1 Graceful shutdown

**Deltas resolved:** SD-55
**Files:** `server.py`, `syxx_server/app.py`, `syxx_server/dns_authoritative.py`, `syxx_server/scheduler.py`, `syxx_server/dot_listener.py`

**Changes:**
1. `server.py`: Create `stop_event = threading.Event()`, register SIGTERM/SIGINT handlers
2. Signal handler: `stop_event.set()` + `threading.Thread(target=httpd.shutdown).start()`
3. `app.serve()`: Accept `stop_event`, return after `serve_forever()` exits
4. `dns_authoritative.run()`: Accept `stop_event`, use `sock.settimeout(1.0)` + check `stop_event.is_set()`
5. `scheduler.start()`: Accept `stop_event`, use `stop_event.wait(60)` instead of `time.sleep(60)`
6. `dot_listener.run()`: Accept `stop_event`, pass to `server.shutdown()` on set

**Shutdown sequence:**
1. SIGTERM → stop_event.set()
2. httpd.shutdown() from separate thread → serve_forever() returns
3. httpd.server_close() → close listening socket
4. DNS thread exits on next recvfrom timeout (≤1s)
5. Scheduler thread exits on next stop_event.wait() return
6. Process exits cleanly

**Tests:** Start server, send SIGTERM, verify exit code 0, no traceback, all threads joined.

---

### 6.2 Systemd sandboxing hardening

**Deltas resolved:** SD-53 (remaining directives)
**Files:** `scripts/deploy.sh`

**Change:** Add missing systemd directives to the `[Service]` section:
```ini
ProtectKernelTunables=true
ProtectKernelModules=true
ProtectKernelLogs=true
ProtectControlGroups=true
RestrictAddressFamilies=AF_INET AF_INET6 AF_UNIX
RestrictNamespaces=true
LockPersonality=true
RestrictRealtime=true
ProtectClock=true
```

**Note:** `MemoryDenyWriteExecute` and `SystemCallFilter=@system-service` need live testing — Python C extensions may conflict. Add incrementally, one directive at a time, verify `/readyz` returns 200 after each.

---

### 6.3 Crash-loop limit + UFW firewall

**Deltas resolved:** SD-56 (remaining items)
**Files:** `scripts/deploy.sh`

**Changes:**
1. Add to `[Service]`:
   ```ini
   StartLimitIntervalSec=300
   StartLimitBurst=5
   ```
2. Add UFW provisioning after systemd setup:
   ```bash
   if command -v ufw >/dev/null 2>&1; then
       ufw default deny incoming
       ufw default allow outgoing
       ufw allow 22/tcp
       ufw allow 80/tcp
       ufw allow 443/tcp
       ufw allow 53/udp
       ufw --force enable
   fi
   ```

---

## Phase 7 — SEO + Polish Fixes

### 7.1 SEO module fixes (batch)

**Deltas resolved:** SD-104, SD-105, SD-106, SD-107, SD-108
**Files:** `syxx_server/seo.py`, `robots.txt`, `sitemap.xml`

**SD-104** — Inject `<title>` in `_build_tags()`:
```python
if "title" in meta:
    tags.append(f'<title>{meta["title"]}</title>')
```

**SD-105** — Order-independent meta tag regex:
```python
_TAG_RE = re.compile(
    r'<meta\s+(?:[^>]*?\s+)?(?:name|property)\s*=\s*"[^"]*"[^>]*?content\s*=\s*"[^"]*"[^>]*?/?>|'
    r'<meta\s+(?:[^>]*?\s+)?content\s*=\s*"[^"]*"[^>]*?(?:name|property)\s*=\s*"[^"]*"[^>]*?/?>',
    re.IGNORECASE
)
```

**SD-106** — Escape `</` in JSON-LD:
```python
json_ld = json.dumps(meta["json_ld"]).replace("</", "<\\/")
tags.append(f'<script type="application/ld+json">{json_ld}</script>')
```

**SD-107** — Align robots.txt Allow paths with actual routes:
```
Allow: /leak-test
Allow: /leaktest
```
(Remove `.html` suffixes)

**SD-108** — Add `/leaktest` to sitemap.xml or implement canonical redirect.

---

### 7.2 DoT TLS hardening

**Deltas resolved:** SD-95
**Files:** `syxx_server/dot_listener.py`

**Change:** In `run()`, after creating SSLContext:
```python
ctx.set_ciphers("ECDHE+AESGCM:ECDHE+CHACHA20:DHE+AESGCM:DHE+CHACHA20:!aNULL:!MD5:!DSS")
ctx.set_alpn_protocols(["dot"])
```

---

### 7.3 Minor correctness fixes (batch)

**Deltas resolved:** SD-97, SD-98, SD-99, SD-100, SD-101, SD-102, SD-103

| Delta | File | Change |
|-------|------|--------|
| SD-97 | `firewall_test.py` | Catch `DNSError` separately from `socket.timeout` in `_query_resolver` |
| SD-98 | `listeners.py` | Log warning when `set_state` called with unknown name |
| SD-99 | `whois_lookup.py` | Reject domains containing `\r` or `\n` before WHOIS query |
| SD-100 | `lookalike.py` | Return tri-state from `_check_resolution` |
| SD-101 | `email_auth.py` | Parallelize DKIM fallback selectors |
| SD-102 | `scripts/doq_ram_probe.py` | Add platform check at entry |
| SD-103 | `app.py` | Cap trial anchor fingerprint at 128 bytes |

---

## Phase 8 — Licensing Context Tag

**Deltas resolved:** SD-29
**Files:** `syxx_server/licensing.py`

**Change:** Add domain separation to HMAC:
```python
_SESSION_CONTEXT = b"syxx-session-v1"

def _sign(payload_bytes):
    return hmac.new(secret(), _SESSION_CONTEXT + payload_bytes, hashlib.sha256).digest()
```

**⚠️ Breaking change:** Invalidates all existing session tokens. Deploy during low-traffic window. Users will need to re-authenticate.

---

## Phase 9 — Backup Verification

**Deltas resolved:** SD-93
**Files:** `scripts/syxx-backup.sh`

**Change:** Verify backup file exists before declaring success:
```bash
BACKUP_PATH=$(python3 -c "from syxx_server import db; print(db.backup())")
if [ -f "$BACKUP_PATH" ]; then
    echo "✅ Backup created: $BACKUP_PATH"
else
    echo "❌ Backup failed: expected file at $BACKUP_PATH"
    exit 1
fi
```

---

## Implementation Order Summary

| Phase | Focus | Deltas | Effort | Risk |
|-------|-------|--------|--------|------|
| **1** | Foundation (netutil, deadline, config) | 5 | Low | Low |
| **2** | High-severity security | 8 | Medium | Medium |
| **3** | Thread-exhaustion DoS | 7 | Medium | Low |
| **4** | Correctness (email, ratelimit, app, webhook) | 10 | Medium | Low |
| **5** | DNSSEC + DNS | 10 | High (SD-73 NSEC) | Medium |
| **6** | Infrastructure & deploy | 4 | Medium | Medium (restart required) |
| **7** | SEO + polish | 12 | Low | Low |
| **8** | Licensing context tag | 1 | Low | **High** (token invalidation) |
| **9** | Backup verification | 1 | Trivial | Low |

**Total: 58 implementation items across 9 phases.**

---

## Test Infrastructure

### New test files needed

| File | Covers |
|------|--------|
| `tests/test_netutil.py` | IPv4-mapped IPv6, NAT64, CGNAT, all ranges |
| `tests/test_deadline.py` | Happy path, deadline hit, exceptions, empty input |
| `tests/test_email_auth.py` | SPF "all" match, DMARC rua validation |
| `tests/test_bulk_investigate.py` | TimeoutError handling, deadline expiry |
| `tests/test_ratelimit.py` | Prune under fast polling, bucket staleness |
| `tests/test_dnssec_validator.py` | KSK protocol, TCP length, RRSIG signer, NSEC |
| `tests/test_privacy.py` | Audit IP hashing, beta_signups hashing |
| `tests/test_whois.py` | RDAP SSRF, WHOIS size cap, cleartext HTTP |

### Existing test files to extend

| File | Add |
|------|-----|
| `tests/test_resolver_fetcher_ssrf.py` | SD-05 bind test, SD-18 underscore test |
| `tests/test_payments.py` | SD-24 no-email assertion, SD-10 staleness test |
| `tests/test_leak.py` | SD-66 DoT path IP sentinel test |
| `tests/test_server_capacity.py` | SD-55 graceful shutdown test |
| `tests/test_routes.py` | SD-87 metrics cardinality, SD-88 TTL, SD-91 DoH validation |

---

## Deployment Sequence

For changes requiring server restart (Phases 2, 3, 4, 6, 8):

1. **Pre-deploy:** Run `scripts/syxx-backup.sh` to create a DB backup
2. **Deploy code:** Push changes via `deploy.sh` or git pull
3. **Restart:** `systemctl restart syxx-server` (graceful after Phase 6.1)
4. **Verify:** `curl -s https://syxx.app/readyz | python3 -m json.tool`
5. **Monitor:** Check `journalctl -u syxx-server --since "1 min ago"` for errors

**Phase 8 (licensing context tag) must be deployed separately** during a maintenance window — all users will need to re-authenticate.

---

## Files Modified Summary

| File | Phases | Deltas |
|------|--------|--------|
| `syxx_server/netutil.py` | 1 | **NEW** |
| `syxx_server/deadline.py` | 1 | **NEW** |
| `syxx_server/config.py` | 1 | SD-59, SD-64 |
| `syxx_server/resolver.py` | 1, 5 | SD-07, SD-05, SD-18, SD-72 |
| `syxx_server/fetcher.py` | 1 | SD-07 |
| `server.py` | 1, 6 | SD-59, SD-55 |
| `syxx_server/whois_lookup.py` | 2, 7 | SD-67, SD-68, SD-80, SD-99 |
| `syxx_server/dot_listener.py` | 2, 6, 7 | SD-66, SD-55, SD-95 |
| `syxx_server/app.py` | 2, 4, 6, 7 | SD-14, SD-87–92, SD-55, SD-103 |
| `syxx_server/db.py` | 2, 4 | SD-54, SD-10 |
| `syxx_server/fulfillment.py` | 2, 4 | SD-24, SD-10 |
| `index.html` + 8 others | 2 | SD-90 |
| `syxx_server/subdomain_enum.py` | 3 | SD-109 |
| `syxx_server/firewall_test.py` | 3, 7 | SD-78, SD-97 |
| `syxx_server/lookalike.py` | 3, 7 | SD-82, SD-83, SD-100 |
| `syxx_server/bulk_investigate.py` | 3 | SD-69 |
| `syxx_server/scheduler.py` | 3, 6 | SD-110, SD-55 |
| `syxx_server/email_auth.py` | 4, 7 | SD-86, SD-111, SD-101 |
| `syxx_server/ratelimit.py` | 4 | SD-12 |
| `syxx_server/dnssec_validator.py` | 5 | SD-71–73, SD-94, SD-18, SD-68 |
| `syxx_server/dns_authoritative.py` | 6 | SD-55 |
| `scripts/deploy.sh` | 6 | SD-53, SD-56, SD-59 |
| `syxx_server/seo.py` | 7 | SD-104–106 |
| `robots.txt` | 7 | SD-107 |
| `sitemap.xml` | 7 | SD-108 |
| `syxx_server/licensing.py` | 8 | SD-29 |
| `scripts/syxx-backup.sh` | 9 | SD-93 |
| `scripts/doq_ram_probe.py` | 7 | SD-102 |
| `syxx_server/listeners.py` | 7 | SD-98 |
| `syxx_server/leak.py` | (assessed) | SD-40 (by design) |
