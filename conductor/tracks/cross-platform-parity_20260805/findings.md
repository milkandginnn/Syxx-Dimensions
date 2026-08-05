# Findings — cross-platform-parity_20260805

Each entry carries one of the five states from `conductor/workflow.md` and `file:line` evidence.

**Scope amended 2026-08-05:** the track was created detection-only, and the "Not fixed here" notes
below were written under that constraint. The owner has since moved remediation into this track
(Phase 8). Those notes are left as written rather than edited away — they record what was true when
each finding was filed. The **State** line is the current truth; where a finding has since been
fixed, its state says so.

---

## F-01 · `resolverSubstituted` (B174) is macOS-only — substituted evidence can read clean on Windows and Linux

**State:** `open`
**Found by:** Task 1.1 (SectionInputs field-set comparison)
**Severity:** High — the divergence removes a fail-closed guard on two of three platforms

### Evidence

| | |
|---|---|
| Swift field | `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:82` — `public var resolverSubstituted: Bool = false` |
| Swift doc | `:79` — "true when the system resolver was undetectable and a public substitute (e.g. 8.8.8.8)…" |
| Swift use | `:772` — `if inputs.resolverSubstituted { … }` caps the Mirror section at `.degraded` when it would otherwise be `.negative` |
| Swift origin | `macOS/Sources/SyxxKit/Synthesis/FirstRunScan.swift:64` — records the substitution "so downstream artefacts never call the substitute 'local'" |
| C# twin | `Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs` — no such field; `SectionInputs` has 32 members, none equivalent |
| C# search | `grep -rin 'substitut' Windows/dotNET/Syxx.Kit/` returns only unrelated hits (UpdateService, DNSResolver, DicePassphrase, LookalikeDetector) |
| B174 search | `grep -rn 'B174' Windows/dotNET/ Linux/` returns nothing; the identifier exists only in Swift |

### What it means

When the system resolver is undetectable, Syxx substitutes a public resolver. Mirror's whole method
is a local-versus-public comparison, so under substitution **local *is* public** and the comparison
degenerates — it compares a resolver against itself. The Swift engine recognises this and caps Mirror
at `degraded` so the outcome can never be clean on degenerate evidence.

The C# engine has no such input and therefore no such cap. On Windows and Linux the same run yields
an uncapped Mirror section, which can be `negative` (clean).

This inverts goal 2 in `conductor/product.md` — *missing, unfetchable, or unverifiable data never
upgrades a conclusion* — on the two platforms that lack it. It is not a cosmetic difference: it is a
clean result produced from evidence the macOS build considers too degenerate to conclude from.

### Not fixed here

Remediation needs its own track, and a decision the owner owns: whether the C# side gains the field
and the cap, or whether substitution is prevented earlier on those platforms. Either is a behavior
change to a shipping conclusion path.

---

## F-02 · `transportObservation` is absent from the C# `SectionInputs`, though its type exists

**State:** `open`
**Found by:** Task 1.1
**Severity:** Medium — a Prophet/Mirror shared input is unreachable on two platforms

### Evidence

| | |
|---|---|
| Swift field | `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:67` — `public var transportObservation: DNSTransportWindow?` |
| C# type | `Windows/dotNET/Syxx.Kit/DNSTransportState.cs` — `DNSTransportWindow` **does exist** |
| C# wiring | `Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs` — the type is never referenced; no `SectionInputs` member carries it |

### What it means

This is a wiring gap, not a missing port — the type was carried across and then not threaded into the
synthesis input. The Swift doc comment states the observed-transport question is distinct from both
the configured question (`network.encryptedDNS`) and the reachability question
(`encryptedTransport`), that the three "can legitimately disagree and a disagreement is itself a
finding," and that `nil` means undetermined rather than cleartext.

So on Windows and Linux that third question is permanently undetermined, and any finding that would
arise from the three disagreeing cannot be produced there.

### Not fixed here

Same reasoning as F-01: threading a new input into a shipping conclusion path is a behavior change.

---

## F-03 · The matrix golden corpus is loaded by C# only; its Swift twin is quarantined

**State:** `open`
**Found by:** Task 1.2 (corpus inventory)
**Severity:** High — the corpus exists specifically to cross-check the Swift transcription, and does not

### Evidence

| | |
|---|---|
| C# loader, live | `Windows/dotNET/Syxx.Tests/MatrixGoldenCorpusTests.cs` |
| Swift loader, disabled | `macOS/Tests/_quarantine/MatrixGoldenCorpusTests.swift.phase32-compile-break` |
| Why it never runs | `macOS/Package.swift:29,77` declare test targets `SyxxKitTests` and `SyxxAppTests`. SwiftPM resolves those to `Tests/SyxxKitTests` and `Tests/SyxxAppTests`; `Tests/_quarantine/` is inside neither, so nothing there is compiled — including the 19 files still carrying a plain `.swift` extension |
| Live Swift test files | 90 |
| Quarantined entries | 43 |

### What it means

The quarantined file's own header states why it was written:

