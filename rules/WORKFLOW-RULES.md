---
title: Workflow Usage Rules
version: 1.0
updated: 2026-08-06
status: active
companions: ["SUBAGENT-RULES.md", "SKILL-RULES.md", "PLUGIN-RULES.md"]
---

# Workflow Usage Rules

Rules for when multi-agent workflows MAY run, how they are discovered and authored, how
they spend budget, and how they resume.

**Core rule: workflows run only on explicit owner opt-in. A task that would merely
benefit from orchestration is never sufficient — the owner must ask for the scale before
the scale is spent.**

## 0. Conventions and Definitions

0.1. Keywords **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are used per RFC 2119. Every
rule is a single numbered statement; supporting detail is indented beneath it as bullets.

0.2. **Workflow** — a deterministic orchestration script that fans work out across
multiple subagents under scripted control flow (phases, pipelines, loops), as opposed to
an **orchestrator agent** (`SUBAGENT-RULES.md` §10), whose coordination is model-driven.
Deterministic control flow belongs in workflows; judgment calls belong in agents.

0.3. **Workflows compose with the companion rules.** Every agent a workflow spawns is a
subagent under `SUBAGENT-RULES.md` (specialized prompts, scope ceilings); skills onload
inside whichever spawned executor does covered work (`SKILL-RULES.md` 11.3); saved
workflows distribute like any other capability (`PLUGIN-RULES.md`).

0.4. **Workflow purposes are a separate concept from delta domains** (same distinction as
`SUBAGENT-RULES.md` 0.3).

0.5. **Saved workflow** — a named, versioned workflow script on disk. **Inline workflow**
— a script authored for one run. **Run** — one execution, resumable by run ID.

---

## 1. General Principles

1.1. Reach for a workflow only when the structure earns it: coverage a single context
cannot hold, independent perspectives before committing, or fan-out over a discovered
work-list. One agent with one skill is the default; a workflow is the exception.

1.2. Scout before orchestrating: discover the work-list (files, cases, findings) with
cheap targeted checks first, then pipeline over it. The shape of the orchestration step
must be known before the workflow is written — not the shape of the whole task.

1.3. Workflows are deterministic by construction: no wall-clock reads, no randomness
inside the script (they break resume); timestamps and seeds enter as arguments.

1.4. A workflow's phases and agent labels MUST be legible to the owner watching progress:
named phases, per-agent labels, and narrator log lines at every major transition.

---

## 2. Mandatory Opt-In and Authorization

2.1. A workflow MAY run only when one of the following is true:
- the owner asked for orchestration in their own words ("use a workflow", "fan out
  agents", "run the review workflow")
- the owner invoked a saved workflow or a command/skill whose instructions call one
- a standing owner authorization covers it explicitly (session-scoped or written into a
  rules document).

2.2. "This task would benefit from parallelism" is **never** authorization. Absent
opt-in, the agent MAY describe what a workflow would do and what it would roughly cost,
and let the owner decide.

2.3. In this repository, opt-in is further constrained by standing owner directives:
teams and workflows **plan by default** — no implementation seats without an explicit
build instruction — and every run carries a budget ceiling (§6). Planning-mode workflows
constrain each writing agent to named plan documents.

2.4. A workflow MUST NOT be used to launder scale: splitting one unauthorized large task
into several small workflow runs is the same spend and the same violation.

---

## 3. Discovery and Search Order

3.1. When looking for a saved workflow (or a workflow bundled with an agent/command),
search **shared neutral locations first, other IDEs' private configs last** — the same
order as `PLUGIN-RULES.md` §2:
1. `~/agents`, then `~/.agents` (user-level shared)
2. `~/Software/Syxx/agents`, then `~/Software/Syxx/.agents` (project-level shared)
3. Harness-native project locations (for Claude Code: `.claude/workflows/`; plus
   workflows bundled in `plugins/<name>/`)
4. Only then: other installed harnesses' private config folders, per the table in
   `SUBAGENT-RULES.md` 2.4.

3.2. Steps 1–3 MUST be exhausted before any private config in step 4 is opened, and
anything taken from step 4 follows the copy-first + provenance rule
(`SUBAGENT-RULES.md` 2.5–2.6).

3.3. Saved workflows found in shared locations MAY be run in place; edits to them follow
`PLUGIN-RULES.md` 2.4 (announce — other harnesses read the same file).

---

## 4. Authoring Requirements

4.1. Every workflow script begins with a pure-literal `meta` block: `name`,
`description`, and `phases` whose titles match the script's phase calls exactly. No
computed values in `meta`.

4.2. Control flow defaults to **pipeline** (no barrier between stages); a barrier is
justified only when a stage genuinely needs all prior results at once (dedup across the
full set, early-exit on zero findings, cross-item comparison). The justification is
written as a comment at the barrier.

4.3. Structured outputs are schema-validated at the boundary: any agent whose result
feeds later stages declares an output schema rather than having its prose re-parsed.

4.4. Every spawned agent's prompt meets `SUBAGENT-RULES.md` §14 (specialized identity,
inputs, output format) and §16 (scope ceiling: files, search count, no external API calls
unless authorized).

