import XCTest
@testable import SyxxKit

/// B253 — `syxx witness verify` must tell automation the truth through the EXIT STATUS, not only
/// through the printed report.
///
/// Measured on this host before the fix (2026-07-29), against real snapshots:
///
///     macOS   witness verify <valid>                -> exit 2
///     macOS   witness verify <display-mismatch>     -> exit 2
///     macOS   witness verify <unreadable>           -> exit 2
///     macOS   witness --help                        -> exit 2
///
/// Every outcome shared one code, so the code carried no information. Windows was worse: 0 for a
/// TAMPERED snapshot and 0 for an unreadable one, i.e. a CI gate reading `$?` accepted forged
/// evidence as verified.
///
/// THESE TESTS SPAWN THE REAL `syxx` BINARY AND ASSERT ON THE PROCESS EXIT CODE. That is
/// deliberate: a unit test on a helper's return value would not have caught this defect, because
/// the helper was never wired to `SyxxCLI.exitCode` at all. The bug lived in the gap between the
/// function and the process.
///
/// Fixtures are built here with `WitnessSigner` from a synthetic `ComparisonResult`, so the tests
/// need no network. The tamper mutations are applied to the on-disk JSON, exactly as an attacker
/// editing a shared snapshot would.
///
/// Contract under test (`SyxxCLI.swift:7`): 0 = clean, 1 = threat, 2 = inconclusive, 3 = error.
final class WitnessVerifyExitCodeTests: XCTestCase {

    private var dir: URL!
    private var validPath: String!
    private var publicKey: String!

