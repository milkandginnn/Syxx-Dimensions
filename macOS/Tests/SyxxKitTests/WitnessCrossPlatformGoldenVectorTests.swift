import XCTest
import Foundation
@testable import SyxxKit

/// [B251] macOS half of the cross-platform witness GOLDEN VECTOR.
///
/// Loads the checked-in macOS-produced snapshots in `corpus/witness-crossplatform/` — the same
/// files the Windows half (Windows/dotNET/Syxx.Tests/WitnessCrossPlatformGoldenVectorTests.cs)
/// loads — and asserts this platform still reads and verifies them.
///
/// Why a shared artifact and not two unit tests: B251 was invisible to BOTH suites while both were
/// green, because neither suite ever read a file the other platform wrote. `syxx witness verify`
/// on Windows could not so much as parse a macOS snapshot ("The JSON value could not be converted
/// to Syxx.Kit.DNSResponseStatus. Path: $.comparison.dohOutcomes.Cloudflare.status") and nothing in
/// either test run said a word. The defect lives in the seam; only an artifact both suites read
/// covers the seam.
///
/// A missing or empty fixture fails the test. A vacuously-green parity test is the green that lies.
final class WitnessCrossPlatformGoldenVectorTests: XCTestCase {

    private static let vectorNames = [
        "witness-macos-degraded.json",
        "witness-macos-answers.json",
        // B252: v2 legacy answers snapshot — a verbatim copy of the pre-v4 file. Both platforms'
        // verifiers must keep accepting it (v2 = errorMessage inside signed bytes, no canonical
        // DoH) while v4 fixtures carry the new canonical fields.
        "witness-macos-answers-v2legacy.json",
    ]

