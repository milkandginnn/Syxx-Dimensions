# Stage-5 surfacing lane — user-visible WORDING PROPOSALS

> **STATUS: ✅ APPROVED — recommended set, 2026-07-12 (user).** Every item's "(recommended)" option below is now approved copy; **item 5 = Framing 3** (→ Framing 1 relabel fallback in tight/CLI space). The surfacing lane resolves each `CopyToken` to its recommended option; any non-recommended option is NOT approved and stays fail-closed to the approved generic inconclusive/unavailable string. (Original proposal text retained below for provenance.)
> Nothing here is final copy. The Stage-5 surfacing lane is copy-gated: these strings are held
> out of the engine DTOs, SwiftUI, XAML/code-behind, CLI, report text, and accessibility labels
> until the user signs off (per `STAGE5-CONTRACT.md` §10 and `REASONING-SYSTEM-DESIGN.md` §11 handoff).
> Every item below is a **labelled option the user can approve or edit**. Approve by option letter,
> or paste an edit. Once approved, the strings land in one platform-parity copy table
> (macOS SwiftUI, Windows XAML, CLI, reports) keyed by the `CopyToken` identifiers shown.

**Author:** syxx-gui-architect · **Date:** 2026-07-12
**Grounded in:** `STAGE5-CONTRACT.md` (the DTO fields being labelled), `REASONING-SYSTEM-DESIGN.md`
§6.4 (mechanism-class partition), §7 (explainability / three honesty lines), §3 M4 + MINOR-4
(forced-verdict / low-confidence tension), §10 (copy tokens). Voice matched to the live CLI at
`macOS/Sources/SyxxCLI/SyxxCLI.swift` and the verdict view at
`macOS/Sources/SyxxApp/Views/Components/OverallVerdictView.swift`.

---

## Verification & Honesty Protocol — the constraints every option below respects

These are stated up front so the reviewer can hold each proposal to them:

1. **No false reassurance.** No option renders an inconclusive or untested state as green / ✅ / "secure" / "clean."
2. **No "secure"/"clean" before evidence.** A `clean` headline always carries an explicit scope qualifier and is only reachable when the engine's `DeterminationState == .clean` (which the contract forbids under a hard gate, missing critical data, a decisive finding, or unresolved tamper — `STAGE5-CONTRACT.md` §13 rule 9).
3. **Never soften an inconclusive.** Inconclusive reads as *undecided / not verified*, never as "probably fine."
4. **Absent ≠ zero.** A `nil` confidence (`CredalMeasure? == nil`) renders as "no estimate," never "0%" (`STAGE5-CONTRACT.md` §7 rule "No-data confidence is `nil`, never `0`").
5. **Completeness is not confidence.** The K/M coverage figure is lexically and visually separated from the belief number, and is never rendered as a bare percentage that could be misread as confidence (item 6).
6. **The belief number is never altered to hide tension.** For the forced-verdict / low-confidence case (item 5), `Bel` stays true and unedited; only the *framing* around it changes (`REASONING-SYSTEM-DESIGN.md` §3 M4, R3).

**CLI glyph convention** (already established in the tree — kept for parity): 🚨 = tampering / critical, ⚠️ = caution / inconclusive / referral, 🟢 = clean/affirmative state, 🔴 = negative/off, ✅ reserved for a verified per-check MATCH, 🔮 = Prophet. **The cardinal rule holds: 🟢/✅ never appear next to an inconclusive or untested state.**

---

## 1. The mechanism-class verdict headlines

Rendered when `MechanismDetermination.state == .tampered` and `mechanismClass ==` the class shown. These are the **headline** — the first thing the user reads. The confidence number and the within-class split (item 2) are subordinate. Each headline names the *harm in plain English*, not the internal class token. Proposed token stem: `stage5.headline.<class>`.

