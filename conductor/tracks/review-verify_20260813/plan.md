# Implementation Plan: Ultra Review — Dedupe, Verify, Plan Fixes

**Track ID:** review-verify_20260813
**Spec:** [spec.md](./spec.md) · **Source:** [source-report.md](./source-report.md)
**Created:** 2026-08-13
**Status:** [ ] Not Started
**Tier:** 3 — orchestrator mandatory, specialist team, explicit sequencing

## Overview

Six phases. Verification is the product; fixes are planned, never applied.

Phase 0 is blocking — the coordinator must be installed and the carry-forward reconciliation done before any specialist starts, or the team re-verifies a dozen findings that already have evidence.

Clusters are sequenced by impact, not by file order: the DNSSEC trust anchor first, because if confirmed it means the product's own integrity check can be made to confirm an attacker's answer, and that reshapes everything downstream.

**Governing rules:** `IMPLEMENTATION-RULES.md`, `SUBAGENT-RULES.md`, `BUDGET-RULES.md`, `PLUGIN-RULES.md`, `WORKFLOW-RULES.md`, `COMMUNICATION-RULES.md`. Every specialist reads `AGENTS.md` and `rules/*` before starting. `rules/*-RULES.md` are read-only and UNTRACKED — no agent may edit them or propose any operation that sweeps untracked files.

### Cluster map

| Cluster | Subject | Count | Owner | Phase |
|---|---|---|---|---|
| A | DNSSEC validator security | 7 | Python/security | 1 |
| B | MonitorScheduler lifecycle | 5 | C# specialist | 2 |
| C | Forged-packet remaining sites | 2 | carried forward + C#/Swift | 3 |
| D | Stale tests | 2 | test specialist | 3 |
| E | Windows process glue | 4 | C# specialist | 3 |
| F | Dead code from the refactor | 4 | Python/security | 3 |
| G | Behavior/data | 2 | Python/security | 3 |
| H | Deploy | 1 | C# specialist | 3 |
| I | Perf/maintainability | 3 | Python/security | 3 |
| L | macOS test quality | 3 | swift-expert | 3 |
| J | Marketplace | 3 | coordinator | 4 |
| K | Documentation contracts | 2 | coordinator | 4 |

## Team & Budgets

Per `SUBAGENT-RULES.md` §16.1, every assignment declares read scope, write scope, tool ceiling, network permission, and spend ceiling. **Network access is denied to every assignment.** All verification is local.

| Role | Agent | Write scope | Ceiling | Cluster |
|---|---|---|---|---|
| Coordinator | `multi-agent-coordinator` (install first) | track docs only | 40 | assignment, integration, dedup adjudication |
| Python/security | `security-scanning:...-security-auditor` | none — verification only | 55 | A, F, G, I |
| C# specialist | `jvm-languages__csharp-pro` | none — verification only | 55 | B, E, H, C2 |
| Swift specialist | `swift-expert` (project-scoped) | none — verification only | 40 | L, C1 |
| Test specialist | `unit-testing:unit-testing-test-automator` | none — verification only | 40 | D, plus test strategy for every fix plan |

**Every specialist is advisory in this track.** No write scope is granted to any of them, because nothing is implemented here. The coordinator writes track documents only.

**Mandatory first act for every spawned agent, without exception:** read `AGENTS.md` and every file under `rules/` before touching the assignment. This is not optional context — `rules/` carries the banned-word ban, the read-only rule protecting those files, the subagent conduct and budget rules, the communication rules, and the implementation rules that govern how the work is done. An agent that starts without them will violate at least one. The assignment packet must state this as step 0 and require the agent to confirm it in its output header.

Assignment packets carry: the step-0 rules-reading requirement, the specific finding IDs, their `source-report.md` entries, the cited paths, the authoritative decisions from spec.md, the carry-forward table from Phase 0, and the read-only and untracked status of `rules/`. They do **not** carry the orchestrator transcript or unrelated clusters.

## Phase 0: Foundation & Carry-Forward

Blocking. No specialist starts until this is accepted.

### Tasks

