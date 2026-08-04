# Implementation Plan: 10 Paid/Pro Web Server Features

**Target:** `/Server/syxx_server/` — Python HTTP server backing the Syxx web app  
**Author:** Qwen planning agent  
**Date:** 2026-07-26  

---

## Codebase Reference (verified line numbers)

| File | Key Symbols | Lines |
|------|------------|-------|
| `app.py` (1501) | `_require()` | 487–495 |
| | `_rate_ok()` | 508–513 |
| | `_body()` / `_require_json()` | 515–548 |
| | `do_POST()` routing | 555–570 |
| | `_dispatch_get()` routing | 570–604 |
| | `_route_exposure()` | 1134–1180 |
| | `_route_investigate()` | 983–993 |
| | `_route_diff()` | 1016–1098 |
| | `_route_trace()` | 1099–1116 |
| | `_route_baseline()` | 1118–1133 |
| | `_route_passive()` | 1184–1193 |
| | `_start_beta_digest_thread()` | 1427–1480 |
| | `serve()` | 1486–1501 |
| `db.py` | `SCHEMA` (CREATE TABLE block) | 40–100 |
| | `connect()` / `_write_conn()` | 105–120 |
| | `kv_get/set` | 135–160 |
| | `passive_record/query` | 185–210 |
| | `purge_expired()` | 280–340 |
| `licensing.py` | `mint_session()` / `verify_session()` | 95–125 |
| | `Entitlement` class | 55–62 |
| | `entitlement_for()` | 235–250 |
| `investigate.py` | `investigate()` | 160–180 |
| | `_valid_domain()` | 38 |
| `resolver.py` (687) | `_build_query()` (class=1 hardcoded) | 85–98 |
| | `query()` | 197–215 |
| `mailer.py` | `send()` | 28–65 |
| `passivedns.py` | `baseline()` (drift pattern) | 33–61 |
| `ratelimit.py` | `allow(key, capacity, refill)` | 28–55 |
| `index.html` (660) | Command buttons grid | 250–280 |
| | `runCmd()` / `MEMBERS_PRO` | 580–625 |

---

## Phase 0: Market Existing Features

**Features 1 (Content Warp), 3 (Historical Passive DNS), 5 (Authoritative Delegation Trace) are fully implemented server-side.** The gap is frontend discoverability: the members terminal has `mirror` and `warp` buttons, but no dedicated UI sections for passive DNS history, delegation trace, or baseline drift.

### What exists
- `/api/warp/raw?url=` — `app.py:908` — paywalled via `_require()`
- `/api/diff` (POST) — `app.py:1016` — paywalled
- `/api/passive-dns?domain=` — `app.py:1184` — paywalled
- `/api/baseline?domain=` — `app.py:1118` — paywalled
- `/api/trace?domain=&type=` — `app.py:1099` — paywalled
- `/api/authoritative?domain=&type=` — `app.py:994` — paywalled

### Frontend changes (index.html)

**1. Add new command buttons to the "Remote wardens" palette (line ~265)**

Insert after the existing `conclude` button:

```html
<span class="cmd" onclick="runCmd('trace')" style="...">trace <span style="color:#e0b45a">pro</span></span>
<span class="cmd" onclick="runCmd('passive')" style="...">passive <span style="color:#e0b45a">pro</span></span>
<span class="cmd" onclick="runCmd('baseline')" style="...">baseline <span style="color:#e0b45a">pro</span></span>
```

**2. Extend `MEMBERS_PRO` set (line ~595)**

```js
const MEMBERS_PRO = new Set(['mirror','warp','trace','passive','baseline']);
```

**3. Add `runCmd` handlers for the new commands (after the `warp` handler, ~line 625)**

```js
if(name==='trace'){
  mrun('trace '+d+' [Pro]', M('Walking delegation chain for '+d+'…'));
  fetch('/api/trace?domain='+encodeURIComponent(d)+'&type='+ty,
    {headers:{'Authorization':'Bearer '+localStorage.getItem('syxx_session')}})
    .then(r=>{if(!r.ok)throw r;return r.json()})
    .then(data=>{
      const chain=(data.delegation||[]).map(z=>'  '+z.zone+' → '+(z.ns||[]).join(', ')).join('\n');
      mrun('',G('✓ '+data.verdict)+' · '+(data.delegation||[]).length+' zone(s)\n'+chain);
    })
    .catch(()=>{mrun('',R('API error'));});
  return;
}
if(name==='passive'){
  mrun('passive '+d+' [Pro]', M('Querying passive DNS history…'));
  fetch('/api/passive-dns?domain='+encodeURIComponent(d),
    {headers:{'Authorization':'Bearer '+localStorage.getItem('syxx_session')}})
    .then(r=>{if(!r.ok)throw r;return r.json()})
    .then(data=>{
      const rows=(data.records||[]).slice(0,10)
        .map(r=>'  '+r.rtype+' '+r.value+' (seen '+r.hits+'x, last '+new Date(r.lastSeen*1000).toISOString().slice(0,10)+')')
        .join('\n');
      mrun('',G('✓ '+data.count+' record(s)')+'\n'+rows+(data.count>10?'\n  … and '+(data.count-10)+' more':''));
    })
    .catch(()=>{mrun('',R('API error'));});
  return;
}
if(name==='baseline'){
  mrun('baseline '+d+' [Pro]', M('Computing drift baseline…'));
  fetch('/api/baseline?domain='+encodeURIComponent(d),
    {headers:{'Authorization':'Bearer '+localStorage.getItem('syxx_session')}})
    .then(r=>{if(!r.ok)throw r;return r.json()})
    .then(data=>{
      const drift=data.driftSuspected?R('⚠ drift detected'):G('✓ no drift');
      mrun('',drift+' · '+data.recordCount+' record(s)\n  established: '+(data.established||[]).join(', ')+'\n  recently new: '+(data.recentlyNew||[]).join(', '));
    })
    .catch(()=>{mrun('',R('API error'));});
  return;
}
```

### Verification
- Sign in to the web app, run `trace github.com`, `passive github.com`, `baseline github.com` — each must return real data from the existing APIs.
- Verify 402 returned when session is absent/expired.

---

## Phase 1: Quick Wins

### Feature 6: Exposure Assessment (CHAOS class queries)

**Current state:** `_route_exposure` (`app.py:1134`) queries the requester's IP as a recursive resolver using class IN. The gap: it does not probe CHAOS-class TXT records (`version.bind`, `hostname.bind`) which reveal resolver software/version and are a strong open-resolver indicator.

#### Changes

**File: `resolver.py` — add CHAOS class support to `_build_query()` (line 85)**

The `_build_query()` function hardcodes class=1 (IN) in the question section. Add an optional `qclass` parameter:

