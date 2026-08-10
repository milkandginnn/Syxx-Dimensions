---
description: "Make explicit decisions about which deltas to remediate"
argument-hint: "[--auto] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat]"
---

# Delta Decide

Present evaluated deltas for human decision. Record decisions and rationale. Produce a scoped worklist for the current remediation cycle.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--auto`: Let the model decide (FIX NOW for MUST FIX, FIX NEXT for SHOULD FIX, DEFER for COULD FIX, CLOSE for WON'T FIX)
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. Verify `.deltas/evaluation.json` exists. If missing, error: "No evaluation found. Run /delta-evaluation first."

3. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

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