- [ ] Task 0.1: Install `multi-agent-coordinator` copy-first per `PLUGIN-RULES.md` §2.3 and `SUBAGENT-RULES.md` §§2.5-2.6 — copy from `subagents/04-meta-orchestration/multi-agent-coordinator.md` into `.claude/agents/`, record provenance in frontmatter, never edit the source in place, and adapt on copy to satisfy §12's output contract and §14.3's structure. The library copy is OpenCode-format and will not validate as-is.
- [ ] Task 0.2: Record the baseline — `git rev-parse HEAD`, the working-tree diff stat, and which of the 147 uncommitted files touch cited paths. Every finding must later state whether it was checked against HEAD or the dirty tree; several cited defects live in uncommitted code.
- [ ] Task 0.3: Build the **carry-forward table**. For each of the ~37 deduped findings, search `review-fixes_20260812/triage.md` and this session's reported findings for prior evidence. Record: finding ID, prior source, prior conclusion, and whether the cited code has changed since. A carried-forward finding must cite its source; it may never be silently merged.
- [ ] Task 0.4: Reconcile the report's **own dedup claims** against the actual file. Section 2 asserts five findings collapse 2–3×; verify that collapsing is correct rather than assuming it, since a wrongly-merged pair hides a distinct defect.
- [ ] Task 0.5: Re-check the four **self-refuted** findings. The report refuted them in its own adjudication pass; that pass is advisory. Spot-check each and record any disagreement.
- [ ] Task 0.6: Assign clusters to specialists with declared scopes and ceilings; emit the assignment packets.
- [ ] Task 0.7: Emit the Phase 0 durable handoff (`BUDGET-RULES.md` §Durable Handoff Template) and commit.

### Verification

- [ ] `multi-agent-coordinator` installed, provenance recorded, source untouched.
- [ ] Carry-forward table complete; every one of ~37 findings has a row.
- [ ] Report's dedup claims independently checked.
- [ ] Four self-refutations spot-checked.
- [ ] `rules/` confirmed intact at 8 files.

## Phase 1: DNSSEC Security Cluster (A) — highest priority

Seven findings, one of which decides whether the product's integrity check can be subverted.

### Tasks

- [ ] Task 1.1: **A1 — root trust anchor.** Verify the chain end to end: `validate_chain` discovers a root server via `_find(".")`, fetches the root DNSKEY via `_sq(".", TYPE_DNSKEY, ...)`, selects a KSK via `_ksk`, and records `_step(ch, ".", "root-dnskey", "valid", ...)`. Confirm by reading whether **(a)** any RFC 7958 anchor, keyTag 19036/38696 constant, or digest pin exists anywhere in `Server/`, and **(b)** whether the root DNSKEY RRset's own RRSIG is verified before the anchor is trusted. Both must be absent for the finding to stand.
- [ ] Task 1.2: **A1 reachability.** Determine what an attacker needs: on-path capability at the resolver, or less. Trace whether any caller reaches `validate_chain` with an attacker-influencable server list. State the blast radius on `/api/dnssec` and on any client surface that consumes it.
- [ ] Task 1.3: **A1 reproduction.** Attempt a local reproduction with a simulated hostile root using the existing `Server/tests/dns_sim.py` harness. If it cannot be reproduced locally, state precisely why and what would be required — an unreproduced High is reported as unreproduced, never softened.
- [ ] Task 1.4: **A2** — confirm `_ksk`'s three-level fallback terminates at `rs[0]` with no SEP or protocol constraint, and whether A1 subsumes it or it stands independently.
- [ ] Task 1.5: **A3–A6** — verify each: `_vec` on-curve check, `_vrsa` modulus floor, `_dsm` comparison timing, `_parse` QDCOUNT handling versus `resolver.py:232`. For A3 and A5 assess exploitability honestly; the report itself judges their practical impact nil, and a verification pass that inflates them wastes the owner's attention.
- [ ] Task 1.6: **A7** — confirm the swallowing `try/except` around resolver private-helper imports and what `/api/dnssec` returns when it fires.
- [ ] Task 1.7: Cross-runtime check — does the C# or Swift DNSSEC path share any of A1–A6? `DNSSECVerifier.cs` and `.swift` validate RRSIG windows; determine whether either performs chain validation with the same anchor weakness.

### Verification

- [ ] Each of A1–A7 marked confirmed / refuted / already-fixed with file:line evidence.
- [ ] A1 carries a reproduction or an explicit statement of what could not be reproduced and why.
- [ ] Cross-runtime exposure determined, not assumed.

