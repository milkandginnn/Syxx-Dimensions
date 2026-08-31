# BUDGET-RULES.md

## Purpose

These rules govern token-efficient AI-assisted work.

The goal is not to minimize tokens at all costs. The goal is to spend tokens where they improve correctness, security, implementation quality, or decision-making—and avoid spending them on repeated context, speculative work, unnecessary narration, or unbounded exploration.

## Core Principles

1. Preserve high-value context; discard low-value context.
2. Prefer evidence, source files, and tests over repeated explanation.
3. Make narrow changes before proposing broad rewrites.
4. Stop when the requested outcome is complete.
5. Ask for a decision when requirements are materially ambiguous.
6. Never sacrifice correctness, security, or reproducibility solely to save tokens.

## Context Budget

### Keep in active context

Retain only information needed for the current task:

- The user’s current objective and acceptance criteria.
- Relevant architecture, invariants, interfaces, and compatibility contracts.
- Files currently being modified or reviewed.
- Open decisions, risks, blockers, and unresolved test failures.
- Commands run, their relevant output, and facts established from primary sources.
- Security-sensitive assumptions, trust boundaries, and rollback constraints.
- summarized rules or

### Compact aggressively

When a task becomes long, replace verbose history with a concise durable handoff containing:

## Context
- Keep: goal, constraints, changed files, decisions and supporting reason(s), test results.
- Compact after each phase into: status, facts, risks, next action.
- Drop: raw logs, duplicate plans, superseded options.
- Never drop: security constraints, compatibility decisions, uncommitted-change rationale.

## Tools
- State the question before each expensive command.
- Prefer targeted search/test/build.
- Run full validation only when the value outweighs the cost.

## Gates
Ask before: external actions, destructive changes, secrets/access changes,
signed bytes, schemas/protocols, public APIs, production deployment.

### Clear stale context

Clear or stop carrying context when it is no longer relevant to an active decision:

- Finished subtasks with no remaining dependency.
- Superseded plans or rejected alternatives.
- Repeated copies of specifications already captured in a canonical summary.
- Large logs after extracting errors, paths, identifiers, and actionable findings.
- Prior user instructions that have been explicitly replaced.

Never clear:

- Current user requirements.
- Security, privacy, legal, or deployment constraints.
- Signed-format, protocol, schema, or compatibility decisions.
- Uncommitted changes and their rationale.
- Facts required to avoid destructive or irreversible actions.
- Items the user asked that you 'pin to context'.
- Prior user instructions and compressed rules.
- Banned or restricted words.

## Compaction Checkpoints

Create a compact handoff when any of the following occurs:

- A task phase completes: discovery, design, implementation, testing, or review.
- The active discussion has accumulated multiple long outputs.
- The task changes direction or switches repositories, services, or subsystems.
- A major design decision has been approved.
- Large source files, logs, traces, or documents were analyzed.
- The assistant is about to begin a new independent task.

A compaction must preserve conclusions, not merely shorten text.

## Investigation Rules

1. Inspect before changing.
2. Read the smallest sufficient set of files first.
3. Search narrowly using identifiers, symbols, error strings, paths, and protocol fields.
4. Prefer targeted follow-up searches over broad repository-wide exploration.
5. Stop investigation once evidence is sufficient to make the requested decision.
6. Do not repeatedly rediscover facts already recorded in the active handoff.

For large logs or packet captures:

- Extract relevant timestamps, hosts, request IDs, error codes, stack frames, or packet fields.
- Summarize patterns and anomalies.
- Retain the original artifact reference if available.
- Do not paste or repeatedly analyze the entire artifact unless required.

## Planning Rules

Before implementation, define:

- Desired outcome.
- Scope and non-goals.
- Files or components likely affected.
- Compatibility and migration impact.
- Validation plan.
- Rollback or recovery path for risky changes.

Use a short plan for simple work. Use a detailed plan only when the change crosses trust boundaries, changes persistent data, affects signatures or protocols, alters public APIs, or has deployment impact.

Do not produce multiple competing full plans unless the user is choosing among materially different tradeoffs.

## Implementation Rules

1. Make the smallest change that fully satisfies the requirement.
2. Reuse existing project patterns before introducing new abstractions.
3. Avoid cosmetic refactors bundled with behavioral changes.
4. Do not rewrite files solely for style unless requested.
5. Do not generate boilerplate that can be inferred or generated locally.
6. Prefer patches and focused diffs over reproducing whole files.
7. Do not narrate obvious code line-by-line unless asked.

