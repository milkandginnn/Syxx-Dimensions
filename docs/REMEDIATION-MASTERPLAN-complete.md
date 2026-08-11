# Syxx Remediation MASTER PLAN (WS1-WS5)

**Author:** Syxx Neural Planner · **Date:** 2026-07-13 · Branch `feat/beta-signup-and-server-hardening`
**Scope:** the unfinished reasoning-engine remediation (Phases 3->7 + Stage-5 remainder) folded together with
all **301 accepted deltas** (217 implement + 84 implement-as-revised) into **five dependency-ordered
workstreams**, sequenced so it can be implemented top to bottom in one pass.

**Authority:** delta roster + WS buckets from `scratchpad/final-decisions.md`; engine detail from
`ENGINE-PHASE2-7-PLAN.md` (kept as the detailed engine reference this plan links to), `REASONING-SYSTEM-DESIGN.md`
v2.1 (§11 phases, §12 O1-O7), `STAGE5-CONTRACT.md`, `BAKEOFF-DESIGN.md`, `PROJECT-STATE.md`. Honesty spine +
OUTPUT-REQUIRES-APPROVAL + parity + tri-state per `AGENTS.md`.

**Nothing in this plan is "gated."** Every previously-deferred item is a concrete coded step. The three items
that were gated on data that does not exist ship as **coded + runnable with principled defaults**, stated
honestly as such:

- **Ph3 fusion rule** commits with a **STATIC** evidence->cluster lineage/correlation table (not a captured
  corpus); the bake-off runs against the existing Stage0 golden corpus to **record** the choice.
- **Ph4 mechanism-class partition** is coded from the design's working example; only the headline **STRING** is
  COPY-GATED.
- **Ph7 calibration** ships the harness + weight-loader + runner with principled **LLR-tier** defaults.
No step fabricates a calibrated number or a verdict from data that does not exist.

---

## 0. How the five workstreams depend on each other

```
                 ┌─────────────────────────────────────────────────────────────┐
   S0 pre-flight │ commit uncommitted cluster · confirm Ph1/Ph2 gates committed  │
   (WS1 head)    │ both platforms + golden-green · renumber F023 collision        │
                 └───────────────┬─────────────────────────────────────────────┘
                                 │
      WS2-A (wire-parser false-clean) ── MUST precede WS1 Ph3/Ph4 real-run acceptance
      B065·B068·B069·B070·B071·B072·B073·A077·E018·B083   (hazard #1)
                                 │
      WS3-A (collector/leak signing) ── MUST precede WS1 Ph6 signed decisive-act + B098
      D057·D040/A021/A082·D003·D004·D011·A006   (hazard #3)
                                 │
   WS1  ── Ph3 (rule+lineage+clean-support) ─> Ph4 (verdict/confidence) ─> Ph5 (evidence model)
        ── Ph6 (probe robustness + DecisiveAct) ─> Ph7 (calibration) ─> Stage-5 remainder
        (Ph5 mass source consumes WS2-B: B040·B058·B059·B080·B083·B084 — hazard #2)
                                 │
   WS2-B/C  remaining evidence-acquisition integrity (probes, vantage, transport)
   WS4      server correctness & hardening (payments·SSRF·investigate·diff·DoS·growth)
   WS5      client surfaces, capture & platform safety (GUI honesty·Cage·URL/SSRF·platform)
```

### Cross-WS ordering hazards (binding — from final-decisions.md)

1. **Wire-parser false-clean lands before any WS1 real-data acceptance run.** If a truncated/short/off-name/
   off-class answer or an un-combined extended RCODE enters the fusion as CLEAN evidence, WS1's Ph3/Ph4 bar
   passes on garbage — the exact "31% inconclusive on 0 captured queries" failure. Sequence
   **B065, B068-B073, A077, E018, B083 (WS2-A) BEFORE WS1 Ph3/Ph4 real-run acceptance.**
2. **B040 target_answer plumbing + B058/B059 vantage UNION-vs-overlap land before WS1 Ph5 mass scoring.** Ph5
   scores cross-vantage discrepancy into DST mass; if a poisoned baseline can suppress redirection (B059) or a
   benign multi-A reads as DISCREPANCY (B040), Ph5 under-scores real interception or fabricates mass. Fix
   **B040/B058/B059/B080/B083/B084 (WS2-B) before WS1 P5.1.**
3. **Collector/leak signature + pin land before any "signed evidence" trust claim.** **D057** (leak verify
   fails 100% both platforms) and **D003/D004/D011/D040** mean the Witness layer cannot faithfully sign a
   verdict today. **WS1 Ph6 signed DecisiveAct catalog and B098 ride WS3-A** — do not build the signed
   decisive-act trail on a verify path that fails every time.
4. **Transient-false-secure guard.** Never swap the combination rule (Ph3) or add the Ph5 mass source before
   the Phase-1 clean-eligibility gates are **confirmed committed on BOTH platforms + golden-green** (S0).
   **Land B004's static lineage table IN THE SAME commit as the Ph3 rule swap** — swapping to `.cautious` (cap
   dropped) while lineage clustering is still source-string-keyed can double-count correlated symptoms OR wash
   a real one.

### Where the strict dependency reading strained the 5-WS cap, and the resolution

