import Foundation

// Stage-5 engine-to-presentation contract (STAGE5-CONTRACT.md §3-§13). The engine owns MEANING and
// populates these DTOs; GUI/CLI code renders them WITHOUT reconstructing meaning. This file is the
// additive, typed contract — it holds tokens (`CopyToken`), never finalized English prose (STAGE5
// §2/§10). The Windows twin is `Windows/Syxx.Kit/Synthesis/Stage5Presentation.cs`; the two must
// decode one shared v1 fixture to equivalent graphs and re-encode byte-identically (STAGE5 §11).
//
// P1.0: additive, no behavior change. Production writes `Conclusion.stage5 = nil`; renderers show
// the approved unavailable/inconclusive state for an absent object (STAGE5 §12). Populating this
// object lands in P1.4/P1.9/P1.10/P1.11.

// MARK: - Copy token

/// A stable machine identifier that selects approved copy at render time (STAGE5 §10). The engine
/// emits tokens, never prose; presentation resolves them through one platform-parity copy table.
/// Encodes as a bare JSON string (single-value container), byte-identical to the C# `readonly
/// record struct CopyToken(string Value)`.
public struct CopyToken: RawRepresentable, Codable, Sendable, Hashable {
    public let rawValue: String
    public init(rawValue: String) { self.rawValue = rawValue }
    public init(_ value: String) { self.rawValue = value }

    public init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        rawValue = try c.decode(String.self)
    }
    public func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        try c.encode(rawValue)
    }

    // Well-known tokens (STAGE5 §10). Values are firm contract data and may land before copy approval.
    public static let hardGateRequiredChecksMissing = CopyToken("stage5.hard_gate.required_checks_missing")
    public static let conflictEvidenceDisagrees      = CopyToken("stage5.conflict.evidence_disagrees")
    public static let uncertaintyEvidenceThin        = CopyToken("stage5.uncertainty.evidence_thin")
    public static let referralMechanismUnresolved    = CopyToken("stage5.referral.mechanism_unresolved")
    public static let unavailableTimedOut            = CopyToken("stage5.unavailable.timed_out")
    public static let basisDecisiveSafetyGate        = CopyToken("stage5.basis.decisive_safety_gate")
}

// MARK: - Root

public struct Stage5Presentation: Codable, Sendable, Equatable {
    public let schemaVersion: Int
    public let generatedAt: String                  // RFC 3339 UTC, fractional seconds
    public let dataRegime: DataRegime
    public let completeness: EvidenceCompleteness
    public let provenance: ProvenancePartition
    public let answerMatrix: [AnswerMatrixRow]
    public let honesty: HonestySignals
    public let determination: MechanismDetermination
    public let findings: [PrioritizedFinding]

    public init(schemaVersion: Int = 1,
                generatedAt: String,
                dataRegime: DataRegime,
                completeness: EvidenceCompleteness,
                provenance: ProvenancePartition,
                answerMatrix: [AnswerMatrixRow],
                honesty: HonestySignals,
                determination: MechanismDetermination,
                findings: [PrioritizedFinding]) {
        self.schemaVersion = schemaVersion
        self.generatedAt = generatedAt
        self.dataRegime = dataRegime
        self.completeness = completeness
        self.provenance = provenance
        self.answerMatrix = answerMatrix
        self.honesty = honesty
        self.determination = determination
        self.findings = findings
    }
}

// MARK: - CX-1 provenance partition

public struct ProvenancePartition: Codable, Sendable, Equatable {
    public let direct: [DirectObservation]
    public let inferred: [InferredFinding]
    public let unavailable: [UnavailableDatapoint]
    public init(direct: [DirectObservation], inferred: [InferredFinding], unavailable: [UnavailableDatapoint]) {
        self.direct = direct; self.inferred = inferred; self.unavailable = unavailable
    }
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
    public init(observationId: String, datapointId: String, section: Section, observedAt: String,
                vantage: VantageIdentity, rawValues: [String], valueKind: ObservationValueKind,
                sourceEvidenceIds: [EvidenceId]) {
        self.observationId = observationId; self.datapointId = datapointId; self.section = section
        self.observedAt = observedAt; self.vantage = vantage; self.rawValues = rawValues
        self.valueKind = valueKind; self.sourceEvidenceIds = sourceEvidenceIds
    }
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
    public init(vantageId: String, kind: VantageKind, endpoint: String?, transport: String?,
                authenticated: Bool, freshness: FreshnessState) {
        self.vantageId = vantageId; self.kind = kind; self.endpoint = endpoint
        self.transport = transport; self.authenticated = authenticated; self.freshness = freshness
    }
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
    public init(findingId: String, mechanismClass: MechanismClass, detailToken: CopyToken?,
                sourceObservationIds: [String], sourceEvidenceIds: [EvidenceId]) {
        self.findingId = findingId; self.mechanismClass = mechanismClass; self.detailToken = detailToken
        self.sourceObservationIds = sourceObservationIds; self.sourceEvidenceIds = sourceEvidenceIds
    }
}