    override func setUpWithError() throws {
        try super.setUpWithError()
        dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("b253-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        let (priv, pub) = WitnessSigner.generateKeyPair()
        publicKey = pub
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

    // MARK: - The result channel

    /// HAPPY PATH: a snapshot whose signature checks out exits 0, and still prints VALID.
    func testValidSnapshotExitsZero() throws {
        let run = try runSyxx(["witness", "verify", validPath])
        XCTAssertEqual(run.code, 0, "a verified snapshot must exit 0.\n\(run.output)")
        XCTAssertTrue(run.output.contains("VALID"),
            "the printed text must be unchanged by the exit-code fix")
    }

    /// THE DEFECT ITSELF: a snapshot whose DISPLAYED answer was edited after signing must exit
    /// non-zero, and specifically must not share a code with an operational error.
    ///
    /// FAILURE CASE: drop the `Exit`/`exitCode` assignment on the verify path and this reads 2
    /// (macOS pre-fix) or 0 (Windows pre-fix) instead of 1.
    func testTamperedDisplayFieldExitsTamperedNotZeroAndNotError() throws {
        let path = try mutateJSON(named: "tampered-display.json") { root in
            var comparison = root["comparison"] as! [String: Any]
            var localOutcome = comparison["localOutcome"] as! [String: Any]
            var records = localOutcome["records"] as! [[String: Any]]
            records[0]["rdata"] = "203.0.113.66"   // attacker swaps the answer the user reads
            localOutcome["records"] = records
            comparison["localOutcome"] = localOutcome
            root["comparison"] = comparison
        }

        let run = try runSyxx(["witness", "verify", path])
        XCTAssertEqual(run.code, 1,
            "a tampered snapshot must exit 1 (threat), never 0.\n\(run.output)")
        XCTAssertNotEqual(run.code, 3,
            "tampering must be distinguishable from an operational error")
        XCTAssertTrue(run.output.contains("DISPLAY MISMATCH"), run.output)
    }

    /// A forged Ed25519 signature is the other half of "tampered" and must share code 1.
    func testForgedSignatureExitsTampered() throws {
        let path = try mutateJSON(named: "tampered-sig.json") { root in
            var sig = Data(base64Encoded: root["signature"] as! String)!
            sig[0] ^= 0xFF
            root["signature"] = sig.base64EncodedString()
        }

        let run = try runSyxx(["witness", "verify", path])
        XCTAssertEqual(run.code, 1, "a forged signature must exit 1.\n\(run.output)")
        XCTAssertTrue(run.output.contains("INVALID SIGNATURE"), run.output)
    }

    /// A snapshot that cannot be adjudicated (here: older than the caller's freshness window) is
    /// INCONCLUSIVE. It must not read clean, and must not be accused of tampering either.
    func testStaleSnapshotExitsInconclusive() throws {
        let run = try runSyxx(["witness", "verify", validPath, "--max-age", "0"])
        XCTAssertEqual(run.code, 2,
            "a snapshot outside the freshness window is inconclusive, not clean and not forged.\n\(run.output)")
        XCTAssertTrue(run.output.contains("STALE"), run.output)
    }

    /// `--detailed` renders differently but must reach the SAME machine-readable conclusion. A
    /// gate must not be able to change the answer by changing the verbosity.
    func testDetailedFlagDoesNotChangeTheExitCode() throws {
        let path = try mutateJSON(named: "tampered-sig-detailed.json") { root in
            var sig = Data(base64Encoded: root["signature"] as! String)!
            sig[0] ^= 0xFF
            root["signature"] = sig.base64EncodedString()
        }
        XCTAssertEqual(try runSyxx(["witness", "verify", validPath, "--detailed"]).code, 0)
        XCTAssertEqual(try runSyxx(["witness", "verify", path, "--detailed"]).code, 1)
    }

    // MARK: - The operational channel

    /// Unreadable input exited 0 on Windows and 2 on macOS. It must be a non-zero OPERATIONAL
    /// code, distinct from the tampering code, so a gate can tell "someone forged this" from
    /// "the file is not there".
    func testUnreadableAndMalformedInputExitError() throws {
        let missing = dir.appendingPathComponent("does-not-exist.json").path
        let missingRun = try runSyxx(["witness", "verify", missing])
        XCTAssertEqual(missingRun.code, 3, "a missing file is an error.\n\(missingRun.output)")

        let malformedPath = dir.appendingPathComponent("malformed.json")
        try Data("{ \"formatVersion\": 2, \"truncated".utf8).write(to: malformedPath)
        let malformedRun = try runSyxx(["witness", "verify", malformedPath.path])
        XCTAssertEqual(malformedRun.code, 3, "unparseable input is an error.\n\(malformedRun.output)")

        XCTAssertNotEqual(missingRun.code, 1)
        XCTAssertNotEqual(malformedRun.code, 1)
    }

    /// Asking for help is not an inconclusive network measurement.
    func testHelpExitsZero() throws {
        XCTAssertEqual(try runSyxx(["witness", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["--version"]).code, 0)
    }

    /// Command misuse is an operational error, not a network finding. In particular it must not
    /// be 1, which the contract reserves for THREAT DETECTED.
    func testMisuseExitsErrorNotThreat() throws {
        XCTAssertEqual(try runSyxx(["witness"]).code, 3)
        XCTAssertEqual(try runSyxx(["witness", "bogus", validPath]).code, 3)
        XCTAssertEqual(try runSyxx(["definitely-not-a-subcommand"]).code, 3)
    }

    // MARK: - Fixtures

    /// A comparison in which the local resolver and both public vantage points agree. Synthetic on
    /// purpose: these tests must assert exit codes, not this host's DNS reachability.
    private static func honestComparison() -> SyxxKit.ComparisonResult {
        let answer = [DNSRecord(name: "example.com", type: 1, ttl: 300, rdata: "93.184.216.34")]
        return SyxxKit.ComparisonResult(
            localServer: "192.0.2.53",
            localRecords: answer,
            publicResolverResults: ["Cloudflare": answer, "Google": answer],
            isRedirectionDetected: false)
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
        try JSONSerialization.data(withJSONObject: root, options: [.sortedKeys])
            .write(to: url)
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
    /// is how this defect survived in the first place.
    private static func syxxBinaryURL() throws -> URL {
        let bundleDir = Bundle(for: WitnessVerifyExitCodeTests.self).bundleURL
            .deletingLastPathComponent()
        var candidates = [bundleDir.appendingPathComponent("syxx")]
        // Tolerate the arch-triple layout (.build/arm64-apple-macosx/debug) as well as .build/debug.
        candidates.append(bundleDir.deletingLastPathComponent().appendingPathComponent("syxx"))
        for candidate in candidates where FileManager.default.isExecutableFile(atPath: candidate.path) {
            return candidate
        }
        throw NSError(domain: "WitnessVerifyExitCodeTests", code: 1, userInfo: [
            NSLocalizedDescriptionKey:
                "the `syxx` executable was not found next to the test bundle at \(bundleDir.path); "
                + "run `swift build` first. Failing rather than skipping on purpose — a skipped "
                + "exit-code test is indistinguishable from a passing one."
        ])
    }
}