```python
def _build_query(name, qtype, rd=True, do=False, qclass=1):
    txid = secrets.randbits(16)
    flags = 0x0100 if rd else 0x0000
    arcount = 1 if do else 0
    header = struct.pack(">HHHHHH", txid, flags, 1, 0, 0, arcount)
    question = _encode_name(name) + struct.pack(">HH", qtype, qclass)  # ← qclass here (was hardcoded 1)
    # ... rest unchanged
```

**File: `resolver.py` — add `query_chaos()` function (~line 215, after `query()`)**

```python
CLASS_CHAOS = 3

def query_chaos(name, server, timeout=2):
    """CHAOS-class TXT query (version.bind, hostname.bind).
    Used by exposure assessment to fingerprint open resolvers."""
    txid, packet, qname, qtype_ = _build_query(name, 16, qclass=CLASS_CHAOS)  # TXT=16
    sock = socket.socket(_addr_family(server), socket.SOCK_DGRAM)
    sock.settimeout(timeout)
    try:
        sock.connect((server, 53))
        sock.send(packet)
        data = sock.recv(4096)
    except socket.timeout:
        raise DNSError("timeout")
    finally:
        sock.close()
    return _parse_response(data, txid, qtype_, expected_qname=qname)
```

**File: `app.py` — extend `_route_exposure()` (line 1134)**

After the existing recursive-query probe (lines 1158–1166), add CHAOS probe:

```python
# CHAOS class probe — version.bind/hostname.bind reveal resolver software
chaos_results = {}
for label in ("version.bind", "hostname.bind", "id.server"):
    try:
        resp = resolver.query_chaos(label, ip, timeout=2)
        txt_values = [r["value"] for r in resp.get("records", []) if r["type"] == "TXT"]
        if txt_values:
            chaos_results[label] = txt_values
    except (resolver.DNSError, OSError):
        pass

# Upgrade assessment: CHAOS answers = definitive open resolver
if chaos_results and assessment != "open-resolver":
    assessment = "open-resolver"
    recursive = True
```

Add `chaos` field to the response dict (line ~1178):
```python
"chaos": chaos_results,
```

**Rate limit:** Already governed by `_rate_ok(ent)` at `app.py:1137`. No change needed.

#### Testing
- Run `/api/exposure` from a known open resolver IP — must return `open-resolver` with CHAOS data.
- Run from a non-resolver IP — must return `inconclusive` or `closed`.
- Failure case: timeout on CHAOS query must NOT downgrade from `open-resolver` to `inconclusive`.

---

### Feature 9: API Access (Longer-lived API keys)

**Current state:** Session tokens (`licensing.py:95`) are HMAC-signed with a TTL (default from `config.SESSION_TTL`). The `/api/auth/activate` endpoint mints a session. There is no API key management CRUD, usage tracking, or key revocation.

#### Changes

**File: `db.py` — add `api_keys` table to SCHEMA (after line 100)**

```sql
CREATE TABLE IF NOT EXISTS api_keys (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    subject    TEXT NOT NULL,
    key_hash   TEXT NOT NULL UNIQUE,
    key_prefix TEXT NOT NULL,
    label      TEXT,
    scopes     TEXT NOT NULL DEFAULT '*',
    created_at INTEGER NOT NULL,
    expires_at INTEGER,
    last_used  INTEGER,
    use_count  INTEGER NOT NULL DEFAULT 0,
    revoked    INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_api_keys_subject ON api_keys(subject);
CREATE INDEX IF NOT EXISTS idx_api_keys_hash ON api_keys(key_hash);
```

**File: `db.py` — add API key CRUD functions (~line 350, after `passive_count()`)**

```python
def api_key_create(subject, label=None, scopes="*", ttl_days=90):
    import secrets
    raw = "syxx_" + secrets.token_urlsafe(32)
    key_hash = hashlib.sha256(raw.encode()).hexdigest()
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO api_keys(subject,key_hash,key_prefix,label,scopes,created_at,expires_at) "
            "VALUES(?,?,?,?,?,?,?)",
            (subject, key_hash, raw[:12], label, scopes, now, now + ttl_days * 86400),
        )
        conn.commit()
    finally:
        conn.close()
    return raw  # return the plaintext key ONCE; it is never stored

def api_key_lookup(key_hash):
    conn = connect()
    try:
        row = conn.execute(
            "SELECT * FROM api_keys WHERE key_hash=? AND revoked=0", (key_hash,)
        ).fetchone()
        if not row:
            return None
        if row["expires_at"] and row["expires_at"] < int(time.time()):
            return None
        return dict(row)
    finally:
        conn.close()

def api_key_touch(key_id):
    conn = _write_conn()
    try:
        conn.execute(
            "UPDATE api_keys SET last_used=?, use_count=use_count+1 WHERE id=?",
            (int(time.time()), key_id),
        )
        conn.commit()
    finally:
        conn.close()

def api_key_list(subject):
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT id,key_prefix,label,scopes,created_at,expires_at,last_used,use_count,revoked "
            "FROM api_keys WHERE subject=? ORDER BY created_at DESC", (subject,)
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()

def api_key_revoke(key_id, subject):
    conn = _write_conn()
    try:
        conn.execute(
            "UPDATE api_keys SET revoked=1 WHERE id=? AND subject=?", (key_id, subject)
        )
        conn.commit()
    finally:
        conn.close()
```

**File: `licensing.py` — extend `entitlement_for()` to accept API keys (~line 235)**

Add before the Bearer check:

```python
# API key auth: "Syxx-API-Key: syxx_..." header
api_key = headers.get("X-Syxx-API-Key", "")
if not api_key:
    auth = headers.get("Authorization", "")
    if auth.lower().startswith("apikey "):
        api_key = auth[7:].strip()
if api_key:
    import hashlib
    key_hash = hashlib.sha256(api_key.encode()).hexdigest()
    row = db.api_key_lookup(key_hash)
    if row:
        db.api_key_touch(row["id"])
        return Entitlement(row["subject"], "pro", "api-key"), None
```

**File: `app.py` — add API key management routes in `do_POST()` (line ~555)**

Add to POST routing:

```python
if path == "/api/keys":
    return self._route_create_api_key()
if path.startswith("/api/keys/") and self.command == "DELETE":
    return self._route_revoke_api_key(path)
```

And in `_dispatch_get()`:

```python
if path == "/api/keys":
    return self._route_list_api_keys(query)
```

Route handlers:

