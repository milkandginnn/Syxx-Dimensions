# Delta Remediation Plan

**Generated:** 2026-08-10T01:23:52
**Mode:** auto (--detail 5 granular)
**Worklist:** 194 deltas (96 FIX NOW, 98 FIX NEXT)
**Phases:** 53 total
**Merges applied:** 6 (see below)

## Merges Applied

| Merge Into | Merged From | Reason |
|------------|-------------|--------|
| A003 | A006 | A006 description incorrect; actual issue covered by A003 |
| D009 | D052 | D052 is duplicate of D009 |
| D023 | D037 | D037 near-duplicate of D023 |
| D054 | D072 | Same root cause |
| D120 | D144 | Duplicate |
| B002 | B020 | B020 overlaps B002 significantly |

---

## Phase 1: Dependency Updates (23 deltas, ~16-24h)

**Priority:** FIX NOW
**Deltas:** A001, A002, A005, A013, A014, A031, A032, A033, A036, A053, A059, A060, A069, A084, C002, C003, C006, C007, C008, C009, C010, C011, C013

### A001 — Passive DNS drift boolean fallback collapses tri-state

- **Location:** Server/syxx_server/passivedns.py:121
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients to handle tri-state drift string.
- **Entry points:** passivedns.py:123; Server/syxx_server/passivedns.py:121
- **Files:** Server/syxx_server/passivedns.py, passivedns.py
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A002 — IDN/Punycode bypass of per-run unique-host cap enables SSRF fan-out

- **Location:** Server/syxx_server/warp.py:339
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Convert hostnames to punycode canonical form before adding to the host budget. Use encodings.idna.ToASCII + ToUnicode for round-trip normalization, or enforce ASCII-only hostnames in the manifest.
- **Entry points:** Server/syxx_server/warp.py:339
- **Files:** Server/syxx_server/warp.py, xa9.com/a.js, xn--caf-dma.com/a.js
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** None

### A005 — Server exposes two different key_id schemes from the same Ed25519 key

- **Location:** Server/syxx_server/app.py:1933; Server/syxx_server/leak.py:158-161
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Unify on one scheme: update leak.collector_key_id() to return hex form (breaking change for leak-attestation clients). [Fix 2] Add a separate /api/pubkey field "warpKeyId" for the warp format. [Fix 3] Document the difference prominently in the /api/pubkey response.
- **Entry points:** Server/syxx_server/app.py:1933; Server/syxx_server/leak.py:158
- **Files:** Server/syxx_server/app.py, Server/syxx_server/leak.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A013 — IDN domain corruption in DNSSEC validator — `encode("ascii","replace")`

- **Location:** Server/syxx_server/dnssec_validator.py:84
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Raise `DNSError` instead of falling back to `encode("ascii", "replace")`. The caller already handles `DNSError` gracefully. The IDNA path should be the only path; if it fails, the domain is unresolvable for DNSSEC purposes.
- **Entry points:** Server/syxx_server/dnssec_validator.py:84; Server/syxx_server/dnssec_validator.py:73
- **Files:** Server/syxx_server/dnssec_validator.py
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** None

### A014 — backup() leaves stale .partial tmp files on error — no finally cleanup

- **Location:** Server/syxx_server/db.py:986-998
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add a `finally` block that removes the tmp file on error. [Fix 2] Add error-path tests for both failure modes.
- **Entry points:** Server/syxx_server/db.py:986
- **Files:** Server/syxx_server/db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A031 — Tests use unittest.TestCase alongside pytest inconsistently

- **Location:** Server/tests/ (~15 files)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Migrate `unittest.TestCase` tests to pytest-native using fixtures. [Fix 2] Use `pytest.fixture` for DB setup instead of manual `conftest.py` session management. [Fix 3] Standardize on pytest throughout the test suite.
- **Entry points:** Server/tests/ (~15 files)
- **Files:** Server/tests/test_something.py, conftest.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A032 — cryptography is 9 major versions behind — exposed to CVE-2026-69249

- **Location:** Server/requirements.txt (constraint: `cryptography>=41.0.0`)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Pin to `cryptography>=49.0.0` (first version with CVE-2026-69249 fix). [Fix 2] Add `pip-audit` to CI for automated vulnerability scanning. [Fix 3] Migrate Server to uv with lockfile for reproducible builds.
- **Entry points:** Server/requirements.txt (constraint: `cryptography>=41.0.0`)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A033 — sentry-sdk[opentelemetry] is 47 minor versions behind — missing OTel

- **Location:** Server/requirements.txt (constraint: `sentry-sdk[opentelemetry]>=2.19`)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Pin to `sentry-sdk[opentelemetry]>=2.66.0`.
- **Entry points:** Server/requirements.txt (constraint: `sentry-sdk[opentelemetry]>=2.19`)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A036 — brotli is 1 minor version behind — missing performance improvements

- **Location:** Server/requirements.txt (constraint: `brotli>=1.1.0`)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Bump to `brotli>=1.2.0`.
- **Entry points:** Server/requirements.txt (constraint: `brotli>=1.1.0`)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A053 — N+1 sequential DNS queries in firewall_test — 120 DNS queries, all

- **Location:** Server/syxx_server/firewall_test.py:120, 170, 210, 220
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Use ThreadPoolExecutor for parallel DNS queries. [Fix 2] Batch queries by resolver to reduce round-trips.
- **Entry points:** Server/syxx_server/firewall_test.py:120; firewall_test.py:210
- **Files:** Server/syxx_server/firewall_test.py, firewall_test.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A059 — Server OTLP log records lack trace context — log records disconnected

- **Location:** Server/syxx_server/logging_util.py:77-85
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Include trace_id and span_id from the current OTel context in LogRecord.
- **Entry points:** Server/syxx_server/logging_util.py:77; logging_util.py:77
- **Files:** Server/syxx_server/logging_util.py, logging_util.py
- **Edge cases:** Test: concurrent access
- **Rollback:** git revert <commit>
- **Depends on:** None

### A060 — Stripe SPKI pin list is loaded but never enforced on checkout

- **Location:** Server/syxx_server/stripe_checkout.py:45-50, 175
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Call `_verify_spki_pin()` on the peer certificate after the TLS handshake succeeds. [Fix 2] Fail closed when pins are configured but no certificate matches.
- **Entry points:** Server/syxx_server/stripe_checkout.py:45
- **Files:** Server/syxx_server/stripe_checkout.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A069 — RRL pruning is dead code — _rrl_buckets grows unbounded (memory leak)

- **Location:** Server/syxx_server/dns_authoritative.py:85-90
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Move pruning into the `with` block before the returns, or use a separate periodic pruning task.
- **Entry points:** Server/syxx_server/dns_authoritative.py:85; dns_authoritative.py:70
- **Files:** Server/syxx_server/dns_authoritative.py, dns_authoritative.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### A084 — `ResolverConsensusEngine` force-unwraps `dohEndpoints[name]!` — crash

- **Location:** macOS/Sources/SyxxKit/ResolverConsensusEngine.swift:179, 181, 258
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Replace `!` subscript with a nil-guard that returns a `.transportError` outcome when no endpoint is configured for the name.
- **Entry points:** macOS/Sources/SyxxKit/ResolverConsensusEngine.swift:179
- **Files:** macOS/Sources/SyxxKit/ResolverConsensusEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### C002 — WitnessWireJson.cs — IEEE-754 ULP-nudging date math is fragile

- **Location:** Windows/dotNET/Syxx.Kit/WitnessWireJson.cs:200-240
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add comprehensive tests for the ULP-nudging code with edge cases (dates near epoch, dates far in future, leap seconds). [Fix 2] Document the underlying Foundation/.NET date encoding mismatch in the module header. [Fix 3] Consider a protocol-level fix — use an integer milliseconds-since-epoch
- **Entry points:** Windows/dotNET/Syxx.Kit/WitnessWireJson.cs:200; WitnessWireJson.cs:200
- **Files:** Windows/dotNET/Syxx.Kit/WitnessWireJson.cs, WitnessWireJson.cs
- **Edge cases:** Test: boundary values
- **Rollback:** git revert <commit>
- **Depends on:** None

### C003 — HasChanges API divergence — Linux uses computed property, Windows uses

- **Location:** Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffResult.cs:62;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Use the computed property pattern on both platforms. [Fix 2] Extract the Mirror/ engine to Syxx.Kit (D009) so there is only one implementation.
- **Entry points:** Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffResult.cs:62; Windows/dotNET/SyxxUI/Mirror/DiffResult.cs:69
- **Files:** Linux/Ubuntu/SyxxUI.Avalonia/Mirror/DiffResult.cs, Windows/dotNET/SyxxUI/Mirror/DiffResult.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### C006 — C# Assemble drops hard-excluded diagnoses from the clean-blocking

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:1193-1201,
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Remove the `s.Reason != HardExcluded` predicate from the overhang computation in Assemble (:1194) so hard-excluded diagnoses block clean exactly as on macOS. [Fix 2] Keep hardExcluded out of `surfaceSuppressed` (:1199) for display, but never out of the clean-blocking overhang. [Fix 3] Add a 
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:1193; macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:982; MatrixDeterminativeEngine.cs:1193
- **Files:** MatrixDeterminativeEngine.cs, MatrixDeterminativeEngine.swift, Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs, macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B025, B031, B026

### C007 — C# DNS spoofed-then-timeout collapses to `.timeout` where macOS

- **Location:** Windows/dotNET/Syxx.Kit/DNSResolver.cs:244-257, 284-313;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Match macOS ordering — surface `.responseIdMismatch` when an ID mismatch is observed before the deadline instead of overwriting with `.timeout`. [Fix 2] Add a parity replay vector covering the spoofed-then-timeout case in corpus/probe-parity.
- **Entry points:** Windows/dotNET/Syxx.Kit/DNSResolver.cs:244; macOS/Sources/SyxxKit/DNSResolver.swift:423
- **Files:** DNSResolver.cs, Windows/dotNET/Syxx.Kit/DNSResolver.cs, macOS/Sources/SyxxKit/DNSResolver.swift
- **Edge cases:** Test: timeout/slow responses
- **Rollback:** git revert <commit>
- **Depends on:** B033, B005

### C008 — Collector-less Glutton runs promote a never-executed probe to

- **Location:** Windows/dotNET/Syxx.Kit/ProbePlan.cs:224-226;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Map `Signature == null` to `Untested` on both platforms. [Fix 2] Reserve `Invalidated` for probes that actually ran and were structurally unusable.
- **Entry points:** Windows/dotNET/Syxx.Kit/ProbePlan.cs:224; Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:761; Windows/dotNET/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs:370
- **Files:** Windows/dotNET/Syxx.Kit/ProbePlan.cs, Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs, Windows/dotNET/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs, macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** B007, B044, B039