> The mechanism classes map to `REASONING-SYSTEM-DESIGN.md` §6.4:
> REWRITE = {spoofing, interception_onPath} · BLOCK = {censorship} · TRANSPORT = {interception_transport} ·
> CONTENT = {contentTampering} · LEAK = {privacyLeak} · HOST = {localCompromise} · plus `unresolved`.

### 1a. REWRITE — `stage5.headline.rewrite`

The answer you were given was changed; you may be pointed at the wrong server.

- **Option A (recommended):** "The address you were given was changed"
- **Option B:** "Your DNS answers are being rewritten"
- **Option C:** "You're being sent to a different address than the real one"

*Rationale:* A leads with the concrete harm (a wrong address) in words a non-expert reads correctly; B is closest to the class name but "rewritten" is mild jargon; C is the most explicit about consequence.

### 1b. BLOCK — `stage5.headline.block`

A domain is being withheld / blocked where independent vantages can reach it.

- **Option A (recommended):** "This domain is being blocked"
- **Option B:** "Access to this domain is being withheld"
- **Option C:** "A block is stopping this domain from resolving"

### 1c. TRANSPORT — `stage5.headline.transport`

The DNS channel itself is being intercepted in transit (e.g. a transparent proxy or a downgrade), independent of whether any single answer was changed.

- **Option A (recommended):** "Your DNS traffic is being intercepted in transit"
- **Option B:** "Something is sitting between you and your resolver"
- **Option C:** "Your DNS queries are being routed through an intermediary"

### 1d. CONTENT — `stage5.headline.content`

What was served over the connection differs from an independently fetched copy — injection/modification.

- **Option A (recommended):** "The content was altered in transit"
- **Option B:** "Content injection detected on this connection"
- **Option C:** "What you received doesn't match the original"

### 1e. LEAK — `stage5.headline.leak`

DNS queries are escaping to a resolver/observer they should not reach (exposure, not manipulation — keep the wording honest that this is a *privacy* finding).

- **Option A (recommended):** "Your DNS queries are leaking"
- **Option B:** "Your browsing is visible to a resolver it shouldn't reach"
- **Option C:** "Your DNS is escaping the private channel you expected"

*Note:* LEAK is an exposure finding, not answer-tampering. None of these should imply your answers were changed — only that your queries were exposed.

### 1f. HOST — `stage5.headline.host`

The tampering was traced to this device, not the upstream network (hosts file, local resolver, installed software).

- **Option A (recommended):** "The tampering is on this device"
- **Option B:** "Something on this computer is changing DNS"
- **Option C:** "This device — not the network — is altering your DNS"

*Rationale:* A/C both make the key point a user must act on: the fix is local, not the network.

### 1g. UNRESOLVED — `stage5.headline.unresolved`

`state == .tampered`, `mechanismClass == .unresolved`: the threat union cleared but **no** single mechanism-class set qualified. The contract forbids naming a single threat here (`STAGE5-CONTRACT.md` §7 rule; §13 rule 11). Pairs with a referral (item 4).

- **Option A (recommended):** "Interference detected — we can't yet say how"
- **Option B:** "Something is interfering, but the mechanism is unresolved"
- **Option C:** "Tampering is present; its exact method isn't determined"

### 1h. Framing context — the non-tampered states (for reference; not a "mechanism class")

Included so the six labels above are read against the correct state frame. These are separate DTO states (`DeterminationState.clean` / `.inconclusive`).

- **`clean` headline** (`stage5.headline.clean`) — never bare "Secure":
  - **Option A (recommended):** "No tampering detected in the checks that ran"
  - **Option B:** "Clean — no tampering found across the checks that completed"
  - The scope qualifier is mandatory; it mirrors the live CLI's honest "(A-record comparison only)" pattern at `SyxxCLI.swift:324`.
- **`inconclusive` headline** (`stage5.headline.inconclusive`) — never green:
  - **Option A (recommended):** "Inconclusive — not enough evidence to decide"
  - **Option B:** "Undetermined — the run couldn't establish a verdict"

---

