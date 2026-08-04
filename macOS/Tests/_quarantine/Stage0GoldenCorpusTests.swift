import XCTest
import Foundation
@testable import SyxxKit

/// P0.2/P0.3 — the ENGINE-SIDE half of the STAGE0 golden-vector harness. This is the extraction the
/// cross-platform runner (Cline, CL-3) calls, plus the seed corpus encoded as expected-design vectors
/// with `greensAtPhase` tags and the UNIVERSAL never-false-secure invariant — the honesty spine.
///
/// COORDINATION FLAG to Cline (CL-3): the live STAGE0 schema fields this engine-side harness relies on
/// are `greensAtPhase`, `expectedRedUntil`, the universal never-false-secure assertion, and the
/// per-evidence `clusterId` (read here so the future cautious/Dempster within-vs-across-cluster
/// selection can key on it — the current engine still combines under Dempster; the fusion swap is the
/// deferred bake-off pass). Keep these in the shared schema; do not redefine unilaterally.
final class Stage0GoldenCorpusTests: XCTestCase {

    /// The phase the CURRENT engine implementation has reached. Phase-1 CI gates on the
    /// `greensAtPhase <= enginePhase` subset; later-phase (naming/inversion) seeds stay expected-red
    /// but must still satisfy never-false-secure.
    static let enginePhase = 1

    // MARK: - Fixture model (mirrors the STAGE0 fixture shape at the engine boundary)

    struct SeedEvidence {
        let section: Section
        let evidenceId: EvidenceId
        let source: String?
        /// R4 lineage cluster (STAGE0 `clusterId`): items sharing a cluster are non-independent. Read
        /// by the oracle/harness; the within-cluster (cautious) vs across-cluster (Dempster) selection
        /// is the deferred bake-off pass — today everything still combines under Dempster.
        let clusterId: String?
        init(_ section: Section, _ evidenceId: EvidenceId, source: String? = nil, clusterId: String? = nil) {
            self.section = section; self.evidenceId = evidenceId; self.source = source; self.clusterId = clusterId
        }
        var item: EvidenceItem { EvidenceItem(section: section, evidenceId: evidenceId, observation: "o", rationale: "r", source: source) }
    }

    struct Seed {
        let id: String
        let label: String                       // mechanism-class label, or "benign_*"
        let evidence: [SeedEvidence]
        let statuses: [(Section, SectionStatus)]
        let greensAtPhase: Int
        /// Expected `_exact` result once greened: a threat `Hypothesis`, `.inconclusive`, or `.clean`
        /// (benign only). NEVER `.clean` for a tampering label — the never-false-secure corpus invariant.
        let expectedResult: Hypothesis
        /// Whether the design expects a forced referral once greened.
        let expectedReferral: Bool
        /// KNOWN-OPEN false-secure at phase 0: the CURRENT engine reads clean (documented reality). Its
        /// fixing gate flips it; the ratchet below fails when that happens, forcing removal of the flag.
        var knownFalseSecureUntilGate: Bool = false
        var tampering: Bool { !label.hasPrefix("benign") }
    }

    // MARK: - The seed corpus (NE1–NE7 at the engine boundary)

    // WS-B1: the seed corpus is loaded from corpus/stage0/seeds.json — the SINGLE SOURCE OF TRUTH shared
    // line-for-line with the C# harness (Windows/Syxx.Tests/Stage0GoldenCorpusTests.cs). Do NOT re-hand-encode
    // vectors here; edit the JSON. A missing / empty / malformed corpus THROWS: a silently-empty seed list
    // would make the never-false-secure invariant pass vacuously (a green-that-lies), which the honesty spine
    // forbids. Enum strings are the Swift rawValue form, so they decode directly here.

    enum CorpusError: Error, CustomStringConvertible {
        case notFound(String)
        case empty
        case unknownEnum(vector: String, kind: String, value: String)
        case missingSection(vector: String, section: String)
        var description: String {
            switch self {
            case .notFound(let p): return "Stage-0 golden corpus not found at \(p). WS-B1 single source of truth is missing."
            case .empty: return "Stage-0 golden corpus contains no vectors — refusing to run vacuously."
            case .unknownEnum(let v, let k, let val): return "corpus vector '\(v)': unknown \(k) value '\(val)'."
            case .missingSection(let v, let s): return "corpus vector '\(v)': sectionStatuses missing section '\(s)'."
            }
        }
    }

    private struct CorpusFile: Decodable { let vectors: [VectorDTO] }
    private struct VectorDTO: Decodable {
        let id: String
        let label: String
        let evidence: [EvidenceDTO]
        let sectionStatuses: [String: String]
        let greensAtPhase: Int
        let expectedResult: String
        let expectedReferral: Bool
        let knownFalseSecureUntilGate: Bool?
    }
    private struct EvidenceDTO: Decodable {
        let section: String
        let evidenceId: String
        let source: String?
        let clusterId: String?
    }

