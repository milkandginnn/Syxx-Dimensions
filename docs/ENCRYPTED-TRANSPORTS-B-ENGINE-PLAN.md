# Sub-project B — Multi-transport corroboration (engine): Engineer-Ready Plan

**Owner lane:** `syxx-neural-executor` (reasoning/verdict core). **Design source:**
`ENCRYPTED-TRANSPORTS-PLAN.md` §3 (cross-cutting honesty contract), §5.B (goal/shape/real-run
bar), §5.A (the transport-provenance fields B consumes); `PROPOSALS.md` A1 (`:72`); `DESIGN.md`
"Determinative synthesis" (`:103`) + "Score-design invariants" (`:126`).

**One-sentence goal.** After sub-project A lands per-answer transport provenance
(`requestedTransport`, `servedTransport?`, `negotiatedProtocol?`, `DNSTransport` enum, derived
`wasDowngraded`) on `DNSResponse` (both platforms), turn a divergence between a plaintext **Do53**
answer and a **proven encrypted-transport baseline** (DoT / DoH3 / DoQ) into a **tri-state tamper
signal** inside the Mirror discipline and the Dempster–Shafer synthesis engine — such that a poisoned
Do53 path with a clean encrypted path yields a tamper signal, a clean network yields no false tamper,
and an unproven/downgraded/missing encrypted path yields **inconclusive**, never clean and never
tampered.

**Hard dependency.** B does not start until **A0** (shared tri-state `SecureResolutionResponse`) and
**A1** (transport provenance on `DNSResponse`) have landed on both platforms with a real run. B0
below is the gate that verifies this against the live tree.

---

## 0. Codebase infrastructure the implementer must know first

The synthesis engine is a **Dempster–Shafer belief accumulator**, not a boolean/score ladder. Read
these before touching anything:

- **The engine.** `macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift` (Windows
  `Windows/Syxx.Kit/Synthesis/SynthesisEngine.cs`). Signals become *simple support functions*
  `(focusMask, weightOfEvidence)`; they combine under Dempster's rule; the verdict is named only when
  committed belief clears a floor; `inconclusive` is **unassigned mass**, never a class.
- **The evidence contract.** `EvidenceItem` (`Conclusion.swift:332` / `Conclusion.cs:253`) carries a
  `section`, an `EvidenceId`, and a `source` (correlation group). The engine looks up its weight per
  hypothesis in `WeightRubric.getWeight` (`Conclusion.swift:232-328` / `Conclusion.cs`, the
  `(EvidenceId, Hypothesis) => weight` switch beginning ~`:170`). Adding a signal = add an
  `EvidenceId` case + weight rows + an emitter in an adapter. **The engine core itself does not need
  to change** if we route B through the evidence-item path — this is a deliberate design choice (§2).
- **The adapters.** `EvidenceAdapters.mirror(...)` (`Synthesizer.swift:240-352` /
  `Synthesizer.cs:308+`) maps typed Mirror inputs → `SectionEvidence(status, [EvidenceItem])`. Each
  sub-signal appends a `.positive/.negative/.inconclusive/.untested` sub-status and, when positive, an
  `EvidenceItem`. `combine(...)` (`Synthesizer.swift:65`) rolls them up.
- **The section-inputs boundary.** `SectionInputs` (`Synthesizer.swift:23-57` /
  `Synthesizer.cs:13-43`) is the DTO the CLI/GUI populate before calling `Synthesizer.conclude`
  (`Synthesizer.swift:519` / `Synthesizer.cs:690`). B adds one field here.
- **The precedent to copy — A2 authoritative divergence.** `authoritativeDiverges` (strong,
  referral) vs `authoritativeDivergesConfirmed` (decisive, server-corroborated) is the *exact* two-tier
  shape B needs. See the emitter at `Synthesizer.swift:270-300`, the weights at
  `Conclusion.swift:263-266` and `:320-324`, and the decisive-catalog entry at
  `DecisiveActCatalog.swift:24`. B mirrors this precedent for transport divergence.
