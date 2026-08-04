# Delta-Fix Subagent Deployment Plan

**Date:** 2026-07-28
**Companion document:** `DELTA_FIX_IMPLEMENTATION_PLAN.md` (the build plan itself)
**Authority:** `DELTAS_FIXES_PLANNING_INSTRUCTIONS.md.rtf`, steps (1)–(7)
**Scope:** 168 open deltas — every delta in `DELTAS.md` that is neither in `DELTAS-fixed.md` nor in `DELTAS-rejected.md`

---

## How the 168 were derived

Not by reading the ledger prose. `DELTAS.md` is 1.1 MB and three planning agents died attempting it; the inventory was extracted mechanically instead:

| Step | Result |
|------|--------|
| Delta blocks parsed from `DELTAS.md` (`- **[Xnnn]**` anchors) | 373 |
| Minus IDs also present in `DELTAS-fixed.md` | −140 |
| Minus IDs also present in `DELTAS-rejected.md` | −83 |
| Overlap (present in both ledgers) | +18 |
| **Truly open** | **168** |

**Finding that must be resolved before any group starts:** 140 delta IDs appear in **both** `DELTAS.md` and `DELTAS-fixed.md`. That is not a rounding error — it means the open ledger and the fixed ledger disagree about 140 items. Two known instances: **A036** is filed as fixed while `leak.py:227-232` still excludes observations from the signed payload (a *false* fixed entry), and **A118** is still open while `Server/tests/test_routes.py:337-425` fully satisfies it across 16 routes (a *missed* graduation). Until reconciled, no group's "remaining work" figure can be trusted.

**Domain distribution of the 168:** A(server) 46 · B(verdict/evidence) 43 · C(detection efficacy) 25 · D(security/hardening) 25 · E(platform integration) 15 · F(misc) 14. **112 of 168 are marked Critical.**

Machine-readable inventory: `open_deltas.json` / `open_deltas.tsv` / `delta_groups_final.json` in the session scratchpad.

---

## Grouping principle

Deltas are grouped by **shared root cause and shared fix mechanics**, not by domain letter. The test applied to each group: *could one engineer fix every delta in it in a single coherent pass, holding one mental model and reusing one set of test fixtures?* A group that fails that test was split.

Two constraints shaped every boundary:

1. **Twin-bound groups stay whole.** A determinative-core change on macOS needs its `Syxx.Kit` twin in the same pass, or the group ships a parity break. Groups are marked twin-bound below; those cannot be split across sprints.
2. **Ubuntu ships first.** Linux-blocking work is sequenced early even where its deltas are individually less severe, because a defect on the first-ship platform costs more than a higher-severity defect on the third.

---

## The 17 groups

| # | Group | Deltas | Crit | Twin-bound | Primary surfaces |
|---|-------|--------|------|------------|------------------|
| G01 | Tri-state & verdict integrity | 19 | 15 | **Yes** | `Synthesis/` both platforms |
| G02 | Attestation, signing & evidence provenance | 21 | 16 | **Yes** | `WitnessSigner`, `LeakDetector`, server |
| G03 | SSRF & public-IP gating | 4 | 4 | **Yes** | `netutil.py`, `IPClassifier`, `AuthoritativeVantage` |
| G04 | DNS wire parsing & DNSSEC | 11 | 7 | **Yes** | `DNSResolver`, `dnssec_validator.py` |
| G05 | Capture completeness (Cage) | 16 | 6 | **Yes** | `DNSCapture`, `SyxxCage`, report generator |
| G06 | Leak detection & collector attestation | 19 | 12 | **Yes** | `LeakDetector`, `leak.py`, Glutton views |
| G07 | Licensing, entitlement & commerce | 15 | 7 | Partial | `LicenseManager`, `licensing.py`, Stripe |
| G08 | Budgets, timeouts & concurrency | 5 | 3 | **Yes** | `ProbeBudget`, `ProbeRunner`, server threads |
| G09 | Server routing & API surface | 5 | 2 | No | `app.py` |
| G10 | Persistence & secrets at rest | 4 | 2 | **Yes** | `db.py`, keychain/DPAPI, settings |
| G11 | GUI verdict surfacing & accessibility | 14 | 10 | **Yes** | SwiftUI / WinUI3 / Avalonia |
| G12 | CLI surfacing & exit codes | 2 | 2 | **Yes** | `SyxxCLI`, `Program.cs` |
| G13 | Telemetry & observability | 1 | 1 | **Yes** | `SyxxTracer`, bootstraps |
| G14 | Cross-platform parity & packaging | 2 | 1 | **Yes** | build scripts, CI |
| G16 | ContentWarp & HTML/script comparison | 14 | 10 | **Yes** | `ContentWarp`, `warp.py` |
| G17 | Server deployment & service hardening | 3 | 2 | No | `deploy.sh`, systemd |
| G18 | Scoring heuristics & classification thresholds | 13 | 12 | **Yes** | `Synthesizer`, probe classifiers |

