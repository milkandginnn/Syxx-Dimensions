---
name: delta-plan
description: Generate an implementation plan from decided deltas
metadata:
  user-invocable: true
  disable-model-invocation: true
  argument-hint: >-
    [--domain <letter>] [--platform <name>] [--branch <name>] [--schedule
    YYYY-MM-DD HH-MM-SS] [--repeat]
---

# Delta Plan

Group selected deltas by file ownership, dependency order, and risk profile. Produce a phased implementation plan with verification gates.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--domain <letter>`: Plan only deltas in specified domain
   - `--platform <name>`: Plan only deltas for specified platform
   - `--branch <name>`: Plan only deltas from specified branch
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. Verify `.deltas/decisions.json` exists. If missing, error: "No decisions found. Run /delta-decide first."

3. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

## Phase 1: Load Worklist

Read `.deltas/decisions.json`. Extract `fix_now` and `fix_next` arrays. Read `DELTAS.md` for full delta descriptions.

## Phase 2: File Ownership Analysis

For each delta in the worklist:
1. Extract the file path from the delta's file:line citation.
2. Group deltas by file ownership.
3. Detect conflicts: if two deltas touch the same file and are not ordered by dependency, they must be in different phases.

## Phase 3: Dependency Ordering

Build a directed graph from the dependency data in `.deltas/evaluation.json`. Topologically sort deltas. If a cycle exists, break it by promoting the highest-severity delta to the earlier phase.

## Phase 4: Phase Assignment

Assign deltas to phases:

1. **Phase 1:** Deltas with no dependencies, all in different files
2. **Phase 2:** Deltas that depend on Phase 1 fixes, or independent deltas that didn't fit in Phase 1
3. **Phase N:** Continue until all deltas are assigned

Each phase must satisfy:
- No two deltas in the same phase touch the same file (unless dependency is explicit)
- Dependencies are satisfied: if A depends on B, B is in an earlier phase
- Each phase has a verification gate

## Phase 5: Effort Estimation

Estimate per-phase effort:
- Small (≤ 5 deltas, single-file changes): ~1 hour
- Medium (5–15 deltas, multi-file): ~4 hours
- Large (15+ deltas, architectural): ~1 day

## Output

`.deltas/plan.md`:

```markdown
# Delta Remediation Plan

**Generated:** ISO_TIMESTAMP
**Worklist:** 8 deltas (5 FIX NOW, 3 FIX NEXT)

## Phase 1: Foundation Fixes (3 deltas, ~1 hour)
- [ ] D042 — LeakDetector.cs:951 — Trust anchor bootstrap
- [ ] D015 — DimensionClient.cs:64 — Missing decompression
- [ ] D018 — LookalikeDetector.cs:110 — NFC normalization
**Verification:** `dotnet build`, `parity-local-gate.sh`

## Phase 2: Dependent Fixes (3 deltas, ~2 hours)
- [ ] D023 — ParityTranscriptCommand.cs:266 — Encoder divergence
  - Depends on: D042 (trust anchor must be fixed first)
- [ ] D031 — DomainCanonical.swift:99 — Comparison semantics
- [ ] D035 — DNSResolver.cs:1164 — IDNA separators
**Verification:** `dotnet build`, `swift build`, `parity-local-gate.sh`

## Phase 3: Remaining (2 deltas, ~1 hour)
- [ ] D040 — Config source asymmetry
- [ ] D041 — Exponent casing
**Verification:** Full test suite
```

## Contract

- The plan must be executable by parallel agents.
- No two phases touch the same file unless the dependency is explicit and ordered.
- Each phase includes a verification gate.
- Effort estimates are advisory, not commitments.
