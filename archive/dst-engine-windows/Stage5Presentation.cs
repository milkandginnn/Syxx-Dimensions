using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Syxx.Kit;

// Stage-5 engine-to-presentation contract (STAGE5-CONTRACT.md §3-§13) — the Windows-parity twin of
// macOS/Sources/SyxxKit/Synthesis/Stage5Presentation.swift. The engine owns MEANING and populates
// these DTOs; GUI/CLI code renders them WITHOUT reconstructing meaning. The DTO holds CopyTokens,
// never finalized English prose (STAGE5 §2/§10). The two platforms must decode one shared v1 fixture
// to equivalent graphs and re-encode byte-identically (STAGE5 §11).
//
// P1.0: additive, no behavior change. Production writes Conclusion.Stage5 = null; renderers show the
// approved unavailable/inconclusive state for an absent object (STAGE5 §12). Populating this object
// lands in P1.4/P1.9/P1.10/P1.11.

// MARK: - Copy token

/// A stable machine identifier that selects approved copy at render time (STAGE5 §10). Serialized as
/// a bare JSON string, byte-identical to the Swift RawRepresentable `CopyToken`.
[JsonConverter(typeof(CopyTokenJsonConverter))]
public readonly record struct CopyToken(string Value)
{
    public static readonly CopyToken HardGateRequiredChecksMissing = new("stage5.hard_gate.required_checks_missing");
    public static readonly CopyToken ConflictEvidenceDisagrees = new("stage5.conflict.evidence_disagrees");
    public static readonly CopyToken UncertaintyEvidenceThin = new("stage5.uncertainty.evidence_thin");
    public static readonly CopyToken ReferralMechanismUnresolved = new("stage5.referral.mechanism_unresolved");
    public static readonly CopyToken UnavailableTimedOut = new("stage5.unavailable.timed_out");
    public static readonly CopyToken BasisDecisiveSafetyGate = new("stage5.basis.decisive_safety_gate");
}

public sealed class CopyTokenJsonConverter : JsonConverter<CopyToken>
{
    public override CopyToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(reader.GetString() ?? string.Empty);