4.5. Size respects the session's guideline (default: under 15 agents) unless the owner's
prompt calls for a different scale. Exceeding it silently is a violation; ask or scale
down.

4.6. No silent caps: if the script bounds coverage (top-N, sampling, no-retry), it logs
what was dropped. Silent truncation reads as "covered everything" when it did not.

4.7. Worktree isolation is used only when agents mutate files in parallel and would
conflict; it is expensive and never a default.

---

## 5. Execution Rules

5.1. Failed or skipped agents resolve to null results; scripts MUST filter and count
them, and the final report MUST state how many agents completed versus were lost. A
partial fan-out reported as complete is the defect class this repository exists to
refuse.

5.2. Completion is notification-driven: the main loop MUST NOT poll a running workflow on
short intervals; long fallback checks only.

5.3. Agents inside a workflow MUST NOT re-verify a sibling's cited, reproducible fact
with fresh external calls (`SUBAGENT-RULES.md` 16.3); verification stages are designed
in, not improvised per agent.

5.4. A workflow's final result is synthesized for the owner — findings ranked, coverage
boundary stated, dropped work named. Raw agent transcripts are not a deliverable.

---

## 6. Budget Rules

*(Companion to SUBAGENT-RULES.md §16; the controlling constraint for every run.)*

6.1. Every run carries a declared budget ceiling (token target, agent count, or both).
The ceiling is hard: when spent, the workflow stops and reports the boundary of completed
work rather than finishing "one more thing."

6.2. Budget-scaled designs (loop-until-budget, fleet sizing) MUST guard on the ceiling
existing; an unguarded loop with no target runs to the agent cap, which is a backstop,
not a plan.

6.3. Verification depth scales with the owner's words: "find any issues" is a few finders
and single-vote verification; "thoroughly audit" is a larger pool with an adversarial
pass. When unsure, ask the cheaper question first.

6.4. Quality patterns (adversarial verify, judge panels, loop-until-dry, multi-modal
sweeps) are budget multipliers. Each one used is named in the plan the owner approves
under §2 — none is free, and none is default.

---

## 7. Resume and Post-Run Rules

7.1. A paused, killed, or edited workflow is resumed by run ID: the unchanged prefix of
agent calls returns cached results; only edited or new calls run live. Re-running a whole
workflow that could resume is a budget violation.

7.2. Before diagnosing an empty or surprising result, read the run's journal — it records
each agent's actual return value. Do not assume cached results are non-empty, and do not
re-run to "check."

7.3. Iteration happens by editing the persisted script file and re-invoking with resume —
not by re-sending a modified script as a fresh run.

7.4. Results are stamped with timestamps *after* the run returns (1.3: no wall-clock
inside scripts).

---

## 8. Acceptable Workflow Shapes by Purpose (Examples)

8.1. These are **purposes**, not delta domains (0.4). Installed or built-in today —
examples, not exhaustive:

| Purpose | Acceptable shape / entry point (examples) |
|---|---|
| Understand a system | Parallel readers over subsystems → structured map (built-in single-phase shape) |
| Design under uncertainty | Judge panel: N independent approaches → scored synthesis |
| Review changes | Dimensions → find → adversarially verify (the canonical pipeline); `agent-teams:team-review`; owner-invoked `/code-review ultra` (user-billed, never self-launched) |
| Debug a hard failure | Competing hypotheses, one investigator each: `agent-teams:team-debug` |
| Build a decomposed feature | File-ownership streams: `agent-teams:team-feature` (planning-mode by default per 2.3) |
| Research a question | Multi-modal sweep → deep-read → synthesize |
| Migrate / sweep a codebase | Discover sites → transform each (worktree isolation per 4.7) → verify |
| Evaluate plugin quality | `plugin-eval:eval` / `plugin-eval:certify` orchestrations |

8.2. Selection mirrors `SUBAGENT-RULES.md` 15.3: the narrowest shape that answers the
question; record the shape considered and rejected when the choice was close.

---

## 9. Extension and Maintenance of This Document

9.1. New saved workflows are versioned artifacts (`SUBAGENT-RULES.md` 14.5): name,
changelog, and eval evidence before they are cited in §8.

9.2. This document is versioned in lockstep with its companions (`SUBAGENT-RULES.md`,
`SKILL-RULES.md`, `PLUGIN-RULES.md`); a change to shared concepts (opt-in, budgets,
discovery order) updates all four in the same commit.