```python
def _route_create_api_key(self):
    ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    if not self._require_json():
        return
    raw = self._body()
    if self._body_error(raw):
        return
    try:
        payload = json.loads(raw or b"{}")
    except ValueError:
        return self._json_error(400, "invalid JSON")
    label = (payload.get("label") or "").strip()[:64]
    ttl = min(int(payload.get("ttlDays", 90)), 365)
    key = db.api_key_create(ent.subject, label=label, ttl_days=ttl)
    self._send(201, {"key": key, "label": label, "expiresInDays": ttl,
                     "warning": "store this key now; it will not be shown again"})

def _route_list_api_keys(self, query):
    ent, ok = self._require(query, config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    self._send(200, {"keys": db.api_key_list(ent.subject)})

def _route_revoke_api_key(self, path):
    ent, ok = self._require(urllib.parse.parse_qs(""), config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    try:
        key_id = int(path.split("/")[-1])
    except (ValueError, IndexError):
        return self._json_error(400, "invalid key id")
    db.api_key_revoke(key_id, ent.subject)
    self._send(200, {"revoked": True})
```

**Rate limit config:** API key endpoints use the standard `_rate_ok(ent)` bucket. Add a stricter limit for key creation:

```python
# In _route_create_api_key, before creating:
if not ratelimit.allow("keycreate:" + ent.subject, capacity=5, refill_millis=86400000):
    return self._json_error(429, "key creation limit: 5 per day")
```

**Frontend (index.html):** Add a "API Keys" subsection to the members area management panel. Wire to `/api/keys` POST/GET and DELETE.

#### Testing
- Create a key via POST, use it in `X-Syxx-API-Key` header to call `/api/investigate` — must work.
- Revoke the key, retry — must get 401/402.
- Create a key with `ttlDays=1`, verify it expires after the TTL.
- Failure case: use a tampered key hash — must be rejected.
- Failure case: create >5 keys in a day — must get 429.

---

## Phase 2: Core Pro Features

### Feature 4: Bulk Domain Investigation

**Pattern:** Reuses `investigate.investigate()` (`investigate.py:160`) in a concurrent executor, similar to `_clean_vantage_answers()` (`app.py:220`) which already uses `ThreadPoolExecutor` with a deadline.

#### API Design

```
POST /api/bulk-investigate
Content-Type: application/json
Authorization: Bearer <token>

Request:
{
  "domains": ["example.com", "test.org", ...],  // max 50
  "type": "A"                                     // optional, default A
}

Response:
{
  "results": [
    {"domain": "example.com", "consensus": "consistent", "ipv4": [...], ...},
    {"domain": "test.org", "error": "invalid domain"},
    ...
  ],
  "summary": {
    "total": 10,
    "consistent": 7,
    "divergent": 1,
    "inconclusive": 1,
    "errors": 1
  },
  "elapsedMs": 4500
}
```

#### Changes

**File: `app.py` — add route in `do_POST()` (line ~555)**

```python
if path == "/api/bulk-investigate":
    return self._route_bulk_investigate()
```

**File: `app.py` — new route handler (~line 1200, after `_route_passive`)**

```python
def _route_bulk_investigate(self):
    query = urllib.parse.parse_qs("")
    ent, ok = self._require(query, config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    if not self._require_json():
        return
    raw = self._body()
    if self._body_error(raw):
        return
    try:
        payload = json.loads(raw or b"{}")
    except ValueError:
        return self._json_error(400, "invalid JSON")

    domains = payload.get("domains")
    if not isinstance(domains, list) or not domains:
        return self._json_error(400, "domains must be a non-empty list")
    if len(domains) > 50:
        return self._json_error(400, "maximum 50 domains per request")

    # Stricter rate limit for bulk: 10 requests per hour
    if not ratelimit.allow("bulk:" + (ent.subject if ent else self._client_ip()),
                           capacity=10, refill_millis=3600000):
        return self._json_error(429, "bulk investigation limit: 10 per hour")

    start = time.monotonic()
    deadline_sec = 60  # aggregate deadline for entire bulk request

    def do_one(domain):
        if time.monotonic() - start > deadline_sec:
            return {"domain": domain, "error": "deadline exceeded"}
        result, err = investigate.investigate(domain)
        if err:
            return {"domain": domain, "error": err}
        return result

    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=min(len(domains), 10)) as pool:
        futs = {pool.submit(do_one, d): d for d in domains}
        for fut in concurrent.futures.as_completed(futs, timeout=deadline_sec):
            try:
                results.append(fut.result())
            except Exception:
                results.append({"domain": futs[fut], "error": "internal error"})

    summary = {"total": len(results)}
    for r in results:
        if "error" in r:
            summary["errors"] = summary.get("errors", 0) + 1
        else:
            c = r.get("consensus", "inconclusive")
            summary[c] = summary.get(c, 0) + 1

    logging_util.incr("bulk_investigations")
    self._send(200, {
        "results": results,
        "summary": summary,
        "elapsedMs": int((time.monotonic() - start) * 1000),
    })
```

**New module: none.** Bulk investigation is a thin concurrent wrapper over `investigate.investigate()`.

**Frontend (index.html):** Add a "Bulk" button in the DNS tools section. When clicked, show a textarea for domain list input, POST to `/api/bulk-investigate`, render the summary table.

#### Testing
- Submit 5 real domains, verify all return results within the deadline.
- Submit 50 domains (max), verify completion.
- Submit 51 domains, verify 400 error.
- Failure case: submit a mix of valid and invalid domains, verify errors are reported per-domain without aborting the batch.
- Failure case: submit during rate-limit window, verify 429.

---

### Feature 7: DNS Firewall Evasion Test

**Concept:** Query a curated list of known-blocked domains (gambling, malware, adult) through the user's resolver and compare with clean vantages. If the user's resolver returns NXDOMAIN/refused while clean vantages resolve normally, the user's DNS is filtered.

#### API Design

```
POST /api/firewall-test
Content-Type: application/json
Authorization: Bearer <token>

Request:
{
  "resolverIp": "1.2.3.4",       // optional, defaults to client IP
  "categories": ["malware","gambling"]  // optional filter
}

Response:
{
  "resolverIp": "1.2.3.4",
  "results": [
    {"domain": "known-malware.example", "category": "malware",
     "userResolver": "nxdomain", "cleanVantage": "resolved",
     "blocked": true},
    ...
  ],
  "summary": {
    "tested": 10,
    "blocked": 3,
    "resolved": 7,
    "assessment": "filtered"  // filtered | open | inconclusive
  }
}
```

#### Changes

**New module: `Server/syxx_server/firewall.py` (~80 lines)**

