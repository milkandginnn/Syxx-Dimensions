import Foundation

// MARK: - The observation duty
//
// One observer, four consumers. The menubar glyph, the Dashboard graph, Prophet and Mirror all read
// the SAME published state rather than each deriving its own — a module that re-derives the answer
// is a module that can disagree with what the user is being shown.
//
// The monitor produces one `DNSTransportSample` per tick and hands it to a `DNSTransportStateStore`.
// It makes no conclusion of its own beyond the per-tick determination; Prophet and Mirror each read
// the window and reason about it in their own terms.
//
// OBSERVATION ORDER, per the owner's spec: DNS queries first, then all traffic.
//   Phase A — DNS query flows (peer port 53 / 853 / 8853). This alone can only ever say
//             "cleartext observed" or "nothing on those ports". Silence on port 53 is ambiguous
//             between "the queries were encrypted" and "no DNS happened at all", so Phase A cannot
//             confirm encryption.
//   Phase B — the wider flow table, restricted to what disambiguates that silence: flows from a
//             resolver process to a configured encrypted-resolver address, and unconnected
//             resolver UDP sockets that prove DNS activity occurred without revealing its peer.
//             This is what Phase A cannot answer. It is deliberately NOT "all traffic": inspecting
//             every flow on the machine is broader than the question needs, and this is a privacy
//             product. Nothing outside the DNS path is retained.

/// Health of the observation apparatus itself. Kept separate from the answer so "we could not look"
/// is never confusable with "we looked and saw nothing".
public enum TransportObserverHealth: String, Codable, Sendable, Equatable {
    /// An observer ran and produced a sample.
    case healthy
    /// No observer could run: sampler missing, privileged helper absent / not running / denied.
    case unavailable
    /// An observer ran but failed.
    case failed
}

/// Drives the observation and publishes samples. Not an actor: it holds only the previous flow
/// snapshot and is called from one scheduler; the store does its own locking.
public final class DNSTransportMonitor: @unchecked Sendable {

    private let lock = NSLock()
    private var previousFlows: [ObservedFlow] = []
    private var haveBaseline = false

    public let store: DNSTransportStateStore

    public init(store: DNSTransportStateStore = DNSTransportStateStore()) {
        self.store = store
    }

    /// Identity of the network path currently in effect. A change expires every sample taken under
    /// the old path immediately, rather than letting the validity window carry a reading across a
    /// network switch.
    public static func networkFingerprint(interfaces: [String], resolvers: [String]) -> String {
        (interfaces.sorted() + ["|"] + resolvers.sorted()).joined(separator: ",")
    }

    /// Read the live network fingerprint from the system.
    public static func currentNetworkFingerprint() -> String {
        networkFingerprint(interfaces: NetworkKeyState.getActiveInterfaces(),
                           resolvers: NetworkKeyState.getSystemDNSServers())
    }

    /// Addresses the system is configured to reach over an encrypted transport. Used to decide
    /// whether a port-443 flow is DNS or just HTTPS; without this the classifier would count every
    /// web request as encrypted DNS.
    public static func encryptedResolverAddresses(scoped: [ScopedResolver]) -> Set<String> {
        var addresses = Set<String>()
        for r in scoped where r.dnsProtocol != nil {
            addresses.insert(r.address)
        }
        return addresses
    }

    /// Run one observation tick.
    ///
    /// The FIRST tick establishes the diff baseline and therefore cannot report activity: with no
    /// previous sample, every flow in the table looks "new", including sockets that have been idle
    /// for hours. Reporting from that first sample would turn history into a current claim, so the
    /// baseline tick is `observationFailed`/undetermined by construction and the first real answer
    /// arrives one interval later.
    @discardableResult
    public func tick(sampler: () -> FlowTransportObserver.SampleResult = { FlowTransportObserver.sample() },
                     vpn: () -> VPNStatus = { NetworkKeyState.vpnStatus() },
                     scopedResolvers: () -> [ScopedResolver] = { NetworkKeyState.getScopedResolvers() },
                     fingerprint: () -> String = { DNSTransportMonitor.currentNetworkFingerprint() },
                     now: () -> Date = Date.init,
                     monotonicNow: () -> UInt64 = MonotonicClock.nowNanos) -> DNSTransportSample {

        let vpnStatus = vpn()
        let (vpnSignal, vpnReason) = DNSTransportDetermination.classifyVPN(vpnStatus.state)
        let vpnEvidence = TransportEvidence(method: .interfaceEnumeration,
                                            interfaces: vpnStatus.tunnelInterfaces,
                                            detail: vpnStatus.evidence)

        let result = sampler()
        guard let flows = result.flows else {
            let (signal, reason) = DNSTransportDetermination.classifyDNS(
                evidence: nil, observerAvailable: false, observationSucceeded: false)
            return publish(dns: (signal, reason),
                           dnsEvidence: TransportEvidence(method: .none,
                                                          detail: result.failureDetail ?? "observer unavailable"),
                           vpn: (vpnSignal, vpnReason), vpnEvidence: vpnEvidence,
                           now: now(), monotonic: monotonicNow(), fingerprint: fingerprint())
        }

        lock.lock()
        let previous = previousFlows
        let hadBaseline = haveBaseline
        previousFlows = flows
        haveBaseline = true
        lock.unlock()

        guard hadBaseline else {
            let (signal, reason) = DNSTransportDetermination.classifyDNS(
                evidence: nil, observerAvailable: true, observationSucceeded: false)
            return publish(dns: (signal, reason),
                           dnsEvidence: TransportEvidence(method: .flowObservation,
                                                          detail: "baseline sample; no interval to diff yet"),
                           vpn: (vpnSignal, vpnReason), vpnEvidence: vpnEvidence,
                           now: now(), monotonic: monotonicNow(), fingerprint: fingerprint())
        }

        let active = FlowTransportObserver.activeFlows(previous: previous, current: flows)
        let evidence = FlowTransportObserver.classify(
            activeFlows: active,
            encryptedResolverAddresses: Self.encryptedResolverAddresses(scoped: scopedResolvers()))
        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: evidence, observerAvailable: true, observationSucceeded: true)
        return publish(dns: (signal, reason), dnsEvidence: evidence,
                       vpn: (vpnSignal, vpnReason), vpnEvidence: vpnEvidence,
                       now: now(), monotonic: monotonicNow(), fingerprint: fingerprint())
    }

    private func publish(dns: (TransportSignal, TransportReason),
                         dnsEvidence: TransportEvidence,
                         vpn: (TransportSignal, TransportReason),
                         vpnEvidence: TransportEvidence,
                         now: Date, monotonic: UInt64, fingerprint: String) -> DNSTransportSample {
        let sample = DNSTransportSample(dnsEncryption: dns.0, dnsReason: dns.1,
                                        dnsEvidence: dnsEvidence,
                                        vpn: vpn.0, vpnReason: vpn.1, vpnEvidence: vpnEvidence,
                                        observedAt: now,
                                        observedAtMonotonicNanos: monotonic,
                                        validFor: DNSTransportStateStore.validityWindow,
                                        networkFingerprint: fingerprint)
        store.record(sample)
        return sample
    }
}
