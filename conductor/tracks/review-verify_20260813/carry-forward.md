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

## Task 0.4 — the report's dedup claims

Four of five collapses upheld. **One rejected, one finding disproved outright.**

| Collapse | Result |
|---|---|
| C142 guard: correctness HIGH + maintainability MEDIUM | **Agree** — same three lines, one construct, one fix. The test coupling is confirmed: `MonitorSchedulerTests.cs:141` asserts `afterStop == 2` with a comment naming the parked in-flight tick, so fixing `:114` makes it 1. Code and test move together |
| `lookalike.py:15` | **Agree on the merge, finding is factually wrong** — see below |
| `app.py:1344` | **Agree** — bare expression, one fix, nothing behavioral lost |
| `fulfillment.py:450` | **Disagree — hides a second defect.** Split 1 → 2 |
| `investigate.py` `.format()` | **Agree on merge, wrong label** — four error-path strings are not a measurable cost; relabel from performance to maintainability |

### G1 disproved by execution

The adjudicator ran both map variants. Pre-diff and post-diff both yield `HOMOGLYPH_MAP["1"] == 'i'`, identical length. The removed `"1": "l"` was **shadowed** by `"1": "i"` on the next line — in a dict literal the later key wins — and the sole consumer is a plain `.get(ch)`. The diff removed dead code and changed nothing.

Both claims in the finding were wrong: not introduced by this diff, and coverage was not halved. A real issue survives but is different and pre-existing: the map is 1:1 and cannot express one-to-many substitution, so `1` has only ever permuted to `i`.

**This was also reported through `/code-review` earlier today.** Withdrawn and re-reported with the correction.

### F2 split 1 → 2

The dead expression at `:450` is cosmetic, LOW. But the comment at `:441-442` claims the branch only fires when the subscription reports `active`, and the `invoice.paid` path at `:449-455` performs no such check — `sub_status` is computed at `:447` and used only in the `elif` at `:456`. Unearned license reinstatement, separate fix, separate blast radius. The deleted binding was already unused pre-diff, so it was the last trace of a guard that was never wired.

Total findings 37 → 38.

### Three-way scheduler split upheld, with an overlap the report missed

C142, the Stop→Start race, and the TOCTOU are genuinely distinct — each has a failure mode the others' fixes do not reach. But fixing C142 also repairs the stale-commit half of the race; only the skipped baseline survives a token fix. Sequencing should account for that.

## Task 0.5 — the four self-refutations

All four upheld. One carries a caveat that matters.

- **`MonitorScheduler.cs:82`** — upheld. The comment is correct; C# does forbid `await` in a `lock` (CS1996). The original finding was the wrong thing.
- **`MonitorScheduler.cs:136`** — upheld **on the doc-accuracy claim only**. Two things it misses: the unchanged clause "Runs one tick immediately" is now an outcome `Start()` no longer guarantees at return, and — more importantly — **the second report raises `:141` as a separate MEDIUM** about the dropped synchronous first tick and stale consumer state. That is an API-contract change, not a doc claim. **The refutation must not be allowed to retire it.**
- **`investigate.py:1722-1740`** — upheld. File is 319 lines; the relocation to `:149/:158/:168/:171` is exact.
- **`UnboundManager.cs:117-131`** — upheld from the diff. The 30s drain, 5s grace, kill, and output-dropping all pre-date this change; the diff only swapped the sync wait for an async one.
- **Python floor** — both halves confirmed, and stronger than argued: all four `datetime.UTC` sites were `datetime.timezone.utc` before this diff, so the 3.11+ floor is newly introduced, not pre-existing. README still claims 3.8+.

## Scope limit declared

The original per-dimension finding texts are not in the track's read scope, so collapses 1, 3 and 5 rest on the cited code admitting only one defect — not on comparing reviewer prose.

---

# Phase 0 Handoff

**Objective:** dedupe the ultra review, verify each cited defect, plan fixes. No fixes land in this track.

**Completed:** coordinator installed copy-first with provenance · baseline captured · carry-forward table built · the report's dedup claims adjudicated · its four self-refutations spot-checked.

**Files/components affected:** track documents only. No source changed — verify with `git diff --stat` over source paths.

**Verified facts:**
- HEAD `ecda22c9`, branch `main`. **12 of 13 cited paths are dirty** — the report reviewed the working tree, not HEAD.
- 15 of 38 findings carry forward with prior evidence; 23 need fresh work.
- G1 disproved by execution and withdrawn; F2 split 1 → 2.
- The three MonitorScheduler bugs are genuinely distinct, but fixing C142 also repairs the stale-commit half of the race.
- `.claude/` is gitignored, so `multi-agent-coordinator` and `swift-expert` exist on disk but are **untracked** — they will not survive a clean checkout.

**Decisions made:** DNSSEC anchor in scope at highest priority · trust prior evidence where cited · coordinator plus four advisory specialists · marketplace findings planned separately · every spawned agent reads `AGENTS.md` and `rules/*` as step 0 and confirms it in its output header.

**Validation performed:** none beyond reading — no builds, no suites. Correct for a verification phase; Phase 1 Task 1.3 is the first task requiring execution.

**Remaining work:** Task 0.6 assignment packets · Task 0.7 this handoff · then Phases 1–5.

**Risks / compatibility notes:**
- The dirty tree is the dominant risk. Committing, reverting, or stashing it before fixes land invalidates findings — the same trap that made Phase 0 of `review-fixes_20260812` necessary, recurring one level up.
- This track and `review-fixes_20260812` both touch `VantageComparer`. Whichever runs second must check the other's state.
- The `:136` refutation must not be allowed to retire the separate `:141` MEDIUM.

**Next recommended action:** Phase 1, Task 1.1 — verify the DNSSEC root trust anchor. If confirmed, it likely warrants its own track and would reorder everything after it.