## 2. Within-class secondary phrasing (the `withinClass` split)

Rendered from `MechanismDetermination.withinClass` — subordinate to the headline, never the headline itself (`STAGE5-CONTRACT.md` §7: "`withinClass` is subordinate… not headline confidence"). The optional `share` is a *display* distribution, not confidence. Proposed token stem: `stage5.within.<hypothesis>`. Lead-in token: `stage5.within.lead`.

**Lead-in line** (introduces the split without implying we're unsure it's a threat):

- **Option A (recommended):** "Most likely one of:"
- **Option B:** "The method is one of the following — both are tampering:"
- **Option C:** "Which of these it is (either way, it's interference):"

**REWRITE members** — {spoofing, interception_onPath}:

- **spoofing** → **Option A (recommended):** "Your resolver is returning a forged answer" · **Option B:** "The resolver itself is lying"
- **interception_onPath** → **Option A (recommended):** "Something on the path is rewriting answers" · **Option B:** "An on-path interceptor is altering replies"

**Example rendered block (REWRITE, recommended options):**
> **The address you were given was changed**
> Most likely one of:
> · Your resolver is returning a forged answer
> · Something on the path is rewriting answers

*Rationale:* The lead-in must not let the two-way split read as "we're not sure it's real." Both members are tampering; the ambiguity is *which method*, not *whether*. This directly serves the §6.4 rule that the spoofing-vs-interception split is a within-class distribution, not a threat-vs-clean contest.

*(If the reviewer approves the pattern, the same shape applies to any future multi-member class. Single-member classes — BLOCK/TRANSPORT/CONTENT/LEAK/HOST — render no within-class block.)*

---

## 3. The three honesty lines

These are the three **distinct** signals from `HonestySignals` (`STAGE5-CONTRACT.md` §6). They are independent — any combination may appear. The typed arguments (section names, N%) are interpolated by the renderer from the DTO; the copy is the template.

### 3a. Hard gate — `stage5.hard_gate.required_checks_missing`

Present when `honesty.hardGate != nil`. Section names come from `hardGate.requiredSections` (typed — the UI must **not** hardcode "Mirror/Glutton" per §6 rule and §14 CX-2 test). Blocked verdict(s) come from `hardGate.blockedVerdicts`.

- **Option A (recommended):** "A clean result is impossible until {sections} run — required checks did not complete."
- **Option B:** "We can't clear this as clean: the required {sections} checks didn't run."
- **Option C:** "{sections} did not run. A clean verdict is off the table until they do."

*Rationale:* All three refuse to let "clean" be reachable, matching the design's own phrasing at §7 ("A clean verdict is impossible until Mirror and Glutton run"). "{sections}" is a joined list from `requiredSections`.

### 3b. Conflict — `stage5.conflict.evidence_disagrees`

Present **only** when `honesty.conflict != nil` (genuine, user-relevant contradiction; benign context must not fabricate it — §6 rule, §14 CX-2 test). `N = round(conflict.value * 100)`.

- **Option A (recommended):** "The evidence genuinely disagrees ({N}%) — different checks point to different conclusions."
- **Option B:** "Different checks contradict each other ({N}% conflict)."
- **Option C:** "The sections genuinely disagree — {N}%."

*Rationale:* C matches the design's literal §7 wording; A/B add a clause explaining what "conflict %" means so the number isn't mistaken for confidence.

### 3c. Uncertainty — `stage5.uncertainty.evidence_thin`

Present **only** when `honesty.uncertainty != nil` (raw credal `m(Θ)`; it is **not** completeness — §6 rule). `N = round(uncertainty.value * 100)`.

- **Option A (recommended):** "Evidence is thin ({N}%) — there isn't enough to be sure either way."
- **Option B:** "Thin evidence ({N}% unresolved) — not enough was observed to settle this."
- **Option C:** "Evidence is thin — {N}%."

