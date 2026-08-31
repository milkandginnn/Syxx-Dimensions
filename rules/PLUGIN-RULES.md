# Plugin Usage Rules

Rules for when plugins MUST be used, where they are discovered, how they are structured, and how cross-harness artifacts are generated and maintained.

**Core rule:** Reusable capabilities ship as plugins. If an installed plugin provides a required agent, skill, or command, use that plugin rather than recreating it inline. Edit plugin source only under `plugins/<name>/`; never edit a generated mirror.

***

## 0. Conventions and Definitions

- **0.1.** Keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are used per RFC 2119.

- Every normative requirement is a numbered statement.
- Supporting material belongs beneath the relevant statement.
- Tables are reference material; they do not replace normative requirements.

**0.2.** A **plugin** is a distribution directory under `plugins/<name>/`.

- A plugin may bundle agents under `agents/*.md`, skills under `skills/<name>/SKILL.md`, and commands.
- Plugin metadata, frontmatter, layout, and portable-content requirements are defined in `docs/authoring.md`.

- **0.3.** Plugins are containers; agents, skills, and workflows are plugin contents.

- `SUBAGENT-RULES.md`, `SKILL-RULES.md`, and `WORKFLOW-RULES.md` govern the invocation and behavior of plugin contents.
- This document governs plugin discovery, installation, structure, generation, validation, and lifecycle.

**0.4.** Plugin domains and delta domains are separate concepts.

- A delta domain identifies where a change is tracked.
- A plugin domain identifies the capability family provided by a package.

**0.5.** **Plugin source** means `plugins/<name>/` in this repository.

- A **generated mirror** is any harness-specific artifact emitted by generation tooling, including `.codex/`, `.opencode/`, `.cursor-plugin/`, top-level Gemini `skills/` or `agents/`, and marketplace JSON.

***

## 1. General Principles

**1.1.** Plugin names MUST use lowercase hyphen-case.

- The adapter namespace separator `__` MUST NOT appear in a plugin name.

**1.2.** Generated mirrors are committed for native harness installation but MUST NOT be hand-edited.

- Mirror edits are overwritten by the next generation run and conceal source drift.
- Edit plugin source, then run `make generate-all`.

**1.3.** A reusable capability needed by more than one task SHOULD be implemented as a plugin rather than retained only in an ad-hoc prompt.

- Prompts are transient; plugins are versioned, discoverable, validated, and portable.

**1.4.** Before authoring a new capability, agents MUST check whether an installed plugin already provides it.

- Consult `docs/plugins.md` first.
- Then follow the discovery order in §2.

**1.5.** Duplicating an installed capability is a defect unless the new capability has a documented, materially different scope.

**1.6.** Plugin selection and use MUST preserve context and token budget.

- Load catalog metadata before detailed plugin contents.
- Load only the selected plugin, required agent, skill, command, and task-relevant resources.
- Do not load entire catalogs, unrelated plugins, or all skills for speculative coverage.
- Do not omit required security, validation, accessibility, or safety context solely to reduce token use.

***

## 2. Discovery and Search Order

**2.1.** When a required agent, skill, command, or plugin is absent from the active harness configuration, search in this order:

1. `~/agents`, then `~/.agents` — user-level shared locations.
2. `~/Software/Syxx/agents`, then `~/Software/Syxx/.agents` — project-level shared locations.
3. This repository’s plugin source — `~/Software/Syxx/plugins/<name>/`.
4. Other installed harnesses’ private configuration folders, using the locations listed in `SUBAGENT-RULES.md` §2.4.

**2.2.** Steps 1–3 in §2.1 MUST be exhausted before opening a private configuration directory under step 4.

- Shared locations exist so cross-harness borrowing remains an exception rather than normal workflow.

**2.3.** A capability found under §2.1 step 4 MUST follow the copy-first rule in `SUBAGENT-RULES.md` §§2.5–2.6.

- Copy it into the active harness configuration or shared plugin source.
- Record source path and acquisition date in a frontmatter comment.
- Validate it before first use.
- Never invoke or edit another harness’s private configuration in place.

**2.4.** A capability found in a shared location under §2.1 steps 1–2 MAY be used in place.

- Before modifying shared content, notify its owner because other harnesses may consume the same file.

**2.5.** Discovery MUST be bounded.

