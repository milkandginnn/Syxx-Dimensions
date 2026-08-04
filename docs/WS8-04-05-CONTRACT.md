# WS8-04 / WS8-05: Threat-naming and referral contract

**Status:** architecture approved by owner 2026-07-20. Two user-visible lines approved (§6). The
remedy line and the seven referral strings remain blocked pending the "Free the Witnesses"
dashboard mockups.
**Deltas:** B152 (WS8-04), B153 (WS8-05).

**This supersedes the first draft of this document.** That draft treated referral as the problem
and proposed wording for a referral the engine should never have reached. The defect is upstream:
the engine names threats on evidence its own corroboration rule says is insufficient. Fix the
naming gate and most of the referral question dissolves.

---

## 0. Why this change exists

Two subsystems read the same evidence and disagree about whether it establishes anything.

```swift
// Scorers.swift:118 (the fingerprint refuses to name a mechanism on one signal)
let independentSignals = Set(positiveEvidence.map { $0.evidenceId }).count
guard independentSignals >= 2 else { return nil }
```

```swift
// SynthesisEngine.swift:335 (the verdict names a threat with no corroboration count at all)
} else if belThreatSet >= config.threatNameFloor {
    verdict = threatLead
}
```

`threatNameFloor` is 0.40 and mass is `1 - exp(-w)`, so the solo-naming threshold is w ≥ 0.511.
Of 51 positive weight pairings, **47 clear it**. That includes the entire 0.6 tier, 21 pairings,
which `Conclusion.swift:355-357` explicitly documents as corroboration-only:

> *"Part-F tier 0.6: real signal, but a multi-subnet office with a legitimate internal resolver
> looks the same, so it corroborates, it never decides alone."*

`1 - exp(-0.6) = 0.451 ≥ 0.40`. It decides alone. The comment states the intent and the
arithmetic contradicts it, for every pairing in the tier.

The user-visible consequence is a named threat with a hedge under it: *"DNS spoofing, 45%
confidence"* from one weak signal. That is an accusation the evidence does not support, and the
hedge is not information the user can act on. It is apology copy for a verdict that should not
have been printed.

**A named threat is a claim.** One weak indicator does not support the claim, so the honest
output is inconclusive, with the observation still visible in the evidence list.

---

## 1. Contract text

### 1a. New naming rule, add to `REASONING-SYSTEM-DESIGN.md` §5

> **Threat naming requires either a validated decisive act, or two independent support groups.**
> A decisive act is defined by membership in the signed `DecisiveActCatalog`, not by weight.
> Membership is a deliberate, versioned, signed editorial decision about which acts individually
> justify an accusation. Weights are tuned for how signals combine and must not be reused as a
> naming gate: they are per `(evidenceId, hypothesis)` pairing, so a weight threshold lets the
> same act name one hypothesis and not another in the same run, and any numeric cutoff silently
> excludes catalog entries whose weight sits below it.
>
> `threatNameFloor` is unchanged and continues to govern whether committed belief is sufficient.
> Corroboration is an additional requirement, not a replacement.
>
> **Invariant:** catalog membership and the ≥2.5 weight band must not silently diverge. A test
> asserts this. If an act is added to the catalog, its weight is reviewed in the same change.

### 1b. Replaces `REASONING-SYSTEM-DESIGN.md:475`

The current I7 row lists five triggers. The code has had seven since Ph6.2, so this reconciles
the row with the code as well as amending it.

> | I7 | **Referral fires on a CAUSE:** low confidence, a narrow between-mechanism-class margin,
> genuine conflict, a hard-gated or incomplete critical section, or a dampener-forced referral.
> **A validated decisive act suppresses the first three.** A decisive act is itself direct
> corroboration, and those three thresholds proxy for corroboration, so applying both double-counts
> and refers a diagnosis the evidence has settled. **A decisive act never suppresses a hard gate or
> a dampener-forced referral.** Those report evidence that is absent, or evidence already explained
> by a named mechanism, and neither is answerable by proving one act. **Suppressed conflict is
> disclosed, not discarded.**
>
> **Inconclusive is not on this list and never will be.** It is not a cause of referral. It is the
> fallback rendered when no diagnosis can be safely reached, and every path that produces it already
> fires one of the causes above. Report the cause, which the user can act on, never the state, which
> tells them only that we failed. | Stage 4 |

Note the trigger list drops `belowMinCorroboration`. §4 proves it unreachable.

---

## 2. Change 1: B153 triplet, which lands first

B153 goes first because it is what makes catalog membership and the ≥2.5 band coincide.
`scriptMismatch` is the only catalog member outside that band, and the only Strong-band act
claiming decisive status. It is the odd one out on both axes.

