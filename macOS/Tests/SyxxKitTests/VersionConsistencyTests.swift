import XCTest
@testable import SyxxKit

/// One binary must report one version.
///
/// `SyxxCLI.printVersion()` hardcoded "Syxx 1.0.0" while `SyxxConfig.version` was "2.0.0", so
/// `syxx --version` and `syxx nslook --version` — the latter routing through
/// `SyxxConfig.handleGlobalFlags` — printed DIFFERENT versions from the same executable. A tool
/// that misreports its own version makes every field bug report ambiguous: you cannot tell which
/// build a user is running, so you cannot tell whether a fix shipped.
///
/// Caught by a real run, not by the suite, which is why this test now exists.
final class VersionConsistencyTests: XCTestCase {

    /// The canonical version must be a real semantic version, not a placeholder. Failure case:
    /// reset it to "1.0.0" while the product is 2.x, or to an empty string → red.
    func testCanonicalVersionIsWellFormed() {
        let v = SyxxConfig.version
        let parts = v.split(separator: ".")

        XCTAssertEqual(parts.count, 3, "version must be MAJOR.MINOR.PATCH, got \(v)")
        XCTAssertTrue(parts.allSatisfy { Int($0) != nil }, "every component must be numeric, got \(v)")
        XCTAssertFalse(v.hasPrefix("0."), "a shipping build must not claim a 0.x version")
    }

    /// `handleGlobalFlags` is the path every subcommand's `--version` takes, and it interpolates
    /// `SyxxConfig.version` rather than a literal (SyxxConfig.swift:51). This pins that it HANDLES
    /// the flag at all — the contract the CLI's own `printVersion()` broke by printing its own
    /// hardcoded string instead of routing here.
    /// Failure case: stop recognising `--version` → the subcommand path stops reporting → red.
    func testGlobalFlagHandlerRecognisesVersionFlags() {
        XCTAssertTrue(SyxxConfig.handleGlobalFlags(["syxx", "--version"], tool: "syxx"),
                      "--version must be handled centrally so no subcommand can invent its own")
        XCTAssertTrue(SyxxConfig.handleGlobalFlags(["syxx", "-V"], tool: "syxx"))
        XCTAssertFalse(SyxxConfig.handleGlobalFlags(["syxx", "nslook"], tool: "syxx"),
                       "an ordinary argument must not be swallowed as a global flag")
    }

    /// The regression itself: no source file may hardcode a version string that contradicts the
    /// canonical one. `SyxxCLI.printVersion()` printed "Syxx 1.0.0" while this said "2.0.0", so one
    /// binary reported two versions depending on which flag you used.
    /// Failure case: reintroduce any literal "Syxx <x.y.z>" that is not the canonical version → red.
    func testNoSourceFileHardcodesAContradictoryVersion() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .appendingPathComponent("Sources")

        let canonical = SyxxConfig.version
        let pattern = try NSRegularExpression(pattern: #"Syxx (\d+\.\d+\.\d+)"#)
        var offenders: [String] = []

        guard let walker = FileManager.default.enumerator(at: root, includingPropertiesForKeys: nil) else {
            return XCTFail("cannot enumerate \(root.path) — a scan that reaches no file must not read as a clean scan")
        }
        let files = walker.compactMap { $0 as? URL }.filter { $0.pathExtension == "swift" }

        var scanned = 0
        for file in files {
            // An unreadable source file used to be skipped here, which shrank the scan silently:
            // the offender that could not be read is exactly the one this test exists to catch.
            let text: String
            do { text = try String(contentsOf: file, encoding: .utf8) } catch {
                XCTFail("cannot read \(file.path) as UTF-8 (\(error)) — the version scan must not skip a source file")
                continue
            }
            let range = NSRange(text.startIndex..., in: text)
            for m in pattern.matches(in: text, range: range) {
                guard let r = Range(m.range(at: 1), in: text) else {
                    XCTFail("\(file.lastPathComponent): matched \"Syxx <version>\" but its capture group did not map back to the source")
                    continue
                }
                let found = String(text[r])
                if found != canonical {
                    offenders.append("\(file.lastPathComponent): \"Syxx \(found)\" != canonical \(canonical)")
                }
            }
            scanned += 1
        }

        // Same rule the warp corpus oracle follows: a scan proves how many files it read, or its
        // green means nothing.
        print("[version scan] scanned \(scanned) of \(files.count) Swift sources under \(root.path)")
        XCTAssertGreaterThan(scanned, 0, "no Swift source was scanned — a vacuous pass, not a clean tree")
        XCTAssertEqual(scanned, files.count, "every discovered source must be scanned; \(scanned) of \(files.count) were")
        XCTAssertTrue(offenders.isEmpty,
                      "hardcoded version(s) contradicting SyxxConfig.version:\n" + offenders.joined(separator: "\n"))
    }
}
