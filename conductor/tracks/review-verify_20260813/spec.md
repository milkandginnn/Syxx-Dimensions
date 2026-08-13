# Specification: Ultra Review — Dedupe, Verify, Plan Fixes

**Track ID:** review-verify_20260813
**Type:** Bug
**Created:** 2026-08-13
**Status:** Draft
**Tier:** 3 (`IMPLEMENTATION-RULES.md` §3 — cross-platform, security-sensitive)

## Summary

Deduplicate the ultra review report, verify each cited defect against HEAD, and produce an implementation plan per confirmed defect. **Verification and planning only — no fixes land in this track.**

## Context

Source: [`source-report.md`](./source-report.md), preserved here from `~/Documents/REVIEW-ultra-macos-2026-08-12.md`.

The file is **three concatenated reports** with heavy overlap:

1. A macOS ultra review (6 findings, `MatrixDeterminativeEngine.swift` + `VantageComparer.swift`)
2. An adjudication pass — 20 confirmed, 4 refuted, 3 severity re-ranks, plus its own dedup notes
3. A working-tree-diff ultra review (4 Medium, 11 Low)

Section 2 already did partial dedup work and explicitly refuted four findings. That adjudication is **advisory, not authoritative** — this track re-derives conclusions from code.

**Deduped: ~37 unique findings.** Roughly 12 were already verified earlier today and are recorded in `review-fixes_20260812/triage.md` or were reported through `/code-review`.

## Problem Description

### A — DNSSEC validator security (7) · `Server/syxx_server/`

| ID | Location | Defect | Sev |
|---|---|---|---|
| A1 | `dnssec_validator.py:409-426` | **Root trust anchor is attacker-choosable.** `validate_chain` fetches the root DNSKEY over plain UDP from a server found by the same unauthenticated mechanism, picks a KSK, and records `"valid"` with no RFC 7958 pin and no verification of the RRset's own RRSIG. Every downstream step then verifies internally against a forged anchor. | High |
| A2 | `dnssec_validator.py:353-364` | `_ksk` falls back SEP+protocol3 → any protocol3 → bare `rs[0]`, so a non-SEP or protocol≠3 key is accepted as the trust key | Med |
| A3 | `dnssec_validator.py:189-198` | `_vec` performs no on-curve check before verify | Low |
| A4 | `dnssec_validator.py:200-208` | `_vrsa` has no modulus-size floor | Low |
| A5 | `dnssec_validator.py:217-233` | `_dsm` digest comparison is not constant-time | Low |
| A6 | `dnssec_validator.py:93-101` | `_parse` accepts `QDCOUNT != 1`, contradicting `resolver.py:232` (A061) in the same package | Low |
| A7 | `app.py:62-64` | DNSSEC imports resolver private helpers under a swallowing `try/except`; a rename silently 502s `/api/dnssec` | Low |

**A1 is the reason this track exists.** For a product whose purpose is detecting DNS tampering, a forgeable trust anchor means an on-path attacker can make the DNSSEC check confirm their own answer.

### B — MonitorScheduler lifecycle (5) · `Windows/dotNET/Syxx.Kit/Trust/`

| ID | Location | Defect | Sev |
|---|---|---|---|
| B1 | `:114` | C142 guard tests `_cts`, but `Stop()` nulls it, so the guard is dead in the exact path it was written for. Post-stop tick is counted, latched, and delivered to `_onTick`. **`MonitorSchedulerTests.cs:135-141` requires the broken guard** — fixing the code fails that test, so both move together | High |
| B2 | `:83-88`, `:157`, `:172` | `Stop()`→`Start()` race: new loop's first tick returns null while old `_inFlight` is held, so the baseline is silently skipped while `State=Running`; the old tick then commits stale data into the new run | Med |
| B3 | `:156-159` | TOCTOU on `State` — `Stop()` can interleave so `State=Running` is set with no live loop and `_cts==null`, leaving the scheduler stuck until a second `Stop()` | Med |
| B4 | `:128`, `:193-205` | `_onTick` fires inside `_runLock` while `StopAsync` awaits `_loopTask`; a handler awaiting `StopAsync()` deadlocks | Med |
| B5 | `:109` | `_inFlight` released before the state commit, so two calls interleave and `LastTick` keeps the older result | Med |

Four distinct root causes in one class. The macOS twin discards correctly at `MonitorScheduler.swift:91`.

### C — Forged-packet exclusion, remaining sites (2)

| ID | Location | Defect | Sev |
|---|---|---|---|
| C1 | `VantageComparer.swift:320` | `udpSetsForCheck` filters `hasRecords` but not `!didFail` — a forged outcome can manufacture or break the unanimity the W04 residual guard keys on. Recorded as N20 | Med |
| C2 | `VantageComparer.cs:396`, `:358` | The C# twin still filters `Records.Count > 0` only, at both sites. Recorded as N20 | Med |

### D–I — remaining Syxx-app findings (16)

**Stale tests (2):** four `VantageComparerTests` pinning superseded behavior (already classified in `triage.md`, cause 1) · `MonitorSchedulerTests` first-tick assertions deleted with the doc still promising the behavior.

**Windows process glue (4):** `UnboundManager.cs:120-128` drops diagnostic output on the drain timeout · `:122` blocking `WaitForExit` in an async method · `:131` `catch(Exception)` would mask cancellation · `UnboundResolver.cs:59-66` async seam diverges from the synchronous macOS protocol.

