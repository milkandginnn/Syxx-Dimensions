import XCTest
@testable import SyxxKit

/// STAGE5-CONTRACT §14 "Shared DTO/parity tests" — the POPULATED-object cross-platform byte-parity
/// regression. Unlike `Stage5PresentationTests.fixtureV1()` (a hand-built DTO graph), this fixture is
/// the FROZEN canonical JSON that the PRODUCTION `Synthesizer.conclude(from:now:)` path emits for the
/// canonical poisoned-local scenario (local `192.168.1.1` → `127.0.0.1`, three public resolvers →
/// `93.184.216.34`, redirection detected, `now = 1752268477`). It was captured ONCE from that path
/// and committed as a static golden; these tests LOAD it and round-trip it — they never regenerate it
/// from `conclude()` at test time (that would test the code against itself, not a fixed golden).
///
/// The C# twin (`Stage5PopulatedParityTests.cs`) embeds the IDENTICAL literal and asserts the same
/// round-trip, so a green run on each platform proves macOS and Windows decode the SAME 4749-byte
/// object and re-encode the SAME bytes. Each happy-path assertion is paired with a mutation that
/// proves it can fail (§14: "Each happy-path test requires a mutation/failure case").
final class Stage5PopulatedParityTests: XCTestCase {

    /// The exact 4749-byte canonical object the production path emits for the poisoned-local scenario.
    /// Byte-for-byte identical to the C# twin's `PopulatedFixtureJson`.
    static let populatedFixtureJSON =
        #"{"answerMatrix":[{"comparison":{"classification":"discrepancy","consensusAnswers":["93.184.216.34"],"localVantageIds":["local"],"outlierVantageIds":["local"],"referenceVantageIds":["cloudflare","google","quad9"]},"entries":[{"canonicalAnswers":["127.0.0.1"],"observationId":"obs:mirror:0:local","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"192.168.1.1","freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"canonicalAnswers":["93.184.216.34"],"observationId":"obs:mirror:0:cloudflare","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"cloudflare"}},{"canonicalAnswers":["93.184.216.34"],"observationId":"obs:mirror:0:google","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"8.8.8.8","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"google"}},{"canonicalAnswers":["93.184.216.34"],"observationId":"obs:mirror:0:quad9","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"9.9.9.9","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"quad9"}}],"queryName":"example.com.","queryType":1,"rowId":"row:mirror:0"}],"completeness":{"criticalAvailable":1,"criticalExpected":3,"expected":6,"expectedProfileId":"profile.sections.v0","observed":1},"dataRegime":"partialData","determination":{"basis":"ordinaryCredalGate","confidence":{"kind":"belief","target":"mechanism:unresolved","value":0.8646647167633873},"mechanismClass":"unresolved","referralRequired":true,"referralToken":"stage5.referral.mechanism_unresolved","state":"tampered","withinClass":[{"displayOrder":0,"hypothesis":"spoofing","share":0.45697995778887185},{"displayOrder":1,"hypothesis":"interception_onPath","share":0.45697995778887185},{"displayOrder":2,"hypothesis":"censorship","share":0.017208016884451293},{"displayOrder":3,"hypothesis":"interception_transport","share":0.017208016884451293},{"displayOrder":4,"hypothesis":"contentTampering","share":0.017208016884451293},{"displayOrder":5,"hypothesis":"privacyLeak","share":0.017208016884451293},{"displayOrder":6,"hypothesis":"localCompromise","share":0.017208016884451293}]},"findings":[],"generatedAt":"2025-07-11T21:14:37.000Z","honesty":{"hardGate":{"blockedResults":["clean"],"requiredSections":["prophet","portal","glutton","cage","witness"],"token":"stage5.hard_gate.required_checks_missing","unavailableDatapointIds":[]},"uncertainty":{"token":"stage5.uncertainty.evidence_thin","value":0.1353352832366127}},"provenance":{"direct":[{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:local","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["127.0.0.1"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"192.168.1.1","freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:cloudflare","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["93.184.216.34"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"cloudflare"}},{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:google","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["93.184.216.34"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"8.8.8.8","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"google"}},{"datapointId":"mirror.answer.example.com..1","observationId":"obs:mirror:0:quad9","observedAt":"2025-07-11T21:14:37.000Z","rawValues":["93.184.216.34"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"9.9.9.9","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"quad9"}}],"inferred":[],"unavailable":[{"datapointId":"section.prophet","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"prophet"},{"datapointId":"section.portal","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"portal"},{"datapointId":"section.glutton","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"glutton"},{"datapointId":"section.cage","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"cage"},{"datapointId":"section.witness","reason":"untested","reasonToken":"stage5.unavailable.untested","section":"witness"}]},"schemaVersion":1}"#

    private static var fixtureData: Data { populatedFixtureJSON.data(using: .utf8)! }

    // MARK: - Happy path: cross-platform round-trip

