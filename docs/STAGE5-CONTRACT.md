# Stage-5 engine-to-presentation contract

**Status:** engineer-ready contract for `HANDOFF-CODEX.md` CX-1 through CX-3  
**Generated:** 2026-07-11  
**Normative sources:** `REASONING-SYSTEM-DESIGN.md` §5-§6, the Verification & Honesty Protocol in `AGENTS.md`, and the accepted Stage-5 handoff  
**Ownership:** the engine owns meaning and populates this contract; GUI/CLI code renders it without reconstructing meaning.

This document preserves the original contract's direct/inferred/unavailable partition, answer matrix, three honesty signals, mechanism-class headline, and RPN list, but makes their types and failure behavior precise. It deliberately does **not** prescribe a fusion rule, post-combination belief floor, independence rule, weight, threshold, or user-visible wording.

## 1. Live-tree baseline and integration points

The current model is not sufficient for CX-1 or CX-3:

- macOS `Conclusion` currently exposes singleton `verdict`, integer `confidence`, `evidence`, section statuses, `generatedAt`, `uncertainty`, `conflict`, and singleton `belief` at `macOS/Sources/SyxxKit/Synthesis/Conclusion.swift:11-89`.
- The Windows twin exposes the same core fields plus additive properties at `Windows/Syxx.Kit/Synthesis/Conclusion.cs:271-304`.
- The current report flattens answers into string datapoints at `macOS/Sources/SyxxKit/Synthesis/Report.swift:64-100` and `Windows/Syxx.Kit/Synthesis/Report.cs:51-86`. It is not the canonical answer matrix: each A/AAAA member becomes a separate value, so a multi-address answer can be mistaken for disagreement.
- Text rendering currently reads the legacy conclusion/report at `macOS/Sources/SyxxKit/Synthesis/Report.swift:102-165` and `Windows/Syxx.Kit/Synthesis/Report.cs:88-146`.
- The macOS verdict view consumes `Conclusion` directly at `macOS/Sources/SyxxApp/Views/Components/OverallVerdictView.swift:4-50`; it renders singleton probabilities at `:101-133` and unpartitioned evidence at `:137-175`.
- Windows renders only the legacy verdict/confidence/referral at `Windows/SyxxUI/Views/ConclusionsView.xaml.cs:24-40`.
- CLI conclusion surfaces synthesize and print the legacy object directly at `macOS/Sources/SyxxCLI/SyxxCLI.swift:91-117` and `Windows/Syxx/Program.cs:136-162`.

The engine must add one optional, additive `stage5` property to `Conclusion`. Existing fields remain until all consumers migrate. The presentation lane may build components against these DTOs, but production must render an absent/invalid `stage5` object as unavailable/inconclusive; it may not populate a fake preview object.

## 2. Ownership boundary

### Engine-owned

Engine work belongs in the platform twins under:

- `macOS/Sources/SyxxKit/Synthesis/Conclusion.swift` and the engine/report builders under `macOS/Sources/SyxxKit/Synthesis/**`.
- `Windows/Syxx.Kit/Synthesis/Conclusion.cs` and the twins under `Windows/Syxx.Kit/Synthesis/**`.

The engine owns:

- all IDs, raw observations, provenance and observation lineage;
- expected-datapoint enumeration and unavailable reasons;
- answer-set canonicalization, vantage grouping, consensus status, and discrepancy classification;
- hard-gate applicability and missing required sections;
- whether conflict and uncertainty are display-applicable;
- mechanism class, within-class distribution, unresolved-mechanism state, confidence meaning, and decision basis;
- finding priority factors, priority score, and stable order;
- validation before the object crosses the boundary.

The engine must not place finalized English prose in this DTO. Existing `EvidenceItem.observation`, `rationale`, `Referral`, and `nextSteps` remain legacy fields, not the normative Stage-5 copy contract.

### Presentation-owned

Presentation work belongs in:

