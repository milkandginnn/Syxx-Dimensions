# Specification: Cross-Platform Parity Detection

**Track ID:** cross-platform-parity_20260805
**Type:** Feature
**Created:** 2026-08-05
**Status:** Draft

## Summary

Build the detection machinery that proves the Swift and C# builds of Syxx produce the same
conclusions, expose the same CLI surface, and behave the same way across macOS, Windows, and Linux.
Detection only — divergences this track surfaces are filed with evidence, not fixed here.

## Context

`conductor/product.md` goal 3: *the same input produces the same conclusion on macOS, Windows, and
Linux, proven by executing both engines against a shared vector, not by reading them.*

Today that goal is asserted by machinery that cannot support it:

- The blocking `parity-transcript` CI job diffs **exactly one input** — `ParityTranscriptCommand.cs`
  constructs `new SectionInputs()` and tags it `vectorId: "empty-section-inputs"`. It proves the two
  synthesizers agree that nothing in yields nothing out. Every populated state is uncovered.
- **No gate compares the two CLI surfaces at all.** A five-minute pre-flight for this spec found an
  asymmetry already: the .NET dispatcher accepts `-h` and `-v` short flags, the Swift dispatcher
  accepts only `--help` and `--version`. Same product, different input contract, and nothing would
  have caught it.
- Behavioral parity across the module commands (cage, filter, forensics, key, lan, leak, mirror,
  nslook, route, sentinel, speed, unbound, warp, witness) is untested as a cross-build property.

The architecture makes this tractable: two shared-logic twins, four runtimes. `Syxx.Kit` (C#) serves
both Windows and Linux; `SyxxKit` (Swift) serves macOS. So the work splits into a **cross-language**
comparison, verifiable locally in ~90s, and a **runtime matrix** for the single C# assembly, which
only CI can produce.

## User Story

As a security-conscious individual, I want the conclusion Syxx gives me to depend on the evidence
rather than on which platform's build I happen to be running, so that a second opinion from another
machine actually means something.

## Acceptance Criteria

### Engine — conclusion parity

- [ ] Both emitters accept a vector directory and emit one canonical record per case
- [ ] Both assert `evaluated == case_count` and exit non-zero on mismatch
- [ ] The CI diff covers every case, and still fails on an empty or incomplete transcript set
- [ ] Full-corpus Swift ↔ C# comparison runs as one documented local command

### CLI — input and output parity

- [ ] A gate compares the two dispatch surfaces — command names, flags, aliases, exit codes — and
      fails the build on asymmetry
- [ ] Output text for a shared set of invocations is compared across builds and reconciled against
      `outputs-catalog-{general,story}-mode.csv`
- [ ] The `-h` / `-v` asymmetry found during spec pre-flight is filed with `file:line` evidence
      (not fixed in this track)

### Behavioral and functional parity

- [ ] A shared behavioral vector set covers the module command surfaces at whatever depth is
      deterministic offline
- [ ] Surfaces that cannot be made deterministic (live network, live resolvers) are listed
      explicitly as not covered, with the reason — an uncovered surface must never render like a
      covered one

### Cross-cutting

- [ ] Runtime matrix legs (windows-x64, ubuntu-x64, ubuntu-arm64) diff clean, or the divergence is
      filed
- [ ] Every divergence gets a `Linux/PARITY-LEDGER.md` row carrying one of the five states and
      `file:line` evidence
- [ ] The `WarpEngineTests.swift` silent brotli skip is closed — same defect class one layer down,
      and it currently makes a green run uninformative
- [ ] No engine behavior is changed by this track. A reviewer can confirm this from the diff

## Dependencies

Depends on existing code; no incomplete tracks.

| Dependency | Use |
|---|---|
| `corpus/{matrix,witness-testify-parity,probe-parity,witness-crossplatform,appcast-crossplatform,stage0}` | Existing golden vectors to feed the generalized emitter |
| `warp-manifest-vectors/` (31 cases) | Largest existing corpus; has a regeneration drift gate to model |
| `ParityTranscriptCommand.cs` / `ParityTranscript.swift` | The emitters being generalized |
| `.github/workflows/ci.yml` — `parity`, `parity-transcript` jobs | Gates being extended |
| `scripts/cross-platform-parity-gate.py` | Existing source-shape gate; the CLI gate should follow its shape |
| `outputs-catalog-{general,story}-mode.csv` | Authority for expected output strings |
| `Server/venv/bin/python` | Corpus rebuild — system `python3` lacks `brotli` |

## Scope amendment — 2026-08-05

**Remediation is now in scope.** The track was created detection-only; the owner moved behavior-change
remediation into it after the first six findings landed. Recorded here rather than silently rewritten,
because "detection only" is what shaped the original phase structure.

Consequences: Phase 8 added for remediation; the "fixing is out of scope" exclusion below is struck;
and several tasks that said *file, do not fix* now have a follow-on fix task. The instruction to file
each finding with evidence **still stands** — remediation does not replace the record.

## Out of Scope

- **GUI parity** across SwiftUI / WinUI / Avalonia — separate track
- ~~**Fixing** any divergence this track surfaces~~ — **struck 2026-08-05, now in scope (Phase 8)**
- **Amending** `docs/warp-manifest-v1-spec.md` — this track may produce a dated amendment
  *proposal*; ratification is the owner's, matching how A1–A11 were handled
- **Python server parity** — single implementation, no twin to compare against
- Live-network behavior that cannot be made deterministic offline

## Technical Notes

- Vector format is JSON, one file per case, loadable by both runtimes without a hand-written parser
  on either side. Three hand-written parsers already failed to hold byte-parity once in this
  codebase; do not create a fourth pair.
- Pin the clock per case, extending the existing `FixedNow` pattern.
- Sort before emitting. C# uses `StringComparer.Ordinal`; Swift must match ordinal semantics, not
  locale-aware ones.
- One canonical record per case, sorted, so `diff` is the entire comparison.
- The local loop is ~90 seconds (`dotnet run … parity-transcript` + `swift run … parity-transcript`
  + `diff <(jq --sort-keys …)`). Design for local iteration; CI is confirmation, not the loop.
- `Windows/dotNET/SyxxUI` cannot build on macOS, but nothing in this track needs it — `Syxx.csproj`
  is plain `net10.0`.
- A gate that cannot fail is not a gate. Every gate added here must be demonstrated going red.

---

_Generated by Conductor. Review and edit as needed._
