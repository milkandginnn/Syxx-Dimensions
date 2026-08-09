# Delta Remediation Rules

When performing defect remediation, follow the delta lifecycle:

1. **Analysis** (`/delta-analysis`): Multi-agent audit → `DELTAS.md` catalog
2. **Evaluation** (`/delta-evaluation`): Score and tier → `.deltas/evaluation.json`
3. **Decide** (`/delta-decide`): Human decisions → `.deltas/decisions.json`
4. **Plan** (`/delta-plan`): Phased implementation → `.deltas/plan.md`
5. **Implement** (`/delta-implement`): Fix deltas → `fixed-unverified` status
6. **Review** (`/delta-review`): Verify fixes → `verified-by-CI` status
7. **Clean** (`/delta-clean`): Archive verified → `.deltas/archive/`

## States

- `open` — discovered, not yet addressed
- `fixed-unverified` — code changed, not yet verified
- `verified-by-CI` — CI gate passed
- `verified-by-real-run` — executed and inspected
- `deliberately-out` — decided not to fix, reason recorded

## Contract

- "Fixed" is never a state. Evidence is required to promote.
- Analysis is read-only. Implementation is phase-gated.
- Review is independent of implementation.
- Archive is non-destructive.