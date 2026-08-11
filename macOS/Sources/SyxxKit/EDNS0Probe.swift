import Foundation

// ROADMAP Workstream H · datapoint F1 — EDNS0 capability probe.
//
// Sends an EDNS0 query (an OPT pseudo-record advertising a 4096-byte UDP buffer) and reads back
// whether the resolver speaks EDNS0. Critically, it distinguishes the three outcomes a plain
// success/failure boolean hides:
//   • resolver echoes an OPT record            → supported (modern resolver).
//   • resolver answers but returns NO OPT       → notSupported (legacy / pre-2020 flag day) — this is
//                                                 SIGNAL, not an error: it explains "the resolver is
//                                                 old", not "the resolver is blocking me".
//   • EDNS0 query gets no response but a PLAIN   → broken: an on-path middlebox is dropping OPT
//     query succeeds                              queries. This is interference worth flagging.
//   • neither works                             → inconclusive (never rendered as clean).
//
// The second (plain) query only runs when the EDNS0 query failed, so a healthy resolver costs one
// query. Honest by construction: an EDNS negotiation failure is never collapsed into "clean".

public struct EDNS0Capability: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case supported      // resolver echoed an OPT record — EDNS0 works
        case notSupported   // resolver answered but with no OPT — legacy (context signal, not an error)
        case broken         // EDNS0 query dropped on-path while a plain query works — middlebox interference
        case inconclusive   // neither the EDNS0 nor the plain query got a usable response
        case untested
    }

    public let status: Status
    public let udpPayloadSize: UInt16?
    public let version: UInt8?
    public let detail: String

    public init(status: Status, udpPayloadSize: UInt16? = nil, version: UInt8? = nil, detail: String = "") {
        self.status = status
        self.udpPayloadSize = udpPayloadSize
        self.version = version
        self.detail = detail
    }

    public static let untested = EDNS0Capability(status: .untested, detail: "not run")
}

public enum EDNS0Probe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1
    public static let probeUDPSize: UInt16 = 4096

    /// Probe `server` for EDNS0 support. Sends one EDNS0 query; only on its failure sends a plain
    /// fallback query to tell "middlebox dropping OPT" (broken) from "resolver/path down" (inconclusive).
    public static func probe(domain: String = "cloudflare.com",
                             server: String,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> EDNS0Capability {
        let ednsResp = await query(DNSQuery(name: domain, type: typeA, server: server, edns0UDPSize: probeUDPSize))
        // C069: BADVERS (extended RCODE 16) is the EDNS version negotiation answer — the
        // resolver explicitly rejected our EDNS version. This is not middlebox interference
        // and must not be reported as `.broken`. Surface it as a distinct finding.
        if ednsResp.status == .rcodeOther && ednsResp.responseCode == 16 {
            return EDNS0Capability(status: .notSupported,
                detail: "resolver answered BADVERS (extended RCODE 16) — it speaks EDNS but rejected the advertised version; EDNS0 negotiation failed, not dropped on-path")
        }
        if !ednsResp.didNotSucceed {
            if let e = ednsResp.edns {
                return EDNS0Capability(status: .supported, udpPayloadSize: e.udpPayloadSize, version: e.version,
                    detail: "resolver echoed an EDNS0 OPT record (UDP \(e.udpPayloadSize), version \(e.version)) — EDNS0 supported")
            }
            // B073: the additional section declared records but the OPT could not be parsed. That
            // is a malformed response, NOT a legacy resolver — EDNS support is UNKNOWN and must
            // never read as a confident "not supported".
            if ednsResp.ednsMalformed {
                return EDNS0Capability(status: .inconclusive,
                    detail: "resolver answered but its additional/OPT section was malformed — EDNS0 support unknown, not merely absent")
            }
            return EDNS0Capability(status: .notSupported,
                detail: "resolver answered but returned no OPT record — no EDNS0 (legacy / pre-2020 flag-day resolver)")
        }
        // The EDNS0 query got no usable response — does a plain (no-OPT) query work?
        let plainResp = await query(DNSQuery(name: domain, type: typeA, server: server))
        if !plainResp.didNotSucceed {
            return EDNS0Capability(status: .broken,
                detail: "the EDNS0 query got no response but a plain query succeeded — OPT is being dropped on-path (middlebox); EDNS0 is broken, not merely unsupported")
        }
        return EDNS0Capability(status: .inconclusive,
            detail: "neither the EDNS0 nor the plain query got a usable response — resolver/path unreachable")
    }
}