public struct UnavailableDatapoint: Codable, Sendable, Equatable {
    public let datapointId: String
    public let section: Section
    public let reason: UnavailableReason
    public let reasonToken: CopyToken
    public init(datapointId: String, section: Section, reason: UnavailableReason, reasonToken: CopyToken) {
        self.datapointId = datapointId; self.section = section; self.reason = reason; self.reasonToken = reasonToken
    }
}

public enum UnavailableReason: String, Codable, Sendable {
    case notRequested, untested, timedOut, invalidated, parseFailed
    case probeFailed, unauthenticated, stale, permissionDenied, dependencyUnavailable
}

// MARK: - CX-1 canonical answer matrix

public struct AnswerMatrixRow: Codable, Sendable, Equatable {
    public let rowId: String
    public let queryName: String
    public let queryType: UInt16
    public let entries: [VantageAnswer]
    public let comparison: AnswerComparison
    public init(rowId: String, queryName: String, queryType: UInt16, entries: [VantageAnswer], comparison: AnswerComparison) {
        self.rowId = rowId; self.queryName = queryName; self.queryType = queryType
        self.entries = entries; self.comparison = comparison
    }
}

public struct VantageAnswer: Codable, Sendable, Equatable {
    public let vantage: VantageIdentity
    public let status: AnswerStatus
    public let canonicalAnswers: [String]
    public let rcode: Int?
    public let observationId: String?
    public init(vantage: VantageIdentity, status: AnswerStatus, canonicalAnswers: [String], rcode: Int?, observationId: String?) {
        self.vantage = vantage; self.status = status; self.canonicalAnswers = canonicalAnswers
        self.rcode = rcode; self.observationId = observationId
    }
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
    public init(classification: AnswerComparisonClass, localVantageIds: [String], referenceVantageIds: [String],
                consensusAnswers: [String], outlierVantageIds: [String]) {
        self.classification = classification; self.localVantageIds = localVantageIds
        self.referenceVantageIds = referenceVantageIds; self.consensusAnswers = consensusAnswers
        self.outlierVantageIds = outlierVantageIds
    }
}

public enum AnswerComparisonClass: String, Codable, Sendable {
    case agreement, benignSetVariation, discrepancy, insufficientReferences
    case incomparable, notApplicable
}

// MARK: - CX-2 distinct honesty signals

public struct HonestySignals: Codable, Sendable, Equatable {
    public let hardGate: HardGateSignal?
    public let conflict: ScalarHonestySignal?
    public let uncertainty: ScalarHonestySignal?
    public init(hardGate: HardGateSignal?, conflict: ScalarHonestySignal?, uncertainty: ScalarHonestySignal?) {
        self.hardGate = hardGate; self.conflict = conflict; self.uncertainty = uncertainty
    }
}

public struct HardGateSignal: Codable, Sendable, Equatable {
    public let token: CopyToken
    /// Wire key `blockedResults` (never the banned word). States that the hard gate forbids.
    public let blockedResults: [DeterminationState]
    public let requiredSections: [Section]
    public let unavailableDatapointIds: [String]
    public init(token: CopyToken, blockedResults: [DeterminationState], requiredSections: [Section], unavailableDatapointIds: [String]) {
        self.token = token; self.blockedResults = blockedResults
        self.requiredSections = requiredSections; self.unavailableDatapointIds = unavailableDatapointIds
    }
}

public struct ScalarHonestySignal: Codable, Sendable, Equatable {
    public let token: CopyToken
    public let value: Double
    public init(token: CopyToken, value: Double) { self.token = token; self.value = value }
}