- macOS `macOS/Sources/SyxxApp/**`, especially `ConclusionsView.swift` and `Views/Components/OverallVerdictView.swift`;
- Windows `Windows/SyxxUI/**`, especially `Views/ConclusionsView.xaml` and `.xaml.cs`;
- CLI layout at `macOS/Sources/SyxxCLI/SyxxCLI.swift:91-117` and `Windows/Syxx/Program.cs:136-162`;
- the text-layout portion of `Report.renderText`/`RenderText` only after an explicit ownership handoff, because those functions share files with Claude-owned extraction.

Presentation owns layout, typography, accessibility structure, platform-native controls, and lookup of approved localized copy. It must not:

- derive a mechanism class from legacy `Hypothesis` or probabilities;
- calculate consensus, discrepancy, confidence, completeness, conflict applicability, or RPN;
- reinterpret a signature as proof that an observation is true;
- infer evidential independence from act class, section, or vantage;
- invent missing required sections from a Boolean hard gate;
- replace an absent value with zero or a reassuring default.

## 3. Additive root contract

Add this property without changing the legacy constructor's required parameters:

```swift
// Conclusion.swift (engine-owned)
public let stage5: Stage5Presentation?
```

Swift decoding uses `decodeIfPresent`; old persisted conclusions decode with `stage5 == nil`.

```csharp
// Conclusion.cs (engine-owned)
public Stage5Presentation? Stage5 { get; init; }
```

Old JSON without `stage5` deserializes with `Stage5 == null`. The initial production writer emits `schemaVersion: 1` on every new conclusion.

```swift
public struct Stage5Presentation: Codable, Sendable, Equatable {
    public let schemaVersion: Int
    public let generatedAt: String
    public let dataRegime: DataRegime
    public let completeness: EvidenceCompleteness
    public let provenance: ProvenancePartition
    public let answerMatrix: [AnswerMatrixRow]
    public let honesty: HonestySignals
    public let determination: MechanismDetermination
    public let findings: [PrioritizedFinding]
}
```

```csharp
public sealed record Stage5Presentation(
    int SchemaVersion,
    string GeneratedAt,
    DataRegime DataRegime,
    EvidenceCompleteness Completeness,
    ProvenancePartition Provenance,
    IReadOnlyList<AnswerMatrixRow> AnswerMatrix,
    HonestySignals Honesty,
    MechanismDetermination Determination,
    IReadOnlyList<PrioritizedFinding> Findings);
```

`generatedAt` is an RFC 3339/ISO-8601 UTC string with fractional seconds, for example `2026-07-11T21:14:37.123Z`. It represents the same instant as legacy `Conclusion.generatedAt`/`GeneratedAt`. The engine formats it; UI code does not apply locale-dependent conversion to the report's audit anchor.

## 4. CX-1: provenance partition

```swift
public struct ProvenancePartition: Codable, Sendable, Equatable {
    public let direct: [DirectObservation]
    public let inferred: [InferredFinding]
    public let unavailable: [UnavailableDatapoint]
}

public struct DirectObservation: Codable, Sendable, Equatable {
    public let observationId: String
    public let datapointId: String
    public let section: Section
    public let observedAt: String
    public let vantage: VantageIdentity
    public let rawValues: [String]
    public let valueKind: ObservationValueKind
    public let sourceEvidenceIds: [EvidenceId]
}

public enum ObservationValueKind: String, Codable, Sendable {
    case dnsAnswerSet, dnsRcode, dnssecStatus, transport, routeHop
    case contentDigest, resolverIdentity, leakObserver, captureEvent, other
}

public struct VantageIdentity: Codable, Sendable, Equatable {
    public let vantageId: String
    public let kind: VantageKind
    public let endpoint: String?
    public let transport: String?
    public let authenticated: Bool
    public let freshness: FreshnessState
}

public enum VantageKind: String, Codable, Sendable {
    case localSystem, publicResolver, authoritative, dimensionServer
    case packetCapture, localHost, unknown
}

public enum FreshnessState: String, Codable, Sendable {
    case currentRun, freshCached, stale, unknown
}

public struct InferredFinding: Codable, Sendable, Equatable {
    public let findingId: String
    public let mechanismClass: MechanismClass
    public let detailToken: CopyToken?
    public let sourceObservationIds: [String]
    public let sourceEvidenceIds: [EvidenceId]
}

public struct UnavailableDatapoint: Codable, Sendable, Equatable {
    public let datapointId: String
    public let section: Section
    public let reason: UnavailableReason
    public let reasonToken: CopyToken
}

public enum UnavailableReason: String, Codable, Sendable {
    case notRequested, untested, timedOut, invalidated, parseFailed
    case probeFailed, unauthenticated, stale, permissionDenied, dependencyUnavailable
}
```

