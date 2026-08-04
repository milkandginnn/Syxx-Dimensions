import Foundation

/// B281 — the SINGLE cross-vantage agreement policy, shared by Prophet's multi-resolver consensus
/// card (`SyxxApp/ProphetView.evaluateConsensus`) and Mirror's local-vs-baseline comparison
/// (`VantageComparer.evaluate`). Keeping the policy in ONE place is the whole point: two hand-rolled
/// copies are exactly how B271/B272 (macOS fixed, Windows twin left behind) happened.
///
/// The owner's reported false positive (2026-08-02, on a VPN: 140.82.113.3 vs .4, both GitHub) came
/// from Prophet, which reduced each resolver to `records.first?.rdata` and called any set with >1
/// distinct string "disagreement / possible tampering". This type compares FULL A/AAAA RRsets and
/// applies an honest, adversary-aware policy.
///
/// Policy (post adversarial review 2026-08-02 — see B281 body for the audit trail):
///  * Identical normalized A/AAAA RRsets across the reachable vantages → `.match`.
///  * A blocking / non-routable answer (127/8, 10/8, 0.0.0.0, …) sitting beside routable answers →
///    `.mismatch` (the classic redirect-to-block signature; needs no second transport).
///  * Any other DIFFERENCE is classified ONLY against an attacker-INDEPENDENT reference (a DoH
///    consensus, `dohCorroboratedPool`). A UDP/Do53 "baseline" is authored by the same on-path
///    attacker who would poison the local answer, so it is never trusted for this decision.
///     - Different responsible network from the trusted reference → `.mismatch`.
///     - Same responsible network (or mixed family / not comparable) → `.inconclusive`.
///     - No trusted reference available at all → `.inconclusive` (strength is not computable; we do
///       NOT silently normalize the disagreement into agreement, and we do NOT invent an accusation).
///
/// What this policy deliberately does NOT do, and why (adversarial review findings):
///  * It never turns a difference into a CLEAN "match" from an environmental fact. There is no VPN
///    branch: a tunnel interface (`utun`/`ppp`/…) proves nothing about whether THIS query egressed
///    it, `ppp` matches every PPPoE subscriber's ISP link, and a VPN operator is itself a full
///    on-path adversary. VPN state, if ever surfaced, must be an annotation on an inconclusive
///    result with positive proof of egress — never a state transition. So the owner's VPN case
///    resolves to `.inconclusive`, which already removes the false "tampering", without minting an
///    exculpating claim.
///  * Numeric /16-/32 adjacency is a WEAK, forgeable signal. It is used ONLY to separate `.mismatch`
///    from `.inconclusive` against the attacker-independent reference — it never GRANTS a match.
public enum CrossVantageConsensus {

    /// The shared five-way outcome (plus the two "not enough to judge" states Prophet needs).
    public enum Outcome: String, Sendable, Equatable, Codable {
        case match          // identical normalized A/AAAA RRsets across the reachable vantages
        case mismatch       // a confirmed redirect: blocking answer, or different responsible
                            // network from an attacker-independent reference
        case inconclusive   // differs, but no attacker-independent evidence confirms a redirect —
                            // never rendered as a clean, never a bare accusation
        case insufficient   // fewer than two vantages returned a usable answer
        case noData         // no vantage returned a usable answer
    }

    // MARK: - Reduction helpers (callers must feed FULL A/AAAA RRsets, never `records.first`)

