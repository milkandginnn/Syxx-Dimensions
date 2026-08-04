import Foundation

/// Encrypt-all-DNS: the portable, testable core of the protection feature. Syxx configures the
/// SYSTEM to send every DNS query over an encrypted transport (DoH/DoT) using the OS's own
/// programmatic API — no downloaded profile, no local proxy. This file holds everything that is
/// platform-neutral and unit-testable; the macOS `NEDNSSettingsManager` glue lives in the app target
/// so the NetworkExtension framework never contaminates the cross-platform Kit or the CLI.
///
/// HONESTY SPINE (this is a Syxx feature, so the rule is absolute): the app must NEVER tell the user
/// "your DNS is encrypted" on the strength of a save call returning without error. A configuration
/// being *installed* is not proof it is *in effect*. `classifyApply` below only yields `.enabled`
/// when the save succeeded AND the system reports the setting active AND an encrypted query actually
/// completed. Anything short of all three is `.inconclusive` — never a reassuring green.

/// An Anonymized-DNSCrypt relay: a `dnscrypt-proxy` "static" server whose `sdns://` stamp (protocol
/// byte `0x81`) describes a relay, not a resolver. When a resolver is routed *via* one of these, the
/// final resolver never sees the client's real IP — the relay forwards the already-encrypted DNSCrypt
/// packet on the client's behalf. This is the specific property that makes DNSCrypt-with-relay a
/// stronger transport than DoT/DoH alone (which still expose the client IP to the resolver).
public struct DNSCryptRelay: Codable, Sendable, Equatable {
    public let name: String     // dnscrypt-proxy static server name, e.g. "anon-scaleway"
    public let stamp: String    // sdns:// relay stamp
    public init(name: String, stamp: String) { self.name = name; self.stamp = stamp }
}

/// A well-known encrypted resolver Syxx can point the system at. The audit path stays cleartext and
/// separate; this is the protection path only.
///
/// The DoH/DoT fields describe the OS-native "encrypt-all-DNS" path (`NEDNSSettingsManager`). The
/// optional DNSCrypt fields describe the *forward-transport* path: `dnscrypt-proxy` speaks the DNSCrypt
/// protocol to this resolver and Unbound forwards to it (DESIGN.md:322-325 layer-2 resolver profile,
/// extended with a `dnscrypt` transport). A resolver that does not offer DNSCrypt simply leaves
/// `dnscryptStamp` nil and does not appear as a DNSCrypt option.
public struct EncryptedResolver: Codable, Sendable, Equatable, Identifiable {
    public let id: String            // stable key, e.g. "cloudflare"
    public let displayName: String   // user-facing, e.g. "Cloudflare (1.1.1.1)"
    public let dohURL: String?       // DoH template, e.g. "https://cloudflare-dns.com/dns-query"
    public let dotHost: String?      // DoT server name, e.g. "one.one.one.one"
    public let addresses: [String]   // server IPs (v4+v6), used as NEDNSSettings serverAddresses

    // MARK: DNSCrypt forward-transport (optional; nil ⇒ resolver offers no DNSCrypt profile)
    /// The `dnscrypt-proxy` static server name for this resolver, e.g. "quad9-dnscrypt-ip4-filter-pri".
    public let dnscryptServerName: String?
    /// The resolver's `sdns://` DNSCrypt stamp (protocol byte `0x01`). Long-lived (encodes the
    /// provider's long-term public key + address), so pinning it is safe; source cited in the catalog.
    public let dnscryptStamp: String?
    /// Anonymized-DNSCrypt relays to route this resolver through, in preference order. `dnscrypt-proxy`
    /// picks the first that can actually reach the resolver and skips incompatible ones. Empty ⇒ the
    /// resolver is reached directly over DNSCrypt (still encrypted, but the resolver sees the client IP).
    public let dnscryptRelays: [DNSCryptRelay]

    public var supportsDNSCrypt: Bool { dnscryptStamp != nil && dnscryptServerName != nil }

    public init(id: String, displayName: String, dohURL: String? = nil, dotHost: String? = nil, addresses: [String],
                dnscryptServerName: String? = nil, dnscryptStamp: String? = nil,
                dnscryptRelays: [DNSCryptRelay] = []) {
        self.id = id; self.displayName = displayName; self.dohURL = dohURL
        self.dotHost = dotHost; self.addresses = addresses
        self.dnscryptServerName = dnscryptServerName; self.dnscryptStamp = dnscryptStamp
        self.dnscryptRelays = dnscryptRelays
    }
}

