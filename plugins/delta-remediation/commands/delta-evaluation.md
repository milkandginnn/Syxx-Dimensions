---
description: "Score and classify discovered deltas for remediation planning"
argument-hint: "[--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Evaluation

Read the delta catalog and assign each delta a composite score. Group into remediation tiers. Produce a ranked remediation queue.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--help`: Display help information and exit without executing
   - `--domain <letter>`: Evaluate only deltas in specified domain
   - `--platform <name>`: Evaluate only deltas for specified platform
   - `--branch <name>`: Evaluate only deltas from specified branch
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. If `--help` is set, display the following and exit:

---

### `/delta-remediation` — Delta Remediation Suite

A thorough, multi-agent debugging and defect remediation workflow for large codebases. The suite discovers defects through parallel specialized agents, scores them by severity and impact, records human decisions about what to fix, generates phased implementation plans, executes fixes with strict file ownership, verifies the fixes through independent review, and archives completed work.

### Command Pipeline (intended order)

```
1. /delta-analysis    — Discover defects (reads code, writes DELTAS.md)
2. /delta-evaluation  — Score and classify deltas (reads DELTAS.md, writes .deltas/evaluation.json)
3. /delta-decide      — Human decides what to fix (reads evaluation, writes .deltas/decisions.json)
4. /delta-plan        — Generate implementation plan (reads decisions, writes .deltas/plan.md)
5. /delta-implement   — Execute fixes phase by phase (reads plan, modifies code, updates DELTAS.md)
6. /delta-review      — Verify fixes through independent review (reads code + DELTAS.md, writes .deltas/review-report.md)
7. /delta-clean       — Archive verified deltas (rewrites DELTAS.md, archives to .deltas/archive/)
```

### `/delta-evaluation` — Score and Classify Deltas

Read the delta catalog and evaluate each delta for remediation readiness. Assign one of four statuses: READY (can proceed to planning), READY_WITH_REVISIONS (needs refinement before planning), REJECT (incorrect, duplicate, or stale), or ALREADY_FIXED (code has changed since filing). Produce a readiness report with revision notes and merge recommendations.

**Prerequisite:** `DELTAS.md` must exist (run `/delta-analysis` first).

**Flags and arguments:**

| Flag | Description | Default | Example |
|---|---|---|---|
| `--help` | Display this help and exit | — | `/delta-evaluation --help` |
| `--domain <letter>` | Evaluate only one domain | All domains | `/delta-evaluation --domain D` |
| `--platform <name>` | Evaluate only one platform | All platforms | `/delta-evaluation --platform Server` |
| `--branch <name>` | Evaluate only deltas from a branch | All | `/delta-evaluation --branch feat/1` |
| `--schedule <datetime>` | Run at a future time | Now | `/delta-evaluation --schedule 2026-08-15 09-00-00` |
| `--repeat` | Recurring schedule | Once | `/delta-evaluation --schedule 2026-08-15 09-00-00 --repeat` |

**Readiness statuses:**

| Status | Meaning |
|---|---|
| READY | Delta is correct, defect confirmed in code, fix is clear and actionable |
| READY_WITH_REVISIONS | Directionally right but description or fix needs refinement |
| REJECT | Incorrect, misguided, duplicate of another delta, or by-design |
| ALREADY_FIXED | Referenced code has changed; defect no longer exists |

**Output:** `.deltas/evaluation.json`

---

3. Verify `DELTAS.md` exists. If missing, error: "No delta catalog found. Run /delta-analysis first."

4. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

## Phase 1: Read and Parse

1. Read `DELTAS.md`.
2. Parse each delta entry: extract ID, severity, domain, file:line, description, platform.
3. Filter by `--domain`, `--platform`, `--branch` if specified.

## Phase 2: Scoring

For each delta, compute a composite score (0.0–1.0):

| Factor | Weight | Source |
|---|---|---|
| Severity | 0.35 | Critical=1.0, High=0.7, Medium=0.4, Low=0.1 |
| Exploitability | 0.25 | Externally reachable=1.0, authenticated=0.6, local-only=0.3 |
| Fix complexity | 0.20 | One-line=0.1, single-file=0.3, multi-file=0.6, architectural=1.0 |
| Blast radius | 0.20 | Single function=0.1, single module=0.3, cross-module=0.6, platform-wide=1.0 |

Composite = Σ(weight × factor_score)

## Phase 3: Tier Assignment

| Tier | Score range | Meaning |
|---|---|---|
| MUST FIX | ≥ 0.70 | Critical/high severity, exploitable, wide blast radius |
| SHOULD FIX | 0.40–0.69 | Medium severity, moderate impact |
| COULD FIX | 0.15–0.39 | Low severity, limited impact |
| WON'T FIX | < 0.15 | Cosmetic or intentionally deferred |

## Phase 4: Dependency Detection

Scan delta descriptions for references to other deltas (e.g., "depends on D042"). Build a dependency graph. Flag deltas that block others. If delta A depends on delta B, B must be fixed before A.

## Output

`.deltas/evaluation.json`:

```json
{
  "timestamp": "ISO_TIMESTAMP",
  "filters": { "domain": "S", "platform": null, "branch": null },
  "deltas": [
    {
      "id": "D001",
      "severity": "critical",
      "composite_score": 0.85,
      "tier": "MUST FIX",
      "factors": { "severity": 1.0, "exploitability": 0.8, "fix_complexity": 0.6, "blast_radius": 0.8 },
      "blocks": ["D015"],
      "blocked_by": []
    }
  ],
  "summary": {
    "total": 42,
    "must_fix": 12,
    "should_fix": 18,
    "could_fix": 10,
    "wont_fix": 2
  }
}
```

## Contract

- Evaluation is mechanical and reproducible. Two runs against the same catalog produce the same scores.
- Tier boundaries are fixed and documented. They are not adjusted per-run.
- Dependency detection is based on explicit references and domain analysis, not guesswork.