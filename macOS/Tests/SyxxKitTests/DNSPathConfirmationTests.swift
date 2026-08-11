import XCTest
@testable import SyxxKit

/// B019: the definitive-tier DNS path confirmation engine (`DNSPathConfirmation.swift`, 318 lines)
/// previously had ZERO tests. This file exercises every tri-state combination, flavor
/// disambiguation (DoH vs DoT vs DNSCrypt vs plaintext vs unknown), the honesty-spine invariants
/// (VPN ≠ encrypted DNS; unknown ≠ plaintext), and the JSON round-trip used over XPC.
///
/// Every test is written so it turns RED if the corresponding honesty rule is removed — the same
/// convention as `DNSTransportDeterminationTests`.
final class DNSPathConfirmationTests: XCTestCase {

    private let fingerprint = "en0|1.1.1.1"
    private let observedAt = Date(timeIntervalSince1970: 1_700_000_000)

    // MARK: - Snapshot builders

    private func snapshot(vpn: ConditionState,
                          encryptedDNS: ConditionState,
                          configuredTransport: DNSProtocol? = nil,
                          systemServers: [String] = ["1.1.1.1"],
                          scopedResolvers: [ScopedResolver] = [],
                          dnsCryptRunning: Bool = false,
                          dnsCryptServerName: String? = nil) -> DNSPathSnapshot {
        DNSPathSnapshot(
            vpn: VPNStatus(state: vpn, tunnelInterfaces: vpn == .yes ? ["utun4"] : [], evidence: "test"),
            encryptedDNS: EncryptedDNSStatus(state: encryptedDNS, evidence: "test",
                                             configuredTransport: configuredTransport),
            systemServers: systemServers,
            scopedResolvers: scopedResolvers,
            dnsCryptRunning: dnsCryptRunning,
            dnsCryptServerName: dnsCryptServerName,
            observedAt: observedAt,
            observedAtMonotonicNanos: 1_000,
            networkFingerprint: fingerprint)
    }

    private func scopedResolver(_ address: String, protocol p: DNSProtocol) -> ScopedResolver {
        ScopedResolver(address: address, interface: "en0", matchDomains: [],
                       isScoped: false, dnsProtocol: p, port: 853, isReachable: true)
    }

    // MARK: - VPN leg tri-state

