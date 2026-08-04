import Foundation

// A2 (plain-language explanation layer), re-derived onto the matrix determinative engine.
//
// Previously this rendered a Dempster-Shafer synthesis ("we believe X because A and B, despite C")
// off `Conclusion.diagnosis/confidence/probabilities/evidence[].supports`. That engine was removed
// (owner directive 2026-07-21) and replaced by `MatrixDeterminativeEngine`, whose output lives on
// `Conclusion.determination: DeterminativeResult?`. This file now composes the same because/despite
// shape from that result, so its public API and its consumers (ConclusionsView, SyxxCLI,
// FirstRunScan) are unchanged.
//
// HONESTY CONTRACT: an inconclusive / hard-gated / no-determination result is explained as
// UNFINISHED, never "clean"; the "despite" list (section gaps + unverified threat overhang) is never
// suppressed to make a result look cleaner.
//
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md). Not yet ported to
// Windows/Ubuntu (the matrix engine is macOS-only so far).

/// The A2 plain-language explanation of a Conclusion. `narrative` composes the parts into one
/// paragraph; `because` / `despite` expose them for structured UI.
public struct PlainExplanation: Equatable, Sendable {
    public let summary: String
    public let because: [String]
    public let despite: [String]

    public init(summary: String, because: [String], despite: [String]) {
        self.summary = summary
        self.because = because
        self.despite = despite
    }

    /// One-paragraph prose: "<summary> because <a> and <b>. That said, <c> and <d>."
    public var narrative: String {
        var s = summary
        if !because.isEmpty { s += " because " + Self.joinClauses(because) }
        s += "."
        if !despite.isEmpty { s += " That said, " + Self.joinClauses(despite) + "." }
        return s
    }

    private static func joinClauses(_ parts: [String]) -> String {
        if parts.count == 1 { return parts[0] }
        return parts.dropLast().joined(separator: ", ") + " and " + parts[parts.count - 1]
    }
}

public enum PlainExplanations {
    private static let maxBecause = 3
    private static let maxDespite = 3

    /// B175: truncate the despite list to `maxDespite`, appending a "+N more" clause when entries
    /// were cut so no section-gap or suppressed-diagnosis clause silently vanishes.
    static func truncateDespite(_ items: [String]) -> [String] {
        guard items.count > maxDespite else { return items }
        let kept = Array(items.prefix(maxDespite))
        let dropped = items.count - maxDespite
        return kept + ["+\(dropped) more unresolved item\(dropped == 1 ? "" : "s")"]
    }

    public static func For(_ c: Conclusion) -> PlainExplanation {
        // Section gaps are the same in every regime — compute once for the "despite" list.
        func sectionGaps() -> [String] {
            var out: [String] = []
            for s in c.sectionStatuses where s.status != .positive && s.status != .negative {
                let name = s.section.rawValue.capitalized
                switch s.status {
                case .untested:     out.append("the \(name) check did not run")
                case .invalidated:  out.append("the \(name) check could not be trusted")
                case .degraded:     out.append("the \(name) check was degraded")
                case .inconclusive: out.append("the \(name) check was inconclusive")
                default: break
                }
            }
            return out
        }

        // No determination at all: the engine could not run. Honest "unfinished".
        guard let det = c.determination else {
            return PlainExplanation(
                summary: "We could not determine whether your DNS is trustworthy — this result is unfinished, not clean",
                because: [],
                despite: Self.truncateDespite(sectionGaps()))
        }

        switch det.state {
        case .untested:
            return PlainExplanation(
                summary: "No sections have been evaluated yet — run a scan to see results",
                because: [],
                despite: [])
        case .inconclusive:
            // B175: section gaps first — they are run-quality facts the user must always see.
            // Suppressed-diagnosis clauses follow. Truncation appends "+N more" when anything
            // was cut so no clause silently vanishes.
            var despite: [String] = sectionGaps()
            despite.append(contentsOf: det.suppressed.map { "a possible \($0.displayName.lowercased()) signal could not be verified" })
            return PlainExplanation(
                summary: "We could not confirm whether your DNS is trustworthy — this result is unfinished, not clean",
                because: [],
                despite: Self.truncateDespite(despite))

        case .clean:
            var because = c.sectionStatuses
                .filter { $0.status == .negative }
                .prefix(maxBecause)
                .map { "the \($0.section.rawValue.capitalized) check ran and found nothing wrong" }
            if because.isEmpty { because = ["the checks that ran found no signs of tampering or leakage"] }
            return PlainExplanation(
                summary: "We believe your DNS is clean",
                because: Array(because),
                despite: Self.truncateDespite(sectionGaps()))

        case .threatDetected:
            let top = det.diagnoses.first
            let summary = top.map { "We believe this is \($0.displayName.lowercased())" }
                ?? "We believe your DNS is being tampered with"
            // BECAUSE (C064) — ONLY observations that actually support the NAMED diagnosis
            // (`DiagnosisScore.supportingIndicators`, reverse-mapped to `EvidenceId`s), in ledger
            // order, truncated AFTER filtering. Never a raw-ledger prefix: an unrelated
            // observation presented as a reason for this threat is a self-contradiction.
            let supportingIds = SynthesisEngine.evidenceIds(supporting: top?.supportingIndicators ?? [])
            var because = Array(c.evidence.lazy
                .filter { supportingIds.contains($0.evidenceId) }
                .map { $0.observation }
                .prefix(maxBecause))
            if because.isEmpty {
                // Should be impossible for an engine-emitted diagnosis (its supportingIndicators
                // are observed by construction) — treat as a bug, log it, and fall back to ONE
                // generic clause. NEVER to unrelated raw evidence presented as reasons.
                // COPY: PROPOSED, PENDING OWNER APPROVAL (outputs-catalog.csv).
                FileHandle.standardError.write(Data(
                    "SyxxKit C064: emitted diagnosis '\(top?.id.rawValue ?? "?")' has no matching evidence in the ledger — falling back to a generic clause. This is a defect; report it.\n".utf8))
                because = ["the determinative engine recorded supporting evidence for this diagnosis (details in the full evidence list)"]
            }
            // DESPITE — B175: section gaps first (run-quality facts must never be silently cut),
            // then runner-up diagnosis, then unverified overhang. Truncation appends "+N more".
            var despite: [String] = sectionGaps()
            if det.diagnoses.count > 1 {
                despite.append("there are also signs of \(det.diagnoses[1].displayName.lowercased())")
            }
            despite.append(contentsOf: det.suppressed.map { "a possible \($0.displayName.lowercased()) signal could not be verified" })
            return PlainExplanation(
                summary: summary,
                because: Array(because),
                despite: Self.truncateDespite(despite))
        }
    }
}