```python
"""DNS firewall evasion detection.

Queries a curated list of domains known to be blocked by common DNS filters
through the user's specified resolver and compares with clean vantage results.
"""
from . import config, resolver, logging_util

# Curated test domains — chosen to be reliably blocked by major DNS filters
# but resolvable from clean vantages. These are NOT malware C2 domains; they
# are well-known test/sinkhole domains used by DNS filter vendors.
_TEST_DOMAINS = [
    {"domain": "testsafebrowsing.googleapis.com", "category": "malware"},
    {"domain": "malware.testing.google.com", "category": "malware"},
    {"domain": "phishing.testsafebrowsing.com", "category": "phishing"},
    # ... operator-curated list, 10-20 domains
]

def test(resolver_ip, categories=None, deadline=None):
    """Query test domains through resolver_ip and clean vantages.
    Returns (results_list, summary_dict) or (None, error_string)."""
    import time, concurrent.futures

    if categories:
        domains = [d for d in _TEST_DOMAINS if d["category"] in categories]
    else:
        domains = _TEST_DOMAINS

    if not domains:
        return None, "no matching test domains"

    results = []
    blocked_count = 0

    def query_both(entry):
        domain = entry["domain"]
        user_result = "error"
        clean_result = "error"
        try:
            resp = resolver.query(domain, resolver.TYPE_A, resolver_ip, timeout=3)
            if resp.get("rcode") == 3:  # NXDOMAIN
                user_result = "nxdomain"
            elif resp.get("records"):
                user_result = "resolved"
            else:
                user_result = "empty"
        except resolver.DNSError:
            user_result = "timeout"

        # Clean vantage: use first public resolver
        try:
            resp = resolver.query(domain, resolver.TYPE_A,
                                  config.PUBLIC_RESOLVERS[0], timeout=3)
            if resp.get("records"):
                clean_result = "resolved"
            elif resp.get("rcode") == 3:
                clean_result = "nxdomain"
        except resolver.DNSError:
            clean_result = "timeout"

        is_blocked = (user_result in ("nxdomain", "timeout", "empty")
                      and clean_result == "resolved")
        return {
            "domain": domain, "category": entry["category"],
            "userResolver": user_result, "cleanVantage": clean_result,
            "blocked": is_blocked,
        }

    deadline_t = deadline or (time.monotonic() + 20)
    with concurrent.futures.ThreadPoolExecutor(max_workers=min(len(domains), 5)) as pool:
        futs = [pool.submit(query_both, d) for d in domains]
        for fut in concurrent.futures.as_completed(futs, timeout=max(1, deadline_t - time.monotonic())):
            try:
                r = fut.result()
                results.append(r)
                if r["blocked"]:
                    blocked_count += 1
            except Exception:
                pass

    total = len(results)
    if total == 0:
        assessment = "inconclusive"
    elif blocked_count == 0:
        assessment = "open"
    elif blocked_count >= total * 0.3:
        assessment = "filtered"
    else:
        assessment = "partially-filtered"

    return results, {
        "tested": total, "blocked": blocked_count,
        "resolved": total - blocked_count, "assessment": assessment,
    }
```

**File: `app.py` — import `firewall` (line ~29)**

Add `firewall` to the import list:
```python
from . import (broadcast, config, db, fetcher, firewall, fulfillment, investigate, leak, licensing,
               listeners, logging_util, mailer, passivedns, ratelimit, resolver,
               stripe_checkout, warp)
```

**File: `app.py` — add route in `do_POST()` (line ~555)**

```python
if path == "/api/firewall-test":
    return self._route_firewall_test()
```

**File: `app.py` — route handler**

```python
def _route_firewall_test(self):
    query = urllib.parse.parse_qs("")
    ent, ok = self._require(query, config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    if not self._require_json():
        return
    raw = self._body()
    if self._body_error(raw):
        return
    try:
        payload = json.loads(raw or b"{}")
    except ValueError:
        return self._json_error(400, "invalid JSON")

    resolver_ip = (payload.get("resolverIp") or self._client_ip()).strip()
    categories = payload.get("categories")
    if categories and not isinstance(categories, list):
        return self._json_error(400, "categories must be a list")

    results, summary = firewall.test(resolver_ip, categories)
    if results is None:
        return self._json_error(400, summary)

    logging_util.incr("firewall_tests")
    self._send(200, {"resolverIp": resolver_ip, "results": results, "summary": summary})
```

**Frontend (index.html):** Add a `firewall` button in the Remote wardens section. Wire to `/api/firewall-test` POST, render results as a table showing each domain's blocked/resolved status.

#### Testing
- Run against a known-filtered resolver (e.g., NextDNS with blocklist) — must show `filtered`.
- Run against 1.1.1.1 (open) — must show `open`.
- Failure case: submit an unreachable IP — must return `inconclusive`, not crash.

---

## Phase 3: Retention Features

### Feature 2: Asset Integrity Monitor

**Pattern:** Follow `passivedns.baseline()` (`passivedns.py:33`) for drift detection. Store periodic snapshots of a domain's DNS records and HTTP content hashes, compare current vs historical.

#### API Design

```
POST /api/monitor/assets
Authorization: Bearer <token>
Content-Type: application/json

Request:
{
  "domain": "example.com",
  "checkNow": true   // optional: force immediate re-check
}

Response:
{
  "domain": "example.com",
  "current": {
    "a": ["93.184.216.34"],
    "aaaa": ["2606:2800:220:1:248:1893:25c8:1946"],
    "mx": ["..."],
    "ns": ["..."],
    "txtHash": "sha256:abc...",
    "contentHash": "sha256:def...",
    "checkedAt": 1722000000
  },
  "history": [
    {"checkedAt": ..., "a": [...], "changes": ["a:removed:93.184.216.35"]},
    ...
  ],
  "changes": [...],   // diff from last check
  "driftDetected": false
}
```

#### Changes

**File: `db.py` — add `asset_snapshots` table (after line 100)**

```sql
CREATE TABLE IF NOT EXISTS asset_snapshots (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    subject    TEXT NOT NULL,
    domain     TEXT NOT NULL,
    rtype      TEXT NOT NULL,
    values     TEXT NOT NULL,         -- JSON array
    txt_hash   TEXT,
    content_hash TEXT,
    checked_at INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_asset_snap_domain ON asset_snapshots(subject, domain, checked_at DESC);
```

**File: `db.py` — add asset CRUD (~line 350)**

```python
def asset_snapshot_save(subject, domain, rtype, values, txt_hash=None, content_hash=None):
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO asset_snapshots(subject,domain,rtype,values,txt_hash,content_hash,checked_at) "
            "VALUES(?,?,?,?,?,?,?)",
            (subject, domain.lower(), rtype, json.dumps(values), txt_hash, content_hash, now),
        )
        conn.commit()
    finally:
        conn.close()

def asset_snapshot_history(subject, domain, limit=30):
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT rtype, values, txt_hash, content_hash, checked_at "
            "FROM asset_snapshots WHERE subject=? AND domain=? "
            "ORDER BY checked_at DESC LIMIT ?",
            (subject, domain.lower(), limit),
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()
```

