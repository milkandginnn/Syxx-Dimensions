import Foundation

// ROADMAP Workstream H · datapoint F7 — CNAME-chain cross-vantage comparison.
//
// VantageComparer compares the final A-record set across resolvers; it does not look at the CNAME
// path taken to get there. Poisoning / redirection can rewrite a mid-chain CNAME target. This
// reconstructs the full CNAME chain (a recursive resolver returns the whole chain in one answer:
// `domain CNAME x`, `x CNAME y`, `y A …`) at the resolver-under-test and at trusted references, and
// compares the chain STRUCTURE.
//
// Low-false-positive by design: CDN/geo routing legitimately gives different edge CNAMEs to different
// resolvers, so this only calls `divergent` when the references AGREE unanimously and the
// resolver-under-test is the lone outlier. References disagreeing among themselves (ordinary geo
// variance) is `inconclusive`, never a false positive. `divergent` is weak corroboration.

public struct CNAMEChainAudit: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case consistent    // the resolver-under-test's chain matches a trusted reference — benign
        case noCNAME       // no CNAME chain anywhere (direct A/AAAA) — not applicable
        case divergent     // references agree; the resolver-under-test's chain is the lone outlier (corroborate)
        case inconclusive  // references disagree among themselves (geo), or insufficient data
        case untested
    }

    public let status: Status
    public let testChain: [String]        // ordered hop names at the resolver under test
    public let referenceChains: [[String]]
    public let detail: String

    public init(status: Status, testChain: [String] = [], referenceChains: [[String]] = [], detail: String = "") {
        self.status = status
        self.testChain = testChain
        self.referenceChains = referenceChains
        self.detail = detail
    }

    public static let untested = CNAMEChainAudit(status: .untested, detail: "not run")
}

