import Foundation

/// Comparison result of a single query at one vantage point, carrying the typed DNS status
/// so a transport failure is never confused with an authoritative empty answer.
public struct ResolverQueryOutcome: Codable, Hashable, Sendable {
    public let records: [DNSRecord]
    public let status: DNSResponseStatus
    public let errorDescription: String?

    public init(records: [DNSRecord], status: DNSResponseStatus = .success, errorDescription: String? = nil) {
        self.records = records
        self.status = status
        self.errorDescription = errorDescription
    }

    public init(response: DNSResponse) {
        self.records = response.records
        self.status = response.status
        self.errorDescription = response.errorMessage
    }

    /// True only for outcomes where the resolver could not be reached or its answer could not
    /// be parsed. A valid NXDOMAIN/NODATA is *not* a failure — it is a usable negative answer.
    public var didFail: Bool {
        switch status {
        case .timeout, .transportError, .malformedResponse, .responseIdMismatch, .truncated:
            return true
        default:
            return false
        }
    }

    public var hasRecords: Bool {
        !records.isEmpty
    }
}

public struct ComparisonResult: Codable, Sendable {
    public let localServer: String
    public let localRecords: [DNSRecord]
    public let publicResolverResults: [String: [DNSRecord]]
    public let isRedirectionDetected: Bool
    public let isInconclusive: Bool
    public let isDegraded: Bool
    public let localOutcome: ResolverQueryOutcome
    public let publicResolverOutcomes: [String: ResolverQueryOutcome]
    /// DoH cross-vantage outcomes (B010). Carried alongside the UDP outcomes but *not* folded into
    /// `publicResolverOutcomes` — the signed authoritative set is UDP-only, so DoH never changes the
    /// attested bytes. `nil` when no DoH leg was run. Twin of C# `ComparisonResult.DohOutcomes`.
    public let dohOutcomes: [String: ResolverQueryOutcome]?

    public init(
        localServer: String,
        localRecords: [DNSRecord],
        publicResolverResults: [String : [DNSRecord]],
        isRedirectionDetected: Bool,
        isInconclusive: Bool = false,
        isDegraded: Bool = false,
        localOutcome: ResolverQueryOutcome? = nil,
        publicResolverOutcomes: [String: ResolverQueryOutcome]? = nil,
        dohOutcomes: [String: ResolverQueryOutcome]? = nil
    ) {
        self.localServer = localServer
        self.localRecords = localRecords
        self.publicResolverResults = publicResolverResults
        self.isRedirectionDetected = isRedirectionDetected
        self.isInconclusive = isInconclusive
        self.isDegraded = isDegraded
        self.localOutcome = localOutcome ?? ResolverQueryOutcome(records: localRecords)
        self.publicResolverOutcomes = publicResolverOutcomes ?? publicResolverResults.mapValues {
            ResolverQueryOutcome(records: $0)
        }
        self.dohOutcomes = dohOutcomes
    }
}

/// C046/C056: tri-state result for the unanimous-tampering detector. `Bool` is the wrong
/// return type — it cannot distinguish "no tampering" from "could not tell".
public enum UnanimousTamperResult: Sendable {
    case tampered          // confirmed: UDP answers unanimously disagree with DoH consensus
    case notTampered       // no tampering pattern detected
    case undetermined      // insufficient evidence to judge (missing DoH, too few baselines)
}

public enum VantageComparer {

    /// Stream A: the public-resolver roster and DoH endpoints are now PROJECTIONS of the single
    /// canonical roster on `ResolverConsensusEngine`. The IPs/URLs are no longer hand-maintained
    /// here — the roster lives in ONE Kit place so Prophet and Mirror can never drift. Shapes are
    /// unchanged (`[String: String]`), so `publicResolvers.keys.sorted()` and lookups by name at the
    /// CLI / app call sites behave identically.
    public static let publicResolvers: [String: String] =
        Dictionary(uniqueKeysWithValues: ResolverConsensusEngine.publicResolvers.map { ($0.name, $0.ip) })