| Step | macOS | Windows | Action |
|---|---|---|---|
| 1 | `DecisiveActCatalog.swift:32-34` | `DecisiveActCatalog.cs:33-35` | Delete the entry and its comment. Bump catalog version. |
| 2 | `Conclusion.swift:278` | `Conclusion.cs:222` | Weight 2.0 drops below `threatNameFloor`. Since `1 - exp(-w) < 0.40`, the weight must be **under 0.51**. |
| 3 | `Synthesizer.swift:283` | `Synthesizer.cs:~396` | `.positive` becomes advisory, so it stops contributing a `positiveSupportGroup`. |

Step 1 alone leaves committed mass at `1 - exp(-2.0) = 0.865`, clearing `threatNameFloor` on the
non-decisive branch. The catalog gate in §3 then blocks it for lacking corroboration, but the
weight remains wrong on its own terms and would name on any two-group run.

**The catalog comment must go with the entry.** It claims the act proves content tampering via an
SRI digest divergence. The producer at `Synthesizer.swift:278` is
`else if !w.isScriptStructureIdentical`, an order-sensitive array comparison of script tag URLs.
`grep -rn "integrity=\|\bSRI\b" macOS/Sources Windows/Syxx.Kit` returns only the two catalog
comments, so the mechanism described exists in neither tree.

Bumping the version invalidates previously signed `ValidatedDecisiveActs`. Confirm
`DecisiveActCatalogTests` (`SyxxKitTests.cs:3466-3499`) still passes: it asserts a version
mismatch fails verification, which is correct behaviour.

