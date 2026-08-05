import Foundation

public enum SyxxSection: String, CaseIterable, Codable, Sendable {
    case prophet
    case mirror
    case portal
    case glutton
    case cage
    case witness

    public var id: SectionID { SectionID(rawValue: rawValue) }
}

public enum SyxxIndicator: String, CaseIterable, Codable, Sendable {
    // Prophet
    case resolverAgreement
    case resolverDisagreement
    case authoritativeMismatch
    case signedAnswerMismatch

    // Mirror
    case remoteLocalDivergence
    case geoVantageDivergence
    case multiResolverDisparity

    // Portal
    case lanDnsRedirect
    case resolverPathAnomaly
    case routeClean
    case captivePortalSignal

    // Glutton
    case dnsLeakDetected
    case protectedTransportBypass
    case forcedPlainDnsFallback

    // Cage
    case liveQueryTamperPattern
    case liveQueryContentRewrite
    case suspiciousNxDomainPattern

    // Witness
    case trustedWitnessOK
    case signedWitnessMismatch
    case localEvidenceSealValid
    case localEnvironmentCompromised

    public var id: IndicatorID { IndicatorID(rawValue: rawValue) }
}

public enum SyxxDiagnosis: String, CaseIterable, Codable, Sendable {
    case clean
    case dnsSpoofing
    case censorshipBlocking
    case transportDowngrade
    case dnsInterception
    case contentTampering
    case dnsPrivacyLeak
    case localCompromise

    public var id: DiagnosisID { DiagnosisID(rawValue: rawValue) }
}

public struct SyxxMatrixBuilder {
    public init() {}