Full per-group delta tables — every ID, criticality and location — are in `DELTA_FIX_IMPLEMENTATION_PLAN.md`.

---

## Sequence

Ordered so that no phase leaves the engine transiently false-secure, foundations precede consumers, and Ubuntu unblocks first.

| Wave | Groups | Rationale |
|------|--------|-----------|
| **W1 — Foundations** | G03, G10, G08 | Gating primitives and at-rest protection. Everything downstream assumes a working `ip_is_public` and a settings store that fails closed. Small (13 deltas), high certainty, no dependents. |
| **W2 — Evidence truth** | G04, G16, G06 | What the probes actually *observe*. Parsing and comparison must be correct before anything reasons over their output; fixing verdict logic on top of a mis-parsed record just relocates the error. |
| **W3 — Determinative core** | G01, G18, G02 | The reasoning layer and the provenance that makes its output trustworthy. **The highest-risk wave** — must land as one coherent set (see the cluster note in `DELTAS.md` on B055/B086/B130-B134/C015/C046) or intermediate states are false-secure. |
| **W4 — Capture & platform** | G05, G14, G13 | Cage completeness plus the parity and telemetry work that makes W1–W3 observable in the field. |
| **W5 — Surfaces** | G11, G12 | GUI and CLI rendering. Deliberately last among engineering waves: a surface can only be honest about a verdict the engine computes honestly, so re-rendering before W3 lands means doing it twice. |
| **W6 — Commerce & operations** | G07, G09, G17 | Revenue path and server operations. Independent of the engine, so it can run in parallel with W2–W4 if staffing allows; sequenced last only because it does not gate the honesty spine. |

**Parallelism.** W6 is independent and may run alongside W2–W4. W1 must complete before W2. W3 must not begin until W2 is green. W5 must not begin until W3 is green.

---

## Standing agents — deployed for the entire planning process (step 7A)

| Agent | Copies | Responsibility |
|-------|--------|----------------|
| **multi-agent-coordinator** | 1 | Owns the wave sequence. Deploys each wave's agents, confirms the previous wave's exit criteria before advancing, and redirects any agent that drifts outside its assigned group. |
| **context-manager** | 1 | Enforces the extraction discipline that three dead agents established the need for: agents receive the pre-extracted inventory, never the 1.1 MB ledger. Advises when to compact. |
| **error-coordinator** | 1 | Owns the cross-group defect taxonomy and the "which failure caused which inconclusive" link. Present from W1 so the taxonomy is designed before the captures are written, not retrofitted. |
| **knowledge-synthesizer** | 1 | Deduplicates findings across groups and maintains the delta cross-reference so the same root cause filed under two IDs is fixed once. Also owns the 140-ID ledger reconciliation. |
| **risk-manager** | 1 | Holds the honesty-spine veto. Any group whose plan would permit a transiently false-secure state, or a verdict rendered before its evidence, is blocked at planning time rather than at review. |
| **qa-expert** | 1 | Owns the mutation-resistance bar. Every group's acceptance criteria must name a mutation that turns its tests red; a group whose tests pass when its subject is deleted does not exit planning. |

