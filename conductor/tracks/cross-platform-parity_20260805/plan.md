# Implementation Plan: Cross-Platform Parity Detection

**Track ID:** cross-platform-parity_20260805
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-05
**Status:** [~] In Progress

## Overview

Three detection surfaces — engine conclusions, CLI contract, module behavior — each following the
same shape: a shared vector set both runtimes execute, one canonical record per case, sorted, diffed.
No hand-written per-language parsers, no gate that cannot be shown failing, no harness that cannot
say how many cases it ran.

Phases 1–2 are the spine and unblock everything else. Phases 3 and 4 are independent of each other
and can run in parallel. Phase 7 is where findings leave the track.

Per `conductor/workflow.md`, evidence is gated but not test-first, and every claim closes with one of
the five states — several tasks here are expected to close as `fixed-unverified` until CI runs.

---

## Phase 1: Vector contract and loaders

Define the shared input format once, and prove both runtimes read it identically before anything
depends on it.

### Tasks

- [x] Task 1.1: Define the vector format — JSON, one file per case, `vectorId` plus a `SectionInputs`
      payload. Write it down as a short contract document before writing either loader
- [x] Task 1.2: Inventory existing corpora against that format. Record which can feed the emitter
      directly, which need a shim, and which cannot — with the reason for each
- [ ] Task 1.3: Swift loader in `SyxxCLI`
- [ ] Task 1.4: C# loader in `Syxx/Commands`
- [ ] Task 1.5: Decide and document the behavior for a malformed vector — it must fail loudly, never
      be skipped
- [ ] Task 1.6: Author the initial vector set — no existing corpus is feed-ready (see
      [corpus-inventory.md](./corpus-inventory.md)). Cover each of the 6 sections populated, plus the
      fail-closed boundaries: unfetchable, unverifiable, degenerate. Mirror the
      `warp-manifest-vectors` per-case directory layout

### Verification

- [ ] Both loaders read the same directory and report identical case counts and identical sorted
      case ID lists
- [ ] A deliberately malformed vector causes both to exit non-zero with the case named

---

## Phase 2: Generalize the engine emitters

### Tasks

- [ ] Task 2.1: C# — iterate vectors, emit one canonical record per case, clock pinned per case
- [ ] Task 2.2: Swift — same contract, same field set, same ordering rules
- [ ] Task 2.3: Add `evaluated == case_count` assertion to both; exit non-zero on mismatch
- [ ] Task 2.4: Keep the single-vector invocation working, so the existing CI job never regresses
      while the corpus job is being built
- [ ] Task 2.5: Prove the gate can fail — perturb one vector's expected output and confirm red

### Verification

- [ ] Local full-corpus run: `dotnet run … parity-transcript --vectors=<dir>` and
      `swift run … parity-transcript --vectors=<dir>`, then `diff <(jq --sort-keys …)` — clean, or
      every divergence captured
- [ ] The proven-red run from 2.5 is recorded, with what was perturbed and what the gate printed

---

## Phase 3: CLI surface gate

Independent of Phase 4. Follows the shape of `scripts/cross-platform-parity-gate.py` — match on
emitted strings, not symbol names, so renames stay cheap and contract drift stays impossible.

### Tasks

- [ ] Task 3.1: Extract the dispatch surface from both CLIs — command names, long flags, short
      aliases, and exit codes
- [ ] Task 3.2: Gate asserting the two surfaces are identical; fails on any asymmetry, and fails
      on an empty extraction rather than passing vacuously
- [ ] Task 3.3: File the known `-h` / `-v` asymmetry with `file:line` evidence. **Do not fix it** —
      remediation is a separate track
