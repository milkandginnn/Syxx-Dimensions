# ENGINE-PHASE2-7-PLAN — the deferred later pass of the reasoning-engine remediation

**Author:** Syxx Neural Planner (design/plan only — no feature code lands here)
**Date:** 2026-07-12
**Status:** Draft, engineer-directed but **gated**. Design is APPROVED (`REASONING-SYSTEM-DESIGN.md` v2.1). This
plan covers **Phases 2 through 7** — everything `ENGINE-PHASE01-PLAN.md` explicitly deferred ("the fusion-rule
bake-off result, the Stage-3/4 combination + datapoint-scoped N/M clean-support + TBM credal readout +
mechanism-class naming, the Phase-5 discrepancy→DST-mass, the Phase-6 probe/validated-decisive-set work, and
Phase-7 calibration"). It does **not** re-plan Phase 0/1 — see `ENGINE-PHASE01-PLAN.md` for those (Phase 0
partially landed: `P0.1`/`P1.0` committed at `f40dc58`/`bcf817e`; `P0.2`/`P0.3` golden-corpus harness committed
at `daa97c4`, macOS+Windows twins. `P0.4` offline bake-off harness is **not yet built** and is the hard gate for
this entire plan, below).
**Handoff target:** `syxx-neural-executor` for Phases 2, 6 (client-side) and the Phase-6 server touches;
`syxx-gui-architect`/Codex for the Phase-5 Stage-5 rendering once the engine populates it; **no phase in this
document may start implementation before its stated dependency is verified landed** — re-check `git log` /
the live tree before beginning any phase, not just this plan's say-so.

---

## 0. Plan overview

### Goal

Sequence the full remainder of `REASONING-SYSTEM-DESIGN.md` (§11, Phases 2-7) into engineer-ready steps, so
that once Phase 1's structural gates are verified landed and `P0.4`'s bake-off selects a fusion rule, the
executor has a dependency-ordered path from "rule-agnostic honesty gates" to "calibrated, cross-vantage,
probe-hardened TBM engine" without ever passing through a transient false-secure state.

### The single hard gate on this entire plan: O3 (fusion-rule selection)

Per `REASONING-SYSTEM-DESIGN.md` §4.2/§11/§12-O3 and `ENGINE-PHASE01-PLAN.md`'s own closing line: **Phase 3
commits a fusion rule that does not exist yet.** The rule (Dempster / cautious / Yager / Dubois–Prade /
weighted-mass) and the display-tier `ε` are **outputs of `P0.4`** (the offline bake-off), not inputs to it.
**Nothing in Phase 3 may be implemented against production `synthesize()` until `P0.4` selects a winner and
pins `ε`.** Phase 2 (the M1/M2 causal split + decision-policy wiring) does **not** depend on the rule — it can
proceed once Phase 1 is verified landed. Phases 4-7 depend transitively on Phase 3.

### Current state verified in the live tree (2026-07-12, re-checked this session)

- **Landed (committed):** `P0.1` (integer-% gate + deterministic DST fold, `f40dc58`), `P1.0` (additive
  `Stage5Presentation` DTOs, `stage5` still always `nil` in production, `bcf817e`).
- **Landed (committed, this session):** `P0.2`/`P0.3` golden-corpus harness on both platforms (`daa97c4`).
- **Landed (uncommitted in the working tree as of this session — verify committed before relying on it):**
  the Phase-1 false-secure gates read as substantially implemented in
  `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift` — `positiveSectionGate` (:119, P1.5),
  `criticalNotConclusive` (:120-122, P1.2), `committedThreatMassFloorHit`/`threatNameFloor` (:156-198, P1.6),
  `groupReliability`/`degradedSectionReliability` (:159-161, P1.3), `belowMinCorroboration` (:288, P1.7), and
  `.invalidated` dominance in `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:61` (`ran.contains(.invalidated)`,
  P1.1). **This plan's Phase 2 dependency is "Phase 1 verified landed" — the executor must confirm these are
  committed (and their Windows twins + golden-vector-green state) before starting Phase 2, not assume from this
  note.** `P1.4`/`P1.9`/`P1.10`/`P1.11` (Stage-5 population: completeness, provenance/matrix, honesty signals)
  are **not yet implemented** — `stage5` is not constructed anywhere in `SynthesisEngine.swift`/`.cs`. `P1.8`
  (Windows NE5 parity) not independently re-verified this session.
- **Not started:** `P0.4` (offline bake-off harness) — **no file matching an offline fusion-rule harness exists
  in either test target**. This is the plan's hard gate; see Phase 3.
- **Not started:** all of Phases 2, 4, 5, 6, 7 as implementation (Phase 5's *prerequisite read paths* —
  `VantageComparer.swift:38-65` `ComparisonResult.publicResolverResults`/`localRecords`/`localServer`,
  `DimensionClient.swift:10` `GroundTruth` — already exist and are read by `P1.9`, which is itself unimplemented).

### Phases ordered by mission risk and dependency

```
Phase 1 (ENGINE-PHASE01-PLAN.md) — VERIFY LANDED (both platforms, golden-vector green) before Phase 2.
        │
        ├──> Phase 2 (M1/M2 causal split + decision-policy wiring) ── independent of the fusion rule
        │         │
P0.4 (bake-off: NOT YET BUILT) ──> selects rule + pins ε ──> Phase 3 (combination + clean-support, COMMITS the rule)
        │                                                          │
        │                                                          ├──> Phase 4 (TBM verdict/confidence semantics)
        │                                                          │         │
        │                                                          │         └──> Phase 5 (evidence model upgrade:
        │                                                          │                cross-vantage matrix → DST mass)
        │                                                          │                   │
        │                                          Phase 2 (safety-gate scaffold) ──────┤
        │                                                                               └──> Phase 6 (probe robustness +
        │                                                                                     validated-decisive-set)
        │                                                                                            │
        └───────────────────────────────────────────────────────────────────────────────────────────┴──> Phase 7
                                                                                                            (calibration)
```

Recommended landing sequence: **Phase 2** (can start once Phase 1 is verified) in parallel with **`P0.4`**
(must be built regardless — it is standalone/offline and blocks nothing else while running) → **Phase 3**
(only after `P0.4` selects) → **Phase 4** → **Phase 5** → **Phase 6** → **Phase 7**.

---

## Phase 2 — The four separated mechanisms for context (M1 vs M2), + decision policy wiring

### Scope

Design §3 M2 (§235-262) + M4 decision-policy wiring (§278-379, D1); `REASONING-SYSTEM-DESIGN.md` §11 Phase 2.
Closes: NE1 Δ4, NE2 Δ1/Δ2, NE4 Δ1/Δ2. **Independent of the fusion rule — may start as soon as Phase 1 is
verified landed on both platforms with golden-vector confirmation.**

### Verified current-state anchor (re-grep before implementing — line numbers will drift)