public enum EncryptedDNSTransport: String, Codable, Sendable, CaseIterable {
    /// DoH (443) is the default: it is the hardest for an on-path device to block or single out,
    /// which is the whole point of "encrypt all queries" on a hostile network.
    case doh
    case dot
    /// DNSCrypt via a local `dnscrypt-proxy`, preferring an Anonymized-DNSCrypt relay. This is NOT an
    /// `NEDNSSettingsManager` transport (the OS supports only DoH/DoT there); it is realised by the
    /// local-resolver path — `dnscrypt-proxy` fronted by Unbound. See `DNSCryptProxyConfig`.
    case dnscrypt
}

/// The result of trying to turn encryption on. A tri-state, never a bool — a save that "worked" but
/// could not be confirmed active is NOT success.
public enum EncryptedDNSApplyResult: Codable, Sendable, Equatable {
    /// Save succeeded, the system reports the setting active, AND an encrypted query completed.
    case enabled(resolverID: String, transport: EncryptedDNSTransport)
    /// The OS rejected the configuration because the app is not signed with the DNS-Settings
    /// entitlement. Distinct so the UI can say exactly what is needed, not a generic failure.
    case entitlementRequired
    /// Encryption was successfully disabled and removed from system preferences.
    case disabled
    /// The save or a precondition failed; DNS was left as it was (the failsafe held).
    case failed(reason: String)
    /// Installed but could NOT be confirmed in effect (e.g. the verify probe did not complete).
    /// Honest resting state: we do not claim encryption we did not observe.
    case inconclusive(reason: String)

    public var isEnabled: Bool { if case .enabled = self { return true }; return false }
}

public enum EncryptedDNSConfig {

