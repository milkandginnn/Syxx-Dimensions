import Foundation

/// The output of a full Syxx run: which checks ran, what they observed, and any raw exposure
/// findings. This is deliberately NOT a diagnosis. Syxx does not compute a result, a confidence
/// score, or a probability distribution over threat hypotheses — it reports what was actually
/// measured and leaves interpretation to the reader. (Owner directive, 2026-07-21: the prior
/// determinative engine — Dempster-Shafer hypothesis combination, a hand-tuned weight rubric, a
/// synthesized "confidence" and "diagnosis" — produced a uniform, unmoving distribution across
/// every run regardless of real evidence, and is being removed in full, not patched again.)
public struct Conclusion: Codable, Sendable {
    public let generatedAt: Date
    /// Per-section terminal status — did each check run, and how. `untested`/`invalidated` mean
    /// the check produced no usable evidence; that is reported plainly, not folded into a score.
    public let sectionStatuses: [SectionStatusEntry]
    /// Raw observations, ranked/unranked — whatever each check actually saw. No weight, no
    /// "supports hypothesis X" framing: just what was observed and why it was observed.
    public let evidence: [EvidenceItem]
    /// Exposure / security-hygiene findings (e.g. an open resolver) — always reported as raw
    /// findings, never as part of any synthesized judgment.
    public let advisories: [Advisory]

    /// The matrix-based determinative result, when the determinative engine could run over this
    /// run's evidence. This is the *new* result layer (owner directive, 2026-07-21, superseding the
    /// full-removal directive): a tri-state `DeterminativeResult` — `.clean` / `.threatDetected` /
    /// `.inconclusive` — plus the ranked diagnoses the evidence affirmatively supports and the
    /// honesty overhang (threat signals seen but unverifiable). It is `nil` only when the engine
    /// itself could not be constructed/evaluated (a spec or bundle validation failure) — in which
    /// case NO result is manufactured; the raw sections/evidence above still stand on their own.
    /// The engine NEVER emits an abstention as a scored diagnosis; abstention lives only in
    /// `determination?.state == .inconclusive` with an empty `diagnoses` list.
    public let determination: DeterminativeResult?

    public init(generatedAt: Date, sectionStatuses: [SectionStatusEntry] = [],
                evidence: [EvidenceItem] = [], advisories: [Advisory] = [],
                determination: DeterminativeResult? = nil) {
        self.generatedAt = generatedAt
        self.sectionStatuses = sectionStatuses
        self.evidence = evidence
        self.advisories = advisories
        self.determination = determination
    }

    /// True when a required check never ran — this report is incomplete, not "clean."
    /// Purely about what was measured, not a judgment about what it means.
    /// Distinct from `.invalidated` (check ran but produced corrupted data) and
    /// `.inconclusive` (check ran, data was valid, but no conclusion was reached).
    public var hasUntestedSections: Bool {
        sectionStatuses.contains { $0.status == .untested }
    }

    /// True when a required check ran but produced untrustworthy output — the data
    /// is structurally invalid (corrupted packet, failed signature, out-of-bounds).
    public var hasInvalidatedSections: Bool {
        sectionStatuses.contains { $0.status == .invalidated }
    }

    /// True when any required section is either untested or invalidated — this report
    /// is structurally incomplete and cannot render a clean result.
    public var hasHardGatedSection: Bool {
        hasUntestedSections || hasInvalidatedSections
    }
}

/// One observed signal — what a check saw and why. No weight, no hypothesis-support mapping:
/// those encoded a determinative judgment this product no longer makes. `source` is kept because
/// it is about deduplicating which probe produced an observation, not about scoring it.
public struct EvidenceItem: Codable, Sendable {
    public let section: Section
    public let evidenceId: EvidenceId
    public let observation: String
    public let rationale: String
    public let source: String?

    public init(section: Section, evidenceId: EvidenceId, observation: String, rationale: String, source: String? = nil) {
        self.section = section
        self.evidenceId = evidenceId
        self.observation = observation
        self.rationale = rationale
        self.source = source ?? EvidenceItem.correlationGroup(for: evidenceId)
    }

