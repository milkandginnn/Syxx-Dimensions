import XCTest
@testable import SyxxKit

/// B254 — `syxx witness report` must VERIFY a snapshot before it renders one.
///
/// Measured on this host BEFORE the fix (2026-07-29), using the B253 display-mismatch fixture
/// (a snapshot whose displayed answer was edited after signing):
///
///     syxx witness verify tampered-display.json   ->  "❌ DISPLAY MISMATCH"        exit 1
///     syxx witness report tampered-display.json   ->  "SYXX SIGNED INCIDENT REPORT"
///                                                     "Local Answer(s): 203.0.113.66"
///                                                     "⚠ Tamper-evident: altering any data
///                                                      above will break the Ed25519 signature"
///                                                                                  exit 0
///
/// `verify` did the right thing; `report` simply never called it. The command printed the
/// ATTACKER's answers under a header that asserts integrity, told the reader the content was
/// tamper-evident, and exited 0. That is not a missing feature, it is an affirmative false claim,
/// and the artifact is meant to be handed to an ISP, a regulator, or a court.
///
/// THESE TESTS SPAWN THE REAL `syxx` BINARY AND ASSERT ON THE PROCESS EXIT CODE, for the same
/// reason B253's do: an in-process unit test on a helper's return value would not have caught this,
/// because the defect was the ABSENCE of a call, in the gap between the function and the process.
///
/// Exit-code contract, shared with `verify` (`SyxxCLI.swift:7`, `WitnessExitStatus`):
/// VALID 0 | DISPLAY MISMATCH 1 | INVALID SIGNATURE 1 | STALE 2 | EXPIRED 2 | malformed 3 |
/// missing 3. 1 means THREAT DETECTED, so a tampered snapshot exiting 1 is correct and deliberate.
final class WitnessReportVerificationTests: XCTestCase {

    private var dir: URL!
    private var validPath: String!
    private var privateKey: String!

    /// The answer an attacker substitutes for the signed one. Asserted ABSENT from every refusal.
    private static let attackerAnswer = "203.0.113.66"