- **Answer-set equivalence already exists.** `Stage5Builder.classify(...)`
  (`Stage5Builder.swift:78-107` / `Stage5Builder.cs`) already implements *disjoint → discrepancy /
  equal → agreement / partial-overlap → benignSetVariation* over canonicalized answer sets
  (`canonicalAnswerSet` / `canonicalMerge`, `Stage5Builder.swift:127-133`, documented byte-identical
  across platforms). `IPClassifier.isBlockingAnswer(...)` (used at `Stage5Builder.swift:101`) flags
  block-page IPs. **B reuses these primitives verbatim** — it must not invent a raw byte compare.
- **The minimum-signal precedent — C009.** `ExfilAnalyzer.swift:75-89` (Windows
  `Forensics.cs:364`): "require at least two of {highEntropy, regular, flaggedBy}." A lone signal →
  insufficient. B mirrors this *inside the comparator* (both a Do53 answer AND a proven encrypted
  baseline must be present) and *in the engine* (the existing `belowMinCorroboration` gate,
  `SynthesisEngine.swift:369`, already forces referral when `positiveSupportGroups < 2`).
- **Cross-platform determinism.** The engine folds masses in ascending-key order and rounds through
  the single `roundPercent` primitive (`SynthesisEngine.swift:530`) so macOS and Windows are
  bit-identical. Any new canonicalization B adds must be byte-identical on both platforms (§5 golden
  vectors enforce this).

**Build/test.** macOS: `cd macOS && swift build && swift test`. Windows: `dotnet build
Windows/Syxx.sln && dotnet test Windows/Syxx.Tests`. Golden-vector harness:
`macOS/Tests/SyxxKitTests/Stage0GoldenCorpusTests.swift` + `Windows/Syxx.Tests/Stage0GoldenCorpusTests.cs`.

---

## 1. Design decision (recommended) + the alternative

**Recommendation — route B through the evidence-item path, two tiers, mirroring A2.**
Model transport divergence as **new `EvidenceId` cases** emitted by a new **`TransportCorroborator`**
comparator wired into `EvidenceAdapters.mirror`. This is preferred over reusing the answer-matrix
mass path (`SynthesisEngine.swift:270-280`) because:

1. The answer-matrix mass path weights by *number of corroborating public resolvers*
   (`referenceVantageIds.count`) — a **majority-vote** trust model. The encrypted baseline is trusted
   by **construction** (unforgeable transport), not by vote, so it needs its own weight rubric.
2. The provenance gate (proven + not-downgraded, else inconclusive) has **no equivalent** in the
   answer-matrix `classify()`. Putting it in a dedicated comparator keeps the gate explicit and
   testable.
3. The evidence-item path already gives B, for free: min-corroboration
   (`positiveSupportGroups < 2` → referral), correlation grouping / degraded-reliability discounting,
   and eligibility for the signed decisive-act catalog. **Zero change to the DST core** = lowest risk
   of a transient false-secure regression.

**Two tiers (mirroring A2):**
- `transportDivergence` — strong, **non-decisive**. One proven encrypted vantage diverges from Do53.
  Produces a tamper verdict that, as a lone signal, is forced to **referral**.
- `transportDivergenceConfirmed` — **decisive candidate**. The divergence has a *second independent
  leg* (a 2nd proven encrypted vantage that agrees with the 1st, **or** the Do53 answer is a known
  blocking IP, **or** the existing public-consensus answer matrix already flags the Do53 answer as the
  outlier). Only this tier is added to the signed `DecisiveActCatalog`.

**Alternative (documented, not recommended).** Extend the answer-matrix mass loop
(`SynthesisEngine.swift:270-280`) with transport rows. *Trade-off:* smaller diff, but it forces the
majority-vote weighting onto a construction-trust signal, has nowhere clean to put the provenance
gate, does not participate in `positiveSupportGroups` min-corroboration, and cannot be made decisive
without special-casing. Reject unless the executor finds the evidence-item path blocked.

---

## 2. Score / evidence contract (binding — DESIGN.md `:126` regimes)

`TransportCorroborator.classify(do53:encrypted:secondEncrypted:publicConsensusOutlier:)` returns a
tri-state `TransportCorroboration` with these regimes. **The provenance gate runs first and dominates
everything.**

