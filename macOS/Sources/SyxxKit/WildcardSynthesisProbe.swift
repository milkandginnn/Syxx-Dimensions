import Foundation

// ROADMAP Workstream H · datapoint F5 — cross-vantage NXDOMAIN-hijack / wildcard-synthesis.
//
// The global NX canary (DNSCanaries) proves a resolver hijacks NON-EXISTENT names using neutral
// canary domains. This proves the same thing for the TARGET domain's OWN namespace, which catches
// TARGET-SPECIFIC interference a global canary misses (a resolver that only block-pages nonexistent
// subdomains of a banned site). It queries a few random, guaranteed-nonexistent subdomains of the
// target at BOTH the resolver-under-test and a trusted reference resolver:
//
//   • reference DENIES the name (NXDOMAIN/NODATA) but the resolver-under-test hands back an address
//       → nxHijack: the resolver invented an answer for a name that provably does not exist. A legit
//         wildcard cannot cause this (a wildcard is authoritative, so BOTH vantages would see it).
//   • BOTH vantages resolve the random names → wildcardZone: the zone uses `*.domain` (benign; and a
//         useful disambiguation so a wildcard is never mistaken for hijacking).
//   • BOTH deny → noSynthesis: honest negative (benign).
//
// Honest v1 limits: needs a trusted reference resolver reachable (else inconclusive); a resolver that
// hijacks BOTH the target and the reference path identically would read as wildcardZone (rare — it
// would have to forge an authoritative-looking wildcard on both vantages). Cross-vantage disagreement
// on existence is the strong, low-false-positive signal; treat wildcardZone/noSynthesis as benign.

public struct WildcardSynthesis: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case noSynthesis     // random nonexistent subdomains denied at both vantages — honest (benign)
        case wildcardZone    // random subdomains resolve at both vantages — zone wildcard (benign)
        case nxHijack        // reference denies the name, resolver-under-test synthesizes an address — hijack
        case inconclusive    // no usable cross-vantage comparison, or mixed/ambiguous
        case untested
    }

    public let status: Status
    public let probedLabels: [String]        // the random subdomains queried
    public let syntheticAddresses: [String]  // addresses the resolver-under-test invented for denied names
    public let usableCount: Int              // labels with a usable cross-vantage comparison
    public let detail: String

    public init(status: Status, probedLabels: [String] = [], syntheticAddresses: [String] = [],
                usableCount: Int = 0, detail: String = "") {
        self.status = status
        self.probedLabels = probedLabels
        self.syntheticAddresses = syntheticAddresses
        self.usableCount = usableCount
        self.detail = detail
    }

    public static let untested = WildcardSynthesis(status: .untested, detail: "not run")
}

