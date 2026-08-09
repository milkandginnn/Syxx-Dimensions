import XCTest
import Foundation
@testable import SyxxKit

/// B281 — macOS half of the SHARED cross-platform golden vector for the cross-vantage agreement
/// policy. Loads the checked-in `corpus/b281-consensus/decision-table.json` — the SAME file the
/// Windows half (`Syxx.Tests/CrossVantageConsensusGoldenVectorTests.cs`) loads — and asserts this
/// platform's `CrossVantageConsensus.classifyPeerConsensus` returns the expected outcome for every
/// row. If macOS and Windows ever disagree on a row, one suite goes red. A missing or empty corpus
/// fails the test: a vacuously-green parity test is the green that lies.
final class CrossVantageConsensusGoldenVectorTests: XCTestCase {

    private struct Case: Decodable {
        let name: String
        let vantages: [[String]]
        let trustedReference: [String]
        let expected: String
    }
    private struct Corpus: Decodable { let cases: [Case] }

    private static func corpusURL() -> URL {
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
            .appendingPathComponent("corpus")
            .appendingPathComponent("b281-consensus")
            .appendingPathComponent("decision-table.json")
    }

    private func outcome(_ s: String) -> CrossVantageConsensus.Outcome? {
        CrossVantageConsensus.Outcome(rawValue: s == "noData" ? "noData" : s)
    }

    func testEveryGoldenRowMatchesPolicy() throws {
        let url = Self.corpusURL()
        guard FileManager.default.fileExists(atPath: url.path) else {
            return XCTFail("B281 golden vector missing at \(url.path)")
        }
        let data = try Data(contentsOf: url)
        guard !data.isEmpty else { return XCTFail("B281 golden vector is empty — refusing to pass vacuously") }
        let corpus = try JSONDecoder().decode(Corpus.self, from: data)
        XCTAssertGreaterThanOrEqual(corpus.cases.count, 10, "golden vector must carry the full decision table")

        var evaluated = 0
        for c in corpus.cases {
            let vantages = c.vantages.map { Set($0) }
            let ref = Set(c.trustedReference)
            let got = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: vantages, trustedReference: ref)
            guard let expected = outcome(c.expected) else {
                return XCTFail("unknown expected outcome '\(c.expected)' in case \(c.name)")
            }
            XCTAssertEqual(got, expected, "B281 golden row '\(c.name)': expected \(expected), got \(got)")
            evaluated += 1
        }
        print("[b281 consensus] evaluated \(evaluated) of \(corpus.cases.count) golden rows")
        XCTAssertGreaterThan(evaluated, 0, "no golden row was evaluated — an empty run must not read as coverage")
        XCTAssertEqual(evaluated, corpus.cases.count, "every golden row must be evaluated; \(evaluated) of \(corpus.cases.count) ran")
    }
}
