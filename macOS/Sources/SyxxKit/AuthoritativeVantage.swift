import Foundation

// ROADMAP Workstream H · datapoint A2 — authoritative-vs-resolver ground truth.
//
// The Synthesis engine otherwise establishes "truth" only by CONSENSUS across recursive
// resolvers. If an answer is rewritten upstream of all of them — or is a legitimate CDN/geo
// difference — consensus cannot tell which. This module gives the engine the reference it
// lacks: it discovers the domain's own authoritative nameservers and queries them DIRECTLY
// (checking the AA bit), then diffs the recursive resolver's answer against authoritative
// ground truth. A recursive answer the authority does not serve is real evidence of
// resolver-level manipulation; a recursive answer contained in the authoritative set rules it out.
//
// Honest v1 limits (documented so the engine weights this conservatively, never decisively):
//   • Delegation is discovered via a reputable public resolver (harder to manipulate than the
//     local one), not a full walk from the DNS root. A future hardening can walk the chain.
//   • CDN / geo-routed domains legitimately return DIFFERENT addresses to different queriers,
//     so a disjoint result is a signal to CORROBORATE (Moderate tier), not proof — and a
//     partial overlap is treated as inconclusive, not divergence. ASN-cohort matching
//     (datapoint A7) will later dampen benign CDN variance further.

public struct AuthoritativeComparison: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case matches         // recursive answer is contained in authoritative truth — clean
        case diverges        // recursive answer includes addresses authoritative does not serve
        case inconclusive    // authoritative unreachable/unverifiable, or ambiguous (CDN-like) overlap
        case untested        // not run
    }

    public let status: Status
    public let authoritativeAnswers: [String]   // A/AAAA rdata the authoritative NS actually serves
    public let resolverAnswers: [String]        // the recursive answer being checked
    public let nameservers: [String]            // authoritative NS hostnames consulted
    public let detail: String

    public init(status: Status,
                authoritativeAnswers: [String] = [],
                resolverAnswers: [String] = [],
                nameservers: [String] = [],
                detail: String = "") {
        self.status = status
        self.authoritativeAnswers = authoritativeAnswers
        self.resolverAnswers = resolverAnswers
        self.nameservers = nameservers
        self.detail = detail
    }

    public static let untested = AuthoritativeComparison(status: .untested, detail: "not run")
}

public enum AuthoritativeVantage {
    public static let typeA: UInt16 = 1
    public static let typeAAAA: UInt16 = 28
    public static let typeNS: UInt16 = 2

    /// Injectable query so this is fully unit-testable offline. Defaults to the real resolver.
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse

    /// Trusted bootstrap resolvers used for delegation discovery.  Using three independent
    /// resolvers prevents a single compromised resolver from poisoning the NS record set (B012).
    public static let defaultBootstrapResolvers = ["1.1.1.1", "8.8.8.8", "9.9.9.9"]