- [ ] Task 3.4: Choose a set of invocations whose output is deterministic offline (`--help`,
      `--version`, and each command's usage text) and capture both builds' output
- [ ] Task 3.5: Reconcile captured output against `outputs-catalog-{general,story}-mode.csv`,
      appending rows where a string is absent — append only, never reorder

### Verification

- [ ] Gate goes red on a deliberately introduced flag asymmetry
- [ ] Output capture is byte-identical between builds, or each difference is filed
- [ ] Catalog reconciliation adds only appended rows; `git diff` shows no reordering

---

## Phase 4: Behavioral vectors for the module commands

### Tasks

- [ ] Task 4.1: Triage all 15 module commands into deterministic-offline, deterministic-with-fixture,
      and inherently-live
- [ ] Task 4.2: Author behavioral vectors for the first two groups
- [ ] Task 4.3: Run both builds against them; capture one canonical record per case
- [ ] Task 4.4: Write down the inherently-live set as explicitly uncovered, with the reason each
      cannot be made deterministic. This list is a deliverable, not an omission

### Verification

- [ ] Every command appears in exactly one triage group, and the groups sum to 15
- [ ] Covered commands diff clean across builds, or the divergence is filed
- [ ] The uncovered list is published where a reader of the gate's output will see it

---

## Phase 5: Close the silent-skip class

The same defect this track exists to prevent, one layer down.

### Tasks

- [ ] Task 5.1: `WarpEngineTests.swift` — brotli cases currently `return nil` and the caller
      `continue`s, with no skip marker and no counter. Either decode (the justifying comment is
      wrong: `COMPRESSION_BROTLI` ships in the macOS SDK from macOS 12 and the package targets 14)
      or emit an explicit `XCTSkip`
- [ ] Task 5.2: Add an evaluated-case counter to that suite, asserted equal to the case count
- [ ] Task 5.3: Audit the other harnesses for the same `return nil` → `continue` shape and fix any
      found

### Verification

- [ ] The Warp suite reports how many of the 31 cases it evaluated, and asserts the number
- [ ] A run with a deliberately unreadable case fails rather than passing quietly

---

## Phase 6: CI integration and the runtime matrix

### Tasks

- [ ] Task 6.1: Extend the `parity-transcript` job to the full corpus across all four legs
- [ ] Task 6.2: Preserve the existing anti-vacuity guards (empty set, fewer than four transcripts)
      and add a case-count guard
- [ ] Task 6.3: Wire the Phase 3 CLI gate into the `parity` job, which takes no `needs:` so its
      answer is never withheld by an unrelated failure
- [ ] Task 6.4: Wire the Phase 4 behavioral comparison
- [ ] Task 6.5: Add a corpus regeneration drift gate for any new corpus, modelled on the existing
      `warp-manifest-vectors` one

### Verification

- [ ] All four legs green with equal case counts
- [ ] A deliberate divergence pushed to a scratch branch turns the build red, and the failure names
      the diverging case

---

## Phase 7: Findings, ledger, and proposals

### Tasks

- [ ] Task 7.1: File each divergence as its own track with `file:line` evidence and a reproduction
- [ ] Task 7.2: Add a `Linux/PARITY-LEDGER.md` row per finding, carrying one of the five states
- [ ] Task 7.3: Where a finding implies a contract change, write a dated amendment **proposal**
      against `docs/warp-manifest-v1-spec.md`. Do not edit the contract
- [ ] Task 7.4: Record what this track did *not* cover, so the next reader is not misled by its green

### Verification

- [ ] Every finding has a ledger row and a track
- [ ] `git diff docs/warp-manifest-v1-spec.md` is empty
- [ ] The not-covered list exists and is reachable from the ledger

---

## Phase 8: Remediation (added 2026-08-05 — scope amendment)

The track was created detection-only. The owner moved behavior-change remediation into it. Each
finding keeps its filed record; these tasks fix them.

### Tasks

- [x] Task 8.1: F-04 — make `MatrixSpec` encoding deterministic. `knownSections` and
      `mappableIndicators` are `Set`s and were emitted in hash order; three runs on one build gave
      three orders. Custom `encode(to:)` sorts both by `rawValue`
- [x] Task 8.2: F-04 twin — audit the C# `MatrixSpec` for the same hazard.
      `IReadOnlySet<SectionId> KnownSections` has the same latent instability
- [ ] Task 8.3: F-05 — reconcile `default-spec.frozen.json`. It predates B156's
      `mappableIndicators` and carries `knownSections` in neither platform's order.
      `MATRIX-MIGRATION-PLAN.md:358` requires regeneration to be an explicit spec-version bump
      commit — owner-gated, not an agent edit
- [ ] Task 8.4: F-05 — decide the forward-slash escaping contract. The oracle escapes (`\/`),
      C# does not. One of the two must change; pick deliberately and record why
- [ ] Task 8.5: F-01 — add `resolverSubstituted` and the Mirror degraded-cap to the C# twin, so
      substituted evidence cannot read clean on Windows and Linux
- [ ] Task 8.6: F-02 — thread `DNSTransportWindow` into the C# `SectionInputs`
- [ ] Task 8.7: F-06 — correct the `DELTAS-fixed.md` record for F042. It is not fixed
- [ ] Task 8.9: F-07 — port the vector-evaluation and classification tests to the C# twin
- [ ] Task 8.8: The `-h` / `-v` CLI asymmetry from Task 3.3 — now fixable in-track

### Verification

- [ ] Each fix is verified by a test that fails without it
- [ ] No fix lands without its finding's record being updated to the state the evidence supports
- [ ] `swift test --filter Matrix` and the C# twin agree on the spec bytes

---

## Final Verification

- [ ] All acceptance criteria in `spec.md` met, or explicitly carried with a state and a reason
- [ ] Every gate added has been demonstrated going red at least once
- [ ] No engine behavior changed — confirmable from the diff
- [ ] Ledger updated; findings filed; contract untouched
- [ ] Ready for review

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._