`SynthesisEngine.swift:200-206` (dampener loop): every negative-weight (`w < 0`) `WeightRubric` entry is
folded into a support for `Θ∖{h}` **unconditionally, per evidence item**, with no causal/observation gate
distinguishing "this negative signal is a causal explanation for a specific threat's absence" (M2, correct)
from "this negative signal is itself an intrinsically-unreliable/degraded reading of an unrelated threat" (M1,
already handled separately via `groupReliability` at `:159-161`). This is the **undifferentiated dampener**
the design's P2 diagnosis (§90-104) targets: a captive-portal marker or an encrypted-DNS-on marker currently
dampens via the same blanket per-item mechanism regardless of whether the specific threat it's explaining is
actually the one a positive signal named. The design requires M2 to be **causal and observation-gated** — a
benign-context marker may only dampen a threat it has a documented causal link to, and must **force referral**
rather than cancel when the link is ambiguous — never silently reduce belief in a threat the context marker
cannot actually explain (anti-cancellation, R1).

- **macOS:** `SynthesisEngine.swift:200-206` (dampener construction) and `WeightRubric.getWeight`
  (`Conclusion.swift:185+`, holds the negative-weight table — re-grep for the current entries, e.g. captive
  portal / encrypted-DNS-on / benign-CDN markers).
- **Windows:** twin dampener loop in `SynthesisEngine.cs` (~:180-190 by proportional line count vs the 416-line
  file — re-grep `w < 0` before editing) and the negative-weight table in `Conclusion.cs`.

### P2.1 — Introduce the `BenignExplanation` act catalog (causal link table)

- **Action:** Define an explicit table mapping each context marker (`EvidenceId`, e.g. captive-portal-active,
  encrypted-DNS-on) to the **specific threat hypotheses it may causally explain** (not "all threats", not a
  blanket dampener). This is a new small enum/struct, not a change to `WeightRubric`'s positive-weight table.
  A context marker with no entry in this table dampens **nothing** — it must not fall back to the old
  blanket behavior.
- **macOS:** new type near `WeightRubric` in `Conclusion.swift` or a new `Synthesis/BenignExplanation.swift`.
- **Windows:** twin in `Conclusion.cs` or a new `Synthesis/BenignExplanation.cs`.
- **Serves:** design §3 M2; NE1 Δ4, NE2 Δ1/Δ2 (BLOCKER-1 "channel-3 must not lower confidence from missing
  data" is adjacent but distinct — verify this step doesn't reintroduce that regression; M2 is about *causal*
  dampening of a *specific* threat, not a missing-data discount).
- **Effort:** M.
- **Real-data acceptance bar:** a real captive-portal network run where Mirror also independently finds a
  DNSSEC strip → the captive-portal marker (which has no causal link to DNSSEC integrity) does **not** dampen
  the strip finding at all; a real captive-portal-only run (no independent positive finding) dampens exactly
  the redirection-class hypotheses the portal table lists, nothing else. Observed by running both scenarios.
- **Parity:** the causal-link table identical on both platforms (this is exactly the kind of silent-divergence
  risk NE5 Δ1 already demonstrated — a table edited on one platform and not the other is a parity bug by
  construction).
- **Approval gate:** none (internal data structure, no new user-visible string).
- **Depends on:** Phase 1 verified landed.

### P2.2 — Replace the blanket per-item dampener with the gated M2 lookup

- **Action:** In the dampener construction loop, look up the causal-link table (P2.1) instead of treating
  every negative `WeightRubric` weight as an unconditional dampener. A context marker whose table entry lists
  hypothesis `h` dampens only `h` (and only when a section that could observe `h` actually ran); a context
  marker with **no** table entry, or whose only candidate explanation is **ambiguous** (could explain more
  than one live positive finding), **forces referral** instead of dampening (per design's "or force referral"
  in M2's title). This directly fixes the anti-cancellation violation (R1): a confirmed positive from an
  unrelated, independently-reliable source must never be slid toward Θ by an unrelated context marker.
- **macOS:** `SynthesisEngine.swift:200-206`.
- **Windows:** twin dampener loop in `SynthesisEngine.cs`.
- **Serves:** design §1 P2 diagnosis (§90-104), §3 M2; NE1 Δ4, NE2 Δ1/Δ2, NE4 Δ1/Δ2.
- **Effort:** M/L.
- **Real-data acceptance bar:** the **same real captive-portal + concurrent DNSSEC-strip adversarial fixture**
  the design's own Phase-2 acceptance bar names (§770-772): the poison is still **named** (or referred, if the
  portal itself is what rewrote it — an ambiguous case), conflict does not spike from context alone, and
  inducing the portal marker does **not** slide the confirmed signal's committed mass toward Θ. Observed by
  running the fixture on real captured traffic, not a synthetic unit input.
- **Parity:** both platforms; add an `_exact` golden vector asserting identical verdict + referral state for
  the fixture on both.
- **Approval gate:** if this step introduces a new "ambiguous context — referred" sentence, that wording is a
  `syxx-gui-architect` proposal; the referral *trigger* is structural, no gate needed for the trigger itself.
- **Depends on:** P2.1.

### P2.3 — Wire the M4 decision-policy gate at the verdict layer (D1, formalize what Phase 1 scaffolded)

- **Action:** Phase 1 (`P1.5`/`P1.6`) already implemented the **rule-agnostic instances** of the M4 safety gate
  (`.positive`-blocks-clean, committed-threat-mass floor). Phase 2's job is to generalize this into the
  **named, documented decision-policy layer** the design calls M4 (§278-379): a single well-documented function
  (or clearly-delineated code region) that reads section statuses + committed masses and applies the
  deterministic safety gate, with an **explicit, empty, versioned `validatedDecisiveActs` set** (scaffolded
  here, populated in Phase 6) as one of its inputs. This is a refactor/consolidation of the Phase-1 gates into
  the shape Phase 6 will extend — not new gating logic.
- **macOS:** consolidate `SynthesisEngine.swift`'s scattered gate predicates (`statusHardGated`,
  `positiveSectionGate`, `criticalNotConclusive`, `committedThreatMassFloorHit`, `belowMinCorroboration`) behind
  one named decision-policy evaluation, with the empty decisive-act set as a typed (currently-empty) parameter.
- **Windows:** twin consolidation in `SynthesisEngine.cs`.
- **Serves:** design §3 M4, D1; sets up Phase 6's `enum DecisiveAct` population point.
- **Effort:** M.
- **Real-data acceptance bar:** re-run the full Phase-1 golden-vector corpus (`Stage0GoldenCorpusTests`) after
  the refactor — every currently-green vector stays green, every currently-red (xfail) vector stays red for the
  same reason (no accidental behavior change from the consolidation). Observed by running the suite before and
  diffing.
- **Parity:** both platforms; this is a pure refactor so the parity bar is "identical verdicts before and
  after, on both platforms," not new behavior.
- **Approval gate:** none.
- **Depends on:** P2.2 (so the M2 causal gate is included in what gets consolidated).

