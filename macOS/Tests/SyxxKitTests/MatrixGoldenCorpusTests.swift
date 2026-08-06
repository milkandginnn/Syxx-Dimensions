import XCTest
@testable import SyxxKit

/// F042: Loader for corpus/matrix/** — the frozen spec oracle and the golden vectors for the
/// matrix determinative engine. Before this file, no test on any platform loaded these vectors,
/// so the ~100 hand-transcribed weights and thresholds across SyxxMatrixIntegration.swift had
/// zero mechanical cross-check against the shared oracle.
///
/// Twin of Windows/dotNET/Syxx.Tests/MatrixGoldenCorpusTests.cs (exists and runs).
/// Both platforms must assert:
/// 1. defaultSpecJSON() equals default-spec.frozen.json byte-for-byte
/// 2. Each ENGINE golden vector drives evaluate() and produces the expected DeterminativeResult.
///
/// Restored from macOS/Tests/_quarantine/MatrixGoldenCorpusTests.swift.phase32-compile-break on
/// 2026-08-05. The quarantine was a single type change: `SectionResult.status` moved from
/// `SectionStatus` to `EvidenceUsability`. The DTO key names were also wrong against the corpus
/// as it actually ships (`section`, not `id`; `indicator`, not `id`) — a runtime decode failure
/// that the compile break was masking.
final class MatrixGoldenCorpusTests: XCTestCase {

    /// Vectors in corpus/matrix/vectors/ that are NOT engine input/expected pairs, with the reason.
    /// Named explicitly rather than schema-sniffed: a new vector with an unrecognised shape must
    /// fail this suite, not be silently skipped into a coverage hole.
    private static let nonEngineVectors: Set<String> = [
        // A round6 helper table (banker's rounding boundary ties). Shape is {name, spec, values, why}
        // with no input/expected pair, so it does not drive evaluate(). Its coverage belongs to
        // RoundingParityTests, which is itself quarantined — tracked in the quarantine audit.
        "gv-10-rounding.json",
    ]

    enum CorpusError: Error, CustomStringConvertible {
        case notFound(String)
        case empty
        case malformed(String)
        var description: String {
            switch self {
            case .notFound(let p): return "Matrix golden corpus not found at \(p). The frozen spec oracle is missing."
            case .empty: return "Matrix golden corpus contains no vectors — refusing to run vacuously."
            case .malformed(let reason): return "Matrix golden corpus malformed: \(reason)"
            }
        }
    }

