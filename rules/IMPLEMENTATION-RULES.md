# Implementation Rules


## 1. Authority and Scope


This document governs implementation planning, execution, validation, continuity, and Git workflow.


`SUBAGENT-RULES.md` governs subagent behavior, authority, communication, delegation, and safety. Do not duplicate or override it here.


Apply rules in this order:


1. Repository-specific instructions, when stricter or more specific.
2. `SUBAGENT-RULES.md` for subagent conduct and coordination.
3. This document for planning, implementation, validation, continuity, and source control.


Escalate unresolved conflicts to the orchestrator before proceeding.


These rules exist to produce implementation that is specialist-informed, observable, testable, recoverable after interruption, and complete.


## 2. Token and Context Budget


Treat context as a finite engineering resource. Load enough information to make correct decisions, but do not load, repeat, or forward unnecessary material.


- Read only the sections relevant to the current task tier and workstream.
- Use repository paths, line ranges, task IDs, concise summaries, and references to canonical documents.
- Do not copy full policies, raw logs, long transcripts, complete diffs, or unrelated plans into every subagent brief.
- Give each subagent only the context required for its assigned responsibility.
- Use an information synthesist or orchestrator to summarize findings for downstream agents.
- Load detailed templates, examples, research, framework documentation, or skill resources only when needed.
- Do not omit required safety, security, UI, accessibility, telemetry, or validation information merely to reduce tokens.


Plans must be detailed enough to implement safely, but must not duplicate source code or repository documentation that can be referenced directly.


## 3. Task Classification


Classify every task before implementation. When uncertain, classify upward.


| Tier | Scope | Minimum process |
|---|---|---|
| Tier 0 — Trivial | Isolated non-UI correction with no meaningful behavioral, API, architectural, operational, or user-facing impact | Inspect, implement, run the most relevant targeted check |
| Tier 1 — Small | Narrow non-UI bug fix, test addition, documentation update, or limited internal refactor | Brief plan, targeted validation, coherent commit |
| Tier 2 — Standard | Multi-file non-UI work, public API change, shared-code change, dependency update, backend workflow, telemetry change, or meaningful operational change | Detailed plan, selected specialists, validation plan, continuity updates |
| Tier 3 — Complex | **Any UI work**, multi-agent work, parallel edits, migrations, security/privacy-sensitive work, multi-service work, cross-platform work, or performance-critical work | Orchestrator, specialist team, detailed plan, explicit sequencing, consultation, integration validation, continuity artifacts |


Any UI change is Tier 3, including text, styling, layout, navigation, a single control, or a one-file change. UI work can affect accessibility, state, rendering, telemetry, automation, localization, platform conventions, and user trust.


## 4. Planning Requirements


All work above Tier 0 requires a written plan before broad implementation begins.


A plan must be implementation-ready. Statements such as “update the UI,” “add tests,” or “integrate the SDK” are insufficient without affected scope, ownership, approach, and validation.


### Tier 1 plan


Include:


- Objective and expected outcome.
- Affected files or components.
- Implementation approach.
- Targeted validation.
- Commit boundary.


### Tier 2 plan


Include all Tier 1 requirements, plus:


- Ordered tasks, dependencies, and ownership.
- Required specialist advice and decisions.
- Relevant API, data, concurrency, security, telemetry, performance, and testing implications.
- Build, format, lint, static-analysis, test, and integration commands.
- Required `TODO`, `CHANGELOG.md`, and `MEMORY.md` updates.


### Tier 3 plan


Include all Tier 2 requirements, plus:


- Mandatory orchestrator.
- Information synthesist when more than two agents contribute.
- Workstream, interface, and file ownership.
- Specialist selection, sequencing, and consultation paths.
- Parallel-work boundaries and integration points.
- Temporary handoff requirements where collision risk exists.
- Full validation ladder and integration acceptance criteria.
- UI, accessibility, framework, platform, telemetry, performance, and automation requirements when UI is involved.


### Required plan contents


When applicable, plans must identify:


- Objective, scope, constraints, assumptions, non-goals, and acceptance criteria.
- Projects, modules, files, interfaces, contracts, configuration, and tests affected.
- Tools, skills, plugins, SDKs, frameworks, environments, and workflows required.
- Documentation, TODO, handoff, commit, and push obligations.
- Blockers, approval gates, rollback, and recovery procedures.


### Code exemplars and pseudocode


