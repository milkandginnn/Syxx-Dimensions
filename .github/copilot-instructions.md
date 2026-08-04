# Syxx — GitHub Copilot instructions

Repo-wide guidance auto-loaded by Copilot (CLI and IDE). The binding rules live in
`AGENTS.md` and `PROJECT-STATE.md` — **read those first; never edit `AGENTS.md`.**
This file is the short doctrine every Copilot session and custom agent shares.

## Mission

Syxx is a cross-platform toolkit (macOS `SyxxKit` / Windows `Syxx.Kit` / Ubuntu
`Server/`) that **detects DNS tampering, hijacking, spoofing, and censorship and
produces evidence a user can actually trust**. Six toolsets — Prophet, Mirror,
Portal, Glutton, Cage, Witness — over a shared core, plus a Python server. Values:
Security, Efficacy, Efficiency, Ingenuity, Simplicity — always **honest-inconclusive
over unearned "secure."**

## Lanes — Copilot owns GUI / CLI surfacing

| Lane | Owner | Copilot may touch |
|---|---|---|
| Reasoning engine (`**/Synthesis/**`, `Conclusion.*`, `Report.*` extractor, golden vectors) | Claude | **No** — render its output, never edit it |
| Server / trust / probes / corpus (`Server/**`, probes, `WitnessSigner`/`LeakDetector`) | Cline | **No** |
| **GUI / CLI surfacing** (`macOS/Sources/SyxxApp/**`, `Windows/SyxxUI/**`, CLI report *layout*) | **Copilot** | **Yes — this is your lane** |

Copilot renders the `Conclusion.stage5` DTO Claude populates (contract:
`STAGE5-CONTRACT.md`). Need a field that isn't in the contract? **Flag it** for
Claude — do not edit `Report.*`/`Conclusion` or reach into the engine. **Never
edit, disable, re-flag, soften, or delete a test or golden vector** — a failing
honesty test is data, not an obstacle.

## Honesty spine (binding, every change, every intermediate commit)

1. **No verdict before evidence.** Never render a verdict, confidence, probability,
   colour, or reassuring label before its inputs are queried, parsed, and validated.
2. **Missing data is not clean data.** Missing / invalid / degraded / timed-out /
   `nil` inputs render as inconclusive/unavailable — never secure/clean/success.
3. **Tri-state, never boolean.** Preserve secure/tampered/inconclusive (and
   success/failure/unknown) end to end; never collapse to a bool.
4. **No fabricated value shown as measured.** Every user-visible number/label traces
   to live evidence, not a mock, default, or placeholder. (Syxx once shipped a
   suite-green build that printed "Success… 0 queries captured… Verdict: 31%
   inconclusive" — never again.)
5. **The bar is a real run with honest output, not green tests.** One real run per
   item, per platform.
6. **Platform parity.** A macOS view change needs its `Syxx.Kit`/WinUI twin.

## Copy is gated

User-visible strings ship only from the **approved recommended set** in
`STAGE5-WORDING-PROPOSALS.md`. Any new wording is a proposal pending approval;
resolve an unknown/non-recommended token **fail-closed** to the approved generic
inconclusive/unavailable string. **Catalog every user-facing string** you add or
render into `outputs-catalog.csv` (`file:line,trigger,module,task,output,change`),
one row per string.

## Reference library

The project's papers are extracted to text at `research/reference-library/`
(`INDEX.md` is the manifest). For any research, threat-model, or citation need,
**grep that folder before external web search** and cite the original PDF filename.
(Copilot reads text, not PDF bytes — the extracted `.txt` is what's searchable.)

## Discipline

Always cite `file:line`. Explicit-path commits only — stage named files and run
`git diff --cached --name-only` before committing; **never `git add -A`**. Anchor
work to an open delta ID / ROADMAP workstream, not free-floating.

## Custom agents

The Syxx pipeline is available as Copilot custom agents in `.github/agents/`
(invoke with `/agent` or the `agent` tool): `syxx-neural-researcher` (investigate)
→ `syxx-neural-engineer` (design) → `syxx-neural-planner` (sequence) →
`syxx-neural-executor` (implement) → `syxx-neural-surfacer` (**your lane** — render
Stage 5). Reusable procedures live in `.github/prompts/` (honesty audit, reference
lookup).