    /// Load the frozen golden → decode → non-nil → valid → re-encode is byte-identical to the golden.
    /// This is the cross-platform round-trip: both platforms decode the SAME fixture and re-encode the
    /// SAME bytes. Mutation that proves it can fail: `testSchemaVersionBumpFailsClosed` etc. below.
    func testPopulatedFixtureRoundTripsToParityBytes() throws {
        XCTAssertEqual(Self.fixtureData.count, 4749, "the committed golden must be the 4749-byte object")
        let decoded = try XCTUnwrap(Stage5Presentation.decode(Self.fixtureData),
                                    "the frozen production golden must decode")
        XCTAssertTrue(decoded.isValid, "the production golden must be §13-valid: \(decoded.validationErrors())")
        XCTAssertEqual(try decoded.canonicalBytes(), Self.fixtureData,
                       "re-encoding the decoded golden must reproduce the exact fixture bytes")
    }

    // MARK: - Semantic anchors (catch a silent structural drift the byte compare would still catch,
    // but that a future intentional re-capture might otherwise wave through)

    func testPopulatedFixtureSemanticAnchors() throws {
        let d = try XCTUnwrap(Stage5Presentation.decode(Self.fixtureData))
        XCTAssertEqual(d.answerMatrix.first?.comparison.classification, .discrepancy)
        XCTAssertEqual(d.determination.state, .tampered)
        XCTAssertEqual(d.determination.mechanismClass, .unresolved)
        XCTAssertEqual(d.provenance.direct.count, 4)
        XCTAssertEqual(d.provenance.unavailable.count, 5)
        XCTAssertFalse(d.determination.withinClass.isEmpty, "within-class distribution must be present")
        let shareSum = d.determination.withinClass.compactMap { $0.share }.reduce(0, +)
        XCTAssertEqual(shareSum, 1.0, accuracy: 1e-9, "within-class shares must sum to ~1")
    }

    // MARK: - Negative / mutation cases (§14: each happy path must be provably able to fail)

    /// schemaVersion bumped to an unsupported value fails closed at BOTH gates: `decode()` returns nil,
    /// and a raw decode that bypasses the guard is rejected by the §13 validator (`isValid == false`).
    func testSchemaVersionBumpFailsClosed() throws {
        let bumped = Self.populatedFixtureJSON
            .replacingOccurrences(of: "\"schemaVersion\":1", with: "\"schemaVersion\":2")
            .data(using: .utf8)!
        XCTAssertNil(Stage5Presentation.decode(bumped), "schemaVersion 2 must fail closed (nil)")
        let raw = try XCTUnwrap(try? JSONDecoder().decode(Stage5Presentation.self, from: bumped),
                                "raw decode bypasses decode()'s guard for the isValid check")
        XCTAssertFalse(raw.isValid, "the §13 validator must also reject schemaVersion 2")
    }

    /// A required enum value that does not match any case fails to decode. NB: a pure re-casing
    /// ("discrepancy" → "Discrepancy") is NOT used here because System.Text.Json's enum reader can be
    /// case-insensitive, which would make the C# twin of this negative unable to fail — a negative that
    /// can't fail proves nothing. A clear misspelling fails on BOTH platforms.
    func testMisspelledEnumValueFailsDecode() {
        let mutated = Self.populatedFixtureJSON
            .replacingOccurrences(of: "\"classification\":\"discrepancy\"",
                                  with: "\"classification\":\"discrepency\"")
            .data(using: .utf8)!
        XCTAssertNil(Stage5Presentation.decode(mutated), "an unknown enum value must fail to decode")
    }

    /// Editing a within-class share so the distribution no longer sums to 1 is a §13 violation.
    func testWithinClassSharesNotSummingToOneAreInvalid() throws {
        let mutated = Self.populatedFixtureJSON
            .replacingOccurrences(of: "\"share\":0.45697995778887185", with: "\"share\":0.05")
            .data(using: .utf8)!
        let d = try XCTUnwrap(Stage5Presentation.decode(mutated), "the mutation keeps valid schema/JSON")
        XCTAssertFalse(d.validationErrors().isEmpty, "shares that do not sum to 1 must be flagged")
        XCTAssertTrue(d.validationErrors().contains { $0.contains("withinClass shares do not sum to 1") },
                      "the specific dangling-sum error must be reported")
    }

    /// Corrupting a direct observation's id leaves an answer entry pointing at a missing observation —
    /// a dangling source link the §13 validator must flag.
    func testDanglingAnswerObservationLinkIsInvalid() throws {
        let mutated = Self.populatedFixtureJSON
            .replacingOccurrences(of: "\"observationId\":\"obs:mirror:0:local\",\"observedAt\":",
                                  with: "\"observationId\":\"obs:mirror:0:corrupted\",\"observedAt\":")
            .data(using: .utf8)!
        let d = try XCTUnwrap(Stage5Presentation.decode(mutated), "the mutation keeps valid schema/JSON")
        XCTAssertFalse(d.validationErrors().isEmpty, "a dangling answer→observation link must be flagged")
        XCTAssertTrue(d.validationErrors().contains { $0.contains("references missing observation obs:mirror:0:local") },
                      "the specific dangling-link error must be reported")
    }
}