*Rationale:* The "either way" clause in A blocks the dangerous misread that thin evidence means "probably clean." C matches the design's literal §7 wording but is safe only because the surrounding verdict already reads inconclusive.

---

## 4. The referral text (defer-to-human)

Rendered when `MechanismDetermination.referralRequired == true`, keyed by `referralToken`. Referral can fire for several distinct reasons (`REASONING-SYSTEM-DESIGN.md` I7: low confidence, narrow between-class margin, genuine conflict, hard-gated/incomplete critical section, or inconclusive). Two layers are proposed: a **general referral block** (shown for any referral) and the **unresolved-mechanism-specific** line.

### 4a. General referral block — `stage5.referral.general`

- **Option A (recommended):**
  > "This is at the edge of what automated checks can decide. Get a second opinion from a network expert before you act on it."
- **Option B:**
  > "Automated diagnostics can't settle this one on their own. We recommend a human second opinion before drawing conclusions."
- **Option C** (closest to the live copy at `OverallVerdictView.swift:260`):
  > "Due to the limits of automated diagnostic checks, we strongly recommend a human second opinion from network experts."

### 4b. Unresolved-mechanism referral — `stage5.referral.mechanism_unresolved`

Pairs with headline 1g (tampered + unresolved).

- **Option A (recommended):** "Something is interfering, but the checks that ran can't pin down how. Have a network expert review the evidence below."
- **Option B:** "Interference is present; its mechanism is unresolved. This warrants a human review of the captured evidence."

*Rationale:* Both keep the tampered fact firm (per §6.4, we do not soften into "maybe nothing") while honestly deferring on the *mechanism*. Neither invites the user to relax.

---

## 5. THE LOAD-BEARING ONE — the M4 pair (forced verdict + low confidence)

**The problem.** M4 floors the *verdict* at tampered/referral without editing the *belief number* (`REASONING-SYSTEM-DESIGN.md` §3 M4 / MINOR-4; `STAGE5-CONTRACT.md` §7: "A decisive safety gate may make `state == .tampered` while the credal measure is low or absent"). So a real, verified decisive act can present as **"Tampered — 20%"**, which reads self-contradictory to a user ("if it's only 20%, why does it say tampered?"). The belief number is **never** altered to hide this (R3, §13). The renderer detects this case by `MechanismDetermination.basis == .decisiveSafetyGate` (or `.hardGate` with a decisive finding) and `confidence.value` low/`nil`. Token: `stage5.basis.decisive_safety_gate`.

The task is to make the pair read **coherent, not contradictory.** Three distinct framings follow.

### Framing 1 — Relabel the number: it's not "chance this is real"

Keep the number visible and unedited, but **never call it "Confidence"** in this layout. Label it as what it actually is: the strength of *corroborating* belief accumulated so far. Lead with the verified act.

- **Headline:** the mechanism-class headline (item 1), unqualified — the verdict IS tampered.
- **Number label option A (recommended):** "Corroboration so far" — e.g. "Corroboration so far: 20%"
- **Number label option B:** "Belief strength: 20%"
- **Explainer (`stage5.basis.decisive_safety_gate`):**
  > "This verdict is forced by a verified, decisive signal — {act}. The {N}% is how much *additional independent evidence* has corroborated it so far, not the chance the tampering is real."

*Trade-off:* Keeps the number (users who want it get it) and resolves the contradiction by fixing the *label*, which was the real defect. Risk: a single relabelled number can still be misread on a fast scan if the explainer is collapsed.

### Framing 2 — Lead with the basis, demote the number

Replace the headline number with a **qualitative basis chip** and move the raw belief into a clearly-labelled detail row.

- **Headline:** mechanism-class headline (item 1).
- **Basis chip (in place of a % gauge):**
  - **Option A (recommended):** "Flagged by a verified signal"
  - **Option B:** "Decisive signal — verdict forced"
- **Detail row (below the fold / secondary):** "Corroborating belief: {N}% · Basis: 1 verified decisive signal ({act})"

