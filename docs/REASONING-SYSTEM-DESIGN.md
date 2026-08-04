# REASONING-SYSTEM-DESIGN v2.1 — the determinative core Syxx actually needs

**Author:** Syxx Neural Engineer (design & planning only — no feature code lands here)
**Date:** 2026-07-11
**Revision:** v2.1 (audit fixes on v2 — BLOCKER-1/2, MINOR-3/4, AMEND-O2/O3/O7; supersedes v2 but does not overwrite it)
**Status:** Proposal pending approval. Anything touching user-visible wording is a proposal, not a decision.
**Scope:** The determinative/reasoning core — how Prophet/Mirror/Portal/Glutton/Cage/Witness evidence
becomes one accurate, explainable, defensible tri-state verdict.
**Grounded in:** `neural/NE1.json … NE7.json` (30+ deltas, 20 proposals), `DESIGN.md`
§"Determinative synthesis" (95-116) and §"Score-design invariants (B005)" (118-192, incl. the three
named scores at 139-192 and the minimum-corroboration rule at 169-172), and both engines:
`macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift`, `.../Conclusion.swift`,
`.../Synthesizer.swift`, `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs`, `.../Conclusion.cs`.
Every code claim below was re-opened at `file:line` in the live tree on 2026-07-11.

**What changed in v2 (peer-review summary).** v1 diagnosed the live deltas correctly (independent
reviewer #9 confirmed the P1–P5 code deltas are real) but over-claimed in three ways the reviewers
rejected: (1) it framed one architectural choice (Dempster) as the root of *every* delta, when several
are status-aggregation, dropped data, weak probes, unsigned baselines, and platform drift that Dempster
does not cause; (2) it conflated four *distinct* concerns — source reliability, benign explanation,
evidence completeness, and decision policy — into overlapping mechanisms (a discounting operator, a
belief floor, and a completeness multiplier that all fought each other and broke the confidence
contract); and (3) it pre-committed to a single fusion rule and to calibration semantics that do not
hold for belief functions. v2 keeps the confirmed scaffolding and the delta map, and rebuilds the four
concerns as **four separate, explicitly-named mechanisms** (§3), makes the score/verdict contract
internally consistent (§5), and reframes rule selection and calibration as designed experiments
(§4, §8). A full revision log mapping each accepted review item to its implementing section is at §13.

**What changed in v2.1 (audit fixes).** Seven targeted corrections on v2, none of which reopens a
resolved item: **BLOCKER-1** rewrites §6.1 channel 3 so it no longer smuggles back the
"missing-data lowers confidence" framing R8/I2 removed — thin within-section coverage routes through
M1, a wholly-unrun section through M3, and genuinely unobserved data is a vacuous source (I2) with no
mass and **no** confidence decrement; **BLOCKER-2** restores a deterministic backstop against the NE1 Δ1
wash-to-CLEAN class that deleting v1's belief floor (R3) left uncovered until Phase 6 — a verdict-level
confirmed-threat-mass floor gate (I8/M4) outside the DST number and independent of the fusion rule;
**MINOR-3** corrects a §1 P4 overreach (`.degraded` *is* referenced; the real delta is that neither DST
engine acts on it); **MINOR-4** flags the forced-verdict/low-confidence display tension as a
syxx-gui-architect surfacing proposal; **AMEND-O2** requires the decisive quorum to be *authenticated*
and versions the decisive set as a WitnessSigner-signed `enum DecisiveAct`; **AMEND-O3** keeps exact
integer-% equality at the gates and makes the display-tier ε an output of the Phase-0 bake-off, not a
pre-pinned constant; **AMEND-O7** admits an opt-in adaptive Dirichlet-Multinomial occurrence prior
behind five guardrails, fenced to the Stage-5 surfacing axis only — never the belief masses or verdict.
Revision-log rows for all seven are at §13.

---

## 0. The one-paragraph verdict (honest scope)

The engine is a **single-pass Dempster combination over a flat bag of supports with a pignistic
readout misused as the confidence number**. That is one real, high-leverage defect — it is why
confirmed threats wash out (NE1 Δ1; NE2 Δ3), why two independent mechanisms merge and *invert*
confidence (NE1 Δ2), and why symmetric confirmed signals report ~`Bel/2` and defer to a human
(NE1 Δ3). But it is **not** the single root cause of every delta, and the fix is **not** a small one.
Independent of the belief calculus, the engine also: aggregates sub-statuses so an `.invalidated`
premise is laundered into `.negative` clean-support (NE2 Δ5; NE7 Δ2); never implements the §B005
partial-data contracts, so a critical section that ran *inconclusive* or *degraded* still rides to
CLEAN (NE7 Δ1/Δ4; NE1 Δ5/Δ6; NE4 Δ3); drops real cross-vantage divergence before it can score, so a
poisoned local answer renders as an agreed fact (NE3 Δ1; NE6 Δ1); runs single-point-fragile probes an
attacker evades or a benign lapse false-fires (NE2 Δ9/Δ10/Δ11; NE7 Δ3); and diverges between platforms
so a merely-slow resolver names "censorship" on Windows and "inconclusive" on macOS (NE5 Δ1; NE7 Δ5).
Closing this is a program of work, honestly scoped: an **evidence ledger with full lineage**, an
**act catalog**, a **fusion-rule + determinism bake-off**, a **mechanism-class partition**, a
**causal benign-explanation model**, a **labeled corpus**, a **probe redesign**, a **calibration
pass**, and a **cross-platform golden harness**. The organizing principle is that the four things the
current design tangles together — *how reliable is the source*, *is there a benign explanation*, *how
complete is the evidence*, and *what verdict do we commit to* — must be **four separate mechanisms**
(§3), not one overloaded discounting/floor/multiplier knot. This document specifies that engine, its
per-regime contract, its invariants, the delta-closure map, parity, and a phased roadmap with a
real-data acceptance bar per phase.

---

## 1. Diagnosis — the five pathology classes (each anchored; not a single root cause)

The 30+ deltas group into five structural pathologies. **P1–P3 are belief-calculus defects; P4–P5 are
not** — they are aggregation, dropped-data, probe, and platform-parity defects that would survive any
change to the combination rule and must be fixed on their own terms. Naming the modes is the FMEA
"classification first" discipline (Ramiarina, *Classification First: FMEA as a Cognitive Diagnostic
System in STEM*, 2026, pp.1-2: classification "converts the error from an event into data").

### P1 — Dempster over non-independent evidence (over-count *and* wash-out) [belief calculus]
`coveredThreats` assigns the *same* threat to multiple sections — spoofing is "covered" by Prophet,
Mirror **and** Witness (SynthesisEngine.swift:96-101). Each `.negative` section emits
`cleanSupportPerSection = 2.0` (SynthesisEngine.swift:62, 184-187) as an *independent* Dempster
support, so three clean sections stack to `1 − e^(−6) ≈ 0.9975` against one section that actually
found the threat — driving `Bel(threatUnion)` below the `0.40` floor and flipping a **confirmed
DNSSEC strip to CLEAN** (NE1 Δ1; NE2 Δ3/Δ8). The same independence error runs the other way for
co-derived positives, which Dempster over-counts into inflated confidence (NE4 fusion proposal).

### P2 — Negative-mass dampeners collide with confirmed signals (anti-cancellation violation) [belief calculus + modeling]
Dampeners are realized as support for the complement `Θ∖{h}` (SynthesisEngine.swift:170-175;
Conclusion.swift:190-196) and Dempster-combined against **every** positive with no immunity.
`captivePortalActive` carries `-2.0` on spoofing *and* interception_onPath — exactly cancelling
`redirection`'s `+2.0` (Conclusion.swift:187-192; Windows Conclusion.cs:139-141). Consequences:
(a) an on-path attacker who *presents a captive portal* (trivially forgeable) gets a free `-2.0`
discount on the hypothesis that would flag them (NE1 Δ4; NE4 Δ1); (b) the ∅-collision manufactures a
false "the sections disagree (conflict 87%)" from a *single* section plus context (NE1 Δ4); (c) the
portal dampener omits censorship, so a benign portal's own NX-hijack reads as **censorship at high
confidence** (NE2 Δ1); (d) `dnsEncrypted → spoofing −1.0` bakes in "encryption ⇒ resolver honest"
(NE4 Δ2). This is the charter rule *"a dampener may never zero a confirmed tampering signal"* violated
at the calculus level. **Note:** the *fix* is not "discount the source" (that just moves the
weaponization — see §3, R1); it is a causal benign-explanation model plus a decision-layer safety gate.

### P3 — Gate, name, and confidence are three different quantities [belief calculus / readout]
The gate is `Bel(threatUnion) ≥ 0.40` on **set** mass (SynthesisEngine.swift:216); the name is
`argmax BetP` over **singletons** (line 214); the confidence is `BetP(verdict)` — the singleton's
`1/|A|` share of that set-mass (lines 241-242). So the engine prints "spoofing, 55%" when
`Bel({spoofing}) = 0` (NE4 Δ4), and for a symmetric confirmed signal
(`dnssecCanaryStripped → {spoofing, interception_onPath}`, equal weight) it reports `≈ Bel/2 = 48%`;
the tied members zero the top-two margin so `margin < 30` **always** fires → a decisive cryptographic
strip surfaces as "48%, ask a human" (NE1 Δ3). The margin gate conflates *within-mechanism* ambiguity
(spoofing-vs-interception, both bad) with *threat-vs-clean* ambiguity (the only case warranting defer).

### P4 — The §B005 partial-data contracts do not exist [aggregation; NOT a Dempster defect]
`DESIGN.md:182` is binding: `score = baseScore * (K/6)` and "if any critical section (Prophet, Mirror,
Glutton) is inconclusive, the score is further halved." Grep confirms **neither DST synthesis engine
(`SynthesisEngine.swift`/`.cs`) applies a `.degraded` discount/penalty to confidence** — `.degraded`
*is* referenced (it is produced in `Synthesizer.swift`/`.cs`, e.g. Synthesizer.swift:111/126/247, and
defined on `SectionStatus` in Conclusion.swift:336 / Conclusion.cs:33), but the engine that computes the
confidence number never reads it — and confidence is `round(100·BetP)` with no completeness factor
(SynthesisEngine.swift:241-243; SynthesisEngine.cs:221-225); the NE1 Δ6 degraded-penalty delta is real. `.inconclusive` is neither clean-blocker
nor penalty; `blocksClean` gates only `untested`/`invalidated` (Conclusion.swift:341). A critical
section that *ran and could not decide* still rides to CLEAN (NE7 Δ1; NE1 Δ5); a `.degraded` section
contributes full-strength evidence (NE1 Δ6; NE2 Δ7; NE4 Δ3; NE7 Δ4); and `combine()` **launders
`.invalidated` into `.negative`** when mixed with any clean sub (Synthesizer.swift:57-64), so a
malformed target answer reads Prophet-CLEAN and *emits* clean-support (NE2 Δ5; NE7 Δ2). *(NE-cited
`DESIGN.md:700` for a `pow(1−degradedPenalty, n)` formula is stale — no such line exists in the current
tree; R2 supersedes any post-combination multiplier regardless.)*

