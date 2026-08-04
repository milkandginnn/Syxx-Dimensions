using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;

namespace SyxxUI.Presentation;

/// <summary>Colour/urgency bucket for the Stage-5 headline. <c>Clean</c> is the ONLY success/green
/// state; <c>Tampered</c>, <c>Inconclusive</c>, and <c>Unavailable</c> are distinct NON-green states
/// (STAGE5 §14 presentation tests: clean is the only green; missing/incomplete never renders green).</summary>
public enum Stage5Severity { Clean, Tampered, Inconclusive, Unavailable }

public enum Stage5HonestyKind { HardGate, Conflict, Uncertainty }

/// <summary>
/// Pure mapping from the validated Stage-5 DTO to display-ready values — the honesty-critical twin of
/// macOS <c>Stage5DisplayModel</c>. Built only when the object is present AND §13-valid; otherwise
/// <see cref="Make"/> returns null and the view shows the unavailable note (never a fabricated clean
/// preview). The view reads these values and never reconstructs meaning.
/// </summary>
public sealed class Stage5DisplayModel
{
    public Stage5Presentation P { get; }
    private Stage5DisplayModel(Stage5Presentation p) => P = p;

    /// Fail-closed constructor: null for an absent OR schema-invalid object (STAGE5 §12/§13).
    public static Stage5DisplayModel? Make(Stage5Presentation? stage5)
    {
        if (stage5 is null || !stage5.IsValid) return null;
        return new Stage5DisplayModel(stage5);
    }

    public Stage5Severity Severity => P.Determination.State switch
    {
        DeterminationState.Clean => Stage5Severity.Clean,
        DeterminationState.Tampered => Stage5Severity.Tampered,
        _ => Stage5Severity.Inconclusive,
    };

    public string Headline => Stage5Copy.Headline(P.Determination.State, P.Determination.MechanismClass);

    /// M4: a decisive safety gate forces the verdict independent of the belief number → Framing-3 layout.
    public bool IsDecisiveGate => P.Determination.Basis == DecisionBasis.DecisiveSafetyGate;

    /// The audit anchor — the engine's exact RFC 3339 UTC string, verbatim (no locale conversion).
    public string TimeAnchor => P.GeneratedAt;

    /// The three honesty signals, fixed order, only those the engine marked present (independent).
    public IReadOnlyList<(Stage5HonestyKind Kind, string Text)> HonestyLines
    {
        get
        {
            var outp = new List<(Stage5HonestyKind, string)>();
            if (P.Honesty.HardGate is { } hg)
                outp.Add((Stage5HonestyKind.HardGate, Stage5Copy.HardGate(hg.RequiredSections)));
            if (P.Honesty.Conflict is { } c)
                outp.Add((Stage5HonestyKind.Conflict, Stage5Copy.Conflict(Stage5Copy.Percent(c.Value))));
            if (P.Honesty.Uncertainty is { } u)
                outp.Add((Stage5HonestyKind.Uncertainty, Stage5Copy.Uncertainty(Stage5Copy.Percent(u.Value))));
            return outp;
        }
    }

    /// Within-class members with approved copy (single-member classes render nothing); an unmapped
    /// member is dropped (fail closed) rather than shown raw.
    public IReadOnlyList<string> WithinClassLines =>
        P.Determination.WithinClass
            .OrderBy(w => w.DisplayOrder)
            .Select(w => Stage5Copy.WithinClass(w.Hypothesis))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();

    /// Ordinary-case confidence text. The decisive-gate case uses the Framing-3 block instead. Nil
    /// belief → "No estimate" (never "0%"; STAGE5 §7).
    public string ConfidenceText =>
        P.Determination.Confidence is { } cm
            ? $"{Stage5Copy.ConfidenceLabel}: {Stage5Copy.Percent(cm.Value)}%"
            : Stage5Copy.ConfidenceNone;

    public string CompletenessText =>
        Stage5Copy.CompletenessValue(P.Completeness.Observed, P.Completeness.Expected);

    public string? CriticalGapText =>
        P.Honesty.HardGate is { } hg && hg.RequiredSections.Count > 0
            ? Stage5Copy.CriticalGap(hg.RequiredSections)
            : null;

    public string? ReferralText =>
        P.Determination.ReferralRequired ? Stage5Copy.Referral(P.Determination.ReferralToken) : null;
}