**New module: `Server/syxx_server/assets.py` (~100 lines)**

```python
"""Asset integrity monitor — DNS + content snapshot and drift detection.

Records periodic snapshots of a domain's DNS records and content hashes.
Compares current state against the most recent prior snapshot to detect changes.
"""
import hashlib
import json
import time

from . import config, db, fetcher, resolver, logging_util, warp

_RTYPE_MAP = {
    "A": resolver.TYPE_A, "AAAA": resolver.TYPE_AAAA,
    "MX": 15, "NS": resolver.TYPE_NS, "TXT": 16,
}

def check(domain, subject, check_now=False):
    """Run an asset integrity check for domain.
    Returns (result_dict, error_string|None)."""
    domain = domain.lower().strip()

    current = {"checkedAt": int(time.time())}
    for rtype_str, qtype in _RTYPE_MAP.items():
        try:
            resp = resolver.query(domain, qtype, config.PUBLIC_RESOLVERS[0], timeout=3)
            values = sorted({r["value"] for r in resp["records"] if r["type"] == rtype_str})
            current[rtype_str.lower()] = values
        except resolver.DNSError:
            current[rtype_str.lower()] = []

    # Content hash (best-effort via fetcher)
    try:
        url = "https://" + domain
        html = fetcher.fetch(url)
        current["contentHash"] = "sha256:" + hashlib.sha256(html).hexdigest()[:16]
    except Exception:
        current["contentHash"] = None

    # Save snapshot
    for rtype_str in _RTYPE_MAP:
        vals = current.get(rtype_str.lower(), [])
        if vals:
            db.asset_snapshot_save(
                subject, domain, rtype_str, vals,
                content_hash=current.get("contentHash"),
            )

    # Compare with previous
    history = db.asset_snapshot_history(subject, domain, 30)
    changes = []
    if len(history) > len(_RTYPE_MAP):
        # Group by rtype, find previous values
        prev_by_type = {}
        for row in history:
            rt = row["rtype"]
            if rt not in prev_by_type:
                prev_by_type[rt] = json.loads(row["values"])
        for rtype_str in _RTYPE_MAP:
            cur = set(current.get(rtype_str.lower(), []))
            prev = set(prev_by_type.get(rtype_str, []))
            added = cur - prev
            removed = prev - cur
            for v in added:
                changes.append({"type": rtype_str, "change": "added", "value": v})
            for v in removed:
                changes.append({"type": rtype_str, "change": "removed", "value": v})

    drift_detected = bool(changes)
    return {
        "domain": domain,
        "current": current,
        "history": [{"checkedAt": r["checked_at"], "rtype": r["rtype"],
                      "values": json.loads(r["values"])} for r in history[:10]],
        "changes": changes,
        "driftDetected": drift_detected,
    }, None
```

**File: `app.py` — import `assets` (line ~29), add route**

```python
if path == "/api/monitor/assets":
    return self._route_asset_monitor(query)
```

Route handler:
```python
def _route_asset_monitor(self, query):
    ent, ok = self._require(query, config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    if not self._require_json():
        return
    raw = self._body()
    if self._body_error(raw):
        return
    try:
        payload = json.loads(raw or b"{}")
    except ValueError:
        return self._json_error(400, "invalid JSON")
    domain = (payload.get("domain") or "").strip()
    if not domain or len(domain) > 253:
        return self._json_error(400, "invalid domain")

    # Rate limit: 20 asset checks per hour
    if not ratelimit.allow("assets:" + ent.subject, capacity=20, refill_millis=3600000):
        return self._json_error(429, "asset monitor limit: 20 per hour")

    result, err = assets.check(domain, ent.subject)
    if err:
        return self._json_error(400, err)
    logging_util.incr("asset_checks")
    self._send(200, result)
```

**Frontend (index.html):** Add `assets` button in Remote wardens. Render current DNS records, change history, and drift status.

#### Testing
- Check a stable domain twice — second check must show no changes.
- Check a domain after a known DNS change — must detect the diff.
- Failure case: check a nonexistent domain — must return empty records, not crash.

---

### Feature 10: Scheduled Monitoring

**Pattern:** Follow `_start_beta_digest_thread()` (`app.py:1427`) — daemon thread, hourly tick, kv-stored timestamp for cadence, mailer for notifications.

#### API Design

```
POST /api/monitors
Authorization: Bearer <token>
Content-Type: application/json

Request:
{
  "domain": "example.com",
  "checks": ["dns", "content", "exposure"],
  "intervalHours": 24,
  "alertEmail": "user@example.com"
}

Response:
{
  "id": 1,
  "domain": "example.com",
  "checks": ["dns", "content"],
  "intervalHours": 24,
  "alertEmail": "user@example.com",
  "createdAt": 1722000000,
  "nextRunAt": 1722086400,
  "status": "active"
}

GET /api/monitors — list user's monitors
DELETE /api/monitors/<id> — cancel a monitor
```

#### Changes

**File: `db.py` — add `monitors` and `monitor_results` tables**

```sql
CREATE TABLE IF NOT EXISTS monitors (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    subject        TEXT NOT NULL,
    domain         TEXT NOT NULL,
    checks         TEXT NOT NULL DEFAULT 'dns',
    interval_hours INTEGER NOT NULL DEFAULT 24,
    alert_email    TEXT,
    status         TEXT NOT NULL DEFAULT 'active',
    last_run_at    INTEGER,
    next_run_at    INTEGER,
    created_at     INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_monitors_subject ON monitors(subject);
CREATE INDEX IF NOT EXISTS idx_monitors_next ON monitors(next_run_at);

CREATE TABLE IF NOT EXISTS monitor_results (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    monitor_id  INTEGER NOT NULL,
    check_type  TEXT NOT NULL,
    result      TEXT NOT NULL,
    changes     TEXT,
    ran_at      INTEGER NOT NULL,
    FOREIGN KEY (monitor_id) REFERENCES monitors(id)
);
CREATE INDEX IF NOT EXISTS idx_monitor_results_mid ON monitor_results(monitor_id, ran_at DESC);
```

**File: `db.py` — add monitor CRUD (~line 380)**

