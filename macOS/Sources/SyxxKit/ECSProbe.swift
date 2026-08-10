import Foundation

// ROADMAP Workstream H · datapoint F10 — EDNS Client Subnet (ECS) leak probe.
//
// ECS lets a resolver attach the client's subnet to upstream queries so authoritative/CDN servers
// can geo-route. The privacy cost: a resolver that forwards ECS exposes (a /24 of) your address to
// every authoritative you look up. This sends a query carrying an ECS option for a specific test
// subnet and reads back whether the resolver ECHOES a non-zero ECS scope — the definitive proof it
// applied/forwarded the subnet (reliable, unlike comparing answers, which CDN load-balancing can
// perturb). Reuses the F1 EDNS0/OPT contract on both encode and decode.
//
// Discipline: GLUTTON (privacy). `forwarded` is a weak privacyLeak signal — a resolver forwarding
// ECS is a privacy exposure, not tampering; a resolver that strips it is privacy-preserving.

public struct ECSLeak: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case forwarded      // resolver echoed a non-zero ECS scope — it forwards the client subnet upstream (privacy exposure)
        case notForwarded   // resolver echoed EDNS but applied no ECS scope — it strips/ignores ECS (privacy-preserving)
        case inconclusive   // no EDNS/OPT in the response, or the query failed — cannot assess
        case untested
    }

    public let status: Status
    public let scopePrefixLength: UInt8?
    public let detail: String

    public init(status: Status, scopePrefixLength: UInt8? = nil, detail: String = "") {
        self.status = status
        self.scopePrefixLength = scopePrefixLength
        self.detail = detail
    }

    public static let untested = ECSLeak(status: .untested, detail: "not run")
}

public enum ECSProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1
    // A documentation-range (RFC 5737) test subnet — not the user's real address; we only need to
    // learn whether the resolver honours an ECS option, not to reveal anything real.
    public static let testSubnetV4 = "203.0.113.0"

    /// Send a query carrying an ECS option for `testSubnet` and report whether the resolver forwarded it.
    public static func probe(domain: String = "google.com",
                             server: String,
                             testSubnet: String = testSubnetV4,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> ECSLeak {
        let resp = await query(DNSQuery(name: domain, type: typeA, server: server,
                                        edns0UDPSize: 4096, edns0ClientSubnetV4: testSubnet))
        guard !resp.didFail, let edns = resp.edns else {
            return ECSLeak(status: .inconclusive,
                detail: "no EDNS/OPT echoed for the ECS query (\(resp.status.rawValue)) — cannot assess ECS forwarding")
        }
        // B138: the probe sends a SYNTHETIC test subnet (203.0.113.0, RFC 5737 documentation
        // range), so a non-zero echoed scope proves only that the resolver honoured the synthetic
        // ECS option — it is a capability observation, not proof the user's real subnet was
        // forwarded. Privacy-leak evidence requires collector-grounded or paired plain/ECS
        // comparison (PROPOSALS.md:258-264); this probe alone cannot establish that.
        if let scope = edns.ecsScopePrefixLength, scope > 0 {
            return ECSLeak(status: .forwarded, scopePrefixLength: scope,
                detail: "resolver echoed an ECS scope of /\(scope) for the synthetic test subnet — ECS capability confirmed; whether your real subnet is forwarded requires a paired plain/ECS or collector observation")
        }
        // C067: distinguish "resolver echoed an ECS option with scope 0" (evidence the resolver
        // considered and declined ECS) from "resolver echoed no ECS option at all" (silence —
        // a forwarder can add ECS on its upstream query and never echo it back to the stub).
        if let scope = edns.ecsScopePrefixLength, scope == 0 {
            return ECSLeak(status: .notForwarded, scopePrefixLength: 0,
                detail: "resolver echoed an ECS option with scope /0 — it considered and declined Client Subnet (privacy-preserving)")
        }
        // No ECS option in the echoed OPT — the resolver is not forwarding ECS.
        return ECSLeak(status: .notForwarded,
            detail: "resolver echoed EDNS but no ECS option — subnet is not being forwarded")
    }
}