    public static let dohEndpoints: [String: String] = ResolverConsensusEngine.dohEndpoints

    /// Compares the results of a query resolved locally against public DNS resolvers.
    ///
    /// The result is deliberately conservative. Because large sites sit behind CDNs and
    /// geo-DNS, two honest resolvers routinely return *different* IPs for the same name.
    /// We therefore only call it redirection when the local answer set shares **no** member
    /// with the consensus of the public resolvers — and we surface degraded/inconclusive
    /// states so the caller never prints "secure" on the back of a failed baseline.
    public static func compareVantagePoints(domain: String, type: UInt16 = 1, localServer: String) async throws -> ComparisonResult {
        // Stream A: the local + Do53 + DoH fan-out now lives in the shared ResolverConsensusEngine,
        // sourced from the single canonical roster. The comparer keeps ownership of the asymmetric
        // decision (`evaluate`) and the `ComparisonResult` assembly — including the exact wire keys
        // the signed evidence depends on — unchanged. Mirror does NOT write the shared store: per §3
        // it READS Prophet's last peer result and never re-runs (or overwrites) consensus itself.
        let (localOutcome, publicOutcomes, dohOutcomes) =
            await ResolverConsensusEngine.runLocalDeltaFanOut(domain: domain, type: type, localServer: localServer)
        let localRecords = localOutcome.records

        let result = evaluate(localOutcome: localOutcome, publicOutcomes: publicOutcomes, dohOutcomes: dohOutcomes)

        return ComparisonResult(
            localServer: localServer,
            localRecords: localRecords,
            publicResolverResults: publicOutcomes.mapValues(\.records),
            isRedirectionDetected: result.redirection,
            isInconclusive: result.inconclusive,
            isDegraded: result.degraded,
            localOutcome: localOutcome,
            publicResolverOutcomes: publicOutcomes,
            dohOutcomes: dohOutcomes
        )
    }

    struct Result {
        let redirection: Bool
        let inconclusive: Bool
        let degraded: Bool
    }

