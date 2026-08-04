using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Syxx.Kit;

// MATRIX-MIGRATION-PLAN.md Phase 3, Step 3.2 — the ADDITIVE C# twin of
// macOS/Sources/SyxxKit/Synthesis/SyxxMatrixIntegration.swift. The Swift file is the
// SPECIFICATION: same 8 diagnoses, same rule table, same thresholds, transcribed
// constant-for-constant from SyxxMatrixIntegration.swift:64-229. CONSUMED BY PRODUCTION:
// SynthesisEngine.RunDeterminativeEngine bridges raw evidence through this spec into the
// MatrixDeterminativeEngine. The DST engine has been retired.
//
// CONSTANTS REMAIN GATE-ONLY (plan Step 3.2 / Phase 6): every weight, expectedStrength,
// decayExponent and threshold below is an UNCALIBRATED expert constant. They gate results; they
// must NEVER surface as user-visible numbers, and no surface may render fit/coverage/margin
// beyond what macOS already shows.

/// The six canonical Syxx sections, with the exact Swift rawValue strings.
[JsonConverter(typeof(SyxxSectionJsonConverter))]
public enum SyxxSection
{
    Prophet,
    Mirror,
    Portal,
    Glutton,
    Cage,
    Witness,
}

public static class SyxxSectionExtensions
{
    public static string RawValue(this SyxxSection s) => s switch
    {
        SyxxSection.Prophet => "prophet",
        SyxxSection.Mirror => "mirror",
        SyxxSection.Portal => "portal",
        SyxxSection.Glutton => "glutton",
        SyxxSection.Cage => "cage",
        SyxxSection.Witness => "witness",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static SectionId Id(this SyxxSection s) => new(s.RawValue());
}

public sealed class SyxxSectionJsonConverter : JsonConverter<SyxxSection>
{
    public override SyxxSection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "prophet" => SyxxSection.Prophet,
            "mirror" => SyxxSection.Mirror,
            "portal" => SyxxSection.Portal,
            "glutton" => SyxxSection.Glutton,
            "cage" => SyxxSection.Cage,
            "witness" => SyxxSection.Witness,
            var s => throw new JsonException($"unknown SyxxSection '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, SyxxSection value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

[JsonConverter(typeof(SyxxIndicatorJsonConverter))]
public enum SyxxIndicator
{
    // Prophet
    ResolverAgreement,
    ResolverDisagreement,
    AuthoritativeMismatch,
    SignedAnswerMismatch,

    // Mirror
    RemoteLocalDivergence,
    GeoVantageDivergence,
    MultiResolverDisparity,

    // Portal
    LanDnsRedirect,
    ResolverPathAnomaly,
    RouteClean,
    CaptivePortalSignal,

    // Glutton
    DnsLeakDetected,
    ProtectedTransportBypass,
    ForcedPlainDnsFallback,

    // Cage
    LiveQueryTamperPattern,
    LiveQueryContentRewrite,
    SuspiciousNxDomainPattern,

    // Witness
    TrustedWitnessOK,
    SignedWitnessMismatch,
    LocalEvidenceSealValid,
    LocalEnvironmentCompromised,
}

public static class SyxxIndicatorExtensions
{
    public static string RawValue(this SyxxIndicator i) => i switch
    {
        SyxxIndicator.ResolverAgreement => "resolverAgreement",
        SyxxIndicator.ResolverDisagreement => "resolverDisagreement",
        SyxxIndicator.AuthoritativeMismatch => "authoritativeMismatch",
        SyxxIndicator.SignedAnswerMismatch => "signedAnswerMismatch",
        SyxxIndicator.RemoteLocalDivergence => "remoteLocalDivergence",
        SyxxIndicator.GeoVantageDivergence => "geoVantageDivergence",
        SyxxIndicator.MultiResolverDisparity => "multiResolverDisparity",
        SyxxIndicator.LanDnsRedirect => "lanDnsRedirect",
        SyxxIndicator.ResolverPathAnomaly => "resolverPathAnomaly",
        SyxxIndicator.RouteClean => "routeClean",
        SyxxIndicator.CaptivePortalSignal => "captivePortalSignal",
        SyxxIndicator.DnsLeakDetected => "dnsLeakDetected",
        SyxxIndicator.ProtectedTransportBypass => "protectedTransportBypass",
        SyxxIndicator.ForcedPlainDnsFallback => "forcedPlainDnsFallback",
        SyxxIndicator.LiveQueryTamperPattern => "liveQueryTamperPattern",
        SyxxIndicator.LiveQueryContentRewrite => "liveQueryContentRewrite",
        SyxxIndicator.SuspiciousNxDomainPattern => "suspiciousNxDomainPattern",
        SyxxIndicator.TrustedWitnessOK => "trustedWitnessOK",
        SyxxIndicator.SignedWitnessMismatch => "signedWitnessMismatch",
        SyxxIndicator.LocalEvidenceSealValid => "localEvidenceSealValid",
        SyxxIndicator.LocalEnvironmentCompromised => "localEnvironmentCompromised",
        _ => throw new ArgumentOutOfRangeException(nameof(i)),
    };

    public static IndicatorId Id(this SyxxIndicator i) => new(i.RawValue());
}

public sealed class SyxxIndicatorJsonConverter : JsonConverter<SyxxIndicator>
{
    private static readonly Dictionary<string, SyxxIndicator> ByRawValue =
        Enum.GetValues<SyxxIndicator>().ToDictionary(i => i.RawValue(), i => i, StringComparer.Ordinal);

    public override SyxxIndicator Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s is not null && ByRawValue.TryGetValue(s, out var i)
            ? i : throw new JsonException($"unknown SyxxIndicator '{s}'");
    }
    public override void Write(Utf8JsonWriter writer, SyxxIndicator value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

[JsonConverter(typeof(SyxxDiagnosisJsonConverter))]
public enum SyxxDiagnosis
{
    Clean,
    DnsSpoofing,
    CensorshipBlocking,
    TransportDowngrade,
    DnsInterception,
    ContentTampering,
    DnsPrivacyLeak,
    LocalCompromise,
}

public static class SyxxDiagnosisExtensions
{
    public static string RawValue(this SyxxDiagnosis d) => d switch
    {
        SyxxDiagnosis.Clean => "clean",
        SyxxDiagnosis.DnsSpoofing => "dnsSpoofing",
        SyxxDiagnosis.CensorshipBlocking => "censorshipBlocking",
        SyxxDiagnosis.TransportDowngrade => "transportDowngrade",
        SyxxDiagnosis.DnsInterception => "dnsInterception",
        SyxxDiagnosis.ContentTampering => "contentTampering",
        SyxxDiagnosis.DnsPrivacyLeak => "dnsPrivacyLeak",
        SyxxDiagnosis.LocalCompromise => "localCompromise",
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public static DiagnosisId Id(this SyxxDiagnosis d) => new(d.RawValue());
}

public sealed class SyxxDiagnosisJsonConverter : JsonConverter<SyxxDiagnosis>
{
    private static readonly Dictionary<string, SyxxDiagnosis> ByRawValue =
        Enum.GetValues<SyxxDiagnosis>().ToDictionary(d => d.RawValue(), d => d, StringComparer.Ordinal);

    public override SyxxDiagnosis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s is not null && ByRawValue.TryGetValue(s, out var d)
            ? d : throw new JsonException($"unknown SyxxDiagnosis '{s}'");
    }
    public override void Write(Utf8JsonWriter writer, SyxxDiagnosis value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

public sealed class SyxxMatrixBuilder
{
    public SyxxMatrixBuilder() { }

    /// Transcribed constant-for-constant from `SyxxMatrixIntegration.swift` `buildDefaultSpec()`
    /// (rules :79-199, thresholds :212-226). A transcription typo in any constant is a
    /// same-evidence-different-result parity defect; the Phase 5 spec byte-oracle pins this.
    public MatrixSpec BuildDefaultSpec()
    {
        var prophet = SyxxSection.Prophet.Id();
        var mirror = SyxxSection.Mirror.Id();
        var portal = SyxxSection.Portal.Id();
        var glutton = SyxxSection.Glutton.Id();
        var cage = SyxxSection.Cage.Id();
        var witness = SyxxSection.Witness.Id();

        var clean = new DiagnosisSpec(
            id: SyxxDiagnosis.Clean.Id(),
            displayName: "Clean",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.ResolverAgreement.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "resolver"),
                new DiagnosisIndicatorRule(SyxxIndicator.RouteClean.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.90, Weight: 0.80, EvidenceClass.Major, IndependenceGroup: "route"),
                new DiagnosisIndicatorRule(SyxxIndicator.TrustedWitnessOK.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.20, EvidenceClass.Decisive, IndependenceGroup: "trust"),
                new DiagnosisIndicatorRule(SyxxIndicator.LocalEvidenceSealValid.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 0.70, EvidenceClass.Minor, IndependenceGroup: "local"),
                new DiagnosisIndicatorRule(SyxxIndicator.LanDnsRedirect.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 1.25, EvidenceClass.Decisive, IndependenceGroup: "path"),
                new DiagnosisIndicatorRule(SyxxIndicator.AuthoritativeMismatch.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "answer"),
                new DiagnosisIndicatorRule(SyxxIndicator.DnsLeakDetected.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 0.90, EvidenceClass.Major, IndependenceGroup: "privacy"),
                new DiagnosisIndicatorRule(SyxxIndicator.LocalEnvironmentCompromised.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 1.30, EvidenceClass.Decisive, IndependenceGroup: "host"),
            },
            requiredSections: new[] { prophet, witness },
            exclusionaryIndicators: new[]
            {
                SyxxIndicator.LanDnsRedirect.Id(),
                SyxxIndicator.AuthoritativeMismatch.Id(),
                SyxxIndicator.LocalEnvironmentCompromised.Id(),
            },
            mutuallyExclusiveWith: new[]
            {
                SyxxDiagnosis.DnsInterception.Id(),
                SyxxDiagnosis.DnsSpoofing.Id(),
                SyxxDiagnosis.ContentTampering.Id(),
                SyxxDiagnosis.LocalCompromise.Id(),
            });

        var dnsSpoofing = new DiagnosisSpec(
            id: SyxxDiagnosis.DnsSpoofing.Id(),
            displayName: "DNS spoofing / poisoning",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.AuthoritativeMismatch.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.20, EvidenceClass.Decisive, IndependenceGroup: "answer", DecayExponent: 1.3),
                new DiagnosisIndicatorRule(SyxxIndicator.SignedAnswerMismatch.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.90, Weight: 1.10, EvidenceClass.Major, IndependenceGroup: "signature"),
                new DiagnosisIndicatorRule(SyxxIndicator.SignedWitnessMismatch.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.80, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "trust"),
                new DiagnosisIndicatorRule(SyxxIndicator.ResolverDisagreement.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.80, Weight: 0.80, EvidenceClass.Major, IndependenceGroup: "resolver"),
                new DiagnosisIndicatorRule(SyxxIndicator.TrustedWitnessOK.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 0.90, EvidenceClass.Major, IndependenceGroup: "trust"),
            },
            requiredSections: new[] { prophet, mirror, witness },
            exclusionaryIndicators: new[] { SyxxIndicator.TrustedWitnessOK.Id() },
            mutuallyExclusiveWith: new[] { SyxxDiagnosis.Clean.Id() },
            allowedCooccurrenceWith: new[] { SyxxDiagnosis.ContentTampering.Id() });

        var censorshipBlocking = new DiagnosisSpec(
            id: SyxxDiagnosis.CensorshipBlocking.Id(),
            displayName: "Censorship / blocking",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.CaptivePortalSignal.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.80, Weight: 0.70, EvidenceClass.Major, IndependenceGroup: "portal"),
                new DiagnosisIndicatorRule(SyxxIndicator.RemoteLocalDivergence.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.85, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "vantage"),
                new DiagnosisIndicatorRule(SyxxIndicator.GeoVantageDivergence.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.90, Weight: 1.10, EvidenceClass.Decisive, IndependenceGroup: "geo"),
                new DiagnosisIndicatorRule(SyxxIndicator.SuspiciousNxDomainPattern.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.70, Weight: 0.80, EvidenceClass.Minor, IndependenceGroup: "live"),
            },
            requiredSections: new[] { mirror, portal },
            mutuallyExclusiveWith: Array.Empty<DiagnosisId>(),
            allowedCooccurrenceWith: new[] { SyxxDiagnosis.TransportDowngrade.Id() });

