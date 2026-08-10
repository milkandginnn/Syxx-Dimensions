---
title: Skill Usage Rules
version: 1.0
updated: 2026-08-06
status: active
companion: "SUBAGENT-RULES.md"
---

# Skill Usage Rules

Rules for when skills MUST be used, when they are onloaded and offloaded, what triggers
them, and how often the onload/offload cycle runs.

**Core rule: if an installed skill covers the task at hand, that skill MUST be onloaded
BEFORE the work begins — never improvised around, and never loaded only after a first
attempt fails.**

## 0. Conventions and Definitions

0.1. Keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are used per RFC 2119. Every
rule is a single numbered statement; supporting detail is indented beneath it as bullets.

0.2. **Skill** — a packaged instruction set (`SKILL.md` with `name` and `description`
frontmatter, optional `references/` detail files) that loads procedure and knowledge into
the *current* executor. In this repo: `plugins/*/skills/<name>/SKILL.md`, mirrored
per-harness.

0.3. **Skills are not subagents.** A skill changes how the current agent works; a subagent
is a separate executor. The two compose: when `SUBAGENT-RULES.md` §2 requires a
specialized subagent for an artifact, any applicable skill loads **into that subagent**,
not into the generalist instead of it.

0.4. **Skill domains are a separate concept from delta domains** (same distinction as
SUBAGENT-RULES.md 0.3). A delta domain says where a change is tracked; a skill domain says
what procedure guides it. Never select a skill by delta-domain label.

0.5. **Onload** — reading a skill's body into active context (via the Skill tool, a slash
command, or the harness's auto-discovery). **Offload** — releasing that body from active
context: the skill's procedures stop governing new work and MUST be re-onloaded before
they are relied on again.

0.6. **Progressive disclosure** — the three-tier economy every skill obeys:
- Tier 1: `name` + `description` (metadata) — always resident, costs almost nothing.
- Tier 2: the `SKILL.md` body — onloaded only when triggered.
- Tier 3: `references/` files — onloaded only when the body directs it, file by file.

0.7. **Trigger** — a condition that makes onloading mandatory (§3). **Skip condition** — a
skill-declared condition that makes onloading wrong (§3.6).

---

## 1. General Principles

1.1. The installed skill catalog is the **first stop** before improvising a procedure,
searching the web, or answering from memory in any area the catalog covers.

1.2. While onloaded, a skill's instructions **override default behavior** for matching
work. A skill that is onloaded and then ignored is a defect, not a style choice.

1.3. Skill selection uses metadata only (Tier 1). Agents MUST NOT onload skill bodies
speculatively to "see what's inside" — the `description` exists so relevance is decidable
without paying for the body.

1.4. Only skills that appear in the harness's listing (or that the user named explicitly)
are valid. Agents MUST NOT guess skill names; a plausible-but-unlisted name is an error.

1.5. When both a directory-scoped and an unscoped variant of a skill exist, the most
specific scope covering the files in play wins (e.g., `apps/web:deploy` over `deploy`).

1.6. Skills compose with subagents (0.3): an orchestrator MAY name required skills in a
subagent's prompt, and a subagent MUST onload any skill its own task triggers, subject to
the same rules in this document.

---

## 2. Mandatory Skill Usage

2.1. Onloading is **mandatory** when any of the following is true:
- the user invokes the skill by name (`/<name>` or "use the X skill") — this is a direct
  instruction, not a suggestion
- the task matches a listed skill's `description`
- a skill declares an explicit trigger condition that the current task satisfies (§3.6)
- another rules document routes the task to a skill (e.g., a workflow or lane doc).

2.2. The skill MUST be onloaded **before** the first substantive action of the covered
work — before the first line of code, the first tool call against the target artifact, or
the first sentence of the deliverable. Onloading after a failed first attempt is a
recorded process violation, not a recovery.

2.3. If two or more listed skills cover the same task, the agent MUST onload the most
specific one first, and MAY onload the second only if the first's body does not subsume it
(§6 precedence; §11 budget).

