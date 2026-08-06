# Track: Cross-Platform Parity Detection

**ID:** cross-platform-parity_20260805
**Status:** Pending

## Documents

- [Specification](./spec.md)
- [Implementation Plan](./plan.md)

## Progress

- Phases: 0/7 complete
- Tasks: 0/31 complete

## Scope at a glance

**In:** engine conclusion parity (Swift ↔ C#), CLI input/output parity, behavioral/functional
parity, runtime matrix (windows-x64, ubuntu-x64, ubuntu-arm64).

**Out:** GUI parity (separate track), fixing any divergence found (separate tracks per finding),
amending the Warp manifest contract (proposal only — owner ratifies), Python server (no twin).

## Open finding at spec time

The .NET dispatcher accepts `-h` and `-v`; the Swift dispatcher accepts only `--help` and
`--version`. Found during spec pre-flight, filed by Task 3.3, **not fixed by this track**.

## Quick Links

- [Back to Tracks](../../tracks.md)
- [Product Context](../../product.md)
- [Workflow](../../workflow.md) — the five verification states this track records against
- `Linux/PARITY-LEDGER.md` — where findings land
