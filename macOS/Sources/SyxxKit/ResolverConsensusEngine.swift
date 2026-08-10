import Foundation

// Stream A (Warp/Mirror reimagining §3): the SINGLE cross-resolver consensus engine.
//
// Before this type, Prophet's peer-consensus card and Mirror's local-vs-consensus comparison were
// two independent fan-outs over two independently-maintained resolver rosters (the app-layer
// ProphetOracleRoster vs VantageComparer.publicResolvers) with their own tri-state derivations. This
// engine owns the ONE canonical Kit-level roster + the fan-out machinery, and derives BOTH views:
//   (a) SYMMETRIC peer-consensus  — agree / disagree / insufficient / inconclusive across the
//       distinct IPs the reachable resolvers returned (what Prophet renders).
//   (b) ASYMMETRIC local-vs-consensus delta — consistent / redirection / degraded / inconclusive of
//       the LOCAL answer against the public baseline (what Mirror renders).
// Every run records its result + timestamp in the shared ResolverConsensusStore keyed by domain.
//
// Behavioural twin of Windows Syxx.Kit/ResolverConsensusEngine.cs — keep the roster, the fan-out,
// and the classification wiring in lockstep.
//
// SCOPE NOTE (macOS): Prophet's roster + 12-oracle fan-out currently live in the App layer
// (`SyxxApp/AppModel.swift`: ProphetOracleRoster / ProphetResult / runLookup), which this Kit stream
// does not own. The UI stream migrates that path to `runUnified` / this roster later; today the
// engine is the single Kit-level roster consumed by VantageComparer and the unified run/store.
//
// HONESTY: the decision policy itself is NOT re-implemented here. Asymmetric classification reuses
// `VantageComparer.evaluate` (routed through the single `CrossVantageConsensus` policy); symmetric
// classification is the Kit twin of the App's `ProphetView.evaluateConsensus`. The tri-state is
// preserved end to end: an unreachable leg is never a clean IP, and a not-measured / single-vantage
// run is never read back as agreement.
public enum ResolverConsensusEngine {

    /// Symmetric peer-consensus over the distinct returned IPs (what Prophet needs).
    public enum PeerConsensusState: String, Sendable, Equatable, Codable {
        case inconclusive   // 0 reachable resolvers: no data to judge
        case insufficient   // 1 reachable resolver: need >= 2 for consensus
        case agree          // all reachable resolvers returned the same IP
        case disagree       // reachable resolvers returned different IPs
    }

    /// Asymmetric local-vs-consensus delta (what Mirror needs). 1:1 with the Mirror comparer's
    /// (isRedirectionDetected / isInconclusive / isDegraded) tri-state; a distinct engine-level
    /// vocabulary so the shared store does not depend on the comparer's own booleans.
    public enum LocalDeltaState: String, Sendable, Equatable, Codable {
        case consistent
        case redirectionDetected
        case degraded
        case inconclusive
    }

    /// A single resolver's rendered answer (Prophet row shape), Kit-level so the engine does not
    /// depend on the app-layer `ProphetResult`.
    public struct PeerResponse: Sendable, Equatable {
        public let resolver: String
        public let ip: String
        public init(resolver: String, ip: String) {
            self.resolver = resolver
            self.ip = ip
        }
    }

    // MARK: - The ONE canonical roster

    /// The 11 public reference resolvers, plain-named. THE single Kit source of truth for the
    /// resolver list: `VantageComparer.publicResolvers` is a projection of this list — it no longer
    /// hand-maintains IPs.
    public static let publicResolvers: [(name: String, ip: String)] = [
        ("Cloudflare", "1.1.1.1"),
        ("Google", "8.8.8.8"),
        ("Quad9", "9.9.9.9"),
        ("OpenDNS", "208.67.222.222"),
        ("AdGuard", "94.140.14.14"),
        ("NextDNS", "45.90.28.0"),
        ("Control D", "76.76.2.0"),
        ("Comodo", "8.26.56.26"),
        ("Level3", "4.2.2.1"),
        ("Verisign", "64.6.64.6"),
        ("DNS.WATCH", "84.200.69.80")
    ]

