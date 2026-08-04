import XCTest
import Foundation
@testable import SyxxKit

/// [B251/E162] Gated generator for the cross-platform witness golden vectors in
/// `corpus/witness-crossplatform/`. Not part of the standing suite — run with:
///
///     SYXX_GENERATE_FIXTURES=1 swift test --filter WitnessCrossPlatformFixtureGenerator
///
/// The committed artifacts are what BOTH suites verify (macOS
/// `WitnessCrossPlatformGoldenVectorTests`, Windows `WitnessCrossPlatformGoldenVectorTests`).
/// A unit test on the JSON converter alone would not have caught B251: the defect lived in the
/// SEAM between two independently-green suites, so the regression guard has to be one artifact
/// that both suites read.
///
/// PROVENANCE, stated honestly:
///  - `witness-macos-degraded.json` was originally a verbatim copy of the output of a real
///    `syxx witness create example.com` run on 2026-07-29. That capture predated the E011 wire-key
///    rename (`verdict` → `conclusion`) and became undecodable by the current readers, so on
///    2026-08-01 it was regenerated HERE with the real SyxxKit `WitnessSigner` and the real Swift
///    `JSONEncoder`, from stated inputs that mirror the original capture's observed statuses
///    (local + public legs `timeout`, DoH legs `transportError`, no records anywhere, U+2019 in
///    the Foundation error text). The signature and encoding are genuine; the capture itself is
///    not.
///  - `witness-macos-answers.json` is generated here: the real SyxxKit `WitnessSigner` and the real
///    Swift `JSONEncoder` sign and encode a ComparisonResult that is assembled in this file rather
///    than fetched from the network. The signature and the encoding are genuine; the answer data is
///    stated inputs, not a live capture. It exists because the degraded capture (all legs failed)
///    cannot exercise `success`, records, TTL/type arrays, or the nxDomain/servFail/noData/refused
///    wire spellings.
final class WitnessCrossPlatformFixtureGenerator: XCTestCase {

    /// A throwaway key. Only the PUBLIC half is embedded in the fixture, which is all verification
    /// needs; the private half is deliberately not committed anywhere.
    private static let fixturePrivateKey = "5LGFY6cFfd6w/8GeaLwv8IeZ4JoXIwcIXSQDeRTuRCE="