### Phase 2 acceptance bar (design §11, verbatim scope)

On a **real captive-portal network** with a concurrently poisoned answer (adversarial fixture on the wire),
the poison is still **named** (or referred, when the portal itself rewrites), conflict does not spike from
context alone, and inducing the portal marker does **not** slide the confirmed signal toward Θ.

---

## Phase 3 — Combination + clean-support correctness (commits the selected rule) — **BLOCKED on `P0.4`**

### Scope

Design §4 (fusion-rule selection), §6.3 (full-lineage correlation), §3 M3 refinement (datapoint-scoped N/M
clean-support), §11 Phase 3. Closes: NE1 Δ1/Δ2, NE2 Δ3/Δ4/Δ6/Δ8.

### Hard gate — do not start implementation before this exists

**`P0.4` (the offline fusion-rule bake-off harness) does not exist in the live tree** (verified: no file
matching an offline candidate-rule harness in either test target). Per `ENGINE-PHASE01-PLAN.md` P0.4's own
spec: it must be built as an **offline, test-target-only** harness that evaluates Dempster / cautious / Yager
/ Dubois–Prade / weighted-mass (design §4.2) over the golden corpus (from `P0.2`/`P0.3`, now landed) and
produces (a) an adversarial-vector pass/fail matrix and (b) a float-determinism/bit-reproducibility profile
that sets the display-tier `ε`. **It must not touch production `synthesize()`.**

### P3.0 — Build `P0.4` (prerequisite step, restated from ENGINE-PHASE01-PLAN.md since Phase 3 cannot start without it)

- **Action:** As specified in `ENGINE-PHASE01-PLAN.md` §"P0.4". Restated here because Phase 3 literally cannot
  be planned in file:line terms until this selects a rule — the combination-rule code Phase 3 patches does not
  yet have a target implementation to point at.
- **Effort:** M (per original estimate).
- **Real-data acceptance bar:** the harness produces a recorded pass/fail matrix + determinism profile for
  each candidate rule over the real-capture corpus (`P0.2`/`P0.3`); the shipped Dempster verdict is diffed
  before/after and is **byte-identical** (proves non-interference with production).
- **Depends on:** `P0.2`/`P0.3` (verified landed, this session).
- **Owner:** `syxx-neural-executor`, per the original Phase-0/1 plan's handoff — **flagged here as the literal
  hard blocker for everything below in this document.**

### Steps below are PROVISIONAL — file:line targets depend on which rule `P0.4` selects

The design (§4.2, §4.3) identifies **cautious conjunctive combination** as "the leading candidate" but is
explicit this is not a pre-commitment (R5). The steps below are written against the **current Dempster
implementation's structure** so the executor has a starting shape; **if `P0.4` selects a non-Dempster rule,
the executor must re-derive these steps' file:line targets against the new combination function** before
implementing — this plan does not pre-judge that result.

### P3.1 — Replace `dempster()` with the `P0.4`-selected combination rule, applied within lineage clusters

- **Action:** Swap the production combination function (currently `dempster()`,
  `SynthesisEngine.swift:332-...`) for the rule `P0.4` selected, applied **within** a lineage cluster (full
  R4 tuple: `{source/vantage, raw-observation, probe/control, act-class}` — not just `source` string equality,
  which is the current `correlationGroup` proxy at `SynthesisEngine.swift:145-151`). Cross-cluster combination
  may still use plain Dempster if the selected rule is cautious-within/Dempster-across (design §4.2's likely
  outcome) — confirm against the actual bake-off result, do not assume.
- **macOS:** `SynthesisEngine.swift` combination core (~:225-235 `dempster()` call site, ~:332+ the function
  itself) + the correlation-grouping logic (:145-151, currently keyed on `EvidenceItem.source` string equality
  — this is the **R4 gap**: `Conclusion.swift:314`'s `correlationGroup(for:)` returns a static per-`EvidenceId`
  string, not the full lineage tuple the design requires).
- **Windows:** twin combination core + grouping logic in `SynthesisEngine.cs`.
- **Serves:** design §4 (rule selection), §6.3 R4 (full lineage), O4 (open, §12); NE1 Δ2 (the two-mechanism
  inversion — this is its actual fix, not just the Phase-1 never-clean gate).
- **Effort:** L.
- **Real-data acceptance bar:** the seed `NE1-d2-two-mechanism-inversion` golden vector (already in the
  corpus, currently `knownFalseSecureUntilGate` under Dempster) now produces the design-correct verdict
  (`.spoofing`, not merely non-clean) on a **real capture** with two genuinely independent mechanisms present;
  a capture with two correlated symptoms of the **same** mechanism does **not** double-count. Observed by
  running both real scenarios.
- **Parity:** the selected rule + the lineage-tuple grouping identical on both, with an `_epsN` tolerance
  vector at the `ε` `P0.4` measured.
