import XCTest
@testable import SyxxKit

/// C7 — per-app resolver attribution. Each test names its fail state; the changelog mutation
/// matrix demonstrates each reds under the matching defect.
final class ResolverAttributionTests: XCTestCase {

    /// The acceptance bar: an app querying a resolver the system did NOT configure is flagged as a
    /// hardcoded-DNS bypass. Fail state: don't subtract the system set → the bypass app reads clean.
    func testHardcodedResolverIsFlagged() {
        let usage = ["Chrome": Set(["8.8.8.8"]), "Safari": Set(["192.168.1.1"])]
        let r = ResolverAttribution.classify(usage: usage, systemResolvers: ["192.168.1.1"])
        let chrome = r.attributions.first { $0.process == "Chrome" }!
        XCTAssertTrue(chrome.usesHardcodedResolver, "Chrome used 8.8.8.8, not the system 192.168.1.1 — a bypass")
        XCTAssertEqual(chrome.offSystemResolvers, ["8.8.8.8"])
        XCTAssertEqual(r.flagged.map(\.process), ["Chrome"])
    }

    /// An app using ONLY the system resolver is not flagged.
    /// Fail state: flag any resolver → every normal app false-alarms.
    func testSystemResolverOnlyIsNotFlagged() {
        let usage = ["Safari": Set(["192.168.1.1"])]
        let r = ResolverAttribution.classify(usage: usage, systemResolvers: ["192.168.1.1", "192.168.1.2"])
        XCTAssertFalse(r.attributions[0].usesHardcodedResolver)
        XCTAssertTrue(r.flagged.isEmpty)
    }

    /// With NO baseline (empty system-resolver set) nothing can be judged a bypass — the result is
    /// coverage-limited, never a clean or a fabricated flag.
    /// Fail state: flag off-system with an empty baseline → every app is "bypassing" against nothing.
    func testEmptyBaselineIsUnknownNeverFlagged() {
        let usage = ["Chrome": Set(["8.8.8.8"])]
        let r = ResolverAttribution.classify(usage: usage, systemResolvers: [])
        XCTAssertTrue(r.baselineUnknown)
        XCTAssertTrue(r.flagged.isEmpty, "no baseline means no bypass judgment, not a flag")
        XCTAssertEqual(r.attributions[0].offSystemResolvers, [])
    }

    /// A process using multiple resolvers reports only the off-system ones as the bypass set.
    func testMixedResolverSetReportsOnlyOffSystem() {
        let usage = ["App": Set(["192.168.1.1", "8.8.8.8", "1.1.1.1"])]
        let r = ResolverAttribution.classify(usage: usage, systemResolvers: ["192.168.1.1"])
        XCTAssertEqual(r.attributions[0].offSystemResolvers, ["1.1.1.1", "8.8.8.8"])
        XCTAssertTrue(r.attributions[0].usesHardcodedResolver)
    }

    /// usage(from:) keys the resolver on the PORT-53 side, not the QR bit (D063 — the forgeable
    /// direction). A record whose QR claims "Response" but whose source port is 53 still attributes
    /// the resolver to the port-53 peer. Fail state: key on QR → a forged QR bit misattributes.
    func testUsageKeysResolverOnPort53NotQRBit() {
        // Local host 10.0.0.5:54321 ↔ resolver 8.8.8.8:53. QR forged to "Query" on an inbound packet.
        let net = NetworkContextReport(ipVersion: 4, sourceIP: "8.8.8.8", destIP: "10.0.0.5",
                                       sourcePort: 53, destPort: 54321, direction: "Inbound Response")
        let rec = DNSCaptureRecord(
            process: ProcessInfoReport(path: nil, signingID: nil, pid: 900, name: "curl"),
            timestamp: "t",
            packet: DNSPacketReport(opcode: "Query", qr: "Query", questions: [], answers: [],
                                    ra: "false", rcode: "No error", rd: "true", xid: 1, tc: "false", aa: "false"),
            network: net)
        let usage = ResolverAttribution.usage(from: [rec])
        XCTAssertEqual(usage["curl"], Set(["8.8.8.8"]),
                       "the resolver must be the port-53 peer regardless of the (forgeable) QR bit")
    }
}
