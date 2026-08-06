import Foundation

// MARK: - MatrixDeterminativeEngine
//
// The matrix-based determinative core. It turns a bundle of section results and observed
// indicators (produced by Prophet / Mirror / Portal / Glutton / Cage / Witness) into a
// `DeterminativeResult`: an overall tri-state result plus the ranked set of diagnoses that the
// evidence actually supports, each with the metrics that justify it.
//
// HARD INVARIANT (from the reasoning-design discussion, not optional): the non-scored states —
// untested, invalidated, and inconclusive — are NEVER emitted as a ranked/scored diagnosis. They
// are represented by the overall `DeterminativeResult.state` (`.inconclusive`) with an *empty*
// ranked list and an explanation. A diagnosis appears in `result.diagnoses` only when the
// evidence affirmatively reaches at least the `.probable` tier for it.
//
// CRITICAL DISTINCTION: `.inconclusive` is categorically different from `.untested` and
// `.invalidated`. `.inconclusive` means the check RAN, produced VALID data, but the data did
// not resolve to a clear finding — it is USABLE evidence (EvidenceUsability.isUsable == true)
// and does NOT hard-gate diagnoses that depend on that section. `.untested` (never ran) and
// `.invalidated` (ran but produced corrupted/untrustworthy data) are NOT usable and DO
// hard-gate. Conflating these three states — treating a valid-but-ambiguous run the same as a
// missing or corrupted one — is a structural defect that misclassifies working evidence.
//
// Missing / not-observed indicators (nil `observedStrength`) are treated as "not observed" —
// they lower coverage and push toward abstention; they are never read as support and never
// manufacture a confident result.
//
// Honesty-over-reassurance is enforced structurally:
//  * A required section that is missing, untested, invalidated, or locked HARD-GATES the
//    diagnoses that depend on it — a gated diagnosis cannot be emitted (fail-closed).
//    `.inconclusive` sections do NOT hard-gate (see EvidenceUsability.isUsable).
//  * `clean` cannot be emitted while any threat diagnosis carries real observed support that was
//    suppressed by a hard gate / missing evidence, or that fired but fell below the reporting tier
//    (C062 `belowTier`) — an unverifiable or unconfirmed threat signal forces `.inconclusive`,
//    never a reassuring `.clean`. Overhang admission and the clean-blocking re-check both measure
//    the un-attenuated observed support (C065): gating crushes strength and thin sections crush
//    quality, and neither may hide a signal that actually fired.
//  * A dampener (low `quality`, a `contradictory` flag) can lower a diagnosis's fit but the
//    exclusionary / contradiction mass is bounded — it can attenuate, never silently zero, an
//    otherwise-present threat, and a genuinely-firing exclusionary indicator dominates by hard gate
//    rather than by arithmetic cancellation.

// MARK: Identifier value types