// MARK: - CX-3 mechanism determination

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
    public init(state: DeterminationState, mechanismClass: MechanismClass?, confidence: CredalMeasure?,
                withinClass: [WithinClassEntry], basis: DecisionBasis, referralRequired: Bool, referralToken: CopyToken?) {
        self.state = state; self.mechanismClass = mechanismClass; self.confidence = confidence
        self.withinClass = withinClass; self.basis = basis; self.referralRequired = referralRequired
        self.referralToken = referralToken
    }
}

public struct CredalMeasure: Codable, Sendable, Equatable {
    public let kind: CredalMeasureKind
    public let value: Double
    public let target: String
    public init(kind: CredalMeasureKind, value: Double, target: String) {
        self.kind = kind; self.value = value; self.target = target
    }
}

public enum CredalMeasureKind: String, Codable, Sendable {
    case belief
}

/// Stage-5 within-class hypothesis labels. Wire values match the historical DST spellings
/// (including snake_case `interception_onPath`) so Swift and C# re-encode byte-identically.
public enum Hypothesis: String, Codable, Sendable, CaseIterable {
    case clean
    case spoofing
    case censorship
    case interception_transport
    case interception_onPath
    case contentTampering
    case privacyLeak
    case localCompromise
    case inconclusive
}

public struct WithinClassEntry: Codable, Sendable, Equatable {
    public let hypothesis: Hypothesis
    public let displayOrder: Int
    public let share: Double?
    public init(hypothesis: Hypothesis, displayOrder: Int, share: Double?) {
        self.hypothesis = hypothesis; self.displayOrder = displayOrder; self.share = share
    }
}

public enum DecisionBasis: String, Codable, Sendable {
    case ordinaryCredalGate, decisiveSafetyGate, hardGate, unresolvedMechanism
    case insufficientEvidence
}

// MARK: - Completeness / regime

public enum DataRegime: String, Codable, Sendable {
    case noData, partialData, fullData
}

public struct EvidenceCompleteness: Codable, Sendable, Equatable {
    public let observed: Int
    public let expected: Int
    public let criticalAvailable: Int
    public let criticalExpected: Int
    public let expectedProfileId: String
    public init(observed: Int, expected: Int, criticalAvailable: Int, criticalExpected: Int, expectedProfileId: String) {
        self.observed = observed; self.expected = expected; self.criticalAvailable = criticalAvailable
        self.criticalExpected = criticalExpected; self.expectedProfileId = expectedProfileId
    }
}

// MARK: - CX-3 RPN ordering

public struct PrioritizedFinding: Codable, Sendable, Equatable {
    public let findingId: String
    public let mechanismClass: MechanismClass
    public let inferenceId: String?
    public let sourceObservationIds: [String]
    public let priority: FindingPriority
    public init(findingId: String, mechanismClass: MechanismClass, inferenceId: String?,
                sourceObservationIds: [String], priority: FindingPriority) {
        self.findingId = findingId; self.mechanismClass = mechanismClass; self.inferenceId = inferenceId
        self.sourceObservationIds = sourceObservationIds; self.priority = priority
    }
}

public struct FindingPriority: Codable, Sendable, Equatable {
    public let modelId: String
    public let modelVersion: Int
    public let factors: PriorityFactors
    public let score: Int
    public let rank: Int
    public init(modelId: String, modelVersion: Int, factors: PriorityFactors, score: Int, rank: Int) {
        self.modelId = modelId; self.modelVersion = modelVersion; self.factors = factors
        self.score = score; self.rank = rank
    }
}

public struct PriorityFactors: Codable, Sendable, Equatable {
    public let severity: Int
    public let detectability: Int
    public let occurrence: Int?
    public init(severity: Int, detectability: Int, occurrence: Int?) {
        self.severity = severity; self.detectability = detectability; self.occurrence = occurrence
    }
}

// MARK: - Canonical encoding (STAGE5 §11) — byte-identical to the C# twin

public extension Stage5Presentation {
    /// The canonical wire bytes: camelCase keys (matching Swift spellings), lower-camel string enums,
    /// omitted nil optionals, `/` unescaped, recursive ordinal key sort. Byte-identical to the C#
    /// `Stage5Presentation.CanonicalBytes()`.
    func canonicalBytes() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(self)
    }

    /// Decode a persisted Stage-5 object. A higher `schemaVersion` than this reader supports fails
    /// closed (returns nil) so the caller renders unavailable/inconclusive (STAGE5 §12.3).
    static func decode(_ data: Data) -> Stage5Presentation? {
        guard let obj = try? JSONDecoder().decode(Stage5Presentation.self, from: data) else { return nil }
        guard obj.schemaVersion == 1 else { return nil }
        return obj
    }
}

