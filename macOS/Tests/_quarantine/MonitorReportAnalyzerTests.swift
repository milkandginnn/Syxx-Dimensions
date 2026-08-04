import XCTest
@testable import SyxxKit

/// Tests for the Cage capture → determinative-engine bridge (Workstream H). The central guarantee: a
/// PASSIVE capture, which lacks every active cross-vantage premise, can never yield a confident clean
/// bill of health — it refers / lands inconclusive.
final class MonitorReportAnalyzerTests: XCTestCase {

    private func record(direction: String, xid: UInt16, sourcePort: UInt16, payload: Int,
                        qname: String, qr: String = "Query",
                        timestamp: String = "2026-07-06 12:00:00.000") -> DNSCaptureRecord {
        let packet = DNSPacketReport(
            opcode: "Standard", qr: qr,
            questions: [DNSQuestionReport(questionName: qname, questionClass: "IN", questionType: "A")],
            answers: [], ra: "Recursion available", rcode: "No error", rd: "Recursion desired",
            xid: xid, tc: "Non-Truncated", aa: "Non-Authoritative", payloadBytes: payload)
        let net = NetworkContextReport(ipVersion: 4, sourceIP: "192.168.1.5", destIP: "8.8.8.8",
            sourcePort: sourcePort, destPort: 53, direction: direction)
        return DNSCaptureRecord(process: .unknown, timestamp: timestamp, packet: packet, network: net)
    }

    /// A healthy-looking capture: distinct ports/xids, normal sizes, benign domain.
    private func benignRecords() -> [DNSCaptureRecord] {
        var recs: [DNSCaptureRecord] = []
        for i in 0..<12 {
            recs.append(record(direction: "Outbound Query", xid: UInt16(i * 5000 + 123),
                               sourcePort: UInt16(40000 + i * 137), payload: 40, qname: "example.com"))
            recs.append(record(direction: "Inbound Response", xid: UInt16(i * 5000 + 123),
                               sourcePort: 53, payload: 120, qname: "example.com", qr: "Response"))
        }
        return recs
    }

    // MARK: - The honesty guarantee

    func testCaptureOnlyNeverProducesCleanResult() {
        let inputs = MonitorReportAnalyzer.sectionInputs(from: benignRecords())
        let c = Synthesizer.conclude(from: inputs)
        let isClean = c.diagnosis == .clean && c.conclusivity == .conclusive
        XCTAssertFalse(isClean, "a passive capture must never yield a clean result")
        XCTAssertTrue(c.referralTriggered, "capture-only analysis must refer to a human")
    }

    /// Zero-records contract: no queries seen → `exfil` is nil (Cage UNTESTED, which hard-gates
    /// clean), never an empty array (which would read as "ran, nothing suspicious" = a clean
    /// section). The GUI live path (AppModel.runCage) mirrors this with an explicit empty→nil
    /// guard; this test pins the contract both paths share.
    func testZeroRecordsYieldNilExfilSoCageStaysUntested() {
        let inputs = MonitorReportAnalyzer.sectionInputs(from: [])
        XCTAssertNil(inputs.exfil, "empty capture must map to nil (untested), not [] (clean)")
        let c = Synthesizer.conclude(from: inputs)
        let isClean = c.diagnosis == .clean && c.conclusivity == .conclusive
        XCTAssertFalse(isClean, "a zero-packet capture must never read as clean")
    }

    func testBenignCaptureIsWellRandomizedAndNormalSize() {
        let inputs = MonitorReportAnalyzer.sectionInputs(from: benignRecords())
        XCTAssertEqual(inputs.stubEntropy?.status, .wellRandomized)
        XCTAssertEqual(inputs.responseSize?.status, .normal)
    }

    // MARK: - Capture-sourced signals reach the engine