For iterative work:

- Apply a coherent patch.
- Validate it.
- Report only meaningful results.
- Continue only if validation reveals a real issue.

## Tool and Command Budget

Use tools when they materially reduce uncertainty.

Before each expensive search, scan, build, test suite, or external query, ask:

- What specific question will this answer?
- What action depends on the answer?
- Is there a smaller or more targeted command?
- Is the result already available in current context?

Prefer:

- Focused tests before full suites.
- File- or package-scoped builds before workspace builds.
- Targeted searches before broad searches.
- Structured output and machine-readable formats where available.
- Diffs, status commands, and test summaries over full logs.

Run full builds, integration tests, fuzzing, or exhaustive scans when required by the change’s risk—not by habit.

## Communication Budget

Responses should be proportionate to the task.

### Default response shape

1. Result or recommendation.
2. Important evidence or rationale.
3. Changed files, commands, or validation performed.
4. Remaining risk, blocker, or required decision.

Avoid:

- Repeating the user’s request.
- Restating unchanged plans.
- Excessive reassurance.
- Long preambles.
- Full source-file dumps when a diff or summary suffices.
- Multiple summaries of the same result.

Use detail when it changes a technical decision, records a security property, explains a failure, or enables reproducibility.

## Decision Gates

Pause and request explicit approval before:

- Sending communications or publishing externally.
- Deleting, overwriting, migrating, or irreversibly transforming data.
- Rotating keys, modifying secrets, or changing access controls.
- Changing signed bytes, canonicalization, schemas, wire protocols, or public APIs.
- Disabling security controls or weakening validation.
- Making production deployment, billing, purchase, or licensing changes.
- Selecting among options with materially different compatibility, security, or maintenance consequences.

At a decision gate, present:

- The decision required.
- Viable options.
- Recommended option.
- Blast radius.
- Reversibility.
- Migration and rollback implications.

Do not spend tokens implementing speculative branches before the decision is made.

## Compatibility and Evidence Rules

For formats, signatures, protocol payloads, snapshots, and forensic evidence:

1. Treat canonical bytes as an API contract.
2. Version intentional byte-layout changes explicitly.
3. Do not silently alter serialization, normalization, ordering, or default values.
4. Preserve historical verification only through named, bounded compatibility profiles.
5. Record compatibility decisions in tests and documentation.
6. Use golden vectors for cross-runtime or cross-language encodings.
7. Separate cryptographic validity from semantic trustworthiness when historical defects exist.

Compatibility work deserves extra token budget because an incorrect shortcut can create permanent operational cost.

## Test Budget

Match validation depth to risk.

| Change type | Minimum validation |
|---|---|
| Documentation or comments | Render/lint if applicable; inspect diff |
| Local refactor | Targeted unit tests; compile affected module |
| Behavioral change | Targeted tests plus relevant regression tests |
| Public API, persistence, or schema change | Compatibility tests; migration and rollback review |
| Security, signatures, parsing, or network code | Negative tests, boundary tests, regression vectors, and cross-platform validation where applicable |

Do not claim validation that was not performed.

If full validation cannot run, state:

- What was run.
- What was not run.
- Why it was not run.
- The residual risk.

## Stop Conditions

Stop work when:

- The requested deliverable is complete.
- Acceptance criteria are met.
- Relevant validation passes.
- Remaining work is optional or outside scope.
- Further investigation has diminishing value without new input.
- A decision gate requires user or owner approval.

Do not continue polishing, expanding scope, or exploring hypothetical issues after completion unless asked.

## Durable Handoff Template

Use this when compacting context or transferring work:

```text
Objective:
- 

Completed:
- 

Files/components affected:
- 

Verified facts:
- 

Decisions made:
- 

Validation performed:
- 

Remaining work:
- 

Risks / compatibility notes:
- 

Next recommended action:
- 
```

## Exception Rule

These rules may be overridden when additional work is necessary to:

- Prevent security harm.
- Avoid data loss or corruption.
- Establish the correctness of a high-impact change.
- Satisfy an explicit user request.
- Meet a documented release, audit, or compliance requirement.

When overriding the budget, state the reason briefly and return to normal budget discipline afterward.