# plan.md — Check Test Adequacy

**Track ID:** check-test-adequacy_20260808
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-08
**Status:** [x] Complete

## Overview

Six phases, all detection-only. No test code is modified. Every finding carries file:line evidence
and a severity ranking. The report is the sole deliverable.

---

## Phase 1: Suite Inventory

Catalog every test suite across all four platforms. This is the foundation every other phase
depends on.

### Tasks

- [x] Task 1.1: Inventory Swift test suites — list every test file under `macOS/Tests/`, count
      test methods per file, classify each as corpus-driven (iterates over input files) or static,
      note whether it asserts evaluated count
- [x] Task 1.2: Inventory C# test suites — list every test file under `Windows/dotNET/Syxx.Tests/`,
      count test methods, classify corpus-driven vs static, note evaluated-count assertion
- [x] Task 1.3: Inventory Python test suites — list every test file under `Server/tests/`,
      count test functions, classify corpus-driven vs static
- [x] Task 1.4: Inventory cross-platform gates — `scripts/cross-platform-parity-gate.py`,
      `scripts/cli-surface-gate.py`, `scripts/behavioral-vector-runner.py`, CI gate steps in
      `.github/workflows/ci.yml`
- [x] Task 1.5: Publish the inventory as a table: platform, file, test count, corpus-driven (Y/N),
      evaluated-count assertion (Y/N), last modified date

### Verification

- [x] Every test file under `macOS/Tests/`, `Windows/dotNET/Syxx.Tests/`, and `Server/tests/` appears
      in the inventory
- [x] Classification is evidenced by reading the test file, not by filename alone

---

## Phase 2: Anti-Vacuity Audit

For every corpus-driven harness identified in Phase 1, verify the two workflow.md rules.

### Tasks

- [x] Task 2.1: Swift — verify `WarpEngineTests` asserts `evaluated == case_count` and the counter
      is nonzero-guarded. Verify the proven-red run from cross-platform-parity Task 5.2 still holds
      (re-run with a deliberately truncated case)
- [x] Task 2.2: Swift — audit remaining corpus-driven suites (VersionConsistencyTests,
      GuidedSetupLifecycleTests, any others found in Phase 1) for the same anti-vacuity pattern
- [x] Task 2.3: C# — verify `MatrixGoldenCorpusTests` asserts evaluated count. Audit any other
      corpus-driven C# suites for anti-vacuity
- [x] Task 2.4: Python — audit `Server/tests/` for any corpus-driven or parametrized tests that
      could silently skip. Flag any that iterate without counting
- [x] Task 2.5: Cross-platform — verify `parity-transcript` job in CI asserts `caseCount ==
      evaluated == records`. Verify the case-count guard added in cross-platform-parity Task 6.2
      is present and nonzero-guarded
- [x] Task 2.6: For every harness that lacks anti-vacuity, file a finding with: the harness path,
      what it silently skips, the severity, and the recommended fix pattern

### Verification

- [x] Every corpus-driven harness has a documented yes/no for anti-vacuity
- [x] Every "no" has a filed finding with severity and fix pattern
- [x] Proven-red runs are re-verified where feasible

---

## Phase 3: Softened Test Detection

Git-diff every test file modified in the fix-build-errors track against the pre-track baseline.
Flag any weakening.

### Tasks

- [x] Task 3.1: Identify the pre-track baseline commit for fix-build-errors_20260807. Run
      `git log --oneline --all` to find the commit before the track started (on or before 2026-08-07)
- [x] Task 3.2: Diff every test file modified in fix-build-errors — `git diff <baseline>..HEAD --
      macOS/Tests/ Windows/dotNET/Syxx.Tests/ Server/tests/`. Flag files with changes
- [x] Task 3.3: For each changed test file, classify every diff hunk:
      - **Relaxed assertion** — expected value changed to match current output, tolerance broadened
      - **Removed assertion** — assertion deleted without replacement
      - **Mock replacement** — real integration test replaced with mock
      - **New test** — test added (not a softening concern, but note for completeness)
      - **Refactor** — structural change with no semantic effect on assertions
- [x] Task 3.4: For each softened assertion, produce a before/after snippet with file:line. Rank
      severity: 🔴 if it hides a real defect, 🟡 if it weakens confidence, 💡 if it's a best-practice
      concern
