# WS8-FIX-PLAN.md — Code Health Fix Implementation Plan

*Generated 2026-07-24. Detailed solution designs for WS8 Phase 1 findings.*

---

## Phase 2: Solution Design — Critical Priority Fixes

### 1. Server SSRF (fetcher.py) — IPv4-mapped IPv6 bypass

**Finding:** SSRF guard can be bypassed via IPv4-mapped IPv6 addresses (`::ffff:127.0.0.1`), allowing attackers to reach internal services.

**Root Cause:** The SSRF guard only checks IPv4 loopback ranges but does not account for IPv4-mapped IPv6 addresses that resolve to the same targets.

**Solution Design:**
1. Parse all destination addresses and normalize to IPv4 before SSRF checks
2. Add IPv4-mapped IPv6 detection: `::ffff:0:0/96` prefix check
3. Reject any address that maps to loopback, link-local, or private ranges after normalization
4. Add comprehensive blocklist: `127.0.0.0/8`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `::1/128`, `fc00::/7`

**Implementation Files:**
- `Server/syxx_server/fetcher.py` — add `is_ipv4_mapped_ipv6()` and `normalize_ipv6_to_ipv4()` functions
- Update SSRF guard to use normalized addresses

**Validation:**
- Test with `::ffff:127.0.0.1` → should be rejected
- Test with `::ffff:10.0.0.1` → should be rejected
- Test with legitimate IPv6 → should pass if not in blocklist

---

### 2. Server SQL Injection (resolver.py) — Parameterized Query Missing

**Finding:** SQL-building in resolver.py uses string interpolation instead of parameterized queries, enabling direct SQL injection.

**Root Cause:** Direct string concatenation in SQL query construction without proper escaping or parameterization.

**Solution Design:**
1. Replace all string-interpolated SQL queries with parameterized queries
2. Use `cursor.execute()` with `%s` placeholders and tuple parameters
3. Add input validation for all query parameters before SQL execution
4. Add SQL injection test cases to the test suite

**Implementation Files:**
- `Server/syxx_server/resolver.py` — audit all SQL statements, convert to parameterized form
- Add `validate_sql_input()` helper function

**Validation:**
- Test with `' OR 1=1 --` injection → should be rejected/sanitized
- Test with legitimate queries → should work correctly
- Add unit tests for SQL injection vectors

---

### 3. macOS osascript Privilege Loops (AppModel.swift:589, D060)

**Finding:** Shell commands run with `osascript -e "do shell script ... with administrator privileges"` but privileges never dropped after use. Elevated handles persist.

**Root Cause:** The `osascript` command is used to run privileged operations, but the process never explicitly drops privileges after completion, leaving elevated handles open.

**Solution Design:**
1. Replace `osascript` privilege escalation with proper Authorization Services API
2. Use `AuthorizationExecuteWithPrivileges()` with explicit privilege drop after operation
3. Add privilege cleanup in `deinit` or explicit cleanup functions
4. Audit all privilege escalation points and ensure each has corresponding drop

**Implementation Files:**
- `macOS/Sources/SyxxApp/AppModel.swift:589` — replace osascript with Authorization Services
- Add `PrivilegeManager` class to handle privilege lifecycle
- Add explicit cleanup in app termination handlers

**Validation:**
- Test privilege escalation → operation completes successfully
- Verify privilege drop → no elevated handles remain after operation
- Test with multiple privilege operations → no handle leaks

---

### 4. Windows DPAPI Double-Free (WitnessKeyStore)

**Finding:** `Marshal.FreeHGlobal` called unconditionally after DPAPI CryptUnprotectData; if native alloc fails, double-free on zero-length buffer.

**Root Cause:** The code assumes DPAPI always allocates memory, but on failure it may return a zero-length buffer, causing double-free.

**Solution Design:**
1. Check if `pDataOut.cbData > 0` before calling `Marshal.FreeHGlobal`
2. Add null checks for `pDataOut.pbData` before free
3. Wrap DPAPI operations in try-catch with proper cleanup in finally block
4. Add defensive checks for all P/Invoke memory allocations

**Implementation Files:**
- `Windows/Syxx.Kit/WitnessKeyStore.cs` — add guard before `Marshal.FreeHGlobal`
- Add `SafeDPAPIHandle` wrapper class for RAII-style cleanup

**Validation:**
- Test with valid DPAPI data → should decrypt and free correctly
- Test with invalid data → should fail gracefully without double-free
- Add unit tests for DPAPI failure paths

---

### 5. macOS GUI Broken — DashboardView.swift Refactor

**Finding:** Recent DashboardView.swift refactor removed verdict card and chronicle without resizing right-column space, causing content overflow and clipped views.

**Root Cause:** Layout contract broken during refactor — right-column space not adjusted when components were removed/added.

**Solution Design:**
1. Revert or fix the DashboardView.swift layout to match mockup
2. Restore verdict card and chronicle components
3. Adjust right-column spacing to accommodate activity terminal and Cage ticker
4. Use SwiftUI `GeometryReader` for dynamic layout if needed
5. Reference `Syxx macOS GUI Mxups (standalone).html` as source of truth

**Implementation Files:**
- `macOS/Sources/SyxxApp/DashboardView.swift` — restore layout components
- Compare against mockup at `/Users/masterrabbit/Software/Syxx/Syxx GUI Mockups/Syxx macOS GUI Mxups (standalone).html`

**Validation:**
- Build and run macOS GUI → should display correctly
- Compare visual output against mockup → should match
- Test all dashboard interactions → should work without clipping

---