Include pseudocode, interfaces, state models, event schemas, data-flow descriptions, or command examples only when they reduce meaningful ambiguity.


They are normally required for:


- Algorithms and non-obvious rules.
- Concurrency, cancellation, retries, caching, and recovery.
- Public APIs, SDK integrations, contracts, migrations, and persistence.
- UI states, navigation, and lifecycle behavior.
- Security-sensitive authorization or validation flows.
- Privacy-sensitive telemetry.
- New framework or platform integrations.


Do not copy routine code into plans when file references and implementation steps are clearer.


### Command batching


Bundle approximately 10–25 independent, related, safe command requests when practical.


Suitable batches include repository inspection, search, formatting, linting, static analysis, targeted tests, builds, and Git-status checks.


Do not batch destructive, production-affecting, migration-applying, deployment, order-dependent, interactive, or difficult-to-diagnose commands.


## 5. Orchestration and Specialist Use


An orchestrator is mandatory for Tier 3 work. It is recommended for Tier 2 work involving multiple specialties, interfaces, repositories, or workstreams.


The orchestrator owns:


- Task decomposition, ownership, dependencies, sequencing, and integration.
- Subagent selection and compliance with `SUBAGENT-RULES.md`.
- File and interface ownership boundaries.
- Specialist consultation before irreversible decisions.
- Master TODO, decisions, blockers, and escalation state.
- Prevention of duplicate, conflicting, or unintegrated work.
- Validation, documentation, commits, pushes, CI follow-up, and final acceptance.


The orchestrator may delegate work, but not responsibility for coordination or integration.


Select specialists according to actual risk and technical scope.


| Role | Responsibility |
|---|---|
| Primary implementor | Owns assigned code, tests, documentation, validation, and status reporting |
| Information synthesist | Consolidates findings, decisions, risks, dependencies, and status |
| Research analyst | Answers focused technical questions with evidence, recommendations, limits, and validation implications |
| Language specialist | Advises on language semantics, types, concurrency, memory, errors, packages, and tests |
| SDK/framework specialist | Advises on supported APIs, lifecycle, compatibility, integration, and migration |
| GUI specialist | Advises on UI architecture, state, navigation, accessibility, rendering, and platform conventions |
| Telemetry/performance specialist | Defines metrics, traces, event schemas, profiling, budgets, privacy controls, and regression checks |
| API/data-contract specialist | Advises on contracts, compatibility, persistence, validation, caching, migrations, and contract tests |
| Security/privacy specialist | Reviews authorization, input handling, secrets, sensitive data, logging, dependencies, and privacy risk |
| Test/quality specialist | Defines automated and manual validation, fixtures, regressions, and acceptance criteria |
| Build/CI specialist | Advises on builds, packaging, CI, reproducibility, release checks, and failures |
| Documentation specialist | Maintains user, developer, architecture, operational, release, and migration documentation |


Do not use a generalist as the sole authority for a specialist domain when an appropriate specialist can be consulted.


## 6. Cross-Agent Consultation


Subagents must consult one another when a decision affects another specialty, subject to `SUBAGENT-RULES.md`.


Consultation is required for changes affecting:


- Framework or platform behavior.
- UI state, navigation, rendering, responsiveness, or accessibility.
- API compatibility, schemas, contracts, migrations, or persistence.
- Authentication, authorization, privacy, security, or sensitive data.
- Telemetry, performance budgets, profiling, logs, metrics, or traces.
- Test strategy, CI, packaging, deployment, or release behavior.


Use this sequence:


1. The implementor asks a focused question, including context and the decision required.
2. The specialist or research analyst returns a recommendation, evidence, limitations, and validation implications.
3. The orchestrator or synthesist records the decision and affected work.
4. The orchestrator updates tasks, dependencies, and TODO state if needed.
5. The implementor proceeds according to the documented decision.


Escalate uncertainty. Do not silently assume behavior owned by another specialty.


## 7. Mandatory UI Governance


All UI work is Tier 3.


No UI implementation may begin until the orchestrator has approved an implementation-ready plan, assigned ownership, selected specialists, and established sequencing.


At minimum, a UI workstream requires:


- An orchestrator.
- A primary UI implementor.
- A relevant UI-framework and platform specialist.
- A language and SDK/framework specialist when those are distinct responsibilities.
- An accessibility specialist or designated accessibility reviewer.
- A telemetry/performance specialist.
- A test/quality specialist.
- A research analyst when platform, SDK, repository, or implementation questions require investigation.
- An information synthesist when more than two agents contribute.


