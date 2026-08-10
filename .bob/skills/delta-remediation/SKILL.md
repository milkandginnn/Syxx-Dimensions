---
name: delta-remediation
description: Delta remediation pipeline reference — quick-start guide and command index
metadata:
  user-invocable: true
  disable-model-invocation: true
  argument-hint: '[--help]'
---

# Delta Remediation — Pipeline Reference

Quick-start reference for the full delta remediation workflow. Run the commands below in order to discover, triage, plan, implement, verify, and archive defects.

## Pipeline Order

```
/delta-analysis  →  /delta-evaluation  →  /delta-decide  →  /delta-plan  →  /delta-implement  →  /delta-review  →  /delta-clean
```

## Commands

### /delta-analysis

Deploy specialized agents to audit the codebase for defects across security, correctness, parity, performance, and observability dimensions. Each agent produces structured findings with file:line citations, severity ratings, and concrete failure scenarios. Findings are deduplicated and merged into `DELTAS.md`.

**Flags:** `--target-number N`, `--deep`, `--deepest`, `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

### /delta-evaluation

Read `DELTAS.md`. Score each delta (severity × exploitability × fix complexity × blast radius). Group into MUST FIX, SHOULD FIX, COULD FIX, WON'T FIX tiers. Detect dependencies. Output `.deltas/evaluation.json`.

**Flags:** `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

### /delta-decide

Present evaluated deltas for human decision: FIX NOW, FIX NEXT, DEFER, CLOSE (with reason), or AUTO. Record decisions in `.deltas/decisions.json`.

**Flags:** `--auto`, `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

### /delta-plan

Group deltas by file ownership and dependency order. Produce a phased plan. No two phases touch the same file unless the dependency is explicit. Output `.deltas/plan.md`.

**Flags:** `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

### /delta-implement

Execute plan phase by phase. Deploy implementer agents with strict file ownership. Mark deltas `fixed-unverified`. Run verification gates. User approval required between phases. Output `.deltas/implementation-log.json`.

**Flags:** `--domain <letter>`, `--platform <name>`, `--branch <name>`, `--phase N`, `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

### /delta-review

Deploy multi-reviewer team (correctness, regression, completeness). Assess each `fixed-unverified` delta. Promote to `verified-by-CI` or revert to `open`. Output `.deltas/review-report.md`.

**Flags:** `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

### /delta-clean

Move verified deltas to `.deltas/archive/`. Rewrite `DELTAS.md` with only open, deferred, and fixed-unverified deltas. Output `.deltas/clean-log.json`.

**Flags:** `--schedule YYYY-MM-DD HH-MM-SS`, `--repeat`

---

## Delta States

```
open → fixed-unverified → verified-by-CI → verified-by-real-run
```

"Fixed" is never a terminal state — promotion requires independent review.

## Contracts

- **Analysis** is read-only. No code is modified.
- **Implementation** is phase-gated. User approval required before each new phase.
- **Review** is independent — reviewers have not seen the implementation plan.
- **Archive** is non-destructive — verified deltas are preserved, never deleted.
