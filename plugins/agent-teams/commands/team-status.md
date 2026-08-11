---
description: "Display team members, task status, and progress for an active agent team"
argument-hint: "[team-name] [--tasks] [--members] [--json]"
---

# Team Status

Display the current state of an active agent team including members, tasks, and progress.

## Phase 1: Team Discovery

1. Parse `$ARGUMENTS` for team name and flags:
   - If team name provided, use it directly
   - If no team name, check `~/.claude/teams/` for active teams
   - If multiple teams exist and no name specified, list all teams and ask user to choose
   - `--tasks`: show only task details
   - `--members`: show only member details
   - `--json`: output raw JSON instead of formatted table

2. Read team config from `~/.claude/teams/{team-name}/config.json` using the Read tool
3. Call `TaskList` to get current task state

## Phase 2: Status Display

### Members Table

Display each team member as a `task-id=member-name` pair that can be copied inline for use with `--assign`:

```
Team: {team-name}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  comprehensive-review__code-reviewer-call_abc123def456=security-rev      completed
  comprehensive-review__code-reviewer-call_def789ghi012=perf-rev          completed
  comprehensive-review__code-reviewer-call_ghi345jkl678=arch-rev          completed
```

### Tasks Table

Display tasks with status, assignee, and dependencies:

```
Tasks:
  ID   Status        Owner           Subject
  ─────────────────────────────────────────────────
  #1   completed     security-rev    Review auth module
  #2   in_progress   security-rev    Review API endpoints
  #3   completed     perf-rev        Profile database queries
  #4   in_progress   arch-rev        Analyze module structure
  #5   pending       (unassigned)    Consolidate findings

Progress: 40% (2/5 completed)
```

### JSON Output

If `--json` flag is set, output the raw team config and task list as JSON.