**Dead code from the %→f-string refactor (4):** `app.py:1344` · `fulfillment.py:450` · `test_licensing.py:65` · `investigate.py` half-migrated `.format()` at `:149/:158/:168/:171`.

**Behavior/data (2):** `lookalike.py:15` homoglyph `1→l` dropped, halving digit-impersonation coverage · `app.py:1308-1337` Keygen webhook has no timestamp window and no event-id idempotency, unlike the Stripe handler.

**Deploy (1):** `deploy.sh:218` group provisioning masks failure with `|| true` and duplicates the block at `:549`.

**Perf/maintainability (3):** `dnssec_validator.py:370`/`:255` redundant rdata copies · `whois_lookup.py:19` private-API coupling plus a stale comment · `app.py:1312-1313` function-local re-imports.

**macOS test quality (3):** `WitnessKeychainTestSupport.swift:133` ignores the `SecItemDelete` status, orphaning probe items · `WarpEngineTests` strict decode may diverge from a lenient C# twin · duplicated `backupWasTaken` guard across two classes.

### J — Marketplace half of the repo (3) — planned separately

`capabilities.py:284-289` five opencode aliases collapse to one model id, erasing tier/inherit semantics · `AGENTS.md:7` grants every agent standing authorization to rewrite its own governing file, reaching the mandatory-rules list that points at read-only `rules/` · `AGENTS.md:2` claims 164 agents where the same file later claims 203.

### K — Documentation contracts (2)

`Server/README.md:4` claims "Python 3.8+" while the code now requires 3.11+ (`StrEnum`, `datetime.UTC`) — a user following the deploy doc gets an immediate ImportError · `docs/USER-GUIDE.md:200`, `:279`, `:368` use the term banned by the owner's global RULE 1.

### Refuted by the report's own adjudication — re-check, do not assume

Four findings were refuted in section 2: the `deploy.sh:220` usermod High (heredoc escaping is correct) · a claim that C# forbids `await` in `lock` being "factually wrong" (the language does forbid it — CS1996 — so the *finding* was wrong) · `investigate.py:1722-1740` (the file is 319 lines) · the "new" framing on the UnboundManager timeout path. Treat these as advisory; spot-check rather than accept.

## Authoritative Decisions

1. **A1 is in scope and is the highest priority.** Pre-existing, but verified in code with line-level evidence, and no other finding compares in impact.
2. **Trust prior evidence where it exists.** Cross-reference each finding against `review-fixes_20260812/triage.md` and today's reported findings; where a prior verification cites file:line evidence against an unchanged tree, carry it forward. Spend the team on the ~24 genuinely new items.
3. **Team: `multi-agent-coordinator` plus four specialists** — Python/security (cluster A), C# (cluster B), `swift-expert` (macOS items), test specialist (stale-test and coverage findings). Every specialist reads `AGENTS.md` and `rules/*` before starting.
4. **Marketplace findings (J) are verified but planned separately** — different product, different test suite, different release path.
5. **No fixes land in this track.** Deliverable is a verified findings table plus one implementation plan per confirmed defect.

## Acceptance Criteria

- [ ] All ~37 deduped findings carry a state: **confirmed** / **refuted** / **already-fixed** / **absent-code**, each with file:line evidence.
- [ ] Findings carried forward from prior verification are labeled as such, with their source cited — never silently merged.
- [ ] A1 carries either a reproduction or a precise statement of what could not be reproduced locally and why.
- [ ] Each confirmed defect has an implementation plan: approach, affected files, test strategy, blast radius, and any cross-runtime twin that must move with it.
- [ ] Cross-runtime twins are identified for every confirmed defect — the failure mode that produced 24 divergences is repairing one side only.
- [ ] Marketplace fix plans are in their own section.
- [ ] The four self-refuted findings are spot-checked, and any disagreement with the report's adjudication is recorded.
- [ ] `rules/*-RULES.md` unmodified. No agent writes outside its declared scope.

## Dependencies

- `review-fixes_20260812` — overlapping findings and the `triage.md` evidence this track carries forward. That track is in progress with its own open work (N20 `udpBaselines`, the C# ports).
- `multi-agent-coordinator` is **not installed** — it lives at `subagents/04-meta-orchestration/multi-agent-coordinator.md` and needs copy-first installation per `PLUGIN-RULES.md` §2.3, as `swift-expert` did.
- Working tree carries 147 uncommitted files. Verification reads the working tree, so findings must state whether they were checked against HEAD or the dirty tree.

## Out of Scope

- Implementing any fix.
- The open items already tracked in `review-fixes_20260812` — N20's `udpBaselines`, the C# forged-packet ports, the 26 stale Swift tests. Referenced, not re-planned.
- Ultra review PR splitting and the review branches in the scratchpad worktree.

## Technical Notes

- Hosted CI is off. Local only: `dotnet test`, `swift test`, `pytest`. `SyxxUI`/`SyxxTests` are Windows-pinned and cannot compile here.
- C# holds 12 stable pre-existing failures on this host; the Swift suite sits at 29 distinct failing names. **Compare failing test NAMES, never counts** — one timing test is flaky by construction.
- B1 and its test must be planned as a single change: the test currently asserts the broken behavior.
- `rules/` is UNTRACKED. No agent may propose `git clean`, `git checkout --`, or any operation sweeping untracked files.
- The banned word applies to every agent's output, including K2's own description.

---

_Generated by Conductor. Review and edit as needed._
