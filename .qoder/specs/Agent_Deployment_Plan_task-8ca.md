# Syxx Agent Deployment Plan

## Context

The project has 224 open deltas (97 MUST FIX), 10 v1 features blocking ship, 7 better-harness infrastructure findings, and 3 platforms requiring parity. 84 custom agents exist but all fail the Qoder-required name field. 22+ skills are available across `.agents/skills/`, `.claude/skills/`, and `skills/`. The existing `SUBAGENT_DEPLOYMENT_PLAN.md` is delta-analysis focused and references stale category paths; this plan supersedes it for the current cycle.

**Binding constraints (AGENTS.md):** GUI > CLI. Tri-state never boolean. No verdict before evidence. Real run beats green tests. Platform parity is a deliverable. Output requires user approval. Skills must be applied before starting work.

---

## Phase 0: Harness Repair (deploy first, complete before any other phase)

**Goal:** Fix the 7 better-harness findings so the agent infrastructure actually works before deploying specialists.

**Coordinator:** The calling agent (you) directly. No subagents needed for this phase.

### 0.1 Fix all 84 custom agent name fields
- **What:** Add `name:` to frontmatter of every `.qoder/agents/*.md` file, matching the filename stem.
- **Why:** Without the name field, Qoder cannot route or discover agents. Every subsequent phase depends on agents being invocable.
- **Skill to equip:** `syxx-agent-development` (`.claude/skills/syxx-agent-development/SKILL.md`)
- **Validation:** Run `better-harness coding-agent-practices asset-baseline qoder --workspace . --language en --json` and confirm 0 lint errors.

### 0.2 Fix Windows solution and CI paths
- **What:** Update `Windows/Syxx.sln`, `Windows/Syxx.Linux.slnf`, and `.github/workflows/ci.yml` to reference projects under `Windows/dotNET/` instead of stale `Windows/` relative paths.
- **Why:** The .NET parity matrix cannot build. No Windows/Linux agent can verify its work.
- **Skill to equip:** `syxx-cross-platform-parity` (`.claude/skills/syxx-cross-platform-parity/SKILL.md`)
- **Validation:** `dotnet build Windows/Syxx.sln` restores and builds without path errors.

### 0.3 Add Server CI test job
- **What:** Add a pytest job to `.github/workflows/ci.yml` for `Server/tests/`.
- **Why:** The Server security core (DNS tampering detection, Ed25519 signing) has 21 test files but no CI job. Regressions merge unchecked.
- **Skill to equip:** `syxx-server-architecture` (`.claude/skills/syxx-server-architecture/SKILL.md`)
- **Validation:** `cd Server && python3 -m pytest tests/` passes locally; CI job runs on push and PR.

