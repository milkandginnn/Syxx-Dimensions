import XCTest
@testable import SyxxKit

/// B253 remainder — every subcommand must report a machine-readable process result.
///
/// THESE TESTS SPAWN THE REAL `syxx` BINARY AND ASSERT ON THE PROCESS EXIT CODE. Unit-testing
/// only a mapping helper would not have caught the defect: most subcommands previously returned
/// void and the dispatcher forced `exitCode = 0`, so confirmed leaks and incomplete evidence
/// read as clean to automation.
///
/// Contract (`CLIExitStatus` / `WitnessExitStatus`):
/// - 0 completed non-threat
/// - 1 detected threat / tampering where applicable
/// - 2 inconclusive or invalid evidence
/// - 3 operational failure
///
/// Offline-safe cases only. Paths that call `NetworkKeyState.getSystemDNSServers()` can hang in
/// `runProcess` on this host (pre-existing D-class defect, not B253); those mappings are still
/// implemented in the CLI and covered by in-process return values where the command returns before
/// the hang, plus the inventory table in CHANGELOG Phase 2.4.
///
/// Witness `verify` / `report` behavior remains covered by WitnessVerifyExitCodeTests and
/// WitnessReportVerificationTests.
final class CLIExitCodeProcessTests: XCTestCase {

    // MARK: - Result classes reachable without NetworkKeyState

    /// Operational failure (3): missing required arguments.
    func testMisuseExitsFailureNotThreat() throws {
        XCTAssertEqual(try runSyxx(["nslook"]).code, 3)
        XCTAssertEqual(try runSyxx(["mirror"]).code, 3)
        XCTAssertEqual(try runSyxx(["warp"]).code, 3)
        XCTAssertEqual(try runSyxx(["forensics"]).code, 3)
        XCTAssertEqual(try runSyxx(["route", "--not-a-host"]).code, 3)
        XCTAssertEqual(try runSyxx(["definitely-not-a-subcommand"]).code, 3)
        XCTAssertEqual(try runSyxx(["unbound", "not-a-real-action"]).code, 3)
        XCTAssertEqual(try runSyxx(["firstrun", "--local", "1.1.1.1"]).code, 3)
        // Threat code is reserved for findings, never misuse.
        XCTAssertNotEqual(try runSyxx(["nslook"]).code, 1)
    }

    /// Completed non-threat (0): help and version are not network measurements.
    func testHelpAndVersionExitCompleted() throws {
        XCTAssertEqual(try runSyxx(["--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["--version"]).code, 0)
        XCTAssertEqual(try runSyxx(["leak", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["mirror", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["key", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["lan", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["sentinel", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["forensics", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["filter", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["speed", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["unbound", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["cage", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["warp", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["nslook", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["route", "--help"]).code, 0)
        XCTAssertEqual(try runSyxx(["witness", "--help"]).code, 0)
    }

    /// Completed (0): forensics --lists is non-adjudicating inventory work.
    func testForensicsListsExitsCompleted() throws {
        let run = try runSyxx(["forensics", "--lists"])
        XCTAssertEqual(run.code, 0, run.output)
    }

    /// Threat (1) or completed (0): classify returns a typed finding, never the old forced 0-on-void.
    func testForensicsClassifyReturnsTypedResult() throws {
        let run = try runSyxx(["forensics", "--classify", "doubleclick.net", "example.com"])
        XCTAssertTrue(run.code == 0 || run.code == 1,
            "classify must complete (0) or flag (1), got \(run.code)\n\(run.output)")
        XCTAssertNotEqual(run.code, 2)
        XCTAssertNotEqual(run.code, 3, run.output)
    }

    /// Preserve verified witness report behavior: missing file is error 3.
    func testWitnessReportMissingFileStillExitsFailure() throws {
        let missing = FileManager.default.temporaryDirectory
            .appendingPathComponent("b253-missing-\(UUID().uuidString).json").path
        let run = try runSyxx(["witness", "report", missing])
        XCTAssertEqual(run.code, 3, run.output)
    }

    /// FAILURE CASE for the old dispatcher: if `exitCode = 0` is restored after a void
    /// forensics misuse path, this turns red (would get 0 instead of 3).
    func testForensicsMisuseIsNotSilentZero() throws {
        let run = try runSyxx(["forensics"])
        XCTAssertEqual(run.code, 3, run.output)
    }

    // MARK: - Process harness

    private struct Run { let code: Int32; let output: String }

    private func runSyxx(_ args: [String]) throws -> Run {
        let binary = try Self.syxxBinaryURL()
        let p = Process()
        p.executableURL = binary
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        try p.run()
        // Bound hang-prone paths. Offline help/usage paths finish in milliseconds.
        let deadline = Date().addingTimeInterval(20)
        while p.isRunning && Date() < deadline {
            Thread.sleep(forTimeInterval: 0.05)
        }
        if p.isRunning {
            p.terminate()
            p.waitUntilExit()
            throw NSError(domain: "CLIExitCodeProcessTests", code: 2, userInfo: [
                NSLocalizedDescriptionKey: "syxx \(args.joined(separator: " ")) exceeded 20s; treated as hang"
            ])
        }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        return Run(code: p.terminationStatus, output: String(data: data, encoding: .utf8) ?? "")
    }

    private static func syxxBinaryURL() throws -> URL {
        let bundleDir = Bundle(for: CLIExitCodeProcessTests.self).bundleURL
            .deletingLastPathComponent()
        var candidates = [bundleDir.appendingPathComponent("syxx")]
        candidates.append(bundleDir.deletingLastPathComponent().appendingPathComponent("syxx"))
        // Prefer the package product built next to Sources.
        candidates.append(URL(fileURLWithPath: "/Users/masterrabbit/Software/Syxx/macOS/.build/arm64-apple-macosx/debug/syxx"))
        candidates.append(URL(fileURLWithPath: "/Users/masterrabbit/Software/Syxx/macOS/.build/debug/syxx"))
        for candidate in candidates where FileManager.default.isExecutableFile(atPath: candidate.path) {
            return candidate
        }
        throw NSError(domain: "CLIExitCodeProcessTests", code: 1, userInfo: [
            NSLocalizedDescriptionKey:
                "the `syxx` executable was not found next to the test bundle at \(bundleDir.path); "
                + "run `swift build --product syxx` first. Failing rather than skipping on purpose."
        ])
    }
}