public enum WildcardSynthesisProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1

    /// One label's cross-vantage outcome. `refDenies` = the trusted resolver confirmed the name does
    /// not exist; `testAddrs` = addresses the resolver-under-test returned (empty if it also denied).
    public struct LabelObservation: Sendable {
        public let label: String
        public let testAddrs: [String]
        public let testHasAnswer: Bool
        public let refHasAnswer: Bool
        public let refDenies: Bool
        public init(label: String, testAddrs: [String], testHasAnswer: Bool, refHasAnswer: Bool, refDenies: Bool) {
            self.label = label
            self.testAddrs = testAddrs
            self.testHasAnswer = testHasAnswer
            self.refHasAnswer = refHasAnswer
            self.refDenies = refDenies
        }
    }

    /// Probe `count` random nonexistent subdomains of `domain` at the resolver-under-test vs a trusted
    /// reference, and classify. A label is only used if BOTH vantages returned a usable (non-transport-
    /// failure) existence signal, so a flaky path yields `inconclusive`, never a false hijack.
    public static func probe(domain: String,
                             resolverUnderTest: String,
                             reference: String = "1.1.1.1",
                             count: Int = 3,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> WildcardSynthesis {
        // B057: the reference must differ from the resolver-under-test — a vacuous cross-vantage
        // compare (same resolver on both sides) makes nxHijack impossible to fire and emits a
        // false-clean .negative. Mirror CNAMEChainProbe's filter; pick a distinct fallback.
        let effectiveReference: String
        if reference != resolverUnderTest {
            effectiveReference = reference
        } else {
            // Default 1.1.1.1 collided with the resolver-under-test; try alternatives.
            let fallbacks = ["8.8.8.8", "9.9.9.9"].filter { $0 != resolverUnderTest }
            guard let alt = fallbacks.first else {
                // All known references equal the resolver-under-test — cannot compare.
                return WildcardSynthesis(status: .inconclusive, probedLabels: [],
                    syntheticAddresses: [], usableCount: 0,
                    detail: "no distinct reference resolver available (reference == resolver-under-test)")
            }
            effectiveReference = alt
        }
        let n = max(1, min(count, 5))
        var obs: [LabelObservation] = []
        for _ in 0..<n {
            let name = "\(randomLabel()).\(domain)"
            let testResp = await query(DNSQuery(name: name, type: typeA, server: resolverUnderTest, port: 53))
            let refResp = await query(DNSQuery(name: name, type: typeA, server: effectiveReference, port: 53))

            // Both sides must give a definitive existence signal (resolved or genuine denial); a
            // transport failure / timeout on either side makes the label unusable.
            guard isDefinitive(testResp), isDefinitive(refResp) else { continue }

            let testAddrs = aRecords(testResp)
            obs.append(LabelObservation(
                label: name,
                testAddrs: testAddrs,
                testHasAnswer: testResp.status == .success && !testAddrs.isEmpty,
                refHasAnswer: refResp.status == .success && !aRecords(refResp).isEmpty,
                refDenies: refResp.status == .nxDomain || refResp.status == .noData))
        }
        return classify(obs)
    }

    static func classify(_ obs: [LabelObservation]) -> WildcardSynthesis {
        let labels = obs.map { $0.label }
        func make(_ s: WildcardSynthesis.Status, _ synthetic: [String], _ d: String) -> WildcardSynthesis {
            WildcardSynthesis(status: s, probedLabels: labels,
                              syntheticAddresses: Array(Set(synthetic)).ordinalSorted(), usableCount: obs.count, detail: d)
        }
        guard !obs.isEmpty else {
            return make(.inconclusive, [], "no label produced a usable cross-vantage comparison — reference unreachable or all transport-failed")
        }
        // A single confirmed synthesis (reference denies, test invents) is the strong signal.
        let hijacks = obs.filter { $0.refDenies && $0.testHasAnswer }
        if !hijacks.isEmpty {
            let synth = hijacks.flatMap { $0.testAddrs }
            return make(.nxHijack, synth,
                        "resolver invented address(es) for \(hijacks.count) name(s) a trusted resolver reports do not exist — NXDOMAIN hijack (block/ad page)")
        }
        if obs.allSatisfy({ $0.refHasAnswer && $0.testHasAnswer }) {
            return make(.wildcardZone, [], "random subdomains resolve at both vantages — the zone uses a wildcard (benign)")
        }
        if obs.allSatisfy({ $0.refDenies && !$0.testHasAnswer }) {
            return make(.noSynthesis, [], "random nonexistent subdomains are denied at both vantages — honest negative")
        }
        return make(.inconclusive, [], "mixed cross-vantage outcomes across \(obs.count) labels — no consistent wildcard or hijack signal")
    }

    // MARK: - helpers

    /// A definitive existence signal: the resolver resolved, or gave a genuine negative — not a
    /// transport failure that tells us nothing about whether the name exists.
    static func isDefinitive(_ r: DNSResponse) -> Bool {
        switch r.status {
        case .success, .nxDomain, .noData: return true
        default:                           return false
        }
    }

    static func aRecords(_ r: DNSResponse) -> [String] {
        r.records.filter { $0.type == typeA }.map { $0.rdata.trimmingCharacters(in: .whitespaces).lowercased() }
    }

    /// A 12-char lowercase alphanumeric label — collision with a real subdomain is negligible.
    static func randomLabel() -> String {
        let alphabet = Array("abcdefghijklmnopqrstuvwxyz0123456789")
        return String((0..<12).map { _ in alphabet.randomElement()! })
    }
}