A strict "no step depends on a later step" reading wants the WS2-A wire-parser fixes and the WS3-A D057
signing fix to execute **before** WS1's phases, which would put engine-feeding work in WS2/WS3 ahead of the
"WS1 first" directive. **Resolution (per the brief's escape hatch):** WS1 stays the engine workstream and the
count stays at five; the specific WS2-A and WS3-A steps are **sequenced ahead in the global execution order
below** and cross-referenced from WS1's phase preconditions. WS1's *code* for Ph3/Ph4/Ph5/Ph6 can be written
against verified seams immediately; only its **real-data acceptance runs** wait on WS2-A/WS2-B/WS3-A. This
keeps the honesty spine intact (no false-secure acceptance on unhardened input) without a sixth workstream.

---

## Global execution order — the first steps tonight

Execute in this exact sequence. Each entry names its WS-step (detailed below).

1. **[WS1-S0]** Commit the uncommitted working-tree cluster (B031, B032, F006, F007, A019, A022 + the
   ProbePlan/ProbeRunner/DomainCanonical refactor + the A020 residual) with explicit-path commits on both
   platforms; **confirm Phase-1 gates committed both platforms + golden-vector green** (`SynthesisEngine.swift:139-152`,
   `Synthesizer.swift` `.invalidated` dominance; Windows twins) and **Phase-2 M2 dampener committed**
   (`SynthesisEngine.swift:236` / `SynthesisEngine.cs:210`). **Renumber the F023 id-collision** (two live deltas
   share it). Close the fixed cluster to `DELTAS-fixed.md` only after the commit lands.
2. **[WS2-A.1]** `DNSResponse.didFail` must include `.truncated` and servfail/refused/format-error
   (`DNSResolver.swift:181` / `DNSResolver.cs`) — **B065, B083**.
3. **[WS2-A.2]** Answer loop must not return `.success` when ANCOUNT > records actually parsed or the buffer is
   short; bound `rdlength`; carry a truncation/partial flag (`DNSResolver.swift:709-785`, `parseRecordsBestEffort`
   `:811`) — **B068, A077**.
4. **[WS2-A.3]** Match answer **CLASS** and **owner-name** to the question/CNAME chain; reject off-class/
   off-name RRs instead of accepting smuggled addresses (`DNSResolver.swift:737-785`) — **B069, B070**.
5. **[WS2-A.4]** Combine **extended RCODE** so BADVERS is not read as success; `findOPTRecord` returning nil on
   a malformed OPT must yield EDNS-**inconclusive**, not `notSupported`; parse authority strictly so malformed
   != absent-denial (`DNSResolver.swift:834`, `:682-695`) — **B072, B073, B071**.
6. **[WS2-A.5]** Windows `ParseName` must throw on loop-exit partial names / hostile compression rather than
   returning a partial name that reads as a clean answer (`Windows/Syxx.Kit/DNSResolver.cs` `ParseName`) —
   **E018**; confirm `A077` rdlength-bounds parity across both `DNSResolver` twins.
7. **[WS3-A.1]** Fix the leak-collector signature base64 so verify stops failing 100%: the collector emits a
   `urlsafe`-**unpadded** signature (`leak.py:158`) while the client verifies a standard/padded decode — align
   both sides on one encoding (recommend standard `b64decode` with padding restored) and add a round-trip test
   (`Server/syxx_server/leak.py:158,163`; client `LeakDetector.swift:261` verify / `LeakDetector.cs`) — **D057**.
8. **[WS1-Ph3.1]** Flip `Config.combinationRule` default to `.cautious` (`SynthesisEngine.swift:76` /
   `SynthesisEngine.cs:25`), **dropping the `maxWeightPerSource` cap path** (`:199-202` / `:162`), **and in the
   SAME commit** author the static evidence->cluster lineage table — extend `correlationGroup(for:)`
   (`Conclusion.swift:347` / `Conclusion.cs:266`) to add the `authoritativeDiverges`+`authoritativeDivergesConfirmed`
   pair and any other correlated symptom clusters; pin `ε` — **B004, B055, B007-class**.
9. **[WS1-Ph3.2]** Datapoint-scoped clean-support N/M (`SynthesisEngine.swift:258` clean-support loop /
   `SynthesisEngine.cs`) replacing the blanket per-section flag; build + run the **bake-off runner** over the
   existing Stage0 golden corpus to record the rule choice (no real captures required).
10. **[WS1-Ph3 acceptance]** Real-data acceptance run of Ph3 **(now safe — WS2-A + D057 landed)**: a real
    DNSSEC-strip capture with N clean sections still names REWRITE; a resolver that both NX-hijacks and
    DNSSEC-strips (disjoint lineage) reports higher confidence than either alone (no 74->31 inversion).

After step 10, continue WS1 Ph4->Ph7 + Stage-5 remainder, then WS2-B/C, WS3-B, WS4, WS5 in the order written.

---

# WS1 — Determinative core: engine remediation finish

**Goal:** finish the reasoning engine (Phases 3->4->5->6->7 + Stage-5 remainder + the reasoning/scoring/verdict
deltas) so every verdict is produced by the committed cautious/Dempster TBM engine with honest tri-state,
datapoint-scoped completeness, class-level confidence, cross-vantage mass, signed decisive acts, and principled
calibration — never transiently false-secure.

**Deltas (17):** B002, B004, B005, B040(engine-consume), B050, B051, B054, B055, B062, B064, B086, B088, B090,
E003, E004 + Ph7 magic-number class + Stage-5 remainder.

**Preconditions:** S0 (Ph1/Ph2 gates committed both platforms, golden-green); WS2-A for the Ph3/Ph4 real run;
WS2-B for Ph5; WS3-A for Ph6/B098. Detailed phase spec: `ENGINE-PHASE2-7-PLAN.md`.

## WS1-S0 — Pre-flight: commit, confirm gates, renumber (prerequisite)

- **Action:** as global step 1 above.
- **Closes:** unblocks all phases; closes the fixed cluster (B031/B032/F006/F007/A019/A022) to `DELTAS-fixed.md`
  after commit; renumbers F023.
- **Targets (verified):** `SynthesisEngine.swift:139-152` (`positiveSectionGate`/`criticalNotConclusive`/
  `cleanEligibilityBlocked`), `:236` (M2 `BenignExplanation.canExplain`); Windows `SynthesisEngine.cs:210`.
- **Parity:** verify Windows twins committed + golden-green before proceeding.
- **Acceptance bar:** `git log` shows the cluster committed on both platforms; golden-vector corpus runs green
  on both; F023 no longer collides. No engine behavior change (pure snapshot + confirmation).
- **Effort:** S. **Copy-gated:** no.

### Phase 3 — Fusion rule + lineage + datapoint clean-support

- **WS1-Ph3.1 (global step 8)** — swap rule to `.cautious` + drop cap + author static lineage table (same
  commit) + pin `ε`. **Closes B004, B055, B007-class.**
  - **Targets (verified):** `SynthesisEngine.swift:76` (`combinationRule`), `:199-202` (cap path), `:485`
    (`dempster()`), `Conclusion.swift:347` (`correlationGroup`), `:218` (`WeightRubric.getWeight`); Windows
    `SynthesisEngine.cs:25,162,458`, `Conclusion.cs:266,154`.
  - **Parity:** the rule, dropped cap, and lineage table byte-identical both platforms; add an `_epsN`
    tolerance golden vector at the pinned `ε`.
  - **Acceptance bar:** see global step 10. **Effort:** L. **Copy-gated:** no (internal arithmetic).
- **WS1-Ph3.2 (global step 9)** — datapoint-scoped clean-support N/M + bake-off runner over Stage0 corpus.
  - **Targets (verified):** `SynthesisEngine.swift:258` (clean-support), `Synthesizer.swift` sub-check
    collapse point (re-grep `subs.append`); Windows twins.
  - **Parity:** identical datapoint-count + scaling both platforms. **Acceptance bar:** a real Prophet run
    with 4/5 sub-checks clean + 1 timeout shows visibly lower clean-support/completeness than a 5/5 run, the
    timeout never absorbed into a binary "ran clean". **Effort:** L. **Copy-gated:** no (coordinate one
    completeness concept with Stage-5 `EvidenceCompleteness`).

### Phase 4 — Verdict/confidence semantics

- **WS1-Ph4.1** — confidence = `Bel(class-set)` single credal quantity (I6). Replace single-hypothesis
  `verdictBetP` readout with class-set belief. **Closes B086** (all-inconclusive must not fabricate
  `criticalAvailable==0`->fullData clean), part of B088.
  - **Targets (verified):** `SynthesisEngine.swift:314` (`verdictBetP`), `:289` (threat-name floor), `:325`
    (`belowMinCorroboration`); Windows twins. Depends on Ph4.2 class-set.
  - **Parity:** class-set + `Bel` identical. **Acceptance bar:** two captures with identical class-level
    evidence but different within-class emphasis report the **same** headline confidence. **Effort:** M.
    **Copy-gated:** no.
- **WS1-Ph4.2** — code the **mechanism-class partition** + `Hypothesis->MechanismClass` map (O5 working
  example, e.g. REWRITE = {spoofing, interception_onPath}). Partition logic coded; **headline STRING is
  COPY-GATED only.** **Closes B050** (expired-RRSIG benign lapse must not read `.positive`), **B051**
  (transport timeout must not read `.blocked`).
  - **Targets (verified):** new `MechanismClass` map near `Hypothesis` (`Conclusion.swift:106` / `.cs:13`);
    `Stage5Presentation` already carries `mechanismClass` (`.unresolved` today per PROJECT-STATE) — this fills
    it. B050/B051 status-map: `DNSSECVerifier.swift:47` (`signatureExpiredOrNotYetValid`), transport-timeout
    path in the Prophet section builder (`Synthesizer.swift`).
  - **Parity:** identical partition table. **Acceptance bar (COPY-GATED headline):** the real DNSSEC-strip
    surfaces as an interference-detected mechanism headline (exact wording proposed for approval), not
    "48%, ask a human"; expired-RRSIG on a benign lapse reads degraded/inconclusive not tampered; a transport
    timeout reads inconclusive not blocked. **Effort:** M. **Copy-gated:** YES (headline wording proposal).
- **WS1-Ph4.3** — within-class secondary split + class-vs-class referral margin; **B090** headline gating
  (engine side: verdict must not be drawable before Stage-5 fail-closed can gate it).
  - **Targets (verified):** `SynthesisEngine.swift:325` (margin/corroboration), Stage-5 within-class already
    landed (`3bf3048`); `ConclusionsView` draw-order is the WS5 twin of B090.
  - **Parity:** `_exact` boundary vector both platforms. **Acceptance bar:** within-class split renders as a
    clearly-subordinate figure; a class with two close internal hypotheses does not self-trigger a thin-margin
    referral. **Effort:** M. **Copy-gated:** the subordinate-line wording (WS5 surface).
- **E003/E004** status-map cleanups (BareDenial->Positive, Oversized->Degraded) ride Ph4's status handling;
  confirm both already-fixed states remain correct under the new class map. **Effort:** S.

### Phase 5 — Evidence model upgrade (consumes WS2-B)

- **WS1-Ph5.1** — cross-vantage discrepancy as a **scored** DST mass source (not just a gate); typed
  `DivergenceKind`; scaled by corroborating-vantage count; benign multi-homed CDN contributes zero mass.
  **Closes B002** (ship-gate provenance scorer), **B040** (engine-consume of set-valued target_answer),
  **B054** (warp-adapter inline-script/mismatched-hash signals feed mass).
  - **Targets (verified):** new support-construction branch near `SynthesisEngine.swift:179-258`;
    `VantageComparer.swift:121` (`evaluate`), `:146` (consensus union) provide the classification; ContentWarp
    signals from `ContentWarp.swift`. Windows twins.
  - **Precondition:** WS2-B (B040/B058/B059/B080/B083/B084) landed so the classification is trustworthy.
  - **Parity:** mass-scaling rule identical + `_epsN` vector. **Acceptance bar:** a real locally-poisoned run
    both **scores** toward interception and displays the true public consensus + outlier; a real multi-homed
    CDN contributes **zero** mass (assert pre/post `Bel`/`Pl`/`m(Θ)` unchanged for the CDN case). **Effort:** L.
    **Copy-gated:** no (matrix display already copy-gated).
- **WS1-Ph5.2** — **B005** sub-scorers (Resolver Provenance Confidence, Tamper Mechanism Fingerprint,
  Evidence Confidence Score) implemented to the DESIGN.md no/partial/full-data contracts; ship-gate the two
  unshipped scorers. **Targets:** `DESIGN.md` "Score-design invariants" section; scorer files under
  `Synthesis/`. **Acceptance bar:** a thin/empty-evidence run reads inconclusive, never confident. **Effort:**
  L. **Copy-gated:** no.

### Phase 6 — Probe robustness + DecisiveAct population (consumes WS2-B, rides WS3-A)

- **WS1-Ph6.1** — author the versioned, `WitnessSigner`-signed `enum DecisiveAct` catalog (scaffold + signing
  plumbing). **Rides WS3-A** (must not sign over a broken verify path). **B098** (signature must cover full
  evidence, not a lossy projection) is fixed here: extend `canonicalBytes()` to cover answer ownership/TTL/
  type so verify cannot pass while those are swapped.
  - **Targets (verified):** new `Synthesis/DecisiveAct.swift`/`.cs`; `WitnessSigner.swift:95` (sign), `:117`
    (`verifySnapshot`), `:164` (`canonicalBytes`); Windows `WitnessSigner.cs`. `validatedDecisiveActs` scaffold
    already present (`SynthesisEngine.swift:127`).
  - **Parity:** catalog + signing byte-identical. **Acceptance bar:** a real conclusion's evidence trail
    carries the signed catalog version + membership, verifiable via `WitnessSigner`; a mutated answer
    TTL/type/owner makes verify **fail** (B098 regression proof). **Effort:** M. **Copy-gated:** no.
- **WS1-Ph6.2** — fill `ValidatedDecisiveActs` + wire M4 safety-gate inputs; RRSIG-validated-only DNSSEC
  quorum (a `recordsPresentUnverified` posture does not reach the decisive tier); structural NXDOMAIN rule.
  **Closes B062, B064** (engine side: DNSSECVerifier-true-for-expired must not feed a decisive act);
  consumes **B056, B057, B058, B059, B061, C010** from WS2-B.
  - **Targets (verified):** `DNSSECVerifier.swift:44` (`recordsPresentUnverified`), `:72` (isUnverified
    helper); quorum aggregation new; structural NXDOMAIN in `Synthesizer.swift`/DNS probe layer. Windows
    `DNSSECVerifier.cs`. **Server touch:** R7 authenticated-acquisition of GroundTruth (coordinate WS3/WS4).
  - **Parity:** quorum size + RRSIG-only filter identical. **Acceptance bar:** a real round-robin block page
    trips `.positive` hijack (not "atypical"); a quorum built from `recordsPresentUnverified` controls does not
    reach the decisive tier; an unsigned GroundTruth cannot elevate to decisive or trip the M4 gate. **Effort:**
    L. **Copy-gated:** advisory text (WS5).

### Phase 7 — Calibration (coded + runnable, principled defaults)

- **WS1-Ph7.1** — build the calibration **harness + weight-loader + runner**; ship principled **LLR-tier**
  defaults as the `WeightRubric` values (design-sanctioned, honestly labeled expert-elicited-until-corpus).
  Do NOT fabricate calibrated numbers. **Closes the magic-number class.**
  - **Targets (verified):** `WeightRubric.getWeight` (`Conclusion.swift:218` / `Conclusion.cs:154`) becomes a
    loader over a defaults table; offline harness in the test target (pattern of the bake-off runner).
  - **Parity:** identical default table + loader both platforms. **Acceptance bar:** the runner executes over
    the Stage0 corpus and emits a coverage/reliability report; every weight has a recorded derivation
    (expert-elicited LLR tier), not a bare number; loading a different weight file changes verdicts
    deterministically. Reported honestly as "coded + runnable with principled defaults," not "calibrated on a
    real corpus." **Effort:** L. **Copy-gated:** no.
- **WS1-Ph7.2** — RPN Occurrence static expert-elicited default (S x detection-tier, honestly labeled);
  optional adaptive Dirichlet-Multinomial backing fenced to the Stage-5 surfacing axis only (never feeds
  belief mass or verdict). **Targets:** `Stage5Presentation` `PriorityFactors`/`PrioritizedFinding`; new
  `Synthesis/RPNOccurrence.swift`/`.cs`. **Acceptance bar:** adaptive opt-in changes the surfaced Occurrence
  figure but a pre/post diff proves belief mass + verdict are byte-identical for the same evidence. **Effort:**
  L. **Copy-gated:** the adaptive-vs-static framing (proposal).

### Stage-5 remainder (engine population; surfacing is WS5)

- **WS1-S5.1** — datapoint-level completeness (retire `profile.sections.v0`); populate mechanism headline /
  provenance (direct/inferred/unavailable) / within-class / cross-vantage answer matrix in the Stage-5 DTO.
  **Targets:** `Stage5Builder.swift`, `Stage5Presentation.swift`; Windows twins. **Acceptance bar:** §15
  real-run on a real machine (sandbox has no UDP:53) produces a validated `Stage5Presentation`
  (`validationErrors: []`) with datapoint completeness replacing the interim section-level flag. **Effort:** M.
  **Copy-gated:** the headline map (Ph4.2) and any new field labels (WS5).

**WS1 handoff:** `syxx-neural-executor` (all engine code, both platforms + golden vectors + real run).
Headline/subordinate/advisory wording -> `syxx-neural-surfacer` proposals. R7 server touch -> WS3/WS4.

---

# WS2 — Evidence acquisition integrity (feeds the core)

**Goal:** make every probe/transport/vantage input the engine consumes fail-honest — a truncated, short,
off-name, off-class, timed-out, or single-vantage reading can never enter the engine as CLEAN evidence, and
cross-vantage discrepancy is scored on a UNION consensus, not an overlap a poisoned baseline can suppress.

**Deltas (56):** A030, A036, A039, A040, A041-A045, A048, A049(rev), A050(rev), A051, A058, A059, A077, B010,
B025(rev), B040, B046, B047, B056, B057, B058, B059, B061, B065, B068-B075, B076, B077, B078, B079, B080, B083,
B084, B039(rev), C007, C013, C014, C015, C016, C017, C018, D056, E001, E008, E010, E014(rev).

## WS2-A — Wire-parser false-clean (EXECUTE FIRST — before WS1 Ph3/Ph4 real run; hazard #1)

- **WS2-A.1 (global step 2)** — `didFail` includes `.truncated` + servfail/refused/format-error. **B065, B083.**
  `DNSResolver.swift:181` / `DNSResolver.cs`. Parity: both twins. Acceptance: a truncated/servfail response
  reads inconclusive, never clean, on a real query. Copy-gated: no.
- **WS2-A.2 (global step 3)** — answer loop rejects ANCOUNT>parsed / short buffers; bound `rdlength`; carry a
  partial flag. **B068, A077.** `DNSResolver.swift:709-785,811`. Parity: both. Acceptance: a crafted
  ANCOUNT-inflated packet reads inconclusive/partial, not `.success`. Copy-gated: no.
- **WS2-A.3 (global step 4)** — match answer CLASS + owner-name to question/CNAME; reject off-class/off-name.
  **B069, B070.** `DNSResolver.swift:737-785`. Parity: both. Acceptance: a response with a smuggled off-name A
  record does not surface that address as the answer. Copy-gated: no.
- **WS2-A.4 (global step 5)** — combine extended RCODE (BADVERS != success); `findOPTRecord` nil-on-malformed
  -> EDNS inconclusive not `notSupported`; strict authority parse (malformed != absent-denial). **B072, B073,
  B071.** `DNSResolver.swift:834,682-695`. Parity: both. Acceptance: a BADVERS response reads failure/
  inconclusive. Copy-gated: no.
- **WS2-A.5 (global step 6)** — Windows `ParseName` throws on partial/hostile compression; confirm `A077`
  rdlength parity. **E018, A077(Win).** `Windows/Syxx.Kit/DNSResolver.cs` `ParseName`. Parity: match macOS
  `parseName` (`DNSResolver.swift:949`, which already `throws`). Acceptance: a compression-loop packet is
  rejected on Windows, not returned as a clean partial answer. Copy-gated: no.

### WS2-B — Vantage / authoritative / DNSSEC corroboration (before WS1 Ph5; hazard #2)

- **WS2-B.1** — B040 set-valued `target_answer` (isConflicting must not fire on benign multi-A, `values.count>1`);
  thread target answer to the answer matrix. `VantageComparer.swift:121-146`; Report/Stage5 matrix. Parity:
  both. Acceptance: a benign multi-A domain does not read DISCREPANCY; a genuine single-value swap does.
- **WS2-B.2** — B058/B059 vantage **UNION vs overlap** (poisoned baseline must not suppress redirection);
  upgrade LIKELY->CONFIRMED on union corroboration; B057 wildcard reference must filter `!=resolver`; B056 CLI
  must not mix localResolver into known-modern SVCB. `VantageComparer.swift:146`; probe layer. Parity: both.
  Acceptance: a run with one poisoned baseline still surfaces redirection via the union consensus.
- **WS2-B.3** — B080 amp-timeout must not fabricate `recursiveLowAmp ~0.0x` (missing != clean); B061
  `.notADenial` handling; C010 version.bind gating parity (Win gates Prophet, macOS tilt-only — forgeable
  banner). Probe layer + `Synthesizer`. Parity: reconcile macOS/Windows. Acceptance: an amp probe timeout reads
  inconclusive, not a fabricated low factor.
- **WS2-B.4** — DNSSEC integrity: A030/A073 DoH EDNS DO-bit (RRSIG strip); A042 RRSIG inception + RFC1982
  wrap; A043 typeCovered==DNSKEY check; B046 no-OPT/DO->weight3-on-signed; B047 RRSIG-vs-local-clock spoofing;
  A036 verify unsigned observations. Server `resolver.py` + client `DNSSECVerifier`. Parity: client+server.
  Acceptance: a zone with stripped DO-bit reads unsigned/inconclusive, not signed.
- **WS2-B.5** — B084 ContentWarp CNAME-target `.invalid` blocks all CDN/CNAME (fix mapping); B074 SVCB ignores
  failed vantage; B075 CNAME-chain partial validity flag; B079 empty-intersection `.unstable` (benign CDN
  rotation); B076/B077/B078 TTL-vary edge cases. `ContentWarp.swift`, `VantageComparer.swift`. Parity: both.
  Acceptance: a legitimate CNAME/CDN answer is not flagged tampered.

### WS2-C — IP classification / transport / misc acquisition

- **WS2-C.1** — B010 macOS DoH vantage (VantageComparer UDP-only; DoH Windows-only — **not fixed macOS**);
  bring macOS to DoH parity. `VantageComparer.swift:86`. Parity: macOS<->Windows. Acceptance: macOS runs a real
  DoH cross-vantage query.
- **WS2-C.2** — B025/C013 Windows `ClassifyIPv6` `::ffff:0:0/96` mapped-private branch (mapped sinkhole
  non-blocking on Windows); C016 IDN punycode vs raw Unicode compare; C017 formatRData hex CNAME; C018 SVCB
  param/ALPN/ECH stripping. `Windows/Syxx.Kit` IPClassifier + `AuthoritativeVantage.cs`; macOS twins. Parity:
  both. Acceptance: a mapped-IPv6 sinkhole is blocked on Windows.
- **WS2-C.3** — C014 macOS `DetectUnanimousTampering` (union-only, blind to unanimous UDP tamper); C015
  sharedProvider softening (CDN co-location evasion); D056 NetworkKeyState `.yes` if ANY resolver encrypted
  (mixed->secure while plaintext leaks); A048 warp utf-8 errors=ignore deletes bytes; A051 warp regex truncates
  at `>`. Parity: both. Acceptance: a unanimous-UDP-tamper case is caught on macOS.
- **WS2-C.4** — A039 DoH missing Status->0 / hostile JSON->500; A040 passivedns drift drops rtype; A044/A045
  vantage-walker drops reachable-empty / marks non-throwing "ok" regardless RCODE (root of A044); A041/A060
  early-return-first-vantage->false observed:false; A058/A059 AF_INET-only blind IPv6; A049/A050 warp
  fetch-error / set-vs-ordered compare (revised: downgrade, monotonic); E001/E008/E010/E014 IPv6 special-use +
  IDNA + Win registry-template false-clean. Server `investigate.py`/`resolver.py`/`passivedns.py` + client.
  Parity: client+server. Acceptance: the vantage walker no longer returns after the first vantage.

**Parity note (WS2):** every `DNSResolver`/`VantageComparer`/`DNSSECVerifier`/`ContentWarp` change needs its
`Syxx.Kit` twin; the DoH/RRSIG/DO-bit changes also touch `Server/resolver.py`.
**Copy-gated (WS2):** any new warning/advisory string (e.g. B056 SVCB caveat, D056 mixed-encryption caveat) ->
propose wording. **Handoff:** `syxx-engineer` (probes/transports), `syxx-neural-executor` where it feeds the
answer matrix.

---

# WS3 — Trust infrastructure: signing, identity, collector, licensing

**Goal:** make signed evidence actually trustworthy end to end — leak verify must stop failing 100% (D057),
signatures must gate verdicts and cover the full evidence, keys must be pinned and non-forgeable, and licensing
must fail **closed** (never fail-open on a missing key).

**Deltas (53):** A006, A021, A023, A024, A034, A056, A082, A109, A110, A111, A113, A114, B026(rev), B030, B049,
B053, B063, B066, B067, B082, B085, B091, B092, B093, B098, B099, B100, C026->A024, C028, C031, D001, D002(rev),
D003, D004, D005(rev), D006, D011, D021, D027, D030(rev), D040, D050(rev), D051, D052, D053, D057, D066, E011(rev),
E019, E020, E021, E024(rev), B081->D011.

## WS3-A — Collector/leak signing correctness (EXECUTE before WS1 Ph6 + B098; hazard #3)

- **WS3-A.1 (global step 7)** — **D057** leak signature base64 fix (urlsafe/padding). `leak.py:158,163`;
  client `LeakDetector.swift:261` / `.cs`. Parity: server+both clients. Acceptance: a real collector
  attestation **verifies** (today it fails every time); a corrupted signature fails. Copy-gated: no.
- **WS3-A.2** — **D040/A021/A082** licensing fail-open: `licensing.py:130,136` `return True` when
  `KEYGEN_PUBLIC_KEY` empty or Ed25519 lib absent -> fail **closed** (deny, never grant) in production, with an
  explicit dev/test opt-out flag. Parity: server. Acceptance: with no pubkey configured in production mode, a
  validate returns denial, not a granted entitlement. Copy-gated: no.
- **WS3-A.3** — **D003** collector TLS/SPKI pin residual (fail-closed to inconclusive) + **D004** well-known
  pubkey endpoint + **D011** thread `trustedPublicKeys` into `LeakDetector.verify()` + **A006** unpinned
  collector pubkey. `LeakDetector.swift:97-147,261` (session `:102`, verify `:261`); `Server/app.py`
  `GET /api/pubkey`. Parity: both clients + server. Acceptance: a MITM-proxied collector reads inconclusive; a
  signature from an untrusted key reads untrusted (not valid); no-trust-set keeps today's behavior.
- **WS3-A.4** — **B098** signature covers full evidence (answer ownership/TTL/type), not a lossy projection.
  `WitnessSigner.swift:164` `canonicalBytes` / `.cs`. Parity: both. Acceptance: mutating a signed answer's
  TTL/type/owner makes verify fail. (Landed in WS1-Ph6.1 if sequenced together — cross-referenced.)

### WS3-B — Attestation liveness, freshness, replay, identity

- **WS3-B.1** — **A024/C026/B030** empty-collector signed `leaked:false` zero-liveness (forgeable); sign over a
  liveness proof. **A111** leak.verify signs before consume()/delete race. **A034** leak-token replay unbounded
  INSERT. `Server/leak.py`. Parity: server. Acceptance: an empty collector cannot produce a signed clean leak
  result; a replayed token is rejected.
- **WS3-B.2** — **A056** signs Date header with no freshness (Keygen replay bypass expiry); **B085** max-age
  gate `now>timestamp` future-dated snapshot skips freshness. `licensing.py`, `WitnessSigner.swift:117`
  (`verifySnapshot` maxAge). Parity: server+client. Acceptance: a future-dated snapshot is rejected as stale/
  invalid.
- **WS3-B.3** — **A109** deploy.sh world-readable `Environment=` incl `COLLECTOR_PRIVATE_KEY`; **A110** `User=root`
  network parsers -> RCE=root; **A113** leak key config DER/seed contradiction; **A114** pubkey rotation
  contract. `Server/deploy.sh`, systemd unit, `leak.py` key load (`:55`). Parity: server/ops. Acceptance:
  private key file not world-readable; service runs unprivileged.
- **WS3-B.4** — **A023** resolver_ip mirrored to cross-readable passive_dns PII. `passivedns.py`. Acceptance:
  resolver IP PII not cross-readable.

### WS3-C — Client verdict-gating on signature validity (GUI trust surfaces)

- **WS3-C.1** — **B091** Win leak verdict from OUTER json not signed payload; **B092** DimensionClient no
  sig/pin, Synthesizer upgrades to decisive on unsigned; **D004(client)/D005** sig verified but NOT
  verdict-gating; **B026** version-mismatch discarded -> bare false; **D050** Win collapses verify-throw to
  false. `LeakDetector.cs`, `DimensionClient.swift/.cs`, `WitnessSigner`. Parity: both. Acceptance: a verdict
  built from an unsigned/forged payload reads inconclusive, not confirmed.
- **WS3-C.2** — **B049/B053/C028** WitnessView green "no tampering"/"Signature valid" ungated by
  `witnessValid`; **B063** `witnessValid Bool ??false` (not-run/threw/mismatch all render tampered); **B066**
  GluttonView colors on `signatureValid` alone ignoring `keyTrusted`; **B067** `.notDetected` green while
  posture `notConfigured`; **B093** empty drifts->`.negative` clean on timeout; **B082** Win CLI HMAC
  not-verifiable misinfo (server signs Ed25519). `WitnessView.swift`, `GluttonView.swift`, Windows twins.
  Parity: both. Acceptance: a not-run/failed witness renders inconclusive, never green or tampered. Copy-gated:
  YES (trust-surface labels; includes A075 "signed"->"dnskeyObserved" wording — proposal).
- **WS3-C.3** — **C031** leak baseline: `leaked=len>0` with no expected-resolver baseline (any resolver
  "confirms leak"); add an expected-resolver baseline. `LeakDetector` reconcile (`:283`). Parity: both.
  Acceptance: the user's own resolver does not read as a leak.
- **WS3-C.4** — identity/canonical: **E011** cross-platform sign/verify byte-parity real run; **E019** Win
  ISO-8601 vs macOS Double date (verify throws); **E020** errorMessage free text in canonicalBytes breaks
  byte-identity; **E021** Win VerifySnapshot collapses expired/untrusted->false; **E024** guard rebuilds from
  signed millis not display Date. `WitnessSigner` both. Parity: both. Acceptance: a snapshot signed on one
  platform verifies on the other (real run).
- **WS3-C.5** — lower-severity licensing/key hygiene (revised/low): **D001/D021** forgeable HMAC trial key;
  **D002** Keychain-delete reset; **D006** Win MachineName fallback; **D027** license key over http; **D030**
  unsigned baseline/Sentinel JSON; **D051/D052/D053** WitnessTestify/KeyManager silent no-op / offline DB no
  HMAC; **D066** unsigned meta.valid MITM; **B099/B100** keyId relabelable / crypto-validity-vs-quality
  conflation. Parity: both. Acceptance: none of these paths grant trust on unauthenticated input.

**Parity note (WS3):** trust-material changes touch `Server/` (leak.py, licensing.py, app.py, deploy) AND both
clients. **Copy-gated (WS3):** WS3-C.2 trust-surface labels + A075 rename + D003 pin-failure strings.
**Handoff:** `syxx-engineer`/`syxx-neural-executor` (signing/verify), `syxx-gui-architect` (trust surfaces),
Server lane for the collector/licensing/deploy fixes.

---

# WS4 — Server correctness & hardening

**Goal:** the Python server must never double-mint, never SSRF/rebind into internal targets, never crash a
diagnostic into a masked 500, and never leak or exhaust — every endpoint returns an honest tri-state and
bounded resource use.

**Deltas (59):** A001-A004, A005(rev), A007(rev), A008(rev), A012(rev), A013, A020(rev), A025, A026(rev),
A027(rev), A028, A029(rev), A031, A032, A033, A035(rev), A037, A038, A046, A047, A052, A053(rev), A054(rev),
A057, A063, A066(rev), A067, A069->A013, A073->A030, A075, A078(rev), A079(rev), A080(rev), A083(rev), A084->A035,
A085, A090->A067, A091(rev), A092(rev), A093->A051, A094, A095->A079, A096->A028, A097(rev), A098(rev),
A100->A067, A101->A028, A102(rev), A104(rev), A105, A107(rev), A108, A112, F030.

## WS4-A — Payments integrity (Critical)

- **WS4-A.1** — **A013/A069** TOCTOU double-mint: `fulfillment.py:83-88` check-then-set race -> atomic
  compare-and-set (`kv_insert_once`-style). Parity: server. Acceptance: two concurrent webhook deliveries mint
  exactly one license.
- **WS4-A.2** — **A025** single stripe_cust slot overwrite->lockout; **A035/A084** checkout idempotency_key;
  **A046** stripe GET expand[] in body; **A047** ignores mailer.send / redelivery duplicate / buyer no key;
  **A032** over-cap evicts first_launch->paywall bypass. `stripe_checkout.py`, `fulfillment.py`. Acceptance: a
  redelivered webhook does not duplicate, and a paid buyer always gets a key.

### WS4-B — SSRF / rebind / bailiwick (Critical)

- **WS4-B.1** — **A067/A088/A090/A100** DNS-rebind SSRF: `resolver.py:244` `HTTPSConnection(ip,443)` then
  `:245 conn.host=sni` re-resolves; **pin to the vetted sockaddr** (dial the already-validated IP; never let
  the connection re-resolve the hostname) for both `fetcher.py` and `query_doh`. Confirm `_ip_is_public`
  (`:30`) gates the pinned IP. Parity: server. Acceptance: a hostname that resolves public-then-private cannot
  reach an internal target.
- **WS4-B.2** — **A063** out-of-bailiwick glue accepted (poisons ground truth): reject glue not in-bailiwick
  for the delegated zone. `resolver.py:423-429`. Acceptance: a malicious authoritative's out-of-bailiwick glue
  is dropped.
- **WS4-B.3** — **A033/A100** no port allowlist (port scanner) — allowlist 443/53 as applicable. `resolver.py`,
  `fetcher.py`. Acceptance: a request to a non-allowlisted port is rejected.

### WS4-C — Crash-to-500 masking honest inconclusive

- **WS4-C.1** — **A078/A098** unguarded `struct.unpack` -> `struct.error` -> 500 masks inconclusive:
  `resolver.py:103,114,321,329` (question + record parse). Guard and return inconclusive. **A039** hostile DoH
  JSON->500. Parity: server. Acceptance: a malformed DNS/JSON payload returns an honest inconclusive, not a 500.
- **WS4-C.2** — **A038** exposure DNSError->false "closed/good"; **A075** `_zone_is_signed` DNSKEY-presence
  labeled "signed" -> rename `dnskeyObserved` (COPY-GATED). `investigate.py`, `dns_authoritative.py`. Acceptance:
  a DNS error reads inconclusive; the "signed" label reflects actual verification.

### WS4-D — Diff / investigate correctness

- **WS4-D.1** — **A026/A027** cross-host script injected->hard tampered / first-race-winner clean w/ no
  authority (accuse/certify asymmetry->false-secure); **A012** AA geo-DNS zone still tampered; **A041/A044/A045/
  A060** vantage-walker fixes (server side); **A092** cache keyed target_url but assets vs final_url->stale
  baseline; **A094** inline-hash strips comments; **A097** plaintext resolver IP intended output (retention is
  the fix). `investigate.py`, `warp.py`, `fetcher.py`. Acceptance: a benign page is not false-tampered and a
  race-winner without authority does not certify clean.

### WS4-E — DoS / resource bounds

- **WS4-E.1** — **A005/A028/A066/A096/A101/A102/A108** absolute request deadline + connection cap on the
  ThreadingHTTPServer / sequential vantages (no aggregate deadline; unbounded threads). `app.py`, `investigate.py`,
  `resolver.py`. Acceptance: a slow-drip request is bounded by an absolute deadline.
- **WS4-E.2** — **A054** ratelimit truncation->0-refill fast-poll lockout (monotonic anchor); **A079/A091/A095**
  retention/row-cap (bounded disk); **A052** kv no timestamp; purge omits kv; **A031** beta_signups never
  purged / weekly re-email; **A085** no reactivation handler; **A057** version compare ragged tuples (beta
  collides stable). `ratelimit.py`, `db.py`, `fulfillment.py`. Acceptance: sustained polling does not lock out;
  disk stays bounded.

### WS4-F — Info-leak / hygiene / misc server

- **WS4-F.1** — **A105** readyz leaks sqlite path in 503; **A112** read(1<<20) truncation; **A029** escape all
  csv cells; **A037** env secret no entropy check; **A107** SMTP insecure certs default; **A104** broadcast no
  scheme validation; **A001-A004/A007/A008/A020/A080/A083** confirm-fixed residuals; **F030** test-harness stub
  leak. Parity: server. Acceptance: no internal path in error bodies; residuals confirmed committed.

**Parity note (WS4):** server-only, but A075 rename + any wording touches the client "signed" label -> confirm
with WS3-C. **Copy-gated (WS4):** A075 label rename, A038 exposure wording, A105 error text. **Handoff:**
`syxx-engineer` (server lane); `syxx-neural-executor` for A026/A027 verdict semantics if they touch synthesis.

---

# WS5 — Client surfaces, capture & platform safety

**Goal:** every user-visible surface renders the engine's tri-state honestly (no green-on-inconclusive, no
verdict-before-Stage-5), the Cage capture path never reports "Captured N"/"Saved"/"erased" on a partial or
failed run, and client URL/SSRF/resource-safety holes and platform parity gaps are closed.

