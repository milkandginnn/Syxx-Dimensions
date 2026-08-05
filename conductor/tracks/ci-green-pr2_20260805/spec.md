# Specification: CI Green for PR #2 — no softened tests, only fixed code

**Track ID:** ci-green-pr2_20260805 · **Type:** Bug · **Created:** 2026-08-05 · **Status:** Draft

## Summary

Drive every failing check on PR #2 to green by fixing the root cause of each. **Softening is
forbidden:** no weakened or deleted assertions, no skips, no loosened gates, no filtering a failing
test out of the run to achieve green. Per `MATRIX-MIGRATION-PLAN.md:194` and `conductor/workflow.md`:
a gate that cannot fail is not a gate.

A test may be *changed* only when it asserts an implementation detail rather than the behavior
(e.g. exact brush class instead of the color), and the behavioral assertion must be preserved or
strengthened. Every such change is declared as a harness fix in its own commit with the reason.

## Failure inventory (from ci-latest-failures.log, run 3, 2026-08-05)

| # | Check | Known cause / state |
|---|---|---|
| 1 | `SelfVerify_StaleSnapshot_DoesNotShowGreen` (Avalonia) | **Honesty-critical, FIRST.** Stale snapshot renders "Verified: the seal is valid…". Determine: product false-green (fix product) or stale test premise (correct test, with evidence). Written conclusion required either way |
| 2 | ~7 WitnessViewModelTests | `ColorOf` (WitnessViewModelTests.cs:333) asserts exact `SolidColorBrush`; Avalonia returns `Immutable.SolidColorBrush`. Harness fix: assert via `ISolidColorBrush.Color` — color assertion preserved |
| 3 | LinuxSecretStoreTests `FileFallback_*` ×4 | File-fallback store writes nothing on CI (retrieve→null). Find why (CI HOME/XDG? S-5 timer change?). Ledger G-16 adjacent |
| 4 | Server pytest job — Warp corpus drift gate | Committed `_build_vectors.py` is stale vs committed cases 015–031; the working-tree (M) builder is the fix. Review diff, commit. The gate itself is correct — do not touch it |
| 5 | `.deb` install+smoke ×2 | Dynamic-linker assertion: a shipped native library has an unresolved dependency. Real packaging defect, new §P finding |
| 6 | Keyring job | "Assert the filtered run actually executed tests" fired — the filter matches zero tests. Fix the filter; the anti-vacuity assert stays |
| 7 | macos-arm64 leg | Dies ~14s, pre-build, cause UNKNOWN — pull its log first, do not guess |
| 8 | Server pytest remainder, markdownlint, Validate/cross-harness generation, Claude Code Review workflow | Untriaged; read from the saved log / per-job logs |

## Acceptance Criteria

- [ ] All PR #2 checks green, or each remaining red recorded in `Linux/PARITY-LEDGER.md` as a
      deliberate, owner-acknowledged exception
- [ ] Zero assertions weakened, skipped, or filtered out; harness changes individually declared
- [ ] Item 1 closed with a written, evidenced conclusion before its suite is touched for item 2
- [ ] Every fix commit names the failing check and the root cause `file:line`
- [ ] Remaining working-tree M-files that CI depends on are reviewed (`git diff`) and committed —
      today's pattern: every root cause so far was uncommitted-tree vs HEAD drift
- [ ] Ledger §P/§CP rows updated (.deb build green is already earned and unrecorded)

## Sources

`conductor/tracks/cross-platform-parity_20260805/ci-latest-failures.log` (954 lines) · MEMORY2
handoff §8 · PR #2 · `gh run view <id> --log-failed`

## Out of Scope

Task 8.8 (`-v` flag, owner decision) · cross-platform-parity_20260805 Phases 1–7 build-out · GUI
parity track · any change to what a gate measures
