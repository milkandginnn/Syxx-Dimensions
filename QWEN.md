# Qwen Code — Agentic Plugin Marketplace Integration

Look in ./Subagents/ for additional available options.

Compliance with rules/* is not optional. You do not have room for judgmnent calls. There are no exceptions.

> 91 plugins installed: **180 skills**, **164 agents**, **145 commands**.
> Generated from [wshobson/agents](https://github.com/wshobson/agents) for all 5 harnesses.

## Installed resources

| Type | Count | Path | Format |
|---|---:|---|---|
| **Skills** | 180 | `skills/<plugin>__<skill>/SKILL.md` | Markdown knowledge packages |
| **Agents** | 164 | `agents/<plugin>__<agent>.md` | Domain-expert agent definitions |
| **Commands** | 145 | `commands/<plugin>/` + `commands/<plugin>.toml` | Workflow definitions |
| **Source plugins** | 91 | `plugins/<name>/` | Original source-of-truth |

Cross-harness generated artifacts (for use in other CLIs from this same project):

| Harness | Path | Files |
|---|---|---:|
| Gemini CLI | `skills/`, `agents/`, `commands/` | 489 |
| OpenCode | `.opencode/{skills,agents,commands}/` | 694 |
| Codex CLI | `.codex/{skills,agents}/` | 761 |
| Copilot | `.copilot/{agents,skills,commands}/` | 641 |
| Cursor | `.cursor/rules/`, `.cursor-plugin/` | 96 |

## Using skills

When you need domain expertise for a task, read the relevant skill:

```
read_file skills/<plugin>__<skill>/SKILL.md
```

Key skills by domain:

- **Backend**: `backend-development__architecture-patterns`, `backend-development__microservices-patterns`
- **Security**: `security-scanning__security-sast`, `backend-api-security` (plugin)
- **Testing**: `tdd-workflows__tdd-cycle`, `unit-testing__test-generate`
- **DevOps**: `cicd-automation__workflow-automate`, `kubernetes-operations` (plugin)
- **Code quality**: `code-refactoring__refactor-clean`, `comprehensive-review__full-review`
- **Debugging**: `error-debugging__error-analysis`, `debugging-toolkit` (plugin)
- **Documentation**: `documentation-generation__doc-generate`, `code-documentation__code-explain`
- **Frontend**: `ui-design__responsive-design`, `frontend-mobile-development__component-scaffold`
- **ML/AI**: `llm-finetuning` (8 skills), `machine-learning-ops__ml-pipeline-workflow`
- **Infrastructure**: `cloud-infrastructure` (plugin), `deployment-strategies` (plugin)

## Using agents

Delegate specialized work via the `agent` tool with the agent definition as context:

```
agent(prompt="...", subagent_type="general-purpose")
```

Read the agent definition first to get its specialized instructions:

```
read_file agents/<plugin>__<agent>.md
```

Key agents by domain:

- **Architecture**: `backend-development__backend-architect`, `c4-architecture` (plugin)
- **Code review**: `comprehensive-review` (plugin), `incident-response__code-reviewer`
- **Debugging**: `error-debugging__error-trace`, `incident-response__debugger`
- **Security**: `backend-development__security-auditor`, `security-scanning` (plugin)
- **Testing**: `backend-development__tdd-orchestrator`, `backend-development__test-automator`
- **Performance**: `application-performance__performance-engineer`
- **Teams**: `agent-teams__team-lead`, `agent-teams__parallel-feature-development`
- **DevOps**: `kubernetes-operations` (plugin), `cicd-automation` (plugin)
- **ML**: `machine-learning-ops__ml-engineer`, `llm-finetuning` (plugin)
- **Full-stack**: `full-stack-orchestration` (plugin), `conductor` (plugin)

## Using commands

Commands define multi-step workflows. Gemini-generated TOML commands reference source plugin markdown:

```
read_file commands/<plugin>/<command>.toml    # workflow definition + @{} source refs
read_file plugins/<plugin>/commands/<command>.md  # full source protocol
```

Key commands (under `commands/<plugin>/`):

- `/incident-response` — multi-agent incident triage (`incident-response/incident-response.toml`)
- `/smart-fix` — automated fix suggestions (`incident-response/smart-fix.toml`)
- `/full-review` — comprehensive code review (`comprehensive-review/full-review.toml`)
- `/pr-enhance` — PR enhancement workflow (`comprehensive-review/pr-enhance.toml`)
- `/security-sast` — static analysis scan (`security-scanning/security-sast.toml`)
- `/security-dependencies` — dependency audit (`security-scanning/security-dependencies.toml`)
- `/security-hardening` — hardening workflow (`security-scanning/security-hardening.toml`)
- `/tdd-cycle` — test-driven development cycle (`tdd-workflows/tdd-cycle.toml`)
- `/test-generate` — generate tests for code (`unit-testing/test-generate.toml`)
- `/ml-pipeline` — ML pipeline setup (`machine-learning-ops/ml-pipeline.toml`)
- `/monitor-setup` — observability setup (`observability-monitoring/monitor-setup.toml`)
- `/doc-generate` — documentation generation (`documentation-generation/doc-generate.toml`)

## Plugin catalog (91 plugins)

**Architecture & Design**: c4-architecture, database-design, ui-design, brand-landingpage
**Backend**: backend-development, backend-api-security, api-scaffolding, api-testing-observability
**Frontend/Mobile**: frontend-mobile-development, frontend-mobile-security, multi-platform-apps
**Languages**: python-development, javascript-typescript, jvm-languages, julia-development, functional-programming, systems-programming, shell-scripting, dotnet-contribution
**Testing & Quality**: tdd-workflows, unit-testing, comprehensive-review, performance-testing-review, code-refactoring, codebase-cleanup, code-documentation
**Security**: security-scanning, security-compliance, reverse-engineering, protect-mcp, signed-audit-trails
**DevOps & Infra**: cicd-automation, cloud-infrastructure, kubernetes-operations, deployment-strategies, deployment-validation, observability-monitoring, application-performance
**Data & ML**: data-engineering, data-validation-suite, machine-learning-ops, llm-application-dev, llm-finetuning, dgx-spark-ops
**Debugging**: debugging-toolkit, error-debugging, error-diagnostics, distributed-debugging
**Business**: startup-business-analyst, business-analytics, customer-sales-automation, hr-legal-compliance, payment-processing, quantitative-trading
**Documentation**: documentation-generation, documentation-standards
**Git & Collaboration**: git-pr-workflows, team-collaboration, agent-teams, agent-orchestration, conductor
**SEO & Content**: seo-analysis-monitoring, seo-content-creation, seo-technical-optimization, content-marketing, social-publishing
**Specialized**: blockchain-web3, game-development, arm-cortex-microcontrollers, meigen-ai-design, pptx-deck-creation, file-conversion, hermes-tweet, runapi-mcp, ship-mate
**Meta**: plugin-eval, review-agent-governance, skill-forge-essentials, context-management, developer-essentials, operating-kit, before-you-build, block-no-verify, framework-migration, dependency-management, database-migrations, database-cloud-optimization, accessibility-compliance, incident-response, full-stack-orchestration

## Regenerating artifacts

```bash
cd /Users/masterrabbit/Software/Syxx
uv run --project plugins/plugin-eval python tools/generate.py --harness <gemini|opencode|codex|copilot|cursor> --all
```
