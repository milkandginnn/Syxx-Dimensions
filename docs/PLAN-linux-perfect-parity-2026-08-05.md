# Linux → macOS Perfect Parity: Comprehensive Implementation Plan

- **Date:** 2026-08-05
- **Produced by:** 5-seat review team (feature-parity, architecture, testing, observability, security)
- **Supersedes:** Complements `PLAN-linux-parity-remaining-2026-08-05.md` (security/secret-store focus)
  and `PLAN-linux-parity-team-2026-08-04 - saved.md` (original scope + gap register)
- **Goal:** Perfect functional parity of the Linux client with macOS, excluding GUI files and
  platform-specific frameworks/SDKs that have no cross-platform analog.
- **Status:** PLANNING ONLY. No code changes. All work to be performed by subagents.

---

## 0. Audit Summary

### What the 5 reviewers found

| Dimension | Agent | Key Finding |
|-----------|-------|-------------|
| **Feature Parity** | Feature audit | 1 CRITICAL missing module (Stage5Presentation), 3 MEDIUM missing modules, 6+ missing functions in shared modules, 1 behavior divergence |
| **Architecture** | Architecture audit | .NET secret storage is architecturally *superior* to macOS; indicator strength divergence (0.80 vs 0.95) is a concrete parity bug |
| **Testing** | Testing audit | macOS: 770 tests, .NET: 773 tests, Linux: 96 tests; zero Linux tests for probes/synthesis/DNS; 26 quarantined macOS tests |
| **Observability** | Observability audit | OTel span names: 5/5 identical ✅; macOS missing 5 resource attributes; no mid-session consent withdrawal on .NET; Sentry tag redaction gap |
| **Security** | Security audit | S-1 through S-4 FIXED; DNSSEC missing C059+C125 validation on .NET (HIGH); process security gaps on both platforms |

### Parity gate status: **GREEN**

```
scripts/cross-platform-parity-gate.py → PASS (exit 0)
  - 5 span names identical
  - 6 wire vocabularies byte-identical
  - Signed payload keys match
```

### What is NOT a gap (consolidation pattern)

The .NET side consolidates multiple Swift files into fewer, larger files. These are **not** missing:

| macOS Files (separate) | .NET Location (consolidated) |
|------------------------|------------------------------|
| BeaconAnalyzer, Blocklist, ExfilAnalyzer, SieveBlocklistLoader | `Forensics.cs` (429 lines) — all 4 types present |
| AnswerLedger, IntegrityTag, Sentinel | `Trust.cs` (565 lines) — all 3 types present |
| ARPTableParser, CommandRunner, NetworkAddressClassifier | `LanCartography.cs` (920 lines) — all 3 types present |

---

## 1. Scope

### In scope

- `Windows/dotNET/Syxx.Kit/**` — shared core library (Linux behavior)
- `Windows/dotNET/Syxx/**` — shared CLI (Linux behavior)
- `Windows/dotNET/Syxx.Telemetry/**` — telemetry bootstrap
- `macOS/Sources/SyxxKit/**` — reference implementation (Swift twin)
- `macOS/Sources/SyxxTelemetry/**` — telemetry reference
- `macOS/Sources/SyxxCLI/**` — CLI reference
- `Linux/Ubuntu/SyxxUI.Avalonia.Tests/**` — Linux test surface (non-GUI tests only)
- `scripts/cross-platform-parity-gate.py` — parity gate
- CI pipeline (`.github/workflows/ci.yml`)

### Hard keep-out

- **All GUI files**: `.axaml`, `.xaml`, ViewModels bound to GUI-only surfaces, views, styles, assets
- **Platform-specific with no analog**: Ccage (C pktap/BPF), DeepSyxx (launchd XPC), PktapHeader,
  PrivilegedHelperProtocol, DNSCryptProxyManager, DNSCryptProxyConfig, UnboundManager (macOS daemon)
- **DNSPathConfirmation.swift**: Tightly coupled to macOS XPC/launchd — not portable without re-architecture
- **FlowTransportObserver.swift**: Uses macOS `nettop` — Linux equivalent needs `ss`/`/proc/net/`

