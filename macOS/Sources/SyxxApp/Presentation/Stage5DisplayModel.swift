import Foundation
import SyxxKit

// MARK: - Honesty-critical, testable display model (no SwiftUI, no side effects)

/// The colour/urgency bucket for the Stage-5 headline. `clean` is the ONLY success/green state;
/// `tampered`, `inconclusive`, and `unavailable` are distinct NON-green states (STAGE5 §14
/// presentation tests: clean is the only green; missing/incomplete data never renders green).
enum Stage5Severity: Equatable { case clean, tampered, inconclusive, unavailable }

enum Stage5HonestyKind: Equatable { case hardGate, conflict, uncertainty }

/// One direct observation row for the provenance partition. Pure projection of engine fields —
/// the adapter never invents values or strengthens meaning.
struct Stage5DirectRow: Equatable {
    let observationId: String
    let datapointId: String
    let section: Section
    let vantageId: String
    let rawValues: [String]
    let valueKind: ObservationValueKind
}

/// One inferred finding row for the provenance partition.
struct Stage5InferredRow: Equatable {
    let findingId: String
    let mechanismClass: MechanismClass
    let sourceObservationIds: [String]
}

/// One unavailable datapoint row for the provenance partition.
struct Stage5UnavailableRow: Equatable {
    let datapointId: String
    let section: Section
    let reason: UnavailableReason
    let reasonText: String
}

/// One cross-vantage answer-matrix row. Classification and consensus come from the engine only.
struct Stage5MatrixDisplayRow: Equatable {
    let rowId: String
    let queryName: String
    let queryType: UInt16
    let classification: AnswerComparisonClass
    let consensusAnswers: [String]
    let outlierVantageIds: [String]
    let entries: [(vantageId: String, status: AnswerStatus, answers: [String])]

    static func == (lhs: Stage5MatrixDisplayRow, rhs: Stage5MatrixDisplayRow) -> Bool {
        lhs.rowId == rhs.rowId
            && lhs.queryName == rhs.queryName
            && lhs.queryType == rhs.queryType
            && lhs.classification == rhs.classification
            && lhs.consensusAnswers == rhs.consensusAnswers
            && lhs.outlierVantageIds == rhs.outlierVantageIds
            && lhs.entries.map(\.vantageId) == rhs.entries.map(\.vantageId)
            && lhs.entries.map(\.status) == rhs.entries.map(\.status)
            && lhs.entries.map(\.answers) == rhs.entries.map(\.answers)
    }
}

/// One RPN-ordered finding, preserving engine rank order (never re-sorted by the UI).
struct Stage5FindingRow: Equatable {
    let findingId: String
    let mechanismClass: MechanismClass
    let rank: Int
    let score: Int
    let sourceObservationIds: [String]
}

/// Pure mapping from the validated Stage-5 DTO to display-ready values. Built only when the object
/// is present AND §13-valid; otherwise `make(from:)` returns nil and the view shows the unavailable
/// note. The adapter NEVER recomputes consensus, confidence, completeness, conflict applicability,
/// mechanism class, or RPN — it only partitions and formats what the engine already decided.
struct Stage5DisplayModel: Equatable {
    let p: Stage5Presentation

    /// Fail-closed constructor: nil for an absent OR schema-invalid object (STAGE5 §12/§13).
    static func make(from stage5: Stage5Presentation?) -> Stage5DisplayModel? {
        guard let s = stage5, s.isValid else { return nil }
        return Stage5DisplayModel(p: s)
    }

    var severity: Stage5Severity {
        switch p.determination.state {
        case .clean:        return .clean
        case .tampered:     return .tampered
        case .inconclusive: return .inconclusive
        }
    }

    var headline: String {
        Stage5Copy.headline(state: p.determination.state, mechanismClass: p.determination.mechanismClass)
    }

    /// Engine-supplied mechanism class, unchanged. Nil for clean; may be `.unresolved` for tampered.
    var mechanismClass: MechanismClass? { p.determination.mechanismClass }

    /// M4: a decisive safety gate forces the result independent of the belief number. The renderer
    /// switches to the Framing-3 "Corroboration" layout so a low number never reads self-contradictory.
    var isDecisiveGate: Bool { p.determination.basis == .decisiveSafetyGate }

    /// The audit anchor — the engine's exact RFC 3339 UTC string, rendered verbatim (no locale
    /// conversion; STAGE5 §3).
    var timeAnchor: String { p.generatedAt }

