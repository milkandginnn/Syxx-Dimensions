# Syxx — Subagent Deployment & Management Plan

**Purpose:** Deploy, manage, and task subagents during the planning stage of fixing DELTAS.md deltas.  
**Trigger:** When beginning a delta analysis and implementation cycle.  
**Prerequisite:** IMPLEMENTATION_PLAN.md must be current and contain specific task assignments for each subagent.

---

## Phase 0 — Meta-Orchestration Layer

**Deploy first. These agents coordinate all others. Leave running until goal is complete.**

| # | Subagent | File | Specific Tasks |
|---|----------|------|----------------|
| 1 | **it-ops-orchestrator** | `categories/09-meta-orchestration/it-ops-orchestrator.md` | Coordinate subagent communication between platforms (macOS Swift / Windows .NET) and SDKs. Standardize implementation language. Route cross-domain tasks to correct specialists. |
| 2 | **multi-agent-coordinator** | `categories/09-meta-orchestration/multi-agent-coordinator.md` | Coordinate subagents to ensure they stay on task, do not drift from their goal, and remain productive. Guide them so they do not get stuck in the weeds over artifact typos. Focus instruction on groups of subagents, not individuals. |
| 3 | **performance-monitor** | `categories/09-meta-orchestration/performance-monitor.md` | Work with error-coordinator to oversee implementation and expansion of telemetry system (OTel and Sentry Self-hosted). Specialize in effective, meaningful collection of performance data (resource management, sensor readings). Ensure telemetry data indicates how it is used in the software and why it matters. |
| 4 | **error-coordinator** | `categories/09-meta-orchestration/error-coordinator.md` | Work with performance-monitor to oversee telemetry implementation (OTel and Sentry Self-hosted). Focus on meaningful collection of datapoints for crashes, errors, error handling, warnings, software conflicts. Ensure telemetry almost writes fixes for agents/subagents and that delta analyses are effectively written by the telemetry software itself. |
| 5 | **context-manager** | `categories/09-meta-orchestration/context-manager.md` | Advise subagents on managing their context, when to compact, and how to maintain state across distributed work. |
| 6 | **agent-organizer** | `categories/09-meta-orchestration/agent-organizer.md` | Coordinate instruction for individual agents. Assemble teams, assign roles, manage task decomposition. |
| 7 | **knowledge-synthesizer** | `categories/09-meta-orchestration/knowledge-synthesizer.md` | Coordinate the meaningful flow of information from subagents to the calling agent so that it is clear, complete, and disciplined. |
| 8 | **error-coordinator** (2nd instance) | `categories/09-meta-orchestration/error-coordinator.md` | Oversee implementation with respect to testing and error handling. Ensure tests have adequate fail states and predict intended, useful, and trustworthy app behavior. Ensure agents do not soften tests to support a pass when the app should have failed. |
| 9 | **git-workflow-manager** | `categories/06-developer-experience/git-workflow-manager.md` | Oversee commits, ensure things don't get overwritten, manage branch strategy, coordinate merge order for cross-platform changes. |

---

## Phase 1 — Core Development & Infrastructure Review

**Deploy after Phase 0 agents are running. These agents review code and recommend changes.**

| # | Subagent | File | Specific Tasks |
|---|----------|------|----------------|
| 1 | **websocket-engineer** | `categories/01-core-development/websocket-engineer.md` | (A) Review real-time communication paths relevant to their expertise. (B) Recommend changes to the implementation plan. (C) Scan DELTAS.md for overlooked implementation candidates. |
| 2 | **frontend-developer** | `categories/01-core-development/frontend-developer.md` | (A) Review GUI code (macOS SwiftUI / Windows WinUI 3) relevant to their expertise. (B) Recommend changes to the plan. (C) Scan DELTAS.md for overlooked items. |
| 3 | **backend-developer** | `categories/01-core-development/backend-developer.md` | (A) Review server code (Python/Flask) relevant to their expertise. (B) Recommend changes to the plan. (C) Scan DELTAS.md for overlooked items. |
| 4 | **api-designer** | `categories/01-core-development/api-designer.md` | (A) Review API contracts and endpoints relevant to their expertise. (B) Recommend changes to the plan. (C) Scan DELTAS.md for overlooked items. |
| 5 | **kubernetes-specialist** | `categories/03-infrastructure/kubernetes-specialist.md` | (A) Review deployment/container configuration relevant to their expertise. (B) Recommend changes to the plan. (C) Scan DELTAS.md for overlooked items. |
| 6 | **cli-developer** | `categories/06-developer-experience/cli-developer.md` | (A) Review CLI entry points and command structure relevant to their expertise. (B) Recommend changes to the plan. (C) Scan DELTAS.md for overlooked items. |

