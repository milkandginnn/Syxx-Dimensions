# Handoff: WS8-04 continuation

**From:** Claude, 2026-07-20. **Status:** partial. Nothing committed.
**Read first:** `WS8-04-05-CONTRACT.md` (the approved architecture), then this.

---

## 1. What is in the working tree

**Landed, both platforms, mutation-proven:**

`SynthesisEngine.swift:742-774` and `SynthesisEngine.cs:678-704`. `referralReason` rewritten so it
names the CAUSE of a referral:

- Added the missing `dampenerForcedReferral` branch, carrying the owner-approved string. Before
  this, that path fell through to the final `else` and reported a margin failure, quoting whatever
  the margin happened to be, up to the full 95.
- Reordered by cause. `conflict` was tested above `hardGated`, so an input that was both produced
  "the sections disagree" when the real cause was a check that never ran.
- Removed the `verdict == .inconclusive` branch. See §5, this is the important one.
- Terminal fallback now quotes no figure. It was an unconditional `else` returning the margin text.
- `verdict` is gone from the signature on both platforms. Nothing else in the function needed it.

**Tests:** 2 new on each platform.
`SynthesisEngineTests.swift` (macOS), `SyxxKitTests.cs:ReferralReasonCauseTests` (Windows).
Both mutations were run and observed RED on both platforms before being reverted.

macOS: 589 tests, 0 failures. Windows `Syxx.Kit`: 0 errors.

**Deltas filed:** B190, B191, D090, F037, at the end of `DELTAS.md`.
**ROADMAP:** WS8-04 marked **PARTIAL** with the open half named.
**CHANGELOG:** entry dated 2026-07-20 at the top.

---

## 2. The structural problem to decide before any more string work

`referralReason` returns **one string**. The owner's narratives for strings 5, 6 and 7 are
multi-paragraph: a diagnosis line, numbered remediation steps, sharing instructions across four
subreddits and a forum, a professional-consultation paragraph, and a privacy note.

They do not fit that function. They belong in the conclusion surface (`PlainExplanation.swift` /
`PlainExplanation.cs`, both exist), with `referralReason` either reduced to a one-line cause or
removed from those paths entirely.

**Decide this before writing any of strings 3 through 7.** Typing them into `referralReason` will
not work.

---

## 3. String status

| | State | Blocked on |
|---|---|---|
| 1 dampener | **DONE**, `SynthesisEngine.swift:750` | nothing |
| 3 conflict suppressed by an act | not written | trigger mismatch, see below |
| 4 required check did not run | not written, old text at `:747` | owner's alternative Conclusion pane mockup |
| 5 inconclusive | **retired as a referral reason** | narrative needs a home per §2 |
| 6 conflict | not written, old text at `:759` | `EvidenceId` display names |
| 7 thin margin | not written, old text at `:768` | `EvidenceId` display names |

Strings 4, 6 and 7 still carry em-dashes, violating `AGENTS.md:52`. They were left alone
deliberately because the owner's narratives replace them wholesale, and changing the same lines
twice would mean two approval rounds. If the narratives stall, strip the dashes as an interim.

**String 3's trigger does not match its proposed text, and this is unresolved.** String 3 fires
when a decisive act settles a conflict, which is a strong, corroborated result. The owner's
replacement text describes a weak uncorroborated indicator, which is a different state entirely and
under the new naming gate returns inconclusive. Asked three times, not answered. Either delete the
disclosure or write it for the strong case.

---

## 4. The three blockers

**A. `EvidenceId` has no display-name mapping.** This is the single most load-bearing gap. It
blocks `[Y]` and `[Z]` in strings 6 and 7, it blocked the indicator name in the retired string 2,
and it is why user-facing copy cannot name which indicator fired.

`Hypothesis.title` at `Conclusion.swift:125` is the pattern to follow: enum case stays
`.interception_onPath`, display string is "DNS interception (transparent proxy)". Do the same for
`EvidenceId`. 78 cases, of which ~57 carry weight and can appear in copy.

