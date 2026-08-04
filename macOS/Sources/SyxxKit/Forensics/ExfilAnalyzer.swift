// Derivative datapoint X6: DNS exfiltration / tunneling detection.
//
// No single signal proves tunneling. This fuses three that the capture pipeline already
// produces — high-entropy subdomain labels (encoded payload), a regular query cadence
// (automated beaconing), and blocklist category — into one honest tri-state result.
// It never claims "clean": absence of signal is `.benign`, genuine ambiguity is `.inconclusive`.
import Foundation

public enum ExfilSuspicion: String, Codable, Sendable {
    case inconclusive = "Inconclusive"
    case benign = "Benign"
    case suspicious = "Suspicious"
    case likelyExfil = "Likely DNS exfiltration / tunneling"
}

public struct ExfilAssessment: Sendable {
    public let domain: String
    public let longestLabel: String
    public let labelLength: Int
    public let entropyBitsPerChar: Double
    public let beacon: BeaconClassification
    /// B190: tri-state for the beacon leg. `false` means the cadence signal was NEVER evaluated
    /// (no per-domain timing data was supplied), which is distinct from an evaluated negative
    /// ("no beacon observed"). When this is `false` the `beacon` field carries `.insufficientData`
    /// as a placeholder and MUST NOT be read as an evaluated negative: a beaconing-only channel
    /// cannot be ruled out. The fusion below treats an unevaluated beacon as an absent signal
    /// (not a positive, not an evaluated negative), so the maximum achievable signal count is 2.
    public let beaconEvaluated: Bool
    public let flaggedByLists: [String]
    /// C082: tri-state for the blocklist leg. `false` when the lists used are hardcoded stubs
    /// (~50 domains) rather than real blocklists (millions of entries). When this is `false`,
    /// a "no match" result is not an evaluated negative — the coverage was insufficient to
    /// rule out blocklist-flagged domains. Analogous to `beaconEvaluated` (B190).
    public let listsEvaluated: Bool
    public let suspicion: ExfilSuspicion
    public let rationale: [String]
}

public enum ExfilAnalyzer {
    /// Shannon entropy in bits per character. Random base32/64 encodings approach 5–6;
    /// natural-language domains sit well below.
    public static func shannonEntropyBitsPerChar(_ s: String) -> Double {
        guard !s.isEmpty else { return 0 }
        var counts: [Character: Int] = [:]
        for c in s { counts[c, default: 0] += 1 }
        let n = Double(s.count)
        var h = 0.0
        for (_, count) in counts {
            let p = Double(count) / n
            h -= p * log2(p)
        }
        return h
    }

    /// The longest label that is *not* part of the registrable domain,
    /// i.e. the subdomain space where tunneled data would be encoded.
    /// C036: use the public-suffix-aware `registrableDomain` instead of a hard
    /// `dropLast(2)`, which misclassifies the registrant label of multi-label
    /// suffixes (e.g. `co.uk`, `com.au`) as tunneled payload.
    public static func longestSubdomainLabel(_ domain: String) -> String {
        let labels = domain.lowercased().split(separator: ".").map(String.init)
        guard labels.count >= 2 else { return "" }
        // Determine how many trailing labels form the registrable domain:
        // 3 labels when the last two are a known two-level public suffix, else 2.
        let registrableCount: Int
        if labels.count >= 3 {
            let lastTwo = labels.suffix(2).joined(separator: ".")
            registrableCount = DNSCaptureReportGenerator.twoLevelPublicSuffixes.contains(lastTwo) ? 3 : 2
        } else {
            registrableCount = 2
        }
        guard labels.count > registrableCount else { return "" }
        let subLabels = labels.dropLast(registrableCount)
        return subLabels.max(by: { $0.count < $1.count }) ?? ""
    }

