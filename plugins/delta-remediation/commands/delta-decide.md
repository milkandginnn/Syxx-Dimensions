---
description: "Make explicit decisions about which deltas to remediate"
argument-hint: "[--auto] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Decide

Present evaluated deltas for human decision. Record decisions and rationale. Produce a scoped worklist for the current remediation cycle.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--help`: Display help information and exit without executing
   - `--auto`: Let the model decide (FIX NOW for MUST FIX, FIX NEXT for SHOULD FIX, DEFER for COULD FIX, CLOSE for WON'T FIX)
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

### `/delta-decide` — Decide What to Fix

Present evaluated deltas for human decision. For each delta in the MUST FIX and SHOULD FIX tiers, prompt the user to choose: FIX NOW (highest priority this cycle), FIX NEXT (after FIX NOW items), DEFER (skip this cycle), or CLOSE (mark resolved with reason). Record all decisions with rationale and produce a scoped worklist.

**Prerequisite:** `.deltas/evaluation.json` must exist (run `/delta-evaluation` first).

**Flags and arguments:**

| Flag | Description | Default | Example |
|---|---|---|---|
| `--help` | Display this help and exit | — | `/delta-decide --help` |
| `--auto` | Automatically decide based on tier (MUST FIX→FIX NOW, SHOULD FIX→FIX NEXT, COULD FIX→DEFER, WON'T FIX→CLOSE) | Interactive | `/delta-decide --auto` |
| `--schedule <datetime>` | Run at a future time | Now | `/delta-decide --schedule 2026-08-15 09-00-00` |
| `--repeat` | Recurring schedule | Once | `/delta-decide --schedule 2026-08-15 09-00-00 --repeat` |

**Decisions:**

| Decision | Meaning |
|---|---|
| FIX NOW | Include in current cycle, highest priority |
| FIX NEXT | Include in current cycle, after FIX NOW items |
| DEFER | Keep in catalog, skip this cycle |
| CLOSE | Mark resolved (requires reason) |
| AUTO | Delegate to model using `--auto` logic |

**Output:** `.deltas/decisions.json` (contains `worklist` with `fix_now` and `fix_next` arrays)

---

3. Verify `.deltas/evaluation.json` exists. If missing, error: "No evaluation found. Run /delta-evaluation first."

4. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

## Phase 1: Present Deltas

Read `.deltas/evaluation.json`. Present deltas grouped by tier, with the top N deltas by composite score shown first. For each delta, display:

```
D042 [critical] [S] — LeakDetector.cs:951
Trust anchor bootstrap unpinned — empty HashSet allows MITM
Score: 0.85 | Tier: MUST FIX | Blocks: D015
```

## Phase 2: Decision Collection

For each delta in MUST FIX and SHOULD FIX tiers, prompt for a decision:

| Decision | Meaning |
|---|---|
| FIX NOW | Include in current remediation cycle, highest priority |
| FIX NEXT | Include in current cycle, after FIX NOW items |
| DEFER | Keep in catalog but skip this cycle |
| CLOSE | Mark as resolved, with required reason |
| AUTO | Delegate to model using --auto logic |

If `--auto` is set, apply model decisions automatically:
- MUST FIX → FIX NOW
- SHOULD FIX → FIX NEXT
- COULD FIX → DEFER
- WON'T FIX → CLOSE (reason: "Below remediation threshold")

## Phase 3: Record

Write `.deltas/decisions.json`:

```json
{
  "timestamp": "ISO_TIMESTAMP",
  "mode": "interactive",
  "decisions": [
    {
      "delta_id": "D042",
      "decision": "FIX NOW",
      "rationale": "Critical security vulnerability, unpinned trust anchor",
      "decided_by": "user",
      "decided_at": "ISO_TIMESTAMP"
    }
  ],
  "worklist": {
    "fix_now": ["D042", "D015"],
    "fix_next": ["D018", "D023"],
    "deferred": ["D031"],
    "closed": []
  }
}
```

## Contract

- Decisions are human-made and recorded immutably unless `--auto` is set.
- The agent recommends but does not decide (unless AUTO mode).
- Closed deltas require a reason. Deferred deltas require no reason.
- The worklist is scoped: only FIX NOW and FIX NEXT items proceed to planning.