    public override void Write(Utf8JsonWriter writer, CopyToken value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

/// Maps the shared `Hypothesis` enum to the exact Swift `Hypothesis.rawValue` strings so a
/// WithinClassEntry serializes byte-identically across platforms (Swift uses snake_case for
/// `interception_transport`/`interception_onPath`, which the default camelCase policy would break).
public sealed class HypothesisJsonConverter : JsonConverter<Hypothesis>
{
    public override Hypothesis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (reader.GetString() ?? "inconclusive") switch
        {
            "clean" => Hypothesis.Clean,
            "spoofing" => Hypothesis.Spoofing,
            "censorship" => Hypothesis.Censorship,
            "interception_transport" => Hypothesis.InterceptionTransport,
            "interception_onPath" => Hypothesis.InterceptionOnPath,
            "contentTampering" => Hypothesis.ContentTampering,
            "privacyLeak" => Hypothesis.PrivacyLeak,
            "localCompromise" => Hypothesis.LocalCompromise,
            _ => Hypothesis.Clean,
        };

    public override void Write(Utf8JsonWriter writer, Hypothesis value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            Hypothesis.Clean => "clean",
            Hypothesis.Spoofing => "spoofing",
            Hypothesis.Censorship => "censorship",
            Hypothesis.InterceptionTransport => "interception_transport",
            Hypothesis.InterceptionOnPath => "interception_onPath",
            Hypothesis.ContentTampering => "contentTampering",
            Hypothesis.PrivacyLeak => "privacyLeak",
            Hypothesis.LocalCompromise => "localCompromise",
            _ => "inconclusive",
        });
}

// MARK: - Enums

public enum ObservationValueKind { DnsAnswerSet, DnsRcode, DnssecStatus, Transport, RouteHop, ContentDigest, ResolverIdentity, LeakObserver, CaptureEvent, Other }
public enum VantageKind { LocalSystem, PublicResolver, Authoritative, DimensionServer, PacketCapture, LocalHost, Unknown }
public enum FreshnessState { CurrentRun, FreshCached, Stale, Unknown }
public enum UnavailableReason { NotRequested, Untested, TimedOut, Invalidated, ParseFailed, ProbeFailed, Unauthenticated, Stale, PermissionDenied, DependencyUnavailable }
public enum AnswerStatus { Answered, NoData, Nxdomain, Servfail, Refused, TimedOut, Invalid, Unavailable }
public enum AnswerComparisonClass { Agreement, BenignSetVariation, Discrepancy, InsufficientReferences, Incomparable, NotApplicable }
public enum VerdictState { Clean, Tampered, Inconclusive }
public enum MechanismClass { Rewrite, Block, Transport, Content, Leak, Host, Unresolved }
public enum DeterminationState { Clean, Tampered, Inconclusive }
public enum CredalMeasureKind { Belief }
public enum DecisionBasis { OrdinaryCredalGate, DecisiveSafetyGate, HardGate, UnresolvedMechanism, InsufficientEvidence }
public enum DataRegime { NoData, PartialData, FullData }

// MARK: - CX-1 provenance partition

public sealed record VantageIdentity(
    string VantageId,
    VantageKind Kind,
    string? Endpoint,
    string? Transport,
    bool Authenticated,
    FreshnessState Freshness);

public sealed record DirectObservation(
    string ObservationId,
    string DatapointId,
    Section Section,
    string ObservedAt,
    VantageIdentity Vantage,
    IReadOnlyList<string> RawValues,
    ObservationValueKind ValueKind,
    IReadOnlyList<EvidenceId> SourceEvidenceIds);

public sealed record InferredFinding(
    string FindingId,
    MechanismClass MechanismClass,
    CopyToken? DetailToken,
    IReadOnlyList<string> SourceObservationIds,
    IReadOnlyList<EvidenceId> SourceEvidenceIds);

public sealed record UnavailableDatapoint(
    string DatapointId,
    Section Section,
    UnavailableReason Reason,
    CopyToken ReasonToken);

public sealed record ProvenancePartition(
    IReadOnlyList<DirectObservation> Direct,
    IReadOnlyList<InferredFinding> Inferred,
    IReadOnlyList<UnavailableDatapoint> Unavailable);

// MARK: - CX-1 canonical answer matrix

public sealed record VantageAnswer(
    VantageIdentity Vantage,
    AnswerStatus Status,
    IReadOnlyList<string> CanonicalAnswers,
    int? Rcode,
    string? ObservationId);

public sealed record AnswerComparison(
    AnswerComparisonClass Classification,
    IReadOnlyList<string> LocalVantageIds,
    IReadOnlyList<string> ReferenceVantageIds,
    IReadOnlyList<string> ConsensusAnswers,
    IReadOnlyList<string> OutlierVantageIds);

public sealed record AnswerMatrixRow(
    string RowId,
    string QueryName,
    ushort QueryType,
    IReadOnlyList<VantageAnswer> Entries,
    AnswerComparison Comparison);

// MARK: - CX-2 honesty signals

public sealed record HardGateSignal(
    CopyToken Token,
    IReadOnlyList<VerdictState> BlockedVerdicts,
    IReadOnlyList<Section> RequiredSections,
    IReadOnlyList<string> UnavailableDatapointIds);

public sealed record ScalarHonestySignal(CopyToken Token, double Value);

public sealed record HonestySignals(
    HardGateSignal? HardGate,
    ScalarHonestySignal? Conflict,
    ScalarHonestySignal? Uncertainty);

// MARK: - CX-3 mechanism determination

public sealed record CredalMeasure(CredalMeasureKind Kind, double Value, string Target);

public sealed record WithinClassEntry(Hypothesis Hypothesis, int DisplayOrder, double? Share);

public sealed record MechanismDetermination(
    DeterminationState State,
    MechanismClass? MechanismClass,
    CredalMeasure? Confidence,
    IReadOnlyList<WithinClassEntry> WithinClass,
    DecisionBasis Basis,
    bool ReferralRequired,
    CopyToken? ReferralToken);

// MARK: - Completeness / RPN

public sealed record EvidenceCompleteness(
    int Observed,
    int Expected,
    int CriticalAvailable,
    int CriticalExpected,
    string ExpectedProfileId);

public sealed record PriorityFactors(int Severity, int Detectability, int? Occurrence);

public sealed record FindingPriority(
    string ModelId,
    int ModelVersion,
    PriorityFactors Factors,
    int Score,
    int Rank);

public sealed record PrioritizedFinding(
    string FindingId,
    MechanismClass MechanismClass,
    string? InferenceId,
    IReadOnlyList<string> SourceObservationIds,
    FindingPriority Priority);

// MARK: - Root

public sealed record Stage5Presentation(
    int SchemaVersion,
    string GeneratedAt,
    DataRegime DataRegime,
    EvidenceCompleteness Completeness,
    ProvenancePartition Provenance,
    IReadOnlyList<AnswerMatrixRow> AnswerMatrix,
    HonestySignals Honesty,
    MechanismDetermination Determination,
    IReadOnlyList<PrioritizedFinding> Findings)
{
    /// Canonical JSON options: camelCase keys, lower-camel string enums (Hypothesis via the exact
    /// Swift raw values), null optionals omitted, `/`/non-ASCII raw. Key order is normalized to an
    /// ordinal recursive sort by <see cref="CanonicalBytes"/> to match Swift's `.sortedKeys`.
    public static readonly JsonSerializerOptions CanonicalOptions = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        // Order matters: the Hypothesis-specific converter must precede the general enum factory.
        o.Converters.Add(new HypothesisJsonConverter());
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    /// The canonical wire bytes — byte-identical to Swift's `canonicalBytes()`.
    public byte[] CanonicalBytes()
    {
        var json = JsonSerializer.Serialize(this, CanonicalOptions);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Stage5Presentation must serialize to a JSON object");
        var sorted = SortRecursive(root);
        return JsonSerializer.SerializeToUtf8Bytes(sorted, CanonicalOptions);
    }

    private static JsonNode? SortRecursive(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var kv in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sorted[kv.Key] = SortRecursive(kv.Value?.DeepClone());
                return sorted;
            case JsonArray arr:
                var result = new JsonArray();
                foreach (var item in arr)
                    result.Add(SortRecursive(item?.DeepClone()));
                return result;
            default:
                return node;
        }
    }