### Boundary note

Files that are "pure logic" on macOS but currently have no .NET twin are in scope even if they
touch the Cage/transport domain, as long as the logic itself is platform-independent
(e.g., `CageRedundancyCorrelator` — explicitly documented as "PURE logic, no I/O, no root, no BPF").

---

## 2. Phased Plan

### Phase 0 — Immediate Fixes (Critical Parity Bugs)

**These are concrete parity bugs discovered by the audit that exist right now and are
independent of any missing module work.**

#### P-01: Indicator strength divergence (architecture finding)

- **What:** macOS maps `authoritativeDivergesConfirmed` → 0.80 strength; .NET maps it → 0.95
- **Where:** `Synthesizer.swift` vs `Synthesizer.cs` — indicator strength mapping
- **Severity:** HIGH — produces different synthesis conclusions for identical input
- **Fix:** Align to one value. Determine which is correct from the synthesis spec, fix the other.
- **Files:** `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift`, `Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs`
- **Gate:** Golden vector tests on both platforms produce identical output

#### P-02: DenialOfExistence.notADenial divergence (feature finding)

- **What:** Swift maps `.notADenial` to `break` (skip — contributes nothing); C# maps `NotADenial` to `.Negative` (clean)
- **Where:** `Synthesizer.swift` / `Synthesizer.cs` — denial handling
- **Severity:** MEDIUM — Swift comment B061 explicitly says "absence of applicability is not cleanliness"
- **Fix:** Align behavior. If B061 is correct, C# should skip (`break`) rather than mapping to `.Negative`.
- **Files:** Same as P-01
- **Gate:** New test: `NotADenial` input produces `inconclusive` (not `clean`) on both platforms

#### P-03: DNSSEC validation missing C059+C125 on .NET (security finding)

- **What:** macOS validates owner/signer binding (C059) and DNSKEY field validation (C125: protocol=3, ZONE flag, algorithm≠0). .NET accepts ANY DNSKEY/RRSIG regardless of owner.
- **Where:** `DNSSECVerifier.cs` vs `DNSSECVerifier.swift`
- **Severity:** HIGH — .NET could validate a DNSSEC chain against the wrong signer
- **Fix:** Port C059 owner binding and C125 DNSKEY field validation from Swift to C#
- **Files:** `Windows/dotNET/Syxx.Kit/DNSSECVerifier.cs`
- **Gate:** New test: DNSSEC verification rejects RRSIG with non-matching signer name

#### P-04: macOS OTel resource attributes gap (observability finding)

- **What:** macOS OTelBootstrap sends only `service.name`; .NET sends 5 additional attributes (`service.namespace`, `service.version`, `deployment.environment.name`, `os.type`, `syxx.surface`)
- **Where:** `macOS/Sources/SyxxTelemetry/OTelBootstrap.swift`
- **Severity:** MEDIUM — macOS invisible to dashboard queries filtering by os/surface/version
- **Fix:** Add the 5 resource attributes to macOS OTel bootstrap to match .NET
- **Note:** This is a macOS fix, not a Linux fix — but it's parity work
- **Gate:** Both platforms emit identical resource attribute sets

---

### Phase 1 — Missing Cross-Platform Modules

**These are genuinely cross-platform modules that exist on macOS and should exist on .NET.**

#### P-05: Stage5Presentation (CRITICAL — largest gap)

- **What:** 534-line Stage-5 engine-to-presentation DTO contract: `CopyToken`, `Stage5Presentation`, `ProvenancePartition`, `AnswerMatrixRow`, `MechanismDetermination`, `PrioritizedFinding`, validation invariants, canonical JSON encoding
- **Where:** `macOS/Sources/SyxxKit/Synthesis/Stage5Presentation.swift`
- **Why critical:** File header explicitly states "The Windows twin is `Windows/Syxx.Kit/Synthesis/Stage5Presentation.cs`; the two must decode one shared v1 fixture to equivalent graphs and re-encode byte-identically (STAGE5 §11)."
- **Fix:** Port the full 534-line file to `Windows/dotNET/Syxx.Kit/Synthesis/Stage5Presentation.cs`
- **Gate:** Both platforms decode a shared v1 fixture to equivalent graphs and re-encode byte-identically
- **Subagent:** Implementer with `dotnet-contribution:dotnet-backend-patterns` skill

