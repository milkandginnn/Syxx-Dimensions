---
description: "Multi-dimensional code review across correctness, security, performance, architecture, testing, and best practices — with effort levels from quick to ultra"
argument-hint: "<target> [--effort quick|medium|high|ultra] [--scope files|diff|branch] [--base main] [--comment] [--fix] [--security-focus] [--performance-critical] [--strict-mode] [--framework react|spring|django|rails|fastapi|nextjs|swiftui|avalonia]"
---

# Code Review

Multi-dimensional code review that scales from a quick sanity check to an exhaustive multi-agent audit. Deploy specialized review agents across correctness, security, performance, architecture, testing, and best practices.

## Pre-flight Checks

1. Parse `$ARGUMENTS`:
   - `<target>`: file path, directory, git diff range (e.g., `main...HEAD`), or PR number
   - `--effort`: `quick` (single-pass, 1 agent), `medium` (2 agents, code + security), `high` (4 agents, all dimensions), `ultra` (5 agents + stateful phases, full-report output). Default: `high` for PRs, `medium` for local changes.
   - `--scope`: `files` (review target as-is), `diff` (git diff against base), or `branch` (full branch diff). Default: `diff`.
   - `--base`: base branch for diff comparison. Default: `main`.
   - `--comment`: post inline comments on the PR (when target is a PR number).
   - `--fix`: apply safe fixes directly to the working tree.
   - `--security-focus`: prioritize security findings, deploy security-auditor even at lower effort levels.
   - `--performance-critical`: prioritize performance analysis.
   - `--strict-mode`: halt on critical findings, require approval before continuing.
   - `--framework`: hint the framework (react, spring, django, rails, fastapi, nextjs, swiftui, avalonia) for framework-specific checks.

## Phase 1: Scope Resolution

1. Determine what to review:
   - **File/Directory**: Use as-is.
   - **Git diff**: Run `git diff {base}...HEAD --name-only` to get changed files.
   - **Branch**: Diff against base branch.
   - **PR number**: Fetch PR diff via `gh pr diff {number}`.
2. Collect the full diff content.
3. Display: "Reviewing {N} files at {effort} effort level."

## Phase 2: Review Execution

### Effort: quick (single agent, ~30s)

Launch a single code-reviewer agent:

```
subagent_type: "comprehensive-review__code-reviewer"
```

The agent reviews for:
1. Correctness: logic errors, edge cases, race conditions
2. Security: OWASP Top 10, injection, auth flaws, hardcoded secrets
3. Code quality: naming, complexity, duplication, error handling
4. Performance: N+1 queries, unnecessary allocations, blocking calls

For each finding: severity (Critical/High/Medium/Low), file:line, failure scenario, remediation.

### Effort: medium (2 agents, parallel)

Launch both agents in parallel:

```
Agent 1: subagent_type: "comprehensive-review__code-reviewer"
  → correctness, code quality, error handling, testing gaps

Agent 2: subagent_type: "comprehensive-review__security-auditor"
  → OWASP Top 10, input validation, auth/authz, crypto, dependencies, config
```

Merge findings, deduplicate, and present organized by severity.

### Effort: high (4 agents, parallel)

Launch all four agents in parallel:

```
Agent 1: subagent_type: "comprehensive-review__code-reviewer"
  → correctness, code quality, maintainability, error handling

Agent 2: subagent_type: "comprehensive-review__security-auditor"
  → security vulnerabilities, OWASP, auth, crypto, secrets

Agent 3: subagent_type: "comprehensive-review__architect-review"
  → architecture, design patterns, component boundaries, dependency management

Agent 4: subagent_type: "general-purpose"
  → testing coverage, documentation, best practices, CI/CD
```

Merge all findings, deduplicate, calibrate severity across reviewers, and present organized by severity and category.

### Effort: ultra (5 agents + phased execution)

Full stateful review with output files in `.code-review/`. Execute in phases:

**Phase 2A — Scope & Context** (write `.code-review/00-scope.md`)

**Phase 2B — Code Quality & Architecture** (2 agents parallel)
- `comprehensive-review__code-reviewer`
- `comprehensive-review__architect-review`
- Output: `.code-review/01-quality-architecture.md`

**Phase 2C — Security & Performance** (2 agents parallel)
- `comprehensive-review__security-auditor`
- `general-purpose` (performance focus)
- Output: `.code-review/02-security-performance.md`

**PHASE CHECKPOINT** — If `--strict-mode` is set and critical findings exist, pause for user approval.

**Phase 2D — Testing & Best Practices** (2 agents parallel)
- `general-purpose` (test coverage)
- `general-purpose` (best practices + framework review)
- Output: `.code-review/03-testing-best-practices.md`

**Phase 2E — Consolidated Report** (`.code-review/04-final-report.md`)
- Executive summary
- All findings by severity (Critical → Low)
- All findings by category (Code Quality, Security, Performance, Architecture, Testing, Best Practices)
- Recommended action plan with effort estimates

## Phase 3: Report & Action

### Standard output (all effort levels)

Present findings organized by severity:
```
# Code Review: {target}

## Executive Summary
[2-3 sentence overview]

## Critical
- [file:line] Finding — fix recommendation

## High
- [file:line] Finding — fix recommendation

## Medium
- [file:line] Finding — fix recommendation

## Low
- [file:line] Finding — fix recommendation

## Verdict: ✅ SHIP / ⚠️ SHIP WITH FIXES / 🛑 DO NOT SHIP
```

### --comment flag

When `--comment` is set and the target is a PR number, post findings as inline review comments using `gh pr review {number} --comment`.

### --fix flag

When `--fix` is set, apply safe, low-risk fixes directly to the working tree:
- Remove dead code
- Fix obvious typos in strings/comments
- Add missing null checks
- Replace hardcoded values with named constants
- Fix incorrect error messages
- Add missing `try/finally` for resource cleanup

Do NOT apply fixes that change behavior, API contracts, or require test updates.

## Execution

1. Initialize the session according to the Pre-flight Checks.
2. Execute the steps sequentially. Pause at every `PHASE CHECKPOINT` / `GATE`.
3. Use available subagents and skills as the protocol directs.