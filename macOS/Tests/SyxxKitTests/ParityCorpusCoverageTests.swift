import XCTest
@testable import SyxxKit

/// Coverage integrity for the shared corpus at `corpus/parity-vectors/` (track
/// cross-platform-parity_20260805, Task 6.5). Twin of
/// Windows/dotNET/Syxx.Tests/ParityCorpusCoverageTests.cs.
///
/// This is the only Swift test that reads the real corpus. ParityVectorLoaderTests is
/// synthetic-fixture-only by design (see its header), which left the 11 canonical cases read by
/// nothing on this platform: the C# suite has CanonicalCorpusLoadsWhole, macOS had no counterpart,
/// so a case that stopped loading on Swift would first be noticed by a CI job.
///
/// Three expectations, because each catches something the others cannot:
///  1. Every entry in the corpus directory IS a case file. Both loaders skip on extension alone
///     and silently (ParityVectorLoader.swift:195, ParityVectorLoader.cs:197), so a case renamed
///     `.json.orig` costs a case with no message and no count change anywhere downstream. A glob
///     for `*.json` would share that blind spot, so the sweep looks at every entry.
///  2. The case ids equal the committed manifest, in order. A DELETED case is consistent with
///     every count derived from the corpus; only an expectation stored outside the corpus can see
///     it.
///  3. Every case reaches Synthesizer.conclude, counted (Task 5.2 pattern, WarpEngineTests.swift:210).
final class ParityCorpusCoverageTests: XCTestCase {

    private static func repoRoot() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
    }

    private static func corpusDir() -> URL {
        repoRoot().appendingPathComponent("corpus").appendingPathComponent("parity-vectors")
    }

    private static func manifestURL() -> URL {
        repoRoot().appendingPathComponent("corpus").appendingPathComponent("parity-vectors.manifest")
    }

    /// Expectation 1 — no entry in the corpus directory is anything but a case file.
    func testCorpusDirectoryHoldsOnlyCaseFiles() throws {
        let dir = Self.corpusDir()
        let entries = try FileManager.default.contentsOfDirectory(atPath: dir.path)
        XCTAssertFalse(entries.isEmpty, "\(dir.path) is empty — refusing to pass vacuously")

        let strays = entries.filter { ParityVectorLoader.parseCaseFilename($0) == nil }.sorted()
        XCTAssertTrue(strays.isEmpty,
                      """
                      \(dir.path) holds entries that are not PARITY_VECTOR_V1 cases: \
                      \(strays.joined(separator: ", ")). Both loaders skip a non-.json entry \
                      silently (ParityVectorLoader.swift:195, ParityVectorLoader.cs:197), so a \
                      case renamed out of its extension stops counting on both engines with no \
                      message — caseCount, evaluated and the cross-platform diff all agree about \
                      the smaller corpus.
                      """)
    }

    /// Expectation 2 — the corpus is exactly what the committed manifest says it is.
    func testCorpusMatchesTheCommittedManifest() throws {
        let text = try String(contentsOf: Self.manifestURL(), encoding: .utf8)
        var lines = text.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
        if lines.last == "" { lines.removeLast() }
        guard let header = lines.first, header.hasPrefix("caseCount="),
              let declared = Int(header.dropFirst("caseCount=".count)) else {
            return XCTFail("manifest does not start with caseCount=<n>: \(lines.first ?? "<empty>")")
        }
        let expectedIds = Array(lines.dropFirst())
        XCTAssertEqual(declared, expectedIds.count, "manifest header disagrees with its own id list")

        let cases = try ParityVectorLoader.load(directory: Self.corpusDir())
        XCTAssertEqual(cases.map(\.vectorId), expectedIds,
                       """
                       the corpus and corpus/parity-vectors.manifest disagree. A case added, \
                       removed, renamed or reordered without regenerating the manifest is a \
                       coverage change nothing else can see: a deletion leaves caseCount, \
                       evaluated and all four transcripts in perfect agreement about the smaller \
                       corpus. Regenerate with: swift run --package-path macOS syxx \
                       parity-transcript --vectors=corpus/parity-vectors --list-only \
                       > corpus/parity-vectors.manifest
                       """)
    }

    /// Expectation 3 — every case reaches the engine, counted rather than assumed.
    func testEveryCaseReachesTheEngine() throws {
        let cases = try ParityVectorLoader.load(directory: Self.corpusDir())
        XCTAssertFalse(cases.isEmpty, "empty corpus — an empty run must not read as coverage")

        var evaluated = 0
        for c in cases {
            let conclusion = Synthesizer.conclude(from: c.inputs, now: c.pinnedNow)
            XCTAssertEqual(conclusion.sectionStatuses.count, 6, "[\(c.caseFile)] section count")
            evaluated += 1
        }
        print("[parity corpus] evaluated \(evaluated) of \(cases.count) cases in \(Self.corpusDir().path)")
        XCTAssertGreaterThan(evaluated, 0, "no case was evaluated — an empty run must not read as coverage")
        XCTAssertEqual(evaluated, cases.count,
                       "every discovered case must reach the engine; \(evaluated) of \(cases.count) ran")
    }
}