    /// DoH endpoints for the resolvers that publish one. Canonical superset shared with Windows;
    /// each façade projects the subset it queries (Mirror uses the primary three below).
    public static let dohEndpoints: [String: String] = [
        "Cloudflare": "https://cloudflare-dns.com/dns-query",
        "Google": "https://dns.google/dns-query",
        "Quad9": "https://dns.quad9.net/dns-query",
        "OpenDNS": "https://doh.opendns.com/dns-query",
        "AdGuard": "https://dns.adguard.com/dns-query",
        "NextDNS": "https://dns.nextdns.io/dns-query",
        "Control D": "https://freedns.controld.com/p0"
    ]

    /// The resolvers Mirror's local-vs-consensus comparison fans out to (UDP + DoH). A NAMED slice of
    /// the canonical roster, not a separate list — the witness wire is pinned to exactly these keys.
    public static let mirrorPrimaryResolvers = ["Cloudflare", "Google", "Quad9"]

    /// The full 12-entry roster for a given local resolver address (Local first), display-named as
    /// "Name (ip)" — the format Prophet's rows use.
    public static func roster(localServer: String) -> [(name: String, ip: String)] {
        [("Local (\(localServer))", localServer)] + publicResolvers.map { ("\($0.name) (\($0.ip))", $0.ip) }
    }

    private static func ip(of name: String) -> String {
        publicResolvers.first(where: { $0.name == name })?.ip ?? ""
    }

    // MARK: - Symmetric classification (agree / disagree / insufficient / inconclusive)

    /// Reachable resolvers are those whose rendered answer is not "[unreachable]". Twin of the C#
    /// `ReachableCount` / `ProphetPresentation.EvaluateConsensus` active-leg filter.
    public static func reachableCount(_ responses: [PeerResponse]) -> Int {
        responses.filter { !$0.ip.contains("[unreachable]") }.count
    }

    /// Symmetric classification from the distinct returned IPs. Kit twin of the App's
    /// `ProphetView.evaluateConsensus`: >= 2 reachable AND all the same IP before agreement; a single
    /// reachable leg is insufficient; none reachable is inconclusive (never silently promoted).
    public static func classifyPeer(_ responses: [PeerResponse]) -> PeerConsensusState {
        let active = responses.filter { !$0.ip.contains("[unreachable]") }
        if active.isEmpty { return .inconclusive }
        if active.count < 2 { return .insufficient }
        let uniqueIPs = Set(active.map(\.ip))
        return uniqueIPs.count == 1 ? .agree : .disagree
    }

    // MARK: - Prophet fan-out (symmetric)

    /// Query the full 12-oracle roster in parallel (UDP) and return the display-named responses,
    /// Local first. The Kit-level twin of the app's `runLookup` fan-out; the app path migrates here.
    public static func runPeerFanOut(domain: String, qtype: UInt16, localServer: String) async -> [PeerResponse] {
        let oracles = roster(localServer: localServer)
        var responses = await withTaskGroup(of: PeerResponse.self) { group -> [PeerResponse] in
            for oracle in oracles {
                group.addTask {
                    let res = await DNSResolver.queryUDPDetailed(DNSQuery(name: domain, type: qtype, server: oracle.ip))
                    return PeerResponse(resolver: oracle.name, ip: rendered(res))
                }
            }
            var out: [PeerResponse] = []
            for await r in group { out.append(r) }
            return out
        }
        sortLocalFirst(&responses)
        return responses
    }

    private static func sortLocalFirst(_ responses: inout [PeerResponse]) {
        responses.sort { a, b in
            if a.resolver.hasPrefix("Local") { return true }
            if b.resolver.hasPrefix("Local") { return false }
            return a.resolver < b.resolver
        }
    }