    public func buildDefaultSpec() -> MatrixSpec {
        let prophet = SyxxSection.prophet.id
        let mirror = SyxxSection.mirror.id
        let portal = SyxxSection.portal.id
        let glutton = SyxxSection.glutton.id
        let cage = SyxxSection.cage.id
        let witness = SyxxSection.witness.id

        let clean = DiagnosisSpec(
            id: SyxxDiagnosis.clean.id,
            displayName: "Clean",
            rules: [
                .init(indicatorID: SyxxIndicator.resolverAgreement.id, polarity: .supports, expectedStrength: 0.95, weight: 1.00, evidenceClass: .major, independenceGroup: "resolver"),
                .init(indicatorID: SyxxIndicator.routeClean.id, polarity: .supports, expectedStrength: 0.90, weight: 0.80, evidenceClass: .major, independenceGroup: "route"),
                .init(indicatorID: SyxxIndicator.trustedWitnessOK.id, polarity: .supports, expectedStrength: 0.95, weight: 1.20, evidenceClass: .decisive, independenceGroup: "trust"),
                .init(indicatorID: SyxxIndicator.localEvidenceSealValid.id, polarity: .supports, expectedStrength: 0.95, weight: 0.70, evidenceClass: .minor, independenceGroup: "local"),
                .init(indicatorID: SyxxIndicator.lanDnsRedirect.id, polarity: .excludes, expectedStrength: 1.0, weight: 1.25, evidenceClass: .decisive, independenceGroup: "path"),
                .init(indicatorID: SyxxIndicator.authoritativeMismatch.id, polarity: .excludes, expectedStrength: 1.0, weight: 1.00, evidenceClass: .major, independenceGroup: "answer"),
                .init(indicatorID: SyxxIndicator.dnsLeakDetected.id, polarity: .excludes, expectedStrength: 1.0, weight: 0.90, evidenceClass: .major, independenceGroup: "privacy"),
                .init(indicatorID: SyxxIndicator.localEnvironmentCompromised.id, polarity: .excludes, expectedStrength: 1.0, weight: 1.30, evidenceClass: .decisive, independenceGroup: "host")
            ],
            requiredSections: [prophet, witness],
            exclusionaryIndicators: [
                SyxxIndicator.lanDnsRedirect.id,
                SyxxIndicator.authoritativeMismatch.id,
                SyxxIndicator.localEnvironmentCompromised.id
            ],
            mutuallyExclusiveWith: [
                SyxxDiagnosis.dnsInterception.id,
                SyxxDiagnosis.dnsSpoofing.id,
                SyxxDiagnosis.contentTampering.id,
                SyxxDiagnosis.localCompromise.id
            ]
        )

        let dnsSpoofing = DiagnosisSpec(
            id: SyxxDiagnosis.dnsSpoofing.id,
            displayName: "DNS spoofing / poisoning",
            rules: [
                .init(indicatorID: SyxxIndicator.authoritativeMismatch.id, polarity: .supports, expectedStrength: 0.95, weight: 1.20, evidenceClass: .decisive, independenceGroup: "answer", decayExponent: 1.3),
                .init(indicatorID: SyxxIndicator.signedAnswerMismatch.id, polarity: .supports, expectedStrength: 0.90, weight: 1.10, evidenceClass: .major, independenceGroup: "signature"),
                .init(indicatorID: SyxxIndicator.signedWitnessMismatch.id, polarity: .supports, expectedStrength: 0.80, weight: 1.00, evidenceClass: .major, independenceGroup: "trust"),
                .init(indicatorID: SyxxIndicator.resolverDisagreement.id, polarity: .supports, expectedStrength: 0.80, weight: 0.80, evidenceClass: .major, independenceGroup: "resolver"),
                .init(indicatorID: SyxxIndicator.trustedWitnessOK.id, polarity: .excludes, expectedStrength: 1.0, weight: 0.90, evidenceClass: .major, independenceGroup: "trust")
            ],
            requiredSections: [prophet, mirror, witness],
            exclusionaryIndicators: [SyxxIndicator.trustedWitnessOK.id],
            mutuallyExclusiveWith: [SyxxDiagnosis.clean.id],
            allowedCooccurrenceWith: [SyxxDiagnosis.contentTampering.id]
        )

        let censorshipBlocking = DiagnosisSpec(
            id: SyxxDiagnosis.censorshipBlocking.id,
            displayName: "Censorship / blocking",
            rules: [
                .init(indicatorID: SyxxIndicator.captivePortalSignal.id, polarity: .supports, expectedStrength: 0.80, weight: 0.70, evidenceClass: .major, independenceGroup: "portal"),
                .init(indicatorID: SyxxIndicator.remoteLocalDivergence.id, polarity: .supports, expectedStrength: 0.85, weight: 1.00, evidenceClass: .major, independenceGroup: "vantage"),
                .init(indicatorID: SyxxIndicator.geoVantageDivergence.id, polarity: .supports, expectedStrength: 0.90, weight: 1.10, evidenceClass: .decisive, independenceGroup: "geo"),
                .init(indicatorID: SyxxIndicator.suspiciousNxDomainPattern.id, polarity: .supports, expectedStrength: 0.70, weight: 0.80, evidenceClass: .minor, independenceGroup: "live")
            ],
            requiredSections: [mirror, portal],
            mutuallyExclusiveWith: [],
            allowedCooccurrenceWith: [SyxxDiagnosis.transportDowngrade.id]
        )

        let transportDowngrade = DiagnosisSpec(
            id: SyxxDiagnosis.transportDowngrade.id,
            displayName: "Transport downgrade",
            rules: [
                .init(indicatorID: SyxxIndicator.forcedPlainDnsFallback.id, polarity: .supports, expectedStrength: 0.95, weight: 1.20, evidenceClass: .decisive, independenceGroup: "transport", decayExponent: 1.4),
                .init(indicatorID: SyxxIndicator.protectedTransportBypass.id, polarity: .supports, expectedStrength: 0.90, weight: 1.00, evidenceClass: .major, independenceGroup: "privacy"),
                .init(indicatorID: SyxxIndicator.remoteLocalDivergence.id, polarity: .supports, expectedStrength: 0.60, weight: 0.60, evidenceClass: .minor, independenceGroup: "vantage")
            ],
            requiredSections: [glutton, mirror],
            mutuallyExclusiveWith: [],
            allowedCooccurrenceWith: [SyxxDiagnosis.censorshipBlocking.id, SyxxDiagnosis.dnsPrivacyLeak.id]
        )

        let dnsInterception = DiagnosisSpec(
            id: SyxxDiagnosis.dnsInterception.id,
            displayName: "DNS interception (transparent proxy)",
            rules: [
                .init(indicatorID: SyxxIndicator.lanDnsRedirect.id, polarity: .supports, expectedStrength: 0.95, weight: 1.30, evidenceClass: .decisive, independenceGroup: "path", decayExponent: 1.5),
                .init(indicatorID: SyxxIndicator.resolverPathAnomaly.id, polarity: .supports, expectedStrength: 0.90, weight: 1.00, evidenceClass: .major, independenceGroup: "route"),
                .init(indicatorID: SyxxIndicator.remoteLocalDivergence.id, polarity: .supports, expectedStrength: 0.70, weight: 0.70, evidenceClass: .minor, independenceGroup: "vantage"),
                .init(indicatorID: SyxxIndicator.trustedWitnessOK.id, polarity: .excludes, expectedStrength: 1.0, weight: 1.00, evidenceClass: .major, independenceGroup: "trust")
            ],
            requiredSections: [portal, prophet],
            exclusionaryIndicators: [SyxxIndicator.trustedWitnessOK.id],
            mutuallyExclusiveWith: [SyxxDiagnosis.clean.id],
            allowedCooccurrenceWith: [SyxxDiagnosis.contentTampering.id]
        )

        let contentTampering = DiagnosisSpec(
            id: SyxxDiagnosis.contentTampering.id,
            displayName: "Content tampering",
            rules: [
                .init(indicatorID: SyxxIndicator.liveQueryTamperPattern.id, polarity: .supports, expectedStrength: 0.90, weight: 1.20, evidenceClass: .decisive, independenceGroup: "live", decayExponent: 1.3),
                .init(indicatorID: SyxxIndicator.liveQueryContentRewrite.id, polarity: .supports, expectedStrength: 0.85, weight: 1.10, evidenceClass: .major, independenceGroup: "rewrite"),
                .init(indicatorID: SyxxIndicator.remoteLocalDivergence.id, polarity: .supports, expectedStrength: 0.65, weight: 0.70, evidenceClass: .minor, independenceGroup: "vantage"),
                .init(indicatorID: SyxxIndicator.signedWitnessMismatch.id, polarity: .supports, expectedStrength: 0.65, weight: 0.80, evidenceClass: .major, independenceGroup: "trust")
            ],
            requiredSections: [cage, mirror],
            mutuallyExclusiveWith: [],
            allowedCooccurrenceWith: [SyxxDiagnosis.dnsSpoofing.id, SyxxDiagnosis.dnsInterception.id]
        )

        let dnsPrivacyLeak = DiagnosisSpec(
            id: SyxxDiagnosis.dnsPrivacyLeak.id,
            displayName: "DNS privacy leak",
            rules: [
                .init(indicatorID: SyxxIndicator.dnsLeakDetected.id, polarity: .supports, expectedStrength: 0.95, weight: 1.20, evidenceClass: .decisive, independenceGroup: "privacy"),
                .init(indicatorID: SyxxIndicator.protectedTransportBypass.id, polarity: .supports, expectedStrength: 0.80, weight: 1.00, evidenceClass: .major, independenceGroup: "transport"),
                .init(indicatorID: SyxxIndicator.routeClean.id, polarity: .excludes, expectedStrength: 1.0, weight: 0.40, evidenceClass: .minor, independenceGroup: "route")
            ],
            requiredSections: [glutton],
            mutuallyExclusiveWith: [],
            allowedCooccurrenceWith: [SyxxDiagnosis.transportDowngrade.id]
        )

        let localCompromise = DiagnosisSpec(
            id: SyxxDiagnosis.localCompromise.id,
            displayName: "Local compromise / exfiltration",
            rules: [
                .init(indicatorID: SyxxIndicator.localEnvironmentCompromised.id, polarity: .supports, expectedStrength: 0.95, weight: 1.30, evidenceClass: .decisive, independenceGroup: "host"),
                .init(indicatorID: SyxxIndicator.localEvidenceSealValid.id, polarity: .excludes, expectedStrength: 1.0, weight: 0.90, evidenceClass: .major, independenceGroup: "seal"),
                .init(indicatorID: SyxxIndicator.dnsLeakDetected.id, polarity: .supports, expectedStrength: 0.75, weight: 0.70, evidenceClass: .minor, independenceGroup: "privacy"),
                .init(indicatorID: SyxxIndicator.liveQueryTamperPattern.id, polarity: .supports, expectedStrength: 0.70, weight: 0.80, evidenceClass: .major, independenceGroup: "live")
            ],
            requiredSections: [witness],
            exclusionaryIndicators: [],
            mutuallyExclusiveWith: [SyxxDiagnosis.clean.id]
        )

        // B156: the set of indicator IDs that have at least one reachable EvidenceId mapping.
        // Support rules referencing unmapped indicators (e.g. resolverAgreement,
        // remoteLocalDivergence, trustedWitnessOK) are excluded from the coverage denominator,
        // preventing unmapped rules from creating permanent dead weight that hard-caps coverage
        // below the minimum thresholds and makes entire diagnoses unreachable.
        let mappableIndicators = Set(
            SynthesisEngine.indicatorMapping.values.map { $0.0.id }
        )

        return MatrixSpec(
            diagnoses: [
                clean,
                dnsSpoofing,
                censorshipBlocking,
                transportDowngrade,
                dnsInterception,
                contentTampering,
                dnsPrivacyLeak,
                localCompromise
            ],
            thresholds: ThresholdProfile(
                minimumCoverageForProbable: 0.55,
                minimumCoverageForConclusive: 0.80,
                minimumFitForProbable: 0.62,
                minimumFitForConclusive: 0.84,
                minimumMarginForProbable: 0.08,
                minimumMarginForConclusive: 0.18,
                maxContradictionForConclusive: 0.10,
                maxContradictionForProbable: 0.22,
                minimumIndependentSupportGroupsForProbable: 1,
                minimumIndependentSupportGroupsForConclusive: 2,
                requireDecisiveOrTwoMajorsForConclusive: true,
                hardGateOnMissingRequiredSections: true,
                failClosedOnInvalidatedRequiredSections: true
            ),
            mappableIndicators: mappableIndicators
        )
    }
}

