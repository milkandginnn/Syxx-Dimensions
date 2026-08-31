# Skill Usage Rules

Rules for when skills MUST be used, when they are onloaded and offloaded, what triggers them, and how skill context is budgeted.

**Core rule:** If an installed skill covers the active task, that skill MUST be onloaded before covered work begins. Do not improvise around an applicable skill or load it only after an initial attempt fails.

***

## 0. Conventions and Definitions

**0.1.** Keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are used per RFC 2119.

- Every normative requirement is a numbered statement.
- Supporting material belongs beneath the relevant statement.
- Tables are reference material; they do not replace normative requirements.

**0.2.** A **skill** is a packaged instruction set.

- A skill contains `SKILL.md` with `name` and `description` frontmatter.
- It may include detailed material under `references/`.
- In this repository, source skills live under `plugins/*/skills/<name>/SKILL.md` and are mirrored per harness.

**0.3.** Skills are not subagents.

- A skill changes how the current executor performs work.
- A subagent is a separate executor.
- When `SUBAGENT-RULES.md` requires a specialized subagent, applicable skills MUST load into that subagent rather than into a generalist acting in its place.

**0.4.** Skill domains and delta domains are separate concepts.

- A delta domain identifies where a change is tracked.
- A skill domain identifies the procedure that governs work.
- Agents MUST NOT select a skill solely because its name resembles a delta-domain label.

**0.5.** **Onload** means reading a skill body into active context through a Skill tool, slash command, harness auto-discovery, or equivalent native mechanism.

**0.6.** **Offload** means the skill body is no longer active context.

- After offload, the skill’s detailed procedures MUST NOT be relied upon until it is onloaded again.
- Offload is not deletion; the skill remains discoverable through Tier-1 metadata.

**0.7.** **Progressive disclosure** is the required three-tier loading model.

| Tier | Content | Loading rule |
|---|---|---|
| Tier 1 | `name` and `description` metadata | Available for discovery |
| Tier 2 | `SKILL.md` body | Onload only after a trigger matches |
| Tier 3 | `references/` files | Load only when the Tier-2 body directs it or the active task specifically requires it |

**0.8.** A **trigger** makes onloading mandatory. A **skip condition** makes onloading inappropriate even when the subject appears related.

***

## 1. General Principles

**1.1.** The installed skill catalog MUST be checked before improvising a procedure, searching the web, or answering from memory in a domain covered by the catalog.

**1.2.** While onloaded, a skill’s instructions override default agent behavior for work within the skill’s scope.

- An onloaded skill that is ignored is a process defect.
- A skill never overrides owner directives, repository rules, safety requirements, or higher-precedence instructions.

**1.3.** Skill selection MUST begin with Tier-1 metadata.

- Agents MUST NOT speculatively onload skill bodies merely to inspect their contents.
- The description must be sufficient to determine whether the skill applies.

**1.4.** Agents MUST NOT guess skill names.

- A skill is valid only when listed by the active harness or explicitly named by the user.
- A plausible but unlisted skill name is not a valid invocation target.

**1.5.** When both scoped and unscoped variants exist, the narrowest applicable scope wins.

- Example: `apps/web:deploy` takes precedence over `deploy` for work under `apps/web`.

**1.6.** An orchestrator MAY identify required skills in a subagent assignment.

- A subagent MUST onload every skill triggered by its own work.
- The orchestrator MUST NOT load a skill merely because a subagent, rather than the orchestrator, needs it.

***

## 2. Mandatory Skill Usage

**2.1.** A skill MUST be onloaded when any of the following applies:

- The user explicitly invokes the skill by name.
- The active task substantively matches a listed skill description.
- The task satisfies an explicit trigger declared by the skill.
- A workflow, lane, command, or repository rule routes the task through the skill.

**2.2.** A required skill MUST be onloaded before the first substantive covered action.

- For implementation work, this means before editing code or invoking target-repository tools.
- For research, this means before conducting covered research.
- For written deliverables, this means before producing the first substantive content.

Onloading only after an unsuccessful first attempt is a recorded process violation, not normal recovery.

**2.3.** When multiple skills may apply, the agent MUST onload the narrowest applicable skill first.

- Onload another skill only when the first does not subsume the needed procedure.
- Follow the precedence rules in §6 and budget rules in §11.

**2.4.** If no installed skill covers a recurring task that repeatedly causes mistakes, repeated web searches, or repeated rediscovery, the agent SHOULD recommend authoring a skill.

**2.5.** A skill invocation that reports asynchronous execution or pending subagent work MUST NOT be invoked again while pending.

- The agent MUST NOT predict, fabricate, or pre-state the pending result.

***

## 3. Onload Triggers

**3.1.** An explicit trigger occurs when the user names a skill.

- This is mandatory and immediate.
- No independent relevance judgment is permitted.

**3.2.** A lexical trigger occurs when the request substantively contains trigger terms named in the skill description.

- Incidental word overlap does not trigger a skill.
- A substantive match does.

**3.3.** An artifact trigger occurs when the task modifies or creates an artifact class owned by a skill.