    /// Reduce a resolver response to the string Prophet renders: the first record's rdata, else "no
    /// records" (successful but empty) or "[unreachable]" (transport failure). Never a clean IP for a
    /// leg that did not answer.
    private static func rendered(_ res: DNSResponse) -> String {
        if let first = res.records.first { return first.rdata }
        return res.status == .success ? "no records" : "[unreachable]"
    }

    private static func rendered(_ outcome: ResolverQueryOutcome) -> String {
        if let first = outcome.records.first { return first.rdata }
        return outcome.status == .success ? "no records" : "[unreachable]"
    }

    // MARK: - Mirror fan-out (asymmetric)

    /// Query the local resolver plus the Mirror primary slice over BOTH UDP (Do53) and DoH, in
    /// parallel. Reproduces the fan-out `VantageComparer.compareVantagePoints` used to own inline; the
    /// comparer now delegates here and keeps its own `evaluate` + `ComparisonResult` assembly.
    public static func runLocalDeltaFanOut(domain: String, type: UInt16, localServer: String) async
        -> (local: ResolverQueryOutcome,
            publicOutcomes: [String: ResolverQueryOutcome],
            dohOutcomes: [String: ResolverQueryOutcome]) {

        async let local = queryOutcome(DNSQuery(name: domain, type: type, server: localServer))
        async let cloudflare = queryOutcome(DNSQuery(name: domain, type: type, server: ip(of: "Cloudflare")))
        async let google = queryOutcome(DNSQuery(name: domain, type: type, server: ip(of: "Google")))
        async let quad9 = queryOutcome(DNSQuery(name: domain, type: type, server: ip(of: "Quad9")))
        async let cloudflareDoH = dohOutcome(domain: domain, type: type, endpoint: dohEndpoints["Cloudflare"]!)
        async let googleDoH = dohOutcome(domain: domain, type: type, endpoint: dohEndpoints["Google"]!)
        async let quad9DoH = dohOutcome(domain: domain, type: type, endpoint: dohEndpoints["Quad9"]!)

        var publicOutcomes: [String: ResolverQueryOutcome] = [:]
        publicOutcomes["Cloudflare"] = await cloudflare
        publicOutcomes["Google"] = await google
        publicOutcomes["Quad9"] = await quad9

        var dohOutcomes: [String: ResolverQueryOutcome] = [:]
        dohOutcomes["Cloudflare"] = await cloudflareDoH
        dohOutcomes["Google"] = await googleDoH
        dohOutcomes["Quad9"] = await quad9DoH

        return (await local, publicOutcomes, dohOutcomes)
    }

    /// Asymmetric classification, mapped to the engine vocabulary. Reuses `VantageComparer.evaluate`
    /// (the single asymmetric-logic owner, routed through `CrossVantageConsensus`).
    public static func classifyLocalDelta(localOutcome: ResolverQueryOutcome,
                                          publicOutcomes: [String: ResolverQueryOutcome],
                                          dohOutcomes: [String: ResolverQueryOutcome]? = nil) -> LocalDeltaState {
        let r = VantageComparer.evaluate(localOutcome: localOutcome, publicOutcomes: publicOutcomes, dohOutcomes: dohOutcomes)
        if r.redirection { return .redirectionDetected }
        if r.inconclusive { return .inconclusive }
        if r.degraded { return .degraded }
        return .consistent
    }

    private static func queryOutcome(_ query: DNSQuery) async -> ResolverQueryOutcome {
        let response = await DNSResolver.queryUDPDetailed(query)
        return ResolverQueryOutcome(response: response)
    }

    private static func dohOutcome(domain: String, type: UInt16, endpoint: String) async -> ResolverQueryOutcome {
        // A malformed endpoint is a configuration failure, not an authoritative empty answer — encode
        // it as a transport error so it can never be mistaken for a clean DoH baseline.
        guard let url = URL(string: endpoint) else {
            return ResolverQueryOutcome(records: [], status: .transportError, errorDescription: "Invalid DoH endpoint: \(endpoint)")
        }
        let response = await DNSResolver.queryDoHDetailed(name: domain, type: type, url: url)
        return ResolverQueryOutcome(response: response)
    }