    /// Decode a persisted Stage-5 object. A higher schemaVersion than this reader supports fails
    /// closed (returns null) so the caller renders unavailable/inconclusive (STAGE5 §12.3).
    public static Stage5Presentation? Decode(byte[] data)
    {
        Stage5Presentation? obj;
        try { obj = JsonSerializer.Deserialize<Stage5Presentation>(data, CanonicalOptions); }
        catch { return null; }
        if (obj is null || obj.SchemaVersion != 1) return null;
        return obj;
    }

    /// Validate against the STAGE5 §13 invariants. Returns the list of violations; empty ⇒ valid.
    public IReadOnlyList<string> ValidationErrors()
    {
        var errs = new List<string>();
        static bool IdOK(string s) =>
            !string.IsNullOrEmpty(s) && s.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or ':' or '-');

        // (V1 / STAGE5 §12.3) Unsupported schemaVersion fails closed at the validation gate too — the
        // Conclusion decode path uses default System.Text.Json, bypassing Decode()'s guard, so IsValid
        // must reject an unknown version here or a schemaVersion != 1 object renders as trusted.
        if (SchemaVersion != 1) errs.Add($"unsupported schemaVersion {SchemaVersion}");

        // (1) IDs nonempty, allowed charset, unique within namespace.
        foreach (var d in Provenance.Direct) if (!IdOK(d.ObservationId)) errs.Add($"direct.observationId invalid: {d.ObservationId}");
        foreach (var f in Provenance.Inferred) if (!IdOK(f.FindingId)) errs.Add($"inferred.findingId invalid: {f.FindingId}");
        foreach (var f in Findings) if (!IdOK(f.FindingId)) errs.Add($"findings.findingId invalid: {f.FindingId}");
        var directIds = Provenance.Direct.Select(d => d.ObservationId).ToList();
        if (directIds.Distinct().Count() != directIds.Count) errs.Add("direct observationId not unique");
        var findingIds = Findings.Select(f => f.FindingId).ToList();
        if (findingIds.Distinct().Count() != findingIds.Count) errs.Add("findings findingId not unique");