2.4. If no installed skill covers a recurring task that has produced repeated mistakes or
repeated web searches, the agent SHOULD recommend authoring one (§8) rather than
continuing to improvise.

2.5. A skill invocation that returns "runs in a subagent / result arrives later" MUST NOT
be re-invoked while pending, and its result MUST NOT be predicted.

---

## 3. Onloading Rules (Triggers)

3.1. **Explicit trigger** — the user names the skill. Mandatory, immediate, no relevance
judgment permitted.

3.2. **Lexical trigger** — the request contains terms the skill's `description` names as
triggers (e.g., "chart", "dashboard", "recurring", "rebind keys"). Mandatory when the
match is substantive, not incidental.

3.3. **Artifact trigger** — the task touches an artifact class a skill owns (e.g., a
`SKILL.md` under authoring rules; a Helm chart; a `settings.json` behavior change).
Mandatory before the artifact is opened for modification.

3.4. **Situational trigger** — the task is *about to enter* a state a skill governs
(about to create a visualization, about to call an LLM API, about to publish a page).
Mandatory at the decision point, before the state is entered.

3.5. **Routed trigger** — a workflow, command, or rules document names the skill as a
step. Mandatory at that step.

3.6. **Skill-declared TRIGGER/SKIP blocks are binding as written.** Some skills carry
explicit trigger and skip conditions in their descriptions (e.g., `claude-api`: trigger on
any Anthropic/LLM-shaped task; skip when another provider is named). Where they exist,
they override the agent's own relevance judgment in both directions — onload when the
trigger matches even if it "looks like a one-liner," and do not onload when a skip
condition matches even if the topic seems related.

3.7. A skill already onloaded this turn (its content is present in context) MUST NOT be
re-invoked; follow the loaded instructions directly.

3.8. Onloading Tier 3 (`references/`) is triggered only by the skill body's own
directions, and file by file — never the whole directory at once.

---

## 4. Offloading Rules

4.1. A skill is offloaded at its **task boundary**: when the work it was onloaded for
completes, its procedures stop governing new work.

4.2. After offload, an agent MUST NOT apply a skill's specific procedures from memory to
later work; if the same class of work recurs, the skill is onloaded again (§5). Paraphrase
drift from a half-remembered skill is worse than either following it or not using it.

4.3. Context compaction or summarization **implicitly offloads** every skill whose body
did not survive into the summary. Rule 5.3 governs re-onloading.

4.4. An agent SHOULD offload (stop consulting, allow to fall out of working context)
skills whose task was abandoned or superseded, and MUST NOT let a stale skill's output
format bleed into an unrelated deliverable.

4.5. Offload is never destructive: Tier 1 metadata stays resident, and the skill remains
onloadable at any time. There is therefore no reason to "keep a skill around just in
case" (§11).

---

## 5. Frequency: How Often to Onload and Offload

5.1. **Once per task, not once per step.** A skill is onloaded at most once for a given
task; every step of that task then follows the already-loaded body. Re-invoking the same
skill within one task is waste (3.7).

5.2. **Once per session is not a ceiling.** Distinct tasks in one session each trigger
their own onload if the earlier body has been offloaded (4.1) or compacted away (4.3).

5.3. **Re-onload after compaction is mandatory** if covered work continues: when a
summary replaces context and the task governed by a skill is still in progress, the skill
MUST be re-onloaded before the next covered action. A summary of a skill is not the skill.

5.4. **Concurrency ceiling.** An agent SHOULD hold at most **three** skill bodies active
at once. Needing a fourth means either the task decomposition is wrong (split it, or
delegate to subagents per SUBAGENT-RULES.md, each carrying its own one or two skills) or
an earlier skill is done and should be offloaded first.

5.5. **Session start: zero.** No skill bodies are onloaded at session start; only the
Tier 1 index is resident. Skills onload when their first trigger fires, not
prophylactically.

