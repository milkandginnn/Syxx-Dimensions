import XCTest
@testable import SyxxKit

/// Determination-core tests for the third transport question — "are MY DNS queries actually
/// leaving this machine encrypted?" (`DNSTransportState.swift`).
///
/// Every test below is written so it turns RED if the corresponding honesty rule is removed. The
/// failure each one guards is named in its doc comment, because the whole point of this layer is
/// that it must be impossible to render "encrypted" or "cleartext" without the evidence for it.
final class DNSTransportDeterminationTests: XCTestCase {

    private let fingerprint = "en0|1.1.1.1"

    private func sample(dns: TransportSignal,
                        dnsReason: TransportReason = .encryptedTransportObserved,
                        evidence: TransportEvidence = TransportEvidence(method: .flowObservation),
                        vpn: TransportSignal = .confirmedFalse,
                        vpnReason: TransportReason = .noTunnelInterfaceActive,
                        monotonic: UInt64,
                        validFor: TimeInterval = 15,
                        fingerprint fp: String? = nil) -> DNSTransportSample {
        DNSTransportSample(dnsEncryption: dns, dnsReason: dnsReason, dnsEvidence: evidence,
                           vpn: vpn, vpnReason: vpnReason,
                           vpnEvidence: TransportEvidence(method: .interfaceEnumeration),
                           observedAt: Date(timeIntervalSince1970: 1_700_000_000),
                           observedAtMonotonicNanos: monotonic,
                           validFor: validFor,
                           networkFingerprint: fp ?? fingerprint)
    }

    private func nanos(_ seconds: Double) -> UInt64 { UInt64(seconds * 1_000_000_000) }

    // MARK: - Silence is not evidence

