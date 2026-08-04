import Foundation
import SyxxKit

// Stage-5 presentation copy table (macOS). Presentation-owned, COPY-GATED — every literal here is
// from the APPROVED recommended set in STAGE5-WORDING-PROPOSALS.md (2026-07-12), item 5 = Framing 3.
// The engine DTO carries only `CopyToken`s / enums; this table resolves them to approved English.
// Unknown / non-approved tokens fail closed to `genericInconclusive` — never shown raw, and the
// honesty signal is never suppressed (STAGE5-CONTRACT.md §10). The Windows twin is
// `Windows/SyxxUI/Presentation/Stage5Copy.cs`; keep the two byte-for-byte identical in wording.
enum Stage5Copy {

    // Fail-closed generic. Approved inconclusive headline (proposals item 1h, Option A). Any unknown
    // token or an absent/invalid Stage-5 object resolves here — honest, never green.
    static let genericInconclusive = "Inconclusive — not enough evidence to decide"
    // Approved shipped honesty tail (mirrors the live CLI "(this is NOT a clean result)" pattern).
    static let notACleanResult = "This is not a clean result."

    // MARK: - Mechanism-class headlines (proposals item 1, Option A recommended)

    static func headline(state: DeterminationState, mechanismClass: MechanismClass?) -> String {
        switch state {
        case .clean:        return "No tampering detected in the checks that ran"
        case .inconclusive: return genericInconclusive
        case .tampered:
            switch mechanismClass {
            case .rewrite:    return "The address you were given was changed"
            case .block:      return "This domain is being blocked"
            case .transport:  return "Your DNS traffic is being intercepted in transit"
            case .content:    return "The content was altered in transit"
            case .leak:       return "Your DNS queries are leaking"
            case .host:       return "The tampering is on this device"
            case .unresolved: return "Interference detected — we can't yet say how"
            case .none:       return genericInconclusive   // fail closed: tampered must carry a class
            }
        }
    }

    // MARK: - Within-class split (proposals item 2, Option A recommended)

    static let withinClassLead = "Most likely one of:"

    /// Approved member phrasings exist only for the REWRITE pair (spoofing / interception_onPath).
    /// Any other hypothesis fails closed — the caller then suppresses the within-class row entirely.
    static func withinClass(_ hypothesis: Hypothesis) -> String? {
        switch hypothesis {
        case .spoofing:            return "Your resolver is returning a forged answer"
        case .interception_onPath: return "Something on the path is rewriting answers"
        default:                   return nil
        }
    }

    // MARK: - Three honesty lines (proposals item 3, Option A recommended)

    static func hardGate(sections: [Section]) -> String {
        "A clean result is impossible until \(join(sections)) run — required checks did not complete."
    }
    static func conflict(percent: Int) -> String {
        "The evidence genuinely disagrees (\(percent)%) — different checks point to different conclusions."
    }
    static func uncertainty(percent: Int) -> String {
        "Evidence is thin (\(percent)%) — there isn't enough to be sure either way."
    }

    // MARK: - Referral (proposals item 4, Option A recommended)

    static let referralGeneral =
        "This is at the edge of what automated checks can decide. Get a second opinion from a network expert before you act on it."

    static func referral(_ token: CopyToken?) -> String {
        switch token {
        case .some(.referralMechanismUnresolved):
            return "Something is interfering, but the checks that ran can't pin down how. Have a network expert review the evidence below."
        default:
            return referralGeneral   // fail closed to the approved general referral block
        }
    }

    // MARK: - M4 forced-result / low-belief (proposals item 5, Framing 3 — the load-bearing pair)

    static let safetyGateTag = "(safety gate)"
    static let basisLine     = "Basis: 1 verified decisive signal"
    static func corroboration(percent: Int) -> String { "Corroborating belief: \(percent)%" }
    static func decisiveExplainer(percent: Int) -> String {
        "A verified decisive signal forces this result regardless of the belief number. The \(percent)% reflects how much other evidence has piled on so far — a low number here means 'one strong signal, not yet corroborated,' not 'probably fine.'"
    }

    // MARK: - Provenance headers (proposals item 6a, Option A recommended)

    static let directHeader      = "What we observed directly"
    static let inferredHeader    = "What we concluded from it"
    static let unavailableHeader = "What we couldn't check"

    static func unavailableReason(_ reason: UnavailableReason) -> String {
        switch reason {
        case .notRequested:         return "not requested in this run"
        case .untested:             return "did not run"
        case .timedOut:             return "timed out — no result"
        case .invalidated:          return "ran but the result was invalidated"
        case .parseFailed:          return "response could not be parsed"
        case .probeFailed:          return "the probe failed to complete"
        case .unauthenticated:      return "could not be authenticated"
        case .stale:                return "only stale/cached data was available"
        case .permissionDenied:     return "blocked — insufficient permission"
        case .dependencyUnavailable:return "a required dependency was unavailable"
        }
    }

    // MARK: - Confidence & completeness (proposals item 6b / 6c, Option A recommended)

    static let confidenceLabel = "Confidence"
    static let confidenceNone  = "No estimate — not enough evidence"

    static let completenessLabel = "Evidence completeness"
    static func completenessValue(observed: Int, expected: Int) -> String {
        "\(observed) of \(expected) checks reached a conclusion"
    }
    static func criticalGap(sections: [Section]) -> String {
        "A critical check (\(join(sections))) is missing — this alone prevents a clean result."
    }

    // MARK: - Section names (tool proper nouns, not gated result copy)

    static func sectionName(_ s: Section) -> String { s.rawValue.capitalized }

    static func join(_ sections: [Section]) -> String {
        sections.map(sectionName).joined(separator: ", ")
    }

    /// Round an engine scalar in [0,1] to an integer percent (proposals: `N = round(value*100)`).
    static func percent(_ value: Double) -> Int { Int((value * 100).rounded()) }
}