    static func seeds() throws -> [Seed] {
        // #filePath = <repo>/macOS/Tests/SyxxKitTests/Stage0GoldenCorpusTests.swift (compile-time source
        // path, the exact twin of the C# harness's [CallerFilePath] resolution).
        let repoRoot = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
        let url = repoRoot
            .appendingPathComponent("corpus")
            .appendingPathComponent("stage0")
            .appendingPathComponent("seeds.json")
        guard FileManager.default.fileExists(atPath: url.path) else { throw CorpusError.notFound(url.path) }
        let data = try Data(contentsOf: url)
        let file = try JSONDecoder().decode(CorpusFile.self, from: data)
        guard !file.vectors.isEmpty else { throw CorpusError.empty }
        return try file.vectors.map { try seed(from: $0) }
    }

    private static func seed(from v: VectorDTO) throws -> Seed {
        let evidence: [SeedEvidence] = try v.evidence.map { e in
            guard let sec = Section(rawValue: e.section) else {
                throw CorpusError.unknownEnum(vector: v.id, kind: "Section", value: e.section)
            }
            guard let eid = EvidenceId(rawValue: e.evidenceId) else {
                throw CorpusError.unknownEnum(vector: v.id, kind: "EvidenceId", value: e.evidenceId)
            }
            return SeedEvidence(sec, eid, source: e.source, clusterId: e.clusterId)
        }
        // Order by the canonical enum order (prophet..witness) — reproduces the old with(allNeg, ...) shape.
        let statuses: [(Section, SectionStatus)] = try Section.allCases.map { section in
            guard let raw = v.sectionStatuses[section.rawValue] else {
                throw CorpusError.missingSection(vector: v.id, section: section.rawValue)
            }
            guard let status = SectionStatus(rawValue: raw) else {
                throw CorpusError.unknownEnum(vector: v.id, kind: "SectionStatus", value: raw)
            }
            return (section, status)
        }
        guard let result = Hypothesis(rawValue: v.expectedResult) else {
            throw CorpusError.unknownEnum(vector: v.id, kind: "Hypothesis", value: v.expectedResult)
        }
        return Seed(id: v.id, label: v.label, evidence: evidence, statuses: statuses,
                    greensAtPhase: v.greensAtPhase, expectedResult: result,
                    expectedReferral: v.expectedReferral,
                    knownFalseSecureUntilGate: v.knownFalseSecureUntilGate ?? false)
    }

    // MARK: - P0.2 oracle extraction (what the runner calls)

    struct OracleResult {
        let result: Hypothesis
        let confidencePct: Int
        let referral: Bool
        let belief: [Hypothesis: Double]
        let uncertainty: Double
        let conflict: Double
    }

    static func run(_ seed: Seed) -> OracleResult {
        let statuses = seed.statuses.map { SectionStatusEntry(section: $0.0, status: $0.1) }
        let c = SynthesisEngine.synthesize(evidence: seed.evidence.map { $0.item }, sectionStatuses: statuses)
        var bel: [Hypothesis: Double] = [:]
        for b in c.belief { bel[b.hypothesis] = b.probability }
        return OracleResult(result: c.result, confidencePct: c.confidence, referral: c.referralTriggered,
                            belief: bel, uncertainty: c.uncertainty, conflict: c.conflict)
    }

    // MARK: - P0.3 the honesty spine: universal never-false-secure

    func testNeverFalseSecure_universalInvariant() throws {
        for seed in try Self.seeds() where seed.tampering {
            // (a) Corpus-data invariant — no tampering vector may DECLARE an expected clean result.
            XCTAssertNotEqual(seed.expectedResult, .clean,
                "seed \(seed.id) declares expected CLEAN for a tampering label — corpus is dishonest")
            // (b) Engine-output invariant — the ACTUAL result must never be clean for tampering input.
            let actual = Self.run(seed).result
            if seed.knownFalseSecureUntilGate {
                // The spine lands BEFORE the fixing gate (task hard rule). NE1d1/NE7d1/P15 currently read
                // CLEAN — the open defect. strict expect-failure: when the gate lands and the result is
                // no longer clean, THIS reds ("expected failure did not occur"), forcing the flag removed.
                XCTExpectFailure("seed \(seed.id) is an open false-secure until its Phase-1 gate lands", strict: true) {
                    XCTAssertNotEqual(actual, .clean, "\(seed.id) reads CLEAN for a tampering input")
                }
            } else {
                XCTAssertNotEqual(actual, .clean,
                    "NEVER-FALSE-SECURE VIOLATED: \(seed.id) reads CLEAN for a tampering input")
            }
        }
    }

    // MARK: - Phase-1-reachable exact assertions

    func testPhase1ReachableSeeds_matchExactResult() throws {
        for seed in try Self.seeds() where seed.greensAtPhase <= Self.enginePhase && !seed.knownFalseSecureUntilGate {
            let r = Self.run(seed)
            XCTAssertEqual(r.result, seed.expectedResult, "seed \(seed.id): result")
            XCTAssertEqual(r.referral, seed.expectedReferral, "seed \(seed.id): referral")
        }
    }

    /// The oracle reads `clusterId` off every seed evidence item (wiring for the deferred cautious/Dempster
    /// selection). Today it does not change combination; this asserts the field is present and readable.
    func testOracleReadsClusterLineage() throws {
        let clustered = try Self.seeds().flatMap { $0.evidence }.filter { $0.clusterId != nil }
        XCTAssertFalse(clustered.isEmpty, "the corpus must carry R4 lineage clusters for the runner to key on")
    }
}