public enum CNAMEChainProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1
    public static let typeCNAME: UInt16 = 5
    static let maxHops = 16

    /// Compare the CNAME chain the resolver-under-test returns for `domain` against trusted references.
    public static func probe(domain: String,
                             resolverUnderTest: String,
                             references: [String] = ["1.1.1.1", "8.8.8.8", "9.9.9.9"],
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> CNAMEChainAudit {
        func chainAt(_ server: String) async -> (chain: [String], completion: ChainCompletion) {
            let resp = await query(DNSQuery(name: domain, type: typeA, server: server, port: 53))
            guard resp.status == .success || resp.status == .noData else { return ([], .terminal) }
            return reconstructChain(domain: domain, records: resp.records)
        }
        let test = await chainAt(resolverUnderTest)
        // C072: conflicting CNAMEs (same owner, different targets) are an injection signature —
        // stronger evidence than divergence, surface immediately.
        if test.completion == .conflictingCNAME {
            return CNAMEChainAudit(status: .divergent, testChain: test.chain, referenceChains: [],
                detail: "conflicting CNAME records for one owner detected — invalid DNS and a plain injection signature, not comparable")
        }
        // B075: a loop or cap-exhausted test chain is malformed — do not compare.
        guard test.completion == .terminal else {
            return CNAMEChainAudit(status: .inconclusive, testChain: test.chain, referenceChains: [],
                detail: "CNAME chain reconstruction \(test.completion == .loop ? "hit a loop" : "exceeded \(maxHops)-hop cap") — malformed evidence, not comparable")
        }
        var refChains: [[String]] = []
        var refCompletions: [ChainCompletion] = []
        for ref in references where ref != resolverUnderTest {
            let r = await chainAt(ref)
            if !r.chain.isEmpty {
                refChains.append(r.chain)
                refCompletions.append(r.completion)
            }
        }
        // B075: if any reference chain hit a loop or cap, the comparison is unreliable.
        if refCompletions.contains(where: { $0 != .terminal }) {
            return CNAMEChainAudit(status: .inconclusive, testChain: test.chain, referenceChains: refChains,
                detail: "one or more reference CNAME chains hit a loop or hop cap — malformed reference, not comparable")
        }
        return classify(testChain: test.chain, referenceChains: refChains)
    }

    /// B075: why chain reconstruction stopped. A loop or hop-cap exhaustion is malformed
    /// evidence and must not be compared as an ordinary chain.
    /// C072: conflicting CNAMEs (same owner, different targets) are an injection signature.
    enum ChainCompletion: String, Codable, Sendable {
        case terminal           // reached a name with no further CNAME — normal
        case loop               // a target was already seen — hostile cyclic chain
        case capExceeded        // hit the 16-hop cap — hostile overlong chain
        case conflictingCNAME   // C072: two CNAME records with the same owner but different targets — invalid DNS, injection signature
    }

    /// Follow the CNAME map from `domain` to the terminal, with loop + hop-cap protection.
    /// B075: returns (chain, completion) so callers can distinguish a normal terminal from a
    /// loop or cap-exhaustion, which are malformed evidence rather than ordinary chains.
    static func reconstructChain(domain: String, records: [DNSRecord]) -> (chain: [String], completion: ChainCompletion) {
        var cnameMap: [String: String] = [:]
        for r in records where r.type == typeCNAME {
            let owner = normalize(r.name)
            let target = normalize(r.rdata)
            // C072: two CNAME records with the same owner but different targets are invalid DNS
            // and a plain injection signature — detect and surface instead of silently last-wins.
            if let existing = cnameMap[owner], existing != target {
                return ([normalize(domain)], .conflictingCNAME)
            }
            cnameMap[owner] = target
        }
        var chain = [normalize(domain)]
        var seen: Set<String> = [normalize(domain)]
        var current = normalize(domain)
        while let target = cnameMap[current] {
            if chain.count >= maxHops {
                return (chain, .capExceeded)
            }
            if !seen.insert(target).inserted {
                return (chain, .loop)
            }
            chain.append(target)
            current = target
        }
        return (chain, .terminal)
    }

    static func classify(testChain: [String], referenceChains: [[String]]) -> CNAMEChainAudit {
        func make(_ s: CNAMEChainAudit.Status, _ d: String) -> CNAMEChainAudit {
            CNAMEChainAudit(status: s, testChain: testChain, referenceChains: referenceChains, detail: d)
        }
        guard !testChain.isEmpty, !referenceChains.isEmpty else {
            return make(.inconclusive, "insufficient usable answers to compare CNAME chains")
        }
        func canon(_ c: [String]) -> String { c.joined(separator: " → ") }
        // No CNAME anywhere → not applicable.
        if testChain.count <= 1 && referenceChains.allSatisfy({ $0.count <= 1 }) {
            return make(.noCNAME, "no CNAME chain for this name (direct A/AAAA) at any vantage")
        }
        let refCanons = Set(referenceChains.map(canon))
        // B285: only return .consistent when references AGREE unanimously and test matches.
        // If references disagree among themselves (refCanons.count > 1), the test matching
        // one of them is not "consistent" — it's coincidental alignment with one of several
        // disputed chains. There's no consensus to validate against, so this is inconclusive.
        if refCanons.count == 1 && refCanons.contains(canon(testChain)) {
            return make(.consistent, "the resolver's CNAME chain matches the unanimous trusted reference — consistent")
        }
        if refCanons.count == 1 {
            return make(.divergent, "trusted resolvers agree on the CNAME chain but this resolver returns a different one (\(canon(testChain))) — outlier; corroborate (a split-horizon/geo edge case can also cause this)")
        }
        return make(.inconclusive, "trusted resolvers disagree on the CNAME chain (CDN/geo variance) — no consensus to compare against")
    }

    static func normalize(_ s: String) -> String {
        var n = s.trimmingCharacters(in: .whitespaces).lowercased()
        if n.hasSuffix(".") { n = String(n.dropLast()) }
        return n
    }
}