public struct SyxxSectionInput: Codable, Sendable {
    public let section: SyxxSection
    // NOTE: this is the matrix engine's `EvidenceUsability` (was `SectionStatus` in the original
    // draft). SyxxKit already ships a public `Conclusion.SectionStatus` with different semantics, so
    // the engine's usability axis is a distinct type — see the collision note in
    // `MatrixDeterminativeEngine.swift`. Callers converting a real run status use
    // `EvidenceUsability(sectionStatus:)`.
    public let status: EvidenceUsability
    public let trusted: Bool
    public let notes: String?

    public init(section: SyxxSection, status: EvidenceUsability, trusted: Bool = true, notes: String? = nil) {
        self.section = section
        self.status = status
        self.trusted = trusted
        self.notes = notes
    }

    public func toSectionResult() -> SectionResult {
        SectionResult(id: section.id, status: status, required: false, trusted: trusted, notes: notes)
    }
}

public struct SyxxIndicatorInput: Codable, Sendable {
    public let indicator: SyxxIndicator
    public let section: SyxxSection
    public let observedStrength: Double?
    public let quality: Double
    public let contradictory: Bool
    public let notes: String?

    public init(
        indicator: SyxxIndicator,
        section: SyxxSection,
        observedStrength: Double?,
        quality: Double = 1.0,
        contradictory: Bool = false,
        notes: String? = nil
    ) {
        self.indicator = indicator
        self.section = section
        self.observedStrength = observedStrength
        self.quality = quality
        self.contradictory = contradictory
        self.notes = notes
    }