```python
def monitor_create(subject, domain, checks, interval_hours, alert_email):
    now = int(time.time())
    conn = _write_conn()
    try:
        cur = conn.execute(
            "INSERT INTO monitors(subject,domain,checks,interval_hours,alert_email,next_run_at,created_at) "
            "VALUES(?,?,?,?,?,?,?)",
            (subject, domain.lower(), json.dumps(checks), interval_hours,
             alert_email, now + interval_hours * 3600, now),
        )
        conn.commit()
        return cur.lastrowid
    finally:
        conn.close()

def monitor_list(subject):
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT * FROM monitors WHERE subject=? ORDER BY created_at DESC", (subject,)
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()

def monitor_delete(monitor_id, subject):
    conn = _write_conn()
    try:
        conn.execute("DELETE FROM monitors WHERE id=? AND subject=?", (monitor_id, subject))
        conn.commit()
    finally:
        conn.close()

def monitors_due(now):
    """Return monitors whose next_run_at <= now and status='active'."""
    conn = connect()
    try:
        rows = conn.execute(
            "SELECT * FROM monitors WHERE status='active' AND next_run_at <= ? "
            "ORDER BY next_run_at ASC LIMIT 50", (now,)
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()

def monitor_schedule_next(monitor_id, interval_hours):
    now = int(time.time())
    conn = _write_conn()
    try:
        conn.execute(
            "UPDATE monitors SET last_run_at=?, next_run_at=? WHERE id=?",
            (now, now + interval_hours * 3600, monitor_id),
        )
        conn.commit()
    finally:
        conn.close()

def monitor_result_save(monitor_id, check_type, result, changes=None):
    conn = _write_conn()
    try:
        conn.execute(
            "INSERT INTO monitor_results(monitor_id,check_type,result,changes,ran_at) "
            "VALUES(?,?,?,?,?)",
            (monitor_id, check_type, json.dumps(result),
             json.dumps(changes) if changes else None, int(time.time())),
        )
        conn.commit()
    finally:
        conn.close()
```

**New module: `Server/syxx_server/scheduler.py` (~120 lines)**

```python
"""Background scheduler for periodic monitoring.

Follows the _start_beta_digest_thread pattern: daemon thread, periodic tick,
kv-stored state. Runs due monitors, saves results, sends email alerts on changes.
"""
import json
import threading
import time

from . import assets, config, db, investigate, logging_util, mailer

def _run_checks(monitor):
    """Execute the configured checks for a single monitor."""
    domain = monitor["domain"]
    checks = json.loads(monitor["checks"])
    results = {}
    all_changes = []

    if "dns" in checks:
        result, err = investigate.investigate(domain)
        if result:
            results["dns"] = result
            # Compare with previous
            prev = db.monitor_results_for(monitor["id"], "dns", limit=1)
            if prev:
                prev_result = json.loads(prev[0]["result"])
                # Simple diff: compare consensus
                if prev_result.get("consensus") != result.get("consensus"):
                    all_changes.append({
                        "type": "dns",
                        "detail": f"consensus changed: {prev_result.get('consensus')} → {result.get('consensus')}"
                    })

    if "content" in checks:
        result, err = assets.check(domain, monitor["subject"])
        if result:
            results["content"] = result
            if result.get("driftDetected"):
                all_changes.append({
                    "type": "content",
                    "detail": f"{len(result.get('changes', []))} change(s) detected"
                })

    for check_type, result in results.items():
        db.monitor_result_save(monitor["id"], check_type, result,
                               changes=all_changes if all_changes else None)

    return all_changes


def _send_alert(monitor, changes):
    """Email the monitor owner about detected changes."""
    email = monitor.get("alert_email")
    if not email or not changes:
        return
    domain = monitor["domain"]
    body = f"Syxx Monitor Alert: {domain}\n\n"
    body += f"Detected {len(changes)} change(s):\n"
    for c in changes:
        body += f"  - [{c['type']}] {c['detail']}\n"
    body += f"\nView details at https://syxx.app\n"
    mailer.send(email, f"Syxx Alert: changes detected for {domain}", body)


def _tick():
    """Process one batch of due monitors."""
    now = int(time.time())
    due = db.monitors_due(now)
    for monitor in due:
        try:
            changes = _run_checks(monitor)
            db.monitor_schedule_next(monitor["id"], monitor["interval_hours"])
            if changes:
                _send_alert(monitor, changes)
                logging_util.incr("monitor_alerts")
        except Exception as e:
            logging_util.error("monitor_tick_error", monitor_id=monitor["id"], err=str(e))
            # Still advance the schedule to avoid infinite retry loops
            db.monitor_schedule_next(monitor["id"], monitor["interval_hours"])


def start():
    """Start the scheduler daemon thread."""
    def _loop():
        while True:
            time.sleep(300)  # check every 5 minutes
            try:
                _tick()
            except Exception as e:
                logging_util.error("scheduler_loop_error", err=str(e))

    threading.Thread(target=_loop, name="syxx-scheduler", daemon=True).start()
    logging_util.info("scheduler_started")
```

**File: `app.py` — import `scheduler` (line ~29), start in `serve()` (line ~1488)**

```python
# After _start_beta_digest_thread():
scheduler.start()
```

**File: `app.py` — add routes in `do_POST()`**

```python
if path == "/api/monitors":
    return self._route_create_monitor()
```

In `_dispatch_get()`:
```python
if path == "/api/monitors":
    return self._route_list_monitors(query)
```

In `do_POST()` or a new DELETE handler:
```python
if path.startswith("/api/monitors/"):
    return self._route_delete_monitor(path)
```

Route handlers follow the same pattern as API keys — `_require()`, `_rate_ok()`, `_require_json()`, `_body()`.

**Frontend (index.html):** Add a "Monitors" section in the members area. Form to create a monitor (domain, check types, interval, email). List active monitors with delete buttons. Show latest results inline.

#### Testing
- Create a monitor with 1-hour interval, verify it appears in GET /api/monitors.
- Delete it, verify it's gone.
- Manually trigger `_tick()` with a due monitor — verify results are saved and email is sent (if SMTP configured).
- Failure case: create a monitor for a nonexistent domain — must still save results (with errors), not crash the scheduler.
- Failure case: SMTP not configured — alert must log-and-continue, not crash.

---

## Phase 4: Complex Feature

### Feature 8: Lookalike Domain Scanner

**Most external-dependency-heavy feature.** Requires a permutation engine (dnstwist or custom), batch DNS resolution, and optional WHOIS lookups.

#### API Design

```
POST /api/lookalike
Authorization: Bearer <token>
Content-Type: application/json

Request:
{
  "domain": "example.com",
  "maxPermutations": 100,       // default 100, max 500
  "resolvePermutations": true,   // batch-resolve each permutation
  "whoisLookup": false           // optional WHOIS for resolved permutations
}

Response:
{
  "original": "example.com",
  "permutationsGenerated": 87,
  "resolved": [
    {
      "domain": "examp1e.com",
      "technique": "homoglyph",
      "a": ["1.2.3.4"],
      "aaaa": [],
      "whois": {"registrar": "...", "created": "..."},  // if whoisLookup
      "suspicious": true
    },
    ...
  ],
  "summary": {
    "total": 87,
    "resolved": 12,
    "suspicious": 3,
    "unregistered": 75
  }
}
```

