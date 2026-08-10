---
name: delta-implement
description: 'Execute the remediation plan, fixing deltas phase by phase'
metadata:
  user-invocable: true
  disable-model-invocation: true
  argument-hint: >-
    [--domain <letter>] [--platform <name>] [--branch <name>] [--schedule
    YYYY-MM-DD HH-MM-SS] [--repeat] [--phase N]
---

# Delta Implement

Execute the remediation plan, deploying implementer agents per phase with strict file ownership. Fix deltas, update DELTAS.md, and run verification gates.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--domain <letter>`: Implement only deltas in specified domain
   - `--platform <name>`: Implement only deltas for specified platform
   - `--branch <name>`: Implement only deltas from specified branch
   - `--phase N`: Execute only the specified phase
   - `--schedule YYYY-MM-DD HH-MM-SS`: Run at specified time
   - `--repeat`: Recurring schedule

2. Verify `.deltas/plan.md` exists. If missing, error: "No plan found. Run /delta-plan first."

3. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

4. Check for an existing `.deltas/implementation-log.json`. If found, resume from the last incomplete phase.

## Phase-by-Phase Execution

For each phase (or the single phase specified by `--phase`):

### 1. Phase Start

- Read the phase's delta list from `.deltas/plan.md`.
- Filter by `--domain`, `--platform`, `--branch` if specified.
- Display: "Phase {N}: {M} deltas to fix"

### 2. Agent Deployment

For each file touched by the phase, deploy one implementer agent with exclusive ownership of that file. Agents receive:
- The delta description from DELTAS.md
- The file path and line numbers
- The current file contents
- Instructions to make the minimal fix

Agents work in parallel when their files are independent.

### 3. Fix Application

Each agent:
1. Reads the target file
2. Applies the minimal fix described in the delta
3. Verifies the fix compiles/builds
4. Reports the fix with a diff

### 4. Delta Status Update

After each fix is applied:
- Update the delta in DELTAS.md: change `Status: open` to `Status: fixed-unverified`
- Add: `Fixed by: {agent_name}` and `Fixed at: {ISO_TIMESTAMP}`
- Record the commit hash if committed

### 5. Phase Verification

After all deltas in the phase are fixed:
1. Run the phase's verification gate (from plan.md)
2. If the gate fails: HALT. Report failing deltas. Do not proceed.
3. If the gate passes: Proceed to next phase.

### 6. Phase Interlock

**CRITICAL: Wait for explicit user approval before proceeding to the next phase.** The user must confirm the phase results before the next phase begins.

## Output

`.deltas/implementation-log.json`:

```json
{
  "plan": ".deltas/plan.md",
  "started_at": "ISO_TIMESTAMP",
  "completed_at": "ISO_TIMESTAMP",
  "phases": [
    {
      "phase": 1,
      "status": "complete",
      "deltas_fixed": ["D042", "D015", "D018"],
      "verification": "passed",
      "commits": ["abc1234", "def5678"]
    }
  ],
  "deltas": [
    {
      "id": "D042",
      "status": "fixed-unverified",
      "fixed_by": "implementer-1",
      "fixed_at": "ISO_TIMESTAMP",
      "commit": "abc1234",
      "evidence": "CI gate passed"
    }
  ]
}
```

## Contract

- No delta is marked `fixed`. The state is `fixed-unverified` until verified by `/delta-review`.
- The strongest available evidence (CI gate, test suite, real run) is recorded per delta.
- Verification gates are mandatory. Do not skip.
- If a fix introduces a regression, the gate must fail. Do not soften the gate.
- Each phase requires explicit user approval before proceeding.