    /// C117: reduce a resolver's raw records to its A(1)/AAAA(28) rdata set. CNAME(5)/RRSIG(46) rdata
    /// are domain names / hex, not addresses, and must never enter the comparison. Returns `nil` when
    /// the resolver produced no usable address answer (drop that vantage — it is not "agreement").
    public static func addressSet(_ records: [DNSRecord]) -> Set<String>? {
        let addrs = Set(records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))
        return addrs.isEmpty ? nil : addrs
    }

    /// B281 verified equivalence reference: addresses each returned by ≥2 *independent* DoH endpoints
    /// (address records only). DoH is HTTPS-routed and attacker-independent (C025) — a port-53 on-path
    /// hijacker who rewrites every Do53 answer cannot forge DoH corroboration — so ≥2 independent DoH
    /// endpoints agreeing on an address is attacker-independent evidence it is a legitimate endpoint.
    /// Do53/UDP corroboration is deliberately NOT counted: the attacker authors it (adversarial-review
    /// finding 2). `nil` / <2 responding DoH endpoints ⇒ no trusted reference (empty set).
    ///
    /// LIMITATION (documented, not fixed here): the DoH legs use a stock, unpinned URLSession and
    /// bootstrap by hostname through the resolver under test (DNSResolver.swift), so an adversary
    /// holding a client-trusted TLS root can forge all DoH vantages at once. This reference is
    /// attacker-independent against a *port-53 hijacker*, NOT against a TLS-MITM with a trusted root.
    public static func dohCorroboratedPool(_ dohOutcomes: [String: ResolverQueryOutcome]?) -> Set<String> {
        guard let dohOutcomes = dohOutcomes else { return [] }
        var counts: [String: Int] = [:]
        for outcome in dohOutcomes.values where !outcome.didFail {
            // one vote per endpoint per address (dedup within an endpoint's own answer set)
            let addrs = Set(outcome.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))
            for a in addrs { counts[a, default: 0] += 1 }
        }
        return Set(counts.filter { $0.value >= 2 }.keys)
    }

    // MARK: - The one shared decision function

    /// Prophet's entry point: classify agreement across N peer resolver answer sets (each already an
    /// A/AAAA rdata set via `addressSet`). `trustedReference` is an attacker-independent consensus
    /// when the caller has one (empty otherwise — Prophet's oracles are Do53-only today, so it passes
    /// empty and any non-blocking difference is honestly `.inconclusive`).
    public static func classifyPeerConsensus(vantageAnswers: [Set<String>],
                                             trustedReference: Set<String> = []) -> Outcome {
        let usable = vantageAnswers.filter { !$0.isEmpty }
        if usable.isEmpty { return .noData }
        if usable.count < 2 { return .insufficient }

        // Identical normalized RRsets (order-independent set equality) → agreement.
        if usable.dropFirst().allSatisfy({ $0 == usable[0] }) { return .match }

        // Differ. A blocking answer beside a routable one is a redirect-to-block signature.
        let union = usable.reduce(into: Set<String>()) { $0.formUnion($1) }
        let hasBlocking = union.contains { IPClassifier.isBlockingAnswer($0) }
        let hasRoutable = union.contains { !IPClassifier.isBlockingAnswer($0) }
        if hasBlocking && hasRoutable { return .mismatch }

        // Any other difference is judged only against the attacker-independent reference.
        return classifyDivergence(candidate: union, trustedReference: trustedReference)
    }

    /// The load-bearing shared decision: given a candidate answer that DIFFERS from the rest, decide
    /// mismatch vs inconclusive using ONLY an attacker-independent reference. Mirror calls this from
    /// its disjoint branch with the DoH consensus; Prophet calls it (via `classifyPeerConsensus`)
    /// with whatever trusted reference it has. This is the single place the strength policy lives.
    public static func classifyDivergence(candidate: Set<String>, trustedReference: Set<String>) -> Outcome {
        if candidate.isEmpty { return .inconclusive }
        // Finding 2: with no attacker-independent reference, strength is not computable. We refuse to
        // downgrade toward "weak" using attacker-authored Do53, and we refuse to invent an accusation.
        if trustedReference.isEmpty { return .inconclusive }
        return isStrongDisagreement(candidate: candidate, reference: trustedReference) ? .mismatch : .inconclusive
    }

    // MARK: - Strength primitives (responsible-network, family-aware)

    /// B281 "strong disagreement": a candidate answer sits in a *different responsible network* from
    /// the trusted reference. Family-aware (adversarial-review fix): a candidate address is only
    /// judged against SAME-family reference addresses, so a NAT64/DNS64 AAAA compared to A references
    /// is "not comparable" (skipped) rather than a guaranteed false `.mismatch`. An unparseable
    /// candidate address is likewise skipped (a blocking/invalid local answer is caught upstream by
    /// the redirect-to-block check). Strong iff SOME comparable candidate address shares a responsible
    /// network with NO same-family reference address.
    public static func isStrongDisagreement(candidate: Set<String>, reference: Set<String>) -> Bool {
        for c in candidate {
            guard let cFamily = addressFamily(c) else { continue }        // unparseable → skip
            let sameFamilyRefs = reference.filter { addressFamily($0) == cFamily }
            if sameFamilyRefs.isEmpty { continue }                        // cross-family (NAT64) → skip
            if !sameFamilyRefs.contains(where: { sharesResponsibleNetwork(c, $0) }) {
                return true                                               // comparable + out-of-network
            }
        }
        return false
    }

    /// B281: same responsible network? IPv4 /16, IPv6 /32 — deliberately COARSE proxies for
    /// "responsible network". A single operator routinely owns many contiguous /24s, so a narrower
    /// prefix would call same-operator pods "different networks" and re-create the false positive.
    /// The coarse choice errs toward WITHHOLDING an accusation (inconclusive), never toward a false
    /// clean. This is only ever a mismatch-vs-inconclusive discriminator against a trusted reference;
    /// it never grants a match. Mixed families / unparseable ⇒ false (not the same network).
    public static func sharesResponsibleNetwork(_ a: String, _ b: String) -> Bool {
        if let a4 = ipv4Octets(a), let b4 = ipv4Octets(b) {
            return a4[0] == b4[0] && a4[1] == b4[1]           // same /16
        }
        if let a6 = ipv6Bytes(a), let b6 = ipv6Bytes(b) {
            return Array(a6[0..<4]) == Array(b6[0..<4])       // same /32
        }
        return false
    }

    private enum AddressFamily { case v4, v6 }

    private static func addressFamily(_ value: String) -> AddressFamily? {
        if ipv4Octets(value) != nil { return .v4 }
        if ipv6Bytes(value) != nil { return .v6 }
        return nil
    }

    private static func ipv4Octets(_ value: String) -> [UInt8]? {
        var addr = in_addr()
        guard value.withCString({ inet_pton(AF_INET, $0, &addr) }) == 1 else { return nil }
        let raw = UInt32(bigEndian: addr.s_addr)
        return [UInt8((raw >> 24) & 0xff), UInt8((raw >> 16) & 0xff), UInt8((raw >> 8) & 0xff), UInt8(raw & 0xff)]
    }

    private static func ipv6Bytes(_ value: String) -> [UInt8]? {
        var addr = in6_addr()
        guard value.withCString({ inet_pton(AF_INET6, $0, &addr) }) == 1 else { return nil }
        return withUnsafeBytes(of: addr.__u6_addr.__u6_addr8) { Array($0) }
    }
}