/// Opaque string identifiers. Kept as distinct wrapper types so a section id can never be passed
/// where an indicator id is expected, etc. `Codable` as a bare string for compact JSON.
public struct SectionID: RawRepresentable, Hashable, Codable, Sendable, CustomStringConvertible {
    public let rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue }
    public var description: String { rawValue }
    public init(from decoder: Decoder) throws { rawValue = try decoder.singleValueContainer().decode(String.self) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public struct IndicatorID: RawRepresentable, Hashable, Codable, Sendable, CustomStringConvertible {
    public let rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue }
    public var description: String { rawValue }
    public init(from decoder: Decoder) throws { rawValue = try decoder.singleValueContainer().decode(String.self) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public struct DiagnosisID: RawRepresentable, Hashable, Codable, Sendable, CustomStringConvertible {
    public let rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ rawValue: String) { self.rawValue = rawValue }
    public var description: String { rawValue }
    public init(from decoder: Decoder) throws { rawValue = try decoder.singleValueContainer().decode(String.self) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

// MARK: Evidence usability
//
// NOTE ON THE `SectionStatus` NAME COLLISION (deliberate design decision):
// SyxxKit already ships a public `SectionStatus` in `Conclusion.swift` whose semantics answer
// "did this check run and what did it FIND" (untested / invalidated / inconclusive / degraded /
// positive / negative). The matrix engine needs a *different* axis: "is this section's output
// USABLE as a scoring input, and how do we treat it at the required-section gate." Reusing the
// name would be a duplicate `public enum SectionStatus` in the same module — a compile error — and,
// worse, would conflate two genuinely different questions. So the engine defines its own
// `EvidenceUsability`, and the mapping from a real `Conclusion.SectionStatus` is made explicit in
// `EvidenceUsability.init(sectionStatus:)` below rather than guessed silently.
public enum EvidenceUsability: String, Codable, Sendable, CaseIterable {
    /// The section produced usable, self-consistent output that resolved to a finding.
    case passed
    /// The section ran but reached a definite negative/failed outcome that is itself usable input.
    case failed
    /// The section ran but its output does not resolve — usable only as an abstention signal.
    case inconclusive
    /// The section ran but a required premise failed; its output must not be trusted as input.
    case invalidated
    /// The section never ran (skipped, no privilege, no collector, missing input).
    case untested
    /// The section is deliberately unavailable in this build/context (feature-gated).
    case locked

    /// Can this section's output be used as positive scoring evidence at all?
    /// `.invalidated`, `.untested`, `.locked` are NOT usable — they hard-gate any diagnosis that
    /// requires the section. `.passed`/`.failed`/`.inconclusive` are usable (with caveats).
    public var isUsable: Bool {
        switch self {
        case .passed, .failed, .inconclusive: return true
        case .invalidated, .untested, .locked: return false
        }
    }
}

public extension EvidenceUsability {
    /// The documented, explicit mapping from a real Syxx-run section status
    /// (`Conclusion.SectionStatus`) onto the engine's usability axis. This is a real design
    /// decision, spelled out here so it is auditable rather than implicit:
    ///
    ///  * `.untested`     → `.untested`     — never ran ⇒ no usable evidence (hard-gates requirers).
    ///  * `.invalidated`  → `.invalidated`  — ran but premise failed ⇒ not trustworthy input.
    ///  * `.inconclusive` → `.inconclusive` — ran, no resolution ⇒ usable only as abstention.
    ///  * `.degraded`     → `.passed`       — ran with partial (noncritical) data ⇒ usable input;
    ///                                        the per-indicator `quality`/`observedStrength` carries
    ///                                        the attenuation, so the section itself is not gated.
    ///  * `.positive`     → `.passed`       — ran and found something ⇒ usable input.
    ///  * `.negative`     → `.failed`       — ran and found nothing ⇒ usable input (a real negative).
    init(sectionStatus: SectionStatus) {
        switch sectionStatus {
        case .untested:     self = .untested
        case .invalidated:  self = .invalidated
        case .inconclusive: self = .inconclusive
        case .degraded:     self = .passed
        case .positive:     self = .passed
        case .negative:     self = .failed
        }
    }
}

// MARK: Rule vocabulary

public enum IndicatorPolarity: String, Codable, Sendable {
    /// Observing this indicator (at strength) is evidence FOR the diagnosis.
    case supports
    /// Observing this indicator is evidence AGAINST the diagnosis (contradiction / exclusion).
    case excludes
}

/// Evidential weight class of an indicator within a diagnosis. Used by the
/// `requireDecisiveOrTwoMajorsForConclusive` gate: a conclusive result must rest on at least one
/// decisive observation or two independent majors, never on minors alone.
public enum EvidenceClass: String, Codable, Sendable, Comparable {
    case minor
    case major
    case decisive

    private var order: Int {
        switch self { case .minor: return 0; case .major: return 1; case .decisive: return 2 }
    }
    public static func < (lhs: EvidenceClass, rhs: EvidenceClass) -> Bool { lhs.order < rhs.order }
}

/// One rule inside a `DiagnosisSpec`: "for diagnosis D, indicator I with this polarity, at roughly
/// this expected strength, matters this much, is this class of evidence, belongs to this
/// independence group, and its partial-strength contribution decays with this exponent."
public struct DiagnosisIndicatorRule: Codable, Sendable, Equatable {
    public let indicatorID: IndicatorID
    public let polarity: IndicatorPolarity
    /// The strength at which this indicator is considered fully present (0...1).
    public let expectedStrength: Double
    /// Relative importance of this rule within the diagnosis. May exceed 1 (relative, not a mass).
    public let weight: Double
    public let evidenceClass: EvidenceClass
    /// Rules sharing an independence group provide correlated (not independent) support; the engine
    /// counts distinct groups toward `minimumIndependentSupportGroups...`.
    public let independenceGroup: String
    /// How sharply a partial observed strength is discounted: `alignment^decayExponent`.
    /// >1 penalises weak-but-present signals harder (used on decisive rules). Default 1.0 (linear).
    public let decayExponent: Double

    public init(indicatorID: IndicatorID,
                polarity: IndicatorPolarity,
                expectedStrength: Double,
                weight: Double,
                evidenceClass: EvidenceClass,
                independenceGroup: String,
                decayExponent: Double = 1.0) {
        self.indicatorID = indicatorID
        self.polarity = polarity
        self.expectedStrength = expectedStrength
        self.weight = weight
        self.evidenceClass = evidenceClass
        self.independenceGroup = independenceGroup
        self.decayExponent = decayExponent
    }
}

/// Thresholds that gate whether a diagnosis is emitted, and at what confidence tier.
public struct ThresholdProfile: Codable, Sendable, Equatable {
    public let minimumCoverageForProbable: Double
    public let minimumCoverageForConclusive: Double
    public let minimumFitForProbable: Double
    public let minimumFitForConclusive: Double
    public let minimumMarginForProbable: Double
    public let minimumMarginForConclusive: Double
    public let maxContradictionForConclusive: Double
    public let maxContradictionForProbable: Double
    public let minimumIndependentSupportGroupsForProbable: Int
    public let minimumIndependentSupportGroupsForConclusive: Int
    /// A conclusive result must rest on a decisive observation or ≥2 major observations.
    public let requireDecisiveOrTwoMajorsForConclusive: Bool
    /// If a required section is entirely missing from the bundle, gate the diagnosis (fail-closed).
    public let hardGateOnMissingRequiredSections: Bool
    /// If a required section is present but `.invalidated`, gate the diagnosis (fail-closed).
    public let failClosedOnInvalidatedRequiredSections: Bool

    public init(minimumCoverageForProbable: Double,
                minimumCoverageForConclusive: Double,
                minimumFitForProbable: Double,
                minimumFitForConclusive: Double,
                minimumMarginForProbable: Double,
                minimumMarginForConclusive: Double,
                maxContradictionForConclusive: Double,
                maxContradictionForProbable: Double,
                minimumIndependentSupportGroupsForProbable: Int,
                minimumIndependentSupportGroupsForConclusive: Int,
                requireDecisiveOrTwoMajorsForConclusive: Bool,
                hardGateOnMissingRequiredSections: Bool,
                failClosedOnInvalidatedRequiredSections: Bool) {
        self.minimumCoverageForProbable = minimumCoverageForProbable
        self.minimumCoverageForConclusive = minimumCoverageForConclusive
        self.minimumFitForProbable = minimumFitForProbable
        self.minimumFitForConclusive = minimumFitForConclusive
        self.minimumMarginForProbable = minimumMarginForProbable
        self.minimumMarginForConclusive = minimumMarginForConclusive
        self.maxContradictionForConclusive = maxContradictionForConclusive
        self.maxContradictionForProbable = maxContradictionForProbable
        self.minimumIndependentSupportGroupsForProbable = minimumIndependentSupportGroupsForProbable
        self.minimumIndependentSupportGroupsForConclusive = minimumIndependentSupportGroupsForConclusive
        self.requireDecisiveOrTwoMajorsForConclusive = requireDecisiveOrTwoMajorsForConclusive
        self.hardGateOnMissingRequiredSections = hardGateOnMissingRequiredSections
        self.failClosedOnInvalidatedRequiredSections = failClosedOnInvalidatedRequiredSections
    }
}

/// The full specification of one diagnosis: its rules, the sections it depends on, the indicators
/// whose firing hard-excludes it, and its relationships to other diagnoses.
public struct DiagnosisSpec: Codable, Sendable, Equatable {
    public let id: DiagnosisID
    public let displayName: String
    public let rules: [DiagnosisIndicatorRule]
    /// Sections that MUST have produced usable output for this diagnosis to be emittable.
    public let requiredSections: [SectionID]
    /// Indicators whose confirmed firing makes this diagnosis impossible (hard exclusion).
    public let exclusionaryIndicators: [IndicatorID]
    /// Diagnoses that cannot co-hold with this one; the stronger of a conflicting pair wins.
    public let mutuallyExclusiveWith: [DiagnosisID]
    /// Diagnoses that MAY co-hold with this one; they are excluded from this diagnosis's margin
    /// competition (a co-occurring pattern is not a rival hypothesis).
    public let allowedCooccurrenceWith: [DiagnosisID]

    public init(id: DiagnosisID,
                displayName: String,
                rules: [DiagnosisIndicatorRule],
                requiredSections: [SectionID] = [],
                exclusionaryIndicators: [IndicatorID] = [],
                mutuallyExclusiveWith: [DiagnosisID] = [],
                allowedCooccurrenceWith: [DiagnosisID] = []) {
        self.id = id
        self.displayName = displayName
        self.rules = rules
        self.requiredSections = requiredSections
        self.exclusionaryIndicators = exclusionaryIndicators
        self.mutuallyExclusiveWith = mutuallyExclusiveWith
        self.allowedCooccurrenceWith = allowedCooccurrenceWith
    }
}

/// The complete matrix: every diagnosis and the shared threshold profile. Round-trips as JSON.
public struct MatrixSpec: Codable, Sendable, Equatable {
    public let diagnoses: [DiagnosisSpec]
    public let thresholds: ThresholdProfile
    /// The universe of section identifiers a `requiredSections` list may reference. Defaults to the
    /// six canonical Syxx sections; validated in `validate()`.
    public let knownSections: Set<SectionID>
    /// B156: the set of indicator IDs that have at least one reachable `EvidenceId` mapping in the
    /// evidence model. Support rules referencing unmappable indicators are excluded from the
    /// coverage denominator so that unmapped rules don't create permanent dead weight that
    /// hard-caps coverage below the minimum thresholds. When nil (legacy JSON), all indicators
    /// are treated as mappable (original behavior).
    public let mappableIndicators: Set<IndicatorID>?

    public static let defaultKnownSections: Set<SectionID> = [
        SectionID("prophet"), SectionID("mirror"), SectionID("portal"),
        SectionID("glutton"), SectionID("cage"), SectionID("witness")
    ]

    public init(diagnoses: [DiagnosisSpec],
                thresholds: ThresholdProfile,
                knownSections: Set<SectionID> = MatrixSpec.defaultKnownSections,
                mappableIndicators: Set<IndicatorID>? = nil) {
        self.diagnoses = diagnoses
        self.thresholds = thresholds
        self.knownSections = knownSections
        self.mappableIndicators = mappableIndicators
    }

    private enum CodingKeys: String, CodingKey {
        case diagnoses, thresholds, knownSections, mappableIndicators
    }

    /// F-04: encode the two `Set` members as SORTED arrays.
    ///
    /// `Set` has no stable iteration order and Swift seeds its hasher per process, so the
    /// synthesized encoding emitted a different byte sequence on every run — three consecutive
    /// runs on one build produced three different `knownSections` orders. That makes this output
    /// unusable as the frozen-oracle reference `docs/MATRIX-MIGRATION-PLAN.md` designates macOS to
    /// produce, and unstable under any hash, signature, or cross-platform comparison taken over it.
    ///
    /// Sorted by `rawValue`. These identifiers are ASCII, so Swift's `<` agrees with the C# twin's
    /// `StringComparer.Ordinal` here; ordinal is the intended semantics, not locale-aware collation.
    ///
    /// Decoding is unaffected — the synthesized `init(from:)` reads an array into a `Set`, so this
    /// changes byte order only, never the spec's meaning.
    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(diagnoses, forKey: .diagnoses)
        try c.encode(thresholds, forKey: .thresholds)
        try c.encode(knownSections.sorted { $0.rawValue < $1.rawValue }, forKey: .knownSections)
        try c.encodeIfPresent(mappableIndicators?.sorted { $0.rawValue < $1.rawValue },
                              forKey: .mappableIndicators)
    }

    /// Structural invariants a well-formed spec must satisfy. Thrown at engine construction so a
    /// malformed matrix fails loudly at load time rather than silently mis-scoring at run time.
    public func validate() throws {
        guard !diagnoses.isEmpty else { throw MatrixEngineError.emptySpec }

        var seenDiagnosisIDs = Set<DiagnosisID>()
        let allDiagnosisIDs = Set(diagnoses.map { $0.id })

        for d in diagnoses {
            // Duplicate diagnosis id.
            guard seenDiagnosisIDs.insert(d.id).inserted else {
                throw MatrixEngineError.duplicateDiagnosis(d.id)
            }
            guard !d.rules.isEmpty else { throw MatrixEngineError.diagnosisHasNoRules(d.id) }

            // requiredSections must reference known sections only.
            for s in d.requiredSections where !knownSections.contains(s) {
                throw MatrixEngineError.unknownRequiredSection(diagnosis: d.id, section: s)
            }

            // An indicator cannot both SUPPORT and be EXCLUSIONARY for the same diagnosis: that is a
            // contradictory rule (it would be evidence for and hard-proof-against simultaneously).
            let supportingIDs = Set(d.rules.filter { $0.polarity == .supports }.map { $0.indicatorID })
            for ex in d.exclusionaryIndicators where supportingIDs.contains(ex) {
                throw MatrixEngineError.contradictoryIndicatorRole(diagnosis: d.id, indicator: ex)
            }

            // Duplicate rule for the same indicator+polarity within a diagnosis.
            var seenRuleKeys = Set<String>()
            for r in d.rules {
                let key = "\(r.indicatorID.rawValue)|\(r.polarity.rawValue)"
                guard seenRuleKeys.insert(key).inserted else {
                    throw MatrixEngineError.duplicateRule(diagnosis: d.id, indicator: r.indicatorID)
                }
                guard r.expectedStrength >= 0, r.expectedStrength <= 1 else {
                    throw MatrixEngineError.strengthOutOfRange(diagnosis: d.id, indicator: r.indicatorID)
                }
                guard r.weight >= 0, r.decayExponent > 0 else {
                    throw MatrixEngineError.invalidRuleParameter(diagnosis: d.id, indicator: r.indicatorID)
                }
            }

            // Cross-diagnosis references must resolve.
            for other in d.mutuallyExclusiveWith where !allDiagnosisIDs.contains(other) {
                throw MatrixEngineError.danglingReference(diagnosis: d.id, referenced: other)
            }
            for other in d.allowedCooccurrenceWith where !allDiagnosisIDs.contains(other) {
                throw MatrixEngineError.danglingReference(diagnosis: d.id, referenced: other)
            }
        }
    }
}

// MARK: Evidence inputs

/// The terminal usability of one section in a single run.
public struct SectionResult: Codable, Sendable, Equatable {
    public let id: SectionID
    public let status: EvidenceUsability
    /// Whether this section is globally required (informational; per-diagnosis requirement is what
    /// actually gates). Carried through for provenance/rendering.
    public let required: Bool
    /// Whether the section's provenance is trusted (e.g. signed witness). Untrusted usable output
    /// is still usable but is surfaced; the gate keys on `status.isUsable`, not on `trusted`.
    public let trusted: Bool
    public let notes: String?

    public init(id: SectionID, status: EvidenceUsability, required: Bool = false,
                trusted: Bool = true, notes: String? = nil) {
        self.id = id
        self.status = status
        self.required = required
        self.trusted = trusted
        self.notes = notes
    }
}

/// One observed indicator in a single run. `observedStrength == nil` means NOT OBSERVED — the
/// engine treats it as absent (it lowers coverage), never as zero-support-that-counts.
public struct ObservedIndicator: Codable, Sendable, Equatable {
    public let id: IndicatorID
    public let section: SectionID
    /// Normalised strength in 0...1, or nil when the indicator was not observed at all.
    public let observedStrength: Double?
    /// Data-quality of this observation in 0...1 (completeness / reliability). Attenuates, never
    /// zeroes, support.
    public let quality: Double
    /// True when this observation actively contradicts the indicator's supporting reading.
    public let contradictory: Bool
    public let notes: String?

    public init(id: IndicatorID, section: SectionID, observedStrength: Double?,
                quality: Double = 1.0, contradictory: Bool = false, notes: String? = nil) {
        self.id = id
        self.section = section
        self.observedStrength = observedStrength
        self.quality = quality
        self.contradictory = contradictory
        self.notes = notes
    }

    /// An indicator is "observed" only when it carries a non-nil strength.
    public var isObserved: Bool { observedStrength != nil }
}

/// The full evidence for one run: every section's usability and every observed indicator.
public struct EvidenceBundle: Codable, Sendable, Equatable {
    public let sections: [SectionResult]
    public let indicators: [ObservedIndicator]

    public init(sections: [SectionResult], indicators: [ObservedIndicator]) {
        self.sections = sections
        self.indicators = indicators
    }

    /// Duplicate detection: the same section id or the same indicator id must not appear twice —
    /// that would make lookups order-dependent and could silently double-count evidence.
    public func validate() throws {
        var seenSections = Set<SectionID>()
        for s in sections {
            guard seenSections.insert(s.id).inserted else {
                throw MatrixEngineError.duplicateSectionInBundle(s.id)
            }
        }
        var seenIndicators = Set<IndicatorID>()
        for i in indicators {
            guard seenIndicators.insert(i.id).inserted else {
                throw MatrixEngineError.duplicateIndicatorInBundle(i.id)
            }
            if let s = i.observedStrength, !(s.isFinite && s >= 0 && s <= 1) {
                throw MatrixEngineError.indicatorStrengthOutOfRange(i.id)
            }
            guard i.quality.isFinite, i.quality >= 0, i.quality <= 1 else {
                throw MatrixEngineError.indicatorQualityOutOfRange(i.id)
            }
        }
    }
}

// MARK: Results

/// The confidence tier at which a diagnosis is emitted. There is no `.inconclusive` tier here by
/// design: an inconclusive diagnosis is simply NOT emitted (it does not appear in the ranked list).
public enum DiagnosisConfidence: String, Codable, Sendable, Comparable {
    case probable
    case conclusive
    private var order: Int { self == .conclusive ? 1 : 0 }
    public static func < (l: DiagnosisConfidence, r: DiagnosisConfidence) -> Bool { l.order < r.order }
}

/// The overall run-level result. This is the ONLY place abstention lives — `.inconclusive` carries
/// an empty ranked-diagnoses list.
public enum DeterminativeState: String, Codable, Sendable {
    /// Positive evidence that the environment is clean, with no unverifiable threat overhang.
    case clean
    /// At least one threat diagnosis reached the probable+ tier.
    case threatDetected
    /// The evidence does not resolve to clean or to any threat — honest abstention.
    case inconclusive
    /// No sections have been evaluated yet — the scan has not run or every section is untested.
    case untested
}

/// A single emitted diagnosis with the metrics that justify it. Only ever constructed for a
/// diagnosis that actually reached `.probable`/`.conclusive`.
public struct DiagnosisScore: Codable, Sendable, Equatable {
    public let id: DiagnosisID
    public let displayName: String
    public let confidence: DiagnosisConfidence
    /// fit·coverage — the single comparable evidential strength used for ranking and margin.
    public let strength: Double
    public let fit: Double
    public let coverage: Double
    public let margin: Double
    public let contradiction: Double
    public let independentSupportGroups: Int
    /// Indicator ids that actually contributed supporting evidence (observed, non-contradictory).
    public let supportingIndicators: [IndicatorID]
    public let explanation: String

    public init(id: DiagnosisID, displayName: String, confidence: DiagnosisConfidence,
                strength: Double, fit: Double, coverage: Double, margin: Double,
                contradiction: Double, independentSupportGroups: Int,
                supportingIndicators: [IndicatorID], explanation: String) {
        self.id = id
        self.displayName = displayName
        self.confidence = confidence
        self.strength = strength
        self.fit = fit
        self.coverage = coverage
        self.margin = margin
        self.contradiction = contradiction
        self.independentSupportGroups = independentSupportGroups
        self.supportingIndicators = supportingIndicators
        self.explanation = explanation
    }
}

/// One diagnosis that was suppressed (gated / excluded / below tier) despite carrying real observed
/// support — surfaced so the honesty-critical "we saw a threat signal but could not verify it" case
/// is never hidden.
public struct SuppressedDiagnosis: Codable, Sendable, Equatable {
    public enum Reason: String, Codable, Sendable {
        case missingRequiredSection
        case invalidatedRequiredSection
        case hardExcluded
        /// C062: the diagnosis fired with real observed support but fell below the `.probable`
        /// tier and was not hard-gated. It must never silently vanish — it joins the honesty
        /// overhang ("seen but unconfirmed", never a detection) and forbids a bare `.clean`.
        case belowTier
    }
    public let id: DiagnosisID
    public let displayName: String
    public let reason: Reason
    public let rawStrength: Double
    /// C065: the quantity the overhang ADMISSION gate measured — the un-attenuated observed
    /// support (Σ weight·alignment^decay of non-contradictory supporting observations, WITHOUT
    /// quality attenuation). `rawStrength` (fit·coverage) is crushed by the very gate that
    /// suppressed the diagnosis, so it must never be re-used to decide clean-blocking; this field
    /// carries the honest gate-side measure instead. Parity-relevant Codable addition — the
    /// Windows port (Phase 3.1) must carry the same `rawSupport` key.
    public let rawSupport: Double
    public let detail: String
    public init(id: DiagnosisID, displayName: String, reason: Reason, rawStrength: Double,
                rawSupport: Double, detail: String) {
        self.id = id; self.displayName = displayName; self.reason = reason
        self.rawStrength = rawStrength; self.rawSupport = rawSupport; self.detail = detail
    }
}

/// The engine's output for one run.
public struct DeterminativeResult: Codable, Sendable, Equatable {
    public let state: DeterminativeState
    /// Emitted diagnoses, ranked by strength (descending). NEVER contains an abstention state, and
    /// NEVER contains `clean` when `state == .threatDetected`.
    public let diagnoses: [DiagnosisScore]
    /// Diagnoses that carried real raw support but were gated/excluded — the honesty overhang.
    public let suppressed: [SuppressedDiagnosis]
    /// A plain-language account of why the engine reached this state.
    public let explanation: String

    public init(state: DeterminativeState, diagnoses: [DiagnosisScore],
                suppressed: [SuppressedDiagnosis] = [], explanation: String) {
        self.state = state
        self.diagnoses = diagnoses
        self.suppressed = suppressed
        self.explanation = explanation
    }
}

// MARK: Errors

public enum MatrixEngineError: Error, Equatable, CustomStringConvertible {
    case emptySpec
    case duplicateDiagnosis(DiagnosisID)
    case diagnosisHasNoRules(DiagnosisID)
    case unknownRequiredSection(diagnosis: DiagnosisID, section: SectionID)
    case contradictoryIndicatorRole(diagnosis: DiagnosisID, indicator: IndicatorID)
    case duplicateRule(diagnosis: DiagnosisID, indicator: IndicatorID)
    case strengthOutOfRange(diagnosis: DiagnosisID, indicator: IndicatorID)
    case invalidRuleParameter(diagnosis: DiagnosisID, indicator: IndicatorID)
    case danglingReference(diagnosis: DiagnosisID, referenced: DiagnosisID)
    case duplicateSectionInBundle(SectionID)
    case duplicateIndicatorInBundle(IndicatorID)
    case indicatorStrengthOutOfRange(IndicatorID)
    case indicatorQualityOutOfRange(IndicatorID)

    public var description: String {
        switch self {
        case .emptySpec: return "MatrixSpec has no diagnoses"
        case .duplicateDiagnosis(let d): return "duplicate diagnosis id: \(d)"
        case .diagnosisHasNoRules(let d): return "diagnosis has no rules: \(d)"
        case .unknownRequiredSection(let d, let s): return "diagnosis \(d) requires unknown section \(s)"
        case .contradictoryIndicatorRole(let d, let i): return "indicator \(i) is both supporting and exclusionary in \(d)"
        case .duplicateRule(let d, let i): return "duplicate rule for indicator \(i) in \(d)"
        case .strengthOutOfRange(let d, let i): return "expectedStrength out of [0,1] for \(i) in \(d)"
        case .invalidRuleParameter(let d, let i): return "invalid weight/decayExponent for \(i) in \(d)"
        case .danglingReference(let d, let r): return "diagnosis \(d) references unknown diagnosis \(r)"
        case .duplicateSectionInBundle(let s): return "duplicate section in bundle: \(s)"
        case .duplicateIndicatorInBundle(let i): return "duplicate indicator in bundle: \(i)"
        case .indicatorStrengthOutOfRange(let i): return "observedStrength out of [0,1] for \(i)"
        case .indicatorQualityOutOfRange(let i): return "quality out of [0,1] for \(i)"
        }
    }
}

// MARK: - Engine

public struct MatrixDeterminativeEngine: Sendable {
    public let spec: MatrixSpec

    /// The exclusionary strength at or above which an exclusionary/`.excludes` observation is
    /// treated as "firing" (hard exclusion / strong contradiction).
    private static let exclusionFiringThreshold = 0.5
    /// B163: minimum observation quality (section completeness) required for a hard exclusion.
    /// Below this floor the exclusionary observation is treated as contradiction mass only —
    /// a degraded section must not silently zero a threat diagnosis.
    private static let exclusionQualityFloor = 0.5
    /// Raw-support floor above which a gated / below-tier threat diagnosis is considered an
    /// "honesty overhang" that must forbid a `.clean` result and force `.inconclusive`. Compared
    /// against `RawScore.observedSupport` (quality-independent) at BOTH the admission gate and the
    /// clean-blocking re-check in `assemble` — the two must measure the same quantity (C065).
    private static let overhangFloor = 0.30
    /// The one diagnosis id with clean semantics (never a threat; excluded from the overhang).
    private static let cleanID = DiagnosisID("clean")

    public init(spec: MatrixSpec) throws {
        try spec.validate()
        self.spec = spec
    }

    // MARK: Public evaluate

    public func evaluate(_ bundle: EvidenceBundle) throws -> DeterminativeResult {
        try bundle.validate()

        // Untested: every section is still untested AND no indicators have been observed.
        // Distinct from .inconclusive (data collected but ambiguous). Indicators can be
        // present even when sections are untested (e.g. a probe fired but the section
        // didn't complete), and those represent real data that must be analyzed.
        let allUntested = bundle.sections.allSatisfy { $0.status == .untested }
        let noIndicators = bundle.indicators.isEmpty
        if allUntested && noIndicators {
            return DeterminativeResult(state: .untested, diagnoses: [], suppressed: [],
                                       explanation: "No sections have been evaluated yet.")
        }

        let sectionByID = Dictionary(uniqueKeysWithValues: bundle.sections.map { ($0.id, $0) })
        let indicatorByID = Dictionary(uniqueKeysWithValues: bundle.indicators.map { ($0.id, $0) })

        // Pass 1: per-diagnosis raw metrics (pre-margin, pre-mutual-exclusion).
        var raws: [DiagnosisID: RawScore] = [:]
        for d in spec.diagnoses {
            raws[d.id] = rawScore(for: d, indicatorByID: indicatorByID, sectionByID: sectionByID)
        }

        // B159: identify emittable diagnoses BEFORE computing margins. A diagnosis that is gated
        // (required section missing/invalidated) or hard-excluded can never be emitted, so it is
        // not a rival hypothesis — using it as one converts real detections into abstentions.
        //
        // Admission to `suppressed` gates on `observedSupport` (quality-INDEPENDENT): the
        // question is "did real observations fire," and a thin section's low completeness
        // must attenuate scoring, never hide a seen signal (C062/C065).
        //
        // PRECEDENCE (C065, fail-closed): the required-section gate is checked BEFORE hard
        // exclusion. A diagnosis whose required evidential context is missing/invalidated is
        // UNEVALUABLE — it can be neither affirmed nor affirmatively disproved, so a firing
        // exclusionary indicator must not convert it into a clean-permitting `.hardExcluded`.
        // Gated-with-support therefore always reaches the clean-blocking overhang; exclusion
        // dominance applies only when the diagnosis's required evidence was actually usable.
        var emittableIDs = Set<DiagnosisID>()
        var suppressed: [SuppressedDiagnosis] = []
        for d in spec.diagnoses {
            guard let raw = raws[d.id] else { continue }
            if let gate = raw.gate {
                // B168: the clean diagnosis is not a threat signal — it must never enter the
                // overhang (same guard the belowTier path already carries at :682).
                if d.id != Self.cleanID && raw.observedSupport >= Self.overhangFloor {
                    suppressed.append(SuppressedDiagnosis(id: d.id, displayName: d.displayName,
                        reason: gate.reason, rawStrength: round6(raw.strength),
                        rawSupport: round6(raw.observedSupport),
                        detail: gate.detail))
                }
                continue
            }
            if let ex = raw.hardExclusion {
                // B168: same cleanID guard — a ruled-out clean is not a threat overhang.
                if d.id != Self.cleanID && raw.observedSupport >= Self.overhangFloor {
                    suppressed.append(SuppressedDiagnosis(id: d.id, displayName: d.displayName,
                        reason: .hardExcluded, rawStrength: round6(raw.strength),
                        rawSupport: round6(raw.observedSupport),
                        detail: "Excluded by a confirmed \(ex.rawValue) observation."))
                }
                continue
            }
            emittableIDs.insert(d.id)
        }

        // Pass 2: margins (need cross-diagnosis strengths). Competitors exclude self, any
        // explicitly-allowed co-occurrence, AND (B159) any diagnosis that is gated or hard-excluded
        // — an unevaluable or impossible rival is not a hypothesis.
        func competitorBestStrength(for d: DiagnosisSpec) -> Double {
            let allowed = Set(d.allowedCooccurrenceWith)
            var best = 0.0
            for other in spec.diagnoses where other.id != d.id && !allowed.contains(other.id) && emittableIDs.contains(other.id) {
                best = max(best, raws[other.id]?.strength ?? 0)
            }
            return best
        }

        // Pass 3: classify each emittable diagnosis into a tier (or below-tier suppressed).
        var emitted: [DiagnosisID: (DiagnosisScore, DiagnosisSpec)] = [:]
        for d in spec.diagnoses {
            guard emittableIDs.contains(d.id), let raw = raws[d.id] else { continue }
            let margin = round6(raw.strength - competitorBestStrength(for: d))

            guard let tier = tier(for: raw, margin: margin) else {
                // C062: a non-gated THREAT that fired with real observed support but fell below
                // the probable tier must never silently vanish — it joins the honesty overhang as
                // "seen but unconfirmed" (never a detection) and forbids a bare `.clean`. The
                // clean diagnosis itself falling below tier stays OUT of the overhang: it is not a
                // threat signal (assemble's "clean present but weak" path owns that case).
                // COPY (detail string): PROPOSED, PENDING OWNER APPROVAL (outputs-catalog.csv).
                if d.id != Self.cleanID && raw.observedSupport >= Self.overhangFloor {
                    suppressed.append(SuppressedDiagnosis(id: d.id, displayName: d.displayName,
                        reason: .belowTier, rawStrength: round6(raw.strength),
                        rawSupport: round6(raw.observedSupport),
                        detail: "Observed with real support but below the reporting threshold; could not be confirmed or ruled out."))
                }
                continue
            }
            let score = DiagnosisScore(
                id: d.id, displayName: d.displayName, confidence: tier,
                strength: round6(raw.strength), fit: round6(raw.fit), coverage: round6(raw.coverage),
                margin: margin, contradiction: round6(raw.contradiction),
                independentSupportGroups: raw.independentGroups,
                supportingIndicators: raw.supportingIndicators,
                explanation: explanation(for: d, raw: raw, tier: tier, margin: margin))
            emitted[d.id] = (score, d)
        }

        // Pass 4: resolve mutual exclusion among emitted diagnoses — the stronger wins.
        emitted = resolveMutualExclusion(emitted)

        // Pass 5: assemble the overall result.
        return assemble(emitted: emitted, suppressed: suppressed)
    }

    // MARK: Raw scoring

    private struct GateInfo { let reason: SuppressedDiagnosis.Reason; let detail: String }

    private struct RawScore {
        var fit: Double = 0
        var coverage: Double = 0
        var contradiction: Double = 0
        var strength: Double = 0        // fit * coverage
        var rawSupport: Double = 0      // Σ observed supporting weight*effective (pre-normalisation)
        /// Σ weight·alignment^decay for non-contradictory supporting observations WITHOUT quality
        /// attenuation. The overhang-ADMISSION measure (C062/C065): it answers "did real
        /// observations fire," independent of how thin the producing section ran. Quality (section
        /// completeness) attenuates evidential weight (`fit`/`strength`), but a thin section must
        /// never HIDE a seen threat signal — that would be missing data silently reading cleaner.
        var observedSupport: Double = 0
        var independentGroups: Int = 0
        var hasDecisive: Bool = false
        var majorCount: Int = 0
        var supportingIndicators: [IndicatorID] = []
        var hardExclusion: IndicatorID? = nil
        var gate: GateInfo? = nil
    }

    private func rawScore(for d: DiagnosisSpec,
                          indicatorByID: [IndicatorID: ObservedIndicator],
                          sectionByID: [SectionID: SectionResult]) -> RawScore {
        var r = RawScore()

        let supportRules = d.rules.filter { $0.polarity == .supports }
        let excludeRules = d.rules.filter { $0.polarity == .excludes }

        // B156: normalise coverage over *mappable* indicator weight only. Rules referencing
        // unmappable indicators (those with no EvidenceId mapping) are permanent dead weight in
        // the denominator, hard-capping coverage below the minimum thresholds and making entire
        // diagnoses unreachable. When `mappableIndicators` is nil (legacy spec), all rules count.
        let totalSupportWeight: Double
        if let mappable = spec.mappableIndicators {
            totalSupportWeight = supportRules
                .filter { mappable.contains($0.indicatorID) }
                .reduce(0.0) { $0 + $1.weight }
        } else {
            totalSupportWeight = supportRules.reduce(0.0) { $0 + $1.weight }
        }
        var observedSupportWeight = 0.0
        var alignedSupport = 0.0          // Σ weight*effective for non-contradictory support
        var contradictionMass = 0.0
        var groups = Set<String>()

        for rule in supportRules {
            guard let obs = indicatorByID[rule.indicatorID], let s = obs.observedStrength else { continue }
            // B162: skip indicators whose producing section is not usable — the required-section
            // gate only checks listed sections, so an indicator from another invalidated/untested
            // section would otherwise score at face value for every diagnosis that omits it.
            if let producing = sectionByID[obs.section], !producing.status.isUsable { continue }
            observedSupportWeight += rule.weight
            let alignment = rule.expectedStrength > 0 ? min(1.0, s / rule.expectedStrength) : s
            let aligned = pow(alignment, rule.decayExponent)
            // B157: floor quality for a datapoint that itself completed (quality > 0). Without the
            // floor, `fit ≤ quality`, and a section running under `minimumFitForProbable` (0.62)
            // of its hardcoded catalog can never contribute to an emitted diagnosis regardless of
            // how decisive the observation. A collector-confirmed DNS leak (alignment 1.0) on a
            // 50%-complete Glutton section yields fit = 0.5 < 0.62 ⇒ not emitted. The floor at
            // `minimumFitForProbable` lets a completed, well-aligned observation reach the
            // probable tier; the un-attenuated quality still attenuates *coverage* and the tier
            // gates (independence groups, decisive-or-two-majors) prevent inflation.
            let qualityFloor = spec.thresholds.minimumFitForProbable
            let effectiveQuality = max(clamp01(obs.quality), obs.quality > 0 ? qualityFloor : 0)
            let effective = aligned * effectiveQuality
            let contribution = rule.weight * effective
            if obs.contradictory {
                // A supporting indicator observed to contradict → attenuates fit, adds contradiction.
                contradictionMass += contribution
            } else {
                alignedSupport += contribution
                // Quality-independent: the observation FIRED; thin-section attenuation must not
                // erase it from the overhang-admission measure (C062/C065).
                r.observedSupport += rule.weight * aligned
                if effective > 0 {
                    groups.insert(rule.independenceGroup)
                    r.supportingIndicators.append(rule.indicatorID)
                    if rule.evidenceClass == .decisive { r.hasDecisive = true }
                    if rule.evidenceClass == .major { r.majorCount += 1 }
                }
            }
        }

        // Exclude-polarity rules: firing observations add strong contradiction.
        for rule in excludeRules {
            guard let obs = indicatorByID[rule.indicatorID], let s = obs.observedStrength,
                  !obs.contradictory else { continue }
            // B162: same section-usability gate as support rules.
            if let producing = sectionByID[obs.section], !producing.status.isUsable { continue }
            let alignment = rule.expectedStrength > 0 ? min(1.0, s / rule.expectedStrength) : s
            let effective = pow(alignment, rule.decayExponent) * clamp01(obs.quality)
            contradictionMass += rule.weight * effective
        }

        r.rawSupport = alignedSupport
        r.coverage = totalSupportWeight > 0 ? clamp01(observedSupportWeight / totalSupportWeight) : 0
        r.fit = observedSupportWeight > 0 ? clamp01(alignedSupport / observedSupportWeight) : 0
        // Contradiction normalised against the support it competes with (bounded, never > 1).
        r.contradiction = clamp01(contradictionMass / (contradictionMass + observedSupportWeight + 1e-9))
        r.strength = r.fit * r.coverage
        r.independentGroups = groups.count

        // Hard exclusion: a listed exclusionary indicator observed as firing and non-contradictory.
        // B163: require the producing section to be usable and the observation quality to clear
        // the quality floor — a degraded/unreliable observation must not silently zero a threat.
        for exID in d.exclusionaryIndicators {
            if let obs = indicatorByID[exID], let s = obs.observedStrength,
               !obs.contradictory, s >= Self.exclusionFiringThreshold,
               obs.quality >= Self.exclusionQualityFloor,
               sectionByID[obs.section]?.status.isUsable ?? false {
                r.hardExclusion = exID
                break
            }
        }

        // Required-section hard gate (fail-closed).
        for reqSection in d.requiredSections {
            guard let section = sectionByID[reqSection] else {
                if spec.thresholds.hardGateOnMissingRequiredSections {
                    r.gate = GateInfo(reason: .missingRequiredSection,
                                      detail: "Required section '\(reqSection)' did not run.")
                    break
                }
                // B166: when the operator disabled the hard-missing flag, keep checking the
                // remaining required sections — the invalidated / unusable checks below are
                // governed by independent flags and must not be short-circuited.
                continue
            }
            if section.status == .invalidated, spec.thresholds.failClosedOnInvalidatedRequiredSections {
                r.gate = GateInfo(reason: .invalidatedRequiredSection,
                                  detail: "Required section '\(reqSection)' ran but its premise failed (invalidated).")
                break
            }
            if !section.status.isUsable {
                r.gate = GateInfo(reason: .missingRequiredSection,
                                  detail: "Required section '\(reqSection)' produced no usable output (\(section.status.rawValue)).")
                break
            }
        }

        return r
    }

    // MARK: Tiering

    private func tier(for r: RawScore, margin: Double) -> DiagnosisConfidence? {
        let t = spec.thresholds
        let decisiveOK = !t.requireDecisiveOrTwoMajorsForConclusive || r.hasDecisive || r.majorCount >= 2
        if r.coverage >= t.minimumCoverageForConclusive,
           r.fit >= t.minimumFitForConclusive,
           r.contradiction <= t.maxContradictionForConclusive,
           r.independentGroups >= t.minimumIndependentSupportGroupsForConclusive,
           margin >= t.minimumMarginForConclusive,
           decisiveOK {
            return .conclusive
        }
        if r.coverage >= t.minimumCoverageForProbable,
           r.fit >= t.minimumFitForProbable,
           r.contradiction <= t.maxContradictionForProbable,
           r.independentGroups >= t.minimumIndependentSupportGroupsForProbable,
           margin >= t.minimumMarginForProbable {
            return .probable
        }
        return nil
    }

    // MARK: Mutual exclusion

    private func resolveMutualExclusion(
        _ emitted: [DiagnosisID: (DiagnosisScore, DiagnosisSpec)]
    ) -> [DiagnosisID: (DiagnosisScore, DiagnosisSpec)] {
        var kept = emitted
        // B158: iterate a deterministically sorted array of ids. Swift Dictionary iteration order
        // is derived from the per-process random hash seed, so the same evidence could produce
        // different diagnosis sets depending on which pairs were resolved first. Sorting by
        // rawValue ensures cross-run and cross-platform parity. Additionally, a diagnosis that
        // was already deleted must not delete another (the `kept[id] != nil` guard below).
        let sortedIDs = emitted.keys.sorted { $0.rawValue < $1.rawValue }
        for id in sortedIDs {
            guard kept[id] != nil, let (score, dSpec) = emitted[id] else { continue }
            for rivalID in dSpec.mutuallyExclusiveWith {
                guard let (rivalScore, _) = kept[rivalID] else { continue }
                // Drop the weaker of the conflicting pair (ties: keep the one with higher fit,
                // then deterministic id order to stay parity-stable).
                let loser: DiagnosisID
                if score.strength != rivalScore.strength {
                    loser = score.strength < rivalScore.strength ? id : rivalID
                } else if score.fit != rivalScore.fit {
                    loser = score.fit < rivalScore.fit ? id : rivalID
                } else {
                    loser = id.rawValue > rivalID.rawValue ? id : rivalID
                }
                kept[loser] = nil
                if loser == id { break }
            }
        }
        return kept
    }

    // MARK: Assembly

    private func assemble(emitted: [DiagnosisID: (DiagnosisScore, DiagnosisSpec)],
                          suppressed: [SuppressedDiagnosis]) -> DeterminativeResult {
        let cleanID = Self.cleanID
        // B167: hardExcluded diagnoses were affirmatively ruled out by a confirmed contrary
        // observation — they are not "seen but unverifiable" and must not be surfaced alongside
        // genuinely gated/below-tier entries. Filter once, here, so every consumer (ResultAction,
        // PlainExplanation, Report) sees the same honest list.
        let surfaceSuppressed = suppressed.filter { $0.reason != .hardExcluded }
        let threats = emitted
            .filter { $0.key != cleanID }
            .map { $0.value.0 }
            .sorted { rankOrder($0, $1) }

        if !threats.isEmpty {
            let names = threats.map { $0.displayName }.joined(separator: ", ")
            return DeterminativeResult(
                state: .threatDetected,
                diagnoses: threats,
                suppressed: surfaceSuppressed,
                explanation: "Detected: \(names). The evidence affirmatively supports "
                    + (threats.count == 1 ? "this diagnosis." : "these diagnoses."))
        }

        // No threat emitted. Consider clean — but never over an honesty overhang.
        // C065: this re-check measures the SAME quantity the admission gate measured
        // (`rawSupport` = un-attenuated observed support), never `rawStrength` (fit·coverage) —
        // the hard gate that suppressed the threat is precisely what crushes coverage, so a
        // strength-based floor here would let `.clean` ride over an unverified threat signal.
        // B163: hardExcluded entries stay in the overhang — a degraded exclusion must not
        // license a clean result. The full `suppressed` (not `surfaceSuppressed`) is used here.
        let overhang = suppressed.filter { $0.rawSupport >= Self.overhangFloor }
        if let cleanEmitted = emitted[cleanID]?.0, overhang.isEmpty {
            return DeterminativeResult(
                state: .clean,
                diagnoses: [cleanEmitted],
                suppressed: surfaceSuppressed,
                explanation: "No tampering was detected, and the clean pattern is affirmatively supported "
                    + "(coverage \(pct(cleanEmitted.coverage)), fit \(pct(cleanEmitted.fit))).")
        }

        // Abstain.
        var why: String
        if !overhang.isEmpty {
            // The overhang may still contain hardExcluded entries (B163) — use the filtered
            // `surfaceSuppressed` to build the user-visible name list so ruled-out diagnoses
            // are not narrated as lurking unverified threats.
            let surfaceOverhang = surfaceSuppressed.filter { $0.rawSupport >= Self.overhangFloor }
            let names = surfaceOverhang.isEmpty
                ? overhang.map { $0.displayName }.joined(separator: ", ")
                : surfaceOverhang.map { $0.displayName }.joined(separator: ", ")
            // Display rule for the overhang (C062, plan Step 1.4.4): ALWAYS "seen but unconfirmed",
            // never a detection. `.belowTier` entries were below the reporting threshold rather
            // than missing their evidence, so the reason clause matches what the overhang actually
            // contains. COPY: PROPOSED, PENDING OWNER APPROVAL (outputs-catalog.csv).
            let hasGated = surfaceOverhang.contains {
                $0.reason == .missingRequiredSection || $0.reason == .invalidatedRequiredSection
            }
            let hasBelowTier = surfaceOverhang.contains { $0.reason == .belowTier }
            let reasonClause: String
            switch (hasGated, hasBelowTier) {
            case (true, true):
                reasonClause = "required evidence was missing or invalidated, and some of the support stayed below the reporting threshold"
            case (false, true):
                reasonClause = "its support stayed below the reporting threshold"
            default:
                reasonClause = "required evidence was missing or invalidated"
            }
            why = "Inconclusive: a possible threat signal (\(names)) was observed but could not be "
                + "verified because \(reasonClause). Not treated as clean."
        } else if emitted[cleanID] != nil {
            why = "Inconclusive: the clean pattern is present but not strongly enough supported to affirm it."
        } else {
            why = "Inconclusive: the evidence does not affirmatively resolve to clean or to any threat."
        }
        return DeterminativeResult(state: .inconclusive, diagnoses: [], suppressed: surfaceSuppressed, explanation: why)
    }

    // MARK: Explanations & helpers

    private func explanation(for d: DiagnosisSpec, raw: RawScore,
                             tier: DiagnosisConfidence, margin: Double) -> String {
        let basis = raw.hasDecisive ? "a decisive observation"
            : (raw.majorCount >= 2 ? "\(raw.majorCount) major observations" : "supporting observations")
        return "\(tier.rawValue.capitalized) support for \(d.displayName): \(basis) across "
            + "\(raw.independentGroups) independent evidence group\(raw.independentGroups == 1 ? "" : "s") "
            + "(fit \(pct(raw.fit)), coverage \(pct(raw.coverage)), margin \(pct(margin)), "
            + "contradiction \(pct(raw.contradiction)))."
    }

    /// Deterministic ranking: strength desc, then fit desc, then id asc (parity-stable).
    private func rankOrder(_ a: DiagnosisScore, _ b: DiagnosisScore) -> Bool {
        if a.strength != b.strength { return a.strength > b.strength }
        if a.fit != b.fit { return a.fit > b.fit }
        return a.id.rawValue < b.id.rawValue
    }
}

// MARK: - Numeric helpers (single rounding mode for determinism / cross-platform parity)

@inline(__always) private func clamp01(_ x: Double) -> Double { min(1.0, max(0.0, x)) }

/// Round to 6 decimals under a single, explicit rounding rule so the same evidence yields the same
/// number on every platform (macOS today; the Windows port must adopt the identical helper).
/// `internal` (not `private`) so the regression suite (`RoundingParityTests`, P2a/GV-10) can pin the
/// banker's-rounding boundary behaviour directly via `@testable import`; not part of the public API.
@inline(__always) func round6(_ x: Double) -> Double {
    guard x.isFinite else { return 0 }
    return (x * 1_000_000).rounded(.toNearestOrEven) / 1_000_000
}

// B169: drop abs() — `margin` is genuinely signed and must render with its true sign so a
// negative margin is not misread as a positive percentage. All other callers (fit, coverage,
// contradiction) are 0…1 ratios and unaffected.
private func pct(_ x: Double) -> String {
    "\(Int((clamp01(x) * 100).rounded()))%"
}