| Regime | Trigger | Mirror sub-status | Evidence emitted | DST mass |
|---|---|---|---|---|
| **No-data / gated** | encrypted input `nil` (not attempted) | `.untested` (contributes nothing) | none | none |
| **No-data / gated** | encrypted `servedTransport == .unknown` **or** `wasDowngraded == true` **or** encrypted `didFail`/timeout/`.transportError` | **`.inconclusive`** (never `.negative`) | none | **none** (no clean, no tamper) |
| **No-data / gated** | Do53 input `nil` or `didFail` (no plaintext answer to compare) | `.inconclusive` | none | none |
| **Partial-data** | encrypted **proven** (`servedTransport ∈ {dot,doh3,doq}` **and** `!wasDowngraded`) **and** Do53 answered **and** answer sets **equal** (canonical) | `.negative` | none (clean corroboration is a *count-based* clean support via the negative sub-status) | clean support on Mirror's covered threats (existing path) |
| **Partial-data** | encrypted proven **and** answer sets **partial-overlap** (CDN geo / ECS / split-horizon) | `.negative` | **none** (benign — must stay silent) | **none** either direction |
| **Partial-data** | encrypted proven **and** answer sets **disjoint**, **one** encrypted leg only | `.positive` | `transportDivergence` | mass → `{spoofing, interception_onPath}`; if Do53 is NXDOMAIN/refused/blocking-IP while encrypted answers, also `{censorship}` |
| **Full-data** | encrypted proven **and** disjoint **and** a **second independent leg** confirms | `.positive` | `transportDivergenceConfirmed` | decisive mass; fires the M4 decisive gate (`SynthesisEngine.swift:146`) |

**Invariants this table enforces (map to DESIGN `:134-145`):**
1. *No score defaults to confident.* An unproven encrypted path emits `.inconclusive` — never
   `.negative`, so it can **never** add clean support (the load-bearing rule: a `.negative`
   sub-status from an unproven comparison would wash toward clean = false-secure).
2. *Missing data is not evidence.* `nil`/downgraded/unknown/failed → zero mass, both directions.
3. *Partial data → attenuated, bounded confidence.* A lone `transportDivergence` yields a tampered
   verdict but `belowMinCorroboration` (`SynthesisEngine.swift:369`, `positiveSupportGroups < 2`)
   forces the referral gate — bounded, gap visible.
4. *Answer-set equivalence, not byte compare.* Reuse `Stage5Builder.canonicalAnswerSet` /
   `canonicalMerge` + `IPClassifier.isBlockingAnswer`; partial overlap is benign and silent.
5. *Minimum corroboration (C009 analog).* The comparator requires **both** a valid Do53 answer and a
   **proven** encrypted baseline before emitting any tamper evidence; the decisive tier additionally
   requires a second independent leg.

---

## 3. Data model / method contracts to create

### 3.1 `TransportCorroboration` input type + `TransportCorroborator` comparator (new file)

- **macOS:** new `macOS/Sources/SyxxKit/TransportCorroborator.swift`.
- **Windows:** new `Windows/Syxx.Kit/TransportCorroborator.cs`.

```
public enum TransportCorroborationStatus { case matches, diverges, divergesConfirmed, inconclusive, untested }

public struct TransportCorroboration {          // the typed Mirror input
    let status: TransportCorroborationStatus
    let do53Answers: [String]                    // canonical set (may be empty on NXDOMAIN/block)
    let encryptedAnswers: [String]               // canonical set
    let encryptedTransport: DNSTransport         // proven servedTransport (dot/doh3/doq) — for the observation string
    let do53Blocked: Bool                        // IPClassifier.isBlockingAnswer over do53Answers
    let censorshipShape: Bool                    // Do53 NXDOMAIN/refused/blocked while encrypted answered
    let reason: String?                          // why inconclusive/untested (proof gate that failed), for the rationale
}
```

`TransportCorroborator.classify(do53: DNSResponse?, encrypted: DNSResponse?, secondEncrypted:
DNSResponse? = nil, publicConsensusOutlier: Bool = false) -> TransportCorroboration` implements §2's
table. It is **pure, offline, deterministic** (no I/O, no wall-clock) — it consumes two (or three)
already-resolved `DNSResponse` values that A's transport code produced.

**Proof gate (first, dominates):** encrypted is *proven* iff
`encrypted != nil && !encrypted.didFail && encrypted.servedTransport ∈ {.dot,.doh3,.doq} &&
!encrypted.wasDowngraded`. If not proven → `status = .inconclusive` (or `.untested` when
`encrypted == nil`), empty everything, `reason` set. **No further comparison runs.**

