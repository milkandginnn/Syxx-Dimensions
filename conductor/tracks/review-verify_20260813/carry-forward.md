# Phase 0 — Baseline & Carry-Forward

**Baseline:** HEAD `ecda22c9`, branch `main`. Working tree: 202 files changed (+9,401 / −40,123).

## Task 0.2 — the finding that reframes the track

**12 of 13 cited paths are dirty.** The report reviewed the **working tree, not HEAD**:

```
M Server/syxx_server/app.py                    M Windows/dotNET/Syxx.Kit/Trust/MonitorScheduler.cs
M Server/syxx_server/dnssec_validator.py       M Windows/dotNET/Syxx.Kit/UnboundManager.cs
M Server/syxx_server/fulfillment.py            M Windows/dotNET/Syxx.Kit/UnboundResolver.cs
M Server/syxx_server/investigate.py            M Windows/dotNET/Syxx.Tests/MonitorSchedulerTests.cs
M Server/syxx_server/lookalike.py              M docs/USER-GUIDE.md
M Server/syxx_server/whois_lookup.py           M scripts/deploy.sh
```

Only `Server/README.md` is clean.

**Consequences.** Every verification reads the working tree and must say so. Fix plans must state whether they apply to committed or uncommitted code. And any of these findings can vanish or change if the working tree is committed, reverted, or stashed before the fixes land — the same pre-revert-tree trap that made Phase 0 of `review-fixes_20260812` necessary, one level up.

## Task 0.3 — carry-forward table

Prior evidence from `review-fixes_20260812/triage.md` (T) and this session's reported findings (R), both against this same working tree.

| ID | Finding | Prior | Conclusion carried forward | Re-verify? |
|---|---|---|---|---|
| B1 | MonitorScheduler `:114` guard dead in Stop() path | R | CONFIRMED — `_cts` nulled by `Stop()`; post-stop tick counted, latched, delivered | No — extend only to the test coupling |
| B5 | `_inFlight` released before state commit | R | CONFIRMED — interleaving leaves stale `LastTick` | No |
| C1 | `VantageComparer.swift:320` missing `!didFail` | T (N20) | CONFIRMED latent — no post-fix input found yielding a false clean | No |
| C2 | `VantageComparer.cs` twin, both sites | T (N20) | CONFIRMED — C# filters `Records.Count > 0` only; its `DidFail` covers the same five statuses | No |
| D1 | Four stale `VantageComparerTests` | T (cause 1) | CONFIRMED stale — three contradict their own name or doc comment | No |
| D2 | `MonitorSchedulerTests` first-tick assertions deleted | R | CONFIRMED — doc still promises the behavior | No |
| E2 | `UnboundManager.cs:122` blocking `WaitForExit` in async | R | CONFIRMED | No |
| F1 | `app.py:1344` dead expression | R | CONFIRMED — introduced by the %→f-string refactor | No |
| F2 | `fulfillment.py:450` dead expression | R | CONFIRMED — and the subscription id is discarded, so reinstatement is customer-keyed | No |
| F3 | `test_licensing.py:65` dead expression | R | CONFIRMED | No |
| G1 | `lookalike.py:15` homoglyph `1→l` dropped | R | CONFIRMED — map is single-replacement, so the fix is structural | No |
| H1 | `deploy.sh:218` `\|\| true` + duplication | R | CONFIRMED | No |
| J2 | `AGENTS.md:7` self-edit authorization | R | CONFIRMED — `CLAUDE.md` symlinks to it | No |
| J3 | `AGENTS.md:2` contradictory agent counts | R | CONFIRMED — 164 vs 203 in one file | No |
| K2 | `docs/USER-GUIDE.md` banned-term uses | R | CONFIRMED — three lines | No |

**15 carried forward, 22 to verify.** Higher than the 12 estimated in the spec — the `/code-review` pass overlapped the report more than expected.

Not carried forward, and needing fresh work: the whole A cluster (7), B2/B3/B4 (3), E1/E3/E4 (3), F4, G2, I1–I3 (3), L1–L3 (3), J1, K1.

## Remaining Phase 0 tasks

- Task 0.4 — check the report's own dedup claims (five collapses asserted)
- Task 0.5 — spot-check the four self-refuted findings
