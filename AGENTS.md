## AGENTS.md
Production-ready agentic workflows: 94 plugins (90 local + 4 external), 164 agents, 175 skills, 103 commands. Canonical source-of-truth for Claude Code/Desktop, ChatGPT/Codex, Cursor, OpenCode (Design/Desktop), Gemini CLI, Antigravity IDE, Cline, Qwen Code, Devin, Copilot, and IBM Bob IDE/Shell.
This canonical context file is read directly by Codex, Cursor, OpenCode, Qwen, and Cline. Claude Code accesses it via symlink (CLAUDE.md). Gemini CLI reads it via gemini-extension.json (contextFileName).

Read as Table of Contents. Detail in docs/. Conventions in docs/authoring.md. Harness deltas in docs/harnesses.md. Hard cap: ~215 lines (authorized 2026-08-11 to include Syxx App). Push granular detail to docs/.

## IDE/Tool-Specific Enforcement
If your specific runtime environment, configuration paths, or tool specs are missing from the sections below, update the text to accurately reflect your operational requirements. Do not edit for other purposes unless explicitly authorized.

## Mandatory Rules

* SUBAGENT-RULES.md: Core subagent behavior, orchestration, and task mapping (44 sections).
* IMPLEMENTATION-RULES.md: Planning execution requirements and granular code structures.
* SKILL-RULES.md: Rules for onloading/unloading project-specific skills.
* WORKFLOW-RULES.md: Rules for using and deploying automated workflows.
* PLUGIN-RULES.md: Rules for managing, creating, and isolating runtime plugins.
* VISUAL-DESIGN-RULES.md: UI/UX engineering, Dimensions design system, and GUI separation.
* COMMUNICATION-RULES.md: Brevity mandates, caps on explanations, and token conservation.

## Project Map

* ARCHITECTURE.md: Adapter framework, source-of-truth invariants, and capability matrices.
* docs/architecture.md: Core low-level architectural design principles.
* docs/plugins.md: Full plugin catalog segmented by operational category.
* docs/agents.md: Agent reference profiles and assigned LLM model tiers.
* docs/agent-skills.md: Progressive disclosure models for skill execution.
* docs/usage.md: CLI command signatures, workflow examples, and operational recipes.
* docs/harnesses.md: Cross-harness compatibility, setup details, and capability deltas.
* docs/plugin-eval.md: Three-layer quality evaluation verification framework.
* docs/DESIGN.md: Syxx.app functional design specifications and core CLI packaging.
* CONTRIBUTING.md: Repository contribution and PR validation flows.

## Environment Rules

* Tooling ecosystem: Enforce uv (packages), ruff (lint/format), and ty (typing). Never use pip, mypy, or black.
* Plugin paths: Place under plugins/<name>/ with lowercase, hyphen-separated titles. Double underscores __ are forbidden.
* Safety guardrails: Never commit secrets. Hard bans on destructive git commands (force-push, reset --hard, branch -D). [1, 2] 

## Quality Gates (Pre-Push Checklist)

make validate STRICT=1     # Structural validation across all harness outputs
make garden                # Drift detection (dead links, stale artifacts, oversize skills)
make test                  # Full pytest suite (plugin-eval + tools/tests/)
make smoke-test            # Real-CLI subprocess tests against generated artifacts

CI (.github/workflows/validate.yml) runs all four on every PR.
## Cross-Harness Code Generation

make generate HARNESS=codex      # Rebuilds .codex/ (ChatGPT/Codex)
make generate HARNESS=cursor     # Rebuilds .cursor/rules/ & .cursor-plugin/ (Cursor, Visual Code)
make generate HARNESS=opencode    # Rebuilds .opencode/ (OpenCode, OpenCode Design, Desktop)
make generate HARNESS=gemini      # Rebuilds root skills/ & ~/.gemini/antigravity/ (Gemini CLI, Antigravity)
make generate HARNESS=devin       # Rebuilds .devin/ playbooks (Devin)
make generate HARNESS=bob         # Rebuilds .bob/rules/ inside ~/.bob, ~/.bobide, ./.bob, and ~/.bobide (IBM Bob)
make generate HARNESS=clis        # Rebuilds flags for Claude Code, Claude Desktop, Cline, Copilot CLI, Qwen Code (qwen.md)
make generate-all                 # Rebuilds all fourteen environment outputs concurrently


* Generated artifacts are committed for native URL pulls. Source-of-truth lives in plugins/. Do not edit generated output.

## Harness Synchronization Matrices## Skills Model
175 skills hosted under plugins/*/skills/<n>/SKILL.md:

* Native Markdown (Claude Code, Cursor, Cline, Visual Code): Reads .claude/skills/ paths, .cursorrules, or native Anthropic specs directly.
* Mirrored Schema (ChatGPT/Codex): Transpiled directly into structural .codex/skills/ maps with a rigid 8 KB payload body cap.
* Rule-Based Workspace Directories (IBM Bob IDE, Bob Shell): Compiled into root-level .bob/rules/ and .bob/rules-code/ configuration markers.
* Flat Hyphenated Hooks (OpenCode, OpenCode Design, OpenCode Desktop): Mirrored to global workspace scopes using flat, hyphenated plugin-skill naming.
* Autonomous Ingestion (Devin, Qwen Code, Claude Desktop, Copilot CLI, Gemini CLI, Antigravity IDE): Ingested straight into local tool memory, qwen.md context strings, custom task playbooks, or app data paths like ~/.gemini/antigravity/.

## Subagents Model
203 subagents configured under plugins/*/agents/<name>.md:

* Codex (ChatGPT/Codex): Transpiles to TOML; strips tools: and forces mapping to the advanced GPT-5.x baseline.
* Workspace Isolation (OpenCode, OpenCode Design, OpenCode Desktop, IBM Bob IDE, Bob Shell): Embeds explicit security permission: blocks or file-level ignore parameters to deny subagent access on locked structural components.
* Native/Playbook (Claude Code, Cursor, Cline, Visual Code, Devin, Qwen Code, Claude Desktop, Copilot CLI, Gemini CLI, Antigravity IDE): Consumes platform-specific markdown specs natively inside running agent runtime directories, active sandboxes, or task-oriented execution playbooks.

------------------------------
## Syxx.app — the application
Cross-platform network-integrity tool identifying tampering, spoofing, and censorship across six disciplines feeding the Conclusions module. Detail: docs/DESIGN.md.

| Toolset | Diagnostic Domain and Scope |
|---|---|
| Prophet | Resolves real-time DNS consensus anomalies across 12 distinct regional providers (MEMORY.md:1927). |
| Mirror | Network-wide tamper detection; exposes the Warp content-integrity pipeline. |
| Portal | Local routing diagnostics running localized paths via syxx key, syxx route, and syxx lan. |
| Glutton | Exposes private transport data leaks and identifies vulnerable running VPN nodes via syxx leak. |
| Cage | Low-level kernel DNS query/packet capture operations; requires admin elevation. |
| Witness | Validates identities using cryptographically backed Ed25519 attestation signatures. |

## Source Execution Repositories

* macOS: Native Swift/SwiftUI codebase located inside macOS/ (SyxxKit + SyxxApp).
* Windows: Modern desktop implementation written using .NET/WinUI 3 under Windows/dotNET/.