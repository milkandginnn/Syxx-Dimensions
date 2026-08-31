# Workflow Usage Rules

Workflows are deterministic scripts that coordinate multiple subagents. They consume
owner-authorized budget and MAY run only with explicit owner opt-in.

**Core rule:** A task benefiting from orchestration is not authorization to orchestrate.
The owner must request the scale before that scale is spent.

## 0. Terms and precedence

0.1. **MUST**, **MUST NOT**, **SHOULD**, and **MAY** follow RFC 2119.

0.2. A **workflow** is a deterministic orchestration script using scripted phases,
pipelines, loops, or fan-out. An **orchestrator agent** is model-directed coordination
under `SUBAGENT-RULES.md` §10. Scripts own deterministic control flow; agents own judgment.

0.3. A **saved workflow** is a named, versioned script on disk. An **inline workflow** is
created for one run. A **run** is one execution identified by a resumable run ID.

0.4. Workflow purposes are distinct from delta domains, as defined in
`SUBAGENT-RULES.md` §0.3.

0.5. Workflows compose with companion rules:
- Spawned agents are subagents governed by `SUBAGENT-RULES.md`.
- Executors load applicable skills under `SKILL-RULES.md` §11.3.
- Saved workflows distribute as capabilities under `PLUGIN-RULES.md`.

## 1. Authorization

1.1. A workflow MAY run only with one of the following:

- Owner wording that explicitly requests orchestration, fan-out, agents, or a workflow.
- Owner invocation of a saved workflow, command, or skill that requires one.
- Explicit standing owner authorization for the workflow or session.

1.2. “This would benefit from parallelism” is never authorization. Without opt-in, the
agent MAY describe the proposed workflow, scope, and approximate budget, then request
approval.

1.3. Workflows and teams plan by default. They MUST NOT consume implementation seats
without an explicit build instruction. Planning agents may write only named plan documents.

1.4. A workflow MUST NOT evade authorization by splitting an unauthorized task into
multiple smaller runs.

1.5. Every run MUST declare a budget ceiling before its first spawned agent.

## 2. When to use a workflow

2.1. Default to one agent using one applicable skill. Use a workflow only when its
structure is necessary for one or more of the following:

- Coverage exceeds one useful context.
- Independent perspectives are needed before commitment.
- A discovered work-list requires repeatable fan-out.
- A defined verification stage requires separation from discovery or implementation.

2.2. Discover before orchestrating. Use cheap, targeted checks to identify the relevant
files, cases, or findings before creating a pipeline over them.

2.3. The workflow author MUST know the orchestration shape before writing the workflow.
The full task solution need not be known.

2.4. Workflow scripts MUST be deterministic. They MUST NOT read wall-clock time or use
unseeded randomness. Timestamps and seeds enter as arguments.

2.5. Workflows MUST expose named phases, labeled agents, and narrator logs at material
transitions.

## 3. Discovery order

3.1. Search for saved workflows and bundled workflows in this order:

1. `~/agents`, then `~/.agents`
2. `~/Software/Syxx/agents`, then `~/Software/Syxx/.agents`
3. Harness-native project locations, including `.claude/workflows/` and
   `plugins/<name>/`
4. Other installed harnesses’ private configuration folders, following
   `SUBAGENT-RULES.md` §2.4

3.2. Steps 1–3 MUST be exhausted before reading step-4 private configuration.

3.3. Material obtained from step 4 MUST follow the copy-first and provenance rules in
`SUBAGENT-RULES.md` §2.5–2.6.

3.4. Shared saved workflows MAY run in place. Edits follow `PLUGIN-RULES.md` §2.4 and
MUST be announced because other harnesses may consume the same file.

## 4. Workflow authoring

4.1. Each workflow MUST begin with a pure-literal `meta` block containing `name`,
`description`, and ordered `phases`. Every phase title MUST match its invoked phase name.

4.2. Use a pipeline by default. Add a barrier only when a stage requires all prior output,
such as full-set deduplication, zero-result exit, or cross-item comparison. Document the
barrier’s reason inline.

4.3. Agent output consumed by a later stage MUST be schema-validated at the boundary.
Later stages MUST NOT depend on reparsing unstructured prose.

4.4. Every spawned-agent prompt MUST satisfy `SUBAGENT-RULES.md` §14 and §16, including:

- Specialized role and task
- Explicit inputs
- Required output schema or format
- Read/write scope ceiling
- Search ceiling
- External-access prohibition unless separately authorized

4.5. The default ceiling is fewer than 15 agents per run. A workflow exceeding that limit
MUST have explicit owner authorization or be reduced in scope.

4.6. Bounded coverage—sampling, top-N selection, no-retry, or similar—MUST log omitted
items and the reason. Silent truncation is prohibited.

4.7. Use worktree isolation only when parallel writers would otherwise conflict. It is
not a default isolation mode.