**Do not invent a replacement rule.** Ad rotation, CDN variation, and A/B tests all change script
structure benignly, and the measured base rate for in-flight page modification is 1.3% of client
IP addresses (Reis, Gribble, Kohno, Weaver, *Detecting In-Flight Page Changes with Web Tripwires*,
NSDI '08). NOTE: an earlier draft of this document cited that paper as present in
`research/reference-library/`. It is not. The library contains neither Reis nor Axelsson; a
full-text search returns only third-hand bibliography mentions in `s42400-019-0038-7.txt` and
`netfalcon_ai-based_network_traffic_analyzer_with_t.txt`. Both papers must be ingested before that
citation path is used again. Re-open only when an authenticated content check exists.

---

## 3. Change 2: the naming gate

`positiveSupportGroups` is computed at `SynthesisEngine.swift:192-218`, before the naming site,
so it is already in scope. No restructuring is needed.

### 3a. macOS, `SynthesisEngine.swift:332-340`

```swift
        if decisiveActTriggered {
            verdict = threatLead  // catalog-signed act: individually sufficient to name
        } else if belThreatSet >= config.threatNameFloor && positiveSupportGroups >= 2 {
            verdict = threatLead
        } else if belClean >= config.cleanNameFloor && !cleanBlocked {
            verdict = .clean
        } else {
            verdict = .inconclusive
        }
```

The only edit is `&& positiveSupportGroups >= 2` on the second branch. `threatNameFloor` keeps its
value and its meaning.

### 3b. Windows, `SynthesisEngine.cs`, same branch

```csharp
        else if (belThreatSet >= config.ThreatNameFloor && positiveSupportGroups >= 2)
```

### 3c. Side effect worth recording

This makes `Conclusion.swift:355-357` true. The 0.6 tier now genuinely corroborates and cannot
decide alone. The comment stops describing behaviour the code never had.

---

## 4. Change 3: the referral gate, and why `belowMinCorroboration` is deleted

### 4a. It is provably unreachable

`belowMinCorroboration` is `verdict != .clean && verdict != .inconclusive && positiveSupportGroups < 2`.
It reaches referral only through the suppressible set, which is gated on `!decisiveActTriggered`.
Under §3, when `!decisiveActTriggered`, a threat verdict requires `positiveSupportGroups >= 2`.
So within that branch, a threat verdict implies the predicate is false. It can never contribute.

Delete it from the gate and from `referralReason`. There is no String 2 because there is no
state for String 2 to describe.

### 4b. macOS, `SynthesisEngine.swift:392-399`

```swift
        // I7: a decisive act is itself direct corroboration, so it suppresses the three triggers
        // that proxy for corroboration. It never suppresses the three that report absent or
        // already-explained evidence. Suppression is gating only. See §5 for disclosure.
        // belowMinCorroboration is removed: the naming gate makes it unreachable (contract §4a).
        let suppressibleByAct = confBounded < config.confidenceReferralBelow
            || classMargin < config.marginReferralBelow
            || conflict > config.conflictReferralAbove
        // hardGated and dampenerForcedReferral are FACTS ABOUT EVIDENCE. A diagnosis state is not
        // one and does not belong in this disjunction.
        let neverSuppressed = hardGated
            || dampenerForcedReferral
        let referralTriggered = neverSuppressed || (!decisiveActTriggered && suppressibleByAct)
```

Delete the now-false `Ph6.2: a decisive act overrides referral` line at `:390`.

### 4c. Windows, `SynthesisEngine.cs:386-393`

Same structure, same comment, same order.

---

## 5. Change 4: `referralReason`

Two defects here are live today and independent of everything above.

**Defect A: `dampenerForcedReferral` has no branch.** It falls through to the final `else`, which
reports a margin failure quoting whatever the margin happened to be, up to 95 points.

**Defect B: branch order does not match the trigger set.** The `conflict` branch at `:730` sits
above `hardGated` at `:735`. After §4, conflict can exceed threshold while suppressed as a
trigger, so a hard-gate referral on a decisive act would be explained as "the sections disagree."

**Defect C: all four existing strings violate `AGENTS.md:52`**, using em-dashes as clause
separators. Corrected here since the function is being rewritten anyway.

### 5a. Signature

```swift
    private static func referralReason(conf: Int, margin: Double, verdict: Hypothesis,
                                       conflict: Double, hardGated: Bool,
                                       dampenerForcedReferral: Bool,
                                       decisiveActTriggered: Bool, config: Config) -> String {
```

The call site at `:403` passes the booleans that formed the gate. Do not recompute them.

### 5b. Body

**All six branches stay.** `confBounded` and `classMargin` still fire whenever no decisive act is
present, and deleting their branches would recreate Defect A one layer up.

```swift
        if hardGated             { return <STRING 4> }
        if dampenerForcedReferral { return <STRING 1> }
        if !decisiveActTriggered {
            if conflict > config.conflictReferralAbove { return <STRING 6> }
            let lowConf = conf < config.confidenceReferralBelow
            let thinMargin = margin < config.marginReferralBelow
            if lowConf && thinMargin {
                return "Confidence \(conf)% is below the \(config.confidenceReferralBelow)% threshold, and only \(Int(margin)) points separate the top two reads."
            } else if lowConf {
                return "Confidence \(conf)% is below the \(config.confidenceReferralBelow)% threshold."
            } else if thinMargin {
                return <STRING 7>
            }
        }
        return "This result warrants a second opinion."
```

The current unconditional final `else` is what manufactures the 95-point claim. It becomes
`else if thinMargin`, with a terminal fallback quoting no figure. A fallback that names no number
cannot fabricate one.

### 5c. Strings, pending approval

| | Trigger | Proposed |
|---|---|---|
| **1** | `dampenerForcedReferral` (new) | `"A network mechanism was detected that can itself explain this result. Confirm with a human before acting."` |
| **3** | conflict suppressed by act (disclosure only) | `"Sections disagree (conflict N%), but a proven act settles the verdict."` |
| **4** | `hardGated` (replaces `:734`) | `"One or more required checks did not run, so this is not a complete picture. Confirm with a human."` |
| **6** | `conflict` (replaces `:731`) | `"The sections disagree (conflict N%), which is too contested to call automatically."` |
| **7** | thin margin (replaces `:743`) | `"The top two possibilities are within N points, which is too close to call automatically."` |

Strings 2 and 5 are retired: both described a STATE rather than a cause. String 5's text at `:728` is deleted outright, not replaced. String 3's surface is unspecified: a suppressed conflict produces no
referral, so `referralReason` is never called. `PlainExplanation` is the candidate but has no
macOS test coverage (B188). Route through `syxx-neural-surfacer`.

---

## 6. Inconclusive copy

**Approved (first two lines):**

> **Inconclusive.** Syxx did not see enough to reach a firm conclusion.
>
> The strongest read was: {Hypothesis.title}. Only one indicator pointed to it, and not a strong
> one. That is not enough to call it.

**Blocked:** the remedy line, pending the "Free the Witnesses" dashboard mockups, since it must
name the control and its placement on three platforms.

Two constraints that decided this wording:

**No `{evidenceId}`.** There is no display-name mapping for `EvidenceId` anywhere in the tree.
`Hypothesis.title` (`Conclusion.swift:125`) is the only display mapping in the file. Substituting
an evidence ID renders a raw enum case such as `ttlExtreme` to the user. The band is derivable
from the weight and needs no mapping, so the copy says "not a strong one" instead.

**Threat name in terminal position.** `Hypothesis.title` returns catalog labels containing slashes
and parentheses, for example `DNS interception (transparent proxy)` and
`Local compromise / exfiltration`. They do not parse mid-clause. After a colon they read cleanly.

**Rejected:** "constitutes network noise above all else." The engine knows one indicator fired in
the weak band. It does not know the signal is noise rather than a real signal lacking
corroboration. Asserting the former is a verdict the evidence does not support.

Also rejected: "not strong enough to justify naming a threat." "Naming a threat" is the engine's
internal term for the operation, and `AGENTS.md:88` puts novice readability first.

Implement in `PlainExplanation.swift` and `PlainExplanation.cs`. Both exist. B188 requires test
coverage for that surface, added in the same change.

---

## 7. Tests

`SynthesisEngineTests.swift` has 16 tests and none on this path. The Windows suite has none.
`SyxxKitTests.cs:3466-3499` is `DecisiveActCatalogTests` and covers catalog signing only, so no
existing assertion breaks. This contract is entirely uncovered, which is how it drifted from I7.

Each test lands with its mutation run and observed RED.

| # | Setup | Assert | Mutation → RED |
|---|---|---|---|
| 1 | One weak indicator (w = 0.6), no decisive act | verdict is `.inconclusive`, no threat named | drop `&& positiveSupportGroups >= 2` |
| 2 | Two independent indicators, no decisive act | threat named | as above, inverted: gate on `>= 3` |
| 3 | Lone catalog act, one support group | threat named, no referral | remove the `decisiveActTriggered` naming branch |
| 4 | Decisive act, 2 groups, conflict 0.54 | no referral | re-add `conflict` to `neverSuppressed` |
| 5 | Decisive act plus hard-gated section | referral fires, reason is String 4 | move `hardGated` into `suppressibleByAct` |
| 6 | Decisive act plus captive-portal dampener | referral fires, reason is String 1 | (a) move `dampenerForcedReferral` into `suppressibleByAct`; (b) delete the String 1 branch |
| 7 | No decisive act, conflict 0.54 | referral fires, reason is String 6 | drop `!decisiveActTriggered &&` from the gate |
| 8 | **Invariant:** catalog membership equals the ≥2.5 weight band | sets are equal | add any act to the catalog without reviewing its weight |

Test 8 is the guard against the failure that produced this contract. Two gates agreed once, then
drifted, and nothing caught it.

Test 1 is the one that would have caught the original defect.

All eight land on both platforms. `SynthesisEngineTests.swift` is the correct home: these are
synthesis-engine contracts, not witness or evidence contracts.

Run `syxx-parity-check` before closing, and `syxx-honesty-audit` on any surface carrying new
strings.

---

## 8. Ledger corrections owed in the same pass

**WS8-06 / B136 is mis-recorded** as "already present; claimed never ported was stale."

```
$ git show HEAD:Windows/Syxx.Kit/Synthesis/Synthesizer.cs | grep -c "B136"
0
```

The guard exists only in the working tree at `Synthesizer.cs:374-383`. B136 was a true delta
fixed in this run, and `DELTAS-fixed.md` must say so.

**New delta owed:** `EvidenceItem.observation` strings reach users. `SynthesisEngine.swift:751`
builds the prefilled second-opinion post with `"- [\($0.section.rawValue)] \($0.observation)"`,
and those strings were written as internal evidence descriptions. Example from
`Synthesizer.swift:281`: `"Fetched page's script structure differs from the reference copy"`. No
subject, telegraphic register, shipped inside a post the user is invited to publish. Either the
observations are rewritten as copy, or the post uses a display-name mapping. That same absent
mapping is why §6 carries no evidence ID.

**On close:** move B152, B153, B136 to `DELTAS-fixed.md` with house-format evaluation comments
(labels column 24, text column 48). Mark ROADMAP WS8-04, WS8-05, WS8-06 DONE only after the
mutation proofs are observed. `CHANGELOG.md` with `file:line`. Six `outputs-catalog.html` rows.

---

## 9. Order

1. §1 contract text. The code needs a spec to conform to.
2. §2 B153 triplet. It makes catalog membership and the ≥2.5 band coincide, which test 8 asserts.
3. §5 `referralReason`: signature, branch order, the four string rewrites. Defect A closes here,
   on its own evidence, before any gate moves.
4. §3 the naming gate, both platforms. Test 1.
5. §4 the referral gate, deleting `belowMinCorroboration`.
6. §6 inconclusive copy, first two lines, with B188 coverage.
7. Tests 2 through 8 with mutations.
8. §8 ledgers.

§5 precedes §3 and §4 deliberately: no intermediate commit can produce a referral explained by a
fabricated margin. §2 precedes §3 because the naming gate consults the catalog, and the catalog
should be correct before anything depends on it.

**Closure bar, both platforms, real run, honest output.** One weak indicator returns Inconclusive
naming no threat, with the observation still visible in the evidence list. A lone catalog act
names its threat without referral. A decisive act behind a captive portal refers and says so in
the dampener's own terms. A script-structure difference alone names nothing at any confidence.
