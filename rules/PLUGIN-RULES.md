---
title: Plugin Usage Rules
version: 1.0
updated: 2026-08-06
status: active
companions: ["SUBAGENT-RULES.md", "SKILL-RULES.md", "WORKFLOW-RULES.md"]
---

# Plugin Usage Rules

Rules for when plugins MUST be used, where they are discovered (and in what order), how
they are structured, and how their cross-harness artifacts are generated and maintained.

**Core rule: capabilities ship as plugins. If a needed agent, skill, or command exists in
an installed plugin, that plugin is used — never re-improvised inline — and plugin source
is edited only under `plugins/<name>/`, never in a generated mirror.**

## 0. Conventions and Definitions

0.1. Keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are used per RFC 2119. Every
rule is a single numbered statement; supporting detail is indented beneath it as bullets.

0.2. **Plugin** — the distribution unit: a directory `plugins/<name>/` bundling any of
agents (`agents/*.md`), skills (`skills/<n>/SKILL.md`), and commands, with auto-discovery
frontmatter per `docs/authoring.md`.

0.3. **Plugins are containers; subagents, skills, and workflows are contents.** Rules for
*invoking* the contents live in the companion documents; this document governs the
container: discovery, installation, structure, generation, and lifecycle.

0.4. **Plugin domains are a separate concept from delta domains** (same distinction as
`SUBAGENT-RULES.md` 0.3). A delta domain says where a change is tracked; a plugin domain
says what capability family a package provides.

0.5. **Source of truth** — `plugins/<name>/` in this repository. **Generated mirror** —
any per-harness artifact emitted by `make generate` (`.codex/`, `.opencode/`,
`.cursor-plugin/`, top-level `skills/`/`agents/` for Gemini, marketplace JSON files).

---

## 1. General Principles

1.1. Plugin names MUST be lowercase and hyphen-separated. The double underscore (`__`) is
the adapter namespace separator and MUST NOT appear in a plugin name.

1.2. Generated mirrors are committed so every harness installs natively, and they MUST
NOT be hand-edited: a mirror edit is overwritten by the next generation and hides drift
until CI fails it. All changes go through source and `make generate-all`.

1.3. A capability needed by more than one task belongs in a plugin, not in an ad-hoc
prompt: prompts evaporate, plugins version.

1.4. Before authoring anything new, agents MUST check whether an installed plugin already
provides it (`docs/plugins.md` catalog first, then discovery order in §2). Duplicating an
installed capability is a defect.

---

## 2. Discovery and Search Order

2.1. When a required subagent, skill, or command is not found in your own harness's
active configuration, search in this order — **shared neutral locations first, other
IDEs' private configs last**:
1. `~/agents`, then `~/.agents` (user-level shared)
2. `~/Software/Syxx/agents`, then `~/Software/Syxx/.agents` (project-level shared)
3. This repository's plugin source: `~/Software/Syxx/plugins/<name>/`
4. Only then: other installed harnesses' private config folders, per the table in
   `SUBAGENT-RULES.md` 2.4.

2.2. Steps 1–3 MUST be exhausted before any private config folder in step 4 is opened.
The shared locations exist precisely so cross-IDE borrowing is the exception, not the
routine.

2.3. Anything taken from step 4 follows the copy-first rule (`SUBAGENT-RULES.md`
2.5–2.6): copy into your own config, record provenance (source path and date) in a
frontmatter comment, re-validate before first use. Never use or edit a foreign private
config in place.

2.4. A capability found in a shared location (steps 1–2) MAY be used in place — shared
locations are owned jointly — but edits to it MUST be announced to the owner, since other
harnesses read the same file.

---

## 3. When Plugins MUST Be Used

3.1. Plugin use is **mandatory** when:
- a companion rules document requires a specialized subagent (`SUBAGENT-RULES.md` §2) or
  skill (`SKILL-RULES.md` §2) that an installed plugin provides
- the user invokes a plugin's command (`/<plugin>:<command>`) — direct instruction
- a workflow or lane document routes the task through a plugin.

3.2. Prefer, in order: an installed plugin; a plugin available in a shared location
(§2.1 steps 1–2); a plugin copyable from another harness (step 4, copy-first); authoring
a new plugin (§4). Skipping straight to authoring when an install exists violates 1.4.

3.3. Agents MUST NOT enable or install plugins wholesale "for coverage." Each
installation is justified by a task that triggers it — the catalog's Tier-1 metadata
(name + description) is the survey, mirroring `SKILL-RULES.md` 11.4.

---

## 4. Structure and Authoring Requirements

4.1. New plugins follow `docs/authoring.md` exactly: frontmatter shapes, directory
layout, and the portable-content style guide. Read it before adding a plugin; conformance
is checked by `make validate STRICT=1`.

4.2. Contents obey their own companion rules: bundled agent prompts meet
`SUBAGENT-RULES.md` §14; bundled skills meet `SKILL-RULES.md` §8 (including the 8 KB
Codex body cap and decidable Tier-1 descriptions).

4.3. A plugin MUST be coherent: one capability family per plugin. A grab-bag plugin
defeats discovery (its description cannot make triggering decidable) and bloats every
consuming harness's listing.

4.4. Never commit secrets in plugin content; plugin files are mirrored into multiple
harness trees and published via marketplace JSON.

---

