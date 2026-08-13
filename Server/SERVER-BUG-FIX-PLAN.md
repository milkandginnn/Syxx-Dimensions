# Server Bug Fix Plan

**Date:** 2026-07-26  
**Scope:** Deep inspection of Server/syxx_server for bugs and implementation errors
**Updated:** 2026-07-26 (peer review incorporated)

## Summary

A comprehensive audit of the server codebase identified bugs and implementation errors across multiple modules. After peer review, the findings have been re-evaluated. The peer correctly identified that several findings are intentional design choices or have safe failure modes. The plan has been updated to reflect the consensus view.

## Bugs Identified

### 1. **LOW: app.py - Platform fallback in _route_startup**
**File:** `app.py:730-753`  
**Severity:** Low  
**Category:** Data Integrity / User Experience

**Issue:**
The `_route_startup` function uses `.get(raw, "mac")` to map platform strings to canonical keys. If a client sends an unrecognized platform string, it falls back to macOS content and update channel.

**Peer Review:**
The peer correctly notes this is intentional design documented in the code comments. The fallback serves edge cases (e.g., unrecognized platforms) with reasonable default content rather than failing.

**Status:** **Not a bug** - Intentional design choice. No action required.

---

### 2. **NOT A BUG: config.py - Hardcoded production account ID**
**File:** `config.py:272`  

**Issue:**
`KEYGEN_ACCOUNT_ID` has a hardcoded default value.

**Peer Review:**
The peer correctly identifies that the Keygen account ID is a public identifier, not a secret. It is embedded in every Keygen API URL and is overridable via environment variable. The security boundary is the license key and admin token, not the account ID.

**Status:** **Not a bug** - By design, standard practice for Keygen integrations. No action required.

---

### 3. **LOW: db.py - Missing error handling in backup() file operations**
**File:** `db.py:554-601`  
**Severity:** Low  
**Category:** Operator Experience / Reliability

**Issue:**
The `backup()` function performs file operations (rename, remove) without explicit error handling. If `os.replace()` fails due to permission issues or disk full, the error propagates uncaught.

**Peer Review:**
The peer agrees this is valid but low severity, noting the caller (background thread) would catch the exception. Adding explicit error handling with logging would improve operator awareness.

**Status:** **Valid hardening improvement** - Not a bug, but worth adding for operator experience.

**Proposed Fix:**
```python
try:
    os.replace(tmp, out)
except OSError as e:
    logging_util.error("backup_rename_failed", src=tmp, dst=out, err=str(e))
    try:
        os.remove(tmp)  # Clean up temp file
    except OSError:
        pass
    raise  # Re-raise so caller knows backup failed

# ...
for stale in backups[:-keep]:
    try:
        os.remove(os.path.join(dest_dir, stale))
    except OSError as e:
        logging_util.warn("backup_prune_failed", file=stale, err=str(e))
        # Continue - prune failure is non-critical
```

---

### 4. **LOW: warp.py - Missing error handling for corrupted cache**
**File:** `warp.py:102-106`  
**Severity:** Low  
**Category:** Reliability / Error Handling

**Issue:**
The `build_baseline()` function assumes cached data is valid JSON. If the cache is corrupted (disk corruption, manual editing, etc.), `json.loads()` will raise an exception that crashes the request handler.

**Peer Review:**
The peer claims existing error handling catches this. However, reviewing the code shows no try/except around `json.loads()` in `build_baseline()`. The peer may be mistaken. Adding error handling would improve reliability.

**Status:** **Valid hardening improvement** - Not a critical bug, but worth adding for reliability.

**Proposed Fix:**
```python
cached = db.warp_cache_get(final_url, config.WARP_CACHE_TTL)
if cached:
    try:
        result = json.loads(cached)
        logging_util.incr("warp_cache_hit")
        return result
    except (json.JSONDecodeError, ValueError) as e:
        logging_util.warn("warp_cache_corrupted", url=final_url, err=str(e))
        # Fall through to rebuild baseline
```

---

### 5. **LOW: resolver.py - Missing depth check logging in NS resolution**
**File:** `resolver.py:746-757`  
**Severity:** Low  
**Category:** Diagnostics / Operator Experience