#### P-06: CageRedundancyCorrelator (MEDIUM)

- **What:** ~200-line pure-logic module comparing two independent DNS capture streams (daemon vs Cage) and producing agree/disagree/incomparable results
- **Where:** `macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift`
- **Why cross-platform:** File explicitly states "PURE logic — no I/O, no root, no BPF" and "A future Windows Cage twin can reuse it unchanged"
- **Fix:** Port to `Windows/dotNET/Syxx.Kit/CageRedundancyCorrelator.cs`
- **Gate:** Existing macOS tests pass on .NET with same inputs

#### P-07: DNSTransportMonitor (MEDIUM-HIGH)

- **What:** ~130-line module driving periodic DNS transport observation ticks, publishing `DNSTransportSample` to a state store
- **Where:** `macOS/Sources/SyxxKit/DNSTransportMonitor.swift`
- **Cross-platform portion:** The monitor/tick/state-store logic is platform-independent; only the sample source (`FlowTransportObserver`) is macOS-specific
- **Fix:** Port the monitor logic to .NET with a pluggable sample source interface. Linux sample source can use `ss`/`/proc/net/` instead of `nettop`.
- **Gate:** Monitor produces equivalent `DNSTransportSample` sequences from equivalent inputs

#### P-08: CageHelpers (LOW)

- **What:** ~36-line shared helpers for cage capture file directory resolution and newest-file discovery
- **Where:** `macOS/Sources/SyxxKit/CageHelpers.swift`
- **Fix:** Port to .NET — simple filesystem logic using `SyxxPaths.cs` conventions
- **Gate:** Cage capture directory resolves correctly on Linux (`~/.syxx/captures`)

---

### Phase 2 — Missing Functions in Shared Modules

**Functions that exist on macOS in files that already have .NET twins.**

#### P-09: Synthesizer.transportObservation input

- **What:** Swift `SectionInputs` carries `transportObservation: DNSTransportWindow?` as a shared input for Prophet+Mirror sections
- **Where:** `Synthesizer.swift` `SectionInputs` struct
- **Fix:** Add equivalent field to .NET `SectionInputs` and wire it through the synthesis pipeline
- **Gate:** Transport observation reaches Prophet+Mirror synthesis on both platforms

#### P-10: WarpEngine remote functions

- **What:** macOS has `verify(signature:manifest:htmlState:assets:)`, `fetchServerCleanAsset(url:client:)`, `runVantageCheck(origin:client:)` — standalone verification/fetch functions
- **Where:** `WarpEngine.swift`
- **Cross-platform?** Partially — the crypto verification is portable; the HTTP fetch functions depend on DimensionClient which exists on both platforms
- **Fix:** Port the verification function. Evaluate whether the fetch functions belong in CLI scope.
- **Gate:** WarpEngine verification produces identical results on both platforms

#### P-11: SyxxTracer runtime control

- **What:** macOS has `disable()`/`enable()` for mid-session consent withdrawal and `resetTracerForTesting()`
- **Where:** `SyxxTracer.swift`
- **Fix:** Add `Disable()`/`Enable()` and `ResetForTesting()` to .NET `SyxxTracer.cs`
- **Gate:** Consent withdrawal stops span export on both platforms

---

### Phase 3 — Observability Alignment

#### P-12: .NET Sentry tag redaction (privacy gap)

- **What:** macOS `SentryDiagnosticSink` does NOT redact tag values (B224 gap); .NET DOES redact. macOS is the weaker implementation.
- **Where:** `macOS/Sources/SyxxTelemetry/SentryDiagnosticSink.swift`
- **Fix:** Add B224 tag redaction to macOS SentryDiagnosticSink to match .NET
- **Gate:** Tag values containing IPs/domains/paths are redacted on both platforms

