using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Syxx.Kit;

/// The output of a full Syxx run: which checks ran, what they observed, and any raw exposure
/// findings. This is deliberately NOT a diagnosis. Syxx does not compute a result, a confidence
/// score, or a probability distribution over threat hypotheses — it reports what was actually
/// measured and leaves interpretation to the reader. (Owner directive, 2026-07-21: the prior
/// determinative engine — Dempster-Shafer hypothesis combination, a hand-tuned weight rubric, a
/// synthesized "confidence" and "diagnosis" — produced a uniform, unmoving distribution across
/// every run regardless of real evidence, and is being removed in full, not patched again.)
///
/// MATRIX-MIGRATION-PLAN.md Phase 3.3 — the Windows twin of
/// macOS/Sources/SyxxKit/Synthesis/Conclusion.swift. DST fields removed: Hypothesis, Conclusivity,
/// Confidence, Probabilities, ReferralTriggered, Referral, MechanismFingerprint,
/// ValidatedDecisiveActs, Stage5, HypothesisProbability, WeightRubric, BenignExplanation.
/// Replaced by `Determination: DeterminativeResult?` — the matrix engine's tri-state result.
public sealed record Conclusion(
    [property: JsonPropertyName("generatedAt")]
    [property: JsonConverter(typeof(AppleReferenceDateJsonConverter))]
    DateTime GeneratedAt,
    [property: JsonPropertyName("sectionStatuses")] IReadOnlyList<SectionStatusEntry> SectionStatuses,
    [property: JsonPropertyName("evidence")] IReadOnlyList<EvidenceItem> Evidence,
    [property: JsonPropertyName("advisories")] IReadOnlyList<Advisory> Advisories,
    [property: JsonPropertyName("determination")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DeterminativeResult? Determination = null)
{
    /// True when a required check never ran — this report is incomplete, not "clean."
    /// Purely about what was measured, not a judgment about what it means.
    /// Distinct from `HasInvalidatedSections` (check ran but produced corrupted data).
    [JsonIgnore]
    public bool HasUntestedSections =>
        SectionStatuses.Any(s => s.Status == SectionStatus.Untested);

    /// True when a required check ran but produced untrustworthy output — the data
    /// is structurally invalid (corrupted packet, failed signature, out-of-bounds).
    [JsonIgnore]
    public bool HasInvalidatedSections =>
        SectionStatuses.Any(s => s.Status == SectionStatus.Invalidated);

    /// True when a required check never ran or ran untrustworthily — this report is incomplete,
    /// not "clean." Purely about what was measured, not a judgment about what it means.
    [JsonIgnore]
    public bool HasHardGatedSection =>
        HasUntestedSections || HasInvalidatedSections;

    /// Backward-compat: some test helpers still reference ReadsClean / IsInconclusive.
    /// These are thin wrappers over Determination, never DST fields.
    public bool ReadsClean() =>
        Determination?.State == DeterminativeState.Clean;

    /// True only when the engine ran and produced an Inconclusive result.
    /// Does NOT conflate with a null determination (engine did not run) — that is
    /// a categorically different state (see HasNoDetermination).
    public bool IsInconclusive() =>
        Determination?.State == DeterminativeState.Inconclusive;

    /// True when the engine could not run at all — the fifth state that must never be
    /// indistinguishable from Inconclusive ("ran, valid data, did not resolve").
    [JsonIgnore]
    public bool HasNoDetermination => Determination is null;
}

/// One observed signal — what a check saw and why. No weight, no hypothesis-support mapping:
/// those encoded a determinative judgment this product no longer makes. `source` is kept because
/// it is about deduplicating which probe produced an observation, not about scoring it.
public sealed record EvidenceItem(
    [property: JsonPropertyName("section")] Section Section,
    [property: JsonPropertyName("evidenceId")] EvidenceId EvidenceId,
    [property: JsonPropertyName("observation")] string Observation,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("source")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Source = null)
{
    /// Convenience constructor matching the macOS `EvidenceItem.init(section:evidenceId:observation:rationale:source:)`.
    public static EvidenceItem Create(Section section, EvidenceId evidenceId, string observation, string rationale, string? source = null)
        => new(section, evidenceId, observation, rationale, source ?? CorrelationGroup(evidenceId));

    /// Grouping for display only (e.g. "these three observations came from the same underlying
    /// probe") — not used to weight or combine anything.
    public static string? CorrelationGroup(EvidenceId id) => id switch
    {
        EvidenceId.NxSubdomainHijack or EvidenceId.DenialInjected or EvidenceId.DenialBare => "nxdomain_hijack",
        EvidenceId.DnsUnencrypted or EvidenceId.EncryptedDnsBlocked or EvidenceId.SvcbStripped => "transport_downgrade",
        EvidenceId.AuthoritativeDiverges or EvidenceId.AuthoritativeDivergesConfirmed => "authoritative_divergence",
        _ => null,
    };
}

[JsonConverter(typeof(EvidenceIdJsonConverter))]
public enum EvidenceId
{
    ResolverStatus,
    DnssecExpired,
    NxCanaryClean,
    NxCanaryHijack,
    NxCanaryAtypical,
    DnssecCanarySecure,
    DnssecCanaryStripped,
    DnssecCanaryExpired,
    Redirection,
    ScriptMismatch,
    ScriptMismatchInconclusive,
    CaptivePortalActive,
    DnsUnencrypted,
    DnsEncrypted,
    DnsLeakConfirmed,
    ExfilLikely,
    ExfilSuspicious,
    AnswerDrift,
    ConfigChange,
    AuthoritativeDiverges,
    SvcbStripped,
    AnswerUnstable,
    EncryptedDnsBlocked,
    NxSubdomainHijack,
    DenialBare,
    DenialInjected,
    Ipv4Suppressed,
    TtlLockstep,
    TtlExtreme,
    CnameChainDivergent,
    EdnsBlocked,
    EcsSubnetLeak,
    ResolverBannerAnomaly,
    WeakStubEntropy,
    ResponseSizeAnomaly,
    AuthoritativeDivergesConfirmed,
    GatewayDhcpOffLinkDNS,
    AttributionIncomplete,
}

/// Serializes EvidenceId as the exact camelCase rawValues used by the macOS twin
/// (Conclusion.swift:98-136). Never use enum-name reflection — a rename must not
/// silently fork the JSON schema.
public sealed class EvidenceIdJsonConverter : JsonConverter<EvidenceId>
{
    public override EvidenceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "resolverStatus" => EvidenceId.ResolverStatus,
            "dnssecExpired" => EvidenceId.DnssecExpired,
            "nxCanaryClean" => EvidenceId.NxCanaryClean,
            "nxCanaryHijack" => EvidenceId.NxCanaryHijack,
            "nxCanaryAtypical" => EvidenceId.NxCanaryAtypical,
            "dnssecCanarySecure" => EvidenceId.DnssecCanarySecure,
            "dnssecCanaryStripped" => EvidenceId.DnssecCanaryStripped,
            "dnssecCanaryExpired" => EvidenceId.DnssecCanaryExpired,
            "redirection" => EvidenceId.Redirection,
            "scriptMismatch" => EvidenceId.ScriptMismatch,
            "scriptMismatchInconclusive" => EvidenceId.ScriptMismatchInconclusive,
            "captivePortalActive" => EvidenceId.CaptivePortalActive,
            "dnsUnencrypted" => EvidenceId.DnsUnencrypted,
            "dnsEncrypted" => EvidenceId.DnsEncrypted,
            "dnsLeakConfirmed" => EvidenceId.DnsLeakConfirmed,
            "exfilLikely" => EvidenceId.ExfilLikely,
            "exfilSuspicious" => EvidenceId.ExfilSuspicious,
            "answerDrift" => EvidenceId.AnswerDrift,
            "configChange" => EvidenceId.ConfigChange,
            "authoritativeDiverges" => EvidenceId.AuthoritativeDiverges,
            "svcbStripped" => EvidenceId.SvcbStripped,
            "answerUnstable" => EvidenceId.AnswerUnstable,
            "encryptedDnsBlocked" => EvidenceId.EncryptedDnsBlocked,
            "nxSubdomainHijack" => EvidenceId.NxSubdomainHijack,
            "denialBare" => EvidenceId.DenialBare,
            "denialInjected" => EvidenceId.DenialInjected,
            "ipv4Suppressed" => EvidenceId.Ipv4Suppressed,
            "ttlLockstep" => EvidenceId.TtlLockstep,
            "ttlExtreme" => EvidenceId.TtlExtreme,
            "cnameChainDivergent" => EvidenceId.CnameChainDivergent,
            "ednsBlocked" => EvidenceId.EdnsBlocked,
            "ecsSubnetLeak" => EvidenceId.EcsSubnetLeak,
            "resolverBannerAnomaly" => EvidenceId.ResolverBannerAnomaly,
            "weakStubEntropy" => EvidenceId.WeakStubEntropy,
            "responseSizeAnomaly" => EvidenceId.ResponseSizeAnomaly,
            "authoritativeDivergesConfirmed" => EvidenceId.AuthoritativeDivergesConfirmed,
            "gatewayDhcpOffLinkDNS" => EvidenceId.GatewayDhcpOffLinkDNS,
            "attributionIncomplete" => EvidenceId.AttributionIncomplete,
            var s => throw new JsonException($"unknown EvidenceId '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, EvidenceId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

public static partial class EvidenceIdExtensions
{
    /// Returns the wire-format camelCase rawValue matching the macOS EvidenceId rawValue
    /// (Conclusion.swift:98-136). Used for JSON serialization and canonical byte construction.
    public static string RawValue(this EvidenceId id) => id switch
    {
        EvidenceId.ResolverStatus => "resolverStatus",
        EvidenceId.DnssecExpired => "dnssecExpired",
        EvidenceId.NxCanaryClean => "nxCanaryClean",
        EvidenceId.NxCanaryHijack => "nxCanaryHijack",
        EvidenceId.NxCanaryAtypical => "nxCanaryAtypical",
        EvidenceId.DnssecCanarySecure => "dnssecCanarySecure",
        EvidenceId.DnssecCanaryStripped => "dnssecCanaryStripped",
        EvidenceId.DnssecCanaryExpired => "dnssecCanaryExpired",
        EvidenceId.Redirection => "redirection",
        EvidenceId.ScriptMismatch => "scriptMismatch",
        EvidenceId.ScriptMismatchInconclusive => "scriptMismatchInconclusive",
        EvidenceId.CaptivePortalActive => "captivePortalActive",
        EvidenceId.DnsUnencrypted => "dnsUnencrypted",
        EvidenceId.DnsEncrypted => "dnsEncrypted",
        EvidenceId.DnsLeakConfirmed => "dnsLeakConfirmed",
        EvidenceId.ExfilLikely => "exfilLikely",
        EvidenceId.ExfilSuspicious => "exfilSuspicious",
        EvidenceId.AnswerDrift => "answerDrift",
        EvidenceId.ConfigChange => "configChange",
        EvidenceId.AuthoritativeDiverges => "authoritativeDiverges",
        EvidenceId.SvcbStripped => "svcbStripped",
        EvidenceId.AnswerUnstable => "answerUnstable",
        EvidenceId.EncryptedDnsBlocked => "encryptedDnsBlocked",
        EvidenceId.NxSubdomainHijack => "nxSubdomainHijack",
        EvidenceId.DenialBare => "denialBare",
        EvidenceId.DenialInjected => "denialInjected",
        EvidenceId.Ipv4Suppressed => "ipv4Suppressed",
        EvidenceId.TtlLockstep => "ttlLockstep",
        EvidenceId.TtlExtreme => "ttlExtreme",
        EvidenceId.CnameChainDivergent => "cnameChainDivergent",
        EvidenceId.EdnsBlocked => "ednsBlocked",
        EvidenceId.EcsSubnetLeak => "ecsSubnetLeak",
        EvidenceId.ResolverBannerAnomaly => "resolverBannerAnomaly",
        EvidenceId.WeakStubEntropy => "weakStubEntropy",
        EvidenceId.ResponseSizeAnomaly => "responseSizeAnomaly",
        EvidenceId.AuthoritativeDivergesConfirmed => "authoritativeDivergesConfirmed",
        EvidenceId.GatewayDhcpOffLinkDNS => "gatewayDhcpOffLinkDNS",
        EvidenceId.AttributionIncomplete => "attributionIncomplete",
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}

/// The six canonical disciplines a piece of evidence can come from.
[JsonConverter(typeof(SectionJsonConverter))]
public enum Section { Prophet, Mirror, Portal, Glutton, Cage, Witness }

public sealed class SectionJsonConverter : JsonConverter<Section>
{
    public override Section Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "prophet" => Section.Prophet,
            "mirror" => Section.Mirror,
            "portal" => Section.Portal,
            "glutton" => Section.Glutton,
            "cage" => Section.Cage,
            "witness" => Section.Witness,
            var s => throw new JsonException($"unknown Section '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, Section value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            Section.Prophet => "prophet",
            Section.Mirror => "mirror",
            Section.Portal => "portal",
            Section.Glutton => "glutton",
            Section.Cage => "cage",
            Section.Witness => "witness",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
}

/// Per-section terminal status. `untested`/`invalidated` mean no usable evidence exists from that
/// check; that is reported as a fact about the report's completeness, not modeled as a score.
[JsonConverter(typeof(SectionStatusJsonConverter))]
public enum SectionStatus { Untested, Invalidated, Inconclusive, Degraded, Positive, Negative }

public static class SectionStatusExtensions
{
    public static bool BlocksClean(this SectionStatus s) =>
        s is SectionStatus.Untested or SectionStatus.Invalidated;
}

public sealed class SectionStatusJsonConverter : JsonConverter<SectionStatus>
{
    public override SectionStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "untested" => SectionStatus.Untested,
            "invalidated" => SectionStatus.Invalidated,
            "inconclusive" => SectionStatus.Inconclusive,
            "degraded" => SectionStatus.Degraded,
            "positive" => SectionStatus.Positive,
            "negative" => SectionStatus.Negative,
            var s => throw new JsonException($"unknown SectionStatus '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, SectionStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            SectionStatus.Untested => "untested",
            SectionStatus.Invalidated => "invalidated",
            SectionStatus.Inconclusive => "inconclusive",
            SectionStatus.Degraded => "degraded",
            SectionStatus.Positive => "positive",
            SectionStatus.Negative => "negative",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
}

public sealed record SectionStatusEntry(
    [property: JsonPropertyName("section")] Section Section,
    [property: JsonPropertyName("status")] SectionStatus Status,
    double Completeness = 1.0,
    int? DatapointsObserved = null,
    int? DatapointsExpected = null)
{
    // Clamp completeness to [0,1] at construction time, matching macOS Conclusion.swift:164
    [property: JsonPropertyName("completeness")]
    public double Completeness { get; init; } = Math.Max(0.0, Math.Min(1.0, Completeness));

    [property: JsonPropertyName("datapointsObserved"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DatapointsObserved { get; init; } = DatapointsObserved;

    [property: JsonPropertyName("datapointsExpected"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DatapointsExpected { get; init; } = DatapointsExpected;
}

/// Colour/urgency buckets, mapped to the Io severity palette in the GUI. Used only by
/// `Advisory` (exposure findings) now — never to color a synthesized diagnosis.
[JsonConverter(typeof(SeverityJsonConverter))]
public enum Severity { Clean, Info, Warning, High, Critical, Unknown }

public sealed class SeverityJsonConverter : JsonConverter<Severity>
{
    public override Severity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "clean" => Severity.Clean,
            "info" => Severity.Info,
            "warning" => Severity.Warning,
            "high" => Severity.High,
            "critical" => Severity.Critical,
            "unknown" => Severity.Unknown,
            var s => throw new JsonException($"unknown Severity '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, Severity value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            Severity.Clean => "clean",
            Severity.Info => "info",
            Severity.Warning => "warning",
            Severity.High => "high",
            Severity.Critical => "critical",
            Severity.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
}

/// An exposure / security-hygiene finding — surfaced as a raw finding, never as part of any
/// synthesized judgment. (ROADMAP Portal / Workstream S.)
public sealed record Advisory(
    [property: JsonPropertyName("section")] Section Section,
    [property: JsonPropertyName("advisoryId")] AdvisoryId AdvisoryId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("severity")] Severity Severity);

[JsonConverter(typeof(AdvisoryIdJsonConverter))]
public enum AdvisoryId { OpenResolverExposure }

public sealed class AdvisoryIdJsonConverter : JsonConverter<AdvisoryId>
{
    public override AdvisoryId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "openResolverExposure" => AdvisoryId.OpenResolverExposure,
            var s => throw new JsonException($"unknown AdvisoryId '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, AdvisoryId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            AdvisoryId.OpenResolverExposure => "openResolverExposure",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        });
}

public static partial class EvidenceIdExtensions
{
    public static string DisplayName(this EvidenceId id) => id switch
    {
        EvidenceId.ResolverStatus => "DNS Resolver Status Check",
        EvidenceId.DnssecExpired => "Expired DNSSEC Signature",
        EvidenceId.NxCanaryClean => "Healthy Non-Existent Domain Resolution",
        EvidenceId.NxCanaryHijack => "Non-Existent Domain Interception",
        EvidenceId.NxCanaryAtypical => "Atypical Non-Existent Domain Resolution",
        EvidenceId.DnssecCanarySecure => "Secure DNSSEC Resolution",
        EvidenceId.DnssecCanaryStripped => "DNSSEC Validation Stripped",
        EvidenceId.DnssecCanaryExpired => "Expired DNSSEC Canary Check",
        EvidenceId.Redirection => "DNS Redirection Detected",
        EvidenceId.ScriptMismatch => "Script Structure Divergence",
        EvidenceId.ScriptMismatchInconclusive => "Inconclusive Content Warp Check",
        EvidenceId.CaptivePortalActive => "Active Captive Portal Detected",
        EvidenceId.DnsUnencrypted => "Unencrypted DNS Queries",
        EvidenceId.DnsEncrypted => "Encrypted DNS Queries",
        EvidenceId.DnsLeakConfirmed => "Confirmed DNS Query Leak",
        EvidenceId.ExfilLikely => "Likely Data Exfiltration",
        EvidenceId.ExfilSuspicious => "Suspicious DNS Query Patterns",
        EvidenceId.AnswerDrift => "DNS Answer Drift Detected",
        EvidenceId.ConfigChange => "System Network Configuration Change",
        EvidenceId.AuthoritativeDiverges => "Authoritative Nameserver Divergence",
        EvidenceId.SvcbStripped => "HTTPS/SVCB Record Stripped",
        EvidenceId.AnswerUnstable => "Unstable DNS Resolution",
        EvidenceId.EncryptedDnsBlocked => "Encrypted DNS Connection Blocked",
        EvidenceId.NxSubdomainHijack => "Subdomain Interception (Wildcard Ingestion)",
        EvidenceId.DenialBare => "Missing Authentication in Negative Answer",
        EvidenceId.DenialInjected => "Injected Negative Answer",
        EvidenceId.Ipv4Suppressed => "IPv4 Address Resolution Blocked",
        EvidenceId.TtlLockstep => "Synchronized Time-To-Live (TTL) Values",
        EvidenceId.TtlExtreme => "Abnormally Large Time-To-Live (TTL)",
        EvidenceId.CnameChainDivergent => "Outlier Canonical Name (CNAME) Chain",
        EvidenceId.EdnsBlocked => "Extension Mechanisms for DNS (EDNS) Blocked",
        EvidenceId.EcsSubnetLeak => "Client Subnet Leakage (ECS)",
        EvidenceId.ResolverBannerAnomaly => "Atypical Local DNS Forwarder Banner",
        EvidenceId.WeakStubEntropy => "Weak Stub Resolver Entropy",
        EvidenceId.ResponseSizeAnomaly => "Abnormally Large DNS Response Size",
        EvidenceId.AuthoritativeDivergesConfirmed => "Confirmed Authoritative Nameserver Divergence",
        EvidenceId.GatewayDhcpOffLinkDNS => "Off-Subnet DHCP DNS Configuration",
        EvidenceId.AttributionIncomplete => "Incomplete Process Attribution",
        _ => id.ToString(),
    };
}

/// Stub retained for WitnessSigner.cs compatibility during the Phase 3 cutover. The DST-era
/// ValidatedDecisiveActs type is gone; this minimal shell keeps the build compiling until
/// Phase 3.6 re-derives the WitnessSigner surface. Do NOT use for new code.
/// B227: SignDecisiveActs and VerifyDecisiveActs are now [Obsolete]; this type follows.
[Obsolete("Compile shim for WitnessSigner compatibility. Do NOT use for new code. Await Phase 3.6.")]
public sealed record ValidatedDecisiveActs(int Version, IReadOnlySet<EvidenceId> Acts)
{
    public static readonly ValidatedDecisiveActs Empty = new(0, new HashSet<EvidenceId>());
    public string? Signature { get; init; }
    public string? PublicKey { get; init; }

    public byte[] CanonicalBytes()
    {
        // Use RawValue() (camelCase wire format) rather than ToString() (PascalCase member names)
        // so signed material matches the rawValues used everywhere else on the wire.
        var acts = Acts.Select(a => a.RawValue()).OrderBy(a => a, StringComparer.Ordinal);
        return System.Text.Encoding.UTF8.GetBytes(
            $"syxx.decisive-acts.v1:{Version}:{string.Join(",", acts)}");
    }
}