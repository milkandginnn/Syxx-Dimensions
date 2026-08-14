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

## UNCOMMITTED AND AT RISK

`Windows/dotNET/Syxx.Kit/Trust/MonitorScheduler.cs` and `Windows/dotNET/Syxx.Tests/MonitorSchedulerTests.cs` — roughly +471/-119 implementing B1, B3, B5, B4 and B2's accounting half (C111 port).

**Do not `git checkout --`, `git clean`, or `stash drop` anything.** `rules/` is untracked and owner-authored; any sweeping operation destroys it.

State when this was written: an agent named `monitor-lifecycle` was mid-edit applying the fix below. The files may be in a partially-applied state. **Verify before committing** — run `dotnet test Windows/dotNET/Syxx.Tests` and compare failing NAMES, never counts.

## The one blocker

Code review returned **commit after fixing one `[high]`**, independently confirmed at the cited lines:

`MonitorScheduler.cs:278` calls the parameterless `RunOnceAsync()`, discarding the token `RunLoopAsync` already holds; `:140` then re-reads the `_cts` field. If `Stop()` lands between the loop's check at `:277` and that capture, the tick captures `CancellationToken.None`, the guard at `:184` cannot fire, and the tick is counted, latched, and delivered to `_onTick` — a toast for a round the user switched off. `Stop()` is called from the UI thread at `AppViewModel.cs:163`.

**Fix:** private `RunOnceCoreAsync(CancellationToken)` holding the body; `RunLoopAsync` passes its own token; the public parameterless entry keeps the field-reading capture and delegates. Also resolves a Swift divergence.

Three `[low]` items folded into the same round: comment at `:136-138` (direct-call semantics), comment at `:98-100` (`LastTickCompletedAt` is a transient signal; `SkippedTickCount` is the durable one), and an unthrottled spin at `MonitorSchedulerTests.cs:208`.

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

Confirm the `[high]` fix landed, re-run the C# suite, compare failing NAMES, then commit both files as ONE change with the test inversion declared openly in the message. Then mark Final Verification 5/5 only if B1–B5 are genuinely closed.