#### P-13: .NET SentryBootstrap centralization

- **What:** .NET has no `SentryBootstrap.cs` — Sentry init is inline in `Program.cs` and `App.xaml.cs`
- **Fix:** Extract into `Windows/dotNET/Syxx.Telemetry/SentryBootstrap.cs` matching macOS pattern
- **Gate:** Both platforms initialize Sentry through a single centralized bootstrap

#### P-14: .NET CLI tracesSampleRate

- **What:** .NET CLI does not set `tracesSampleRate` for Sentry performance tracing
- **Fix:** Add `tracesSampleRate = 0.1` to CLI Sentry init (matching GUI and macOS)
- **Gate:** CLI spans appear in Sentry at the configured sample rate

#### P-15: .NET mid-session consent withdrawal

- **What:** macOS has `SyxxTracer.disable()` + provider swap + NoOpSpan for consent withdrawal mid-session. .NET has no equivalent.
- **Fix:** Implement in .NET (pairs with P-11)
- **Gate:** Spans created after consent withdrawal are NoOp on both platforms

---

### Phase 4 — Test Gap Closure

#### P-16: Linux probe test coverage (CRITICAL)

- **What:** Zero Linux-specific tests for any probe type. macOS has ~157 probe tests, .NET has ~121.
- **Fix:** Since probes run through the shared .NET Kit, existing `Syxx.Tests` probe tests cover Linux. Verify by running `dotnet test` with Linux RID. Add Linux-specific probe tests for:
  - `resolvectl status` parser (NetworkKeyState)
  - `ip neigh show` parser (LanCartography)
  - Linux VPN detection (NetworkKeyState)
  - Linux traceroute (seconds-vs-milliseconds unit fix)
- **Gate:** `dotnet test` green on Linux with ≥20 Linux-path-specific tests

#### P-17: Linux SelfVerify tests (from existing plan T-5)

- **What:** SelfVerify has zero tests on the one platform that has the affordance
- **Fix:** Add to `Linux/Ubuntu/SyxxUI.Avalonia.Tests/WitnessViewModelTests.cs`:
  - `SelfVerify_AfterSuccessfulSeal_ReturnsVerified`
  - `SelfVerify_NoSnapshot_PromptsUser`
  - `SelfVerify_StaleSnapshot_DoesNotShowGreen`
- **Gate:** 3 new tests pass

#### P-18: External-then-self contamination (from existing plan T-6)

- **What:** The defect requires ONE VM doing seal→paste external→self-verify. Current tests use two separate VMs.
- **Fix:** Add single-instance test on all three platforms
- **Gate:** Test passes on Linux, Windows, macOS

#### P-19: macOS quarantined test review

- **What:** 43 quarantined files containing 26 unique disabled tests
- **Fix:** Review each quarantined test — restore if still relevant, delete if obsolete
- **Gate:** Quarantine count reduced; each disposition documented

#### P-20: Severity-tier tests on Windows and macOS (from existing plan T-7)

- **What:** Linux has severity-tier tests (`WitnessViewModelTests.cs:335-345`) pinning exact ARGB for all 9 enum members. Windows and macOS have none.
- **Fix:** Add equivalent tests on both platforms
- **Gate:** Tier mapping tests pass on all 3 platforms

---

### Phase 5 — Architecture Alignment

#### P-21: macOS hasKey collapse (from existing plan G-17)

- **What:** macOS `WitnessKeyManager.hasKey` collapses `.undetermined` → `false`
- **Fix:** Propagate `.undetermined` through the return type
- **Depends on:** G-16b (DNSPathConfirmation.swift must be fixed first for Swift compilation)
- **Gate:** Test: Keychain unreachable → `.undetermined`, not `false`

#### P-22: macOS secret storage abstraction

- **What:** .NET has `ISecretStore` protocol; macOS calls Keychain directly with no abstraction
- **Recommendation:** ACCEPT DIVERGENCE for now. macOS Keychain is the only backend and wrapping it in a protocol adds complexity without benefit until a second macOS backend exists.
- **Revisit:** When macOS gains a non-Keychain storage option