    /// Pure decision logic, factored out so it can be unit-tested without a network.
    /// Twin of C# `VantageComparer.Evaluate`; `dohOutcomes` enables the B010 cross-transport check.
    ///
    /// C025: when DoH outcomes are available, they are used as the primary baseline — an on-path
    /// attacker rewriting all outbound port-53 (including to 1.1.1.1/8.8.8.8/9.9.9.9) also
    /// controls the UDP baselines, making local==baseline and hiding the tampering. DoH baselines
    /// are attacker-independent (HTTPS transport), so they cannot be rewritten by a port-53 hijacker.
    static func evaluate(localOutcome: ResolverQueryOutcome,
                         publicOutcomes: [String: ResolverQueryOutcome],
                         dohOutcomes: [String: ResolverQueryOutcome]? = nil) -> Result {
        // C025: prefer DoH baselines (attacker-independent HTTPS transport) over UDP when
        // available. UDP baselines are still used as a fallback when DoH fails, and for the
        // B010 cross-transport comparison.
        let dohBaselines = dohOutcomes?.values.filter { $0.hasRecords } ?? []
        let udpBaselines = publicOutcomes.values.filter { $0.hasRecords }
        // W04: prefer DoH only once it MEETS the corroboration floor (≥2 answering endpoints) — the
        // same quorum `detectUnanimousTampering` enforces at its own `< 2` gate. A lone answering
        // endpoint used to be preferred anyway, which made `baselines` a single element and tripped
        // the `!sufficientBaseline` early return below: every usable UDP baseline was discarded and
        // the run returned `degraded` with `inconclusive == false`, before the residual guard at the
        // foot of this function could ever be reached. One witness is below the floor, so it counts
        // as none and the UDP baselines carry the comparison instead.
        //
        // F2: what holds here is NARROWER than "strictly fail-closed, opens no new path to
        // `redirection`" — that earlier claim was wrong and is corrected rather than acted on.
        // `baselines` does not only feed the B281 disjoint branch; it also feeds `publicUnion`,
        // `resolverOverlapCount` and `sufficientBaseline`. In the `dohBaselines.count == 1 &&
        // udpBaselines.count >= 2` case the old code early-returned `degraded` at
        // `!sufficientBaseline`; this code instead runs the full comparison over UDP baselines an
        // on-path port-53 attacker authors — including the B058/B059 poisoned-baseline upgrade, which
        // can set `redirection = true`. Because `dohBlockedWhileUdpUnanimous` requires `!redirection`,
        // the W04 residual guard then does not fire on that input.
        //
        // What IS true: the B281 disjoint branch classifies ONLY against `dohCorroboratedPool`, never
        // `publicUnion`, so *that* path gains no new route to `redirection`. The B058/B059 path is an
        // attacker-constructible positive, not a false clean, and it is not a NEW exposure: the
        // identical path is already reachable whenever DoH is wholly absent (`dohBaselines.isEmpty`),
        // the common case on a filtered network. Behaviour is therefore left unchanged and the shape
        // is pinned by `testF2_LoneDoHWitnessHandsTheComparisonToUdpBaselines`.
        let baselines = dohBaselines.count >= 2 ? dohBaselines : udpBaselines
        let anyPublicBaseline = !baselines.isEmpty
        let sufficientBaseline = baselines.count >= 2

        // No trustworthy public baseline, or the local query itself failed → inconclusive.
        if localOutcome.didFail || !anyPublicBaseline {
            return Result(redirection: false, inconclusive: true, degraded: false)
        }

        // Single baseline — insufficient evidence for a redirection result.
        if !sufficientBaseline {
            return Result(redirection: false, inconclusive: false, degraded: true)
        }

        // A successful-but-empty local answer (NODATA/NXDOMAIN — NOT a transport failure, which is
        // already handled above via didFail) is not tampering: split-horizon DNS, geo-specific zones,
        // or a name that genuinely has no record of this type all produce it. With no local IP to
        // corroborate a block, calling this "redirection" is a fabricated positive from a non-answer
        // (B017), so it is inconclusive — a real redirection needs a blocking IP or a disjoint
        // non-empty answer.
        if localOutcome.records.isEmpty {
            return Result(redirection: false, inconclusive: true, degraded: false)
        }

        // Consensus set: the union of answers seen across public resolvers that responded.
        // CDN/geo variation shows up as *partial* overlap; tampering shows up as *no* overlap.
        // C117: filter to address records (A=1, AAAA=28) before comparing. CNAME (type 5) and
        // RRSIG (type 46) rdata are domain names / hex strings, not IPs — mixing them into the
        // comparison set produces false positives (a CNAME target like "e1.akamaiedge.net" is
        // disjoint from any IP → "redirection detected") and false negatives (two vantages
        // sharing a CNAME target but returning disjoint A records appear non-disjoint).
        let publicUnion = Set(baselines.flatMap { $0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata) })
        let localRdatas = Set(localOutcome.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))

        // WS2-B.2 (B058/B059): poisoned baseline must not suppress redirection.
        // Use union-based overlap for redirection (CDN-compatible), but flag when a single
        // overlapping resolver has a unique uncorroborated answer as degraded (potential poison).
        // C117: filter to address records for overlap checks (same rationale as publicUnion).
        let resolverOverlapCount = baselines.filter { bl in
            let blRdatas = Set(bl.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))
            return !blRdatas.isDisjoint(with: localRdatas)
        }.count

        // A local resolver returning private/loopback/blocking IPs while the public
        // resolvers return routable addresses is a strong redirection signal on its own.
        // C055: restrict the blocking test to ADDRESS records only (A=1, AAAA=28). CNAME (type 5)
        // and RRSIG (type 46) rdata are domain names / hex strings, not IPs — passing them to
        // `isBlockingAnswer` would fail inet_pton → `.invalid` → false positive "blocked".
        let localLooksBlocked = localOutcome.records
            .filter { $0.type == 1 || $0.type == 28 }
            .contains { IPClassifier.isBlockingAnswer($0.rdata) }

        var redirection: Bool
        var inconclusiveFromUnanimous = false   // C046/C056: tri-state undetermined → inconclusive
        var inconclusiveFromDivergence = false  // B281: disjoint answer not confirmable as a redirect
        if localLooksBlocked {
            redirection = true
        } else if localRdatas.isDisjoint(with: publicUnion) {
            // B281: a fully-disjoint local answer is no longer an automatic redirection. It is
            // classified by CrossVantageConsensus — the SAME shared decision function Prophet uses —
            // against the ATTACKER-INDEPENDENT DoH consensus ONLY (`dohCorroboratedPool`), never the
            // Do53/UDP `publicUnion`, which a port-53 on-path attacker authors (adversarial-review
            // finding 2: using UDP as the strength reference lets the attacker manufacture a benign
            // "same-network" reading at zero cost). A different responsible network from the DoH
            // consensus → the redirection stands; the same responsible network, a mixed family, or no
            // DoH consensus at all → we cannot confirm a redirect, so it is inconclusive — never a
            // silent clean, and never a bare accusation. This is where the owner's near-identical
            // pool answers (e.g. 140.82.113.3 vs a DoH consensus of .4, same /16) stop reading as
            // tampering. There is NO VPN branch (finding 3): a tunnel proves nothing about egress.
            let dohConsensus = CrossVantageConsensus.dohCorroboratedPool(dohOutcomes)
            switch CrossVantageConsensus.classifyDivergence(candidate: localRdatas, trustedReference: dohConsensus) {
            case .mismatch:
                redirection = true
            default:   // .inconclusive — weak / same-network / mixed-family / no attacker-independent reference
                redirection = false
                inconclusiveFromDivergence = true
            }
        } else {
            // B010: unanimous-UDP tamper is invisible to the union test (every UDP vantage, local
            // included, agrees on the tampered IP). Cross-check the HTTPS-routed DoH vantage.
            // C046/C056: tri-state — `undetermined` maps to inconclusive, never to redirection.
            let unanimousResult = detectUnanimousTampering(publicOutcomes: publicOutcomes,
                                                           dohOutcomes: dohOutcomes,
                                                           localRdatas: localRdatas)
            switch unanimousResult {
            case .tampered:
                redirection = true
            case .undetermined:
                // Insufficient evidence to confirm or deny — mark inconclusive so the
                // caller never renders a false-clean conclusion.
                redirection = false
                inconclusiveFromUnanimous = true
            case .notTampered:
                redirection = false
            }
        }

        // Degraded amplification: when redirection is NOT detected but only 1 resolver
        // overlaps with local AND that resolver's answer is unique, flag degraded — the
        // single overlapping resolver could be a poisoned baseline (B059).
        // C117: filter to address records in all overlap/corroboration checks.
        let onlyOverlapIsUnique: Bool
        if resolverOverlapCount == 1, let overlapping = baselines.first(where: { bl in
            let blRdatas = Set(bl.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))
            return !blRdatas.isDisjoint(with: localRdatas)
        }) {
            let onlyAns = Set(overlapping.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))
            let corroborators = baselines.filter { Set($0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)) == onlyAns }.count
            onlyOverlapIsUnique = corroborators < 2
        } else {
            onlyOverlapIsUnique = false
        }

        // WS2-B.2 (B058/B059): upgrade LIKELY→CONFIRMED. When the *only* thing keeping the local
        // answer inside the public union is a single, uncorroborated (i.e. suspected-poisoned)
        // baseline that echoes the local IP, but ≥2 *other* baselines unanimously agree on a
        // different destination disjoint from local, the lone echo is poison — not consensus — and
        // the local answer is a confirmed redirection. Requiring the alternative to be corroborated
        // by ≥2 agreeing resolvers keeps honest CDN/geo divergence (where the other resolvers
        // disagree with each other) at DEGRADED instead of a fabricated positive.
        if !redirection && resolverOverlapCount == 1 && onlyOverlapIsUnique {
            let nonOverlapping = baselines.filter { Set($0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)).isDisjoint(with: localRdatas) }
            let corroboratedAlternative = nonOverlapping.contains { candidate in
                let cand = Set(candidate.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata))
                return nonOverlapping.filter { Set($0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)) == cand }.count >= 2
            }
            if corroboratedAlternative {
                redirection = true
            }
        }

        let degraded = publicOutcomes.values.contains { $0.didFail || $0.records.isEmpty }
            || (!redirection && resolverOverlapCount == 1 && onlyOverlapIsUnique)

        // B010 residual: UDP unanimous + DoH cross-check below quorum → cannot distinguish "tampered
        // + attacker also suppresses DoH" from "clean + DoH provider outage". Must be inconclusive,
        // never a false-secure clean result. Only applies when redirection was NOT already
        // detected (no double-counting) and when we actually provided DoH outcomes.
        //
        // W04: the quorum floor is the SAME one `detectUnanimousTampering` enforces — at least two
        // DoH endpoints must actually return records. Testing "all DoH failed" was too narrow: a
        // single answering endpoint made that false while `detectUnanimousTampering` still bailed
        // out at its own `< 2` gate, so a run with no completed cross-transport corroboration was
        // reported as decided. One witness is below the floor, so it counts as none.
        let dohProvided = dohOutcomes != nil && !dohOutcomes!.isEmpty
        // C117: filter to address records for the B010 residual unanimity check.
        let udpSetsForCheck = publicOutcomes.values.filter { $0.hasRecords }.map { Set($0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)) }
        let udpUnanimous = udpSetsForCheck.count >= 2 && udpSetsForCheck.allSatisfy { $0 == udpSetsForCheck[0] }
        let dohAnsweringCount = dohProvided ? dohOutcomes!.values.filter { $0.hasRecords }.count : 0
        let dohBelowCorroborationFloor = dohProvided && dohAnsweringCount < 2
        let dohBlockedWhileUdpUnanimous = !redirection && udpUnanimous && dohBelowCorroborationFloor

        // C046/C056/B281: combine the inconclusive signals — the tri-state undetermined from
        // detectUnanimousTampering, the B010-residual DoH-blocked check, and B281's unconfirmable
        // disjoint divergence. `degraded` is computed above (before this line and before any early
        // return), so a timed-out UDP baseline can never be erased into a clean (finding 4).
        let isInconclusive = dohBlockedWhileUdpUnanimous || inconclusiveFromUnanimous || inconclusiveFromDivergence

        return Result(redirection: redirection,
                       inconclusive: isInconclusive,
                       degraded: degraded && !isInconclusive)
    }

    /// Detects the unanimous-tampering pattern (B010): all UDP public resolvers agree on a single
    /// answer set, but ≥2 DoH endpoints transparently return a *different* set. The DoH answers are
    /// also checked for mutual agreement so a DoH transport hiccup is not flagged as tampering.
    /// C046/C056: returns a tri-state — `undetermined` (insufficient evidence) must NEVER map to
    /// `redirection = true`. Only a confirmed pattern maps to `.tampered`.
    /// Twin of C# `VantageComparer.DetectUnanimousTampering`.
    static func detectUnanimousTampering(publicOutcomes: [String: ResolverQueryOutcome],
                                         dohOutcomes: [String: ResolverQueryOutcome]?,
                                         localRdatas: Set<String>) -> UnanimousTamperResult {
        // N2: unanimity is a question of RECORD SETS ONLY — never of RCODEs. A resolver that returns
        // SERVFAIL on a temporarily bogus zone, REFUSED from an ACL or rate limit, or NXDOMAIN/NODATA
        // carries no records, so it says nothing about *where* the name points and is excluded here.
        // The previous RCODE-set comparison graded ordinary per-resolver RCODE variation (ten NoError
        // answers with identical rdata plus one SERVFAIL and one REFUSED) as confirmed on-path
        // tampering, with no DoH cross-validation reached — the branch fired before the DoH guard
        // below. Those resolvers are not discarded: `evaluate` still reports them through `degraded`
        // (any public outcome that failed or returned no records), and `ComparisonResult` carries
        // every queried resolver with its typed RCODE. Twin of the C# repair in `VantageComparer.cs`
        // `DetectUnanimousTampering`, which filters on `Records.Count > 0` and compares no statuses.
        //
        // F1: `hasRecords` is the N2 filter and is NOT sufficient on its own — `!didFail` is a
        // SEPARATE, non-overlapping exclusion and both are required. `didFail` here (see
        // `ResolverQueryOutcome.didFail` above) covers `.timeout`, `.transportError`,
        // `.malformedResponse`, `.responseIdMismatch` and `.truncated`. The first two carry no
        // records, but the last three DO: `.responseIdMismatch` is the signature of an off-path
        // spoofer — a response whose ID does not match the query we sent, so the packet was not
        // written by the resolver we asked — and it arrives full of attacker-chosen records. Admitting
        // it lets one forged packet either BREAK unanimity (silencing a real tamper conclusion) or
        // SUPPLY it (manufacturing the second witness the `< 2` gate demands). Dropping `!didFail`
        // never helped N2 either: SERVFAIL/REFUSED/NXDOMAIN/NODATA are not in `didFail` at this
        // layer at all, so `hasRecords` alone is what excludes them, with or without this clause.
        // The C# twin filters on `Records.Count > 0` ONLY and therefore still admits the forged
        // outcomes — on this point Swift is correct and `VantageComparer.cs` needs the same repair;
        // it is deliberately NOT parity-matched downwards.
        // C117: filter to address records — CNAME/RRSIG rdata differ across resolvers even
        // when the A/AAAA answers agree, producing spurious "disagreement".
        let udpSets = publicOutcomes.values
            .filter { !$0.didFail && $0.hasRecords }
            .map { Set($0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)) }
        if udpSets.count < 2 { return .notTampered }

        // Unanimity: all UDP answer sets point to the same destination.
        let firstUdp = udpSets[0]
        if !udpSets.allSatisfy({ $0 == firstUdp }) { return .notTampered }

        // C046/C056: if DoH is unavailable, the UDP unanimity cannot be independently
        // verified. This is UNDETERMINED (evidence toward interception), never a confirmed positive.
        guard let dohOutcomes = dohOutcomes, !dohOutcomes.isEmpty else { return .undetermined }

        // DoH rdata sets from endpoints that returned records.
        // C117: filter to address records (same rationale as udpSets above).
        let dohSets = dohOutcomes.values
            .filter { $0.hasRecords }
            .map { Set($0.records.filter { $0.type == 1 || $0.type == 28 }.map(\.rdata)) }
        // C056 Fix 3: fewer than 2 DoH endpoints with records means we cannot verify the UDP
        // unanimity. This is UNDETERMINED (real signal: captive portal, enterprise TLS filter,
        // Little Snitch), never a confirmed positive.
        if dohSets.count < 2 { return .undetermined }

        // DoH must also show internal agreement (mutual consistency).
        let firstDoh = dohSets[0]
        if !dohSets.allSatisfy({ $0 == firstDoh }) { return .notTampered }

        // The UDP unanimity answer and the DoH consensus must differ.
        if firstUdp == firstDoh { return .notTampered }

        // The local answer must match the UDP tampered answer (the local resolver is also being
        // intercepted) — otherwise this is just strange network behavior, not on-path UDP tamper.
        if localRdatas != firstUdp { return .notTampered }

        return .tampered
    }
}
