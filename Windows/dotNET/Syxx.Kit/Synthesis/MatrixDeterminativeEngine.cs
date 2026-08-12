using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Syxx.Kit;

// MARK: - MatrixDeterminativeEngine (Windows twin)
//
// MATRIX-MIGRATION-PLAN.md Phase 3, Step 3.1 — the ADDITIVE, semantically identical C# port of
// macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift (post-Phase-1: belowTier,
// rawSupport/observedSupport, quality-independent overhang admission, required-section gate BEFORE
// hard exclusion). The Swift file is the SPECIFICATION; this file mirrors it section-for-section,
// including the doc comments (they carry the honesty contract). CONSUMED BY PRODUCTION:
// SynthesisEngine.RunDeterminativeEngine bridges raw evidence through these types into the
// determinative engine; the DST engine has been retired.
//
// Type-mapping decisions (made once, per plan Step 3.1):
//  * ID wrappers -> readonly record structs with System.Text.Json converters serializing as bare
//    strings (parity with Swift's singleValueContainer encoding).
//  * Enums serialize as the exact Swift rawValue strings (camelCase) via EXPLICIT converters —
//    never enum-name reflection — so a rename can never silently fork the JSON schema.
//  * Swift structs -> sealed records with IReadOnlyList<> collections; JsonPropertyName carries the
//    exact Swift Codable key on every property.
//  * MatrixEngineError -> MatrixEngineException carrying the same 13-case discriminant
//    (MatrixEngineErrorKind) and byte-identical message strings.
//  * round6 -> MatrixRounding.Round6: Math.Round(x * 1_000_000, MidpointRounding.ToEven) /
//    1_000_000 — the twin of Swift's ×1e6, .rounded(.toNearestOrEven), ÷1e6. NOTE: the retired
//    DST `SynthesisEngine.RoundPercent` (away-from-zero) and the engine-internal `Pct` helper are
//    DELIBERATELY different rounding modes; do not unify them (plan Step 3.1 arithmetic-parity
//    note: Swift's bare `.rounded()` in `pct` is schoolbook away-from-zero).
//
// The matrix engine turns a bundle of section results and observed indicators (produced by
// Prophet / Mirror / Portal / Glutton / Cage / Witness) into a `DeterminativeResult`: an overall
// tri-state result plus the ranked set of diagnoses that the evidence actually supports, each
// with the metrics that justify it.
//
// HARD INVARIANT (from the reasoning-design discussion, not optional): the non-scored states —
// untested, invalidated, and inconclusive — are NEVER emitted as a ranked/scored diagnosis. They are
// represented by the overall `DeterminativeResult.State` (`Inconclusive`) with an *empty* ranked
// list and an explanation. A diagnosis appears in `result.Diagnoses` only when the evidence
// affirmatively reaches at least the `Probable` tier for it.
//
// CRITICAL DISTINCTION: `Inconclusive` is categorically different from `Untested` and
// `Invalidated`. `Inconclusive` means the check RAN, produced VALID data, but the data did
// not resolve to a clear finding — it is USABLE evidence (EvidenceUsability.IsUsable == true)
// and does NOT hard-gate diagnoses that depend on that section. `Untested` (never ran) and
// `Invalidated` (ran but produced corrupted/untrustworthy data) are NOT usable and DO
// hard-gate. Conflating these three states — treating a valid-but-ambiguous run the same as a
// missing or corrupted one — is a structural defect that misclassifies working evidence.
//
// Missing / not-observed indicators (null `ObservedStrength`) are treated as "not observed" —
// they lower coverage and push toward abstention; they are never read as support and never
// manufacture a confident result.
//
// Honesty-over-reassurance is enforced structurally:
//  * A required section that is missing, untested, invalidated, or locked HARD-GATES the
//    diagnoses that depend on it — a gated diagnosis cannot be emitted (fail-closed).
//    `Inconclusive` sections do NOT hard-gate (see EvidenceUsability.IsUsable).
//  * `clean` cannot be emitted while any threat diagnosis carries real observed support that was
//    suppressed by a hard gate / missing evidence, or that fired but fell below the reporting tier
//    (C062 `belowTier`) — an unverifiable or unconfirmed threat signal forces `Inconclusive`,
//    never a reassuring `Clean`. Overhang admission and the clean-blocking re-check both measure
//    the un-attenuated observed support (C065): gating crushes strength and thin sections crush
//    quality, and neither may hide a signal that actually fired.
//  * A dampener (low `Quality`, a `Contradictory` flag) can lower a diagnosis's fit but the
//    exclusionary / contradiction mass is bounded — it can attenuate, never silently zero, an
//    otherwise-present threat, and a genuinely-firing exclusionary indicator dominates by hard gate
//    rather than by arithmetic cancellation.

// MARK: Identifier value types

/// Opaque string identifiers. Kept as distinct wrapper types so a section id can never be passed
/// where an indicator id is expected, etc. Serializes as a bare string for compact JSON.
[JsonConverter(typeof(SectionIdJsonConverter))]
public readonly record struct SectionId(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(IndicatorIdJsonConverter))]
public readonly record struct IndicatorId(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(DiagnosisIdJsonConverter))]
public readonly record struct DiagnosisId(string Value)
{
    public override string ToString() => Value;
}

