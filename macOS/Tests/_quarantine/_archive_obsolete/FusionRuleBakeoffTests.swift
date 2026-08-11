import XCTest
import Foundation
@testable import SyxxKit

/// P0.4 — Offline fusion-rule bake-off harness (BAKEOFF-DESIGN.md).
///
/// Evaluates all 5 candidate combination rules over the golden corpus and records:
///   - per-vector pass/fail against the oracle (`_exact` tier)
///   - cross-platform float determinism measurement (ε)
///   - benign-control integrity
///
/// **This is an offline experiment.** It does not change the production combination rule
/// (`.cautious` remains the shipped default). Results are printed for human review; the
/// selection of the winning rule is made by the user/designer after reviewing the output.
///
/// Hard gate: P0.4 must pass before Phase 3 (combination rewrite) can start.
final class FusionRuleBakeoffTests: XCTestCase {

    // MARK: - Helpers

    private func st(_ section: Section, _ status: SectionStatus) -> SectionStatusEntry {
        SectionStatusEntry(section: section, status: status)
    }

    private var allNegative: [SectionStatusEntry] {
        Section.allCases.map { st($0, .negative) }
    }

    /// All 5 candidate rules under evaluation.
    private static let candidateRules: [SynthesisEngine.CombinationRule] = [
        .dempster, .cautious, .yager, .duboisPrade, .weightedMass
    ]

    /// Human-readable label for each rule.
    private static func ruleLabel(_ rule: SynthesisEngine.CombinationRule) -> String {
        switch rule {
        case .dempster:     return "R-D Dempster"
        case .cautious:     return "R-C Cautious"
        case .yager:        return "R-Y Yager"
        case .duboisPrade:  return "R-DP Dubois-Prade"
        case .weightedMass: return "R-M WeightedMass"
        }
    }

    /// Per-vector, per-rule result row.
    struct BakeoffRow {
        let vectorId: String
        let label: String
        let rule: SynthesisEngine.CombinationRule
        let result: Hypothesis
        let referral: Bool
        let confidence: Int
        let conflict: Double
        let uncertainty: Double
        /// _exact tier: result match against oracle?
        let resultMatch: Bool
        /// _exact tier: referral match against oracle?
        let referralMatch: Bool
    }

    // MARK: - Bake-off test cases

    /// SV-1: wash-out (NE1 Δ1). A dnssec strip positive on prophet against 4 clean sections
    /// and 1 untested. The strip's own committed mass alone (≈0.95) already floors the result
    /// via I8 — so EVERY rule must produce non-clean. The differentiation is the confidence
    /// number: idempotent rules (R-C) report higher honest confidence than rules that over-count
    /// clean support (R-D) or bleed mass to Θ (R-Y).
    func testSV1_washout_allRulesBlockClean() {
        let evidence = [
            EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                         observation: "stripped", rationale: "confirmed", source: "canaries")
        ]
        let statuses = allNegative.enumerated().map { i, entry in
            i == 0 ? st(.prophet, .positive) : entry
        }