- **Approval gate:** none (internal arithmetic).
- **Depends on:** `P3.0` (rule selected + `ε` pinned).
- **OPEN QUESTION (O4, design §12):** "the full `evidenceId → lineage` map... must be authored and reviewed
  against real co-occurrence in the corpus." This is a **content-authoring task**, not just a code change —
  flag to the corpus owner (Cline, per the Phase-0/1 plan's coordination note) before this step can complete;
  do not invent the lineage map unilaterally in code.

### P3.2 — Datapoint-scoped clean-support (N/M per datapoint, not per-section blanket)

- **Action:** Refine the clean-support mechanism (`SynthesisEngine.swift:219-223`, currently: a `.negative`
  section contributes one blanket support for `Θ∖coveredThreats(section)`) to be **datapoint-scoped**: track
  how many of a section's *specific sub-checks* (datapoints) ran clean out of how many were scheduled for that
  section (N/M at the datapoint level, distinct from the Phase-1 M3 completeness K/M at the *section* level),
  and scale clean support by that finer ratio rather than an all-or-nothing per-section flag. This is what
  lets "Prophet ran 4/5 sub-checks clean, 1 inconclusive" produce calibrated partial clean-support instead of
  the current binary treatment.
- **macOS:** `SynthesisEngine.swift:219-223` clean-support loop; requires `SectionStatusEntry` (or a new
  per-datapoint status list) to carry sub-check-level granularity — check whether `Synthesizer.swift`'s
  per-discipline functions already produce sub-check statuses that are currently collapsed to one section
  status before reaching the engine (likely yes, given `Synthesizer.swift`'s `subs.append(...)` pattern seen
  at `:459` and elsewhere — re-grep to confirm the collapse point).
- **Windows:** twin in `SynthesisEngine.cs` + `Synthesizer.cs`.
- **Serves:** design §3 M3 refinement, §11 Phase 3; NE2 Δ3/Δ4/Δ6/Δ8 (verify each Δ's specific citation against
  `DELTAS.md` before implementing — this plan has not re-derived them individually).
- **Effort:** L (touches the Synthesizer→SynthesisEngine boundary, not just the engine).
- **Real-data acceptance bar:** a real Prophet run where 4/5 internal checks pass and 1 times out reports
  **visibly lower** clean support / completeness than a real run where all 5 pass, without the timeout being
  silently absorbed into a binary "ran clean" flag. Observed by running both.
- **Parity:** the datapoint-count + scaling rule identical on both.
- **Approval gate:** if this surfaces a new "N/M sub-checks" figure, coordinate with the Stage-5
  `EvidenceCompleteness` DTO (`P1.4`) so there is one completeness concept, not two competing ones — flag to
  `syxx-gui-architect` before introducing a second completeness figure.
- **Depends on:** `P3.1` is not a hard blocker for this step's logic, but land after it for corpus stability
  (both touch the same combination core).

### Phase 3 acceptance bar (design §11, verbatim scope)

A **real DNSSEC-strip capture** with N clean sections still names REWRITE; a capture where a resolver **both**
NX-hijacks and DNSSEC-strips (disjoint lineage) reports **higher** confidence than either alone (no 74→31
inversion).

---

## Phase 4 — Verdict/confidence semantics (TBM two-level readout)

### Scope

Design §5.4 I6, §8.1-8.2 (partial), §11 Phase 4. Closes: NE1 Δ3, NE4 Δ4, NE7 Δ5.

### P4.1 — Confidence = `Bel(class-set)` as the single credal quantity (I6)

- **Action:** Replace the current single-hypothesis `verdictBetP`/`confInt` readout
  (`SynthesisEngine.swift:277-279`, which reports the pignistic probability of the single named hypothesis)
  with the **mechanism-class-set** belief (`Bel` of the class the verdict names, e.g. `{spoofing,
  interception_onPath}` for REWRITE — the class partition is `frame`'s coarser grouping, not `Hypothesis`
  itself). This requires first defining the mechanism-class partition (P4.2) since I6's `Bel(class-set)` is
  meaningless without one.
- **macOS:** `SynthesisEngine.swift:277-279` (readout), depends on the class-set definition from `P4.2`.
- **Windows:** twin readout in `SynthesisEngine.cs`.
- **Serves:** design §5.4 I6; NE1 Δ3 (confidence semantics currently conflate gate/name/confidence — design
  §1 P3 diagnosis, §104-114).
- **Effort:** M.
- **Real-data acceptance bar:** the real DNSSEC-strip capture reports a **class-level** confidence (belief
  the mechanism is in the REWRITE class) that does not fluctuate based on which specific within-class
  hypothesis (spoofing vs on-path interception) the pignistic tiebreak happens to favor — i.e., two captures
  with identical class-level evidence but different within-class emphasis report the **same** headline
  confidence. Observed by constructing/running both.
- **Parity:** class-set definition + `Bel` computation identical on both.
- **Approval gate:** none (internal number; the class-set *names* are copy-gated separately, see `P4.3`).
- **Depends on:** Phase 3 (needs the committed rule's `Bel` semantics finalized; a rule swap changes what
  `Bel(class-set)` even means).

### P4.2 — Define and implement the mechanism-class partition

- **Action:** Implement the class partition the design proposes (design §6.4, "Mechanism-class partition
  (fixes P3 confidence + margin)") — grouping `Hypothesis` cases into named mechanism classes (e.g. REWRITE =
  `{spoofing, interception_onPath}`, per the design's own working example at §11 Phase 4/§O5). **This is
  explicitly an open question (O5, design §12): "Is REWRITE = `{spoofing, interception_onPath}` the right
  partition... or should a portal-that-NX-hijacks case join a broader BLOCK/REWRITE superclass?"** Do not
  invent the final partition unilaterally — implement the design's stated working example, flag the O5
  boundary question to the user/design owner for sign-off before treating the partition as final, since
  "it drives the headline number" per the design's own caution.
- **macOS:** new type (`MechanismClass` enum or similar) near `Hypothesis` in `Conclusion.swift`, with a
  `Hypothesis → MechanismClass` mapping function.
- **Windows:** twin in `Conclusion.cs`.
- **Serves:** design §6.4, O5 (§12); prerequisite for `P4.1`/`P4.3`.
- **Effort:** S/M.
- **Real-data acceptance bar:** N/A (structural/data-modeling step; validated by `P4.1`'s acceptance bar).
- **Parity:** identical partition table on both platforms.
- **Approval gate:** **the partition boundary itself needs user sign-off (O5)** before Phase 4 is considered
  design-complete — this is a design decision, not just an implementation detail, despite living in code.
- **Depends on:** Phase 3.

### P4.3 — Within-class secondary split + margin, gate-consistent naming

- **Action:** Report the within-class split (e.g. "REWRITE — 70% spoofing / 30% on-path interception, of the
  74% class confidence") as a **secondary**, clearly-subordinate figure — never the headline — and recompute
  the referral margin (`SynthesisEngine.swift:285-290`) as the margin **between mechanism classes**, not
  between raw `Hypothesis` values, so a class with two closely-matched internal hypotheses doesn't spuriously
  trigger a thin-margin referral against itself.
- **macOS:** `SynthesisEngine.swift:285-290` margin computation; new Stage-5 `WithinClassEntry` population
  (already scaffolded in `Stage5Presentation.swift` per `P1.0` — this step is what actually fills it, closing
  the loop `ENGINE-PHASE01-PLAN.md` P1.0 left as "populate nothing yet").
- **Windows:** twin margin computation + `WithinClassEntry` population.
- **Serves:** design §6.4, §11 Phase 4; NE4 Δ4, NE7 Δ5 (verify each against `DELTAS.md` before implementing).
- **Effort:** M.
- **Real-data acceptance bar:** the real DNSSEC-strip surfaces as **"interference detected — spoofing or
  on-path proxy — high confidence"** (the design's own verbatim example, §11 Phase 4), not "48%, ask a human";
  the cross-platform golden boundary vector agrees on the integer verdict. Observed by running the real
  capture end-to-end through the CLI/GUI report path.
- **Parity:** both platforms; add an `_exact` boundary vector.
- **Approval gate:** the headline **wording** ("interference detected...") is copy-gated → emit typed
  class name + confidence + margin fields and a `CopyToken`, not the finished sentence — `syxx-gui-architect`
  proposes the exact phrasing.
- **Depends on:** `P4.1`, `P4.2`.

### Phase 4 acceptance bar (design §11, verbatim scope)

The real DNSSEC-strip surfaces as "interference detected — spoofing or on-path proxy — high confidence," not
"48%, ask a human"; the cross-platform golden boundary vector agrees on the integer verdict.

---

## Phase 5 — Evidence model upgrade (cross-vantage matrix → DST mass)

### Scope

Design §6.2, §7, §11 Phase 5. Closes: NE3 Δ1/Δ2/Δ3/Δ4, NE6 Δ1. **Note:** `ENGINE-PHASE01-PLAN.md`'s `P1.9`/
`P1.10` already implement this phase's **presentation-only, verdict-gate-only** slice (matrix into Stage 5 +
discrepancy-forbids-clean, no DST mass change) as a Phase-1 rule-agnostic step. **Phase 5 here is the
remainder**: actually feeding the cross-vantage discrepancy classification **into the DST mass** as a genuine
evidence source (not just a display-layer gate), which does depend on the committed combination rule (Phase 3)
because it adds a new mass-contributing support whose interaction with the selected rule must be verified.

### P5.1 — Cross-vantage discrepancy as a scored evidence source (not just a gate)

- **Action:** Once `P1.9`/`P1.10` (Phase 1) have the canonical answer-matrix classification
  (`AnswerComparisonClass.discrepancy` vs `.benignSetVariation`/`.agreement`) available, add it as an actual
  `EvidenceItem`-equivalent mass contribution in `SynthesisEngine.swift`'s support-construction loop — a
  genuine `discrepancy` classification contributes committed mass toward the threat union (interception/
  spoofing), scaled by how many independent public vantages corroborate the consensus side (more corroborating
  vantages → stronger mass, per the design's "typed `DivergenceKind`" concept, §11 Phase 5). A
  `benignSetVariation` (multi-homed CDN) contributes **no** mass either way (neither threat nor clean) —
  it must remain silent, not accidentally read as corroborating "clean."
- **macOS:** new support-construction branch in `SynthesisEngine.swift` (near the existing dampener/positive
  loops, :153-224), fed by the `P1.9` matrix output threaded into `synthesize()`'s inputs (additive parameter,
  same pattern as `P1.4`'s invocation-profile thread).
- **Windows:** twin in `SynthesisEngine.cs`.
- **Serves:** design §6.2, §7 (typed `DivergenceKind`); NE6 Δ1 (closes the *scoring* half; `P1.10` already
  closed the *verdict-gate* half).
- **Effort:** L.
- **Real-data acceptance bar:** the same real locally-poisoned run used in `P1.9`/`P1.10`'s acceptance bar now
  **scores** toward interception (not just gates away from clean) **and** displays the true public consensus
  and the outlier; a real multi-homed CDN domain contributes **zero** mass in either direction (confirm by
  asserting the pre/post-`P5.1` `Bel`/`Pl`/`m(Θ)` trail is unchanged for the CDN case). Observed by running
  both real scenarios.
- **Parity:** the mass-scaling-by-corroborating-vantage-count rule identical on both, with an `_epsN` vector.
- **Approval gate:** none (internal mass contribution; the matrix *display* was already copy-gated in `P1.9`).
- **Depends on:** Phase 3 (new mass source's interaction with the selected combination rule must be verified,
  not assumed compatible with whatever `P0.4` picks); `P1.9`/`P1.10` (Phase 1, for the classification itself).

### P5.2 — Typed `DivergenceKind` (distinguish discrepancy causes)

- **Action:** Before scoring a discrepancy uniformly, classify **why** the vantages diverge — e.g.
  geo-routing (benign), CDN load-balancing (benign, already partly covered by `benignSetVariation`),
  DNS-based ad/malware blocking by a legitimate corporate resolver (context-dependent), vs. no plausible
  benign explanation (the strongest signal). This typed classification feeds `P5.1`'s mass-scaling — a
  `DivergenceKind.noExplanation` discrepancy scores higher than one with a plausible-but-unconfirmed benign
  cause, which itself should force referral (composing with the Phase-2 M2 machinery) rather than silently
  scoring low.
- **macOS:** new `DivergenceKind` enum, likely alongside `AnswerComparisonClass` in `Stage5Presentation.swift`
  (already has the answer-matrix scaffolding from `P1.0`) or a new small file; consumed by `P5.1`.
- **Windows:** twin.
- **Serves:** design §7 ("typed `DivergenceKind`"), §11 Phase 5.
- **Effort:** M.
- **Real-data acceptance bar:** a real geo-routed CDN discrepancy classifies as a benign `DivergenceKind` and
  scores near-zero; a real discrepancy with no available benign explanation classifies as `.noExplanation`
  and scores at `P5.1`'s upper mass range. Observed by running both real cases.
- **Parity:** identical classification logic on both.
- **Approval gate:** none (internal typed field feeding scoring; any surfaced label is copy-gated separately).
- **Depends on:** `P5.1` (or land together — they're tightly coupled).

### P5.3 — Three honesty lines + time anchor (already scaffolded by `P1.11`; verify full population)

- **Action:** `ENGINE-PHASE01-PLAN.md`'s `P1.11` already specifies populating `HonestySignals`
  (hard-gate/conflict/uncertainty) + `generatedAt` as a **Phase-1, rule-agnostic** step. This Phase-5 item is
  purely a **verification checkpoint**: confirm `P1.11` is fully landed and its `conflict` signal correctly
  reflects the *new* `P5.1`/`P5.2` discrepancy-driven conflict sources (a genuine cross-vantage contradiction
  should be able to trigger the `conflict` honesty signal, which `P1.11`'s original scope — written before
  Phase 5 existed as a mass source — may not have anticipated). If `P1.11` landed before `P5.1`, re-verify its
  `conflict` derivation still fires correctly now that a new mass-contributing evidence source exists.
- **Serves:** design §11 Phase 5 (folds NE3 Δ2/Δ4 verification into this phase); no new file:line — a
  verification/regression step.
- **Effort:** S.
- **Real-data acceptance bar:** the real locally-poisoned run's `conflict` honesty signal fires (genuine
  evidential contradiction between the poisoned local answer and the public consensus), independent of the
  `hardGate`/`uncertainty` signals. Observed by running.
- **Depends on:** `P1.11` (Phase 1) and `P5.1`/`P5.2`.

### Phase 5 acceptance bar (design §11, verbatim scope)

A real locally-poisoned run both scores toward interception **and** displays the true public consensus and
the outlier; a multi-homed CDN does **not** false-fire a discrepancy.

---

## Phase 6 — Probe robustness (populates the validated-decisive-set for M4)

### Scope

Design §3 M4 (checklist, R7), §11 Phase 6, AMEND-O2 (§12 O2). Closes: NE2 Δ9/Δ10/Δ11, NE7 Δ3, NE5 Δ3. This is
the phase that finally **populates** the `validatedDecisiveActs` set Phase 2 (`P2.3`) scaffolded empty.

### P6.1 — Author the versioned, `WitnessSigner`-signed `enum DecisiveAct` catalog

- **Action:** Per AMEND-O2 (design §12 O2, §13 revision-log row): the decisive-act set must be a **versioned**
  enum whose membership and version are signed by `WitnessSigner` for forensic reproducibility — not a bare
  code-level set. Author the catalog structure (empty membership initially — this step is the *scaffold +
  signing plumbing*, not yet the population of specific acts, which is `P6.3`/`P6.4` below once their
  checklists are implemented).
- **macOS:** new `Synthesis/DecisiveAct.swift`; sign via `WitnessSigner.swift`'s existing signing primitive
  (re-grep its exact `sign`/`func` name and signature before wiring — this plan has confirmed the file exists
  at 167 lines with a `WitnessSnapshot` struct at `:11`, but has not traced its exact signing API).
- **Windows:** twin `Synthesis/DecisiveAct.cs`, signed via `WitnessSigner.cs` (168 lines, confirmed to exist).
- **Serves:** design §3 M4, AMEND-O2; prerequisite for `P6.3`-`P6.5`.
- **Effort:** M.
- **Real-data acceptance bar:** a real conclusion's evidence trail includes the signed `DecisiveAct` catalog
  version + (empty) membership, verifiable via `WitnessSigner`'s existing verification path, byte-identical on
  both platforms. Observed by running a real conclusion through the signing path.
- **Parity:** catalog structure + signing byte-identical.
- **Approval gate:** none (internal trust-plumbing).
- **Depends on:** Phase 2 (`P2.3`, the empty scaffold it fills).

### P6.2 — Structural NXDOMAIN rule (hijack detection independent of content heuristics)

- **Action:** Implement the design's "structural NXDOMAIN rule" (§11 Phase 6) — detect a round-robin
  block-page substitution or NXDOMAIN-hijack pattern via **structural** DNS-response characteristics (e.g.
  response codes, answer-section shape across repeated/varied queries) rather than content-string heuristics,
  so it's resistant to a censor varying the block-page text.
- **macOS/Windows:** likely lands in the Prophet/Mirror probe layer (`Synthesizer.swift` disciplines, or a
  probe-level file — re-grep for existing NXDOMAIN handling, e.g. search `NXDOMAIN`/`nxCanaryHijack` in
  `Synthesizer.swift`/`.cs` and the relevant DNS probe files before placing this).
- **Serves:** design §11 Phase 6 (verbatim: "a real round-robin block page trips the hijack (Positive, not
  'atypical')"); NE2 Δ9/Δ10/Δ11 (verify exact Δ mapping against `DELTAS.md` before implementing).
- **Effort:** L.
- **Real-data acceptance bar:** a **real round-robin block page** (multiple distinct substituted answers
  across repeated queries, a known censor evasion technique) trips a `.positive` hijack finding, not a
  downgraded "atypical" advisory. Observed by running against a controlled round-robin block-page rig.
- **Parity:** both platforms; this becomes a Phase-6 `DecisiveAct` candidate once its checklist (below) passes.
- **Approval gate:** none (detection logic; any new advisory text is copy-gated).
- **Depends on:** `P6.1` (scaffold ready to receive this once validated).

### P6.3 — Multi-control DNSSEC quorum + DO-bit gate (authenticated-only, per AMEND-O2)

- **Action:** Per AMEND-O2 (design §12 O2, §13): the DNSSEC canary quorum must count **only RRSIG-validated**
  controls — a `DNSSECStatus.recordsPresentUnverified` posture (confirmed to exist at `DNSSECVerifier.swift:44`
  this session) **does not count** toward the decisive quorum. Implement the DO-bit-gated multi-control
  quorum check and wire `dnssecCanaryStripped` to **only** enter the `DecisiveAct` set when the quorum is
  built entirely from RRSIG-validated (not merely `recordsPresentUnverified`) controls.
- **macOS:** `DNSSECVerifier.swift` (140 lines, `DNSSECStatus` enum confirmed at `:39-49` with cases
  `unsigned`/`unverifiedButPresent`/`recordsPresentUnverified`/`signatureExpiredOrNotYetValid`/`inconclusive`
  — re-grep exact case names before implementing, they may have evolved); the quorum logic likely needs a new
  multi-control aggregation function, and the DO-bit gate needs the query construction path (probe layer, not
  yet located precisely — re-grep `DO` / `dnssecOk` bit-setting in the DNS query builder).
- **Windows:** twin in `DNSSECVerifier.cs` (140 lines, confirmed to exist).
- **Serves:** design §3 M4, AMEND-O2, §11 Phase 6 (verbatim: "a quorum built from `recordsPresentUnverified`
  controls does not reach the decisive tier").
- **Effort:** L.
- **Real-data acceptance bar:** a real capture with a genuine RRSIG-validated multi-control quorum showing a
  strip reaches the decisive tier (feeds `DecisiveAct`); a real capture where the "validation" is only
  `recordsPresentUnverified` does **not** reach the decisive tier, even with the same apparent strip pattern.
  Observed by running both against real signed and real (deliberately) unvalidated zones.
- **Parity:** quorum size + RRSIG-only filter identical on both.
- **Approval gate:** none.
- **Depends on:** `P6.1`.

### P6.4 — Encrypted-transport wildcard reference + full R7 GroundTruth validation checklist

- **Action:** Implement the design's R7 checklist for `authoritativeDivergesConfirmed`: signed **and** fresh
  **and** domain/record-bound **and** authenticated-acquisition **and authenticated quorum** (≥2
  disjoint-lineage resolver cross-checks per AMEND-O2) **and** replay-protected. Also implement the
  "encrypted-transport wildcard reference" (design §11 Phase 6) so a real transparent DoH/DoT-terminating proxy
  no longer misreads as `wildcardZone`-benign.
- **macOS:** `GroundTruth` (`DimensionClient.swift:10`, confirmed to exist) needs the freshness/binding/replay
  fields if not already present (re-grep its current fields before assuming what's missing); the signed-quorum
  check composes with `WitnessSigner.swift`; the wildcard-reference fix likely lives in the transport-probe
  layer (Portal discipline — re-grep for existing wildcard-zone handling).
- **Windows:** twin — `GroundTruth` (`DimensionClient.cs:12`, confirmed to exist), `DNSSECVerifier.cs`/probe
  layer twins.
- **Serves:** design §3 M4 (full R7 checklist), AMEND-O2; NE7 Δ3, NE5 Δ3 (verify exact Δ mapping against
  `DELTAS.md`); **this also touches the Dimension server** (`Server/`) since R7's "authenticated acquisition"
  of GroundTruth is a server-side signing/serving concern — flag the server-side sub-scope explicitly to the
  executor; do not assume it's client-only.
- **Effort:** XL (spans client + server; likely the largest single item in this plan — consider splitting into
  a client-side sub-plan and a server-side sub-plan as separate reviewable commits).
- **Real-data acceptance bar:** an unsigned or unauthenticated GroundTruth **cannot** elevate a finding to the
  decisive tier or trip the M4 safety gate, even when its content would otherwise qualify; a real transparent
  Do53/DoH-terminating proxy no longer reads `wildcardZone`-benign. Observed by running against a real
  deliberately-unsigned GroundTruth response and a real transparent-proxy rig.
- **Parity:** the full checklist + signed `DecisiveAct` membership+version byte-identical on both platforms
  (design's own acceptance bar: "the signed `DecisiveAct` membership+version is present in the evidence trail
  and byte-identical on both platforms").
- **Approval gate:** none (trust/validation logic; no new user-visible wording implied).
- **Depends on:** `P6.1`, `P6.3` (shares the authenticated-quorum concept).

### P6.5 — Crypto RNG on Windows (replay-nonce scheme prerequisite)

- **Action:** The design's Phase 6 scope (§11) explicitly calls out "Crypto RNG on Windows" as a named
  sub-item — verify the current Windows nonce/random generation (wherever replay-protection nonces are
  produced for `P6.4`'s replay-safe requirement) uses a cryptographically-secure RNG (`RandomNumberGenerator`/
  `RNGCryptoServiceProvider`-equivalent), not a non-cryptographic `Random`. This is a small, standalone
  hardening item that unblocks `P6.4`'s replay-nonce scheme.
- **Windows:** locate the current nonce-generation call site (likely in `WitnessSigner.cs` or the
  GroundTruth-request path) and confirm/replace with a crypto-secure RNG.
- **macOS:** confirm the Swift twin already uses `SystemRandomNumberGenerator` or equivalent (Swift's default
  is typically crypto-secure; this is primarily a **Windows-side** fix per the design's explicit callout).
- **Serves:** design §11 Phase 6; prerequisite for `P6.4`'s replay-nonce scheme.
- **Effort:** S.
- **Real-data acceptance bar:** N/A beyond a code-level RNG-source audit (this is a hardening/prerequisite
  item, not itself a scoring behavior) — but do not mark it "done" without actually locating and confirming
  the RNG source in the live tree, not assuming.
- **Depends on:** nothing (can land any time before `P6.4`'s replay-nonce step).

### Phase 6 acceptance bar (design §11, verbatim scope)

A real round-robin block page trips the hijack (Positive, not "atypical"); a single benign `cloudflare.com`
lapse does not fire decisive stripping; a quorum built from `recordsPresentUnverified` controls does not reach
the decisive tier; a real transparent Do53 proxy no longer reads `wildcardZone`-benign; an unsigned or
unauthenticated GroundTruth cannot elevate to the decisive tier or trip the safety gate; the signed
`DecisiveAct` membership+version is present in the evidence trail and byte-identical on both platforms.

---

## Phase 7 — Calibration (valid belief-function semantics)

### Scope

Design §8.1-8.2, §11 Phase 7, O1/O7 (§12). Closes: the magic-number proposals (NE1/NE4/NE7).

### Hard gate — O1 (calibration corpus provenance, "the biggest evidence gap")

Per design §12 O1: Phase 7 needs a **labeled tampered/clean corpus per mechanism class**, which does not
exist yet. Options named in the design: a lab harness (dnsmasq NXDOMAIN-hijack rig, on-path DNSSEC-strip rig,
transparent-proxy VM), public censorship datasets, or synthesized fixtures. **This plan does not resolve O1**
— it is a data-acquisition project, not a code-design decision, and per the design's own words "until it
exists, weights stay expert-elicited and the false-secure budget is asserted, not measured." Flag O1's
resolution to the user as a prerequisite decision (which corpus-sourcing strategy to pursue) before Phase 7
implementation steps below can produce a *measured* (not merely *documented*) result.

### P7.1 — Singleton-focal LLRs and set-mass class-vs-clean weights from the corpus (R6)

- **Action:** Once a labeled corpus exists (O1), derive the `WeightRubric` LLR values from measured
  true/false-positive rates per evidence item, replacing the current expert-elicited constants
  (`Conclusion.swift:184+`) — but **only** for singleton-focal weights (an item that names exactly one
  hypothesis); multi-element (set-valued) weights use the class-vs-clean + intra-class split method (design
  §8.1), not a naive per-hypothesis LLR, to avoid the calibration error the design's R6 specifically corrects.
- **macOS/Windows:** `WeightRubric.getWeight` (`Conclusion.swift:185+` / twin in `Conclusion.cs`) — replace
  hardcoded constants with corpus-derived values, preserving the function signature so this is a data change,
  not an API change.
- **Serves:** design §8.1, R6; the magic-number proposals (NE1/NE4/NE7 — verify exact Δ mapping against
  `DELTAS.md`).
- **Effort:** L (statistical derivation + validation, not just a code edit).
- **Real-data acceptance bar:** measured missed-tampering rate on a **held-out** (not training) slice of the
  adversarial corpus is at or below the agreed false-secure budget (§8.3); every weight has a recorded
  derivation (which corpus subset, which statistical method) documented alongside it, not just a bare number.
- **Parity:** identical derived weight table on both platforms (a single derivation, mechanically copied — not
  independently re-derived per platform, which would risk silent divergence).
- **Approval gate:** none (internal calibration constants).
- **Depends on:** O1 resolved (corpus exists); Phase 6 (decisive-act population feeds which items are
  singleton-focal candidates vs already-decisive).

### P7.2 — Coverage calibration of `[Bel,Pl]` and a `BetP` reliability diagram (§8.2)

- **Action:** Validate (not just compute) that the `[Bel,Pl]` interval genuinely brackets the true state at
  the claimed coverage rate on held-out corpus data, and that the `BetP` pignistic point estimate is
  well-calibrated via a reliability diagram — **not** a naive reliability diagram directly on `Bel` (the
  specific error R6 corrects, design §8.2).
- **Serves:** design §8.2, R6.
- **Effort:** M (analysis/validation task with a small supporting harness, likely offline like `P0.4`).
- **Real-data acceptance bar:** interval coverage and the `BetP` diagram are within the design's stated
  tolerance on the held-out corpus. Observed by running the calibration harness and reporting the actual
  measured numbers — not asserting compliance without the measurement.
- **Parity:** the calibration harness + its held-out split identical on both (or run once against a
  platform-agnostic golden-vector export, since this is fundamentally a statistics validation, not a
  runtime behavior — confirm with the executor which is more practical).
- **Approval gate:** none.
- **Depends on:** `P7.1`.

### P7.3 — RPN Occurrence: static expert-elicited default + pluggable adaptive Dirichlet-Multinomial (AMEND-O7)

- **Action:** Ship the default static expert-elicited Occurrence constants (v1 RPN = Severity ×
  detection-tier, honestly labeled per D4/AMEND-O7 — **not** full `S×O×D` until the corpus exists). Separately,
  implement the **opt-in** adaptive Dirichlet-Multinomial per-SSID Occurrence backing, strictly behind
  AMEND-O7's five guardrails: (1) user-owned SQLite storage, (2) weak `α=1` prior, (3) confirmed-only updates,
  (4) Stage-5 transparency (the adaptive state must be visible/inspectable, not a hidden black box), (5)
  `WitnessSigner`-signed state hash. **Fenced to the Stage-5 surfacing axis only — must never feed the belief
  masses or the verdict** (this is a hard architectural boundary the design repeats three times; treat any
  code path that lets the adaptive Occurrence prior influence `SynthesisEngine`'s mass construction as a
  Verdict & Evidence Integrity defect, not a style choice).
- **macOS/Windows:** likely a new `Synthesis/RPNOccurrence.swift`/`.cs` (or wherever RPN findings are currently
  constructed — re-grep `RPN`/`PriorityFactors` in `Stage5Presentation.swift`, which already scaffolds
  `PrioritizedFinding`/`FindingPriority`/`PriorityFactors` per `P1.0`); the adaptive backing needs new
  user-owned SQLite storage plumbing (check whether an existing local-storage pattern exists to reuse, e.g.
  `AnswerLedger`'s persistence approach referenced in `CHANGELOG.md`'s KTRU-12 entry, rather than inventing a
  new storage mechanism).
- **Serves:** design §7 (RPN), D4/AMEND-O7, O7 (§12).
- **Effort:** L (the guardrails are non-trivial to implement correctly, especially the signed-state-hash and
  the strict scoring/surfacing separation).
- **Real-data acceptance bar:** the static default produces documented, derivation-recorded RPN scores on a
  real finding; when the adaptive opt-in is enabled, a confirmed per-SSID update changes the **surfaced**
  Occurrence figure but is proven (by test + a real run diffing pre/post-`Bel`/verdict) **not** to change any
  belief mass or verdict for an identical evidence input. Observed by running both configurations against the
  same real capture and diffing the engine's core numbers (must be identical) vs the Stage-5 Occurrence figure
  (may differ).
- **Parity:** static constants + guardrail implementation identical on both; the SQLite schema and signed-hash
  format must be byte-compatible if a signed state is ever exported/verified cross-platform.
- **Approval gate:** any user-visible framing of "confirmed-only" updates or the adaptive-vs-static distinction
  is copy-gated → `syxx-gui-architect` proposal.
- **Depends on:** `P1.0` (Stage-5 `PriorityFactors` scaffold); independent of `P7.1`/`P7.2` (the static default
  can ship without the full corpus; only the *adaptive opt-in* needs its own guardrail validation, not O1).
- **STARVATION-SAFETY OPEN QUESTION (design §12 O7, remaining item):** "validating that the confirmed-only
  update gate is starvation-safe (not perpetually empty) on real per-SSID traffic" — this needs a real-world
  usage measurement, not just a code review; flag as a post-ship monitoring task, not a pre-ship blocker,
  since the guardrail (confirmed-only) is itself the safety property and a perpetually-empty adaptive state
  degrades gracefully to the static default (verify this degradation path explicitly as part of `P7.3`'s
  acceptance bar).

### Phase 7 acceptance bar (design §11, verbatim scope)

Measured missed-tampering rate ≤ the agreed budget on a held-out adversarial corpus; interval coverage and the
`BetP` diagram within tolerance; every weight has a recorded derivation.

---

## Risks & open questions (consolidated across Phases 2-7)

### Gating open questions (block specific phases, not the whole plan)

- **O3 — fusion rule + `ε` (GATES Phase 3 and everything after it).** Unresolved until `P0.4` runs. This is
  the single most important blocker in this entire document — Phase 2 and the `P0.4` build itself are the
  only work that can proceed today.
- **O4 — lineage-cluster boundaries (GATES `P3.1`).** The full `evidenceId → lineage` map needs authoring
  and corpus review — a content task, flagged to the corpus owner, not invented in this plan.
- **O5 — mechanism-class boundaries (GATES `P4.2`/`P4.1`/`P4.3`).** Needs explicit user/design-owner sign-off
  before treating the class partition as final — it drives the headline number.
- **O1 — calibration corpus provenance (GATES Phase 7 measurement, not the static-default ship).** The
  biggest evidence gap per the design's own words; needs a corpus-sourcing decision from the user.
- **O2/AMEND-O2 — decisive-act freshness window, quorum size, replay-nonce scheme (partly settled;
  remaining detail GATES `P6.3`/`P6.4`'s exact parameters, not their structure).** The representation and
  quorum-authentication bar are fixed; the exact numbers need to be pinned in the signed versioned spec
  (`P6.1`) as it's authored.

### Assumptions / decisions to confirm before implementation

- **Phase 2 independence from the fusion rule** — this plan asserts Phase 2 (M1/M2 split) does not depend on
  `P0.4`'s rule selection, per the design's own dependency graph (§11: "Phase 2... Depends on: Phase 1").
  Confirm this reading is correct before parallelizing Phase 2 against the `P0.4` build.
- **`P6.4`'s server-side sub-scope** — flagged as potentially the largest single item in this plan; confirm
  whether it should be split into an explicitly separate server-side plan/workstream before an engineer picks
  it up, to avoid one oversized "commit" that isn't reviewable.
- **`P7.2`'s harness placement** (per-platform vs shared export) — flagged as needing an executor decision,
  not pre-decided here.

### Where a phase-start would transiently break honesty if sequenced wrong

- **Do not start Phase 3 implementation before `P0.4` exists and has selected a rule** — implementing against
  a guessed rule risks shipping a combination change that the bake-off later shows fails an adversarial
  vector, which would be a *regression* relative to Phase 1's rule-agnostic gates.
- **Do not start Phase 5's `P5.1` mass-scoring before Phase 3 lands** — a new mass source's interaction with
  whatever rule `P0.4` selects must be verified against that rule, not against today's Dempster arithmetic,
  which Phase 3 will have already replaced.
- **`P4.2`'s mechanism-class partition must get O5 sign-off before `P4.1`/`P4.3` are treated as done-done**
  (they can be *implemented* against the design's working example, but not shipped as final without that
  sign-off, since the design itself flags the partition as open).

---

## Handoff note

- **`syxx-neural-executor`** owns Phase 2, `P0.4`, and Phase 6's client-side steps once their gates clear.
  Land **Phase 2 first** (unblocked today) in parallel with building **`P0.4`** (also unblocked today, offline,
  touches no production path). **Do not start Phase 3 until `P0.4` reports a selected rule and pinned `ε`** —
  this is a hard stop, re-verify by checking for the bake-off's recorded result before writing any Phase-3
  code. Phases 4-7 follow in the stated order once their individual gates (O5 for Phase 4, corpus/O1 for
  Phase 7's measurement) are addressed.
- **Server-side work** (flagged explicitly in `P6.4`) needs its own coordination with whoever owns `Server/`
  changes in the live multi-agent split — confirm current server-lane ownership before scheduling `P6.4`'s
  server sub-scope.
- **`syxx-gui-architect`/Codex** render Phase 4's class-headline wording, Phase 5's `DivergenceKind`
  presentation (if surfaced), and any Phase 6/7 advisory text — all pending approval per the standing
  copy-gate rule. The engine emits typed fields/CopyTokens only.
- **Open items requiring explicit user decisions before their gated phase proceeds:** O3 (Phase 3), O4
  (`P3.1`), O5 (Phase 4), O1 (Phase 7 measurement), the `P6.4` server-scope split question, and the `P7.2`
  harness-placement question. None of these should be resolved by an implementing agent's unilateral judgment
  — surface them per this plan's citations when reached.