    override func setUpWithError() throws {
        try super.setUpWithError()
        dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("b254-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        let (priv, _) = WitnessSigner.generateKeyPair()
        privateKey = priv
        let snapshot = try WitnessSigner.createSignedSnapshot(
            domain: "example.com",
            comparison: Self.honestComparison(),
            privateKeyBase64: priv)
        validPath = try write(snapshot: snapshot, named: "valid.json")
    }

    override func tearDownWithError() throws {
        if let dir { try? FileManager.default.removeItem(at: dir) }
        try super.tearDownWithError()
    }

    // MARK: - The defect

    /// THE DEFECT ITSELF. A snapshot whose displayed answer was edited after signing must not be
    /// rendered as a report at all, and must exit 1 (THREAT DETECTED).
    ///
    /// FAILURE CASE / MUTATION: delete the `verifySnapshotDetailed` guard in the `report` branch of
    /// SyxxWitness.swift and this test goes RED on every assertion — the header comes back, the
    /// attacker's answer comes back, and the code drops to 0.
    func testTamperedDisplayIsRefusedNotRendered() throws {
        let path = try tamperedDisplayFixture(named: "tampered-display.json")
        let run = try runSyxx(["witness", "report", path])

        XCTAssertEqual(run.code, 1,
            "a display-tampered snapshot must exit 1 (threat), never 0.\n\(run.output)")
        XCTAssertFalse(run.output.contains("SIGNED INCIDENT REPORT"),
            "the report header asserts integrity and must NOT appear for unverified evidence.\n\(run.output)")
        XCTAssertFalse(run.output.contains(Self.attackerAnswer),
            "the attacker's substituted answer must not be printed anywhere.\n\(run.output)")
        XCTAssertTrue(run.output.contains("DISPLAY MISMATCH"),
            "the user must be told plainly what failed.\n\(run.output)")
    }

    /// A forged Ed25519 signature is the other half of "tampered" and shares code 1. Same refusal.
    func testForgedSignatureIsRefusedNotRendered() throws {
        let path = try mutateJSON(named: "tampered-sig.json") { root in
            var sig = Data(base64Encoded: root["signature"] as! String)!
            sig[0] ^= 0xFF
            root["signature"] = sig.base64EncodedString()
        }
        let run = try runSyxx(["witness", "report", path])

        XCTAssertEqual(run.code, 1, "a forged signature must exit 1.\n\(run.output)")
        XCTAssertFalse(run.output.contains("SIGNED INCIDENT REPORT"), run.output)
        XCTAssertTrue(run.output.contains("INVALID SIGNATURE"), run.output)
    }

    /// The refusal must leak NOTHING that came out of the snapshot. Once verification has failed,
    /// every field in the file is attacker-controlled, and this output is a shareable artifact that
    /// gets screenshotted and forwarded stripped of its banner.
    ///
    /// FAILURE CASE: reinstate any snapshot-derived line (signature, key ID, public key, domain,
    /// conclusion, answers) in `printReportRefusal` and this goes RED.
    func testRefusalLeaksNoSnapshotContent() throws {
        let path = try tamperedDisplayFixture(named: "leak-check.json")
        let snapshot = try JSONDecoder().decode(
            WitnessSnapshot.self, from: try Data(contentsOf: URL(fileURLWithPath: path)))
        let run = try runSyxx(["witness", "report", path])

        for forbidden in [snapshot.signature, snapshot.keyId, snapshot.publicKey,
                          snapshot.targetDomain, Self.attackerAnswer, "93.184.216.34",
                          "DIVERGES", "Tamper-evident"] {
            XCTAssertFalse(run.output.contains(forbidden),
                "refusal output must not echo attacker-controlled snapshot content: \(forbidden)\n\(run.output)")
        }
        // What it SHOULD contain: only the path the user typed, and Syxx's own finding.
        XCTAssertTrue(run.output.contains(path), run.output)
        XCTAssertTrue(run.output.contains("REFUSED"), run.output)
    }

    // MARK: - The inconclusive band

    /// A snapshot outside the caller's freshness window cannot be adjudicated. Not clean, and not
    /// an accusation of tampering either: code 2, and still no report.
    func testStaleSnapshotIsRefusedInconclusive() throws {
        let run = try runSyxx(["witness", "report", validPath, "--max-age", "0"])
        XCTAssertEqual(run.code, 2,
            "a stale snapshot is inconclusive, not clean and not forged.\n\(run.output)")
        XCTAssertFalse(run.output.contains("SIGNED INCIDENT REPORT"), run.output)
        XCTAssertTrue(run.output.contains("STALE"), run.output)
    }

    /// An honestly-signed snapshot whose own expiry has passed. Reachable with no flags at all, so
    /// this covers the inconclusive band even for a caller that passes nothing.
    func testExpiredSnapshotIsRefusedInconclusive() throws {
        let expired = try WitnessSigner.createSignedSnapshot(
            domain: "example.com",
            comparison: Self.honestComparison(),
            privateKeyBase64: privateKey,
            expiresAt: Date(timeIntervalSinceNow: -3600))
        let path = try write(snapshot: expired, named: "expired.json")

        let run = try runSyxx(["witness", "report", path])
        XCTAssertEqual(run.code, 2, "an expired snapshot must not exit 0.\n\(run.output)")
        XCTAssertFalse(run.output.contains("SIGNED INCIDENT REPORT"), run.output)
        XCTAssertTrue(run.output.contains("EXPIRED"), run.output)
    }

    // MARK: - Ordering

    /// Verification must run BEFORE the `--conclusion` file is read. Otherwise a tampered snapshot
    /// paired with an unreadable conclusion reports 3 ("could not open a file") instead of 1
    /// ("this evidence is forged"), and the more serious finding is masked by the lesser one.
    ///
    /// FAILURE CASE / MUTATION: move the verification guard below the `--conclusion` load and this
    /// reads 3.
    func testTamperingIsReportedEvenWhenTheConclusionFileIsUnreadable() throws {
        let path = try tamperedDisplayFixture(named: "tampered-with-bad-conclusion.json")
        let run = try runSyxx(["witness", "report", path,
                               "--conclusion", dir.appendingPathComponent("nope.json").path])
        XCTAssertEqual(run.code, 1,
            "forgery must not be downgraded to an operational error by a second failure.\n\(run.output)")
        XCTAssertTrue(run.output.contains("DISPLAY MISMATCH"), run.output)
    }

    // MARK: - The happy path must survive

    /// A snapshot that verifies still renders exactly as before, and still exits 0. The fix must
    /// not have turned the command into one that refuses everything.
    func testValidSnapshotStillRendersAndExitsZero() throws {
        let run = try runSyxx(["witness", "report", validPath])
        XCTAssertEqual(run.code, 0, "a verified snapshot must still render.\n\(run.output)")
        XCTAssertTrue(run.output.contains("SIGNED INCIDENT REPORT"), run.output)
        XCTAssertTrue(run.output.contains("93.184.216.34"),
            "the signed answer must still appear in a genuine report.\n\(run.output)")
        XCTAssertFalse(run.output.contains("REFUSED"), run.output)
    }

    /// Unreadable input is an operational error (3), distinct from forgery (1), on this path too.
    func testUnreadableAndMalformedInputExitError() throws {
        let missing = dir.appendingPathComponent("does-not-exist.json").path
        let missingRun = try runSyxx(["witness", "report", missing])
        XCTAssertEqual(missingRun.code, 3, missingRun.output)
        XCTAssertFalse(missingRun.output.contains("SIGNED INCIDENT REPORT"), missingRun.output)

        let malformedPath = dir.appendingPathComponent("malformed.json")
        try Data("{ \"formatVersion\": 2, \"truncated".utf8).write(to: malformedPath)
        let malformedRun = try runSyxx(["witness", "report", malformedPath.path])
        XCTAssertEqual(malformedRun.code, 3, malformedRun.output)
        XCTAssertFalse(malformedRun.output.contains("SIGNED INCIDENT REPORT"), malformedRun.output)
    }

    // MARK: - Fixtures

    /// A comparison in which the local resolver and both public vantage points agree. Synthetic on
    /// purpose: these tests assert exit codes and rendering, not this host's DNS reachability.
    private static func honestComparison() -> SyxxKit.ComparisonResult {
        let answer = [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")]
        return SyxxKit.ComparisonResult(
            localServer: "192.0.2.53",
            localRecords: answer,
            publicResolverResults: ["Cloudflare": answer, "Google": answer],
            isRedirectionDetected: false)
    }

    /// The B253 display-mismatch mutation: the attacker swaps the answer the READER sees while
    /// leaving the signed evidence block alone, and flips the finding to match.
    private func tamperedDisplayFixture(named name: String) throws -> String {
        try mutateJSON(named: name) { root in
            var comparison = root["comparison"] as! [String: Any]
            var localOutcome = comparison["localOutcome"] as! [String: Any]
            var records = localOutcome["records"] as! [[String: Any]]
            records[0]["rdata"] = Self.attackerAnswer
            localOutcome["records"] = records
            comparison["localOutcome"] = localOutcome
            var localRecords = comparison["localRecords"] as! [[String: Any]]
            localRecords[0]["rdata"] = Self.attackerAnswer
            comparison["localRecords"] = localRecords
            comparison["isRedirectionDetected"] = true
            root["comparison"] = comparison
        }
    }

    private func write(snapshot: WitnessSnapshot, named name: String) throws -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let url = dir.appendingPathComponent(name)
        try encoder.encode(snapshot).write(to: url)
        return url.path
    }

    /// Copy the valid snapshot, apply an attacker-style edit to the raw JSON, and return the path.
    private func mutateJSON(named name: String, _ mutate: (inout [String: Any]) -> Void) throws -> String {
        let data = try Data(contentsOf: URL(fileURLWithPath: validPath))
        var root = try JSONSerialization.jsonObject(with: data) as! [String: Any]
        mutate(&root)
        let url = dir.appendingPathComponent(name)
        try JSONSerialization.data(withJSONObject: root, options: [.sortedKeys]).write(to: url)
        return url.path
    }

    // MARK: - Process harness

    private struct Run { let code: Int32; let output: String }

    /// Run the real `syxx` binary produced by this build and return its PROCESS exit code.
    private func runSyxx(_ args: [String]) throws -> Run {
        let binary = try Self.syxxBinaryURL()
        let p = Process()
        p.executableURL = binary
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        try p.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return Run(code: p.terminationStatus, output: String(data: data, encoding: .utf8) ?? "")
    }

    /// `swift test` builds every product in the package, so `syxx` sits next to the test bundle.
    /// If it is genuinely absent the tests FAIL rather than skip: a silently-skipped exit-code test
    /// is indistinguishable from a passing one, which is how this defect class survives.
    private static func syxxBinaryURL() throws -> URL {
        let bundleDir = Bundle(for: WitnessReportVerificationTests.self).bundleURL
            .deletingLastPathComponent()
        var candidates = [bundleDir.appendingPathComponent("syxx")]
        candidates.append(bundleDir.deletingLastPathComponent().appendingPathComponent("syxx"))
        for candidate in candidates where FileManager.default.isExecutableFile(atPath: candidate.path) {
            return candidate
        }
        throw NSError(domain: "WitnessReportVerificationTests", code: 1, userInfo: [
            NSLocalizedDescriptionKey:
                "the `syxx` executable was not found next to the test bundle at \(bundleDir.path); "
                + "run `swift build` first. Failing rather than skipping on purpose."
        ])
    }
}
