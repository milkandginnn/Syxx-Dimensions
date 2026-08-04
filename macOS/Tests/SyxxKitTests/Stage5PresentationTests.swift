import XCTest
@testable import SyxxKit

/// P1.0 — the additive `Stage5Presentation` DTO (STAGE5-CONTRACT.md §3-§14). Proves: the shared v1
/// fixture decodes to an equivalent graph and re-encodes to the SAME canonical bytes as the C#
/// twin (`Stage5PresentationTests.cs` asserts the identical `expectedCanonicalJson` literal), the
/// §13 validator accepts the valid fixture and rejects each negative mutation, an absent object is
/// never read as clean, and an unsupported schemaVersion fails closed.
final class Stage5PresentationTests: XCTestCase {

    /// The shared v1 fixture — built IDENTICALLY on both platforms (STAGE5 §14). Exercises the
    /// direct/inferred/unavailable partition, a poisoned-local answer matrix (local outlier vs public
    /// consensus), all three honesty signals present, a tampered/rewrite determination with a
    /// within-class split (including the snake_case `interception_onPath` hypothesis that stresses the
    /// cross-platform enum encoding), and two ranked findings.
    static func fixtureV1() -> Stage5Presentation {
        let localVantage = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil,
                                           transport: "do53", authenticated: false, freshness: .currentRun)
        let public1 = VantageIdentity(vantageId: "public1", kind: .publicResolver, endpoint: "1.1.1.1",
                                      transport: "do53", authenticated: false, freshness: .currentRun)
        let public2 = VantageIdentity(vantageId: "public2", kind: .publicResolver, endpoint: "8.8.8.8",
                                      transport: "do53", authenticated: false, freshness: .currentRun)

        let obsLocal = DirectObservation(observationId: "obs.local.a", datapointId: "target_answer:local",
            section: .prophet, observedAt: "2026-07-11T21:14:30.000Z", vantage: localVantage,
            rawValues: ["203.0.113.5"], valueKind: .dnsAnswerSet, sourceEvidenceIds: [.redirection])
        let obsPublic = DirectObservation(observationId: "obs.public.a", datapointId: "target_answer:public",
            section: .mirror, observedAt: "2026-07-11T21:14:31.000Z", vantage: public1,
            rawValues: ["198.51.100.10"], valueKind: .dnsAnswerSet, sourceEvidenceIds: [])

        let provenance = ProvenancePartition(
            direct: [obsLocal, obsPublic],
            inferred: [InferredFinding(findingId: "find.rewrite.1", mechanismClass: .rewrite,
                detailToken: nil, sourceObservationIds: ["obs.local.a"], sourceEvidenceIds: [.redirection])],
            unavailable: [UnavailableDatapoint(datapointId: "leak", section: .glutton, reason: .timedOut,
                reasonToken: .unavailableTimedOut)])

        let row = AnswerMatrixRow(rowId: "row.target.a", queryName: "example.com.", queryType: 1, entries: [
            VantageAnswer(vantage: localVantage, status: .answered, canonicalAnswers: ["203.0.113.5"], rcode: 0, observationId: "obs.local.a"),
            VantageAnswer(vantage: public1, status: .answered, canonicalAnswers: ["198.51.100.10"], rcode: 0, observationId: "obs.public.a"),
            VantageAnswer(vantage: public2, status: .answered, canonicalAnswers: ["198.51.100.10"], rcode: 0, observationId: nil),
        ], comparison: AnswerComparison(classification: .discrepancy, localVantageIds: ["local"],
            referenceVantageIds: ["public1", "public2"], consensusAnswers: ["198.51.100.10"], outlierVantageIds: ["local"]))

        let honesty = HonestySignals(
            hardGate: HardGateSignal(token: .hardGateRequiredChecksMissing, blockedResults: [.clean],
                requiredSections: [.glutton], unavailableDatapointIds: ["leak"]),
            conflict: ScalarHonestySignal(token: .conflictEvidenceDisagrees, value: 0.5),
            uncertainty: ScalarHonestySignal(token: .uncertaintyEvidenceThin, value: 0.25))

