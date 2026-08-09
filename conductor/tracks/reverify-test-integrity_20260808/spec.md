# spec.md — Re-Verify Test Integrity (Clean Checkout)

**Track ID:** reverify-test-integrity_20260808
**Type:** chore (verification)
**Status:** new

## Overview

Re-verify all 15 🔴/🟡 findings from check-test-adequacy_20260808 on a **clean checkout** —
committed code only, no working tree changes. The fix-test-integrity_20260808 track committed
fixes for 14 of 15 findings. This track proves which fixes are complete and which still need
attention.

## Scope — All 15 Findings

| ID | Severity | Finding | Fix Commit | Expected |
|----|----------|---------|-----------|----------|
| S-01 | 🔴 | TriState test expects 4, enum had 3 | `d133a8a` + `984e802` | PASS |
| S-02 | 🔴 | Font count test expects 24, source had 23 | NOT committed | FAIL |
| G-01 | 🔴 | behavioral-vector-runner.py UNTRACKED | `d577d9a` | PASS |
| S-03 | 🟡 | 5 mock `**kwargs` | `0c50430` | PASS |
| A-01 | 🟡 | CrossVantageConsensus missing counter | `3b2e255` | PASS |
| A-02 | 🟡 | WitnessCrossPlatform hardcoded list | `3b2e255` | PASS |
| D-01 | 🟡 | Catch-all exception helper | `774235c` | PASS |
| D-02 | 🟡 | Zero-assertion characterization test | `774235c` | PASS |
| D-03..09 | 🟡 | 7 OS-gated returns | xUnit v2 limitation | deliberately-out |
| G-02 | 🟡 | cli-surface-gate RED | `201c8a2` | PASS |
| G-03 | 🟡 | parity-behavioral nonzero guard | `201c8a2` | PASS |

## Method

1. `git stash --include-untracked` — working tree clean
2. Run each verification
3. `git stash pop` — restore working tree
4. Report pass/fail for each finding

## Acceptance Criteria

- [ ] S-01: `python3 -m pytest Server/tests/test_types.py -v` — 5/5 pass
- [ ] S-02: `swift test --package-path macOS --filter TypographyResourcesTests` — expected FAIL
- [ ] G-01: `git ls-files scripts/behavioral-vector-runner.py` — file tracked
- [ ] S-03: `grep -r '\*\*kwargs' Server/tests/test_payments.py Server/tests/test_resolver_fetcher_ssrf.py` — zero matches
- [ ] A-01: `grep 'evaluated.*cases.count' macOS/Tests/SyxxKitTests/CrossVantageConsensusGoldenVectorTests.swift` — present
- [ ] A-02: `grep 'contentsOfDirectory' macOS/Tests/SyxxKitTests/WitnessCrossPlatformGoldenVectorTests.swift` — present
- [ ] D-01: `grep 'catch (Exception)' Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs` — present
- [ ] D-02: `grep 'Record.Exception' Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs` — present
- [ ] G-02: `python3 scripts/cli-surface-gate.py` exits 0
- [ ] G-03: `grep 'PASS' .github/workflows/ci.yml` in parity-behavioral job — nonzero guard present
- [ ] Python tests: 81/81 pass
- [ ] `swift build --package-path macOS` zero errors
- [ ] Working tree restored

## Out of Scope

- Fixing S-02 (belongs to gui-complete-macos track)
- Full anti-vacuity re-audit of all harnesses
- Running dotnet build/tests
- Running full test suites (keychain prompt risk)

## Expected Outcome

13 of 14 committed fixes should pass. S-02 will fail because the EBGaramond-Regular source change
is still in the gui-complete-macos working tree. This confirms exactly one finding remains open.