    private static func corpusDir() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
            .appendingPathComponent("corpus")
            .appendingPathComponent("witness-crossplatform")
    }

    private func rawVector(_ name: String) throws -> Data {
        let url = Self.corpusDir().appendingPathComponent(name)
        guard FileManager.default.fileExists(atPath: url.path) else {
            XCTFail("witness golden vector missing at \(url.path)")
            throw CocoaError(.fileNoSuchFile)
        }
        let data = try Data(contentsOf: url)
        guard !data.isEmpty else {
            XCTFail("witness golden vector \(name) is empty — refusing to pass vacuously")
            throw CocoaError(.fileReadCorruptFile)
        }
        return data
    }

    /// The production read path: a bare `JSONDecoder()`, exactly what
    /// SyxxCLI/Commands/SyxxWitness.swift uses.
    private func load(_ name: String) throws -> WitnessSnapshot {
        try JSONDecoder().decode(WitnessSnapshot.self, from: try rawVector(name))
    }

    func testEveryVectorVerifies() throws {
        for name in Self.vectorNames {
            let snapshot = try load(name)
            XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snapshot), .valid,
                           "\(name) must verify on the platform that produced it")
        }
    }

    func testDisplayTimestampProjectsOntoTheSignedMillisecond() throws {
        // This equality IS the D083 guard. It is asserted here as well so a future change to the
        // date encoding cannot satisfy `verifySnapshotDetailed` for some other reason.
        for name in Self.vectorNames {
            let snapshot = try load(name)
            XCTAssertEqual(UInt64(snapshot.timestamp.timeIntervalSince1970 * 1000),
                           snapshot.evidence.timestamp, "\(name)")
            XCTAssertEqual(snapshot.formatVersion, snapshot.evidence.formatVersion, "\(name)")
            XCTAssertEqual(snapshot.targetDomain, snapshot.evidence.queryId, "\(name)")
        }
    }

    func testVectorSetCoversEveryStatusWireSpelling() throws {
        // Guards the fixtures: if a later edit narrows them to the handful of statuses a lucky
        // capture contained, a reader that mishandles the rest would sail through on both platforms.
        var seen = Set<DNSResponseStatus>()
        for name in Self.vectorNames {
            let s = try load(name)
            seen.insert(s.comparison.localOutcome.status)
            s.comparison.publicResolverOutcomes.values.forEach { seen.insert($0.status) }
            (s.comparison.dohOutcomes ?? [:]).values.forEach { seen.insert($0.status) }
        }
        let all: Set<DNSResponseStatus> = [
            .success, .noData, .nxDomain, .servFail, .refused,
            .timeout, .truncated, .responseIdMismatch, .malformedResponse, .transportError
        ]
        XCTAssertEqual(all.subtracting(seen), [], "golden vectors no longer exercise these statuses")
    }

    func testStatusesAreOnTheWireAsSwiftRawValues() throws {
        // The wire contract Windows now reads. If a Swift rawValue is ever renamed, the .NET reader
        // rejects the value outright, so pin the exact strings from this side too.
        let json = try JSONSerialization.jsonObject(with: try rawVector("witness-macos-degraded.json"))
        let root = try XCTUnwrap(json as? [String: Any])
        let comparison = try XCTUnwrap(root["comparison"] as? [String: Any])
        let doh = try XCTUnwrap(comparison["dohOutcomes"] as? [String: Any])
        let cloudflare = try XCTUnwrap(doh["Cloudflare"] as? [String: Any])
        // The literal path from the Windows failure report.
        XCTAssertEqual(cloudflare["status"] as? String, "transportError")
        let local = try XCTUnwrap(comparison["localOutcome"] as? [String: Any])
        XCTAssertEqual(local["status"] as? String, "timeout")
        // Error text uses the macOS field name; Windows was taught to read and write this one.
        XCTAssertNotNil(local["errorDescription"] as? String)
        XCTAssertNil(local["errorMessage"])
    }

    // MARK: - Failure cases

    func testTamperingTheDisplayedAnswerIsRejected() throws {
        // The answer set is signed evidence; rewriting any displayed rdata must be caught.
        for name in Self.vectorNames {
            let snapshot = try load(name)
            var outcomes = snapshot.comparison.publicResolverOutcomes
            let key = try XCTUnwrap(outcomes.keys.sorted().first)
            let injected = DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: "6.6.6.6")
            outcomes[key] = ResolverQueryOutcome(records: [injected],
                                                 status: outcomes[key]!.status,
                                                 errorDescription: outcomes[key]!.errorDescription)
            let tampered = WitnessSnapshot(
                formatVersion: snapshot.formatVersion,
                timestamp: snapshot.timestamp,
                expiresAt: snapshot.expiresAt,
                targetDomain: snapshot.targetDomain,
                comparison: ComparisonResult(
                    localServer: snapshot.comparison.localServer,
                    localRecords: snapshot.comparison.localRecords,
                    publicResolverResults: outcomes.mapValues(\.records),
                    isRedirectionDetected: snapshot.comparison.isRedirectionDetected,
                    isInconclusive: snapshot.comparison.isInconclusive,
                    isDegraded: snapshot.comparison.isDegraded,
                    localOutcome: snapshot.comparison.localOutcome,
                    publicResolverOutcomes: outcomes,
                    dohOutcomes: snapshot.comparison.dohOutcomes),
                evidence: snapshot.evidence,
                signature: snapshot.signature,
                publicKey: snapshot.publicKey,
                keyId: snapshot.keyId)
            XCTAssertNotEqual(WitnessSigner.verifySnapshotDetailed(tampered), .valid, name)
        }
    }

    func testTamperingTheDisplayTimestampIsRejected() throws {
        for name in Self.vectorNames {
            let snapshot = try load(name)
            let tampered = WitnessSnapshot(
                formatVersion: snapshot.formatVersion,
                timestamp: snapshot.timestamp.addingTimeInterval(60),
                expiresAt: snapshot.expiresAt,
                targetDomain: snapshot.targetDomain,
                comparison: snapshot.comparison,
                evidence: snapshot.evidence,
                signature: snapshot.signature,
                publicKey: snapshot.publicKey,
                keyId: snapshot.keyId)
            XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(tampered), .displayMismatch, name)
        }
    }

    func testUnknownStatusIsRejectedRatherThanDefaulted() throws {
        // A hostile or corrupt snapshot must not decode into a run where every resolver answered.
        // Swift has no implicit default for a String-raw enum, so this must throw.
        let raw = try rawVector("witness-macos-degraded.json")
        let text = try XCTUnwrap(String(data: raw, encoding: .utf8))
        let mutated = text.replacingOccurrences(of: "\"transportError\"", with: "\"someStatusFromTheFuture\"")
        XCTAssertThrowsError(try JSONDecoder().decode(WitnessSnapshot.self,
                                                      from: Data(mutated.utf8)))
    }

    func testKnownGap_B252_rewritingDoHEvidenceIsNeverValid() throws {
        // B252, post-fix characterization. `comparison.dohOutcomes` is the DISPLAY mirror of the
        // DoH evidence; the display-integrity rebuild must reject a rewritten block. (At v2 the
        // canonical `evidence.comparisonResult.dohOutcomes` is nil, so this rewrite could still
        // verify — B252's defect — and the display guard was the only backstop. With v4 canonical
        // DoH signing the signature-level guard is asserted separately in
        // testTamperingCanonicalDoHEvidenceIsRejected.) A rewritten DoH block must NEVER read
        // Valid.
        let snapshot = try load("witness-macos-degraded.json")
        var doh = try XCTUnwrap(snapshot.comparison.dohOutcomes)
        let key = try XCTUnwrap(doh.keys.sorted().first)
        doh[key] = ResolverQueryOutcome(
            records: [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "6.6.6.6")],
            status: .success,
            errorDescription: nil)
        let rewritten = WitnessSnapshot(
            formatVersion: snapshot.formatVersion,
            timestamp: snapshot.timestamp,
            expiresAt: snapshot.expiresAt,
            targetDomain: snapshot.targetDomain,
            comparison: ComparisonResult(
                localServer: snapshot.comparison.localServer,
                localRecords: snapshot.comparison.localRecords,
                publicResolverResults: snapshot.comparison.publicResolverResults,
                isRedirectionDetected: snapshot.comparison.isRedirectionDetected,
                isInconclusive: snapshot.comparison.isInconclusive,
                isDegraded: snapshot.comparison.isDegraded,
                localOutcome: snapshot.comparison.localOutcome,
                publicResolverOutcomes: snapshot.comparison.publicResolverOutcomes,
                dohOutcomes: doh),
            evidence: snapshot.evidence,
            signature: snapshot.signature,
            publicKey: snapshot.publicKey,
            keyId: snapshot.keyId)
        XCTAssertNotEqual(WitnessSigner.verifySnapshotDetailed(rewritten), .valid)
    }

    // MARK: - Combined golden vectors (Phase 0)
    //
    // These read the checked-in corrupted artifacts — deliberately NOT members of `vectorNames` (the
    // "must be .valid" set). Each combines two conditions so a crypto-LAST verifier's non-crypto
    // verdict (`.expired`) is caught masking the real one. macOS is crypto-first and reads them
    // correctly today; the Windows/Linux Kit reads `.expired` until Phase 1a lands.
    // See docs/PROPOSAL-witness-crypto-first-2026-08-03.md §4.

    func testGVA_ForgedExpired_IsInvalidSignature_NotExpired() throws {
        // GV-A: forged signature (sig[0]^=0xFF) over a snapshot whose signed ExpiresAt is past.
        let snapshot = try load("witness-forged-expired.json")
        let v = WitnessSigner.verifySnapshotDetailed(snapshot)
        XCTAssertEqual(v, .invalidSignature,
                       "a forged signature must be reported before the (masking) expiry verdict")
        XCTAssertNotEqual(v, .expired, "the expiry must never mask the forgery")
    }

    func testGVB_DisplayTamperedExpired_IsDisplayMismatch_NotExpired() throws {
        // GV-B: honest signature + past ExpiresAt + display timestamp edited 60s post-sign.
        let snapshot = try load("witness-display-tampered-expired.json")
        let v = WitnessSigner.verifySnapshotDetailed(snapshot)
        XCTAssertEqual(v, .displayMismatch,
                       "an edited display field must be reported before the (masking) expiry verdict")
        XCTAssertNotEqual(v, .expired, "the expiry must never mask the display tamper")
    }
}
