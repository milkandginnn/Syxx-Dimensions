using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Threading;

namespace Syxx.Kit;

/// Assembles a `Conclusion` from what each check actually produced.
///
/// MATRIX-MIGRATION-PLAN.md Phase 3.4 — the Windows twin of
/// macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift. The DST engine (Dempster-Shafer belief
/// combination, 746 lines) is retired; this file is now the thin bridge that maps raw evidence
/// onto the matrix determinate engine.
///
/// History: this once fronted a Dempster-Shafer belief-combination engine (weight-of-evidence mass
/// functions, a hypothesis frame, pignistic betting probabilities, referral thresholds) that — across
/// several rewrites — kept producing a confidence/probability distribution that did not move in
/// response to real evidence. On 2026-07-21 the owner directed that engine removed in full. A later,
/// superseding owner directive (same day) directed a *new* determinative layer be integrated for
/// real: the matrix-based `MatrixDeterminativeEngine`.
///
/// So this file does two things now:
///  1. It still reports, un-massaged, exactly what ran / what each check observed / raw exposure
///     findings — every raw field traces directly to something a check produced.
///  2. It ALSO runs the matrix determinative engine over that same evidence and attaches the
///     resulting `DeterminativeResult` to the `Conclusion`. The engine is honest by construction:
///     unmapped / not-observed indicators are absent (never fabricated), missing/invalidated required
///     sections hard-gate their diagnoses (fail-closed), and abstention is a first-class outcome
///     (`.inconclusive`) rather than an unearned `.clean`. If the engine cannot even run (spec/bundle
///     validation failure), `determination` is left `null` — no result is invented.
public static class SynthesisEngine
{
    /// `now`/`config` kept for call-site compatibility during the migration off the old signature;
    /// `config` is currently unused (no thresholds left to tune here — the matrix engine owns its
    /// own `ThresholdProfile`).
    public sealed class Config
    {
        /// When true (default), the matrix determinative engine runs over the evidence to produce
        /// a result (`Determination` on the `Conclusion`). When false, no result is computed
        /// (`Determination` remains null) and the raw section/evidence data is still populated
        /// regardless. Useful for rollout gating or A/B testing during the matrix-engine cutover.
        public bool UseMatrixEngine { get; init; } = true;

        public Config() { }
    }

    public static Conclusion Synthesize(
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<SectionStatusEntry> sectionStatuses,
        IReadOnlyList<Advisory>? advisories = null,
        Config? config = null,
        DateTimeOffset? now = null,
        CancellationToken ct = default,
        // DST-era parameters — kept for call-site compatibility, ignored by the matrix engine.
        object? validatedDecisiveActs = null,
        object? answerMatrix = null,
        object? directObservations = null)
    {
        config ??= new Config();
        var when = now ?? DateTimeOffset.UtcNow;