    /// Grouping for display only (e.g. "these three observations came from the same underlying
    /// probe") — not used to weight or combine anything.
    static func correlationGroup(for id: EvidenceId) -> String? {
        switch id {
        case .nxSubdomainHijack, .denialInjected, .denialBare:
            return "nxdomain_hijack"
        case .dnsUnencrypted, .encryptedDnsBlocked, .svcbStripped:
            return "transport_downgrade"
        case .authoritativeDiverges, .authoritativeDivergesConfirmed:
            return "authoritative_divergence"
        default:
            return nil
        }
    }
}

public enum EvidenceId: String, Codable, Sendable {
    case resolverStatus
    case dnssecExpired
    case nxCanaryClean
    case nxCanaryHijack
    case nxCanaryAtypical
    case dnssecCanarySecure
    case dnssecCanaryStripped
    case dnssecCanaryExpired
    case redirection
    case scriptMismatch
    case captivePortalActive
    case dnsUnencrypted
    case dnsEncrypted
    case dnsLeakConfirmed
    case exfilLikely
    case exfilSuspicious
    case answerDrift
    case configChange
    case authoritativeDiverges
    case svcbStripped
    case answerUnstable
    case encryptedDnsBlocked
    case nxSubdomainHijack
    case denialBare
    case denialInjected
    case ipv4Suppressed
    case ttlLockstep
    case ttlExtreme
    case cnameChainDivergent
    case ednsBlocked
    case ecsSubnetLeak
    case resolverBannerAnomaly
    case weakStubEntropy
    case responseSizeAnomaly
    case authoritativeDivergesConfirmed
    case gatewayDhcpOffLinkDNS
    case attributionIncomplete   // C058 — a material fraction of capture records carried no process identity (run-quality caveat, never a threat-naming datapoint)
    case scriptMismatchInconclusive  // C199 — warp could not determine whether content was modified (distinct from confirmed tampering)
}

/// The six canonical disciplines a piece of evidence can come from.
public enum Section: String, Codable, Sendable, CaseIterable {
    case prophet, mirror, portal, glutton, cage, witness
}

/// Per-section terminal status. `untested`/`invalidated` mean no usable evidence exists from that
/// check; that is reported as a fact about the report's completeness, not modeled as a score.
public enum SectionStatus: String, Codable, Sendable {
    case untested       // never ran: skipped, locked, missing/invalid input, no privilege, no collector
    case invalidated    // ran, but a required premise failed — its output must not be interpreted
    case inconclusive   // ran, evidence exists but does not resolve to a clear observation
    case degraded       // ran, evidence exists but is partial (noncritical field missing)
    case positive       // ran, found tampering / leak / exfiltration / change
    case negative       // ran, no positive finding

    public var blocksClean: Bool { self == .untested || self == .invalidated }
}

public struct SectionStatusEntry: Codable, Sendable {
    public let section: Section
    public let status: SectionStatus
    /// Fraction of expected probes that ran successfully (0..1).
    public let completeness: Double
    public init(section: Section, status: SectionStatus, completeness: Double = 1.0) {
        self.section = section
        self.status = status
        self.completeness = max(0.0, min(1.0, completeness))
    }
    enum CodingKeys: String, CodingKey {
        case section, status, completeness
    }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        section = try c.decode(Section.self, forKey: .section)
        status = try c.decode(SectionStatus.self, forKey: .status)
        // B165: default to 0.0 (missing data = unknown completeness, not fabricated certainty)
        // and apply the same [0,1] clamp the memberwise init uses — an out-of-range value from
        // an older payload must not survive decoding as user-visible completeness / quality.
        let raw = try c.decodeIfPresent(Double.self, forKey: .completeness) ?? 0.0
        completeness = max(0.0, min(1.0, raw))
    }
}

/// Colour/urgency buckets, mapped to the Io severity palette in the GUI. Used only by
/// `Advisory` (exposure findings) now — never to color a synthesized diagnosis.
public enum Severity: String, Codable, Sendable {
    case clean, info, warning, high, critical, unknown
}

/// An exposure / security-hygiene finding — surfaced as a raw finding, never as part of any
/// synthesized judgment. (ROADMAP Portal / Workstream S.)
public struct Advisory: Codable, Sendable {
    public let section: Section
    public let advisoryId: AdvisoryId
    public let title: String
    public let detail: String
    public let severity: Severity
    public init(section: Section, advisoryId: AdvisoryId, title: String, detail: String, severity: Severity) {
        self.section = section
        self.advisoryId = advisoryId
        self.title = title
        self.detail = detail
        self.severity = severity
    }
}