        // (2) Source observations exist in provenance.direct.
        var directSet = directIds.ToHashSet();
        foreach (var f in Provenance.Inferred)
            foreach (var sid in f.SourceObservationIds)
                if (!directSet.Contains(sid)) errs.Add($"inferred {f.FindingId} references missing observation {sid}");
        foreach (var f in Findings)
            foreach (var sid in f.SourceObservationIds)
                if (!directSet.Contains(sid)) errs.Add($"finding {f.FindingId} references missing observation {sid}");

        // (3) Answer-entry observationId links to a dnsAnswerSet observation with the same vantage.
        var byObsId = Provenance.Direct.GroupBy(d => d.ObservationId).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in AnswerMatrix)
            foreach (var e in row.Entries)
                if (e.ObservationId is { } oid)
                {
                    if (!byObsId.TryGetValue(oid, out var obs)) { errs.Add($"answer entry references missing observation {oid}"); continue; }
                    if (obs.ValueKind != ObservationValueKind.DnsAnswerSet) errs.Add($"answer entry {oid} is not a dnsAnswerSet observation");
                    if (obs.Vantage.VantageId != e.Vantage.VantageId) errs.Add($"answer entry {oid} vantage mismatch");
                }

        // (4) Direct and unavailable datapoint identities do not overlap.
        var directDatapoints = Provenance.Direct.Select(d => d.DatapointId).ToHashSet();
        foreach (var u in Provenance.Unavailable)
            if (directDatapoints.Contains(u.DatapointId)) errs.Add($"datapoint both direct and unavailable: {u.DatapointId}");

        // (6) Scalars finite and in range.
        void FiniteIn01(double v, string label) { if (!double.IsFinite(v) || v < 0 || v > 1) errs.Add($"{label} out of [0,1]: {v}"); }
        if (Honesty.Conflict is { } cf) FiniteIn01(cf.Value, "conflict");
        if (Honesty.Uncertainty is { } un) FiniteIn01(un.Value, "uncertainty");
        if (Determination.Confidence is { } cm) FiniteIn01(cm.Value, "confidence");
        double shareSum = 0; bool anyShare = false;
        foreach (var w in Determination.WithinClass) if (w.Share is { } s) { FiniteIn01(s, "withinClass.share"); shareSum += s; anyShare = true; }
        if (anyShare && Math.Abs(shareSum - 1.0) > 1e-9) errs.Add($"withinClass shares do not sum to 1: {shareSum}");

        // (7)/(8) Comparison classification consistency.
        foreach (var row in AnswerMatrix)
        {
            var cmp = row.Comparison;
            switch (cmp.Classification)
            {
                case AnswerComparisonClass.Agreement:
                case AnswerComparisonClass.BenignSetVariation:
                    if (cmp.OutlierVantageIds.Count > 0) errs.Add($"row {row.RowId}: {cmp.Classification} lists outliers");
                    break;
                case AnswerComparisonClass.Discrepancy:
                    if (cmp.OutlierVantageIds.Count == 0) errs.Add($"row {row.RowId}: discrepancy has no outlier");
                    if (cmp.ReferenceVantageIds.Count == 0) errs.Add($"row {row.RowId}: discrepancy has no reference");
                    break;
                case AnswerComparisonClass.InsufficientReferences:
                    if (cmp.ConsensusAnswers.Count > 0) errs.Add($"row {row.RowId}: insufficientReferences must not carry consensus");
                    break;
            }
        }

        // (9)/(11) State/class compatibility.
        if (Determination.State == DeterminationState.Clean)
        {
            if (Determination.MechanismClass is not null) errs.Add("clean state must have null mechanismClass");
            if (Determination.WithinClass.Count > 0) errs.Add("clean state must have empty withinClass");
            if (Determination.Basis == DecisionBasis.DecisiveSafetyGate) errs.Add("clean state cannot use decisiveSafetyGate basis");
            if (Honesty.HardGate is { } hg && hg.BlockedVerdicts.Contains(VerdictState.Clean))
                errs.Add("clean state while a hard gate blocks clean");
            // (V2, §13 rule 9) Clean is impossible with critical data missing.
            if (Completeness.CriticalAvailable < Completeness.CriticalExpected)
                errs.Add($"clean state impossible with critical missing data ({Completeness.CriticalAvailable}/{Completeness.CriticalExpected})");
            // (V3) A clean verdict cannot sit above prioritized tamper findings.
            if (Findings.Count > 0) errs.Add("clean state must have no prioritized findings");
            // (V4) A clean verdict cannot carry an active conflict signal ("the evidence disagrees").
            if (Honesty.Conflict is { } cfl && cfl.Value > 0)
                errs.Add($"clean state cannot carry an active conflict signal ({cfl.Value})");
        }
        if (Determination.State == DeterminationState.Tampered && Determination.MechanismClass is null)
            errs.Add("tampered state requires a mechanismClass (use Unresolved)");

        // (10) No-data ⇒ inconclusive, null confidence, zero inferred findings, observed == 0.
        if (DataRegime == DataRegime.NoData)
        {
            if (Determination.State != DeterminationState.Inconclusive) errs.Add("noData regime must be inconclusive");
            if (Determination.Confidence is not null) errs.Add("noData regime must have null confidence");
            if (Provenance.Inferred.Count > 0) errs.Add("noData regime must have zero inferred findings");
            if (Completeness.Observed != 0) errs.Add("noData regime must have observed == 0");
        }

        // Completeness counts (STAGE5 §8).
        var cc = Completeness;
        if (cc.Observed < 0 || cc.Expected <= 0 || cc.Observed > cc.Expected)
            errs.Add($"completeness observed/expected invalid: {cc.Observed}/{cc.Expected}");
        if (cc.CriticalAvailable < 0 || cc.CriticalExpected < 0 || cc.CriticalAvailable > cc.CriticalExpected)
            errs.Add($"completeness critical counts invalid: {cc.CriticalAvailable}/{cc.CriticalExpected}");

        // (13) Priority formula, unique ranks, ascending order.
        foreach (var f in Findings)
        {
            var p = f.Priority;
            if (p.ModelId == "syxx.stage5.sd" && p.ModelVersion == 1)
            {
                if (p.Score != p.Factors.Severity * p.Factors.Detectability) errs.Add($"finding {f.FindingId} score != severity*detectability");
                if (p.Factors.Occurrence is not null) errs.Add($"finding {f.FindingId} v1 must have null occurrence");
            }
            // Ph7.2: the v2 SOD model REQUIRES occurrence and scores S×O×D.
            else if (p.ModelId == RPNOccurrence.ModelId && p.ModelVersion == RPNOccurrence.ModelVersion)
            {
                if (p.Factors.Occurrence is not { } occ)
                    errs.Add($"finding {f.FindingId} sod v2 must carry occurrence");
                else
                {
                    if (occ < 1 || occ > 10) errs.Add($"finding {f.FindingId} occurrence out of 1..10: {occ}");
                    if (p.Score != p.Factors.Severity * p.Factors.Detectability * occ)
                        errs.Add($"finding {f.FindingId} score != severity*detectability*occurrence");
                }
            }
        }
        var ranks = Findings.Select(f => f.Priority.Rank).ToList();
        if (ranks.Distinct().Count() != ranks.Count) errs.Add("finding ranks not unique");
        if (!ranks.SequenceEqual(ranks.OrderBy(r => r))) errs.Add("findings not in ascending rank order");

        return errs;
    }

    [JsonIgnore]
    public bool IsValid => ValidationErrors().Count == 0;
}