- The skill MUST be onloaded before the artifact is modified.
- Examples include `SKILL.md`, Helm charts, visual artifacts, or configuration files governed by a skill.

**3.4.** A situational trigger occurs when work is about to enter a state governed by a skill.

- Onload at the decision point, before creating the governed artifact, calling the governed API, publishing the governed content, or entering the governed workflow.

**3.5.** A routed trigger occurs when a workflow, command, lane, or rules document names a skill as a required step.

**3.6.** A skill’s declared `TRIGGER` and `SKIP` conditions are binding.

- Onload when its trigger matches, even when the work appears small.
- Do not onload when its skip condition matches, even when the topic appears related.
- A declared skip condition overrides lexical or topical similarity.

**3.7.** A skill already onloaded in the current active context MUST NOT be invoked again.

- Follow the already-loaded body.
- Do not spend context on duplicate loading.

**3.8.** Tier-3 reference files MUST be loaded individually.

- Load a reference only when directed by the skill body or required by the active subtask.
- Do not load an entire `references/` directory preemptively.

***

## 4. Offloading Rules

**4.1.** A skill is offloaded when the task for which it was onloaded is complete, abandoned, superseded, or no longer active.

**4.2.** After offload, an agent MUST NOT apply skill-specific procedures from memory.

- If covered work resumes, onload the skill again.
- A remembered summary is not a replacement for the current skill body.

**4.3.** Context compaction implicitly offloads every skill body not retained in active context.

- If covered work continues after compaction, re-onload the skill before the next covered action.

**4.4.** Agents SHOULD offload skills that no longer govern active work.

- A stale skill’s output format, terminology, or workflow MUST NOT bleed into unrelated deliverables.

**4.5.** Agents MUST NOT retain a skill body “just in case.”

- Tier-1 metadata remains available.
- Re-onload when a new trigger occurs.

***

## 5. Onload and Offload Frequency

**5.1.** A skill MUST be onloaded at most once per active task unless it was offloaded or lost during compaction.

**5.2.** Distinct tasks in one session may independently trigger the same skill.

- Earlier use does not satisfy a later task after the skill has been offloaded.

**5.3.** A skill MUST be re-onloaded after compaction when covered work remains active.

- A summary of the skill is not the skill.

**5.4.** An executor SHOULD hold no more than three Tier-2 skill bodies in active context at once.

- Needing a fourth usually indicates incorrect task decomposition, an opportunity to delegate, or a skill ready to offload.
- If four skills are genuinely required, document the reason in the plan or handoff.

**5.5.** No Tier-2 skill bodies are active at session start.

- Only Tier-1 metadata is available until a trigger occurs.
- Skills MUST NOT be preloaded prophylactically.

**5.6.** A recurring or scheduled task MUST re-onload its governing skill on every execution.

- Recurrence does not permit relying on stale instruction bodies.

***

## 6. Precedence and Conflicts

**6.1.** When instructions conflict, follow this precedence order:

1. Owner directives.
2. Repository rules, including this document, `SUBAGENT-RULES.md`, workflow rules, lane rules, and `CLAUDE.md`.
3. Onloaded skill instructions.
4. Default agent behavior.

**6.2.** A skill overrides default behavior within its scope but MUST NOT override owner directives, repository rules, safety requirements, or honesty requirements.

**6.3.** When two skills conflict, the more specific skill governs its domain.

- The later-loaded skill governs only its own scope.
- It MUST NOT rewrite outputs or decisions owned by the earlier skill outside its scope.

**6.4.** A skill MAY require delegation but MUST NOT be used to bypass `SUBAGENT-RULES.md`.

- A skill explaining a procedure does not authorize a generalist to perform work that repository rules require a specialist to perform.

**6.5.** If a skill conflicts with safety, security, or honesty requirements, follow the higher-precedence rule.

- Report the conflict to the owner or orchestrator.
- Apply the skill only to the non-conflicting portion of its domain.

***

## 7. Skill Output and Conformance

**7.1.** Work produced under an onloaded skill MUST conform to the skill’s declared output format, quality gates, and validation requirements.

- Running a required validator is part of using the skill.

**7.2.** If a skill cannot be followed because a required tool, file, environment, or reference is unavailable, the agent MUST report the limitation.

- Do not silently approximate.
- Verify named tools, files, flags, APIs, and commands before relying on them.

**7.3.** Cross-agent handoffs MUST identify governing skills.

- Use the `skills:` field in the output format required by `SUBAGENT-RULES.md`.
- This allows reviewers and successor agents to re-onload the same skills for conformance review.

***

## 8. Authoring and Format Requirements

**8.1.** New skills MUST follow `docs/authoring.md` and the repository `SKILL.md` specification.

- Use `name` and `description` frontmatter.
- Keep the main body focused enough to load as a whole.
- Put extensive detail in `references/`.

**8.2.** A skill description MUST make triggering decidable from Tier-1 metadata alone.

- State what the skill does.
- State when it applies.
- Include concrete trigger phrases where useful.
- A description that requires reading the body to decide whether to read the body is defective.

**8.3.** Skills with dangerous or misleading look-alike tasks SHOULD declare explicit skip conditions.