**Deltas (116):** B014(rev), B041, B042, B043, B045(rev), B052, B087, B088(surface), B089, B090(render), B094,
B095, B096, B097, C001(rev), C005(rev), C006(rev), C009(rev), C010(surface), C019-C025, C027(rev), C029->C024,
C030, C032-C045, C034(rev), C040(rev), D014->C005, D017, D018, D019, D020, D022, D023, D024, D025(rev),
D026(rev), D028, D029, D032-D039, D041, D042, D043, D044, D045, D054(rev), D055, D059(rev), D060, D061(rev),
D062, D063(rev), D064, D065, D067, E012, E015(rev), E016, E017(rev), E022(rev), E026(rev), E027, E028, E029,
E030, E031, E032, E033, E034, E035, E036, E037, E038, E039, E040, E041(rev), E044(rev), E045, E046, F009, F013(rev),
F015-F029, F026(rev).

## WS5-A — GUI honesty sweep (green-on-inconclusive; verdict-before-evidence)

- **WS5-A.1** — **B090(render)** ConclusionsView draws verdict BEFORE Stage-5 (fail-closed can't gate
  headline) -> render only after Stage-5 `isValid`; **B088(surface)** macOS trust=100-confidence green on
  threat / Win labels Confidence as TRUST SCORE. `ConclusionsView.swift`, Windows `ConclusionsView.xaml.cs`
  (E015: Windows view is **absent** -> build it). Parity: both (build the missing Windows view). Acceptance: a
  threat verdict never renders green; the headline waits on valid Stage-5. Copy-gated: YES (labels).