## Phase 2: Solution Design — High Priority Fixes

### 6. Windows WFP P/Invoke Return Codes Unchecked

**Finding:** `FwpmEngineOpen0`, `FwpmSubLayerAdd0` return codes never checked. Filter operations silently fail.

**Root Cause:** P/Invoke calls to Windows Filtering Platform functions don't check HRESULT return values.

**Solution Design:**
1. Add return value checking for all WFP P/Invoke calls
2. Throw descriptive exceptions on failure with HRESULT details
3. Add logging for all WFP operation failures
4. Add retry logic for transient WFP failures

**Implementation Files:**
- `Windows/Syxx.Kit/WfpFilterManager.cs` — add HRESULT checking
- Add `WfpException` class for WFP-specific errors

**Validation:**
- Test with valid WFP operations → should succeed
- Test with invalid operations → should throw with descriptive error
- Add unit tests for WFP failure paths

---

### 7. macOS DNSCapture I/O Storm (D067)

**Finding:** Per-packet I/O on main queue, no batching. 10,000+ packets blocks UI.

**Root Cause:** Each packet triggers immediate I/O operation on main thread without batching or background processing.

**Solution Design:**
1. Move packet processing to background queue
2. Implement packet batching (process in batches of 100-1000 packets)
3. Add rate limiting for I/O operations
4. Use `DispatchQueue.global(qos: .userInitiated)` for background processing

**Implementation Files:**
- `macOS/Sources/SyxxKit/DNSCapture.swift` — add background queue and batching
- Add `PacketBatchProcessor` class

**Validation:**
- Test with 10,000+ packets → UI should remain responsive
- Measure packet processing latency → should be acceptable
- Test with no packets → should not leak resources

---

### 8. Server Hardcoded KEYGEN_PUBLIC_KEY

**Finding:** Default value in config.py means all builds share the same trust anchor.

**Root Cause:** KEYGEN_PUBLIC_KEY has a hardcoded default instead of requiring explicit configuration.

**Solution Design:**
1. Remove hardcoded default for KEYGEN_PUBLIC_KEY
2. Make KEYGEN_PUBLIC_KEY required in environment configuration
3. Add startup validation that KEYGEN_PUBLIC_KEY is set
4. Add documentation for key generation and deployment

**Implementation Files:**
- `Server/syxx_server/config.py` — remove default, add validation
- Add preflight check in `preflight.py`

**Validation:**
- Test with KEYGEN_PUBLIC_KEY set → should start successfully
- Test without KEYGEN_PUBLIC_KEY → should fail with clear error message
- Add deployment documentation

---

### 9. Windows Synthesis O(n²) Scan

**Finding:** Rule evaluation scans all rules linearly per connection. At 500+ rules, latency degrades.

**Root Cause:** Nested loops over rule set for each connection evaluation.

**Solution Design:**
1. Implement rule indexing (hash map or trie) for O(1) rule lookup
2. Cache rule evaluation results where possible
3. Add rule prioritization to evaluate most likely matches first
4. Consider rule compilation to bytecode for faster execution

**Implementation Files:**
- `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs` — add rule indexing
- Add `RuleIndex` class for fast lookup

**Validation:**
- Test with 500+ rules → should evaluate in acceptable time
- Measure latency before/after → should show improvement
- Add performance benchmarks

---

### 10. Server World-Writable Root Logs

**Finding:** Log files owned by root but `666` permissions. Any local user can read/write logs.

**Root Cause:** Log files created with overly permissive permissions.

**Solution Design:**
1. Set log file permissions to `600` (owner read/write only)
2. Ensure log directory has `700` permissions
3. Add permission validation in log file creation
4. Add log rotation with proper permission inheritance

**Implementation Files:**
- `Server/syxx_server/logging_util.py` — fix permission settings
- Add `ensure_log_permissions()` function

**Validation:**
- Check log file permissions → should be `600`
- Check log directory permissions → should be `700`
- Test log rotation → new logs should have correct permissions

---

## Phase 2: Solution Design — Medium Priority Fixes

### 11-17. Theme Token and GUI Layout Fixes

**Summary:** 11 Warden theme token mismatches, 28 GUI layout deviations across platforms.

**Solution Design:**
1. Create centralized theme token definition file
2. Add automated token validation in build process
3. Fix each layout deviation by comparing against HTML mockups
4. Add visual regression tests

**Implementation Files:**
- Theme token files for each platform
- GUI view files for each platform
- Build scripts for token validation

---

## Phase 3: Implementation Priority Order

1. **Critical Security Fixes (Immediate):**
   - Server SSRF (1)
   - Server SQL Injection (2)
   - macOS Privilege Loops (3)
   - Windows DPAPI Double-Free (4)

2. **Critical Functionality Fixes (Immediate):**
   - macOS GUI Broken (5)

3. **High Priority (Within 1 week):**
   - Windows WFP Return Codes (6)
   - macOS DNSCapture I/O Storm (7)
   - Server Hardcoded Keys (8)
   - Windows Synthesis O(n²) (9)
   - Server Log Permissions (10)

4. **Medium Priority (Within 2 weeks):**
   - Theme token fixes (11-17)
   - GUI layout fixes (18-45)
   - Remaining code health findings

---

## Next Steps

1. Begin implementation of Critical Security Fixes (1-4)
2. Fix macOS GUI (5) to restore functionality
3. Implement High Priority fixes (6-10)
4. Address Medium Priority fixes (11-45)
5. Comprehensive testing and verification

*Plan updated 2026-07-24. Ready for Phase 3 implementation.*
