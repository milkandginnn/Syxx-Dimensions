# ENGINE-PHASE01-PLAN — the unblocked slice of the reasoning-engine remediation

**Author:** Syxx Neural Planner (design/plan only — no feature code lands here)
**Date:** 2026-07-11
**Status:** Engineer-ready. Design is APPROVED (`REASONING-SYSTEM-DESIGN.md` v2.1). This plan covers **only**
the fusion-rule-**agnostic** slice: Phase 0 (engine side of the golden harness + bake-off *setup*) and
Phase 1 (the reviewer-#9-confirmed structural honesty gates + the `Stage5Presentation` model).
**Explicitly out of scope (deferred, gated on the Phase-0 bake-off):** the fusion-rule selection and the
Stage-3/4 combination + clean-support-correctness + TBM-readout rewrite (design Phases 3-7). Those are a
**later pass** and are called out as blockers below.
**Handoff target:** `syxx-neural-executor` (core + server). Stage-5 rendering is `syxx-gui-architect`/Codex.
**Every `file:line` below was re-opened in the live tree on 2026-07-11** (Cline + Codex are editing
concurrently — re-grep the symbol before you touch the line; several design-doc line numbers had already
drifted, noted inline).

---

## 0. Plan overview

### Goal

Make the engine **honest under the current (Dempster) arithmetic** without committing a fusion rule: no
confirmed threat may read `clean`; no critical section that ran inconclusive/degraded/invalidated may ride
to `clean`; no single signal may name a threat with un-referred confidence; the poisoned-local-vs-consensus
split and the three honesty signals become *visible*; and both platforms agree to the integer. Land the
additive `Stage5Presentation` DTO **first** — Codex (CX-1..CX-3) is blocked on it.

### The honesty spine (why this ordering is safe)

Every Phase-1 step is **monotone toward honesty**: each one either *removes* a false-`clean` / false-`tampered`
/ false-confident path, or *adds* a referral / a reported figure. **No Phase-1 step introduces a transient
false-secure state.** The only intra-phase ordering constraint is P1.9↔P1.10 (surfacing a divergence in
Stage 5 must not reach production before the discrepancy-forbids-clean gate, or the determination could read
`clean` beside a matrix showing a local outlier). That pairing is called out at both steps.

The wash-out *arithmetic* (three clean sections stacking under Dempster) is **not** fixed here — that is the
deferred Phase-3 pass. Phase 1 instead installs **deterministic verdict-level gates** (`.positive`-blocks-clean,
the I8 committed-mass floor, critical-section gate, discrepancy gate) *outside* the DST number, so a confirmed
threat that still gets washed below the floor falls through to **inconclusive+referral**, never `clean`. Full
naming (`REWRITE` at high confidence) arrives with Phase 3/4.

### Phases ordered by mission risk

- **Phase 0 (engine side)** — the golden-vector oracle + integer-% determinism + the offline bake-off harness.
  Enables regression-proofing and cross-platform parity; selects nothing yet.
- **Phase 1 (structural honesty gates)** — the rule-agnostic gates that close the false-secure classes, plus
  the additive Stage-5 presentation model and the answer-matrix/honesty-signal population.

### Hard dependency graph

```
P0.1 rounding/integer-% parity ─┐
                                ├─> P0.2 oracle adapter ─> P0.3 seed RED→GREEN + phase-greening map
P1.0 Stage5 DTOs (additive) ────┘                          │
   │  (Codex-unblock: land FIRST)                          └─> P0.4 offline bake-off harness (feeds O3)
   ├─> P1.9 populate provenance + answer matrix ──(MUST precede/pair with)──> P1.10 discrepancy-forbids-clean
   ├─> P1.11 honesty signals + completeness + generatedAt (needs P1.4 count)
   └─> P1.4 K/M completeness (needs invocation-profile param)

P1.1 invalidated-dominates ─┐
P1.2 critical-not-conclusive-forbids-clean ─┤
P1.3 degraded discount (shares the critical predicate with P1.2) ─┤ (independent, monotone)
P1.5 .positive-blocks-clean ─┤
P1.6 I8 committed-threat-mass floor ─┤
P1.7 min-corroboration → referral ─┤
P1.8 NE5 Δ1 Windows parity fix ─┘
```

All Phase-1 gates are independent of each other and independent of the fusion rule. They depend on Phase 0
only for **regression proof** (the corpus), not for correctness — each carries its own real-data acceptance
bar and can land before the full corpus exists.

### Recommended landing sequence

`P1.0` → `P0.1` → `P0.2` → `P0.3` → `P1.1` → `P1.5` → `P1.6` → `P1.2` → `P1.3` → `P1.7` → `P1.8` →
`P1.4` → `P1.9` → `P1.10` → `P1.11` → `P0.4`. (P1.0 first for Codex; the false-secure gates P1.1/P1.5/P1.6
next as highest mission risk; matrix pair P1.9/P1.10 late so its dependency P1.0 is in.)

---

## Phase 0 — golden-vector oracle + determinism + bake-off setup (engine side)

### P0.1 — Cross-platform rounding parity + a shared integer-% gate primitive

- **Action:** Make the confidence/verdict/name/referral comparisons compare **exact integer percentages** with
  one rounding mode on both platforms; add a single `roundPercent(_:)` gate helper used everywhere a float
  crosses a threshold. Align C# `Math.Round` (banker's/ToEven) to Swift's away-from-zero.
- **macOS:** `SynthesisEngine.swift:242` (`Int((verdictBetP * 100).rounded())` — already away-from-zero; keep,
  factor into the helper) and the gate at `:250-254`.
- **Windows:** `SynthesisEngine.cs:224` (`(int)Math.Round(verdictBetP * 100)` → `Math.Round(x, MidpointRounding.AwayFromZero)`)
  and the gate at `:232-236`.
- **Serves:** NE7 Δ5 (rounding straddles the 75% gate); design §10.2 / D2 / AMEND-O3.
- **Effort:** S.
- **Real-data acceptance bar:** feed the same real `verdictBetP` near a `.5` boundary (e.g. a capture that lands
  at `round(100·BetP)=74.5`) through **both** production binaries; both print the **same integer** confidence and
  the **same** referral decision. Observed by running both, not by a shared unit fixture.
- **Parity:** this *is* a parity fix — the two platforms must agree to the integer; add the `74.5` boundary golden
  vector (NE7 Δ5 seed).
- **Approval gate:** none (no new user-visible strings; the integer already renders).
- **Depends on:** nothing.

### P0.2 — Engine-side golden-vector oracle adapter

- **Action:** Provide the engine half of the STAGE0 contract: a deterministic adapter that maps a fixture's
  `input.evidence` / `input.sectionStatuses` / `input.unobserved` onto `SynthesisEngine.synthesize(...)` /
  `Synthesizer.conclude(...)` and extracts the two assertion tiers — `_exact` (integer `verdict` / `withinClass`
  membership / `referral` / `completeness`) and `_epsN` (raw `belief[]` / `uncertainty` / `conflict`). Cline
  (CL-3) owns the runner + corpus + real captures; this step is only the engine-output extraction the runner
  calls.
- **macOS:** new test-target adapter alongside `macOS/Tests/SyxxKitTests/SynthesisEngineTests.swift` (existing
  offline-invariant pattern is the template); consumes `SynthesisEngine.synthesize` (`SynthesisEngine.swift:105`).
- **Windows:** new test-target adapter alongside `Windows/Syxx.Tests/SyxxKitTests.cs`; consumes
  `SynthesisEngine.Synthesize` (`SynthesisEngine.cs:63`).
- **Serves:** design §10.1 (shared golden-vector suite); STAGE0 contract "expected oracle" + "runner requirements".
- **Effort:** M.
- **Real-data acceptance bar:** the adapter, run over ≥1 **real capture per mechanism class** (Cline's corpus),
  reproduces the design-correct `_exact` gates for the vectors the current engine already satisfies and reproduces
  **red** for every unfixed NE seed — observed by running the adapter on both platforms, not by asserting a mock.
- **Parity:** byte-identical fixture in, identical `_exact` integers out on Swift + C#; any divergence fails CI.
- **Approval gate:** none.
- **Depends on:** P0.1 (integer-% extraction must be settled first).

### P0.3 — Seed RED→GREEN oracle + per-vector phase-greening map + never-false-secure invariant

- **Action:** Encode the NE1-NE7 seeds (STAGE0 §"Seed corpus") as expected-design vectors, and add two
  engine-side controls the runner needs: (a) a **`greensAtPhase` tag per vector** — because several seeds
  (NE1 Δ1 *naming* REWRITE, NE1 Δ2 *inversion*) only reach their full `_exact` verdict after the deferred
  Phase-3/4 fusion pass, Phase 1 must gate CI on the Phase-1-reachable subset while those stay expected-red; and
  (b) a **universal invariant asserted on ALL vectors at every phase**: no vector whose design label is a threat
  class or `referral` may ever produce `verdict ∈ {clean}` / a green success state. This never-false-secure
  assertion is the honesty spine and must hold even while a naming vector is still red.
- **Cross-platform:** same map + invariant on both runners (Cline owns the runner file; this step supplies the
  engine-side expected values and the invariant predicate).
- **Serves:** design §11 Phase-0/Phase-1 acceptance bars; STAGE0 §"Seed corpus" + §"Lanes"; honesty ordering
  (this plan §0 spine).
- **Effort:** M.
- **Real-data acceptance bar:** every NE seed reproduces **red** on both platforms *before* any Phase-1 step
  lands; after Phase 1, every **Phase-1-tagged** seed is green and **no** seed (Phase-1 or later) ever emits a
  false-`clean`. Observed by running the corpus on both platforms.
- **Parity:** identical `greensAtPhase` map and invariant on both.
- **Approval gate:** none.
- **Depends on:** P0.2.
- **COORDINATION / schema flag to Cline:** the STAGE0 schema has **no `greensAtPhase` (or `expectedRedUntil`)
  field** and **no universal never-false-secure assertion** — flag both to Cline per the contract's "If you need
  a schema field that isn't here, flag it" rule. Do not redefine the schema unilaterally.

### P0.4 — Offline fusion-rule bake-off harness (setup only; selects nothing)

- **Action:** Build an **offline, test-target** harness that can evaluate candidate combination functions
  (Dempster / cautious / Yager / Dubois–Prade / weighted-mass, design §4.2) over the golden corpus and emit, per
  candidate, (i) adversarial-vector pass/fail and (ii) a **float-determinism measurement** (the cross-platform
  bit-reproducibility profile that sets the `_epsN` tolerance `ε`). **It must not touch the production
  `synthesize()` path** — the shipped rule stays Dempster; this is the input to the deferred selection, not the
  selection.
- **macOS/Windows:** new offline harness in each test target; wraps candidate rules over the same corpus P0.2 feeds.
- **Serves:** design §4.2 / §11 Phase 0 (bake-off *setup*); O3 determinism spike.
- **Effort:** M.
- **Real-data acceptance bar:** the harness produces a recorded pass/fail matrix + a determinism profile for each
  candidate over the real-capture corpus; Dempster's production output is **unchanged** (diff the shipped verdict
  on the corpus before/after — must be identical). Observed by running.
- **Parity:** the determinism measurement itself is the parity artifact.
- **Approval gate:** none (offline).
- **Depends on:** P0.3.
- **GATES THE LATER PASS:** the *result* (which rule wins, and `ε`) is **O3 open** — see Blockers.

---

## Phase 1 — structural honesty gates (fusion-rule-agnostic)

### P1.0 — `Stage5Presentation` DTO twins (additive, no behavior change) — **LAND FIRST**

- **Action:** Add the STAGE5-CONTRACT DTOs exactly as specified: the optional additive `stage5` property plus
  `Stage5Presentation`, `ProvenancePartition`, `DirectObservation`, `InferredFinding`, `UnavailableDatapoint`,
  `AnswerMatrixRow` / `VantageAnswer` / `AnswerComparison`, `HonestySignals`, `MechanismDetermination` /
  `CredalMeasure` / `WithinClassEntry`, `EvidenceCompleteness`, `DataRegime`, `PrioritizedFinding` /
  `FindingPriority` / `PriorityFactors`, `CopyToken`, and all enums — with the CX JSON/Codable settings, the
  §13 validator, and the shared v1 fixtures. **Populate nothing yet** (production writes `stage5 = nil`;
  renderers show the approved unavailable/inconclusive state).
- **macOS:** add `public let stage5: Stage5Presentation?` to `Conclusion` (`Conclusion.swift:11-62`; extend the
  memberwise init at `:41-62` with a defaulted `stage5: Stage5Presentation? = nil` and the decode-tolerant
  `init(from:)` at `:66-81` with `decodeIfPresent`). New file `macOS/Sources/SyxxKit/Synthesis/Stage5Presentation.swift`.
- **Windows:** add `public Stage5Presentation? Stage5 { get; init; }` to the `Conclusion` record
  (`Conclusion.cs:271-305`, additive init-only like `Advisories`/`Uncertainty` at `:285-300`). New file
  `Windows/Syxx.Kit/Synthesis/Stage5Presentation.cs` with `System.Text.Json` camel-case + string-enum + omit-null
  settings (STAGE5 §11).
- **Serves:** STAGE5-CONTRACT §3-§13, §16 step 1; unblocks Codex CX-1..CX-3.
- **Effort:** L (large surface; may split into DTO commit + validator commit + parity-fixture commit — keep each
  reviewable).
- **Real-data acceptance bar:** on both production binaries, a real run still emits a truthful legacy conclusion
  with `stage5 == nil`; a legacy persisted conclusion **without** `stage5` decodes (does not throw, does not read
  as clean/zero); the shared v1 fixture decodes to equivalent object graphs on Swift + C# and **re-encodes
  byte-identically**. Observed by running both + the parity fixture round-trip.
- **Parity:** byte-canonical JSON parity is the contract (STAGE5 §11) — camelCase, lower-camel string enums,
  omitted optionals, `schemaVersion == 1`.
- **Approval gate:** **none on the DTOs/tokens** — STAGE5 §10 makes `CopyToken` values firm contract data that
  "may land before copy approval." The engine emits **tokens, not prose** (STAGE5 §2). Any *rendered* English is a
  `syxx-gui-architect` proposal.
- **Depends on:** nothing. **Priority: first** (task directive; Codex is blocked).

### P1.1 — `.invalidated` dominates `combine()`

- **Action:** Rank `.invalidated` above `.negative`/`.degraded` so a required-premise failure can never be
  laundered into clean-support. Order: `positive > invalidated(any) > inconclusive > degraded > negative`
  (design §5.2). Replace the `allSatisfy(.invalidated)` guard with `contains(.invalidated)` **after** the
  positive short-circuit.
- **macOS:** `Synthesizer.swift:57-65` (`combine`) — change `:61` `if ran.allSatisfy({ $0 == .invalidated })` to
  `if ran.contains(.invalidated)`.
- **Windows:** **two** methods carry the identical bug — fix **both**: `Synthesizer.cs:53-63` (`Combine`, line `:59`)
  **and** `Synthesizer.cs:65-75` (`CombineWithEvidence`, line `:71`). (Design cited only one; the live tree has two.)
- **Serves:** NE2 Δ5, NE7 Δ2; design §5.2 "Invalidated dominates".
- **Effort:** S.
- **Real-data acceptance bar:** a **real run** against a resolver that returns a **malformed/SERVFAIL target answer**
  while the neutral NX canary reads clean → Prophet reports **`.invalidated`/`.inconclusive`** (not `.negative`),
  emits **no** clean-support, and the run does **not** read `clean`. Observed by running against such a resolver
  (or a controlled malformed-response rig), not a fixture.
- **Parity:** SyxxKit ⇔ Syxx.Kit; note the extra Windows method — both must change or Windows diverges.
- **Approval gate:** none.
- **Depends on:** nothing.

### P1.2 — Critical section not conclusive (inconclusive/degraded) forbids `clean`

- **Action:** Implement the M3 clean-gate: if any **critical** section (Prophet, Mirror, Glutton per
  DESIGN.md:185-190) is not conclusive (`inconclusive` / `degraded` / `untested` / `invalidated`), forbid a
  `clean` verdict. (`untested`/`invalidated` already block via `blocksClean`; the **new** coverage is
  `inconclusive` + `degraded` for the three critical sections.) This is a **gate on eligibility, not a `Bel`
  multiplier** (R2/D3) — do **not** revive the DESIGN.md:182 `K/6`-halving-of-confidence framing.
- **macOS:** add a `criticalNotConclusive` predicate feeding the clean branch at `SynthesisEngine.swift:218`
  (`else if belClean >= config.cleanNameFloor && !cleanBlocked`). Critical set = `.prophet/.mirror/.glutton`;
  conclusive = `.negative || .positive`.
- **Windows:** twin at `SynthesisEngine.cs:193` (`else if (belClean >= config.CleanNameFloor && !cleanBlocked)`).
- **Serves:** NE7 Δ1, NE1 Δ5; DESIGN.md:169-172/182/185-190; design §3 M3, §5.2.
- **Effort:** S/M.
- **Real-data acceptance bar:** a **real run** with the target resolver returning SERVFAIL/NXDOMAIN for the target
  (Prophet `.inconclusive`) and the other five clean → verdict is **`inconclusive` with the hard-gate honesty line**,
  never `clean`. Observed by running against a blocking/failing resolver.
- **Parity:** critical set + predicate identical on both.
- **Approval gate:** the hard-gate honesty *sentence* is copy-gated → emit the `stage5.hard_gate.required_checks_missing`
  token (P1.11); the *gate* itself (verdict ≠ clean) is structural, no approval needed.
- **Depends on:** shares its critical-not-conclusive predicate with P1.3 — land together or P1.2 first.

### P1.3 — M1 degraded intrinsic-source discounting (pre-combination Shafer α)

- **Action:** Discount evidence originating from a `.degraded` section toward ignorance **at mass-construction
  time, before combination**: `m'(A)=α·m(A)` for `A≠Θ`, `m'(Θ)=α·m(Θ)+(1−α)`, `α∈(0,1)` (design §3 M1). Map each
  `EvidenceItem` to its section's combined status (via `EvidenceItem.section`) and apply α when that section is
  `.degraded`. **No post-combination penalty** (R2). Pair with the P1.2 gate so a `.degraded` **critical** section
  also forbids `clean` (shared predicate).
- **macOS:** mass construction in `SynthesisEngine.swift:134-188` (positive supports built at `:157-167`; α applied
  to `focusMass`/weight before `dempster()` at `:193-199`). `.degraded` is defined at `Conclusion.swift:336` and
  **produced** at `Synthesizer.swift:111` (latency), `:126` (ttlAnomaly), `:247` (mirror) — confirming the status
  exists to read.
- **Windows:** twin mass construction `SynthesisEngine.cs:96-157` (positives `:128-138`; combine `:162-173`);
  `.Degraded` at `Conclusion.cs:33`, produced in `Synthesizer.cs` (e.g. `:61/:73` combine, latency/ttl branches).
- **Serves:** NE1 Δ6, NE2 Δ7, NE4 Δ3, NE7 Δ4; design §3 M1, §5.2.
- **Effort:** M.
- **Real-data acceptance bar:** a **real run** where Glutton (critical) runs on a missing non-critical field
  (`.degraded`) and the rest clean → the run does **not** read `clean` (P1.2 gate) **and** the reported confidence
  is **strictly lower** than the same run with Glutton fully `.negative`, with the degradation surfaced. Observed by
  running the degraded probe against real infrastructure.
- **Parity:** the α value and the section→status mapping identical on both (this is a determinism-sensitive
  numeric — cover it with an `_epsN` vector, tolerance pending the bake-off).
- **Approval gate:** none (no new strings; α is an internal constant — record its derivation next to it).
- **Depends on:** P1.2 (shares the critical-not-conclusive predicate).
- **OPEN QUESTION (flag):** the α magnitude is a **calibration** constant (design §8, Phase 7). Ship a **defensible
  expert-elicited α** now (documented, signable), not a calibrated one — the calibrated value is deferred. Do not
  block Phase 1 on it.

### P1.5 — `.positive` blocks `clean` (engine-local predicate)

- **Action:** A section that affirmatively found tampering (`.positive`) must forbid a `clean` verdict, exactly as
  `untested`/`invalidated` do. Implement at the engine's `cleanBlocked` computation, **not** by widening
  `SectionStatus.blocksClean`.
- **macOS:** `SynthesisEngine.swift:113` — extend `let cleanBlocked = sectionStatuses.contains { $0.status.blocksClean }`
  to `{ $0.status.blocksClean || $0.status == .positive }`.
- **Windows:** `SynthesisEngine.cs:74` — extend `sectionStatuses.Any(s => s.Status.BlocksClean())` to also match
  `SectionStatus.Positive`.
- **Serves:** NE1 Δ1 (the reads-`clean` symptom); design §3 M4 / §5.4 I8 / §6.2; DELTAS NE1 Δ1 fix lever (1).
- **Effort:** S.
- **Real-data acceptance bar:** a **real DNSSEC-strip capture** (Prophet `.positive` via `dnssecCanaryStripped`) with
  the other five sections clean → verdict is **not `clean`** (falls to inconclusive+referral under current
  arithmetic; full REWRITE naming is the deferred Phase-3/4 pass). Observed by replaying a real strip capture through
  the production path.
- **Parity:** both platforms.
- **Approval gate:** none.
- **Depends on:** nothing.
- **DESIGN DECISION (explicit, recommended):** do **not** add `.positive` to `SectionStatus.blocksClean`
  (`Conclusion.swift:341` / `Conclusion.cs:40-41`). That property also feeds `hasHardGatedSection`'s **inline**
  predicate (`Conclusion.swift:88`, uses `.untested`/`.invalidated` directly — unaffected) and, more importantly,
  the referral post's "Not run / not trustable" filter (`SynthesisEngine.swift:365`, `SynthesisEngine.cs:362`) —
  widening `blocksClean` would mis-list a *positive* (evidence-present) section as "not run". Keep `blocksClean`
  meaning "absence/untrustworthiness of evidence"; gate `.positive` at the engine's `cleanBlocked` local instead.
  *(If the executor prefers to widen `blocksClean` per the design's literal §5.4 text, it must also patch the two
  prefilled-post call sites — flag before doing so.)*

### P1.6 — I8 confirmed-threat-mass floor gate (verdict-level, outside DST, rule-independent)

- **Action:** Add the BLOCKER-2 backstop: a **committed** threat mass — the mass a *confirmed* source assigns
  **directly** to a subset of the threat union via its **own** simple-support focal set, evaluated **before**
  dilution by other supports — that meets/exceeds `threatNameFloor` (`0.40`) **forbids `clean`** and floors the
  verdict at referral (or tampered, when a mechanism-class set already qualifies). It **reads** committed mass but
  **never** overwrites `Bel/Pl/BetP/m(Θ)` (R3), and it keys on the per-support focal mass, **not** the
  post-combination `Bel(threatUnion)`, so no combination wash erases it. In Phase 1 the confirmed sources are the
  `.positive`-section supports whose focus ⊆ threat-union (e.g. `dnssecCanaryStripped`, `w=3.0 → m=1−e⁻³≈0.95`); the
  future confirmed cross-vantage discrepancy act (Phase 5) plugs into the same gate.
- **macOS:** compute over the per-group positive supports built at `SynthesisEngine.swift:157-167` (each
  `(focus, delta)` with `focusMass = 1−e^{−weight}`), against `threatUnion` at `:204` and `config.threatNameFloor`
  (`:63`); apply at the verdict branch `:216-222`.
- **Windows:** twin over positives at `SynthesisEngine.cs:128-138`, `threatUnion` at `:178`, `Config.ThreatNameFloor`
  (`:18`), verdict branch `:189-200`.
- **Serves:** NE1 Δ1 (BLOCKER-2 backstop); design §3 M4 (D5), §5.4 I8, §6.2, §9 [NE1 Δ1 row].
- **Effort:** M.
- **Real-data acceptance bar:** a **real capture** where a confirmed threat commits `m(subset of threat-union) ≥ 0.40`
  but several clean sections wash the fused `Bel(threatUnion)` below `0.40` → verdict is **still not `clean`**
  (inconclusive+referral), and the signed `Bel/Pl/BetP/m(Θ)` trail is **unchanged** by the gate (assert the numbers
  match the pre-gate combination). Observed by running the strip+clean-sections scenario on real data.
- **Parity:** the committed-mass computation + floor identical on both (an `_exact` boundary vector at the `0.40`
  floor).
- **Approval gate:** the forced-verdict/low-confidence display tension (MINOR-4) is a **`syxx-gui-architect`
  surfacing proposal** — the engine emits `basis == .decisiveSafetyGate` and the true (possibly low) `Bel`; it must
  **not** floor the belief number. The presentation of "Tampered — 20%" is Codex's proposal, not this step.
- **Depends on:** nothing (redundant with P1.5; both land in Phase 1 by design so the backstop exists before the
  Phase-5 discrepancy act arrives).

### P1.7 — Minimum-corroboration gate (single source → force referral)

- **Action:** A single evidence item (or a single **lineage cluster** — for Phase 1, a single `source`/
  `correlationGroup`, since full-lineage clustering is the deferred R4 pass) may *raise* a threat but must **force
  referral** (tentative, confidence-capped) unless it is a **validated decisive act**. The validated-decisive set is
  **scaffolded empty** in Phase 1 (populated in Phase 6), so for now any lone-source threat forces referral.
- **macOS:** count distinct independent sources contributing positive mass at the verdict/referral stage
  (`SynthesisEngine.swift:216-254`); add `< 2 independent sources` to the `referralTriggered` disjunction at
  `:250-254`. Reuse the correlation grouping already built at `:126-132`.
- **Windows:** twin at `SynthesisEngine.cs:189-236` (referral disjunction `:232-236`), grouping at `:87-94`.
- **Serves:** NE7 Δ5b; DESIGN.md:169-172; design §5.3 min-corroboration, §3 M4 (empty decisive set).
- **Effort:** M.
- **Real-data acceptance bar:** a **real run** where exactly **one** source names a threat (e.g. a lone
  `redirection` or an unsigned `authoritativeDiverges`) → the verdict **forces referral** ("tentative — corroborate")
  rather than naming with un-referred confidence. Observed by running a single-signal scenario; and a two-independent-
  source run does **not** get spuriously referred by this gate.
- **Parity:** the independent-source count identical on both.
- **Approval gate:** the tentative-referral **wording** is copy-gated → emit a referral CopyToken; the referral
  *trigger* is structural. Flag the new sentence as a gui-architect proposal.
- **Depends on:** nothing. **Note:** composes with P1.6 (I8 forbids clean; min-corroboration forces referral) — a
  lone confirmed strip lands as "not clean + refer", honest for Phase 1.

### P1.8 — NE5 Δ1 minimal parity fix (Windows: stop emitting threat-weighted run-quality overlays)

- **Action:** Windows Prophet must stop injecting threat mass from run-quality-only sub-signals that macOS
  withholds. Remove the `EvidenceItem.Create(...)` emission in the latency-`Degraded` and stability-`TtlAnomaly`
  branches so they contribute **only** a `.Degraded` sub-status, matching macOS. (This is the **minimal** parity
  fix; the full weight-free `QualityCaveat` channel that also *explains* the knock on both platforms is the
  deferred Phase-3 pass — NE5 quality-caveat proposal.)
- **macOS:** already correct — `Synthesizer.swift:109-115` (latency → `.degraded`, no `EvidenceItem`), `:118-131`
  (stability `.ttlAnomaly` → `.degraded`, no `EvidenceItem`). No change; this is the parity reference.
- **Windows:** remove the two `ev.Add(EvidenceItem.Create(... ResolverStatus ...))` / `... AnswerUnstable ...`
  calls in the latency-`Degraded` and stability-`TtlAnomaly` branches of `Synthesizer.cs` Prophet (the branches that
  set `SectionStatus.Degraded`). Confirm the two threat weights they inject — `ResolverStatus → Censorship 0.8 /
  InterceptionOnPath 0.6` (`Conclusion.cs:124-125`) and `AnswerUnstable → Spoofing 0.6 / InterceptionOnPath 0.6`
  (`Conclusion.cs:162-163`) — no longer reach the accumulator.
- **Serves:** NE5 Δ1 (critical false-tampered parity divergence); design §6.1 (QualityCaveat), §9 [NE5 Δ1 row], §10.3.
- **Effort:** S (Windows-only edit).
- **Real-data acceptance bar:** a **real Windows run** on a merely-**slow** resolver with Mirror/Glutton/Cage/Witness
  skipped → verdict is **`inconclusive`**, matching macOS on identical inputs — **not** "censorship ~37%". Observed
  by running the Windows binary against a slow/flaky resolver; and the NE5 parity golden vector goes green on both.
- **Parity:** this *is* the parity fix — after it, the `EvidenceId`-emission set per adapter matches across platforms
  (design §10.3 lint).
- **Approval gate:** none.
- **Depends on:** nothing.
- **DECISION (flag):** this closes NE5 Δ1 with a Windows delete; the *paired proposal* (route overlays through a
  weight-free `QualityCaveat` channel so macOS also *explains* its silent confidence knock) is **deferred to
  Phase 3**. If the executor would rather bundle NE5 into the Phase-3 QualityCaveat work, the interim risk is that
  the NE5 parity vector stays red until Phase 3 — recommend landing the minimal fix now so the corpus greens.

### P1.4 — K/M completeness as a separate reported dimension (M3)

- **Action:** Compute `K` = sections that reached a **conclusive** status (`negative`/`positive`) and `M` = sections
  **scheduled** for this invocation (D3: user-skipped decrements `M`; scheduled-but-failed stays in `M`), and expose
  it via the Stage-5 `EvidenceCompleteness` DTO + `DataRegime`. It is a **reported figure and a clean-eligibility
  input**, **never** a `Bel` multiplier (R2). The clean-gate itself is already P1.2; this step is the *reported*
  number and the regime classification.
- **macOS:** populate `EvidenceCompleteness` (`observed`/`expected`/`criticalAvailable`/`criticalExpected`/
  `expectedProfileId`) in the Stage-5 builder; thread an invocation-profile param into
  `Synthesizer.conclude`/`SynthesisEngine.synthesize` (`SynthesisEngine.swift:105`, additive, defaulted).
- **Windows:** twin via `SynthesisEngine.cs:63` (additive profile param) + the Stage-5 builder.
- **Serves:** NE7 Δ1 (the *reporting* half); design §3 M3, §5.2; STAGE5 §8 (regime table), §13 invariant 10.
- **Effort:** M.
- **Real-data acceptance bar:** a **real `syxx-nslook` (Prophet-only) run** reports `M=1` (skipped sections
  **decrement** `M`, no penalty) while a **full run with Glutton timed out** reports that Glutton stays in `M` and
  lowers `K/M`; both surface the completeness figure **beside** the verdict and never multiply it into confidence.
  Observed by running both invocation profiles.
- **Parity:** the `K`/`M` counting rule + `expectedProfileId` identical on both.
- **Approval gate:** completeness *phrasing* is copy-gated → the DTO carries counts + `expectedProfileId` (machine),
  no prose (STAGE5 §10).
- **Depends on:** P1.0.
- **OPEN QUESTION (flag, O6):** today the engine cannot distinguish **user-skipped** from **scheduled-but-untested**
  — both are `.untested`. The invocation profile (which sections were *scheduled*) must be supplied by the CLI/GUI
  caller (`expectedProfileId` + scheduled set). Interim: full `syxx` ⇒ `M=6`; `-nslook` ⇒ `M=1`; caller passes the
  schedule. This is an additive signature change, not a behavior regression.

### P1.9 — Populate provenance partition + canonical answer matrix into `stage5`

- **Action:** Extract every vantage's answer set — local resolver, each `publicResolverResults` vantage,
  `authoritative`, `serverGroundTruth` — into `ProvenancePartition` (direct/inferred/unavailable) and the canonical
  `AnswerMatrix` with engine-owned canonicalization (lower-case absolute names, one canonical IP form, de-dup, ordinal
  sort, set-compare) and `AnswerComparison.classification` (agreement / benignSetVariation / discrepancy /
  insufficientReferences). This closes NE3 Δ1 **at the presentation layer**: a poisoned local answer surfaces the
  true public consensus and the outlier side-by-side. **No scoring change** — the matrix feeds display only (STAGE5
  §2 forbids the UI deriving scoring; feeding discrepancy into DST mass is the deferred Phase-5 pass, NE6 Δ1).
- **macOS:** read `ComparisonResult.publicResolverResults` (`VantageComparer.swift:41`), `localServer` (`:39`),
  `localRecords` (`:40`), `inputs.authoritative`, `inputs.serverGroundTruth` (`GroundTruth`, `DimensionClient.swift:10`);
  build the matrix in the Stage-5 builder. **Do not** reuse the scalar `DatapointExtractor.extract`
  (`Report.swift:67-100`) which omits `publicResolverResults` — that omission is the NE3 Δ1 bug.
- **Windows:** twin — `PublicResolverResults` (`VantageComparer.cs:12`), `GroundTruth` (`DimensionClient.cs:12`);
  the current `DatapointExtractor.Extract` (`Report.cs:51-86`) has the identical omission.
- **Serves:** NE3 Δ1 (poison shown as agreed fact), NE3 Δ3 (same-vantage collapse via vantage identity); design §7;
  STAGE5 §4-§5, §13 invariants 3/7/8.
- **Effort:** L (canonicalization + validation surface; may split matrix vs provenance-partition commits).
- **Real-data acceptance bar:** a **real locally-poisoned run** (local resolver returns the wrong A while ≥2 public
  vantages return the true A) renders the local **outlier** and the true **consensus** simultaneously with vantage
  identity in the Stage-5 output on **both** platform paths; a **real multi-homed CDN** domain canonicalizes to
  `agreement`/`benignSetVariation`, **not** `discrepancy`. Observed by running against a controlled poisoned resolver
  and a real CDN.
- **Parity:** canonicalization + comparison classification byte-identical (STAGE5 §5, §11).
- **Approval gate:** headings/copy are copy-gated → emit `CopyToken`s + typed fields only (STAGE5 §10).
- **Depends on:** P1.0. **HONESTY-ORDERING CONSTRAINT:** must pair with **P1.10** — surfacing a `discrepancy` in the
  matrix while `determination.state` could still read `clean` is internally contradictory. P1.9 must not reach
  production rendering before P1.10 lands (or the two land together).

### P1.10 — Canonical cross-vantage discrepancy forbids `clean` (verdict-level gate)

- **Action:** When the P1.9 answer matrix classifies a **canonical** key (`target_answer`, `local_resolver`) as
  `AnswerComparisonClass.discrepancy` (a local outlier vs a valid reference consensus — **not** `benignSetVariation`),
  forbid a `clean`/`negative` verdict (the Phase-1, presentation-time analog of I8: a verdict-level gate that
  **reads** the classification but **never** edits `Bel`). This is the "at minimum, hard-gate the verdict away from
  clean" fix of NE6 Δ1; the full "discrepancy → bounded DST mass" is the deferred Phase-5 pass.
- **macOS:** consume the matrix classification in the verdict branch (`SynthesisEngine.swift:216-222`) via the
  Stage-5 builder or a threaded discrepancy flag; requires the set-valued comparison from P1.9 so multi-homed A-sets
  do **not** false-fire (design §6.2 pre-req).
- **Windows:** twin at `SynthesisEngine.cs:189-200`.
- **Serves:** NE6 Δ1 (discrepancy display-only → verdict-affecting, minimal form); design §6.2, §7; STAGE5 §13
  invariants 8/9.
- **Effort:** M.
- **Real-data acceptance bar:** the **same real locally-poisoned run** as P1.9 now yields a **non-`clean`** verdict
  (referral) *because* the matrix shows a genuine local-outlier discrepancy; a **real multi-homed CDN** does **not**
  trip the gate. Observed by running both.
- **Parity:** both platforms; the discrepancy predicate keys on the shared canonicalization (P1.9).
- **Approval gate:** referral wording copy-gated → CopyToken; the gate is structural.
- **Depends on:** P1.9 (needs the set-valued classification). **This is P1.9's honesty partner — see P1.9.**

### P1.11 — Populate structured honesty signals + `generatedAt` into `stage5`

- **Action:** Populate `HonestySignals` as **three independent** structured signals (hard-gate / conflict /
  uncertainty), the `DataRegime`, and the RFC 3339 UTC `generatedAt` mirroring legacy `Conclusion.generatedAt`.
  `hardGate` present iff a decision-policy gate blocks a verdict (P1.2/P1.5/P1.6/P1.10), naming the engine-supplied
  required sections; `conflict` present **only** on genuine evidential contradiction (benign context/reliability
  discount/missing evidence must **not** fabricate it); `uncertainty` = raw `m(Θ)` when defined. Emit CopyTokens,
  never prose.
- **macOS:** map from `hasHardGatedSection` (`Conclusion.swift:87-89`), `conflict`/`uncertainty` (`Conclusion.swift:27-30`),
  `generatedAt` (`:20`) into the Stage-5 builder; the current `renderText` (`Report.swift:111-155`) surfaces **none**
  of the hard-gate/time-anchor — that is NE3 Δ2/Δ4.
- **Windows:** twin — `HasHardGatedSection` (`Conclusion.cs:304`), `Uncertainty`/`Conflict` (`:290-294`), `GeneratedAt`
  (`:280`); `RenderText` (`Report.cs:92-135`) has the identical omission.
- **Serves:** NE3 Δ2 (three honesty lines collapse to one), NE3 Δ4 (no time anchor); design §7; STAGE5 §6 (CX-2), §13.
- **Effort:** M.
- **Real-data acceptance bar:** three **real runs** on each platform — a partial run with a required check
  unavailable, a genuinely contradictory-vantage run, and a thin-evidence run **without** contradiction — produce the
  hard-gate / conflict / uncertainty structures **independently** (benign context alone never fabricates `conflict`),
  and every report carries the **same** RFC 3339 instant as the engine result. Observed by running all three.
- **Parity:** all eight present/absent combinations valid and identical on both; RFC 3339 UTC formatting identical
  (STAGE5 §6, §11).
- **Approval gate:** the three honesty **sentences** are copy-gated → CopyTokens only (STAGE5 §10).
- **Depends on:** P1.0; benefits from P1.4 (completeness) landing first for the regime figure.

---

## Risks & open questions

### Gating open questions (block the LATER pass, not this slice)

- **O3 — fusion rule + `ε` (GATES the deferred Phase-3/4).** The winning combination rule and the `_epsN`
  display-tolerance `ε` are outputs of the P0.4 bake-off + the float-determinism spike. Until they exist: mark all
  `_epsN` assertions `pending-bakeoff` (STAGE0 §"assertion tiers"); do **not** pre-pin `ε`; the naming vectors
  (NE1 Δ1 REWRITE, NE1 Δ2 inversion) stay expected-red past Phase 1. **This is the single hard gate on the later
  pass.**
- **O6 — completeness denominator `M` (P1.4).** The engine cannot today distinguish user-skipped from
  scheduled-but-untested. Needs the caller-supplied invocation profile (`expectedProfileId` + scheduled set). Interim
  full=6 / nslook=1 is safe; the exact per-CLI schedule is a small caller-side follow-up.

### Assumptions / decisions to confirm

- **P1.5 `.positive` gate placement** — recommended at the engine-local `cleanBlocked`, **not** by widening
  `SectionStatus.blocksClean` (which would corrupt the "not run / not trustable" referral list). Confirm before
  implementing; if widening `blocksClean` is preferred, the two prefilled-post call sites must also change.
- **P1.8 NE5 scope** — recommended as the **minimal Windows delete now** (so the NE5 parity vector greens), with the
  full weight-free `QualityCaveat` channel deferred to Phase 3. Confirm this split vs bundling NE5 into Phase 3.
- **P1.3 α magnitude** — ship an expert-elicited, documented, signable discount now; the calibrated value is
  Phase 7. Confirm the interim α is acceptable.
- **P1.9/P1.10 as a Phase-1 pair** — the matrix *display* (P1.9) and the discrepancy *gate* (P1.10) must land
  together; P1.10 is the minimal verdict-level form (not DST mass). Confirm the discrepancy-forbids-clean gate is
  in-scope for this slice (it is required for P1.9 to be honest).

### Where a lone step transiently breaks honesty (call-outs)

- **P1.9 without P1.10** — a `discrepancy` in the Stage-5 matrix beside a `clean` determination is a dishonest
  intermediate. **Do not** ship P1.9 to production rendering before P1.10. (Only such pairing in the slice.)
- Every other Phase-1 step is monotone toward honesty and safe to land alone.

### Reference-drift caught during verification (use the live-tree numbers above)

- Windows clean-support loop is `SynthesisEngine.cs:152-157` (design cited ~:180).
- Windows has **two** combine methods — `Combine` (`Synthesizer.cs:53-63`) **and** `CombineWithEvidence`
  (`:65-75`) — both need the P1.1 invalidated-dominates fix (design cited one).
- `SectionStatus.blocksClean` (`Conclusion.swift:341`/`Conclusion.cs:40-41`) is **distinct** from
  `hasHardGatedSection`'s inline predicate (`Conclusion.swift:88`) — relevant to P1.5.
- No golden-vector runner exists yet (Cline builds it in CL-3) — P0.2/P0.3 are the engine side only.

---

## Handoff note

- **`syxx-neural-executor`** implements every step above in `SyxxKit` **and** `Syxx.Kit` (both platforms, per the
  parity obligations noted per step). Land **P1.0 first** (Codex is blocked), then the false-secure gates
  (P1.1/P1.5/P1.6/P1.2/P1.3), then P1.7/P1.8, then the completeness + matrix + honesty-signal population
  (P1.4/P1.9/P1.10/P1.11), with P0.1-P0.3 interleaved to keep the corpus green. **Do not** touch the production
  `synthesize()` combination rule (P0.4 is offline). Every step's "done" is its **real-data acceptance bar observed
  by running it**, not a green suite — Syxx has shipped a suite-green build that lied on real data.
- **Coordinate with Cline (CL-3):** flag the two missing STAGE0 schema fields (`greensAtPhase` + the universal
  never-false-secure assertion, P0.3) — do not redefine the schema unilaterally.
- **`syxx-gui-architect` / Codex** render `stage5` (CX-1..CX-3). **All user-visible wording** introduced by the
  engine gates (new referral sentences in P1.6/P1.7/P1.10, the hard-gate/conflict/uncertainty sentences in P1.11,
  the forced-verdict/low-confidence presentation from P1.6/MINOR-4) is a **proposal pending approval** — the engine
  emits **CopyTokens and typed fields only**; the belief number is **never** floored to match a forced verdict (R3).
- **Deferred later pass (blocked on O3):** design Phases 3-7 — the fusion-rule bake-off *result*, the Stage-3/4
  combination + datapoint-scoped N/M clean-support + TBM credal readout + mechanism-class naming, the Phase-5
  discrepancy→DST-mass, the Phase-6 probe/validated-decisive-set work, and Phase-7 calibration. Plan those once
  P0.4 selects the rule and pins `ε`.