C# uses records/enums with the same type names, fields, nullability, and JSON values. Arrays are `IReadOnlyList<T>` and are never `null`.

Rules:

1. The three arrays are a partition of the expected presentation datapoints for this invocation. A datapoint cannot be both direct and unavailable.
2. Every inference has at least one source observation and every referenced observation ID exists in `direct`.
3. `rawValues` contains the values actually observed. It is nonempty and cannot contain copy, conclusions, or synthesized consensus prose.
4. Authentication means only that provenance/integrity checks succeeded. It does **not** assert correctness or truth. A signed Dimension response can set `authenticated = true`; it still needs current-run/freshness, query binding, and independent acquisition attributes before the engine may give it whatever evidential role policy allows.
5. No correlation or independence claim is encoded by `mechanismClass`, `section`, or `vantageId`. If the engine needs correlation lineage for scoring, that remains in the engine ledger. The UI never combines evidence.

## 5. CX-1: canonical answer matrix

```swift
public struct AnswerMatrixRow: Codable, Sendable, Equatable {
    public let rowId: String
    public let queryName: String
    public let queryType: UInt16
    public let entries: [VantageAnswer]
    public let comparison: AnswerComparison
}

public struct VantageAnswer: Codable, Sendable, Equatable {
    public let vantage: VantageIdentity
    public let status: AnswerStatus
    public let canonicalAnswers: [String]
    public let rcode: Int?
    public let observationId: String?
}

public enum AnswerStatus: String, Codable, Sendable {
    case answered, noData, nxdomain, servfail, refused, timedOut
    case invalid, unavailable
}

public struct AnswerComparison: Codable, Sendable, Equatable {
    public let classification: AnswerComparisonClass
    public let localVantageIds: [String]
    public let referenceVantageIds: [String]
    public let consensusAnswers: [String]
    public let outlierVantageIds: [String]
}

public enum AnswerComparisonClass: String, Codable, Sendable {
    case agreement, benignSetVariation, discrepancy, insufficientReferences
    case incomparable, notApplicable
}
```

Canonicalization is engine-owned and identical on both platforms:

- normalize domain names to lower-case absolute form without presentation punctuation;
- normalize IPv4/IPv6 textual representation using one documented canonical form;
- de-duplicate exact members;
- sort ordinally by canonical value before serialization;
- preserve RCODE/status independently from the answer set;
- compare sets, never record order;
- classify partial overlap, CDN/geo variation, CNAME context, and insufficient references explicitly. UI code must use `comparison.classification`, not `canonicalAnswers !=`.

`consensusAnswers` is nonempty only when the engine has enough valid reference vantages to declare a consensus. A local answer must never be copied into `consensusAnswers` merely because references are missing. Ground truth is a vantage, not an oracle: a signature authenticates bytes and producer but does not establish freshness, vantage independence, or correctness.

## 6. CX-2: distinct honesty signals

```swift
public struct HonestySignals: Codable, Sendable, Equatable {
    public let hardGate: HardGateSignal?
    public let conflict: ScalarHonestySignal?
    public let uncertainty: ScalarHonestySignal?
}

public struct HardGateSignal: Codable, Sendable, Equatable {
    public let token: CopyToken
    public let blockedVerdicts: [VerdictState]
    public let requiredSections: [Section]
    public let unavailableDatapointIds: [String]
}

public struct ScalarHonestySignal: Codable, Sendable, Equatable {
    public let token: CopyToken
    public let value: Double
}

public enum VerdictState: String, Codable, Sendable {
    case clean, tampered, inconclusive
}
```

