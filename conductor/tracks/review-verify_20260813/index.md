# Track: Ultra Review — Dedupe, Verify, Plan Fixes

**ID:** review-verify_20260813
**Status:** Pending
**Type:** Bug · **Tier:** 3

Deduplicate the ultra review report, verify each cited defect against HEAD, and produce a fix plan per confirmed defect. **No fixes land in this track.**

## Documents

- [Specification](./spec.md)
- [Implementation Plan](./plan.md)
- [Source report](./source-report.md) — preserved from `~/Documents/`

## Progress

- Phases: 0/6 complete
- Tasks: 0/39 complete
- Findings: 0/37 resolved

## Headline

The report's top finding is a **pre-existing security defect**: the DNSSEC validator takes its root trust anchor over plain UDP from an unauthenticated source and records it valid with no RFC 7958 pin, so an on-path attacker can forge a chain that validates for any domain. For a product built to detect DNS tampering, that means the integrity check can be made to confirm the attacker's answer. Verified first, in Phase 1.

## Notes

- The source is **three concatenated reports** with heavy overlap; its own section 2 already did partial dedup and refuted four findings. That adjudication is advisory — Phase 0 re-checks it.
- Roughly 12 of 37 findings were verified earlier and carry forward from `review-fixes_20260812/triage.md`. Carried-forward findings must cite their source, never be silently merged.
- `multi-agent-coordinator` is **not installed** — copy-first from `subagents/04-meta-orchestration/` per `PLUGIN-RULES.md` §2.3, as `swift-expert` was.
- B1 cannot be fixed alone: `MonitorSchedulerTests.cs:135-141` asserts the broken guard, so code and test move together.
- Every fix plan must name its cross-runtime twin. Twenty-four divergences already exist from repairing one side only.

## Quick Links

- [Back to Tracks](../../tracks.md)
- [Product Context](../../product.md)
- [Prior track](../review-fixes_20260812/) — overlapping findings and carried-forward evidence