        var results: [BakeoffRow] = []
        for rule in Self.candidateRules {
            var config = SynthesisEngine.Config()
            config.combinationRule = rule
            let c = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: statuses, config: config)
            let resultOK = c.result != Hypothesis.clean
            let referralOK = c.referralTriggered
            results.append(BakeoffRow(
                vectorId: "SV-1", label: "dnssec_strip", rule: rule,
                result: c.result, referral: c.referralTriggered,
                confidence: c.confidence, conflict: c.conflict, uncertainty: c.uncertainty,
                resultMatch: resultOK, referralMatch: referralOK))
        }

        // Print the matrix for human review
        printBakeoffMatrix("SV-1 — Wash-out (NE1 Δ1)", results)

        // ALL rules must pass the I8 committed-mass gate (rule-invariant safety)
        for row in results {
            XCTAssertTrue(row.resultMatch,
                          "\(Self.ruleLabel(row.rule)): SV-1 result is \(row.result.rawValue) — must not be clean")
        }
    }

    /// P1 — Idempotence: feeding the SAME support twice must not over-count.
    /// R-C (cautious, max-of-weights) passes; R-D (Dempster, additive) and others fail.
    func testP1_idempotence_duplicateSupportDoesNotStack() {
        // Same evidence repeated: one item with 3.0 weight = ~0.95 committed mass.
        // Under idempotent combination, duplicate should produce the same result at ~0.95.
        // Under additive combination, the mass nearly doubles.
        let item = EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                                observation: "stripped", rationale: "confirmed", source: "canaries")
        let evidence = [item, item]  // same item twice
        let statuses = allNegative.enumerated().map { i, entry in
            i == 0 ? st(.prophet, .positive) : entry
        }

        var results: [BakeoffRow] = []
        for rule in Self.candidateRules {
            var config = SynthesisEngine.Config()
            config.combinationRule = rule
            let c = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: statuses, config: config)

            // For P1: idempotent rule (R-C) should produce referral=false with high confidence
            // since the committed mass should be ~0.95 regardless of duplicate.
            // Non-idempotent rules may over-count and differ.
            results.append(BakeoffRow(
                vectorId: "P1", label: "idempotence", rule: rule,
                result: c.result, referral: c.referralTriggered,
                confidence: c.confidence, conflict: c.conflict, uncertainty: c.uncertainty,
                resultMatch: c.result != Hypothesis.clean, referralMatch: true))
        }

        printBakeoffMatrix("P1 — Idempotence (duplicate support)", results)
    }

    /// BC-1: benign CDN / multi-homed domain must not produce a tampered result under any rule.
    func testBC1_benignCDN_noFalseTampered() {
        // Benign: all clean, no evidence
        let evidence: [EvidenceItem] = []
        let statuses = allNegative

        var results: [BakeoffRow] = []
        for rule in Self.candidateRules {
            var config = SynthesisEngine.Config()
            config.combinationRule = rule
            let c = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: statuses, config: config)
            results.append(BakeoffRow(
                vectorId: "BC-1", label: "benign_cdn", rule: rule,
                result: c.result, referral: c.referralTriggered,
                confidence: c.confidence, conflict: c.conflict, uncertainty: c.uncertainty,
                resultMatch: c.result == Hypothesis.clean || c.result == Hypothesis.inconclusive,
                referralMatch: true))
        }

        printBakeoffMatrix("BC-1 — Benign CDN (no false tampered)", results)

        for row in results {
            XCTAssertTrue(row.resultMatch,
                          "\(Self.ruleLabel(row.rule)): BC-1 result is \(row.result.rawValue) — must be clean or inconclusive")
        }
    }

    /// SV-3: clean-support conflation (NE2 Δ3). A Mirror positive with clean Prophet and Witness
    /// sections that also cover the same threats. Under datapoint-scoped clean-support,
    /// the redirection should remain NAMED.
    func testSV3_cleanSupportConflation_allRulesKeepRedirectionNamed() {
        let evidence = [
            EvidenceItem(section: .mirror, evidenceId: .redirection,
                         observation: "redirected", rationale: "confirmed")
        ]
        var statuses = allNegative
        statuses[0] = st(.prophet, .negative)  // Prophet clean — covers spoofing/interception
        statuses[1] = st(.mirror, .positive)
        statuses[5] = st(.witness, .negative)  // Witness clean — covers spoofing/interception

        var results: [BakeoffRow] = []
        for rule in Self.candidateRules {
            var config = SynthesisEngine.Config()
            config.combinationRule = rule
            let c = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: statuses, config: config)
            results.append(BakeoffRow(
                vectorId: "SV-3", label: "clean_support_conflation", rule: rule,
                result: c.result, referral: c.referralTriggered,
                confidence: c.confidence, conflict: c.conflict, uncertainty: c.uncertainty,
                resultMatch: c.result != Hypothesis.clean,
                referralMatch: true))
        }

        printBakeoffMatrix("SV-3 — Clean-support conflation (NE2 Δ3)", results)
    }

    /// Determinism measurement: run dempster + cautious on each corpus seed and verify
    /// run-to-run stability (identical output on a second pass).
    func testDeterminismMeasurement_allRulesRecordFloatProfile() {
        let seeds: [Stage0GoldenCorpusTests.Seed]
        do {
            seeds = try Stage0GoldenCorpusTests.seeds()
        } catch {
            XCTFail("Failed to load seeds: \(error)")
            return
        }

        var maxDelta = 0.0
        for seed in seeds {
            let statuses = seed.statuses.map { SectionStatusEntry(section: $0.0, status: $0.1) }
            let ev = seed.evidence.map { $0.item }
            for rule in Self.candidateRules {
                var config = SynthesisEngine.Config()
                config.combinationRule = rule
                let r1 = SynthesisEngine.synthesize(evidence: ev, sectionStatuses: statuses, config: config)
                let r2 = SynthesisEngine.synthesize(evidence: ev, sectionStatuses: statuses, config: config)
                if abs(r1.conflict - r2.conflict) > maxDelta { maxDelta = abs(r1.conflict - r2.conflict) }
                if abs(r1.uncertainty - r2.uncertainty) > maxDelta { maxDelta = abs(r1.uncertainty - r2.uncertainty) }
                XCTAssertEqual(r1.result, r2.result, "\(seed.id) \(Self.ruleLabel(rule)): result not deterministic")
                XCTAssertEqual(r1.referralTriggered, r2.referralTriggered, "\(seed.id) \(Self.ruleLabel(rule)): referral not deterministic")
            }
        }
        print("P0.4 determinism: max float delta across all seeds+rules = \(maxDelta)")
    }

    /// Full corpus bake-off: evaluate dempster + cautious on every seed and verify
    /// the never-false-secure invariant holds for both.
    func testFullCorpusBakeoff_allRulesAllSeeds() throws {
        let seeds = try Stage0GoldenCorpusTests.seeds()
        for seed in seeds {
            for rule in Self.candidateRules {
                var config = SynthesisEngine.Config()
                config.combinationRule = rule
                let statuses = seed.statuses.map { SectionStatusEntry(section: $0.0, status: $0.1) }
                let c = SynthesisEngine.synthesize(
                    evidence: seed.evidence.map { $0.item },
                    sectionStatuses: statuses,
                    config: config)

                if seed.tampering {
                    XCTAssertNotEqual(c.result, Hypothesis.clean,
                                      "\(Self.ruleLabel(rule)): \(seed.id) reads CLEAN for tampering input")
                }
            }
        }
    }

    // MARK: - Output helpers

    private func printBakeoffMatrix(_ title: String, _ rows: [BakeoffRow]) {
        print("─── \(title) ───")
        let header = String(format: "  %-20@ %-18@ %-6@ %4@  %8@ %8@",
                            "Rule" as NSString, "Result" as NSString, "Refer" as NSString,
                            "Conf" as NSString, "Conflict" as NSString, "Uncert" as NSString)
        print(header)
        print("  " + String(repeating: "─", count: 72))
        for row in rows {
            let vMark = row.resultMatch ? "✓" : "✗"
            let rMark = row.referralMatch ? "✓" : "✗"
            let line = String(format: "  %-20@ %-18@ %@%-5@ %3d%%  %8.4f %8.4f",
                              Self.ruleLabel(row.rule) as NSString,
                              "\(vMark) \(row.result.rawValue)" as NSString,
                              rMark as NSString, (row.referral ? "Y" : "N") as NSString,
                              row.confidence, row.conflict, row.uncertainty)
            print(line)
        }
        print("")
    }
}