        using var span = SyxxTracer.StartSpan("syxx.synthesis.synthesize",
            ActivityKind.Internal,
            ("synthesis.evidence_count", evidence.Count),
            ("synthesis.section_count", sectionStatuses.Count));
        var determination = config.UseMatrixEngine
            ? RunDeterminativeEngine(evidence, sectionStatuses, ct)
            : null;
        if (determination != null)
        {
            span?.SetTag("syxx.result", determination.State.ToString());
            span?.SetTag("syxx.result.diagnosis_count", determination.Diagnoses.Count);
            span?.SetTag("syxx.result.suppressed_count", determination.Suppressed.Count);
            SyxxTracer.SetOutcome(span, determination.State switch
            {
                DeterminativeState.Clean => SyxxTracer.Outcome.Success,
                DeterminativeState.ThreatDetected => SyxxTracer.Outcome.Failure,
                DeterminativeState.Inconclusive => SyxxTracer.Outcome.Inconclusive,
                _ => SyxxTracer.Outcome.Untested,
            }, evidence: true);
        }
        else
        {
            span?.SetTag("syxx.result", "none");
            SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive, reason: "engine_did_not_run", evidence: false);
        }
        var conclusion = new Conclusion(
            GeneratedAt: when,
            SectionStatuses: sectionStatuses,
            Evidence: evidence,
            Advisories: advisories ?? Array.Empty<Advisory>(),
            Determination: determination);
        SyxxDiagnostics.Breadcrumb(
            category: "engine.result",
            message: "SynthesisEngine concluded",
            level: DiagnosticSeverity.Warning,
            data: new Dictionary<string, string>
            {
                ["result"] = determination?.State.ToString() ?? "none",
                ["confidence"] = "",
                ["section_count"] = sectionStatuses.Count.ToString(CultureInfo.InvariantCulture),
                ["engine_version"] = "matrix.1"
            });
        return conclusion;
    }

    // MARK: - Determinative engine bridge

    /// Builds a `SyxxRunInput` from the raw evidence + section statuses and evaluates it with the
    /// matrix engine. Returns `null` (no manufactured result) if the engine throws — which, for a
    /// well-formed default spec, only happens on a malformed evidence bundle. Any such failure is a
    /// bug to fix, never a silent "clean."
    internal static DeterminativeResult? RunDeterminativeEngine(
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<SectionStatusEntry> sectionStatuses,
        CancellationToken ct = default)
    {
        var sections = sectionStatuses.Select(entry =>
        {
            var syxxSection = SynthesisSectionExtensions.FromSection(entry.Section);
            return new SyxxSectionInput(syxxSection, SynthesisEvidenceUsabilityExtensions.FromSectionStatus(entry.Status));
        }).ToList();

        // C063: real per-section datapoint completeness is the quality signal for every indicator
        // observed in that section. A duplicate section entry cannot survive to evaluation
        // (EvidenceBundle.Validate() rejects it → null determination), but this dict takes the
        // MINIMUM on collision (attenuates, never inflates).
        var completenessBySection = new Dictionary<Section, double>();
        foreach (var entry in sectionStatuses)
        {
            if (completenessBySection.TryGetValue(entry.Section, out var existing))
                completenessBySection[entry.Section] = Math.Min(existing, entry.Completeness);
            else
                completenessBySection[entry.Section] = entry.Completeness;
        }

        var indicators = MapEvidenceToIndicators(evidence, completenessBySection);
        var input = new SyxxRunInput(sections, indicators);
        try
        {
            return SyxxMatrixIntegration.Evaluate(input, ct);
        }
        catch (Exception ex)
        {
            // Honest failure: the engine could not evaluate this bundle. Do NOT fabricate a result.
            // Honest to the USER — but this was also silent to US. A null determination is how a
            // result goes missing: the user gets "the engine could not evaluate this run" and no
            // one ever learns why. Report it; still return null. (macOS twin: SynthesisEngine.swift)
            var kind = (ex as MatrixEngineException)?.Kind.ToString() ?? "unknown";
            SyxxDiagnostics.Capture(new DiagnosticEvent(
                ErrorClass: "engine.no_determination",
                Component: "SynthesisEngine",
                Section: null,
                Impact: MeasurementImpact.NoDetermination,
                Severity: DiagnosticSeverity.Error,
                Underlying: ex.GetType().FullName + ": " + ex.Message,
                // Group on the error VARIANT, never the interpolated message — the message embeds
                // diagnosis and indicator ids and would shard one bug into hundreds of issues.
                Fingerprint: new[] { "engine.no_determination", kind },
                Tags: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["syxx.engine_error_kind"] = kind,
                    ["syxx.section_count"] = sections.Count.ToString(CultureInfo.InvariantCulture),
                    ["syxx.indicator_count"] = indicators.Count.ToString(CultureInfo.InvariantCulture),
                },
                Extra: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    // Shape only — no hostnames, no addresses, no user data.
                    ["section_statuses"] = string.Join(",", sections
                        .Select(s => $"{s.Section}={s.Status}")
                        .OrderBy(x => x, StringComparer.Ordinal)),
                    ["indicator_ids"] = string.Join(",", indicators
                        .Select(i => i.Indicator.ToString())
                        .OrderBy(x => x, StringComparer.Ordinal)),
                }));
            return null;
        }
    }

    /// The current, deliberately-conservative mapping from raw `EvidenceId` observations onto the
    /// matrix engine's normalised 0…1 indicator strengths.
    ///
    /// Rules of this mapping (honesty-critical):
    ///  * Only observations whose meaning maps *unambiguously* to an indicator are emitted. Where a
    ///    single `EvidenceId` does not cleanly imply one indicator, it is left UNMAPPED — the
    ///    indicator stays absent (null strength), which the engine treats as "not observed," never
    ///    as zero-support-that-counts.
    ///  * Several `EvidenceId`s can map to the same indicator. Because `EvidenceBundle.Validate()`
    ///    rejects a duplicate indicator id, we collapse to ONE observation per indicator, taking the
    ///    MAX strength seen — a stronger corroboration never gets diluted by a weaker one.
    ///  * `contradictory` is false (presence means the observation fired). `quality` is NOT
    ///    hardcoded (C063): it is the datapoint completeness of the SAME section that produced the
    ///    winning observation. The engine attenuates multiplicatively:
    ///    `Pow(alignment, decay) * clamp01(quality)`. A section MISSING from `sectionCompleteness`
    ///    reads 0.0, never 1.0 (the B151/B173 lesson: an inert 1.0 default is fabricated certainty).
    ///
    /// Clean-support indicators (`resolverAgreement`, `routeClean`, `trustedWitnessOK`,
    /// `localEvidenceSealValid`) are intentionally NOT wired here: the current evidence model emits
    /// items for *findings*, and the trusted-witness / evidence-seal signals that would affirm a
    /// clean pattern are not yet plumbed as `EvidenceId`s. Consequently a benign run reaches
    /// `.inconclusive` rather than `.clean` — the honest posture (inconclusive over unearned secure)
    /// until those affirmative-clean signals are wired.
    internal static List<SyxxIndicatorInput> MapEvidenceToIndicators(
        IReadOnlyList<EvidenceItem> evidence,
        Dictionary<Section, double> sectionCompleteness)
    {
        // indicator -> (max strength, originating section)
        var best = new Dictionary<SyxxIndicator, (double Strength, Section Section)>();

        void Note(SyxxIndicator indicator, double strength, Section section)
        {
            if (best.TryGetValue(indicator, out var existing))
            {
                if (strength > existing.Strength)
                    best[indicator] = (strength, section);
            }
            else
            {
                best[indicator] = (strength, section);
            }
        }

        foreach (var item in evidence)
        {
            if (IndicatorMapping.TryGetValue(item.EvidenceId, out var entry))
                Note(entry.Indicator, entry.Strength, item.Section);
        }

        return best.Select(kvp =>
        {
            var (indicator, (strength, section)) = (kvp.Key, kvp.Value);
            // C063 — quality is the completeness of the SAME section as the winning observation.
            // A section MISSING from sectionCompleteness reads 0.0, never 1.0 (the B151/B173 lesson).
            // But this should not happen silently — it means an invariant broke.
            if (!sectionCompleteness.TryGetValue(section, out var rawCompleteness))
            {
                SyxxDiagnostics.Capture(new DiagnosticEvent(
                    ErrorClass: "synthesis.missing_completeness",
                    Component: "SynthesisEngine",
                    Section: section,
                    Impact: MeasurementImpact.NoDetermination,
                    Severity: DiagnosticSeverity.Warning,
                    Underlying: $"C063: section {section} has evidence but no completeness entry; quality defaults to 0.0",
                    Fingerprint: new[] { "synthesis.missing_completeness", section.ToString() },
                    Tags: null));
            }
            var quality = Math.Min(1.0, Math.Max(0.0, rawCompleteness));
            return new SyxxIndicatorInput(
                indicator,
                SynthesisSectionExtensions.FromSection(section),
                ObservedStrength: strength,
                Quality: quality,
                Contradictory: false);
        })
        // Stable order for deterministic bundles / parity.
        .OrderBy(i => i.Indicator.RawValue(), StringComparer.Ordinal)
        .ToList();
    }

    /// C064 — the reverse of `IndicatorMapping`: the set of raw `EvidenceId`s that feed any of the
    /// given indicators. Surfaces use it so a named diagnosis is justified ONLY by observations
    /// that actually support it (`DiagnosisScore.SupportingIndicators`), never by a raw-ledger
    /// prefix (the "spoofing headline because a privacy-leak line" disease).
    public static HashSet<EvidenceId> EvidenceIdsSupporting(IReadOnlyList<IndicatorId> indicators)
    {
        var wanted = new HashSet<IndicatorId>(indicators);
        var result = new HashSet<EvidenceId>();
        foreach (var (evidenceId, entry) in IndicatorMapping)
        {
            if (wanted.Contains(entry.Indicator.Id()))
                result.Add(evidenceId);
        }
        return result;
    }

    /// The unambiguous `EvidenceId` → (`SyxxIndicator`, strength) table. Anything not listed is
    /// deliberately unmapped. Strengths are conservative: a "confirmed" observation reads
    /// near-certain, an unconfirmed one moderate.
    private static readonly Dictionary<EvidenceId, (SyxxIndicator Indicator, double Strength)> IndicatorMapping = new()
    {
        // Prophet — answer authenticity
        [EvidenceId.AuthoritativeDivergesConfirmed] = (SyxxIndicator.AuthoritativeMismatch, 0.95),
        [EvidenceId.AuthoritativeDiverges] = (SyxxIndicator.AuthoritativeMismatch, 0.70),
        [EvidenceId.DnssecCanaryStripped] = (SyxxIndicator.SignedAnswerMismatch, 0.80),

        // Portal — path / redirection
        [EvidenceId.Redirection] = (SyxxIndicator.LanDnsRedirect, 0.90),
        [EvidenceId.GatewayDhcpOffLinkDNS] = (SyxxIndicator.ResolverPathAnomaly, 0.70),
        [EvidenceId.CaptivePortalActive] = (SyxxIndicator.CaptivePortalSignal, 0.90),

        // Glutton — transport / privacy
        [EvidenceId.DnsLeakConfirmed] = (SyxxIndicator.DnsLeakDetected, 0.95),
        [EvidenceId.EncryptedDnsBlocked] = (SyxxIndicator.ForcedPlainDnsFallback, 0.85),
        [EvidenceId.SvcbStripped] = (SyxxIndicator.ProtectedTransportBypass, 0.70),

        // Cage — live tampering patterns
        [EvidenceId.NxSubdomainHijack] = (SyxxIndicator.SuspiciousNxDomainPattern, 0.80),
        [EvidenceId.NxCanaryHijack] = (SyxxIndicator.SuspiciousNxDomainPattern, 0.80),
        [EvidenceId.DenialInjected] = (SyxxIndicator.LiveQueryTamperPattern, 0.75),
        [EvidenceId.ScriptMismatch] = (SyxxIndicator.LiveQueryContentRewrite, 0.75),

        // Witness / host — tentative: DNS exfiltration implies a local process tunnelling out.
        [EvidenceId.ExfilLikely] = (SyxxIndicator.LocalEnvironmentCompromised, 0.60),
    };
}

/// Extension to map the shared `Section` enum onto `SyxxSection`.
internal static class SynthesisSectionExtensions
{
    internal static SyxxSection FromSection(Section s) => s switch
    {
        Section.Prophet => SyxxSection.Prophet,
        Section.Mirror => SyxxSection.Mirror,
        Section.Portal => SyxxSection.Portal,
        Section.Glutton => SyxxSection.Glutton,
        Section.Cage => SyxxSection.Cage,
        Section.Witness => SyxxSection.Witness,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}

/// Extension to map the shared `SectionStatus` onto `EvidenceUsability`.
internal static class SynthesisEvidenceUsabilityExtensions
{
    internal static EvidenceUsability FromSectionStatus(SectionStatus s) => s switch
    {
        SectionStatus.Untested => EvidenceUsability.Untested,
        SectionStatus.Invalidated => EvidenceUsability.Invalidated,
        SectionStatus.Inconclusive => EvidenceUsability.Inconclusive,
        SectionStatus.Degraded => EvidenceUsability.Passed,
        SectionStatus.Positive => EvidenceUsability.Passed,
        SectionStatus.Negative => EvidenceUsability.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}