        var transportDowngrade = new DiagnosisSpec(
            id: SyxxDiagnosis.TransportDowngrade.Id(),
            displayName: "Transport downgrade",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.ForcedPlainDnsFallback.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.20, EvidenceClass.Decisive, IndependenceGroup: "transport", DecayExponent: 1.4),
                new DiagnosisIndicatorRule(SyxxIndicator.ProtectedTransportBypass.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.90, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "privacy"),
                new DiagnosisIndicatorRule(SyxxIndicator.RemoteLocalDivergence.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.60, Weight: 0.60, EvidenceClass.Minor, IndependenceGroup: "vantage"),
            },
            requiredSections: new[] { glutton, mirror },
            mutuallyExclusiveWith: Array.Empty<DiagnosisId>(),
            allowedCooccurrenceWith: new[] { SyxxDiagnosis.CensorshipBlocking.Id(), SyxxDiagnosis.DnsPrivacyLeak.Id() });

        var dnsInterception = new DiagnosisSpec(
            id: SyxxDiagnosis.DnsInterception.Id(),
            displayName: "DNS interception (transparent proxy)",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.LanDnsRedirect.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.30, EvidenceClass.Decisive, IndependenceGroup: "path", DecayExponent: 1.5),
                new DiagnosisIndicatorRule(SyxxIndicator.ResolverPathAnomaly.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.90, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "route"),
                new DiagnosisIndicatorRule(SyxxIndicator.RemoteLocalDivergence.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.70, Weight: 0.70, EvidenceClass.Minor, IndependenceGroup: "vantage"),
                new DiagnosisIndicatorRule(SyxxIndicator.TrustedWitnessOK.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "trust"),
            },
            requiredSections: new[] { portal, prophet },
            exclusionaryIndicators: new[] { SyxxIndicator.TrustedWitnessOK.Id() },
            mutuallyExclusiveWith: new[] { SyxxDiagnosis.Clean.Id() },
            allowedCooccurrenceWith: new[] { SyxxDiagnosis.ContentTampering.Id() });

        var contentTampering = new DiagnosisSpec(
            id: SyxxDiagnosis.ContentTampering.Id(),
            displayName: "Content tampering",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.LiveQueryTamperPattern.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.90, Weight: 1.20, EvidenceClass.Decisive, IndependenceGroup: "live", DecayExponent: 1.3),
                new DiagnosisIndicatorRule(SyxxIndicator.LiveQueryContentRewrite.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.85, Weight: 1.10, EvidenceClass.Major, IndependenceGroup: "rewrite"),
                new DiagnosisIndicatorRule(SyxxIndicator.RemoteLocalDivergence.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.65, Weight: 0.70, EvidenceClass.Minor, IndependenceGroup: "vantage"),
                new DiagnosisIndicatorRule(SyxxIndicator.SignedWitnessMismatch.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.65, Weight: 0.80, EvidenceClass.Major, IndependenceGroup: "trust"),
            },
            requiredSections: new[] { cage, mirror },
            mutuallyExclusiveWith: Array.Empty<DiagnosisId>(),
            allowedCooccurrenceWith: new[] { SyxxDiagnosis.DnsSpoofing.Id(), SyxxDiagnosis.DnsInterception.Id() });

        var dnsPrivacyLeak = new DiagnosisSpec(
            id: SyxxDiagnosis.DnsPrivacyLeak.Id(),
            displayName: "DNS privacy leak",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.DnsLeakDetected.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.20, EvidenceClass.Decisive, IndependenceGroup: "privacy"),
                new DiagnosisIndicatorRule(SyxxIndicator.ProtectedTransportBypass.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.80, Weight: 1.00, EvidenceClass.Major, IndependenceGroup: "transport"),
                new DiagnosisIndicatorRule(SyxxIndicator.RouteClean.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 0.40, EvidenceClass.Minor, IndependenceGroup: "route"),
            },
            requiredSections: new[] { glutton },
            mutuallyExclusiveWith: Array.Empty<DiagnosisId>(),
            allowedCooccurrenceWith: new[] { SyxxDiagnosis.TransportDowngrade.Id() });

        var localCompromise = new DiagnosisSpec(
            id: SyxxDiagnosis.LocalCompromise.Id(),
            displayName: "Local compromise / exfiltration",
            rules: new[]
            {
                new DiagnosisIndicatorRule(SyxxIndicator.LocalEnvironmentCompromised.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.95, Weight: 1.30, EvidenceClass.Decisive, IndependenceGroup: "host"),
                new DiagnosisIndicatorRule(SyxxIndicator.LocalEvidenceSealValid.Id(), IndicatorPolarity.Excludes, ExpectedStrength: 1.0, Weight: 0.90, EvidenceClass.Major, IndependenceGroup: "seal"),
                new DiagnosisIndicatorRule(SyxxIndicator.DnsLeakDetected.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.75, Weight: 0.70, EvidenceClass.Minor, IndependenceGroup: "privacy"),
                new DiagnosisIndicatorRule(SyxxIndicator.LiveQueryTamperPattern.Id(), IndicatorPolarity.Supports, ExpectedStrength: 0.70, Weight: 0.80, EvidenceClass.Major, IndependenceGroup: "live"),
            },
            requiredSections: new[] { witness },
            exclusionaryIndicators: Array.Empty<IndicatorId>(),
            mutuallyExclusiveWith: new[] { SyxxDiagnosis.Clean.Id() });

        return new MatrixSpec(
            diagnoses: new[]
            {
                clean,
                dnsSpoofing,
                censorshipBlocking,
                transportDowngrade,
                dnsInterception,
                contentTampering,
                dnsPrivacyLeak,
                localCompromise,
            },
            thresholds: new ThresholdProfile(
                MinimumCoverageForProbable: 0.55,
                MinimumCoverageForConclusive: 0.80,
                MinimumFitForProbable: 0.62,
                MinimumFitForConclusive: 0.84,
                MinimumMarginForProbable: 0.08,
                MinimumMarginForConclusive: 0.18,
                MaxContradictionForConclusive: 0.10,
                MaxContradictionForProbable: 0.22,
                MinimumIndependentSupportGroupsForProbable: 1,
                MinimumIndependentSupportGroupsForConclusive: 2,
                RequireDecisiveOrTwoMajorsForConclusive: true,
                HardGateOnMissingRequiredSections: true,
                FailClosedOnInvalidatedRequiredSections: true));
    }
}