- **WS5-A.2** — **B052** MirrorView green for DEGRADED; **B097** ProphetView `.No`+`.Unknown` both->cleartext
  (tri-state collapsed); **B087** ConfigChange->empty AnswerDrift->`.positive` false tamper / drifts=[] on no
  baseline->`.negative`; **B067(surface)/F013/F018/F019** dashboard green on "—"/inconclusive->criticalRed;
  **E027** IsInconclusive/IsDegraded collapse. `MirrorView.swift`, `ProphetView.swift`, `DashboardView.swift`,
  Windows twins. Parity: both. Acceptance: degraded/unknown/inconclusive never renders clean or green.
  Copy-gated: YES.
- **WS5-A.3** — **B041/B042** renderText drops advisories/conflict (unwired); **B043** Win Glutton adapter
  returns every leak branch; **B044/B048** confirm; **B094** EndpointLogAnalyzer skips invalid JSON no
  counter->"nothing stands out"; **B095** Win lowercases then Contains mixed-case markers (dead rules); **B096**
  RecordTypePicker decorative hardcodes A; **B014** Win `Program.cs:339` unqualified SECURE. Parity: both.
  Acceptance: advisories/conflicts render; invalid input is counted, not silently clean. Copy-gated: where new
  strings.
- **WS5-A.4** — **B089** force:true returns stale cache stamped upToDate (hides shipped update); **C001/C005/
  C006/C009** fallback/probe residuals; **E026** wrong-host/path-doubled leak endpoint->inconclusive not false;
  **F015/F016** static "Verifying with Keygen"/CURRENT pill (mock, copy-gated); **F026** download handoff
  overstated. Parity: both. Acceptance: an update check reflects real state, not a stamped stale cache.

