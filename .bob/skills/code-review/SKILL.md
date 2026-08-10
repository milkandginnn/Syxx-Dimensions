---
name: code-review
description: >-
  Multi-dimensional code review command. Trigger: user invokes /code-review or
  asks to review code, a PR, a diff, or a set of files. Dispatches specialized
  reviewer subagents at four effort levels (quick/medium/high/ultra) and returns
  a severity-ranked report with a SHIP / SHIP WITH FIXES / DO NOT SHIP verdict.
metadata:
  user-invocable: true
  disable-model-invocation: true
---

# /code-review — Multi-Dimensional Code Review

**Usage:** `/code-review <target> [flags]`

**Effort Levels:** `quick` | `medium` | `high` | `ultra`

**Flags:**
- `--effort quick|medium|high|ultra` — Review depth (default: `high` for PRs, `medium` for local)
- `--scope files|diff|branch` — What to review (default: `diff`)
- `--base main` — Base branch for diff comparison
- `--comment` — Post inline PR comments
- `--fix` — Apply safe fixes to working tree
- `--security-focus` — Prioritize security
- `--performance-critical` — Prioritize performance
- `--strict-mode` — Halt on critical findings
- `--framework react|spring|django|rails|fastapi|nextjs|swiftui|avalonia` — Framework hint

## Protocol

You are executing a multi-dimensional code review. Parse the user's arguments and follow this protocol.

### Phase 1: Scope Resolution

Determine what to review:
- **File/Directory**: Use as-is
- **Git diff**: `git diff {base}...HEAD --name-only`
- **Branch**: Diff against base branch
- **PR number**: `gh pr diff {number}`

Display: "Reviewing {N} files at {effort} effort level."

### Phase 2: Review Execution

#### Effort: quick (1 agent)

Deploy the `code-reviewer` agent. Review for: correctness, security (OWASP Top 10), code quality, performance.

#### Effort: medium (2 agents, parallel)

Deploy in parallel:
1. `code-reviewer` — correctness, code quality, error handling, testing gaps
2. `security-auditor` — OWASP Top 10, input validation, auth/authz, crypto, dependencies, config

Merge findings, deduplicate, present by severity.

#### Effort: high (4 agents, parallel)

Deploy in parallel:
1. `code-reviewer` — correctness, code quality, maintainability
2. `security-auditor` — vulnerabilities, OWASP, auth, crypto, secrets
3. `architect-reviewer` — architecture, design patterns, boundaries, dependencies
4. `general-purpose` — testing coverage, documentation, best practices, CI/CD

Merge all findings, calibrate severity, present by severity and category.

#### Effort: ultra (phased, stateful)

Execute in phases, writing output to `.code-review/`:

1. **Scope & Context** → `.code-review/00-scope.md`
2. **Code Quality & Architecture** (2 agents parallel) → `.code-review/01-quality-architecture.md`
3. **Security & Performance** (2 agents parallel) → `.code-review/02-security-performance.md`
4. **PHASE CHECKPOINT** — pause if `--strict-mode` and critical findings exist
5. **Testing & Best Practices** (2 agents parallel) → `.code-review/03-testing-best-practices.md`
6. **Consolidated Report** → `.code-review/04-final-report.md`

### Phase 3: Report

Present findings organized by severity with executive summary and verdict:
- ✅ SHIP
- ⚠️ SHIP WITH FIXES
- 🛑 DO NOT SHIP

### --fix Flag

When `--fix` is set, apply safe fixes directly: dead code removal, typo fixes, missing null checks, constant extraction, error message fixes, resource cleanup.

### --comment Flag

When `--comment` is set and target is a PR, post inline comments via `gh pr review`.

## Execution

1. Parse arguments from the user's invocation.
2. Execute phases sequentially. Pause at checkpoints.
3. Use available agents and tools as directed.
