# spec.md — Fix Test Integrity Findings

**Track ID:** fix-test-integrity_20260808
**Type:** chore (remediation)
**Status:** new

## Overview

Fix all 15 🔴 Critical and 🟡 Should Fix findings from the check-test-adequacy_20260808 audit.
Source changes that were left uncommitted will be committed. Tests will be hardened against future
drift. No new features — this track restores honesty to tests that were weakened during
fix-build-errors.

## Scope

| ID | Severity | Platform | What | Fix |
|----|----------|----------|------|-----|
| S-01 | 🔴 | Python | TriState test expects 4, enum has 3 | Commit `UNKNOWN` enum member to `types.py` |
| S-02 | 🔴 | Swift | Font count test expects 24, source has 23 | Commit `EBGaramond-Regular` to `bundledFonts` |
| G-01 | 🔴 | Cross | behavioral-vector-runner.py UNTRACKED | `git add` runner + corpus |
| S-03 | 🟡 | Python | 5 mock signatures relaxed with `**kwargs` | Remove `**kwargs`, declare explicit params |
| A-01 | 🟡 | Swift | CrossVantageConsensus missing counter | Add WarpEngineTests house pattern |
| A-02 | 🟡 | Swift | WitnessCrossPlatform hardcoded 3-vector list | Derive from directory, add counter |
| D-01 | 🟡 | C# | Catch-all exception helper | Narrow to `catch (Exception)` |
| D-02 | 🟡 | C# | Zero-assertion characterization test | Add `Record.Exception` / `Assert.Null` |
| D-03..09 | 🟡 | C# | 7 OS-gated early returns | Replace `return` with `Assert.Skip` |
| G-02 | 🟡 | Cross | cli-surface-gate RED (2 unledgered) | Ledger the `speed` command asymmetries |
| G-03 | 🟡 | Cross | parity-behavioral no nonzero guard | Add minimum-executed-count assertion to CI |

## Acceptance Criteria

- [ ] S-01: `test_three_distinct_members` passes on clean checkout (enum has 4 members)
- [ ] S-02: `testEveryDeclaredFontResourceContainsItsDeclaredPostScriptName` passes on clean checkout (24 fonts)
- [ ] G-01: `git ls-files scripts/behavioral-vector-runner.py` returns the file
- [ ] S-03: No `**kwargs` in mock function signatures in test_payments.py or test_resolver_fetcher_ssrf.py
- [ ] A-01: CrossVantageConsensusGoldenVectorTests asserts `evaluated == cases.count`
- [ ] A-02: WitnessCrossPlatformGoldenVectorTests derives vector list from directory, asserts count
- [ ] D-01: `Try` helper catches `Exception` not all exceptions
- [ ] D-02: Characterization test has explicit assertion
- [ ] D-03..09: All 7 OS-gated tests use `Assert.Skip` or equivalent
- [ ] G-02: cli-surface-gate.py exits 0 (all asymmetries ledgered or fixed)
- [ ] G-03: parity-behavioral CI step has nonzero guard
- [ ] `swift build --package-path macOS` zero errors
- [ ] `dotnet build` zero errors on macOS-buildable subset
- [ ] `ruff check Server/` exits 0
- [ ] `ty` exits 0

## Out of Scope

- 💡 Consider findings (A-03, A-04, D-10, D-11, T-01) — separate track
- Fixing the `speed` command asymmetry itself — ledgering only
- Running full CI (C-06 blocked)
- Windows GUI tests (can't build SyxxUI on macOS)