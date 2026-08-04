import Foundation

/// Two dependency-less DNS probes that resolve the most common ambiguity in a Syxx run:
/// *is a negative/normal answer honest, or is the path rewriting it?*
///
/// Design constraints (from the DoW #1 vetting round):
/// - **One-shot, per-session, high-entropy.** Names are freshly randomized each call and never
///   reused, so a cached or learned answer can't train the resolver into a stable-but-wrong reply.
/// - **Weak alone.** A single anomaly is only a *signal*; each probe corroborates *within itself*
///   (two random names must agree) and the synthesis accumulator corroborates *across* sections.
///   These functions never emit a result — they emit an `EvidenceItem` + a `SectionStatus` for the
///   Prophet section, and the combiner decides.
/// - **Honest failure.** A probe that can't reach the resolver returns `.invalidated`/`.inconclusive`,
///   never a false `.negative`.
public enum DNSCanaries {

    /// The outcome of one canary probe, expressed in the shared conclusion vocabulary.
    public struct CanaryResult: Sendable {
        public let section: Section          // always .prophet (resolver honesty)
        public let status: SectionStatus
        public let evidence: EvidenceItem?   // nil for untested/invalidated (no usable finding)
        public let detail: String            // one-line technical note for the report
        public init(section: Section, status: SectionStatus, evidence: EvidenceItem?, detail: String) {
            self.section = section
            self.status = status
            self.evidence = evidence
            self.detail = detail
        }
    }

    /// A fresh 96-bit random hex label — never reused across probes or sessions.
    private static func randomLabel() -> String {
        let hex = "0123456789abcdef"
        return String((0..<24).map { _ in hex.randomElement()! })
    }

