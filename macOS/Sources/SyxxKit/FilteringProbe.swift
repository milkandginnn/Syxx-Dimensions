import Foundation

/// C2 — Filtering verification: "is my Pi-hole / NextDNS / AdGuard actually working?"
///
/// Three questions, each answered honestly and independently:
///  1. ENFORCEMENT — do well-known blocklisted ad/tracker domains actually come back sinkholed
///     from the system resolver? (NXDOMAIN, 0.0.0.0/::, loopback, or a private redirect all count
///     as sinkholed; a real public answer means the domain got through.)
///  2. DIRECT-DNS BYPASS — can an app with a hardcoded resolver (8.8.8.8) still resolve a blocked
///     domain over plain UDP:53, sidestepping the filter?
///  3. DoH BYPASS — is a public DoH endpoint reachable, letting a browser sidestep the filter?
///
/// Every probe failure is `.unknown`/`.inconclusive`, never silently "filtering works" — a filter
/// this code cannot measure is a filter it must not vouch for.
public enum FilteringProbe {

    /// Domains present on the standard default blocklists (StevenBlack / AdGuard base). Benign to
    /// query; when unfiltered they resolve to real public addresses, so a public answer is
    /// decisive evidence the filter did not act.
    public static let probeDomains = ["doubleclick.net", "googlesyndication.com", "googleadservices.com"]

    public enum DomainOutcome: String, Codable, Sendable {
        case sinkholed   // the filter acted: NXDOMAIN or a null/loopback/private answer
        case resolved    // a real public answer got through
        case unknown     // timeout / transport error — no reading, not a clean one
    }

    public enum Enforcement: String, Codable, Sendable {
        case active        // every readable probe came back sinkholed
        case inactive      // every readable probe resolved publicly (no filtering observed)
        case partial       // a mix — some sinkholed, some got through (leaky blocklist)
        case inconclusive  // nothing readable
    }

    public struct Result: Codable, Sendable {
        public let enforcement: Enforcement
        public let perDomain: [String: DomainOutcome]
        /// Can a hardcoded-DNS app bypass the filter over plain UDP:53? yes = a blocked domain
        /// resolved publicly via a direct external resolver; no = direct egress is blocked or
        /// also filtered; unknown = the check could not run.
        public let directBypassOpen: ConditionState
        public let detail: String
    }

    /// Pure classifier for one answer — the mutation-tested core.
    public static func classify(_ response: DNSResponse) -> DomainOutcome {
        switch response.status {
        case .nxDomain:
            return .sinkholed
        case .success:
            let addrs = response.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)
            guard !addrs.isEmpty else { return .sinkholed }   // empty answer for an ad domain = filtered (NODATA style)
            // A public address means the query escaped the filter. Anything else — 0.0.0.0, ::,
            // loopback, private redirect — is a sinkhole answer.
            let anyPublic = addrs.contains { IPClassifier.classify($0) == .publicRoutable }
            return anyPublic ? .resolved : .sinkholed
        case .noData:
            return .sinkholed
        default:
            return .unknown
        }
    }

    /// Pure aggregation — the mutation-tested core.
    public static func aggregate(_ outcomes: [DomainOutcome]) -> Enforcement {
        let readable = outcomes.filter { $0 != .unknown }
        guard !readable.isEmpty else { return .inconclusive }
        let sinkholed = readable.filter { $0 == .sinkholed }.count
        if sinkholed == readable.count { return .active }
        if sinkholed == 0 { return .inactive }
        return .partial
    }

    /// Live probe through the system resolver, plus the direct-bypass canary via `bypassResolver`.
    /// C073: removed the unused `timeout` parameter — it was declared but never referenced,
    /// so callers reading the signature would size their own budget wrongly.
    /// B139: `controlResolver` is used to establish expected resolvability before classifying
    /// NXDOMAIN/NODATA as filtering. Without an independent control, any expired or globally
    /// nonexistent probe domain becomes evidence that a filter is working. The control defaults
    /// to a well-known public resolver (Cloudflare); if it returns NXDOMAIN/NODATA, the domain
    /// is globally unresolvable and the sinkholed classification is demoted to unknown.
    public static func probe(resolver: String,
                             bypassResolver: String = "8.8.8.8",
                             controlResolver: String = "1.1.1.1") async -> Result {
        var perDomain: [String: DomainOutcome] = [:]
        for domain in probeDomains {
            let resp = await DNSResolver.queryUDPDetailed(
                DNSQuery(name: domain, type: 1, server: resolver, port: 53))
            let initial = classify(resp)
            // B139: when the system resolver returns a block-shaped answer (NXDOMAIN/NODATA),
            // confirm through an independent control that the domain SHOULD resolve. A globally
            // nonexistent domain is not evidence of filtering — it is ordinary DNS.
            if initial == .sinkholed {
                let controlResp = await DNSResolver.queryUDPDetailed(
                    DNSQuery(name: domain, type: 1, server: controlResolver, port: 53))
                if controlResp.status == .nxDomain || controlResp.status == .noData {
                    perDomain[domain] = .unknown
                    continue
                }
            }
            perDomain[domain] = initial
        }
        let enforcement = aggregate(probeDomains.compactMap { perDomain[$0] })

        // Direct-bypass canary: only meaningful relative to a filter. When no filtering was
        // observed there is nothing to bypass; still measured, still reported.
        let canary = await DNSResolver.queryUDPDetailed(
            DNSQuery(name: probeDomains[0], type: 1, server: bypassResolver, port: 53))
        let directBypassOpen: ConditionState
        switch classify(canary) {
        case .resolved:  directBypassOpen = .yes
        case .sinkholed: directBypassOpen = .no
        case .unknown:   directBypassOpen = .unknown
        }

        let counts = "\(perDomain.values.filter { $0 == .sinkholed }.count) sinkholed / " +
                     "\(perDomain.values.filter { $0 == .resolved }.count) resolved / " +
                     "\(perDomain.values.filter { $0 == .unknown }.count) unreadable of \(probeDomains.count)"
        return Result(enforcement: enforcement, perDomain: perDomain,
                      directBypassOpen: directBypassOpen,
                      detail: "Blocklist probes via \(resolver): \(counts)")
    }
}