The original draft used `hardGate: String?`; that is forbidden because it would finalize engine-authored visible copy and would not give the UI structured section identities. The token selects approved copy; arguments come from typed fields.

Rules:

- `hardGate` is present exactly when a decision-policy gate blocks one or more verdict states. Missing required section names are supplied by the engine, never inferred by UI from `hasHardGatedSection`.
- `conflict` is present only when the engine determines genuine evidential contradiction is user-relevant. Benign context, reliability discount, missing evidence, or competing causal explanation does not itself require a conflict signal.
- `uncertainty` is present only when the raw credal uncertainty value is defined and relevant. It is not completeness.
- scalar values are finite and in `[0,1]`. An absent scalar is “no displayable estimate,” not zero.
- the three signals are independent. Any combination, including all absent or all present, is valid if the engine's validated state supports it.

## 7. CX-3: mechanism determination

```swift
public enum MechanismClass: String, Codable, Sendable {
    case rewrite, block, transport, content, leak, host, unresolved
}

public enum DeterminationState: String, Codable, Sendable {
    case clean, tampered, inconclusive
}

public struct MechanismDetermination: Codable, Sendable, Equatable {
    public let state: DeterminationState
    public let mechanismClass: MechanismClass?
    public let confidence: CredalMeasure?
    public let withinClass: [WithinClassEntry]
    public let basis: DecisionBasis
    public let referralRequired: Bool
    public let referralToken: CopyToken?
}

public struct CredalMeasure: Codable, Sendable, Equatable {
    public let kind: CredalMeasureKind
    public let value: Double
    public let target: String
}

public enum CredalMeasureKind: String, Codable, Sendable {
    case belief
}

public struct WithinClassEntry: Codable, Sendable, Equatable {
    public let hypothesis: Hypothesis
    public let displayOrder: Int
    public let share: Double?
}

public enum DecisionBasis: String, Codable, Sendable {
    case ordinaryCredalGate, decisiveSafetyGate, hardGate, unresolvedMechanism
    case insufficientEvidence
}
```

Rules:

- `state == .clean` requires `mechanismClass == nil` and empty `withinClass`.
- `state == .tampered` may have a named class or `.unresolved`. A threat-union decision with no qualifying class uses `.unresolved`; UI must not select the leading singleton.
- `state == .inconclusive` cannot be green/success.
- `confidence` is the engine-declared `Bel` of its `target`; it is not BetP, completeness, or a post-hoc product. The target is a stable machine identifier such as `mechanism:rewrite`, not visible copy.
- No-data confidence is `nil`, never `0`.
- A decisive safety gate may make `state == .tampered` while the credal measure is low or absent. `basis == .decisiveSafetyGate` tells the renderer to use the separately approved decisive-evidence layout. The engine must not mutate/floor calculated belief after combination merely to raise the printed number.
- `withinClass` is subordinate. Its optional `share` is a within-class display distribution supplied by the engine; it is not headline confidence. Shares, when present, are finite `[0,1]` and sum to `1 ± 1e-9`.

## 8. Completeness and no/partial/full-data regimes

```swift
public enum DataRegime: String, Codable, Sendable {
    case noData, partialData, fullData
}

public struct EvidenceCompleteness: Codable, Sendable, Equatable {
    public let observed: Int
    public let expected: Int
    public let criticalAvailable: Int
    public let criticalExpected: Int
    public let expectedProfileId: String
}
```

`expectedProfileId` identifies the invocation contract, for example a full six-section run versus a Prophet-only command. The UI does not decide whether a user-skipped check belongs in the denominator.