#### P-23: macOS DNSPathConfirmation compilation blocker (G-16b)

- **What:** `DNSPathConfirmation.swift:90` — `DNSPathSnapshot` does not conform to `Equatable`. File is untracked.
- **Fix:** Add `Equatable` conformance (or claim/fix the untracked file)
- **Gate:** `swift build --package-path macOS --target SyxxKit` succeeds

---

### Phase 6 — CLI Parity

#### P-24: FirstRunCommand on macOS CLI

- **What:** .NET has `FirstRunCommand.cs`; macOS has no dedicated first-run CLI command
- **Assessment:** .NET-only feature (first-run scan runs inside the GUI on macOS). May not need macOS CLI equivalent.
- **Action:** Verify whether macOS GUI handles first-run internally. If so, document as justified divergence.

#### P-25: CLI command flag parity

- **What:** 15 macOS commands vs 16 .NET commands. Names are equivalent (SyxxNSLook → NslookCommand).
- **Action:** Audit each command's flags and output format for parity.
- **Gate:** Same command with same flags produces same output structure on both platforms

---

## 3. Execution Order

```
Phase 0: P-01 → P-02 → P-03 → P-04         (critical bugs, independent)
Phase 1: P-05 → P-06 → P-07 → P-08         (missing modules, P-05 is largest)
Phase 2: P-09 → P-10 → P-11                 (missing functions)
Phase 3: P-12 → P-13 → P-14 → P-15         (observability, can parallel with Phase 2)
Phase 4: P-16 → P-17 → P-18 → P-19 → P-20  (tests, can parallel with Phases 2-3)
Phase 5: P-23 → P-21 → P-22                 (architecture, P-23 unblocks P-21)
Phase 6: P-24 → P-25                         (CLI, lowest priority)
```

**Critical path:** Phase 0 (P-01 and P-03 are highest-severity parity bugs)

**Parallelizable:** Phases 2, 3, 4 can all run concurrently once Phase 0 lands.

**Dependencies:**

- P-21 depends on P-23 (Swift must compile)
- P-15 depends on P-11 (tracer runtime control)
- P-09 depends on P-07 (transport observation must exist to wire into synthesis)

---

## 4. Subagent Deployment Plan

### Recommended Team Composition

| Phase | Seats | Types | Notes |
|-------|-------|-------|-------|
| Phase 0 | 2 implementers + 1 auditor | `team-implementer` × 2, `team-reviewer` × 1 | P-01/P-02 in one commit (both Synthesizer), P-03 separate (DNSSEC), P-04 separate (macOS OTel) |
| Phase 1 | 2 implementers + 1 auditor | Same | P-05 is a dedicated seat (534-line port). P-06/P-07/P-08 in parallel. |
| Phase 2-3 | 2 implementers | `team-implementer` × 2 | Disjoint files, can run in parallel |
| Phase 4 | 1 implementer + 1 auditor | Test specialist + reviewer | Test writing and quarantined test review |
| Phase 5 | 1 implementer | `team-implementer` | Swift fixes, blocked on P-23 |
| Phase 6 | 1 researcher | `Explore` agent | CLI audit, read-only |

### File Ownership (per seat)

**Phase 0 — Seat A (Synthesis):**

```
macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift
Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
```

**Phase 0 — Seat B (DNSSEC + OTel):**

```
Windows/dotNET/Syxx.Kit/DNSSECVerifier.cs
macOS/Sources/SyxxTelemetry/OTelBootstrap.swift
```

**Phase 1 — Seat C (Stage5):**

```
Windows/dotNET/Syxx.Kit/Synthesis/Stage5Presentation.cs  (NEW)
Reference: macOS/Sources/SyxxKit/Synthesis/Stage5Presentation.swift
```

**Phase 1 — Seat D (Other modules):**

```
Windows/dotNET/Syxx.Kit/CageRedundancyCorrelator.cs     (NEW)
Windows/dotNET/Syxx.Kit/DNSTransportMonitor.cs          (NEW)
Windows/dotNET/Syxx.Kit/CageHelpers.cs                  (NEW, or merge into SyxxPaths.cs)
```