- Check the stated locations directly.
- Do not sweep the filesystem recursively for possible agent, skill, plugin, or configuration directories.

***

## 3. When Plugins MUST Be Used

**3.1.** Plugin use is mandatory when:

- `SUBAGENT-RULES.md` requires a specialized subagent that an installed plugin provides.
- `SKILL-RULES.md` requires a skill that an installed plugin provides.
- The user invokes a plugin command, such as `/<plugin>:<command>`.
- A workflow, lane document, or repository instruction routes the task through a plugin.

**3.2.** When multiple acquisition paths exist, agents MUST select capabilities in this order:

1. Installed plugin in the active harness.
2. Plugin in a shared location under §2.1 steps 1–2.
3. Plugin copied from another harness under the copy-first rule.
4. New plugin authoring.

**3.3.** Agents MUST NOT author a replacement plugin before checking installed and shared capabilities.

**3.4.** Agents MUST NOT install, enable, or load plugins wholesale for speculative coverage.

- Every installed or enabled plugin requires an active task justification.
- Tier-1 metadata — plugin name and description — is used for initial selection.

**3.5.** Detailed plugin content MUST be loaded progressively.

1. Read catalog metadata.
2. Select the narrowest relevant plugin.
3. Load the required agent, skill, or command instructions.
4. Load detailed examples, templates, and resources only when needed for the active subtask.

***

## 4. Structure and Authoring Requirements

**4.1.** New plugins MUST follow `docs/authoring.md`.

- Required layout, frontmatter, portable-content style, and harness compatibility are defined there.
- Conformance is validated with:

```sh
make validate STRICT=1
```

**4.2.** Plugin contents MUST comply with their companion rules.

| Content | Governing document |
|---|---|
| Agent prompts | `SUBAGENT-RULES.md` §14 |
| Skills | `SKILL-RULES.md` §8 |
| Commands and workflows | `WORKFLOW-RULES.md` |
| Harness capability differences | `docs/harnesses.md` |

**4.3.** A plugin MUST represent one coherent capability family.

- Grab-bag plugins are prohibited.
- A plugin description must make triggering decidable without reading every contained prompt.
- Split unrelated responsibilities into separate plugins.

**4.4.** Plugin content MUST NOT contain secrets, credentials, tokens, private keys, or sensitive production data.

- Plugin content is mirrored into multiple harness trees and may be published through marketplace metadata.

**4.5.** Plugin instructions MUST be concise and non-duplicative.

- Reference canonical rules and source documentation rather than copying them.
- Move long examples, templates, framework references, and deep implementation material into progressively loaded resources.

***

## 5. Quality Gates

**5.1.** Before pushing a plugin change, all applicable local gates MUST be run:

```sh
make validate STRICT=1
make garden
make test
make smoke-test
```

| Gate | Purpose |
|---|---|
| `make validate STRICT=1` | Validates source structure and generated harness output |
| `make garden` | Detects drift, dead links, stale artifacts, and oversize skills |
| `make test` | Runs the full pytest suite, including plugin evaluation and tool tests |
| `make smoke-test` | Runs real CLI subprocess checks against generated artifacts |

**5.2.** New or substantially revised plugins SHOULD be evaluated through the plugin-evaluation framework before they are presented as recommended examples in rules or authoring guidance.

```text
plugin-eval:eval
plugin-eval:certify
```

**5.3.** A quality gate that cannot run because of a missing tool, unavailable environment, or exhausted budget MUST be recorded as **not run**.

- It MUST NOT be described as passed.
- It MUST NOT be silently skipped.

***

## 6. Cross-Harness Generation

**6.1.** Per-harness artifacts MUST be regenerated after plugin-source changes.

```sh
make generate HARNESS=<codex|cursor|opencode|gemini>
make generate-all
```

- `make generate-all` MUST run before committing plugin-source changes.
- CI fails generated-source drift.

**6.2.** Generated artifacts MUST NOT be hand-edited.

| Harness | Generated artifacts |
|---|---|
| Codex CLI | `.codex/skills/`, `.codex/agents/`, `.codex/plugins/<plugin>/`, `.agents/plugins/marketplace.json` |
| Cursor | `.cursor-plugin/{marketplace,plugin}.json`, `.cursor/rules/` |
| OpenCode | `.opencode/{skills,agents,commands,plugins}/`, `opencode.json` |
| Gemini CLI | Extension-root `skills/`, `agents/`, and `commands/` |

