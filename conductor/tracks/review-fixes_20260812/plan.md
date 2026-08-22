# Implementation Plan: Recovered xhigh Review — Confirmed Defect Repairs

**Track ID:** review-fixes_20260812
**Spec:** [spec.md](./spec.md) · **Findings:** [findings.md](./findings.md)
**Created:** 2026-08-12
**Status:** [~] Wave 1 complete
**Triage:** [triage.md](./triage.md)
**Tier:** 3 — orchestrator mandatory, specialist team, explicit sequencing, integration validation

## Overview

36 unique CONFIRMED defects repaired in seven sequential waves. Each wave is a coherent design unit, not a bag of patches: the defects inside a wave interact, share fixtures, and in four cases have cross-runtime twins that must land together or re-diverge.

Phase 0 is mandatory and blocking. The findings were authored against a pre-revert working tree, so every defect is first classified against HEAD before any repair begins.

**Governing rules:** `IMPLEMENTATION-RULES.md` (planning, validation, continuity, Git), `SUBAGENT-RULES.md` (specialist selection, assignment packets, budgets), `BUDGET-RULES.md` (decision gates, test budget, compaction), `WORKFLOW-RULES.md`, `COMMUNICATION-RULES.md`. Files under `rules/` are read-only for every agent in this track — no edit, move, restore, or bulk operation may touch them.

### Wave map

| Wave | Theme | Defects | Count | Gate |
|---|---|---|---|---|
| 0 | Baseline & triage | all 36 | 36 | — |
| 1 | Synthesis honesty matrix | W01–W03, W20, M02, M09, M10 | 7 | — |
| 2 | Comparator tri-state & classification parity | W04–W06, W11, W16, M03, M13, M14 | 8 | W16 ratified — binding work only |
| 3 | Secrets, keys, licensing | W09, W13, W17, W18, M06, M08 | 6 | — |
| 4 | Parsing & network trust gates | W07*, W08, W10, W12, W14, W15, M01, M07, M11 | 8+ | — |
| 5 | Signed-evidence contract v5 | M12 | 1 | **owner-approved design, re-gate on deviation** |
| 6 | CLI & UI surfaces | W19, W21, M04, M05, M15 | 5 | **Tier 3 UI governance** |

\* W07 carries a Phase 0 disposition rather than a repair — its cited file does not exist at HEAD.

## Team & Budgets

Per `SUBAGENT-RULES.md` §16.1, every assignment declares read scope, write scope, tool ceiling, network permission, and spend ceiling. **Network and external API access is denied by default and is not granted to any assignment in this track.** All verification is local.

| Role | Agent | Write scope | Tool ceiling / wave | Notes |
|---|---|---|---|---|
| Orchestrator | `agent-teams:team-lead` | plan.md, TODO, handoffs | 40 | Owns sequencing, integration, gates. Never edits code. |
| C# implementer | `jvm-languages__csharp-pro` | `Windows/dotNET/**`, `Linux/Ubuntu/**` | 60 | Primary for W-series |
| Swift implementer | `swift-expert` (project-scoped) | `macOS/Sources/**` | 60 | Primary for M-series |
| Python implementer | `python-development:python-pro` | `Server/syxx_server/**`, `Server/tests/**` | 25 | W16 parity only; uv/ruff/ty |
| Security specialist | `security-scanning:security-scanning-security-auditor` | none (advisory) | 30 | Mandatory consult: waves 2–5 |
| Test specialist | `unit-testing:unit-testing-test-automator` | `**/tests/**`, `Syxx.Tests/**` | 50 | Owns red tests + golden vectors |
| Code reviewer | `git-pr-workflows__code-reviewer` | none (advisory) | 40 | Severity-tagged per §3.3 |
| Synthesist | `agent-orchestration:...-context-manager` | handoff docs | 20 | Required — more than two agents contribute |
| UI specialist | `ui-design:ui-designer` | `macOS/Sources/SyxxApp/**` | 25 | Wave 6 only |
| Accessibility reviewer | `ui-design:accessibility-expert` | none (advisory) | 15 | Wave 6 only, Tier 3 requirement |

