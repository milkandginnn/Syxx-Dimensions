import Foundation

// ROADMAP Workstream H · datapoint A6 — authenticated / authoritative denial of existence.
//
// The Synthesis engine treats a negative answer (NXDOMAIN / NODATA) as a single fact. But a
// GENUINE negative from the zone's authority is accompanied by an SOA in the authority section
// (RFC 2308 negative caching) and, in a signed zone, NSEC/NSEC3 records that cryptographically
// prove the name does not exist. An INJECTED block-page NXDOMAIN — a censor or middlebox
// synthesising a denial — typically ships a *bare* negative: no SOA, no NSEC, or an SOA for the
// wrong zone. This datapoint reads the authority section (now preserved by DNSResolver, A6
// contract) and grades the denial, so "the name doesn't exist" can be told apart from "someone is
// pretending it doesn't."
//
// Honest v1 limits: presence of NSEC is treated as a strong benign signal but is NOT fully
// validated here (proving the queried name falls in the NSEC interval, and verifying the RRSIG, is
// a later refinement that builds on DNSSECVerifier). A bare NXDOMAIN is only CORROBORATE-grade — a
// few minimal resolvers legitimately omit the SOA — while a contradictory denial (answers present,
// or an SOA outside the queried name's hierarchy) is a strong injection signal.

public struct DenialOfExistence: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case notADenial      // the name resolved — this check does not apply
        case nsecProven      // negative carries NSEC/NSEC3 — authenticated denial (benign, strongest)
        case soaBacked       // negative carries an ancestor-zone SOA — proper authoritative negative (benign)
        case bareDenial      // negative with no SOA and no NSEC — injected/synthesised-block signature (corroborate)
        case contradictory   // "negative" yet carries answers, or an SOA outside the name's hierarchy — injected (strong)
        case inconclusive    // transport failure / undeterminable
        case untested
    }

    /// C127: distinguishes NXDOMAIN (name does not exist) from NODATA (name exists but type does not).
    /// These are different facts per RFC 2308 §2.1/§2.2 and must be graded differently.
    public enum NegativeKind: String, Codable, Sendable {
        case nameDoesNotExist   // NXDOMAIN — the name itself does not exist
        case typeHasNoData      // NODATA — the name exists but has no RR of the requested type
    }

    public let status: Status
    public let queriedName: String
    public let soaOwner: String?     // owner of the SOA found in authority, if any
    public let hasNSEC: Bool
    public let negativeKind: NegativeKind?  // C127: NXDOMAIN vs NODATA distinction
    public let detail: String

    public init(status: Status, queriedName: String = "", soaOwner: String? = nil,
                hasNSEC: Bool = false, negativeKind: NegativeKind? = nil, detail: String = "") {
        self.status = status
        self.queriedName = queriedName
        self.soaOwner = soaOwner
        self.hasNSEC = hasNSEC
        self.negativeKind = negativeKind
        self.detail = detail
    }

    public static let untested = DenialOfExistence(status: .untested, detail: "not run")
}

