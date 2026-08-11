import XCTest
@testable import SyxxKit

/// B018: tests for the Cage redundancy correlator (`CageRedundancyCorrelator.swift`, 235 lines).
/// The correlator compares two independent BPF capture streams (daemon + Cage) of the same DNS
/// traffic and produces a three-valued result: agree, disagree, or incomparable. Every test uses
/// synthetic fixture data — no I/O, no root, no BPF — so the pure-logic path is exhaustively
/// covered.
///
/// Fixture shape: `DNSCaptureRecord` with `network.direction = "Outbound Query"`, one question
/// in `packet.questions`, and a timestamp string in the `MonitorReportAnalyzer.parseTimestamp`
/// format ("yyyy-MM-dd HH:mm:ss.SSS").
final class CageRedundancyCorrelatorTests: XCTestCase {

    // MARK: - Fixture builders

    /// Build a minimal outbound-query `DNSCaptureRecord` for the correlator.
    private func record(qname: String, qtype: String = "A", xid: UInt16,
                        timestamp: String = "2026-08-10 12:00:00.000",
                        direction: String = "Outbound Query",
                        qr: String = "Query") -> DNSCaptureRecord {
        DNSCaptureRecord(
            process: ProcessInfoReport(path: nil, signingID: nil, pid: nil, name: "test"),
            timestamp: timestamp,
            packet: DNSPacketReport(
                opcode: "Query", qr: qr,
                questions: [DNSQuestionReport(questionName: qname, questionClass: "IN", questionType: qtype)],
                answers: [],
                ra: "No", rcode: "No error", rd: "Yes", xid: xid, tc: "No", aa: "No"),
            network: NetworkContextReport(
                ipVersion: 4, sourceIP: "192.168.1.10", destIP: "1.1.1.1",
                sourcePort: 54321, destPort: 53, direction: direction))
    }

    /// Convenience: build a record at a specific wall-clock offset (seconds from a base).
    private func recordAt(qname: String, qtype: String = "A", xid: UInt16,
                          offsetSeconds: Double) -> DNSCaptureRecord {
        let base = Date(timeIntervalSince1970: 1_723_291_200) // 2024-08-10 12:00:00 UTC
        let d = base.addingTimeInterval(offsetSeconds)
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"
        f.timeZone = TimeZone(identifier: "UTC")
        let ts = f.string(from: d)
        return record(qname: qname, qtype: qtype, xid: xid, timestamp: ts)
    }

    // MARK: - Perfect agreement

