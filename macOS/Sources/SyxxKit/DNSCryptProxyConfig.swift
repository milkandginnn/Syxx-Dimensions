import Foundation

/// DNSCrypt forward-transport core (the sibling of `UnboundResolver`'s forward seam, DESIGN.md:326-328).
/// Syxx runs a second local process, the real `dnscrypt-proxy` binary, on `127.0.0.1:<port>`; Unbound
/// forwards the root zone to it (see `ResolverConfig.forwardToDNSCryptPort`). `dnscrypt-proxy` encrypts
/// the query with the DNSCrypt protocol and — when the chosen resolver supports it — routes it through an
/// **Anonymized-DNSCrypt relay** so the final resolver never sees the client's real IP. Unbound still
/// DNSSEC-validates whatever comes back: dnscrypt-proxy is transport, Unbound is the validation authority.
///
/// We do NOT implement the DNSCrypt protocol natively — we shell out to `dnscrypt-proxy`, following the
/// exact pattern used for Unbound itself: require it pre-installed (`brew install dnscrypt-proxy`), locate
/// it on PATH / known Homebrew paths, and manage it as a child process. No port-53 binding is involved
/// here (Unbound is the only thing that talks to this proxy, on a private loopback port), so the
/// mDNSResponder :53 conflict never arises and no pf redirect is needed for this leg.
///
/// HONESTY SPINE (this is a Syxx feature, so the rule is absolute): the app must NEVER claim DNSCrypt is
/// protecting the user on the strength of a process having spawned. A proxy being *launched* is not proof
/// it *reached a resolver*, and "reached a resolver directly" is NOT the same as "reached it through a
/// relay." `classifyStart` below distinguishes every one of those states. Critically, the feature must
/// NEVER silently fall back to plain recursion (or to non-anonymized direct DNSCrypt) without saying so.
public enum DNSCryptProxyConfig {

    // MARK: - Honest tri-state (never a bare bool)

    /// The outcome of trying to bring the local `dnscrypt-proxy` up as Unbound's forward transport. Every
    /// failure mode — and the two DISTINCT success-ish modes (relayed vs. direct) — is its own case so the
    /// UI can say exactly what is true and never overstate the protection.
    public enum DNSCryptResult: Sendable, Equatable {
        /// The proxy is up AND reached its resolver THROUGH an Anonymized-DNSCrypt relay. Full-strength.
        case readyAnonymized(port: UInt16, resolver: String, relay: String)
        /// The proxy is up and reached its resolver over DNSCrypt, but WITHOUT a relay (no configured relay
        /// could reach it, or none was configured). Still encrypted, but the resolver sees the client IP —
        /// a materially weaker state that must be surfaced, never dressed up as anonymized.
        case readyDirect(port: UInt16, resolver: String)
        /// The `dnscrypt-proxy` binary could not be located; nothing was started. `hint` names how to install.
        case notInstalled(hint: String)
        /// The process launched but could not reach ANY configured resolver/relay before timing out. DNS is
        /// NOT silently degraded — the caller must refuse to enable rather than fall back to plain recursion.
        case unreachable(reason: String)
        /// A step failed (config write, launch, crash) and nothing usable is running.
        case failed(reason: String)
        /// The process is up but its effect could not be confirmed yet (readiness probe inconclusive).
        case inconclusive(reason: String)

        /// True only when a resolver was actually reached over DNSCrypt (relayed or direct). A caller that
        /// requires anonymization must check `isAnonymized`, not this.
        public var isReady: Bool {
            switch self { case .readyAnonymized, .readyDirect: return true; default: return false }
        }
        /// True ONLY for the full-strength relayed state.
        public var isAnonymized: Bool { if case .readyAnonymized = self { return true }; return false }
        /// The loopback port Unbound should forward to, when ready.
        public var readyPort: UInt16? {
            switch self {
            case .readyAnonymized(let p, _, _), .readyDirect(let p, _): return p
            default: return nil
            }
        }
    }