    /// Compare the recursive resolver's answer for `domain` against its authoritative nameservers.
    ///
    /// - Parameters:
    ///   - domain: the queried name.
    ///   - qtype: A (default) or AAAA — must match the type `resolverAnswers` came from.
    ///   - resolverAnswers: the rdata strings the user's recursive path returned (from Prophet).
    ///   - bootstrapResolvers: trusted public resolvers used to discover the delegation (B012).
    ///   - maxNameservers / maxServersProbed: bounds so a hostile/large delegation can't fan out.
    public static func compare(domain: String,
                               qtype: UInt16 = typeA,
                               resolverAnswers: [String],
                               bootstrapResolvers: [String] = defaultBootstrapResolvers,
                               maxNameservers: Int = 6,
                               maxServersProbed: Int = 4,
                               query: QueryFn = { await DNSResolver.queryUDPDetailed($0) },
                               resolverDidFail: Bool = false) async -> AuthoritativeComparison {

        let resolverSet = Set(resolverAnswers.map(normalizeAddress))

        // 1. Discover the authoritative nameservers by querying all bootstrap resolvers
        //    and keeping NS hosts that appear in ≥2 responses (majority consensus).
        let nsHosts = await discoverNameservers(for: domain, bootstraps: bootstrapResolvers,
                                                cap: maxNameservers, query: query)
        guard !nsHosts.isEmpty else {
            return AuthoritativeComparison(status: .inconclusive, resolverAnswers: Array(resolverSet),
                detail: "no authoritative nameservers found for \(domain) (delegation lookup failed)")
        }

        // 2. Resolve NS hostnames to IPs by querying all bootstrap resolvers and applying
        //    majority consensus (C061): keep an IP only if ≥2 bootstraps returned it, matching
        //    the NS-hostname path. The original code took the raw union with no vote at all.
        var nsIPSets: [Set<String>] = []
        var nsIPHostnames: [String: Set<String>] = [:]  // IP → set of NS hostnames that resolve to it
        for host in nsHosts {
            for bs in bootstrapResolvers {
                let resp = await query(DNSQuery(name: host, type: typeA, server: bs, port: 53))
                let ips = Set(resp.records.filter { $0.type == typeA }.map { $0.rdata })
                nsIPSets.append(ips)
                for ip in ips {
                    nsIPHostnames[ip, default: []].insert(host)
                }
            }
            // C061(b): count UNIQUE IPs (not total including duplicates) for the break condition.
            let uniqueCount = Set(nsIPSets.flatMap { $0 }).count
            if uniqueCount >= maxServersProbed { break }
        }
        // C061(a): apply majority consensus — an IP must appear in ≥2 bootstrap responses.
        var allIPs: [String: Int] = [:]
        for s in nsIPSets {
            for ip in s { allIPs[ip, default: 0] += 1 }
        }
        let consensusIPs = Set(allIPs.filter { $0.value >= 2 }.map(\.key))
        // D018: reject NS IPs that are not public-routable to prevent SSRF to internal hosts
        // via hostile delegation glue (127.0.0.1, 10.x, 169.254.169.254, etc.).
        // C061(c): sort deterministically before prefix so the probed set is reproducible
        // across runs (Set iteration order is hash-seeded per process).
        var nsIPs = Array(consensusIPs)
            .filter { IPClassifier.classify($0) == .publicRoutable }
            .ordinalSorted()
        nsIPs = Array(nsIPs.prefix(maxServersProbed))
        guard !nsIPs.isEmpty else {
            return AuthoritativeComparison(status: .inconclusive, resolverAnswers: Array(resolverSet),
                nameservers: nsHosts, detail: "could not resolve authoritative nameserver IPs (or all resolved to non-public addresses)")
        }

        // 3. Query all discovered authoritative IPs directly.  Each collects the answer set it
        //    serves; we require at least AA-qualified quorum (≥2 responders) so a single
        //    compromised/stale NS cannot fabricate ground truth (B011).
        var authSets: [Set<String>] = []
        // C060: track whether any authoritative responder returned CNAME records without
        // address records — this is a delegation, not a genuine NODATA. An empty auth set
        // from a CNAME-only answer must not produce `.diverges`.
        var sawCNAMEOnly = false
        for ip in nsIPs {
            let resp = await query(DNSQuery(name: domain, type: qtype, server: ip, port: 53))
            guard resp.isAuthoritative,
                  resp.status == .success || resp.status == .noData || resp.status == .nxDomain
            else { continue }
            let addrSet = Set(resp.records.filter { $0.type == qtype }.map { normalizeAddress($0.rdata) })
            // C060: if the response has CNAME records but no address records of the requested
            // type, the zone delegates via CNAME and address ground truth is not obtainable here.
            if addrSet.isEmpty && !resp.records.filter({ $0.type == 5 }).isEmpty {
                sawCNAMEOnly = true
            }
            authSets.append(addrSet)
        }
        guard authSets.count >= 2 else {
            let detail = authSets.isEmpty
                ? "no authoritative server returned an AA-flagged answer"
                : "only 1 authoritative server responded (need ≥2 for quorum)"
            return AuthoritativeComparison(status: .inconclusive, resolverAnswers: Array(resolverSet),
                nameservers: nsHosts, detail: detail)
        }
        // Majority consensus: pick the answer set shared by the most responders.
        let grouped = Dictionary(grouping: authSets, by: { $0 })
        // C061(d): detect ties — a 2-vs-2 split among authoritative responders is itself a
        // strong tampering signal. Pick a winner only when one group strictly outnumbers the
        // rest; otherwise return inconclusive with both sets recorded.
        let sortedGroups = grouped.sorted { $0.value.count > $1.value.count }
        guard let topGroup = sortedGroups.first else {
            return AuthoritativeComparison(status: .inconclusive, resolverAnswers: Array(resolverSet),
                nameservers: nsHosts, detail: "no authoritative answer sets collected")
        }
        if sortedGroups.count > 1 && sortedGroups[0].value.count == sortedGroups[1].value.count {
            let setDescriptions = sortedGroups.prefix(3).map { "\($0.value.count)×\($0.key.sorted())" }.joined(separator: " vs ")
            return AuthoritativeComparison(status: .inconclusive,
                authoritativeAnswers: Array(topGroup.key).sorted(),
                resolverAnswers: Array(resolverSet), nameservers: nsHosts,
                detail: "authoritative responders disagree (\(setDescriptions)) — split is itself a tampering signal; inconclusive")
        }
        let authSet = topGroup.key

        // B289: surface disagreeing authoritative servers in the result. Previously, servers
        // not in the majority group were silently discarded. Their answer sets may indicate
        // split-brain DNS, a compromised NS, or geo-specific serving — all useful signals
        // the synthesis engine should weigh, not lose.
        let disagreeingSets: [Set<String>] = sortedGroups.dropFirst().compactMap(\.key)
        let disagreeingDetail: String
        if !disagreeingSets.isEmpty {
            let descs = disagreeingSets.map { "[\($0.sorted().joined(separator: ", "))]" }
            disagreeingDetail = "; \(disagreeingSets.count) disagreeing server(s) returned: \(descs.joined(separator: ", "))"
        } else {
            disagreeingDetail = ""
        }

        // C060: if the consensus auth set is empty but the authoritative answer was a CNAME
        // delegation (not a genuine NODATA/NXDOMAIN), address ground truth is not obtainable
        // from this zone. Return inconclusive instead of a fabricated `.diverges`.
        if authSet.isEmpty && sawCNAMEOnly {
            return AuthoritativeComparison(status: .inconclusive, resolverAnswers: Array(resolverSet),
                nameservers: nsHosts,
                detail: "authoritative answer is a CNAME delegation — address ground truth not obtainable from this zone\(disagreeingDetail)")
        }

        // 4. Compare the recursive answer against authoritative ground truth.
        return classify(resolverSet: resolverSet, authSet: authSet, nameservers: nsHosts, domain: domain,
                        resolverDidFail: resolverDidFail, disagreeingDetail: disagreeingDetail)
    }