    // MARK: CX-1 provenance partition (direct / inferred / unavailable)

    var directRows: [Stage5DirectRow] {
        p.provenance.direct.map {
            Stage5DirectRow(
                observationId: $0.observationId,
                datapointId: $0.datapointId,
                section: $0.section,
                vantageId: $0.vantage.vantageId,
                rawValues: $0.rawValues,
                valueKind: $0.valueKind
            )
        }
    }

    var inferredRows: [Stage5InferredRow] {
        p.provenance.inferred.map {
            Stage5InferredRow(
                findingId: $0.findingId,
                mechanismClass: $0.mechanismClass,
                sourceObservationIds: $0.sourceObservationIds
            )
        }
    }

    var unavailableRows: [Stage5UnavailableRow] {
        p.provenance.unavailable.map {
            Stage5UnavailableRow(
                datapointId: $0.datapointId,
                section: $0.section,
                reason: $0.reason,
                reasonText: Stage5Copy.unavailableReason($0.reason)
            )
        }
    }

    // MARK: CX-1 cross-vantage answer matrix

    var matrixRows: [Stage5MatrixDisplayRow] {
        p.answerMatrix.map { row in
            Stage5MatrixDisplayRow(
                rowId: row.rowId,
                queryName: row.queryName,
                queryType: row.queryType,
                classification: row.comparison.classification,
                consensusAnswers: row.comparison.consensusAnswers,
                outlierVantageIds: row.comparison.outlierVantageIds,
                entries: row.entries.map { e in
                    (vantageId: e.vantage.vantageId, status: e.status, answers: e.canonicalAnswers)
                }
            )
        }
    }

    // MARK: CX-2 three DISTINCT honesty lines

    /// The three honesty signals, in fixed order, only those the engine marked present. They are
    /// independent — any combination is valid (STAGE5 §6); one signal never controls another.
    var honestyLines: [(Stage5HonestyKind, String)] {
        var out: [(Stage5HonestyKind, String)] = []
        if let hg = p.honesty.hardGate {
            out.append((.hardGate, Stage5Copy.hardGate(sections: hg.requiredSections)))
        }
        if let c = p.honesty.conflict {
            out.append((.conflict, Stage5Copy.conflict(percent: Stage5Copy.percent(c.value))))
        }
        if let u = p.honesty.uncertainty {
            out.append((.uncertainty, Stage5Copy.uncertainty(percent: Stage5Copy.percent(u.value))))
        }
        return out
    }

    // MARK: CX-3 within-class secondaries + RPN findings (engine order preserved)

    /// Within-class members with approved copy (single-member classes render nothing). A member with
    /// no approved phrasing is dropped (fail closed) rather than shown raw. Order follows
    /// engine `displayOrder`.
    var withinClassLines: [String] {
        p.determination.withinClass
            .sorted { $0.displayOrder < $1.displayOrder }
            .compactMap { Stage5Copy.withinClass($0.hypothesis) }
    }

    /// RPN findings in engine rank order. Presentation must not re-sort by severity or confidence.
    var findingRows: [Stage5FindingRow] {
        p.findings.map {
            Stage5FindingRow(
                findingId: $0.findingId,
                mechanismClass: $0.mechanismClass,
                rank: $0.priority.rank,
                score: $0.priority.score,
                sourceObservationIds: $0.sourceObservationIds
            )
        }
    }

    /// Ordinary-case confidence text. In the decisive-gate case the view uses the Framing-3 block
    /// instead and never calls this. Nil belief → "No estimate" (never "0%"; STAGE5 §7).
    var confidenceText: String {
        if let cm = p.determination.confidence {
            return "\(Stage5Copy.confidenceLabel): \(Stage5Copy.percent(cm.value))%"
        }
        return Stage5Copy.confidenceNone
    }

    var completenessText: String {
        Stage5Copy.completenessValue(observed: p.completeness.observed, expected: p.completeness.expected)
    }

    /// Critical-gap note only when a hard gate names the missing required sections.
    var criticalGapText: String? {
        guard let hg = p.honesty.hardGate, !hg.requiredSections.isEmpty else { return nil }
        return Stage5Copy.criticalGap(sections: hg.requiredSections)
    }

    var referralText: String? {
        guard p.determination.referralRequired else { return nil }
        return Stage5Copy.referral(p.determination.referralToken)
    }

    var dataRegime: DataRegime { p.dataRegime }
}