### C009 — Conclusions scan drops accumulated session evidence and falls back to

- **Location:** Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs:62-66;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Pass the current session/domain into the conclusions scan path. [Fix 2] Do not default to `github.com` when the user is on the conclusions surface. [Fix 3] Preserve prior session evidence unless the user explicitly requests a fresh scan.
- **Entry points:** Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs:62; Windows/dotNET/Syxx.Kit/Synthesis/FirstRunScan.cs:41
- **Files:** Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs, Windows/dotNET/Syxx.Kit/Synthesis/FirstRunScan.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### C010 — Windows CaptureEngine BothFailed produces Completed outcome via empty

- **Location:** Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs:125-128
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add synthetic error to _loopErrors. [Fix 2] Return Aborted in OutcomeAsync when no capture loops ran.
- **Entry points:** Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs:125; CaptureEngine.cs:125
- **Files:** CaptureEngine.cs, Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### C011 — macOS PrivilegeManager failed helper install permanently suppresses

- **Location:** macOS/Sources/SyxxApp/PrivilegeManager.swift:145-155
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Reset hasAttemptedInstall = false in catch block. [Fix 2] Add "Retry Installation" button in UI.
- **Entry points:** macOS/Sources/SyxxApp/PrivilegeManager.swift:145; PrivilegeManager.swift:149
- **Files:** PrivilegeManager.swift, macOS/Sources/SyxxApp/PrivilegeManager.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### C013 — `AnswerLedger.trimEntriesPreservingLastPerKey` anchor key omits

- **Location:** macOS/Sources/SyxxKit/Trust/AnswerLedger.swift:188
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Include `networkKey` in the anchor key: `"\(e.domain)|\(e.queryType)|\(e.networkKey ?? "")"`.
- **Entry points:** macOS/Sources/SyxxKit/Trust/AnswerLedger.swift:188; AnswerLedger.swift:188
- **Files:** AnswerLedger.swift, macOS/Sources/SyxxKit/Trust/AnswerLedger.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 2: Warp & Content Integrity (7 deltas, ~8-12h)

**Priority:** FIX NOW
**Deltas:** A003, A011, A016, A018, A070, A081, C012

### A003 — No Unicode normalization of hostnames in host budget comparison

- **Location:** Server/syxx_server/warp.py:339, 442
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Apply unicodedata.normalize('NFC', host.lower()) to all hostnames before adding to host budget set.
- **Entry points:** Server/syxx_server/warp.py:339
- **Files:** Server/syxx_server/warp.py
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** A002

### A011 — json.dumps with ensure_ascii=False produces non-normalized UTF-8 signed

- **Location:** Server/syxx_server/app.py:1925
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Normalize string values in the manifest dict before serialization using unicodedata.normalize('NFC', ...). Apply to origin_url and final_url fields.
- **Entry points:** Server/syxx_server/app.py:1925
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** A005

### A016 — dnssec_validator.py has zero test coverage — 785-line crypto module