    // MARK: - Internals

    static func classify(resolverSet: Set<String>, authSet: Set<String>,
                         nameservers: [String], domain: String,
                         resolverDidFail: Bool = false,
                         disagreeingDetail: String = "") -> AuthoritativeComparison {
        let auth = Array(authSet).ordinalSorted()
        let res = Array(resolverSet).ordinalSorted()
        func make(_ s: AuthoritativeComparison.Status, _ d: String) -> AuthoritativeComparison {
            AuthoritativeComparison(status: s, authoritativeAnswers: auth, resolverAnswers: res,
                                    nameservers: nameservers, detail: d + disagreeingDetail)
        }

        if resolverSet.isEmpty && authSet.isEmpty {
            // B036: if the resolver query itself failed (timeout/transport error), we cannot
            // conclude "matches" — the empty set might mean "no data" or "no response."
            if resolverDidFail {
                return make(.inconclusive, "resolver query failed — cannot confirm consistency with authoritative empty answer")
            }
            return make(.matches, "both the recursive resolver and authoritative report no \(domain) address — consistent")
        }
        if resolverSet.isEmpty {
            // Authority serves records but the recursive path returned none — a blocked/failed
            // recursive answer, not a manipulated one. Inconclusive here (Prophet already flags the miss).
            return make(.inconclusive, "recursive returned no address though authoritative serves \(authSet.count) — recursive lookup incomplete")
        }
        if authSet.isEmpty {
            // Authority says the name has no such record, yet the recursive path handed back an
            // address — the classic injected/hijacked answer for a non-existent name.
            return make(.diverges, "recursive returned \(resolverSet.count) address(es) the authoritative NS does not serve at all (authoritative negative) — likely injected answer")
        }
        if resolverSet.isSubset(of: authSet) {
            return make(.matches, "every recursive address is served by the authoritative NS — consistent with ground truth")
        }
        if resolverSet.isDisjoint(with: authSet) {
            // C015: same-CDN-provider observation annotates the divergence but does NOT erase it.
            // A /13 CDN block (Cloudflare, Akamai) spans millions of IPs — an attacker sitting in
            // the same provider's address space is not evidence of benign geo-routing. The
            // `diverges` result is retained; the provider is surfaced as a corroboration note
            // for the synthesis layer to weigh (not for this function to resolve).
            if let provider = NetworkCohort.sharedProvider(Array(resolverSet), Array(authSet)) {
                return make(.inconclusive, "recursive and authoritative addresses are disjoint but both lie in \(provider) ranges — same-provider softening; inconclusive (CDN/geo routing possible)")
            }
            return make(.diverges, "recursive addresses share nothing with the authoritative set — possible upstream rewrite (corroborate: CDN/geo routing can also cause this)")
        }
        // Partial overlap: some addresses match authoritative, some don't — the CDN/geo signature.
        // Not enough to call manipulation without ASN-cohort corroboration (A7).
        return make(.inconclusive, "recursive and authoritative overlap only partially — consistent with CDN/geo-routed answers; inconclusive without ASN corroboration")
    }