> "Before this file, no test on any platform loaded these vectors, so the ~100 hand-transcribed
> weights and thresholds across `SyxxMatrixIntegration.swift` had zero mechanical cross-check
> against the shared oracle."

It then names the C# file as "Twin of … (to be created)". The C# twin was created and runs. The
Swift original was quarantined. The net effect is that the ~100 hand-transcribed values in
`SyxxMatrixIntegration.swift` — the exact thing the corpus was built to verify — **still have no
mechanical cross-check**, while the corpus reports green from the C# side.

This is the shape the `parity` CI job was rewritten to prevent: a check that renders as covering both
platforms while executing on one. It is also the shape already recorded for the Ubuntu witness
harness, where three-platform parity was asserted by two platforms.

### Not fixed here

Un-quarantining requires fixing whatever `phase32-compile-break` denotes, which is a code change to
the Swift test target. Detection-only track.

### Adjacent, not investigated

`macOS/Tests/_quarantine/` holds 43 entries in three categories — `*-compile-break`,
`*-active-dup`, and 19 plain `.swift` files — including a 115 KB `SyxxKitTests.swift.phase32-compile-break`.
Whether each is deliberate and recorded is a separate audit; this track observed the directory while
inventorying corpora and did not audit it. Flagged so the observation is not lost.

---

## F-04 · Spec serialization was nondeterministic on BOTH platforms — different bytes every launch

**State:** `verified-by-real-run` (fixed 2026-08-05, Tasks 8.1 + 8.2) · **Found by:** A-03 · **Severity:** High

**Fix:** Swift sorts in `MatrixSpec.encode(to:)`; C# sorts via `SortedSectionIdSetConverter`. Twin
tests on both platforms assert the emitted order is *sorted*, not merely stable — two serializations
inside one process share a hash seed and would agree even with the defect present. Both pass.

**Not newly discovered.** `MatrixDeterminativeEngine.cs:365` recorded this during the port and
deferred it to "the Phase 5 canonical-encoding rule (sort the set on BOTH platforms)". That rule
never landed on either side. The note's one error was scope: it called the hazard Swift's alone, but
`SectionId` is a record struct over `string` and .NET randomizes string hashing per process, so the
C# set enumerated per-process too — and `MatrixCanonicalJson` sorts object keys while preserving
array order, so nothing corrected it.

### Evidence

Three consecutive runs of `testDefaultSpecMatchesFrozenOracle` on one machine, one build:

```
run 1  "knownSections":["portal","mirror","prophet","glutton","witness","cage"]
run 2  "knownSections":["prophet","mirror","witness","portal","cage","glutton"]
run 3  "knownSections":["mirror","prophet","glutton","cage","witness","portal"]
```

| | |
|---|---|
| Source | `macOS/Sources/SyxxKit/Synthesis/SyxxMatrixIntegration.swift:206` — `let mappableIndicators = Set(…)` |
| Type | `macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:286` — `public let mappableIndicators: Set<IndicatorID>?` |

`Set` has no stable iteration order, and Swift hashing is seeded per process, so the serialized order
varies run to run.

### What it means

The spec serialization can never equal a frozen oracle — not because the values are wrong, but
because the *bytes* are unstable. Any hash, signature, or comparison taken over this output is
unstable with it. `docs/MATRIX-MIGRATION-PLAN.md:358` designates macOS as the reference platform
whose output is "GENERATED once … then frozen"; a nondeterministic emitter cannot serve that role.

This is the ordering hazard `docs/parity-vector-v1-spec.md` warns about, found in production rather
than in the harness.

---

## F-05 · Neither platform matches the frozen oracle, and they fail differently

**State:** `open` · **Found by:** A-03 · **Severity:** High

| Platform | Test | Diverges from oracle by |
|---|---|---|
| Swift | `MatrixGoldenCorpusTests.testDefaultSpecMatchesFrozenOracle` | Emits an extra top-level key `mappableIndicators`; `knownSections` order unstable (F-04) |
| C# | `Syxx.Tests.MatrixGoldenCorpusTests.DefaultSpecMatchesFrozenOracle` | Forward-slash escaping — oracle has `"DNSspoofing\/poisoning"`, C# emits `"DNSspoofing/poisoning"` (pos 1721) |

The frozen oracle's top-level keys are `["diagnoses","knownSections","thresholds"]` — no
`mappableIndicators`. Swift matches the oracle on slash escaping; C# matches on key set. **Neither
matches on both, and the two platforms would not match each other.**

The C# half is live and runs in CI (`ci.yml:142`, full `Syxx.Tests` suite, no filter), so this has
been failing there. It appears in `docs/PLAN-linux-parity-master-2026-08-04.md:938` inside a list
described as baseline — the list this project already resolved to treat as unaudited rather than
cleared. It is now audited: a real defect, three ways.

---

## F-06 · F042 is recorded as FIXED on the strength of a file that never compiled

**State:** `open` · **Found by:** A-03 · **Severity:** Medium — a process defect, not a code one

`docs/DELTAS-fixed.md:64844`:

