import Foundation
import SyxxKit
import SwiftUI

/// Pure Dashboard warden chip labels and display helpers. Kept out of the View so tests can
/// mutation-prove honesty contracts without mounting SwiftUI.
enum DashboardWardenState {
    /// Mirror branch only. Existing Dashboard chip strings; no new user-facing copy.
    /// Qualified as SyxxKit.ComparisonResult to avoid Foundation.NSComparisonResult ambiguity.
    static func mirror(isPro: Bool, result: SyxxKit.ComparisonResult?) -> String {
        guard isPro else { return "LOCKED · PRO" }
        guard let r = result else { return "NOT RUN" }
        // Only validated matching evidence may show PASSED. Redirection/tampering
        // must never collapse into PASSED (false-pass predicate).
        if r.isRedirectionDetected { return "TAMPERING DETECTED" }
        if r.isInconclusive { return "INCONCLUSIVE" }
        if r.isDegraded { return "DEGRADED" }
        return "PASSED"
    }

    // MARK: - Cage capture ticker (owner: modulo 1000)

    /// Display count for the Cage tile ticker. Real `cageEventsCount` modulo 1000
    /// ("goes up to 999 and starts over"). Negative inputs clamp to 0.
    static func cageCaptureDisplayCount(_ cageEventsCount: Int) -> Int {
        let n = max(0, cageEventsCount)
        return n % 1000
    }

    /// Approved Cage ticker label: "Captures: {n}" with n = count % 1000.
    static func cageCaptureLabel(_ cageEventsCount: Int) -> String {
        "Captures: \(cageCaptureDisplayCount(cageEventsCount))"
    }

    // MARK: - Chip tone (honesty: never green/teal on inconclusive)

    /// Visual tone for a warden status chip. Inconclusive/unmeasured must not read as success.
    enum ChipTone: Equatable {
        case success
        case threat
        case inconclusive
        case locked
        case idle
    }

    /// Map an existing chip label to a tone. Unknown labels stay idle (muted), never success.
    static func chipTone(for label: String) -> ChipTone {
        switch label {
        case "PASSED", "CLEAR", "SIGNED", "CAPTURED", "MAPPED":
            return .success
        case "TAMPERING DETECTED", "LEAK FOUND", "INVALID", "ERROR", "EXPIRED":
            return .threat
        case "INCONCLUSIVE", "DEGRADED", "NO DATA", "STALE":
            return .inconclusive
        case "LOCKED · PRO":
            return .locked
        default:
            return .idle
        }
    }

    /// Color for a chip tone. Success may use the warden accent; inconclusive is always amber.
    static func chipColor(tone: ChipTone, wardenAccent: Color) -> Color {
        switch tone {
        case .success:
            return wardenAccent
        case .threat:
            return .ioCriticalRed
        case .inconclusive:
            return .ioWarningAmber
        case .locked:
            return .ioInfoPurple
        case .idle:
            return .ioTextMuted
        }
    }
}
