import XCTest
@testable import SyxxKit

/// Parser and classifier tests for the unprivileged flow-observation tier.
///
/// The fixture below is VERBATIM output captured from this machine on 2026-08-02 with
/// `nettop -x -n -l 1 -J interface,state,bytes_in,bytes_out`, not invented text — including the
/// real IPv6 router address mDNSResponder was talking cleartext DNS to, the ProtonVPN tunnel rows,
/// and the unconnected UDP socket whose peer no socket-level tool can see.
final class FlowTransportObserverTests: XCTestCase {

    /// Real `nettop` output. IPv4 rows use `addr:port`, IPv6 rows use `addr.port` — a real format
    /// quirk that a hand-written fixture would have missed.
    private let realSample = """
                                                                                     interface         state        bytes_in       bytes_out
    launchd.1                                                                                                              0               0
       tcp4 127.0.0.1:8021<->*:*                                                           lo0        Listen
       tcp6 ::1.8021<->*.*                                                                 lo0        Listen
    apsd.388                                                                                                           14423          145745
       tcp4 10.2.0.2:61724<->17.57.144.138:5223                                          utun4   Established           14423          145745
    mDNSResponder.1328                                                                                               1344479         1219765
       tcp6 2601:2c4:4182:ee00:ac5a:4d9e:8742:90ac.61868<->2601:2c4:4182:ee00::1.53        en6        Closed
       udp6 *.5353<->*.*                                                             bridge100                        342750          342750
       udp4 *:5353<->*:*                                                             bridge101                        994010          864788
       udp4 *:60090<->*:*                                                                utun4                             0              46
       quic4 10.2.0.2:49733<->172.224.186.202:443                                        utun4                          4000            8783
    """

    // MARK: - Parsing

    func testParsesRealNettopOutput() {
        let flows = FlowTransportObserver.parse(realSample)
        XCTAssertEqual(flows.count, 8, "one row per flow line, header and process lines excluded")

        // IPv6 `addr.port` split.
        guard let dns53 = flows.first(where: { $0.remotePort == 53 }) else {
            return XCTFail("the cleartext TCP/53 flow must be parsed")
        }
        XCTAssertEqual(dns53.processName, "mDNSResponder")
        XCTAssertEqual(dns53.pid, 1328)
        XCTAssertEqual(dns53.networkProtocol, "tcp6")
        XCTAssertEqual(dns53.remoteAddress, "2601:2c4:4182:ee00::1")
        XCTAssertEqual(dns53.interfaceName, "en6")
        XCTAssertEqual(dns53.state, "Closed")

        // IPv4 `addr:port` split.
        guard let apns = flows.first(where: { $0.remotePort == 5223 }) else {
            return XCTFail("the IPv4 flow must be parsed")
        }
        XCTAssertEqual(apns.remoteAddress, "17.57.144.138")
        XCTAssertEqual(apns.bytesIn, 14423)
        XCTAssertEqual(apns.bytesOut, 145745)

        // The unconnected UDP socket: a real DNS query left through it (46 bytes out) but the peer
        // is not a property of the socket, so it is genuinely unknown here.
        guard let unconnected = flows.first(where: { $0.localPort == 60090 }) else {
            return XCTFail("the unconnected UDP socket must be parsed")
        }
        XCTAssertNil(unconnected.remoteAddress)
        XCTAssertNil(unconnected.remotePort)
        XCTAssertEqual(unconnected.bytesOut, 46)
    }

    /// FAILURE GUARDED: a process name containing dots (`com.apple.WebKi.908`) being mis-split so
    /// the pid is lost and every flow under it is dropped or misattributed.
    func testProcessTokenTakesPidFromTheLastDot() {
        XCTAssertEqual(FlowTransportObserver.parseProcessToken("mDNSResponder.1328")?.pid, 1328)
        let dotted = FlowTransportObserver.parseProcessToken("com.apple.WebKi.908")
        XCTAssertEqual(dotted?.name, "com.apple.WebKi")
        XCTAssertEqual(dotted?.pid, 908)
        XCTAssertNil(FlowTransportObserver.parseProcessToken("interface"))
    }

    // MARK: - Activity diffing

    /// FAILURE GUARDED: counting the historical rows `nettop` retains for closed sockets as current
    /// activity. That would turn hours-old traffic into a present-tense claim — the same staleness
    /// failure the validity window exists to prevent.
    func testUnchangedHistoricalFlowsAreNotActivity() {
        let flows = FlowTransportObserver.parse(realSample)
        XCTAssertTrue(FlowTransportObserver.activeFlows(previous: flows, current: flows).isEmpty,
                      "an identical table between two samples means nothing happened")
    }

