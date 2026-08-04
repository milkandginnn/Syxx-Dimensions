# Syxx — Claude Code / Cowork memory

Loaded automatically by Claude Code (CLI) and Cowork. The **binding** rules live
in `AGENTS.md` and the canonical current state in `PROJECT-STATE.md` — **read both
first; never edit `AGENTS.md`.** This file is the short shared doctrine.

## Mission

Syxx (macOS `SyxxKit` / Windows `Syxx.Kit` / Ubuntu `Server/`) **detects DNS
tampering, hijacking, spoofing, and censorship and produces evidence a user can
actually trust.** Six toolsets — Prophet, Mirror, Portal, Glutton, Cage, Witness —
over a shared core, plus a Python server. Values: Security, Efficacy, Efficiency,
Ingenuity, Simplicity — always **honest-inconclusive over unearned "secure."**

## Lanes (three-way split)

- **Claude** — the reasoning engine (`**/Synthesis/**`, `Conclusion.*`, `Report.*`
  extractor, golden-vector oracle) + design + plan + Stage-5 wording.
- **Cline** — `Server/**`, trust/GroundTruth consumers, probes, the labeled corpus.
- **Copilot** — GUI/CLI **app** surfacing (`SyxxApp/**`, `SyxxUI/**`, CLI report
  layout).

Stay in lane; explicit-path commits only (`git diff --cached --name-only`; **never
`git add -A`**). Flag cross-lane needs — don't edit another lane.

## Honesty spine (binding, every change, every intermediate commit)

1. **No verdict before evidence** — never emit/render a verdict, confidence,
   probability, or reassuring label before its inputs are queried, parsed, validated.
2. **Missing data is not clean data** — missing/invalid/degraded/timed-out →
   inconclusive/unknown, never a silent clean/secure/success.
3. **Tri-state, never boolean** — preserve secure/tampered/inconclusive (and
   success/failure/unknown) end to end.
4. **No fabricated value shown as measured** — every user-visible number traces to
   live evidence, not a mock/default. (Syxx once shipped a suite-green build that
   printed "Success… 0 queries captured… Verdict: 31% inconclusive" — never again.)
5. **The bar is a real run with honest output, not green tests** — one real run per
   phase, per platform. Never edit/disable/soften/delete a test or golden vector to
   go green; a failing honesty test is data.
6. **Platform parity** — a `SyxxKit` change needs its `Syxx.Kit` twin;
   trust-material changes touch `Server/` too.

## Reference library

Papers are extracted to text at `research/reference-library/` (`INDEX.md` =
manifest). **Grep it before external web search** and cite the original PDF — Claude
reads the extracted `.txt`, not PDF bytes. See the `syxx-reference-lookup` skill.
Rebuild: `python3 scripts/build-reference-library.py --source ~/Downloads`.

## Subagents (`.claude/agents/`)

Pipeline: `syxx-neural-researcher` (investigate) → `syxx-neural-engineer` (design)
→ `syxx-neural-planner` (sequence) → `syxx-neural-executor` (implement) →
`syxx-neural-surfacer` (render Stage 5). They pin **`claude-opus-4-8`** (swap to
`claude-fable-5` if preferred). Legacy `syxx-{planner,engineer,researcher,
gui-architect}` remain for continuity.

## Skills & guardrails

Skills in `.claude/skills/`: `syxx-honesty-audit`, `syxx-reference-lookup`. A
PreToolUse hook (`.claude/hooks/lane-guard.sh`) blocks `git add -A` and edits to
`AGENTS.md`. MCP servers (DNS / SQLite / HTTP) are wired in `.mcp.json`.

## Discipline

Always cite `file:line`. Catalog every user-facing string into
`outputs-catalog.csv` (`file:line,trigger,module,task,output,change`). Anchor work
to an open delta ID / ROADMAP workstream.