public sealed class SectionIdJsonConverter : JsonConverter<SectionId>
{
    public override SectionId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("SectionId must be a string"));
    public override void Write(Utf8JsonWriter writer, SectionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

public sealed class IndicatorIdJsonConverter : JsonConverter<IndicatorId>
{
    public override IndicatorId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("IndicatorId must be a string"));
    public override void Write(Utf8JsonWriter writer, IndicatorId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

public sealed class DiagnosisIdJsonConverter : JsonConverter<DiagnosisId>
{
    public override DiagnosisId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? throw new JsonException("DiagnosisId must be a string"));
    public override void Write(Utf8JsonWriter writer, DiagnosisId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

// MARK: Evidence usability
//
// NOTE ON THE `SectionStatus` NAME COLLISION (deliberate design decision, mirrored from macOS):
// Syxx.Kit already ships a public `SectionStatus` in `Conclusion.cs` whose semantics answer
// "did this check run and what did it FIND" (Untested / Invalidated / Inconclusive / Degraded /
// Positive / Negative). The matrix engine needs a *different* axis: "is this section's output
// USABLE as a scoring input, and how do we treat it at the required-section gate." Reusing the
// name would conflate two genuinely different questions. So the engine defines its own
// `EvidenceUsability`, and the mapping from a real `SectionStatus` is made explicit in
// `EvidenceUsabilityExtensions.FromSectionStatus` below rather than guessed silently.
[JsonConverter(typeof(EvidenceUsabilityJsonConverter))]
public enum EvidenceUsability
{
    /// The section produced usable, self-consistent output that resolved to a finding.
    Passed,
    /// The section ran but reached a definite negative/failed outcome that is itself usable input.
    Failed,
    /// The section ran but its output does not resolve — usable only as an abstention signal.
    Inconclusive,
    /// The section ran but a required premise failed; its output must not be trusted as input.
    Invalidated,
    /// The section never ran (skipped, no privilege, no collector, missing input).
    Untested,
    /// The section is deliberately unavailable in this build/context (feature-gated).
    Locked,
}

public static class EvidenceUsabilityExtensions
{
    /// Can this section's output be used as positive scoring evidence at all?
    /// `Invalidated`, `Untested`, `Locked` are NOT usable — they hard-gate any diagnosis that
    /// requires the section. `Passed`/`Failed`/`Inconclusive` are usable (with caveats).
    public static bool IsUsable(this EvidenceUsability u) => u switch
    {
        EvidenceUsability.Passed or EvidenceUsability.Failed or EvidenceUsability.Inconclusive => true,
        EvidenceUsability.Invalidated or EvidenceUsability.Untested or EvidenceUsability.Locked => false,
        _ => throw new ArgumentOutOfRangeException(nameof(u)),
    };

    /// The exact Swift rawValue string (used in JSON and in gate-detail strings — parity-relevant).
    public static string RawValue(this EvidenceUsability u) => u switch
    {
        EvidenceUsability.Passed => "passed",
        EvidenceUsability.Failed => "failed",
        EvidenceUsability.Inconclusive => "inconclusive",
        EvidenceUsability.Invalidated => "invalidated",
        EvidenceUsability.Untested => "untested",
        EvidenceUsability.Locked => "locked",
        _ => throw new ArgumentOutOfRangeException(nameof(u)),
    };

    /// The documented, explicit mapping from a real Syxx-run section status (`SectionStatus` in
    /// `Conclusion.cs`) onto the engine's usability axis — the twin of Swift's
    /// `EvidenceUsability.init(sectionStatus:)`. This is a real design decision, spelled out here
    /// so it is auditable rather than implicit:
    ///
    ///  * `Untested`     -> `Untested`     — never ran ⇒ no usable evidence (hard-gates requirers).
    ///  * `Invalidated`  -> `Invalidated`  — ran but premise failed ⇒ not trustworthy input.
    ///  * `Inconclusive` -> `Inconclusive` — ran, no resolution ⇒ usable only as abstention.
    ///  * `Degraded`     -> `Passed`       — ran with partial (noncritical) data ⇒ usable input;
    ///                                       the per-indicator `Quality`/`ObservedStrength` carries
    ///                                       the attenuation, so the section itself is not gated.
    ///  * `Positive`     -> `Passed`       — ran and found something ⇒ usable input.
    ///  * `Negative`     -> `Failed`       — ran and found nothing ⇒ usable input (a real negative).
    public static EvidenceUsability FromSectionStatus(SectionStatus status) => status switch
    {
        SectionStatus.Untested => EvidenceUsability.Untested,
        SectionStatus.Invalidated => EvidenceUsability.Invalidated,
        SectionStatus.Inconclusive => EvidenceUsability.Inconclusive,
        SectionStatus.Degraded => EvidenceUsability.Passed,
        SectionStatus.Positive => EvidenceUsability.Passed,
        SectionStatus.Negative => EvidenceUsability.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

public sealed class EvidenceUsabilityJsonConverter : JsonConverter<EvidenceUsability>
{
    public override EvidenceUsability Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "passed" => EvidenceUsability.Passed,
            "failed" => EvidenceUsability.Failed,
            "inconclusive" => EvidenceUsability.Inconclusive,
            "invalidated" => EvidenceUsability.Invalidated,
            "untested" => EvidenceUsability.Untested,
            "locked" => EvidenceUsability.Locked,
            var s => throw new JsonException($"unknown EvidenceUsability '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, EvidenceUsability value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

// MARK: Rule vocabulary

[JsonConverter(typeof(IndicatorPolarityJsonConverter))]
public enum IndicatorPolarity
{
    /// Observing this indicator (at strength) is evidence FOR the diagnosis.
    Supports,
    /// Observing this indicator is evidence AGAINST the diagnosis (contradiction / exclusion).
    Excludes,
}

public static class IndicatorPolarityExtensions
{
    public static string RawValue(this IndicatorPolarity p) => p switch
    {
        IndicatorPolarity.Supports => "supports",
        IndicatorPolarity.Excludes => "excludes",
        _ => throw new ArgumentOutOfRangeException(nameof(p)),
    };
}

public sealed class IndicatorPolarityJsonConverter : JsonConverter<IndicatorPolarity>
{
    public override IndicatorPolarity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "supports" => IndicatorPolarity.Supports,
            "excludes" => IndicatorPolarity.Excludes,
            var s => throw new JsonException($"unknown IndicatorPolarity '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, IndicatorPolarity value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

/// Evidential weight class of an indicator within a diagnosis. Used by the
/// `RequireDecisiveOrTwoMajorsForConclusive` gate: a conclusive result must rest on at least one
/// decisive observation or two independent majors, never on minors alone.
/// (Swift declares Comparable minor &lt; major &lt; decisive; the C# member order carries the same
/// ordinal, and the engine only ever compares for equality, exactly as the Swift engine does.)
[JsonConverter(typeof(EvidenceClassJsonConverter))]
public enum EvidenceClass
{
    Minor = 0,
    Major = 1,
    Decisive = 2,
}

public static class EvidenceClassExtensions
{
    public static string RawValue(this EvidenceClass c) => c switch
    {
        EvidenceClass.Minor => "minor",
        EvidenceClass.Major => "major",
        EvidenceClass.Decisive => "decisive",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };
}

public sealed class EvidenceClassJsonConverter : JsonConverter<EvidenceClass>
{
    public override EvidenceClass Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "minor" => EvidenceClass.Minor,
            "major" => EvidenceClass.Major,
            "decisive" => EvidenceClass.Decisive,
            var s => throw new JsonException($"unknown EvidenceClass '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, EvidenceClass value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

/// One rule inside a `DiagnosisSpec`: "for diagnosis D, indicator I with this polarity, at roughly
/// this expected strength, matters this much, is this class of evidence, belongs to this
/// independence group, and its partial-strength contribution decays with this exponent."
public sealed record DiagnosisIndicatorRule(
    [property: JsonPropertyName("indicatorID")] IndicatorId IndicatorId,
    [property: JsonPropertyName("polarity")] IndicatorPolarity Polarity,
    /// The strength at which this indicator is considered fully present (0...1).
    [property: JsonPropertyName("expectedStrength")] double ExpectedStrength,
    /// Relative importance of this rule within the diagnosis. May exceed 1 (relative, not a mass).
    [property: JsonPropertyName("weight")] double Weight,
    [property: JsonPropertyName("evidenceClass")] EvidenceClass EvidenceClass,
    /// Rules sharing an independence group provide correlated (not independent) support; the engine
    /// counts distinct groups toward `MinimumIndependentSupportGroups...`.
    [property: JsonPropertyName("independenceGroup")] string IndependenceGroup,
    /// How sharply a partial observed strength is discounted: `alignment^decayExponent`.
    /// &gt;1 penalises weak-but-present signals harder (used on decisive rules). Default 1.0 (linear).
    [property: JsonPropertyName("decayExponent")] double DecayExponent = 1.0);

/// Thresholds that gate whether a diagnosis is emitted, and at what confidence tier.
public sealed record ThresholdProfile(
    [property: JsonPropertyName("minimumCoverageForProbable")] double MinimumCoverageForProbable,
    [property: JsonPropertyName("minimumCoverageForConclusive")] double MinimumCoverageForConclusive,
    [property: JsonPropertyName("minimumFitForProbable")] double MinimumFitForProbable,
    [property: JsonPropertyName("minimumFitForConclusive")] double MinimumFitForConclusive,
    [property: JsonPropertyName("minimumMarginForProbable")] double MinimumMarginForProbable,
    [property: JsonPropertyName("minimumMarginForConclusive")] double MinimumMarginForConclusive,
    [property: JsonPropertyName("maxContradictionForConclusive")] double MaxContradictionForConclusive,
    [property: JsonPropertyName("maxContradictionForProbable")] double MaxContradictionForProbable,
    [property: JsonPropertyName("minimumIndependentSupportGroupsForProbable")] int MinimumIndependentSupportGroupsForProbable,
    [property: JsonPropertyName("minimumIndependentSupportGroupsForConclusive")] int MinimumIndependentSupportGroupsForConclusive,
    /// A conclusive result must rest on a decisive observation or ≥2 major observations.
    [property: JsonPropertyName("requireDecisiveOrTwoMajorsForConclusive")] bool RequireDecisiveOrTwoMajorsForConclusive,
    /// If a required section is entirely missing from the bundle, gate the diagnosis (fail-closed).
    [property: JsonPropertyName("hardGateOnMissingRequiredSections")] bool HardGateOnMissingRequiredSections,
    /// If a required section is present but `Invalidated`, gate the diagnosis (fail-closed).
    [property: JsonPropertyName("failClosedOnInvalidatedRequiredSections")] bool FailClosedOnInvalidatedRequiredSections);

/// The full specification of one diagnosis: its rules, the sections it depends on, the indicators
/// whose firing hard-excludes it, and its relationships to other diagnoses.
public sealed record DiagnosisSpec
{
    [JsonPropertyName("id")] public DiagnosisId Id { get; init; }
    [JsonPropertyName("displayName")] public string DisplayName { get; init; }
    [JsonPropertyName("rules")] public IReadOnlyList<DiagnosisIndicatorRule> Rules { get; init; }
    /// Sections that MUST have produced usable output for this diagnosis to be emittable.
    [JsonPropertyName("requiredSections")] public IReadOnlyList<SectionId> RequiredSections { get; init; }
    /// Indicators whose confirmed firing makes this diagnosis impossible (hard exclusion).
    [JsonPropertyName("exclusionaryIndicators")] public IReadOnlyList<IndicatorId> ExclusionaryIndicators { get; init; }
    /// Diagnoses that cannot co-hold with this one; the stronger of a conflicting pair wins.
    [JsonPropertyName("mutuallyExclusiveWith")] public IReadOnlyList<DiagnosisId> MutuallyExclusiveWith { get; init; }
    /// Diagnoses that MAY co-hold with this one; they are excluded from this diagnosis's margin
    /// competition (a co-occurring pattern is not a rival hypothesis).
    [JsonPropertyName("allowedCooccurrenceWith")] public IReadOnlyList<DiagnosisId> AllowedCooccurrenceWith { get; init; }

    [JsonConstructor]
    public DiagnosisSpec(DiagnosisId id,
                         string displayName,
                         IReadOnlyList<DiagnosisIndicatorRule> rules,
                         IReadOnlyList<SectionId>? requiredSections = null,
                         IReadOnlyList<IndicatorId>? exclusionaryIndicators = null,
                         IReadOnlyList<DiagnosisId>? mutuallyExclusiveWith = null,
                         IReadOnlyList<DiagnosisId>? allowedCooccurrenceWith = null)
    {
        Id = id;
        DisplayName = displayName;
        Rules = rules;
        RequiredSections = requiredSections ?? Array.Empty<SectionId>();
        ExclusionaryIndicators = exclusionaryIndicators ?? Array.Empty<IndicatorId>();
        MutuallyExclusiveWith = mutuallyExclusiveWith ?? Array.Empty<DiagnosisId>();
        AllowedCooccurrenceWith = allowedCooccurrenceWith ?? Array.Empty<DiagnosisId>();
    }
}

/// The complete matrix: every diagnosis and the shared threshold profile. Round-trips as JSON.
public sealed record MatrixSpec
{
    [JsonPropertyName("diagnoses")] public IReadOnlyList<DiagnosisSpec> Diagnoses { get; init; }
    [JsonPropertyName("thresholds")] public ThresholdProfile Thresholds { get; init; }
    /// The universe of section identifiers a `RequiredSections` list may reference. Defaults to the
    /// six canonical Syxx sections; validated in `Validate()`.
    /// NOTE (parity, observed while porting): Swift encodes `Set&lt;SectionID&gt;` as an array in
    /// per-process hash order — the Swift-side `defaultSpecJSON` is therefore NOT byte-stable for
    /// this one field across launches. Flagged for the Phase 5 canonical-encoding rule (sort the
    /// set on BOTH platforms in the frozen oracle); everything else in the spec is order-stable.
    [JsonPropertyName("knownSections")] public IReadOnlySet<SectionId> KnownSections { get; init; }

    public static IReadOnlySet<SectionId> DefaultKnownSections { get; } = new HashSet<SectionId>
    {
        new("prophet"), new("mirror"), new("portal"),
        new("glutton"), new("cage"), new("witness"),
    };

    [JsonConstructor]
    public MatrixSpec(IReadOnlyList<DiagnosisSpec> diagnoses,
                      ThresholdProfile thresholds,
                      IReadOnlySet<SectionId>? knownSections = null)
    {
        Diagnoses = diagnoses;
        Thresholds = thresholds;
        KnownSections = knownSections ?? DefaultKnownSections;
    }

    /// Structural invariants a well-formed spec must satisfy. Thrown at engine construction so a
    /// malformed matrix fails loudly at load time rather than silently mis-scoring at run time.
    /// Check ORDER mirrors the Swift `validate()` exactly, so the first error thrown on a given
    /// malformed spec is the same on both platforms.
    public void Validate()
    {
        if (Diagnoses.Count == 0) { throw MatrixEngineException.EmptySpec(); }

        var seenDiagnosisIds = new HashSet<DiagnosisId>();
        var allDiagnosisIds = new HashSet<DiagnosisId>(Diagnoses.Select(d => d.Id));

        foreach (var d in Diagnoses)
        {
            // Duplicate diagnosis id.
            if (!seenDiagnosisIds.Add(d.Id)) { throw MatrixEngineException.DuplicateDiagnosis(d.Id); }
            if (d.Rules.Count == 0) { throw MatrixEngineException.DiagnosisHasNoRules(d.Id); }

            // RequiredSections must reference known sections only.
            foreach (var s in d.RequiredSections)
            {
                if (!KnownSections.Contains(s)) { throw MatrixEngineException.UnknownRequiredSection(d.Id, s); }
            }

            // An indicator cannot both SUPPORT and be EXCLUSIONARY for the same diagnosis: that is a
            // contradictory rule (it would be evidence for and hard-proof-against simultaneously).
            var supportingIds = new HashSet<IndicatorId>(
                d.Rules.Where(r => r.Polarity == IndicatorPolarity.Supports).Select(r => r.IndicatorId));
            foreach (var ex in d.ExclusionaryIndicators)
            {
                if (supportingIds.Contains(ex)) { throw MatrixEngineException.ContradictoryIndicatorRole(d.Id, ex); }
            }

            // Duplicate rule for the same indicator+polarity within a diagnosis.
            var seenRuleKeys = new HashSet<string>();
            foreach (var r in d.Rules)
            {
                var key = $"{r.IndicatorId.Value}|{r.Polarity.RawValue()}";
                if (!seenRuleKeys.Add(key)) { throw MatrixEngineException.DuplicateRule(d.Id, r.IndicatorId); }
                if (!(r.ExpectedStrength >= 0) || !(r.ExpectedStrength <= 1))
                {
                    throw MatrixEngineException.StrengthOutOfRange(d.Id, r.IndicatorId);
                }
                if (!(r.Weight >= 0) || !(r.DecayExponent > 0))
                {
                    throw MatrixEngineException.InvalidRuleParameter(d.Id, r.IndicatorId);
                }
            }

            // Cross-diagnosis references must resolve.
            foreach (var other in d.MutuallyExclusiveWith)
            {
                if (!allDiagnosisIds.Contains(other)) { throw MatrixEngineException.DanglingReference(d.Id, other); }
            }
            foreach (var other in d.AllowedCooccurrenceWith)
            {
                if (!allDiagnosisIds.Contains(other)) { throw MatrixEngineException.DanglingReference(d.Id, other); }
            }
        }
    }
}

// MARK: Evidence inputs

/// The terminal usability of one section in a single run.
public sealed record SectionResult(
    [property: JsonPropertyName("id")] SectionId Id,
    [property: JsonPropertyName("status")] EvidenceUsability Status,
    /// Whether this section is globally required (informational; per-diagnosis requirement is what
    /// actually gates). Carried through for provenance/rendering.
    [property: JsonPropertyName("required")] bool Required = false,
    /// Whether the section's provenance is trusted (e.g. signed witness). Untrusted usable output
    /// is still usable but is surfaced; the gate keys on `Status.IsUsable()`, not on `Trusted`.
    [property: JsonPropertyName("trusted")] bool Trusted = true,
    [property: JsonPropertyName("notes")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null);

/// One observed indicator in a single run. `ObservedStrength == null` means NOT OBSERVED — the
/// engine treats it as absent (it lowers coverage), never as zero-support-that-counts.
public sealed record ObservedIndicator(
    [property: JsonPropertyName("id")] IndicatorId Id,
    [property: JsonPropertyName("section")] SectionId Section,
    /// Normalised strength in 0...1, or null when the indicator was not observed at all.
    [property: JsonPropertyName("observedStrength")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? ObservedStrength,
    /// Data-quality of this observation in 0...1 (completeness / reliability). Attenuates, never
    /// zeroes, support.
    [property: JsonPropertyName("quality")] double Quality = 1.0,
    /// True when this observation actively contradicts the indicator's supporting reading.
    [property: JsonPropertyName("contradictory")] bool Contradictory = false,
    [property: JsonPropertyName("notes")][property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes = null)
{
    /// An indicator is "observed" only when it carries a non-null strength.
    [JsonIgnore] public bool IsObserved => ObservedStrength.HasValue;
}

/// The full evidence for one run: every section's usability and every observed indicator.
public sealed record EvidenceBundle(
    [property: JsonPropertyName("sections")] IReadOnlyList<SectionResult> Sections,
    [property: JsonPropertyName("indicators")] IReadOnlyList<ObservedIndicator> Indicators)
{
    /// Duplicate detection: the same section id or the same indicator id must not appear twice —
    /// that would make lookups order-dependent and could silently double-count evidence.
    public void Validate()
    {
        var seenSections = new HashSet<SectionId>();
        foreach (var s in Sections)
        {
            if (!seenSections.Add(s.Id)) { throw MatrixEngineException.DuplicateSectionInBundle(s.Id); }
        }
        var seenIndicators = new HashSet<IndicatorId>();
        foreach (var i in Indicators)
        {
            if (!seenIndicators.Add(i.Id)) { throw MatrixEngineException.DuplicateIndicatorInBundle(i.Id); }
            if (i.ObservedStrength is double s && !(double.IsFinite(s) && s >= 0 && s <= 1))
            {
                throw MatrixEngineException.IndicatorStrengthOutOfRange(i.Id);
            }
            if (!double.IsFinite(i.Quality) || !(i.Quality >= 0) || !(i.Quality <= 1))
            {
                throw MatrixEngineException.IndicatorQualityOutOfRange(i.Id);
            }
        }
    }
}

// MARK: Results

/// The confidence tier at which a diagnosis is emitted. There is no `inconclusive` tier here by
/// design: an inconclusive diagnosis is simply NOT emitted (it does not appear in the ranked list).
[JsonConverter(typeof(DiagnosisConfidenceJsonConverter))]
public enum DiagnosisConfidence
{
    Probable = 0,
    Conclusive = 1,
}

public static class DiagnosisConfidenceExtensions
{
    public static string RawValue(this DiagnosisConfidence c) => c switch
    {
        DiagnosisConfidence.Probable => "probable",
        DiagnosisConfidence.Conclusive => "conclusive",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };
}

public sealed class DiagnosisConfidenceJsonConverter : JsonConverter<DiagnosisConfidence>
{
    public override DiagnosisConfidence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "probable" => DiagnosisConfidence.Probable,
            "conclusive" => DiagnosisConfidence.Conclusive,
            var s => throw new JsonException($"unknown DiagnosisConfidence '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, DiagnosisConfidence value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

/// The overall run-level result. This is the ONLY place abstention lives — `Inconclusive` carries
/// an empty ranked-diagnoses list.
[JsonConverter(typeof(DeterminativeStateJsonConverter))]
public enum DeterminativeState
{
    /// Positive evidence that the environment is clean, with no unverifiable threat overhang.
    Clean,
    /// At least one threat diagnosis reached the probable+ tier.
    ThreatDetected,
    /// The evidence does not resolve to clean or to any threat — honest abstention.
    Inconclusive,
    /// No sections have been evaluated yet — the scan has not run or every section is untested.
    Untested,
}

public static class DeterminativeStateExtensions
{
    public static string RawValue(this DeterminativeState v) => v switch
    {
        DeterminativeState.Clean => "clean",
        DeterminativeState.ThreatDetected => "threatDetected",
        DeterminativeState.Inconclusive => "inconclusive",
        DeterminativeState.Untested => "untested",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };
}

public sealed class DeterminativeStateJsonConverter : JsonConverter<DeterminativeState>
{
    public override DeterminativeState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "clean" => DeterminativeState.Clean,
            "threatDetected" => DeterminativeState.ThreatDetected,
            "inconclusive" => DeterminativeState.Inconclusive,
            "untested" => DeterminativeState.Untested,
            var s => throw new JsonException($"unknown DeterminativeState '{s}'"),
        };
    public override void Write(Utf8JsonWriter writer, DeterminativeState value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

/// A single emitted diagnosis with the metrics that justify it. Only ever constructed for a
/// diagnosis that actually reached `Probable`/`Conclusive`.
public sealed record DiagnosisScore(
    [property: JsonPropertyName("id")] DiagnosisId Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("confidence")] DiagnosisConfidence Confidence,
    /// fit·coverage — the single comparable evidential strength used for ranking and margin.
    [property: JsonPropertyName("strength")] double Strength,
    [property: JsonPropertyName("fit")] double Fit,
    [property: JsonPropertyName("coverage")] double Coverage,
    [property: JsonPropertyName("margin")] double Margin,
    [property: JsonPropertyName("contradiction")] double Contradiction,
    [property: JsonPropertyName("independentSupportGroups")] int IndependentSupportGroups,
    /// Indicator ids that actually contributed supporting evidence (observed, non-contradictory).
    [property: JsonPropertyName("supportingIndicators")] IReadOnlyList<IndicatorId> SupportingIndicators,
    [property: JsonPropertyName("explanation")] string Explanation);

/// One diagnosis that was suppressed (gated / excluded / below tier) despite carrying real observed
/// support — surfaced so the honesty-critical "we saw a threat signal but could not verify it" case
/// is never hidden.
public sealed record SuppressedDiagnosis(
    [property: JsonPropertyName("id")] DiagnosisId Id,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("reason")] SuppressedDiagnosis.SuppressionReason Reason,
    [property: JsonPropertyName("rawStrength")] double RawStrength,
    /// C065: the quantity the overhang ADMISSION gate measured — the un-attenuated observed
    /// support (Σ weight·alignment^decay of non-contradictory supporting observations, WITHOUT
    /// quality attenuation). `RawStrength` (fit·coverage) is crushed by the very gate that
    /// suppressed the diagnosis, so it must never be re-used to decide clean-blocking; this field
    /// carries the honest gate-side measure instead. Parity-relevant Codable addition — carries
    /// the same `rawSupport` key as the Swift engine.
    [property: JsonPropertyName("rawSupport")] double RawSupport,
    [property: JsonPropertyName("detail")] string Detail)
{
    [JsonConverter(typeof(SuppressionReasonJsonConverter))]
    public enum SuppressionReason
    {
        MissingRequiredSection,
        InvalidatedRequiredSection,
        HardExcluded,
        /// C062: the diagnosis fired with real observed support but fell below the `Probable`
        /// tier and was not hard-gated. It must never silently vanish — it joins the honesty
        /// overhang ("seen but unconfirmed", never a detection) and forbids a bare `Clean`.
        BelowTier,
    }

    public sealed class SuppressionReasonJsonConverter : JsonConverter<SuppressionReason>
    {
        public override SuppressionReason Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString() switch
            {
                "missingRequiredSection" => SuppressionReason.MissingRequiredSection,
                "invalidatedRequiredSection" => SuppressionReason.InvalidatedRequiredSection,
                "hardExcluded" => SuppressionReason.HardExcluded,
                "belowTier" => SuppressionReason.BelowTier,
                var s => throw new JsonException($"unknown SuppressedDiagnosis.Reason '{s}'"),
            };
        public override void Write(Utf8JsonWriter writer, SuppressionReason value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                SuppressionReason.MissingRequiredSection => "missingRequiredSection",
                SuppressionReason.InvalidatedRequiredSection => "invalidatedRequiredSection",
                SuppressionReason.HardExcluded => "hardExcluded",
                SuppressionReason.BelowTier => "belowTier",
                _ => throw new ArgumentOutOfRangeException(nameof(value)),
            });
    }
}

/// The engine's output for one run.
public sealed record DeterminativeResult(
    [property: JsonPropertyName("state")] DeterminativeState State,
    /// Emitted diagnoses, ranked by strength (descending). NEVER contains an abstention state, and
    /// NEVER contains `clean` when `State == ThreatDetected`.
    [property: JsonPropertyName("diagnoses")] IReadOnlyList<DiagnosisScore> Diagnoses,
    /// Diagnoses that carried real raw support but were gated/excluded — the honesty overhang.
    [property: JsonPropertyName("suppressed")] IReadOnlyList<SuppressedDiagnosis> Suppressed,
    /// A plain-language account of why the engine reached this state.
    [property: JsonPropertyName("explanation")] string Explanation);

// MARK: Errors

/// The 13-case discriminant of `MatrixEngineException` — the exact mirror of Swift's
/// `MatrixEngineError` cases.
public enum MatrixEngineErrorKind
{
    EmptySpec,
    DuplicateDiagnosis,
    DiagnosisHasNoRules,
    UnknownRequiredSection,
    ContradictoryIndicatorRole,
    DuplicateRule,
    StrengthOutOfRange,
    InvalidRuleParameter,
    DanglingReference,
    DuplicateSectionInBundle,
    DuplicateIndicatorInBundle,
    IndicatorStrengthOutOfRange,
    IndicatorQualityOutOfRange,
}

/// The C# carrier of Swift's `MatrixEngineError`: same 13 cases (`Kind`), byte-identical message
/// strings (they can reach logs on both platforms; a diverging message is a parity defect).
public sealed class MatrixEngineException : Exception
{
    public MatrixEngineErrorKind Kind { get; }

    private MatrixEngineException(MatrixEngineErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }

    public static MatrixEngineException EmptySpec()
        => new(MatrixEngineErrorKind.EmptySpec, "MatrixSpec has no diagnoses");
    public static MatrixEngineException DuplicateDiagnosis(DiagnosisId d)
        => new(MatrixEngineErrorKind.DuplicateDiagnosis, $"duplicate diagnosis id: {d.Value}");
    public static MatrixEngineException DiagnosisHasNoRules(DiagnosisId d)
        => new(MatrixEngineErrorKind.DiagnosisHasNoRules, $"diagnosis has no rules: {d.Value}");
    public static MatrixEngineException UnknownRequiredSection(DiagnosisId d, SectionId s)
        => new(MatrixEngineErrorKind.UnknownRequiredSection, $"diagnosis {d.Value} requires unknown section {s.Value}");
    public static MatrixEngineException ContradictoryIndicatorRole(DiagnosisId d, IndicatorId i)
        => new(MatrixEngineErrorKind.ContradictoryIndicatorRole, $"indicator {i.Value} is both supporting and exclusionary in {d.Value}");
    public static MatrixEngineException DuplicateRule(DiagnosisId d, IndicatorId i)
        => new(MatrixEngineErrorKind.DuplicateRule, $"duplicate rule for indicator {i.Value} in {d.Value}");
    public static MatrixEngineException StrengthOutOfRange(DiagnosisId d, IndicatorId i)
        => new(MatrixEngineErrorKind.StrengthOutOfRange, $"expectedStrength out of [0,1] for {i.Value} in {d.Value}");
    public static MatrixEngineException InvalidRuleParameter(DiagnosisId d, IndicatorId i)
        => new(MatrixEngineErrorKind.InvalidRuleParameter, $"invalid weight/decayExponent for {i.Value} in {d.Value}");
    public static MatrixEngineException DanglingReference(DiagnosisId d, DiagnosisId r)
        => new(MatrixEngineErrorKind.DanglingReference, $"diagnosis {d.Value} references unknown diagnosis {r.Value}");
    public static MatrixEngineException DuplicateSectionInBundle(SectionId s)
        => new(MatrixEngineErrorKind.DuplicateSectionInBundle, $"duplicate section in bundle: {s.Value}");
    public static MatrixEngineException DuplicateIndicatorInBundle(IndicatorId i)
        => new(MatrixEngineErrorKind.DuplicateIndicatorInBundle, $"duplicate indicator in bundle: {i.Value}");
    public static MatrixEngineException IndicatorStrengthOutOfRange(IndicatorId i)
        => new(MatrixEngineErrorKind.IndicatorStrengthOutOfRange, $"observedStrength out of [0,1] for {i.Value}");
    public static MatrixEngineException IndicatorQualityOutOfRange(IndicatorId i)
        => new(MatrixEngineErrorKind.IndicatorQualityOutOfRange, $"quality out of [0,1] for {i.Value}");
}

// MARK: - Engine

public sealed class MatrixDeterminativeEngine
{
    public MatrixSpec Spec { get; }

    /// The exclusionary strength at or above which an exclusionary/`Excludes` observation is
    /// treated as "firing" (hard exclusion / strong contradiction).
    private const double ExclusionFiringThreshold = 0.5;
    /// Raw-support floor above which a gated / below-tier threat diagnosis is considered an
    /// "honesty overhang" that must forbid a `Clean` result and force `Inconclusive`. Compared
    /// against `RawScore.ObservedSupport` (quality-independent) at BOTH the admission gate and the
    /// clean-blocking re-check in `Assemble` — the two must measure the same quantity (C065).
    private const double OverhangFloor = 0.30;
    /// The one diagnosis id with clean semantics (never a threat; excluded from the overhang).
    private static readonly DiagnosisId CleanId = new("clean");

    public MatrixDeterminativeEngine(MatrixSpec spec)
    {
        spec.Validate();
        Spec = spec;
    }

    // MARK: Public evaluate

    /// Cached cancellation result returned whenever a cancellation request is observed.
    private static readonly DeterminativeResult CancelledResult = new(
        DeterminativeState.Inconclusive,
        Array.Empty<DiagnosisScore>(),
        Array.Empty<SuppressedDiagnosis>(),
        "The synthesis was cancelled before it could complete.");

    public DeterminativeResult Evaluate(EvidenceBundle bundle, CancellationToken ct = default)
    {
        bundle.Validate();

        if (ct.IsCancellationRequested) { return CancelledResult; }

        // Untested: every section is still untested AND no indicators have been observed.
        // Distinct from Inconclusive (data collected but ambiguous). Indicators can be
        // present even when sections are untested (e.g. a probe fired but the section
        // didn't complete), and those represent real data that must be analyzed.
        var allUntested = bundle.Sections.All(s => s.Status == EvidenceUsability.Untested);
        var noIndicators = bundle.Indicators.Count == 0;
        if (allUntested && noIndicators)
        {
            return new DeterminativeResult(DeterminativeState.Untested,
                                           new List<DiagnosisScore>(),
                                           new List<SuppressedDiagnosis>(),
                                           "No sections have been evaluated yet.");
        }

        // Validate() rejected duplicates, so ToDictionary cannot collide here.
        var sectionById = bundle.Sections.ToDictionary(s => s.Id);
        var indicatorById = bundle.Indicators.ToDictionary(i => i.Id);

        // Pass 1: per-diagnosis raw metrics (pre-margin, pre-mutual-exclusion).
        var raws = new Dictionary<DiagnosisId, RawScore>();
        foreach (var d in Spec.Diagnoses)
        {
            if (ct.IsCancellationRequested) { return CancelledResult; }
            raws[d.Id] = ComputeRawScore(d, indicatorById, sectionById);
        }

        // Pass 2: margins (need cross-diagnosis strengths). Competitors exclude self and any
        // explicitly-allowed co-occurrence.
        double CompetitorBestStrength(DiagnosisSpec d)
        {
            var allowed = new HashSet<DiagnosisId>(d.AllowedCooccurrenceWith);
            var best = 0.0;
            foreach (var other in Spec.Diagnoses)
            {
                if (other.Id == d.Id || allowed.Contains(other.Id)) { continue; }
                best = Math.Max(best, raws.TryGetValue(other.Id, out var r) ? r.Strength : 0);
            }
            return best;
        }

        // Pass 3: classify each diagnosis into a tier (or not emitted) honouring gates.
        var emitted = new Dictionary<DiagnosisId, (DiagnosisScore Score, DiagnosisSpec Spec)>();
        var suppressed = new List<SuppressedDiagnosis>();
        foreach (var d in Spec.Diagnoses)
        {
            if (ct.IsCancellationRequested) { return CancelledResult; }
            if (!raws.TryGetValue(d.Id, out var raw)) { continue; }
            var margin = MatrixRounding.Round6(raw.Strength - CompetitorBestStrength(d));

            // Admission to `suppressed` gates on `ObservedSupport` (quality-INDEPENDENT): the
            // question is "did real observations fire," and a thin section's low completeness
            // must attenuate scoring, never hide a seen signal (C062/C065).
            //
            // PRECEDENCE (C065, fail-closed): the required-section gate is checked BEFORE hard
            // exclusion. A diagnosis whose required evidential context is missing/invalidated is
            // UNEVALUABLE — it can be neither affirmed nor affirmatively disproved, so a firing
            // exclusionary indicator must not convert it into a clean-permitting `hardExcluded`.
            // Gated-with-support therefore always reaches the clean-blocking overhang; exclusion
            // dominance applies only when the diagnosis's required evidence was actually usable.
            if (raw.Gate is { } gate)
            {
                if (raw.ObservedSupport >= OverhangFloor)
                {
                    suppressed.Add(new SuppressedDiagnosis(d.Id, d.DisplayName,
                        gate.Reason, MatrixRounding.Round6(raw.Strength),
                        MatrixRounding.Round6(raw.ObservedSupport),
                        gate.Detail));
                }
                continue;
            }
            // Hard exclusion dominates everything evaluable.
            if (raw.HardExclusion is { } ex)
            {
                if (raw.ObservedSupport >= OverhangFloor)
                {
                    suppressed.Add(new SuppressedDiagnosis(d.Id, d.DisplayName,
                        SuppressedDiagnosis.SuppressionReason.HardExcluded, MatrixRounding.Round6(raw.Strength),
                        MatrixRounding.Round6(raw.ObservedSupport),
                        $"Excluded by a confirmed {ex.Value} observation."));
                }
                continue;
            }

            if (Tier(raw, margin) is not { } tier)
            {
                // C062: a non-gated THREAT that fired with real observed support but fell below
                // the probable tier must never silently vanish — it joins the honesty overhang as
                // "seen but unconfirmed" (never a detection) and forbids a bare `Clean`. The
                // clean diagnosis itself falling below tier stays OUT of the overhang: it is not a
                // threat signal (Assemble's "clean present but weak" path owns that case).
                // COPY (detail string): shipped on macOS under the same PENDING-OWNER-APPROVAL
                // marker (outputs-catalog.csv); reused verbatim for parity.
                if (d.Id != CleanId && raw.ObservedSupport >= OverhangFloor)
                {
                    suppressed.Add(new SuppressedDiagnosis(d.Id, d.DisplayName,
                        SuppressedDiagnosis.SuppressionReason.BelowTier, MatrixRounding.Round6(raw.Strength),
                        MatrixRounding.Round6(raw.ObservedSupport),
                        "Observed with real support but below the reporting threshold; could not be confirmed or ruled out."));
                }
                continue;
            }
            var score = new DiagnosisScore(
                d.Id, d.DisplayName, tier,
                MatrixRounding.Round6(raw.Strength), MatrixRounding.Round6(raw.Fit), MatrixRounding.Round6(raw.Coverage),
                margin, MatrixRounding.Round6(raw.Contradiction),
                raw.IndependentGroups,
                raw.SupportingIndicators,
                ExplanationFor(d, raw, tier, margin));
            emitted[d.Id] = (score, d);
        }

        if (ct.IsCancellationRequested) { return CancelledResult; }

        // Pass 4: resolve mutual exclusion among emitted diagnoses — the stronger wins.
        emitted = ResolveMutualExclusion(emitted);

        // Pass 5: assemble the overall result.
        return Assemble(emitted, suppressed);
    }

    // MARK: Raw scoring

    private readonly record struct GateInfo(SuppressedDiagnosis.SuppressionReason Reason, string Detail);

    private sealed class RawScore
    {
        public double Fit;
        public double Coverage;
        public double Contradiction;
        public double Strength;        // fit * coverage
        public double RawSupport;      // Σ observed supporting weight*effective (pre-normalisation)
        /// Σ weight·alignment^decay for non-contradictory supporting observations WITHOUT quality
        /// attenuation. The overhang-ADMISSION measure (C062/C065): it answers "did real
        /// observations fire," independent of how thin the producing section ran. Quality (section
        /// completeness) attenuates evidential weight (`Fit`/`Strength`), but a thin section must
        /// never HIDE a seen threat signal — that would be missing data silently reading cleaner.
        public double ObservedSupport;
        public int IndependentGroups;
        public bool HasDecisive;
        public int MajorCount;
        public List<IndicatorId> SupportingIndicators = new();
        public IndicatorId? HardExclusion;
        public GateInfo? Gate;
    }

    private RawScore ComputeRawScore(DiagnosisSpec d,
                                     IReadOnlyDictionary<IndicatorId, ObservedIndicator> indicatorById,
                                     IReadOnlyDictionary<SectionId, SectionResult> sectionById)
    {
        var r = new RawScore();

        var supportRules = d.Rules.Where(x => x.Polarity == IndicatorPolarity.Supports).ToList();
        var excludeRules = d.Rules.Where(x => x.Polarity == IndicatorPolarity.Excludes).ToList();

        var totalSupportWeight = supportRules.Sum(x => x.Weight);
        var observedSupportWeight = 0.0;
        var alignedSupport = 0.0;          // Σ weight*effective for non-contradictory support
        var contradictionMass = 0.0;
        var groups = new HashSet<string>();

        foreach (var rule in supportRules)
        {
            if (!indicatorById.TryGetValue(rule.IndicatorId, out var obs) || obs.ObservedStrength is not { } s) { continue; }
            var alignment = rule.ExpectedStrength > 0 ? Math.Min(1.0, s / rule.ExpectedStrength) : s;
            var aligned = Math.Pow(alignment, rule.DecayExponent);

            // B162 (Swift twin :782-785): an indicator whose PRODUCING section produced no usable
            // output must not SCORE. The required-section gate below only inspects the sections
            // THIS diagnosis lists, so an observation originating in some other Untested/
            // Invalidated section would otherwise count at full face value — inflating the
            // coverage numerator, deflating `Contradiction` (unusable weight padding its
            // denominator), and, worst, reaching `HasDecisive`/`MajorCount`, where evidence from a
            // section that produced nothing usable could clear the decisive-or-two-majors gate and
            // promote a diagnosis to `Conclusive`. An indicator whose section is ABSENT from the
            // bundle is NOT skipped — there is no status to consult — matching the Swift guard.
            if (sectionById.TryGetValue(obs.Section, out var producing) && !producing.Status.IsUsable())
            {
                // C062/C065 — SHARED WITH THE SWIFT TWIN (MatrixDeterminativeEngine.swift:804,
                // landed by 9a7efa68; the two runtimes agree here). Skipping the whole rule would
                // ALSO delete the observation from the overhang-ADMISSION measure, which is
                // quality-independent on purpose: it answers "did a real observation FIRE," so
                // that a thin or incomplete section can attenuate evidential weight but can never
                // HIDE a seen threat signal. Dropping it reopens exactly the false-clean hole the
                // overhang exists to close — a strong threat indicator behind untested required
                // sections would vanish from `Suppressed` instead of forbidding a bare `Clean`
                // (pinned by MatrixUntestedStateTests
                // .UntestedSectionsWithObservedIndicators_IsNotUntested, whose Swift twin
                // testUntestedSectionsWithObservedIndicators_isNotUntested asserts the same thing).
                // So: no scoring contribution (B162 satisfied — it cannot inflate coverage, fit,
                // contradiction, independence groups or the tier gate), but the firing is still
                // admitted to the overhang.
                //
                // Fail-closed for NON-CONTRADICTORY observations only, and the qualifier is not
                // pedantry. For a CONTRADICTORY one the skip removes the same term from BOTH the
                // numerator and the denominator of
                // `Contradiction = contradictionMass / (contradictionMass + observedSupportWeight)`,
                // which generally LOWERS measured contradiction and so makes tiering easier: with
                // c=0.3, w=2.6 and a ghost section contributing dc=dw=0.2, the old value 0.1515
                // becomes 0.1034. That is accepted — it matches the Swift twin, and a section that
                // produced nothing usable should not be able to manufacture contradiction mass
                // against a diagnosis either.
                if (!obs.Contradictory) { r.ObservedSupport += rule.Weight * aligned; }
                continue;
            }

            observedSupportWeight += rule.Weight;
            var effective = aligned * Clamp01(obs.Quality);
            var contribution = rule.Weight * effective;
            if (obs.Contradictory)
            {
                // A supporting indicator observed to contradict → attenuates fit, adds contradiction.
                contradictionMass += contribution;
            }
            else
            {
                alignedSupport += contribution;
                // Quality-independent: the observation FIRED; thin-section attenuation must not
                // erase it from the overhang-admission measure (C062/C065).
                r.ObservedSupport += rule.Weight * aligned;
                if (effective > 0)
                {
                    groups.Add(rule.IndependenceGroup);
                    r.SupportingIndicators.Add(rule.IndicatorId);
                    if (rule.EvidenceClass == EvidenceClass.Decisive) { r.HasDecisive = true; }
                    if (rule.EvidenceClass == EvidenceClass.Major) { r.MajorCount += 1; }
                }
            }
        }

        // Exclude-polarity rules: firing observations add strong contradiction.
        foreach (var rule in excludeRules)
        {
            if (!indicatorById.TryGetValue(rule.IndicatorId, out var obs) || obs.ObservedStrength is not { } s
                || obs.Contradictory) { continue; }
            var alignment = rule.ExpectedStrength > 0 ? Math.Min(1.0, s / rule.ExpectedStrength) : s;
            var effective = Math.Pow(alignment, rule.DecayExponent) * Clamp01(obs.Quality);
            contradictionMass += rule.Weight * effective;
        }

        r.RawSupport = alignedSupport;
        r.Coverage = totalSupportWeight > 0 ? Clamp01(observedSupportWeight / totalSupportWeight) : 0;
        r.Fit = observedSupportWeight > 0 ? Clamp01(alignedSupport / observedSupportWeight) : 0;
        // Contradiction normalised against the support it competes with (bounded, never > 1).
        r.Contradiction = Clamp01(contradictionMass / (contradictionMass + observedSupportWeight + 1e-9));
        r.Strength = r.Fit * r.Coverage;
        r.IndependentGroups = groups.Count;

        // Hard exclusion: a listed exclusionary indicator observed as firing and non-contradictory.
        foreach (var exId in d.ExclusionaryIndicators)
        {
            if (indicatorById.TryGetValue(exId, out var obs) && obs.ObservedStrength is { } s
                && !obs.Contradictory && s >= ExclusionFiringThreshold)
            {
                r.HardExclusion = exId;
                break;
            }
        }

        // Required-section hard gate (fail-closed).
        foreach (var reqSection in d.RequiredSections)
        {
            if (!sectionById.TryGetValue(reqSection, out var section))
            {
                if (Spec.Thresholds.HardGateOnMissingRequiredSections)
                {
                    r.Gate = new GateInfo(SuppressedDiagnosis.SuppressionReason.MissingRequiredSection,
                                          $"Required section '{reqSection.Value}' did not run.");
                }
                break;
            }
            if (section.Status == EvidenceUsability.Invalidated && Spec.Thresholds.FailClosedOnInvalidatedRequiredSections)
            {
                r.Gate = new GateInfo(SuppressedDiagnosis.SuppressionReason.InvalidatedRequiredSection,
                                      $"Required section '{reqSection.Value}' ran but its premise failed (invalidated).");
                break;
            }
            if (!section.Status.IsUsable())
            {
                r.Gate = new GateInfo(SuppressedDiagnosis.SuppressionReason.MissingRequiredSection,
                                      $"Required section '{reqSection.Value}' produced no usable output ({section.Status.RawValue()}).");
                break;
            }
        }

        return r;
    }

    // MARK: Tiering

    private DiagnosisConfidence? Tier(RawScore r, double margin)
    {
        var t = Spec.Thresholds;
        var decisiveOk = !t.RequireDecisiveOrTwoMajorsForConclusive || r.HasDecisive || r.MajorCount >= 2;
        if (r.Coverage >= t.MinimumCoverageForConclusive
            && r.Fit >= t.MinimumFitForConclusive
            && r.Contradiction <= t.MaxContradictionForConclusive
            && r.IndependentGroups >= t.MinimumIndependentSupportGroupsForConclusive
            && margin >= t.MinimumMarginForConclusive
            && decisiveOk)
        {
            return DiagnosisConfidence.Conclusive;
        }
        if (r.Coverage >= t.MinimumCoverageForProbable
            && r.Fit >= t.MinimumFitForProbable
            && r.Contradiction <= t.MaxContradictionForProbable
            && r.IndependentGroups >= t.MinimumIndependentSupportGroupsForProbable
            && margin >= t.MinimumMarginForProbable)
        {
            return DiagnosisConfidence.Probable;
        }
        return null;
    }

    // MARK: Mutual exclusion

    /// Swift iterates its `emitted` dictionary in hash order; the outcome is order-independent
    /// because the tie-break chain (strength → fit → id) is total. Per plan Step 3.1, C# iterates
    /// in DiagnosisId ordinal order anyway — deterministic by construction — and the permutation
    /// test (GV-6 shape) proves outcome-invariance.
    private static Dictionary<DiagnosisId, (DiagnosisScore Score, DiagnosisSpec Spec)> ResolveMutualExclusion(
        Dictionary<DiagnosisId, (DiagnosisScore Score, DiagnosisSpec Spec)> emitted)
    {
        var kept = new Dictionary<DiagnosisId, (DiagnosisScore Score, DiagnosisSpec Spec)>(emitted);
        foreach (var id in emitted.Keys.OrderBy(k => k.Value, StringComparer.Ordinal))
        {
            if (!kept.ContainsKey(id)) { continue; }
            var (score, spec) = emitted[id];
            foreach (var rivalId in spec.MutuallyExclusiveWith)
            {
                if (!kept.TryGetValue(rivalId, out var rival)) { continue; }
                var rivalScore = rival.Score;
                // Drop the weaker of the conflicting pair (ties: keep the one with higher fit,
                // then deterministic id order to stay parity-stable).
                DiagnosisId loser;
                if (score.Strength != rivalScore.Strength)
                {
                    loser = score.Strength < rivalScore.Strength ? id : rivalId;
                }
                else if (score.Fit != rivalScore.Fit)
                {
                    loser = score.Fit < rivalScore.Fit ? id : rivalId;
                }
                else
                {
                    loser = string.CompareOrdinal(id.Value, rivalId.Value) > 0 ? id : rivalId;
                }
                kept.Remove(loser);
                if (loser == id) { break; }
            }
        }
        return kept;
    }

    // MARK: Assembly

    private static DeterminativeResult Assemble(
        Dictionary<DiagnosisId, (DiagnosisScore Score, DiagnosisSpec Spec)> emitted,
        List<SuppressedDiagnosis> suppressed)
    {
        var threats = emitted
            .Where(kv => kv.Key != CleanId)
            .Select(kv => kv.Value.Score)
            .ToList();
        threats.Sort(RankOrderComparison);

        if (threats.Count != 0)
        {
            var names = string.Join(", ", threats.Select(t => t.DisplayName));
            return new DeterminativeResult(
                DeterminativeState.ThreatDetected,
                threats,
                suppressed,
                $"Detected: {names}. The evidence affirmatively supports "
                    + (threats.Count == 1 ? "this diagnosis." : "these diagnoses."));
        }

        // No threat emitted. Consider clean — but never over an honesty overhang.
        // C065: this re-check measures the SAME quantity the admission gate measured
        // (`RawSupport` = un-attenuated observed support), never `RawStrength` (fit·coverage) —
        // the hard gate that suppressed the threat is precisely what crushes coverage, so a
        // strength-based floor here would let `Clean` ride over an unverified threat signal.
        var overhang = suppressed
            .Where(s => s.Reason != SuppressedDiagnosis.SuppressionReason.HardExcluded && s.RawSupport >= OverhangFloor)
            .ToList();
        if (emitted.TryGetValue(CleanId, out var clean) && overhang.Count == 0)
        {
            var cleanEmitted = clean.Score;
            return new DeterminativeResult(
                DeterminativeState.Clean,
                new List<DiagnosisScore> { cleanEmitted },
                suppressed,
                $"No tampering was detected, and the clean pattern is affirmatively supported "
                    + $"(coverage {Pct(cleanEmitted.Coverage)}, fit {Pct(cleanEmitted.Fit)}).");
        }

        // Abstain.
        string why;
        if (overhang.Count != 0)
        {
            var names = string.Join(", ", overhang.Select(o => o.DisplayName));
            // Display rule for the overhang (C062, plan Step 1.4.4): ALWAYS "seen but unconfirmed",
            // never a detection. `belowTier` entries were below the reporting threshold rather
            // than missing their evidence, so the reason clause matches what the overhang actually
            // contains. COPY: shipped on macOS under the same PENDING-OWNER-APPROVAL marker
            // (outputs-catalog.csv); reused verbatim for parity.
            var hasGated = overhang.Any(o =>
                o.Reason is SuppressedDiagnosis.SuppressionReason.MissingRequiredSection
                    or SuppressedDiagnosis.SuppressionReason.InvalidatedRequiredSection);
            var hasBelowTier = overhang.Any(o => o.Reason == SuppressedDiagnosis.SuppressionReason.BelowTier);
            var reasonClause = (hasGated, hasBelowTier) switch
            {
                (true, true) => "required evidence was missing or invalidated, and some of the support stayed below the reporting threshold",
                (false, true) => "its support stayed below the reporting threshold",
                _ => "required evidence was missing or invalidated",
            };
            why = $"Inconclusive: a possible threat signal ({names}) was observed but could not be "
                + $"verified because {reasonClause}. Not treated as clean.";
        }
        else if (emitted.ContainsKey(CleanId))
        {
            why = "Inconclusive: the clean pattern is present but not strongly enough supported to affirm it.";
        }
        else
        {
            why = "Inconclusive: the evidence does not affirmatively resolve to clean or to any threat.";
        }
        return new DeterminativeResult(DeterminativeState.Inconclusive, Array.Empty<DiagnosisScore>(), suppressed, why);
    }

    // MARK: Explanations & helpers

    private static string ExplanationFor(DiagnosisSpec d, RawScore raw, DiagnosisConfidence tier, double margin)
    {
        var basis = raw.HasDecisive ? "a decisive observation"
            : (raw.MajorCount >= 2 ? $"{raw.MajorCount} major observations" : "supporting observations");
        return $"{Capitalized(tier.RawValue())} support for {d.DisplayName}: {basis} across "
            + $"{raw.IndependentGroups} independent evidence group{(raw.IndependentGroups == 1 ? "" : "s")} "
            + $"(fit {Pct(raw.Fit)}, coverage {Pct(raw.Coverage)}, margin {Pct(margin)}, "
            + $"contradiction {Pct(raw.Contradiction)}).";
    }

    /// Deterministic ranking: strength desc, then fit desc, then id asc (parity-stable).
    /// Implemented as a total-order Comparison (List.Sort needs an ordering, not Swift's
    /// areInIncreasingOrder predicate); the order produced is identical.
    private static int RankOrderComparison(DiagnosisScore a, DiagnosisScore b)
    {
        if (a.Strength != b.Strength) { return a.Strength > b.Strength ? -1 : 1; }
        if (a.Fit != b.Fit) { return a.Fit > b.Fit ? -1 : 1; }
        return string.CompareOrdinal(a.Id.Value, b.Id.Value);
    }

    /// Twin of Swift's `tier.rawValue.capitalized` for the single-ASCII-word tier strings.
    private static string Capitalized(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    /// Twin of Swift's private `pct`: `Int((clamp01(abs(x)) * 100).rounded())` — Swift's bare
    /// `.rounded()` is schoolbook AWAY-FROM-ZERO, deliberately NOT `Round6`'s to-even. Do not
    /// unify them (plan Step 3.1); explanation strings would fork across platforms.
    private static string Pct(double x)
        => $"{(int)Math.Round(Clamp01(Math.Abs(x)) * 100, MidpointRounding.AwayFromZero)}%";

    /// Twin of Swift's `clamp01` = `min(1.0, max(0.0, x))`. Written branch-wise because C#'s
    /// `Math.Max` PROPAGATES NaN while Swift's `max` returns the other operand for NaN; this form
    /// matches Swift for NaN (→ 0), −0.0 (preserved), and all ordinary values. Validation keeps
    /// NaN out of every current call path; the branch is defence-in-depth parity, not a live path.
    private static double Clamp01(double x) => x >= 0.0 ? (x <= 1.0 ? x : 1.0) : 0.0;
}

// MARK: - Numeric helpers (single rounding mode for determinism / cross-platform parity)

/// Round to 6 decimals under a single, explicit rounding rule so the same evidence yields the same
/// number on every platform — the bit-for-bit twin of Swift's `round6` (×1e6,
/// `.rounded(.toNearestOrEven)`, ÷1e6). `internal` (not `public`) so the regression suite
/// (`MatrixRoundingParityTests`, GV-10) can pin the banker's-rounding boundary behaviour directly
/// via `InternalsVisibleTo`; not part of the public API. NOTE: the retired DST
/// `SynthesisEngine.RoundPercent` is deliberately AWAY-FROM-ZERO — a different primitive for a
/// different (retired) contract; do not unify them.
internal static class MatrixRounding
{
    internal static double Round6(double x)
        => double.IsFinite(x) ? Math.Round(x * 1_000_000, MidpointRounding.ToEven) / 1_000_000 : 0;
}
