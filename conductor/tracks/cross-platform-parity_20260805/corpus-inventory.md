# Corpus inventory — against PARITY_VECTOR_V1

**Task 1.2** · 2026-08-05 · Measured against `docs/parity-vector-v1-spec.md`

## Headline

**No existing corpus can feed the generalized emitter directly.** Every one of them sits at either
the *module* layer or the *determinative* layer. None sits at the `SectionInputs → Conclusion` layer
that `parity-transcript` exercises.

The plan assumed these could be harvested. They cannot. The conclusion-parity corpus has to be
**authored**, and Phase 1 should be re-read with that in mind (see "Plan impact" below).

## The three layers

```
module fixtures        →  probe / witness / warp / appcast / consensus
                          inputs are that module's own domain objects

SectionInputs          →  ◄── PARITY_VECTOR_V1 targets HERE. No corpus exists.
   ↓ Synthesizer
section statuses + evidence
   ↓ matrix determinative engine
determinative state    →  corpus/matrix, corpus/stage0
                          inputs are already-computed section statuses
```

The determinative corpora start *downstream* of the synthesizer, taking section statuses as input.
They cannot drive `Synthesizer.Conclude` because they begin where it ends.

## Per-corpus

| Corpus | Size | Shape | Feeds v1? | Reason |
|---|---|---|---|---|
| `corpus/matrix/vectors/` | 3 | `{name, spec, input:{indicators, sections[]}, expected:{state, diagnoses, explanation, suppressed}}` | **No** | Determinative layer. Input is section statuses, the synthesizer's *output*. Belongs in a sibling gate — see F-03 |
| `corpus/matrix/default-spec.frozen.json` | 1 | `{diagnoses, knownSections, thresholds}` | **No** | A spec oracle, not a vector set |
| `corpus/stage0/seeds.json` | 8 vectors | `{id, label, evidence, sectionStatuses, greensAtPhase, expected…, expectedReferral, knownFalseSecureUntilGate}` | **No** | Determinative layer, same as matrix |
| `warp-manifest-vectors/` | 31 cases | per-case dir: `envelope.json`, `expected.json`, `inputs/`, `now_ms`, `pinned_keys.json`, `requested_origin` | **No** | Module layer (Warp only). **But it is the structural model v1 should copy** — see below |
| `corpus/probe-parity/` | 3 vectors | `{id, probe, localOutcome, publicOutcomes, provenance, expected}` | **No** | Module layer (probes) |
| `corpus/b281-consensus/` | 13 cases | `{name, vantages, trustedReference, expected}` | **No** | Module layer (consensus) |
| `corpus/witness-testify-parity/` | 7 scenarios | `{id, layer, description, planner, expected}` | **No** | Module layer (Witness) |
| `corpus/witness-crossplatform/` | 5 | signed artifacts: `{comparison, evidence, signature, keyId, publicKey, expiresAt, …}` | **No** | Module layer; these are outputs, not inputs |
| `corpus/appcast-crossplatform/` | 1 | `{canonicalVectors, signatureVectors, testPublicKeyBase64, …}` | **No** | Module layer (update feed) |
| `corpus/resolvectl-status/` | fixtures, no JSON | captured `resolvectl` output | **No** | Linux-only parser fixtures; no twin to compare against |

## `warp-manifest-vectors` is the model

It already does everything v1 needs, and it predates this track:

- one directory per case, sorted by numeric prefix
- `now_ms` — the clock pinned **per case**, which is exactly `pinnedUnixSeconds`
- `expected.json` carrying a conclusion plus per-dimension states
- a regeneration drift gate in `ci.yml`: rebuild, then `git diff --exit-code`, so a hand-edited
  generated file or a silent builder change fails the build

v1 should mirror this layout rather than invent one, and Task 6.5's drift gate should be a copy of
that job.

## Plan impact

Task 1.2 as written assumed an inventory would yield feed-ready material. It yields none. Two
consequences:

1. **Phase 1 gains an authoring task.** The conclusion-parity corpus must be written from scratch.
   The existing `empty-section-inputs` case is case 001; everything else is new. Proposed addition:
   *Task 1.6 — author the initial vector set covering each of the 6 sections populated, plus the
   fail-closed boundaries (unfetchable, unverifiable, degenerate).*
2. **The determinative corpora deserve their own gate, not a shim.** Forcing `corpus/matrix` and
   `corpus/stage0` into v1 would mean synthesizing `SectionInputs` that happen to produce the section
   statuses those vectors already state — inventing inputs to reach a known intermediate. That is a
   fabricated correlation, and it would test the inversion rather than the engine. They should be a
   second gate at their own layer, which is also where F-03 gets resolved.

## Observation, not a finding

`corpus/stage0/seeds.json` uses the banned word as the suffix of its `expected…` field name across
all 8 vectors, and `expectedReferral` sits beside it. This is pre-existing corpus data with live
consumers, so renaming it is a decision with a blast radius, not a cleanup. Flagged for the owner;
not touched.
