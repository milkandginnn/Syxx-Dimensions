# PARITY_VECTOR_V1 — shared conclusion-vector format

**Status:** Draft · **Created:** 2026-08-05 · **Track:** `cross-platform-parity_20260805` (Task 1.1)

The input contract for the generalized `parity-transcript` emitters. One format, read by both
`SyxxKit` (Swift) and `Syxx.Kit` (C#), so a single corpus drives both engines and the comparison is
a plain `diff`.

## Why a written contract precedes the loaders

Three hand-written parsers already failed to hold byte-parity in this codebase; that is the finding
that produced the Warp signed-manifest pivot. This format is deliberately trivial to parse so the
same class cannot recur one layer up: no nesting beyond one level, no dates, no numbers whose
formatting varies by runtime, no optional syntax.

## Layout

```
<vector-dir>/
  001_empty-section-inputs.json
  002_.../
```

One case per file. The file's basename before `_` is its sort key; the remainder is the `vectorId`.
Case order is the sorted basename list — never directory-enumeration order, which differs by
filesystem and runtime.

## Case shape

```json
{
  "schemaVersion": 1,
  "vectorId": "empty-section-inputs",
  "pinnedUnixSeconds": 1752268477,
  "sectionInputs": {}
}
```

| Key | Rule |
|---|---|
| `schemaVersion` | Integer. A loader that does not recognise the value **rejects the case**; it never best-effort parses |
| `vectorId` | Must equal the filename remainder. Mismatch is a rejection, not a warning |
| `pinnedUnixSeconds` | Integer seconds. Both engines construct their clock from this and nothing else |
| `sectionInputs` | Object whose keys are drawn from the shared field set below. `{}` means every field absent |

## The shared field set

`SectionInputs` is **not symmetric between the twins.** Measured 2026-08-05:

- Swift: 34 fields
- C#: 32 fields
- Shared: 32
- Swift-only: `resolverSubstituted`, `transportObservation`
- C#-only: none

v1 covers the **32 shared fields only**, named in lowerCamelCase (the Swift spelling; the C# loader
maps to its PascalCase members).

```
resolution, dnssec, nxCanary, dnssecCanary, comparison, targetDomain, targetQueryType,
warp, warpManifest, network, leak, exfil, cageAttributionRate, drifts, configChanges,
latency, stability, wildcardSynthesis, denial, dualStack, edns0, versionBind, stubEntropy,
authoritative, svcb, ttlAnomaly, cnameChain, encryptedTransport, openResolver, ecs,
responseSize, serverGroundTruth
```

## Unknown and unrepresentable fields — reject, never ignore

**Both loaders reject any key not in the shared set, naming the key and the case, and exit non-zero.**

This is the single most important rule in the format, and it is the harness-level form of the defect
this product exists to refuse. If the Swift loader accepted `resolverSubstituted` and the C# loader
silently ignored it, one of two things follows:

- the engines diverge and the diff blames the engines for a harness defect, or worse
- both emit `untested`, the diff is clean, and a field that changes the conclusion is recorded as
  covered when nothing exercised it

Silently dropping an input is how a coverage hole renders as a pass. So: a vector that needs
`resolverSubstituted` or `transportObservation` **cannot be expressed in v1**, and both loaders must
say so by name rather than accept it partially. The gap stays loud until the underlying asymmetry is
resolved by its own track.

## Output record

One record per case, emitted in sorted case order:

```json
{
  "schemaVersion": 1,
  "vectorId": "empty-section-inputs",
  "evidenceCompleteness": {"untestedSections": 6, "totalSections": 6, "observedEvidenceCount": 0},
  "determinationState": "untested",
  "diagnosisIds": [],
  "hasHardGatedSection": true,
  "confidencePresent": false,
  "engine": "Synthesizer.conclude"
}
```

Unchanged from the current single-vector emitters, so the existing CI job keeps working while the
corpus path is built.

**Ordering:** `diagnosisIds` sorted with ordinal semantics — C# `StringComparer.Ordinal`; Swift must
match ordinal, not locale-aware, comparison. Any collection reaching output is explicitly sorted;
Dictionary and Set iteration order differs between the runtimes.

## Transcript envelope

```json
{
  "schemaVersion": 1,
  "caseCount": 31,
  "evaluated": 31,
  "records": [ ... ]
}
```

`evaluated` is written by the emitter after the loop, from a counter incremented per case actually
concluded — not from the length of the input list. The emitter asserts `evaluated == caseCount` and
exits non-zero on mismatch.

A harness that cannot state how many cases it ran cannot be trusted when it is green. `WarpEngineTests.swift`
currently `return nil`s brotli cases and the caller `continue`s, with no skip marker and no counter,
which is why a green Warp run says nothing about how many of its 31 cases executed. This envelope is
the fix for that class at the transcript layer; Phase 5 closes it at the test layer.

## Rejection behavior — summary

A loader exits non-zero, naming the case and the reason, when:

1. `schemaVersion` is unrecognised
2. `vectorId` disagrees with the filename
3. Any key in `sectionInputs` is outside the shared set
4. The file is not valid JSON
5. `pinnedUnixSeconds` is absent or not an integer

There is no partial-success path. A corpus either loads whole or fails loudly.