**Roster correction (2026-08-12).** `multi-platform-apps:ios-developer` was the wrong specialist — its domain is iOS app development, while this codebase is a macOS SwiftPM package (library + CLI + privileged helper, no iOS target, no UIKit). A `swift-expert` existed in this repo's own `subagents/` library and was not in the active configuration. Installed project-scoped at `.claude/agents/swift-expert.md` under the copy-first rule (`PLUGIN-RULES.md` §2.3, `SUBAGENT-RULES.md` §§2.5-2.6): provenance recorded in frontmatter, never edited in place at source, adapted on copy to satisfy §12's output contract and §14.3's structure, with the iOS-app material removed and repo constraints added. Wave 6's UI specialist should be checked against `subagents/` the same way before use.

Skills equipped on assignment: `swiftui-pro`, `dotnet-contribution:dotnet-backend-patterns`, `python-development:python-testing-patterns`, `python-development:python-type-safety`, `developer-essentials:debugging-strategies`, `developer-essentials:error-handling-patterns`, `security-scanning:security-requirement-extraction`, `ui-design:accessibility-compliance` (wave 6).

Every assignment packet carries: the specific defect IDs, their `findings.md` entries, the cited file paths, the authoritative decisions from spec.md, and the read-only status of `rules/`. It does **not** carry the orchestrator transcript, unrelated defects, or full file dumps.

## Phase 0: Baseline & Triage

Blocking. No repair work begins until this phase is accepted.

### Tasks

- [x] Task 0.1: Establish and record the baseline commit. Capture `git rev-parse HEAD`, the working-tree diff stat, and the disposition of the 53 modified code files. Confirm no `rules/*-RULES.md` is modified or staged.
- [x] Task 0.2: Classify all 36 defects against HEAD into exactly one of four states, with file:line evidence for each: **live** (defect present as described), **repaired** (code changed and the repair is sound — verify soundness, not merely difference), **absent-code** (cited construct does not exist at HEAD, e.g. W07), **moved** (code relocated; re-cite the true location, e.g. W13 → `LinuxSecretStore.cs:541`/`:565`).
- [x] Task 0.3: For every **repaired** classification, record what makes the repair sound — the mechanism, not the absence of the old line. A repair that merely deletes the defective branch without covering the failure scenario is reclassified **live**.
- [x] Task 0.4: For every **absent-code** classification, record whether an associated *feature* is also missing (W07: the persisted-pin-file source is gone along with the defect) and file it as an owner question. Do not restore features under a bug track.
- [x] Task 0.5: Re-verify each **live** defect's cited line still matches the described construct; correct drifted line numbers in a triage table appended to this plan.
- [x] Task 0.6: **Quarantine audit** (bounded discovery — see below). Produces an inventory, not remediation.
- [x] Task 0.7: Record the LicenseManager.cs overlap: capture the exact working-tree delta from `warp-hardening_20260811` Task 4.3 so W17/W18 apply on top of it rather than reverting it.
- [x] Task 0.8: Emit the Phase 0 durable handoff (`BUDGET-RULES.md` §Durable Handoff Template) and commit the triage table.

### W16 — RATIFIED 2026-08-12 (owner ruling)

> **Ratified:** `MaxPlausibleValiditySpan = 63_072_000` seconds (730 days) across Swift, Python, and C#.
> **Required remediation:** live cross-runtime value and boundary tests, backed by a shared fixture; restore or replace quarantined Swift coverage.
> **Follow-up:** bounded audit of quarantined Swift tests for additional dead security-relevant coverage.

**Status at HEAD: already unified and verified sound.** The three-way split described in `findings.md` was pre-revert state. All three runtimes carry `63_072_000` and implement the same algorithm — RFC 1982 serial arithmetic mod 2³², identical check order (malformed → span → in-window), equivalent wraparound handling (`unchecked` / `&-` / `& 0xFFFFFFFF`), same B047 rationale.

| Runtime | Constant | Guard |
|---|---|---|
| C# | `Syxx.Kit/DNSSECVerifier.cs:153` | `:179` |
| Swift | `SyxxKit/DNSSECVerifier.swift:180` | `:198` |
| Python | `Server/syxx_server/resolver.py:97` | `:763` |

