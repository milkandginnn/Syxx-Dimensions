using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;

namespace SyxxUI.Presentation;

/// <summary>
/// Stage-5 presentation copy table (Windows) — the byte-for-byte wording twin of macOS
/// <c>macOS/Sources/SyxxApp/Views/Components/Stage5Copy.swift</c>. Presentation-owned, COPY-GATED:
/// every literal is from the APPROVED recommended set in STAGE5-WORDING-PROPOSALS.md (2026-07-12),
/// item 5 = Framing 3. The engine DTO carries only <see cref="CopyToken"/>s / enums; this table
/// resolves them to approved English. Unknown / non-approved tokens fail closed to
/// <see cref="GenericInconclusive"/> — never shown raw, honesty never suppressed (STAGE5 §10).
/// </summary>
public static class Stage5Copy
{
    // Fail-closed generic (approved inconclusive headline, proposals item 1h Option A).
    public const string GenericInconclusive = "Inconclusive — not enough evidence to decide";
    public const string NotACleanResult = "This is not a clean result.";

    // Mechanism-class headlines (proposals item 1, Option A recommended).
    public static string Headline(DeterminationState state, MechanismClass? mechanismClass) => state switch
    {
        DeterminationState.Clean => "No tampering detected in the checks that ran",
        DeterminationState.Inconclusive => GenericInconclusive,
        DeterminationState.Tampered => mechanismClass switch
        {
            MechanismClass.Rewrite => "The address you were given was changed",
            MechanismClass.Block => "This domain is being blocked",
            MechanismClass.Transport => "Your DNS traffic is being intercepted in transit",
            MechanismClass.Content => "The content was altered in transit",
            MechanismClass.Leak => "Your DNS queries are leaking",
            MechanismClass.Host => "The tampering is on this device",
            MechanismClass.Unresolved => "Interference detected — we can't yet say how",
            _ => GenericInconclusive, // fail closed: tampered must carry a class
        },
        _ => GenericInconclusive,
    };

    // Within-class split (proposals item 2, Option A recommended). Approved members exist only for
    // the REWRITE pair; any other hypothesis fails closed (null → caller suppresses the row).
    public const string WithinClassLead = "Most likely one of:";
    public static string? WithinClass(Hypothesis hypothesis) => hypothesis switch
    {
        Hypothesis.Spoofing => "Your resolver is returning a forged answer",
        Hypothesis.InterceptionOnPath => "Something on the path is rewriting answers",
        _ => null,
    };

    // Three honesty lines (proposals item 3, Option A recommended).
    public static string HardGate(IReadOnlyList<Section> sections) =>
        $"A clean result is impossible until {Join(sections)} run — required checks did not complete.";
    public static string Conflict(int percent) =>
        $"The evidence genuinely disagrees ({percent}%) — different checks point to different conclusions.";
    public static string Uncertainty(int percent) =>
        $"Evidence is thin ({percent}%) — there isn't enough to be sure either way.";

    // Referral (proposals item 4, Option A recommended).
    public const string ReferralGeneral =
        "This is at the edge of what automated checks can decide. Get a second opinion from a network expert before you act on it.";
    public static string Referral(CopyToken? token) =>
        token is { } t && t == CopyToken.ReferralMechanismUnresolved
            ? "Something is interfering, but the checks that ran can't pin down how. Have a network expert review the evidence below."
            : ReferralGeneral;

    // M4 forced-verdict / low-belief (proposals item 5, Framing 3 — the load-bearing pair).
    public const string SafetyGateTag = "(safety gate)";
    public const string BasisLine = "Basis: 1 verified decisive signal";
    public static string Corroboration(int percent) => $"Corroborating belief: {percent}%";
    public static string DecisiveExplainer(int percent) =>
        $"A verified decisive signal forces this verdict regardless of the belief number. The {percent}% reflects how much other evidence has piled on so far — a low number here means 'one strong signal, not yet corroborated,' not 'probably fine.'";

    // Provenance headers (proposals item 6a, Option A recommended).
    public const string DirectHeader = "What we observed directly";
    public const string InferredHeader = "What we concluded from it";
    public const string UnavailableHeader = "What we couldn't check";

    public static string UnavailableReasonText(UnavailableReason reason) => reason switch
    {
        UnavailableReason.NotRequested => "not requested in this run",
        UnavailableReason.Untested => "did not run",
        UnavailableReason.TimedOut => "timed out — no result",
        UnavailableReason.Invalidated => "ran but the result was invalidated",
        UnavailableReason.ParseFailed => "response could not be parsed",
        UnavailableReason.ProbeFailed => "the probe failed to complete",
        UnavailableReason.Unauthenticated => "could not be authenticated",
        UnavailableReason.Stale => "only stale/cached data was available",
        UnavailableReason.PermissionDenied => "blocked — insufficient permission",
        UnavailableReason.DependencyUnavailable => "a required dependency was unavailable",
        _ => "unavailable",
    };

    // Confidence & completeness (proposals item 6b / 6c, Option A recommended).
    public const string ConfidenceLabel = "Confidence";
    public const string ConfidenceNone = "No estimate — not enough evidence";
    public const string CompletenessLabel = "Evidence completeness";
    public static string CompletenessValue(int observed, int expected) =>
        $"{observed} of {expected} checks reached a conclusion";
    public static string CriticalGap(IReadOnlyList<Section> sections) =>
        $"A critical check ({Join(sections)}) is missing — this alone prevents a clean result.";

    // Section names (tool proper nouns, not gated verdict copy).
    public static string SectionName(Section s) => s.ToString();
    public static string Join(IReadOnlyList<Section> sections) =>
        string.Join(", ", sections.Select(SectionName));

    /// <summary>Round an engine scalar in [0,1] to an integer percent (proposals: N = round(value*100)).</summary>
    public static int Percent(double value) => (int)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);
}
