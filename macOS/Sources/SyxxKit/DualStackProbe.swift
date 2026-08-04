import Foundation

// ROADMAP Workstream H · datapoint F8 — IPv4/IPv6 dual-stack resolution audit + DNS64 detection.
//
// The engine reasons only about A records; AAAA and dual-stack behaviour are invisible. This
// resolves A and AAAA for the same name at the same resolver and characterises the pairing:
//   • both resolve to real addresses            → dualStack (normal).
//   • A resolves, AAAA is a genuine negative     → ipv4Only (benign: no IPv6 for this name).
//   • every AAAA is in 64:ff9b::/96              → dns64Synthesis (a NAT64/DNS64 network is
//        synthesising AAAA from A — BENIGN, and flagged explicitly so a future AAAA-divergence
//        check never mistakes NAT64 addresses for a rewrite).
//   • AAAA resolves but A does not               → ipv4Suppressed (unusual: the A record is missing
//        while IPv6 works — a weak selective-IPv4-blocking signal, corroborate).
//
// Discipline: PROPHET. This audits ONE resolver's own dual-stack behaviour (not a cross-vantage
// comparison), so it lives with the resolver-integrity checks, alongside latency/stability/denial.
//
// Honest v1 limits: DNS64 detection is exact (the well-known 64:ff9b::/96 prefix, parsed via the OS
// resolver so `::`-compression and embedded-IPv4 forms all match). `ipv4Suppressed` is weak — a
// domain legitimately may publish AAAA but no A — so it never decides alone.

public struct DualStackAudit: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case dualStack        // A and AAAA both resolve to real addresses
        case ipv4Only         // A resolves; AAAA is a genuine negative (no IPv6 for the name)
        case dns64Synthesis   // AAAA answers are all NAT64/DNS64 (64:ff9b::/96) — benign
        case ipv4Suppressed   // AAAA resolves but A does not — weak selective-IPv4-blocking signal
        case inconclusive     // neither family produced a usable existence signal
        case untested
    }

    public let status: Status
    public let aAddresses: [String]
    public let aaaaAddresses: [String]
    public let dns64Count: Int
    public let detail: String

    public init(status: Status, aAddresses: [String] = [], aaaaAddresses: [String] = [],
                dns64Count: Int = 0, detail: String = "") {
        self.status = status
        self.aAddresses = aAddresses
        self.aaaaAddresses = aaaaAddresses
        self.dns64Count = dns64Count
        self.detail = detail
    }

    public static let untested = DualStackAudit(status: .untested, detail: "not run")
}

public enum DualStackProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1
    public static let typeAAAA: UInt16 = 28

    /// Resolve A and AAAA for `domain` at `server` and classify the dual-stack pairing.
    public static func probe(domain: String,
                             server: String = "1.1.1.1",
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> DualStackAudit {
        let a = await query(DNSQuery(name: domain, type: typeA, server: server, port: 53))
        let aaaa = await query(DNSQuery(name: domain, type: typeAAAA, server: server, port: 53))
        return classify(aStatus: a.status, aAddrs: a.records.filter { $0.type == typeA }.map { $0.rdata },
                        aaaaStatus: aaaa.status, aaaaAddrs: aaaa.records.filter { $0.type == typeAAAA }.map { $0.rdata })
    }

    static func classify(aStatus: DNSResponseStatus, aAddrs: [String],
                         aaaaStatus: DNSResponseStatus, aaaaAddrs: [String]) -> DualStackAudit {
        let aOk = aStatus == .success && !aAddrs.isEmpty
        let aaaaOk = aaaaStatus == .success && !aaaaAddrs.isEmpty
        let aDenied = aStatus == .nxDomain || aStatus == .noData
        let aaaaDenied = aaaaStatus == .nxDomain || aaaaStatus == .noData
        let dns64 = aaaaAddrs.filter { isDNS64($0) }
        func make(_ s: DualStackAudit.Status, _ d: String) -> DualStackAudit {
            DualStackAudit(status: s, aAddresses: aAddrs, aaaaAddresses: aaaaAddrs, dns64Count: dns64.count, detail: d)
        }

        // Need a definitive existence signal on at least one family, else we can't say anything.
        guard aOk || aaaaOk || aDenied || aaaaDenied else {
            return make(.inconclusive, "neither A nor AAAA produced a usable answer — cannot audit dual-stack")
        }
        if aaaaOk && dns64.count == aaaaAddrs.count {
            return make(.dns64Synthesis, "all \(aaaaAddrs.count) AAAA answer(s) are in 64:ff9b::/96 — NAT64/DNS64 synthesis (benign)")
        }
        if aOk && aaaaOk {
            return make(.dualStack, "A and AAAA both resolve — normal dual-stack")
        }
        if aOk && aaaaDenied {
            return make(.ipv4Only, "A resolves; AAAA is a genuine negative — IPv4-only name (benign)")
        }
        if aaaaOk && aDenied {
            // Only a GENUINE A negative (nxDomain/noData) alongside a working AAAA is a
            // selective-IPv4-blocking signal. An A *failure* (timeout/transport/servfail) is a
            // missing datum, not a result — it must fall through to inconclusive (§1). [B038]
            return make(.ipv4Suppressed, "AAAA resolves but A does not (\(aStatus.rawValue)) — the IPv4 record is missing while IPv6 works; corroborate for selective blocking")
        }
        return make(.inconclusive, "A (\(aStatus.rawValue)) / AAAA (\(aaaaStatus.rawValue)) pairing is ambiguous")
    }

    /// True when `addr` is a NAT64/DNS64 address (well-known prefix 64:ff9b::/96). Parsed via the OS
    /// resolver so `::`-compressed and embedded-IPv4 renderings all match on the first 96 bits.
    static func isDNS64(_ addr: String) -> Bool {
        var a = in6_addr()
        guard inet_pton(AF_INET6, addr, &a) == 1 else { return false }
        let bytes = withUnsafeBytes(of: &a) { Array($0) }
        guard bytes.count == 16 else { return false }
        // 64:ff9b::/96 → the first 12 bytes are fixed.
        let prefix: [UInt8] = [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0]
        return Array(bytes.prefix(12)) == prefix
    }
}