    func testNewOrGrowingFlowsAreActivity() {
        let before = FlowTransportObserver.parse(realSample)
        var after = before
        // A brand-new flow.
        after.append(ObservedFlow(processName: "mDNSResponder", pid: 1328, networkProtocol: "tcp4",
                                  localPort: 51111, remoteAddress: "1.1.1.1", remotePort: 53,
                                  interfaceName: "en6", state: "Established",
                                  bytesIn: 100, bytesOut: 60))
        let active = FlowTransportObserver.activeFlows(previous: before, current: after)
        XCTAssertEqual(active.count, 1)
        XCTAssertEqual(active.first?.remoteAddress, "1.1.1.1")

        // The same flow with a grown counter is also activity.
        var grown = before
        if let idx = grown.firstIndex(where: { $0.remotePort == 5223 }) {
            let f = grown[idx]
            grown[idx] = ObservedFlow(processName: f.processName, pid: f.pid,
                                      networkProtocol: f.networkProtocol, localPort: f.localPort,
                                      remoteAddress: f.remoteAddress, remotePort: f.remotePort,
                                      interfaceName: f.interfaceName, state: f.state,
                                      bytesIn: (f.bytesIn ?? 0) + 1, bytesOut: f.bytesOut)
        }
        XCTAssertEqual(FlowTransportObserver.activeFlows(previous: before, current: grown).count, 1)
    }

    // MARK: - Classification

    func testCleartextDNSFlowIsClassifiedAsOffHostCleartext() {
        let flow = ObservedFlow(processName: "mDNSResponder", pid: 1328, networkProtocol: "tcp6",
                                localPort: 61868, remoteAddress: "2601:2c4:4182:ee00::1",
                                remotePort: 53, interfaceName: "en6", state: "Established",
                                bytesIn: 200, bytesOut: 90)
        let e = FlowTransportObserver.classify(activeFlows: [flow], encryptedResolverAddresses: [])
        XCTAssertEqual(e.cleartextOffHost, 1)
        XCTAssertEqual(e.encryptedDNS, 0)
        XCTAssertEqual(e.interfaces, ["en6"])
    }

    func testDoTAndDoQPortsAreClassifiedAsEncrypted() {
        let dot = ObservedFlow(processName: "stubby", pid: 5, networkProtocol: "tcp4",
                               localPort: 5000, remoteAddress: "1.1.1.1", remotePort: 853,
                               interfaceName: "en0", state: "Established", bytesIn: 300, bytesOut: 90)
        let doq = ObservedFlow(processName: "dnscrypt-proxy", pid: 6, networkProtocol: "quic4",
                               localPort: 5001, remoteAddress: "9.9.9.9", remotePort: 8853,
                               interfaceName: "en0", state: nil, bytesIn: 300, bytesOut: 90)
        let e = FlowTransportObserver.classify(activeFlows: [dot, doq], encryptedResolverAddresses: [])
        XCTAssertEqual(e.encryptedDNS, 2)
        XCTAssertEqual(e.cleartextOffHost, 0)
    }

    /// FAILURE GUARDED: counting every HTTPS flow as encrypted DNS, which would make ordinary web
    /// browsing confirm "your DNS is encrypted". Port 443 counts ONLY at a configured
    /// encrypted-resolver address.
    func testPort443CountsOnlyAtAConfiguredEncryptedResolver() {
        let web = ObservedFlow(processName: "Safari", pid: 900, networkProtocol: "tcp4",
                               localPort: 5000, remoteAddress: "93.184.216.34", remotePort: 443,
                               interfaceName: "en0", state: "Established", bytesIn: 1, bytesOut: 1)
        let doh = ObservedFlow(processName: "mDNSResponder", pid: 1328, networkProtocol: "tcp4",
                               localPort: 5001, remoteAddress: "1.1.1.1", remotePort: 443,
                               interfaceName: "en0", state: "Established", bytesIn: 1, bytesOut: 1)
        XCTAssertEqual(FlowTransportObserver.classify(activeFlows: [web, doh],
                                                     encryptedResolverAddresses: []).encryptedDNS, 0)
        XCTAssertEqual(FlowTransportObserver.classify(activeFlows: [web, doh],
                                                     encryptedResolverAddresses: ["1.1.1.1"]).encryptedDNS, 1)
    }

