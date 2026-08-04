using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Syxx.Kit
{
    /// <summary>
    /// The canonical, cross-platform byte-stable representation of the evidence a witness snapshot
    /// signs (E011). Its field names, casing, null-omission, key sort, and array sort are IDENTICAL
    /// to the Swift <c>SignedEvidencePayload</c>, so an Ed25519 signature produced on one platform
    /// verifies on the other.
    /// </summary>
    /// <remarks>
    /// A dedicated DTO — rather than signing the wire <see cref="ComparisonResult"/> — is required
    /// because the wire types diverge (Swift <c>errorDescription</c> vs C# <c>ErrorMessage</c>; a
    /// C#-only <c>DohOutcomes</c>; a <c>DNSResponseStatus</c> enum the platforms encode differently).
    /// Signing them directly makes byte-parity impossible and every cross-platform verify reads a
    /// false TAMPERED. <c>Success</c> is computed once at sign time so nothing platform-specific
    /// reaches the signed bytes.
    /// Byte-parity rules: camelCase keys; recursive key sort; nulls omitted; `/`/`+`/non-ASCII raw
    /// (UnsafeRelaxedJsonEscaping); set-valued fields serialized as ordinal-sorted arrays (NOT a
    /// HashSet, whose order is nondeterministic); resolverQueryOutcomes sorted by resolverId.
    /// </remarks>
    public sealed class SignedEvidencePayload
    {
        public int FormatVersion { get; set; } = 2;
        /// <summary>Epoch milliseconds (UTC).</summary>
        public ulong Timestamp { get; set; }
        /// <summary>The subject of the witnessed query (the domain).</summary>
        public string QueryId { get; set; } = string.Empty;
        public List<ResolverQueryOutcomeCanonical> ResolverQueryOutcomes { get; set; } = new();
        public ComparisonResultCanonical ComparisonResult { get; set; } = new();
        /// <summary>Epoch milliseconds (UTC); omitted when null.</summary>
        public ulong? ExpiresAt { get; set; }
        /// <summary>
        /// Present for evidentiary completeness; the field exists on both platforms so byte-parity
        /// holds when null (both omit it). Populated at <see cref="CurrentFormatVersion"/>+ from
        /// the display <c>ComparisonResult.DohOutcomes</c> (B252): one canonical outcome per
        /// provider, sorted by resolverId, <c>success</c> computed from the typed status.
        /// </summary>
        public DoHOutcomesCanonical? DohOutcomes { get; set; }

        /// <summary>
        /// B098/B252: per-resolver typed record lists (name/type/TTL/rdata) binding answer
        /// ownership to the specific resolver that produced them (Swift twin:
        /// <c>ResolverAnswerRecordsCanonical</c>). Sorted by resolverId; each record list sorted
        /// by (name, type, ttl, rdata); omitted when the list is empty.
        /// </summary>
        public List<ResolverAnswerRecordsCanonical>? PerResolverAnswers { get; set; }

        private static readonly JsonSerializerOptions CanonicalOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// The canonical signed bytes — byte-identical to Swift's <c>canonicalBytes()</c>.
        /// </summary>
        public byte[] CanonicalBytes()
        {
            var json = JsonSerializer.Serialize(this, CanonicalOptions);
            var root = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("Payload must be a JSON object");
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

        /// <summary>Ordinal sort + de-dup shared by every set-valued field (matches Swift).</summary>
        public static List<string> SortedUnique(IEnumerable<string> values)
            => values.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        /// <summary>Null-safe canonical equality for the DoH outcomes block (B252 display guard).</summary>
        public static bool DoHOutcomesCanonicalEquals(DoHOutcomesCanonical? a, DoHOutcomesCanonical? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            return a.CanonicalEquals(b);
        }

        /// <summary>
        /// Builds the canonical payload from the wire <see cref="ComparisonResult"/>. Result and
        /// success are computed here (at sign time) from the in-memory typed status, so no
        /// platform-specific wire encoding reaches the signed bytes.
        /// </summary>
        /// <summary>
        /// E020: format version 3 removes free-text <c>errorMessage</c> from the signed canonical
        /// bytes. The strings are platform-divergent (Swift vs C# exception text for the same
        /// failure breaks byte-parity) and attacker-influenceable free text does not belong in
        /// signed evidence — <c>success</c> already carries the signal. MIGRATION: verifiers on
        /// BOTH platforms must accept v3 before either signer emits it; the Windows signer stays
        /// on v2 until the macOS verifier twin lands (tracked in CHANGELOG 2026-07-15).
        /// </summary>
        public const int ErrorMessageFreeFormatVersion = 3;

        /// <summary>
        /// B252: current signed format. v4 = v3 semantics (errorMessage-free) PLUS canonical
        /// <c>dohOutcomes</c> (normalized DoH evidence, formerly display-only and unsigned) and
        /// <c>perResolverAnswers</c> (C# twin of the Swift field, closing the byte-parity gap).
        /// </summary>
        public const int CurrentFormatVersion = 4;

        public static SignedEvidencePayload Build(string domain, ComparisonResult comparison, ulong timestampMillis, ulong? expiresAtMillis, int formatVersion = 2)
        {
            bool includeErrorText = formatVersion < ErrorMessageFreeFormatVersion;
            var outcomes = new List<ResolverQueryOutcomeCanonical>
            {
                new()
                {
                    ResolverId = comparison.LocalServer,
                    Success = !comparison.LocalOutcome.DidFail,
                    ErrorMessage = includeErrorText ? comparison.LocalOutcome.ErrorMessage : null,
                    ResponseTimeMs = null
                }
            };
            foreach (var kv in comparison.PublicResolverOutcomes)
            {
                outcomes.Add(new ResolverQueryOutcomeCanonical
                {
                    ResolverId = kv.Key,
                    Success = !kv.Value.DidFail,
                    ErrorMessage = includeErrorText ? kv.Value.ErrorMessage : null,
                    ResponseTimeMs = null
                });
            }
            outcomes = outcomes.OrderBy(o => o.ResolverId, StringComparer.Ordinal).ToList();

            // No result before evidence: a degraded or inconclusive comparison never reads "matches".
            // E027: bind IsInconclusive and IsDegraded into the guard as distinct tokens so the
            // degraded-vs-inconclusive nuance is not flippable on a VALID snapshot. Previously
            // both collapsed to "inconclusive", so an attacker could flip the nuance without
            // invalidating the signature. The load-bearing result is protected; only the nuance
            // was exposed.
            string result;
            if (comparison.IsRedirectionDetected) result = "diverges";
            else if (comparison.IsInconclusive) result = "inconclusive";
            else if (comparison.IsDegraded) result = "degraded";
            else result = "matches";

            var vantage = SortedUnique(comparison.LocalOutcome.Records.Select(r => r.Rdata));
            var authoritativeRecords = comparison.PublicResolverOutcomes.Values.SelectMany(o => o.Records);
            var authoritative = SortedUnique(authoritativeRecords.Select(r => r.Rdata));
            // Ph6.1/B098/D100: extract TTLs and types from authoritative records for canonical signing.
            // D100 fix: `comparison.PublicResolverOutcomes.Values` enumerates a Dictionary's values,
            // whose order .NET does not contractually guarantee. When multiple resolvers agree on the
            // same rdata but report different (independently-counting-down) TTLs, the representative
            // record picked for that rdata MUST NOT depend on that enumeration order — otherwise the
            // same honest, unmodified evidence resigns differently across runs and later fails its own
            // verification by chance. Within each rdata group we sort candidates by a fully-stable key
            // (Ttl ascending, then Type, then Name as final tie-breakers) and take the first: this
            // deterministically picks the MINIMUM TTL among agreeing resolvers — the conservative,
            // meaningful choice (the answer expires soonest per the freshest-reporting resolver) —
            // regardless of input ordering. Must match Swift's authByRdata selection exactly.
            var authByRdata = authoritativeRecords
                .GroupBy(r => r.Rdata)
                .Select(g => g
                    .OrderBy(r => r.Ttl)
                    .ThenBy(r => r.Type)
                    .ThenBy(r => r.Name, StringComparer.Ordinal)
                    .First())
                .OrderBy(r => r.Rdata, StringComparer.Ordinal)
                .ToList();
            var authoritativeTtls = authByRdata.Count == 0
                ? null
                : authByRdata.Select(r => (uint)r.Ttl).ToList();
            var authoritativeTypes = authByRdata.Count == 0
                ? null
                : authByRdata.Select(r => r.Type).ToList();

            // B098/B252: per-resolver answer records bind ownership — each resolver's records are
            // signed individually so swapping answers between resolvers invalidates the signature.
            // Mirrors Swift build(): local + public, sorted by resolverId; null when the list is
            // empty (even if individual entries carry empty record lists).
            var perResolver = new List<ResolverAnswerRecordsCanonical>
            {
                new(comparison.LocalServer, comparison.LocalOutcome.Records)
            };
            foreach (var kv in comparison.PublicResolverOutcomes)
                perResolver.Add(new ResolverAnswerRecordsCanonical(kv.Key, kv.Value.Records));
            perResolver = perResolver.OrderBy(p => p.ResolverId, StringComparer.Ordinal).ToList();

            // B252: canonical DoH evidence at v4+ (v2/v3 bytes must stay exactly as before, and
            // pre-v4 evidence never carried dohOutcomes — the display banner marks it UNSIGNED).
            DoHOutcomesCanonical? dohCanonical = null;
            if (formatVersion >= CurrentFormatVersion && comparison.DohOutcomes is { Count: > 0 })
            {
                dohCanonical = new DoHOutcomesCanonical
                {
                    Outcomes = comparison.DohOutcomes
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => new ResolverQueryOutcomeCanonical
                        {
                            ResolverId = kv.Key,
                            Success = !kv.Value.DidFail,
                            ErrorMessage = includeErrorText ? kv.Value.ErrorMessage : null,
                            ResponseTimeMs = null
                        })
                        .ToList()
                };
            }

            return new SignedEvidencePayload
            {
                FormatVersion = formatVersion,
                Timestamp = timestampMillis,
                QueryId = domain,
                ResolverQueryOutcomes = outcomes,
                ComparisonResult = new ComparisonResultCanonical
                {
                    Result = result,
                    AuthoritativeSet = authoritative.Count == 0 ? null : authoritative,
                    VantageSet = vantage.Count == 0 ? null : vantage,
                    AuthoritativeTtls = authoritativeTtls,
                    AuthoritativeTypes = authoritativeTypes,
                },
                ExpiresAt = expiresAtMillis,
                DohOutcomes = dohCanonical,
                PerResolverAnswers = perResolver.Count == 0 ? null : perResolver
            };
        }
    }

    public sealed class ResolverQueryOutcomeCanonical
    {
        public string ResolverId { get; set; } = string.Empty;
        public bool Success { get; set; }
        /// <summary>Unified name (NOT errorDescription); omitted when null.</summary>
        public string? ErrorMessage { get; set; }
        /// <summary>
        /// Omitted when null. Left null today; if ever populated it must be an integral millisecond
        /// value to avoid double-formatting divergence across platforms.
        /// </summary>
        public double? ResponseTimeMs { get; set; }

        public bool CanonicalEquals(ResolverQueryOutcomeCanonical other)
            => ResolverId == other.ResolverId
               && Success == other.Success
               && ErrorMessage == other.ErrorMessage
               && Nullable.Equals(ResponseTimeMs, other.ResponseTimeMs);

        // Outcomes are ordinal-sorted by ResolverId in Build(), so index-wise comparison is canonical.
        public static bool SequenceCanonicalEquals(List<ResolverQueryOutcomeCanonical> a, List<ResolverQueryOutcomeCanonical> b)
        {
            if (a is null || b is null) return ReferenceEquals(a, b);
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].CanonicalEquals(b[i])) return false;
            return true;
        }
    }

    public sealed class ComparisonResultCanonical
    {
        /// <summary>"matches" | "diverges" | "inconclusive".</summary>
        // Wire key is the byte-stable "conclusion" (E011 Ed25519 cross-platform parity); the CLR
        // symbol is Result per the code-naming ruling. JsonPropertyName pins the wire so the
        // symbol rename never reaches the signed bytes. Twin: Swift ComparisonResultCanonical.conclusion.
        [JsonPropertyName("conclusion")]
        public string Result { get; set; } = string.Empty;
        /// <summary>Reference (authoritative/public-consensus) IPs, ordinal-sorted; omitted when empty.</summary>
        public List<string>? AuthoritativeSet { get; set; }
        /// <summary>Local-vantage IPs, ordinal-sorted; omitted when empty.</summary>
        public List<string>? VantageSet { get; set; }
        /// <summary>Ph6.1/B098: per-record TTLs for authoritative set; omitted when empty.</summary>
        public List<uint>? AuthoritativeTtls { get; set; }
        /// <summary>Ph6.1/B098: per-record types (1=A, 28=AAAA); omitted when empty.</summary>
        public List<ushort>? AuthoritativeTypes { get; set; }

        public bool CanonicalEquals(ComparisonResultCanonical other)
            => Result == other.Result
               && SetEquals(AuthoritativeSet, other.AuthoritativeSet)
               && SetEquals(VantageSet, other.VantageSet)
               && SeqEquals(AuthoritativeTtls, other.AuthoritativeTtls)
               && SeqEquals(AuthoritativeTypes, other.AuthoritativeTypes);

        private static bool SetEquals(List<string>? a, List<string>? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            return a.SequenceEqual(b);
        }

        private static bool SeqEquals<T>(List<T>? a, List<T>? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            return a.SequenceEqual(b);
        }
    }

    public sealed class DoHOutcomesCanonical
    {
        public List<ResolverQueryOutcomeCanonical> Outcomes { get; set; } = new();

        public bool CanonicalEquals(DoHOutcomesCanonical? other)
        {
            if (other is null) return false;
            return ResolverQueryOutcomeCanonical.SequenceCanonicalEquals(Outcomes, other.Outcomes);
        }
    }

    /// <summary>
    /// B098/B252: per-resolver typed record list binding answer ownership to a specific resolver
    /// (Swift twin: <c>ResolverAnswerRecordsCanonical</c>). Records sorted by
    /// (name, type, ttl, rdata); the outer list is sorted by resolverId. Byte-stable across
    /// platforms so an Ed25519 signature made on one verifies on the other.
    /// </summary>
    public sealed class ResolverAnswerRecordsCanonical
    {
        public string ResolverId { get; set; } = string.Empty;
        public List<CanonicalRecord> Records { get; set; } = new();

        public ResolverAnswerRecordsCanonical() { }

        public ResolverAnswerRecordsCanonical(string resolverId, IEnumerable<DNSRecord> records)
        {
            ResolverId = resolverId;
            Records = records
                .Select(r => new CanonicalRecord
                {
                    Name = r.Name,
                    Type = r.Type,
                    Ttl = r.Ttl,
                    Rdata = r.Rdata
                })
                .OrderBy(r => r.Name, StringComparer.Ordinal)
                .ThenBy(r => r.Type)
                .ThenBy(r => r.Ttl)
                .ThenBy(r => r.Rdata, StringComparer.Ordinal)
                .ToList();
        }

        public bool CanonicalEquals(ResolverAnswerRecordsCanonical other)
        {
            if (other is null) return false;
            if (ResolverId != other.ResolverId) return false;
            if (Records.Count != other.Records.Count) return false;
            for (int i = 0; i < Records.Count; i++)
                if (!Records[i].CanonicalEquals(other.Records[i])) return false;
            return true;
        }

        public static bool SequenceCanonicalEquals(List<ResolverAnswerRecordsCanonical>? a, List<ResolverAnswerRecordsCanonical>? b)
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].CanonicalEquals(b[i])) return false;
            return true;
        }
    }

    public sealed class CanonicalRecord
    {
        public string Name { get; set; } = string.Empty;
        public ushort Type { get; set; }
        public uint Ttl { get; set; }
        public string Rdata { get; set; } = string.Empty;

        public bool CanonicalEquals(CanonicalRecord other)
            => Name == other.Name && Type == other.Type && Ttl == other.Ttl && Rdata == other.Rdata;
    }
}
