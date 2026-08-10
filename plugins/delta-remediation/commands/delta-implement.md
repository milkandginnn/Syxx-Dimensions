---
description: "Execute the remediation plan, fixing deltas phase by phase"
argument-hint: "[--domain <letter>] [--platform <name>] [--branch <name>] [--schedule YYYY-MM-DD HH-MM-SS] [--repeat] [--phase N]"
---

# Delta Implement

Execute the remediation plan, deploying implementer agents per phase with strict file ownership. Fix deltas, update DELTAS.md, and run verification gates.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `--help`: Display help information and exit without executing
   - `--domain <letter>`: Implement only deltas in specified domain
   - `--platform <name>`: Implement only deltas for specified platform
   - `--branch <name>`: Implement only deltas from specified branch
   - `--phase N`: Execute only the specified phase
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

### `/delta-implement` — Execute Fixes

Execute the remediation plan phase by phase. For each phase, deploy one implementer agent per file with exclusive ownership. Apply minimal fixes, update delta status to `fixed-unverified`, and run the phase's verification gate. **Each phase requires explicit user approval before proceeding to the next.**

**Prerequisite:** `.deltas/plan.md` must exist (run `/delta-plan` first).

**Flags and arguments:**

| Flag | Description | Default | Example |
|---|---|---|---|
| `--help` | Display this help and exit | — | `/delta-implement --help` |
| `--domain <letter>` | Implement only deltas in one domain | All domains | `/delta-implement --domain D` |
| `--platform <name>` | Implement only deltas for one platform | All platforms | `/delta-implement --platform Server` |
| `--branch <name>` | Implement only deltas from a branch | All | `/delta-implement --branch feat/1` |
| `--phase N` | Execute only phase N | All phases | `/delta-implement --phase 1` |
| `--schedule <datetime>` | Run at a future time | Now | `/delta-implement --schedule 2026-08-15 09-00-00` |
| `--repeat` | Recurring schedule | Once | `/delta-implement --schedule 2026-08-15 09-00-00 --repeat` |

**Execution rules:**
- One agent per file, exclusive ownership (no two agents edit the same file)
- Agents work in parallel when their files are independent
- After each fix: delta status → `fixed-unverified`
- After each phase: run verification gate (build, test, parity check)
- If gate fails: HALT. Do not proceed to next phase
- Phase interlock: wait for explicit user approval before next phase

**Output:** `.deltas/implementation-log.json` (fix status, commit hashes, verification results)

---

3. Verify `.deltas/plan.md` exists. If missing, error: "No plan found. Run /delta-plan first."

4. If `--schedule` is set, register a cron job and exit. Otherwise proceed.

5. Check for an existing `.deltas/implementation-log.json`. If found, resume from the last incomplete phase.

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