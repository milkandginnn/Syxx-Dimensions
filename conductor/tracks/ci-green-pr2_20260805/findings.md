# Findings — ci-green-pr2_20260805

## C-01 · SelfVerify false-green on domain switch (Task 1.1 conclusion)

**Product defect, not a stale test.** Required written conclusion, with evidence:

- `WitnessViewModel.cs:293` — `_lastSnapshot` is written only on a successful seal, and **no
  failure branch clears or re-scopes it**.
- `WitnessViewModel.cs:438` (pre-fix) — `SelfVerify` guarded only on nullity, then verified the
  retained snapshot with **no check that it belongs to the domain currently shown**.
- Sequence: seal `a.com` → switch `Domain` to `b.com` → seal fails ("Nothing to witness") →
  self-verify replays `a.com`'s valid seal → "Verified: the seal is valid…" in green, rendered
  beside `b.com`. The user reads it as a claim about `b.com`. G-12 Route B false-confidence class.
- The test (`WitnessViewModelTests.cs:485-513`) encodes exactly this and was RIGHT.

**Fix (1.2):** `SelfVerify` now also requires `snap.TargetDomain` to equal the current domain
(ordinal, case-insensitive); on mismatch it renders a caution naming both domains —
`SelfVerifyDomainMismatchFormat`, deliberately containing no form of "Verified". Nullity guard,
same-domain re-verify, and `ApplyVerification` severity matrix untouched.

**Evidence:** CI run 3 = red on unfixed code. Local post-fix: all 4 `SelfVerify` tests pass,
including the happy path.

**Cross-platform note, filed not fixed:** the T-6 test comment records that Windows and macOS
*share* a snapshot field between verify paths (DELTAS.md G-15). Whether their self-verify has the
same missing domain guard is a parity question for cross-platform-parity_20260805 Phase 4 —
checking it is out of this track's scope, but the question is now written down.