---

## Per-wave agent assignments

Copy counts are sized to the group's delta count and to how many independent surfaces must change simultaneously — a twin-bound group needs at least one specialist per platform working in lockstep, not one generalist serially.

### W1 — Foundations (13 deltas)

| Agent | Copies | Assigned groups | Why this count |
|-------|--------|-----------------|----------------|
| security-engineer | 2 | G03, G10 | One owns network-path gating (SSRF, glue records), one owns at-rest secrets. Different threat models; splitting prevents a single reviewer normalising both. |
| python-pro | 1 | G03, G10 | `netutil.py`, `db.py` — server half of both groups. |
| swift-expert | 1 | G03, G10 | `IPClassifier`, keychain, settings store. |
| csharp-developer | 1 | G03, G10 | DPAPI twin, `IPClassifier.cs`. Must move with swift-expert. |
| performance-engineer | 1 | G08 | Budgets and timeouts are a latency/efficacy tradeoff, not a correctness question alone. |
| test-automator | 1 | all W1 | Failure-case fixtures for the gating predicates. |

### W2 — Evidence truth (44 deltas)

| Agent | Copies | Assigned groups | Why this count |
|-------|--------|-----------------|----------------|
| network-engineer | 2 | G04, G06 | DNS wire format and resolver behaviour. Two copies: one on parsing, one on the collector protocol. |
| swift-expert | 2 | G04, G16, G06 | 44 deltas across three files with heavy overlap; one copy would serialise the whole wave. |
| csharp-developer | 2 | G04, G16, G06 | Twin parity, same reasoning. |
| python-pro | 2 | G04, G06, G16 | `dnssec_validator.py`, `leak.py`, `warp.py` — three distinct server subsystems. |
| security-auditor | 1 | G16 | ContentWarp parses attacker-controlled HTML; the regex bypasses (A093, A094, A051) are parser-security work. |
| error-detective | 1 | G06 | The `leaked`→`queriesObserved` wire break that silently killed the collector path on both clients is exactly this agent's shape. |
| qa-expert | 1 | all W2 | Golden vectors for parsing edge cases. |

### W3 — Determinative core (53 deltas) — highest risk

| Agent | Copies | Assigned groups | Why this count |
|-------|--------|-----------------|----------------|
| architect-reviewer | 1 | G01, G18, G02 | The cluster must be reviewed as one design, not 53 changes. |
| swift-expert | 2 | G01, G18, G02 | Reference platform; two copies because scoring and attestation are separable workstreams. |
| csharp-developer | 2 | G01, G18, G02 | Twin. Must land in the same commit as the Swift half. |
| security-engineer | 1 | G02 | Signing, trust sets, provenance. B081 (`.untrustedSigner` unreachable because no caller supplies a trust set) is the anchor defect. |
| python-pro | 1 | G02 | Server-side attestation (A036 signed-payload scope). |
| data-scientist | 1 | G18 | Threshold and classifier calibration — the TTL/stability/amplification heuristics are statistical, not logical. |
| qa-expert | 2 | G01, G18, G02 | One owns golden vectors, one owns adversarial/mutation cases. |
| risk-manager | 1 | G01, G02 | Veto on any intermediate state that could read clean. |

### W4 — Capture & platform (19 deltas)

| Agent | Copies | Assigned groups | Why |
|-------|--------|-----------------|-----|
| swift-expert / csharp-developer | 1 each | G05, G14 | Capture stacks are per-platform native. |
| debugger | 1 | G05 | Partial-parse and packet-loss defects need reproduction, not reading. |
| performance-monitor | 1 | G13 | Owns the OTel span/attribute schema. |
| error-coordinator | (standing) | G13 | Owns the Sentry half; the seam between the two is named in their specs. |
| build-engineer | 1 | G14 | Packaging and CI. |
| chaos-engineer | 1 | G05 | Hostile/truncated capture input. |

### W5 — Surfaces (16 deltas)

