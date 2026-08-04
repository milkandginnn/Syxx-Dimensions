import Foundation
import SyxxKit

// Shared, presentation-owned Stage-5 fixture for previews and the display-model tests. It mirrors the
// engine's shared v1 fixture value for value, built from the public DTO initializers — a poisoned-local
// answer matrix (local outlier vs public consensus), all three honesty signals present, a
// tampered/rewrite determination on a DECISIVE safety gate with a within-class split, and two ranked
// findings. Presentation code renders this identically to how it will render the real engine-populated
// object.
enum Stage5PreviewFixture {
    static let v1: Stage5Presentation = {
        let local = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil,
                                    transport: "do53", authenticated: false, freshness: .currentRun)
        let public1 = VantageIdentity(vantageId: "public1", kind: .publicResolver, endpoint: "1.1.1.1",
                                      transport: "do53", authenticated: false, freshness: .currentRun)
        let public2 = VantageIdentity(vantageId: "public2", kind: .publicResolver, endpoint: "8.8.8.8",
                                      transport: "do53", authenticated: false, freshness: .currentRun)

        let obsLocal = DirectObservation(observationId: "obs.local.a", datapointId: "target_answer:local",
            section: .prophet, observedAt: "2026-07-11T21:14:30.000Z", vantage: local,
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
            VantageAnswer(vantage: local, status: .answered, canonicalAnswers: ["203.0.113.5"], rcode: 0, observationId: "obs.local.a"),
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
            confidence: CredalMeasure(kind: .belief, value: 0.2, target: "mechanism:rewrite"),
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
    }()
}