    /// FAILURE GUARDED: an observer that saw nothing reporting "encrypted" (the "no cleartext
    /// therefore encrypted" fallacy) or "cleartext". Zero observations is undetermined.
    func testNoObservedActivityIsUndeterminedNotEncryptedAndNotCleartext() {
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation),
            observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(signal, .undetermined)
        XCTAssertEqual(reason, .noDNSActivityObserved)
        XCTAssertNotEqual(signal, .confirmedTrue)
        XCTAssertNotEqual(signal, .confirmedFalse)
    }

    /// FAILURE GUARDED: an absent observer (helper not installed, not running, denied, sampler
    /// missing) resolving to either answer. Helper absence is undetermined, full stop.
    func testObserverUnavailableIsUndetermined() {
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation, cleartextOffHost: 5),
            observerAvailable: false, observationSucceeded: true)
        XCTAssertEqual(signal, .undetermined)
        XCTAssertEqual(reason, .observerUnavailable)
    }

    /// FAILURE GUARDED: a failed observation being treated as a successful empty one.
    func testObservationFailureIsUndetermined() {
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: nil, observerAvailable: true, observationSucceeded: false)
        XCTAssertEqual(signal, .undetermined)
        XCTAssertEqual(reason, .observationFailed)
    }

    // MARK: - Positive evidence

    /// FAILURE GUARDED: an observed cleartext query failing to produce a confirmed-cleartext state.
    func testObservedCleartextConfirmsCleartext() {
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation, cleartextOffHost: 1),
            observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(signal, .confirmedFalse)
        XCTAssertEqual(reason, .cleartextQueryObserved)
    }

    /// FAILURE GUARDED: "encrypted" being inferred from the absence of cleartext rather than from
    /// affirmative encrypted-DNS evidence.
    func testEncryptedRequiresAffirmativeEncryptedEvidence() {
        let noEvidence = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation, transportUnobservable: 3),
            observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(noEvidence.0, .undetermined)
        XCTAssertEqual(noEvidence.1, .dnsActivityTransportUnobservable)

        let withEvidence = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation, encryptedDNS: 2),
            observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(withEvidence.0, .confirmedTrue)
        XCTAssertEqual(withEvidence.1, .encryptedTransportObserved)
    }

    /// FAILURE GUARDED: a mixed window reading as encrypted. If any query left in cleartext, the
    /// answer to "are my queries encrypted" is no — the same all-must-be-encrypted rule D056
    /// applies to the configured question.
    func testCleartextDominatesAMixedWindow() {
        let (signal, _) = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation,
                                        cleartextOffHost: 1, encryptedDNS: 9),
            observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(signal, .confirmedFalse)
    }

    /// FAILURE GUARDED: an on-host proxy hop (client to 127.0.0.1:53) being reported as queries
    /// leaving the machine in cleartext. It is not off-host, and the proxy's own upstream transport
    /// is a separate, unobserved question.
    func testLoopbackProxyHopIsNotOffHostCleartext() {
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: TransportEvidence(method: .flowObservation, cleartextLoopback: 4),
            observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(signal, .undetermined)
        XCTAssertEqual(reason, .dnsActivityTransportUnobservable)
    }

    // MARK: - Staleness (MEMORY.md:1809 — never cache staleness into a claim)

    /// FAILURE GUARDED: a sample past its validity window still returning its last value. This is
    /// the exact defect MEMORY.md:1809 forbids for the menubar glyph.
    func testExpiredSampleReturnsUndeterminedNotItsLastValue() {
        let s = sample(dns: .confirmedTrue, monotonic: nanos(1000), validFor: 15)
        let fresh = s.reading(nowMonotonicNanos: nanos(1010), networkFingerprint: fingerprint)
        XCTAssertEqual(fresh.dnsEncryption, .confirmedTrue)

        let stale = s.reading(nowMonotonicNanos: nanos(1016), networkFingerprint: fingerprint)
        XCTAssertEqual(stale.dnsEncryption, .undetermined)
        XCTAssertEqual(stale.dnsReason, .sampleExpired)
        XCTAssertNil(stale.observedAt, "an expired reading must not hand back the sample it expired")
    }

    /// FAILURE GUARDED: a backwards clock jump making an arbitrarily old sample look fresh. The
    /// staleness check runs on a monotonic clock, and a reading that precedes the observation is
    /// treated as untrustworthy rather than as age zero.
    func testReadingBeforeObservationIsTreatedAsExpired() {
        let s = sample(dns: .confirmedTrue, monotonic: nanos(1000))
        let r = s.reading(nowMonotonicNanos: nanos(999), networkFingerprint: fingerprint)
        XCTAssertEqual(r.dnsEncryption, .undetermined)
        XCTAssertEqual(r.dnsReason, .sampleExpired)
    }

    /// FAILURE GUARDED: a reading taken on one network being served after the machine moved to
    /// another. The sample describes a path that is no longer in effect.
    func testNetworkChangeInvalidatesTheSampleImmediately() {
        let s = sample(dns: .confirmedTrue, monotonic: nanos(1000))
        let r = s.reading(nowMonotonicNanos: nanos(1001), networkFingerprint: "en0|9.9.9.9")
        XCTAssertEqual(r.dnsEncryption, .undetermined)
        XCTAssertEqual(r.dnsReason, .networkChanged)
    }

    /// FAILURE GUARDED: a store with no sample at all defaulting to anything but undetermined.
    func testEmptyStoreIsUndetermined() {
        let store = DNSTransportStateStore()
        let r = store.current(nowMonotonicNanos: nanos(1000), networkFingerprint: fingerprint)
        XCTAssertEqual(r.dnsEncryption, .undetermined)
        XCTAssertEqual(r.vpn, .undetermined)
        XCTAssertEqual(r.dnsReason, .notYetObserved)
    }

    // MARK: - Two independent signals

    /// The four combinations that mean different things to the user and to the modules.
    /// FAILURE GUARDED: collapsing DNS encryption and VPN presence into one value, in which case
    /// at least one of these four assertions becomes impossible to express.
    func testDNSAndVPNAreIndependentSignals() {
        // 1. DNS encrypted on its own merits, no tunnel.
        var s = sample(dns: .confirmedTrue, vpn: .confirmedFalse, monotonic: nanos(100))
        var r = s.reading(nowMonotonicNanos: nanos(101), networkFingerprint: fingerprint)
        XCTAssertEqual(r.dnsEncryption, .confirmedTrue)
        XCTAssertEqual(r.vpn, .confirmedFalse)

        // 2. Tunnel up, DNS still leaving in cleartext. A real finding, close to Glutton's core
        //    question, and it must NOT read as encrypted just because a VPN is present.
        s = sample(dns: .confirmedFalse, dnsReason: .cleartextQueryObserved,
                   vpn: .confirmedTrue, vpnReason: .tunnelInterfaceActive, monotonic: nanos(100))
        r = s.reading(nowMonotonicNanos: nanos(101), networkFingerprint: fingerprint)
        XCTAssertEqual(r.dnsEncryption, .confirmedFalse)
        XCTAssertEqual(r.vpn, .confirmedTrue)

        // 3. Encrypted DNS inside a tunnel.
        s = sample(dns: .confirmedTrue, vpn: .confirmedTrue, vpnReason: .tunnelInterfaceActive,
                   monotonic: nanos(100))
        r = s.reading(nowMonotonicNanos: nanos(101), networkFingerprint: fingerprint)
        XCTAssertEqual(r.dnsEncryption, .confirmedTrue)
        XCTAssertEqual(r.vpn, .confirmedTrue)

        // 4. A confirmed VPN must never fill in an unobserved DNS state.
        s = sample(dns: .undetermined, dnsReason: .noDNSActivityObserved,
                   vpn: .confirmedTrue, vpnReason: .tunnelInterfaceActive, monotonic: nanos(100))
        r = s.reading(nowMonotonicNanos: nanos(101), networkFingerprint: fingerprint)
        XCTAssertEqual(r.dnsEncryption, .undetermined)
        XCTAssertEqual(r.vpn, .confirmedTrue)
    }

    /// FAILURE GUARDED: flattening `VPNStatus`'s existing tri-state (NetworkKeyState.swift:432)
    /// into a boolean, which would turn "could not enumerate interfaces" into "no VPN".
    func testVPNTriStateIsPreserved() {
        XCTAssertEqual(DNSTransportDetermination.classifyVPN(.yes).0, .confirmedTrue)
        XCTAssertEqual(DNSTransportDetermination.classifyVPN(.no).0, .confirmedFalse)
        XCTAssertEqual(DNSTransportDetermination.classifyVPN(.unknown).0, .undetermined)
    }

    // MARK: - Menubar glyph

    /// The glyph animates on EITHER signal (MEMORY.md:1808 — it must loop when a third-party VPN is
    /// doing the encrypting, not only when Syxx is). This either-or is correct for the glyph and
    /// only for the glyph.
    /// FAILURE GUARDED: the glyph continuing to animate on an undetermined state, i.e. animating
    /// off a stale or absent reading.
    func testGlyphAnimatesOnEitherSignalAndStopsOnUndetermined() {
        func reading(_ dns: TransportSignal, _ vpn: TransportSignal) -> DNSTransportReading {
            sample(dns: dns, vpn: vpn, monotonic: nanos(100))
                .reading(nowMonotonicNanos: nanos(101), networkFingerprint: fingerprint)
        }
        XCTAssertTrue(reading(.confirmedTrue, .confirmedFalse).glyphShouldAnimate)
        XCTAssertTrue(reading(.confirmedFalse, .confirmedTrue).glyphShouldAnimate)
        XCTAssertTrue(reading(.confirmedTrue, .confirmedTrue).glyphShouldAnimate)
        XCTAssertFalse(reading(.undetermined, .undetermined).glyphShouldAnimate)
        XCTAssertFalse(reading(.confirmedFalse, .confirmedFalse).glyphShouldAnimate)

        // And an expired sample stops the animation rather than continuing the last state.
        let expired = sample(dns: .confirmedTrue, vpn: .confirmedTrue, monotonic: nanos(100))
            .reading(nowMonotonicNanos: nanos(200), networkFingerprint: fingerprint)
        XCTAssertFalse(expired.glyphShouldAnimate)
    }

    // MARK: - Scan-window correlation (Prophet / Mirror)

    /// FAILURE GUARDED: a partially covered window yielding "encrypted". A gap can hide a cleartext
    /// query, so incomplete coverage can never confirm encryption.
    func testIncompleteWindowCoverageCannotConfirmEncrypted() {
        let samples = [sample(dns: .confirmedTrue,
                              evidence: TransportEvidence(method: .flowObservation, encryptedDNS: 1),
                              monotonic: nanos(100))]
        let w = DNSTransportDetermination.summarize(samples: samples, expectedSampleCount: 4,
                                                    start: Date(), end: Date())
        XCTAssertEqual(w.dnsEncryption, .undetermined)
        XCTAssertEqual(w.dnsReason, .incompleteWindowCoverage)
        XCTAssertEqual(w.coverage, 0.25, accuracy: 0.001)
    }

    /// FAILURE GUARDED: symmetric treatment of positive and negative evidence. One observed
    /// cleartext query confirms cleartext even with gaps — a gap cannot un-observe a query.
    func testPartialCoverageStillConfirmsCleartext() {
        let samples = [sample(dns: .confirmedFalse, dnsReason: .cleartextQueryObserved,
                              evidence: TransportEvidence(method: .flowObservation, cleartextOffHost: 1),
                              monotonic: nanos(100))]
        let w = DNSTransportDetermination.summarize(samples: samples, expectedSampleCount: 10,
                                                    start: Date(), end: Date())
        XCTAssertEqual(w.dnsEncryption, .confirmedFalse)
        XCTAssertEqual(w.cleartextOffHost, 1)
    }

    /// FAILURE GUARDED: reporting a stable transport beside results gathered while it changed —
    /// a fabricated correlation.
    func testDisagreeingSamplesInAWindowAreUndetermined() {
        let samples = [
            sample(dns: .confirmedTrue,
                   evidence: TransportEvidence(method: .flowObservation, encryptedDNS: 1),
                   monotonic: nanos(100)),
            sample(dns: .undetermined, dnsReason: .noDNSActivityObserved, monotonic: nanos(105))
        ]
        let w = DNSTransportDetermination.summarize(samples: samples, expectedSampleCount: 2,
                                                    start: Date(), end: Date())
        XCTAssertEqual(w.dnsEncryption, .undetermined)
        XCTAssertEqual(w.dnsReason, .stateChangedDuringWindow)
    }

    /// FAILURE GUARDED: a VPN that came up or dropped mid-scan being reported as a stable condition
    /// of the scan.
    func testVPNChangingMidWindowIsUndetermined() {
        let samples = [
            sample(dns: .undetermined, vpn: .confirmedTrue, vpnReason: .tunnelInterfaceActive,
                   monotonic: nanos(100)),
            sample(dns: .undetermined, vpn: .confirmedFalse, monotonic: nanos(105))
        ]
        let w = DNSTransportDetermination.summarize(samples: samples, expectedSampleCount: 2,
                                                    start: Date(), end: Date())
        XCTAssertEqual(w.vpn, .undetermined)
        XCTAssertEqual(w.vpnReason, .stateChangedDuringWindow)
    }

    /// FAILURE GUARDED: a module scanning while no sample existed getting anything but undetermined.
    func testWindowWithNoSamplesIsUndetermined() {
        let w = DNSTransportDetermination.summarize(samples: [], expectedSampleCount: 3,
                                                    start: Date(), end: Date())
        XCTAssertEqual(w.dnsEncryption, .undetermined)
        XCTAssertEqual(w.vpn, .undetermined)
        XCTAssertEqual(w.coverage, 0)
        XCTAssertEqual(w.sampleCount, 0)
    }

    /// A fully covered, unanimously encrypted window is the ONLY shape that confirms encryption.
    func testFullyCoveredUnanimousWindowConfirmsEncrypted() {
        let samples = (0..<3).map {
            sample(dns: .confirmedTrue,
                   evidence: TransportEvidence(method: .flowObservation, encryptedDNS: 1),
                   monotonic: nanos(100 + Double($0) * 5))
        }
        let w = DNSTransportDetermination.summarize(samples: samples, expectedSampleCount: 3,
                                                    start: Date(), end: Date())
        XCTAssertEqual(w.dnsEncryption, .confirmedTrue)
        XCTAssertEqual(w.coverage, 1.0, accuracy: 0.001)
        XCTAssertEqual(w.encryptedDNS, 3)
    }

    // MARK: - Dashboard timeline

    /// FAILURE GUARDED: a gap in the graph being indistinguishable from a measured value. Gaps are
    /// emitted as explicit undetermined slots so a renderer cannot interpolate across one without
    /// seeing it.
    func testTimelineGapsAreExplicitAndDistinctFromCleartext() {
        let store = DNSTransportStateStore()
        // Two samples, 25 s apart, in a 6-slot (30 s) view: four slots must be gaps.
        store.record(sample(dns: .confirmedFalse, dnsReason: .cleartextQueryObserved,
                            monotonic: nanos(1000)))
        store.record(sample(dns: .confirmedTrue, monotonic: nanos(1025)))
        let slots = store.timeline(nowMonotonicNanos: nanos(1030),
                                   networkFingerprint: fingerprint, slots: 6)
        XCTAssertEqual(slots.count, 6)
        let gaps = slots.filter(\.isGap)
        XCTAssertEqual(gaps.count, 4)
        for gap in gaps {
            XCTAssertEqual(gap.dnsEncryption, .undetermined)
            XCTAssertNotEqual(gap.dnsEncryption, .confirmedFalse,
                              "a gap must never render as the cleartext value")
        }
        XCTAssertTrue(slots.contains { $0.dnsEncryption == .confirmedFalse })
        XCTAssertTrue(slots.contains { $0.dnsEncryption == .confirmedTrue })
    }

    // MARK: - Monitor

    /// FAILURE GUARDED: the very first tick reporting activity. With no previous sample every flow
    /// in the table looks new, including sockets idle for hours; reporting from it would turn
    /// history into a current claim.
    func testFirstTickIsBaselineOnlyAndReportsUndetermined() {
        let monitor = DNSTransportMonitor()
        let flows = [ObservedFlow(processName: "mDNSResponder", pid: 1, networkProtocol: "tcp4",
                                  localPort: 5000, remoteAddress: "1.1.1.1", remotePort: 53,
                                  interfaceName: "en0", state: "Closed", bytesIn: 0, bytesOut: 0)]
        let first = monitor.tick(sampler: { .init(flows: flows, failureDetail: nil) },
                                 vpn: { VPNStatus(state: .no, tunnelInterfaces: [], evidence: "") },
                                 scopedResolvers: { [] },
                                 fingerprint: { self.fingerprint },
                                 now: { Date() }, monotonicNow: { self.nanos(100) })
        XCTAssertEqual(first.dnsEncryption, .undetermined)
        XCTAssertEqual(first.dnsReason, .observationFailed)

        // Second tick with the SAME table and no counter movement: still no activity, still
        // undetermined — a persisting historical row is not a current observation.
        let second = monitor.tick(sampler: { .init(flows: flows, failureDetail: nil) },
                                  vpn: { VPNStatus(state: .no, tunnelInterfaces: [], evidence: "") },
                                  scopedResolvers: { [] },
                                  fingerprint: { self.fingerprint },
                                  now: { Date() }, monotonicNow: { self.nanos(105) })
        XCTAssertEqual(second.dnsEncryption, .undetermined)
        XCTAssertEqual(second.dnsReason, .noDNSActivityObserved)
    }

    /// FAILURE GUARDED: an unavailable sampler producing anything other than undetermined.
    func testMonitorWithUnavailableSamplerIsUndetermined() {
        let monitor = DNSTransportMonitor()
        let s = monitor.tick(sampler: { .init(flows: nil, failureDetail: "sampler missing") },
                             vpn: { VPNStatus(state: .yes, tunnelInterfaces: ["utun4"], evidence: "") },
                             scopedResolvers: { [] },
                             fingerprint: { self.fingerprint },
                             now: { Date() }, monotonicNow: { self.nanos(100) })
        XCTAssertEqual(s.dnsEncryption, .undetermined)
        XCTAssertEqual(s.dnsReason, .observerUnavailable)
        // The VPN signal is independent and still observable through interface enumeration.
        XCTAssertEqual(s.vpn, .confirmedTrue)
    }
}