    // MARK: - Honesty gate (pure, exhaustive, testable)

    /// Turns the observations of a start attempt into the honest result. Kept pure so every state is
    /// unit-testable without spawning a process or touching the network.
    ///
    /// - binaryFound: the `dnscrypt-proxy` executable was located on disk.
    /// - processStarted: the process launched and is still alive.
    /// - resolverReached: a real query completed through the proxy (proving it reached a DNSCrypt resolver).
    /// - viaRelay: that query was observed to have transited an Anonymized-DNSCrypt relay.
    /// - relaysConfigured: at least one relay was configured (so "not via relay" is a downgrade to report,
    ///   not merely "no relay was asked for").
    public static func classifyStart(port: UInt16,
                                     resolver: String,
                                     relay: String?,
                                     binaryFound: Bool,
                                     processStarted: Bool,
                                     resolverReached: Bool,
                                     viaRelay: Bool,
                                     relaysConfigured: Bool,
                                     failureReason: String? = nil) -> DNSCryptResult {
        if !binaryFound { return .notInstalled(hint: installHint) }
        if !processStarted { return .failed(reason: failureReason ?? "the dnscrypt-proxy process failed to start") }
        // Process is up. But launched ≠ reaching a resolver: demand a real completed query.
        if !resolverReached {
            if relaysConfigured {
                // Relays were configured but nothing came back — an honest "unreachable," NOT a fallback.
                return .unreachable(reason: failureReason ?? "dnscrypt-proxy started but could not reach the resolver \(resolver) through any configured relay")
            }
            return .unreachable(reason: failureReason ?? "dnscrypt-proxy started but could not reach the resolver \(resolver)")
        }
        // A resolver WAS reached. Relayed is full-strength; direct is a real but weaker state we must name.
        if viaRelay, let relay = relay {
            return .readyAnonymized(port: port, resolver: resolver, relay: relay)
        }
        return .readyDirect(port: port, resolver: resolver)
    }

    public static let installHint = "dnscrypt-proxy is not installed. Install it with: brew install dnscrypt-proxy"

    // MARK: - Config generation (pure, testable)

    /// Options for the generated `dnscrypt-proxy.toml`. Minimal and self-contained: it pins the resolver
    /// and relay `sdns://` stamps as `[static]` servers and configures no remote `[sources]`, so the proxy
    /// never fetches (or depends on) a remote resolver list — the config is deterministic and reproducible.
    public struct ProxyConfig: Sendable, Equatable {
        public var listenAddress: String
        public var port: UInt16
        public var resolverName: String
        public var resolverStamp: String
        public var relays: [DNSCryptRelay]
        /// Where dnscrypt-proxy writes its per-query log (used as one readiness signal + relay proof).
        public var queryLogPath: String
        /// Plain-DNS bootstrap resolvers used only to fetch the resolver's DNSCrypt certificate at startup.
        public var bootstrapResolvers: [String]

        public init(listenAddress: String = "127.0.0.1",
                    port: UInt16,
                    resolverName: String,
                    resolverStamp: String,
                    relays: [DNSCryptRelay],
                    queryLogPath: String,
                    bootstrapResolvers: [String] = ["9.9.9.9:53", "1.1.1.1:53"]) {
            self.listenAddress = listenAddress
            self.port = port
            self.resolverName = resolverName
            self.resolverStamp = resolverStamp
            self.relays = relays
            self.queryLogPath = queryLogPath
            self.bootstrapResolvers = bootstrapResolvers
        }
    }

