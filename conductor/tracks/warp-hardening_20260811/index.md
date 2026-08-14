# Track: Warp Hardening & Contract Drift Fixes

**ID:** warp-hardening_20260811
**Status:** In Progress — implementation done, sign-off blocked

## Documents

- [Specification](./spec.md)
- [Implementation Plan](./plan.md)

## Progress

- Phases: 4/4 complete
- Tasks: 19/19 complete
- Final Verification: 4/5 — only "ready for review" remains, and its blocker is cleared

## Awaiting owner sign-off

- **B1–B5 — CLOSED** by `e22126f8`. Phase 4's `MonitorScheduler` shipped at `b03118e` as a recovery checkpoint carrying confirmed defects; the remediation ([review-verify_20260813 fix-plans P4](../review-verify_20260813/fix-plans.md)) has since landed. The `[high]` from code review is fixed but was **author-verified, not independently re-reviewed** — the session ended first.
- **Cross-runtime verification** — re-scoped 2026-08-13 to the available macOS host; Windows and Linux runtime runs never executed and no hosted CI at present. Residual risk accepted and recorded in `spec.md`.

## Open owner decisions (none block this track)

1. Direct-call parity — `MonitorScheduler.cs:151`, one line; a "Check now" during a running loop currently inherits the loop's token where Swift commits.
2. B2 remainder — twin fix; recommendation is to serialize the loop's first tick rather than refuse it.
3. `[Paid]`/`[Public]` mistag on `/api/leaktest/start` POST and `/api/leaktest/check` — auth-adjacent.

## Quick Links

- [Back to Tracks](../../tracks.md)
- [Product Context](../../product.md)