| Regime | Required contract behavior |
|---|---|
| No data | `observed == 0`; all expected datapoints appear in `unavailable`; no direct/inferred entries; no answer consensus; determination is inconclusive with `confidence == nil`; uncertainty may be present only if the engine defines the vacuous state as a displayable value; never emit a numeric zero as “confidence.” |
| Partial data | Every expected datum is direct or unavailable; completeness exposes the gap; critical unavailability produces the appropriate hard gate; a positive/decisive tamper may still produce tampered, but missing data cannot license clean; confidence, if present, is the engine's credal measure and is not multiplied by `observed/expected` in presentation. |
| Full data | `observed == expected`, no expected unavailable entries, and all required validation passed. Full data permits but does not guarantee clean or a confident mechanism; genuine conflict may still require referral. |

`observed`, `expected`, and critical counts are counts of contract-defined datapoints, not merely sections. Counts are nonnegative; `observed <= expected`, `criticalAvailable <= criticalExpected`, and `expected > 0` for a valid invocation profile.

## 9. CX-3: RPN ordering and versioning

```swift
public struct PrioritizedFinding: Codable, Sendable, Equatable {
    public let findingId: String
    public let mechanismClass: MechanismClass
    public let inferenceId: String?
    public let sourceObservationIds: [String]
    public let priority: FindingPriority
}

public struct FindingPriority: Codable, Sendable, Equatable {
    public let modelId: String
    public let modelVersion: Int
    public let factors: PriorityFactors
    public let score: Int
    public let rank: Int
}

public struct PriorityFactors: Codable, Sendable, Equatable {
    public let severity: Int
    public let detectability: Int
    public let occurrence: Int?
}
```

For the initial fallback, use `modelId = "syxx.stage5.sd"`, `modelVersion = 1`, `occurrence = nil`, and `score = severity * detectability`. This is explicitly not full FMEA RPN. When corpus-derived occurrence is approved, introduce a new model ID/version; do not silently change v1 semantics.

The engine emits findings sorted by ascending `rank`; ranks are unique positive integers. Presentation preserves that order. It may not calculate or re-sort by severity, confidence, or legacy probability. Stable ties are broken by an engine-defined deterministic rule, such as ordinal `findingId`, before serialization.

## 10. Copy tokens and approval gate

```swift
public struct CopyToken: RawRepresentable, Codable, Sendable, Hashable {
    public let rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
}
```

C# uses `readonly record struct CopyToken(string Value)`. Tokens are stable machine identifiers such as:

- `stage5.hard_gate.required_checks_missing`
- `stage5.conflict.evidence_disagrees`
- `stage5.uncertainty.evidence_thin`
- `stage5.referral.mechanism_unresolved`
- `stage5.unavailable.timed_out`
- `stage5.basis.decisive_safety_gate`

Tokens are firm contract data and may land before copy approval. The following are copy-gated and cannot be finalized in engine DTOs, SwiftUI, XAML/code-behind, CLI strings, report strings, or accessibility labels until user approval:

- headline/verdict labels;
- direct/inferred/unavailable headings;
- confidence/belief/completeness phrasing;
- all three honesty sentences;
- unresolved/referral wording;
- decisive-safety-gate wording;
- priority-model disclosure and next-step prose.

Presentation resolves tokens through one platform-parity copy table. Unknown tokens fail closed: show an approved generic unavailable/inconclusive message and log the token; never show the raw token as polished user output and never suppress the honesty signal.

## 11. Codable and JSON parity

The golden wire format is UTF-8 JSON with:

- camel-case property names exactly matching the Swift spellings above;
- enum values exactly equal to the lower-camel raw values above;
- arrays always present and never `null`;
- absent optionals encoded as omitted, not empty strings or sentinel numbers;
- finite JSON numbers only; no `NaN`/infinity;
- RFC 3339 UTC timestamps as strings;
- canonical answer and ID arrays in deterministic ordinal order where the contract specifies set semantics;
- `schemaVersion` required and equal to `1` for this version.

