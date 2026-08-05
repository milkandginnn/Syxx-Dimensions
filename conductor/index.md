# Conductor — Syxx

Navigation hub for project context.

## Quick Links

- [Product Definition](./product.md)
- [Product Guidelines](./product-guidelines.md)
- [Tech Stack](./tech-stack.md)
- [Workflow](./workflow.md)
- [Tracks](./tracks.md)
- [Code Style Guides](./code_styleguides/) — `swift.md` (authored for this repo), `csharp.md`,
  `python.md`, `general.md`

## Scope note

This directory describes the **Syxx application**. The same repository also holds the claude-agents
plugin marketplace, which is what root `CLAUDE.md`, `AGENTS.md`, and root `ARCHITECTURE.md` describe.
Those documents are not about this product; do not read them as project context for Syxx work.

## Standing references

These predate Conductor and remain authoritative in their areas:

| Document | Authority over |
|---|---|
| `Linux/PARITY-LEDGER.md` | Linux parity claims and the five-state vocabulary |
| `docs/warp-manifest-v1-spec.md` | The frozen Warp manifest contract (amendments are dated, never silent) |
| `outputs-catalog-{general,story}-mode.csv` | Exact user-facing strings; append-only |
| `docs/CLAUDE.md` | Governance lanes and the honesty doctrine |

## Active Tracks

<!-- Auto-populated by /conductor:new-track -->

| Track | Type | Status | Progress |
|---|---|---|---|
| [Cross-Platform Parity Detection](./tracks/cross-platform-parity_20260805/index.md) | Feature | Pending | 0/7 phases · 0/31 tasks |

**Planned, not yet created:** GUI parity across SwiftUI / WinUI / Avalonia — deliberately split out
of the track above.

## Getting Started

Run `/conductor:implement cross-platform-parity_20260805` to begin the active track, or
`/conductor:new-track` to add another.