    // MARK: - Unified single run (exposes BOTH, persists to the shared store)

    /// The result of a single unified consensus run: the symmetric peer view AND the asymmetric
    /// local-vs-consensus view, plus the responses both were derived from.
    public struct ResolverConsensusRun: Sendable, Equatable {
        public let domain: String
        public let timestamp: Date
        public let responses: [PeerResponse]
        public let peerConsensus: PeerConsensusState
        public let reachableCount: Int
        public let localDelta: LocalDeltaState
    }

    /// ONE fan-out (full 12 roster over UDP + the primary DoH legs) that derives BOTH the symmetric
    /// peer-consensus result and the asymmetric local-vs-consensus delta, records the snapshot in the
    /// shared store, and returns both. This is the entry point the Mirror/Prophet UI will migrate to
    /// so a single scan feeds both cards; the existing per-façade paths remain wire-identical.
    @discardableResult
    public static func runUnified(domain: String, qtype: UInt16, localServer: String) async -> ResolverConsensusRun {
        // Full-roster UDP fan-out keyed by plain name, plus the local leg and the primary DoH legs.
        async let localTask = queryOutcome(DNSQuery(name: domain, type: qtype, server: localServer))

        let udp = await withTaskGroup(of: (String, ResolverQueryOutcome).self) { group -> [String: ResolverQueryOutcome] in
            for r in publicResolvers {
                group.addTask { (r.name, await queryOutcome(DNSQuery(name: domain, type: qtype, server: r.ip))) }
            }
            var results: [(String, ResolverQueryOutcome)] = []
            for await (name, outcome) in group { results.append((name, outcome)) }
            results.sort { $0.0 < $1.0 }
            var out: [String: ResolverQueryOutcome] = [:]
            for (name, outcome) in results { out[name] = outcome }
            return out
        }
        let doh = await withTaskGroup(of: (String, ResolverQueryOutcome).self) { group -> [String: ResolverQueryOutcome] in
            for name in mirrorPrimaryResolvers {
                group.addTask { (name, await dohOutcome(domain: domain, type: qtype, endpoint: dohEndpoints[name]!)) }
            }
            var results: [(String, ResolverQueryOutcome)] = []
            for await (name, outcome) in group { results.append((name, outcome)) }
            results.sort { $0.0 < $1.0 }
            var out: [String: ResolverQueryOutcome] = [:]
            for (name, outcome) in results { out[name] = outcome }
            return out
        }
        let localOutcome = await localTask

        // Symmetric view: display-named responses (Local first) over every UDP leg.
        var responses: [PeerResponse] = [PeerResponse(resolver: "Local (\(localServer))", ip: rendered(localOutcome))]
        responses.append(contentsOf: publicResolvers.map {
            PeerResponse(resolver: "\($0.name) (\($0.ip))", ip: rendered(udp[$0.name] ?? ResolverQueryOutcome(records: [], status: .transportError)))
        })
        sortLocalFirst(&responses)

        let peer = classifyPeer(responses)
        let reachable = reachableCount(responses)

        // Asymmetric view: local vs the primary UDP slice, cross-checked against the DoH legs.
        var primaryUdp: [String: ResolverQueryOutcome] = [:]
        for name in mirrorPrimaryResolvers { primaryUdp[name] = udp[name] }
        let delta = classifyLocalDelta(localOutcome: localOutcome, publicOutcomes: primaryUdp, dohOutcomes: doh)

        let run = ResolverConsensusRun(
            domain: domain, timestamp: Date(), responses: responses,
            peerConsensus: peer, reachableCount: reachable, localDelta: delta)
        ResolverConsensusStore.record(ResolverConsensusSnapshot(
            domain: domain, timestamp: run.timestamp,
            peerConsensus: peer, reachableCount: reachable, localDelta: delta))
        return run
    }
}