public sealed record SyxxSectionInput(
    [property: JsonPropertyName("section")] SyxxSection Section,
    // NOTE: this is the matrix engine's `EvidenceUsability` (was `SectionStatus` in the original
    // draft). Syxx.Kit already ships a public `SectionStatus` (Conclusion.cs) with different
    // semantics, so the engine's usability axis is a distinct type — see the collision note in
    // `MatrixDeterminativeEngine.cs`. Callers converting a real run status use
    // `EvidenceUsabilityExtensions.FromSectionStatus`.
    [property: JsonPropertyName("status")] EvidenceUsability Status,
    [property: JsonPropertyName("trusted")] bool Trusted = true,
    [property: JsonPropertyName("notes")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null)
{
    public SectionResult ToSectionResult()
        => new(Section.Id(), Status, Required: false, Trusted: Trusted, Notes: Notes);
}

public sealed record SyxxIndicatorInput(
    [property: JsonPropertyName("indicator")] SyxxIndicator Indicator,
    [property: JsonPropertyName("section")] SyxxSection Section,
    [property: JsonPropertyName("observedStrength")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ObservedStrength,
    [property: JsonPropertyName("quality")] double Quality = 1.0,
    [property: JsonPropertyName("contradictory")] bool Contradictory = false,
    [property: JsonPropertyName("notes")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null)
{
    public ObservedIndicator ToObservedIndicator()
        => new(Indicator.Id(), Section.Id(), ObservedStrength, Quality, Contradictory, Notes);
}

public sealed record SyxxRunInput(
    [property: JsonPropertyName("sections")] IReadOnlyList<SyxxSectionInput> Sections,
    [property: JsonPropertyName("indicators")] IReadOnlyList<SyxxIndicatorInput> Indicators)
{
    public EvidenceBundle ToEvidenceBundle()
        => new(Sections.Select(s => s.ToSectionResult()).ToList(),
               Indicators.Select(i => i.ToObservedIndicator()).ToList());
}

public static class SyxxMatrixIntegration
{
    public static MatrixDeterminativeEngine MakeEngine()
        => new(new SyxxMatrixBuilder().BuildDefaultSpec());

    public static DeterminativeResult Evaluate(SyxxRunInput input, CancellationToken ct = default)
        => MakeEngine().Evaluate(input.ToEvidenceBundle(), ct);

    /// The default spec as JSON with SORTED KEYS, the C# side of the Phase 5 spec-parity oracle
    /// (`corpus/matrix/default-spec.frozen.json`). F042: now loaded by MatrixGoldenCorpusTests.cs
    /// on both platforms — a transcription typo in any of the ~100 constants is caught here.
    /// Sorted-key emission is done by a canonical re-write because System.Text.Json has no native
    /// sorted-keys mode; number tokens are preserved verbatim from the serializer (shortest
    /// round-trip, same rule Swift's encoder uses).
    /// KNOWN CROSS-PLATFORM CAVEAT (report, don't hide): Swift encodes the `knownSections`
    /// Set as an array in per-process hash order, so the Swift dump of that ONE field is not
    /// byte-stable across launches; the Phase 5 canonical rule must sort it on both platforms.
    public static string DefaultSpecJson(bool prettyPrinted = true)
    {
        var node = JsonSerializer.SerializeToNode(new SyxxMatrixBuilder().BuildDefaultSpec())
            ?? throw new InvalidOperationException("default spec serialized to null");
        return MatrixCanonicalJson.SortedKeys(node, prettyPrinted);
    }
}

/// Canonical (sorted-key) JSON emission for parity comparison. Internal: a parity-harness tool,
/// not public API.
internal static class MatrixCanonicalJson
{
    internal static string SortedKeys(System.Text.Json.Nodes.JsonNode node, bool prettyPrinted)
    {
        using var stream = new System.IO.MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = prettyPrinted,
            // Deterministic output regardless of host OS line endings.
            NewLine = "\n",
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            WriteSorted(node, writer);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSorted(System.Text.Json.Nodes.JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case System.Text.Json.Nodes.JsonObject obj:
                writer.WriteStartObject();
                foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(kv.Key);
                    WriteSorted(kv.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case System.Text.Json.Nodes.JsonArray arr:
                writer.WriteStartArray();
                foreach (var element in arr) { WriteSorted(element, writer); }
                writer.WriteEndArray();
                break;
            case null:
                writer.WriteNullValue();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }
}
