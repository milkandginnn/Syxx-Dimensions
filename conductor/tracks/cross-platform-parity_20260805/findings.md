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

**State:** `verified-by-real-run` (fixed 2026-08-05, Tasks 8.3 + 8.4 + 8.10) · **Found by:** A-03 · **Severity:** High

**Fix:** three coupled changes. (8.4) unescaped `/` declared canonical — Swift `defaultSpecJSON`
gains `.withoutEscapingSlashes`, matching C#'s deliberate `UnsafeRelaxedJsonEscaping`; the
function's only callers are the oracle tests, so no wire format moves. (8.10/F-09) B156 ported to
C# so both builders emit `mappableIndicators`. (8.3) oracle regenerated from the reference
platform via an env-gated generator (`_MatrixSpecOracleGenerator.swift`), under the
spec-version-bump protocol, with a mechanical hand-review: 309 scalar constants deep-equal under
parse; the byte diff decomposes into exactly three declared classes (slash unescaping, sorted
`knownSections`, added `mappableIndicators`). **Both platforms now match the same oracle bytes**
— which is itself the first cross-serializer byte-parity result in the repo.

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

**State:** `verified-by-real-run` (fixed 2026-08-05, Task 8.9) · **Found by:** Task 8.2 · **Severity:** Medium

**Fix:** the vector-evaluation and classification twins now exist on C#, including the
evaluated-count assertion. Original finding:

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

### Addendum — 2026-08-05, verified by execution: Swift's top-level `-V` is dead code

The table above is right about intent and about the subcommand path, but the top-level Swift row
overstates. `SyxxCLI.swift:48` lowercases `args[1]` **before** the dispatch switch, so `-V`
arrives as `-v`, matches neither `"--version"` nor `"-V"` at `SyxxCLI.swift:93`, and falls through
to `default` — "Unknown subcommand". Measured against the built binary:

```
syxx --version   exit=0   Syxx 2.0.0
syxx -V          exit=3   Unknown subcommand '-V'. Use 'syxx --help' for usage.
```

The subcommand path is unaffected — `SyxxConfig.handleGlobalFlags`
(`macOS/Sources/SyxxKit/SyxxConfig.swift:48-63`) checks the **un**-lowercased argv, so
`syxx nslook -V` works. Swift therefore disagrees with itself between levels, and B240's
reserved `-V` is defeated by the lowercasing at exactly the level where C# accepts it
(`Program.cs:96`). `CLIExitCodeProcessTests.swift:42-61` tests only the long forms, which is
why the dead branch never surfaced. Any remediation of F-08 must fix the lowercasing as well
as pick a winner between B240 and E088.

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

---

## F-09 · B156 (`mappableIndicators`) was Swift-only — the engines computed coverage differently

**State:** `verified-by-real-run` (fixed 2026-08-05, Task 8.10) · **Found by:** Task 8.3 pre-flight · **Severity:** High

### What was missing

C# had the source data (`SynthesisEngine.cs` `IndicatorMapping`) but neither the spec field nor the
coverage filter (Swift: `MatrixDeterminativeEngine.swift:789-800`). Support rules referencing
unmappable indicators were permanent dead weight in the C# denominator, hard-capping coverage —
diagnoses reachable on macOS could be unreachable on Windows and Linux from identical evidence.

### Fix

`MatrixSpec.MappableIndicators` + `SortedIndicatorIdSetConverter` + the engine filter + builder
wiring, all twinned against the Swift lines.

### Honesty notes

- **The shared golden vectors do not discriminate on B156.** Verified by neutralizing the filter:
  gv-1/gv-3 pass either way. gv-3's stored `coverage` (0.560976) equals the *unfiltered* ratio
  2.3/4.1, so that stored number predates B156 — the comparisons read state and diagnosis IDs, not
  the numeric block, on both platforms. The discriminating evidence is the twin
  `MappableIndicatorsCoverageTests` (half the support weight unmappable; emitted with the filter,
  not emitted without), proven red with the filter neutralized. B156 itself had no test on either
  platform before this.
- **gv-3's numeric block is now stale on both platforms.** Informational-only today; whether to
  regenerate the vectors is a separate decision, deliberately not folded into 8.3.
- **My builder edit was initially incomplete** — it computed the set and never passed it. The C#
  oracle test caught it (emission jumped `knownSections`→`thresholds`). The gate caught a real
  wiring bug before it shipped, which is the argument for the gate.

---

## F-10 · `guided` is a Windows-only top-level command

**State:** `open` · **Found by:** Task 3.2 (CLI surface gate) · **Severity:** Low

