# Audit — `macOS/Tests/_quarantine/`

**Requested:** 2026-08-05, mid-track, after Task 1.2 surfaced the directory
**Scope:** what is in it, whether it is recorded, and what it costs

## Correction to my first reading

I initially reported that 14 entries labelled `.phase32-active-dup` "carry a label that is not true,"
on the grounds that no live file of that name exists. **That framing was wrong and I am withdrawing
it.** The label describes *why the file was moved* — it was one of two divergent copies of the same
test — and both copies were then parked. It is stale, not false.

Measured: of five sampled pairs, four hold **divergent** content (`ReportTests` 6454 B vs 5953 B;
`SynthesizerTests` 5252 B vs 6129 B), one is byte-identical. So a real duplication problem existed
and both copies were quarantined together. That is a defensible action, and the label records it.

## The quarantine is recorded, not silent

This is the important correction to the impression Task 1.2 left. There is a protocol, and it was
followed:

**`docs/MATRIX-MIGRATION-PLAN.md:194`** —

> "Interim quarantine of a not-yet-rewritten file is allowed ONLY by moving it to
> `macOS/Tests/_quarantine/` (out of the target via Package.swift exclude) with a tracking line in
> this plan's checklist — never by deleting assertions or marking skip."

That is a good rule: it refuses the two soft options (weaken the assertion, mark skip) and forces the
loss to be visible as a moved file.

Subsequent waves are recorded in `CHANGELOG.md`:

| Wave | Recorded at | Occasion |
|---|---|---|
| `.phase1-compile-break` | `CHANGELOG.md:589` | Phase 1 exit gate, 2026-08-01 — "Broken compile units moved … so production still builds" |
| `.phase3-compile-break` | `CHANGELOG.md:468` | ContinuousMonitor, CNAMEChain, RoundingParity, ScorersContract, GuidedSetupLifecycle |
| `.phase32-*` | `CHANGELOG.md:574` | "additional units parked … so the update suite can run" |

And it is filed as a defect: **`docs/IMPLEMENTATION_PLAN.md:2644` — F047, Criticality Critical**,
"209 quarantined macOS tests uncompilable by construction."

The directory is git-tracked (43 files), not ignored, and present since the initial commit of this
repo's visible history. Nothing here was hidden.

## What the audit does find

### A-01 · F047's numbers are stale, and they understate

| | F047 says | Measured 2026-08-05 |
|---|---|---|
| Files | 19 | **43 entries; 21 distinct test files with no live counterpart** |
| Tests | 209 | **351 distinct test functions** |

The quarantine grew after F047 was written — the `.phase32-*` wave post-dates it. A reader who
budgets against "209 tests" is short by 142.

This is the failure mode this codebase already named for itself: *check failure names, never counts.*
F047 is a count, and it has drifted the way counts do. It should carry the file list, not a number.

**Scale:** 767 live test functions across 90 files, 351 quarantined. **31% of the Swift tests that
have been written do not compile.**

### A-02 · The protocol's tracking requirement lapsed after the first wave

`docs/MATRIX-MIGRATION-PLAN.md:196` records the first quarantine precisely — one file, named, with
its unblock condition — and ends:

> "Nothing else is quarantined."

That was true on 2026-07-23. Forty-two entries later it is not, and the plan's checklist was never
updated. The waves are in `CHANGELOG.md`, so the information exists, but the checklist the protocol
designated as the tracking surface stopped tracking. The rule was good; it decayed at the second
application.

### A-03 · The Matrix corpus loader's stated unblocker has landed, and nobody went back

`MATRIX-MIGRATION-PLAN.md:196` quarantined `Stage0GoldenCorpusTests.swift` with an explicit
condition:

> "its REWRITE target (`corpus/matrix/`, Phase 5 §6) does not exist yet, so the file is quarantined …
> until the Phase 5 corpus lands and it is rewritten into `MatrixGoldenCorpusTests.swift`."

Both halves of that condition are now satisfied:

- `corpus/matrix/` **exists** — `default-spec.frozen.json` plus three golden vectors
- `MatrixGoldenCorpusTests.swift` **was written** — the rewrite happened

And then the rewrite was itself quarantined as `.phase32-compile-break`, so the sequence completed
with the same coverage gap it started with. This is F-03 in `findings.md`, seen from the other side:
the C# twin runs, the Swift half does not, and the ~100 hand-transcribed weights in
`SyxxMatrixIntegration.swift` that the corpus exists to cross-check still have none.

This is the highest-value item in the audit, because the stated blocker is gone. It is not "waiting
on the corpus" any more; it is waiting on someone to notice that it isn't.

### A-04 · F040 may be resolved and still filed

`IMPLEMENTATION_PLAN.md:2609` files F040 as Critical: "TrayGlyphTests false-clean guard quarantined
out of build AND is red against current code," citing `macOS/Tests/_quarantine/TrayGlyphTests.swift:25-26`.

A live `macOS/Tests/SyxxAppTests/TrayGlyphTests.swift` now exists (2700 B, 4 test functions) beside
the quarantined copy (2528 B). Whether the false-clean guard specifically was restored was not
verified here — that needs the two files compared assertion by assertion. Flagged so F040 is either
closed or re-confirmed rather than left ambiguous.

## What this audit did not do

- It did not determine, for each of the 21 orphaned files, whether the coverage is duplicated by a
  live test under a different name. Some of it plausibly is; `SyxxKitTests.swift.phase32-compile-break`
  alone holds 142 test functions and predates the current per-area file split.
- It did not check whether the quarantined tests would pass if un-quarantined. F040's wording
  ("is red against current code") suggests at least one would not.
- It did not audit the C# or Avalonia suites for an equivalent construct.

Each of those is a separate piece of work. Saying so here so this document's green is not read as
covering them.

## Recommendation

Ranked by value, not effort:

1. **A-03 first.** One file, blocker already gone, and it restores the cross-check on ~100
   hand-transcribed values. It is also the only item that directly unblocks this track's F-03.
2. **Re-state F047 as a file list with per-file status**, not a count. Counts drift; names do not.
3. **Restore the tracking line** the protocol requires, or move tracking to a surface that is
   actually maintained — `Linux/PARITY-LEDGER.md` already has the five-state vocabulary for exactly
   this.
4. **Resolve F040** — compare the two `TrayGlyphTests` and either close it or restate it.
5. Triage the remaining 20 orphaned files against live coverage before deciding restore-vs-retire.
   `MATRIX-MIGRATION-PLAN.md:194` already prescribes the retire path: `git rm` in one dedicated
   commit so history stays greppable.
