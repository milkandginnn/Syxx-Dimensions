# Delta Remediation — Standalone Prompt

Copy the relevant command prompt below into your AI coding assistant.

---

## /delta-analysis

Deploy specialized agents to audit the codebase for defects across security, correctness, parity, performance, and observability dimensions. Each agent produces structured findings with file:line citations, severity ratings, and concrete failure scenarios. Deduplicate and merge into `DELTAS.md`.

Flags: `--target-number N`, `--deep`, `--deepest`, `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--schedule`, `--repeat`

---

## /delta-evaluation

Read `DELTAS.md`. Score each delta (severity × exploitability × fix complexity × blast radius). Group into MUST FIX, SHOULD FIX, COULD FIX, WON'T FIX tiers. Detect dependencies. Output `.deltas/evaluation.json`.

Flags: `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--schedule`, `--repeat`

---

## /delta-decide

Present evaluated deltas for human decision: FIX NOW, FIX NEXT, DEFER, CLOSE (with reason), or AUTO. Record decisions in `.deltas/decisions.json`.

Flags: `--auto`, `--schedule`, `--repeat`

---

## /delta-plan

Group deltas by file ownership and dependency order. Produce phased plan. No two phases touch the same file unless dependency is explicit. Output `.deltas/plan.md`.

Flags: `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--schedule`, `--repeat`

---

## /delta-implement

Execute plan phase by phase. Deploy implementer agents with strict file ownership. Mark deltas `fixed-unverified`. Run verification gates. User approval between phases. Output `.deltas/implementation-log.json`.

Flags: `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--phase N`, `--schedule`, `--repeat`

---

## /delta-clean

Move verified deltas to `.deltas/archive/`. Rewrite `DELTAS.md` with only open, deferred, fixed-unverified deltas. Output `.deltas/clean-log.json`.

Flags: `--schedule`, `--repeat`

---

## /delta-review

Deploy multi-reviewer team (correctness, regression, completeness). Assess each `fixed-unverified` delta. Promote to `verified-by-CI` or revert to `open`. Output `.deltas/review-report.md`.

Flags: `--schedule`, `--repeat`

---

## States

`open` → `fixed-unverified` → `verified-by-CI` → `verified-by-real-run`. "Fixed" is never a state.

## Contract

- Analysis is read-only. Implementation is phase-gated. Review is independent. Archive is non-destructive.