### WS5-B — Cage capture / forensics honesty (no fabricated "Captured/Saved/erased")

- **WS5-B.1** — **C037** report write `try?` swallowed prints "Saved" unconditionally; **C039** `try?
  removeItem` swallowed prints "erased" (false erasure); **C038** parsedRecords>0 only gate, unparsedCount
  printed not gating; **C044** pcap err breaks then reports "Captured N"; **C041** Win capture catch swallows
  -> ordinary report; **C037-C045** capture-completeness cluster. `DNSCaptureReportGenerator.swift`/`.cs`,
  `Ccage.c`, Windows `DNSCapture.cs`. Parity: both. Acceptance: a failed/partial capture reads INCONCLUSIVE and
  does not print "Saved"/"erased"/"Captured N". Copy-gated: YES (report strings).
- **WS5-B.2** — **C019-C024/C029** UDP-only capture / TCP-53 absent / IPv6 nextHeader==17-only / fragment-
  offset / parse-break-then-report / slice-to-buffer-end padding (forgeable tail); **C042/D063** direction/
  resolver/attribution from attacker QR bit (forgeable); **C043** BPF udp53-only (DoT/DoH invisible, no
  coverage line); **C045** single-signal benign / Win mean0 HighlyRegular vs macOS insufficientData parity;
  **C025** plaintext-UDP baselines on-path controls (false "matches"). `Ccage.c`, `DNSCapture.cs`, analyzers.
  Parity: both. Acceptance: a spoofed QR-bit packet does not drive attribution; coverage gaps are disclosed.