    func testGenerateAnswersGoldenVector() throws {
        guard ProcessInfo.processInfo.environment["SYXX_GENERATE_FIXTURES"] == "1" else {
            throw XCTSkip("Set SYXX_GENERATE_FIXTURES=1 to regenerate corpus/witness-crossplatform/.")
        }
        func rec(_ rdata: String, ttl: UInt32, type: UInt16 = 1) -> DNSRecord {
            DNSRecord(name: "example.com", type: type, ttl: ttl, rdata: rdata)
        }

        // Deliberately covers ALL TEN DNSResponseStatus wire spellings across the three outcome
        // maps, so a reader that mishandles any single one fails this vector rather than passing on
        // the two or three that happen to appear in a lucky capture.
        let local = ResolverQueryOutcome(
            records: [rec("93.184.216.34", ttl: 300), rec("2606:2800:220:1:248:1893:25c8:1946", ttl: 300, type: 28)],
            status: .success,
            errorDescription: nil)

        let publicOutcomes: [String: ResolverQueryOutcome] = [
            "Cloudflare": ResolverQueryOutcome(
                records: [rec("93.184.216.34", ttl: 216)], status: .success, errorDescription: nil),
            "Google": ResolverQueryOutcome(
                records: [rec("93.184.216.34", ttl: 300), rec("2606:2800:220:1:248:1893:25c8:1946", ttl: 120, type: 28)],
                status: .success, errorDescription: nil),
            "Quad9": ResolverQueryOutcome(records: [], status: .noData, errorDescription: nil),
            "OpenDNS": ResolverQueryOutcome(records: [], status: .nxDomain, errorDescription: nil),
            "AdGuard": ResolverQueryOutcome(records: [], status: .servFail, errorDescription: nil),
            "NextDNS": ResolverQueryOutcome(records: [], status: .refused, errorDescription: nil),
            "Control D": ResolverQueryOutcome(records: [], status: .truncated,
                                              errorDescription: "TC bit set and TCP fallback failed"),
        ]

        let dohOutcomes: [String: ResolverQueryOutcome] = [
            "Cloudflare": ResolverQueryOutcome(records: [rec("93.184.216.34", ttl: 60)],
                                               status: .success, errorDescription: nil),
            // Non-ASCII (U+2019) is on the wire in real Foundation error text; it must survive the
            // round trip on both platforms.
            "Google": ResolverQueryOutcome(records: [], status: .transportError,
                                           errorDescription: "The operation couldn’t be completed. (NSURLErrorDomain error -1005.)"),
            "Quad9": ResolverQueryOutcome(records: [], status: .responseIdMismatch,
                                          errorDescription: "response id 0x1a2b did not match query id 0x3c4d"),
            "OpenDNS": ResolverQueryOutcome(records: [], status: .malformedResponse,
                                            errorDescription: "answer section truncated mid-record"),
        ]

        let comparison = ComparisonResult(
            localServer: "10.2.0.1",
            localRecords: local.records,
            publicResolverResults: publicOutcomes.mapValues(\.records),
            isRedirectionDetected: false,
            isInconclusive: false,
            isDegraded: true,
            localOutcome: local,
            publicResolverOutcomes: publicOutcomes,
            dohOutcomes: dohOutcomes)

        let snapshot = try WitnessSigner.createSignedSnapshot(
            domain: "example.com",
            comparison: comparison,
            privateKeyBase64: Self.fixturePrivateKey)

        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snapshot), .valid,
                       "refusing to write a golden vector that does not verify on the platform that produced it")

        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try enc.encode(snapshot)

        let out = Self.corpusDir().appendingPathComponent("witness-macos-answers.json")
        try FileManager.default.createDirectory(at: Self.corpusDir(), withIntermediateDirectories: true)
        try data.write(to: out)
        print("WROTE FIXTURE: \(out.path) (\(data.count) bytes)")
    }

    /// [B252] Regenerates `witness-macos-degraded.json`. Inputs mirror the statuses the original
    /// 2026-07-29 real capture observed: local + public UDP legs timed out, every DoH leg hit a
    /// transport error, no resolver produced records, and Foundation's error text (including
    /// U+2019) is on the wire. The degraded file exists to exercise the all-legs-failed shape and
    /// the `timeout`/`transportError` wire spellings; `witness-macos-answers.json` covers the rest.
    func testGenerateDegradedGoldenVector() throws {
        guard ProcessInfo.processInfo.environment["SYXX_GENERATE_FIXTURES"] == "1" else {
            throw XCTSkip("Set SYXX_GENERATE_FIXTURES=1 to regenerate corpus/witness-crossplatform/.")
        }

        let nwError = "The operation couldn’t be completed. (Network.NWError error 57 - Socket is not connected)"
        let connectionLost = "The network connection was lost."

        let local = ResolverQueryOutcome(
            records: [], status: .timeout, errorDescription: nwError)
        let publicOutcomes: [String: ResolverQueryOutcome] = [
            "Cloudflare": ResolverQueryOutcome(records: [], status: .timeout, errorDescription: nwError),
            "Google": ResolverQueryOutcome(records: [], status: .timeout, errorDescription: nwError),
            "Quad9": ResolverQueryOutcome(records: [], status: .timeout, errorDescription: nwError),
        ]
        let dohOutcomes: [String: ResolverQueryOutcome] = [
            "Cloudflare": ResolverQueryOutcome(records: [], status: .transportError, errorDescription: connectionLost),
            "Google": ResolverQueryOutcome(records: [], status: .transportError, errorDescription: connectionLost),
            "Quad9": ResolverQueryOutcome(records: [], status: .transportError, errorDescription: connectionLost),
        ]

        let comparison = ComparisonResult(
            localServer: "10.2.0.1",
            localRecords: [],
            publicResolverResults: publicOutcomes.mapValues(\.records),
            isRedirectionDetected: false,
            isInconclusive: true,
            isDegraded: false,
            localOutcome: local,
            publicResolverOutcomes: publicOutcomes,
            dohOutcomes: dohOutcomes)

        let snapshot = try WitnessSigner.createSignedSnapshot(
            domain: "example.com",
            comparison: comparison,
            privateKeyBase64: Self.fixturePrivateKey)

        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(snapshot), .valid,
                       "refusing to write a golden vector that does not verify on the platform that produced it")

        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try enc.encode(snapshot)

        let out = Self.corpusDir().appendingPathComponent("witness-macos-degraded.json")
        try FileManager.default.createDirectory(at: Self.corpusDir(), withIntermediateDirectories: true)
        try data.write(to: out)
        print("WROTE FIXTURE: \(out.path) (\(data.count) bytes)")
    }

    // MARK: - Combined golden vectors (Phase 0)
    //
    // Every pre-existing vector exercises expired / forged / untrusted in ISOLATION, so the
    // crypto-LAST ordering bug on Windows/Linux is invisible. These two combine two conditions in a
    // single artifact so the masking is caught: a non-crypto verdict (`.expired`) must never be
    // returned ahead of the cryptographic/display verdict it hides. Neither file is a member of
    // `WitnessCrossPlatformGoldenVectorTests.vectorNames` (the "must be .valid" set) — a corrupted
    // fixture must never be folded into the valid set. See docs/PROPOSAL-witness-crypto-first-2026-08-03.md §4.

    /// A minimal, honestly-signable comparison — the status-coverage vectors above already exercise
    /// every wire spelling, so these combined vectors only need a well-formed payload to sign.
    private static func simpleComparison() -> SyxxKit.ComparisonResult {
        let record = DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: "93.184.216.34")
        return SyxxKit.ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [record],
            publicResolverResults: ["Cloudflare": [record]],
            isRedirectionDetected: false)
    }

    /// [GV-A] FORGED signature over a snapshot whose signed `ExpiresAt` is already in the past.
    /// macOS verifies crypto-FIRST, so it reads `.invalidSignature`; a crypto-LAST verifier reads
    /// `.expired` and masks the forgery. Pins C175 prong 1.
    func testGenerateForgedExpiredGoldenVector() throws {
        guard ProcessInfo.processInfo.environment["SYXX_GENERATE_FIXTURES"] == "1" else {
            throw XCTSkip("Set SYXX_GENERATE_FIXTURES=1 to regenerate corpus/witness-crossplatform/.")
        }
        // Sign HONESTLY with an ExpiresAt already in the past: the pre-corruption record is a genuine
        // but lapsed document, so it verifies as `.expired` (NOT `.valid`) — self-asserted below.
        let honest = try WitnessSigner.createSignedSnapshot(
            domain: "example.com", comparison: Self.simpleComparison(),
            privateKeyBase64: Self.fixturePrivateKey,
            expiresAt: Date(timeIntervalSinceNow: -60))
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(honest), .expired,
                       "precondition: the honest-but-lapsed record reads .expired before corruption")

        // Single documented mutation: flip the first signature byte so the Ed25519 check fails.
        var sigBytes = try XCTUnwrap(Data(base64Encoded: honest.signature))
        sigBytes[0] ^= 0xFF
        let forged = WitnessSnapshot(
            formatVersion: honest.formatVersion, timestamp: honest.timestamp,
            expiresAt: honest.expiresAt, targetDomain: honest.targetDomain,
            comparison: honest.comparison, evidence: honest.evidence,
            signature: sigBytes.base64EncodedString(), publicKey: honest.publicKey, keyId: honest.keyId)
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(forged), .invalidSignature,
                       "crypto-first: a forged signature reads .invalidSignature even when also expired")

        try Self.write(forged, to: "witness-forged-expired.json")
    }

    /// [GV-B] Honestly-signed snapshot with a past `ExpiresAt` whose DISPLAY timestamp was edited
    /// after signing. macOS checks display-integrity before time, so it reads `.displayMismatch`; a
    /// crypto-LAST verifier reads `.expired` and masks the tamper. Pins the display-before-time order.
    func testGenerateDisplayTamperedExpiredGoldenVector() throws {
        guard ProcessInfo.processInfo.environment["SYXX_GENERATE_FIXTURES"] == "1" else {
            throw XCTSkip("Set SYXX_GENERATE_FIXTURES=1 to regenerate corpus/witness-crossplatform/.")
        }
        let honest = try WitnessSigner.createSignedSnapshot(
            domain: "example.com", comparison: Self.simpleComparison(),
            privateKeyBase64: Self.fixturePrivateKey,
            expiresAt: Date(timeIntervalSinceNow: -60))
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(honest), .expired,
                       "precondition: the honest-but-lapsed record reads .expired before tampering")

        // Single documented mutation: shift the DISPLAY timestamp 60s forward, leaving the signed
        // evidence untouched — the D083 display-integrity guard must catch it.
        let tampered = WitnessSnapshot(
            formatVersion: honest.formatVersion, timestamp: honest.timestamp.addingTimeInterval(60),
            expiresAt: honest.expiresAt, targetDomain: honest.targetDomain,
            comparison: honest.comparison, evidence: honest.evidence,
            signature: honest.signature, publicKey: honest.publicKey, keyId: honest.keyId)
        XCTAssertEqual(WitnessSigner.verifySnapshotDetailed(tampered), .displayMismatch,
                       "display-before-time: an edited display timestamp reads .displayMismatch, not .expired")

        try Self.write(tampered, to: "witness-display-tampered-expired.json")
    }

    /// Encodes with the SAME encoder the other fixtures use, so the artifact is byte-compatible with
    /// both readers, and writes it under `corpus/witness-crossplatform/`.
    private static func write(_ snapshot: WitnessSnapshot, to name: String) throws {
        let enc = JSONEncoder()
        enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try enc.encode(snapshot)
        try FileManager.default.createDirectory(at: corpusDir(), withIntermediateDirectories: true)
        let out = corpusDir().appendingPathComponent(name)
        try data.write(to: out)
        print("WROTE FIXTURE: \(out.path) (\(data.count) bytes)")
    }

    static func corpusDir() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
            .appendingPathComponent("corpus")
            .appendingPathComponent("witness-crossplatform")
    }
}
