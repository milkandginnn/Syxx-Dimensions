import Foundation

// ROADMAP Workstream H · datapoint A8 (v1) — encrypted-transport health.
//
// NetworkKeyState infers "DNS is encrypted" from a loopback-resolver heuristic (KDNS-23 flagged it
// as unreliable — it never proves the encrypted path actually works). This actively completes a
// DNS-over-HTTPS query end to end and reports whether the encrypted path resolves. A network that
// silently blocks/filters DoH (captive portal, enterprise middlebox) shows up as `blocked` here —
// the case where users believe they're encrypted but DNS is quietly falling open to plaintext.
//
// Additive: wraps the committed `DNSResolver.queryDoHDetailed`; no edits to it. v1 verifies
// reachability + successful resolution; TLS certificate introspection is a later refinement.

public struct EncryptedTransport: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case working        // the DoH endpoint completed a resolution over TLS — encrypted path works
        case blocked        // the DoH endpoint could not be reached — encrypted path filtered/blocked
        case inconclusive   // reached but returned an unusable/odd answer
        case untested
    }

    public let status: Status
    public let endpoint: String
    public let detail: String

    public init(status: Status, endpoint: String = "", detail: String = "") {
        self.status = status
        self.endpoint = endpoint
        self.detail = detail
    }

    public static let untested = EncryptedTransport(status: .untested, detail: "not run")
}

public enum EncryptedTransportProbe {
    public typealias DoHQueryFn = @Sendable (String, UInt16, URL) async -> DNSResponse
    /// Cleartext Do53 control query (name, type) → response. Proves the network path itself is up so a
    /// failed DoH path can be honestly attributed to encrypted-DNS filtering rather than an offline host.
    public typealias ControlQueryFn = @Sendable (String, UInt16) async -> DNSResponse

    /// Default DoH endpoint (Cloudflare). A constant literal, so the force-unwrap cannot fail.
    public static let defaultEndpoint = URL(string: "https://cloudflare-dns.com/dns-query")!

    /// Resolve a stable control name over DoH and report whether the encrypted path completed. When the
    /// DoH path does NOT clearly succeed, a cleartext Do53 control decides between `blocked` (network up,
    /// encrypted path filtered) and `inconclusive` (network/provider down — no honest result). The
    /// control runs only when needed, so a healthy encrypted path costs no extra query.
    public static func probe(testName: String = "cloudflare.com",
                             qtype: UInt16 = 1,
                             endpoint: URL = defaultEndpoint,
                             query: DoHQueryFn = { await DNSResolver.queryDoHDetailed(name: $0, type: $1, url: $2) },
                             control: ControlQueryFn = { await DNSResolver.queryUDPDetailed(DNSQuery(name: $0, type: $1)) }) async -> EncryptedTransport {
        let resp = await query(testName, qtype, endpoint)
        var controlSucceeded = false
        if !(resp.status == .success && resp.records.count > 0) {
            let c = await control(testName, qtype)
            controlSucceeded = (c.status == .success && c.records.count > 0)
        }
        return classify(status: resp.status, recordCount: resp.records.count,
                        controlSucceeded: controlSucceeded,
                        endpoint: endpoint.host ?? endpoint.absoluteString)
    }

    static func classify(status: DNSResponseStatus, recordCount: Int,
                         controlSucceeded: Bool, endpoint: String) -> EncryptedTransport {
        func make(_ s: EncryptedTransport.Status, _ d: String) -> EncryptedTransport {
            EncryptedTransport(status: s, endpoint: endpoint, detail: d)
        }
        switch status {
        case .success where recordCount > 0:
            return make(.working, "DoH resolution completed over TLS via \(endpoint) — encrypted path works")
        case .timeout, .transportError:
            // B051: a failed DoH path is `blocked` ONLY when a cleartext Do53 control succeeds, proving
            // the network is up so the encrypted path is being filtered. Without that proof (the control
            // also failed → offline host or provider down) the failure is unattributable, so it stays
            // inconclusive — a missing/timed-out path must never manufacture a "blocked" finding.
            return controlSucceeded
                ? make(.blocked, "DoH endpoint \(endpoint) unreachable — encrypted DNS is blocked/filtered on this network (DNS may fall open to plaintext)")
                : make(.inconclusive, "DoH endpoint \(endpoint) did not complete and the cleartext control also failed, so blocking cannot be distinguished from an offline network")
        default:
            return make(.inconclusive, "DoH endpoint \(endpoint) reached but returned \(status.rawValue) — encrypted path status unclear")
        }
    }
}