    public func toObservedIndicator() -> ObservedIndicator {
        ObservedIndicator(
            id: indicator.id,
            section: section.id,
            observedStrength: observedStrength,
            quality: quality,
            contradictory: contradictory,
            notes: notes
        )
    }
}

public struct SyxxRunInput: Codable, Sendable {
    public let sections: [SyxxSectionInput]
    public let indicators: [SyxxIndicatorInput]

    public init(sections: [SyxxSectionInput], indicators: [SyxxIndicatorInput]) {
        self.sections = sections
        self.indicators = indicators
    }

    public func toEvidenceBundle() -> EvidenceBundle {
        EvidenceBundle(
            sections: sections.map { $0.toSectionResult() },
            indicators: indicators.map { $0.toObservedIndicator() }
        )
    }
}

public enum SyxxMatrixIntegration {
    /// B209: cache the built engine — the spec is immutable and constant, so rebuilding and
    /// re-validating on every `evaluate` is wasted work. `MatrixDeterminativeEngine` is immutable
    /// and `evaluate` has no instance state, so a shared instance is safe.
    private static let cachedEngine: Result<MatrixDeterminativeEngine, Error> = {
        Result { try MatrixDeterminativeEngine(spec: SyxxMatrixBuilder().buildDefaultSpec()) }
    }()

    public static func makeEngine() throws -> MatrixDeterminativeEngine {
        try cachedEngine.get()
    }

    public static func evaluate(_ input: SyxxRunInput) throws -> DeterminativeResult {
        let engine = try makeEngine()
        return try engine.evaluate(input.toEvidenceBundle())
    }

    public static func defaultSpecJSON(prettyPrinted: Bool = true) throws -> String {
        let encoder = JSONEncoder()
        // Task 8.4 (2026-08-05): `.withoutEscapingSlashes` — the canonical spec form uses an
        // unescaped `/`. JSONEncoder escapes it (`\/`) by default; System.Text.Json on the C# side
        // deliberately does not (UnsafeRelaxedJsonEscaping in MatrixCanonicalJson), and unescaped
        // is also what jq/python emit, which keeps oracle diffs reviewable. `.sortedKeys` applies
        // in both modes so the emission is deterministic regardless of formatting. This function's
        // only callers are the parity/oracle tests — no wire format or signature depends on it.
        encoder.outputFormatting = prettyPrinted
            ? [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]
            : [.sortedKeys, .withoutEscapingSlashes]
        let data = try encoder.encode(SyxxMatrixBuilder().buildDefaultSpec())
        return String(decoding: data, as: UTF8.self)
    }
}