#### Changes

**New module: `Server/syxx_server/lookalike.py` (~200 lines)**

This module implements a lightweight permutation engine (avoiding a hard dnstwist dependency, which pulls in many optional deps). Key techniques:

```python
"""Lookalike domain scanner — permutation generation + batch resolution.

Generates common typosquatting/homoglyph permutations, batch-resolves them,
and flags suspicious registrations. Lightweight: no external dependencies
beyond the existing resolver module.
"""
import concurrent.futures
import itertools
import time

from . import config, resolver, logging_util

# Homoglyph map: visually similar character substitutions
_HOMOGLYPHS = {
    'a': ['а', 'ɑ', '@', '4'], 'b': ['d', 'lb', '6'], 'c': ['e', '¢'],
    'd': ['b', 'cl', 'd'], 'e': ['3', 'é', 'ë'], 'f': ['ﬁ'],
    'g': ['9', 'q'], 'h': ['lh', 'n'], 'i': ['1', 'l', 'í', '|'],
    'j': ['i'], 'k': ['lk', 'ik'], 'l': ['1', 'i', '|'],
    'm': ['n', 'nn', 'rn'], 'n': ['m', 'r'], 'o': ['0', 'ó', 'ö'],
    'p': ['q'], 'q': ['g', 'p'], 'r': ['n'], 's': ['5', '$', 'z'],
    't': ['7', '+'], 'u': ['v', 'ú'], 'v': ['u', 'v'],
    'w': ['vv', 'w'], 'x': ['×'], 'y': ['j'], 'z': ['2', 's'],
    '0': ['o', 'O'], '1': ['l', 'i', 'I'],
}

def _generate_permutations(domain):
    """Generate permutations using multiple techniques."""
    results = set()
    parts = domain.rstrip('.').split('.')
    if len(parts) < 2:
        return []
    name = '.'.join(parts[:-1])
    tld = parts[-1]

    # 1. Omission (remove one character)
    for i in range(len(name)):
        perm = name[:i] + name[i+1:]
        if perm:
            results.add((perm + '.' + tld, 'omission'))

    # 2. Repetition (double a character)
    for i, c in enumerate(name):
        if c.isalpha():
            perm = name[:i] + c + name[i:]
            results.add((perm + '.' + tld, 'repetition'))

    # 3. Replacement (swap with adjacent key on QWERTY)
    _adjacent = {'q':'wa','w':'qe','e':'wr','r':'et','t':'ry','y':'tu',
                 'u':'yi','i':'uo','o':'ip','p':'o',
                 'a':'qs','s':'ad','d':'sf','f':'dg','g':'fh',
                 'h':'gj','j':'hk','k':'jl','l':'k',
                 'z':'x','x':'zc','c':'xv','v':'cb','b':'vn','n':'bm','m':'n'}
    for i, c in enumerate(name):
        for adj in _adjacent.get(c, []):
            perm = name[:i] + adj + name[i+1:]
            results.add((perm + '.' + tld, 'replacement'))

    # 4. Transposition (swap adjacent characters)
    for i in range(len(name) - 1):
        perm = name[:i] + name[i+1] + name[i] + name[i+2:]
        if perm != name:
            results.add((perm + '.' + tld, 'transposition'))

    # 5. Hyphenation
    for i in range(1, len(name)):
        perm = name[:i] + '-' + name[i:]
        results.add((perm + '.' + tld, 'hyphenation'))

    # 6. TLD swap
    alt_tlds = ['com', 'net', 'org', 'io', 'co', 'info', 'biz']
    for alt in alt_tlds:
        if alt != tld:
            results.add((name + '.' + alt, 'tld-swap'))

    return list(results)


def scan(domain, max_permutations=100, resolve=True, deadline=None):
    """Generate permutations and optionally resolve them.
    Returns (results_list, summary_dict) or (None, error_string)."""
    domain = domain.lower().strip().rstrip('.')
    permutations = _generate_permutations(domain)

    if len(permutations) > max_permutations:
        permutations = permutations[:max_permutations]

    if not resolve:
        return [{"domain": d, "technique": t} for d, t in permutations], {
            "total": len(permutations), "resolved": 0,
            "suspicious": 0, "unregistered": len(permutations),
        }

    resolved = []
    suspicious_count = 0
    deadline_t = deadline or (time.monotonic() + 30)

    def resolve_one(entry):
        perm_domain, technique = entry
        if time.monotonic() > deadline_t:
            return None
        try:
            resp = resolver.query(perm_domain, resolver.TYPE_A,
                                  config.PUBLIC_RESOLVERS[0], timeout=2)
            a_records = [r["value"] for r in resp["records"] if r["type"] == "A"]
            if a_records:
                return {
                    "domain": perm_domain, "technique": technique,
                    "a": a_records,
                    "aaaa": [],
                    "suspicious": True,  # resolves = potentially active lookalike
                }
            return {"domain": perm_domain, "technique": technique,
                    "a": [], "aaaa": [], "suspicious": False}
        except resolver.DNSError:
            return {"domain": perm_domain, "technique": technique,
                    "a": [], "aaaa": [], "suspicious": False}

    with concurrent.futures.ThreadPoolExecutor(max_workers=min(len(permutations), 20)) as pool:
        futs = [pool.submit(resolve_one, p) for p in permutations]
        for fut in concurrent.futures.as_completed(futs, timeout=max(1, deadline_t - time.monotonic())):
            try:
                r = fut.result()
                if r:
                    resolved.append(r)
                    if r.get("suspicious"):
                        suspicious_count += 1
            except Exception:
                pass

    unregistered = len(resolved) - suspicious_count
    return resolved, {
        "total": len(permutations), "resolved": len(resolved),
        "suspicious": suspicious_count, "unregistered": unregistered,
    }
```

**File: `app.py` — import `lookalike` (line ~29), add route**

```python
if path == "/api/lookalike":
    return self._route_lookalike()
```