        let determination = MechanismDetermination(state: .tampered, mechanismClass: .rewrite,
            confidence: CredalMeasure(kind: .belief, value: 0.4, target: "mechanism:rewrite"),
            withinClass: [
                WithinClassEntry(hypothesis: .spoofing, displayOrder: 0, share: 0.6),
                WithinClassEntry(hypothesis: .interception_onPath, displayOrder: 1, share: 0.4),
            ], basis: .decisiveSafetyGate, referralRequired: true, referralToken: .referralMechanismUnresolved)

        let findings = [
            PrioritizedFinding(findingId: "find.rewrite.1", mechanismClass: .rewrite, inferenceId: "find.rewrite.1",
                sourceObservationIds: ["obs.local.a"],
                priority: FindingPriority(modelId: "syxx.stage5.sd", modelVersion: 1,
                    factors: PriorityFactors(severity: 5, detectability: 4, occurrence: nil), score: 20, rank: 1)),
            PrioritizedFinding(findingId: "find.leak.2", mechanismClass: .leak, inferenceId: nil,
                sourceObservationIds: ["obs.local.a"],
                priority: FindingPriority(modelId: "syxx.stage5.sd", modelVersion: 1,
                    factors: PriorityFactors(severity: 3, detectability: 3, occurrence: nil), score: 9, rank: 2)),
        ]

