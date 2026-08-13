# Development Workflow

## Core Principles

1. **plan.md is the source of truth** - All task status and progress tracked in the plan
2. **Test-Driven Development** - Tests required BEFORE implementation. If a test fails, fix the code — never soften the test to advance faulty code. 90% of the things that appear "gated" are not actually gated: verify every gate yourself.
3. **CI/CD Compatibility** - All changes must pass automated pipelines before merge
4. **Incremental Progress** - Small, verifiable commits with clear purpose
5. **Subagent-driven** - Use subagents for all implementation tasks after granular, careful planning

## Task Lifecycle

### Step 1: Task Selection

- Review plan.md for next pending task
- Verify dependencies are complete
- Confirm understanding of acceptance criteria

### Step 2: Progress Marking

- Update task status in plan.md from `[ ]` to `[~]`
- Note start time if tracking velocity

### Step 3: Red Phase (Write Failing Tests)

- Write test(s) that define expected behavior
- Verify test fails for the right reason
- Keep tests focused and minimal
- **Never weaken a test to make faulty code pass — fix the code.**

### Step 4: Green Phase (Make Tests Pass)

- Write minimum code to pass tests
- Avoid premature optimization
- Focus on correctness over elegance

### Step 5: Refactor Phase

- Improve code structure without changing behavior
- Apply relevant style guide conventions
- Remove duplication and clarify intent

### Step 6: Coverage Verification

- Run coverage report
- Ensure new code meets threshold (security core ≥ 80%)
- Add edge case tests if coverage gaps exist
- Consult `.coverage-honesty.json` — verify the anchors actually bind

### Step 7: Deviation Documentation

- If implementation differs from spec, document why
- Update spec if change is permanent
- Flag for review if uncertain

### Step 8: Code Commit

- Stage related changes only
- Write clear commit message referencing task
- Format: `[track-id] task: description` (Conventional Commits)

### Step 9: Git Notes (Optional)

- Add implementation notes for complex changes
- Reference relevant decisions or trade-offs

### Step 10: Plan Update

- Mark task as `[x]` completed in plan.md
- Update any affected downstream tasks
- Note blockers or follow-up items

### Step 11: Plan Commit

- Commit plan.md changes separately
- Format: `[track-id] plan: mark task X complete`

## Phase Completion Protocol

### Checkpoint Commits

At the end of each phase:

1. Ensure all phase tasks are `[x]` complete
2. Run full test suite
3. Verify coverage meets threshold
4. Create checkpoint commit: `[track-id] checkpoint: phase N complete`

### Test Verification

```bash
cd Server && OTEL_TRACES_EXPORTER=none uv run pytest tests/ -q
make validate STRICT=1
make garden
```

### Manual Approval Gates

Phases requiring approval before proceeding:

- Architecture changes
- API contract modifications
- Database schema changes
- Security-sensitive implementations

## Quality Assurance Gates

All code must pass these criteria before merge:

| Gate        | Requirement              | Command                          |
| ----------- | ------------------------ | -------------------------------- |
| 1. Tests    | All tests passing        | `uv run pytest tests/ -q`        |
| 2. Coverage | Security core ≥ 80%      | `pytest --cov` + honesty gate    |
| 3. Style    | Follows style guide      | `ruff check syxx_server/ tests/` |
| 4. Docs     | Public APIs documented   | OpenAPI drift check              |
| 5. Types    | No type errors           | `ty`                             |
| 6. Linting  | No lint errors           | `ruff` / `swiftlint --strict`    |
| 7. Parity   | 4-runtime matrix green   | CI matrix                        |
| 8. Security | No known vulnerabilities | `bandit` / dependency audit      |

> **Verify every gate actually runs and fails as expected.** A gate that silently passes or never runs is not a gate.

## Development Commands

### Environment Setup

```bash
uv sync
```

### Development Server

```bash
cd Server && uv run python server.py
```

### Pre-Commit Checks

```bash
ruff check syxx_server/ tests/
swiftlint --strict
dotnet build
```

### Full Validation

```bash
make validate STRICT=1
make garden
make test
make smoke-test
```

## Workflow Diagram

```
┌─────────────┐
│ Select Task │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Mark [~]    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ RED: Write  │
│ Failing Test│
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ GREEN: Make │
│ Test Pass   │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ REFACTOR    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Verify      │
│ Coverage    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Commit Code │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Mark [x]    │
└──────┬──────┘
       │
       ▼
┌─────────────┐
│ Commit Plan │
└─────────────┘
```