> **Boundary note for the executor.** The *actual dual-transport resolution* (running the query over
> Do53 and over DoT/DoH3/DoQ) is sub-project A/C transport-lane code that already exists after A lands;
> B does **not** re-implement resolution. B consumes the two `DNSResponse` values. The call that
> populates `inputs.transportCorroboration` lives at the probe-plan boundary (`ProbePlan.swift:167`
> area, alongside the existing `encryptedTransport` probe) — wire it there but keep the classifier and
> engine wiring in the neural lane.

### 3.2 New `EvidenceId` cases

- **macOS:** `Conclusion.swift:196` (after `authoritativeDivergesConfirmed`) — add
  `case transportDivergence` and `case transportDivergenceConfirmed`.
- **Windows:** `Conclusion.cs:132` — add `TransportDivergence,` and `TransportDivergenceConfirmed,`.

### 3.3 `WeightRubric` weight rows (mirror A2's magnitudes; see B020 note at `Conclusion.swift:267`)

- **macOS** `Conclusion.swift` (in the switch, near `:322`):
  - `(.transportDivergence, .interception_onPath) => 2.0`
  - `(.transportDivergence, .spoofing) => 2.0`
  - `(.transportDivergence, .censorship) => 1.5`  *(only meaningful when `censorshipShape`; emitter
    controls whether the censorship leg is present — see §3.5)*
  - `(.transportDivergenceConfirmed, .interception_onPath) => 3.0`
  - `(.transportDivergenceConfirmed, .spoofing) => 3.0`
  - `(.transportDivergenceConfirmed, .censorship) => 2.0`
- **Windows** `Conclusion.cs` (same switch): the C# tuple equivalents.

> **Anti-double-count / calibration guard.** Do **not** also emit an answer-matrix `.discrepancy` row
> for the same transport comparison — that path (`SynthesisEngine.swift:270-280`) would add a second
> `{spoofing, interception_onPath}` support and double the mass. Transport divergence contributes DST
> mass **only** via the evidence item. (Surfacing the encrypted vantage in Stage 5 is a presentation
> concern for `syxx-neural-surfacer`, out of B's engine scope; if added later it must not re-emit
> mass.)

### 3.4 Correlation group

The transport-divergence signal is the same "no protected transport reaching the wire" family as the
existing `transport_downgrade` group (`dnsUnencrypted`/`encryptedDnsBlocked`/`svcbStripped`). But a
*proven-encrypted-vs-poisoned-Do53 divergence* is a **stronger, independent** observation than "the
encrypted path was blocked." Keep it **independent** (`nil` correlation group → treated as independent
corroboration) so a real divergence is not merged-and-capped against a weaker downgrade hint. Decision:
leave `transportDivergence`/`transportDivergenceConfirmed` **out** of the `correlationGroup` map
(`Conclusion.swift:362-373` / `Conclusion.cs:281-290`) — no edit needed there. *(Flag for calibration:
if a future corpus shows the downgrade hint and the divergence co-firing and over-counting, revisit.)*

### 3.5 `EvidenceAdapters.mirror` emitter

- **macOS:** `Synthesizer.swift:352` (before the closing `return SectionEvidence(...)`).
- **Windows:** end of `EvidenceAdapters.Mirror` (`Synthesizer.cs:308+`).

```
if let tc = transportCorroboration {
    switch tc.status {
    case .divergesConfirmed:
        ev.append(EvidenceItem(section: .mirror, evidenceId: .transportDivergenceConfirmed,
            observation: "Plaintext DNS answer diverges from a proven \(tc.encryptedTransport) answer — CONFIRMED by a second independent vantage",
            rationale: "The encrypted transport cannot be transparently rewritten on-path; a corroborated divergence is near-proof of plaintext DNS interception/spoofing."))
        subs.append(.positive)
    case .diverges:
        ev.append(EvidenceItem(section: .mirror, evidenceId: .transportDivergence,
            observation: "Plaintext DNS answer diverges from a proven \(tc.encryptedTransport) answer",
            rationale: "The encrypted transport is the trustworthy baseline; a divergent plaintext answer indicates on-path rewriting — corroborate."))
        subs.append(.positive)
    case .matches:       subs.append(.negative)     // clean corroboration → covered-threat clean support
    case .inconclusive:  subs.append(.inconclusive) // NEVER .negative — unproven path must not wash toward clean
    case .untested:      break
    }
}
```