Route handler:
```python
def _route_lookalike(self):
    query = urllib.parse.parse_qs("")
    ent, ok = self._require(query, config.ENFORCE_WEB)
    if not ok or not self._rate_ok(ent):
        return
    if not self._require_json():
        return
    raw = self._body()
    if self._body_error(raw):
        return
    try:
        payload = json.loads(raw or b"{}")
    except ValueError:
        return self._json_error(400, "invalid JSON")

    domain = (payload.get("domain") or "").strip()
    if not domain or len(domain) > 253:
        return self._json_error(400, "invalid domain")

    max_perm = min(int(payload.get("maxPermutations", 100)), 500)
    do_resolve = bool(payload.get("resolvePermutations", True))

    # Strict rate limit: 5 lookalike scans per hour
    if not ratelimit.allow("lookalike:" + (ent.subject if ent else self._client_ip()),
                           capacity=5, refill_millis=3600000):
        return self._json_error(429, "lookalike scan limit: 5 per hour")

    results, summary = lookalike.scan(domain, max_perm, do_resolve)
    if results is None:
        return self._json_error(400, summary)

    logging_util.incr("lookalike_scans")
    self._send(200, {
        "original": domain,
        "permutationsGenerated": summary["total"],
        "resolved": results,
        "summary": summary,
    })
```

**Frontend (index.html):** The existing `dnstwist` button (line ~270) currently shows fake output. Wire it to `/api/lookalike` POST and render real results.

Replace the existing `dnstwist` case in `runCmd()` (~line 650):

```js
case 'dnstwist':{
  mrun('dnstwist '+d+' [Pro]', M('Generating permutations and resolving…'));
  fetch('/api/lookalike',{method:'POST',headers:{'Content-Type':'application/json','Authorization':'Bearer '+localStorage.getItem('syxx_session')},body:JSON.stringify({domain:d,maxPermutations:100,resolvePermutations:true})})
    .then(r=>{if(!r.ok)throw r;return r.json()})
    .then(data=>{
      const susp=(data.resolved||[]).filter(r=>r.suspicious);
      const lines=susp.slice(0,15).map(r=>' '+r.domain+'   '+r.technique+'   '+(r.a||[]).join(',')+'  '+R('⚠ suspicious')).join('\n');
      mrun('',G('✓ '+data.summary.total+' permutations, '+data.summary.resolved+' resolve, '+data.summary.suspicious+' suspicious')+'\n'+lines);
    })
    .catch(()=>{mrun('',R('API error — check your session and try again.'));});
  break;
}
```

Also add `dnstwist` to `MEMBERS_PRO`:
```js
const MEMBERS_PRO = new Set(['mirror','warp','trace','passive','baseline','dnstwist']);
```

#### Testing
- Scan a common domain (e.g., `google.com`) — must generate permutations and resolve some.
- Verify suspicious domains are flagged correctly.
- Failure case: scan a very long domain name — must not generate excessive permutations or exceed memory.
- Failure case: hit the 5/hour rate limit — must get 429.
- Failure case: resolver timeout on some permutations — must return partial results, not crash.

---

## Dependency Graph

```
Phase 0 (no deps)
  └── Frontend only — wires existing APIs to buttons

Phase 1 (no inter-deps)
  ├── Feature 6 (CHAOS) — modifies resolver.py, extends app.py
  └── Feature 9 (API keys) — adds db.py tables, extends licensing.py

Phase 2 (depends on Phase 1 for rate limiting patterns)
  ├── Feature 4 (bulk) — uses investigate.investigate()
  └── Feature 7 (firewall) — new module, uses resolver.py

Phase 3 (depends on Phase 2)
  ├── Feature 2 (assets) — uses resolver.py + fetcher.py
  └── Feature 10 (monitors) — uses assets + investigate, depends on scheduler pattern

Phase 4 (depends on Phase 2 for resolver patterns)
  └── Feature 8 (lookalike) — new module, uses resolver.py
```

## Sequencing for Implementation

| Order | Feature | Effort | Files Touched | Risk |
|-------|---------|--------|---------------|------|
| 1 | Phase 0: Frontend wiring | S | `index.html` | Low — no server changes |
| 2 | Feature 6: CHAOS queries | S | `resolver.py`, `app.py` | Low — additive to existing code |
| 3 | Feature 9: API keys | M | `db.py`, `licensing.py`, `app.py` | Medium — new auth path |
| 4 | Feature 4: Bulk investigation | M | `app.py` | Low — thin wrapper |
| 5 | Feature 7: Firewall evasion | M | new `firewall.py`, `app.py` | Medium — curated domain list needs operator input |
| 6 | Feature 2: Asset monitoring | M | new `assets.py`, `db.py`, `app.py` | Medium — change detection logic |
| 7 | Feature 10: Scheduled monitoring | L | new `scheduler.py`, `db.py`, `app.py` | High — background thread + email |
| 8 | Feature 8: Lookalike scanner | L | new `lookalike.py`, `app.py` | High — permutation volume, resolver load |

## Shared Patterns Summary

Every new feature follows this template:

1. **Auth gate:** `ent, ok = self._require(query, config.ENFORCE_WEB)` — always first
2. **Rate limit:** `self._rate_ok(ent)` or custom `ratelimit.allow()` with stricter limits
3. **Input validation:** domain format, list bounds, JSON parsing
4. **Tri-state results:** never boolean — always `assessment` string (clean/tampered/inconclusive/filtered/open/etc.)
5. **No mocked output:** every result comes from real DNS queries via `resolver.py`
6. **Logging:** `logging_util.incr("feature_name")` for metrics
7. **DB schema:** new tables added to `db.SCHEMA`, purge logic added to `purge_expired()`
8. **Frontend:** new command button + `runCmd()` handler + add to `MEMBERS_PRO` set

## Rate Limit Configuration Summary

| Feature | Key Prefix | Capacity | Refill |
|---------|-----------|----------|--------|
| Existing (all) | `sub:<subject>` or `ip:<ip>` | default | default |
| API key creation | `keycreate:<subject>` | 5 | 24h |
| Bulk investigation | `bulk:<subject>` | 10 | 1h |
| Firewall test | standard | standard | standard |
| Asset monitor | `assets:<subject>` | 20 | 1h |
| Lookalike scan | `lookalike:<subject>` | 5 | 1h |
| Monitor CRUD | standard | standard | standard |

## New DB Tables Summary

```sql
-- Feature 9: API Keys
api_keys (id, subject, key_hash, key_prefix, label, scopes, created_at, expires_at, last_used, use_count, revoked)

-- Feature 2: Asset Snapshots
asset_snapshots (id, subject, domain, rtype, values, txt_hash, content_hash, checked_at)

-- Feature 10: Scheduled Monitors
monitors (id, subject, domain, checks, interval_hours, alert_email, status, last_run_at, next_run_at, created_at)
monitor_results (id, monitor_id, check_type, result, changes, ran_at)
```

## New Modules Summary

| Module | Lines | Purpose |
|--------|-------|---------|
| `firewall.py` | ~80 | DNS firewall evasion detection |
| `assets.py` | ~100 | Asset integrity monitoring |
| `scheduler.py` | ~120 | Background monitoring scheduler |
| `lookalike.py` | ~200 | Lookalike domain permutation + resolution |