> "**[F042] FIXED 2026-07-30 (qwen-code).** macOS loader exists (`MatrixGoldenCorpusTests.swift`) —
> asserts `defaultSpecJSON()` equals `default-spec.frozen.json` byte-for-byte and evaluates each
> golden vector. Windows twin created."

Every clause is literally true. The file existed, and it did contain those assertions. It also sat
in `macOS/Tests/_quarantine/` outside both test targets, so none of them ever executed — and when
executed for the first time today, the byte-for-byte assertion **fails**.

This is the exact failure `Linux/PARITY-LEDGER.md` refuses the word "fixed" to prevent, quoted from
its own header: *a change is in the tree, nothing has executed it, and the fix was real but nobody
could have known.* Here is a concrete instance. F042 should be `fixed-unverified` at best, and is
actually `open`.

---

## Consequence for the vector format

`PARITY_VECTOR_V1` (`docs/parity-vector-v1-spec.md`) covers the **32 shared fields only**, and both
loaders must **reject** a vector referencing `resolverSubstituted` or `transportObservation` by name
rather than ignoring it.

If one loader accepted a field the other silently dropped, either the diff would blame the engines
for a harness defect, or — worse — both would emit `untested`, the diff would come back clean, and a
field that changes the conclusion would be recorded as covered while nothing exercised it. Rejecting
loudly keeps the gap visible until F-01 and F-02 are resolved by their own tracks.

---

## Pending ledger rows

Both findings need rows in `Linux/PARITY-LEDGER.md` under Task 7.2. Recorded here first so the
evidence is not lost if the track pauses.

---

## F-07 · The C# golden-corpus twin implements half of what its own header promises

**State:** `open` · **Found by:** Task 8.2 · **Severity:** Medium

`Windows/dotNET/Syxx.Tests/MatrixGoldenCorpusTests.cs:16-19` states:

> "Both platforms must assert: 1. `DefaultSpecJson()` equals `default-spec.frozen.json`
> byte-for-byte 2. Each golden vector in `corpus/matrix/vectors/*.json` drives `Evaluate()` and
> produces the expected `DeterminativeResult` JSON."

The file implements assertion 1 only. There is no vector-evaluation test on the C# side.

Combined with F-03, the history is: the vectors were evaluated by **neither** platform (Swift
quarantined, C# never written), and as of today by **Swift only**. The file that declares the
two-platform contract is the one missing half of it.

Fix belongs to Phase 8: port `testGoldenVectorsEvaluateToExpectedResults` and
`testEveryVectorFileIsClassified` to C#, including the evaluated-count assertion.

---

## F-08 · `-v` means different things on different platforms — two documented decisions in conflict

**State:** `open` (owner decision required) · **Found by:** Task 8.8 · **Severity:** Medium

### Correction to the earlier claim

The track spec and `index.md` record this as *"the .NET dispatcher accepts `-h` and `-v`; the Swift
dispatcher accepts only `--help` and `--version`."* **That is wrong.** Swift has `-h`, and it has
`-V`. My extraction used `case "[a-z0-9-]+"`, which is lowercase-only, so it never matched `-V` and
I reported an absence that my own regex created. The spec's version of this finding should be read
as superseded by this entry.

### What is actually true

| Flag | Swift | C# |
|---|---|---|
| `-h` | help (`SyxxCLI.swift:88`) | help (`Program.cs:101`) |
| `-V` | version (`SyxxCLI.swift:93`) | version (`SyxxConfig.cs:33`) |
| `-v` | **verbose** (`SyxxConfig.swift:67`) | **version** (`Program.cs:95`, `SyxxConfig.cs:33`) |

`syxx -v` prints a version string on Windows and Linux, and enables verbose output on macOS. A
script written on one platform behaves differently on the other, silently.

### Two decisions, both documented, that contradict each other

- **B240** (`SyxxCLI.swift:91`): *"reserve -V for version (not -v) to avoid collision with -v =
  verbose in SyxxConfig.isVerbose. --version is the canonical long form."*
- **E088** (`SyxxConfig.cs:32`): *"accept both -v and -V for --version to match the top-level
  dispatcher."*

Each is locally reasonable. Together they guarantee the divergence. Note also that C# has **no
verbose flag at all** — `grep -i verbose Windows/dotNET/Syxx.Kit/SyxxConfig.cs` returns nothing —
so E088's cost is invisible today and lands the moment C# gains one.

### Why this is not fixed here despite remediation being in scope

Resolving it changes the meaning of a shipped CLI flag on at least one platform. Following B240 —
the decision with the stronger rationale — means `-v` stops printing a version on Windows and Linux,
which breaks any existing invocation that relies on it. That is an outward-facing behavior change
with a user-visible consequence, and it is the owner's call which way it goes, not an agent's.

**Options:** (a) follow B240 — `-v` becomes verbose everywhere, C# gains verbose parsing, `-v` stops
reporting version; (b) follow E088 — Swift drops `-v` as verbose, which contradicts B240's stated
reason; (c) accept both meanings per platform and document the divergence deliberately, which this
product's own honesty doctrine argues against.
