# Syxx Delta Remediation Plan

**Date:** 2026-08-07
**Total valid deltas:** 400
**Created by:** 16 audit agents across 2 rounds + 1 targeted round

## Phase Overview

| Phase | Scope | Deltas | Priority |
|-------|-------|--------|----------|
| 1 | Critical — false verdicts, forgeable evidence, server compromise | ~15 | Immediate |
| 2 | Server Python (A-domain) | ~49 | Sprint 1 |
| 3 | Detection Efficacy (B-domain) | ~42 | Sprint 1 |
| 4 | Verdict & Evidence Integrity (C-domain) | ~56 | Sprint 2 |
| 5 | macOS Swift (D-domain macOS) | ~50 | Sprint 2 |
| 6 | Windows C# (D-domain Windows) | ~50 | Sprint 3 |
| 7 | Linux C# (D-domain Linux) | ~30 | Sprint 3 |
| 8 | Security & Hardening (E-domain) | ~64 | Sprint 4 |
| 9 | CI/CD, Build, Tests (F-domain) | ~44 | Sprint 4 |

## Phase 1: Critical Deltas (Immediate)

These 15 deltas can cause false verdicts, forgeable evidence, or server compromise:

1. **A281** — dnssec_validator broken import (entire DNSSEC non-functional)
2. **A282** — warp.py brotli bomb (memory DoS)
3. **A309** — scheduler investigate lambda tuple-unpack crash
4. **C207** — Cross-platform indicator strength divergence (different verdicts per platform)
5. **C208** — Windows UseMatrixEngine kill-switch (structural asymmetry)
6. **C211** — Linux Witness Signed regardless of verification
7. **C212** — Windows FirstRunScan silent 8.8.8.8 substitution
8. **D300** — macOS DeepSyxx arbitrary PF rules (privilege escalation)
9. **D301** — macOS DeepSyxx DNS server injection (DNS hijack)
10. **E205** — Windows trial HMAC empty key (forgeable trial state)
11. **E210** — XSS via PTR record innerHTML
12. **E211** — XSS via terminal innerHTML
13. **E223** — No webhook signature tests (forgeable Stripe events)
14. **F079** — deploy.sh no rollback mechanism
15. **WG-042** — Windows DPAPI path traversal

## Phase 2: Server Python (A-domain, ~49 deltas)

Sub-phases:
- 2a: Scheduler fixes (A309-A313) — parallel execution, graceful shutdown
- 2b: Input validation (A273, A278, A280) — valid_domain, email format, checks JSON
- 2c: DNSSEC module (A277, A281, SM-021-SM-026) — fix imports, add Ed25519, deadlines
- 2d: Warp/content integrity (A282, SM-013-SM-017) — brotli fix, CSS comments, truncation
- 2e: Investigation accuracy (A279, SM-001-SM-012) — IDN, consensus, deadlines
- 2f: Ancillary modules (A274-A276, A314-A318, SM-027-SM-055) — lookalike, whois, firewall, seo, mailer

## Phase 3: Detection Efficacy (B-domain, ~42 deltas)

Sub-phases:
- 3a: Probe logic fixes (B271-B286, WIN-010-WIN-024) — CNAME chain, denial of existence, canary
- 3b: Cross-platform detection parity (B283, B324-B325) — qtype threading, DNS protocol enum
- 3c: Firewall test accuracy (B321-B323, SM-041-SM-045) — sinkhole expansion, vantage fallback

## Phase 4: Verdict & Evidence Integrity (C-domain, ~56 deltas)

Sub-phases:
- 4a: Cross-platform synthesis parity (C207-C212) — indicator strengths, matrix engine, 8.8.8.8 gates
- 4b: macOS verdict accuracy (C199-C201, C222-C225) — warp evidence, tri-state, types
- 4c: Windows verdict accuracy (C197-C200, WIN-001-WIN-009) — combine order, resolver substitution
- 4d: Linux verdict accuracy (C199-C206) — witness verification, mirror details

## Phase 5: macOS Swift (D-domain macOS, ~50 deltas)

Sub-phases:
- 5a: DeepSyxx security (D300-D301, M2-011-M2-016) — PF rules, DNS injection, XPC validation
- 5b: NetworkKeyState (D401-D409, B324) — async migration, protocol enum, drain timeout
- 5c: CLI and Telemetry (M2-001-M2-010, M2-023-M2-026) — exit codes, SIGINT, traces
- 5d: License and biometric (M2-027-M2-032) — race condition, HMAC key, grace period
- 5e: Probes and forensics (M2-033-M2-048) — DoH pinning, beacon variance, blocklist

## Phase 6: Windows C# (D-domain Windows, ~50 deltas)

Sub-phases:
- 6a: WinUI 3 views (WG-001-WG-015) — design system, virtualization, dead code
- 6b: ViewModels (WG-016-WG-027) — Pro gate, monitoring persistence, reset completeness
- 6c: Windows integration (WG-028-WG-038) — toast, tray, theme, telemetry
- 6d: Security (WG-039-WG-048) — DPAPI roaming, path traversal, SSRF, subdomain allowlist
- 6e: Accessibility (WG-049-WG-054) — severity badges, automation properties

## Phase 7: Linux C# (D-domain Linux, ~30 deltas)

Sub-phases:
- 7a: App lifecycle (L2-001-L2-009) — teardown, exception handlers, tracing
- 7b: Mirror engine (L2-010-L2-019) — O(m×n) bounds, line splitting, shared Kit move
- 7c: Views and controls (L2-020-L2-029) — null-forgiving, diff rendering, license UX
- 7d: ViewModels (L2-030-L2-043) — cancellation, async patterns, progress throttling

## Phase 8: Security & Hardening (E-domain, ~64 deltas)

Sub-phases:
- 8a: XSS remediation (E210-E211, HT-001-HT-007, SEC-001-SEC-004) — innerHTML → textContent
- 8b: Authentication & session (SEC-004-SEC-006, HT-005, HT-017) — httpOnly cookies, CSRF, webhook tests
- 8c: Cryptographic hygiene (SEC-008-SEC-009, SEC-026, SEC-049) — cert pinning, HMAC sub-keys
- 8d: Server hardening (SEC-013-SEC-025, SEC-030-SEC-050) — SSRF escape hatch, rate limiting, headers

## Phase 9: CI/CD, Build, Tests (F-domain, ~44 deltas)

Sub-phases:
- 9a: CI pipeline (CI-001-CI-011) — arm64 leg, vulnerability scanning, SBOM
- 9b: Build scripts (CI-012-CI-017) — code signing, DMG, WiX, Telemetry
- 9c: Deployment safety (CI-018-CI-027, F079) — rollback, backup, health checks
- 9d: Package management (CI-028-CI-035) — Dependabot, version bounds, SBOM
- 9e: Test infrastructure (CI-036-CI-044) — integration tests, coverage, benchmarks

## Execution Strategy

Each phase will be executed by specialized subagents per SUBAGENT-RULES.md:
- **Server Python**: `python-development__python-pro` agents
- **macOS Swift**: `multi-platform-apps__ios-developer` agents
- **Windows C#**: `jvm-languages__csharp-pro` agents
- **Linux C#**: `error-debugging__error-detective` agents
- **Cross-platform**: `comprehensive-review__code-reviewer` agents
- **CI/CD**: `cicd-automation__deployment-engineer` agents

Each implementation agent will:
1. Read the specific delta(s) from DELTAS.md
2. Implement the fix(es) in the source code
3. Verify the fix doesn't break existing functionality
4. Report the delta as ALREADY FIXED in DELTAS.md