**Censorship leg.** Emit the `censorship`-weighted reading only when `tc.censorshipShape` is true. The
simplest honest implementation: keep one `EvidenceId` but let the weight rubric carry the censorship
weight, and have the *classifier* set `censorshipShape` only when Do53 is NXDOMAIN/refused/blocking
while encrypted answered — then the emitter chooses observation wording. Because `WeightRubric` is
static per `EvidenceId`, the cleanest split is a **third case `transportBlockedVsEncrypted`** if the
executor wants the censorship weight gated by shape rather than always-on. *Recommended:* start with
the two ids above and the censorship weight always present (it only matters when Do53 fails, and a
disjoint-answer divergence rarely also looks like censorship); add the third id only if a golden vector
shows the always-on censorship weight misclassifying. Note this as a calibration open item.

### 3.6 `SectionInputs` field + `Synthesizer.conclude` wiring

- **macOS** `Synthesizer.swift:46` (after `serverGroundTruth`): `public var transportCorroboration:
  TransportCorroboration?   // Mirror · datapoint A1/B` — then pass it into the `.mirror(...)` call at
  `Synthesizer.swift:530-533`.
- **Windows** `Synthesizer.cs:39` (after `Authoritative`) + the `EvidenceAdapters.Mirror(...)` call at
  `Synthesizer.cs:695`.
- Extend the `mirror(...)` / `Mirror(...)` signatures (`Synthesizer.swift:240-245` / `:308-309`) with
  a defaulted `transportCorroboration: TransportCorroboration? = nil` parameter (additive, so no other
  call site breaks).

### 3.7 Decisive-act catalog (tier 2 only — gated, §4 approval)

- **macOS** `DecisiveActCatalog.swift:15-33` — add `.transportDivergenceConfirmed` to `acts`, bump
  `currentVersion` `1 → 2` (`:11`).
- **Windows** `Windows/Syxx.Kit/Synthesis/DecisiveActCatalog.cs:23` area + version bump — must match
  the macOS set and version **exactly** (the catalog is `WitnessSigner`-signed for reproducibility;
  a mismatch breaks parity).
- **Do NOT** add `transportDivergence` (tier 1) to the catalog — only the corroborated tier is
  decisive, preserving the C009 minimum-corroboration guarantee.

---

## 4. Sequencing (never transiently false-secure)

Each step is one reviewable commit **per platform**, landed macOS+Windows together (parity). The
capacity for a trustworthy divergence signal comes online at **B3**; nothing before it can regress
honesty because the signal is inert until wired.

- **B0 — Dependency gate (verify, no code).** Confirm A0 (shared tri-state
  `SecureResolutionResponse`) and A1 (`requestedTransport`/`servedTransport?`/`negotiatedProtocol?`/
  `DNSTransport`/`wasDowngraded` on `DNSResponse` at `DNSResolver.swift:111-190` and the Windows twin)
  have landed and pass a real run. **Effort:** S. **Blocker if absent** — do not proceed.

- **B1 — `TransportCorroborator` + `TransportCorroboration` (both platforms), isolated golden
  vectors, NO engine wiring.** Pure classifier + input type. Engine untouched → cannot regress
  honesty. Land with unit + golden fixtures for every §2 regime including all FAILURE cases (§5).
  **Effort:** M. **Parity:** classifier logic + canonicalization byte-identical.

- **B2 — `EvidenceId` cases + `WeightRubric` rows + signatures (dormant).** Add the enum cases
  (§3.2), weight rows (§3.3), and the defaulted adapter parameter (§3.6 signature only). No emitter
  yet → no `EvidenceItem` is ever produced → zero behavior change. **Effort:** S. **Parity:** enum
  order and weights identical.

- **B3 — Wire the emitter + `SectionInputs` field + `conclude` call site.** This turns the signal on.
  Land the emitter (§3.5) and the field/call-site (§3.6) in one commit per platform. **Honesty crux
  of the whole sub-project:** the `.inconclusive` branch must map to the `.inconclusive` sub-status,
  **never `.negative`** — an unproven transport comparison must not add clean support. Ship with the
  engine-level golden vectors + never-false-secure corpus seeds (§5). **Effort:** M. **Parity:**
  adapter switch identical.

