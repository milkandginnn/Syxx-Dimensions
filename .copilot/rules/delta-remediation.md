# Delta Remediation — Copilot Rules

When performing defect remediation, follow the delta lifecycle:

## Commands

1. `/delta-analysis` — Multi-agent audit → `DELTAS.md` catalog. Flags: `--target-number N`, `--deep`, `--deepest`, `--domain`, `--platform`, `--branch`, `--schedule`, `--repeat`
2. `/delta-evaluation` — Score and tier → `.deltas/evaluation.json`. Flags: `--domain`, `--platform`, `--branch`, `--schedule`, `--repeat`
3. `/delta-decide` — Human decisions → `.deltas/decisions.json`. Options: FIX NOW, FIX NEXT, DEFER, CLOSE, AUTO. Flags: `--auto`, `--schedule`, `--repeat`
4. `/delta-plan` — Phased implementation → `.deltas/plan.md`. Flags: `--domain`, `--platform`, `--branch`, `--schedule`, `--repeat`
5. `/delta-implement` — Fix deltas → `fixed-unverified`. Flags: `--domain`, `--platform`, `--branch`, `--phase N`, `--schedule`, `--repeat`
6. `/delta-review` — Verify fixes → `.deltas/review-report.md`. Flags: `--schedule`, `--repeat`
7. `/delta-clean` — Archive verified → `.deltas/archive/`. Flags: `--schedule`, `--repeat`

## States

`open` → `fixed-unverified` → `verified-by-CI` → `verified-by-real-run`

"Fixed" is never a state. Evidence is required to promote.

## Contract

- Analysis is read-only. Findings are evidence-cited.
- Implementation is phase-gated. User approval between phases.
- Review is independent of implementation.
- Archive is non-destructive. Only verified deltas are cleaned.