- **Location:** Server/syxx_server/dnssec_validator.py (entire file, 785 lines)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Create `test_dnssec_validator.py` with tests for `_nw` IDN encoding, `_parse` response parsing, `_dsm` DS matching, `_vsig` signature validation, `validate_chain` happy path, and `verify_denial_of_existence`. [Fix 2] Use known test vectors (RFC-compliant DNSSEC-signed zones with published RR
- **Entry points:** Server/syxx_server/dnssec_validator.py:1
- **Files:** Server/syxx_server/dnssec_validator.py, dnssec_validator.py, test_dnssec_validator.py
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** A013

### A018 — db.py is a 1,351-line data access monolith with no repository classes

- **Location:** Server/syxx_server/db.py (entire file, 1,351 lines)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Split into domain-specific repository modules: `license_repo.py`, `leak_repo.py`, `monitor_repo.py`, etc. [Fix 2] Each repository owns its schema, migrations, and CRUD. [Fix 3] Keep `db.py` as the connection pool and migration runner.
- **Entry points:** Server/syxx_server/db.py (entire file, 1,351 lines)
- **Files:** Server/syxx_server/db.py, db.py, leak_repo.py, license_repo.py, monitor_repo.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A014

### A070 — _dns_rate_buckets has no pruning — unbounded memory growth

- **Location:** Server/syxx_server/dns_authoritative.py:30-51
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add periodic pruning or max-size eviction.
- **Entry points:** Server/syxx_server/dns_authoritative.py:30; dns_authoritative.py:30
- **Files:** Server/syxx_server/dns_authoritative.py, dns_authoritative.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A069

### A081 — N+1 query in passivedns.ingest() — SELECT LIMIT 101 before every INSERT

- **Location:** Server/syxx_server/passivedns.py:24-30
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Use COUNT(DISTINCT rtype, value) with LIMIT 101 shortcut.
- **Entry points:** Server/syxx_server/passivedns.py:24; passivedns.py:24
- **Files:** Server/syxx_server/passivedns.py, passivedns.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A001

### C012 — `MatrixDeterminativeEngine` gates `.untested`/`.locked` sections as

- **Location:** macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:884-895
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Check `hardGateOnMissingRequiredSections` (or a dedicated `hardGateOnUntestedRequiredSections`) before gating on present-but- untested sections, matching the intent of the flag for absent sections.
- **Entry points:** macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:884; MatrixDeterminativeEngine.swift:891
- **Files:** MatrixDeterminativeEngine.swift, macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** C006

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
```

---

## Phase 3: Warp & Content Integrity (6 deltas, ~6-8h)

**Priority:** FIX NOW
**Deltas:** A004, A012, A017, A021, A071, C001

### A004 — _envelope_decode function name conflates Content-Encoding with Transfer-

- **Location:** Server/syxx_server/warp.py:218, 233, 269, 277
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Rename _envelope_decode to _content_encoding_decode and update all call sites. [Fix 2] Update spec \xc2\xa78 to use "Content-Encoding" instead of "transfer envelope" / "envelope-decoded."
- **Entry points:** Server/syxx_server/warp.py:218
- **Files:** Server/syxx_server/warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A003

### A012 — In-memory lockout counters reset on server restart

- **Location:** Server/syxx_server/app.py:1586 (_ACTIVATE_FAILURES dict),
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Persist failure counts to the SQLite kv table with a TTL equal to the lockout window. The kv table already supports timestamped entries and purge_expired already cleans them. This is a follow-up, not a blocker.
- **Entry points:** Server/syxx_server/app.py:1586; Server/syxx_server/app.py:1667
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test: boundary values, server restart / crash recovery, adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A011

### A017 — DNS wire protocol duplicated between dnssec_validator.py and resolver.py

- **Location:** Server/syxx_server/dnssec_validator.py:89-200;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract shared DNS query/parse into a single implementation in `resolver.py`. [Fix 2] Have `dnssec_validator` use `resolver` for all transport-layer operations, keeping only DNSSEC-specific validation logic.
- **Entry points:** Server/syxx_server/dnssec_validator.py:89; Server/syxx_server/resolver.py:638; dnssec_validator.py:110
- **Files:** Server/syxx_server/dnssec_validator.py, Server/syxx_server/resolver.py, dnssec_validator.py, resolver.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A016

### A021 — Circular dependency workarounds in config.py, db.py, licensing.py

- **Location:** Server/syxx_server/config.py:39;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract shared dependencies into a `core.py` or `types.py` that both modules can import without cycles. [Fix 2] Use dependency injection — pass the HMAC secret and db handle as parameters rather than importing them.
- **Entry points:** Server/syxx_server/config.py:39; Server/syxx_server/db.py:485; Server/syxx_server/licensing.py:521
- **Files:** Server/syxx_server/config.py, Server/syxx_server/db.py, Server/syxx_server/licensing.py, config.py, core.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A018

### A071 — _token_obs_counts has no pruning — permanent counter per leak token

- **Location:** Server/syxx_server/dns_authoritative.py:97-108
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add periodic pruning or TTL-based eviction.
- **Entry points:** Server/syxx_server/dns_authoritative.py:97; dns_authoritative.py:97
- **Files:** Server/syxx_server/dns_authoritative.py, dns_authoritative.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A070

### C001 — Passive DNS drift boolean fallback collapses tri-state

- **Location:** Server/syxx_server/passivedns.py:121
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients to handle tri-state drift string.
- **Entry points:** passivedns.py:123; Server/syxx_server/passivedns.py:121
- **Files:** Server/syxx_server/passivedns.py, passivedns.py
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** B001

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 4: Warp & Content Integrity (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A006, A027

### A006 — Python str.lower() is locale-sensitive for hostname normalization

- **Location:** Server/syxx_server/warp.py:339, 442
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Replace .lower() with .casefold() which is locale-insensitive. [Fix 2] Remove the redundant call since urlsplit already lowercases.
- **Entry points:** Server/syxx_server/warp.py:339
- **Files:** Server/syxx_server/warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A004, A003

### A027 — resolve_authoritative — 6 levels of nesting, ~160 lines, 8 return sites

- **Location:** Server/syxx_server/resolver.py:830-990
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract glue record resolution into a dedicated function. [Fix 2] Extract bailiwick validation into a dedicated function. [Fix 3] Reduce nesting by using early returns for error conditions.
- **Entry points:** Server/syxx_server/resolver.py:830
- **Files:** Server/syxx_server/resolver.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A017

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 5: Warp & Content Integrity (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A007, A030

### A007 — _envelope_decode does not handle multiple Content-Encoding values

- **Location:** Server/syxx_server/warp.py:226-233
- **Decision:** FIX NOW
- **Approach:** [Fix 1] If multi-encoding support is desired, split on comma and decode in reverse order. Given the rarity and correct fail-safe behavior, this may be deferred.
- **Entry points:** Server/syxx_server/warp.py:226
- **Files:** Server/syxx_server/warp.py
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** A006

### A030 — No unified DNS query interface — 5 different query paths for 3 transports

- **Location:** Server/syxx_server/resolver.py;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Define a `DNSQuery` protocol/ABC with `query(domain, rtype, flags)` → `DNSResponse`. [Fix 2] Implement concrete transports (UDP, TCP, DoH, System) conforming to the protocol. [Fix 3] Have `dnssec_validator` use the shared transport layer.
- **Entry points:** Server/syxx_server/resolver.py
- **Files:** Server/syxx_server/dnssec_validator.py, Server/syxx_server/dot_listener.py, Server/syxx_server/resolver.py
- **Edge cases:** Test: timeout/slow responses
- **Rollback:** git revert <commit>
- **Depends on:** A027, A017

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 6: Warp & Content Integrity (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A008, A073

### A008 — Server uses time.time() (wall clock) for manifest timestamps without clock-

- **Location:** Server/syxx_server/warp.py:309
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Document the clock-source dependency in the spec and code comments. [Fix 2] Consider using a monotonic clock source anchored to a known epoch for the timestamp, though this would require a protocol change.
- **Entry points:** Server/syxx_server/warp.py:309
- **Files:** Server/syxx_server/warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A007

### A073 — DoT listener has no restart mechanism — silent daemon death on

- **Location:** Server/syxx_server/dot_listener.py:147-151
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add _rebind_loop pattern matching dns_authoritative.py.
- **Entry points:** Server/syxx_server/dot_listener.py:147; dot_listener.py:147
- **Files:** Server/syxx_server/dot_listener.py, dns_authoritative.py, dot_listener.py
- **Edge cases:** Test: server restart / crash recovery
- **Rollback:** git revert <commit>
- **Depends on:** A030, A071

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 7: Warp & Content Integrity (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A009, A074

### A009 — No upper bound on freshness_seconds emitted by the server

- **Location:** Server/syxx_server/warp.py:310
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Clamp at the server: min(config.WARP_MANIFEST_FRESHNESS, 600). [Fix 2] Document the ceiling in the config.
- **Entry points:** Server/syxx_server/warp.py:310
- **Files:** Server/syxx_server/warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A008

### A074 — _serve_dns() has no top-level exception handler — unexpected exceptions

- **Location:** Server/syxx_server/dns_authoritative.py:294-319
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add catch-all exception handler with logging and restart.
- **Entry points:** Server/syxx_server/dns_authoritative.py:294; dns_authoritative.py:294
- **Files:** Server/syxx_server/dns_authoritative.py, dns_authoritative.py
- **Edge cases:** Test: server restart / crash recovery
- **Rollback:** git revert <commit>
- **Depends on:** A073, A071

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 13: Server API Endpoints (4 deltas, ~6-8h)

**Priority:** FIX NOW
**Deltas:** A023, A034, A035, A051

### A023 — _env_int helper duplicated across 3 modules — copy-paste pattern

- **Location:** Server/syxx_server/app.py:233;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Export `_env_int` from `config.py` and have all modules import it. [Fix 2] Standardize on `config._i()` directly.
- **Entry points:** Server/syxx_server/app.py:233; Server/syxx_server/investigate.py:32; Server/syxx_server/db.py:31
- **Files:** Server/syxx_server/app.py, Server/syxx_server/db.py, Server/syxx_server/investigate.py, app.py, config.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A022, A020, A021

### A034 — Server uses pip not uv — no lockfile, no reproducible builds, no

- **Location:** Server/ (no uv.lock, no Pipfile.lock, no poetry.lock)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add a proper `[project]` section to `Server/pyproject.toml`. [Fix 2] Add `[tool.uv]` section and generate `uv.lock`. [Fix 3] Pin all transitive dependencies via the lockfile.
- **Entry points:** Server/ (no uv.lock, no Pipfile.lock, no poetry.lock)
- **Files:** Server/pyproject.toml
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** A022

### A035 — idna package is a transitive dependency of cryptography — should be

- **Location:** Server/requirements.txt (idna not listed)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add `idna>=3.7` as an explicit direct dependency in `requirements.txt`.
- **Entry points:** Server/requirements.txt (idna not listed)
- **Files:** dnssec_validator.py
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** A022

### A051 — leak.py _ensure_collector_key raises RuntimeError at import time —

- **Location:** Server/syxx_server/leak.py:89-97
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add try/except RuntimeError in /api/pubkey handler to return structured error instead of 500.
- **Entry points:** Server/syxx_server/leak.py:89; leak.py:93
- **Files:** Server/syxx_server/leak.py, leak.py, preflight.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A022, A005

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 14: Server API Endpoints (3 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A024, A026, C005

### A024 — _diff_content and _route_diff use different comparison logic for the

- **Location:** Server/syxx_server/app.py:265-354 vs app.py:2062-2180
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract both into a shared `diff.py` module. [Fix 2] Define a common tri-state comparison interface. [Fix 3] Add tests that verify both paths produce consistent results for the same input.
- **Entry points:** Server/syxx_server/app.py:265; app.py:2062; app.py:265
- **Files:** Server/syxx_server/app.py, app.py, diff.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A015, A023

### A026 — _dnssec_observed deeply nested — ~120 lines, 5 levels of nesting,

- **Location:** Server/syxx_server/investigate.py:230-350
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract into a dedicated `dnssec_observation.py` module. [Fix 2] Each transport branch becomes a separate, testable function.
- **Entry points:** Server/syxx_server/investigate.py:230
- **Files:** Server/syxx_server/investigate.py, dnssec_observation.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A023

### C005 — cryptography CVE-2026-69249 exposes Ed25519 leak attestation signing

- **Location:** Server/requirements.txt (constraint: `cryptography>=41.0.0`);
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Pin to `cryptography>=49.0.0`. [Fix 2] Verify attestation signing still works after upgrade. [Fix 3] Add regression test for Ed25519 attestation signing.
- **Entry points:** Server/requirements.txt (constraint: `cryptography>=41.0.0`)
- **Files:** Server/syxx_server/leak.py, leak.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A051

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 15: Server API Endpoints (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A025, A047

### A025 — seo.py duplicates pricing constants from app.py — not shared via config

- **Location:** Server/syxx_server/seo.py:33-34;
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Move pricing constants to `config.py` as the single source of truth. [Fix 2] Have both `app.py` and `seo.py` import from config.
- **Entry points:** Server/syxx_server/seo.py:33; Server/syxx_server/app.py:178; seo.py:33
- **Files:** Server/syxx_server/app.py, Server/syxx_server/seo.py, app.py, config.py, seo.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A024, A015, A023

### A047 — investigate.py _resolve_all uses type(e).__name__ in broad except —

- **Location:** Server/syxx_server/investigate.py:122-127
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Use traceback.format_exception_only(type(e), e). [Fix 2] Include e.__cause__ recursively.
- **Entry points:** Server/syxx_server/investigate.py:122; investigate.py:127
- **Files:** Server/syxx_server/investigate.py, investigate.py
- **Edge cases:** Test: concurrent access
- **Rollback:** git revert <commit>
- **Depends on:** A026, A019

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 16: Server API Endpoints (3 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A028, A029, A049

### A028 — _diff_content lives in app.py but depends on warp.build_baseline

- **Location:** Server/syxx_server/app.py:265-354
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract into `diff.py` along with `_diff_result` and `_route_diff`. [Fix 2] `app.py` delegates to `diff.py` for all comparison logic.
- **Entry points:** Server/syxx_server/app.py:265; app.py:265
- **Files:** Server/syxx_server/app.py, app.py, diff.py, warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A024, A025

### A029 — config.py mixes product catalog (Stripe SKUs) with infrastructure config

- **Location:** Server/syxx_server/config.py (624 lines)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Extract `STRIPE_SKUS` and `_sku()` into a dedicated `catalog.py` module. [Fix 2] Keep `config.py` for infrastructure config only (hosts, ports, TLS, timeouts, retention).
- **Entry points:** Server/syxx_server/config.py:280
- **Files:** Server/syxx_server/config.py, catalog.py, config.py, seo.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A021, A025

### A049 — investigate.py hasattr(resolver, "TYPE_DNSKEY") fallback to 48 is

- **Location:** Server/syxx_server/investigate.py:200
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Remove hasattr guard, reference resolver.TYPE_DNSKEY directly.
- **Entry points:** resolver.py:27; Server/syxx_server/investigate.py:200; investigate.py:200
- **Files:** Server/syxx_server/investigate.py, investigate.py, resolver.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A047, A017

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 17: Server API Endpoints (4 deltas, ~6-8h)

**Priority:** FIX NOW
**Deltas:** A037, A076, A077, A079

### A037 — do_DELETE has no CSRF protection while do_POST does — attacker can

- **Location:** Server/syxx_server/app.py:996
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add the same CSRF check at the top of `do_DELETE`: `if path not in ("/api/webhooks/stripe", "/api/webhooks/keygen"): if not self._check_csrf(): return`
- **Entry points:** Server/syxx_server/app.py:996; app.py:996
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A028

### A076 — DoH DNSSEC probe reports signed zones as "unsigned" when DO bit

- **Location:** Server/syxx_server/investigate.py:296-299
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Change to signed="unknown" with note about DO bit.
- **Entry points:** Server/syxx_server/investigate.py:296; investigate.py:296
- **Files:** Server/syxx_server/investigate.py, investigate.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A049

### A077 — zone_signing_status ignores parseComplete flag — false "unsigned" on

- **Location:** Server/syxx_server/resolver.py:873-880
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add `if not resp.get("parseComplete", True): return None, []` before DNSKEY check.
- **Entry points:** Server/syxx_server/resolver.py:873; resolver.py:873
- **Files:** Server/syxx_server/resolver.py, resolver.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A049, A030

### A079 — SMTP password and all server secrets stored as immutable Python

- **Location:** Server/syxx_server/config.py:574 (SMTP), 149 (Stripe), 173 (Keygen)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Restrict core dumps. [Fix 2] Use secrets manager with short-lived tokens. [Fix 3] Subprocess isolation for most sensitive.
- **Entry points:** Server/syxx_server/config.py:574; config.py:574
- **Files:** Server/syxx_server/config.py, config.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A029

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 22: Server API Endpoints (3 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A042, A054, A072

### A042 — Server Sentry SDK initialized but never used — zero capture_exception

- **Location:** Server/syxx_server/app.py:74 (sentry_sdk.init)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add `sentry_sdk.capture_exception(e)` to the `handler_exception` catch-all in app.py. [Fix 2] Add `sentry_sdk.capture_exception()` to all `logging_util.error()` call sites that represent operational failures. [Fix 3] Or remove the `sentry_sdk.init()` call and the dependency if Sentry is not 
- **Entry points:** Server/syxx_server/app.py:74
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A041

### A054 — N+1 sequential script fetches in warp.build_baseline — up to 50 scripts

- **Location:** Server/syxx_server/warp.py:120-150; Server/syxx_server/fetcher.py
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Use asyncio.gather() for parallel script fetches. [Fix 2] Add a concurrency limit (e.g., Semaphore(5)).
- **Entry points:** Server/syxx_server/warp.py:120; warp.py:145
- **Files:** Server/syxx_server/fetcher.py, Server/syxx_server/warp.py, warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A041, A028

### A072 — _sync_api_keys() ThreadPoolExecutor can hang scheduler loop beyond

- **Location:** Server/syxx_server/scheduler.py:163-173
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Cancel pending futures on timeout. [Fix 2] Use `shutdown(wait=False, cancel_futures=True)` (Python 3.9+).
- **Entry points:** Server/syxx_server/scheduler.py:163; scheduler.py:163
- **Files:** Server/syxx_server/scheduler.py, scheduler.py
- **Edge cases:** Test: timeout/slow responses
- **Rollback:** git revert <commit>
- **Depends on:** A041

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 23: Server API Endpoints (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A043, A075

### A043 — handler_exception catch-all creates no Sentry event — production

- **Location:** Server/syxx_server/app.py:919
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add `sentry_sdk.capture_exception(e)` to the `handler_exception` block. [Fix 2] Include route path and request context as Sentry tags.
- **Entry points:** Server/syxx_server/app.py:919
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A042

### A075 — db.backup() blocks scheduler shutdown for unbounded time — large DB

- **Location:** Server/syxx_server/scheduler.py:276-279; db.py:956-1002
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Check _shutdown.is_set() periodically during backup. [Fix 2] Use backup API's step callback to check for cancellation.
- **Entry points:** Server/syxx_server/scheduler.py:276; db.py:956; scheduler.py:276
- **Files:** Server/syxx_server/scheduler.py, db.py, scheduler.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A018, A072

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 24: Server API Endpoints (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A044, A080

### A044 — Server OTel spans cover only 16 of ~30+ endpoint paths — most-called

- **Location:** Server/syxx_server/app.py (span creation sites)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add OTel spans to all un-instrumented endpoint handlers. [Fix 2] Use a decorator or middleware pattern to auto-instrument all routes rather than adding spans per-route.
- **Entry points:** Server/syxx_server/app.py (span creation sites)
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test: concurrent access
- **Rollback:** git revert <commit>
- **Depends on:** A043, A042

### A080 — Missing index on passive_dns.source — full-table scan on every purge

- **Location:** Server/syxx_server/db.py:1280
- **Decision:** FIX NOW
- **Approach:** [Fix 1] CREATE INDEX idx_passive_source ON passive_dns(source).
- **Entry points:** Server/syxx_server/db.py:1280; db.py:1280
- **Files:** Server/syxx_server/db.py, db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A075, A023

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 25: Server API Endpoints (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A045, A082

### A045 — Server /readyz and /healthz endpoints do not create OTel spans

- **Location:** Server/syxx_server/app.py:1044 (/healthz), 1551 (/readyz)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Add OTel spans to /healthz and /readyz handlers. [Fix 2] Include dependency health (DB, DNS listener) as span attributes.
- **Entry points:** Server/syxx_server/app.py:1044; Server/syxx_server/app.py:1551
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A044

### A082 — _cap_rows double full-table scan — COUNT(*) + DELETE subquery per purge

- **Location:** Server/syxx_server/db.py:942-953
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Single query combining count + delete. [Fix 2] Index on last_seen makes sort O(excess).
- **Entry points:** Server/syxx_server/db.py:942; db.py:942
- **Files:** Server/syxx_server/db.py, db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A080

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 26: Server API Endpoints (2 deltas, ~4-6h)

**Priority:** FIX NOW
**Deltas:** A046, A083

### A046 — _route_diff performs two sequential DNS resolutions that could run

- **Location:** Server/syxx_server/app.py:2097-2108
- **Decision:** FIX NOW
- **Approach:** [Fix 1] Use `asyncio.gather()` to run the authoritative walk and clean-vantage query concurrently. [Fix 2] Ensure both tasks respect the same deadline.
- **Entry points:** Server/syxx_server/app.py:2097
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A045

### A083 — Missing index on warp_cache.created_at — full-table scan during purge

- **Location:** Server/syxx_server/db.py (schema + lines 948, 1301)
- **Decision:** FIX NOW
- **Approach:** [Fix 1] CREATE INDEX idx_warp_created ON warp_cache(created_at).
- **Entry points:** Server/syxx_server/db.py (schema + lines 948, 1301)
- **Files:** Server/syxx_server/db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A082

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
```

---

## Phase 43: Testing & Coverage (43 deltas, ~16-24h)

**Priority:** FIX NEXT
**Deltas:** B001, B002, B003, B004, B005, B006, B007, B008, B009, B010, B011, B012, B013, B014, B015, B016, B017, B018, B019, B035, B045, B046, E002, E003, E007, E008, E009, E010, E012, E013, E015, E016, E017, E028, E029, E038, E040, E041, E042, E043, E046, E048, E049

### B001 — Passive DNS drift boolean fallback collapses tri-state

- **Location:** Server/syxx_server/passivedns.py:121
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients to handle tri-state drift string.
- **Entry points:** passivedns.py:123; Server/syxx_server/passivedns.py:121
- **Files:** Server/syxx_server/passivedns.py, passivedns.py
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** A081

### B002 — 208 quarantined test methods in macOS _quarantine/ — synthesis engine

- **Location:** macOS/Tests/_quarantine/ (19 active test files, 208 test methods)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Re-enable the 19 active quarantined test files — fix compilation issues, run them, fix failures. [Fix 2] Delete the 23 duplicate/compile-broken files. [Fix 3] Add CI gate to prevent future test quarantines.
- **Entry points:** macOS/Tests/_quarantine/ (19 active test files, 208 test methods)
- **Files:** FusionRuleBakeoffTests.swift, ScorersContractTests.swift, Stage0GoldenCorpusTests.swift, SynthesisEngineTests.swift, SynthesizerDatapointWiringTests.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B003 — Synthesizer.cs — 15 duplicated switch (probe.Status) blocks across 6

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs (1,073 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Extract a `ProbeEvaluator` interface with a single `Evaluate()` method. [Fix 2] Each probe type implements the interface. [Fix 3] The adapter reduces to a single loop over evaluators.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs (1,073 lines)
- **Files:** Synthesizer.cs, Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B004 — MatrixDeterminativeEngine.cs — 1,319-line port of Swift engine with

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Extract type definitions into a separate file. [Fix 2] Extract scoring phases (coverage, fit, exclusion, threshold) into composable methods. [Fix 3] Externalize the diagnosis spec (see B007).
- **Entry points:** MatrixDeterminativeEngine.cs:1; MatrixDeterminativeEngine.cs:808
- **Files:** MatrixDeterminativeEngine.cs, Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B005 — DNSResolver.cs — 1,289-line static class mixing transport, parsing,

- **Location:** Windows/dotNET/Syxx.Kit/DNSResolver.cs (1,289 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Split into `DNSWireFormat` (parsing), `DNSTransport` (UDP/TCP/ DoH), and `DNSResolver` (orchestration). [Fix 2] Extract common response validation into a shared method used by both UDP and DoH.
- **Entry points:** Windows/dotNET/Syxx.Kit/DNSResolver.cs (1,289 lines)
- **Files:** DNSResolver.cs, Windows/dotNET/Syxx.Kit/DNSResolver.cs
- **Edge cases:** Test: Unicode edge cases, timeout/slow responses
- **Rollback:** git revert <commit>
- **Depends on:** None

### B006 — LeakDetector.cs — 1,092-line monolith mixing HTTP, TLS, Ed25519,

- **Location:** Windows/dotNET/Syxx.Kit/LeakDetector.cs (1,092 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Extract enums into their own files. [Fix 2] Extract HTTP pipeline into a separate class. [Fix 3] Extract `CollectorFailure` into its own file with user-facing strings separated from classification logic.
- **Entry points:** Windows/dotNET/Syxx.Kit/LeakDetector.cs (1,092 lines)
- **Files:** LeakDetector.cs, Windows/dotNET/Syxx.Kit/LeakDetector.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B007 — SyxxMatrixIntegration.cs — 200-line inline BuildDefaultSpec() with ~100

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs (485 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Load the diagnosis spec from the JSON produced by `DefaultSpecJson()` rather than building it inline. [Fix 2] Make the JSON file path configurable for tuning experiments.
- **Entry points:** SyxxMatrixIntegration.cs:200
- **Files:** SyxxMatrixIntegration.cs, Windows/dotNET/Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B008 — DNSResolver.swift — 1,698-line monolith mixing transport and wire format

- **Location:** macOS/Sources/SyxxKit/DNSResolver.swift (1,698 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Split into `DNSWireFormat` (parsing), `DNSTransport` (UDP/TCP/ DoH), and `DNSResolver` (orchestration). [Fix 2] The wire-format parser should be a standalone module with its own test suite.
- **Entry points:** macOS/Sources/SyxxKit/DNSResolver.swift (1,698 lines)
- **Files:** DNSResolver.swift, macOS/Sources/SyxxKit/DNSResolver.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B009 — MatrixDeterminativeEngine.swift — 1,068 lines, deeply nested scoring

- **Location:** macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Extract the scoring pipeline into smaller composable phases (coverage calculation, fit scoring, exclusion gating, threshold application). [Fix 2] Use a builder pattern for `ThresholdProfile`.
- **Entry points:** macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
- **Files:** MatrixDeterminativeEngine.swift, macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B010 — SynthesisEngine incomplete indicator mapping — 4 clean-support

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:180-230
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Wire the 4 clean-support indicators. [Fix 2] Add tests that verify clean runs can reach `.clean` when all indicators are positive. [Fix 3] Gate on the indicators being properly calibrated before enabling.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:180; SynthesisEngine.cs:180
- **Files:** SynthesisEngine.cs, Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B011 — No integration tests on macOS — all 889 active tests are unit tests

- **Location:** macOS/Tests/ (889 test methods, all unit tests)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add integration tests against a known-good public resolver (8.8.8.8) for basic DNS resolution. [Fix 2] Add integration tests against a test matrix of known probe inputs for the synthesis engine. [Fix 3] Gate integration tests behind an environment flag.
- **Entry points:** macOS/Tests/ (889 test methods, all unit tests)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B012 — No integration tests on any platform — cannot verify DNS tampering

- **Location:** All platforms (macOS, Windows, Linux, Server)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Create a test matrix of known DNS inputs (clean, tampered, censored, spoofed). [Fix 2] Build integration tests that run the full pipeline against these inputs. [Fix 3] Gate on an environment flag and run in CI on a schedule.
- **Entry points:** All platforms (macOS, Windows, Linux, Server)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B013 — _diff_content lives in app.py but belongs in a dedicated diff module

- **Location:** Server/syxx_server/app.py:265-354
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Extract into `diff.py` along with `_diff_result`. [Fix 2] `app.py` delegates to `diff.py` for all comparison logic.
- **Entry points:** Server/syxx_server/app.py:265; app.py:265
- **Files:** Server/syxx_server/app.py, app.py, diff.py, warp.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A028, A054, A078

### B014 — sentry-sdk 47 minor versions behind — missing OTel interop fixes could

- **Location:** Server/requirements.txt (constraint: `sentry-sdk[opentelemetry]>=2.19`)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Pin to `sentry-sdk[opentelemetry]>=2.66.0`.
- **Entry points:** Server/requirements.txt (constraint: `sentry-sdk[opentelemetry]>=2.19`)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B015 — Swift Forensics module (1,173 lines) has zero tests — C# twin has

- **Location:** macOS/Sources/SyxxKit/Forensics/ (1,173 lines, no test file);
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Create `macOS/Tests/SyxxKitTests/ForensicsAnalyzerTests.swift` consuming the same behavioral vectors as the C# tests. [Fix 2] Use C# `ForensicsAnalyzerTests.cs` as a template.
- **Entry points:** macOS/Sources/SyxxKit/Forensics/ (1,173 lines, no test file)
- **Files:** ForensicsAnalyzerTests.cs, ForensicsAnalyzerTests.swift, Windows/dotNET/Syxx.Tests/ForensicsAnalyzerTests.cs, macOS/Tests/SyxxKitTests/ForensicsAnalyzerTests.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B016 — CaptureEvents.ClassifyWindow triple-enumerates windowed records —

- **Location:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:170-210
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Single-pass enumeration with accumulators for outbound count, inbound count, nxdomain count, and servfail count.
- **Entry points:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:170; CaptureEvents.cs:170
- **Files:** CaptureEvents.cs, Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B017 — ParityVectorLoader.Walker.Done() does O(n log n) key-sort on every

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:1650-1660
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace with single-pass scan: `foreach (var kv in _o) { if (!_read.Contains(kv.Key)) throw Bad(...); }` Determinism is not needed here — any key-order dependency in tests is a smell.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:1650; ParityVectorLoader.cs:1650
- **Files:** ParityVectorLoader.cs, Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B018 — CageRedundancyCorrelator.swift (235 lines) has zero tests — pure logic

- **Location:** macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift (235 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Create `macOS/Tests/SyxxKitTests/CageRedundancyCorrelatorTests.swift` with synthetic fixture data for perfect agreement, partial overlap, complete disagreement, XID collision, and empty captures.
- **Entry points:** macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift (235 lines)
- **Files:** CageRedundancyCorrelator.swift, macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift, macOS/Tests/SyxxKitTests/CageRedundancyCorrelatorTests.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B019 — DNSPathConfirmation.swift (318 lines) has zero tests — accuracy-critical

- **Location:** macOS/Sources/SyxxKit/DNSPathConfirmation.swift (318 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Create `macOS/Tests/SyxxKitTests/DNSPathConfirmationTests.swift` testing all tri-state combinations, flavor disambiguation (DoH vs DoT vs DNSCrypt), and honesty-spine invariants.
- **Entry points:** macOS/Sources/SyxxKit/DNSPathConfirmation.swift (318 lines)
- **Files:** DNSPathConfirmation.swift, macOS/Sources/SyxxKit/DNSPathConfirmation.swift, macOS/Tests/SyxxKitTests/DNSPathConfirmationTests.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B035 — C# authoritative negative treats CNAME-only answers as divergence

- **Location:** Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:148-190;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Port the Swift CNAME delegation guard to C#. [Fix 2] Only classify a zero-address authoritative response as divergence when no CNAME delegation was observed.
- **Entry points:** Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:148; macOS/Sources/SyxxKit/AuthoritativeVantage.swift:131; Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:189
- **Files:** Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs, macOS/Sources/SyxxKit/AuthoritativeVantage.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### B045 — macOS DNSCapture parser lacks monotonic compression-pointer defense —

- **Location:** macOS/Sources/SyxxKit/DNSCapture.swift:690-695
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `guard pointer < current` after bounds check.
- **Entry points:** resolver.py:189; macOS/Sources/SyxxKit/DNSCapture.swift:690; DNSCapture.swift:690
- **Files:** DNSCapture.swift, macOS/Sources/SyxxKit/DNSCapture.swift, resolver.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A077

### B046 — ProbeRunner.runTyped classifies deliberate nil return as transportError

- **Location:** macOS/Sources/SyxxKit/ProbeRunner.swift:193-201
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add .noResult case to ProbeOutcome. [Fix 2] Or change produced to carry Output?.
- **Entry points:** macOS/Sources/SyxxKit/ProbeRunner.swift:193; ProbeRunner.swift:197
- **Files:** ProbeRunner.swift, macOS/Sources/SyxxKit/ProbeRunner.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E002 — TOCTOU DNS rebinding in Swift client-vantage HTTP fetch bypasses SSRF gate

- **Location:** macOS/Sources/SyxxKit/WarpEngine.swift:520-525, 601-605
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Resolve DNS once and pin the connection to that IP, mirroring the C# approach. Use NWConnection or a custom URLSession delegate. [Fix 2] For HTTP specifically, forbid HTTP entirely in vantage fetch (HTTPS-only).
- **Entry points:** macOS/Sources/SyxxKit/WarpEngine.swift:520
- **Files:** macOS/Sources/SyxxKit/WarpEngine.swift
- **Edge cases:** Test: adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E003 — No key rotation, expiration, or revocation support in trust store

- **Location:** macOS/Sources/SyxxKit/WarpManifestPinning.swift:19-48;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Extend the trust-set format to carry per-key metadata: expires, revoked, transition. [Fix 2] Consider a structured JSON format for the key file with per-key objects.
- **Entry points:** macOS/Sources/SyxxKit/WarpManifestPinning.swift:19; Windows/dotNET/Syxx.Kit/WarpManifestPinning.cs:22
- **Files:** Windows/dotNET/Syxx.Kit/WarpManifestPinning.cs, macOS/Sources/SyxxKit/WarpManifestPinning.swift
- **Edge cases:** Test: concurrent access
- **Rollback:** git revert <commit>
- **Depends on:** None

### E007 — IntegrityTag.constantTimeEqual leaks HMAC length via early return

- **Location:** macOS/Sources/SyxxKit/Trust/IntegrityTag.swift:117-124
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Use constant-time comparison that does not branch on length: pad the shorter input to the longer length before comparing, or use CryptoKit's built-in HMAC validation.
- **Entry points:** macOS/Sources/SyxxKit/Trust/IntegrityTag.swift:117
- **Files:** macOS/Sources/SyxxKit/Trust/IntegrityTag.swift
- **Edge cases:** Test: adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E008 — Test Ed25519 private keys committed to the repository

- **Location:** warp-manifest-vectors/keys/test_signing_key.pem;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace PEM files with a build step that generates keypairs from seeds at test time. [Fix 2] Add a .gitignore rule note and pre-commit hook warning about private key material.
- **Entry points:** warp-manifest-vectors/_build_vectors.py:68
- **Files:** _build_vectors.py, pinned_keys.js, warp-manifest-vectors/_build_vectors.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E009 — Keygen API calls over configurable base URL lack HTTPS enforcement

- **Location:** Server/syxx_server/fulfillment.py:51,85,109;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add a startup validation in config.py: if KEYGEN_BASE_URL is set and does not start with https://, log a warning and refuse to start unless SYXX_INSECURE_KEYGEN_URL=1 is set.
- **Entry points:** Server/syxx_server/fulfillment.py:51; Server/syxx_server/licensing.py:341
- **Files:** Server/syxx_server/fulfillment.py, Server/syxx_server/licensing.py, api.keygen.sh, config.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A021, A079

### E010 — _cap_rows f-string SQL construction gates on caller discipline only

- **Location:** Server/syxx_server/db.py:885 (_cap_rows function signature)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add an assertion at the top of _cap_rows: assert table in ("passive_dns", "audit", "warp_cache"), f"unexpected table: {table}". One-line guard that makes the invariant machine-checkable.
- **Entry points:** Server/syxx_server/db.py:885
- **Files:** Server/syxx_server/db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A083

### E012 — No architecture documentation on macOS — no ADRs, C4 diagrams, or

- **Location:** macOS/ (no architecture docs exist)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Create `macOS/ARCHITECTURE.md` documenting: SyxxKit module boundaries, synthesis pipeline (DNS query → probe → evidence → matrix engine → conclusion), and data flow diagrams.
- **Entry points:** macOS/ (no architecture docs exist)
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E013 — colorthief is unmaintained since Feb 2017 — Python 2 only, will not

- **Location:** tools/yt-design-extractor/pyproject.toml
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace with Pillow's built-in color analysis (`Image.getcolors()`, `Image.quantize()`) or `colorgram.py`. [Fix 2] Remove `colorthief` from pyproject.toml.
- **Entry points:** tools/yt-design-extractor/pyproject.toml
- **Files:** colorgram.py, pyproject.toml, tools/yt-design-extractor/pyproject.toml
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E015 — No automated dependency vulnerability scanning in CI for any ecosystem

- **Location:** .github/workflows/ (no dependency audit workflow)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `pip-audit` step to CI for Python dependencies. [Fix 2] Add `dotnet list package --vulnerable` step for NuGet. [Fix 3] Add `npm audit` step (future-proofing). [Fix 4] Schedule weekly scans on cron.
- **Entry points:** .github/workflows/ (no dependency audit workflow)
- **Files:** ci.yml, code-quality.yml, validate.yml
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E016 — Breadcrumb() called from catch blocks can throw, replacing the

- **Location:** Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Wrap the `Breadcrumb` body in its own try/catch: `try { SyxxDiagnostics.Breadcrumb(...); } catch { /* best-effort */ }`
- **Entry points:** Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs
- **Files:** LinuxSecretStore.cs, Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E017 — Hardcoded P-256 Keygen public key in macOS and Windows clients — no

- **Location:** macOS/Sources/SyxxKit/LicenseManager.swift:74;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Deliver public key via authenticated /api/startup response. [Fix 2] Cache locally with TTL. [Fix 3] Fall back to compiled-in key only when server is unreachable.
- **Entry points:** LicenseManager.swift:74; LicenseManager.cs:508; macOS/Sources/SyxxKit/LicenseManager.swift:74
- **Files:** LicenseManager.cs, LicenseManager.swift, Windows/dotNET/Syxx.Kit/LicenseManager.cs, macOS/Sources/SyxxKit/LicenseManager.swift
- **Edge cases:** Test: adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E028 — random.shuffle in firewall_test uses non-cryptographic Mersenne Twister

- **Location:** Server/syxx_server/firewall_test.py:233
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace with secrets.SystemRandom().shuffle().
- **Entry points:** Server/syxx_server/firewall_test.py:233
- **Files:** Server/syxx_server/firewall_test.py, random.sh
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A053

### E029 — Witness key stored as plain base64 in Keychain — no additional wrapping

- **Location:** macOS/Sources/SyxxKit/WitnessKeyManager.swift:100-120
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Wrap key with device-specific key before Keychain storage.
- **Entry points:** macOS/Sources/SyxxKit/WitnessKeyManager.swift:100
- **Files:** macOS/Sources/SyxxKit/WitnessKeyManager.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E038 — Email auth DNS queries (SPF/DMARC/DKIM) not DNSSEC-validated — MITM

- **Location:** Server/syxx_server/email_auth.py:30-90
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Set DO bit on DNS queries. [Fix 2] Validate DNSSEC signatures for TXT responses. [Fix 3] Mark as "unverified" when DNSSEC unavailable.
- **Entry points:** Server/syxx_server/email_auth.py:30; email_auth.py:30
- **Files:** Server/syxx_server/email_auth.py, email_auth.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E040 — Witness signing key exposed via SYXX_WITNESS_KEY env var — visible to

- **Location:** macOS/Sources/SyxxCLI/Commands/SyxxWitness.swift:176
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Prefer --passphrase-file (0600) or interactive prompt. [Fix 2] Clear env var after reading: setenv("SYXX_WITNESS_KEY", "", 1). [Fix 3] Document exposure in help text.
- **Entry points:** macOS/Sources/SyxxCLI/Commands/SyxxWitness.swift:176; SyxxWitness.swift:176
- **Files:** SyxxWitness.swift, macOS/Sources/SyxxCLI/Commands/SyxxWitness.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E041 — Hardcoded OTLP telemetry endpoint in macOS binary — redirect risk if

- **Location:** macOS/Sources/SyxxTelemetry/OTelBootstrap.swift:14
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Make configurable via env var with current value as default.
- **Entry points:** macOS/Sources/SyxxTelemetry/OTelBootstrap.swift:14; OTelBootstrap.swift:14
- **Files:** OTelBootstrap.swift, macOS/Sources/SyxxTelemetry/OTelBootstrap.swift
- **Edge cases:** Test: concurrent access, adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E042 — Unicode homograph attacks possible in domain validation regex — mixed-

- **Location:** Server/syxx_server/investigate.py:25-28
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add confusable character detection using Unicode confusables. txt. [Fix 2] Warn on mixed-script characters.
- **Entry points:** Server/syxx_server/investigate.py:25; investigate.py:25
- **Files:** Server/syxx_server/investigate.py, investigate.py
- **Edge cases:** Test: Unicode edge cases, adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A076

### E043 — DPAPI legacy entropy constants are static across all installations —

- **Location:** Windows/dotNET/Syxx.Kit/DpapiSecretStore.cs:220-225
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Derive from machine-specific secret. [Fix 2] Remove legacy path after migration window.
- **Entry points:** Windows/dotNET/Syxx.Kit/DpapiSecretStore.cs:220; DpapiSecretStore.cs:220
- **Files:** DpapiSecretStore.cs, Windows/dotNET/Syxx.Kit/DpapiSecretStore.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E046 — Settings file permissions not verified on load — overly permissive

- **Location:** macOS/Sources/SyxxKit/SyxxSettings.swift:176-177, 143-152
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Verify file permissions in load(). [Fix 2] Reject files with overly permissive modes; log warning and fall back to defaults.
- **Entry points:** macOS/Sources/SyxxKit/SyxxSettings.swift:176; SyxxSettings.swift:176; SyxxSettings.swift:143
- **Files:** SyxxSettings.swift, macOS/Sources/SyxxKit/SyxxSettings.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E048 — WKContentRuleList compilation races with first navigation — brief

- **Location:** macOS/Sources/SyxxApp/RemoteContentView.swift:207-224
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Defer loadContent until rule list compilation callback fires.
- **Entry points:** macOS/Sources/SyxxApp/RemoteContentView.swift:207; RemoteContentView.swift:207
- **Files:** RemoteContentView.swift, macOS/Sources/SyxxApp/RemoteContentView.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

### E049 — Captive portal detection uses unencrypted HTTP to gateway IP — MITM

- **Location:** macOS/Sources/SyxxKit/NetworkKeyState.swift:804
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add comment documenting deliberate HTTP for reachability. [Fix 2] Add timeout and response size limit.
- **Entry points:** macOS/Sources/SyxxKit/NetworkKeyState.swift:804; NetworkKeyState.swift:804
- **Files:** NetworkKeyState.swift, macOS/Sources/SyxxKit/NetworkKeyState.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** None

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 44: Synthesis Engine (19 deltas, ~16-24h)

**Priority:** FIX NEXT
**Deltas:** B020, B021, B022, B024, B025, B032, B034, B047, E001, E004, E005, E011, E014, E018, E019, E023, E024, E030, E047

### B020 — 19 unique test files quarantined in macOS _quarantine/ — SynthesisEngine

- **Location:** macOS/Tests/_quarantine/ (44 files: 19 unique, 14 .phase32-active-dup,
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Delete 14 `.phase32-active-dup` files (exact duplicates). [Fix 2] Fix `SyxxKitTests.swift.phase32-compile-break` (2,119 lines). [Fix 3] Fix `SynthesisEngineTests.swift.phase1-compile-break`. [Fix 4] Fix remaining 7 `.compile-break` files.
- **Entry points:** macOS/Tests/_quarantine/ (44 files: 19 unique, 14 .phase32-active-dup,
- **Files:** SynthesisEngineTests.swift, SyxxKitTests.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B002

### B021 — CaptureEvents.cs (407 lines) has zero tests — new Windows capture

- **Location:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs (407 lines)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Create `Windows/dotNET/Syxx.Tests/CaptureEventsTests.cs` with tests for event parsing, filtering, classification, and edge cases.
- **Entry points:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs (407 lines)
- **Files:** CaptureEvents.cs, CaptureEventsTests.cs, Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs, Windows/dotNET/Syxx.Tests/CaptureEventsTests.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B016

### B022 — ParityVectorLoader.RejectDuplicateMembers allocates HashSet per JSON

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:380-410
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Reuse a single `HashSet<string>` per scope, cleared on `EndObject`, tracked by a count stack. [Fix 2] Or use a ref struct approach with `Utf8JsonReader` to detect duplicates without allocating per-object.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs:380; ParityVectorLoader.cs:380
- **Files:** ParityVectorLoader.cs, Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B017

### B024 — ExfilSuspicious EvidenceId unmapped in C# SynthesisEngine — macOS

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:283-310;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `[EvidenceId.ExfilSuspicious] = (SyxxIndicator. LocalEnvironmentCompromised, 0.35)` to the C# `IndicatorMapping` dictionary at line 310. [Fix 2] Add a parity test that verifies both platforms handle ExfilSuspicious identically.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:283; macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:290; SynthesisEngine.swift:290
- **Files:** SynthesisEngine.cs, SynthesisEngine.swift, Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs, macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B010

### B025 — MatrixDeterminativeEngine.evaluate() O(n²) competitor strength

- **Location:** macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:640-646
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Pre-compute `competitorBestStrength` for all diagnoses in a single O(d²) pass before Pass 2. [Fix 2] Cache results in a dictionary keyed by diagnosis ID.
- **Entry points:** macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:640; MatrixDeterminativeEngine.swift:640
- **Files:** MatrixDeterminativeEngine.swift, macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B009

### B032 — DNSResolver spoof-detection uses fragile string-matching on

- **Location:** macOS/Sources/SyxxKit/DNSResolver.swift:513, 525
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Track spoof detection as a separate `Bool` flag rather than pattern-matching on human-readable error strings.
- **Entry points:** macOS/Sources/SyxxKit/DNSResolver.swift:513; DNSResolver.swift:513
- **Files:** DNSResolver.swift, macOS/Sources/SyxxKit/DNSResolver.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B008

### B034 — ScriptMismatchInconclusive EvidenceId missing from C# — inconclusive

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:463;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `ScriptMismatchInconclusive` to C# EvidenceId enum, RawValue(), JSON converter, DisplayName(). [Fix 2] Change Synthesizer.cs:463 to use EvidenceId.ScriptMismatchInconclusive.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:463; macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:354; Synthesizer.cs:463
- **Files:** Synthesizer.cs, Synthesizer.swift, Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs, macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B003

### B047 — macOS AAAA answer rdata uses uncompressed IPv6 format — C077 fix

- **Location:** macOS/Sources/SyxxKit/DNSCapture.swift:639-642
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Use inet_ntop(AF_INET6, ...) for AAAA rdata.
- **Entry points:** macOS/Sources/SyxxKit/DNSCapture.swift:639; DNSCapture.swift:639
- **Files:** DNSCapture.swift, macOS/Sources/SyxxKit/DNSCapture.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B045

### E001 — Passive DNS drift boolean fallback collapses tri-state

- **Location:** Server/syxx_server/passivedns.py:121
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Remove driftSuspected boolean from API response JSON forcing clients to handle tri-state drift string.
- **Entry points:** passivedns.py:123; Server/syxx_server/passivedns.py:121
- **Files:** Server/syxx_server/passivedns.py, passivedns.py
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** C001

### E004 — No explicit certificate revocation checking (CRL/OCSP) in TLS connections

- **Location:** macOS/Sources/SyxxKit/WarpEngine.swift:508-512;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] C#: set SslOptions.CertificateRevocationCheckMode = X509RevocationMode.Online. [Fix 2] Swift: use a custom URLSessionDelegate with urlSession(_:didReceive:completionHandler:) for revocation checking.
- **Entry points:** macOS/Sources/SyxxKit/WarpEngine.swift:508; Windows/dotNET/Syxx.Kit/WarpEngine.cs:574
- **Files:** Windows/dotNET/Syxx.Kit/WarpEngine.cs, macOS/Sources/SyxxKit/WarpEngine.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E002

### E005 — No zero-width character handling in URL parsing enables invisible host

- **Location:** Server/syxx_server/warp.py:339; Server/syxx_server/fetcher.py:90-98
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Strip or reject zero-width and bidi control characters in hostnames before processing. Apply to hostname at warp.py:339, 442 and fetcher.py.
- **Entry points:** Server/syxx_server/warp.py:339; Server/syxx_server/fetcher.py:90; warp.py:339
- **Files:** Server/syxx_server/fetcher.py, Server/syxx_server/warp.py, fetcher.py, warp.py
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** B013, A054

### E011 — logging_util event category uses f-string interpolation for caller data

- **Location:** Server/syxx_server/fulfillment.py:140,227,260
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Document the constraint on logging_util functions that the category parameter must be a static string literal. No code change needed at this time.
- **Entry points:** Server/syxx_server/fulfillment.py:140
- **Files:** Server/syxx_server/fulfillment.py
- **Edge cases:** Test: adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E009

### E014 — Pillow constraint >=10.0.0 allows vulnerable versions — 11 HIGH CVEs

- **Location:** tools/yt-design-extractor/pyproject.toml
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Change constraint to `"Pillow>=12.3.0"`.
- **Entry points:** tools/yt-design-extractor/pyproject.toml
- **Files:** tools/yt-design-extractor/pyproject.toml
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E013

### E018 — Key algorithm mismatch — server docstring says Ed25519, clients use

- **Location:** Server/syxx_server/licensing.py:61-63 (Ed25519);
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Pick one algorithm and use consistently. [Fix 2] Document in deployment docs.
- **Entry points:** Server/syxx_server/licensing.py:61; macOS/Sources/SyxxKit/LicenseManager.swift:74; Windows/dotNET/Syxx.Kit/LicenseManager.cs:508
- **Files:** Keygen.sh, LicenseManager.cs, LicenseManager.swift, Server/syxx_server/licensing.py, Windows/dotNET/Syxx.Kit/LicenseManager.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E017, E009

### E019 — CSP on main site leaks user IPs to Google Fonts — privacy violation

- **Location:** Server/syxx_server/app.py:628-638
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Self-host fonts. [Fix 2] Use font-src data: matching the whats-new page CSP.
- **Entry points:** Server/syxx_server/app.py:628; app.py:628; app.py:1209
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B013

### E023 — Trial fingerprint bypass on VMs/containers — documented, enforcement

- **Location:** Server/syxx_server/config.py:370-375
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Verify trial anchor endpoint refuses InstallScoped fingerprints. [Fix 2] Add test.
- **Entry points:** Server/syxx_server/config.py:370; config.py:370
- **Files:** Server/syxx_server/config.py, config.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A079, E009

### E024 — Leak tokens stored as plaintext in SQLite — no hashing

- **Location:** Server/syxx_server/db.py:715-760; leak.py:185
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Hash tokens with SHA-256 before storage.
- **Entry points:** Server/syxx_server/db.py:715; leak.py:185
- **Files:** Server/syxx_server/db.py, db.py, leak.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A082, C005, E010

### E030 — License key Keychain fallback to password-only when biometric

- **Location:** macOS/Sources/SyxxKit/LicenseManager.swift:130-180
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Document tradeoff. [Fix 2] Consider requiring biometric for Pro.
- **Entry points:** macOS/Sources/SyxxKit/LicenseManager.swift:130
- **Files:** macOS/Sources/SyxxKit/LicenseManager.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E017

### E047 — Keystore passphrase-derived key held in memory for 15-minute idle

- **Location:** Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs (KeystorePassphraseSession)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Reduce idle timeout to 5 minutes. [Fix 2] Use ProtectedMemory (Windows) or mlock (Linux) to prevent swap.
- **Entry points:** Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs (KeystorePassphraseSession)
- **Files:** Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs
- **Edge cases:** Test: timeout/slow responses
- **Rollback:** git revert <commit>
- **Depends on:** E016

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 45: Capture Events (9 deltas, ~8-12h)

**Priority:** FIX NEXT
**Deltas:** B023, B026, B033, B036, E006, E020, E025, E026, E036

### B023 — CaptureEvents rcode string matching fragile to producer changes —

- **Location:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Use numeric DNS rcode values as primary match (3 = NXDOMAIN, 2 = SERVFAIL). [Fix 2] Define a shared `Rcode` enum or constants in `DNSCaptureRecord` so producer and classifier use same vocabulary.
- **Entry points:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs
- **Files:** CaptureEvents.cs, Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B021

### B026 — C# MatrixDeterminativeEngine missing B162 section-usability gate for

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:920-950
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `sectionById.TryGetValue(obs.Section, out var producing) && !producing.Status.IsUsable()` guard in the support-rules loop, counting only toward overhang then `continue`.
- **Entry points:** MatrixDeterminativeEngine.cs:920; Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:920; MatrixDeterminativeEngine.swift:800
- **Files:** MatrixDeterminativeEngine.cs, MatrixDeterminativeEngine.swift, Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B004, B025

### B033 — DNSResponse.didFail includes .servFail and .refused as "failures" —

- **Location:** macOS/Sources/SyxxKit/DNSResolver.swift:330
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Rename to `didNotSucceed` or split into `didFail` (transport only) and `didNotResolve` (includes RCODE errors).
- **Entry points:** macOS/Sources/SyxxKit/DNSResolver.swift:330; DNSResolver.swift:330
- **Files:** DNSResolver.swift, macOS/Sources/SyxxKit/DNSResolver.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B032

### B036 — Denial NotADenial maps to Negative in C# but skipped in Swift —

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:304-306;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Change C# to `break` (skip), matching Swift B061.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:304; macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:230
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs, macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B034

### E006 — No homoglyph / confusable character detection in domain names

- **Location:** Server/syxx_server/warp.py:339; macOS/Sources/SyxxKit/WarpEngine.swift;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add a confusable-character warning in the CLI output and UI when the requested URL contains characters from multiple scripts. Extend SyxxDiagnostics.swift dns.name.is_idn check to the Warp CLI.
- **Entry points:** Server/syxx_server/warp.py:339
- **Files:** Server/syxx_server/warp.py, SyxxDiagnostics.swift, Windows/dotNET/Syxx.Kit/WarpEngine.cs, macOS/Sources/SyxxKit/WarpEngine.swift
- **Edge cases:** Test: Unicode edge cases
- **Rollback:** git revert <commit>
- **Depends on:** E005, E004

### E020 — CSP script-src 'self' allows any same-origin script — bypasses nonce

- **Location:** Server/syxx_server/app.py:628-638
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace with script-src 'nonce-{nonce}' (no 'self'). [Fix 2] Use strict-dynamic.
- **Entry points:** Server/syxx_server/app.py:628
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E019

### E025 — SQLite WAL/SHM sidecar permissions may drift from 0600 to umask default

- **Location:** Server/syxx_server/db.py:311-330
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Set process umask to 0077. [Fix 2] Periodic chmod. [Fix 3] Verify in /readyz.
- **Entry points:** Server/syxx_server/db.py:311; db.py:311
- **Files:** Server/syxx_server/db.py, db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E024

### E026 — DNS authoritative server binds to 0.0.0.0:53 by default

- **Location:** Server/syxx_server/config.py:393
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Change default to specific IP or require explicit config.
- **Entry points:** Server/syxx_server/config.py:393
- **Files:** Server/syxx_server/config.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E023

### E036 — Custom ASN.1 DER parser in SPKI pin extraction is error-prone —

- **Location:** Server/syxx_server/stripe_checkout.py:60-96
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Use cryptography.x509 instead of custom DER parsing. The `cryptography` library is already a dependency via leak.py.
- **Entry points:** Server/syxx_server/stripe_checkout.py:60; stripe_checkout.py:60
- **Files:** Server/syxx_server/stripe_checkout.py, leak.py, stripe_checkout.py
- **Edge cases:** Test: adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** A060, E024

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 46: Synthesis Engine (7 deltas, ~8-12h)

**Priority:** FIX NEXT
**Deltas:** B027, B037, B048, E022, E031, E035, E050

### B027 — C# MatrixDeterminativeEngine missing B162 section-usability gate for

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:958-966
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add section-usability guard before processing exclude rules.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:958
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B026

### B037 — Latency Unreachable maps to Invalidated in C# but Degraded in Swift —

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:213-214;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Move Unreachable into Degraded group, matching Swift.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:213; macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:165
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs, macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift
- **Edge cases:** Test: timeout/slow responses
- **Rollback:** git revert <commit>
- **Depends on:** B036

### B048 — Windows CaptureEvents totalResponses>0 guard silently excludes 100%

- **Location:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:259
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Remove the guard; (double)0/outbound = 0.0 <= 0.5 satisfies ratio check.
- **Entry points:** Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:259; CaptureEvents.cs:259
- **Files:** CaptureEvents.cs, Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** B023

### E022 — No rate limiting on /api/auth/activate — session token brute-force

- **Location:** Server/syxx_server/app.py (activation endpoint)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add per-IP rate limiting. [Fix 2] Add exponential backoff.
- **Entry points:** Server/syxx_server/app.py (activation endpoint)
- **Files:** Server/syxx_server/app.py, ratelimit.py
- **Edge cases:** Test: boundary values
- **Rollback:** git revert <commit>
- **Depends on:** E020

### E031 — SQLite database has no encryption at rest beyond filesystem permissions

- **Location:** Server/syxx_server/db.py:311-330
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Use SQLCipher or SEE. [Fix 2] Encrypt sensitive columns at application level.
- **Entry points:** Server/syxx_server/db.py:311
- **Files:** Server/syxx_server/db.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E025

### E035 — Session signing secret stored as plaintext in SQLite kv table — DB

- **Location:** Server/syxx_server/licensing.py:90-99; db.py:get_or_create_secret()
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Encrypt session secret at rest with key derived from server- local secret. [Fix 2] Rotate on restart if encryption key unavailable.
- **Entry points:** Server/syxx_server/licensing.py:90; licensing.py:90
- **Files:** Server/syxx_server/licensing.py, db.py, licensing.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E018, E025

### E050 — Hardcoded Stripe product IDs and price IDs in server config — reveals

- **Location:** Server/syxx_server/config.py (STRIPE_SKUS table)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Make env vars mandatory for production. [Fix 2] Remove hardcoded defaults.
- **Entry points:** Server/syxx_server/config.py (STRIPE_SKUS table)
- **Files:** Server/syxx_server/config.py, config.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E023, E026

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 47: Synthesis Engine (5 deltas, ~6-8h)

**Priority:** FIX NEXT
**Deltas:** B028, B038, E027, E044, E045

### B028 — C# MatrixDeterminativeEngine missing B163 ExclusionQualityFloor — no

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:969-976
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `private const double ExclusionQualityFloor = 0.5;` and gate hard exclusion on `Clamp01(obs.Quality) >= ExclusionQualityFloor`.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:969
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B027

### B038 — AnswerStabilityStatus.Rotating defined in C# enum but not handled in

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:229-255
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add Rotating case alongside Stable → Negative. [Fix 2] Add default arm that logs unhandled values.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:229
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B037

### E027 — Missing Sec-Fetch-* header validation for cross-origin request

- **Location:** Server/syxx_server/app.py (request handling)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Validate Sec-Fetch-Site for mutation endpoints. [Fix 2] Reject cross-site requests to /api/auth/activate and /api/checkout.
- **Entry points:** Server/syxx_server/app.py (request handling)
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E022

### E044 — In-memory rate limit buckets lost on server restart — burst attack

- **Location:** Server/syxx_server/ratelimit.py:22-23
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Persist state to DB with short TTL. [Fix 2] Add startup grace period with stricter limits.
- **Entry points:** Server/syxx_server/ratelimit.py:22; ratelimit.py:22
- **Files:** Server/syxx_server/ratelimit.py, ratelimit.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E022

### E045 — Session token TTL bound to server wall clock — no clock-skew tolerance

- **Location:** Server/syxx_server/licensing.py:94
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Use time.monotonic() for relative TTL. [Fix 2] Document skew tolerance.
- **Entry points:** Server/syxx_server/licensing.py:94; licensing.py:94
- **Files:** Server/syxx_server/licensing.py, licensing.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E035

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 48: Synthesis Engine (3 deltas, ~4-6h)

**Priority:** FIX NEXT
**Deltas:** B029, B039, E032

### B029 — C# MatrixDeterminativeEngine hard exclusion missing section-usability

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:969-976
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add `sectionById.TryGetValue(obs.Section, out var producing) && producing.Status.IsUsable()` to the hard-exclusion guard.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:969
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B028

### B039 — Glutton Inconclusive without signature maps to Invalidated in C# but

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:763;
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Change C# to SectionStatus.Untested for null-signature path.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:763; macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:596
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs, macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift
- **Edge cases:** Test: null/empty inputs
- **Rollback:** git revert <commit>
- **Depends on:** B037, B038

### E032 — Server error messages may leak internal paths in debug scenarios

- **Location:** Server/syxx_server/app.py:1556
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Audit all endpoints. [Fix 2] Centralize error sanitization.
- **Entry points:** Server/syxx_server/app.py:1556
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E027

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
cd macOS && swift build 2>&1 | tail -5 && swift test 2>&1 | tail -20
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 49: Synthesis Engine (3 deltas, ~4-6h)

**Priority:** FIX NEXT
**Deltas:** B030, B040, E033

### B030 — C# MatrixDeterminativeEngine missing B159 emittableIDs filter in

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:850-860
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Compute `emittableIDs` (gated/hard-excluded → excluded) before CompetitorBestStrength, filter competitors accordingly.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:850
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B029

### B040 — ECS leak text in C# overclaims — B138 correction not ported

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:781-783
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace C# text with B138-corrected Swift text.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:781
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B039

### E033 — No Subresource Integrity hashes for whats-new HTML files

- **Location:** Server/syxx_server/app.py:1166-1210
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add Content-Digest header based on file hash.
- **Entry points:** Server/syxx_server/app.py:1166
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E032

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 50: Synthesis Engine (3 deltas, ~4-6h)

**Priority:** FIX NEXT
**Deltas:** B031, B041, E037

### B031 — C# MatrixDeterminativeEngine missing B168 cleanID guard — clean

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:880-895
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Insert `d.Id != CleanId` guard at both admission sites.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:880
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B030

### B041 — Server-confirmed authoritative divergence text overclaims in C# —

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:488-493
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Replace C# text with B092-corrected Swift text.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:488
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B040

### E037 — Unauthenticated trial anchor endpoint accepts arbitrary POST requests

- **Location:** Server/syxx_server/app.py (/api/trial/anchor route)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add rate limiting specific to trial anchor. [Fix 2] Validate fingerprint format. [Fix 3] Require minimal proof-of-work.
- **Entry points:** Server/syxx_server/app.py (/api/trial/anchor route)
- **Files:** Server/syxx_server/app.py
- **Edge cases:** Test: boundary values
- **Rollback:** git revert <commit>
- **Depends on:** E033

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 51: Synthesis Engine (2 deltas, ~4-6h)

**Priority:** FIX NEXT
**Deltas:** B042, E039

### B042 — Portal B176(c) assertion guard missing from C# — silent evidence drop

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:600-730
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add Debug.Assert matching Swift B176(c).
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:600
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B041

### E039 — ThreadingHTTPServer lacks production security features — no request

- **Location:** Server/syxx_server/app.py (ThreadingHTTPServer usage)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Consider gunicorn/uvicorn behind reverse proxy. [Fix 2] Document reverse proxy as hard requirement in production.
- **Entry points:** Server/syxx_server/app.py (ThreadingHTTPServer usage)
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E019, E037

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 52: Synthesis Engine (2 deltas, ~4-6h)

**Priority:** FIX NEXT
**Deltas:** B043, E051

### B043 — ResponseSizeStatus and StubEntropyStatus InsufficientData not

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:853-868, 396-397
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add explicit InsufficientData cases. [Fix 2] Remove default arms so unhandled values become compile errors.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:853
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B042

### E051 — Server OTel export endpoint hardcoded — same as E041 but for server

- **Location:** Server/syxx_server/app.py:109
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Make configurable via env var.
- **Entry points:** app.py:109; Server/syxx_server/app.py:109
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E039

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Phase 53: Synthesis Engine (2 deltas, ~4-6h)

**Priority:** FIX NEXT
**Deltas:** B044, E053

### B044 — ResponseSizeStatus enum missing Inconclusive member in C#

- **Location:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:853-868
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Add Inconclusive to C# enum or confirm intentional omission.
- **Entry points:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs:853
- **Files:** Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** B043

### E053 — Server query parameters logged verbatim — domain names in telemetry

- **Location:** Server/syxx_server/app.py (route handler logging calls)
- **Decision:** FIX NEXT
- **Approach:** [Fix 1] Redact query domain names in telemetry logs. [Fix 2] Hash or truncate domain names. [Fix 3] Add redact_domain() helper.
- **Entry points:** Server/syxx_server/app.py (route handler logging calls)
- **Files:** Server/syxx_server/app.py, app.py
- **Edge cases:** Test with normal, edge-case, and adversarial inputs
- **Rollback:** git revert <commit>
- **Depends on:** E051

#### Verification Gate

```bash
cd Server && python -m pytest tests/ -v --tb=short
dotnet build Windows/ --verbosity quiet && dotnet test Windows/ --verbosity minimal
```

---

## Single-Delta Phases (Summary)

These 25 phases each contain a single delta. See DELTAS.md for full implementation details.

| Phase | Delta | Priority | Theme | Title | Effort | Verification |
|------:|-------|----------|-------|-------|--------|--------------|
| 8 | A010 | FIX NOW | Warp & Content Integrity | Deprecated build_baseline serializes without sort_keys=True | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 9 | A015 | FIX NOW | Server API Endpoints | app.py is a 3,425-line God module mixing 5 distinct concerns | ~3h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 10 | A019 | FIX NOW | Server API Endpoints | No architecture documentation for Server — no ADRs, C4 diagr... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 11 | A020 | FIX NOW | Server API Endpoints | _diff_content has no direct unit tests — tested only indirec... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 12 | A022 | FIX NOW | Dependencies | 12 type safety suppression overrides in pyproject.toml mask ... | ~3h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 18 | A038 | FIX NOW | Server API Endpoints | _client_ip returns loopback (127.0.0.1) on malformed XFF, en... | ~3h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 19 | A039 | FIX NOW | Server API Endpoints | HSTS emitted via X-Forwarded-Proto without peer validation —... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 20 | A040 | FIX NOW | Server API Endpoints | Trial-anchor per-IP limiter `_TRIAL_ANCHOR_IP_COUNTS` grows ... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 21 | A041 | FIX NOW | Server API Endpoints | `/api/diff` content half runs `warp.build_baseline` with no ... | ~3h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 27 | A048 | FIX NOW | Server API Endpoints | _deadline_timer comment claims "per-CONNECTION" but setup() ... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 28 | A050 | FIX NOW | Server API Endpoints | _route_investigate records domain in OTel span BEFORE valida... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 29 | A052 | FIX NOW | Server API Endpoints | No ETag/If-None-Match support on server — full body retransm... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 30 | A055 | FIX NOW | Server API Endpoints | No Cache-Control on any API response — zero browser/CDN cach... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 31 | A056 | FIX NOW | Server API Endpoints | Server OTel spans have no error status on exception paths — ... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 32 | A057 | FIX NOW | Server API Endpoints | No health check for Sentry/OTel connectivity — server can be... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 33 | A058 | FIX NOW | Server API Endpoints | Server /metrics returns raw JSON dict, not Prometheus exposi... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 34 | A061 | FIX NOW | Server API Endpoints | Keygen revocation webhook is replayable because it checks HM... | ~3h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 35 | A062 | FIX NOW | Server API Endpoints | HTML injection via _SEO_PRICE_* env vars — operator-controll... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 36 | A063 | FIX NOW | Server API Endpoints | Keygen webhook has no rate limiting — resource exhaustion ve... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 37 | A064 | FIX NOW | Server API Endpoints | _route_warp and _route_warp_raw missing URL length validatio... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 38 | A065 | FIX NOW | Server API Endpoints | _route_benchmark missing domain length validation (253 char ... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 39 | A066 | FIX NOW | Server API Endpoints | _route_investigate missing domain validation at route level | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 40 | A067 | FIX NOW | Server API Endpoints | _route_leak_verify and _route_leaktest_check missing token l... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 41 | A068 | FIX NOW | Server API Endpoints | _route_activate missing license key upper length bound | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |
| 42 | A078 | FIX NOW | Server API Endpoints | Warp route error messages leak internal IP addresses and net... | ~1-2h | `cd Server && python -m pytest tests/ -v --tb=short` |

## Phase Dependency Summary

Key dependency chains (from `dep_edges`):

- Server warp chain: A002 → A003 → A004 → A007 → A008 → A009 → A010 → A041 → A054 → E005 → E006
- Server API chain: A005 → A011 → A012 → A015 → A020 → A023 → A024 → A025 → A028 → A037 → ... → A078
- DB chain: A014 → A018 → A021 → A083 → E010 → E024 → E025 → E035 → E045
- DNSSEC chain: A013 → A016 → A017 → A027 → A030 → A077
- Investigation chain: A019 → A047 → A049 → A076 → E042
- Licensing chain: E017 → E018 → E009 → E011 → E023 → E050
- Synthesizer C# chain: B003 → B034 → B036 → B037 → B038 → B039 → B040 → B041 → B042 → B043 → B044 → C008
- Matrix C# chain: B004 → B025 → B026 → B027 → B028 → B029 → B030 → B031 → C006 → C012
- DNSResolver chain: B008 → B032 → B033 → C007

## Contract

- The plan is executable by parallel agents.
- No two phases touch the same file unless the dependency is explicit and ordered.
- Each phase includes a verification gate.
- Effort estimates are advisory, not commitments.
- Detail level: 5 (granular) — full implementation brief per delta.
- Multi-delta phases: 28 (full sections above)
- Single-delta phases: 25 (summary table)
- Total phases: 53