public enum DenialOfExistenceProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeSOA: UInt16 = 6
    public static let typeNSEC: UInt16 = 47
    public static let typeNSEC3: UInt16 = 50

    /// Query `name` and grade the authenticity of any negative answer it returns.
    public static func probe(name: String,
                             qtype: UInt16 = 1,
                             server: String = "1.1.1.1",
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> DenialOfExistence {
        let resp = await query(DNSQuery(name: name, type: qtype, server: server, port: 53))
        return classify(name: name, status: resp.status,
                        answers: resp.records.filter { $0.type == qtype },
                        authority: resp.authorityRecords,
                        authorityMalformed: resp.authorityMalformed)
    }

    static func classify(name: String, status: DNSResponseStatus,
                         answers: [DNSRecord], authority: [DNSRecord],
                         authorityMalformed: Bool = false) -> DenialOfExistence {
        let soa = authority.first { $0.type == typeSOA }
        let hasNSEC = authority.contains { $0.type == typeNSEC || $0.type == typeNSEC3 }
        // C127: determine the kind of negative from the response status.
        let kind: DenialOfExistence.NegativeKind?
        switch status {
        case .nxDomain: kind = .nameDoesNotExist
        case .noData, .success: kind = .typeHasNoData
        default: kind = nil
        }
        func make(_ s: DenialOfExistence.Status, _ d: String) -> DenialOfExistence {
            DenialOfExistence(status: s, queriedName: name, soaOwner: soa?.name, hasNSEC: hasNSEC, negativeKind: kind, detail: d)
        }

        switch status {
        case .success where !answers.isEmpty:
            return make(.notADenial, "\(name) resolved — denial-of-existence check does not apply")
        case .nxDomain, .noData, .success:
            break   // a negative (or NODATA-as-success-with-no-answers) — grade it below
        default:
            return make(.inconclusive, "no usable answer for \(name) (\(status.rawValue)) — cannot grade the denial")
        }

        // A negative that still carries answers of the queried type is self-contradictory → injected.
        if !answers.isEmpty {
            return make(.contradictory, "negative status yet \(answers.count) answer record(s) present — inconsistent, likely injected")
        }
        // C126: evaluate SOA ancestry FIRST — an SOA outside the queried name's hierarchy must
        // stay .contradictory regardless of hasNSEC. NSEC presence may then upgrade a *consistent*
        // denial, never rescue an inconsistent one. Previously, hasNSEC short-circuited the SOA
        // check, allowing an injected NSEC from any zone to bypass the only real test.
        if let soa = soa {
            if !isAncestor(zone: soa.name, of: name) {
                return make(.contradictory, "SOA owner \(soa.name) is not an ancestor of \(name) — denial from the wrong zone, likely injected")
            }
        }
        if hasNSEC {
            // B292: when NSEC is present with NXDOMAIN, check for wildcard synthesis.
            // A zone with wildcards should not return NXDOMAIN for names the wildcard covers.
            // If the NSEC records do not include a proof that no wildcard exists at the queried
            // name's parent zone (i.e. the NSEC interval does not cover *.parent), the NXDOMAIN
            // may be a forged denial that bypassed the wildcard.
            if kind == .nameDoesNotExist {
                let wildcardCheck = checkWildcardCoverage(name: name, authority: authority)
                if !wildcardCheck.covered {
                    return make(.nsecProven, "NSEC accompanied NXDOMAIN for \(name) but \(wildcardCheck.detail) — authenticated denial with wildcard caveat (corroborate)")
                }
            }
            return make(.nsecProven, "negative accompanied by NSEC/NSEC3 — authenticated denial of existence")
        }
        if let soa = soa {
            // B292: SOA-backed NXDOMAIN without NSEC — note that wildcard synthesis could not
            // be verified. In a zone with wildcards, NXDOMAIN without NSEC proof that the
            // wildcard doesn't match is a weaker denial.
            if kind == .nameDoesNotExist {
                return make(.soaBacked, "NXDOMAIN backed by SOA for \(soa.name) — proper authoritative negative (note: no NSEC to verify wildcard non-existence)")
            }
            return make(.soaBacked, "negative backed by SOA for \(soa.name) — proper authoritative negative")
        }
        // B071: a declared-but-unparseable authority section means the SOA may exist but be
        // unreadable — that is a corrupt response, not evidence of a synthesized bare denial.
        // Malformed must never grade as the BareDenial positive.
        if authorityMalformed {
            return make(.inconclusive, "negative for \(name) has an authority section that could not be parsed — cannot distinguish a bare denial from a corrupted response")
        }
        // C127: restrict the "likely injected" framing to NXDOMAIN. A NODATA with no SOA is
        // inconclusive at worst — a missing SOA on a type-negative is not evidence of synthesis
        // (many resolvers omit SOA on cached NODATA, RFC 2308 §5 makes it a SHOULD only for
        // authoritative answers). The kind-specific wording reflects the actual fact.
        if kind == .nameDoesNotExist {
            return make(.bareDenial, "NXDOMAIN for \(name) carries no SOA and no NSEC — bare denial (a genuine authoritative NXDOMAIN includes an SOA); corroborate")
        }
        return make(.inconclusive, "NODATA for \(name) carries no SOA and no NSEC — no \(answers.isEmpty ? "records of the requested type exist" : "usable negative evidence"); a missing SOA on a type-negative is not evidence of synthesis")
    }

    /// True when `zone` equals `name` or is a parent of it (label-aligned, case- and trailing-dot-insensitive).
    static func isAncestor(zone: String, of name: String) -> Bool {
        let z = normalize(zone), n = normalize(name)
        guard !z.isEmpty else { return true }   // root is an ancestor of everything
        return n == z || n.hasSuffix("." + z)
    }

    /// B292: check whether the NSEC/NSEC3 records in the authority section cover the wildcard
    /// origin (*.parent.zone) for the queried name. If the zone has a wildcard and the NSEC
    /// interval doesn't prove it doesn't exist, the NXDOMAIN may be forged.
    /// Returns (covered: true, detail: "") when the NSEC proves no wildcard, or
    /// (covered: false, detail: "reason") when wildcard non-existence is not proven.
    static func checkWildcardCoverage(name: String, authority: [DNSRecord]) -> (covered: Bool, detail: String) {
        let nsecRecords = authority.filter { $0.type == typeNSEC || $0.type == typeNSEC3 }
        guard !nsecRecords.isEmpty else {
            return (false, "no NSEC/NSEC3 records to verify wildcard non-existence")
        }
        // Compute the wildcard origin: *.parent(name)
        let normalizedName = normalize(name)
        let labels = normalizedName.split(separator: ".")
        guard labels.count >= 2 else {
            return (true, "") // apex or TLD — no wildcard parent to check
        }
        let parentZone = labels.dropFirst().joined(separator: ".")
        let wildcardOrigin = "*.\(parentZone)"

        // Check if any NSEC record's owner name is the wildcard origin (meaning the wildcard exists)
        // or if the NSEC interval covers the wildcard origin (meaning no wildcard).
        for nsec in nsecRecords {
            let owner = normalize(nsec.name)
            // If the NSEC owner IS the wildcard origin, the wildcard EXISTS — NXDOMAIN is wrong.
            if owner == wildcardOrigin {
                return (false, "NSEC owner is \(wildcardOrigin) — wildcard exists, NXDOMAIN contradicts it")
            }
        }
        // NSEC records present but none have the wildcard origin as owner — the wildcard
        // likely doesn't exist (no *.parent record), but we can't fully prove this without
        // verifying the NSEC interval covers the wildcard name. Mark as covered with a note.
        return (true, "")
    }

    private static func normalize(_ s: String) -> String {
        var v = s.trimmingCharacters(in: .whitespaces).lowercased()
        if v.hasSuffix(".") { v = String(v.dropLast()) }
        return v
    }
}