Swift uses explicit `CodingKeys` where synthesized names could drift and an ISO-8601 formatter configured for UTC fractional seconds. C# configures `System.Text.Json` with camel-case properties, string enums using the agreed lower-camel converter, and omission of null optionals. Do not rely on platform-default enum or date serialization.

Parity means semantic and byte-canonical parity. Both platforms must deserialize the shared fixture, validate it identically, and re-encode the same canonical bytes after JSON object keys are written in the contract's fixed order.

## 12. Backward and forward compatibility

1. `Conclusion.stage5`/`Stage5` is optional during rollout. Legacy constructors and decoders continue to work.
2. A missing Stage-5 object is not evidence of clean. New presentation surfaces show the approved “Stage-5 detail unavailable” inconclusive state and may fall back to legacy display only where that legacy display remains truthful.
3. Version 1 readers reject unsupported higher `schemaVersion` for semantic rendering and fall closed to unavailable/inconclusive. They may preserve raw JSON for export.
4. New optional fields may be added within version 1 only when omission has an honest meaning. Any change to enum meaning, confidence target, canonicalization, priority formula, or required field increments `schemaVersion` or the relevant `modelVersion`.
5. During migration, legacy `Conclusion.generatedAt` and `stage5.generatedAt` must represent the same instant; legacy verdict/confidence may coexist but Stage-5-capable surfaces use only `stage5.determination`.

## 13. Validation invariants

The engine validates before returning a conclusion. Invalid contract output changes Stage-5 presentation to unavailable/inconclusive and records a diagnostic; it must not be partially rendered as trusted.

1. IDs are nonempty, unique within their namespace, and contain only `[A-Za-z0-9._:-]`.
2. Every inference/finding source observation exists in `provenance.direct`.
3. Every answer entry `observationId`, when present, exists and identifies a DNS answer-set direct observation with the same vantage.
4. Direct and unavailable datapoint identities do not overlap; expected coverage equals the invocation profile.
5. All timestamps parse, and direct observation times are not after `generatedAt` beyond documented clock tolerance.
6. Every scalar is finite and within its declared range.
7. Consensus is absent unless sufficient valid reference vantages exist; local-only data cannot become public consensus.
8. `benignSetVariation` and `agreement` cannot list outlier vantages; `discrepancy` requires at least one outlier and one valid reference.
9. Clean is impossible with a hard gate blocking clean, critical missing data, a positive decisive finding, or unresolved tamper.
10. No-data is inconclusive with nil confidence and zero inferred findings.
11. A named mechanism class is engine-supplied and compatible with state; UI never repairs an incompatible pair.
12. Decisive basis does not authorize changing belief. It only records why decision policy forced the state.
13. Priority scores equal the declared model formula, ranks are unique, and list order matches rank.
14. Authentication/freshness fields are descriptive. No invariant equates `authenticated == true` with truth.
15. Nothing in this DTO declares evidence independent. Independence/correlation remains an engine-ledger decision based on full lineage, not act class alone.

## 14. Required automated tests

Each happy-path test requires a mutation/failure case that proves it can fail.

### Shared DTO/parity tests

- Swift and C# decode one shared v1 fixture and produce equivalent object graphs. Failure: change an enum spelling or field name on one platform.
- Canonical encode is byte-identical. Failure: change date formatting, answer order, null handling, or enum casing.
- Legacy conclusion JSON without `stage5` decodes. Failure: presentation treats absence as clean or numeric zero confidence.
- Unsupported schema version fails closed. Failure: reader renders it as a valid determination.
- Every validation invariant has a negative fixture: duplicate ID, dangling source link, invalid scalar, fabricated consensus, contradictory state/class, post-generation timestamp, wrong RPN score, or rank disorder.

### CX-1 tests

- Locally poisoned answer fixture retains local and each public/authoritative answer side-by-side. Failure: remove the reference answer or label local as consensus.
- Multi-address CDN sets in different order canonicalize to agreement/benign variation, not discrepancy. Failure: compare record order or individual members.
- Timed-out/invalid/unrequested expected datapoints all appear in unavailable with distinct reasons. Failure: silently drop one.
- Every inferred finding links to a direct observation. Failure: sever or invent the link.
- Signed but stale/unbound ground truth cannot be represented as current authenticated truth. Failure: signature alone upgrades freshness or consensus.

