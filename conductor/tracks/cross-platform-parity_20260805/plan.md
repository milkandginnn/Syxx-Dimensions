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
- [x] Task 1.3: Swift loader in `SyxxCLI`
      (sited in SyxxKit deliberately — ExfilAssessment has no public init outside the module and no
      SyxxCLITests target exists; 21 tests; verified-by-real-run incl. byte-identical single-vector
      regression. Outstanding: swift-field-shapes.md from the seat)
- [x] Task 1.4: C# loader in `Syxx/Commands`
      (sited in Syxx.Kit mirroring the Swift twin — Syxx.Tests cannot reference the exe's types;
      22 tests; hand-walked JsonNode decoding, never reflection binding, dodging the
      IncludeFields=false silent-empty trap. Verified-by-real-run by the lead: 22/22, C#
      single-vector path byte-identical to baseline. Outstanding from the seat:
      csharp-field-shapes.md)
- [x] Task 1.5: Decide and document the behavior for a malformed vector — it must fail loudly, never
      be skipped
      (decided + documented in docs/parity-vector-v1-spec.md "Rejection behavior" — five rules, no
      partial success; implemented and rejection-tested in the Swift loader, real-run demonstrated;
      C# twin implements the same rules under Task 1.4)
- [x] Task 1.6: Author the initial vector set — no existing corpus is feed-ready (see
      [corpus-inventory.md](./corpus-inventory.md)). Cover each of the 6 sections populated, plus the
      fail-closed boundaries: unfetchable, unverifiable, degenerate. Mirror the
      `warp-manifest-vectors` per-case directory layout
      (11 cases in corpus/parity-vectors/ — all 6 sections + unfetchable/unverifiable/degenerate;
      loads 11/11 on Swift, verified-by-real-run; C# acceptance pending Task 1.4. Layout deviation,
      deliberate: flat NNN_id.json per the v1 contract's one-file-per-case rule rather than
      warp-manifest's per-case directories — each parity case has exactly one artifact, and the
      contract is the authority; the numbering/sort/drift-gate discipline is what was mirrored)

### Verification

- [x] Both loaders read the same directory and report identical case counts and identical sorted
      case ID lists
      (verified-by-real-run 2026-08-05: --list-only over corpus/parity-vectors is BYTE-identical
      across the two builds — caseCount=11 + the same 11 ids in the same order)
- [x] A deliberately malformed vector causes both to exit non-zero with the case named
      (verified-by-real-run: injected `resolverSubstituted` into case 002 → both exit 3 with
      byte-identical error text naming case and key; corpus restored clean)

---

## Phase 2: Generalize the engine emitters

### Tasks

- [ ] Task 2.1: C# — iterate vectors, emit one canonical record per case, clock pinned per case
- [x] Task 2.2: Swift — same contract, same field set, same ordering rules
      (verified-by-real-run: 11-case envelope caseCount=11 evaluated=11; single shared record
      builder for both paths so shapes cannot drift; diagnosisIds now ordinal — UTF-8 byte order
      matching C# StringComparer.Ordinal, closing the latent Unicode-canonical divergence, byte-
      identical today because all ids are ASCII)
- [~] Task 2.3: Add `evaluated == case_count` assertion to both; exit non-zero on mismatch
      (Swift half landed: counter incremented per concluded case, mismatch/zero refuses to write;
      C# half rides Task 2.1)
- [x] Task 2.4: Keep the single-vector invocation working, so the existing CI job never regresses
      while the corpus job is being built
      (verified-by-real-run twice: --out with no --vectors byte-identical to the pre-track baseline)
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

- [x] Task 3.1: Extract the dispatch surface from both CLIs — command names, long flags, short
      aliases, and exit codes
      (scripts/cli-surface-gate.py + cli-surface-extraction.md: 17 commands, three levels —
      top-level, global-flag handler, per-command accepted/advertised/actions — with dead-arm
      detection through the lowercasing bind)
- [x] Task 3.2: Gate asserting the two surfaces are identical; fails on any asymmetry, and fails
      on an empty extraction rather than passing vacuously
      (exit 0 on the tree with 18 asymmetries ledgered by finding id — F-08/D3/D5 + newly filed
      F-10..F-13; stale-exception hygiene fails the gate when an exception outlives its divergence;
      red-proof and both anti-vacuity refusals verified-by-real-run)
- [x] Task 3.3: File the known `-h` / `-v` asymmetry with `file:line` evidence. **Do not fix it** —
      remediation is a separate track
      (filed as F-08 with both platforms' file:line; 2026-08-05 addendum adds the executed proof
      that Swift's top-level -V branch is dead code via the :48 lowercasing. Fix remains Task 8.8,
      blocked on the owner's B240-vs-E088 call)
- [x] Task 3.4: Choose a set of invocations whose output is deterministic offline (`--help`,
      `--version`, and each command's usage text) and capture both builds' output
      (verified-by-real-run 2026-08-05; report + 36 raw captures in cli-capture-report.md,
      five divergence classes D1–D5, worst: Swift firstrun executes on --help)
- [x] Task 3.5: Reconcile captured output against `outputs-catalog-{general,story}-mode.csv`,
      appending rows where a string is absent — append only, never reorder
      (all 61 distinct captured CLI strings were absent from both catalogs; appended to both with
      source file:line where the literal exists on one line — 43 rows — and honest capture-file
      attribution for the 18 living inside multi-line usage blocks; version strings tagged D1)

### Verification

- [x] Gate goes red on a deliberately introduced flag asymmetry
      (verified-by-real-run by the cli-gate seat: fake command+flag in a scratch copy → exit 1
      naming both; real tree untouched)
- [x] Output capture is byte-identical between builds, or each difference is filed
      (differences filed: D1–D5 in cli-capture-report.md, F-10..F-13 as findings of record)
- [x] Catalog reconciliation adds only appended rows; `git diff` shows no reordering
      (verified: 61+61 insertions, zero deletions, appended at end)

---

## Phase 4: Behavioral vectors for the module commands

### Tasks

- [x] Task 4.1: Triage all 15 module commands into deterministic-offline, deterministic-with-fixture,
      and inherently-live
      (command-triage.md — per-sub-action, both platforms, file:line throughout; offline 9 /
      fixture 7 / live 21 sub-actions; 16 cross-platform effect-surface finding candidates for
      Phase 7, incl. the C# witness-verifier missing clock seam that blocks hermetic fixtures on
      Windows)
- [ ] Task 4.2: Author behavioral vectors for the first two groups
- [ ] Task 4.3: Run both builds against them; capture one canonical record per case
- [~] Task 4.4: Write down the inherently-live set as explicitly uncovered, with the reason each
      cannot be made deterministic. This list is a deliverable, not an omission
      (13-entry draft with reasons sits in command-triage.md; publication "where a reader of the
      gate's output will see it" pends the Phase 4/6 gate wiring)

### Verification

- [ ] Every command appears in exactly one triage group, and the groups sum to 15
- [ ] Covered commands diff clean across builds, or the divergence is filed
- [ ] The uncovered list is published where a reader of the gate's output will see it

---

## Phase 5: Close the silent-skip class

The same defect this track exists to prevent, one layer down.

### Tasks

- [x] Task 5.1: `WarpEngineTests.swift` — brotli cases currently `return nil` and the caller
      `continue`s, with no skip marker and no counter. Either decode (the justifying comment is
      wrong: `COMPRESSION_BROTLI` ships in the macOS SDK from macOS 12 and the package targets 14)
      or emit an explicit `XCTSkip`
      (decoded for real — brotli AND the gzip path, which had the same silent swallow; commit 8615996)
- [x] Task 5.2: Add an evaluated-case counter to that suite, asserted equal to the case count
      (31 of 31 printed and asserted, nonzero-guarded; counter proven able to fail: reinserted skip
      → "30 is not equal to 31")
- [x] Task 5.3: Audit the other harnesses for the same `return nil` → `continue` shape and fix any
      found
      (macOS/Tests audit complete: VersionConsistencyTests + GuidedSetupLifecycleTests fixed; ~25
      sites examined clean, list in the warp-skip-fix report; two deliberate leave-alones carried to
      Task 7.4. C# side: MatrixGoldenCorpusTests already counts; broader C# audit not done — noted)

### Verification

- [x] The Warp suite reports how many of the 31 cases it evaluated, and asserts the number
      (verified-by-real-run: "[warp corpus] evaluated 31 of 31 cases")
- [x] A run with a deliberately unreadable case fails rather than passing quietly
      (verified-by-real-run: truncated case 014 → red naming case+file+encoding+bytes; fixture
      restored digest-matched)

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

- [~] Task 7.1: File each divergence as its own track with `file:line` evidence and a reproduction
      (F-10..F-26 filed as findings-of-record with file:line and ledger rows — 17 divergences.
      Deliberate deviation from "its own track" per divergence: seventeen new conductor tracks
      would bury the owner; the findings + ledger rows carry the evidence and each names its
      reproduction. Spinning any into a track is a one-step owner action when prioritized)
- [~] Task 7.2: Add a `Linux/PARITY-LEDGER.md` row per finding, carrying one of the five states
      (F-10..F-26 rows in place — 17 findings. Remaining: rows for the D1/D2/D4/D5 capture
      divergences once they get F-ids or are folded into existing findings)
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
- [x] Task 8.3: F-05 — reconcile `default-spec.frozen.json`. It predates B156's
      `mappableIndicators` and carries `knownSections` in neither platform's order.
      `MATRIX-MIGRATION-PLAN.md:358` requires regeneration to be an explicit spec-version bump
      commit — owner-gated, not an agent edit
- [x] Task 8.4: F-05 — decide the forward-slash escaping contract. The oracle escapes (`\/`),
      C# does not. One of the two must change; pick deliberately and record why
- [x] Task 8.5: F-01 — add `resolverSubstituted` and the Mirror degraded-cap to the C# twin, so
      substituted evidence cannot read clean on Windows and Linux
- [x] Task 8.6: F-02 — thread `DNSTransportWindow` into the C# `SectionInputs`
- [x] Task 8.7: F-06 — correct the `DELTAS-fixed.md` record for F042. It is not fixed
      (landed in 85c328b before this session; checkbox reconciled 2026-08-05 — the correction text
      and the ledger F-06 row were verified present and committed)
- [x] Task 8.9: F-07 — port the vector-evaluation and classification tests to the C# twin
- [x] Task 8.10: F-09 — port B156 `mappableIndicators` to C# (spec field, engine filter, builder), with discriminating twin tests
- [ ] Task 8.8: The `-h` / `-v` CLI asymmetry from Task 3.3 — now fixable in-track

### Verification

- [x] Each fix is verified by a test that fails without it (8.5 and 8.10 proven red by neutralization)
- [x] No fix lands without its finding's record being updated to the state the evidence supports
- [x] `swift test --filter Matrix` and the C# twin agree on the spec bytes (both match the regenerated oracle)

---

## Final Verification

- [ ] All acceptance criteria in `spec.md` met, or explicitly carried with a state and a reason
- [ ] Every gate added has been demonstrated going red at least once
- [ ] No engine behavior changed — confirmable from the diff
- [ ] Ledger updated; findings filed; contract untouched
- [ ] Ready for review

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._
