# Implementation Plan: CI Green for PR #2

**Track ID:** ci-green-pr2_20260805 · **Spec:** [spec.md](./spec.md) · **Status:** [ ] Not Started

Loop: fix → push → `gh pr checks 2 --watch` → re-read logs → repeat until green. Never batch
unrelated fixes in one commit; each names its check and cause.

## Phase 1: The honesty-critical one
- [x] 1.1 Investigate SelfVerify_StaleSnapshot false-green: reproduce, read the ViewModel path,
      decide product-bug vs stale-test WITH evidence; write the conclusion into findings
- [x] 1.2 Fix accordingly (product change or evidenced test correction)
### Verification: the test fails on a stale snapshot rendered green, passes on correct behavior

## Phase 2: Deterministic harness/env fixes
- [ ] 2.1 ColorOf → assert ISolidColorBrush.Color (declared harness fix)
- [ ] 2.2 Keyring job filter matches its tests again; anti-vacuity assert untouched
- [ ] 2.3 FileFallback root cause on CI (env vs S-5 timer), fixed at cause
### Verification: Avalonia suite + keyring job green in CI, not just locally

## Phase 3: Uncommitted-drift sweep
- [ ] 3.1 `git diff` review each remaining M-file CI depends on (_build_vectors.py, test files,
      ci.yml, build_deb.sh…), commit deliberately — no `git add -A`
- [ ] 3.2 Warp drift gate green via the committed builder matching the committed corpus
### Verification: drift gate green; `git status` shows only intentionally-uncommitted residue

## Phase 4: Packaging
- [ ] 4.1 .deb smoke: identify the unresolved .so, fix Depends/payload; new §P ledger row
- [ ] 4.2 snap: .NET SDK in the snapcraft build environment
### Verification: install+smoke and snap green — first ever

## Phase 5: Unknowns
- [ ] 5.1 macos-arm64 early failure: pull log, diagnose, fix
- [ ] 5.2 Server pytest remainder · 5.3 markdownlint · 5.4 Validate generation · 5.5 Claude review wf
### Verification: each closed green or ledgered as an accepted exception

## Final Verification
- [ ] `gh pr checks 2` all green / exceptions ledgered · no softened tests (diff-audited) ·
      ledger updated incl. .deb-build green conversion