    /// FAILURE GUARDED: a VPN being up is NOT evidence that DNS is encrypted (honesty spine,
    /// DNSPathConfirmation.swift:26-27). The VPN leg must be confirmedTrue but the encrypted-DNS
    /// leg must remain independent.
    func testVPNUp_doesNotImplyEncryptedDNS() {
        let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: .yes, encryptedDNS: .unknown))
        XCTAssertEqual(c.vpn, .confirmedTrue)
        XCTAssertEqual(c.vpnReason, .tunnelInterfaceActive)
        // The encrypted-DNS leg is NOT flipped by the VPN leg.
        XCTAssertEqual(c.encryptedDNS, .undetermined)
        XCTAssertEqual(c.encryptedDNSFlavor, .unknown)
    }

    /// FAILURE GUARDED: no tunnel interface → confirmedFalse, never undetermined.
    func testVPNNo_isConfirmedFalse() {
        let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: .no, encryptedDNS: .no))
        XCTAssertEqual(c.vpn, .confirmedFalse)
        XCTAssertEqual(c.vpnReason, .noTunnelInterfaceActive)
    }

    /// FAILURE GUARDED: enumeration failure (C091) must be undetermined — the absence of a tunnel
    /// may be an artefact and must never read as "no VPN".
    func testVPNUnknown_isUndetermined_notConfirmedFalse() {
        let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: .unknown, encryptedDNS: .no))
        XCTAssertEqual(c.vpn, .undetermined)
        XCTAssertEqual(c.vpnReason, .vpnEnumerationFailed)
        XCTAssertNotEqual(c.vpn, .confirmedFalse)
    }

    // MARK: - Encrypted-DNS leg tri-state

    /// FAILURE GUARDED: every resolver configured for DoH → confirmedTrue with flavor .doh.
    func testEncryptedYes_DoH_flavorNamed() {
        let resolvers = [scopedResolver("1.1.1.1", protocol: .doh)]
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .yes, configuredTransport: .doh,
                               scopedResolvers: resolvers))
        XCTAssertEqual(c.encryptedDNS, .confirmedTrue)
        XCTAssertEqual(c.encryptedDNSReason, .encryptedDNSConfiguredConfirmed)
        XCTAssertEqual(c.encryptedDNSFlavor, .doh)
    }

    /// FAILURE GUARDED: every resolver configured for DoT → flavor .dot, never .doh.
    func testEncryptedYes_DoT_flavorNamed() {
        let resolvers = [scopedResolver("1.1.1.1", protocol: .dot)]
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .yes, configuredTransport: .dot,
                               scopedResolvers: resolvers))
        XCTAssertEqual(c.encryptedDNS, .confirmedTrue)
        XCTAssertEqual(c.encryptedDNSFlavor, .dot)
    }

    /// FAILURE GUARDED: `.yes` with no named DoH/DoT transport but the DNSCrypt proxy up → the
    /// flavor is DNSCrypt (process-confirmed), never guessed as DoT (PR-06/D279 regression).
    func testEncryptedYes_noNamedTransport_dnscryptRunning_flavorIsDNSCrypt() {
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .yes, configuredTransport: nil,
                               dnsCryptRunning: true, dnsCryptServerName: "2.dnscrypt-cert.example"))
        XCTAssertEqual(c.encryptedDNS, .confirmedTrue)
        XCTAssertEqual(c.encryptedDNSFlavor, .dnscrypt)
        XCTAssertNotEqual(c.encryptedDNSFlavor, .dot)
    }

    /// FAILURE GUARDED: `.yes` but no flavor could be named → signal affirmative, flavor .unknown.
    /// Never invent a protocol (PR-06/D279).
    func testEncryptedYes_noFlavorNamed_flavorIsUnknown() {
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .yes, configuredTransport: nil,
                               dnsCryptRunning: false))
        XCTAssertEqual(c.encryptedDNS, .confirmedTrue)
        XCTAssertEqual(c.encryptedDNSFlavor, .unknown)
    }

    /// FAILURE GUARDED: plaintext configured → confirmedFalse with flavor .plaintextConfigured.
    func testEncryptedNo_isConfirmedFalse_plaintextConfigured() {
        let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: .no, encryptedDNS: .no))
        XCTAssertEqual(c.encryptedDNS, .confirmedFalse)
        XCTAssertEqual(c.encryptedDNSReason, .plaintextDNSConfigured)
        XCTAssertEqual(c.encryptedDNSFlavor, .plaintextConfigured)
    }

    /// FAILURE GUARDED: a running DNSCrypt proxy must NOT flip a plaintext configuration to
    /// encrypted — the proxy is up but not in the system resolver path. The evidence notes it,
    /// the signal stays confirmedFalse.
    func testEncryptedNo_dnscryptRunning_doesNotFlipSignal() {
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .no, dnsCryptRunning: true))
        XCTAssertEqual(c.encryptedDNS, .confirmedFalse)
        XCTAssertEqual(c.encryptedDNSFlavor, .plaintextConfigured)
        XCTAssertTrue(c.encryptedDNSEvidence.contains("not in the system resolver path"))
    }

    /// FAILURE GUARDED: `.unknown` + loopback resolver + DNSCrypt proxy running → the daemon-only
    /// process signal resolves the loopback case to confirmedTrue (the redundant confirmation the
    /// client could not make).
    func testEncryptedUnknown_loopback_dnscryptRunning_confirmedTrue() {
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .unknown,
                               systemServers: ["127.0.0.1"],
                               dnsCryptRunning: true, dnsCryptServerName: "2.dnscrypt-cert.example"))
        XCTAssertEqual(c.encryptedDNS, .confirmedTrue)
        XCTAssertEqual(c.encryptedDNSReason, .dnscryptProcessConfirmed)
        XCTAssertEqual(c.encryptedDNSFlavor, .dnscrypt)
    }

    /// FAILURE GUARDED: `.unknown` + loopback resolver + NO DNSCrypt proxy → undetermined, never
    /// plaintext (honesty spine: unknown ≠ plaintext).
    func testEncryptedUnknown_loopback_noDnscrypt_isUndetermined_notPlaintext() {
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .unknown, systemServers: ["127.0.0.1"]))
        XCTAssertEqual(c.encryptedDNS, .undetermined)
        XCTAssertEqual(c.encryptedDNSReason, .encryptedDNSIndeterminable)
        XCTAssertEqual(c.encryptedDNSFlavor, .unknown)
        XCTAssertNotEqual(c.encryptedDNS, .confirmedFalse)
        XCTAssertNotEqual(c.encryptedDNSFlavor, .plaintextConfigured)
    }

    /// FAILURE GUARDED: `.unknown` with no loopback and no DNSCrypt → undetermined.
    func testEncryptedUnknown_noLoopback_isUndetermined() {
        let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: .no, encryptedDNS: .unknown))
        XCTAssertEqual(c.encryptedDNS, .undetermined)
        XCTAssertEqual(c.encryptedDNSFlavor, .unknown)
    }

    /// FAILURE GUARDED: no resolvers discovered at all → undetermined with .noResolversDiscovered,
    /// never plaintext.
    func testNoResolvers_isUndetermined() {
        let c = DNSPathConfirmationEngine.confirm(
            snapshot: snapshot(vpn: .no, encryptedDNS: .yes, systemServers: [], scopedResolvers: []))
        XCTAssertEqual(c.encryptedDNS, .undetermined)
        XCTAssertEqual(c.encryptedDNSReason, .noResolversDiscovered)
        XCTAssertEqual(c.encryptedDNSFlavor, .unknown)
    }

    // MARK: - Honesty-spine invariants

    /// FAILURE GUARDED: the two tri-states are independent — every combination of VPN and
    /// encrypted-DNS signals must be representable without one collapsing into the other.
    func testTriStateCombinations_areIndependent() {
        let vpnStates: [ConditionState] = [.yes, .no, .unknown]
        let dnsStates: [ConditionState] = [.yes, .no, .unknown]
        for v in vpnStates {
            for d in dnsStates {
                let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: v, encryptedDNS: d))
                // VPN leg mirrors the input tri-state exactly.
                switch v {
                case .yes: XCTAssertEqual(c.vpn, .confirmedTrue, "vpn=\(v) dns=\(d)")
                case .no: XCTAssertEqual(c.vpn, .confirmedFalse, "vpn=\(v) dns=\(d)")
                case .unknown: XCTAssertEqual(c.vpn, .undetermined, "vpn=\(v) dns=\(d)")
                }
                // The encrypted-DNS leg mirrors the DNS input tri-state, independent of the VPN
                // leg: a VPN being up must not fabricate encrypted DNS (dns=no/unknown), and a
                // VPN being down must not erase a real encrypted configuration (dns=yes).
                switch d {
                case .yes: XCTAssertEqual(c.encryptedDNS, .confirmedTrue, "vpn=\(v) dns=\(d)")
                case .no: XCTAssertEqual(c.encryptedDNS, .confirmedFalse, "vpn=\(v) dns=\(d)")
                case .unknown: XCTAssertEqual(c.encryptedDNS, .undetermined, "vpn=\(v) dns=\(d)")
                }
            }
        }
    }

    /// FAILURE GUARDED: `.unknown` is never rendered as plaintext anywhere in the confirmation.
    func testUnknownIsNeverPlaintext() {
        let c = DNSPathConfirmationEngine.confirm(snapshot: snapshot(vpn: .unknown, encryptedDNS: .unknown))
        XCTAssertNotEqual(c.encryptedDNSFlavor, .plaintextConfigured)
        XCTAssertNotEqual(c.encryptedDNS, .confirmedFalse)
        XCTAssertNotEqual(c.vpn, .confirmedFalse)
    }

    /// FAILURE GUARDED: the raw snapshot rides along so an auditor can re-derive the conclusion
    /// instead of trusting a boolean.
    func testSnapshotRidesAlong() {
        let snap = snapshot(vpn: .yes, encryptedDNS: .yes, configuredTransport: .doh)
        let c = DNSPathConfirmationEngine.confirm(snapshot: snap)
        XCTAssertEqual(c.snapshot, snap)
        XCTAssertEqual(c.networkFingerprint, fingerprint)
        XCTAssertEqual(c.observedAt, observedAt)
    }

    // MARK: - JSON round-trip (XPC payload)

    /// FAILURE GUARDED: the XPC reply must round-trip losslessly (sorted keys, stable payload).
    func testJSONRoundTrip() throws {
        let snap = snapshot(vpn: .yes, encryptedDNS: .yes, configuredTransport: .doh,
                            scopedResolvers: [scopedResolver("1.1.1.1", protocol: .doh)])
        let c = DNSPathConfirmationEngine.confirm(snapshot: snap)
        let data = try XCTUnwrap(c.jsonEncoded())
        let decoded = try XCTUnwrap(DNSPathConfirmation.fromJSON(data))
        XCTAssertEqual(decoded, c)
        XCTAssertEqual(decoded.encryptedDNSFlavor, .doh)
        XCTAssertEqual(decoded.vpnReason, .tunnelInterfaceActive)
    }

    /// FAILURE GUARDED: a malformed payload decodes to nil — the caller must treat that as
    /// undetermined, never as a clean confirmation (honesty spine: decode failure is not a
    /// measurement).
    func testMalformedJSONDecodesToNil() {
        let garbage = Data("not json".utf8)
        XCTAssertNil(DNSPathConfirmation.fromJSON(garbage))
    }
}