- **WS5-B.3** — **D065** symlink-race root file-clobber TOCTOU (O_NOFOLLOW); **D067** pidCache unbounded +
  stale-on-reuse; **D060** single-quotes paths in osascript admin (apostrophe->root cmd exec); **E035/E036/
  E037/E040/E046** attribution/interface/VPN-visibility parity; **D055** osascript child orphan. `Ccage.c`,
  Forensics, admin-exec path. Parity: both. Acceptance: a symlink at the capture path cannot clobber a root
  file; an apostrophe in a path cannot execute a command.

### WS5-C — Client URL / SSRF / resource safety

- **WS5-C.1** — **D032/D033/D034/D035/D036/D038/D064** open-any-URL / substring host bypass / no-HTTPS /
  verbatim Update.Url / any-scheme NSWorkspace.open; **D045** `new Uri(broadcast.Url)` LaunchUri no scheme/
  try-catch (crash + arbitrary scheme); **D037** startup cache plain JSON trusted no sig; **D041** unsigned
  MOTD. `DashboardView.swift`, `RemoteContentView.swift`, `BroadcastPanelView.swift`, Windows twins. Parity:
  both. Acceptance: only vetted HTTPS origins open; a malformed broadcast URL does not crash. Copy-gated: where
  new strings.
- **WS5-C.2** — **D062** TextDiff full matrix before cap (unbounded HTML OOM); **D020** name-decompression
  O(n^2) on 64KB TC; **D022** streamLines unbounded; **D023** ContentWarp no timeout/script cap (slow-loris);
  **D024** MDNSScanner orphan thread; **F024** WKWebView cache never evicts; **F028** duration accepts
  neg/nan/inf (Win AddSeconds(NaN) crash); **F029** Win IPv4-opt on v6 socket. Parity: both. Acceptance: a
  hostile large page is bounded, not OOM; NaN duration is rejected.
