# Syxx — Product Definition

## What it is

A cross-platform network-integrity tool that detects DNS tampering and content substitution, and
produces signed, verifiable evidence of what it observed.

Four runtimes ship from two shared-logic twins:

| Platform | UI | Shared logic |
|---|---|---|
| macOS | SwiftUI (`macOS/`) | `SyxxKit` (Swift) |
| Windows | WinUI (`Windows/dotNET/SyxxUI`) | `Syxx.Kit` (C#) |
| Linux | Avalonia (`Linux/Ubuntu/SyxxUI.Avalonia`) | `Syxx.Kit` (C#, referenced) |
| Server | — | `Server/syxx_server` (Python) |

## The problem

From a single machine you cannot tell whether the DNS answers and page content you receive are what
the origin actually published. Resolvers lie, middleboxes inject, and a tampered path is
indistinguishable from a clean one at the point of use. Tools that do check tend to report an
outcome without showing the evidence behind it, or without stating what they failed to measure —
which makes a green result and an unmeasured result look identical.

## Primary users

**Security-conscious individuals** — people who want to know whether their own connection is being
tampered with.

This choice is load-bearing, not decorative:

- The GUI is the product. The CLI is a supporting surface, not the delivery vehicle.
- Defaults must be safe without tuning. A user who changes nothing must not end up with a weaker
  check than one who reads the manual.
- Every conclusion must be legible to a non-specialist **without** softening what it means. Plain
  language is a requirement; reassurance is not a permitted substitute for it.

## Governing goals

These are the standard track plans get checked against. A plan that trades one of these away needs
that trade stated explicitly, not assumed.

1. **Honesty over reassurance** — never report a conclusion stronger than the evidence supports, and
   name what was not measured. Absence of a finding is not a finding of absence.
2. **Fail-closed conclusions** — missing, unfetchable, or unverifiable data never upgrades a
   conclusion. No evidence yields `inconclusive` or `untested`, never `clean`.
3. **Cross-platform parity** — the same input produces the same conclusion on macOS, Windows, and
   Linux, proven by executing both engines against a shared vector, not by reading them.
4. **Verifiable evidence** — every conclusion is backed by a signed artifact a third party can check
   offline, without trusting Syxx or its server.
5. **Confirmable results** — a user can independently confirm what Syxx reported. A result the user
   cannot re-derive or re-check is a claim, not a result.

## Modules

| Module | Role |
|---|---|
| **Prophet** | DNS consensus across 12 vantage oracles |
| **Mirror** | Cross-vantage tamper detection. Its content-integrity feature is **Warp** |
| **Portal** | Resolver and transport posture |
| **Glutton** | — |
| **Cage** | DNS query and packet capture |
| **Witness** | Ed25519 attestation over collected evidence |
| **Conclusions** | Synthesis surface — where the above become one stated outcome |

## Related documents

- `Linux/PARITY-LEDGER.md` — the durable answer to "is Linux at parity?", with the five-state
  vocabulary this project verifies by
- `docs/warp-manifest-v1-spec.md` — frozen Warp manifest contract
- `outputs-catalog-general-mode.csv` / `outputs-catalog-story-mode.csv` — the authority for
  user-facing strings; append-only, never reordered