### CX-2 tests

- Exercise all eight present/absent combinations of hard gate, conflict, and uncertainty. Failure: one signal controls another's visibility.
- Partial run names the engine-supplied required sections/datapoints. Failure: UI hardcodes Mirror/Glutton or omits one.
- Benign context/reliability discount does not fabricate conflict. Failure: context alone creates the conflict token.
- Timestamp is exact RFC 3339 UTC on GUI view model and CLI formatter. Failure: locale-only time or missing timezone.

### CX-3 tests

- DNSSEC-strip fixture yields `state=tampered`, `mechanismClass=rewrite`, subordinate spoofing/on-path entries. Failure: singleton becomes headline.
- Threat union with no qualifying class yields `.unresolved` plus referral. Failure: UI chooses the largest singleton.
- Decisive safety-gate fixture may carry low/nil belief without any post-hoc floor. Failure: builder edits the belief number or generic UI prints a contradictory “20% confidence” phrase.
- v1 findings arrive in S×D order with model/version disclosure. Failure: UI recomputes order or labels it full FMEA.

### Presentation tests

- Semantic state-to-color mapping: clean is the only success/green state; tampered and inconclusive are distinct non-green states. Failure: missing Stage-5 data or incomplete data renders green.
- Token lookup parity between macOS, Windows, and CLI. Failure: unknown token disappears or exposes misleading copy.
- Snapshot/accessibility structure preserves DIRECT, INFERRED, UNAVAILABLE, honesty signals, time anchor, headline, subordinate split, and priority order without testing unapproved literal wording.

## 15. Real-run acceptance bars

Tests are necessary but do not complete CX work.

### CX-1

On both production platform paths, run against a controlled locally poisoned resolver while at least two independent public/authoritative vantages remain reachable. Capture UI and CLI output showing the local outlier and true reference answers simultaneously with vantage identity. Then run a real multi-homed CDN domain and verify answer-order/set variation does not render as a discrepancy. Record raw DNS inputs and rendered output.

### CX-2

On each platform, capture three production runs: a partial run with required checks unavailable, a genuinely contradictory-vantage run, and a thin-evidence run without genuine contradiction. Verify the hard-gate, conflict, and uncertainty structures appear independently and every report includes the same RFC 3339 generation instant as the engine result.

### CX-3

On each platform, replay or safely generate a real DNSSEC-strip capture through the production entry point. Verify REWRITE is the headline, spoofing/on-path is subordinate, and referral is not caused merely by the within-class split. Run a decisive-safety-gate case and verify the approved layout distinguishes the determination basis from the raw credal measure without altering it. Run at least two simultaneous findings and verify the rendered order matches engine ranks.

Windows GUI verification must run on Windows/WinUI; a `.NET` library test on macOS is not a Windows GUI real run. Network/dependency failures are reported rather than converted to fixtures or false completion.

## 16. Dependency-aware landing order

1. Engine: add DTO twins, JSON settings, validator, and shared fixtures; no behavior change.
2. Engine: populate provenance/unavailable and canonical answer matrix from the evidence ledger; run CX-1 real inputs.
3. Presentation: wire CX-1 components and CLI formatter using tokens; no finalized copy.
4. Engine: populate structured honesty signals and RFC 3339 anchor; presentation wires CX-2.
5. Engine: populate finalized mechanism-class determination, basis, subordinate distribution, and versioned priority order; presentation wires CX-3.
6. Approve one parity copy table, then enable visible strings across macOS, Windows, CLI, reports, and accessibility.
7. Run the real acceptance matrix and only then mark CX-1 through CX-3 complete.

No phase may temporarily map missing Stage-5 data to clean, fabricate an answer matrix from legacy flattened datapoints, or infer engine semantics in the UI.
