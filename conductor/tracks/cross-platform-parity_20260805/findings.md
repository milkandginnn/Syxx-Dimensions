# Findings — cross-platform-parity_20260805

Detection-only track. Every entry here is **filed, not fixed**. Each carries one of the five states
from `conductor/workflow.md` and `file:line` evidence.

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