5.6. **Cadence for recurring/scheduled work.** A recurring task (loop, cron, routine)
re-onloads its governing skill on **every firing** — recurrence never grandfathers a
stale copy of the instructions.

---

## 6. Precedence and Conflicts

6.1. Precedence when instructions collide, highest to lowest:
1. Owner directives (session or standing)
2. Repository rules documents (this file, `SUBAGENT-RULES.md`, lane rules, `CLAUDE.md`)
3. The onloaded skill's body
4. The agent's defaults.

An onloaded skill overrides agent defaults (1.2) but never an owner directive or a
repository rule.

6.2. Skill vs skill: the more specific skill wins (1.5); where both remain applicable,
the later-onloaded skill governs only its own domain and MUST NOT rewrite the earlier
skill's outputs.

6.3. Skill vs subagent rules: a skill MAY direct that work be delegated (it can instruct
spawning subagents); it MUST NOT be used to bypass `SUBAGENT-RULES.md` §2 — "a skill told
me how" does not license a generalist to edit code.

6.4. A skill that instructs actions contrary to safety, security, or the honesty rules in
`docs/CLAUDE.md` is followed in its domain expertise and overridden on the conflicting
instruction, with the conflict reported to the owner.

---

## 7. Skill Output and Conformance

7.1. Work produced under an onloaded skill MUST conform to the skill's declared output
format, quality gates, and validation steps — running a skill's validator is part of the
skill, not optional homework.

7.2. If a skill's instructions cannot be satisfied (missing tool, missing file, obsolete
reference), the agent MUST say so in the deliverable rather than silently approximating.
A recalled or onloaded skill reflects what was true when written; named files, flags, and
tools are verified before being relied on.

7.3. When multiple agents collaborate on one task, the outputs they exchange MUST state
which skills governed them (a `skills:` line in the §12-style output block of
`SUBAGENT-RULES.md`), so a reviewer can re-onload the same skills to check conformance.

---

## 8. Authoring and Format Requirements

8.1. New skills follow the repo's authoring conventions (`docs/authoring.md`) and the
SKILL.md spec: `name` + `description` frontmatter; body focused enough to be onloaded
whole; detail pushed to `references/`.

8.2. The `description` MUST make triggering decidable from Tier 1 alone: what the skill
does AND when to use it, with concrete trigger phrases. A description that requires
onloading the body to know whether to onload the body is a defect.

8.3. Skills with dangerous look-alike tasks SHOULD declare explicit skip conditions
(3.6) naming when NOT to fire.

8.4. Body size respects the tightest consuming harness (8 KB body cap for the Codex
mirror; keep bodies lean and push detail to `references/`).

8.5. Skills are versioned artifacts like prompts (`SUBAGENT-RULES.md` 14.5): changes go
through the repo's quality gates (`make validate`, `make garden`, plugin-eval where
scored), and drift between mirrors is a build defect, not a per-harness edit opportunity.

---

## 9. Cross-Harness Onloading

9.1. Source of truth is `plugins/*/skills/<name>/SKILL.md`. Per-harness mirrors are
generated; agents onload from their own harness's location and MUST NOT hand-edit
mirrors:

| Harness | Onload location |
|---|---|
| Claude Code | auto-discovery of `SKILL.md` (Anthropic spec) |
| Codex CLI | `.codex/skills/<plugin>__<skill>/` |
| OpenCode | `.opencode/skills/<plugin>-<skill>/` (hyphenated) |
| Cursor | reads `.claude/skills/` directly |
| Gemini CLI | `skills/<plugin>__<skill>/SKILL.md` (extension root) |

9.2. Top-level `skills/` is Gemini output; it MUST NOT be used as the onload source for
OpenCode installs.

9.3. When a needed skill exists only in another harness's tree, follow the copy-first
rule of `SUBAGENT-RULES.md` 2.5–2.6: copy, record provenance, re-validate — never onload
a foreign mirror in place as if it were native.

---

## 10. Acceptable Skills by Domain (Examples)