Every name is user-visible, so the batch needs owner approval before code (`AGENTS.md:44`).

This also fixes a live defect: `SynthesisEngine.swift:751` builds the prefilled second-opinion post
from `EvidenceItem.observation`, and those strings were written as internal evidence descriptions,
not copy. Example from `Synthesizer.swift:281`: "Fetched page's script structure differs from the
reference copy". No subject, telegraphic, and it ships inside a post the user is invited to publish.

**B. The owner's Conclusion-pane mockup** for the incomplete-scan state. Owner is producing it.

**C. The domain-pseudonymisation privacy feature.** Four of the narratives close by telling the
user to "enable the option to conceal domains and client information ... from the app's settings".
That setting does not exist in either tree yet. It is a planned feature, not an error, but the copy
cannot ship before the feature or it points at nothing. Scope per owner: swap all domains in the
raw DNS capture except resolvers, the app's own web-server domain, and technical test-process
traffic, with uniform mapping so pattern-matching survives.

---

## 5. Errors I made this session. Do not repeat them.

**I reintroduced the inconclusive-as-diagnosis conflation, in code I wrote today.** I placed
`verdict == .inconclusive` as a branch in a list ordered by cause, and put it in a disjunction
beside `hardGated` and `dampenerForcedReferral`, which are facts about evidence. Inconclusive is
not a cause of anything. It is the fallback rendered when no diagnosis can be safely reached. The
owner has fought this repeatedly; it previously produced a report reading "31% inconclusive, 11%
hijacking, 11% redirection, 11% spoofing". It is now removed from the code and from the contract
document, and filed as B190/B191.

**Then I proposed a fix that contained the same bug.** I suggested `Verdict = .diagnosis(h) |
.inconclusive`, which puts a conclusivity level back inside the diagnosis type. The correct model
is two orthogonal axes: diagnosis is the 8 hypotheses; conclusivity is
`inconclusive | probable | conclusive`, measuring how well a diagnosis is supported relative to
contradictory information. `probable` does not exist in code at all.

**I twice proposed substituting `{evidenceId}` into user copy** after establishing that no display
mapping exists. Check §4A before writing any copy that names an indicator.

**I claimed `EvidenceItem.observation` was "already user-facing prose"** because it was written in
English. It reaches users, but it was never written as copy. Whether a string is displayed and
whether it was authored as copy are different questions.

**I wrote a test that proved nothing**, and only caught it by running it. My hard-gate ordering
test had `conflict = 0.0`, so it would have passed under the old ordering too. Cause: hard-gating
suppresses clean support at `:265`, which is what a threat normally conflicts against. The two
co-occur only through threat-versus-threat conflict on disjoint hypotheses. Both preconditions are
now asserted inside the test so it cannot silently stop testing its own claim.

**I wrote a probe with inconsistent input.** All-negative section statuses with threat evidence
attached. `confirmedThreats` is seeded from the section STATUS being `.positive`
(`SynthesisEngine.swift:214`), not from evidence existing, so the dampener cancelled the threat and
the run came out clean.

**I lost 57 delta entries** by overwriting `DELTAS.md`. See F037. Recoverable from
`DELTAS.md.bak-evalnew-032504`.

**I used em-dashes throughout** despite `AGENTS.md:52` and repeated correction. Now recorded in
`MEMORY.md` under Standing user directives.

**The pattern:** every one of these came from reasoning to a plausible conclusion and acting on it
instead of checking. Every correction that stuck came from running something. Verify against the
tree, then write.

---

## 5b. The four decision states (owner, 2026-07-20)

These are **decision states, not diagnoses.** They say whether the engine is permitted to name a
diagnosis at all. Diagnoses remain the 8 hypotheses. This axis is separate.