The UI plan must cover, where applicable:


- Framework, SDK, operating-system, device, display, and input assumptions.
- Component hierarchy, reusable components, ownership boundaries, and state ownership.
- State transitions, restoration, cancellation, lifecycle, loading, errors, retries, offline behavior, permissions, and partial success.
- Navigation, deep links, back behavior, restoration, modal behavior, and dismissal.
- Responsive layout, resizing, orientation, text scaling, display scaling, and window behavior.
- Keyboard, pointer, touch, focus order, focus restoration, and shortcuts.
- Screen-reader semantics, labels, hints, contrast, localization, font scaling, and reduced motion.
- Rendering, memory, startup, animation, responsiveness, and large-content performance.
- Privacy-safe telemetry, failures, latency, and user-journey observability.
- Unit tests, integration tests, UI automation, accessibility checks, manual visual review, and performance validation.


## 8. Research and Capability Inventory


Research requests must state:


- The exact decision required.
- Relevant language, framework, SDK, version, platform, repository, and code context.
- Constraints such as compatibility, privacy, offline operation, latency, accessibility, deployment, or migration.
- The expected output: recommendation, example, compatibility analysis, migration path, risk assessment, or validation approach.


Research responses must provide:


- A direct recommendation.
- Relevant evidence, source material, repository findings, or API examples.
- Limitations and compatibility constraints.
- Rejected alternatives and rationale.
- Implementation and validation implications.


Tier 2 and Tier 3 plans must identify the tools and workflows required for the task, including relevant:


- Repository inspection and code search.
- Package, dependency, build, test, formatting, lint, analysis, and security tools.
- Debugging, profiling, logging, tracing, metrics, and telemetry validation.
- Simulators, emulators, devices, accessibility inspectors, and UI automation.
- Git, GitHub, CI, issue tracking, code review, and deployment tooling.
- Available plugins, extensions, MCP servers, browser automation, SDK documentation, and code intelligence.


Do not merely list tools. State how each selected capability will be used.


## 9. Continuity and Documentation


Continue work until completion criteria are met. Do not stop after a subtask, successful compilation, isolated passing test, specialist reply, or commit if required cleanup, validation, documentation, push, or CI work remains.


When blocked:


1. Record the blocker, impact, owner, and next action in the TODO.
2. Record restart context in `MEMORY.md`.
3. Escalate to the correct specialist, researcher, or orchestrator.
4. Continue only safe independent work that does not create conflict or rework.
5. Resume promptly when the dependency is resolved.


### TODO


Maintain a self-updating TODO for Tier 2 and Tier 3 work, using the repository’s established task system where available.


Each item must include:


```text
Task ID
Status
Owner
Dependencies
Current state
Next action
Blocker and escalation target
Validation status
Commit and push status
```


Update it before implementation; after scope, dependency, or ownership changes; at meaningful milestones; before handoff or interruption; and after validation, commit, or push actions.


### CHANGELOG.md


Update `CHANGELOG.md` for user-visible, release-relevant, API, migration, security, compatibility, or meaningful performance changes.


Follow repository conventions. If none exist, use an `Unreleased` section with appropriate categories:


```text
Added
Changed
Fixed
Deprecated
Removed
Security
Performance
```


### MEMORY.md


Maintain concise restart and handoff context. At relevant checkpoints, record:


- Objective and task tier.
- Completed, active, blocked, and remaining work.
- Key decisions and rationale.
- Agent ownership and active handoffs.
- Changed files and interfaces.
- Commands run, results, failures, warnings, and limitations.
- Pending questions and escalation targets.
- Current branch, commit, push, and CI state.
- Environment-specific resume instructions.


Never record secrets, credentials, private keys, personally identifiable information, or sensitive production data.


## 10. Parallel Work and Handoffs


Create a temporary handoff document only when parallel work, shared files, shared interfaces, or integration timing creates meaningful collision risk.


Suggested names:


```text
.agent-handoff-<workstream>.md
.tmp-handoff-<task-id>.md
```


A handoff must include:


- Owner, participants, scope, and task IDs.
- Files and interfaces owned or requiring coordination.
- Current status, decisions, assumptions, and dependencies.
- Open questions and escalation targets.
- Completed and pending validation.
- Integration instructions and deletion condition.