    public static func assess(domain: String,
                              timestamps: [Date] = [],
                              lists: [NamedDomainList] = SieveBlocklistLoader.bundledLists(),
                              listsEvaluated: Bool = false) -> ExfilAssessment {
        let trimmed = domain.trimmingCharacters(in: .whitespacesAndNewlines)
        let longest = longestSubdomainLabel(trimmed)
        let entropy = shannonEntropyBitsPerChar(longest)
        // B190: distinguish "beacon leg never evaluated" (no timing data) from an evaluated negative.
        // With no timestamps we do not even attempt cadence scoring — reporting `.insufficientData`
        // here would masquerade as "evaluated, no beacon"; the `beaconEvaluated` flag keeps the
        // honest tri-state (unknown) intact for downstream consumers.
        let beaconEvaluated = !timestamps.isEmpty
        let beacon: BeaconClassification = beaconEvaluated
            ? BeaconAnalyzer.score(BeaconSeries(processName: "", destination: trimmed, timestamps: timestamps)).classification
            : .insufficientData
        // L3 — the beacon leg only produced a CONCLUSIVE reading when it was evaluated AND scoring
        // succeeded (not `.insufficientData`: <3 timing samples or a degenerate/zero-mean series). Only a
        // conclusive leg may back the "no beaconing signal" clean claim below.
        let beaconConclusive = beaconEvaluated && beacon != .insufficientData
        let flaggedBy = lists.filter { $0.matches(trimmed) }.map(\.name).sorted()

        let highEntropy = longest.count >= 20 && entropy >= 3.5
        // An unevaluated beacon can never be a positive `regular` signal (missing data is not a finding).
        let regular = beaconEvaluated && (beacon == .highlyRegular || beacon == .regular)

        var rationale: [String] = []
        if highEntropy {
            rationale.append(String(format: "high-entropy %d-char label (%.2f bits/char) — consistent with encoded data", longest.count, entropy))
        }
        if regular {
            rationale.append("regular query cadence (\(beacon.rawValue.lowercased())) — consistent with automated beaconing")
        }
        if !beaconEvaluated {
            rationale.append("beacon cadence not evaluated (no per-domain timing data)")
        } else if !beaconConclusive {
            // L3 — timing data existed but was too sparse/degenerate to score; disclose factually rather
            // than letting the benign line claim "no beaconing signal" over an inconclusive beacon leg.
            rationale.append("beacon cadence not evaluated (insufficient timing samples)")
        }
        if !flaggedBy.isEmpty {
            rationale.append("registrable domain flagged by: \(flaggedBy.joined(separator: ", "))")
        }
        // C082: when the blocklist leg uses stub lists, disclose the coverage gap so a
        // "no match" is not read as an evaluated negative against real blocklists.
        if !listsEvaluated && flaggedBy.isEmpty {
            rationale.append("blocklist coverage: built-in stub lists only (~50 domains each) — load external lists for full coverage")
        }

        // C009: require at least two of {highEntropy, regular, flaggedBy} for suspicious.
        // A single signal (entropy-only or beacon-only) is insufficient and causes false positives.
        // B190: when the beacon leg is unevaluated, `regular` is forced false above, so the maximum
        // achievable signalCount is 2 of 3 — `.likelyExfil` (>= 3) is honestly unreachable rather
        // than the leg silently contributing an evaluated negative.
        let signalCount = [highEntropy, regular, !flaggedBy.isEmpty].filter { $0 }.count

        let suspicion: ExfilSuspicion
        if trimmed.isEmpty {
            suspicion = .inconclusive
        } else if signalCount >= 3 {
            suspicion = .likelyExfil
        } else if signalCount >= 2 {
            suspicion = .suspicious
        } else if signalCount == 1 {
            // C045: one real signal is inconclusive/degraded, never benign. The actual signal
            // is retained in rationale (already appended above) so it is not erased by a false
            // "no encoded-label or beaconing signal" clean claim.
            suspicion = .inconclusive
            rationale.append("single signal detected — insufficient for a confident conclusion")
        } else {
            suspicion = .benign
            // Do not claim the beacon leg saw nothing when it was never CONCLUSIVELY checked (unevaluated,
            // or evaluated but insufficient/degenerate). L3: the "or beaconing" clause requires a
            // conclusive beacon leg; otherwise the clean claim covers only the encoded-label check.
            rationale.append(beaconConclusive ? "no encoded-label or beaconing signal" : "no encoded-label signal")
        }

        return ExfilAssessment(
            domain: trimmed,
            longestLabel: longest,
            labelLength: longest.count,
            entropyBitsPerChar: entropy,
            beacon: beacon,
            beaconEvaluated: beaconEvaluated,
            flaggedByLists: flaggedBy,
            listsEvaluated: listsEvaluated,
            suspicion: suspicion,
            rationale: rationale
        )
    }
}
