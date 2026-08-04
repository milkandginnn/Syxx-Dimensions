# BAKEOFF-DESIGN — Phase-0 fusion-rule + determinism bake-off

**Author:** Syxx Neural Engineer (design & planning only — no feature code lands here)
**Date:** 2026-07-12
**Status:** Proposal pending approval. This document specifies an *offline experiment*; it commits no
shipped verdict and touches no user-visible wording. The winning rule and the measured `ε` become
inputs to Phase 3/4, not decisions made here.
**Scope:** The Phase-0 gate that must run and pass **before** the contested DST-readout/fusion-rule
rewrite (Phases 3–4 of `REASONING-SYSTEM-DESIGN.md` v2.1). It selects the combination rule used *within
a non-independent lineage cluster* (§4.2 of the design) and settles the cross-platform determinism
contract (D2/AMEND-O3) — the two are settled **together** because the selected rule's float profile
determines both what is bit-reproducible and what display tolerance `ε` the golden schema needs.
**Grounds:** `REASONING-SYSTEM-DESIGN.md` v2.1 §4.2 (candidate table) + §11 Phase 0 + §10 (parity);
`STAGE0-GOLDEN-VECTOR-CONTRACT.md` (fixture schema, `_exact`/`_epsN` tiers, integer-gate rule D2);
`neural/NE1.json … NE7.json` (the failure scenarios the winning rule must survive). Every code claim
was re-opened at `file:line` in the live tree on 2026-07-12 (Appendix A).

---

## 0. What this gate decides, in one paragraph

Stage 3 of the target pipeline combines belief masses. The current engine uses **one rule everywhere —
Dempster's — over a flat bag of supports it wrongly treats as independent**, and that single choice is
why a confirmed DNSSEC strip washes to CLEAN (NE1 Δ1), why two independent mechanisms merge-and-cap so
confidence *inverts* 74%→31% (NE1 Δ2), and why a clean canary conflict-reduces a Mirror-confirmed
redirection to "I don't know" (NE2 Δ3). The design (R5) refuses to pre-commit a replacement; it makes
rule selection a **Phase-0 experiment** scored on two axes at once: (a) does the rule, dropped into the
otherwise-fixed pipeline, produce the *honest* verdict on every NE-derived adversarial vector, and (b)
is it **bit-reproducible at the integer-% gate** across the Swift and C# engines. This document defines
the candidate set, the adversarial battery, the determinism test and the `ε`-measurement procedure, the
selection rubric and its fallback ladder, and the O3 numerical spike that answers whether macOS and
Windows can compute the chosen rule identically at the gate or whether an integer-stable readout
boundary is required. **The load-bearing safety property is that the verdict must be correct
*regardless of which rule wins* — the I8 confirmed-threat-mass floor and the M4 safety gate live
outside the DST number, so the bake-off chooses the rule on honesty-of-the-number, determinism, and
cost, never on whether it alone prevents a false-clean.**

---

## 1. The candidate fusion rules

Five candidates, per design §4.2. Each is scored on the same battery (§2) and determinism test (§3).
The property summaries below are the *reason each is in the running*; the bake-off confirms them
empirically on the corpus rather than asserting them.

| # | Candidate | One-line property relevant to Syxx | Cost / risk | Source |
|---|---|---|---|---|
| R-D | **Dempster** (current, `SynthesisEngine.swift:196`,`.cs:170`) | Weights of evidence are **additive** on the same focus when sources agree; native, cheap, associative → order-independent. | Assumes independence → over-counts non-independent evidence (P1 wash-out/over-count); aggressive ∅-renormalization amplifies false "sections disagree" conflict. | Shafer, *A Mathematical Theory of Evidence*, Princeton, 1976; Dempster, *Upper and Lower Probabilities Induced by a Multivalued Mapping*, Ann. Math. Stat. 38, 1967. |
| R-C | **Cautious conjunctive** (Denoeux) | **Idempotent** (`m ⌢ m = m`): redundant/co-derived supports on the same focus do **not** amplify. In engine weight-space this is exactly **`max` of the weights-of-evidence per focus** (see §1.1) — the principled form of what the engine already gropes toward at `:126-156`. | Requires canonical weights via `log`/`exp` on commonality *for a general BBA*; for Syxx's simple-support inputs no `log` decomposition is needed (§1.1), so its float exposure is *lower* than the design's generic warning. Associative/commutative → order-independent. | Denoeux, *Conjunctive and Disjunctive Combination of Belief Functions Induced by Nondistinct Bodies of Evidence*, Artificial Intelligence 172(2-3), 2008. |
| R-Y | **Yager** | Conflict mass is reassigned to **Θ (ignorance)**, not normalized away → cannot manufacture a spurious "sections disagree" spike from a lone context marker. | **Non-associative** → a canonical combination order must be pinned or the result diverges even on one platform; less confident under genuine agreement. | Yager, *On the Dempster–Shafer Framework and New Combination Rules*, Information Sciences 41, 1987. |
| R-DP | **Dubois–Prade** (adaptive conjunctive/disjunctive) | Conflict between two focals is routed to their **union**, never ∅ → partially-reliable sources degrade gracefully instead of fabricating conflict. | **Non-associative** (order must be pinned); result interpretation needs care; more focal elements → more mass bookkeeping. | Dubois & Prade, *Representation and Combination of Uncertainty with Belief Functions and Possibility Measures*, Computational Intelligence 4, 1988. |
| R-M | **Weighted-mass / averaging** (Murphy) | Averages the `n` masses before combining → robust to a few high-conflict sources; **order-independent** (averaging is commutative); trivially deterministic float profile. | Not a true evidence-accumulating rule → can **under-count** genuine independent corroboration; averaging + `n−1` conjunctive passes is not idempotent. | Murphy, *Combining Belief Functions When Evidence Conflicts*, Decision Support Systems 29, 2000. |

