import XCTest
@testable import SyxxKit

/// Offline tests for the IPv4/IPv6 dual-stack + DNS64 datapoint (Workstream H · F8).
final class DualStackProbeTests: XCTestCase {

    func testDualStackWhenBothResolve() {
        let r = DualStackProbe.classify(aStatus: .success, aAddrs: ["93.184.216.34"],
                                        aaaaStatus: .success, aaaaAddrs: ["2606:2800:220:1:248:1893:25c8:1946"])
        XCTAssertEqual(r.status, .dualStack)
    }

    func testIpv4OnlyWhenAAAAIsGenuineNegative() {
        let r = DualStackProbe.classify(aStatus: .success, aAddrs: ["1.2.3.4"],
                                        aaaaStatus: .noData, aaaaAddrs: [])
        XCTAssertEqual(r.status, .ipv4Only)
    }

    func testDns64SynthesisWhenAllAAAAInWellKnownPrefix() {
        // 64:ff9b::5db8:d822 == 64:ff9b::/96 with 93.184.216.34 embedded.
        let r = DualStackProbe.classify(aStatus: .success, aAddrs: ["93.184.216.34"],
                                        aaaaStatus: .success, aaaaAddrs: ["64:ff9b::5db8:d822"])
        XCTAssertEqual(r.status, .dns64Synthesis)
        XCTAssertEqual(r.dns64Count, 1)
    }

    func testIpv4SuppressedWhenAAAAWorksButAIsGenuineNegative() {
        let r = DualStackProbe.classify(aStatus: .nxDomain, aAddrs: [],
                                        aaaaStatus: .success, aaaaAddrs: ["2606:2800:220:1:248:1893:25c8:1946"])
        XCTAssertEqual(r.status, .ipv4Suppressed)
    }

    /// [B038] An A-record TRANSPORT FAILURE (timeout) with a working AAAA is a MISSING datum, not a
    /// result — it must be inconclusive, NEVER ipv4Suppressed (rendering a censorship signal on a
    /// failure is the §1 violation). Before the fix this returned .ipv4Suppressed.
    func testATimeoutWithWorkingAAAAIsInconclusiveNotSuppressed() {
        let r = DualStackProbe.classify(aStatus: .timeout, aAddrs: [],
                                        aaaaStatus: .success, aaaaAddrs: ["2606:2800:220:1:248:1893:25c8:1946"])
        XCTAssertEqual(r.status, .inconclusive,
            "an A-record timeout (missing datum) with working AAAA must be inconclusive, not a censorship signal")
        XCTAssertNotEqual(r.status, .ipv4Suppressed)
    }

    func testInconclusiveWhenBothTransportFail() {
        let r = DualStackProbe.classify(aStatus: .timeout, aAddrs: [],
                                        aaaaStatus: .timeout, aaaaAddrs: [])
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testMixedDns64AndRealIsNotDns64() {
        // One DNS64 + one real global AAAA → not a pure NAT64 synthesis → treated as dual-stack.
        let r = DualStackProbe.classify(aStatus: .success, aAddrs: ["1.2.3.4"],
                                        aaaaStatus: .success, aaaaAddrs: ["64:ff9b::5db8:d822", "2606:2800:220:1:248:1893:25c8:1946"])
        XCTAssertEqual(r.status, .dualStack)
        XCTAssertEqual(r.dns64Count, 1)
    }

    func testDns64PrefixDetection() {
        XCTAssertTrue(DualStackProbe.isDNS64("64:ff9b::5db8:d822"))
        XCTAssertTrue(DualStackProbe.isDNS64("64:ff9b::1.2.3.4"))          // embedded-IPv4 rendering
        XCTAssertFalse(DualStackProbe.isDNS64("2606:2800:220:1:248:1893:25c8:1946"))
        XCTAssertFalse(DualStackProbe.isDNS64("64:ff9c::1"))               // one bit off the prefix
        XCTAssertFalse(DualStackProbe.isDNS64("not-an-ip"))
        XCTAssertFalse(DualStackProbe.isDNS64("1.2.3.4"))                  // IPv4 string
    }

    func testProbeViaMock() async {
        let query: DualStackProbe.QueryFn = { q in
            q.type == 28
                ? DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 28, ttl: 60, rdata: "64:ff9b::5db8:d822")])
                : DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 60, rdata: "93.184.216.34")])
        }
        let r = await DualStackProbe.probe(domain: "example.com", server: "192.168.1.1", query: query)
        XCTAssertEqual(r.status, .dns64Synthesis)
    }
}