| Agent | Copies | Assigned groups | Why |
|-------|--------|-----------------|-----|
| swiftui-architect | 1 | G11 | macOS is the reference surface. |
| winui3-architect | 1 | G11 | Windows twin. |
| avalonia-architect | 1 | G11 | **Ubuntu — ships first.** |
| gui-cross-platform-continuity | 1 | G11 | Owns the three-way parity contract so the platforms cannot drift again. |
| accessibility-tester | 1 | G11 | Rune labels, VoiceOver/Narrator, colour-only indicators. |
| cli-developer | 1 | G12 | Exit codes, `--json`, `--verbose`. |
| ux-researcher | 1 | G11 | Whether a corrected verdict actually reads correctly to a novice. |
| product-manager | 1 | G11, G12 | **Approval gate** for every user-visible string (AGENTS.md §4.7). |

### W6 — Commerce & operations (23 deltas)

| Agent | Copies | Assigned groups | Why |
|-------|--------|-----------------|-----|
| payment-integration | 1 | G07 | Stripe/Keygen lifecycle. |
| legal-advisor | 1 | G07 | EULA unification, auto-renewal disclosure, refund terms. **Blocking** — several G07 deltas cannot be closed without an owner decision. |
| backend-developer | 1 | G09, G07 | Server routes and fulfilment. |
| api-designer | 1 | G09 | Versioning, pagination, error taxonomy. |
| deployment-engineer | 1 | G17 | systemd hardening, service user, backups. |
| linux-infra-admin | 1 | G17 | Distro specifics on the first-ship platform. |
| compliance-auditor | 1 | G07, G10 | PII in audit logs (A072), data-retention claims. |

---

## Deployment protocol (steps 7B–7G)

For each wave, in order:

1. **Call** the wave's agents. Standing agents remain from W1.
2. **Brief** each with: its group's delta table, the honesty-spine constraints, the twin-bound flag, and the pre-extracted inventory path. **Never** the full ledger.
3. **Plan** the group. Output per delta: root cause, exact `file:line` targets on every affected platform, the fix, the failure-case test, and the mutation that must turn it red.
4. **Gate** on risk-manager (no false-secure intermediate) and qa-expert (mutation named) before the group is marked planned.
5. **Dismiss** the wave's agents. Standing agents persist.
6. **Advance** to the next wave.

---

## Risks that require an owner decision before their group can be planned

These cannot be resolved by any agent and will block their groups if left open:

| # | Decision | Blocks |
|---|----------|--------|
| 1 | **Ledger reconciliation** — 140 IDs in both open and fixed ledgers. Who adjudicates? | All waves (scope is unknown until resolved) |
| 2 | **EULA unification + device count** (unlimited vs 5 vs the "3 activations" the UI already shows) | G07 |
| 3 | **Telemetry consent default** — opt-in vs opt-out for EU users | G13, G10 |
| 4 | **Legal entity name** — "Joel J. Warne d/b/a MilkandGin" vs "MilkandGin LLC" | G07 |
| 5 | **Governing-law jurisdiction** | G07 |
| 6 | **Every user-visible string** produced by W5 (AGENTS.md §4.7) | G11, G12 |
| 7 | **Probe budget vs transport timeouts** — the 2.5 s cap sits below 4–8 s transports, which may be manufacturing false inconclusives. Raise the cap or lower the transports? Needs field data (spec'd in the performance-monitor telemetry plan). | G08, G18 |

---

## Exit criteria for the planning process

Planning is complete when, for all 17 groups:

- every one of the 168 deltas has a named root cause, `file:line` targets on **every** affected platform, and a fix;
- every fix has a failure-case test **and** a named mutation that turns it red — per `AGENTS.md` §5, a test that passes when its subject is deleted does not count;
- every twin-bound group's plan changes both platforms in the same step;
- no step in any sequence leaves the engine able to render a clean or reassuring verdict from unmeasured evidence;
- every user-visible string is either reused from approved copy or flagged for owner approval and catalogued in `outputs-catalog.csv`.