public enum AdvisoryId: String, Codable, Sendable {
    case openResolverExposure
}

#if DEBUG
public extension Conclusion {
    /// Sample data for GUI previews and engine tests ONLY. Compiled exclusively in DEBUG builds
    /// (C061): this object carries invented section statuses and evidence, so it must be
    /// structurally unreachable from a release binary — CI verifies the symbol is absent from the
    /// release products. Never bind it in a production path.
    static let mockInconclusive = Conclusion(
        generatedAt: Date(),
        sectionStatuses: [
            .init(section: .prophet, status: .negative),
            .init(section: .mirror,  status: .untested),
            .init(section: .portal,  status: .degraded),
            .init(section: .glutton, status: .untested),
            .init(section: .cage,    status: .negative),
            .init(section: .witness, status: .inconclusive),
        ],
        evidence: [
            .init(section: .portal,
                  evidenceId: .configChange,
                  observation: "Active resolver is an unexpected on-link device",
                  rationale: "Not the default gateway, and encrypted DNS is off — a device you didn't choose may be answering."),
        ])
}
#endif

public extension EvidenceId {
    var displayName: String {
        switch self {
        case .resolverStatus:                    return "DNS Resolver Status Check"
        case .dnssecExpired:                     return "Expired DNSSEC Signature"
        case .nxCanaryClean:                     return "Healthy Non-Existent Domain Resolution"
        case .nxCanaryHijack:                    return "Non-Existent Domain Interception"
        case .nxCanaryAtypical:                  return "Atypical Non-Existent Domain Resolution"
        case .dnssecCanarySecure:                return "Secure DNSSEC Resolution"
        case .dnssecCanaryStripped:              return "DNSSEC Validation Stripped"
        case .dnssecCanaryExpired:               return "Expired DNSSEC Canary Check"
        case .redirection:                       return "DNS Redirection Detected"
        case .scriptMismatch:                    return "Script Structure Divergence"
        case .captivePortalActive:               return "Active Captive Portal Detected"
        case .dnsUnencrypted:                    return "Unencrypted DNS Queries"
        case .dnsEncrypted:                      return "Encrypted DNS Queries"
        case .dnsLeakConfirmed:                  return "Confirmed DNS Query Leak"
        case .exfilLikely:                       return "Likely Data Exfiltration"
        case .exfilSuspicious:                   return "Suspicious DNS Query Patterns"
        case .answerDrift:                       return "DNS Answer Drift Detected"
        case .configChange:                      return "System Network Configuration Change"
        case .authoritativeDiverges:             return "Authoritative Nameserver Divergence"
        case .svcbStripped:                      return "HTTPS/SVCB Record Stripped"
        case .answerUnstable:                    return "Unstable DNS Resolution"
        case .encryptedDnsBlocked:               return "Encrypted DNS Connection Blocked"
        case .nxSubdomainHijack:                 return "Subdomain Interception (Wildcard Ingestion)"
        case .denialBare:                        return "Missing Authentication in Negative Answer"
        case .denialInjected:                    return "Injected Negative Answer"
        case .ipv4Suppressed:                    return "IPv4 Address Resolution Blocked"
        case .ttlLockstep:                       return "Synchronized Time-To-Live (TTL) Values"
        case .ttlExtreme:                        return "Abnormally Large Time-To-Live (TTL)"
        case .cnameChainDivergent:               return "Outlier Canonical Name (CNAME) Chain"
        case .ednsBlocked:                       return "Extension Mechanisms for DNS (EDNS) Blocked"
        case .ecsSubnetLeak:                     return "Client Subnet Leakage (ECS)"
        case .resolverBannerAnomaly:             return "Atypical Local DNS Forwarder Banner"
        case .weakStubEntropy:                   return "Weak Stub Resolver Entropy"
        case .responseSizeAnomaly:                return "Abnormally Large DNS Response Size"
        case .authoritativeDivergesConfirmed:    return "Confirmed Authoritative Nameserver Divergence"
        case .gatewayDhcpOffLinkDNS:              return "Off-Subnet DHCP DNS Configuration"
        case .attributionIncomplete:             return "Incomplete Process Attribution"
        case .scriptMismatchInconclusive:        return "Content Integrity Check Inconclusive"
        }
    }
}
