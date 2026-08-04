import XCTest
@testable import SyxxKit

/// REAL-RUN bar for the transport determination (AGENTS.md §2/§9): the production observer and the
/// production determination, driven over genuine traffic on the live host, not fixtures.
///
/// Gated on `SYXX_REAL_RUN=1` so it never runs unattended in CI, where generating real DNS traffic
/// and asserting on a live network would be flaky rather than informative. Invoke with:
///
///   SYXX_REAL_RUN=1 swift test --filter DNSTransportRealRunTests
///
/// The three arms are the three answers the contract must be able to reach:
///   • actually-cleartext — a real DNS query over TCP/53 to a public resolver ⇒ `confirmedFalse`;
///   • actually-encrypted — a real DNS-over-TLS query on TCP/853 ⇒ `confirmedTrue`;
///   • quiet interval — no DNS at all ⇒ `undetermined`, never "encrypted".
final class DNSTransportRealRunTests: XCTestCase {

    private var enabled: Bool { ProcessInfo.processInfo.environment["SYXX_REAL_RUN"] == "1" }

    private func run(_ path: String, _ args: [String]) -> Int32 {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return -1 }
        p.waitUntilExit()
        return p.terminationStatus
    }

    /// Sample, generate traffic, sample again, classify the interval — exactly what the monitor
    /// does on each tick, run against the live flow table.
    private func observeInterval(while generate: () -> Void) throws -> TransportEvidence {
        let before = FlowTransportObserver.sample()
        guard let previous = before.flows else {
            throw XCTSkip("flow sampler unavailable on this host: \(before.failureDetail ?? "unknown")")
        }
        generate()
        let after = FlowTransportObserver.sample()
        guard let current = after.flows else {
            throw XCTSkip("flow sampler unavailable on the second sample")
        }
        let active = FlowTransportObserver.activeFlows(previous: previous, current: current)
        let scoped = NetworkKeyState.getScopedResolvers()
        return FlowTransportObserver.classify(
            activeFlows: active,
            encryptedResolverAddresses: DNSTransportMonitor.encryptedResolverAddresses(scoped: scoped))
    }

    /// ACTUALLY-CLEARTEXT ARM. `dig +tcp @1.1.1.1` sends a genuine DNS query over cleartext TCP/53
    /// from a process that is not Syxx. The determination must reach `confirmedFalse`.
    func testRealCleartextQueryIsConfirmedCleartext() throws {
        try XCTSkipUnless(enabled, "set SYXX_REAL_RUN=1 to run against live traffic")
        let evidence = try observeInterval {
            _ = self.run("/usr/bin/dig", ["+tcp", "+time=5", "+tries=1", "@1.1.1.1",
                                          "example.com", "A", "+short"])
        }
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: evidence, observerAvailable: true, observationSucceeded: true)
        print("[REAL RUN · cleartext] evidence=\(evidence)")
        print("[REAL RUN · cleartext] signal=\(signal) reason=\(reason)")
        XCTAssertGreaterThan(evidence.cleartextOffHost, 0,
                             "a real cleartext TCP/53 query must be observed leaving the host")
        XCTAssertEqual(signal, .confirmedFalse)
        XCTAssertEqual(reason, .cleartextQueryObserved)
    }

    /// ACTUALLY-ENCRYPTED ARM. A genuine DNS-over-TLS exchange on TCP/853 (RFC 7858), from a
    /// process that is not Syxx, with no cleartext in the same interval. The determination must
    /// reach `confirmedTrue`.
    ///
    /// The helper script path is supplied by `SYXX_DOT_SCRIPT`; without it the arm skips rather
    /// than silently passing on absent evidence.
    func testRealDoTQueryIsConfirmedEncrypted() throws {
        try XCTSkipUnless(enabled, "set SYXX_REAL_RUN=1 to run against live traffic")
        guard let script = ProcessInfo.processInfo.environment["SYXX_DOT_SCRIPT"],
              FileManager.default.fileExists(atPath: script) else {
            throw XCTSkip("set SYXX_DOT_SCRIPT to a DoT client script")
        }
        let python = FileManager.default.isExecutableFile(atPath: "/opt/homebrew/bin/python3")
            ? "/opt/homebrew/bin/python3" : "/usr/bin/python3"
        let evidence = try observeInterval {
            _ = self.run(python, [script, "1.1.1.1", "example.com"])
        }
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: evidence, observerAvailable: true, observationSucceeded: true)
        print("[REAL RUN · encrypted] evidence=\(evidence)")
        print("[REAL RUN · encrypted] signal=\(signal) reason=\(reason)")
        XCTAssertGreaterThan(evidence.encryptedDNS, 0,
                             "a real DoT exchange on TCP/853 must be observed")
        XCTAssertEqual(evidence.cleartextOffHost, 0,
                       "no cleartext DNS may be observed in the encrypted arm's interval")
        XCTAssertEqual(signal, .confirmedTrue)
        XCTAssertEqual(reason, .encryptedTransportObserved)
    }

    /// QUIET-INTERVAL ARM. The most dangerous case: an interval in which Syxx generates no DNS at
    /// all must NOT produce "encrypted". Silence is not evidence.
    func testQuietIntervalIsNeverConfirmedEncrypted() throws {
        try XCTSkipUnless(enabled, "set SYXX_REAL_RUN=1 to run against live traffic")
        let evidence = try observeInterval { /* deliberately generate nothing */ }
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: evidence, observerAvailable: true, observationSucceeded: true)
        print("[REAL RUN · quiet] evidence=\(evidence)")
        print("[REAL RUN · quiet] signal=\(signal) reason=\(reason)")
        XCTAssertNotEqual(signal, .confirmedTrue,
                          "an interval with no DNS activity must never confirm encryption")
    }

    /// LIVE MONITOR ARM. Two real ticks through the production monitor, then the reading and the
    /// scan-window summary a consumer would take — proving the whole path, not just the classifier.
    func testLiveMonitorTicksAndPublishesAnHonestReading() throws {
        try XCTSkipUnless(enabled, "set SYXX_REAL_RUN=1 to run against live traffic")
        let monitor = DNSTransportMonitor()
        let fingerprint = DNSTransportMonitor.currentNetworkFingerprint()
        let startMono = MonotonicClock.nowNanos()
        let startWall = Date()

        let baseline = monitor.tick()
        XCTAssertEqual(baseline.dnsEncryption, .undetermined,
                       "the baseline tick has no interval to diff and must be undetermined")

        _ = run("/usr/bin/dig", ["+tcp", "+time=5", "+tries=1", "@1.1.1.1", "example.com", "A", "+short"])
        let second = monitor.tick()
        let endMono = MonotonicClock.nowNanos()

        let reading = monitor.store.current(networkFingerprint: fingerprint)
        let window = monitor.store.window(startMonotonicNanos: startMono, endMonotonicNanos: endMono,
                                          startWall: startWall, endWall: Date(),
                                          networkFingerprint: fingerprint)
        print("[REAL RUN · monitor] tick2 dns=\(second.dnsEncryption)/\(second.dnsReason) vpn=\(second.vpn)/\(second.vpnReason)")
        print("[REAL RUN · monitor] tick2 evidence=\(second.dnsEvidence)")
        print("[REAL RUN · monitor] reading dns=\(reading.dnsEncryption) vpn=\(reading.vpn) glyph=\(reading.glyphShouldAnimate) age=\(reading.age.map { String(format: "%.2fs", $0) } ?? "n/a")")
        print("[REAL RUN · monitor] window dns=\(window.dnsEncryption)/\(window.dnsReason) vpn=\(window.vpn)/\(window.vpnReason) coverage=\(window.coverage) samples=\(window.sampleCount)")

        // Whatever the network is doing, the published reading must be internally consistent: it
        // must never be confirmedTrue without affirmative encrypted evidence in the window.
        if window.dnsEncryption == .confirmedTrue {
            XCTAssertGreaterThan(window.encryptedDNS, 0,
                                 "confirmedTrue with zero encrypted-DNS observations is fabricated")
            XCTAssertEqual(window.cleartextOffHost, 0)
        }
        if window.dnsEncryption == .confirmedFalse {
            XCTAssertGreaterThan(window.cleartextOffHost, 0,
                                 "confirmedFalse with zero cleartext observations is fabricated")
        }
        // A stale reading must expire rather than persist.
        let farFuture = MonotonicClock.nowNanos() + UInt64(60 * 1_000_000_000)
        let expired = monitor.store.current(nowMonotonicNanos: farFuture, networkFingerprint: fingerprint)
        XCTAssertEqual(expired.dnsEncryption, .undetermined)
        XCTAssertEqual(expired.dnsReason, .sampleExpired)
    }
}