## 5. Quality Gates

5.1. Before pushing any plugin change, all four gates MUST pass locally:
- `make validate STRICT=1` — structural validation across harness outputs
- `make garden` — drift detection (dead links, stale artifacts, oversize skills)
- `make test` — full pytest suite (plugin-eval + tools/tests/)
- `make smoke-test` — real-CLI subprocess tests against generated artifacts.

5.2. New or substantially revised plugins SHOULD be scored through the plugin-eval
framework (`plugin-eval:eval`, `plugin-eval:certify`) before being treated as an
acceptable example in any rules document.

5.3. A quality gate that cannot run (missing tool, exhausted budget) is reported as
not-run — never described as passed, and never quietly skipped. This mirrors the honesty
rules the rest of the repository operates under.

---

## 6. Cross-Harness Generation

6.1. Per-harness artifacts are regenerated with `make generate HARNESS=<codex|cursor|
opencode|gemini>` or `make generate-all`, and `make generate-all` MUST run before
committing source changes — CI fails on drift.

6.2. Generation targets (never hand-edited, per 1.2):

| Harness | Generated artifacts |
|---|---|
| Codex CLI | `.codex/skills/`, `.codex/agents/`, `.codex/plugins/<p>/`, `.agents/plugins/marketplace.json` |
| Cursor | `.cursor-plugin/{marketplace,plugin}.json`, `.cursor/rules/` |
| OpenCode | `.opencode/{skills,agents,commands,plugins}/`, `opencode.json` |
| Gemini CLI | `skills/`, `agents/`, `commands/` at extension root |

6.3. Top-level `skills/` is Gemini output (`SKILL-RULES.md` 9.2); `.claude/` content is
read directly by Claude Code and Cursor and is not a generation target.

6.4. Harness capability deltas (what each harness drops or transforms — `tools:`
stripping, model alias mapping, permission blocks) are documented in
`docs/harnesses.md`; authors MUST consult it rather than assuming parity.

---

## 7. Versioning and Maintenance

7.1. A plugin change is one commit spanning source and regenerated mirrors together;
mirrors MUST NOT lag source at any commit (CI treats drift as failure).

7.2. Retiring or renaming a plugin MUST leave no dangling references — rules documents,
workflows, agent prompts, marketplace JSON, and the catalogs in `docs/` are all updated
in the same change.

7.3. Counts and catalogs (`CLAUDE.md` header, `docs/plugins.md`) are regenerated or
corrected when plugin membership changes; a stale count is drift.

---

## 8. Acceptable Plugins by Domain (Examples)

8.1. Installed today in this marketplace — examples, not exhaustive, not frozen. These
are **plugin domains**, not delta domains (0.4):

| Domain | Acceptable installed plugins (examples) |
|---|---|
| Multi-agent teamwork | `agent-teams`; `conductor`; `operating-kit` |
| Debugging / diagnostics | `debugging-toolkit`; `error-diagnostics`; `distributed-debugging` |
| Code quality / review | `git-pr-workflows`; `codebase-cleanup`; `comprehensive-review`; `plugin-eval` (meta: scores the others) |
| Languages | `python-development`; `systems-programming`; `jvm-languages`; `javascript-typescript`; `shell-scripting`; `functional-programming` |
| Backend / APIs | `backend-development`; `backend-api-security`; `api-testing-observability` |
| Frontend / mobile / UI | `frontend-mobile-development`; `multi-platform-apps`; `ui-design`; `accessibility-compliance` |
| Infrastructure / CI/CD | `cicd-automation`; `cloud-infrastructure`; `kubernetes-operations` |
| Data / databases | `data-engineering`; `database-design`; `database-migrations`; `database-cloud-optimization` |
| Security | `security-scanning`; `security-compliance`; `frontend-mobile-security`; `reverse-engineering` |
| Observability / performance | `observability-monitoring`; `application-performance` |
| ML / LLM | `llm-application-dev`; `llm-finetuning`; `machine-learning-ops` |
| Documentation | `documentation-generation`; `documentation-standards`; `c4-architecture` |
| Business / compliance | `hr-legal-compliance`; `payment-processing`; `startup-business-analyst`; `seo-content-creation` |
| Incident response | `incident-response` |

8.2. Selection mirrors `SUBAGENT-RULES.md` 15.3: the narrowest plugin whose description
covers the task wins; record close alternatives considered.

---

## 9. Cost Controls

*(Companion to SUBAGENT-RULES.md §16 and SKILL-RULES.md §11.)*

9.1. Every enabled plugin adds Tier-1 listings to every session's context. Plugins that
no current work triggers SHOULD be disabled rather than carried.

9.2. Generation and quality gates run locally and cost no external credits; hosted-CI
verification of plugin changes is subject to the same budget rules as all other CI work.

9.3. Searching other harnesses' configs (§2.1 step 4) is bounded: check the table's
locations directly; do not sweep the filesystem for config folders.

---

## 10. Extension and Maintenance of This Document

10.1. New plugin domains are added to §8 when a plugin passes §5's gates; the table cites
only installed, validated plugins.

10.2. This document is versioned in lockstep with its companions
(`SUBAGENT-RULES.md`, `SKILL-RULES.md`, `WORKFLOW-RULES.md`); a change to shared concepts
(discovery order, budgets, domains) updates all four in the same commit.
