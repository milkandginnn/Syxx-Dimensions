import Foundation

// ROADMAP Workstream H · datapoint A3 — SVCB/HTTPS (type 65) presence across resolvers.
//
// Modern connectivity increasingly rides HTTPS/SVCB records (ECH keys, HTTP/3 alt-svc, port/ALPN
// hints). A stripped or filtered HTTPS RR silently downgrades ECH / transport, a growing failure
// class the resolver-only A/AAAA checks are blind to. This queries type 65 across several resolvers
// and flags DIVERGENCE — one resolver serves the HTTPS record while another returns none.
//
// Honest v1 caveat: an absent record can mean the domain genuinely has no HTTPS RR, or that a
// resolver doesn't forward type 65. Point this at KNOWN-modern resolvers (1.1.1.1 / 8.8.8.8 /
// 9.9.9.9, which all support HTTPS records) so divergence is more likely stripping than a
// capability gap — and treat `divergent` as a signal to corroborate, not proof.
//
// C018: presence alone is not integrity. A vantage can keep the type-65 RR while stripping
// ECH/ALPN/port parameters — compare canonical record SETS across resolvers too.

public struct SVCBFinding: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case present        // every responding resolver returned an HTTPS record — consistent
        case absent         // every responding resolver returned none (domain likely has no HTTPS RR)
        case divergent      // some resolvers serve it, some don't — possible stripping (corroborate)
        case inconclusive   // no resolver produced a usable answer
        case untested
    }

    public let status: Status
    public let presentAt: [String]   // resolvers that returned an HTTPS record
    public let absentAt: [String]    // resolvers that answered but returned no HTTPS record
    public let failedAt: [String]    // resolvers that did not answer
    public let detail: String

    public init(status: Status, presentAt: [String] = [], absentAt: [String] = [],
                failedAt: [String] = [], detail: String = "") {
        self.status = status
        self.presentAt = presentAt
        self.absentAt = absentAt
        self.failedAt = failedAt
        self.detail = detail
    }

    public static let untested = SVCBFinding(status: .untested, detail: "not run")
}

public enum SVCBProbe {
    public static let typeHTTPS: UInt16 = 65
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse

