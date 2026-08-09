# spec.md — Check Test Adequacy

**Track ID:** check-test-adequacy_20260808
**Type:** chore (audit)
**Status:** new

## Overview

Audit every test suite across all four platforms for softened tests, anti-vacuity violations, and
dead test paths. The fix-build-errors track touched code across Swift, C#, and Python — this track
verifies that no test was weakened to make faulty code pass, and that every harness honestly reports
what it measured.

Governed by the two rules from `conductor/workflow.md`:

1. **A harness must prove how many cases it ran.** A suite that silently skips is worse than no suite.
2. **A gate must be able to fail.** A gate that cannot go red is measuring nothing.

## Scope

| Platform | Test suites | What to check |
|----------|------------|---------------|
| macOS (Swift) | `macOS/Tests/` — SyxxKitTests, SyxxAppTests, WarpEngineTests, PurpleScrollViewTests, etc. | Anti-vacuity, soft assertions, dead paths |
| Windows/Linux (C#) | `Windows/dotNET/Syxx.Tests/` — MatrixGoldenCorpusTests, etc. | Anti-vacuity, soft assertions, dead paths |
| Linux (Avalonia) | `Linux/Ubuntu/SyxxUI.Avalonia.Tests/` | Build-only parity, anti-vacuity |
| Server (Python) | `Server/tests/` — test_netutil, test_payments, etc. | Anti-vacuity, soft assertions, dead paths |
| Cross-platform | `corpus/parity-vectors/`, `corpus/behavioral-vectors/` | Gate integrity, case-count guards |

## Functional Requirements

### FR-1: Suite Inventory
Produce a complete inventory of every test suite with:
- Platform, test count, harness type (XCTest/xUnit/pytest)
- Whether it is corpus-driven (iterates over input files) or static
- Whether it asserts evaluated count equals input count

### FR-2: Anti-Vacuity Audit
For every corpus-driven harness, verify:
- It counts how many cases it evaluated
- It asserts `evaluated == case_count`
- It fails on empty input (zero cases)
- It fails when a case is silently skipped
- It names the skipped case, not just the count

### FR-3: Softened Test Detection
For every test file modified in the fix-build-errors track (by git diff against the pre-track baseline):
- Flag any assertion that was relaxed (broadened tolerance, removed assertion, mock-ified real check)
- Flag any mock that replaced a real integration test
- Flag any test whose expected value changed to match current (possibly broken) output
- Provide file:line evidence and before/after diff

### FR-4: Dead Test Path Detection
Identify tests that:
- Always pass regardless of code behavior (mock pass-throughs, no assertions)
- Have unreachable assertion branches
- Use `try?` or `catch` that silently swallows failures
- Are marked `skip` or `XCTSkip` without a recorded reason

### FR-5: Gate Integrity
For every CI gate and parity gate:
- Verify it can be proven to fail (deliberate divergence → red)
- Verify it fails on empty input
- Verify it fails on incomplete input
- Record the last proven-red run for each gate

### FR-6: Report
Produce a single report artifact with:
- Executive summary (total suites, total tests, findings count by severity)
- Per-platform findings with file:line evidence
- Severity ranking: 🔴 Critical (hides real defects), 🟡 Should Fix (weakens confidence), 💡 Consider (best practice)
- Recommended fixes for each finding
- No code changes — findings only

## Acceptance Criteria

- [ ] Every test suite across all four platforms is inventoried
- [ ] Every corpus-driven harness verified to assert `evaluated == case_count`
- [ ] Every gate verified to fail on a deliberate divergence, or documented as unverified
- [ ] All softened tests (if any) identified with file:line evidence and before/after diff
- [ ] All dead test paths (if any) identified
- [ ] Report delivered with severity-ranked findings and recommended fixes
- [ ] No test code is modified by this track

## Out of Scope

- Fixing any findings — this track is detection-only
- Running tests on Windows (can't build SyxxUI on macOS)
- Performance benchmarking of test suites
- Adding new tests to cover gaps (separate track)