    /// FAILURE GUARDED: Syxx confirming a transport state out of its own traffic. Syxx sends
    /// cleartext Do53 on purpose during every audit (`DNSResolver.queryUDPDetailed`) and DoH on
    /// purpose during every Portal run (`EncryptedTransportProbe.probe()`,
    /// EncryptedTransportProbe.swift:48). Either would be a fabricated measurement.
    func testSyxxOwnTrafficIsExcludedFromBothDirections() {
        let ownCleartext = ObservedFlow(processName: "syxx", pid: 700, networkProtocol: "tcp4",
                                        localPort: 5000, remoteAddress: "8.8.8.8", remotePort: 53,
                                        interfaceName: "en0", state: "Established",
                                        bytesIn: 100, bytesOut: 50)
        let ownDoH = ObservedFlow(processName: "Syxx", pid: 701, networkProtocol: "tcp4",
                                  localPort: 5001, remoteAddress: "1.1.1.1", remotePort: 443,
                                  interfaceName: "en0", state: "Established",
                                  bytesIn: 100, bytesOut: 50)
        let e = FlowTransportObserver.classify(activeFlows: [ownCleartext, ownDoH],
                                               encryptedResolverAddresses: ["1.1.1.1"])
        XCTAssertEqual(e.cleartextOffHost, 0, "Syxx's own audit query must not confirm cleartext")
        XCTAssertEqual(e.encryptedDNS, 0, "Syxx's own DoH probe must not confirm encryption")
        XCTAssertTrue(e.isEmpty)
    }

    /// FAILURE GUARDED: treating the unconnected-UDP case as either answer. This is the ordinary
    /// cleartext path and it is structurally invisible at socket level, so it must resolve to
    /// undetermined and be answered by the privileged packet tier.
    func testUnconnectedResolverUDPIsUnobservableNotAnAnswer() {
        let flows = FlowTransportObserver.parse(realSample)
        let active = flows.filter { $0.localPort == 60090 }
        let e = FlowTransportObserver.classify(activeFlows: active, encryptedResolverAddresses: [])
        XCTAssertEqual(e.transportUnobservable, 1)
        XCTAssertEqual(e.cleartextOffHost, 0)
        XCTAssertEqual(e.encryptedDNS, 0)

        let (signal, reason) = DNSTransportDetermination.classifyDNS(
            evidence: e, observerAvailable: true, observationSucceeded: true)
        XCTAssertEqual(signal, .undetermined)
        XCTAssertEqual(reason, .dnsActivityTransportUnobservable)
    }

    /// FAILURE GUARDED: mDNS (5353) or NAT-PMP (5350/5351) sockets being counted as unicast DNS
    /// activity, which would make a machine with zero DNS traffic look busy.
    func testMulticastDNSSocketsAreNotCountedAsUnicastDNSActivity() {
        let flows = FlowTransportObserver.parse(realSample)
        let mdns = flows.filter { $0.localPort == 5353 }
        XCTAssertEqual(mdns.count, 2)
        let e = FlowTransportObserver.classify(activeFlows: mdns, encryptedResolverAddresses: [])
        XCTAssertEqual(e.transportUnobservable, 0)
        XCTAssertTrue(e.isEmpty)
    }

    /// FAILURE GUARDED: a query to a local resolver proxy on 127.0.0.1:53 being reported as
    /// cleartext leaving the machine. It never leaves; the proxy's upstream is a separate question.
    func testLoopbackResolverHopIsCountedSeparately() {
        let flow = ObservedFlow(processName: "curl", pid: 800, networkProtocol: "tcp4",
                                localPort: 5000, remoteAddress: "127.0.0.1", remotePort: 53,
                                interfaceName: "lo0", state: "Established", bytesIn: 100, bytesOut: 50)
        let e = FlowTransportObserver.classify(activeFlows: [flow], encryptedResolverAddresses: [])
        XCTAssertEqual(e.cleartextLoopback, 1)
        XCTAssertEqual(e.cleartextOffHost, 0)
    }

    /// FAILURE GUARDED: a missing sampler being reported as an empty observation rather than as
    /// "no observer", which would let "we could not look" read as "we looked and saw nothing".
    func testMissingSamplerIsUnavailableNotEmpty() {
        let result = FlowTransportObserver.sample(runner: { _, _ in nil })
        XCTAssertFalse(result.available)
        XCTAssertNil(result.flows)
        XCTAssertNotNil(result.failureDetail)
    }
}