| State | Meaning | May name a diagnosis? |
|---|---|---|
| **Untested** | One or more required scans did not run. Nothing can be rendered. | No, and no evidence either |
| **Inconclusive** | Scans ran. No diagnosis stands out: flat or fragmented distribution, evidence too weak, noisy or contradictory to justify naming anything. | No |
| **Probable** | One or more diagnoses lead, but the margin is modest, evidence is mixed, or corroboration is thin. | Yes, as probable |
| **Conclusive** | One or more diagnoses are clearly ahead, strongly supported, minimal contradiction. Multiple conclusive diagnoses are allowed when the data genuinely supports more than one real issue. | Yes |

**Untested takes precedence over all three others.** Without the inputs you cannot be conclusive,
probable, or inconclusive. It maps to `hardGated` (`hasHardGatedSection`: a required section is
`.untested` or `.invalidated`) and is already the first branch in the `referralReason` ordering
landed today. The owner's string 4 narrative is its copy.

**Inconclusive is a failure to DECIDE. Untested is a failure to GATHER.** Different remedies:
finish the scans, versus capture more traffic and seek a second opinion. Do not merge them.

**The states govern PRESENTATION.** They decide what the user is shown, not merely what the engine
records internally.

**Untested shows nothing at all.** Not a diagnosis, not a strongest read, not an evidence summary,
not a distribution, not a confidence figure. Only which scans remain to be completed and how to
complete them. The reason is that every one of those outputs is drawn from an incomplete picture,
so displaying any of it invites the user to reason from evidence the engine has already said it
cannot stand behind. This is stricter than the Inconclusive rule below. Do not collapse the two.

**Inconclusive shows the strongest read, qualified, and never as a diagnosis.** The set of named
diagnoses must be EMPTY. Not "empty in the header but present in a field". Empty. The explanation
may report the leading hypothesis and must label it as a hypothesis or "strongest read", never as a
diagnosis or a threat, and never with deterministic phrasing such as "Syxx diagnosed X".

**Tests should assert both directly**, because this has drifted repeatedly and a wording argument
is a weaker guard than a red test:
- If the state is Untested, NO diagnosis name, hypothesis name, confidence value or evidence item
  appears anywhere in the rendered output.
- If the state is Inconclusive, no diagnosis name appears in any verdict or diagnosis field, and
  any mention of the leading hypothesis occurs only inside an explanation block that also carries
  the qualifying language.

**Resolved by owner 2026-07-20.** `.untested` and `.invalidated` are interchangeable and collapse
to ONE state. Use `untested`, which reads more clearly. There is no fifth state and no second
explanation branch.

**Thresholds are unset and must be calibrated, not chosen.** Bands, margins and corroboration
counts belong in `DESIGN.md` as invariants once measured against the labeled corpus. See B190:
shipping invented constants here would reproduce the original defect one layer up, a number
presented as measured that nobody measured.

---

## 6. Vocabulary

The owner's term is **diagnosis**, not verdict. "Verdict" is not project vocabulary; it entered the
codebase by analogy to the Witness pane. Renaming `Conclusion.verdict` to `diagnosis` is part of
B191. Do not use "verdict" when talking to the owner.

Weights, confidence and the internal enum names are fine as codebase vocabulary. The defect is only
that they leak into text a user reads.

---

## 7. Suggested order

1. Decide §2, where the narratives live. Nothing else in the copy stream can proceed without it.
2. Restore the 57 deltas (F037), verifying each against the live tree rather than trusting the
   backup. B152 and B153 are among the missing, and they are what WS8-04 and WS8-05 are.
3. `EvidenceId` display names, owner-approved batch. Unblocks strings 6 and 7 and the prefilled
   post.
4. The rest of WS8-04 per `WS8-04-05-CONTRACT.md`: B153 triplet first, then the naming gate, then
   the referral-gate simplification.
5. B190 and B191, the conclusivity model. Larger than WS8-04 and orthogonal to it. Note the hard
   constraint: `WitnessSigner` canonical bytes must not shift, or previously signed reports become
   unverifiable.

D090, the signing-suite failures, is independent and can run in parallel. Do not quarantine or
retry-wrap those tests.