    func testWeakEntropyCaptureSurfacesAndRefers() {
        // Every query on a single fixed source port → weak stub randomization.
        var recs: [DNSCaptureRecord] = []
        for i in 0..<12 {
            recs.append(record(direction: "Outbound Query", xid: UInt16(i * 4013 + 7),
                               sourcePort: 51111, payload: 40, qname: "example.com"))
        }
        let inputs = MonitorReportAnalyzer.sectionInputs(from: recs)
        XCTAssertEqual(inputs.stubEntropy?.status, .weakRandomization)
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertNotEqual(c.diagnosis, .clean)
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .weakStubEntropy })
    }

    func testHighEntropyTunnelDomainIsAssessed() throws {
        var recs = benignRecords()
        let tunnel = "aGVsbG8gd29ybGQgdGhpcyBpcyBleGZpbA.dGhpcyBpcyBtb3Jl.evil-tunnel.com"
        // Use spaced timestamps so BeaconAnalyzer sees a regular cadence (every 60s).
        let base = "2026-07-06 12:00:00."
        for i in 0..<6 {
            recs.append(record(direction: "Outbound Query", xid: UInt16(30000 + i),
                               sourcePort: UInt16(45000 + i * 91), payload: 90, qname: tunnel,
                               timestamp: "\(base)\(String(format: "%03d", i * 60))"))
        }
        let inputs = MonitorReportAnalyzer.sectionInputs(from: recs)
        let assessments = try XCTUnwrap(inputs.exfil)
        let tunnelAssessment = try XCTUnwrap(assessments.first { $0.domain == tunnel })
        // Presence alone proves nothing (it's present because it was queried). The high-entropy
        // encoded tunnel must actually be FLAGGED — if the entropy detector were gutted to return
        // .benign, the old "contains { domain == tunnel }" check stayed green; this does not.
        XCTAssertTrue(tunnelAssessment.suspicion == .suspicious || tunnelAssessment.suspicion == .likelyExfil,
            "a high-entropy encoded tunnel domain must be flagged suspicious/likelyExfil, got \(tunnelAssessment.suspicion)")
        // Corroborating signal: the encoded tunnel has genuinely high per-character entropy.
        XCTAssertGreaterThan(tunnelAssessment.entropyBitsPerChar, 3.5,
            "an encoded tunnel domain must measure high entropy, not be flagged on name presence alone")
    }

    // MARK: - Parsing

    func testParseNDJSONRoundTrip() throws {
        let enc = JSONEncoder()
        let recs = benignRecords()
        let ndjson = try recs.map { String(data: try enc.encode($0), encoding: .utf8)! }.joined(separator: "\n")
        let parsed = MonitorReportAnalyzer.parseNDJSON(ndjson)
        XCTAssertEqual(parsed.count, recs.count)
        // Field fidelity, not just count: a decoder that garbled every field would pass a count check.
        // Assert the per-record identity round-trips (xid + payloadBytes), not merely the row total.
        XCTAssertEqual(parsed.map { $0.packet.xid }, recs.map { $0.packet.xid })
        XCTAssertEqual(parsed.map { $0.packet.payloadBytes }, recs.map { $0.packet.payloadBytes })
    }

    func testParseSkipsBlankAndCorruptLines() {
        let ndjson = "\n{not json}\n   \n"
        XCTAssertTrue(MonitorReportAnalyzer.parseNDJSON(ndjson).isEmpty)
    }

    func testPayloadBytesDecodeTolerantForOldReports() throws {
        // A packet written before F9 (no "Payload Bytes" key) must still decode — payloadBytes → nil.
        let oldPacketJSON = #"{"Opcode":"Standard","QR":"Query","Questions":[],"Answers":[],"RA":"Recursion available","Rcode":"No error","RD":"Recursion desired","XID":4242,"TC":"Non-Truncated","AA":"Non-Authoritative"}"#
        let packet = try JSONDecoder().decode(DNSPacketReport.self, from: Data(oldPacketJSON.utf8))
        XCTAssertNil(packet.payloadBytes)
        XCTAssertEqual(packet.xid, 4242)
    }
}