Verified: **a window exactly at the limit is accepted** in all three (C# rejects on `>`, Swift guards `<=`, Python rejects on `>`), so the boundary assertion below encodes existing behavior rather than changing it.

**The real defect is enforcement drift, not the value.** The constant is a bare literal in three files with no cross-runtime binding. C# is the only runtime where a live test references it (`Syxx.Tests/TriStateAndDnssecTests.cs:129`); the Swift assertions sit in `macOS/Tests/_quarantine/SyxxKitTests.swift.phase32-compile-break`, which never compiles, and Python has no test for `_rrsig_window_valid` at all. That is how it diverged the first time.

### Quarantine audit (Task 0.6) — bounded

`macOS/Tests/_quarantine/` holds **44 files, ~9,839 lines** against **10,448 live lines** in `Tests/SyxxKitTests/`. The `SyxxKitTests` target declares no `path:`, so SwiftPM resolves it to `Tests/SyxxKitTests` and `Tests/_quarantine` is outside every declared target — **nothing in it compiles, including the bare-named `.swift` files** that look active. Three suffix classes are present: `.phase{1,3,32}-compile-break`, `.phase32-active-dup`, and unsuffixed.

Audit output only — enumerate each file, why it is non-compiling, whether it holds unique security-relevant coverage not present in a live test, and a remediation class of **restore / migrate / duplicate-into-live / delete**, each with an owner and priority. **No remediation is performed in this track.**

Directly relevant to Waves 1–2: `SynthesisEngineTests` (34 KB), `SynthesizerDatapointWiringTests` (36 KB), `MatrixGoldenCorpusTests`, `Phase1SynthesisGateTests`, `ScorersContractTests`, `Stage0GoldenCorpusTests`, `SoWhatLayerTests`. Working hypothesis to test during the audit: the synthesis honesty defects regressed because their coverage went dark. If confirmed, Wave 1's red tests should be written against the quarantined assertions rather than authored fresh.

### Verification

- [x] All 36 defects carry a classification with file:line evidence.
- [x] Triage table committed; counts reconcile to 36 (see triage.md).
- [x] W16 recorded as ratified-and-repaired; remediation moves to Task 2.7, not a value change.
- [x] Quarantine inventory complete with per-file remediation class, owner, and priority.
- [x] `rules/` confirmed untouched.

---

## Phase 1: Synthesis Honesty Matrix

Seven defects in one evidence-scoring pipeline. Repairing them independently is what produced the second-occurrence pattern; they share fixtures and are designed together.

**Core invariant this wave restores:** evidence that was never usably measured must contribute to neither support nor coverage, and evidence that contradicts a diagnosis must never count toward it.

### Tasks

- [x] Task 1.1 (red): Build the shared synthesis fixture set — a run with a hard-excluded threat, a run where every section timed out, a run with a non-usable section carrying an observed indicator, a run whose only firing indicators contradict the diagnosis, a thin-but-conclusive section, and a run with unmappable rules in the spec.
- [x] Task 1.2 (red): **W02** — assert `DeterminativeState.Inconclusive` never yields Grade B / `Severity.Clean` / a "looks clean" headline, and that `hasThreatOverhang` counts `HardExcluded` suppressions. Cover both `ReportCard.cs:88` and `:83`.
- [ ] Task 1.3 (red): **W01** — assert a control-domain DNSSEC canary success cannot hard-exclude `dnsSpoofing`/`dnsInterception` for a *target* domain.
- [ ] Task 1.4 (red): **W03** — assert NXDOMAIN/REFUSED/SERVFAIL never produces affirmative clean support, and never counts toward the `MajorCount >= 2` conclusive-clean requirement.
- [x] Task 1.5 (red): **W20 / M02 / M09 / M10** — assert coverage excludes non-usable sections' weight (C# must match `MatrixDeterminativeEngine.swift:785`), contradicting indicators never enter `supportingIndicators` / `hasDecisive` / `alignedSupport`, the quality floor admits thin-but-conclusive sections, and unmappable rules stay out of the coverage denominator.
- [x] Task 1.6 (green): Implement W01, W02, W03, W20 in `Syxx.Kit/Synthesis/`. Consult the security specialist before changing any exclusion threshold or indicator strength.
- [ ] Task 1.7 (green): Implement M02, M09, M10 in `SyxxKit/Synthesis/`.
- [ ] Task 1.8: Cross-runtime parity check — C# and Swift produce identical determinations for every Task 1.1 fixture.
- [ ] Task 1.9: Code review (severity-tagged); resolve `[blocker]`/`[high]` before commit.

### Verification

- [ ] `dotnet test Windows/dotNET/Syxx.Tests` and `swift test` green on the new fixtures.
- [ ] No fixture yields an affirmative clean result from unmeasured or suppressed evidence.
- [ ] Parity assertions pass in both directions.
- [ ] Commit: `[review-fixes_20260812] wave 1: synthesis honesty matrix (W01-W03, W20, M02, M09, M10)`

---

## Phase 2: Comparator Tri-State & Classification Parity

Eight defects. W04/W05/W06 are one method — `DetectUnanimousTampering` — and are designed as a single coherent tri-state, not three patches. M03 is its Swift twin and moves in the same commit.

**Core invariant:** insufficient corroboration returns `Undetermined`; it is neither clean nor tampering. Benign network behavior (RCODE variation, CDN rotation, same-provider divergence) is never graded as confirmed tampering.

### Tasks

- [ ] Task 2.1 (red): Tri-state fixtures — null DoH set, <2 DoH endpoints with records, unanimous UDP with matching local answer, mixed benign RCODEs (NoError + SERVFAIL + REFUSED) over identical A records, and a genuine unanimous redirect.
- [ ] Task 2.2 (green): **W04/W05/W06** — redesign `DetectUnanimousTampering` to return `Undetermined` on every insufficient-evidence path, restrict the RCODE-disagreement arm to states that actually indicate tampering, and make `inconclusiveFromUnanimous` reachable. Verify no caller treats `Undetermined` as clean.
- [ ] Task 2.3 (green): **M03** — Swift twin returns `.undetermined` for DoH-unavailable and <2-endpoint cases; assert C#/Swift agreement on every Task 2.1 fixture.
- [ ] Task 2.4 (red+green): **W11** — same-provider disjoint answers must not emit an affirmative tampering finding. Include the finding's own reproduction: `Classify({"104.16.132.229"}, {"104.17.25.14"}, ["ns1.cloudflare.com"], …)`.
- [ ] Task 2.5 (red+green): **M14** — disjoint CDN edge rotation is not `.unstable` without corroboration; restore the caveat to the detail string.
- [ ] Task 2.6 (red+green): **M13** — an ECS state that cannot be determined from the client is not reported as `.notForwarded`.
- [ ] Task 2.7: **W16 binding** (value ratified at `63_072_000`; no runtime value changes). Author a language-neutral shared fixture — JSON or a generated constants artifact — carrying the normative value and the boundary cases, consumed by all three runtimes. Three independent tests each hardcoding `63_072_000` is strictly worse than one fixture: the fixture *is* the cross-runtime contract.
- [ ] Task 2.7a: Add a **live** test in C#, Swift, and Python asserting, from the shared fixture: the configured limit is exactly `63_072_000`; a window exactly at the limit is accepted; a window one second above is rejected; and serial-arithmetic wrap-adjacent cases do not bypass the guard.
- [ ] Task 2.7b: Relocate the DNSSEC window assertions out of `macOS/Tests/_quarantine/SyxxKitTests.swift.phase32-compile-break` into a compiling target so Swift coverage actually runs. Scope strictly to the DNSSEC assertions — broader quarantine remediation is follow-on work per the Phase 0 audit.
- [ ] Task 2.8: Security review of the tri-state (does any path let a real redirect fall into `Undetermined`?) and code review.

### Verification

- [ ] Every insufficient-evidence path returns `Undetermined`; no dead tri-state arm remains.
- [ ] Benign-RCODE and CDN fixtures produce no tampering finding on either runtime.
- [ ] C#/Swift/Python DNSSEC span identical; asserted by test.
- [ ] `pytest`, `dotnet test`, `swift test` green.
- [ ] Commits: comparator tri-state; classification honesty; W16 parity (separate, gate-referenced).

---

## Phase 3: Secrets, Keys & Licensing

Six defects. Every one can silently destroy user state — a second Witness identity minted over a live key, a paid licence reverting to trial, an unbounded free trial, an HMAC that accepts a mismatch. Security specialist consult is mandatory before each green step.

### Tasks

- [ ] Task 3.1 (red): **M06** — `constantTimeEqual` must reject length deltas that are multiples of 256 (a=32 B, b=288 B). Assert the length term is `a.count == b.count ? 0 : 1`, not a truncated XOR.
- [ ] Task 3.2 (green): Implement M06. Boundary and negative vectors per `BUDGET-RULES.md` §Test Budget.
- [ ] Task 3.3 (red): **W09** — assert an existing Roaming-path keystore is still readable after the Local-path move, and that an unreadable/ambiguous store returns `SecretExistence.Unknown`, never `Absent`. `Absent` is the only state that licenses minting a new identity; it must be unreachable while a legacy store may exist.
- [ ] Task 3.4 (green): Implement W09 with a legacy-path read fallback mirroring the sibling `GetLegacyStaticEntropy` migration.
- [ ] Task 3.5 (red+green): **M08** — detect the `wrapped:v1:` prefix on raw bytes, never by UTF-8-decoding the whole blob. Assert a wrapped key round-trips across a simulated restart with the cache cleared, and that `checkKeyPresence()` cannot report `.present` for a key `loadKey()` will fail to return.
- [ ] Task 3.6 (red+green): **W13** — at the relocated site, apply the time bound before the blocking reads and drain stdout/stderr concurrently. Assert `Exists` honors its never-throws / `Unknown`-on-timeout contract and cannot hang app startup.
- [ ] Task 3.7 (red+green): **W17/W18** — applied on top of the `warp-hardening` working-tree delta. A trial signature must be reproducible across launches (no per-call ephemeral salt), and prior-build signatures must either validate through an explicit legacy key candidate or be rejected without silently re-anchoring the trial window. Assert an upgrade does not grant a fresh trial and a secret-store failure does not produce an unbounded trial.
- [ ] Task 3.8: Security review of the whole wave; code review.

### Verification

- [ ] No path mints a second identity while a legacy store may hold a key.
- [ ] Upgrade simulation preserves licence and trial state; no fresh-trial grant.
- [ ] HMAC negative vectors pass; no length-delta acceptance.
- [ ] `dotnet test`, `swift test` green.
- [ ] Commit boundary per defect cluster (M06 / W09+M08 / W13 / W17+W18).

---

## Phase 4: Parsing & Network Trust Gates

Eight repairs plus the W07 disposition. Parsing code gets negative, boundary, and malformed-input vectors per the test budget.

### Tasks

- [ ] Task 4.1 (red+green): **W10** — non-initial IPv6 fragments must not be parsed as UDP/DNS. Match the IPv4 guard's behavior; assert a fragment-offset-nonzero packet yields no capture record.
- [ ] Task 4.2 (red+green): **W14/W15** — reclassifying an address range must not silently change gate behavior. Repair the `== PublicRoutable` allow-lists so Teredo/6to4 are not rejected as non-public, and restore `198.18/15` recognition in the rogue-DHCP off-link test and `IsOwnNetworkAddress`. Assert each consumer of `IPAddressClass` explicitly, so a future reclassification fails a test instead of a user's scan.
- [ ] Task 4.3 (red+green): **W08** — restore documented precedence (env var first, persisted file as fallback) or change the documentation to match a deliberate new order; the code and its doc comment must agree. Assert a rotated env-var pin is honored while a stale file exists.
- [ ] Task 4.4 (red+green): **W12** — the replay nonce becomes non-optional at the call boundary, or every call site supplies one and a test asserts none can omit it. `EvaluateFreshness` must not silently skip replay checking. Cover `WarpCommand.cs:48` and `Program.cs:701`.
- [ ] Task 4.5 (red+green): **M01** — `::1` is rejected by the restricted-DNS gate. Assert every IPv6 loopback/link-local/multicast/ULA form, including the unsatisfiable-predicate case that let `::1` through.
- [ ] Task 4.6 (red+green): **M07** — the helper's CLI allowlist resolves to the real app bundle Resources path for a non-bundled SMJobBless daemon. Assert a shipped-layout path is accepted and a path outside it is rejected.
- [ ] Task 4.7 (red+green): **M11** — restore the single-endpoint contract: a failing caller-supplied DoH endpoint reports the encrypted path as blocked, not working, and the probe does not silently query third-party endpoints the user never selected. Security specialist consults on the privacy dimension.
- [ ] Task 4.8: **W07 disposition** — apply the Phase 0 ruling. If the persisted-pin-file feature is out of scope, record that and close W07 as absent-code with no change; if the owner wants it restored, it becomes a separate track.
- [ ] Task 4.9: Security review (SSRF gates, replay, pin precedence, privileged-helper boundary); code review.

### Verification

- [ ] Malformed/fragmented packet vectors produce no fabricated records.
- [ ] Every `IPAddressClass` consumer asserted; reclassification cannot silently change a gate.
- [ ] Replay nonce cannot be omitted from any Warp call path.
- [ ] `::1` and every IPv6 loopback form rejected by the helper gate.
- [ ] `dotnet test`, `swift test` green; commits split by cluster.

---

## Phase 5: Signed-Evidence Contract v5 (M12)

**Owner-approved design — recorded in spec.md §M12. Any deviation from it re-gates to the owner before implementation.** `BUDGET-RULES.md` §Compatibility grants this wave extra budget: an incorrect shortcut here creates permanent operational cost.

### Tasks

- [ ] Task 5.1: Document the byte-level canonical schema for each historical layout before writing code. If v2/v3/v4 each underwent multiple silent byte-layout shifts, enumerate each layout as a distinct profile even where the recorded `formatVersion` is identical. This document is the contract; code follows it.
- [ ] Task 5.2 (red): Author the cross-runtime golden vectors in both Swift and C# — legacy real TTL, legacy zero TTL, v5 real TTL, and nonzero / zero / boundary / absent-normalized TTL cases. Vectors are fixed bytes committed to the repo, not regenerated at test time.
- [ ] Task 5.3 (green): Introduce format **v5**, whose canonical representation always serializes `r.Ttl`. All new sealers emit v5 only. Zero is written only when the observed TTL is genuinely zero.
- [ ] Task 5.4 (green): Implement the named canonicalization profiles `legacyRealTTL`, `legacyZeroTTL`, `v5RealTTL`. Verification returns the matched profile (`verifiedCanonicalization: .legacyZeroTTL`). Legacy fallback is bounded to the exact affected versions and build window — no open-ended serializer retry.
- [ ] Task 5.5 (green): A `legacyZeroTTL` match returns a semantic/provenance warning, not a bare valid result: "Signature is valid, but TTL was canonicalized as zero due to a historical producer defect." v5 verification accepts the real-TTL serializer only, with no fallback.
- [ ] Task 5.6: Assert C#/Swift byte-identical output for every golden vector, in both directions.
- [ ] Task 5.7: Security review of the profile-selection logic — can a chosen profile be influenced by attacker-controlled input to force a weaker match?
- [ ] Task 5.8: Document the contract alongside `formatVersion` and add a CHANGELOG migration note.

### Verification

- [ ] Golden vectors pass on both runtimes; bytes identical.
- [ ] A regression-era snapshot verifies and reports `legacyZeroTTL` with its warning.
- [ ] A v5 snapshot with a tampered TTL fails; no fallback rescues it.
- [ ] Profile selection cannot be steered by input.
- [ ] Commit: contract doc + vectors, then implementation.

---

## Phase 6: CLI & UI Surfaces

Two CLI defects, one relocation, two UI defects. **M04 and M05 are Tier 3 UI work** — `IMPLEMENTATION-RULES.md` §7 and `SUBAGENT-RULES.md` §8 apply in full: UI specialist, accessibility reviewer, and test/quality review are mandatory, and no UI implementation begins until the orchestrator approves ownership and sequencing.

### Tasks

- [ ] Task 6.1 (red+green): **W21** — `--verbose` sets its flag and continues; `--help` and `--version` still terminate. Assert every ordering: `-v --help`, `--verbose --version`, `-v` alone, and that no live probe fires when a terminal flag is present.
- [ ] Task 6.2 (red+green): **M15** — flag loop starts at the first real argument; `argv[0]` is never the domain. Assert with and without leading flags.
- [ ] Task 6.3 (green): **W19** — relocate the default-domain guard out of `SyxxUI/Views/ConclusionsView.xaml.cs` into shared `net10.0` code so both Conclusions surfaces call one testable path. Assert a blank domain never silently scans `FirstRunScan.DefaultDomain`, and that the WinUI caller becomes a thin delegate with no policy of its own. State explicitly in the commit that `SyxxUI` itself was not compiled here and why.
- [ ] Task 6.4 (red+green): **M05** — `.displayMismatch` and `.untrustedSigner` must not render as `.signed`. Enumerate the result cases exhaustively so a future case cannot fall to `default:`. Assert the card's colour state, not only the returned string.
- [ ] Task 6.5 (red+green): **M04** — no fabricated degradation badge. When no content comparison occurred, the Mirror surface says so; it does not render a green "0% / Identical" badge or a "no differences detected" panel. Confirm which branches are production-reachable given the retired `sectionInputs.warp` path.
- [ ] Task 6.6: Accessibility review of the changed Mirror and Witness states — colour is not the sole carrier of the result, contrast holds, screen-reader text matches the shown state.
- [ ] Task 6.7: UI specialist review and code review.

### Verification

- [ ] Terminal CLI flags always terminate; no live probes on `--help`.
- [ ] No UI surface renders an affirmative clean state from an unmeasured section.
- [ ] Accessibility review passed; state conveyed non-visually.
- [ ] `dotnet test` (net10.0 projects), `swift test` green.
- [ ] W19's uncompiled-on-this-host status recorded honestly in the commit and in MEMORY.md.

---

## Final Verification

- [ ] All 36 defects resolved or formally retired with recorded evidence; counts reconcile.
- [ ] Every live defect bound by a regression test that was observed failing first.
- [ ] Full local ladder green: `cd Server && OTEL_TRACES_EXPORTER=none uv run pytest tests/ -q`; `dotnet build` + `dotnet test` for all `net10.0` projects; `swift build` + `swift test`; `ruff check`; `ty`; `swiftlint --strict`.
- [ ] Cross-runtime parity asserted by test for: DNSSEC span (shared fixture), comparator tri-state, coverage accumulation, signed-payload canonicalization.
- [ ] W16 closure conditions met: value ratified, three live suites passing off the shared fixture, quarantine audit delivering owner/priority/remediation list.
- [ ] Not-run validation stated explicitly with residual risk: `SyxxUI`/`SyxxUI.Tests` (Windows-only, uncompilable here), Windows and Linux runtime test runs, hosted CI (off until ~2026-08-27).
- [ ] Pre-existing failures distinguished from new ones — the base tree has 12 known `Syxx.Tests` failures on this macOS host; verify the count and names are unchanged, never merely the count.
- [ ] `MEMORY.md`, `CHANGELOG.md`, and the track TODO current.
- [ ] Final diff reviewed for scope, unintended changes, generated artifacts, and sensitive data. No secrets committed.
- [ ] `rules/*-RULES.md` confirmed unmodified across every commit in the track.
- [ ] Owner accepts Tier 3 integration.

## Risks

| Risk | Mitigation |
|---|---|
| Findings describe a reverted tree; repairs applied to code that no longer exists | Phase 0 blocking triage with four-way classification |
| Cross-runtime fixes land separately and re-diverge | Twins ship in one commit with a parity assertion |
| Signed-byte change breaks historical evidence | Owner-approved v5 + bounded named profiles + golden vectors |
| Honesty fixes over-correct into false positives | Wave 2 fixtures cover benign behavior explicitly; security review each wave |
| Windows-only surface cannot be validated here | W19 relocated to shared code; residual gap stated, never claimed green |
| Agent spend under owner budget constraint | Declared ceilings per assignment, sequential waves, no network access, reused standing team |

---

_Generated by Conductor. Tasks are marked `[~]` in progress and `[x]` complete. `[!]` marks blocked-on-verification._