        return Stage5Presentation(schemaVersion: 1, generatedAt: "2026-07-11T21:14:37.123Z",
            dataRegime: .partialData,
            completeness: EvidenceCompleteness(observed: 4, expected: 6, criticalAvailable: 2, criticalExpected: 3, expectedProfileId: "full"),
            provenance: provenance, answerMatrix: [row], honesty: honesty, determination: determination, findings: findings)
    }

    /// The exact canonical bytes both platforms must emit. Captured from Swift; the C# twin asserts
    /// the IDENTICAL literal, so equality on each platform proves byte-parity between them.
    static let expectedCanonicalJson =
        #"{"answerMatrix":[{"comparison":{"classification":"discrepancy","consensusAnswers":["198.51.100.10"],"localVantageIds":["local"],"outlierVantageIds":["local"],"referenceVantageIds":["public1","public2"]},"entries":[{"canonicalAnswers":["203.0.113.5"],"observationId":"obs.local.a","rcode":0,"status":"answered","vantage":{"authenticated":false,"freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"canonicalAnswers":["198.51.100.10"],"observationId":"obs.public.a","rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"public1"}},{"canonicalAnswers":["198.51.100.10"],"rcode":0,"status":"answered","vantage":{"authenticated":false,"endpoint":"8.8.8.8","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"public2"}}],"queryName":"example.com.","queryType":1,"rowId":"row.target.a"}],"completeness":{"criticalAvailable":2,"criticalExpected":3,"expected":6,"expectedProfileId":"full","observed":4},"dataRegime":"partialData","determination":{"basis":"decisiveSafetyGate","confidence":{"kind":"belief","target":"mechanism:rewrite","value":0.4},"mechanismClass":"rewrite","referralRequired":true,"referralToken":"stage5.referral.mechanism_unresolved","state":"tampered","withinClass":[{"displayOrder":0,"hypothesis":"spoofing","share":0.6},{"displayOrder":1,"hypothesis":"interception_onPath","share":0.4}]},"findings":[{"findingId":"find.rewrite.1","inferenceId":"find.rewrite.1","mechanismClass":"rewrite","priority":{"factors":{"detectability":4,"severity":5},"modelId":"syxx.stage5.sd","modelVersion":1,"rank":1,"score":20},"sourceObservationIds":["obs.local.a"]},{"findingId":"find.leak.2","mechanismClass":"leak","priority":{"factors":{"detectability":3,"severity":3},"modelId":"syxx.stage5.sd","modelVersion":1,"rank":2,"score":9},"sourceObservationIds":["obs.local.a"]}],"generatedAt":"2026-07-11T21:14:37.123Z","honesty":{"conflict":{"token":"stage5.conflict.evidence_disagrees","value":0.5},"hardGate":{"blockedResults":["clean"],"requiredSections":["glutton"],"token":"stage5.hard_gate.required_checks_missing","unavailableDatapointIds":["leak"]},"uncertainty":{"token":"stage5.uncertainty.evidence_thin","value":0.25}},"provenance":{"direct":[{"datapointId":"target_answer:local","observationId":"obs.local.a","observedAt":"2026-07-11T21:14:30.000Z","rawValues":["203.0.113.5"],"section":"prophet","sourceEvidenceIds":["redirection"],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"freshness":"currentRun","kind":"localSystem","transport":"do53","vantageId":"local"}},{"datapointId":"target_answer:public","observationId":"obs.public.a","observedAt":"2026-07-11T21:14:31.000Z","rawValues":["198.51.100.10"],"section":"mirror","sourceEvidenceIds":[],"valueKind":"dnsAnswerSet","vantage":{"authenticated":false,"endpoint":"1.1.1.1","freshness":"currentRun","kind":"publicResolver","transport":"do53","vantageId":"public1"}}],"inferred":[{"findingId":"find.rewrite.1","mechanismClass":"rewrite","sourceEvidenceIds":["redirection"],"sourceObservationIds":["obs.local.a"]}],"unavailable":[{"datapointId":"leak","reason":"timedOut","reasonToken":"stage5.unavailable.timed_out","section":"glutton"}]},"schemaVersion":1}"#

    func testCanonicalBytesMatchCrossPlatformFixture() throws {
        let bytes = try Self.fixtureV1().canonicalBytes()
        let json = String(data: bytes, encoding: .utf8)!
        // Print for the initial capture step; the assertion is the gate.
        if json != Self.expectedCanonicalJson { print("STAGE5_ACTUAL_JSON=\(json)") }
        XCTAssertEqual(json, Self.expectedCanonicalJson)
    }

    func testFixtureRoundTrips() throws {
        let bytes = try Self.fixtureV1().canonicalBytes()
        let decoded = try XCTUnwrap(Stage5Presentation.decode(bytes))
        XCTAssertEqual(decoded, Self.fixtureV1())
        // Re-encode is stable (byte-identical).
        XCTAssertEqual(try decoded.canonicalBytes(), bytes)
    }

    func testValidFixturePassesValidator() {
        XCTAssertEqual(Self.fixtureV1().validationErrors(), [], "the shared fixture must be §13-valid")
    }

    // MARK: - Negative fixtures (each proves a §13 invariant can fail)

    func testDanglingSourceLinkFails() {
        let f = Self.fixtureV1()
        let broken = Stage5Presentation(generatedAt: f.generatedAt, dataRegime: f.dataRegime,
            completeness: f.completeness,
            provenance: ProvenancePartition(direct: f.provenance.direct,
                inferred: [InferredFinding(findingId: "find.x", mechanismClass: .rewrite, detailToken: nil,
                    sourceObservationIds: ["obs.does.not.exist"], sourceEvidenceIds: [])],
                unavailable: f.provenance.unavailable),
            answerMatrix: f.answerMatrix, honesty: f.honesty, determination: f.determination, findings: f.findings)
        XCTAssertFalse(broken.isValid, "an inference referencing a missing observation must be invalid")
    }

    func testFabricatedConsensusFails() {
        let f = Self.fixtureV1()
        // insufficientReferences must NOT carry a consensus answer set (invariant 7).
        let badRow = AnswerMatrixRow(rowId: "row.bad", queryName: "example.com.", queryType: 1,
            entries: f.answerMatrix[0].entries,
            comparison: AnswerComparison(classification: .insufficientReferences, localVantageIds: ["local"],
                referenceVantageIds: [], consensusAnswers: ["203.0.113.5"], outlierVantageIds: []))
        let broken = Stage5Presentation(generatedAt: f.generatedAt, dataRegime: f.dataRegime,
            completeness: f.completeness, provenance: f.provenance, answerMatrix: [badRow],
            honesty: f.honesty, determination: f.determination, findings: f.findings)
        XCTAssertFalse(broken.isValid, "local-only data must not be fabricated into public consensus")
    }

    func testCleanWithBlockingHardGateFails() {
        let f = Self.fixtureV1()
        // A clean determination while a hard gate blocks clean is impossible (invariant 9).
        let cleanDet = MechanismDetermination(state: .clean, mechanismClass: nil, confidence: nil,
            withinClass: [], basis: .ordinaryCredalGate, referralRequired: false, referralToken: nil)
        let broken = Stage5Presentation(generatedAt: f.generatedAt, dataRegime: f.dataRegime,
            completeness: f.completeness, provenance: f.provenance, answerMatrix: f.answerMatrix,
            honesty: f.honesty, determination: cleanDet, findings: f.findings)
        XCTAssertFalse(broken.isValid, "clean is impossible with a hard gate blocking clean")
    }

    func testWrongPriorityScoreFails() {
        let f = Self.fixtureV1()
        let badFinding = PrioritizedFinding(findingId: "find.rewrite.1", mechanismClass: .rewrite, inferenceId: nil,
            sourceObservationIds: ["obs.local.a"],
            priority: FindingPriority(modelId: "syxx.stage5.sd", modelVersion: 1,
                factors: PriorityFactors(severity: 5, detectability: 4, occurrence: nil), score: 999, rank: 1))
        let broken = Stage5Presentation(generatedAt: f.generatedAt, dataRegime: f.dataRegime,
            completeness: f.completeness, provenance: f.provenance, answerMatrix: f.answerMatrix,
            honesty: f.honesty, determination: f.determination, findings: [badFinding])
        XCTAssertFalse(broken.isValid, "priority score must equal the declared model formula")
    }

    // MARK: - Absence / version safety

    func testLegacyConclusionWithoutStage5DecodesAndIsNotClean() throws {
        // STAGE5 §12.2: absence of Stage5 must not be read as clean. Current Conclusion has no
        // stage5 property on the wire; a bare Conclusion with no determination is incomplete
        // evidence, not a fabricated clean result. Stage5Presentation itself fails closed on junk.
        let c = Conclusion(generatedAt: Date(timeIntervalSince1970: 770_000_000))
        XCTAssertNil(c.determination, "empty conclusion must not manufacture a clean determination")
        XCTAssertTrue(c.evidence.isEmpty)
        XCTAssertFalse(c.hasHardGatedSection == false && c.determination?.state == .clean,
                       "must not combine into a false clean path")
        XCTAssertNil(
            Stage5Presentation.decode(Data(#"{"schemaVersion":1}"#.utf8)),
            "incomplete stage5 JSON must fail closed, never decode as clean"
        )
        // Failure case: a full clean-looking determination is the only way to claim clean —
        // absence alone is never enough.
        XCTAssertNotEqual(c.determination?.state, .clean)
    }

    func testUnsupportedSchemaVersionFailsClosed() throws {
        // A future schemaVersion must fail closed (decode returns nil) so the caller renders
        // unavailable/inconclusive (STAGE5 §12.3).
        let bytes = try Self.fixtureV1().canonicalBytes()
        let bumped = String(data: bytes, encoding: .utf8)!.replacingOccurrences(of: "\"schemaVersion\":1", with: "\"schemaVersion\":2")
        XCTAssertNil(Stage5Presentation.decode(bumped.data(using: .utf8)!), "schemaVersion 2 must fail closed")
    }
}