**Issue:**
When glue records are not available, the code recursively calls `resolve_authoritative()` to resolve NS names. When `sub_depth <= 0`, the code silently skips without logging. This makes debugging delegation issues harder.

**Status:** **Valid hardening improvement** - Not a bug, but worth adding for better diagnostics.

**Proposed Fix:**
```python
ns_resolved = False
for nsname in ns_names[:2]:
    try:
        sub_depth = min(3, max_depth - depth - 1)
        if sub_depth <= 0:
            logging_util.warn("auth_ns_depth_exceeded", nsname=nsname, depth=depth)
            continue
        sub = resolve_authoritative(nsname, TYPE_A, timeout, sub_depth)
        if sub.get("authoritative") and sub["authoritative"]["answers"]:
            ip = sub["authoritative"]["answers"][0]
            if _ip_is_public(ip):
                next_servers.append({"ip": ip, "label": nsname})
                ns_resolved = True
                break
    except DNSError as e:
        logging_util.warn("auth_ns_resolution_failed", nsname=nsname, err=str(e))
        continue

if not ns_resolved and not next_servers:
    delegation.append({"zone": delegated_zone, "server": None, 
                       "error": "could not resolve any nameserver (NS resolution failed)"})
    return {"name": name, "qtype": qtype, "delegation": delegation, 
            "authoritative": None, "verdict": "inconclusive"}
```

---

### 6. **NOT A BUG: investigate.py - Underscore in hostname regex**
**File:** `investigate.py:19`  

**Issue:**
The hostname regex `_HOSTNAME` allows underscores in domain labels.

**Peer Review:**
The peer correctly notes that underscores are ubiquitous in DNS practice: SRV records (`_sip._tcp.example.com`), DKIM selectors (`google._domainkey.example.com`), DMARC (`_dmarc.example.com`). Rejecting them would break legitimate use cases. The regex is intentionally permissive.

**Status:** **Not a bug** - Intentionally permissive to support real-world DNS use cases. No action required.

---

### 7. **NOT A BUG: config.py - Missing validation for KEYGEN_PUBLIC_KEY format**
**File:** `config.py:281`  

**Issue:**
The `KEYGEN_PUBLIC_KEY` is read as a plain string with no format validation.

**Peer Review:**
The peer correctly notes that the key defaults to empty string, and `_verify_validate_key_response()` checks if the key is empty before using it. An invalid key format would cause signature verification to fail, which treats the license as forged - a safe failure mode.

**Status:** **Not a bug** - Safe failure mode. Early validation would be an operator experience improvement, not a bug fix.

---

## Implementation Status

### Hardening Improvements

1. **db.py backup() error handling** - PARTIALLY FIXED
   - Lines 589-593 now have try/except with logging around `os.replace()`
   - Missing: temp file cleanup on failure (low priority, caller catches exception)

2. **warp.py cache corruption handling** - NOT FIXED
   - Lines 103-106 still lack try/except around `json.loads(cached)`
   - Would crash on corrupted cache entries

3. **resolver.py NS resolution logging** - NOT FIXED
   - Lines 746-750 still lack logging when `sub_depth <= 0`
   - Silent skip makes debugging harder

### Peer Findings

1. **DEF-11: app.py value.lower()** - FIXED
   - Line 248 now uses `str(r["value"]).lower()` for defensive type handling

2. **DEF-12: dnssec_validator.py IDNA encoding** - FIXED
   - Lines 36-38 now use `domain.encode("idna")` for internationalized domain names
   - Fallback to ASCII with replacement on encoding failure

## Testing Requirements

For each hardening improvement:
- Add unit tests covering the error path
- Verify no regression in existing functionality

## Cross-Platform Impact

- All changes are server-side only
- No impact on macOS, Windows, or Linux clients

## Notes

- The codebase shows strong security awareness (SSRF protections, DNS rebinding guards, signature verification)
- Peer review correctly identified that most findings are intentional design choices or have safe failure modes
- The remaining items are hardening improvements for operator experience and reliability, not critical bugs
- DEF-12 (IDNA encoding) is a real correctness bug in dnssec_validator.py that should be addressed when that module is next touched
