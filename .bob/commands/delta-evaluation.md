---
description: "Score and classify discovered deltas for remediation planning"
argument-hint: "[--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Evaluation

Read the delta catalog and assign each delta a composite score. Group into remediation tiers. Produce a ranked remediation queue.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--domain <letter>`: Evaluate only deltas in specified domain
   - `--platform <name>`: Evaluate only deltas for specified platform
   - `--branch <name>`: Evaluate only deltas from specified branch
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. Verify `DELTAS.md` exists. If missing, error: "No delta catalog found. Run /delta-analysis first."

3. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

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