- **B4 — Decisive-catalog promotion (GATED, §3.7).** Add `transportDivergenceConfirmed` to the signed
  catalog + version bump, both platforms. **Approval gate:** this changes verdict-forcing behavior
  (a corroborated divergence forces `tampered` regardless of clean sections) and requires re-signing
  the catalog; land only after B3 is proven on a real run and the "confirmed" predicate is verified to
  genuinely require a 2nd independent leg. Safe to land alone (only strengthens an already-correct
  signal), but do not land before B3. **Effort:** S.

- **B5 — Real-run acceptance (both platforms).** Execute the §6 real runs and record outputs in
  `DELTAS.md`/`ROADMAP.md`. A step is not `[done]` on green tests alone. **Effort:** M.

**Dependency graph:** `A0,A1 → B0 → B1 → B2 → B3 → {B4 [gated], B5}`. B4 and B5 both depend on B3;
B4 additionally needs approval. No step depends on a later step.

---

## 5. Golden-vector parity fixtures (macOS + Windows byte-identical)

Add to `Stage0GoldenCorpusTests.swift` + `Stage0GoldenCorpusTests.cs` (engine-boundary seeds) and, for
the classifier, dedicated `TransportCorroboratorTests`. Every seed asserts the **universal
never-false-secure invariant**. Failure cases are first-class, not afterthoughts.

**Classifier-level (B1):**
- `TC-agree` — encrypted proven (`.dot`, `!wasDowngraded`), Do53 == encrypted → `.matches`.
- `TC-diverge` — encrypted proven, Do53 disjoint (block IP) → `.diverges`.
- `TC-confirmed` — encrypted proven + 2nd encrypted agrees, Do53 disjoint → `.divergesConfirmed`.
- `TC-benign` — encrypted proven, partial overlap (CDN/ECS) → `.matches` sub-path, **no divergence**.
- **`TC-FAIL-unknown`** — encrypted `servedTransport == .unknown` + Do53 disjoint → **`.inconclusive`**
  (NOT diverges).
- **`TC-FAIL-downgraded`** — encrypted `wasDowngraded == true` (h3→h2 / DoT→Do53) + Do53 disjoint →
  **`.inconclusive`**.
- **`TC-FAIL-encfail`** — encrypted `.timeout`/`.transportError` → **`.inconclusive`** (missing
  encrypted probe is inconclusive, not clean).
- **`TC-FAIL-noencrypted`** — encrypted `nil` → **`.untested`** (contributes nothing; does not
  fabricate clean).
- **`TC-FAIL-nodo53`** — Do53 `nil`/`didFail` → **`.inconclusive`** (no plaintext to compare).

**Engine-boundary (B3 / B4):**
- `NE-transport-lone` — one `transportDivergence`, all other sections `.negative` → verdict
  `interception_onPath` **with forced referral** (`positiveSupportGroups < 2`). Confidence bounded.
- `NE-transport-confirmed` (B4) — one `transportDivergenceConfirmed`, other sections `.negative` →
  decisive `tampered`, **no referral** required (M4 gate). Confirms the decisive path parallels
  `authoritativeDivergesConfirmed`.
- **`NE-transport-inconclusive-notclean`** — Mirror transport sub-status `.inconclusive`, other
  sections `.negative` → verdict must be `inconclusive`+referral (hard-gate on non-conclusive critical
  section, `SynthesisEngine.swift:140`), **never `clean`**. This is the anti-false-secure seed.
- **`NE-transport-benign-notamper`** — benign partial-overlap → no evidence item → verdict is
  whatever the other sections say, with **no `transportDivergence`** present (no false tamper).

**Determinism assertion.** Each engine seed asserts identical verdict, confidence (`roundPercent`),
referral, and per-hypothesis distribution across macOS and Windows (the existing
`Stage5PopulatedParityTests` / `RoundingParityTests` pattern).

---

## 6. Real-run acceptance bar (per platform — the actual DoD)

Observe the **honest output of a real run**, not a green suite. `swift test` / `dotnet test` are the
floor. Record commands + outputs in `DELTAS.md`/`ROADMAP.md`.