    /// FAILURE GUARDED: two captures that see the exact same queries with matching keys → .agree.
    /// This is the strongest evidence: two independent paths corroborate each other.
    func testPerfectAgreement() {
        let daemon = [
            record(qname: "example.com", xid: 100),
            record(qname: "test.org", xid: 200)
        ]
        let cage = [
            record(qname: "example.com", xid: 100),
            record(qname: "test.org", xid: 200)
        ]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .agree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 2)
            XCTAssertEqual(evidence.daemonOnlyCount, 0)
            XCTAssertEqual(evidence.cageOnlyCount, 0)
            XCTAssertEqual(evidence.xidDriftCount, 0)
        } else {
            XCTFail("Expected .agree for identical captures, got \(result)")
        }
    }

    /// FAILURE GUARDED: perfect agreement with matching timestamps → delta is 0 (or very close).
    func testPerfectAgreementWithMatchingTimestamps() {
        let daemon = [recordAt(qname: "example.com", xid: 100, offsetSeconds: 0)]
        let cage = [recordAt(qname: "example.com", xid: 100, offsetSeconds: 0)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .agree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 1)
            if let delta = evidence.matched.first?.deltaMs {
                XCTAssertEqual(delta, 0, accuracy: 1.0, "matching timestamps should have ~0 ms delta")
            }
        } else {
            XCTFail("Expected .agree, got \(result)")
        }
    }

    // MARK: - Partial overlap

    /// FAILURE GUARDED: one capture sees queries the other missed → .disagree, with the
    /// daemon-only and cage-only lists populated correctly. Never silently averaged into .agree.
    func testPartialOverlap_isDisagree() {
        let daemon = [
            record(qname: "shared.com", xid: 100),
            record(qname: "daemon-only.com", xid: 200)
        ]
        let cage = [
            record(qname: "shared.com", xid: 100),
            record(qname: "cage-only.com", xid: 300)
        ]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .disagree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 1)
            XCTAssertEqual(evidence.matched.first?.key.qname, "shared.com")
            XCTAssertEqual(evidence.daemonOnlyCount, 1)
            XCTAssertEqual(evidence.daemonOnly.first?.qname, "daemon-only.com")
            XCTAssertEqual(evidence.cageOnlyCount, 1)
            XCTAssertEqual(evidence.cageOnly.first?.qname, "cage-only.com")
            XCTAssertEqual(evidence.xidDriftCount, 0)
        } else {
            XCTFail("Expected .disagree for partial overlap, got \(result)")
        }
    }

    // MARK: - Complete disagreement

    /// FAILURE GUARDED: entirely disjoint query sets → .disagree with zero matches.
    /// This is itself a forensic finding (capture evasion or a broken capture path).
    func testCompleteDisagreement() {
        let daemon = [
            record(qname: "alpha.com", xid: 100),
            record(qname: "beta.com", xid: 200)
        ]
        let cage = [
            record(qname: "gamma.com", xid: 300),
            record(qname: "delta.com", xid: 400)
        ]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .disagree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 0)
            XCTAssertEqual(evidence.daemonOnlyCount, 2)
            XCTAssertEqual(evidence.cageOnlyCount, 2)
            XCTAssertEqual(evidence.xidDriftCount, 0)
        } else {
            XCTFail("Expected .disagree for disjoint captures, got \(result)")
        }
    }

    // MARK: - XID collision / drift

    /// FAILURE GUARDED: same xid in both streams but different qname → xid drift is detected
    /// as its own signal, never counted as a match. A re-used transaction id with a different
    /// question is drift, not agreement.
    func testXidDrift_sameXidDifferentQname() {
        let daemon = [record(qname: "real.com", qtype: "A", xid: 42)]
        let cage = [record(qname: "spoofed.com", qtype: "A", xid: 42)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .disagree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 0, "xid drift must NOT be counted as a match")
            XCTAssertEqual(evidence.xidDriftCount, 1)
            XCTAssertEqual(evidence.xidDrift.first?.xid, 42)
            XCTAssertEqual(evidence.xidDrift.first?.daemonQname, "real.com")
            XCTAssertEqual(evidence.xidDrift.first?.cageQname, "spoofed.com")
            // Both are also daemon-only and cage-only (different keys).
            XCTAssertEqual(evidence.daemonOnlyCount, 1)
            XCTAssertEqual(evidence.cageOnlyCount, 1)
        } else {
            XCTFail("Expected .disagree for xid drift, got \(result)")
        }
    }

    /// FAILURE GUARDED: same xid + same qname but different qtype → xid drift.
    func testXidDrift_sameXidDifferentQtype() {
        let daemon = [record(qname: "example.com", qtype: "A", xid: 99)]
        let cage = [record(qname: "example.com", qtype: "AAAA", xid: 99)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .disagree(let evidence) = result {
            XCTAssertEqual(evidence.xidDriftCount, 1)
            XCTAssertEqual(evidence.xidDrift.first?.daemonQtype, "A")
            XCTAssertEqual(evidence.xidDrift.first?.cageQtype, "AAAA")
        } else {
            XCTFail("Expected .disagree for qtype drift, got \(result)")
        }
    }

    // MARK: - Empty captures (incomparable)

    /// FAILURE GUARDED: both streams empty → .incomparable, never .agree. Silence is not
    /// agreement (DNSTransportState.swift:33-36).
    func testBothEmpty_isIncomparable() {
        let result = CageRedundancyCorrelator.correlate(daemon: [], cage: [])
        if case .incomparable(let reason) = result {
            XCTAssertTrue(reason.contains("neither"), "reason should mention neither capture: \(reason)")
        } else {
            XCTFail("Expected .incomparable for both empty, got \(result)")
        }
    }

    /// FAILURE GUARDED: daemon empty, cage has queries → .incomparable.
    func testDaemonEmpty_isIncomparable() {
        let cage = [record(qname: "example.com", xid: 100)]
        let result = CageRedundancyCorrelator.correlate(daemon: [], cage: cage)
        if case .incomparable(let reason) = result {
            XCTAssertTrue(reason.contains("daemon"), "reason should mention daemon: \(reason)")
        } else {
            XCTFail("Expected .incomparable for daemon empty, got \(result)")
        }
    }

    /// FAILURE GUARDED: cage empty, daemon has queries → .incomparable.
    func testCageEmpty_isIncomparable() {
        let daemon = [record(qname: "example.com", xid: 100)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: [])
        if case .incomparable(let reason) = result {
            XCTAssertTrue(reason.contains("Cage"), "reason should mention Cage: \(reason)")
        } else {
            XCTFail("Expected .incomparable for cage empty, got \(result)")
        }
    }

    // MARK: - Non-outbound records are filtered

    /// FAILURE GUARDED: inbound responses are filtered out — only outbound queries are compared.
    func testInboundResponsesAreFiltered() {
        let daemon = [
            record(qname: "example.com", xid: 100, direction: "Inbound Response", qr: "Response")
        ]
        let cage = [
            record(qname: "example.com", xid: 100, direction: "Outbound Query", qr: "Query")
        ]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        // Daemon has no outbound queries → daemon is effectively empty → incomparable.
        if case .incomparable = result {
            // expected
        } else {
            XCTFail("Expected .incomparable when daemon has only inbound, got \(result)")
        }
    }

    /// FAILURE GUARDED: records with no questions are skipped.
    func testRecordsWithNoQuestionsAreSkipped() {
        let noQuestion = DNSCaptureRecord(
            process: ProcessInfoReport(path: nil, signingID: nil, pid: nil, name: "test"),
            timestamp: "2026-08-10 12:00:00.000",
            packet: DNSPacketReport(
                opcode: "Query", qr: "Query",
                questions: [], answers: [],
                ra: "No", rcode: "No error", rd: "Yes", xid: 100, tc: "No", aa: "No"),
            network: NetworkContextReport(
                ipVersion: 4, sourceIP: "192.168.1.10", destIP: "1.1.1.1",
                sourcePort: 54321, destPort: 53, direction: "Outbound Query"))
        let result = CageRedundancyCorrelator.correlate(daemon: [noQuestion], cage: [noQuestion])
        // No questions extracted → both effectively empty → incomparable.
        if case .incomparable = result {
            // expected
        } else {
            XCTFail("Expected .incomparable when no questions parseable, got \(result)")
        }
    }

    // MARK: - Timestamp delta recording

    /// FAILURE GUARDED: matched queries with different timestamps record the delta.
    func testTimestampDeltaIsRecorded() {
        let daemon = [recordAt(qname: "example.com", xid: 100, offsetSeconds: 1.5)]
        let cage = [recordAt(qname: "example.com", xid: 100, offsetSeconds: 0.0)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .agree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 1)
            let delta = evidence.matched.first?.deltaMs
            XCTAssertNotNil(delta)
            // daemon(1.5s) - cage(0.0s) = 1500 ms
            XCTAssertEqual(delta!, 1500, accuracy: 100, "delta should be ~1500 ms")
        } else {
            XCTFail("Expected .agree, got \(result)")
        }
    }

    /// FAILURE GUARDED: when either timestamp is unparsable, delta is nil — never fabricated
    /// against a missing clock.
    func testUnparsableTimestampProducesNilDelta() {
        let daemon = [record(qname: "example.com", xid: 100, timestamp: "not-a-date")]
        let cage = [record(qname: "example.com", xid: 100, timestamp: "2026-08-10 12:00:00.000")]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .agree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 1)
            XCTAssertNil(evidence.matched.first?.deltaMs,
                         "delta must be nil when a timestamp is unparsable")
        } else {
            XCTFail("Expected .agree, got \(result)")
        }
    }

    // MARK: - QR-based outbound detection

    /// FAILURE GUARDED: a record with `qr = "Query"` but no "Outbound Query" direction is still
    /// treated as outbound (either signal is accepted).
    func testQRQueryIsTreatedAsOutbound() {
        let daemon = [record(qname: "example.com", xid: 100,
                             direction: "Some Other Direction", qr: "Query")]
        let cage = [record(qname: "example.com", xid: 100)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .agree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 1)
        } else {
            XCTFail("Expected .agree when qr=Query, got \(result)")
        }
    }

    // MARK: - Honesty-spine invariants

    /// FAILURE GUARDED: any daemon-only, cage-only, or xid-drift prevents .agree.
    /// Even one unmatched query is a disagreement — never averaged away.
    func testAnyDisagreementPreventsAgree() {
        // One extra daemon-only query alongside a match.
        let daemon = [
            record(qname: "shared.com", xid: 100),
            record(qname: "extra.com", xid: 200)
        ]
        let cage = [record(qname: "shared.com", xid: 100)]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .disagree(let evidence) = result {
            XCTAssertEqual(evidence.matchedCount, 1)
            XCTAssertEqual(evidence.daemonOnlyCount, 1)
        } else {
            XCTFail("Expected .disagree when extra daemon query exists, got \(result)")
        }
    }

    /// FAILURE GUARDED: matched results are sorted by xid for deterministic output.
    func testMatchedResultsAreSortedByXid() {
        let daemon = [
            record(qname: "b.com", xid: 200),
            record(qname: "a.com", xid: 100)
        ]
        let cage = [
            record(qname: "b.com", xid: 200),
            record(qname: "a.com", xid: 100)
        ]
        let result = CageRedundancyCorrelator.correlate(daemon: daemon, cage: cage)
        if case .agree(let evidence) = result {
            XCTAssertEqual(evidence.matched.count, 2)
            XCTAssertLessThan(evidence.matched[0].key.xid, evidence.matched[1].key.xid)
        } else {
            XCTFail("Expected .agree, got \(result)")
        }
    }

    // MARK: - Codable round-trip

    /// FAILURE GUARDED: CageQueryKey is Codable + Equatable.
    func testCageQueryKeyCodableRoundTrip() throws {
        let key = CageQueryKey(qname: "example.com", qtype: "A", xid: 42)
        let data = try JSONEncoder().encode(key)
        let decoded = try JSONDecoder().decode(CageQueryKey.self, from: data)
        XCTAssertEqual(decoded, key)
    }

    /// FAILURE GUARDED: CageRedundancyResult is Codable — the incomparable case round-trips.
    func testCageRedundancyResultCodable_incomparable() throws {
        let result: CageRedundancyResult = .incomparable(reason: "test reason")
        let data = try JSONEncoder().encode(result)
        let decoded = try JSONDecoder().decode(CageRedundancyResult.self, from: data)
        XCTAssertEqual(decoded, result)
    }

    // MARK: - outboundQueries (internal helper)

    /// FAILURE GUARDED: outboundQueries filters correctly — only outbound queries appear.
    func testOutboundQueriesFiltersCorrectly() {
        let outbound = record(qname: "out.com", xid: 1)
        let inbound = record(qname: "in.com", xid: 2, direction: "Inbound Response", qr: "Response")
        let queries = CageRedundancyCorrelator.outboundQueries([outbound, inbound])
        XCTAssertEqual(queries.count, 1)
        XCTAssertEqual(queries.first?.key.qname, "out.com")
    }
}
