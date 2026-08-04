import Foundation

// A6 (ROADMAP "Implementation plan v2" — menu-bar/tray trust indicator). The glanceable
// green/amber/red for the current network: the psychological anchor for A5's monitoring.
//
// This is the pure STATE mapping only. The platform status item itself (NSStatusItem / WinUI tray)
// is a GUI shell that renders what this returns and must add no logic of its own.
//
// SINGLE SOURCE OF TRUTH — the important design decision: the indicator is derived FROM the A3
// report card, not from a second reading of the Conclusion. A tray with its own severity mapping is
// a tray that can eventually disagree with the card and the result badge, and "the surfaces
// contradict each other" is exactly the defect class this product exists to eliminate (D069 shipped
// three mutually exclusive rows in one panel). `ReportCards.For` already carries the mutation-proven
// honesty gate, so reusing it makes divergence structurally impossible rather than merely unlikely.
//
// HONESTY CONTRACT (Protocol §1/§10):
//   * Never scanned => unknown/grey, NEVER green. "No data" is not "safe" — a green dot on a
//     network nobody has looked at is the most-seen lie this product could tell, because the tray
//     is always visible.
//   * Inconclusive, or a clean result that skipped a required check => unknown (via the card's
//     ungraded gate), never green.
//
// Byte-parity twin of Windows `Syxx.Kit/Synthesis/TrayIndicator.cs` — the strings MUST match.
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md:42).

/// What the tray/menu-bar item should show. `attention` means the item should draw the eye
/// (badge/bounce) — reserved for states a user must act on, never for good news.
public struct TrayIndicator: Equatable, Sendable {
    public let severity: Severity
    public let glyph: String
    public let tooltip: String
    public let attention: Bool

    public init(severity: Severity, glyph: String, tooltip: String, attention: Bool) {
        self.severity = severity
        self.glyph = glyph
        self.tooltip = tooltip
        self.attention = attention
    }
}

public enum TrayIndicators {
    /// Shown before any scan has ever run. Grey, not green.
    public static let neverScannedTooltip = "Syxx has not checked this network yet: run a scan."

    /// Map the latest conclusion (nil = never scanned) to the indicator state.
    public static func For(_ latest: Conclusion?) -> TrayIndicator {
        guard let latest else {
            return TrayIndicator(severity: .unknown, glyph: glyphFor(.unknown),
                                 tooltip: neverScannedTooltip, attention: false)
        }

        // Derive from the A3 card so the tray, the grade and the result badge cannot diverge.
        let card = ReportCards.For(latest)

        let tooltip = card.grade == .ungraded
            ? "Not enough evidence to grade this network: \(card.headline)"
            : "\(card.gradeLabel): \(card.headline)"

        // Attention is for things the user must act on. `unknown` does NOT bid for attention: an
        // unmeasured network is not an emergency, and crying wolf on grey would train the user to
        // ignore the indicator that must work when it turns red.
        let attention = card.severity == .high || card.severity == .critical

        return TrayIndicator(severity: card.severity, glyph: glyphFor(card.severity),
                             tooltip: tooltip, attention: attention)
    }

    /// A monochrome hint the shell may map to its own iconography. Deliberately not a colour name —
    /// the palette lives in the design system, not here.
    public static func glyphFor(_ severity: Severity) -> String {
        switch severity {
        case .clean:    return "shield.check"
        case .info:     return "shield"
        case .warning:  return "shield.exclamation"
        case .high:     return "shield.exclamation"
        case .critical: return "shield.slash"
        case .unknown:  return "shield.dotted"   // visibly "not established", never a tick
        }
    }
}
