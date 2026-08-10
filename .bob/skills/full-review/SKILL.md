---
name: full-review
description: >-
  Orchestrate comprehensive multi-dimensional code review using specialized
  review agents across architecture, security, performance, testing, and best
  practices
metadata:
  user-invocable: true
  disable-model-invocation: true
  argument-hint: >-
    <target path or description> [--security-focus] [--performance-critical]
    [--strict-mode] [--framework react|spring|django|rails]
---

# Comprehensive Code Review Orchestrator

## CRITICAL BEHAVIORAL RULES

You MUST follow these rules exactly. Violating any of them is a failure.

1. **Execute phases in order.** Do NOT skip ahead, reorder, or merge phases.
2. **Write output files.** Each phase MUST produce its output file in `.full-review/` before the next phase begins. Read from prior phase files -- do NOT rely on context window memory.
3. **Stop at checkpoints.** When you reach a `PHASE CHECKPOINT`, you MUST stop and wait for explicit user approval before continuing. Use the AskUserQuestion tool with clear options.
4. **Halt on failure.** If any step fails (agent error, missing files, access issues), STOP immediately. Present the error and ask the user how to proceed. Do NOT silently continue.
5. **Use only local agents.** All `subagent_type` references use agents bundled with this plugin or `general-purpose`. No cross-plugin dependencies.
6. **Never enter plan mode autonomously.** Do NOT use EnterPlanMode. This command IS the plan -- execute it.

## Pre-flight Checks

Before starting, perform these checks:

### 1. Check for existing session

Check if `.full-review/state.json` exists:

- If it exists and `status` is `"in_progress"`: Read it, display the current phase, and ask the user:

  ```
  Found an in-progress review session:
  Target: [target from state]
  Current phase: [phase from state]

  1. Resume from where we left off
  2. Start fresh (archives existing session)
  ```

- If it exists and `status` is `"complete"`: Ask whether to archive and start fresh.

### 2. Initialize state

Create `.full-review/` directory and `state.json`:

```json
{
  "target": "$ARGUMENTS",
  "status": "in_progress",
  "flags": {
    "security_focus": false,
    "performance_critical": false,
    "strict_mode": false,
    "framework": null
  },
  "current_step": 1,
  "current_phase": 1,
  "completed_steps": [],
  "files_created": [],
  "started_at": "ISO_TIMESTAMP",
  "last_updated": "ISO_TIMESTAMP"
}
```

Parse `$ARGUMENTS` for `--security-focus`, `--performance-critical`, `--strict-mode`, and `--framework` flags. Update the flags object accordingly.

### 3. Identify review target

Determine what code to review from `$ARGUMENTS`:

- If a file/directory path is given, verify it exists
- If a description is given (e.g., "recent changes", "authentication module"), identify the relevant files
- List the files that will be reviewed and confirm with the user

**Output file:** `.full-review/00-scope.md`

Update `state.json`: add `"00-scope.md"` to `files_created`, add step 0 to `completed_steps`.

---

## Phase 1: Code Quality & Architecture Review (Steps 1A-1B)

Run both agents in parallel using multiple Task tool calls in a single response.

### Step 1A: Code Quality Analysis

```
Task:
  subagent_type: "comprehensive-review-code-reviewer"
  description: "Code quality analysis for $ARGUMENTS"
  prompt: |
    Perform a comprehensive code quality review.
    Analyze the target code for: code complexity, maintainability, code duplication,
    Clean Code principles, technical debt, and error handling.
    For each finding: Severity (Critical/High/Medium/Low), file:line, description, fix.
```

### Step 1B: Architecture & Design Review

```
Task:
  subagent_type: "comprehensive-review-architect-review"
  description: "Architecture review for $ARGUMENTS"
  prompt: |
    Review the architectural design and structural integrity.
    Evaluate: component boundaries, dependency management, API design,
    data model, design patterns, architectural consistency.
    For each finding: Severity, architectural impact, improvement recommendation.
```

After both complete, consolidate into `.full-review/01-quality-architecture.md`.

Update `state.json`: set `current_step` to 2, `current_phase` to 2.

---

## Phase 2: Security & Performance Review (Steps 2A-2B)

Read `.full-review/01-quality-architecture.md` for context.

Run both agents in parallel.

### Step 2A: Security Vulnerability Assessment

```
Task:
  subagent_type: "comprehensive-review-security-auditor"
  description: "Security audit for $ARGUMENTS"
  prompt: |
    Execute a comprehensive security audit.
    Analyze for: OWASP Top 10, input validation, authentication/authorization,
    cryptographic issues, dependency vulnerabilities, configuration security.
    For each finding: Severity with CVSS score, CWE reference, file:line,
    attack scenario, remediation steps.
```

### Step 2B: Performance & Scalability Analysis

```
Task:
  subagent_type: "general-purpose"
  description: "Performance analysis for $ARGUMENTS"
  prompt: |
    You are a performance engineer. Analyze for: database performance (N+1 queries,
    missing indexes), memory management, caching opportunities, I/O bottlenecks,
    concurrency issues, frontend performance, scalability concerns.
    For each finding: Severity, estimated performance impact, optimization recommendation.
```

After both complete, consolidate into `.full-review/02-security-performance.md`.

---

## PHASE CHECKPOINT 1 -- User Approval Required

Display a summary of findings from Phase 1 and Phase 2 and ask:

```
Phases 1-2 complete. Summary:
- Code Quality: [X critical, Y high, Z medium findings]
- Architecture: [X critical, Y high, Z medium findings]
- Security: [X critical, Y high, Z medium findings]
- Performance: [X critical, Y high, Z medium findings]

1. Continue -- proceed to Testing & Documentation review
2. Fix critical issues first
3. Pause -- save progress and stop here
```

If `--strict-mode` is set and Critical findings exist, recommend option 2.
Do NOT proceed to Phase 3 until the user approves.

---

## Phase 3: Testing & Documentation Review (Steps 3A-3B)

Run both agents in parallel.

After both complete, consolidate into `.full-review/03-testing-documentation.md`.

---

## Phase 4: Best Practices & Standards (Steps 4A-4B)

Read all previous `.full-review/*.md` files. Run both agents in parallel.

After both complete, consolidate into `.full-review/04-best-practices.md`.

---

## Phase 5: Consolidated Report

Read all `.full-review/*.md` files. Generate `.full-review/05-final-report.md` with:
- Executive Summary
- Findings by Priority (Critical/High/Medium/Low)
- Findings by Category
- Recommended Action Plan

Update `state.json`: set `status` to `"complete"`.

---

## Completion

```
Comprehensive code review complete.

Output files:
- .full-review/00-scope.md
- .full-review/01-quality-architecture.md
- .full-review/02-security-performance.md
- .full-review/03-testing-documentation.md
- .full-review/04-best-practices.md
- .full-review/05-final-report.md
```