- **WS5-C.3** — **D018/D019/D025/D026** client SSRF: AuthoritativeVantage NS glue no IPClassifier gate / SSRF
  validate UDP but fetch re-resolves / public->probeable / SieveBlocklistLoader `://`->file:/// LFI.
  `AuthoritativeVantage.swift`, `SieveBlocklistLoader.swift`, Windows twins. Parity: both. Acceptance: client
  fetch cannot be rebound to an internal/file target.

### WS5-D — Licensing UX honesty + platform parity + hygiene

- **WS5-D.1** — **D042** `try? saveLicenseKey` then "SUCCESS" isPro=true regardless (§5 highest honesty item);
  **D043** bool wrapper collapses networkError->false ("invalid" for outage); **D044** Win GluttonView no leak
  verdict; **D054/D059/D061** trial clock/offline edge cases; **D028/D029** dead IsFeatureUnlocked / Pro path
  on isPro alone; **E044** WinUI sync-over-async UI freeze. Parity: both. Acceptance: a save failure or network
  outage reads honestly, never "SUCCESS"/Pro. Copy-gated: YES (license status strings).
- **WS5-D.2** — parity/hygiene cluster (low): **E012/E016/E017/E022/E029/E030/E031/E032/E033/E034/E038/E039/
  E041/E045/E028**; **F009** coverage-debt seams; **F015-F017/F020-F023/F025/F027**; **F023 collision** (the
  Sentinel `try? setAttributes` 0644-PII variant vs the emoji variant — renumber per S0). Parity: both.
  Acceptance: parity gaps closed; no silent catch drops evidence. Copy-gated: where new strings.

