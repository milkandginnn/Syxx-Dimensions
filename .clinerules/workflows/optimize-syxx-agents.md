# Optimize Syxx agents

**Purpose:** update and optimize your already-installed Cline agents/subagents
(custom modes) and their supporting rules, workflows, and MCP wiring so they match
the *current* Syxx doctrine and your lane. You are editing your own configuration —
Cline can read and rewrite these files. Do this end to end; show diffs before
applying anything under `~/Documents/Cline/`.

## Where your config lives

- **Agents/subagents (custom modes):** `.bob/custom_modes.yaml` in this repo — 12
  modes, including `syxx-researcher`, `syxx-planner`, `syxx-engineer`,
  `syxx-gui-architect`, plus generic `plan`/`research`/`debug`/`feature`/`implement`
  and the UI planner/design modes. This is IBM Bob's format; use it while it's
  compatible with your runtime.
- **Global config:** `~/Documents/Cline/` — `Rules/`, `Workflows/`, `Hooks/`
  (currently empty), and `MCP/` (three servers already built:
  `syxx-dns-server`, `syxx-sqlite-server`, `syxx-http-server`).
- **Workspace rules/workflows:** `.clinerules/` in this repo (the honesty-spine rule
  and this workflow live here).

## 1. Read the current picture first

Read, in order: `PROJECT-STATE.md` (canonical current state + lanes + what's
decided — do not relitigate), `AGENTS.md` (binding rules — never edit it),
`.clinerules/syxx-honesty-spine.md`, `HANDOFF-CLINE.md` (your lane: CL-1 signed
GroundTruth, CL-2 probe robustness, CL-3 corpus + runner), then `REASONING-SYSTEM-
DESIGN.md`, `DELTAS.md`, `ROADMAP.md`, `DESIGN.md`. Do not optimize against stale
assumptions.

## 2. Update each Syxx mode to current doctrine

For `syxx-researcher`, `syxx-planner`, `syxx-engineer`, `syxx-gui-architect` in
`.bob/custom_modes.yaml`, revise `roleDefinition` / `customInstructions` /
`whenToUse` so each:

- Encodes the **honesty spine** verbatim in intent: no verdict before evidence;
  missing data is not clean data; **tri-state, never boolean**; no fabricated value
  shown as measured; the bar is a real run with honest output, not green tests;
  platform parity (`SyxxKit` ⇔ `Syxx.Kit`, `Server/` where trust material is
  involved).
- Names your **lane** and its boundaries: Cline owns `Server/**`, trust/attestation
  consumers, probes, and the corpus; **never** touches `**/Synthesis/**`, the
  `Report.*` extractor shape, or `REASONING-SYSTEM-DESIGN.md` (Claude's engine).
- Keeps **read-only modes read-only** (research: `groups: [read, ...]`, no `edit`);
  gives implementer modes `edit`/`command` but forbids editing `AGENTS.md` and any
  test/golden file to force a green.
- Requires **`file:line`**, **explicit-path commits** (never `git add -A`), and the
  **`outputs-catalog.csv`** row for every user-facing string.
- Points at the **reference library** (`research/reference-library/`, grep before
  web) and this repo's `.clinerules`.

Align `groups` to each mode's real needs (`read`, `edit`, `command`, `mcp`,
`skill`, `subagent`, `mode`) — least privilege. Keep IBM Bob's schema
(`slug`/`name`/`roleDefinition`/`whenToUse`/`customInstructions`/`groups`) intact.

## 3. Optimize for routing and non-overlap

- **De-duplicate** the near-twins so auto-routing is unambiguous: the generic
  `plan`/`research`/`implement` modes vs the `syxx-*` modes. Either fold the Syxx
  specifics into the generic modes' `customInstructions`, or tighten each
  `whenToUse` so a request routes to exactly one. State the choice in your diff.
- Sharpen every `description`/`whenToUse` to a crisp when-to-use — that string is
  what selects the mode.
- Ensure a clean pipeline: research → engineer (design) → planner (sequence) →
  implement, with `syxx-gui-architect` scoped to surfacing (note: GUI/CLI surfacing
  is **Copilot's** lane in the three-way split — keep `syxx-gui-architect` for your
  own server/CLI output only, not the app views).

## 4. Wire the MCP servers into your lane

Confirm the three servers are registered in Cline's MCP settings and build if
needed (`npm run build` in each `~/Documents/Cline/MCP/<server>`). Reference their
tools in the relevant modes so they gather **real evidence**, not code guesses:

- **syxx-dns** (`dns_query`, `dns_doh_query`) → CL-2 probe work (NXDOMAIN/DNSSEC/
  wildcard robustness), multi-vantage checks.
- **syxx-sqlite** (`sqlite_query`, `syxx_db_stats`, `syxx_db_inspect_*`) → CL-3
  corpus inspection, leak-token/passive-DNS/audit verification.
- **syxx-http** (`http_request`, `syxx_server_health`, `syxx_api_*`) → CL-1 signed
  GroundTruth + server endpoint testing.

Add `mcp` to the `groups` of any mode that should call them.

## 5. Sync rules/workflows (optional, global)

The workspace `.clinerules/` here carries the honesty-spine rule and the
`reference-lookup` workflow. If you want them across all projects, copy the
generic parts into `~/Documents/Cline/Rules/` and `~/Documents/Cline/Workflows/`
— but keep Syxx-specific lane content workspace-scoped, not global.

## 6. Verify

- Diff every changed mode; confirm no mode can emit clean/secure on missing
  evidence, none collapses tri-state to a bool, and none can edit the engine lane
  or `AGENTS.md`.
- Do one **real run** per lane item (CL-1/CL-2/CL-3) using the MCP tools and show
  the honest output — not just that a mode loads.
- Update `CHANGELOG.md` with what you changed; do not stop for anything else.

Report a short summary: which modes changed, which were de-duplicated, MCP wiring
confirmed, and the real runs you observed.
