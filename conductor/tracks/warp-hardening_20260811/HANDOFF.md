# HANDOFF — warp-hardening_20260811 + P4 spillover

**Written 2026-08-13 at session budget exhaustion. Read this before touching MonitorScheduler.**

## Objective

Close `warp-hardening_20260811`, then two owner-approved follow-ons: B1–B5 (`review-verify_20260813` fix-plans P4) and a method-level OpenAPI drift gate.

## Completed and committed

| Commit | What |
|---|---|
| `b03118e` | Phase 4 C# sync-over-async conversions — owner-authorized recovery checkpoint, carries open B1–B5 |
| `22b55cb` | Track state; two decisions recorded in `metadata.json` (D-PHASE4-CHECKPOINT, D-CROSSRUNTIME-RESCOPE) |
| `06e3c67` | CHANGELOG entry + documentation item closed |
| `68f5161` | Drift gate compares (METHOD, path) pairs; `GET /api/leaktest/start` documented |

Track Final Verification is 4/5. Only "ready for review" remains, blocked on B1–B5.

## RESOLVED — this section no longer describes risk

**Superseded 2026-08-13.** The MonitorScheduler work is committed at `e22126f8`. The working tree is clean for `Windows/dotNET`. Nothing is at risk.

`Windows/dotNET/Syxx.Kit/Trust/MonitorScheduler.cs` and `Windows/dotNET/Syxx.Tests/MonitorSchedulerTests.cs` — B1, B3, B4, B5 and B2's accounting half (C111 port), plus the `[high]` fix and three `[low]` items.

**Standing caution, unrelated to the above:** never `git checkout --`, `git clean`, or `stash drop` in this repo. `rules/` is untracked and owner-authored; any sweeping operation destroys it.

## The `[high]` — fixed at `e22126f8`

Code review returned **commit after fixing one `[high]`**, independently confirmed at the cited lines before it was acted on:

`MonitorScheduler.cs:278` called the parameterless `RunOnceAsync()`, discarding the token `RunLoopAsync` already held; `:140` then re-read the `_cts` field. If `Stop()` landed between the loop's check at `:277` and that capture, the tick captured `CancellationToken.None`, the guard at `:184` could not fire, and the tick was counted, latched, and delivered to `_onTick` — a toast for a round the user switched off. `Stop()` is called from the UI thread at `AppViewModel.cs:163`.

**Fix as landed:** private `RunOnceCoreAsync(CancellationToken)` carries the body; `RunLoopAsync` passes its own captured token at both call sites. Three `[low]` items folded in: the two comment corrections and the unthrottled test spin.

**VERIFICATION GAP — read this before trusting the fix.** It was confirmed by the implementer's own red/green regression test (`LoopTickEnteringAroundStop_IsNeverCommittedAfterTheFact`, reproduced 10/10 pre-fix at 13–63 ms, passes post-fix) and by the failing-name comparison against baseline. The independent review seat did **not** re-check it — session budget ended first. Author-verified, not independently verified. A re-review is the cheapest remaining confidence gain.

One thing the implementer corrected in the orchestrator's brief, worth preserving: keeping the field-read in the public parameterless entry is what *preserves* the direct-call divergence from Swift, rather than resolving it as the brief assumed. See decision 1 below.

## Verified facts — do not re-derive

- Baseline C#: 12 pre-existing failing NAMES on macOS hosts (`ProbeRunnerTests.Default{Glutton,Mirror,Portal,Prophet}_HasExpectedValues`, `MatrixGoldenCorpusTests.DefaultSpecMatchesFrozenOracle`, `D100TtlTieBreakDeterminismTests.*`, `AuthoritativeVantageConsensusTests.*`, `ReportTests.RenderText_ContainsRequiredStructuralSections`, `ResultProvenanceTests` ×2, `SectionCompletenessTests.TimeoutDatapointLowersCompleteness`, `NetworkKeyStateTests.EncryptedDNSStatusNoForPlainUDP`). With the new tests present: 873/885. No name may be added.
- Python 436 passed + 51 subtests; `ruff` clean; drift gate exit 0 at 54 live / 54 spec.
- `MonitorSchedulerTests.cs:157` — stale test openly inverted, `Assert.Equal(2, afterStop)` → `Assert.Equal(1, s.TickCount)`. B1 makes 1 correct. Declared, not weakened.
- Lock discipline reviewed and sound: `_lifecycleLock` and `_runLock` never nested, no cycle, no lock across an await, `Cancel()` outside the lock is load-bearing, deferred CTS disposal is a real hardening.

## Owner decisions still open

1. **B2 remainder** — loop's first tick is refused after a monitoring toggle, so the UI reports Running with no baseline for a full interval. Implementer recommends queueing the loop's first tick behind the outgoing one (option 4) over the plan's retry-with-backoff (option 1). **This is a twin fix** — `MonitorScheduler.swift:76-81` refuses identically.
2. **`[Paid]` vs `[Public]` tags** — `/api/leaktest/start` POST and `/api/leaktest/check` are tagged `[Paid]` but neither handler performs an entitlement check and both describe themselves as free. Auth-adjacent: either a doc mistag or a missing gate.

## Unfiled twin divergences found in passing

- Windows has no persisted change latch; Swift saves and restores it, so a Windows restart silently erases an unacknowledged change.
- Windows has no resume-from-sleep catch-up tick; macOS ticks on `didWakeNotification`.
- The C111 accounting has **no live reader** on either platform — tests only. The API can report a skipped baseline; no surface shows it.
- `MonitorScheduler.swift:32-34` comment says `skippedTickCount` counts "re-entrant or cancelled"; `swift:79` increments only on the re-entrant path. Comment is wrong.

## Next action

1. **Optional, cheapest confidence gain:** have a review seat re-check the `RunOnceCoreAsync` split at `e22126f8`. The original reviewer holds the full lock analysis in context.
2. **Owner sign-off** on Final Verification item 5. The blocker is cleared; what remains is a judgment about the accepted cross-runtime residual risk, not a mechanical check.
3. Then decide the three open items above.

## Session commits

`b03118e` Phase 4 checkpoint · `22b55cb` state + decisions · `06e3c67` CHANGELOG · `68f5161` method-level drift gate · `339ec5c1` this handoff · `e22126f8` B1–B5 fixes.