---

## 5. Items Deliberately Out of Scope

| Item | Reason |
|------|--------|
| FlowTransportObserver | macOS `nettop`-specific. Linux equivalent needs `ss`/`/proc/net/` — a separate engineering effort |
| DNSPathConfirmation | XPC/launchd coupled. Not portable without complete re-architecture |
| Ccage / DeepSyxx | Platform-specific privileged capture. Different architecture on each platform by design |
| GUI parity | Explicitly excluded. Separate stream (Mirror/Warp/Avalonia) |
| Process sandbox | macOS sandbox disabled, Linux has polkit but no AppArmor/seccomp. Security architecture decision, not parity work |
| macOS secret storage abstraction | .NET is architecturally superior; no second macOS backend exists to justify the protocol |

---

## 6. Verification Gates

These must be green at every phase boundary:

```bash
# Mechanical parity gates
python3 scripts/cross-platform-parity-gate.py     # span names + wire vocabularies
python3 scripts/package-pin-parity.py              # NuGet pin consistency
python3 scripts/check-eula-parity.py               # EULA text parity

# Build gates
dotnet build Windows/Syxx.Linux.slnf -c Release
swift build --package-path macOS --target SyxxKit  # (blocked by P-23 until Phase 5)

# Test gates
dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release
dotnet test Linux/Ubuntu/SyxxUI.Avalonia.Tests/SyxxUI.Avalonia.Tests.csproj -c Release
swift test --package-path macOS --filter SyxxKitTests  # (blocked by P-23 until Phase 5)
```

---

## 7. Summary Statistics

| Metric | Count |
|--------|-------|
| Total work items | 25 |
| Critical severity | 2 (P-01, P-05) |
| High severity | 4 (P-03, P-09, P-16, P-23) |
| Medium severity | 10 |
| Low severity | 9 |
| Missing modules to port | 4 |
| Missing functions to add | 6+ |
| Observability gaps to close | 4 |
| Test gaps to fill | 5 categories |
| Architecture divergences to resolve | 2 |
| Items deliberately accepted as divergent | 6 |
| Estimated phases | 6 |

---

## Appendix A: Existing Plan Cross-Reference

Items from `PLAN-linux-parity-remaining-2026-08-05.md` that this plan subsumes or complements:

| Existing Item | This Plan | Status |
|---------------|-----------|--------|
| S-1 TOCTOU | — | Already FIXED per security audit |
| S-2 Cached key ref | — | Already FIXED per security audit |
| S-3 Env var exposure | — | DOCUMENTED, dormant |
| S-4 StringBuilder zero | — | Already FIXED per security audit |
| G-16 libsecret P/Invoke | — | Still OPEN (complements this plan) |
| G-16b DNSPathConfirmation | P-23 | Carried forward |
| G-17 macOS hasKey collapse | P-21 | Carried forward |
| §7.4 parity gate RED | — | Already GREEN (tracer_outcome fixed) |
| T-5 SelfVerify tests | P-17 | Carried forward |
| T-6 External-then-self | P-18 | Carried forward |
| T-7 Severity-tier tests | P-20 | Carried forward |

## Appendix B: What Linux Already Does Better

The audit found several areas where Linux/.NET is the **reference implementation**:

1. **Secret storage architecture** — `ISecretStore` abstraction, `SecretExistence` 4-state enum, dual-oracle absence proof, passphrase-protected AES-256-GCM fallback
2. **Test coverage for witness severity tiers** — Linux is the only platform with exact ARGB pinning and distinctness assertions
3. **External-verify isolation** — `_lastSnapshot` prevents foreign-snapshot contamination (G-15)
4. **DiagnosticRedaction** — centralized in a separate file vs macOS's embedded approach
5. **OTel resource attributes** — 6 dimensions vs macOS's 1
6. **Sentry tag redaction** — B224 compliant (macOS is not)
7. **Probe test consolidation** — 114 tests in `ProbeTests.cs` vs 157 spread across 18 Swift files
