import Foundation

/// C7 — Per-app resolver attribution. Joins the two facts a capture already holds but never
/// combines: which PROCESS made each query, and which RESOLVER answered it. The payoff is
/// exposing an app with a HARDCODED resolver that ignores the system's DNS settings — the shape
/// of a privacy-bypassing app (or malware) talking to its own DNS server.
///
/// **D063 coverage caveat — disclosed, never implied away:**
///  - The resolver is keyed on the PORT-53 side of the exchange, not the QR bit (the QR bit is
///    attacker-forgeable; the port is not).
///  - Capture is UDP:53 only. Encrypted DNS (DoH/DoT/DoQ) and any non-53 transport are INVISIBLE
///    here, so "no bypass observed" means "none over cleartext UDP", never "none at all". This
///    limit is surfaced in the report, not hidden.
///  - Process attribution is best-effort; an unattributable packet stays `unknown`, never guessed.
public enum ResolverAttribution {

    public struct Attribution: Codable, Sendable, Equatable {
        public let process: String
        /// Resolvers this process was observed querying (port-53 peer IPs).
        public let resolvers: [String]
        /// Resolvers this process used that are NOT in the expected system-resolver set.
        public let offSystemResolvers: [String]
        /// True when the process used a resolver outside the system set — a hardcoded-DNS shape.
        public let usesHardcodedResolver: Bool
    }

    public struct Report: Codable, Sendable, Equatable {
        public let attributions: [Attribution]
        /// Processes that used a resolver the system did not configure (the actionable subset).
        public var flagged: [Attribution] { attributions.filter { $0.usesHardcodedResolver } }
        /// True when the expected-resolver set was empty, so no bypass judgment could be made —
        /// the result is coverage-limited, never a clean bill.
        public let baselineUnknown: Bool
    }

    /// Pure core: given each process's observed resolver set and the expected system resolvers,
    /// classify per-process bypass. The mutation-tested heart of C7.
    ///
    /// - `usage`: process → the resolver IPs it was observed querying.
    /// - `systemResolvers`: the resolvers the OS is configured to use. When EMPTY, no address can
    ///   be judged "off-system", so nothing is flagged and `baselineUnknown` is set (honest: we
    ///   cannot assert a bypass without knowing the baseline).
    public static func classify(usage: [String: Set<String>],
                                systemResolvers: Set<String>) -> Report {
        let baselineUnknown = systemResolvers.isEmpty
        var attributions: [Attribution] = []
        for process in usage.keys.sorted() {
            let resolvers = usage[process] ?? []
            // With no baseline, off-system is unknowable → empty, never a guessed flag.
            let offSystem = baselineUnknown ? [] : resolvers.subtracting(systemResolvers).sorted()
            attributions.append(Attribution(
                process: process,
                resolvers: resolvers.sorted(),
                offSystemResolvers: offSystem,
                usesHardcodedResolver: !offSystem.isEmpty))
        }
        return Report(attributions: attributions, baselineUnknown: baselineUnknown)
    }

    /// Build the per-process resolver usage map from capture records. The resolver is the port-53
    /// side of each exchange (D063); an unnamed process is bucketed as "unknown".
    public static func usage(from records: [DNSCaptureRecord]) -> [String: Set<String>] {
        var usage: [String: Set<String>] = [:]
        for r in records {
            let process = r.process.name ?? "unknown"
            // Port-53 side, NOT the QR bit — the forgeable-direction guard (D063).
            let resolver = r.network.sourcePort == 53 ? r.network.sourceIP : r.network.destIP
            guard !resolver.isEmpty else { continue }
            usage[process, default: []].insert(resolver)
        }
        return usage
    }
}
