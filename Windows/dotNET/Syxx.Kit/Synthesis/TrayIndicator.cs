using System;

namespace Syxx.Kit;

// A6 (ROADMAP "Implementation plan v2" — menu-bar/tray trust indicator). The glanceable
// green/amber/red for the current network: the psychological anchor for A5's monitoring.
//
// This is the pure STATE mapping only. The platform status item itself (WinUI tray icon /
// NSStatusItem) is a GUI shell that cannot be built or verified off-Windows — it renders what this
// returns and must add no logic of its own.
//
// SINGLE SOURCE OF TRUTH — the important design decision: the indicator is derived FROM the A3
// report card, not from a second reading of the Conclusion. A tray with its own severity mapping
// is a tray that can eventually disagree with the card and the result badge, and "the surfaces
// contradict each other" is exactly the defect class this product exists to eliminate (D069 shipped
// three mutually exclusive rows in one panel). ReportCards.For() already carries the mutation-proven
// honesty gate, so reusing it makes divergence structurally impossible rather than merely unlikely.
//
// HONESTY CONTRACT (Protocol §1/§10):
//   * Never scanned => Unknown/grey, NEVER green. "No data" is not "safe" — a green dot on a
//     network nobody has looked at is the most-seen lie this product could tell, because the tray
//     is always visible.
//   * Inconclusive, or a Clean result that skipped a required check => Unknown (via the card's
//     Ungraded gate), never green.
//
// Windows-parity twin owed on macOS (SyxxKit/Synthesis/TrayIndicator.swift).
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md:42).

/// What the tray/menu-bar item should show. <paramref name="Attention"/> means the item should
/// draw the eye (badge/bounce) — reserved for states a user must act on, never for good news.
public sealed record TrayIndicator(
    Severity Severity,
    string Glyph,
    string Tooltip,
    bool Attention);

public static class TrayIndicators
{
    /// Shown before any scan has ever run. Grey, not green.
    public const string NeverScannedTooltip = "Syxx has not checked this network yet — run a scan.";

    /// Map the latest conclusion (null = never scanned) to the indicator state.
    public static TrayIndicator For(Conclusion? latest)
    {
        if (latest is null)
            return new TrayIndicator(Severity.Unknown, GlyphFor(Severity.Unknown), NeverScannedTooltip, Attention: false);

        // Derive from the A3 card so the tray, the grade and the result badge cannot diverge.
        var card = ReportCards.For(latest);

        string tooltip = card.Grade == Grade.Ungraded
            ? $"Not enough evidence to grade this network — {card.Headline}"
            : $"{card.GradeLabel} — {card.Headline}";

        // Attention is for things the user must act on. Unknown does NOT bid for attention: an
        // unmeasured network is not an emergency, and crying wolf on grey would train the user to
        // ignore the indicator that must work when it turns red.
        bool attention = card.Severity is Severity.High or Severity.Critical;

        return new TrayIndicator(card.Severity, GlyphFor(card.Severity), tooltip, attention);
    }

    /// A monochrome hint the shell may map to its own iconography. Deliberately not a colour name —
    /// the palette lives in the design system, not here.
    public static string GlyphFor(Severity severity) => severity switch
    {
        Severity.Clean    => "shield.check",
        Severity.Info     => "shield",
        Severity.Warning  => "shield.exclamation",
        Severity.High     => "shield.exclamation",
        Severity.Critical => "shield.slash",
        _                 => "shield.dotted",   // Unknown — visibly "not established", never a tick
    };
}