Transfer durable information to repository documentation, TODO, or `MEMORY.md`. Delete the temporary handoff after successful integration and validation.


## 11. Validation


No task is complete until validated at the level appropriate to its tier and risk.


The validation plan may include:


1. Formatting, linting, static analysis, type checking, and dependency checks.
2. Targeted unit tests.
3. Integration, contract, migration, UI, end-to-end, or regression tests.
4. Build, package, signing, or deployment-artifact verification.
5. Manual UI, accessibility, performance, telemetry, platform, or difficult-to-automate verification.
6. Security, privacy, authorization, and sensitive-data review.
7. Telemetry and trace verification.
8. Profiling or benchmarks where relevant.
9. Final diff review for scope, regressions, generated artifacts, and sensitive data.
10. TODO, changelog, memory, documentation, and handoff cleanup.


Tier 3 UI validation must include targeted UI automation, accessibility review, manual visual review, and relevant performance validation.


Record material validation results in `MEMORY.md` and report them to the orchestrator.


A successful build is necessary when applicable, but never sufficient by itself.


## 12. Git and GitHub


### Commits


Create a commit when a coherent, reviewable, validated unit is complete.


Good commit boundaries include:


- An implementation slice with tests.
- A verified migration.
- A UI flow with automation and accessibility validation.
- A focused refactor with regression coverage.
- A validated build or CI change.
- A complete documentation update.


Do not commit:


- Secrets, credentials, keys, sensitive data, or local environment files.
- Unrelated changes in the same commit.
- Stale temporary handoff files.
- Work represented as complete without required validation.
- Known-broken work, except an orchestrator-authorized recovery checkpoint.


Follow repository commit-message conventions. If none exist:


```text
feat(account): add retryable summary loading state
fix(api): preserve cancellation during request retries
test(profile): cover failure recovery flow
docs(memory): record account summary decisions
```


### Automatic pushes


Push automatically only when:


- Required validation passes.
- The target remote, branch, and commit are known.
- Repository policy and branch protections allow the push.
- TODO, changelog, memory, and cleanup obligations are current.
- No secrets, prohibited artifacts, or unreviewed sensitive changes exist.
- No human approval gate is required.


Push promptly after a validated checkpoint when interruption risk is high, CI visibility is needed, dependent workstreams need the change, or the completed work should be preserved remotely.


Do not automatically push when:


- The user requested local-only work.
- The remote or branch is unclear.
- Validation is incomplete.
- Review or approval is required.
- The work affects production infrastructure, releases, billing, authentication, security controls, regulated systems, or other approval-gated areas.
- The push would violate `SUBAGENT-RULES.md` or repository policy.


After pushing:


1. Confirm remote branch and commit ID.
2. Check available CI status.
3. Update TODO and `MEMORY.md`.
4. Inform the orchestrator and affected agents.


## 13. Completion Criteria


Work is complete only when applicable requirements are met:


- The plan and acceptance criteria are satisfied.
- Required specialist consultation and research are complete.
- Blockers are resolved or formally documented.
- Code is formatted, analyzed, tested, and built as required.
- Required UI, accessibility, telemetry, performance, security, privacy, migration, and integration validation is complete.
- The final diff is reviewed for correctness, scope, unintended changes, and sensitive data.
- TODO, `CHANGELOG.md`, and `MEMORY.md` are current.
- Temporary handoffs are removed or intentionally retained with documented justification.
- Required commits exist.
- Permitted required pushes and CI follow-up are complete.
- The orchestrator accepts Tier 3 integration.


No agent may declare completion based solely on code changes, compilation, or one passing test.


## 14. Plugins and Workflows


A coded plugin or workflow may impose mandatory behavior that conflicts with this document. For example, a hardcoded review workflow may require a fixed number of subagents or predetermined roles.


When such a conflict exists:


- Follow the coded plugin or workflow for the specific conflicting requirement.
- Continue to follow all non-conflicting requirements in this document.
- Do not treat the plugin as permission to bypass unrelated safety, validation, documentation, or repository rules.


All uncoded plugins and workflows must comply fully with this document.


This document establishes a minimum standard:


- If an uncoded plugin is more permissive, follow this document.
- If any plugin or workflow is stricter, follow the stricter requirement.
- If the conflict or implementation status of a plugin is unclear, pause the affected workstream and escalate to the orchestrator.
