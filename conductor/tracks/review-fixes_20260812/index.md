# Track: Recovered xhigh Review — Confirmed Defect Repairs

**ID:** review-fixes_20260812
**Status:** Pending
**Type:** Bug · **Tier:** 3

36 unique CONFIRMED defects across Windows/.NET and macOS/Swift, recovered from three `/code-review` xhigh runs on 2026-08-11/12.

## Documents

- [Specification](./spec.md)
- [Implementation Plan](./plan.md)
- [Findings](./findings.md) — authoritative defect list, recovered verbatim

## Progress

- Phases: 0/7 complete
- Tasks: 0/59 complete
- Defects: 0/36 resolved

## Gates

- **W16** — **ratified 2026-08-12**: `63_072_000` s (730 days) across all three runtimes, no value change. Already unified at HEAD and verified sound. Remaining work is enforcement binding, not repair.
- **M12** — signed-evidence contract v5 design approved; re-gates on deviation.
- **UI** — Tier 3 governance approval required before Phase 6 (M04/M05).

## Notes

- Phase 0 is blocking: findings were authored against a pre-revert tree, so every defect is classified against HEAD before repair.
- **Half the Swift test corpus is dark.** `macOS/Tests/_quarantine/` holds 44 files / ~9,839 lines against 10,448 live lines, and sits outside every declared SwiftPM target — nothing in it compiles. Several quarantined suites cover Wave 1–2 subject matter directly. Phase 0 audits it; remediation is follow-on work.
- `LicenseManager.cs` overlaps uncommitted work from `warp-hardening_20260811`; W17/W18 apply on top of it.
- `SyxxUI` / `SyxxUI.Tests` are Windows-only and cannot compile on this host. W19 is repaired by relocating logic into shared `net10.0` code.

## Quick Links

- [Back to Tracks](../../tracks.md)
- [Product Context](../../product.md)