### Evidence

| | |
|---|---|
| C# | `Windows/dotNET/Syxx/Program.cs:66` — `args.Length == 0 \|\| args[0] == "guided"` enters interactive mode |
| Swift | interactive mode only on zero args; `syxx guided` falls to `default` and exits 3 "Unknown subcommand" |

### What it means

An invocation that works on Windows/Linux is an operational error on macOS. Anything teaching
`syxx guided` (docs, scripts, support answers) breaks on one platform.

---

## F-11 · the `--json` global flag exists only on macOS

**State:** `open` · **Found by:** Task 3.2 (CLI surface gate) · **Severity:** High — an output-contract asymmetry

### Evidence

| | |
|---|---|
| Swift | `macOS/Sources/SyxxKit/SyxxConfig.swift:72` — global `--json` handled for every subcommand |
| C# | absent from all of `Windows/dotNET/` (confirmed by grep, independent of the gate) |

### What it means

Machine-readable output exists on one platform only. Automation written against macOS `--json`
gets human-formatted text (or an unknown-option path) on Windows/Linux — the exact class of
divergence the CLI-parity acceptance criterion exists to catch.

---

## F-12 · `cage` accepted-flag surface diverges — the capture-mechanism family

**State:** `open` · **Found by:** Task 3.2 (CLI surface gate); mechanism split documented in command-triage.md difference 6 · **Severity:** Medium

### Evidence

| | |
|---|---|
| Swift-only | `--interface` / `-i` (`macOS/Sources/SyxxCLI/Commands/SyxxCage.swift:84`), `--owner-uid` (`:95`), `--owner-gid` (`:104`) |
| Windows-only | `--out` legacy alias (`Windows/dotNET/Syxx/Commands/CageCommand.cs:71`; explained `:55-70` as a D108 alias, no finding id) |
| Root | libpcap+PKTAP capture on macOS vs raw sockets on Windows (command-triage.md §6) |

### What it means

The flag asymmetry is the user-visible edge of the deeper capture-mechanism split. Interface
selection and output-ownership control exist on one platform only; the other platform carries an
undocumented alias. Any cross-platform cage invocation must use the lowest common denominator, and
nothing tells the user what that is.

---

## F-13 · `firstrun --local` / `-l` accepted only on macOS

**State:** `open` · **Found by:** Task 3.2 (CLI surface gate) · **Severity:** Low

### Evidence

| | |
|---|---|
| Swift | `macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift:38` — recognised, exits 3 "not yet supported" |
| C# | unknown option, exits 3 "Unknown option" |

### What it means

Same exit code, different contract: macOS reserves the flag and says so; Windows denies it exists.
The messages differ, so output capture diverges even though the exit codes agree.

---

## F-14..F-26 · Effect-surface divergences from the Task 4.1 triage

Filed 2026-08-05 from [command-triage.md](./command-triage.md), which carries the full file:line
evidence per item; each entry below is the finding of record with its own ledger row. All are
**State: `open`**, found by Task 4.1, severity as marked.

### F-14 · `witness verify` has no clock seam on C# — High

Swift `verifySnapshotDetailed(…, now: Date = Date())` (`WitnessSigner.swift:246-251`); the C#
signature has no `now` (`WitnessSigner.cs:232-233`), so expired/stale/future-dated conclusions read
the wall clock with no injection point. Blocks hermetic `witness verify` fixtures on Windows/Linux
(Tasks 4.2/4.3) — a code change, not a test change. (triage difference 1)

### F-15 · `unbound verify-dnscrypt` exists only on macOS — Medium

Dispatched and implemented on Swift (`SyxxUnbound.swift:24,39-73`); no case in the C# switch
(`UnboundCommand.cs:21-28`). Windows/Linux have no DNSCrypt-chain verification at all. Already
gate-ledgered as D3; this row makes it a finding of record. (triage difference 2)

### F-16 · `witness` trust-anchor sources differ per platform — High

Swift resolves `CollectorPinning.configuredTrustedSigningKeys()` at the call sites
(`SyxxWitness.swift:304,344,422`); C# uses `WitnessPinning.ConfiguredTrustedWitnessKeys()` via
verifier auto-load (`WitnessCommand.cs:735,480,515,611`). Different environment variables anchor
each platform, and macOS conflates the collector trust set with the witness signing set (documented
at `SyxxWitness.swift:501-507`). The same snapshot can verify `valid` on one platform and
`untrustedSigner` on the other. (triage differences 3 + 14)