---

## Phase 2 — Language Specialist & Platform Parity Review

**Deploy after Phase 1 agents have reported. These agents provide implementation advice and parity analysis.**

| # | Subagent | File | Specific Tasks |
|---|----------|------|----------------|
| 1 | **csharp-developer** | `categories/02-language-specialists/csharp-developer.md` | (A) Review Windows C#/.NET code relevant to their expertise. (B) Advise on implementation approach, complex tasks, and platform parity. (C) Scan DELTAS.md for overlooked implementation candidates. |
| 2 | **python-pro** | `categories/02-language-specialists/python-pro.md` | (A) Review server Python code relevant to their expertise. (B) Advise on implementation approach, complex tasks, and platform parity. (C) Scan DELTAS.md for overlooked items. |
| 3 | **swift-expert** | `categories/02-language-specialists/swift-expert.md` | (A) Review macOS Swift code relevant to their expertise. (B) Advise on implementation approach, complex tasks, and platform parity. (C) Scan DELTAS.md for overlooked items. |
| 4 | **dotnet-framework-4.8-expert** | `categories/02-language-specialists/dotnet-framework-4.8-expert.md` | (A) Review any legacy .NET Framework code relevant to their expertise. (B) Advise on implementation approach and platform parity. (C) Scan DELTAS.md for overlooked items. |
| 5 | **dotnet-core-expert** | `categories/02-language-specialists/dotnet-core-expert.md` | (A) Review modern .NET code relevant to their expertise. (B) Advise on implementation approach and platform parity. (C) Scan DELTAS.md for overlooked items. |
| 6 | **platform-engineer** | `categories/03-infrastructure/platform-engineer.md` | (A) Review platform/deployment configuration relevant to their expertise. (B) Advise on implementation approach. (C) Scan DELTAS.md for overlooked items. |
| 7 | **powershell-module-architect** | `categories/06-developer-experience/powershell-module-architect.md` | (A) Review any PowerShell automation relevant to their expertise. (B) Advise on implementation approach. (C) Scan DELTAS.md for overlooked items. |
| 8 | **prompt-engineer** | `categories/05-data-ai/prompt-engineer.md` | (A) Review any LLM/prompt integration relevant to their expertise. (B) Advise on implementation approach. (C) Scan DELTAS.md for overlooked items. |

---

## Phase 3 — Revision Cycle

**After Phases 1 and 2 report back:**

1. Collect all recommendations from Phase 1 and Phase 2 agents
2. Deduplicate and prioritize recommendations
3. Revise IMPLEMENTATION_PLAN.md based on feedback
4. Flag any conflicts between agent recommendations for user resolution
5. Update DELTAS.md if agents identified overlooked deltas

---

## Phase 4 — Specialized Domain & Business/Product Review

**Deploy after revisions are complete. These agents review domain-specific and business aspects.**