    /// The curated catalog. All are public, no-log-policy resolvers with published DoH+DoT
    /// endpoints. The DEFAULT is Cloudflare for widest compatibility and lowest latency; a user may
    /// pick another or supply a custom one. (Choosing which resolver to trust is a real decision and
    /// belongs to the user — Syxx does not silently pin one.)
    public static let catalog: [EncryptedResolver] = [
        EncryptedResolver(id: "cloudflare", displayName: "Cloudflare (1.1.1.1)",
                          dohURL: "https://cloudflare-dns.com/dns-query", dotHost: "one.one.one.one",
                          addresses: ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"]),
        // Quad9 is the DNSCrypt profile: it runs a DNSCrypt v2 resolver that supports Anonymized DNSCrypt,
        // does its own DNSSEC validation, and has a published no-client-IP-logging policy. The DNSCrypt
        // resolver stamp and the relay stamps are pinned from the DNSCrypt project's minisign-signed lists
        // (v3/public-resolvers.md and v3/relays.md, fetched 2026-07-21). Stamps encode long-term provider
        // identity/address, so pinning is stable. Multiple relays are listed in preference order because a
        // given relay may not be able to reach a given resolver (verified live: anon-scaleway failed the
        // initial handshake to Quad9 and dnscrypt-proxy transparently fell through to anon-cs-nyc).
        EncryptedResolver(id: "quad9", displayName: "Quad9 (9.9.9.9)",
                          dohURL: "https://dns.quad9.net/dns-query", dotHost: "dns.quad9.net",
                          addresses: ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"],
                          dnscryptServerName: "quad9-dnscrypt-ip4-filter-pri",
                          dnscryptStamp: "sdns://AQMAAAAAAAAADDkuOS45Ljk6ODQ0MyBnyEe4yHWM0SAkVUO-dWdG3zTfHYTAC4xHA2jfgh2GPhkyLmRuc2NyeXB0LWNlcnQucXVhZDkubmV0",
                          dnscryptRelays: [
                              DNSCryptRelay(name: "anon-scaleway", stamp: "sdns://gRIyMTIuNDcuMjI4LjEzNjo0NDM"),
                              DNSCryptRelay(name: "anon-cs-nyc", stamp: "sdns://gRExNDYuNzAuMTU0LjY3OjQ0Mw"),
                              DNSCryptRelay(name: "anon-cs-la", stamp: "sdns://gRMxOTUuMjA2LjEwNC4yMDM6NDQz"),
                          ]),
        EncryptedResolver(id: "google", displayName: "Google Public DNS (8.8.8.8)",
                          dohURL: "https://dns.google/dns-query", dotHost: "dns.google",
                          addresses: ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"]),
        EncryptedResolver(id: "opendns", displayName: "Cisco OpenDNS (208.67.222.222)",
                          dohURL: "https://doh.opendns.com/dns-query", dotHost: "doh.opendns.com",
                          addresses: ["208.67.222.222", "208.67.220.220"]),
        EncryptedResolver(id: "adguard", displayName: "AdGuard DNS (94.140.14.14)",
                          dohURL: "https://dns.adguard.com/dns-query", dotHost: "dns.adguard.com",
                          addresses: ["94.140.14.14", "94.140.15.15"]),
        EncryptedResolver(id: "nextdns", displayName: "NextDNS (45.90.28.0)",
                          dohURL: "https://dns.nextdns.io/dns-query", dotHost: "dns.nextdns.io",
                          addresses: ["45.90.28.0", "45.90.30.0"]),
        EncryptedResolver(id: "controld", displayName: "Control D (76.76.2.0)",
                          dohURL: "https://freedns.controld.com/p0", dotHost: "p0.freedns.controld.com",
                          addresses: ["76.76.2.0", "76.76.10.0"]),
        EncryptedResolver(id: "comodo", displayName: "Comodo Secure (8.26.56.26)",
                          dohURL: nil, dotHost: nil,
                          addresses: ["8.26.56.26", "8.20.247.20"]),
        EncryptedResolver(id: "level3", displayName: "Level3 / GigaNet (4.2.2.1)",
                          dohURL: nil, dotHost: nil,
                          addresses: ["4.2.2.1", "4.2.2.2"]),
        EncryptedResolver(id: "verisign", displayName: "Verisign Public DNS (64.6.64.6)",
                          dohURL: nil, dotHost: nil,
                          addresses: ["64.6.64.6", "64.6.65.6"]),
        EncryptedResolver(id: "dnswatch", displayName: "DNS.WATCH (84.200.69.80)",
                          dohURL: nil, dotHost: nil,
                          addresses: ["84.200.69.80", "84.200.70.40"]),
    ]

    public static let defaultResolverID = "cloudflare"

    public static func resolver(id: String) -> EncryptedResolver? {
        catalog.first { $0.id == id }
    }

    /// Catalog entries that actually offer a DNSCrypt forward profile (have a pinned resolver stamp).
    public static var dnscryptProfiles: [EncryptedResolver] { catalog.filter { $0.supportsDNSCrypt } }

    /// The default DNSCrypt resolver profile (Quad9), or nil if none is pinned.
    public static var defaultDNSCryptResolver: EncryptedResolver? { dnscryptProfiles.first }

    /// The honesty gate. Turns the three observations into a result; only all-three-true is
    /// `.enabled`. Kept pure so it is exhaustively testable without touching the system.
    ///
    /// - `saveSucceeded`: `NEDNSSettingsManager.saveToPreferences` returned no error.
    /// - `entitlementRejected`: the save error was the missing-entitlement error specifically.
    /// - `systemReportsActive`: after reload, the manager is enabled and on-path for our config.
    /// - `encryptedProbeCompleted`: a real query completed over the encrypted transport afterward.
    public static func classifyApply(resolverID: String,
                                     transport: EncryptedDNSTransport,
                                     saveSucceeded: Bool,
                                     entitlementRejected: Bool,
                                     systemReportsActive: Bool,
                                     encryptedProbeCompleted: Bool,
                                     failureReason: String? = nil) -> EncryptedDNSApplyResult {
        if entitlementRejected { return .entitlementRequired }
        if !saveSucceeded { return .failed(reason: failureReason ?? "the system rejected the configuration") }
        // Save landed. But installed ≠ in effect: demand BOTH the system's active flag AND a real
        // encrypted query before claiming success.
        if systemReportsActive && encryptedProbeCompleted {
            return .enabled(resolverID: resolverID, transport: transport)
        }
        if !systemReportsActive {
            return .inconclusive(reason: "the configuration saved but the system does not report it active yet")
        }
        return .inconclusive(reason: "the configuration is active but no encrypted query has completed to confirm it")
    }
}