## Phase 2: MonitorScheduler Lifecycle (B)

Five findings, four distinct root causes, in one class whose macOS twin behaves correctly.

### Tasks

- [ ] Task 2.1: **B1** — confirm `Stop()` nulls `_cts` synchronously after `Cancel()`, and that the `:114` guard `_cts is { IsCancellationRequested: true }` is therefore false for null and cannot fire in the `Stop()` path. Confirm the post-stop tick is counted, latched, and delivered to `_onTick`. Confirm `StopAsync` keeps `_cts` alive so the guard works on that path only.
- [ ] Task 2.2: **B1 test coupling** — confirm `MonitorSchedulerTests.cs:135-141` asserts `Assert.Equal(2, afterStop)` and therefore *requires* the broken guard. This determines that code and test must change together, which the fix plan must state.
- [ ] Task 2.3: **B2** — trace the `Stop()`→`Start()` interleaving: new `_cts`, first `RunOnceAsync()` returning null because the old `_inFlight` is still held, baseline silently skipped while `State=Running`, old tick committing stale data against the new uncancelled `_cts`.
- [ ] Task 2.4: **B3** — confirm the TOCTOU on `State`: check-then-act without a lock lets `Stop()` interleave so `State=Running` is set with no live loop and `_cts==null`, and the next `Start()` becomes a no-op.
- [ ] Task 2.5: **B4** — confirm `_onTick` fires inside `_runLock` while `StopAsync` awaits `_loopTask`, and that a handler awaiting `StopAsync()` deadlocks. Determine whether any shipping handler does so (`AppViewModel`).
- [ ] Task 2.6: **B5** — confirm `_inFlight` is released in the `finally` before the state commit, permitting a stale `LastTick`.
- [ ] Task 2.7: Confirm the macOS twin discards correctly at `MonitorScheduler.swift:91`, and record the divergence direction.
- [ ] Task 2.8: Adjudicate whether B1–B5 are five defects or fewer. The report's own dedup collapses B1 with a maintainability finding; verify that and check whether B2/B3/B5 share a root cause in `_inFlight` ownership.

### Verification

- [ ] Each of B1–B5 marked with evidence; root-cause count settled rather than assumed.
- [ ] The test-coupling constraint on B1 is explicit.
- [ ] Whether any shipping handler can trigger B4's deadlock is answered.

## Phase 3: Remaining Syxx-App Clusters (C, D, E, F, G, H, I, L)

Runs after Phase 2 so the C# specialist is free. Clusters are independent; specialists work in parallel within the phase.

### Tasks

- [ ] Task 3.1: **C1/C2** — carried forward from N20. Confirm the cited lines still match, and confirm no additional site shares the missing filter. Do not re-derive the mechanism; cite the prior evidence.
- [ ] Task 3.2: **D1** — the four stale `VantageComparerTests`. Carried forward from `triage.md` cause 1. Confirm the classification still holds and that the report's High severity is about incomplete reconciliation rather than a regression.
- [ ] Task 3.3: **D2** — confirm the deleted first-tick assertions and that the doc comment still promises the behavior.
- [ ] Task 3.4: **E1–E4** — the Windows process glue. E1's dropped output on the drain timeout is the only one with a behavioral consequence; verify whether the abandoned `ReadToEndAsync` can fault unobserved.
- [ ] Task 3.5: **F1–F4** — the dead expressions. Confirm each is a bare expression statement and that the removed binding was genuinely unused, so the fix is deletion rather than restoration. F2 is the exception: verify whether the discarded subscription id should drive the reinstatement lookup.
- [ ] Task 3.6: **G1** — confirm the homoglyph map lost `1→l` and that `_permutations` is single-replacement, so the fix is a data-structure change rather than re-adding a key that cannot coexist.
- [ ] Task 3.7: **G2** — confirm the Keygen webhook has no timestamp window and no event-id idempotency, and contrast with the Stripe handler's `kv_insert_once` and the project's own A056 freshness standard.
- [ ] Task 3.8: **H1** — confirm `|| true` masks a groupadd failure and that the block duplicates `:549`.
- [ ] Task 3.9: **I1–I3** — perf and maintainability. Confirm; assess honestly whether any is worth the churn.
- [ ] Task 3.10: **L1–L3** — macOS test quality. L1's orphaned probe items and L2's decode strictness versus the C# twin both need the twin checked, not just the Swift side.