**Fixed, not under test:** across *genuinely disjoint-lineage clusters* (R4), combination stays
**Dempster** — independence there is real, and R-D's additivity is the correct behavior (it is what
makes a *second independent* mechanism *raise* confidence in NE1 Δ2 once the lineage split is applied).
The bake-off selects the **within-cluster** rule only. Every vector therefore exercises two combination
sites: the rule-under-test within a cluster, and fixed-Dempster across clusters.

### 1.1 Why the `1−e^{−w}` path makes the cautious rule cheap and native (load-bearing)

Every support the engine builds is a **simple support function**: `SynthesisEngine.swift:194–195`
sets `m(focus) = 1 − e^{−ω}`, `m(Θ) = e^{−ω}`, where `ω = s.weight` is the weight of evidence
(the C# twin is `.cs:164,168`). In Denoeux's canonical decomposition a generalized simple BBA `A^{w}`
has `m(A)=1−w`, `m(Θ)=w`; so the engine's support **is already `A^{w}` with `w = e^{−ω}`** — it is
born in canonical form. Two consequences the design's generic §4.2 warning ("cautious needs log/exp on
commonality") over-states for Syxx specifically:

- **Combining two supports on the same focus.** Conjunctive/Dempster multiplies the canonical weights:
  `e^{−ω1}·e^{−ω2} = e^{−(ω1+ω2)}` → engine-weight `ω1+ω2` (**additive** — matches the engine header
  at `:22`). The **cautious** rule takes the *min* canonical weight: `min(e^{−ω1}, e^{−ω2}) =
  e^{−max(ω1,ω2)}` → engine-weight **`max(ω1, ω2)`**. So in the engine's own weight-space, cautious =
  **"take the strongest single reading per focus, do not sum."** That is *precisely* the ad-hoc rule
  the engine already hand-rolls inside a correlation group (`SynthesisEngine.swift:130–156`: "the
  group's weight for `h` is the strongest single reading across its items (max, not sum)") — but the
  hand-rolled version is keyed on a coarse `source` string and then **capped at
  `maxWeightPerSource = 2.0`** (`:150–156`), and that cap is the direct cause of the NE1 Δ2 inversion.
  The cautious rule is the principled generalization that **dissolves the cap**.
- **No `log` decomposition is needed.** The general cautious rule recovers canonical weights from an
  arbitrary BBA via `w(A) = ∏ commonality^{(−1)^{…}}` (the `log` step §4.2 warns about). Syxx never
  needs it: inputs are *already* simple `A^{w}`, so cautious combination reduces to (i) group supports
  by focus mask, (ii) take `max(ω)` per focus (exact, integer-safe in weight-space), (iii) reconstruct
  the final mass by conjunctively combining the resulting `{A^{max ω}}`. Step (iii) calls `exp` **once
  per distinct focus mask** — *fewer* transcendental evaluations than R-D, which calls `1−e^{−ω}` once
  per support (`:194`). This is a determinism advantage, not a cost (§3, §5).

This §1.1 result is the single most important finding for the bake-off: **the cautious rule is not a
foreign transplant — it is the closed form of the engine's existing group-merge, minus the cap that
causes NE1 Δ2, with a smaller `exp` footprint.**

---

## 2. The adversarial golden-vector battery

Every candidate must clear **all** of §2.1–§2.3. This is a **gate, not a weighted score**: a rule that
misses one NE seed vector or one property test is eliminated before determinism/cost are even weighed
(§4). Fixtures are authored to the `STAGE0-GOLDEN-VECTOR-CONTRACT.md` schema; the `expected` block is
the design-correct output (the oracle), and the *current-engine* output is the wrong one each vector
RE​Ds against today. During the offline bake-off the "engine under test" is each candidate rule dropped
into the otherwise-fixed Stage-0…Stage-4 pipeline (lineage clustering, datapoint-scoped clean-support,
M1–M4 all present); only Stage-3's within-cluster rule varies.

### 2.1 The three NE seed vectors (the fusion-relevant failures)

**SV-1 — wash-out (NE1 Δ1).** `NE1-d1-dnssec-strip-washed-to-clean`.
A Prophet `.positive` carrying `dnssecCanaryStripped` (REWRITE, canonical weight 3.0 on
`{spoofing, interceptionOnPath}`) against Mirror/Portal/Cage/Witness `.negative`, Glutton `untested`.
- *Current engine (RED):* three overlapping clean-supports stack under Dempster to `1−e^{−6}≈0.9975`,
  drive `Bel(threatUnion)` below `threatNameFloor=0.40` (`SynthesisEngine.swift:63,216`) → verdict
  **CLEAN, 55%** (reproduced in NE1.json Δ1).
- *Expected (oracle):* `verdict = REWRITE`, `withinClass = [spoofing, interceptionOnPath]`,
  `confidence` high (≈0.95 for the credal readout), `completeness = {k:5, m:6}`, `hardGate` per the
  untested Glutton, referral **false**.
- *What the rule must deliver, and what it must not be trusted to:* the rule must **not amplify**
  the co-derived clean-supports (idempotence / cautious max-per-focus) so `Bel(threatUnion)` is not
  driven down; **but** the verdict's correctness is guaranteed by **I8** — the confirmed source's own
  committed `m(threatUnion) = 1−e^{−3} ≈ 0.95 ≥ 0.40` forbids CLEAN **before** any dilution, *under
  every candidate rule*. SV-1 therefore has two assertions: (i) the verdict is floored to
  REWRITE/referral under **all five** rules (I8 holds rule-independently, §2.2 P4), and (ii) the
  *reported confidence number* is highest and most honest under the idempotent rule.

**SV-2 — two-mechanism inversion (NE1 Δ2).** `NE1-d2-two-mechanism-inversion`.
A resolver that **both** NX-hijacks (`nxCanaryHijack`, BLOCK, weight 3.0 on `{censorship}`) **and**
strips DNSSEC (`dnssecCanaryStripped`, REWRITE, 3.0), on **disjoint** raw-observation/control lineage.
- *Current engine (RED):* both share `source:"canaries"`, are merged, per-hypothesis-max'd, and
  **capped at 2.0** (`:150–156`), collapsing to `verdict = spoofing, 31%`, forced referral — *lower*
  than `nxCanaryHijack` alone (`74%`). Detecting a second attack made the engine less sure (NE1.json Δ2).
- *Expected (oracle):* with the R4 lineage split, the two acts are **independent clusters** →
  combined **across** clusters by fixed-Dempster → confidence **rises above either alone**; verdict
  names the leading mechanism class with `confidence ≥ max(single-mechanism confidences)`; no referral
  from a manufactured margin collapse.
- *What the rule must deliver:* SV-2 primarily tests the **lineage split + across-cluster Dempster**
  (independence is real), and asserts the **anti-inversion monotonicity** property (§2.2 P2): combining
  two independent confirmed acts must **never** yield lower `Bel(threatUnion)` than either alone. A
  candidate that, mis-clustered, still inverts here is not eliminated on SV-2 alone (the split is the
  fix) — but SV-2 pins that the across-cluster rule stays Dempster and that no within-cluster rule
  re-introduces a cap.

**SV-3 — clean-support conflation (NE2 Δ3).** `NE2-d3-cleansupport-conflation`.
A Mirror `.positive` `redirection` (REWRITE, 2.0 on `{spoofing, interceptionOnPath}`) with a clean
Prophet (NX+DNSSEC canaries pass) and clean/empty Witness, both of which *cover* `{spoofing,
interceptionOnPath}` in the current section-wide mask.
- *Current engine (RED):* clean Prophet drops `Bel(threatUnion)` 0.865→0.463 (conflict 0.748 forces
  referral); clean Witness drops it to 0.104 → verdict flips to **inconclusive**, `Bel(clean)≈0`
  (NE2.json Δ3). A detected redirection demoted to "I don't know" by sections that observed unrelated
  facets.
- *Expected (oracle):* `verdict = REWRITE`, redirection stands named; clean canary support is
  **datapoint-scoped** (§5.2) so it does not touch a threat it never tested; **no** high-conflict
  spurious referral.
- *What the rule must deliver:* the core fix is **datapoint-scoped, N/M-scaled clean-support** (§5.2,
  fixed in the pipeline). The rule is tested on the residual: (i) whichever clean-supports *are*
  co-derived (Prophet + Witness both keyed on the same resolver answer share lineage) must **not stack**
  (idempotence); (ii) genuine cross-focus tension must not read as a false high **conflict** (Yager→Θ
  and Dubois–Prade→union are structurally immune; Dempster renormalizes and reports it; cautious keeps
  it on ∅ but M2 stops manufacturing it). SV-3 asserts `conflict` stays below `conflictReferralAbove =
  0.40` (`:62`) *and* the redirection is named.

### 2.2 The four property tests (rule-invariants the battery pins directly)

**P1 — Idempotence on redundant / co-derived evidence.** Feed the *same* support twice within one
lineage cluster (`A^{ω}` and `A^{ω}` on identical focus/lineage). Assert `combine(m, m) ≡ m` at the
`_epsN` tier.
- Predicted: **R-C passes** (min-weight → `max(ω,ω)=ω`). **R-D fails** (`ω→2ω`). **R-Y fails**
  (conjunctive core, `ω→2ω` with the excess on Θ). **R-DP fails** in general. **R-M fails**
  (`avg(m,m)=m` then `Dempster(m,m)=A^{2ω}`). This is the discriminator that most directly answers the
  wash-out/over-count pathology (P1 in the design). **Only R-C passes P1.**

**P2 — Monotonicity-or-safety-gate (a confirmed act never lowers threat belief → an honest verdict).**
Belief monotonicity is **not** guaranteed under non-independent evidence for any candidate (the design
dropped it as an invariant, §5.4). P2 is therefore a **two-part** assertion:
- (a) *Verdict correctness (must pass, rule-independent):* adding a confirmed act never turns a threat
  verdict into CLEAN. Guaranteed by **M4/D1** (validated-decisive safety gate) and **I8** (committed
  confirmed-mass floor) — both outside the DST number. Asserted to pass under **all five** rules.
- (b) *Number honesty (scored, not gated):* record whether `Bel(threatUnion)` rises, holds, or falls
  when a second confirmed same-direction act is added within a cluster. R-C holds/raises (max); R-D
  raises (additive) but can over-count; R-Y/R-DP can *lower* (mass bleeds to Θ/union); R-M can lower
  (averaging dilutes). Part (b) feeds the §4 honesty score, not the elimination gate.

**P3 — No manufactured ∅-conflict.** Feed a **single** confirmed section plus a benign context marker
(`captivePortalActive`) with M2 in place (context modeled as observation-gated benign explanation, not
negative mass). Assert the reported `conflict` ≈ 0 (no "the sections disagree" line) because there is
only one section.
- Predicted: with M2 fixing the *source* of the false conflict, **all rules** should pass this vector;
  the rule is then differentiated on *genuine* two-section conflict: **R-Y** (→Θ) and **R-DP** (→union)
  never surface a spurious high-conflict referral; **R-D** renormalizes and reports `K` (the NE1 Δ4
  behavior *if* M2 regressed — so this vector also guards the M2 fix); **R-C** keeps conflict on ∅
  (TBM-unnormalized) which is *correct* for real disagreement and inert for agreement. Assert
  `conflict < conflictReferralAbove (0.40)` on the single-section+context vector for every rule.

**P4 — The I8 confirmed-threat-mass floor holds regardless of rule.** Re-run SV-1 under **each** of the
five rules and assert the verdict is floored to referral/tampered **in every case**, even where the
post-combination `Bel(threatUnion)` differs across rules. I8 keys on the confirmed source's **own
committed focal mass** (`m(threatUnion)` before dilution, evaluated outside `Bel`) and on the
`.positive`-blocks-clean section gate (`blocksClean`, `Conclusion.swift:341` + Phase-1 addition), so it
is **rule-invariant by construction** (design §3 M4/D5, §5.4 I8). P4 is the meta-test that *licenses*
the whole selection: it proves the verdict's safety does **not** depend on which rule wins, so the
choice can be made on honesty/determinism/cost without risking a false-clean.

### 2.3 Benign controls (must not false-fire under any rule)

- **BC-1 `benign_cdn`** — multi-homed / geo-divergent A-sets. Expect **clean/inconclusive**, never
  REWRITE; the set-valued answer comparison must not read CDN divergence as a discrepancy act.
- **BC-2 `benign_captive`** — captive portal, no tampering, redirect target == gateway IP. Expect
  **inconclusive + referral** ("a captive portal is present and is itself rewriting DNS…", M2), never a
  confident BLOCK/REWRITE, and **no** ∅-conflict from the portal marker. Guards NE2 Δ1 and NE1 Δ4.

A rule that turns a benign control into a threat class is eliminated exactly as a missed seed vector is.

### 2.4 Predicted per-rule battery outcome (design-time hypothesis, to be *confirmed* by the offline run)

Stated as a hypothesis the experiment tests, **not** an asserted result — Phase 0's job is to run it.

| Test | R-D Dempster | R-C Cautious | R-Y Yager | R-DP Dubois–Prade | R-M Murphy |
|---|---|---|---|---|---|
| SV-1 wash-out (number honesty) | weak (over-counts clean) | **strong** | weak (→Θ) | medium | weak (dilutes) |
| SV-1 verdict (via I8) | pass | pass | pass | pass | pass |
| SV-2 anti-inversion (across-cluster) | **pass** (additive) | pass* | weak | weak | weak |
| SV-3 conflation (post-scoping) | medium | **strong** | strong | strong | medium |
| P1 idempotence | **fail** | **pass** | fail | fail | fail |
| P2b number monotonicity | over-counts | **holds/raises** | can lower | can lower | can lower |
| P3 no false ∅-conflict | pass w/ M2 | pass | **pass** | **pass** | pass |
| P4 I8 rule-invariance | pass | pass | pass | pass | pass |
| BC-1/BC-2 benign | pass w/ M2 | pass | pass | pass | pass |

\* R-C is used *within* a cluster; SV-2's raise comes from across-cluster Dempster (fixed), so R-C
neither helps nor harms the SV-2 raise. **Leading candidate on the battery: R-C (cautious)** — the only
rule that passes P1 idempotence (the direct antidote to the P1 wash-out/over-count pathology) while
holding the number under P2b. The choice is still made by the run + the determinism test (§3–§4).

---

## 3. Cross-platform determinism test (Swift vs C#)

### 3.1 The two-tier assertion recap (D2 / AMEND-O3)

- **`_exact` tier — the GATES.** `verdict`, `withinClass` membership, `referral`, and `completeness`
  are asserted with **exact equality on rounded integer percentages — no epsilon**. Confidence is
  compared as `round(100·x)` **integer**. A vector fails CI if either platform's gate differs by one
  integer. This is what would have caught NE7 Δ6 (rounding straddles the 75% gate) and NE5 Δ1
  (Windows-only false threat mass).
- **`_epsN` tier — display values only.** `belief[]`, raw `uncertainty` (`m(Θ)`), raw `conflict` may
  differ within tolerance **`ε`**. `ε` is **not** pre-pinned (v1's `1e-7`/`1e-6` are withdrawn); it is
  **measured** in this bake-off for the *selected* rule and only then written into `_epsN` (`N` set
  once the rule is chosen). Until then all `_epsN` assertions are marked `pending-bakeoff`.

### 3.2 The numerical hotspots the test must probe

Two places produce cross-platform float divergence, and both must be instrumented per candidate:

1. **The transcendental `exp`** — `SynthesisEngine.swift:194` `1 − exp(-s.weight)` (and `:195`
   `exp(-s.weight)` on Θ) vs `SynthesisEngine.cs:164` `1 − Math.Exp(-s.Weight)` (`:168`). Swift's `exp`
   is Darwin libm; C#'s is `System.Math.Exp`. **IEEE 754-2008 recommends but does not require
   correctly-rounded transcendental functions** (the table-maker's dilemma), so the two runtimes may
   differ by up to ~1 ULP on the *same* input. This is the primary bit-reproducibility hazard and the
   crux of the O3 spike (§5).
2. **Order-dependent float accumulation inside `dempster()`** — `SynthesisEngine.swift:277–291`
   (`raw[inter, default: 0] += prod`, then `/= denom` at the normalization step) vs `.cs:258–271`. The
   `+=` sums multiple `(a,b)` products that intersect to the same focal `inter`, and it does so **in
   `Dictionary` enumeration order**, which differs between Swift and C# (and is not guaranteed
   stable run-to-run). Floating-point addition is **not associative**, so a different summation order
   can change the result by 1 ULP **before `exp` is even considered**. **This is a latent determinism
   defect in the current engine that the bake-off must surface and the contract must fix: the
   combination must accumulate over a canonical (sorted) key order on both platforms.** Any candidate
   rule that iterates a hash map to accumulate mass inherits this hazard (all five do); the test pins
   the fix as a rule-agnostic requirement.

### 3.3 Procedure to MEASURE `ε` for each candidate

For each candidate rule, over the **entire corpus** (SV-1..3, P1..4 vectors, BC-1/2, plus the ≥1 real
capture per mechanism class required by Phase 0), on **both** platforms:

1. Run the rule inside the fixed pipeline; dump, per vector, the raw pre-round outputs:
   `Bel(class-set)`, `Pl(class-set)`, `m(Θ)` (uncertainty), `conflict`, and per-hypothesis `BetP`.
2. For each numeric field `f` and vector `v`, compute the cross-platform absolute delta
   `δ(f,v) = |f_swift(v) − f_csharp(v)|`.
3. **`ε_rule = max over all (f,v) of δ(f,v)`** — the measured worst-case display divergence for that
   rule. Report the distribution (max, p99, count of nonzero deltas), not just the max.
4. **Separately** compute the **integer-gate agreement**: for every gate field, assert
   `round(100·x)_swift == round(100·x)_csharp` (with a *single shared rounding mode*, §3.4). Count
   **integer disagreements** (must be **zero** for the rule to pass the determinism gate) and record
   the **minimum margin to a half-integer**, `min_v |100·Bel(v) − (⌊100·Bel(v)⌋ + 0.5)|` — the
   headroom before a 1-ULP `exp` difference could flip a gate. This margin drives whether the
   integer-stable readout boundary of §5 is needed.
5. The **winning** rule's `ε_rule` is pinned into the golden schema's `_epsN` fields, rounded **up** to
   the next clean bound with margin (e.g. next power of ten above the measured max, and no tighter than
   `10× ε_rule`). `_exact` tiers stay integer-exact regardless.

### 3.4 Two determinism requirements the contract pins for *all* candidates

- **Canonical accumulation order.** Both engines must iterate focal-mask keys in a **sorted, identical
  order** when accumulating `raw[inter] += prod` and when reducing masses to `Bel/Pl/BetP` — replacing
  the current unordered `Dictionary` enumeration (`:277–291` / `:258–271`). This removes hazard §3.2(2)
  independent of rule. Non-associative candidates (R-Y, R-DP) additionally require a **pinned support
  combination order** (e.g. sort supports by `(focusMask, lineageId)`), or their result is
  non-deterministic even single-platform.
- **Matched rounding mode.** Swift `Int((x*100).rounded())` uses `.toNearestOrAwayFromZero`
  (`SynthesisEngine.swift:241`); C# `(int)Math.Round(x*100)` uses the default `MidpointRounding.ToEven`
  (banker's, `SynthesisEngine.cs:224`) — the exact NE7 Δ6 split (at `100·Bel = 74.5`: Swift→75 = not
  referred, C#→74 = referred). The contract mandates a **single shared mode on both platforms**
  (recommend `AwayFromZero` so C# matches Swift, or `ToEven` on both — the choice is arbitrary but must
  be *one*). This is a rule-agnostic parity fix, but the bake-off must apply it before measuring
  integer-gate agreement or the measurement is confounded by the rounding split.

---

## 4. Selection procedure + decision criteria

### 4.1 Scoring: a gate, then a lexicographic tie-break

Selection is **not** a single weighted sum (that could trade a false-clean for a smaller `ε`). It is a
**gate followed by a lexicographic order**:

1. **Correctness gate (pass/fail).** The rule must produce the **oracle verdict** on **every** SV-1..3,
   pass **P1–P4**, and keep **BC-1/BC-2** benign. Any miss → **eliminated**. (P4 guarantees every
   surviving rule is verdict-safe; the remaining axes only pick *among safe rules*.)
2. **Determinism gate (pass/fail).** **Zero** integer-gate disagreements across platforms on the whole
   corpus under the §3.4 canonical-order + matched-rounding contract. A rule that cannot be made
   integer-stable at the gate → eliminated (or sent to the §4.3 fallback ladder).
3. **Tie-break, in order:**
   - (i) **Least aggressive on conflict consistent with correctness** (design §4.2 selection rule) —
     prefer the rule that does not manufacture referral from benign tension. Ranks R-Y/R-DP/R-C above
     R-D here.
   - (ii) **Number honesty** — best P2b behavior and the highest, most honest confidence on SV-1
     (idempotent rules win; averaging/Θ-bleeding rules understate).
   - (iii) **Cost / determinism margin** — fewest transcendental `exp` evaluations (§1.1 favors R-C),
     associativity (R-D, R-C, R-M are order-free; R-Y, R-DP need pinned order), largest measured margin
     to a half-integer (§3.3.4), simplest twin implementation on both platforms.

### 4.2 Predicted decision (hypothesis, pending the run)

Under §2.4 + §3, **R-C (cautious conjunctive) is the predicted winner within a cluster**, with **R-D
(Dempster) retained across disjoint-lineage clusters**: R-C is the sole P1-idempotent rule (kills
wash-out/over-count at its root), it is the closed form of the engine's existing group-merge minus the
cap (§1.1), it is associative (order-free) and calls `exp` *fewer* times than R-D. R-Y and R-DP are the
strongest *conflict-routing* candidates but pay a non-associativity determinism tax; R-M is the
most trivially deterministic but under-counts corroboration (P2b) and is not idempotent. **The decision
is nonetheless made by the experiment, not asserted here** (design R5/O3).

### 4.3 Fallback ladder if no rule is bit-reproducible at the gate

Bit-reproducibility of the *float* is **not** the requirement — integer-stability of the *gate* is
(D2). If a candidate cannot reach zero integer-gate disagreements, descend this ladder:

1. **Matched rounding + canonical order (§3.4).** Apply first; expected to remove the NE7 Δ6 class and
   the hash-order class outright.
2. **Integer-stable readout boundary (dead-band).** Route any vector whose `100·Bel(class)` lands
   within a measured dead-band `Δ_db` of a half-integer to **referral** (never silently to either side
   of the gate). `Δ_db` is set to `> ε_rule·100` so a 1-ULP `exp` divergence can never cross the
   band. This converts a would-be cross-platform flip into a *shared, deterministic* referral — an
   honest "ask a human at the exact boundary," never a divergent verdict. (§5.3.)
3. **Quantize the readout before the gate.** Quantize combined *weights* (`ω`, exact in weight-space
   for R-C via `max`) or the final `100·Bel` to a fixed decimal grid coarser than `ε_rule` before
   rounding, so both platforms round identical quantized inputs.
4. **Shared deterministic `exp`.** Replace libm `exp` at `:194`/`:164` with one shared, bit-identical
   polynomial/fixed-point `exp` compiled from a single spec on both platforms. Heavy; only if 1–3 fail.
5. **Rule swap.** If a theoretically-preferred rule cannot be made integer-stable even with 1–4, select
   the **highest-ranked rule that IS integer-stable** on the corpus. R-C's `max`-in-weight-space and
   minimal `exp` footprint make it the most likely to survive this ladder; R-M's averaging is the most
   trivially stable if a purely-deterministic fallback is ever forced (at the P2b honesty cost).

---

## 5. The O3 numerical spike

### 5.1 The question, stated precisely

*Can the macOS (Swift) and Windows (C#) engines compute the chosen rule's combination such that
`round(100·Bel(class-set))`, `round(100·BetP(verdict))`, the `withinClass` membership, and the referral
boolean are **bit-identical integers** on every corpus vector — or is an integer-stable readout
boundary required to guarantee gate agreement?*

### 5.2 Why bit-identical `exp` cannot be assumed (so we do not rely on it)

The only transcendental in the combination is `exp` at `SynthesisEngine.swift:194–195` / `.cs:164,168`.
IEEE 754-2008 §9.2 lists `exp` among *recommended* correctly-rounded operations — **recommended, not
required** — precisely because of the table-maker's dilemma (guaranteeing the correctly-rounded last
bit can need unbounded intermediate precision). Darwin libm and .NET's `Math.Exp` are independent
implementations; both are typically ≤0.5–1 ULP accurate but there is **no cross-runtime guarantee they
return the identical bit pattern** for a given `−ω`. Therefore the spike must **not** try to *prove*
bit-identical `exp`; it must **measure** whether any residual `exp` divergence (plus the §3.2(2)
accumulation-order divergence, which is *fixable* to exact) ever reaches the *integer gate*.

### 5.3 The integer-stable readout boundary (the robust answer)

A 1-ULP divergence in `Bel` is ~`1e-16` relative; it flips `round(100·Bel)` **only** when `100·Bel`
sits within ~`1e-14` of a half-integer `x.5`. So the honest, robust contract is not "make `exp`
identical" but **"make the gate insensitive to sub-`ε` divergence":**

- **Matched rounding mode** (§3.4) removes the *deterministic* half-integer split (NE7 Δ6).
- **Canonical accumulation order** (§3.4) removes the hash-order divergence class entirely (it is
  exactly reproducible once ordered).
- **A measured dead-band `Δ_db`** around each half-integer, wider than `ε_rule·100`, routes boundary
  vectors to a **shared deterministic referral**. Because the band is wider than any possible `exp`
  divergence, both platforms land on the *same* side (into the band → referral) for every input — gate
  agreement is then guaranteed *without* bit-identical `exp`.

### 5.4 The spike's concrete deliverable and acceptance

Deliverable (offline, no shipped-verdict change): for the **selected** rule, a table over the whole
corpus of `(vector, Bel_swift, Bel_csharp, δ, round100_swift, round100_csharp, margin-to-half-integer)`
plus the aggregate `ε_rule`, the count of nonzero `δ`, and the count of integer-gate disagreements
(target **0** after §3.4).
- **If integer disagreements are 0 with margin ≥ some safe threshold** → answer O3 "yes, integer-stable
  at the gate without a dead-band"; pin `ε_rule` into `_epsN`; no boundary needed.
- **If any vector's margin is unsafe (≤ `ε_rule·100`)** → answer O3 "an integer-stable readout boundary
  is required"; set `Δ_db` from the measured `ε_rule` and add the §5.3 dead-band (fallback ladder step
  2). Add the NE7 Δ6 boundary vector (`100·Bel = 74.5`) as a permanent regression fixture either way.
Acceptance: the golden runner replays the whole corpus on both platforms with **zero `_exact`-tier
divergence**, and the selected rule + `ε_rule` (+ `Δ_db` if needed) are recorded with the vectors that
justified them (Phase-0 acceptance bar, design §11).

---

## 6. Platform parity & handoff

- **Both engines change identically.** Every element here lands on `macOS/Sources/SyxxKit/Synthesis/
  SynthesisEngine.swift` **and** `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs` as line-for-line
  twins: the selected within-cluster rule, the canonical accumulation order, the matched rounding mode,
  and (if required) the dead-band. A `SyxxKit` reasoning change without its `Syxx.Kit` twin is a parity
  defect the golden runner will red.
- **This bake-off is offline and changes no shipped verdict.** It selects the rule and measures `ε`;
  the rule is *committed* in Phase 3 and the readout in Phase 4 (design §11). No Server change is
  implicated at Phase 0 (the R7 signed/authenticated GroundTruth that touches the Dimension server is
  Phase 6).
- **Handoff.** The corpus + runner are built by Cline (CL-3) to the `STAGE0-GOLDEN-VECTOR-CONTRACT.md`
  schema; **Neural** (this agent) owns the rule-selection labels/oracle and sets `ε` from the run;
  **`syxx-neural-executor` / `syxx-engineer`** implement the selected rule + determinism contract on
  both platforms in Phases 3–4; **`syxx-gui-architect`** surfaces the credal confidence, the within-
  class split, and any boundary-referral wording (a surfacing proposal, pending approval — MINOR-4).
- **Any user-visible wording** (e.g. the boundary-referral line) is a **proposal pending approval**,
  not decided here.

---

## 7. Risks & open questions

- **R1 — `exp` divergence at an unlucky vector.** A real capture could land `100·Bel` on a half-integer
  within `ε` and flip a gate. *Mitigation:* the §5.3 dead-band converts any such flip into a shared
  referral; the spike measures the margin explicitly. **Residual risk: low** once the dead-band is in.
- **R2 — Latent hash-order non-determinism (currently unguarded).** The existing `dempster()`
  accumulates over unordered `Dictionary` keys (`:277–291` / `:258–271`); today's engine may *already*
  be non-bit-reproducible run-to-run and platform-to-platform independent of the rule choice. The
  bake-off must land the canonical-order fix as a **precondition** or every `ε` measurement is
  confounded. **Risk: medium; fix is mechanical but must precede measurement.**
- **R3 — Non-associative candidates need a pinned order.** If R-Y or R-DP wins on conflict-routing,
  their result depends on combination order; without a pinned canonical order they are
  non-deterministic even single-platform. **Risk: medium; adds a determinism tax that ranks them below
  R-C in §4.1(iii).**
- **R4 — Corpus sufficiency.** `ε` and the integer-margin are only as trustworthy as the corpus; a
  sparse corpus can hide a near-half-integer vector. *Mitigation:* require the ≥1 real capture per
  mechanism class (Phase-0 bar) **plus** a synthetic sweep that deliberately places `100·Bel` near
  half-integers for each rule. **Risk: medium; addressable by adversarial fixture generation.**
- **R5 — I8 is the whole safety load.** Selection on honesty/determinism/cost is only sound *because*
  P4 shows the verdict is rule-invariantly safe via I8/M4. If Phase-1's I8/`.positive`-blocks-clean
  scaffolding is not actually in place when the bake-off runs, a wrong rule *could* false-clean and the
  selection loses its safety net. **Dependency, not a defect:** Phase 0's battery must run against a
  pipeline that already has the Phase-1 gates, or the P4 assertion is vacuous. Flag for the planner:
  the bake-off's oracle assumes M1–M4 present.
- **O — Open (schema).** The `STAGE0` schema has no field to carry the *lineage cluster id* per
  evidence item beyond the four lineage sub-fields, nor an explicit `withinCluster` grouping the runner
  can assert on. Recommend Claude (schema owner) consider an optional `clusterId` on each evidence
  item so SV-1/SV-2 can assert *which* items combined under the rule-under-test vs across clusters.
  Flagged per the schema's "if you need a field that isn't here, flag it" rule — not redefined here.

---

## Appendix A — Verified anchors (live tree, 2026-07-12)

- `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift`: `:59–68` Config (`confidenceReferralBelow=75`,
  `marginReferralBelow=30`, `conflictReferralAbove=0.40`, `cleanSupportPerSection=2.0`,
  `threatNameFloor=0.40`, `cleanNameFloor=0.40`, `maxWeightPerSource=2.0`); `:113` `cleanBlocked`;
  `:126–156` correlation grouping + per-hypothesis max + `maxWeightPerSource` cap; `:170–175` dampeners;
  `:184–188` clean-support; `:194` `let focusMass = 1 - exp(-s.weight)`; `:195` support dict; `:196`
  `dempster(...)`; `:198` conflict accumulation; `:200` conflict clamp; `:216` `belThreatSet >=
  config.threatNameFloor` gate; `:241` `Int((verdictBetP*100).rounded())`; `:247–249` margin;
  `:277–291` `dempster()` (`raw[inter, default:0] += prod`; `denom = 1 - conflict`; `/= denom`).
- `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs`: `:164` `double focusMass = 1 - Math.Exp(-s.Weight)`;
  `:168` support dict; `:170` `Dempster(...)`; `:172` conflict accumulation; `:174` conflict clamp;
  `:224` `(int)Math.Round(verdictBetP*100)` (default `MidpointRounding.ToEven` — banker's); `:258–271`
  `Dempster()`.
- `macOS/.../Conclusion.swift`: `:190–196` dampeners; `:262` `authoritativeDivergesConfirmed` weights;
  `:302–319` `correlationGroup`; `:341` `blocksClean` (`untested || invalidated`).
- `macOS/.../Synthesizer.swift`: `:57–64` `combine()` status precedence.
- Rounding split confirmed: Swift `.rounded()` = `.toNearestOrAwayFromZero`; C# `Math.Round` default =
  `MidpointRounding.ToEven` → NE7 Δ6 straddle at `100·Bel = 74.5`.

## Appendix B — Sources (verified)

- G. Shafer, *A Mathematical Theory of Evidence*, Princeton, 1976 (belief functions, discounting).
- A. P. Dempster, *Upper and Lower Probabilities Induced by a Multivalued Mapping*, Ann. Math. Stat.
  38, 1967 (`Bel` as a lower probability bound; Dempster's rule).
- T. Denoeux, *Conjunctive and Disjunctive Combination of Belief Functions Induced by Nondistinct
  Bodies of Evidence*, Artificial Intelligence 172(2-3), 2008 (cautious/idempotent rule; canonical
  weights; simple BBA `A^{w}`).
- R. R. Yager, *On the Dempster–Shafer Framework and New Combination Rules*, Information Sciences 41,
  1987 (conflict → Θ).
- D. Dubois & H. Prade, *Representation and Combination of Uncertainty with Belief Functions and
  Possibility Measures*, Computational Intelligence 4, 1988 (conflict → union).
- C. K. Murphy, *Combining Belief Functions When Evidence Conflicts*, Decision Support Systems 29, 2000
  (averaging/weighted-mass).
- P. Smets & R. Kennes, *The Transferable Belief Model*, Artificial Intelligence 66(2), 1994 (credal vs
  pignistic; unnormalized conjunctive/TBM).
- IEEE 754-2008 §9.2 — `exp` is a *recommended*, not *required*, correctly-rounded operation
  (table-maker's dilemma → no cross-runtime bit-identity guarantee).