### F-17 · `mirror` with no detectable system resolver: Swift refuses, C# queries 127.0.0.1 — High

`SyxxMirror.swift:28-32` returns inconclusive; `MirrorCommand.cs:29-36` silently substitutes
`127.0.0.1` and proceeds — emitting a query Swift never sends and reaching a conclusion Swift
deliberately withholds on identical evidence. Swift's system read also precedes `--local` parsing
(`:27` vs `:35`), so `--local` cannot rescue such a host on macOS. (triage difference 4)

### F-18 · Exit codes are largely absent on the C# side — High

Only mirror/leak/warp/witness/firstrun set `Environment.ExitCode`; ten commands inherit the seeded
2 (`Program.cs:25`). Concrete inversions: `key` with a DETECTED captive portal exits 1 on macOS
(`SyxxKey.swift:60-62`) and 2 on Windows; `forensics --classify` on a flagged domain exits 1 vs 2
(`SyxxForensics.swift:57`). The documented 0/1/2/3 contract (`Program.cs:19-24`) is declared on
both platforms and honored on one. (triage difference 5; includes the filter exit-code note)

### F-19 · `cage` deletes the raw capture even when the evidence export failed — C# only — High

Swift refuses to delete the raw capture when the queries-JSON export failed, retaining the only
surviving evidence artifact (`SyxxCage.swift:402-407`); C# deletes whenever `--keep` was absent,
regardless (`CageCommand.cs:292-319`). Evidence-loss divergence. (triage difference 7)

### F-20 · `cage` reporting honesty gaps on C#: no transport disclosure, no capture counters — Medium

Swift prints that only cleartext UDP/TCP:53 is visible and DoH/DoT/DoQ are structurally invisible
(`SyxxCage.swift:336-338`) and threads six capture counters into the report (`:343-358`); C# does
neither (`CageCommand.cs:245-251`), so an empty Windows capture reads as "no DNS activity
occurred". (triage differences 8 + 9)

### F-21 · `sentinel` Swift-only behaviors: corrupt-baseline quarantine, change log, tri-state, save-failure surfacing — Medium

Swift quarantines a corrupt baseline and re-establishes (`SyxxSentinel.swift:89-94`); C# leaves it
and returns (`SentinelCommand.cs:95-100`). Swift keeps an append-only JSONL change log
(`:121,139-159`); no C# equivalent. Swift prints tri-state `dnsEncrypted` (`:27`) vs C# boolean
(`:51`). Swift surfaces persistence failures (`:36,72,85`); C# ignores every Save result
(`:56,89,103,123`). (triage difference 10)

### F-22 · `lan` on C# states an unsound categorical claim; Swift hedges — Medium