**Parity note (WS5):** every SwiftUI view change needs its WinUI twin (build the absent Windows
`ConclusionsView` for E015); Cage `Ccage.c` changes need the Windows `DNSCapture.cs` twin. **Copy-gated (WS5):**
all verdict/label/report strings (WS5-A, WS5-B.1, WS5-D.1) -> propose wording. **Handoff:**
`syxx-gui-architect`/`syxx-neural-surfacer` (surfaces + Stage-5 rendering), `syxx-engineer` (Cage/capture/URL/
resource logic).

---

## Risks & open questions

- **O5 mechanism-class boundary (WS1-Ph4.2)** is coded from the design's working example; the partition
  *boundary* still wants user sign-off because it drives the headline number. Per the directive it ships coded
  (headline string copy-gated), but flag the boundary for confirmation — do not treat the partition as final
  design without it.
- **Ph7 is "coded + runnable with principled defaults," not "calibrated."** The runner + loader + LLR-tier
  defaults ship; measured calibration still needs the labeled corpus (O1). State this honestly in any report;
  do not present the defaults as measured.
- **Uncommitted-tree cluster** (B031, B032, F006, F007, A019, A022 + ProbePlan/ProbeRunner/DomainCanonical +
  A020 residual) must be committed in S0 before anything closes to `DELTAS-fixed.md`.
- **F023 id-collision** must be renumbered in S0 (two live deltas share the id).
- **Line drift:** engine anchors (`SynthesisEngine.swift`, `Conclusion.swift`, `DNSResolver.swift`) were
  re-verified against the live tree 2026-07-13; bundled WS2/WS4/WS5 anchors in step sub-groups are named at
  file + nearest verified symbol and MUST be re-grepped before edit. Windows engine twins verified present
  (`SynthesisEngine.cs`, `Conclusion.cs`, `DNSResolver.cs`, `DNSSECVerifier.cs`, `WitnessSigner.cs`,
  `LeakDetector.cs`, `VantageComparer.cs`, `ContentWarp.cs`).
- **`MechanismClass` / `DivergenceKind` enums do not yet exist** as named types (the DTO carries a
  `mechanismClass` field defaulted `.unresolved`); WS1-Ph4.2/Ph5.1 create them.
- **Server R7 touch (WS1-Ph6.2)** spans client + `Server/` (authenticated GroundTruth acquisition) —
  coordinate WS3-B/WS4 ownership; it is the largest single engine-adjacent item.

## Handoff

- **`syxx-neural-executor`** — WS1 (all phases), WS2 answer-matrix feeds, WS3 signing/verify, verdict-semantic
  server deltas (A026/A027).
- **`syxx-engineer`** — WS2 probes/transports, WS4 server, WS5 Cage/URL/resource logic.
- **`syxx-gui-architect` / `syxx-neural-surfacer`** — WS5 surfaces + Stage-5 rendering; all copy-gated wording
  as proposals pending approval.
- **Detailed engine reference:** `ENGINE-PHASE2-7-PLAN.md` (this masterplan's WS1 is its executable
  distillation with the gates removed per the tonight directive).

---

*End of REMEDIATION MASTER PLAN (WS1-WS5). All 301 accepted deltas assigned to five dependency-ordered workstreams. Global execution order with hazard sequencing defined in §0 (lines 78–117). Per-workstream step details with delta groups, target files, parity notes, acceptance bars, and agent handoffs in WS1–WS5 sections.*