    /// Renders a minimal `dnscrypt-proxy.toml`. Pure, so it is exhaustively testable without spawning the
    /// proxy. Verified by real run (2026-07-21): this shape brings Quad9 up over an Anonymized-DNSCrypt
    /// relay and Unbound validates DNSSEC on top of it.
    public static func generateToml(_ c: ProxyConfig) -> String {
        var lines: [String] = []
        lines.append("# Syxx-generated dnscrypt-proxy config — do not hand-edit; regenerated on enable.")
        lines.append("# DNSCrypt forward transport for the local Unbound resolver. Static stamps only.")
        lines.append("listen_addresses = ['\(c.listenAddress):\(c.port)']")
        lines.append("max_clients = 128")
        lines.append("ipv4_servers = true")
        lines.append("ipv6_servers = false")
        lines.append("dnscrypt_servers = true")
        lines.append("doh_servers = false")
        lines.append("odoh_servers = false")
        // Unbound is the DNSSEC-validation authority and the cache; dnscrypt-proxy is transport only.
        lines.append("require_dnssec = false")
        lines.append("require_nolog = true")
        lines.append("require_nofilter = false")
        lines.append("force_tcp = false")
        lines.append("timeout = 5000")
        lines.append("keepalive = 30")
        lines.append("cert_refresh_delay = 240")
        lines.append("bootstrap_resolvers = [\(c.bootstrapResolvers.map { "'\($0)'" }.joined(separator: ", "))]")
        lines.append("ignore_system_dns = true")
        lines.append("netprobe_timeout = 60")
        lines.append("netprobe_address = '\(c.bootstrapResolvers.first ?? "9.9.9.9:53")'")
        lines.append("block_ipv6 = false")
        lines.append("cache = false")
        lines.append("server_names = ['\(c.resolverName)']")
        lines.append("")
        lines.append("[static]")
        lines.append("  [static.'\(c.resolverName)']")
        lines.append("    stamp = '\(c.resolverStamp)'")
        for relay in c.relays {
            lines.append("  [static.'\(relay.name)']")
            lines.append("    stamp = '\(relay.stamp)'")
        }
        if !c.relays.isEmpty {
            lines.append("")
            lines.append("[anonymized_dns]")
            let via = c.relays.map { "'\($0.name)'" }.joined(separator: ", ")
            lines.append("  routes = [")
            lines.append("    { server_name = '\(c.resolverName)', via = [\(via)] },")
            lines.append("  ]")
            // Let dnscrypt-proxy skip a relay that cannot reach this resolver rather than hard-failing.
            lines.append("  skip_incompatible = true")
        }
        lines.append("")
        lines.append("[query_log]")
        lines.append("  file = '\(c.queryLogPath)'")
        lines.append("")
        // No [sources] entries ⇒ no remote resolver-list fetch: the static stamps above are the whole world.
        lines.append("[sources]")
        return lines.joined(separator: "\n") + "\n"
    }

    // MARK: - Binary location (tri-state via optional; nil = not installed)

    /// Locates the `dnscrypt-proxy` executable. Same policy as Unbound: REQUIRE it pre-installed via
    /// Homebrew rather than vendoring, so an absent binary is an honest `notInstalled`, never a stale bundle.
    public static func locateBinary() -> String? {
        let name = "dnscrypt-proxy"
        let candidates = [
            "/opt/homebrew/sbin/\(name)",   // Apple-silicon Homebrew (dnscrypt-proxy installs to sbin)
            "/opt/homebrew/bin/\(name)",
            "/usr/local/sbin/\(name)",      // Intel Homebrew
            "/usr/local/bin/\(name)",
            "/opt/local/sbin/\(name)",      // MacPorts
            "/usr/sbin/\(name)",
            "/usr/bin/\(name)"
        ]
        for path in candidates where FileManager.default.isExecutableFile(atPath: path) {
            return path
        }
        if let out = NetworkKeyState.runProcess("/usr/bin/which", [name], timeout: 2.0)?
            .trimmingCharacters(in: .whitespacesAndNewlines), !out.isEmpty,
            FileManager.default.isExecutableFile(atPath: out) {
            return out
        }
        return nil
    }
}