    private static func repoRoot() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
    }

    private static func vectorsDir() -> URL {
        repoRoot()
            .appendingPathComponent("corpus")
            .appendingPathComponent("matrix")
            .appendingPathComponent("vectors")
    }

    private static func jsonFiles() throws -> [String] {
        let dir = vectorsDir()
        guard FileManager.default.fileExists(atPath: dir.path) else {
            throw CorpusError.notFound(dir.path)
        }
        let files = try FileManager.default.contentsOfDirectory(atPath: dir.path)
            .filter { $0.hasSuffix(".json") }
            .sorted()
        guard !files.isEmpty else { throw CorpusError.empty }
        return files
    }

    /// Assert that defaultSpecJSON() equals default-spec.frozen.json byte-for-byte.
    /// A transcription typo in any of the ~100 constants is caught here, not in production.
    func testDefaultSpecMatchesFrozenOracle() throws {
        let oraclePath = Self.repoRoot()
            .appendingPathComponent("corpus")
            .appendingPathComponent("matrix")
            .appendingPathComponent("default-spec.frozen.json")

        guard FileManager.default.fileExists(atPath: oraclePath.path) else {
            throw CorpusError.notFound(oraclePath.path)
        }

        let oracleBytes = try Data(contentsOf: oraclePath)
        guard let oracleRaw = String(data: oracleBytes, encoding: .utf8) else {
            throw CorpusError.malformed("default-spec.frozen.json is not valid UTF-8")
        }
        let oracleString = oracleRaw.replacingOccurrences(of: "\\s+", with: "", options: .regularExpression)

        let specJSON = try SyxxMatrixIntegration.defaultSpecJSON()
        let specString = specJSON.replacingOccurrences(of: "\\s+", with: "", options: .regularExpression)

        XCTAssertEqual(specString, oracleString,
                       "defaultSpecJSON() must match default-spec.frozen.json byte-for-byte (whitespace-normalized)")
    }

    /// F-04: knownSections must be emitted in ordinal-sorted order.
    ///
    /// It is backed by a `Set`, which has no order, so encoding it directly leaks hash order into
    /// the bytes — and Swift seeds its hasher per process, so the emitted spec differed on every
    /// launch. Three consecutive runs of one build produced three different orders before the fix.
    ///
    /// Asserting SORTED rather than merely STABLE is deliberate: two encodings inside one process
    /// share a hash seed and would agree even while the defect was present.
    ///
    /// Twin: MatrixGoldenCorpusTests.DefaultSpecKnownSectionsAreOrdinalSorted (C#).
    func testDefaultSpecKnownSectionsAreOrdinalSorted() throws {
        // defaultSpecJSON() is pretty-printed; normalize so the literal search is whitespace-proof.
        let spec = try SyxxMatrixIntegration.defaultSpecJSON()
            .replacingOccurrences(of: "\\s+", with: "", options: .regularExpression)

        guard let range = spec.range(of: "\"knownSections\":[", options: .literal),
              let close = spec[range.upperBound...].firstIndex(of: "]") else {
            XCTFail("knownSections array not found in emitted spec")
            return
        }
        let emitted = spec[range.upperBound..<close]
            .split(separator: ",")
            .map { $0.trimmingCharacters(in: CharacterSet(charactersIn: "\" ")) }

        XCTAssertFalse(emitted.isEmpty, "knownSections must not be empty")
        XCTAssertEqual(emitted, emitted.sorted(), "knownSections must be emitted ordinal-sorted")
    }

    /// Every .json in the vectors directory is either an engine vector or explicitly named as a
    /// non-engine one. An unclassified file fails the suite: that is how a new vector shape gets
    /// noticed instead of quietly reducing what this test covers.
    func testEveryVectorFileIsClassified() throws {
        for file in try Self.jsonFiles() where !Self.nonEngineVectors.contains(file) {
            let data = try Data(contentsOf: Self.vectorsDir().appendingPathComponent(file))
            let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            XCTAssertNotNil(obj?["input"], "\(file): unclassified vector shape — needs `input`, or list it in nonEngineVectors")
            XCTAssertNotNil(obj?["expected"], "\(file): unclassified vector shape — needs `expected`, or list it in nonEngineVectors")
        }
    }

    /// Load and evaluate each ENGINE golden vector, and assert how many actually ran.
    func testGoldenVectorsEvaluateToExpectedResults() throws {
        let engineFiles = try Self.jsonFiles().filter { !Self.nonEngineVectors.contains($0) }
        guard !engineFiles.isEmpty else { throw CorpusError.empty }

        let engine = try SyxxMatrixIntegration.makeEngine()
        var evaluated = 0

        for file in engineFiles {
            let data = try Data(contentsOf: Self.vectorsDir().appendingPathComponent(file))
            let vector: GoldenVector
            do {
                vector = try JSONDecoder().decode(GoldenVector.self, from: data)
            } catch {
                throw CorpusError.malformed("\(file): \(error)")
            }

            let result = try engine.evaluate(vector.input.toEvidenceBundle())

            XCTAssertEqual(result.state, vector.expected.state,
                           "\(file): state must match expected")
            XCTAssertEqual(result.diagnoses.count, vector.expected.diagnoses.count,
                           "\(file): diagnosis count must match expected")
            XCTAssertEqual(result.diagnoses.map { $0.id.rawValue }.sorted(),
                           vector.expected.diagnoses.map { $0.id }.sorted(),
                           "\(file): diagnosis IDs must match expected")
            evaluated += 1
        }

        // A suite that cannot say how many cases it ran is not evidence when it is green.
        XCTAssertEqual(evaluated, engineFiles.count,
                       "every engine vector must be evaluated; \(evaluated) of \(engineFiles.count) ran")
    }
}

// MARK: - DTOs for golden vector JSON

private struct GoldenVector: Decodable {
    let input: InputDTO
    let expected: ExpectedDTO

    struct InputDTO: Decodable {
        let sections: [SectionDTO]
        let indicators: [IndicatorDTO]

        func toEvidenceBundle() -> EvidenceBundle {
            EvidenceBundle(
                sections: sections.map {
                    SectionResult(id: SectionID($0.section),
                                  status: EvidenceUsability(rawValue: $0.status) ?? .untested,
                                  trusted: $0.trusted ?? true)
                },
                indicators: indicators.map {
                    ObservedIndicator(id: IndicatorID($0.indicator),
                                      section: SectionID($0.section),
                                      observedStrength: $0.observedStrength,
                                      quality: $0.quality ?? 1.0,
                                      contradictory: $0.contradictory ?? false)
                }
            )
        }
    }

    /// Key is `section`, not `id` — this is the shape the corpus actually ships.
    struct SectionDTO: Decodable {
        let section: String
        let status: String
        let trusted: Bool?
    }

    /// Key is `indicator`, not `id`.
    struct IndicatorDTO: Decodable {
        let indicator: String
        let section: String
        let observedStrength: Double?
        let quality: Double?
        let contradictory: Bool?
    }

    struct ExpectedDTO: Decodable {
        let state: DeterminativeState
        let diagnoses: [DiagnosisDTO]
    }

    struct DiagnosisDTO: Decodable {
        let id: String
        let confidence: String
    }
}