**8.4.** Skill bodies MUST respect the tightest consuming-harness limit.

- The Codex mirror permits a maximum 8 KB body.
- Keep bodies concise and move detailed material to `references/`.

**8.5.** Skills are versioned artifacts.

- Changes MUST pass repository quality gates.
- Generated-mirror drift is a build defect.
- Do not create per-harness manual variations.

***

## 9. Cross-Harness Onloading

**9.1.** The source of truth for skills is:

```text
plugins/*/skills/<name>/SKILL.md
```

Harnesses onload generated or directly read native copies from the following locations:

| Harness | Onload location |
|---|---|
| Claude Code | Native `SKILL.md` auto-discovery |
| Codex CLI | `.codex/skills/<plugin>__<skill>/` |
| OpenCode | `.opencode/skills/<plugin>-<skill>/` |
| Cursor | `.claude/skills/` |
| Gemini CLI | `skills/<plugin>__<skill>/SKILL.md` |

**9.2.** Top-level `skills/` is Gemini output.

- It MUST NOT be treated as the native skill source for OpenCode.

**9.3.** When a required skill exists only in another harness’s tree, follow the copy-first rule in `SUBAGENT-RULES.md` §§2.5–2.6.

- Copy it.
- Record provenance.
- Re-validate it.
- Never onload or edit a foreign mirror in place.

***

## 10. Acceptable Skills by Domain

**10.1.** An acceptable skill is one installed in this repository’s marketplace or built into the active harness whose Tier-1 description matches the active task.

- The examples below are not exhaustive or permanent.
- Selection follows the narrowest-match rule.

| Domain | Example skills |
|---|---|
| Python tooling | `python-development:uv-package-manager`; `python-development:python-testing-patterns`; `python-development:python-type-safety` |
| CI/CD | `cicd-automation__github-actions-templates`; `cicd-automation__secrets-management`; `cicd-automation__deployment-pipeline-design` |
| Frontend and web | `frontend-mobile-development__react-state-management`; `frontend-mobile-development__tailwind-design-system`; `javascript-typescript__typescript-advanced-types` |
| Data visualization | `dataviz` |
| LLM applications | `llm-application-dev__rag-implementation`; `llm-application-dev__prompt-engineering-patterns`; `claude-api` |
| Testing | `javascript-typescript__javascript-testing-patterns`; `shell-scripting:bats-testing-patterns`; `backend-development:temporal-python-testing` |
| Databases | `database-design__postgresql`; `developer-essentials:sql-optimization-patterns`; `framework-migration__database-migration` |
| Documentation | `documentation-generation:architecture-decision-records`; `documentation-generation:changelog-automation`; `documentation-standards:hads` |
| Security | `security-scanning:stride-analysis-patterns`; `blockchain-web3__solidity-security`; `kubernetes-operations__k8s-security-policies` |
| Incident response | `incident-response__postmortem-writing`; `incident-response__on-call-handoff-patterns` |
| Team orchestration | `agent-teams:team-composition-patterns`; `agent-teams:task-coordination-strategies` |
| UI and design | `ui-design:visual-design-foundations`; `ui-design:accessibility-compliance`; `artifact-design` |
| Harness configuration | `update-config`; `keybindings-help`; `fewer-permission-prompts` |
| Recurring work | `loop`; `schedule` |

**10.2.** When multiple skills appear applicable, agents MUST select the narrowest match.

- Prefer project-scoped skills over user-scoped skills when both cover the active files.
- Record a close but rejected skill when the selection materially affects implementation or review.

***

## 11. Context and Cost Controls

**11.1.** Every Tier-2 skill onload is a context expenditure.

- Agents MUST NOT onload untriggered skills.
- Agents MUST NOT load Tier-3 references beyond those the skill body or active task requires.

**11.2.** The three-skill concurrency ceiling in §5.4 is a budget requirement.

- Excess skill bodies crowd out task context.
- Context bloat increases compaction risk.
- Compaction increases re-onload cost and creates procedural drift risk.

**11.3.** When work is delegated, skills MUST load into the executor performing the covered work.

- The orchestrator loads a skill only when the orchestrator performs work governed by that skill.
- Do not duplicate a skill body in orchestrator and subagent context without a documented need.

**11.4.** Agents MUST NOT bulk-onload the catalog or a large skill subset to survey capabilities.

- Tier-1 metadata is the survey mechanism.

**11.5.** Repeated cross-session use of the same skill MAY justify promoting a stable, short constraint to a shared rules document or memory.

- Recommend the promotion to the owner.
- Do not independently alter skills owned by another maintainer.

***

## 12. Extension and Maintenance

**12.1.** New skill domains MUST follow §8 and register in the marketplace catalog.

- Once registered, they participate in mandatory skill-selection rules.

**12.2.** Retired or renamed skills MUST leave no dangling references.

- Update rules documents, workflows, commands, agent prompts, marketplace metadata, and generated mirrors in the same change.

**12.3.** This document MUST remain aligned with `SUBAGENT-RULES.md`.

- Changes to shared concepts, including domains, budgets, precedence, discovery, or delegation, MUST update both documents in the same commit.