### 0.4 Wire honesty-coverage gate into CI
- **What:** Add `scripts/honesty-coverage-gate.py` as a CI step in `.github/workflows/ci.yml`.
- **Why:** The gate that prevents security guard deletion (the project's defining failure class) is unwired and known-red.
- **Skill to equip:** `syxx-honesty-audit` (`.agents/skills/syxx-honesty-audit/SKILL.md`)
- **Validation:** `python3 scripts/honesty-coverage-gate.py` runs in CI; guard anchor rot fails the build.

### 0.5 Add AGENTS.md governance section for custom agents
- **What:** Add a section to `AGENTS.md` defining when to use custom agents, selection criteria for security-sensitive tasks, and a requirement that agents handling verdicts/evidence honor the tri-state and honesty protocol.
- **Why:** 84 agents operate outside the honesty fence that AGENTS.md makes binding.
- **Skill to equip:** `syxx-agent-development`
- **Validation:** AGENTS.md includes the section; it references tri-state and honesty protocol.
- **Note:** Per AGENTS.md rules, only the user may edit AGENTS.md. Propose the section text and wait for approval.

### 0.6 Add lint and static-security gates to CI
- **What:** Add swiftlint for macOS sources, ruff for Python, bandit for Python security scanning to CI.
- **Why:** Tri-state backsliding (the most common defect class) has no mechanical backstop.
- **Skill to equip:** `syxx-honesty-audit`
- **Validation:** CI includes lint steps; failures block merge.

### New skill to create for Phase 0
- **`syxx-ci-pipeline`** (in `.agents/skills/syxx-ci-pipeline/SKILL.md`): Reference for the Syxx CI workflow structure, job naming conventions, platform matrix, parity-transcript diff job, honesty-coverage gate wiring, and lint/security step configuration. Use when modifying `.github/workflows/ci.yml` or adding new CI jobs. Create using `syxx-skill-development` as the template.

---

## Phase 1: Meta-Orchestration Layer (deploy after Phase 0)

**Goal:** Establish the coordination backbone that manages all specialist agents.

**Deployment model:** These agents run as long-lived coordinators. Deploy them as the first wave of subagents. They do not write production code; they route, track, and synthesize.

### Agents to deploy (6 agents, parallel)

| # | Agent | File | Specific Functions | Skills to Equip |
|---|-------|------|-------------------|-----------------|
| 1 | **multi-agent-coordinator** | `.qoder/agents/multi-agent-coordinator.md` | Prevent drift across all specialist agents. Focus instruction on groups, not individuals. Redirect agents that get stuck on artifacts or typos. Enforce the "no checkpoints" rule: agents finish assigned work, not just phases. | `syxx-agent-development`, `syxx-honesty-audit` |
| 2 | **it-ops-orchestrator** | `.qoder/agents/it-ops-orchestrator.md` | Route cross-domain tasks between macOS (Swift), Windows (.NET), and Linux (.NET/Avalonia) teams. Standardize implementation language for shared modules (TriState, IPClassifier). Identify which deltas require cross-platform coordination vs. platform-specific work. | `syxx-cross-platform-parity`, `syxx-agent-development` |
| 3 | **git-workflow-manager** | `.qoder/agents/git-workflow-manager.md` | Manage branch strategy: feature branches per delta cluster. Coordinate merge order for cross-platform changes. Ensure no file conflicts between platform teams editing SyxxKit/Syxx.Kit simultaneously. Enforce explicit-path commits only (`git diff --cached --name-only`). | `syxx-agent-development` |
| 4 | **error-coordinator** | `.qoder/agents/error-coordinator.md` | Define error handling contracts for new code (all failure modes map to `.inconclusive`, never crash). Ensure tests have adequate failure cases per AGENTS.md Protocol Section 3. Prevent agents from softening tests to force a pass. Define fail-closed behavior for every security fix. | `syxx-honesty-audit`, `syxx-delta-file` |
| 5 | **context-manager** | `.qoder/agents/context-manager.md` | Maintain shared context between platform teams on design decisions (TriState variants, IPClassifier ranges). Track all file changes to prevent merge conflicts. Advise agents on when to compact context. Store canonical implementation decisions for cross-platform reference. | `syxx-agent-development` |
| 6 | **knowledge-synthesizer** | `.qoder/agents/knowledge-synthesizer.md` | Coordinate the meaningful flow of information from specialist agents to the calling agent. Extract patterns from completed fixes. Document shared infrastructure decisions. Build knowledge base entries for "security fix patterns" to prevent regression. | `syxx-skill-development`, `syxx-delta-file` |

### Coordination structure
- **The calling agent (you)** is the overall lead. You assign tasks, approve output wording, and make architectural decisions.
- **multi-agent-coordinator** is your deputy for drift prevention and group instruction.
- **it-ops-orchestrator** is your deputy for cross-platform routing.
- **git-workflow-manager** owns branch/merge decisions and reports to you.
- **error-coordinator** owns test integrity and reports to multi-agent-coordinator.
- **context-manager** and **knowledge-synthesizer** support all agents and report to multi-agent-coordinator.

### Communication protocol
1. Specialists report findings to **knowledge-synthesizer**, who deduplicates and formats.
2. **knowledge-synthesizer** reports to **multi-agent-coordinator**, who prioritizes.
3. **multi-agent-coordinator** reports to **you** for decisions.
4. **you** assign implementation tasks to specialists via **it-ops-orchestrator**.
5. **git-workflow-manager** manages all commits and merges.

---

## Phase 2: Critical Security and Foundation (deploy after Phase 1 coordinators are running)

**Goal:** Fix the 35 most dangerous deltas: root RCE, privilege escalation, SSRF, critical platform blockers, and licensing/revenue security. Build shared infrastructure (TriState, IPClassifier, observability bootstrap).

**Source:** IMPLEMENTATION_PLAN.md Phase 1 (items 1.1 through 1.5).

### Agents to deploy (8 agents, in 3 waves)

#### Wave 2A: Shared Infrastructure (deploy first, blocks everything)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 1 | **swift-expert** | Implement `SyxxKit/TriState.swift` (enum: `.secure`, `.tampered`, `.inconclusive`). Implement `SyxxKit/IPClassifier.swift` with IPv4-mapped IPv6 normalization. Canonical base: `fetcher.py`'s implementation. | `syxx-cross-platform-parity`, `syxx-dns-detection-logic` |
| 2 | **csharp-developer** | Implement `Syxx.Kit/TriState.cs` (matching Swift variant names exactly). Implement `Syxx.Kit/IPClassifier.cs` with identical classification logic. | `syxx-cross-platform-parity`, `syxx-parity-check` |
| 3 | **python-pro** | Implement `Server/syxx_server/netutil.py` (server-side IPClassifier equivalent). Ensure type hints on all new functions. | `syxx-server-architecture` |

**Gate:** TriState and IPClassifier must land on all three platforms and pass parity checks before Wave 2B starts. Run `syxx-parity-check` skill to verify byte-identical behavior.

#### Wave 2B: Critical Security Fixes (deploy after 2A, parallel by platform)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 4 | **swift-expert** | Fix D060 (shell injection via osascript: replace string interpolation with `Process` arguments array). Fix D065 (symlink race: use `O_CREAT\|O_EXCL\|O_NOFOLLOW`). Fix D033 (remote content origin checks on every navigation including redirects). Fix D048 (activation overwrites known-good credential before validation). | `syxx-honesty-audit`, `syxx-delta-file` |
| 5 | **csharp-developer** | Fix D034 (WebView origin check: replace `Uri.Contains` with parsed host+port comparison). Fix D035/D036/D064 (URI validation in Windows launch contexts). Fix E028 (`isPro` never set true: wire LicenseManager validation result to `isPro` at launch). Fix E026 (hardcoded collector host: create `SyxxConfig.LeakBaseUrl`). | `syxx-honesty-audit`, `syxx-delta-file` |
| 6 | **python-pro** | Fix D053 (server offline license validation: add Ed25519 signing). Fix D063 (malformed packet validation: add QR/opcode/qdcount/qclass checks before observation). Fix A045 (SERVFAIL/REFUSED mapped to status:ok: map each RCODE to typed outcome). | `syxx-server-architecture`, `syxx-evidence-cryptography` |
| 7 | **security-engineer** | Review all Phase 2 security fixes for completeness. Audit D019/D075 (ContentWarp SSRF fail-open: change `return true` to `return false`, add `.noResolver` tri-state). Audit D018 (NS glue IPs queried with no public-IP gate). Audit D037/D038 (unsigned startup cache + arbitrary base URL). Cross-check that no fix introduces a new attack surface. | `syxx-honesty-audit`, `syxx-evidence-cryptography` |

#### Wave 2C: Cross-Platform Witness and Licensing (deploy after 2B, requires both platforms)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 8 | **backend-developer** | Fix E019/E020 (cross-platform witness broken: date encoding + errorMessage divergence). Drop display Date mirrors; render from signed evidence millis. Exclude `errorMessage` from signed bytes. Fix D001/D021 (trial HMAC key publicly derivable: add server-provided nonce). Fix D053 (offline license Ed25519 signing). | `syxx-evidence-cryptography`, `syxx-server-architecture` |

### Phase 2 validation gates
- Every fix must include a test with failure cases (AGENTS.md Protocol Section 3).
- Cross-platform fixes must pass `syxx-parity-check` before merge.
- The honesty-coverage gate (wired in Phase 0) must pass.
- **error-coordinator** reviews every test to confirm it fails when the code is wrong.

### New skill to create for Phase 2
- **`syxx-security-fix-patterns`** (in `.agents/skills/syxx-security-fix-patterns/SKILL.md`): Catalog of proven security fix patterns from DELTAS.md (fail-closed defaults, tri-state replacement, input validation, SSRF prevention, credential handling). Use when implementing any security delta to avoid reintroducing fixed patterns. Create after Wave 2B completes, using patterns extracted by **knowledge-synthesizer**.

---

## Phase 3: Detection and Verdict Integrity (deploy after Phase 2)

**Goal:** Fix the 48 deltas that impair the core product purpose: detecting DNS tampering honestly and producing trustworthy evidence.

**Source:** IMPLEMENTATION_PLAN.md Phase 2 (items 2.1 through 2.1a and beyond).

### Agents to deploy (7 agents, in 2 waves)

#### Wave 3A: Detection Logic (parallel by platform)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 1 | **swift-expert** | Fix C049 (Prophet discards user-selected resolver). Fix C046 (unanimous-tampering detector bails on proof conditions: compare RCODE/status sets, not just rdata). Fix C025 (baseline queries over plaintext UDP: query via DoH/DoT). | `syxx-dns-detection-logic`, `syxx-synthesis-engine` |
| 2 | **csharp-developer** | Fix C010 (version.bind escalated from tilt-only to section-gating: delete `subs.Add()`). Windows-side C046 twin. Windows-side C025 twin. | `syxx-dns-detection-logic`, `syxx-parity-check` |
| 3 | **python-pro** | Fix A036 (signed payload excludes per-observation detail). Fix A044 (consensus drops reachable empty vantages). Fix A041 (DNSSEC posture stops at first reachable vantage). Fix A042/A043 (RRSIG inception ignored + missing typeCovered check). | `syxx-server-architecture`, `syxx-evidence-cryptography` |
| 4 | **backend-developer** | Fix C076 deep-dive: DNS leak test verdict integrity. Add provider enrichment columns to `db.py`. Wire reverse DNS + ASN lookup into leak observation. Replace count-only verdicts with provider-aware logic. Delete hardcoded mock fallback data. | `syxx-server-architecture`, `syxx-honesty-audit` |

#### Wave 3B: Verdict and Evidence Integrity (requires 3A complete)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 5 | **security-engineer** | Audit all verdict paths for false "secure" on missing data. Verify no path aggregates inconclusive as if it were real. Cross-check that tri-state is preserved end-to-end from probe to UI. | `syxx-honesty-audit`, `syxx-synthesis-engine` |
| 6 | **code-reviewer** | Review all Phase 3 changes for tri-state compliance. Verify no bare Bool remains in any verdict path. Check that every fix has a corresponding failure-case test. | `syxx-honesty-audit`, `syxx-delta-file` |
| 7 | **qa-expert** | Design integration tests that exercise the full probe-to-verdict pipeline with known-tampered and known-clean inputs. Ensure tests fail when the code produces wrong verdicts. Verify the golden-vector corpus covers the new fixes. | `syxx-synthesis-engine`, `syxx-parity-check` |

### Phase 3 validation gates
- Every verdict change must pass `syxx-honesty-audit` before merge.
- Cross-platform detection changes must pass `syxx-parity-check`.
- The golden-vector corpus must be updated with new test vectors.
- **error-coordinator** confirms no test was softened.

---

## Phase 4: Platform Parity and v1 Features (deploy after Phase 3)

**Goal:** Build the 10 v1 features from ROADMAP.md that block ship, maintaining platform parity across macOS, Windows, and Linux.

**Source:** ROADMAP.md "What v1 actually requires" (10 features in dependency order).

### Agents to deploy (10 agents, in 4 waves)

#### Wave 4A: Installable Artifacts (deploy first)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 1 | **build-engineer** | Create installable artifacts: DMG (macOS), installer (Windows), `.deb` (Debian/Mint). Set up `bundle-app.sh` for release builds. Ensure debug and release builds both work (AGENTS.md). | `syxx-cross-platform-parity` |
| 2 | **deployment-engineer** | Configure CI release pipeline. Set up notarization for macOS. Set up code signing for Windows. Create Linux self-contained package. | `syxx-cross-platform-parity` |

#### Wave 4B: Mirror Module and First-Run Experience (deploy after 4A)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 3 | **swiftui-architect** | macOS: Wire Mirror module to actually run (B-2: Pro flag is never set). Build first-run experience (A-2): Launch to EULA to one obvious next action. Build first-run scan button (D-1). | `syxx-gui-architecture`, `syxx-ui-design-system`, `macos-tahoe-ui-implementation` |
| 4 | **winui3-architect** | Windows: Wire Mirror module to run. Build first-run experience. Build first-run scan button. Fix Windows GUI Cage trigger (B-5: pane never populates). | `syxx-gui-architecture`, `syxx-ui-design-system`, `windows-ui-implementation` |
| 5 | **avalonia-architect** | Linux: Build first-run experience. Ensure Linux GUI parity with macOS/Windows for all v1 features. Fix Linux license persistence (E-5: cannot persist via DPAPI). | `syxx-gui-architecture`, `syxx-cross-platform-parity` |

#### Wave 4C: Commerce Loop (deploy after 4B, parallel)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 6 | **backend-developer** | Fix E-3 (purchase from inside the app: wire UI to Stripe checkout, fix 404 return page). Fix E-7 (offline paying customers stay unlocked). Fix E-8 (subscription lifecycle: cancelled/suspended/expired/offline are four distinct states). Fix E-10 (pricing consistency: one price everywhere). | `syxx-server-architecture` |
| 7 | **payment-integration** | Review Stripe and Keygen implementations. Ensure subscription lifecycle works end-to-end. Verify trial period rebuild. Ensure new code does not break payment system. | `syxx-server-architecture` |

#### Wave 4D: Continuous Monitoring and Update (deploy after 4C)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 8 | **swift-expert** | macOS: Implement D-2 (continuous monitoring: background re-probe, change detection, state persistence). Implement D-5 (per-network history: write to the existing ledger class). Implement D-6 (network-change detection: SSID join triggers re-evaluation). | `syxx-dns-detection-logic`, `syxx-data-persistence` |
| 9 | **csharp-developer** | Windows: Implement D-2/D-5/D-6 twins. Ensure byte-identical behavior with macOS. | `syxx-dns-detection-logic`, `syxx-parity-check` |
| 10 | **platform-engineer** | Implement G-1 (update mechanism: enable update check, fix dead URL, add appcast/updater). Implement G-4 (diagnostics: logging and crash reporting on both clients). Implement G-6 (settings surface: resolver set, scan scope, notifications, telemetry opt-out). | `syxx-cross-platform-parity` |

### Phase 4 validation gates
- Every GUI feature must be tested on its target platform with a real run.
- Cross-platform features must pass `syxx-parity-check`.
- All user-visible output must be cataloged in `outputs-catalog.csv`.
- All user-visible text must be approved by the user before implementation (AGENTS.md).

### New skills to create for Phase 4
- **`syxx-ci-release`** (in `.agents/skills/syxx-ci-release/SKILL.md`): Reference for release artifact creation, notarization, code signing, and CI release pipeline. Use when building installable artifacts or configuring release workflows.
- **`syxx-commerce-flow`** (in `.agents/skills/syxx-commerce-flow/SKILL.md`): Reference for the Stripe/Keygen licensing flow, subscription lifecycle states, trial period logic, and offline validation. Use when implementing any commerce or licensing feature.

---

## Phase 5: Quality, Delivery, and Documentation (deploy after Phase 4)

**Goal:** Final quality assurance, security audit, documentation, and ship readiness.

### Agents to deploy (8 agents, parallel)

| # | Agent | Specific Functions | Skills to Equip |
|---|-------|-------------------|-----------------|
| 1 | **qa-expert** | Final integration test pass across all three platforms. Verify every test has failure cases. Confirm the app fails tests where it should (AGENTS.md: "you do not want the app passing tests"). | `syxx-honesty-audit`, `syxx-parity-check` |
| 2 | **security-auditor** | Full security audit of all Phase 2-4 changes. Penetration test of server endpoints. Verify no false "secure" verdicts remain. Audit credential handling and key management. | `syxx-honesty-audit`, `syxx-evidence-cryptography` |
| 3 | **penetration-tester** | Attempt to forge evidence, bypass license validation, inject via DNS packets, and exploit SSRF paths. Report any successful attack as a delta. | `syxx-evidence-cryptography`, `syxx-delta-file` |
| 4 | **code-reviewer** | Review all merged code for tri-state compliance, honesty protocol adherence, and platform parity. Flag any bare Bool in verdict paths. | `syxx-honesty-audit`, `syxx-delta-file` |
| 5 | **technical-writer** | Update CHANGELOG.md, ROADMAP.md, DESIGN.md. Write user guides for new features. Update outputs-catalog.csv with all user-visible strings. | `syxx-skill-development` |
| 6 | **documentation-engineer** | Review all documentation for clarity and accuracy. Ensure API documentation is understandable. Write "Explain It Like I'm Syxx" section for APIs. | `syxx-server-architecture` |
| 7 | **compliance-auditor** | Review telemetry/observability for GDPR/privacy compliance. Review EULA. Ensure consent requirements are met. Verify subscription changes comply with consumer protection regulations. | `syxx-honesty-audit` |
| 8 | **accessibility-tester** | Audit GUI for WCAG 2.1 compliance. Add `:focus-visible` outlines, `aria-live` regions, colorblind-safe indicators. Keyboard navigation. | `syxx-gui-architecture`, `syxx-ui-design-system` |

### Phase 5 validation gates
- All three platforms build and run successfully (debug and release, with app bundle).
- The honesty-coverage gate passes green.
- No bare Bool remains in any verdict path.
- All user-visible output is cataloged and approved.
- DELTAS.md is updated with any new findings from the audit.

---

## Skill Equipping Summary

### Existing skills (already available)

| Skill | Location | Equip For |
|-------|----------|-----------|
| `syxx-honesty-audit` | `.agents/skills/` | All agents touching verdicts, tests, or security |
| `syxx-delta-file` | `.agents/skills/` | All agents finding or fixing defects |
| `syxx-parity-check` | `.agents/skills/` | All agents doing cross-platform work |
| `syxx-reference-lookup` | `.agents/skills/` | Research agents, design decisions |
| `syxx-agent-development` | `.claude/skills/` | Coordinators, agent management |
| `syxx-cross-platform-parity` | `.claude/skills/` | Platform teams, build/deploy agents |
| `syxx-dns-detection-logic` | `.claude/skills/` | Detection/probe agents |
| `syxx-evidence-cryptography` | `.claude/skills/` | Evidence/signing agents |
| `syxx-gui-architecture` | `.claude/skills/` | GUI agents |
| `syxx-ui-design-system` | `.claude/skills/` | GUI agents |
| `syxx-synthesis-engine` | `.claude/skills/` | Engine/verdict agents |
| `syxx-scoring-mathematics` | `.claude/skills/` | Scoring/confidence agents |
| `syxx-server-architecture` | `.claude/skills/` | Server/backend agents |
| `syxx-data-persistence` | `.claude/skills/` | Storage/ledger agents |
| `syxx-skill-development` | `.claude/skills/` | Knowledge-synthesizer, skill creation |
| `macos-tahoe-ui-implementation` | `.claude/skills/` | macOS GUI agents |
| `windows-ui-implementation` | `.claude/skills/` | Windows GUI agents |

### New skills to create (4 skills)

| Skill | Phase | Created By | Purpose |
|-------|-------|------------|---------|
| `syxx-ci-pipeline` | Phase 0 | Calling agent | CI workflow structure, job naming, platform matrix, gate wiring |
| `syxx-security-fix-patterns` | Phase 2 | knowledge-synthesizer | Catalog of proven security fix patterns from DELTAS.md |
| `syxx-ci-release` | Phase 4 | build-engineer | Release artifact creation, notarization, code signing |
| `syxx-commerce-flow` | Phase 4 | backend-developer | Stripe/Keygen flow, subscription lifecycle, offline validation |

---

## Deployment Sequence (condensed)

```
Phase 0 (Harness Repair)     [calling agent, direct]
  0.1 Fix 84 agent names     ─┐
  0.2 Fix Windows CI paths    ├── all parallel, no subagents
  0.3 Add Server CI tests     │
  0.4 Wire honesty gate       │
  0.5 AGENTS.md governance    │  (propose to user, wait for approval)
  0.6 Add lint/security CI   ─┘
  CREATE: syxx-ci-pipeline

Phase 1 (Meta-Orchestration)  [6 coordinators, parallel]
  multi-agent-coordinator ─┐
  it-ops-orchestrator      ├── all parallel, long-lived
  git-workflow-manager     │
  error-coordinator        │
  context-manager          │
  knowledge-synthesizer   ─┘

Phase 2 (Critical Security)   [8 specialists, 3 waves]
  Wave 2A: swift-expert + csharp-developer + python-pro  (shared infra)
    GATE: TriState + IPClassifier parity verified
  Wave 2B: swift-expert + csharp-developer + python-pro + security-engineer  (security fixes)
  Wave 2C: backend-developer  (witness + licensing)
  CREATE: syxx-security-fix-patterns

Phase 3 (Detection Integrity) [7 specialists, 2 waves]
  Wave 3A: swift-expert + csharp-developer + python-pro + backend-developer  (detection logic)
  Wave 3B: security-engineer + code-reviewer + qa-expert  (verdict audit)

Phase 4 (v1 Features)         [10 specialists, 4 waves]
  Wave 4A: build-engineer + deployment-engineer  (artifacts)
  Wave 4B: swiftui-architect + winui3-architect + avalonia-architect  (Mirror + first-run)
  Wave 4C: backend-developer + payment-integration  (commerce)
  Wave 4D: swift-expert + csharp-developer + platform-engineer  (monitoring + update)
  CREATE: syxx-ci-release, syxx-commerce-flow

Phase 5 (Quality and Ship)    [8 specialists, parallel]
  qa-expert + security-auditor + penetration-tester + code-reviewer
  technical-writer + documentation-engineer + compliance-auditor + accessibility-tester
```

---

## Total Agent Count

| Phase | Agents | Cumulative |
|-------|--------|------------|
| Phase 0 | 1 (calling agent) | 1 |
| Phase 1 | 6 coordinators | 7 |
| Phase 2 | 8 specialists | 15 |
| Phase 3 | 7 specialists | 22 |
| Phase 4 | 10 specialists | 32 |
| Phase 5 | 8 specialists | 40 |

**Unique agents deployed:** 40 (some agents like swift-expert, csharp-developer, and python-pro appear in multiple phases but are redeployed with new task assignments).

---

## Risks and Mitigations

1. **Windows build remains broken after Phase 0.2.** Mitigation: If `dotnet build` still fails after path fixes, deploy **dotnet-core-expert** to diagnose before Phase 2 starts. The .NET parity matrix is a deliverable, not optional.

2. **Session evidence unavailable (better-harness finding).** The 0-eligible-sessions issue means no workflow learning is possible. Mitigation: Fix Qoder source root configuration during Phase 0 so Phase 1+ coordinators can observe agent behavior.

3. **84 agents are generic boilerplate (better-harness finding).** Only 2 of 84 were sampled. Mitigation: Have **knowledge-synthesizer** audit the full roster during Phase 1 and flag agents that are misaligned with Syxx's domain. Remove or rewrite flagged agents.

4. **Output approval bottleneck.** AGENTS.md requires user approval for all user-visible output. With 40 agents producing output, this could stall. Mitigation: **product-manager** (deployed in Phase 5, but available earlier on request) batches output proposals for batch approval. The calling agent should propose output wording proactively.

5. **Cross-platform merge conflicts.** Multiple agents editing SyxxKit and Syxx.Kit simultaneously. Mitigation: **git-workflow-manager** enforces feature branches per delta cluster and coordinates merge order. **context-manager** tracks all file changes.

6. **Test softening.** Agents may soften tests to force a pass. Mitigation: **error-coordinator** reviews every test for failure cases. AGENTS.md Protocol Section 3 is binding. A failing test is data, not an obstacle.

## Assumptions

- The user approves the AGENTS.md governance section (Phase 0.5) without significant changes.
- The Qoder session source root configuration can be fixed to enable session evidence collection.
- The Windows .NET SDK and macOS Swift toolchain are both available in the build environment.
- The Python server dependencies (`cryptography`, `opentelemetry`) are installed or installable.
- The user is available for output approval at each phase boundary.
