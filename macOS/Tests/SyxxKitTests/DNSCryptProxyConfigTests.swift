import XCTest
@testable import SyxxKit

/// DNSCrypt forward-transport honesty proof. Two rules that must never break:
/// (1) the toml pins static stamps with an anonymized route and fetches no remote list; (2) the classify
/// gate distinguishes relayed (full-strength) from direct (weaker) from unreachable — and NEVER dresses a
/// weaker/absent state up as the stronger one, because the whole point of this feature is anonymization.
final class DNSCryptProxyConfigTests: XCTestCase {

    private let relays = [
        DNSCryptRelay(name: "anon-scaleway", stamp: "sdns://relayA"),
        DNSCryptRelay(name: "anon-cs-nyc", stamp: "sdns://relayB"),
    ]

    private func config(relays: [DNSCryptRelay]) -> DNSCryptProxyConfig.ProxyConfig {
        DNSCryptProxyConfig.ProxyConfig(
            port: 5354, resolverName: "quad9-dnscrypt-ip4-filter-pri", resolverStamp: "sdns://resolver",
            relays: relays, queryLogPath: "/tmp/q.log")
    }

    // MARK: - Toml generation

    func testTomlPinsStaticStampsAndAnonymizedRoute() {
        let toml = DNSCryptProxyConfig.generateToml(config(relays: relays))
        XCTAssertTrue(toml.contains("listen_addresses = ['127.0.0.1:5354']"))
        XCTAssertTrue(toml.contains("dnscrypt_servers = true"))
        XCTAssertTrue(toml.contains("doh_servers = false"), "this is the DNSCrypt path, not DoH")
        XCTAssertTrue(toml.contains("[static.'quad9-dnscrypt-ip4-filter-pri']"))
        XCTAssertTrue(toml.contains("stamp = 'sdns://resolver'"))
        XCTAssertTrue(toml.contains("[static.'anon-scaleway']"))
        XCTAssertTrue(toml.contains("[anonymized_dns]"))
        XCTAssertTrue(toml.contains("via = ['anon-scaleway', 'anon-cs-nyc']"))
        XCTAssertTrue(toml.contains("skip_incompatible = true"))
    }

    // Unbound is the validation authority; the proxy must not require/duplicate DNSSEC or cache.
    func testTomlLeavesValidationAndCacheToUnbound() {
        let toml = DNSCryptProxyConfig.generateToml(config(relays: relays))
        XCTAssertTrue(toml.contains("require_dnssec = false"))
        XCTAssertTrue(toml.contains("cache = false"))
    }

    // No relays configured ⇒ no [anonymized_dns] block (direct DNSCrypt), and that is a distinct config.
    func testTomlWithoutRelaysHasNoAnonymizedBlock() {
        let toml = DNSCryptProxyConfig.generateToml(config(relays: []))
        XCTAssertFalse(toml.contains("[anonymized_dns]"))
    }

    // Fully self-contained: an empty [sources] means no remote resolver-list fetch.
    func testTomlUsesNoRemoteSources() {
        let toml = DNSCryptProxyConfig.generateToml(config(relays: relays))
        XCTAssertTrue(toml.contains("[sources]"))
        XCTAssertFalse(toml.contains("urls ="), "must not fetch a remote resolver list")
    }

    // MARK: - Honesty gate (classifyStart)

    private func classify(binary: Bool = true, started: Bool = true, reached: Bool = true,
                          viaRelay: Bool = true, relaysConfigured: Bool = true,
                          relay: String? = "anon-scaleway") -> DNSCryptProxyConfig.DNSCryptResult {
        DNSCryptProxyConfig.classifyStart(
            port: 5354, resolver: "quad9-dnscrypt-ip4-filter-pri", relay: relay,
            binaryFound: binary, processStarted: started, resolverReached: reached,
            viaRelay: viaRelay, relaysConfigured: relaysConfigured)
    }

    // The one full-strength state: reached AND via a relay.
    func testRelayedIsAnonymized() {
        let r = classify()
        XCTAssertTrue(r.isReady)
        XCTAssertTrue(r.isAnonymized)
        if case .readyAnonymized(let p, _, let relay) = r {
            XCTAssertEqual(p, 5354); XCTAssertEqual(relay, "anon-scaleway")
        } else { XCTFail("expected readyAnonymized, got \(r)") }
    }

    // Reached but NOT via a relay: ready but NOT anonymized — must never read as anonymized.
    func testDirectIsReadyButNotAnonymized() {
        let r = classify(viaRelay: false, relay: nil)
        XCTAssertTrue(r.isReady)
        XCTAssertFalse(r.isAnonymized, "direct DNSCrypt exposes the client IP; must not claim anonymization")
        if case .readyDirect = r {} else { XCTFail("expected readyDirect, got \(r)") }
    }

    // Started but no resolver reached, with relays configured: honest unreachable — NOT a silent fallback.
    func testStartedButUnreachableWithRelays() {
        let r = classify(reached: false, viaRelay: false)
        XCTAssertFalse(r.isReady)
        if case .unreachable = r {} else { XCTFail("expected unreachable, got \(r)") }
    }

    func testMissingBinaryIsNotInstalled() {
        if case .notInstalled = classify(binary: false) {} else { XCTFail("expected notInstalled") }
    }

    func testProcessFailedToStartIsFailed() {
        if case .failed = classify(started: false) {} else { XCTFail("expected failed") }
    }

    // MARK: - Catalog wiring

    func testQuad9HasDNSCryptProfileWithRelays() {
        guard let q = EncryptedDNSConfig.resolver(id: "quad9") else { return XCTFail("quad9 missing") }
        XCTAssertTrue(q.supportsDNSCrypt)
        XCTAssertEqual(q.dnscryptServerName, "quad9-dnscrypt-ip4-filter-pri")
        XCTAssertTrue(q.dnscryptStamp?.hasPrefix("sdns://") ?? false)
        XCTAssertFalse(q.dnscryptRelays.isEmpty, "Quad9 profile must offer Anonymized-DNSCrypt relays")
    }

    // Cloudflare offers no DNSCrypt profile — it must not appear as a DNSCrypt option.
    func testCloudflareHasNoDNSCryptProfile() {
        XCTAssertFalse(EncryptedDNSConfig.resolver(id: "cloudflare")?.supportsDNSCrypt ?? true)
        XCTAssertTrue(EncryptedDNSConfig.dnscryptProfiles.allSatisfy { $0.supportsDNSCrypt })
        XCTAssertEqual(EncryptedDNSConfig.defaultDNSCryptResolver?.id, "quad9")
    }
}