10.1. "Acceptable" means installed in this repo's marketplace (or the harness's built-in
set) with a Tier-1 description that matches the task. Examples installed today — not
exhaustive, not frozen:

| Domain | Acceptable installed skills (examples) |
|---|---|
| Python tooling | `python-development:uv-package-manager`; `python-development:python-testing-patterns`; `python-development:python-type-safety` |
| CI/CD | `cicd-automation__github-actions-templates`; `cicd-automation__secrets-management`; `cicd-automation__deployment-pipeline-design` |
| Frontend / web | `frontend-mobile-development__react-state-management`; `frontend-mobile-development__tailwind-design-system`; `javascript-typescript__typescript-advanced-types` |
| Data visualization | `dataviz` (trigger-heavy: fires before ANY chart/dashboard work, in any medium) |
| LLM applications | `llm-application-dev__rag-implementation`; `llm-application-dev__prompt-engineering-patterns`; `claude-api` (binding TRIGGER/SKIP block — the reference example for 3.6) |
| Testing | `javascript-typescript__javascript-testing-patterns`; `shell-scripting:bats-testing-patterns`; `backend-development:temporal-python-testing` |
| Databases | `database-design__postgresql`; `developer-essentials:sql-optimization-patterns`; `framework-migration__database-migration` |
| Documentation | `documentation-generation:architecture-decision-records`; `documentation-generation:changelog-automation`; `documentation-standards:hads` |
| Security | `security-scanning:stride-analysis-patterns`; `blockchain-web3__solidity-security`; `kubernetes-operations__k8s-security-policies` |
| Incident response | `incident-response__postmortem-writing`; `incident-response__on-call-handoff-patterns` |
| Team orchestration | `agent-teams:team-composition-patterns`; `agent-teams:task-coordination-strategies` (pairs with SUBAGENT-RULES.md §10) |
| UI / design | `ui-design:visual-design-foundations`; `ui-design:accessibility-compliance`; `artifact-design` (before any artifact page) |
| Harness configuration | `update-config` (settings/hooks/permissions); `keybindings-help`; `fewer-permission-prompts` |
| Recurring / scheduled work | `loop`; `schedule` (each firing re-onloads per 5.6) |

10.2. Selection mirrors `SUBAGENT-RULES.md` 15.3: narrowest match wins; project-scoped
over user-scoped; record a considered-but-not-onloaded skill when the choice was close.

---

## 11. Context and Cost Controls

*(Companion to SUBAGENT-RULES.md §16 — onloading spends context the way subagents spend
credits.)*

11.1. Every onload is a context spend. Agents MUST NOT onload skills the current task has
not triggered, and MUST NOT onload Tier 3 references beyond what the body directs.

11.2. The concurrency ceiling (5.4) is a budget rule, not a style rule: skill bodies
crowd out task context, and a compaction forced by skill bloat then costs the re-onloads
of 5.3 on top.

11.3. When work is delegated, skills onload in the executor that does the covered work —
not duplicated in both orchestrator and subagent. The orchestrator carries a skill only
when it performs covered work itself.

11.4. An agent MUST NOT bulk-onload the catalog (or any large subset) to "survey
capabilities"; Tier 1 metadata is the survey.

11.5. Repeatedly needing the same skill across many sessions is a signal to promote its
key constraint into a rules document or memory — a one-line standing rule is cheaper than
a per-session onload of a full body. Recommend the promotion; do not self-apply it to
skills owned by others.

---

## 12. Extension and Maintenance

12.1. New skill domains follow §8 and register in the catalog via the marketplace build;
once listed, they participate in the mandatory-usage logic of §2 automatically.

12.2. Retired or renamed skills MUST leave no dangling references in rules documents,
workflows, or agent prompts; `make garden` drift checks cover the mirrors, and rules docs
are checked by hand at each revision of this file.

12.3. This document is versioned in lockstep with `SUBAGENT-RULES.md`; a change to either
file's shared concepts (domains, budgets, precedence) updates both in the same commit.