## 5. Execution and reporting

5.1. Failed, cancelled, or skipped agents MUST resolve to explicit null or failure
results. The workflow MUST count them and report completed, failed, skipped, and omitted
work separately.

5.2. A partial fan-out MUST NOT be reported as complete coverage.

5.3. Completion MUST be notification-driven. The main loop MUST NOT poll at short
intervals; use only long-interval fallback checks.

5.4. Agents MUST NOT independently repeat a sibling’s cited, reproducible external fact.
Verification belongs in an explicit verification stage under `SUBAGENT-RULES.md` §16.3.

5.5. Final output MUST synthesize owner-relevant findings. It MUST include:

```text
STATUS: complete | partial | blocked
WORKFLOW: <name and version>
RUN_ID: <run ID>
BUDGET: <declared ceiling>; <consumed or stop boundary>
SHAPE: <pipeline|panel|fan-out|other>
COVERAGE: <completed scope>; <omitted scope>
AGENTS: <completed>; <failed>; <skipped>; <cancelled>
FINDINGS: <ranked findings or none>
VERIFICATION: <performed checks and results>
LIMITATIONS: <unverified claims, missing access, or none>
NEXT_ACTION: <specific owner decision or none>
```

5.6. Raw subagent transcripts are internal evidence, not the owner deliverable, unless
the owner explicitly requests them.

## 6. Budget controls

6.1. Every run MUST declare a hard ceiling in tokens, agent count, or both. When the
ceiling is reached, the workflow MUST stop and report completed and omitted scope.

6.2. Budget-scaled workflows—such as loop-until-budget or dynamic fleet sizing—MUST
validate that a ceiling exists before spawning agents. The platform agent cap is a safety
backstop, not a budget plan.

6.3. Match verification depth to owner-requested scope:

| Owner request | Default depth |
|---|---|
| “Find any issues” | Small finder set; single verification pass |
| “Thoroughly audit” | Larger finder set; adversarial verification |
| Ambiguous scope | Ask the cheaper clarifying question first |

6.4. Adversarial verification, judge panels, loop-until-dry, and multi-modal sweeps are
budget multipliers. A plan requiring any of them MUST name each multiplier before the run.

6.5. A workflow MUST NOT spend budget on unrequested expansion, redundant validation, or
fresh reruns when cached resumable results are available.

## 7. Resume and iteration

7.1. Resume paused, killed, or edited workflows by run ID. Reuse the unchanged cached
prefix; run only new or changed calls.

7.2. Re-running a resumable workflow from the beginning is a budget violation unless the
owner explicitly requests a fresh run or cache invalidity is documented.

7.3. Before investigating an empty or surprising result, inspect the run journal. Do not
assume cached output is non-empty and do not rerun merely to check.

7.4. Modify persisted workflow files and resume the run. Do not submit a modified inline
script as a new run when a resumable saved run exists.

7.5. Add timestamps only after the workflow returns. Scripts MUST NOT obtain timestamps
during execution.

## 8. Approved workflow purposes

8.1. Choose the narrowest workflow shape that answers the owner’s question.

| Purpose | Allowed shape or entry point |
|---|---|
| Understand a system | Parallel subsystem readers → structured map |
| Design under uncertainty | Independent approaches → scored synthesis |
| Review changes | Dimensions → find → adversarial verify; `agent-teams:team-review`; owner-invoked `/code-review ultra` only |
| Debug hard failure | Competing hypotheses; one investigator per hypothesis; `agent-teams:team-debug` |
| Build decomposed feature | File-ownership streams; `agent-teams:team-feature`; planning mode by default |
| Research | Multi-modal sweep → deep read → synthesis |
| Migrate or sweep | Discover sites → transform → verify; worktrees only when required |
| Evaluate plugin quality | `plugin-eval:eval` or `plugin-eval:certify` |

8.2. `/code-review ultra` is user-billed and MUST NOT self-launch.

8.3. When two workflow shapes are materially plausible, record the selected shape and the
reason the broader alternative was rejected.

## 9. Maintenance

9.1. New saved workflows are versioned artifacts. Before citation in §8, they MUST have a
name, changelog entry, and evaluation evidence under `SUBAGENT-RULES.md` §14.5.

9.2. Changes to shared concepts—authorization, budgets, or discovery order—MUST update
this document and its companion rules in the same commit.

## Material changes

- Reorganized the document around enforceable gates: **authorization**, **workflow selection**, **authoring**, **execution**, **budget**, and **resume**.
- Converted implied requirements into direct prohibitions, especially around unauthorized fan-out, silent truncation, partial-coverage claims, and non-resumable reruns.
- Added a compact mandatory final-report schema so workflow results are consistently parseable.
- Reduced repeated companion-rule explanations while preserving the governing cross-references.