| # | Subagent | File | Specific Tasks |
|---|----------|------|----------------|
| 1 | **seo-specialist** | `categories/07-specialized-domains/seo-specialist.md` | Review SEO implementation for syxx.app. Provide advice for search engine crawling. Obtain "next five steps" to guide user on ensuring site readiness for the public. Ensure SEO strategies are baked into any new web server code. |
| 2 | **payment-integration** | `categories/07-specialized-domains/payment-integration.md` | Review Stripe and Keygen implementations. Ensure everything works as it should. Ensure new code does not create new issues with the payment system. Provide advice on implementing subscriptions through Stripe. |
| 3 | **api-documenter** | `categories/07-specialized-domains/api-documenter.md` | Review API documentation and ensure it is understandable. Advise on (and potentially write) a short "Explain It Like I'm Syxx" section to introduce people to APIs, how they work, what they are used for, how to create a POST submission using a browser or terminal/PowerShell. Include non-developer use cases. |
| 4 | **technical-writer** | `categories/08-business-product/technical-writer.md` | Review all documentation for clarity and accuracy. Update CHANGELOG.md, ROADMAP.md, DESIGN.md as needed. Write user guides for new features. |
| 5 | **legal-advisor** | `categories/08-business-product/legal-advisor.md` | Review telemetry/observability for GDPR/privacy compliance. Review EULA. Advise on consent requirements. Ensure subscription changes comply with consumer protection regulations. |
| 6 | **customer-success-manager** | `categories/08-business-product/customer-success-manager.md` | Review changes for customer impact. Recommend communication strategy for fixes. Ensure payment/subscription fixes don't disrupt existing users. |
| 7 | **content-marketer** | `categories/08-business-product/content-marketer.md` | Review site content for SEO optimization. Recommend content strategy for public launch. Ensure new web server code includes SEO-friendly content structures. |
| 8 | **ux-researcher** | `categories/08-business-product/ux-researcher.md` | Review GUI changes for usability impact. Provide feedback on verdict display changes. Recommend user testing approach for new features. |
| 9 | **sales-engineer** | `categories/08-business-product/sales-engineer.md` | Review Syxx for technical demonstration readiness. Ensure API documentation supports sales use cases. Prepare competitive positioning materials. |
| 10 | **project-manager** | `categories/08-business-product/project-manager.md` | Track milestones. Coordinate rollout timeline. Manage risk. Create deployment checklist and rollback plan. |
| 11 | **product-manager** | `categories/08-business-product/product-manager.md` | Prioritize remaining items by user impact. Approve user-visible text changes per AGENTS.md §4.7. Balance improvements with user experience. |

---

## Deployment Rules

### When to Deploy
- **Phase 0:** Immediately when beginning a delta cycle. These agents coordinate all others.
- **Phase 1:** After Phase 0 agents are running. Core dev agents review code.
- **Phase 2:** After Phase 1 agents report back. Language specialists provide implementation advice.
- **Phase 3:** After Phase 2. Revise plan based on all feedback.
- **Phase 4:** After revisions complete. Domain/business agents review final plan.

### How to Manage
- **Task assignment:** Each agent receives tasks from the table above. Do not improvise tasks outside the table.
- **Drift prevention:** multi-agent-coordinator monitors all agents. If an agent drifts from its assigned tasks, redirect it.
- **Context management:** context-manager advises agents on when to compact context. Agents should not let context grow unbounded.
- **Conflict resolution:** If two agents give conflicting recommendations, escalate to user. Do not resolve architectural disagreements yourself.
- **Completion:** An agent is complete when it has answered all (A), (B), (C) questions in its task row and reported back.

### What to Collect
From each agent, collect:
1. **Code review findings** (A): What did they find in the relevant code?
2. **Plan recommendations** (B): What changes do they suggest to IMPLEMENTATION_PLAN.md?
3. **Overlooked deltas** (C): What items in DELTAS.md should be implemented but were missed?
4. **Conflicts**: Any disagreements with other agents' findings?

### How to Synthesize
After all agents report:
1. Deduplicate findings across agents
2. Categorize: must-fix, should-fix, defer
3. Flag user-visible text changes for approval (AGENTS.md §4.7)
4. Update IMPLEMENTATION_PLAN.md with revised task assignments
5. Update DELTAS.md if new deltas were identified
6. Present summary to user for approval before implementation begins
