import Foundation

// A3 (network report card), re-derived onto the matrix determinative engine. Grades a network A–F
// with the top reasons: the shareable, glanceable summary of a run.
//
// Previously graded from the removed `Conclusion.diagnosis/confidence/severity`; now reads
// `Conclusion.determination: DeterminativeResult?` (owner directive 2026-07-21). Public API and
// consumers (ConclusionsView, SyxxCLI) are unchanged.
//
// HONESTY CONTRACT — the load-bearing rule: a network we could not measure is UNGRADED, never a
// letter. An `A` on an unfinished scan is false safety; an `F` is a false accusation. `inconclusive`,
// no determination, or `clean` riding over a hard-gated section therefore returns `.ungraded` with
// the GAPS as its reasons — the honest "we don't know yet".
//
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md). Not yet ported to
// Windows/Ubuntu.

/// A–F, or `ungraded` when the evidence does not support grading at all.
public enum Grade: String, Codable, Sendable {
    case a = "A", b = "B", c = "C", d = "D", f = "F", ungraded
}

/// The A3 report card: a letter, a one-line headline, and the real top contributors.
public struct ReportCard: Equatable, Sendable {
    public let grade: Grade
    public let gradeLabel: String
    public let headline: String
    public let topReasons: [String]
    public let severity: Severity

    public init(grade: Grade, gradeLabel: String, headline: String, topReasons: [String], severity: Severity) {
        self.grade = grade
        self.gradeLabel = gradeLabel
        self.headline = headline
        self.topReasons = topReasons
        self.severity = severity
    }
}

public enum ReportCards {
    public static let maxReasons = 3

    /// Severity of a threat diagnosis id, used to pick F/D/C and colour the card.
    private static func severity(forDiagnosisID id: String) -> Severity {
        switch id {
        case "dnsSpoofing", "dnsInterception", "contentTampering", "localCompromise": return .critical
        case "censorshipBlocking", "transportDowngrade", "dnsPrivacyLeak":            return .high
        default:                                                                       return .warning
        }
    }

    public static func For(_ c: Conclusion) -> ReportCard {
        let incomplete = c.hasHardGatedSection

        func gapReasons() -> [String] {
            var gaps = c.sectionStatuses
                .filter { $0.status.blocksClean }
                .prefix(maxReasons)
                .map { $0.status == .untested
                    ? "the \($0.section.rawValue.capitalized) check did not run"
                    : "the \($0.section.rawValue.capitalized) check could not be trusted" }
            if gaps.isEmpty { gaps = ["the evidence did not commit to any conclusion"] }
            return Array(gaps)
        }

        // Honesty gate — never grade what we did not measure.
        guard let det = c.determination else {
            return ReportCard(grade: .ungraded, gradeLabel: "N/A",
                              headline: "The determinative engine could not evaluate this run: finish the checks for a real grade.",
                              topReasons: gapReasons(), severity: .unknown)
        }

        // `.untested` MUST be listed explicitly. It is neither `.inconclusive` nor `.clean`, so
        // without this it falls past both guards into the threat-grading path, where an empty
        // diagnosis list hits `severity(forDiagnosisID:)`'s `default: return .warning` and grades a
        // never-run scan **C** — a letter grade for a network nobody looked at. `.untested` is its
        // own state and it is ungraded. (Windows twin: ReportCard.cs.)
        if det.state == .untested || det.state == .inconclusive || (det.state == .clean && incomplete) {
            return ReportCard(grade: .ungraded, gradeLabel: "N/A",
                              headline: "Not enough evidence to grade this network yet: finish the checks for a real grade.",
                              topReasons: gapReasons(), severity: .unknown)
        }

        if det.state == .clean {
            let solid = (det.diagnoses.first?.confidence == .conclusive) && !incomplete
            let grade: Grade = solid ? .a : .b
            let headline = solid
                ? "This network looks clean: no tampering or leakage was found."
                : "This network looks clean, but confidence is limited: some checks were inconclusive."
            var reasons = c.sectionStatuses
                .filter { $0.status == .negative }
                .prefix(maxReasons)
                .map { "the \($0.section.rawValue.capitalized) check ran and found nothing wrong" }
            if reasons.isEmpty { reasons = ["the checks that ran found no signs of tampering or leakage"] }
            return ReportCard(grade: grade, gradeLabel: grade.rawValue, headline: headline,
                              topReasons: Array(reasons), severity: .clean)
        }

        // threatDetected
        let top = det.diagnoses.first
        let sev = severity(forDiagnosisID: top?.id.rawValue ?? "")
        let grade: Grade
        switch sev {
        case .critical: grade = .f
        case .high:     grade = .d
        case .warning:  grade = .c
        default:        grade = .ungraded
        }
        let name = top?.displayName ?? "Tampering"
        let headline = "\(name): this network is not safe to trust as-is."
        // C064: the top reasons come from the NAMED diagnosis's own supporting indicators
        // (`DiagnosisScore.supportingIndicators`, reverse-mapped to `EvidenceId`s), in ledger
        // order, truncated AFTER filtering — never a raw-ledger prefix. If the filter yields
        // nothing (a bug for an engine-emitted diagnosis), the generic clause below stands in;
        // unrelated observations are never presented as reasons for this threat.
        let supportingIds = SynthesisEngine.evidenceIds(supporting: top?.supportingIndicators ?? [])
        var reasons = Array(c.evidence.lazy
            .filter { supportingIds.contains($0.evidenceId) }
            .map { $0.observation }
            .prefix(maxReasons))
        if reasons.isEmpty { reasons = ["the synthesis points to \(name.lowercased())"] }
        return ReportCard(grade: grade, gradeLabel: grade == .ungraded ? "N/A" : grade.rawValue,
                          headline: headline, topReasons: Array(reasons), severity: sev)
    }
}