    public static func probe(domain: String,
                             resolvers: [String] = ["1.1.1.1", "8.8.8.8", "9.9.9.9"],
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> SVCBFinding {
        var present: [String] = [], absent: [String] = [], failed: [String] = []
        // C018: per-resolver canonical descriptors for parameter-set comparison.
        var recordSets: [String: [String]] = [:]
        for server in resolvers {
            let resp = await query(DNSQuery(name: domain, type: typeHTTPS, server: server, port: 53))
            switch resp.status {
            case .success where resp.records.contains(where: { $0.type == typeHTTPS }):
                present.append(server)
                // C018: canonical per-record descriptors so parameter stripping is comparable.
                recordSets[server] = resp.records
                    .filter { $0.type == typeHTTPS }
                    .compactMap { canonicalizeSvcbRdata($0.rdata) }
                    .sorted()
            case .success, .noData, .nxDomain:
                absent.append(server)          // resolver answered, no HTTPS record present
            default:
                failed.append(server)          // timeout / servfail / transport — no usable answer
            }
        }
        return classify(present: present, absent: absent, failed: failed, recordSetsByServer: recordSets)
    }

    static func classify(present: [String], absent: [String], failed: [String],
                         recordSetsByServer: [String: [String]]? = nil) -> SVCBFinding {
        func make(_ s: SVCBFinding.Status, _ d: String) -> SVCBFinding {
            SVCBFinding(status: s, presentAt: present.sorted(), absentAt: absent.sorted(),
                        failedAt: failed.sorted(), detail: d)
        }
        // B074: require at least two independent usable vantages. A single responder (or a
        // spoofer answering while dropping the rest) is thin evidence, not a clean signal.
        guard present.count + absent.count >= 2 else {
            return make(.inconclusive, "only \(present.count + absent.count) resolver(s) returned a usable HTTPS-record answer — cannot corroborate")
        }
        // B074: degrade when any configured reference failed — a present/absent result with
        // failed vantages is not "all responding resolvers", it is a subset.
        if !failed.isEmpty {
            return make(.inconclusive, "\(present.count + absent.count) responder(s) usable but \(failed.count) failed — insufficient corroboration for a clean present/absent result")
        }
        if !present.isEmpty && !absent.isEmpty {
            return make(.divergent, "\(present.count) resolver(s) serve the HTTPS record, \(absent.count) return none — possible SVCB/HTTPS stripping (corroborate)")
        }
        if !present.isEmpty {
            // C018: presence alone is not integrity. A vantage can keep the type-65 RR while
            // stripping ECH/ALPN/port parameters — compare the canonical record SETS too.
            if let recordSetsByServer, present.count > 1 {
                let canonicalSets = present
                    .compactMap { recordSetsByServer[$0] }
                    .map { $0.joined(separator: "\n") }
                let distinct = Set(canonicalSets)
                if distinct.count > 1 {
                    return make(.divergent,
                        "resolvers all serve an HTTPS record but with DIFFERENT priority/target/parameter sets — possible SVCB parameter stripping (e.g. ECH/ALPN removed); corroborate")
                }
            }
            return make(.present, "all \(present.count) responding resolvers serve the HTTPS record — consistent")
        }
        return make(.absent, "all \(absent.count) responding resolvers report no HTTPS record — domain likely has none")
    }

    // MARK: - C018: SVCB RDATA canonicalization

    /// C018: canonicalize an HTTPS/SVCB record's RDATA hex into a comparable descriptor:
    /// `prio=<n>;target=<name>;<key>=<value-hex>;...` with parameter keys in ascending order
    /// (RFC 9460 wire order is already ascending; sorting makes the comparison robust anyway).
    /// The target name inside SVCB RDATA is REQUIRED to be uncompressed (RFC 9460 §2.2), so the
    /// record-local hex is parseable standalone; a compression pointer or truncated field is
    /// malformed and returns nil (caller surfaces it as unparsed, never silently equal).
    static func canonicalizeSvcbRdata(_ rdataHex: String) -> String? {
        guard !rdataHex.isEmpty, rdataHex.count % 2 == 0 else { return nil }
        let bytes: [UInt8]
        do {
            bytes = try Array(hex: rdataHex)
        } catch {
            return nil
        }
        guard bytes.count >= 3 else { return nil }

        let priority = Int(bytes[0]) << 8 | Int(bytes[1])
        var off = 2

        // Uncompressed wire-format target name (RFC 9460 §2.2).
        var labels: [String] = []
        while true {
            guard off < bytes.count else { return nil }
            let len = Int(bytes[off])
            if len == 0 { off += 1; break }
            guard (len & 0xC0) == 0 else { return nil }  // compression pointer forbidden in SVCB RDATA
            guard off + 1 + len <= bytes.count else { return nil }
            labels.append(String(bytes: bytes[off+1..<off+1+len], encoding: .ascii)?.lowercased() ?? "")
            off += 1 + len
        }
        let target = labels.isEmpty ? "." : labels.joined(separator: ".")

        // SvcParams: key(2) length(2) value(length), keys strictly ascending per RFC 9460.
        var svcParams: [(key: Int, value: String)] = []
        while off < bytes.count {
            guard off + 4 <= bytes.count else { return nil }
            let key = Int(bytes[off]) << 8 | Int(bytes[off+1])
            let len = Int(bytes[off+2]) << 8 | Int(bytes[off+3])
            off += 4
            guard off + len <= bytes.count else { return nil }
            let valueHex = bytes[off..<off+len].map { String(format: "%02x", $0) }.joined()
            svcParams.append((key: key, value: valueHex))
            off += len
        }

        var parts = ["prio=\(priority)", "target=\(target)"]
        for param in svcParams {
            parts.append("\(param.key)=\(param.value)")
        }
        return parts.joined(separator: ";")
    }
}

private extension Array where Element == UInt8 {
    /// Initialize from a hex string.
    init(hex: String) throws {
        guard hex.count % 2 == 0 else { throw NSError(domain: "hex", code: 1) }
        self = []
        var bytes: [UInt8] = []
        bytes.reserveCapacity(hex.count / 2)
        var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2)
            guard let byte = UInt8(hex[index..<next], radix: 16) else {
                throw NSError(domain: "hex", code: 2)
            }
            bytes.append(byte)
            index = next
        }
        self = bytes
    }
}