1. **Poisoned Do53, clean encrypted → tamper signal.** On a network with transparent Do53
   rewriting (or a lab on-path rewriter that answers UDP/53 with a block IP while DoT/DoH3 to
   `1.1.1.1:853` / `cloudflare-dns.com` resolves normally), run the dual-transport query and
   synthesize. **Honest output:** `servedTransport ∈ {dot,doh3}` proven, `!wasDowngraded`, a
   `transportDivergence` evidence item, verdict `interception_onPath`/`spoofing` **with referral**
   (lone signal). Add a 2nd proven encrypted vantage → `transportDivergenceConfirmed`, decisive
   `tampered`, no forced referral.
2. **Clean network → no false tamper.** On a normal network where Do53 and the encrypted path return
   equivalent answers (allow CDN geo-variance / ECS partial overlap), synthesize. **Honest output:**
   no `transportDivergence`; Mirror transport sub-status `.negative`; verdict not tampered on account
   of transport; partial-overlap does **not** read as divergence.
3. **Downgraded / unknown transport → inconclusive.** Force a downgrade (Alt-Svc-stripped host holding
   DoH3 on h2, or a blocked :853 that falls back) so `wasDowngraded == true` or `servedTransport ==
   .unknown`, with Do53 answering. **Honest output:** Mirror transport sub-status `.inconclusive`
   (never `.negative`), **no** `transportDivergence`, and the run does **not** render `clean` on the
   strength of the transport comparison.

---

## 7. Risks & open questions

- **[BLOCKER] A0/A1 must land first.** B0 gates on the live `DNSResponse` provenance fields and the
  `DNSTransport` enum. If A1 ships `servedTransport` as a non-optional or omits `wasDowngraded`, the
  proof gate in §3.1 must be revised. Verify the exact A1 field names/optionality against the tree at
  B0 — this plan assumes A1 as specified in `ENCRYPTED-TRANSPORTS-PLAN.md:167-173`.
- **Censorship-leg modeling (calibration).** §3.5 leaves open whether the `censorship` weight is
  always-on for `transportDivergence` or gated behind a third `EvidenceId`
  (`transportBlockedVsEncrypted`). Resolve with a golden vector; do not guess. Flagged for the
  Phase-7 calibration pass.
- **Decisive predicate rigor (honesty).** B4's "confirmed" tier is decisive and bypasses the
  `positiveSupportGroups < 2` referral. It is only honest if `divergesConfirmed` genuinely requires a
  **second independent leg** (2nd proven encrypted vantage, blocking-IP Do53, or public-consensus
  outlier). The executor must assert this in `TC-confirmed` and must not let a lone single-vantage
  divergence reach the confirmed tier.
- **Double-count guard.** If a later surfacing task adds the encrypted transport as an answer-matrix
  vantage row, it must not classify as `.discrepancy` (that path emits mass at
  `SynthesisEngine.swift:270-280`). B keeps transport mass in the evidence item only. Leave a comment
  at the mass loop noting the reservation.
- **Approval gate — user-visible wording.** The observation/rationale strings in §3.5, the referral
  copy, and any Stage-5 transport labels are user-facing. Per `ENCRYPTED-TRANSPORTS-PLAN.md:92-95` and
  `AGENTS.md`, they are **proposals pending approval** and must be logged to `outputs-catalog.csv`
  before finalizing in code. The strings above are drafts.
- **Windows weight-switch line numbers.** `Conclusion.cs` weight rows were located by symbol
  (`DnssecCanaryStripped` tuples at `:180-181`); confirm the exact insertion point by grepping the
  switch at B2 (line numbers drift).

---

## 8. Handoff

- **`syxx-neural-executor`** — implements B1–B5 across `macOS/Sources/SyxxKit/` and
  `Windows/Syxx.Kit/` per §3–§6, with golden fixtures and the real-run bar. Reports any structural
  conflict (e.g. A1 field shape differs) immediately rather than adapting silently.
- **Transport lane (`syxx-engineer` / sub-project A/C)** — owns the dual-transport resolution and the
  `ProbePlan` call that populates `inputs.transportCorroboration`; B consumes its `DNSResponse`s.
- **`syxx-neural-surfacer`** — later, optionally renders the encrypted vantage in the Stage-5 answer
  matrix (presentation only, no mass), and the referral/tamper copy once wording is approved.
- **User approval** — required for B4 (decisive-catalog + re-sign) and all user-visible wording
  (`outputs-catalog.csv`).