*Trade-off:* Cleanest quick-scan — no naked "20%" next to "Tampered" at the headline. Risk: demoting the number could be read as *hiding* weak evidence, which is its own honesty failure (constraint 5/6). Mitigated only if the number remains one glance away and is never removed.

### Framing 3 — Two explicitly-named figures, never merged

Show the verdict and **two** figures side by side, each labelled so neither can be mistaken for the other, plus the basis.

- **Verdict:** "Tampered" + "(safety gate)" tag.
- **Figure 1 — Basis:** "Basis: 1 verified decisive signal"
- **Figure 2 — Corroboration:** "Corroborating belief: {N}%"
- **One-line explainer (`stage5.basis.decisive_safety_gate`):**
  > "A verified decisive signal forces this verdict regardless of the belief number. The {N}% reflects how much *other* evidence has piled on so far — a low number here means 'one strong signal, not yet corroborated,' not 'probably fine.'"

*Trade-off:* Most honest and most scannable — the low number is explicitly reframed as "one strong signal, not yet corroborated." Cost: it's the most layout/space-heavy, and needs a compact CLI rendering.

### Recommendation

**Framing 3, with Framing 1's relabel as the fallback where space is tight (CLI, narrow panels).**
Reasoning: Framing 3 keeps the true number visible (satisfying R3 and constraint 6), never labels it "confidence," and — critically — *names why a low number is not reassurance* ("one strong signal, not yet corroborated"). That last clause is the load-bearing sentence: it converts the apparent contradiction into a correct mental model. Framing 2's demotion risks reading as concealment; Framing 1 alone leaves a single number that can still be misread on a collapsed view. Framing 3 degrades gracefully to Framing 1 when there's no room for two figures.

**Proposed CLI rendering (Framing 3 → 1 fallback), for review:**
>
> ```
> 🚨 TAMPERED — the address you were given was changed   (safety gate)
>    Basis           : 1 verified decisive signal (authoritative divergence, signed)
>    Corroboration   : 20%  — one strong signal, not yet corroborated (not "20% likely")
> ```

---

## 6. Direct / inferred / unavailable headers + confidence + completeness

### 6a. The three provenance section headers (`ProvenancePartition`)

From `STAGE5-CONTRACT.md` §4 / `REASONING-SYSTEM-DESIGN.md` §7. Proposed token stem: `stage5.provenance.<part>.header`.

| Part | Option A (recommended) | Option B | Option C |
|---|---|---|---|
| DIRECT (`stage5.provenance.direct.header`) | "What we observed directly" | "Observed directly" | "Direct observations" |
| INFERRED (`stage5.provenance.inferred.header`) | "What we concluded from it" | "Inferred from the evidence" | "What this points to" |
| UNAVAILABLE (`stage5.provenance.unavailable.header`) | "What we couldn't check" | "Not checked / unavailable" | "Gaps in this run" |