    /// **NXDOMAIN-hijack canary.** Queries two independent random, guaranteed-nonexistent names.
    /// Both should return NXDOMAIN. Two non-NXDOMAIN answers pointing at the **same** IP is the
    /// hijack signal — a real domain would never share an address with an unrelated random name, so
    /// a shared answer means the resolver is rewriting nonexistent domains (ad/redirect hijack).
    public static func nxdomainHijack(resolver: String) async -> CanaryResult {
        let n1 = randomLabel() + ".com"
        let n2 = randomLabel() + ".com"
        let r1 = await DNSResolver.queryUDPDetailed(DNSQuery(name: n1, type: 1, server: resolver))
        let r2 = await DNSResolver.queryUDPDetailed(DNSQuery(name: n2, type: 1, server: resolver))

        if r1.didFail && r2.didFail {
            return CanaryResult(section: .prophet, status: .invalidated, evidence: nil,
                detail: "NXDOMAIN canary could not reach the resolver (\(r1.status.rawValue)/\(r2.status.rawValue)).")
        }
        if r1.status == .nxDomain && r2.status == .nxDomain {
            return CanaryResult(section: .prophet, status: .negative,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .nxCanaryClean,
                    observation: "Random nonexistent names correctly returned NXDOMAIN",
                    rationale: "The resolver does not rewrite nonexistent domains — no NXDOMAIN hijacking.",
                    source: "canaries"),
                detail: "Both canary names → NXDOMAIN.")
        }
        // C071: compare A records, AAAA records, AND CNAME targets for the shared-destination
        // test. A CNAME-only hijack (redirect to a search/ad page) has no A record but does
        // have a CNAME target — the original code missed this entirely.
        let ips1 = Set(r1.records.filter { $0.type == 1 }.map { $0.rdata })
        let ips2 = Set(r2.records.filter { $0.type == 1 }.map { $0.rdata })
        let shared = ips1.intersection(ips2)
        if let ip = shared.first, !ip.isEmpty {
            return CanaryResult(section: .prophet, status: .positive,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .nxCanaryHijack,
                    observation: "Two unrelated random names both resolved to \(ip)",
                    rationale: "A resolver that answers nonexistent domains with a shared IP is rewriting NXDOMAIN — a redirect/ad hijack, not honest resolution.",
                    source: "canaries"),
                detail: "Canary hijack: \(n1) & \(n2) → \(ip).")
        }
        // C071: compare CNAME targets — a shared CNAME destination is the same hijack signal
        // as a shared A record.
        let cnames1 = Set(r1.records.filter { $0.type == 5 }.map { $0.rdata })
        let cnames2 = Set(r2.records.filter { $0.type == 5 }.map { $0.rdata })
        let sharedCNAME = cnames1.intersection(cnames2)
        if let cname = sharedCNAME.first, !cname.isEmpty {
            return CanaryResult(section: .prophet, status: .positive,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .nxCanaryHijack,
                    observation: "Two unrelated random names both redirected to CNAME \(cname)",
                    rationale: "A resolver that rewrites nonexistent domains with a shared CNAME target is performing a redirect/ad hijack.",
                    source: "canaries"),
                detail: "Canary CNAME hijack: \(n1) & \(n2) → \(cname).")
        }
        // C071: both canaries returned answers (any type) — a random 24-hex-digit label under
        // .com cannot legitimately resolve at all. Two independent random names both producing
        // answers is decisive, regardless of whether the answers overlap.
        let bothAnswered = !r1.records.isEmpty && !r2.records.isEmpty
            && r1.status != .nxDomain && r2.status != .nxDomain
        if bothAnswered {
            return CanaryResult(section: .prophet, status: .positive,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .nxCanaryHijack,
                    observation: "Two unrelated random nonexistent names both returned answers (\(r1.status.rawValue)/\(r2.status.rawValue))",
                    rationale: "A name with 96 bits of entropy cannot exist under .com — two such names both resolving is decisive evidence of NXDOMAIN rewriting, even without a shared destination.",
                    source: "canaries"),
                detail: "Canary hijack: \(n1)=\(r1.status.rawValue) & \(n2)=\(r2.status.rawValue) both resolved (per-name redirect).")
        }
        return CanaryResult(section: .prophet, status: .inconclusive,
            evidence: EvidenceItem(section: .prophet,
                evidenceId: .nxCanaryAtypical,
                observation: "Canary names returned atypical, non-matching answers (\(r1.status.rawValue)/\(r2.status.rawValue))",
                rationale: "Not a clean NXDOMAIN, but not a consistent hijack IP either — treat as a weak signal to corroborate with other sections.",
                source: "canaries"),
            detail: "n1=\(r1.status.rawValue) ips=\(ips1.sorted()); n2=\(r2.status.rawValue) ips=\(ips2.sorted()).")
    }

    /// **DNSSEC control canary.** A domain known to be DNSSEC-signed must still carry DNSKEY+RRSIG
    /// on this path. `unsigned` from a signed control is the stripping/downgrade signal — the
    /// records were removed in transit. Expired-only RRSIGs suggest replay or a broken interposer.
    /// (The client does not do full chain validation, and does not claim to; this detects the
    /// coarse, common attack — records missing/stale — which is exactly what a downgrade looks like.)
    public static func dnssecControl(resolver: String, control: String = "cloudflare.com") async -> CanaryResult {
        let status = await DNSSECVerifier.checkStatus(domain: control, resolver: resolver)
        switch status {
        case .recordsPresentUnverified:
            return CanaryResult(section: .prophet, status: .negative,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .dnssecCanarySecure,
                    observation: "DNSSEC records for the signed control \(control) are intact on this path",
                    rationale: "DNSKEY + RRSIG present within a valid window — no sign of on-path DNSSEC stripping.",
                    source: "canaries"),
                detail: "control=\(control) → recordsPresentUnverified")
        case .unsigned:
            return CanaryResult(section: .prophet, status: .positive,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .dnssecCanaryStripped,
                    observation: "Signed control \(control) came back UNSIGNED — DNSSEC records stripped on this path",
                    rationale: "\(control) is DNSSEC-signed; missing DNSKEY/RRSIG indicates a downgrade/stripping interposer between you and the authoritative server.",
                    source: "canaries"),
                detail: "control=\(control) → unsigned (stripping)")
        case .signatureExpiredOrNotYetValid:
            // B047: expired RRSIGs on a known-good control can mean either a replay/stripping
            // interposer OR a skewed local clock. Since the local clock is attacker-controllable
            // (and benignly wrong on many machines), we cannot render this as a threat signal.
            // Emit inconclusive — the expiry is real but its cause is undecidable from one vantage.
            return CanaryResult(section: .prophet, status: .inconclusive,
                evidence: EvidenceItem(section: .prophet,
                    evidenceId: .dnssecCanaryExpired,
                    observation: "Signed control \(control) returned only expired / not-yet-valid RRSIGs",
                    rationale: "Stale signatures on a known-good control could indicate replay/stripping on the path OR a skewed local clock — the cause cannot be determined from one vantage. This is not used as a threat signal.",
                    source: "canaries"),
                detail: "control=\(control) → expired RRSIGs (clock-sensitive, inconclusive)")
        case .inconclusive:
            return CanaryResult(section: .prophet, status: .invalidated, evidence: nil,
                detail: "DNSSEC control canary could not fetch records for \(control) (transport/timeout).")
        }
    }
}