### Verification

- [ ] Every finding in clusters C–L marked with evidence or an explicit carry-forward citation.
- [ ] No finding is marked confirmed on the report's authority alone.

## Phase 4: Marketplace & Documentation (J, K)

Separate from the app per decision 4.

### Tasks

- [ ] Task 4.1: **J1** — confirm the five opencode aliases collapse to one model id and that `test_adapters.py:637` asserts only one of them. Determine whether the collapse is intentional.
- [ ] Task 4.2: **J2** — confirm `AGENTS.md` grants agents authorization to edit it, that repo-root `CLAUDE.md` symlinks to it, and that the mandatory-rules list it contains points at read-only `rules/`. Also confirm the list omits `rules/BUDGET-RULES.md`, which exists on disk.
- [ ] Task 4.3: **J3** — confirm the two agent counts contradict, and determine the true count from `ls plugins/*/agents/*.md`.
- [ ] Task 4.4: **K1** — confirm `StrEnum` and `datetime.UTC` usage sets a 3.11+ floor against a README claiming 3.8+, and that the authoritative docs already pin 3.12. The fix is documentation, not code.
- [ ] Task 4.5: **K2** — confirm the three banned-term uses in the user guide. **The verifying agent must not reproduce the term in its own output.**

### Verification

- [ ] J and K findings verified; marketplace items kept in their own section.
- [ ] The true agent count is established mechanically.

## Phase 5: Fix Planning

One implementation plan per confirmed defect. No code is written.

### Tasks

- [ ] Task 5.1: For each confirmed defect, draft: approach, affected files, test strategy (red test first, per `workflow.md`), blast radius, and rollback.
- [ ] Task 5.2: **Identify the cross-runtime twin for every confirmed defect.** Twenty-four divergences were created by repairing one side only; a fix plan that does not name its twin repeats that. Where no twin exists, say so explicitly.
- [ ] Task 5.3: Plan B1 and its test as a single coordinated change, stating that the test asserts the broken behavior today.
- [ ] Task 5.4: Plan A1 separately and prominently — it is a trust-model change, not a patch, and it may warrant its own track. Include what pinning approach is proposed, how the anchor is provisioned and rotated, and what happens when the pin cannot be satisfied. Fail-closed behavior is required: an unverifiable anchor must not yield a valid conclusion.
- [ ] Task 5.5: Sequence the plans by dependency and blast radius, and mark which are safe to batch.
- [ ] Task 5.6: Record every owner decision the plans surface, with a recommendation each.
- [ ] Task 5.7: Emit the final handoff and commit.

### Verification

- [ ] Every confirmed defect has a plan; every plan names its twin or states there is none.
- [ ] A1's plan specifies fail-closed behavior.
- [ ] Owner decisions listed with recommendations.

## Final Verification

- [ ] All ~37 findings resolved to a state with evidence; counts reconcile.
- [ ] Carried-forward findings cite their source; none silently merged.
- [ ] The report's dedup claims and self-refutations independently checked.
- [ ] Marketplace plans in their own section.
- [ ] No code changed anywhere in this track — verify with `git diff --stat` over source paths.
- [ ] `rules/*-RULES.md` unmodified across every commit.
- [ ] `MEMORY.md` and the track TODO current.

## Risks

| Risk | Mitigation |
|---|---|
| Report's adjudication accepted as authoritative | Phase 0 re-checks its dedup and its four self-refutations |
| Re-verifying what is already known | Phase 0 carry-forward table gates specialist work |
| A1 confirmed but unreproducible, then quietly downgraded | Task 1.3 requires an explicit unreproduced statement instead |
| Fix plans repeat the one-side-only failure | Task 5.2 makes naming the twin mandatory |
| Findings checked against the dirty tree, not HEAD | Task 0.2 requires each to state which |
| Coordinator installed without validation | Task 0.1 requires §12/§14.3 conformance on copy |

---

_Generated by Conductor. Tasks are marked `[~]` in progress and `[x]` complete._