### P5 — Evidence dropped before it can score; probes fragile; platforms diverge [pipeline/probe/parity; NOT a Dempster defect]
Real cross-vantage divergence never reaches the verdict: `DatapointExtractor` reads `localRecords`/
`localServer` but **never `publicResolverResults`** (Report.swift:78-91 vs
`ComparisonResult.publicResolverResults`), so a poisoned local answer renders as an *agreed fact*
(NE3 Δ1); and the merged datapoint-conflict is *display-only*, computed after `Synthesizer.Conclude`
and never fed back (NE6 Δ1). Probes are single-point-fragile: the NX canary requires a *shared IP* so
a round-robin block page evades it (NE2 Δ9); the DNSSEC canary trusts one hardcoded `cloudflare.com`
(NE2 Δ10); `WildcardSynthesisProbe` uses a **plain-UDP** reference a transparent Do53 proxy rewrites,
mapping an active hijack to benign `.negative` (Synthesizer.swift:144; NE7 Δ3);
`authoritativeDivergesConfirmed` fires the decisive `3.0` tier on **unsigned** GroundTruth
(Synthesizer.swift:269-276; NE2 Δ11). And platforms disagree: Windows emits threat-weighted evidence
for run-quality-only sub-signals macOS withholds (NE5 Δ1); confidence rounding differs (Swift
away-from-zero vs C# banker's) and straddles the 75% gate (SynthesisEngine.swift:242 vs .cs:224; NE7 Δ5).

---

## 2. Target architecture — a six-stage evidence pipeline

Replace the single flat pass with an explicit, testable pipeline. Each stage has a defined
input/output contract and a place where a missing datum short-circuits to inconclusive. The belief
calculus stays DST; the pipeline is what makes it honest. **The four boxed mechanisms M1–M4 (§3) live
at Stages 2 and 4 and are deliberately kept separate.**

```
 raw section outputs (SectionInputs)
        │
        ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │ STAGE 0  EVIDENCE LEDGER  (typed, provenance-tagged, gap-explicit)     │
 │   every datum → {evidenceId, section, LINEAGE=(vantage/source,         │
 │   raw-observation id, probe/control dependency, act-class),            │
 │   observation, DIRECT|INFERRED, source-reliability r∈(0,1], observed?} │
 │   unobserved/timed-out inputs are NOT added as supports — they are     │
 │   counted only in the completeness dimension (M3). (I2)                │
 └──────────────────────────────────────────────────────────────────────┘
        │  (cross-vantage answer matrix lives here — NE3/NE6)
        ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │ STAGE 1  ACT CLASSIFICATION  (FMEA "classification first")             │
 │   map observations → typified tampering ACTS from a fixed catalog.     │
 │   the ACT is ONE field of the lineage tuple (R4): independence also    │
 │   requires disjoint vantage/raw-observation/control lineage.           │
 └──────────────────────────────────────────────────────────────────────┘
        │
        ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │ STAGE 2  M1 SOURCE-RELIABILITY DISCOUNTING  +  M2 BENIGN EXPLANATION   │
 │   M1: Shafer-discount a source toward Θ ONLY for INTRINSIC un-         │
 │       reliability (degraded status, thin N/M coverage). Not context.   │
 │   M2: a benign cause explains away only the SPECIFIC observation that  │
 │       matches its causal signature (observation-gated), else FORCE     │
 │       REFERRAL. It never discounts an unrelated reliable observation.  │
 └──────────────────────────────────────────────────────────────────────┘
        │
        ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │ STAGE 3  BELIEF CONSTRUCTION + COMBINATION  (rule SELECTED in Phase 0) │
 │   simple/consonant support per act; the Phase-0-selected rule WITHIN a │
 │   non-independent lineage cluster, Dempster ACROSS disjoint-lineage    │
 │   clusters. datapoint-scoped clean-support, N/M-scaled.                │
 └──────────────────────────────────────────────────────────────────────┘
        │
        ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │ STAGE 4  VERDICT DECISION  (TBM two-level; M3 completeness + M4 policy)│
 │   confidence = Bel(smallest MECHANISM-CLASS set carrying the mass) —   │
 │   ONE credal quantity (I6). BetP only orders qualifying singletons.    │
 │   M3: completeness K/M is a SEPARATE reported dimension; forbids CLEAN │
 │       when a critical section is not conclusive (never multiplies Bel).│
 │   M4: a VALIDATED decisive act forbids CLEAN + floors the VERDICT at   │
 │       referral/tampered OUTSIDE the DST number. Integer-% gating (D2). │
 │       + confirmed-threat-mass floor (D5) also forbids CLEAN (I8).      │
 └──────────────────────────────────────────────────────────────────────┘
        │
        ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │ STAGE 5  EXPLAIN + REFER  (direct / inferred / unavailable; RPN order) │
 │   3 distinct honesty lines: hard-gate, conflict, uncertainty.          │
 │   FMEA RPN prioritizes surfacing (Sev × detection-tier; D4/O7).        │
 └──────────────────────────────────────────────────────────────────────┘
```

Stages map 1:1 onto testable modules; Stages 3/4 are the DST core; Stages 0/1/2/5 are the honesty
scaffolding the current engine is missing.

---

## 3. The four separated mechanisms (the core structural fix)

The single most important change from v1. The current engine, and v1's redesign, both tangled four
independent concerns into overlapping operators. v2 makes them **four separate, explicitly-named
mechanisms** with disjoint responsibilities and non-interfering effects on the number. This is what
makes the confidence contract (§5) internally consistent.

### M1 — SOURCE RELIABILITY (Shafer discounting, intrinsic only)  [R1]
A source is discounted toward ignorance **only for reasons intrinsic to the source's own trust-
worthiness**: a `.degraded` section (ran on partial data), a thin `N/M` coverage fraction, or a probe
that half-completed. Discounting is applied at **mass-construction time, before combination**:
`m'(A)=α·m(A)` for `A≠Θ`, `m'(Θ)=α·m(Θ)+(1−α)`, `α∈(0,1)` (Shafer, *A Mathematical Theory of
Evidence*, 1976, discounting operator). Because the discount is baked into the source's mass **before**
the rule runs, the resulting `Bel` already reflects it — there is no post-combination multiplier
(R2). **M1 is never triggered by external context.** A captive portal or an encryption posture is not
a statement about whether *this observation's source* is reliable; routing it through M1 is the exact
category error the reviewers rejected (see M2).

### M2 — BENIGN EXPLANATION (causal / observation-gated, or force referral)  [R1]
A benign cause (captive portal, VPN, CDN/geo divergence, encryption on) is modeled by a **causal
likelihood/signature test**, not by discounting a reliable observation. Concretely, for each benign
cause the design declares the **observation signature** it produces, and the cause may explain away an
observation **only when that observation matches the signature**:
- Captive portal explains a `redirection` **only when the redirect target equals the portal/gateway
  IP** (NE4 Δ1 fix). If the redirect points elsewhere, the portal explains nothing and the redirection
  stands at full strength.
- A captive portal *does* produce NX-hijack-shaped answers (it answers every name with the portal IP),
  so when `captivePortalActive` is present **and** the hijack answers point at the portal, the honest
  output is **referral** — "a captive portal is present and is itself rewriting DNS; a tamper verdict
  cannot be separated from the portal until you authenticate" — never a confident `censorship` verdict
  (NE2 Δ1) and never a silent cancellation (NE1 Δ4; NE2 Δ2).
- Encryption-on (`dnsEncrypted`) is evidence against **on-path/transport** interception of *that
  channel* — it is a legitimate observation about transport, so it may reduce transport-hypothesis mass
  — but it says **nothing** about whether the resolver itself lies, so it must never touch spoofing
  (NE4 Δ2). This is modeled as a positive observation about transport, not as a blanket dampener.

**Why the split matters (the weaponization argument, R1).** An attacker can *induce* a benign context
— presenting a captive-portal splash is trivial. If context fed M1 (discounting), inducing the context
would drop `α` on the *real* evidence source and slide a confirmed signal toward Θ — the same
cancellation attack, relocated from negative mass to discounting. Keeping M2 observation-gated closes
this: the portal can only explain the observations that *actually match its signature*; a
DNSSEC-strip, an EDNS drop, or a redirect that does not point at the gateway is not in the portal's
signature and is untouched. When the context is present but the observation is genuinely ambiguous
between benign and adversarial, M2 **forces referral** rather than guessing.

### M3 — EVIDENCE COMPLETENESS (a separate reported dimension; forbids clean; never multiplies Bel)  [R2, D3]
Completeness is `K/M` where `K` = sections that reached a *conclusive* status (`negative`/`positive`)
and `M` = sections *scheduled to run* for this invocation. Per **D3**:
- A **user-skipped** section **decrements `M`** — it was never scheduled, so it is not a penalty
  (`syxx-nslook` legitimately runs 1 section; `M=1`).
- A **scheduled-but-failed / timed-out** section **stays in `M`** and lowers `K/M` — the run *tried*
  and could not complete, which is a real completeness deficit.
`K/M` is **reported alongside** the verdict as a distinct "evidence completeness" figure. It is **not a
multiplier on `Bel`** (R2). Its only decision effect is a **gate**: if any critical section (Prophet,
Mirror, Glutton per DESIGN.md:185-188) is not conclusive (`inconclusive`/`degraded`/`untested`/
`invalidated`), completeness **forbids a `clean` verdict** — clean requires the critical disciplines to
have affirmatively run `negative`, not to be redundantly "covered" by non-critical sections (NE1 Δ5;
NE7 Δ1). Reporting completeness separately (rather than folding it into the confidence number) is what
lets I6 hold: the confidence number stays the single credal quantity `Bel(class-set)`, and "how
complete was the run" is its own honest figure the user and the report can see.

### M4 — DECISION POLICY (deterministic safety gate outside DST; integer-% gating)  [R3, R7, D1, D2]
The verdict/name/referral decision is a deterministic layer **on top of** the DST number, and it never
overwrites that number.
- **Deterministic safety gate (D1, replaces v1's post-combination belief floor).** A **VALIDATED
  decisive act** forbids a `clean` verdict and floors the **verdict** at referral-or-tampered,
  regardless of contradictory contextual evidence. This is enforced *outside* the belief arithmetic:
  the `Bel/Pl/BetP/m(Θ)` numbers and the signed evidence trail remain internally consistent (R3),
  and the gate survives any adverse combination result. A decisive act does **not** get its `Bel`
  rewritten after the fact; if we want it to also earn high belief, that comes from giving it a strong,
  defensible mass function *before* combination (§8) — but the *safety* guarantee is the gate, not the
  number.
- **Confirmed-threat-mass floor gate (D5, restores the NE1 Δ1 backstop deleted with v1's belief floor).**
  Deleting v1's post-combination belief floor (R3, correct — the belief number must never be overwritten)
  removed the *only* deterministic guarantee that a **confirmed** threat can never read CLEAN in the
  window **before** the validated-decisive set is populated (that set is scaffolded empty in Phase 1 and
  filled only in Phase 6). In that window a **confirmed cross-vantage discrepancy act** (§6.2) — which
  carries bounded DST mass and is neither `.positive` nor validated-decisive — can be Dempster-washed (or
  washed by any candidate rule) below the threat-union floor by several clean sections, with no backstop:
  exactly the NE1 Δ1 wash-to-CLEAN class the old floor guarded. The fix is a **verdict-level** gate, not a
  belief edit: a **committed threat mass** — the mass a *confirmed* source assigns **directly** to the
  threat union (its own focal assignment `m(threatUnion)`, evaluated *before* dilution by other sources),
  as opposed to a lone/tentative support — that meets or exceeds the threat-union floor
  (`threatNameFloor = 0.40`, SynthesisEngine.swift:63) **forbids a `clean` verdict**, flooring the verdict
  at referral (or tampered, when a mechanism-class set qualifies). Two properties make this sound:
  - It is evaluated **outside the DST number and independent of the selected fusion rule** (§4.2 has not
    yet chosen one), keying on the confirmed source's own committed mass rather than the post-combination
    `Bel(threatUnion)` (SynthesisEngine.swift:204-206) — so no combination wash can erase it.
  - It is a **decision-policy** gate like D1: it **reads** committed mass but **never** overwrites
    `Bel/Pl/BetP/m(Θ)` (R3-compliant), keeping the signed belief trail internally consistent.

  **Equivalently and additionally**, a confirmed cross-vantage discrepancy act **sets a `.positive`
  section** (§6.2), so the Phase-1 `.positive`-blocks-clean section gate provides a *second, redundant*
  deterministic backstop (today `blocksClean` covers only `.untested`/`.invalidated`,
  Conclusion.swift:341 — Phase 1 adds `.positive`). Stated as invariant **I8** (§5.4).
- **"Validated decisive act" is a full checklist (R7), not a signature alone.** An act qualifies only
  with: signed provenance **and** freshness (anti-stale) **and** domain/record binding (the signature
  covers *this* qname/qtype/answer) **and** authenticated authoritative acquisition (the ground truth
  came from an authenticated path, not an on-path forgeable channel) **and** an **authenticated quorum**
  (≥ the required independent corroborators, each on **disjoint lineage** (R4) and each itself
  cryptographically trustworthy — see the per-act authentication rule below) **and** replay protection
  (nonce/monotonic counter). Until an act can meet the whole checklist it is **not** in the
  validated-decisive set and does not trip the gate — it is ordinary (strong) evidence. This makes
  `authoritativeDivergesConfirmed` decisive only once the signed + authenticated + fresh + bound
  GroundTruth of §6/Phase 6 exists, and `dnssecCanaryStripped` decisive only once the multi-control
  quorum of §6/Phase 6 exists (resolving v1's circularity, O2).
- **The quorum must be AUTHENTICATED, per act (AMEND-O2).** A corroborator only counts toward quorum if
  it is itself cryptographically trustworthy, not merely present — otherwise an on-path adversary who
  controls the transport can manufacture a quorum of forged "corroborators":
  - **`dnssecCanaryStripped`** (Conclusion.swift:140) requires **RRSIG validation** on each corroborating
    control. A control whose DNSSEC posture is only `recordsPresentUnverified` (DNSSECStatus,
    DNSSECVerifier.swift:44 — records observed, signature *window* valid, but signatures **not**
    cryptographically verified by this client) does **not** count; the quorum is over controls whose
    RRSIGs were validated within their validity window (RFC 4035/6840), so a stripper cannot assemble a
    quorum out of signed-looking-but-unverified answers.
  - **`authoritativeDivergesConfirmed`** (Conclusion.swift:169) requires a **`WitnessSigner`-signed
    GroundTruth** (GroundTruth, DimensionClient.swift:10; `serverGroundTruth`, Synthesizer.swift:38,
    ProbePlan.swift:117) that is R7-bound, **and** **≥2 independent resolver cross-checks** on
    disjoint-lineage vantages (R4) that each reproduce the divergence. A single authenticated ground-truth
    read is **not** sufficient for the decisive tier.
- **The validated-decisive set is a VERSIONED, signed catalog (AMEND-O2).** Membership is a new
  `enum DecisiveAct` — no such type exists in the live tree today (grep confirms only `EvidenceId` at
  Conclusion.swift:133 / Conclusion.cs:79), so this is a proposed addition — whose **membership plus a
  monotonic version tag is signed by `WitnessSigner`** (WitnessSigner.swift / WitnessSigner.cs) and
  embedded in the evidence trail. This makes *which acts were decisive at the time of a run* forensically
  reproducible and tamper-evident: the decisive set cannot be silently widened, and a verdict's forcing
  gate is bound to a signed, versioned policy. Both platforms must ship byte-identical `DecisiveAct`
  membership+version (parity, §10).
- **Float determinism (D2, corrected per AMEND-O3).** All verdict/name/referral comparisons are gated on
  **exact integer-percentage equality — no epsilon at the gate** — so sub-1% float differences cannot
  flip a verdict across platforms, and the two platforms must agree to the integer. A separate
  **display-tier tolerance `ε`** governs only *non-gate* belief-array/confidence-value comparisons in CI;
  crucially `ε` is **not pre-pinned** (v1's `1e-7`/`1e-6` are withdrawn) — it is an **output of the
  Phase-0 fusion+determinism bake-off**, measured for the *selected* rule's float profile. Because R5
  defers rule selection to Phase 0, the tolerance the rule implies cannot honestly be fixed before the
  rule is chosen. The golden-vector schema therefore carries two tiers: `_exact` fields (integer
  verdict/name/referral gate assertions, compared for exact equality) and `_epsN` fields (display
  belief/confidence values, compared within the post-bake-off `ε`, with `N` set once the rule is
  selected). The exact fusion rule and this determinism contract are settled **together** in Phase 0
  (§4, §11), because the selected rule's float profile determines both what is bit-reproducible and what
  `ε` the display tier needs.

**Display note (MINOR-4 — surfacing proposal, pending approval).** Because M4 floors the *verdict*
without editing the *belief number* (D1 and D5 both), a forced verdict can pair with an honestly-low
confidence figure — e.g. a validated-decisive or confirmed-mass-floored act that presents as
"Tampered — confidence 20%," which reads self-contradictory to a user. The belief number is **never**
altered to hide that tension (R3): the number stays true. Resolving the *presentation* — e.g. leading
with the decisive/confirmed act and its lineage, and labeling the low figure as "strength of the
corroborating belief," not "chance this is real" — is a **`syxx-gui-architect` surfacing proposal,
pending approval**, not a decision made in this document. Flagged here so the number/verdict split is not
mistaken for an inconsistency to be "fixed" by editing `Bel`.

**How M1–M4 interact with the number (the consistency statement).** M1 shapes each source's mass
*before* combination. M2 either removes a matched benign observation from the threat channel or forces
referral — it never edits an unrelated source's mass. The Phase-0-selected rule then produces one mass
function, from which `Bel/Pl/BetP` are read once (Stage 4). The confidence number is `Bel` of the named
mechanism-class set — full stop (I6). M3 (completeness) and M4 (safety gate + integer gating) act on
the **verdict/eligibility**, never on the confidence number. There is no `Bel × K/M × halving ×
discount` chain anywhere (R2), and nothing overwrites `Bel` after combination (R3).

---

## 4. Framework choice and fusion-rule selection

### 4.1 Representation — keep DST, complete it as a Transferable Belief Model
DST stays the belief representation: ignorance is unassigned mass `m(Θ)`, so zero-data ⇒ ~0 committed
belief by construction, and the engine stays deterministic, `WitnessSigner`-reproducible, and
explainable per-datum (no black-box ML). The missing discipline behind P3 is the **two-level
Transferable Belief Model**: a **credal level** (`Bel/Pl` — what we believe, and the confidence
number) separated from a **pignistic level** (`BetP` — used only to *order* qualifying singletons for
display, never as the confidence). This is the architecture the engine header already claims
(SynthesisEngine.swift:7-13) but does not implement. (Smets & Kennes, *The Transferable Belief Model*,
Artificial Intelligence 66(2), 1994.)

### 4.2 The combination rule is a Phase-0 EXPERIMENT, not a pre-commitment  [R5]
v1 asserted Denoeux's cautious conjunctive rule as *the* answer. v2 does **not** pre-commit. The rule
that governs combination *within a non-independent lineage cluster* (R4) is **selected by a Phase-0
bake-off** that scores candidate rules on two axes simultaneously: (a) correct behavior on the
**adversarial golden vectors** derived from NE1–NE7 (no wash-out, no cap-inversion, no ∅-conflict from
context, decisive acts survive), and (b) **cross-platform bit-reproducibility** under the D2 integer-%
determinism contract. Candidates and what each buys/costs:

| Candidate rule | Property relevant to Syxx | Cost / risk | Reference |
|---|---|---|---|
| **Dempster** (current) | Weights-of-evidence additive when evidence agrees; native; cheap. | Assumes independence; over-counts non-independent evidence (P1); aggressive ∅-normalization amplifies false conflict. | Shafer, *A Mathematical Theory of Evidence*, 1976; Dempster, *Upper and Lower Probabilities Induced by a Multivalued Mapping*, Ann. Math. Stat. 38, 1967. |
| **Cautious conjunctive** (Denoeux) | Idempotent (`m ⌢ m = m`): redundant/co-derived evidence does not amplify — directly kills P1 wash-out/over-count *within a cluster*. | Canonical weights need `log`/`exp` on commonality → float-determinism must be proven under D2. | Denoeux, *Conjunctive and Disjunctive Combination of Belief Functions Induced by Nondistinct Bodies of Evidence*, Artificial Intelligence 172(2-3), 2008. |
| **Yager** | Conflict mass reassigned to `Θ` (ignorance) instead of normalized away → will not manufacture a spurious "sections disagree" spike from a lone context marker. | Non-associative; less confident under genuine agreement. | Yager, *On the Dempster–Shafer Framework and New Combination Rules*, Information Sciences 41, 1987. |
| **Dubois–Prade** (adaptive disjunctive/conjunctive) | Handles partially-reliable sources gracefully; conflict routed to the union rather than ∅. | More complex; interpretation of the result needs care. | Dubois & Prade, *Representation and Combination of Uncertainty with Belief Functions and Possibility Measures*, Computational Intelligence 4, 1988. |
| **Weighted-mass / averaging** (Murphy) | Averaging masses before combining is robust to a few high-conflict sources; simple; trivially deterministic. | Not a true evidence-accumulating rule; can under-count real corroboration. | Murphy, *Combining Belief Functions When Evidence Conflicts*, Decision Support Systems 29, 2000. |

**Selection procedure (Phase 0).** Run all candidates over the golden-vector corpus; require every
NE-derived adversarial vector to produce the honest verdict; require the same integer-% output on both
platforms; then choose the rule that (i) passes all adversarial vectors, (ii) is bit-reproducible under
D2, and (iii) is the least aggressive on conflict consistent with (i). The cautious rule is the
*leading candidate* on merit, but the choice is made by the experiment, and **Dempster is retained
across genuinely disjoint-lineage clusters** regardless (independence there is real).

### 4.3 What we borrow, and what we reject
- **Subjective Logic** — reject a full switch; its opinions `(b,d,u,a)` carry their own independence
  assumptions and the marginal gain over "TBM + selected rule" is small. **Borrow** its explicit
  base-rate `a` discipline into calibration (§8).
- **Calibrated Bayesian (naive-Bayes over LLRs)** — reject as the frame (pure Bayes has no honest "I
  don't know" mass). **Borrow** its method for the *singleton-focal* weights only (§8, R6).
- **FMEA / RPN** — adopt as an overlay: the act catalog (Stage 1, and the R4 lineage tuple) and the
  surfacing-priority lens (Stage 5), **not** the scorer.

---

## 5. The score / verdict contract across the three data regimes

Binding contract for Stages 3-4. It makes `DESIGN.md:126-192` real. `K`, `M`, and the completeness
gate are defined by M3 (§3, D3).

### 5.1 No-data regime (zero conclusive sections)
- **Input:** all sections `untested`/`inconclusive`/empty, or every probe timed out.
- **Belief state:** no supports ⇒ vacuous mass ⇒ `Bel(h)=0 ∀h`, `m(Θ)=1`.
- **Output (binding, R8):** verdict = `inconclusive`; **confidence = NIL / ABSENT (not numeric 0)** —
  "no estimate," which is distinct from "0% sure something is wrong"; `uncertainty = 1`; completeness
  `K/M = 0/M`; referral always fires. No number is ever emitted from absent data (DESIGN.md:128,
  invariant 1). *Closes the false-clean-from-nothing class (NE7 Δ1 no-data half; B008 lineage).*

### 5.2 Partial-data regime (0 < K < M, or any critical section not conclusive)
Where the current engine lies, and where §B005 must become code.
- **Completeness (M3, D3).** `K/M` is reported as its own dimension. If a critical section is not
  conclusive, completeness **forbids `clean`** (not "halves confidence"). This removes the
  coverage-redundancy loophole that let an inconclusive Prophet ride to CLEAN because Mirror+Witness
  cover spoofing (NE1 Δ5; NE7 Δ1). *Completeness never multiplies the `Bel` number (R2).*
- **Intrinsic-source discounting (M1).** A `.degraded` section's evidence is Shafer-discounted by
  `α<1` **before** combination, so `Bel` falls out coherently — not a post-hoc `confidence *= (1−p)^n`
  penalty that leaves `uncertainty`/`Bel` disagreeing with the printed number (NE4 Δ3; NE7 Δ4).
  *Closes NE1 Δ6; NE2 Δ7.*
- **Invalidated dominates.** `combine()` must rank `.invalidated` **above** `.negative`/`.degraded` so
  a required-premise failure can never be laundered into clean-support (Synthesizer.swift:57-64).
  Order: `positive > invalidated(any) > inconclusive > degraded > negative`. *Closes NE2 Δ5; NE7 Δ2.*
- **Clean-support is datapoint-scoped and N/M-scaled.** A `.negative` section emits clean-support
  **only for the specific datapoints that ran clean**, weighted by the fraction of that section's
  defined datapoints that ran clean — not a flat `2.0` over a section-wide `coveredThreats` mask
  (NE2 Δ3/Δ4; DESIGN.md:132 invariant 3).
- **Output:** confidence is `Float? ∈ [0,1]` or `nil`; completeness `K/M` is reported beside it; the
  gap is **visible** (unobserved datapoints listed in the explanation, §7).

### 5.3 Full-data regime (K = M, all sections conclusive)
- Normal DST composition (Stage 3, selected rule), credal readout (Stage 4).
- **Minimum-corroboration gate (DESIGN.md:169-172).** A single evidence item may *raise* a threat but
  must **force referral** (tentative, confidence-capped) unless it is a **validated decisive act**
  (M4/R7). Two independent bodies of evidence — **disjoint lineage** (R4), not merely a distinct act
  class — are required to *name* a threat with un-referred confidence. *Closes NE7 Δ5b.*

### 5.4 Invariant table (all regimes) — corrected per R8

| # | Invariant | Enforced at |
|---|---|---|
| I1 | No score defaults to confident; missing data ⇒ `inconclusive` verdict with **absent (nil) confidence**, never a caveated number and never numeric 0. | Stage 0/4 |
| I2 | Missing/timed-out data is a **vacuous source** (identity element of the combination rule): it contributes **no mass**, adds **no UNOBSERVED support**, and does not tilt toward clean or tampered. It is accounted for **only** in the completeness dimension (M3). | Stage 0/3 |
| I3 | **Source-reliability discounting (M1)** is applied at mass-construction time for **intrinsic** unreliability (degraded, thin N/M) only. It is **not** a post-combination multiplier and is **never** triggered by benign external context. | Stage 2 |
| I4 | **Anti-cancellation (M2).** A benign context may explain away only the observation matching its causal signature (observation-gated), or force referral; it may **never** discount an unrelated reliable observation, and it can **never** assert counter-mass that zeros a hypothesis. | Stage 2 |
| I5 | **Deterministic safety gate (M4, replaces universal monotonicity).** A **validated** decisive act (R7 checklist) forbids `clean` and floors the verdict at referral/tampered, enforced **outside** the DST number, so it survives any contradictory evidence and any combination arithmetic. | Stage 4 |
| I6 | Gate, name, and confidence are the **same** single credal quantity: `Bel` of the named mechanism-class set. Completeness (M3) and the safety gate (M4) are **separate** mechanisms that never multiply or overwrite this number. | Stage 4 |
| I7 | **Referral fires on LOW confidence** (below the integer-% threshold) **or** a narrow between-mechanism-class margin **or** genuine conflict **or** a hard-gated/incomplete critical section **or** an inconclusive verdict. **High** confidence with a **wide** between-class margin does **not** defer. | Stage 4 |
| I8 | **Confirmed-threat-mass floor (verdict-level, fusion-rule-independent — BLOCKER-2/D5).** A **committed** threat mass — mass a *confirmed* source assigns directly to the threat union (e.g. a confirmed cross-vantage discrepancy act, §6.2), evaluated **before** dilution — that meets/exceeds the threat-union floor (`threatNameFloor`, SynthesisEngine.swift:63) **forbids `clean`**, flooring the verdict at referral/tampered. Enforced **outside** the DST number and **independent of the selected fusion rule**, so no combination wash can erase it; it **never** overwrites `Bel/Pl/BetP/m(Θ)` (R3). Redundantly, a confirmed discrepancy act also sets a `.positive` section (§6.2), tripping the `.positive`-blocks-clean gate. | Stage 4 |

*(v1's I5 "adding a confirmed positive never lowers `Bel(threatUnion)`" is dropped: belief
monotonicity is not guaranteed under non-independent evidence or several candidate rules, so it cannot
be an invariant. The safety-gate form of I5 is deterministic and robust to any belief arithmetic.)*

---

## 6. Composition across the six disciplines

### 6.1 What each discipline contributes (three channels, never conflated)
1. **Positive act-supports** — for hypotheses the section *directly observed* (Stage 1 acts).
2. **Datapoint-scoped clean-support** — only for datapoints that ran clean, N/M-scaled, against the
   *specific* threat each datapoint rules out (a clean NX canary dampens NX-hijack; it does **not**
   dampen a Mirror-observed redirection — NE2 Δ3).
3. **Non-observation — three disjoint routes, none of which is a "missing-data lowers confidence"
   decrement (BLOCKER-1 fix; I2 held self-consistent).** How a gap is accounted depends entirely on
   *which kind* of gap it is:
   - **Thin *within-section* coverage** — a section that ran but completed only some of its defined
     datapoints — is a source that is *intrinsically* less reliable, so it legitimately lowers `Bel`,
     but **only** through **M1** (§3): the partial/degraded source is Shafer-discounted (`α<1`) at
     **mass-construction time, before combination**, so the reduction is baked into that source's mass
     and there is **no** post-combination confidence penalty (R2).
   - **A *wholly-unrun* section** is accounted **only** through **M3** completeness (`K/M`): when the
     section is critical it **forbids a `clean` verdict**, but it is a *gate on eligibility*, **not** a
     `Bel` decrement — it never multiplies or lowers the belief number.
   - **Genuinely *unobserved / timed-out* datapoints** are a **vacuous source** — the **identity element**
     of the combination rule (I2): combining with them **leaves the existing belief unchanged**. They add
     **no mass** to any focal set, add **no** `UNOBSERVED` support, and cause **no confidence decrement**
     at all. Their only footprint is in the completeness figure (M3) and the UNAVAILABLE explanation line
     (§7). They never tilt the verdict toward clean or tampered.

Run-quality overlays (slow latency, TTL-rise) go through a **weight-free `QualityCaveat` channel**
(NE5 quality-caveat proposal): they lower Evidence Confidence via M1/M3 and appear in the report to
*explain the knock*, but contribute **zero** hypothesis mass. This closes the macOS/Windows split where
Windows injects false censorship/spoofing mass from a merely-slow resolver (NE5 Δ1) — both platforms
route overlays identically through the caveat channel.

### 6.2 Coverage corrections (which sections may dampen which threats)
- **Witness** coverage narrows from `{spoofing, interception_onPath}` to what a longitudinal drift
  baseline actually rules out (answer-drift persistence — arguably nothing about *live* spoofing); and
  `witness()` returns `.untested`, not `.negative`, when no trustworthy baseline was compared (NE2 Δ8;
  SynthesisEngine.swift:101; Synthesizer.swift:503).
- **Portal encryption state** is a *posture* handled by M2, not a clean tamper determination:
  `encryptedDNS==.no` maps to inconclusive/degraded (keep the weak `dnsUnencrypted` observation), and
  `.yes` drops the redundant `.negative` (M2 carries the transport observation once), ending the
  double-count (NE2 Δ6; Synthesizer.swift:361).
- **Cross-discipline discrepancy is a first-class act.** When Prophet's answer ≠ Mirror's answer for a
  canonical key, that is a *split-horizon / interception* act feeding **bounded** DST mass (subject to
  I2/I4), not a display-only footnote (NE6 Δ1). It requires the set-valued answer fix first (NE6/NE7
  set-comparison proposal) so multi-homed CDN A-sets do not false-fire.
  - **A *confirmed* discrepancy carries a deterministic backstop (BLOCKER-2, I8).** When the set-valued
    mismatch survives the CDN/multi-home false-fire filter **and** is corroborated on ≥1 disjoint-lineage
    vantage (R4), the act is **confirmed**: it commits `m(threatUnion) ≥ threatNameFloor`
    (`0.40`, SynthesisEngine.swift:63) **and sets a `.positive` section**. Either route forbids a `clean`
    verdict even if a later combination wash drives the fused `Bel(threatUnion)` below the floor — the
    verdict-level confirmed-threat-mass floor gate (M4/D5) on the committed mass, and the
    `.positive`-blocks-clean section gate (Phase 1; `blocksClean`, Conclusion.swift:341) redundantly.
    A *tentative* (single-vantage, unconfirmed) discrepancy stays bounded/ordinary evidence and is
    subject to the wash — it is not a deterministic backstop.

### 6.3 Correlation keyed on FULL LINEAGE, not act class alone  [R4]
Two bodies of evidence are **independent** (eligible for Dempster) only when their **full lineage
tuple is disjoint** on all four fields: `{source/vantage identity, raw-observation lineage (which
captured packets / response set), probe-and-control dependency (which reference/control it used), act
class}`. A different act class is **not** sufficient for independence — two distinct acts derived from
the *same* captured response set, or validated against the *same* reference control, are statistically
dependent and must combine under the Phase-0-selected non-independent rule.

This both fixes and refines v1:
- **Splits genuinely independent acts** that the current coarse `source:"canaries"` tag merged: today
  `nxCanaryHijack` and `dnssecCanaryStripped` share `source:"canaries"` (Conclusion.swift correlation
  map + DNSCanaries source tag), so a resolver that *both* NX-hijacks and DNSSEC-strips has its two
  mechanisms merged, capped at `maxWeightPerSource=2.0` (SynthesisEngine.swift:150-156), and confidence
  **inverts** (74%→31%, forced referral — NE1 Δ2). If their raw-observation and control lineages are
  disjoint, they are independent and must corroborate, not cannibalize.
- **Keeps genuinely dependent acts clustered** even when their act classes differ: e.g.
  ttlLockstep / ttlExtreme / cnameChainDivergent / answerUnstable all derived from one resolver-response
  set share raw-observation lineage and must **not** be Dempster-combined as independent (NE4 fusion
  proposal), which would over-count.

### 6.4 Mechanism-class partition (fixes P3 confidence + margin)
Confidence and the margin gate operate on **mechanism classes**, not raw singletons:

- REWRITE = `{spoofing, interception_onPath}` (someone is changing the answer; both bad)
- BLOCK = `{censorship}`
- TRANSPORT = `{interception_transport}`
- CONTENT = `{contentTampering}`
- LEAK = `{privacyLeak}`
- HOST = `{localCompromise}`

Rules:
- **Confidence** = `Bel` of the smallest mechanism-class set carrying the committed mass — the single
  credal quantity (I6). A confirmed `dnssecCanaryStripped` reports `Bel(REWRITE) ≈ 0.95`, not
  `BetP(spoofing) = 48%` (NE1 Δ3).
- **Name** = the leading mechanism class; the spoofing-vs-interception split within REWRITE is a
  **secondary within-class distribution**, not the headline.
- **Margin gate** compares *between classes* and *threat-vs-clean* — never between two members of the
  same class, so an inherently-ambiguous-but-decisive tamper is named with high confidence and only a
  genuine threat/clean contest defers (I6/I7).
- **Naming requires committed belief.** Name a class only when `Bel(class-set) ≥ floor`; use `BetP`
  purely to order qualifying singletons *for display*. When the threat union clears but no class-set
  does, the honest verdict is "some interference, mechanism unresolved — refer," never a confidently
  named single threat (NE4 Δ4).

---

## 7. Explainability — direct vs inferred vs unavailable, in every verdict

Every `Conclusion` carries a three-partition provenance view:

- **DIRECT** — observed this run, with the raw value and vantage identity: *"1.1.1.1 returned 6.6.6.6
  for example.com; public consensus (8.8.8.8, 9.9.9.9, authoritative) returned 93.184.216.34."* This
  requires the **cross-vantage answer matrix** (NE3 proposal): `publicResolverResults`, `authoritative`,
  and `serverGroundTruth` extracted into the bag with per-vantage tags, so a local-vs-consensus split
  *surfaces both values* instead of showing the poisoned answer as an agreed fact (NE3 Δ1;
  Report.swift:78-91 omits `publicResolverResults` today). Same-vantage reads (Prophet resolution ==
  Mirror local) collapse to one vantage so agreement is never miscounted as cross-discipline
  corroboration (NE3 Δ3).
- **INFERRED** — the mechanism derived from direct signals, named against the FMEA act catalog:
  *"on-path interception inferred from a confirmed DNSSEC strip."* Each inferred conclusion links back
  to the direct acts (and their lineage, R4) that produced it.
- **UNAVAILABLE** — every section `untested`/`invalidated` and every datapoint that timed out, listed
  explicitly, and reflected in completeness (M3). The gap is *shown*, not hidden.

Three **distinct** honesty lines (today they collapse to one bare "inconclusive" — NE3 Δ2):
1. **Hard gate** — "A clean verdict is impossible until Mirror and Glutton run (required checks did not
   run)." Surfaces `hasHardGatedSection` as prose.
2. **Conflict** — "The sections genuinely disagree (conflict N%)" — and *only* when they do, now that
   benign context no longer manufactures ∅-conflict (M2 fix).
3. **Uncertainty** — "Evidence is thin (uncertainty N%)" — the raw `m(Θ)`.

Plus a **time anchor** (`generatedAt`, ISO-8601) on every rendered report (NE3 Δ4). The FMEA **RPN**
orders which finding leads when several are present. **Honest scope (D4, amended by AMEND-O7):** for v1
there is no labeled corpus, so the shipped RPN is `RPN = Severity × detection-tier` (a defensible
two-factor priority), **not** the full `S × O × D` FMEA, and this is stated as such in the report. The
**Occurrence axis is built as a pluggable source** with two backings:
- **Static expert-elicited constants** — shipped for v1, deterministic, zero learning surface (the
  default backing).
- **Adaptive Dirichlet-Multinomial per-SSID priors** — an **opt-in** backing store behind **five hard
  guardrails**: (1) storage is **per-SSID, user-owned local SQLite** (never uploaded); (2) a **weak
  uniform prior `α=1`** (Dirichlet-Multinomial), so a fresh SSID starts at the uniform base rate and
  cannot be swung by a handful of observations; (3) **confirmed-only observation gating** — *only*
  **validated-decisive-tier** detections (the M4 checklist / signed `DecisiveAct` set, §3) update the
  counts, so ordinary, tentative, or false-positive detections can **never** feed the prior (this closes
  the false-positive feedback loop); (4) the adaptive contribution is **transparent in the Stage-5
  output** (the report states the Occurrence axis is adaptive and shows the per-SSID counts driving it);
  (5) the SQLite **state hash is `WitnessSigner`-signed** so the prior's provenance is tamper-evident.

**HARD BOUNDARY (AMEND-O7).** Adaptive priors affect **only the Stage-5 RPN Occurrence axis** — the
*surfacing and ordering* of findings. They **never** touch the DST belief masses, `Bel/Pl/BetP`, or the
verdict, so the verdict stays deterministic and `WitnessSigner`-signable regardless of learning state,
and a poisoned or drifted prior can at worst reorder which (already-correct) finding is shown first —
never manufacture, suppress, or reweight a verdict. This fence is exactly why the adaptive store is
admissible where v2's static-only D4 rejected it: it lives entirely **outside** the determinative path.
Because the source is pluggable, v1 ships the static backing and the Dirichlet-adaptive backing is an
opt-in enhancement gated on its five guardrails.

---

## 8. Weight calibration — with valid belief-function semantics  [R6]

The weights (Conclusion.swift:173-267) are admitted first-pass magic numbers and the thresholds
(`0.40` floor, `75`/`30`/`0.40` gates, `2.0` cap — SynthesisEngine.swift:58-68) are round numbers.
Several deltas are *direct consequences* of un-calibrated constants interacting. But calibration must
respect that these are **belief-function** parameters, not probabilities:

### 8.1 What is and is not an LLR
- **Singleton-focal weights** (a signal that argues for exactly `{h}` vs `¬h`) may be estimated as a
  genuine LLR `log[P(signal|h)/P(signal|¬h)]` from a labeled corpus, with the base rate stated
  (borrowing Subjective Logic's base-rate discipline). This is well-posed because `{h}` vs its
  complement is a binary partition.
- **Multi-element focal weights** (e.g. `dnssecCanaryStripped → {spoofing, interception_onPath}`) are
  **not LLRs** (there is no single binary hypothesis). They are modeled in two declared steps: (i) a
  **class-vs-clean discrimination weight** — how well the signal separates the REWRITE class from
  clean, which *is* a binary LLR at the class level; and (ii) an **intra-class mass split** that must be
  justified as a modeling assumption (or left ambiguous as set-mass), and must **not** masquerade as
  evidence for one member over another. The engine currently smears set-mass via BetP and calls the
  result confidence — the exact P3 error.

### 8.2 A valid calibration target (not a naive reliability diagram on Bel)
`Bel(A)` is a **lower probability bound**, not a probability (Dempster, *Upper and Lower Probabilities
Induced by a Multivalued Mapping*, Annals of Mathematical Statistics 38, 1967), so plotting observed
frequency against `Bel` is ill-posed. Two well-posed targets, both declared with explicit hypotheses,
base rates, and sampling assumptions:
- **Coverage calibration of the credal interval `[Bel(class), Pl(class)]`.** Over the labeled corpus,
  the true mechanism class must fall within the reported interval at ≥ the nominal coverage. This is
  the correct calibration notion for lower/upper probability bounds.
- **Reliability-diagram calibration of the pignistic `BetP`.** `BetP` *is* a proper probability
  distribution (Smets & Kennes 1994), so a standard reliability diagram is well-posed on it: for each
  bin of predicted `BetP`, observed frequency must match (Niculescu-Mizil & Caruana, *Predicting Good
  Probabilities With Supervised Learning*, ICML 2005). Use this to calibrate the **display** ordering
  layer, and the coverage target to calibrate the **credal** confidence number.

### 8.3 The false-secure budget
Set the naming floor and referral thresholds to hit an explicit **false-secure (missed-tampering)
budget** on a held-out adversarial set. Still no black-box ML: every number traces to a datapoint and
its derivation is recorded next to the weight and is signable (NE1/NE4/NE7 calibration proposals).

---

## 9. Highest-severity delta closure map (updated where the closure mechanism changed)

Closure mechanisms that changed from v1 are marked **[Δmech]** with the reason.

| Delta (file) | Sev | Pathology | Closed by |
|---|---|---|---|
| Confirmed threat washed to CLEAN (NE1 Δ1) | Critical | P1 | Selected non-independent rule §4.2; datapoint-scoped clean-support §5.2; `.positive` blocks clean; M4 validated-decisive safety gate (D1) **and** the M4 **confirmed-threat-mass floor gate (I8/D5)** — a fusion-rule-independent verdict-level backstop covering the pre-Phase-6 window before the validated-decisive set is populated (§3/§5.4/§6.2) **[Δmech: v1 belief-floor → M4 deterministic gates: D1 validated-decisive + D5 confirmed-mass floor, R3/D1; BLOCKER-2 restores the deleted floor's backstop without editing `Bel`]** |
| Two mechanisms merge+cap, confidence inverts 74→31% (NE1 Δ2) | Critical | P1 | Full-lineage correlation §6.3 **[Δmech: v1 act-class key → full lineage tuple, R4]** |
| Captive-portal dampener → false censorship (NE2 Δ1) | Critical | P2 | M2 benign-explanation → **referral** when portal is itself rewriting §3 **[Δmech: v1 discount → causal/referral, R1]** |
| Mechanism-blind dampeners cancel confirmed on-path (NE2 Δ2; NE4 Δ1) | Critical | P2 | M2 observation-gated explanation + M4 safety gate §3 **[Δmech: v1 confirmed-exemption+floor → M2/M4, R1/R3]** |
| Clean-support conflates covered-threats (NE2 Δ3) | Critical | P1 | Datapoint-scoped clean-support §5.2/6.1 |
| `combine()` launders `.invalidated`→`.negative` (NE2 Δ5; NE7 Δ2) | Critical | P4 | Invalidated-dominates ordering §5.2 |
| §B005 fiction; critical-inconclusive → CLEAN (NE7 Δ1; NE1 Δ5) | Critical | P4 | M3 completeness dimension forbids clean §3/5.2 **[Δmech: v1 `K/M` multiplier → separate dimension + clean-gate, R2/D3]** |
| Wildcard plain-UDP reference reads active hijack benign (NE7 Δ3) | Critical | P5 | Encrypted-transport reference §6/Phase 6; `wildcardZone`→inconclusive until then |
| FACTS drops publicResolverResults; poison shown as agreed fact (NE3 Δ1) | Critical | P5 | Cross-vantage answer matrix §7 |
| Cross-discipline discrepancy display-only, never scores (NE6 Δ1) | Critical | P5 | Discrepancy → first-class DST act §6.2 |
| Windows names false "censorship" from slow resolver (NE5 Δ1) | Critical | P5 | Weight-free QualityCaveat channel §6.1; parity golden vectors §10 |
| Dampeners erode confirmed + fake conflict; portal forgeable (NE1 Δ4) | High | P2 | M2 §3; separate benign-explainer from "sections disagree" §7 |
| Confidence = BetP understates set belief; near-universal referral (NE1 Δ3) | High | P3 | Credal `Bel(class-set)` confidence §6.4; margin between classes |
| dnsEncrypted −1.0 on spoofing wrong (NE4 Δ2) | High | P2 | M2: encryption is a transport observation only, never touches spoofing §3 |
| Degraded penalty unimplemented (NE1 Δ6; NE2 Δ7; NE4 Δ3; NE7 Δ4) | High | P4 | M1 intrinsic-source discounting (pre-combination) §3/5.2 **[Δmech: v1 post-hoc penalty → M1 pre-combination discount, R2]** |
| No min-corroboration gate; one signal names a threat (NE7 Δ5b) | High | P4 | §5.3 min-corroboration on disjoint lineage §6.3 |
| Flat 2.0 clean-support ignores probes run (NE2 Δ4) | High | P1/P4 | N/M-scaled clean-support §5.2 |
| Witness claims spoofing/interception from drift check (NE2 Δ8) | High | P1 | Narrowed coverage + `.untested` §6.2 |
| Portal encryption state → unconditional `.negative` (NE2 Δ6) | High | P1 | Posture via M2 §6.2 |
| NX canary evaded by round-robin block page (NE2 Δ9) | High | P5 | Structural "any answer for a nonexistent name" rule (RFC 8020) §6/Phase 6 |
| DNSSEC canary trusts one cloudflare.com (NE2 Δ10) | High | P5 | Multi-control quorum + DO-bit gate (RFC 4035/6840) §6/Phase 6 |
| Decisive tier on unsigned GroundTruth (NE2 Δ11) | High | P5 | Full R7 validation checklist before an act is decisive §3(M4)/Phase 6 **[Δmech: v1 "once signed" → full signed+fresh+bound+authenticated+quorum+replay checklist, R7]** |
| Gate on set-belief, name on singleton BetP (NE4 Δ4) | Medium | P3 | Gate-consistent class naming §6.4 (I6) |
| Confidence rounding diverges cross-platform (NE7 Δ5) | Medium | P5 | Integer-% gating (D2) + boundary golden test §10 **[Δmech: v1 "one rounding mode" → integer-% decision gating, D2]** |
| Same-vantage Prophet+Mirror read as independent (NE3 Δ3) | Major | P5 | Vantage-identity tagging §7; lineage tuple §6.3 |
| hasHardGatedSection / conflict / uncertainty invisible (NE3 Δ2) | Major | P5 | Three honesty lines §7 |
| Windows DualStack inconclusive dropped (NE5 Δ2); canary RNG (NE5 Δ3) | Non-crit | P5 | Parity fixes §10 |

---

## 10. Platform parity — one engine, one verdict

macOS and Windows engines are line-for-line twins (SynthesisEngine.swift ↔ .cs; Conclusion.swift ↔
.cs), so every design element lands on both. Parity is *enforced*:

1. **Shared golden-vector suite (two-tier schema, D2/AMEND-O3).** A JSON corpus of
   `(evidence, sectionStatuses) → expected {verdict, confidence, completeness, belief[], conflict,
   uncertainty}` runs on **both** platforms; any divergence fails CI. Each vector carries two assertion
   tiers: **`_exact`** — the integer verdict/name/referral gate values, compared for **exact equality
   (no epsilon at the gate)** — and **`_epsN`** — the display belief/confidence values, compared within a
   **display-tier tolerance `ε`** that is an **output of the Phase-0 bake-off** (measured for the selected
   rule, **not** pre-pinned to `1e-7`/`1e-6`, which are withdrawn; `N` is set once the rule is chosen).
   This is the artifact that would have caught NE5 Δ1 and NE7 Δ5.
2. **Integer-% decision gating (D2/AMEND-O3).** Every verdict/name/referral comparison uses
   **exact integer-percentage equality** (no gate epsilon), so a sub-1% float difference cannot flip a
   verdict. Add a boundary golden vector at `Bel(class)·100 = 74.5` to pin the referral edge.
3. **Evidence-emission parity.** The QualityCaveat channel (§6.1) forces both platforms to emit
   run-quality overlays weight-free and identically; no platform may emit threat-weighted evidence the
   other withholds. Lint: the `EvidenceId`-emission set per adapter must match across platforms.
4. **Fusion-rule determinism (settled in Phase 0, D2).** The selected rule must be bit-reproducible
   under the integer-% contract, or its float path must round at an integer-stable boundary before any
   decision. This is a *selection criterion* in the Phase-0 bake-off, not an afterthought.

---

## 11. Phased, dependency-ordered roadmap — real-data acceptance bars

Tests are the floor, never the bar. Each phase is **done** only when it has been *run on real or
realistically-adversarial network data* and the honest output was *observed*. A green suite that billed
"31% inconclusive" on 0 captured queries is the exact anti-pattern this bar prevents. **Phases
renumbered so fusion-rule selection precedes any phase that commits a rule (R5).**

**Phase 0 — Harness + adversarial corpus + fusion-rule/determinism bake-off (SELECTS the rule).**
Build the shared golden-vector runner and a labeled real-data capture corpus (poisoned-local,
transparent-proxy, captive-portal, DNSSEC-strip, benign-CDN, benign-captive). Encode every NE1–NE7
failing scenario as an adversarial golden vector. Run all candidate rules (§4.2) offline over the
corpus and **select** the rule that passes every adversarial vector and is bit-reproducible under the
D2 integer-% contract. No change to the shipped verdict yet — the bake-off is offline.
*Acceptance bar:* every NE failing scenario reproduces **red** on both platforms; the selected rule +
determinism contract are recorded with the vectors that justified them; the **display-tier `ε` is
measured for the selected rule and pinned into the golden schema's `_epsN` fields** (AMEND-O3 — the gate
tiers stay exact-integer); ≥1 real capture per mechanism class exists. *Depends on:* nothing.
*Handoff:* syxx-engineer (harness), Neural (corpus labels + rule selection).

**Phase 1 — Structural honesty gates (rule-agnostic).** Invalidated-dominates `combine()`; M1
intrinsic-source discounting for degraded/thin; M3 completeness dimension + critical-section
forbids-clean; min-corroboration; `.positive` blocks clean; scaffold the M4 safety-gate mechanism with
an **empty** validated-decisive set (populated in Phase 6). *Closes:* NE7 Δ1/Δ2, NE2 Δ5/Δ7, NE1
Δ5/Δ6, NE7 Δ5b, NE4 Δ3. *Acceptance bar:* a **real run** against a resolver returning a
malformed/SERVFAIL target answer, and a **real run** with Mirror/Glutton skipped, both print
`inconclusive` with the hard-gate line and a visible completeness figure — never `clean`. *Depends on:*
Phase 0.

**Phase 2 — The four separated mechanisms for context (M1 vs M2), + decision policy.** Replace
negative-mass dampeners with M2 observation-gated benign-explanation (or referral); keep the M1/M2
split strict (context never discounts an unrelated reliable source, R1); wire the M4 safety gate at the
verdict layer (D1). *Closes:* NE1 Δ4, NE2 Δ1/Δ2, NE4 Δ1/Δ2. *Acceptance bar:* on a **real
captive-portal network** with a concurrently poisoned answer (adversarial fixture on the wire), the
poison is still **named** (or referred, when the portal itself rewrites), conflict does not spike from
context alone, and inducing the portal marker does **not** slide the confirmed signal toward Θ.
*Depends on:* Phase 1.

**Phase 3 — Combination + clean-support correctness (commits the selected rule).** Apply the
Phase-0-selected rule within non-independent lineage clusters; full-lineage correlation keys (R4);
datapoint-scoped N/M clean-support; narrowed Witness/Portal coverage. *Closes:* NE1 Δ1/Δ2, NE2
Δ3/Δ4/Δ6/Δ8. *Acceptance bar:* a **real DNSSEC-strip capture** with N clean sections still names
REWRITE; a capture where a resolver *both* NX-hijacks and DNSSEC-strips (disjoint lineage) reports
**higher** confidence than either alone (no 74→31 inversion). *Depends on:* Phases 0 (rule) + 2.

**Phase 4 — Verdict/confidence semantics (TBM two-level).** Confidence = `Bel(class-set)` as the
single credal quantity (I6); gate-consistent class naming; within-class split as secondary; margin
between classes; integer-% gating. *Closes:* NE1 Δ3, NE4 Δ4, NE7 Δ5. *Acceptance bar:* the **real
DNSSEC-strip** surfaces as "interference detected — spoofing or on-path proxy — high confidence," not
"48%, ask a human"; the cross-platform golden boundary vector agrees on the integer verdict. *Depends
on:* Phase 3.

**Phase 5 — Evidence model upgrade.** Cross-vantage answer matrix; cross-discipline discrepancy → DST
mass; `publicResolverResults`/authoritative/ground-truth into the bag; typed `DivergenceKind`; three
honesty lines + time anchor. *Closes:* NE3 Δ1/Δ2/Δ3/Δ4, NE6 Δ1. *Acceptance bar:* a **real
locally-poisoned run** both scores toward interception **and** displays the true public consensus and
the outlier; a multi-homed CDN does **not** false-fire a discrepancy. *Depends on:* Phase 4.

**Phase 6 — Probe robustness (populates the validated-decisive set for M4).** Structural NXDOMAIN
rule; multi-control DNSSEC quorum + DO-bit gate; encrypted-transport wildcard reference; the full R7
GroundTruth validation checklist (signed + fresh + bound + authenticated acquisition + **authenticated
quorum** + replay protection). **Per AMEND-O2:** the DNSSEC quorum counts only **RRSIG-validated**
controls (a `recordsPresentUnverified` posture, DNSSECVerifier.swift:44, does not count), and
`authoritativeDivergesConfirmed` requires a `WitnessSigner`-signed GroundTruth **and** ≥2
disjoint-lineage resolver cross-checks; and the validated-decisive set is authored as the **versioned,
`WitnessSigner`-signed `enum DecisiveAct`** catalog (§3). Crypto RNG on Windows. Only now do
`authoritativeDivergesConfirmed` and `dnssecCanaryStripped` **enter** the (signed, versioned) M4
validated-decisive set. *Closes:* NE2 Δ9/Δ10/Δ11, NE7 Δ3, NE5 Δ3. *Acceptance bar:* a **real round-robin
block page** trips the hijack (Positive, not "atypical"); a single benign `cloudflare.com` lapse does
**not** fire decisive stripping; a quorum built from `recordsPresentUnverified` controls does **not**
reach the decisive tier; a **real transparent Do53 proxy** no longer reads `wildcardZone`-benign; an
unsigned or unauthenticated GroundTruth cannot elevate to the decisive tier or trip the safety gate; the
signed `DecisiveAct` membership+version is present in the evidence trail and byte-identical on both
platforms. *Depends on:* Phase 5 (matrix), Phase 2 (safety-gate scaffold).

**Phase 7 — Calibration (valid belief-function semantics).** Singleton-focal LLRs and set-mass
class-vs-clean weights from the corpus (R6, §8.1); thresholds from the false-secure budget; coverage
calibration of `[Bel,Pl]` and a `BetP` reliability diagram (§8.2). RPN Occurrence ships **static
expert-elicited** by default, with the **pluggable opt-in adaptive Dirichlet-Multinomial per-SSID
backing** (five guardrails, surfacing-axis-only — §7, AMEND-O7) available behind its guardrails.
*Closes:* the magic-number proposals (NE1/NE4/NE7). *Acceptance bar:* measured
missed-tampering rate ≤ the agreed budget on a **held-out adversarial corpus**; interval coverage and
the `BetP` diagram within tolerance; every weight has a recorded derivation. *Depends on:* Phases 1-6 +
a sufficient corpus.

**Handoff.** `syxx-engineer` implements Stages 0-4 in `SyxxKit`/`Syxx.Kit` (both platforms) and the
Phase-6 probe/server changes (the R7 signed/authenticated GroundTruth touches the Dimension server
too). `syxx-gui-architect` surfaces Stage 5: the direct/inferred/unavailable provenance table, the
three honesty lines, the completeness figure, the within-class secondary split, the vantage matrix, and
the RPN-ordered finding list — without contradicting the underlying belief state. The golden-vector
corpus and the calibration dataset are shared artifacts owned jointly. Any change to user-visible
wording remains a proposal pending approval.

---

## 12. Open questions and where evidence is still needed

Several v1 open questions are now settled by D1–D4; the rest remain.

- **O1 — Calibration corpus provenance (open).** The biggest evidence gap. Phase 7 needs a labeled
  tampered/clean corpus per mechanism class. Source options: a lab harness (dnsmasq NXDOMAIN-hijack, an
  on-path DNSSEC-strip rig, a transparent-proxy VM), public censorship datasets, or synthesized
  fixtures. Until it exists, weights stay expert-elicited and the false-secure budget is asserted, not
  measured.
- **O2 — Validated-decisive-act membership (partly settled; representation fixed by AMEND-O2).** D1 + R7
  fix *what makes an act decisive* (the full checklist) and resolve v1's circularity: an act is decisive
  only when it passes the checklist, which for `authoritativeDivergesConfirmed`/`dnssecCanaryStripped` is
  not until Phase 6. AMEND-O2 fixes the *representation and the quorum bar*: membership is the versioned,
  `WitnessSigner`-signed `enum DecisiveAct`; the quorum must be **authenticated** (RRSIG-validated
  controls for the DNSSEC canary; signed GroundTruth + ≥2 disjoint-lineage resolver cross-checks for
  `authoritativeDivergesConfirmed`). Remaining: the exact freshness window, quorum **size** per act, and
  the replay-nonce scheme — to be pinned in that signed versioned membership spec.
- **O3 — Fusion-rule selection outcome (settled as a procedure, open as a result).** R5/Phase 0 fix the
  *method* (bake-off on adversarial vectors + bit-reproducibility). The *winning rule* is an
  experimental result, pending the corpus and the numerical determinism spike.
- **O4 — Lineage-cluster boundaries (open).** R4 fixes the *definition* (disjoint lineage tuple). The
  full `evidenceId → lineage` map (which raw-observation/control each act depends on) must be authored
  and reviewed against real co-occurrence in the corpus.
- **O5 — Mechanism-class boundaries (open).** Is REWRITE = `{spoofing, interception_onPath}` the right
  partition for the confidence/margin semantics, or should a portal-that-NX-hijacks case join a broader
  BLOCK/REWRITE superclass? The partition drives the headline number, so it needs sign-off validated
  against how users actually remediate.
- **O6 — Completeness denominator M (settled).** D3 fixes it: user-skipped decrements `M` (no penalty);
  scheduled-but-failed stays in `M` (penalized). Remaining detail: the per-invocation schedule that
  defines `M` for each CLI entry (`-nslook` expects 1; full `syxx` expects 6).
- **O7 — RPN occurrence base rates (settled for v1; adaptive path specified, AMEND-O7).** v1 ships
  `RPN = Severity × detection-tier` (honestly labeled), not full `S×O×D`, until a corpus exists, with the
  Occurrence axis backed by **static expert-elicited constants** by default. The Occurrence source is
  **pluggable**: an **opt-in adaptive Dirichlet-Multinomial per-SSID prior** is admissible behind the
  five guardrails of §7 (user-owned SQLite, weak `α=1` prior, confirmed-only updates, Stage-5
  transparency, `WitnessSigner`-signed state hash) and is fenced to the **surfacing axis only** — never
  the belief masses or the verdict. Remaining: the exact expert-elicited constants, and validating that
  the confirmed-only update gate is starvation-safe (not perpetually empty) on real per-SSID traffic.

---

## 13. Revision log — each accepted review item → the v2 / v2.1 section that implements it

The v2 rows are unchanged; the v2.1 audit rows (BLOCKER-1/2, MINOR-3/4, AMEND-O2/O3/O7) are appended
below the KEEP row. Two v2 decisions were amended in v2.1 (D2 by AMEND-O3, D4 by AMEND-O7) and their
rows now say so.

| Item | Where implemented | One-line note |
|---|---|---|
| **R1** — context ≠ auto-discount; split source reliability from benign explanation | §3 M1 & M2; I3/I4 (§5.4); Stage 2 (§2) | M1 discounts intrinsic unreliability only; M2 is causal/observation-gated or forces referral, so an attacker-induced context can't slide reliable evidence to Θ. |
| **R2** — remove post-combination multiplier; completeness folded into discounting or reported separately | §3 M3; §5.2; I6 (§5.4); §9 [Δmech row for NE7 Δ1] | Confidence = single credal `Bel`; completeness `K/M` is a separate reported dimension, never a `Bel` multiplier; degraded handled by M1 pre-combination discounting. |
| **R3** — delete the post-combination belief floor | §3 M4; I5 (§5.4); §9 [Δmech rows for NE1 Δ1, NE2 Δ2] | Decisive evidence gets a strong mass function *before* combination and/or the deterministic safety gate *outside* the DST number — `Bel/Pl/BetP/m(Θ)` never overwritten. |
| **R4** — correlation by full lineage, not act class | §6.3; Stage 0/1 lineage tuple (§2); §9 [Δmech row for NE1 Δ2] | Independence requires disjoint `{source/vantage, raw-observation, probe/control, act-class}`; different act alone ≠ independent. |
| **R5** — fusion rule is a Phase-0 experiment, not a pre-commitment | §4.2 candidate table + selection procedure; Phase 0 (§11) | Dempster / cautious / Yager / Dubois–Prade / weighted-mass compared on adversarial vectors + bit-reproducibility; cautious is only the leading candidate. |
| **R6** — valid calibration semantics | §8.1–8.2 | LLRs only for singleton-focal weights; multi-element weights via class-vs-clean + intra-class split; calibrate `[Bel,Pl]` coverage and the `BetP` reliability diagram, not a naive diagram on `Bel`. |
| **R7** — decisive ≠ signature alone | §3 M4 (checklist); Phase 6 (§11); §9 [Δmech row for NE2 Δ11] | Decisive = signed + fresh + domain/record-bound + authenticated acquisition + quorum + replay-safe. |
| **R8** — fix the invariants | §5.4 (I2, I5, I7); §5.1 (nil confidence) | I2 = vacuous source (no `m(Θ)` bump, no UNOBSERVED support); I5 = deterministic safety gate (not universal monotonicity); I7 wording corrected (low conf / narrow margin defers); no-data = nil, not 0. |
| **R10** — honest headline (no single-root-cause, no "smallest change") | §0; §1 (P1–P3 vs P4–P5 split) | Headline names the belief defect as one high-leverage cause among several; P4/P5 flagged as non-Dempster; scope stated as a full program of work. |
| **D1** — decisive-act = deterministic safety gate outside DST | §3 M4; I5 (§5.4); Phase 1 scaffold + Phase 6 population (§11) | Validated decisive act forbids CLEAN and floors the verdict at referral/tampered without touching the belief number. |
| **D2** (amended by AMEND-O3) — exact integer-% gating; display ε is a bake-off output | §3 M4; §10.1/10.2; Phase 0 (§11) | Verdict/name/referral compare exact integers (no gate epsilon); display-tier ε is measured in Phase 0 for the selected rule (not pre-pinned; `1e-7`/`1e-6` withdrawn); golden schema `_exact` + `_epsN` tiers. |
| **D3** — completeness M semantics | §3 M3; §5.2; O6 (§12) | User-skipped decrements `M` (no penalty); scheduled-but-failed stays in `M`; `K/M` forbids clean when a critical section isn't conclusive, not a `Bel` multiplier. |
| **D4** (amended by AMEND-O7) — honest RPN; pluggable occurrence source | §7 (RPN paragraph); Phase 7 (§11); O7 (§12) | v1 RPN = Severity × detection-tier (labeled as such); Occurrence source is pluggable — static expert-elicited default, opt-in adaptive Dirichlet-Multinomial per-SSID behind five guardrails, fenced to the Stage-5 surfacing axis only (never belief/verdict). |
| **KEEP** — P1–P5 diagnosis + delta map; ledger, provenance, tri-state gates, datapoint-scoped clean-support, mechanism-class reporting, parity golden vectors, real-data bars | §1, §9 (map); §2, §6, §7, §10, §11 | Confirmed scaffolding retained; delta map updated only where a closure mechanism changed (marked [Δmech]). |
| **BLOCKER-1** (v2.1) — channel-3 must not reintroduce "missing-data lowers confidence" | §6.1 channel 3; I2 (§5.4) | Thin *within-section* coverage → M1 pre-combination discount; *wholly-unrun* section → M3 completeness (forbids clean, not a `Bel` decrement); *unobserved* data → vacuous source / combination identity (I2): no mass, no confidence decrement. Wrong "leave mass on Θ (I2)… lower confidence" citation removed; I2 self-consistent everywhere. |
| **BLOCKER-2** (v2.1) — deterministic backstop for confirmed-threat wash-to-CLEAN | §3 M4 (confirmed-threat-mass floor gate, D5); I8 (§5.4); §6.2; §9 [NE1 Δ1 row] | Verdict-level gate **outside** the DST number and **independent of the fusion rule**: committed threat mass ≥ `threatNameFloor` forbids CLEAN; confirmed cross-vantage discrepancy also sets a `.positive` section; never overwrites `Bel/Pl/BetP/m(Θ)` (R3). Restores the guarantee deleting v1's belief floor left uncovered before Phase 6. |
| **MINOR-3** (v2.1) — §1 P4 overreach corrected | §1 P4 | `.degraded` **is** referenced (produced in Synthesizer.swift/.cs; defined on `SectionStatus`, Conclusion.swift:336 / Conclusion.cs:33). Tightened to the true claim: neither DST engine (SynthesisEngine.swift/.cs) applies a degraded discount/penalty to confidence. NE1 Δ6 delta remains real. |
| **MINOR-4** (v2.1) — forced-verdict / low-confidence display tension | §3 M4 (display note) | M4 floors the *verdict* without editing the *belief number*, so e.g. "Tampered, 20%" can appear; the non-contradictory presentation is a `syxx-gui-architect` surfacing proposal, pending approval; `Bel` is never altered to hide the tension (R3). |
| **AMEND-O2** (v2.1) — decisive quorum must be authenticated; versioned signed set | §3 M4; Phase 6 (§11); O2 (§12) | DNSSEC quorum requires RRSIG-validated controls (not `recordsPresentUnverified`); `authoritativeDivergesConfirmed` requires signed GroundTruth + ≥2 disjoint-lineage resolver cross-checks; the decisive set is a versioned `enum DecisiveAct`, membership+version `WitnessSigner`-signed for forensic reproducibility. |
| **AMEND-O3** (v2.1) — exact integer gates; ε is a bake-off output | §3 M4 (D2); §10.1/10.2; Phase 0 (§11); D2 row above | No epsilon at verdict/name/referral gates (exact integer equality); display-tier ε is measured for the selected rule in Phase 0 (not pre-pinned; `1e-7`/`1e-6` withdrawn); golden schema carries `_exact` (integer gate) and `_epsN` (display value) tiers. |
| **AMEND-O7** (v2.1) — adaptive occurrence priors admitted behind a hard boundary | §7 (RPN); Phase 7 (§11); O7 (§12); D4 row above | Occurrence source pluggable: static default + opt-in Dirichlet-Multinomial per-SSID behind five guardrails (user-owned SQLite, `α=1`, confirmed-only updates, Stage-5 transparency, `WitnessSigner`-signed hash); fenced to the Stage-5 surfacing axis only — never the belief masses or the verdict. |

---

## Appendix A — Referenced sources (verified)

**Code (opened at `file:line`, live tree, 2026-07-11):** SynthesisEngine.swift:58-68, 96-101, 113-116,
126-156, 170-188, 190-199, 214-243, 247-249, 277-291; Conclusion.swift:173-267 (WeightRubric incl.
190-196 dampeners, 262-264 authoritativeDivergesConfirmed), 302-319 (correlationGroup), 330-341
(SectionStatus + blocksClean:341); Synthesizer.swift:57-64 (combine), 144 (wildcardZone→.negative),
269-276 (serverConfirmed → authoritativeDivergesConfirmed), 361 (encryptedDNS state), 503 (witness
empty→.negative); Report.swift:78-91 (extractor omits publicResolverResults); `ComparisonResult`
(localServer / localRecords / publicResolverResults). Windows twins: SynthesisEngine.cs:74, 116-118,
180, 187, 189, 221-224; Conclusion.cs:139-145. DESIGN.md:95-116, 118-192 (incl. 169-172
minimum-corroboration; 182/185-188 Evidence Confidence Score + critical-section rule). *Note: the
NE-cited `DESIGN.md:700` `pow()` degraded-penalty formula does not exist in the current tree (stale
reference); R2 supersedes any post-combination multiplier.*

**Additional code re-verified for v2.1 (opened `file:line`, live tree, 2026-07-11):**
SynthesisEngine.swift:63 (`threatNameFloor = 0.40`), 204-206 (`threatUnion` / `Bel(threatUnion)`);
Conclusion.swift:133 (`enum EvidenceId` — no `enum DecisiveAct` exists yet, so AMEND-O2's is a proposed
addition), 140 (`dnssecCanaryStripped`), 169 (`authoritativeDivergesConfirmed`), 336
(`SectionStatus.degraded`); Conclusion.cs:33 (`SectionStatus` incl. `Degraded`), 79 (`enum EvidenceId`);
Synthesizer.swift:38 (`serverGroundTruth`), 111/126/247 (`.degraded` produced); Synthesizer.cs:61/73/131
(`Degraded` produced); DNSSECVerifier.swift:39-64 (`enum DNSSECStatus`, incl. `recordsPresentUnverified`
at :44); DimensionClient.swift:10 (`struct GroundTruth`); ProbePlan.swift:117 (`serverGroundTruth`
acquisition); WitnessSigner.swift / Windows/Syxx.Kit/WitnessSigner.cs (signing surface for the versioned
`DecisiveAct` catalog and the adaptive-prior state hash). Grep confirmed `SynthesisEngine.swift`/`.cs`
contain **no** `.degraded` reference (MINOR-3).

**Papers / standards (cited by title):**
- P. Smets & R. Kennes, *The Transferable Belief Model*, Artificial Intelligence 66(2), 1994 — credal
  vs pignistic two-level architecture.
- A. P. Dempster, *Upper and Lower Probabilities Induced by a Multivalued Mapping*, Annals of
  Mathematical Statistics 38, 1967 — `Bel` is a lower probability bound (basis for §8.2 calibration).
- G. Shafer, *A Mathematical Theory of Evidence*, Princeton, 1976 — belief functions, weights of
  evidence, discounting operator (M1).
- T. Denoeux, *Conjunctive and Disjunctive Combination of Belief Functions Induced by Nondistinct
  Bodies of Evidence*, Artificial Intelligence 172(2-3), 2008 — cautious/idempotent rule (Phase-0
  candidate).
- R. R. Yager, *On the Dempster–Shafer Framework and New Combination Rules*, Information Sciences 41,
  1987 — Yager's rule, conflict → Θ (Phase-0 candidate).
- D. Dubois & H. Prade, *Representation and Combination of Uncertainty with Belief Functions and
  Possibility Measures*, Computational Intelligence 4, 1988 — adaptive/disjunctive combination (Phase-0
  candidate).
- C. K. Murphy, *Combining Belief Functions When Evidence Conflicts*, Decision Support Systems 29, 2000
  — averaging/weighted-mass combination (Phase-0 candidate).
- A. Niculescu-Mizil & R. Caruana, *Predicting Good Probabilities With Supervised Learning*, ICML 2005
  — reliability-diagram calibration (valid on `BetP`, §8.2).
- R. A. Ramiarina, *Classification First: FMEA as a Cognitive Diagnostic System in STEM*, 2026 (pp.1-2,
  `/Users/masterrabbit/Downloads/preprint-fmea-classification-first_en_v0-16.pdf`) —
  classification-first, `RPN = S×O×D`, "collapse one mode, not three."
- *DNS Poisoning of Operating System Caches: Attacks and Mitigations*
  (`/Users/masterrabbit/Downloads/DNS_Poisoning_of_Operating_System_Caches_Attacks_and_Mitigations.pdf`)
  — cross-path answer divergence as a poisoning indicator (cited via NE2/NE6).
- RFC 8020 (NXDOMAIN: There Really Is Nothing Underneath); RFC 4035 / RFC 6840 (DNSSEC DO-bit);
  RFC 8484 (DoH); RFC 7858 (DoT); RFC 8032 (Ed25519).