C# prints that resolvers absent from the ARP cache "are reached through the gateway (off-LAN)"
(`LanCommand.cs:106`) — a resolver merely not contacted recently is also absent. Swift hedges
(`SyxxLan.swift:76-78`), prints the ARP-unauthenticated caveat (`:65-66`), and persists the
snapshot for future diffs (`:83`, no C# equivalent). (triage difference 11)

### F-23 · `leak` collector path on C# hardcodes "1 resolver observed" and drops signature state — High

`LeakCommand.cs:132` prints a fixed count on NotDetected — a fabricated value rendered as measured,
the exact class product rule 4 forbids. C# also prints no signature-validity state (`:147-151`) and
collapses `.invalid` into the default branch (`:135-138`) where Swift maps it to inconclusive
(`SyxxLeak.swift:101-103`; full state rendering `:88-135`). (triage difference 12)

### F-24 · `witness --gen-keypair` presence probe fails open on macOS — Medium

C# distinguishes Present/Absent/Undetermined by opening the file and refuses on Undetermined even
with `--force` (`WitnessCommand.cs:124-142,796-819`); Swift uses `fileExists` (`SyxxWitness.swift:72`)
— the mode the C# comment itself names "M-01 on macOS". macOS can mint over a present-but-unreadable
key, orphaning previously sealed snapshots. (triage difference 13)

### F-25 · `route` timeout and target validation differ — Low

Swift `timeout: 20` (`SyxxRoute.swift:25`) vs C# `waitMs: 1000` (`RouteCommand.cs:33`); different
truncation on slow paths, and C# labels the failure reason where Swift prints one generic line.
Character-allowlist vs `TryParse/CheckHostName` validation accept different target strings
(`:40-41` vs `:67`). (triage difference 15)

### F-26 · `firstrun` does not thread license state on Windows; result banners differ — Medium

Swift computes `isPro` and passes it into the scan (`FirstRunScan.swift:82-86`); C# calls
`RunAsync(domain, progress)` without it (`FirstRunCommand.cs:75`) — a licensed user gets different
scan coverage per platform. Banner "CONCLUSIONS" vs "RESULTS" (`:91` vs `:87`); suppressed-signals
line format differs (`:146` vs `:143`). The nslook DNSSEC-rendering note from the triage rides
here as a transcript-parity risk. (triage difference 16 + smaller notes)

---

## F-27 · The two vector loaders disagree on unknown NESTED members — the silent-drop defect, one level down

**State:** `verified-by-real-run` (found and fixed 2026-08-05) · **Found by:** Task 2.1 (csharp-loader seat raised it; lead reproduced) · **Severity:** High — a corpus could load on one engine and be refused by the other, and the loading engine would be silently testing something the vector did not say

### Evidence — reproduced on this host

Vector with a misspelled nested member (`scopePrefixLenght` inside `ecs`):

```
swift:  caseCount=1 / typo-nested          exit 0   ← accepted, member silently dropped
c#:     case 001_typo-nested.json: … unknown member 'scopePrefixLenght' at ecs.scopePrefixLenght
                                          exit 3   ← refused
```

Root cause: the Swift loader delegated nested objects to each type's synthesized `Decodable`, and
Swift's synthesized conformances ignore unrecognised members by design. So the unknown-key sweep
covered the top level of `sectionInputs` and nothing beneath it. The C# loader hand-walks every
level and refuses at any depth.

### Why it matters

`docs/parity-vector-v1-spec.md` calls reject-never-ignore "the single most important rule in the
format", because a silently dropped input means a vector claims to exercise a field that nothing
read — a coverage hole that renders as a pass. The rule was enforced at depth 1 and abandoned at
depth 2 on one platform. A vector with a typo'd probe member would have been recorded as covering
that probe while testing its default.

### Fix

The Swift loader adopts the stricter behavior (the relaxed side moves, never the strict one): every
nested object is validated against its known member set before decoding, and an unknown member at
any depth is refused, naming the dotted path. Same corpus, same answer, both engines.

### Adjacent divergences raised with it, NOT fixed here

- **`drifts` is accepted by Swift and refused by C#** as v1-unrepresentable. Two independent reasons
  it cannot stand as-is: `AnswerDrift.DetectedAt` is a date (the format bans dates, and the two
  runtimes disagree on the wire form — Foundation writes seconds-since-2001, and C#'s only reader
  for that convention exists because naive conversion lands on the wrong millisecond ~1 in 1000,
  `WitnessWireJson.cs:148-163`), and Swift's `AnswerDrift` carries an `id: UUID`
  (`AnswerLedger.swift:39`) that the C# type has no member for (`Trust.cs:25-33`). The current
  corpus uses `configChanges` for Witness, so nothing is blocked today. **Owner/lead decision:**
  refuse on both sides, admit a dateless drift shape, or record `drifts` as out of v1 scope the way
  `resolverSubstituted` is.
- **Duplicate JSON members**: C# refuses, Swift takes last-wins. Same direction as the above — the
  strict side is right, but changing it is a contract clarification the spec should carry first.

### F-27 fix verification (2026-08-05)

- **CLI real run, before:** Swift `caseCount=1` exit 0 / C# refusal exit 3 on the same file.
- **CLI real run, after:** both refuse, both name `ecs.scopePrefixLenght`, both exit 3.
- **Corpus unaffected:** `corpus/parity-vectors` still loads 11/11 on Swift, and the full-corpus
  transcripts remain identical across engines under `jq --sort-keys`.
- **Unit tests: NOT RUN.** Two regression tests were added
  (`testUnknownNestedMemberIsRejectedByPath`, `testCorrectlySpelledNestedMembersLoad`) but
  `swift test` builds every target and `macOS/Sources/SyxxApp/SyxxApp.swift` is mid-refactor and
  uncommitted from a concurrent session, so the suite cannot compile. The tests are
  `fixed-unverified` until that clears; the behavior claims above are `verified-by-real-run`
  through the CLI, which is the stronger evidence for the defect itself.
- **Implementation note:** rather than hand-maintaining 32 member lists that would drift from the
  types, each decoded field is re-encoded and compared against the bytes that produced it. Four
  fields (`nxCanary`, `dnssecCanary`, `leak`, `exfil`) cannot be round-tripped because their models
  are not `Encodable`; their nested members remain unchecked on the Swift side and that gap is
  recorded here rather than hidden.