*Rationale:* Option A reads as a plain-English narrative (observed → concluded → couldn't check) that a non-expert follows top-to-bottom; B/C are terser for dense/CLI layouts.

**Unavailable reason sub-phrasings** (`UnavailableReason` enum → `reasonToken`, one per case). Each must read as a *gap*, never as a pass. Proposed stem `stage5.unavailable.<reason>`:

| Reason | Proposed phrasing (recommended) |
|---|---|
| `notRequested` | "not requested in this run" |
| `untested` | "did not run" |
| `timedOut` | "timed out — no result" |
| `invalidated` | "ran but the result was invalidated" |
| `parseFailed` | "response could not be parsed" |
| `probeFailed` | "the probe failed to complete" |
| `unauthenticated` | "could not be authenticated" |
| `stale` | "only stale/cached data was available" |
| `permissionDenied` | "blocked — insufficient permission" |
| `dependencyUnavailable` | "a required dependency was unavailable" |

*(These follow the live CLI's honest gap wording, e.g. "Cage stays UNTESTED (this is NOT a clean result)" at `SyxxCLI.swift:551`.)*

### 6b. Confidence phrasing (`MechanismDetermination.confidence: CredalMeasure?`)

The single credal number, `Bel` of the named class-set (§6.4 I6). Three cases:

- **Ordinary case** (`basis == .ordinaryCredalGate`, `confidence != nil`) — `stage5.confidence.label`:
  - **Option A (recommended):** "Confidence" (with value `{N}%`)
  - **Option B:** "Belief in this verdict" (`{N}%`)
  - **Option C:** "Evidence strength" (`{N}%`)
- **Decisive-gate case** (`basis == .decisiveSafetyGate`): **do not use "Confidence"** — use item 5's relabel ("Corroboration" / "Corroborating belief").
- **No estimate** (`confidence == nil`, e.g. no-data regime — §8) — `stage5.confidence.none`:
  - **Option A (recommended):** "No estimate — not enough evidence"
  - **Option B:** "Confidence: not available (insufficient evidence)"
  - **Hard rule:** never "0%". This is the difference between "no estimate" and "0% sure something is wrong" (`REASONING-SYSTEM-DESIGN.md` §5.1).

### 6c. Completeness (K/M) phrasing (`EvidenceCompleteness`) — must NEVER read as confidence

From `STAGE5-CONTRACT.md` §8. `K = completeness.observed`-style conclusive count, `M = expected`. This is a **separate reported dimension** (`REASONING-SYSTEM-DESIGN.md` §3 M3 / R2), not a multiplier on `Bel`. To keep it from being misread as confidence, the recommendation is to render it as a **fraction with a coverage label, not a bare percentage**.

- **Label — `stage5.completeness.label`:**
  - **Option A (recommended):** "Evidence completeness"
  - **Option B:** "Checks completed"
  - **Option C:** "Run coverage"
- **Value — `stage5.completeness.value`:**
  - **Option A (recommended):** "{K} of {M} checks reached a conclusion"
  - **Option B:** "{K}/{M} required checks conclusive"
  - **Option C** (only if the reviewer wants a percent): "{K} of {M} ({pct}%) — coverage, not confidence"
- **Critical-gap note** (when a *critical* section is among the unavailable — ties to the hard gate 3a) — `stage5.completeness.critical_gap`:
  - **Option A (recommended):** "A critical check ({sections}) is missing — this alone prevents a clean result."

*Rationale:* Rendering completeness as "4 of 6 checks reached a conclusion" (fraction + "checks") makes it structurally impossible to confuse with the "Confidence: 72%" figure; a bare "67%" next to a "72%" confidence number is exactly the confusion constraint 5 forbids. If the reviewer insists on a percentage, Option C forces the disambiguating tail "coverage, not confidence."

**Example combined footer (recommended options), for review:**
>
> ```
> Confidence            : 72%
> Evidence completeness : 4 of 6 checks reached a conclusion
>                         (Glutton, Cage did not run)
> ```
>
> The two figures never share a format, so completeness can't be read as confidence.

---

## Approval

**✅ APPROVED (2026-07-12) — the recommended set:** every item's "(recommended)" option, and **item 5 = Framing 3** (degrading to Framing 1 in tight/CLI space). The surfacing lane will:

1. land the strings in one platform-parity copy table keyed by these `CopyToken`s;
2. wire macOS (`OverallVerdictView.swift`, `ConclusionsView.swift`), Windows (`ConclusionsView.xaml(.cs)`), and CLI (`SyxxCLI.swift`, `Program.cs`) to resolve tokens through that table (unknown token → approved generic inconclusive message, per §10 fail-closed);
3. confirm no surface renders green/✅ for inconclusive/untested, and that completeness and confidence never share a format.

Any token whose copy is **not** approved stays fail-closed to the approved generic unavailable/inconclusive string — it is never shown raw and the honesty signal is never suppressed (`STAGE5-CONTRACT.md` §10).