// MARK: - §13 validation invariants

public extension Stage5Presentation {
    /// Validate against the STAGE5 §13 invariants. Returns the list of violations; empty ⇒ valid.
    /// Invalid contract output must change presentation to unavailable/inconclusive (never partially
    /// render as trusted). This is the engine's own guard before the object crosses the boundary.
    func validationErrors() -> [String] {
        var errs: [String] = []
        func idOK(_ s: String) -> Bool {
            !s.isEmpty && s.allSatisfy { c in
                c.isLetter || c.isNumber || c == "." || c == "_" || c == ":" || c == "-"
            }
        }

        // (V1 / STAGE5 §12.3) Unsupported schemaVersion fails closed at the validation gate too — the
        // Conclusion Codable path decodes stage5 via decodeIfPresent, bypassing decode(_:)'s guard, so
        // isValid must reject an unknown version here or a schemaVersion≠1 object renders as trusted.
        if schemaVersion != 1 { errs.append("unsupported schemaVersion \(schemaVersion)") }

        // (1) IDs are nonempty and use the allowed charset.
        for o in provenance.direct where !idOK(o.observationId) { errs.append("direct.observationId invalid: \(o.observationId)") }
        for f in provenance.inferred where !idOK(f.findingId) { errs.append("inferred.findingId invalid: \(f.findingId)") }
        for f in findings where !idOK(f.findingId) { errs.append("findings.findingId invalid: \(f.findingId)") }

        // (1) IDs unique within their namespace.
        let directIds = provenance.direct.map { $0.observationId }
        if Set(directIds).count != directIds.count { errs.append("direct observationId not unique") }
        let findingIds = findings.map { $0.findingId }
        if Set(findingIds).count != findingIds.count { errs.append("findings findingId not unique") }

        // (2) Every inference/finding source observation exists in provenance.direct.
        let directSet = Set(directIds)
        for f in provenance.inferred {
            for sid in f.sourceObservationIds where !directSet.contains(sid) {
                errs.append("inferred \(f.findingId) references missing observation \(sid)")
            }
        }
        for f in findings {
            for sid in f.sourceObservationIds where !directSet.contains(sid) {
                errs.append("finding \(f.findingId) references missing observation \(sid)")
            }
        }

        // (3) Every answer-entry observationId (when present) exists and is a dnsAnswerSet direct
        //     observation with the same vantage.
        let byObsId = Dictionary(provenance.direct.map { ($0.observationId, $0) }, uniquingKeysWith: { a, _ in a })
        for row in answerMatrix {
            for e in row.entries {
                if let oid = e.observationId {
                    guard let obs = byObsId[oid] else { errs.append("answer entry references missing observation \(oid)"); continue }
                    if obs.valueKind != .dnsAnswerSet { errs.append("answer entry \(oid) is not a dnsAnswerSet observation") }
                    if obs.vantage.vantageId != e.vantage.vantageId { errs.append("answer entry \(oid) vantage mismatch") }
                }
            }
        }

        // (4) Direct and unavailable datapoint identities do not overlap.
        let directDatapoints = Set(provenance.direct.map { $0.datapointId })
        for u in provenance.unavailable where directDatapoints.contains(u.datapointId) {
            errs.append("datapoint both direct and unavailable: \(u.datapointId)")
        }

        // (6) Every scalar is finite and within range.
        func finiteIn01(_ v: Double, _ label: String) {
            if !v.isFinite || v < 0 || v > 1 { errs.append("\(label) out of [0,1]: \(v)") }
        }
        if let c = honesty.conflict?.value { finiteIn01(c, "conflict") }
        if let u = honesty.uncertainty?.value { finiteIn01(u, "uncertainty") }
        if let cm = determination.confidence?.value { finiteIn01(cm, "confidence") }
        var shareSum = 0.0; var anyShare = false
        for w in determination.withinClass { if let s = w.share { finiteIn01(s, "withinClass.share"); shareSum += s; anyShare = true } }
        if anyShare && abs(shareSum - 1.0) > 1e-9 { errs.append("withinClass shares do not sum to 1: \(shareSum)") }

        // (7) Consensus is absent unless sufficient valid references exist; local-only cannot be consensus.
        // (8) benignSetVariation/agreement cannot list outliers; discrepancy needs ≥1 outlier and ≥1 reference.
        for row in answerMatrix {
            let cmp = row.comparison
            switch cmp.classification {
            case .agreement, .benignSetVariation:
                if !cmp.outlierVantageIds.isEmpty { errs.append("row \(row.rowId): \(cmp.classification.rawValue) lists outliers") }
            case .discrepancy:
                if cmp.outlierVantageIds.isEmpty { errs.append("row \(row.rowId): discrepancy has no outlier") }
                if cmp.referenceVantageIds.isEmpty { errs.append("row \(row.rowId): discrepancy has no reference") }
            case .insufficientReferences:
                if !cmp.consensusAnswers.isEmpty { errs.append("row \(row.rowId): insufficientReferences must not carry consensus") }
            case .incomparable, .notApplicable:
                break
            }
        }

        // (9) Clean is impossible with a hard gate blocking clean, a positive/unresolved tamper, etc.
        if determination.state == .clean {
            if determination.mechanismClass != nil { errs.append("clean state must have nil mechanismClass") }
            if !determination.withinClass.isEmpty { errs.append("clean state must have empty withinClass") }
            if let hg = honesty.hardGate, hg.blockedResults.contains(.clean) {
                errs.append("clean state while a hard gate blocks clean")
            }
            // (V2, §13 rule 9) Clean is impossible with critical data missing.
            if completeness.criticalAvailable < completeness.criticalExpected {
                errs.append("clean state impossible with critical missing data (\(completeness.criticalAvailable)/\(completeness.criticalExpected))")
            }
            // (V3) A clean result cannot sit above prioritized tamper findings.
            if !findings.isEmpty { errs.append("clean state must have no prioritized findings") }
            // (V4) A clean result cannot carry an active conflict signal ("the evidence disagrees").
            if let c = honesty.conflict?.value, c > 0 {
                errs.append("clean state cannot carry an active conflict signal (\(c))")
            }
        }
        // (11) A named mechanism class must be compatible with state.
        if determination.state == .clean && determination.basis == .decisiveSafetyGate {
            errs.append("clean state cannot use decisiveSafetyGate basis")
        }
        if determination.state == .tampered && determination.mechanismClass == nil {
            errs.append("tampered state requires a mechanismClass (use .unresolved)")
        }

        // (10) No-data ⇒ inconclusive, nil confidence, zero inferred findings.
        if dataRegime == .noData {
            if determination.state != .inconclusive { errs.append("noData regime must be inconclusive") }
            if determination.confidence != nil { errs.append("noData regime must have nil confidence") }
            if !provenance.inferred.isEmpty { errs.append("noData regime must have zero inferred findings") }
            if completeness.observed != 0 { errs.append("noData regime must have observed == 0") }
        }

        // Completeness counts (STAGE5 §8).
        let cc = completeness
        if cc.observed < 0 || cc.expected <= 0 || cc.observed > cc.expected {
            errs.append("completeness observed/expected invalid: \(cc.observed)/\(cc.expected)")
        }
        if cc.criticalAvailable < 0 || cc.criticalExpected < 0 || cc.criticalAvailable > cc.criticalExpected {
            errs.append("completeness critical counts invalid: \(cc.criticalAvailable)/\(cc.criticalExpected)")
        }

        // (13) Priority scores equal the declared formula; ranks unique; list order matches rank.
        for f in findings {
            let p = f.priority
            if p.modelId == "syxx.stage5.sd" && p.modelVersion == 1 {
                if p.score != p.factors.severity * p.factors.detectability {
                    errs.append("finding \(f.findingId) score != severity*detectability")
                }
                if p.factors.occurrence != nil { errs.append("finding \(f.findingId) v1 must have nil occurrence") }
            }
        }
        let ranks = findings.map { $0.priority.rank }
        if Set(ranks).count != ranks.count { errs.append("finding ranks not unique") }
        if ranks != ranks.sorted() { errs.append("findings not in ascending rank order") }

        return errs
    }

    var isValid: Bool { validationErrors().isEmpty }
}