**6.3.** Top-level `skills/` is Gemini output.

- `.claude/` content is read directly by Claude Code and Cursor.
- `.claude/` is not a generation target.

**6.4.** Authors MUST consult `docs/harnesses.md` before assuming cross-harness parity.

- Adapters may strip `tools:`, map model aliases, transform permissions, or omit unsupported functionality.

***

## 7. Versioning and Maintenance

**7.1.** A plugin change MUST commit source and regenerated mirrors together.

- Generated mirrors MUST NOT lag source at any commit.

**7.2.** Renaming or retiring a plugin MUST remove or update every reference in the same change.

- Update rules documents, workflows, commands, agent prompts, skill prompts, marketplace JSON, documentation catalogs, and generated mirrors.

**7.3.** Plugin membership changes MUST update applicable counts and catalogs.

- This includes `CLAUDE.md`, `docs/plugins.md`, marketplace metadata, and generated catalog artifacts.

**7.4.** A stale count, dangling reference, invalid marketplace entry, or generated-source mismatch is repository drift.

***

## 8. Acceptable Plugins by Domain

**8.1.** The following table lists installed-plugin examples by plugin domain.

- It is not exhaustive.
- It is not frozen.
- Plugin domains are not delta domains.

| Domain | Example installed plugins |
|---|---|
| Multi-agent coordination | `agent-teams`; `conductor`; `operating-kit` |
| Debugging and diagnostics | `debugging-toolkit`; `error-diagnostics`; `distributed-debugging` |
| Code quality and review | `git-pr-workflows`; `codebase-cleanup`; `comprehensive-review`; `plugin-eval` |
| Languages | `python-development`; `systems-programming`; `jvm-languages`; `javascript-typescript`; `shell-scripting`; `functional-programming` |
| Backend and APIs | `backend-development`; `backend-api-security`; `api-testing-observability` |
| Frontend, mobile, and UI | `frontend-mobile-development`; `multi-platform-apps`; `ui-design`; `accessibility-compliance` |
| Infrastructure and CI/CD | `cicd-automation`; `cloud-infrastructure`; `kubernetes-operations` |
| Data and databases | `data-engineering`; `database-design`; `database-migrations`; `database-cloud-optimization` |
| Security | `security-scanning`; `security-compliance`; `frontend-mobile-security`; `reverse-engineering` |
| Observability and performance | `observability-monitoring`; `application-performance` |
| ML and LLM | `llm-application-dev`; `llm-finetuning`; `machine-learning-ops` |
| Documentation | `documentation-generation`; `documentation-standards`; `c4-architecture` |
| Business and compliance | `hr-legal-compliance`; `payment-processing`; `startup-business-analyst`; `seo-content-creation` |
| Incident response | `incident-response` |

**8.2.** Agents MUST select the narrowest plugin whose description covers the task.

- When multiple plugins are plausible, record the selected plugin and the reason for rejecting the closest alternative in the plan or handoff.

***

## 9. Cost Controls

**9.1.** Every enabled plugin adds catalog metadata and may add instructions to available context.

- Plugins with no active task trigger SHOULD remain disabled or unloaded.

**9.2.** Plugin use MUST follow progressive disclosure.

1. Catalog metadata.
2. Selected plugin metadata.
3. Required agent, command, or skill instructions.
4. Only the relevant examples, templates, and resources.

**9.3.** Generation and local quality gates use local resources but no external model credits.

- Hosted CI verification remains subject to normal CI and provider budget rules.

**9.4.** Agents MUST NOT use broad plugin discovery, repeated full-catalog loading, or whole-repository prompt injection as substitutes for targeted selection.

***

## 10. Extension and Maintenance of This Document

**10.1.** A new plugin domain MAY be added to §8 only after the plugin passes required quality gates and is installed in the marketplace.

**10.2.** Changes to shared concepts — including discovery order, plugin domains, generation policy, and budget controls — MUST update this document and affected companion documents in the same commit.

**10.3.** This document MUST remain focused on plugin containers and lifecycle.

- Put subagent behavior in `SUBAGENT-RULES.md`.
- Put skill behavior in `SKILL-RULES.md`.
- Put workflow behavior in `WORKFLOW-RULES.md`.