    /// Walk up the label hierarchy (bounded) until an ANSWER-section NS set appears.
    /// Queries all bootstrap resolvers and returns hosts that appear in ≥2 responses
    /// (majority consensus) so a single compromised resolver cannot poison delegation
    /// discovery (B012).
    static func discoverNameservers(for domain: String, bootstraps: [String], cap: Int,
                                    query: QueryFn) async -> [String] {
        var candidate = domain.hasSuffix(".") ? String(domain.dropLast()) : domain
        // At most one strip per label, and never below the two-label registrable floor.
        for _ in 0..<8 {
            let labels = candidate.split(separator: ".")
            guard labels.count >= 2 else { break }

            // Collect NS host sets from each bootstrap resolver.
            var nsSets: [Set<String>] = []
            for bs in bootstraps {
                let resp = await query(DNSQuery(name: candidate, type: typeNS, server: bs, port: 53))
                let hosts = Set(resp.records
                    .filter { $0.type == typeNS }
                    .map { normalizeName($0.rdata) }
                    .filter { !$0.isEmpty })
                if !hosts.isEmpty {
                    nsSets.append(hosts)
                }
            }

            if !nsSets.isEmpty {
                // Majority consensus: a host must appear in at least 2 sets.
                if let agreed = majorityConsensus(nsSets), !agreed.isEmpty {
                    return Array(agreed).sorted().prefix(cap).map { $0 }
                }
                // No consensus — walk up one label and try again (perhaps at the parent zone).
            }

            candidate = labels.dropFirst().joined(separator: ".")
        }
        return []
    }

    /// Returns the intersection of all sets if ≥2 bootstraps responded; otherwise returns
    /// nil (inconclusive) — a single responder cannot provide ground truth (C007).
    /// If no bootstraps responded returns nil.
    static func majorityConsensus(_ sets: [Set<String>]) -> Set<String>? {
        guard !sets.isEmpty else { return nil }
        guard sets.count >= 2 else { return nil }
        // A host must appear in ≥2 responder sets.
        var counts: [String: Int] = [:]
        for s in sets {
            for host in s { counts[host, default: 0] += 1 }
        }
        let threshold = 2
        return Set(counts.filter { $0.value >= threshold }.map(\.key))
    }

    static func normalizeName(_ s: String) -> String {
        var n = s.trimmingCharacters(in: .whitespaces)
        if n.hasSuffix(".") { n = String(n.dropLast()) }
        return n.lowercased()
    }

    static func normalizeAddress(_ s: String) -> String {
        s.trimmingCharacters(in: .whitespaces).lowercased()
    }
}