- [x] Task 3.5: Check the `ty` type-checking fixes in Phase 4 of fix-build-errors. If any type
      annotations were changed to `Any` or `Optional` to silence errors, flag them

### Verification

- [x] Every test file modified in fix-build-errors is accounted for
- [x] Every diff hunk is classified
- [x] Softened assertions have before/after evidence

---

## Phase 4: Dead Test Path Detection

Find tests that always pass regardless of code behavior.

### Tasks

- [x] Task 4.1: Swift — grep for `try?` and `XCTSkip` in `macOS/Tests/`. Flag any `try?` that
      silently swallows a failure where `try` + `XCTAssertNoThrow` would expose it. Flag any
      `XCTSkip` without a recorded reason
- [x] Task 4.2: Swift — identify tests with zero assertions (empty test bodies, or test bodies
      that only call setup/teardown). Flag any mock pass-throughs where the mock is configured
      to return the expected value and the test asserts the mock returned it
- [x] Task 4.3: C# — grep for `Assert.Pass()` called unconditionally, `try-catch` that swallows
      exceptions without failing, tests with zero assertions
- [x] Task 4.4: Python — grep for `pytest.skip`, `@pytest.mark.skip`, bare `except: pass`, and
      test functions with zero assertions
- [x] Task 4.5: Cross-reference with the fix-build-errors diff. Flag any test that was modified
      to add a skip, a `try?`, or a catch-swallow during that track
- [x] Task 4.6: File each dead path as a finding with severity and recommended fix

### Verification

- [ ] Every `try?`, skip, and zero-assertion test is accounted for
- [ ] Cross-reference with fix-build-errors diff is complete

---

## Phase 5: Gate Integrity

Verify every gate can fail. A gate that cannot go red is measuring nothing.

### Tasks

- [x] Task 5.1: `cross-platform-parity-gate.py` — verify the proven-red run from
      cross-platform-parity Task 2.5 is still reproducible. Check that the gate exits non-zero on
      empty input, on a single transcript, and on a deliberately perturbed vector
- [x] Task 5.2: `cli-surface-gate.py` — verify the proven-red run from cross-platform-parity
      Task 3.2. Check anti-vacuity guards (empty extraction, stale-exception hygiene)
- [x] Task 5.3: `behavioral-vector-runner.py` — verify it refuses to run on empty input. Check that
      skipped vectors are counted and reported, not silently dropped
- [x] Task 5.4: CI gates in `.github/workflows/ci.yml` — audit the parity-transcript job, the
      parity-behavioral job, and the parity job. For each: does it fail on empty input? Does it
      assert case count? Does it have a drift gate? Record what is verifiable locally vs what
      requires CI (C-06 blocked)
- [x] Task 5.5: For any gate that cannot be proven to fail locally, file a finding documenting
      what would be needed to prove it (e.g., "requires CI run — blocked on C-06")

### Verification

- [ ] Every gate has a documented yes/no/blocked for "proven to fail"
- [ ] Anti-vacuity guards are verified on every gate that can run locally

---

## Phase 6: Report

Compile all findings into a single report artifact.

### Tasks

- [x] Task 6.1: Compile findings from Phases 2–5 into a single report: `conductor/tracks/
      check-test-adequacy_20260808/test-adequacy-report.md`
- [x] Task 6.2: Executive summary — total suites inventoried, total tests, findings count by
      severity (🔴 Critical / 🟡 Should Fix / 💡 Consider)
- [x] Task 6.3: Per-platform sections — Swift findings, C# findings, Python findings,
      cross-platform findings. Each finding: file:line, severity, what was found, recommended fix
- [x] Task 6.4: Add a "Not Covered" section — what this audit could not verify (CI-only gates
      blocked on C-06, Windows-only tests that can't run on macOS, any suite the inventory missed)
- [x] Task 6.5: Ledger each finding in the report with a unique ID (A-01, A-02, …) so individual
      findings can be spun into remediation tracks
- [x] Task 6.6: Record the report in `conductor/tracks.md` under this track's entry

### Verification

- [ ] Report exists at the specified path
- [ ] Every finding has file:line evidence, severity, and recommended fix
- [ ] Not Covered section is honest about what couldn't be checked
- [ ] No test code was modified

---

## Final Verification

- [ ] All acceptance criteria from spec.md met
- [ ] Report is the only artifact produced — no code changes
- [ ] Every finding carries one of the five verification states from workflow.md
- [ ] Ready for review

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._