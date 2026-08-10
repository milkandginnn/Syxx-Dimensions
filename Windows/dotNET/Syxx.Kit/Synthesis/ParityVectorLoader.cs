using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Syxx.Kit;

// PARITY_VECTOR_V1 — C# side of the shared conclusion-vector loader
// (contract: docs/parity-vector-v1-spec.md; track: cross-platform-parity_20260805, Task 1.4).
// Twin of macOS/Sources/SyxxKit/Synthesis/ParityVectorLoader.swift.
//
// WHY THIS LIVES IN Syxx.Kit AND NOT Syxx/Commands
// ------------------------------------------------
// The plan says "C# loader in Syxx/Commands". It is here instead, deliberately, mirroring the Swift
// twin's placement in SyxxKit rather than SyxxCLI:
//
//  1. Syxx.Tests cannot reference types in the Syxx executable — its ProjectReference carries
//     ReferenceOutputAssembly="false" (Syxx.Tests.csproj), which exists so the CLI is BUILT before
//     the tests run without leaking CLI types into them. A loader in Syxx/Commands would therefore
//     be reachable only through a spawned process, and a loader whose rejection rules are only
//     reachable through a subprocess is a loader whose rejection rules do not get tested.
//  2. Kit placement keeps the two loaders structurally symmetric, which is the point of the track.
//
// Syxx/Commands/ParityTranscriptCommand.cs keeps only the flag parsing and the printing.
//
// FAIL-LOUD DISCIPLINE
// --------------------
// There is no partial-success path. The first rejection throws and nothing is returned — a corpus
// loads whole or fails by name. An unreadable directory and an EMPTY directory are both errors, not
// empty lists: "zero cases loaded" rendering as success is the exact shape of the silent-skip defect
// this track exists to close.
//
// WHY NOTHING HERE BINDS SectionInputs BY REFLECTION
// --------------------------------------------------
// SectionInputs (Synthesizer.cs:19-85) exposes 34 public FIELDS and no properties, and
// JsonSerializerOptions.IncludeFields defaults to FALSE. A JsonSerializer.Deserialize<SectionInputs>
// call therefore returns a fully-default instance and throws nothing — every input silently dropped,
// every section reading `untested`, and a diff that is clean because neither engine was given
// anything. That is precisely the failure the contract's "unknown and unrepresentable fields" rule
// exists to forbid, arriving through the back door. Every key below is hand-walked and assigned
// explicitly, and ParityVectorLoaderTests pins the regression.

/// <summary>One loaded PARITY_VECTOR_V1 case: the id, its sort key, the clock both engines must pin
/// to, and the engine-ready inputs.</summary>
public sealed record LoadedCase(
    /// <summary>The filename remainder after <c>NNN_</c>, cross-checked against the file's own
    /// <c>vectorId</c>.</summary>
    string VectorId,
    /// <summary>The three-digit filename prefix that fixes this case's position in the corpus.</summary>
    string SortKey,
    /// <summary>Built from <c>pinnedUnixSeconds</c> and nothing else.</summary>
    DateTimeOffset PinnedNow,
    SectionInputs Inputs,
    /// <summary>The file this case came from, so a downstream failure can name it too.</summary>
    string CaseFile);

/// <summary>A corpus refusal. Every message names the offending case file, and — where a key is
/// implicated — the key.</summary>
public sealed class ParityVectorException : Exception
{
    public ParityVectorException(string message) : base(message) { }
    public ParityVectorException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Loads a PARITY_VECTOR_V1 case directory into engine-ready <see cref="SectionInputs"/>.
/// </summary>
/// <remarks>
/// <para><b>Wire-shape rules.</b> The case file is <c>NNN_&lt;vectorId&gt;.json</c> — three ASCII
/// digits, an underscore, then the id. <c>NNN</c> is the sort key and <c>&lt;vectorId&gt;</c> must
/// equal what the file declares. Case order is the ordinal-sorted filename list; because duplicate
/// <c>NNN</c> prefixes are rejected, the order is fixed by three ASCII digits and is therefore
/// collation-independent, so the Swift twin cannot order the same corpus differently.</para>
///
/// <para><b>Requiredness follows the Swift twin, member for member.</b> A member that is
/// non-optional in the Swift model is REQUIRED here; a Swift <c>T?</c> may be omitted (omitted →
/// null). This is not a stylistic choice: it is what makes "loads on one engine" equal "loads on
/// both". It also closes the C#-side authoring hazard the brief flagged — several C# models
/// (<see cref="NetworkStateReport"/> most notably) pre-initialise their members, so a partial
/// payload would otherwise inherit a C# default that Swift would have refused outright. Requiring
/// what Swift requires means a partial payload is refused here too, by name.</para>
///
/// <para><b>Unknown members are refused at every depth</b>, not only at the top of
/// <c>sectionInputs</c>. This is STRICTER than the Swift twin, whose synthesized <c>Decodable</c>
/// conformances silently ignore unknown members; see csharp-field-shapes.md, "Divergences from the
/// Swift twin".</para>
///
/// <para>Full per-field shapes:
/// <c>conductor/tracks/cross-platform-parity_20260805/csharp-field-shapes.md</c>.</para>
/// </remarks>
public static class ParityVectorLoader
{
    /// <summary>The only <c>schemaVersion</c> this loader recognises. Any other value is a
    /// rejection — never a best-effort parse (spec rule 1).</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>The four keys a case file may carry at top level — exactly these, no more, no
    /// fewer.</summary>
    public static readonly IReadOnlySet<string> TopLevelKeys =
        new HashSet<string>(StringComparer.Ordinal)
        { "schemaVersion", "vectorId", "pinnedUnixSeconds", "sectionInputs" };

    /// <summary>
    /// The 32 shared field names, in the spec's order. <c>resolverSubstituted</c> and
    /// <c>transportObservation</c> are deliberately ABSENT: both platforms now carry them
    /// (spec Amendment A1, 2026-08-05), but v1's scope stays at 32, so a vector naming either is
    /// rejected by name rather than silently dropped by a loader that could not express it.
    /// </summary>
    public static readonly IReadOnlySet<string> SharedFieldNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "resolution", "dnssec", "nxCanary", "dnssecCanary", "comparison", "targetDomain",
            "targetQueryType", "warp", "warpManifest", "network", "leak", "exfil",
            "cageAttributionRate", "drifts", "configChanges", "latency", "stability",
            "wildcardSynthesis", "denial", "dualStack", "edns0", "versionBind", "stubEntropy",
            "authoritative", "svcb", "ttlAnomaly", "cnameChain", "encryptedTransport",
            "openResolver", "ecs", "responseSize", "serverGroundTruth",
        };

    /// <summary>
    /// Members the shared wire shape carries that the C# model has nowhere to put. Each is a
    /// non-optional stored property on the Swift twin — so the Swift loader REQUIRES it and the
    /// canonical corpus therefore writes it — while the C# type has no corresponding member at all.
    /// </summary>
    /// <remarks>
    /// <para>They are accepted (with their type checked) and then carried by nothing. That is a
    /// drop, and it is recorded here rather than left to be discovered: refusing them instead would
    /// make <c>corpus/parity-vectors/009_unfetchable-resolution-timeout.json</c> — and every future
    /// <c>exfil</c> or <c>scopedResolvers</c> case — unloadable on this engine, which trades a named
    /// asymmetry for a corpus neither engine can share.</para>
    ///
    /// <para>The drop is safe TODAY for one specific reason, and only that reason: no member listed
    /// here is read by <c>Synthesizer.Conclude</c> on either platform. The first code on either side
    /// that starts reading one turns this list into a live divergence, which is why it is a pinned,
    /// asserted list (ParityVectorLoaderTests) and not a comment. Every entry is also named in
    /// csharp-field-shapes.md against its Swift declaration.</para>
    ///
    /// <para>The mirror-image case — a member only the C# model has — is REFUSED instead, because
    /// the Swift twin's synthesized decoders would silently ignore it, so such a vector could only
    /// ever mean different things on the two engines. Those are named at their decoders.</para>
    /// </remarks>
    public static readonly IReadOnlyList<string> UnrepresentedMembers =
    [
        "exfil[].listsEvaluated",
        "network.scopedResolvers[].isMDNS",
        "network.scopedResolvers[].isReachable",
        "network.scopedResolvers[].isSupplemental",
        "resolution.attemptCount",
        "resolution.conflictingCNAMEOwnerCount",
        "resolution.offClassAnswerCount",
        "resolution.spoofedPacketCount",
    ];

    // MARK: - Loading

    /// <summary>
    /// Load every case file in <paramref name="directory"/>, in sorted case order. Throws on the
    /// first rejection and returns nothing — there is no partial success. An empty or unreadable
    /// directory throws rather than returning an empty list.
    /// </summary>
    public static IReadOnlyList<LoadedCase> Load(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ParityVectorException(
                $"parity vector directory could not be read: {directory} — {ex.Message}", ex);
        }

        // Enumeration order differs by filesystem and runtime, so nothing downstream may depend on
        // it. Candidates are collected, checked for filename legality, then sorted by sort key.
        var candidates = new List<(string SortKey, string VectorId, string Path)>();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (Directory.Exists(entry))
            {
                // The spec's layout block shows `002_.../` and plan Task 1.6 says to mirror the
                // per-case DIRECTORY layout of warp-manifest-vectors, but the spec's prose says
                // "One case per file". v1 here is file-per-case; a directory-shaped entry is refused
                // loudly rather than skipped, so the disagreement surfaces at the moment it would
                // otherwise cost a case.
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                throw new ParityVectorException(
                    $"case {name}: unusable case filename — directory-shaped entry; " +
                    "PARITY_VECTOR_V1 is one case per NNN_<vectorId>.json file");
            }
            if (!name.EndsWith(".json", StringComparison.Ordinal)) continue;   // README.md, .DS_Store, …
            if (!TryParseCaseFilename(name, out var sortKey, out var vectorId))
            {
                throw new ParityVectorException(
                    $"case {name}: unusable case filename — a .json entry must be named " +
                    "NNN_<vectorId>.json with a three-digit sort prefix");
            }
            candidates.Add((sortKey, vectorId, entry));
        }

        if (candidates.Count == 0)
        {
            throw new ParityVectorException(
                $"parity vector directory contains no case files: {directory} — " +
                "an empty corpus is a failure, not zero cases; refusing to run vacuously");
        }

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in candidates.OrderBy(c => Path.GetFileName(c.Path), StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(c.Path);
            if (seen.TryGetValue(c.SortKey, out var first))
            {
                throw new ParityVectorException(
                    $"duplicate sort prefix '{c.SortKey}' shared by {first}, {fileName} — " +
                    "case order would be ambiguous");
            }
            seen[c.SortKey] = fileName;
        }

        return candidates
            .OrderBy(c => Path.GetFileName(c.Path), StringComparer.Ordinal)
            .Select(c => LoadCase(c.Path, c.SortKey, c.VectorId))
            .ToList();
    }

    /// <summary><c>NNN_&lt;vectorId&gt;.json</c> → <c>(NNN, vectorId)</c>; false when the name does
    /// not match.</summary>
    internal static bool TryParseCaseFilename(string name, out string sortKey, out string vectorId)
    {
        sortKey = string.Empty;
        vectorId = string.Empty;
        if (!name.EndsWith(".json", StringComparison.Ordinal)) return false;
        var stem = name[..^".json".Length];
        if (stem.Length < 5) return false;                       // NNN + "_" + at least one id char
        for (int i = 0; i < 3; i++)
        {
            if (stem[i] < '0' || stem[i] > '9') return false;    // ASCII digits only, never Unicode
        }
        if (stem[3] != '_') return false;
        sortKey = stem[..3];
        vectorId = stem[4..];
        return vectorId.Length > 0;
    }

    private static LoadedCase LoadCase(string path, string sortKey, string vectorIdFromFilename)
    {
        var caseFile = Path.GetFileName(path);

        byte[] utf8;
        try
        {
            utf8 = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ParityVectorException($"case {caseFile}: could not be read — {ex.Message}", ex);
        }

        // Duplicate members are rejected BEFORE the tree is built. System.Text.Json's object model
        // resolves a repeated key by last-wins, which would drop an input the author wrote and
        // believed was exercised — the silent-drop class the contract forbids, one level down.
        // The Swift twin does not do this (JSONSerialization is also last-wins); recorded in
        // csharp-field-shapes.md rather than assumed symmetric.
        RejectDuplicateMembers(utf8, caseFile);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(utf8, nodeOptions: null, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        }
        catch (JsonException ex)
        {
            throw new ParityVectorException($"case {caseFile}: not valid JSON — {ex.Message}", ex);
        }

        if (root is not JsonObject top)
            throw new ParityVectorException($"case {caseFile}: not valid JSON — the document is not an object");

        // Unknown top-level keys first, smallest name first, so a case with two of them fails the
        // same way on every run and on both runtimes.
        var unknownTop = top.Select(kv => kv.Key)
            .Where(k => !TopLevelKeys.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .FirstOrDefault();
        if (unknownTop is not null)
        {
            throw new ParityVectorException(
                $"case {caseFile}: unknown top-level key '{unknownTop}' — a case carries exactly " +
                "schemaVersion, vectorId, pinnedUnixSeconds, sectionInputs");
        }
        foreach (var required in new[] { "schemaVersion", "vectorId", "sectionInputs" })
        {
            if (!top.ContainsKey(required))
                throw new ParityVectorException($"case {caseFile}: missing required top-level key '{required}'");
        }

        // schemaVersion — a value we do not recognise is refused, including a non-integer one.
        if (!TryGetInt64(top["schemaVersion"], out var version))
        {
            throw new ParityVectorException(
                $"case {caseFile}: unrecognised schemaVersion (not an integer) — this loader " +
                $"recognises {SupportedSchemaVersion} only, and never best-effort parses");
        }
        if (version != SupportedSchemaVersion)
        {
            throw new ParityVectorException(
                $"case {caseFile}: unrecognised schemaVersion {version.ToString(CultureInfo.InvariantCulture)} — " +
                $"this loader recognises {SupportedSchemaVersion} only, and never best-effort parses");
        }

        if (top["vectorId"]?.GetValueKind() != JsonValueKind.String)
        {
            throw new ParityVectorException(
                $"case {caseFile}: sectionInputs key 'vectorId' could not be decoded — expected a string");
        }
        var declaredVectorId = top["vectorId"]!.GetValue<string>();
        if (!string.Equals(declaredVectorId, vectorIdFromFilename, StringComparison.Ordinal))
        {
            throw new ParityVectorException(
                $"case {caseFile}: vectorId '{declaredVectorId}' disagrees with the filename " +
                $"remainder '{vectorIdFromFilename}'");
        }

        // pinnedUnixSeconds — spec rule 5. Absent, null and non-integer are all refusals; both
        // engines build their clock from this and nothing else, so a fabricated fallback would be a
        // clock neither engine measured.
        if (!top.ContainsKey("pinnedUnixSeconds"))
            throw new ParityVectorException($"case {caseFile}: pinnedUnixSeconds is unusable — absent");
        var pinnedNode = top["pinnedUnixSeconds"];
        if (pinnedNode is null)
            throw new ParityVectorException($"case {caseFile}: pinnedUnixSeconds is unusable — null");
        if (!TryGetInt64(pinnedNode, out var pinnedSeconds))
            throw new ParityVectorException($"case {caseFile}: pinnedUnixSeconds is unusable — not an integer");

        DateTimeOffset pinnedNow;
        try
        {
            pinnedNow = DateTimeOffset.FromUnixTimeSeconds(pinnedSeconds);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ParityVectorException(
                $"case {caseFile}: pinnedUnixSeconds is unusable — " +
                $"{pinnedSeconds.ToString(CultureInfo.InvariantCulture)} is outside the representable range", ex);
        }

        if (top["sectionInputs"] is not JsonObject sectionInputs)
        {
            throw new ParityVectorException(
                $"case {caseFile}: sectionInputs key 'sectionInputs' could not be decoded — expected an object");
        }

        var inputs = DecodeSectionInputs(sectionInputs, caseFile);
        return new LoadedCase(declaredVectorId, sortKey, pinnedNow, inputs, caseFile);
    }

    /// <summary>Refuse a repeated member name within any one JSON object, at any depth.</summary>
    private static void RejectDuplicateMembers(byte[] utf8, string caseFile)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        // B022: reuse a single HashSet per scope depth, cleared on EndObject instead of
        // allocating a new one per JSON object. The stack still tracks depth so nested objects
        // each get their own set; we just avoid the per-object heap allocation.
        var scopes = new Stack<HashSet<string>>();
        var pool = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        if (scopes.Count == 0)
                        {
                            pool.Clear();
                            scopes.Push(pool);
                        }
                        else
                        {
                            scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                        }
                        break;
                    case JsonTokenType.EndObject:
                        if (scopes.Count > 0)
                        {
                            var popped = scopes.Pop();
                            if (popped != pool) popped.Clear();
                        }
                        break;
                    case JsonTokenType.PropertyName:
                        var member = reader.GetString() ?? string.Empty;
                        if (scopes.Count > 0 && !scopes.Peek().Add(member))
                        {
                            throw new ParityVectorException(
                                $"case {caseFile}: duplicate JSON member '{member}' in one object — " +
                                "the later value would silently replace the earlier one, dropping an " +
                                "input the vector declares");
                        }
                        break;
                    default:
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON. JsonNode.Parse reports it with a better message a moment later; do
            // not pre-empt it here with a worse one.
        }
    }

    // MARK: - sectionInputs

    private static SectionInputs DecodeSectionInputs(JsonObject o, string f)
    {
        // Keys are visited in ordinal order so the named rejection is deterministic and matches the
        // Swift twin's, which sorts for the same reason.
        var names = o.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var unknown = names.FirstOrDefault(n => !SharedFieldNames.Contains(n));
        if (unknown is not null)
        {
            throw new ParityVectorException(
                $"case {f}: unknown sectionInputs key '{unknown}' — outside the PARITY_VECTOR_V1 " +
                "shared field set, so it cannot be expressed in v1 and is refused rather than dropped");
        }

        var inputs = new SectionInputs();
        foreach (var name in names)
        {
            var v = o[name];
            switch (name)
            {
                case "resolution": inputs.Resolution = DecodeDnsResponse(v, f, name); break;
                case "dnssec": inputs.Dnssec = DecodeEnum(v, f, name, DnssecStatusWire); break;
                case "nxCanary": inputs.NxCanary = DecodeCanaryResult(v, f, name); break;
                case "dnssecCanary": inputs.DnssecCanary = DecodeCanaryResult(v, f, name); break;
                case "comparison": inputs.Comparison = DecodeComparisonResult(v, f, name); break;
                case "targetDomain": inputs.TargetDomain = RequireString(v, f, name); break;
                case "targetQueryType": inputs.TargetQueryType = RequireUInt16(v, f, name); break;
                case "warp": inputs.Warp = DecodeWarpResult(v, f, name); break;
                case "warpManifest": inputs.WarpManifest = DecodeWarpManifestResult(v, f, name); break;
                case "network": inputs.Network = DecodeNetworkStateReport(v, f, name); break;
                case "leak": inputs.Leak = DecodeLeakTestResult(v, f, name); break;
                case "exfil": inputs.Exfil = RequireArray(v, f, name)
                    .Select((n, i) => DecodeExfilAssessment(n, f, $"{name}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                    .ToList(); break;
                case "cageAttributionRate": inputs.CageAttributionRate = RequireDouble(v, f, name); break;
                case "drifts":
                    // v1 admits no dates (spec, "Why a written contract precedes the loaders"), and
                    // AnswerDrift carries one. Refused by name rather than partially accepted.
                    throw new ParityVectorException(
                        $"case {f}: sectionInputs key 'drifts' could not be decoded — drifts is " +
                        "unrepresentable in v1: AnswerDrift carries a date " +
                        "(Windows/dotNET/Syxx.Kit/Trust/Trust.cs:33) and PARITY_VECTOR_V1 admits no " +
                        "dates; use configChanges for witness vectors");
                case "configChanges": inputs.ConfigChanges = RequireArray(v, f, name)
                    .Select((n, i) => DecodeConfigChange(n, f, $"{name}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                    .ToList(); break;
                case "latency": inputs.Latency = DecodeResolverLatency(v, f, name); break;
                case "stability": inputs.Stability = DecodeAnswerStability(v, f, name); break;
                case "wildcardSynthesis": inputs.WildcardSynthesis = DecodeWildcardSynthesis(v, f, name); break;
                case "denial": inputs.Denial = DecodeDenialOfExistence(v, f, name); break;
                case "dualStack": inputs.DualStack = DecodeDualStackAudit(v, f, name); break;
                case "edns0": inputs.Edns0 = DecodeEdns0Capability(v, f, name); break;
                case "versionBind": inputs.VersionBind = DecodeVersionBindResult(v, f, name); break;
                case "stubEntropy": inputs.StubEntropy = DecodeStubEntropyAudit(v, f, name); break;
                case "authoritative": inputs.Authoritative = DecodeAuthoritativeComparison(v, f, name); break;
                case "svcb": inputs.Svcb = DecodeSvcbFinding(v, f, name); break;
                case "ttlAnomaly": inputs.TtlAnomaly = DecodeTtlAnomaly(v, f, name); break;
                case "cnameChain": inputs.CnameChain = DecodeCnameChainAudit(v, f, name); break;
                case "encryptedTransport": inputs.EncryptedTransport = DecodeEncryptedTransport(v, f, name); break;
                case "openResolver": inputs.OpenResolver = DecodeOpenResolverFinding(v, f, name); break;
                case "ecs": inputs.Ecs = DecodeEcsLeak(v, f, name); break;
                case "responseSize": inputs.ResponseSize = DecodeResponseSizeAudit(v, f, name); break;
                case "serverGroundTruth": inputs.ServerGroundTruth = DecodeGroundTruth(v, f, name); break;
                default:
                    // Unreachable: the sweep above already refused anything outside the shared set.
                    // Asserted rather than assumed so a future edit that adds a name to
                    // SharedFieldNames without adding its arm here cannot decode to a silent null —
                    // which would render an untested field as covered.
                    throw new ParityVectorException(
                        $"case {f}: unknown sectionInputs key '{name}' — outside the " +
                        "PARITY_VECTOR_V1 shared field set, so it cannot be expressed in v1 and is " +
                        "refused rather than dropped");
            }
        }
        return inputs;
    }

    // MARK: - Field decoders
    //
    // One method per field type. Each opens a member walker, reads every member it recognises, and
    // lets the walker refuse anything else by name. Requiredness mirrors the Swift model member for
    // member (see the class remarks).

    private static DNSResponse DecodeDnsResponse(JsonNode? n, string f, string p)
    {
        // Mirrors the Swift TOLERANT decoder (DNSResolver.swift:240-261): only `status` is required
        // and every other member carries the same default the Swift decoder supplies.
        var w = new Walker(n, f, p);
        var r = new DNSResponse
        {
            Status = w.Enum("status", DnsResponseStatusWire),
            Records = w.OptArray("records", DecodeDnsRecord)?.ToList() ?? [],
            AuthorityRecords = w.OptArray("authorityRecords", DecodeDnsRecord)?.ToList() ?? [],
            Edns = w.OptObject("edns", DecodeEdnsResponseInfo),
            EdnsMalformed = w.OptBool("ednsMalformed") ?? false,
            AuthorityMalformed = w.OptBool("authorityMalformed") ?? false,
            OffTypeAnswerCount = w.OptInt32("offTypeAnswerCount") ?? 0,
            OffNameAnswerCount = w.OptInt32("offNameAnswerCount") ?? 0,
            ResponseCode = w.OptUInt16("responseCode") ?? 0,
            IsAuthoritative = w.OptBool("isAuthoritative") ?? false,
            IsTruncated = w.OptBool("isTruncated") ?? false,
            RecursionAvailable = w.OptBool("recursionAvailable") ?? false,
            AuthenticatedData = w.OptBool("authenticatedData") ?? false,
            ErrorMessage = w.OptString("errorMessage"),
            ResponseByteLength = w.OptInt32("responseByteLength"),
        };
        // Swift-only stored properties (DNSResolver.swift:172-194). Type-checked, then carried by
        // nothing — see UnrepresentedMembers for why that is accepted rather than refused.
        w.AcceptUnrepresentedInt("offClassAnswerCount", "conflictingCNAMEOwnerCount",
                                 "spoofedPacketCount", "attemptCount");
        w.Done();
        return r;
    }

    private static DNSRecord DecodeDnsRecord(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new DNSRecord
        {
            Name = w.String("name"),
            Type = w.UInt16("type"),
            Ttl = w.UInt32("ttl"),
            Rdata = w.String("rdata"),
        };
        w.Done();
        return r;
    }

    private static EDNSResponseInfo DecodeEdnsResponseInfo(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new EDNSResponseInfo(
            UdpPayloadSize: w.UInt16("udpPayloadSize"),
            Version: w.Byte("version"),
            ExtendedRCode: w.Byte("extendedRCode"),
            EcsScopePrefixLength: w.OptByte("ecsScopePrefixLength"));
        w.Done();
        return r;
    }

    private static EvidenceItem DecodeEvidenceItem(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new EvidenceItem(
            Section: w.Enum("section", SectionWire),
            EvidenceId: w.Enum("evidenceId", EvidenceIdWire),
            Observation: w.String("observation"),
            Rationale: w.String("rationale"),
            Source: w.OptString("source"));
        w.Done();
        return r;
    }

    private static DNSCanaries.CanaryResult DecodeCanaryResult(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new DNSCanaries.CanaryResult(
            Section: w.Enum("section", SectionWire),
            Status: w.Enum("status", SectionStatusWire),
            Evidence: w.OptObject("evidence", DecodeEvidenceItem),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static ResolverQueryOutcome DecodeResolverQueryOutcome(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ResolverQueryOutcome
        {
            Records = w.Array("records", DecodeDnsRecord).ToList(),
            Status = w.Enum("status", DnsResponseStatusWire),
            ErrorMessage = w.OptString("errorDescription"),
        };
        // `errorMessage` is the pre-B251 Windows spelling kept as a set-only read alias
        // (VantageComparer.cs:87-91). It has no Swift counterpart, so a vector using it would load
        // on one engine only.
        w.RefusePlatformOnly("errorMessage");
        w.Done();
        return r;
    }

    private static ComparisonResult DecodeComparisonResult(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ComparisonResult
        {
            LocalServer = w.String("localServer"),
            LocalRecords = w.Array("localRecords", DecodeDnsRecord).ToList(),
            PublicResolverResults = w.Map("publicResolverResults",
                (node, ff, pp) => RequireArray(node, ff, pp)
                    .Select((x, i) => DecodeDnsRecord(x, ff, $"{pp}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                    .ToList()),
            LocalOutcome = w.Object("localOutcome", DecodeResolverQueryOutcome),
            PublicResolverOutcomes = w.Map("publicResolverOutcomes", DecodeResolverQueryOutcome),
            DohOutcomes = w.OptMap("dohOutcomes", DecodeResolverQueryOutcome),
        };
        // The three booleans are the macOS wire form (VantageComparer.swift:42-44) and are
        // write-only-when-true shims on the C# side (VantageComparer.cs:39-56). Reading them in a
        // fixed order would let a payload asserting two of them silently keep whichever came last,
        // so the count is checked instead and more than one is refused.
        bool redirection = w.Bool("isRedirectionDetected");
        bool inconclusive = w.Bool("isInconclusive");
        bool degraded = w.Bool("isDegraded");
        int asserted = (redirection ? 1 : 0) + (inconclusive ? 1 : 0) + (degraded ? 1 : 0);
        if (asserted > 1)
        {
            throw new ParityVectorException(
                $"case {f}: sectionInputs key '{p}' could not be decoded — " +
                "isRedirectionDetected / isInconclusive / isDegraded are mutually exclusive " +
                $"(VantageComparer.cs:29-37) and {asserted.ToString(CultureInfo.InvariantCulture)} of " +
                "them are true; the result would depend on assignment order");
        }
        r.Result = redirection ? VantageComparisonResult.RedirectionDetected
            : inconclusive ? VantageComparisonResult.Inconclusive
            : degraded ? VantageComparisonResult.Degraded
            : VantageComparisonResult.Consistent;
        // `result` is the legacy C# enum spelling accepted on read for pre-alignment files. It has
        // no Swift counterpart and would let a vector contradict the three booleans.
        w.RefusePlatformOnly("result");
        w.Done();
        return r;
    }

    private static ScriptHashCheck DecodeScriptHashCheck(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ScriptHashCheck
        {
            LocalHash = w.String("localHash"),
            RemoteHash = w.String("remoteHash"),
            IsMatch = w.Bool("isMatch"),
        };
        w.Done();
        return r;
    }

    private static WarpResult DecodeWarpResult(JsonNode? n, string f, string p)
    {
        // The retired display shim (ContentWarp.cs:25). It is in the shared 32-field set and is
        // therefore decoded, but Synthesizer.Conclude never reads it — a vector populating `warp`
        // exercises this loader and nothing else, and cannot change a conclusion.
        var w = new Walker(n, f, p);
        var r = new WarpResult
        {
            TargetURL = w.String("targetURL"),
            LocalHtml = w.String("localHTML"),
            RemoteHtml = w.String("remoteHTML"),
            LocalScriptURLs = w.StringArray("localScriptURLs").ToList(),
            RemoteScriptURLs = w.StringArray("remoteScriptURLs").ToList(),
            IsScriptStructureIdentical = w.Bool("isScriptStructureIdentical"),
            LocalStylesheetURLs = w.StringArray("localStylesheetURLs").ToList(),
            RemoteStylesheetURLs = w.StringArray("remoteStylesheetURLs").ToList(),
            IsStylesheetStructureIdentical = w.Bool("isStylesheetStructureIdentical"),
            LocalIframeURLs = w.StringArray("localIframeURLs").ToList(),
            RemoteIframeURLs = w.StringArray("remoteIframeURLs").ToList(),
            IsIframeStructureIdentical = w.Bool("isIframeStructureIdentical"),
            ScriptHashes = w.Map("scriptHashes", DecodeScriptHashCheck)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            MismatchedScriptHashes = w.StringArray("mismatchedScriptHashes").ToList(),
            LocalInlineScriptHashes = w.StringArray("localInlineScriptHashes").ToList(),
            RemoteInlineScriptHashes = w.StringArray("remoteInlineScriptHashes").ToList(),
            IsInlineScriptIdentical = w.Bool("isInlineScriptIdentical"),
            IsSecure = w.Bool("isSecure"),
            IsInconclusive = w.Bool("isInconclusive"),
            InconclusiveReason = w.OptString("inconclusiveReason"),
        };
        w.Done();
        return r;
    }

    private static WarpAssetResult DecodeWarpAssetResult(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new WarpAssetResult
        {
            Url = w.String("url"),
            DeclaredSha256 = w.OptString("declaredSha256"),
            FetchedSha256 = w.OptString("fetchedSha256"),
            ByteLen = w.OptInt32("byteLen"),
            State = w.Enum("state", WarpContentStateWire),
            AssetClass = w.String("assetClass"),
            FetchedBytes = w.OptByteArray("fetchedBytes"),
        };
        w.Done();
        return r;
    }

    private static WarpManifestResult DecodeWarpManifestResult(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new WarpManifestResult
        {
            OriginUrl = w.String("originURL"),
            FinalUrl = w.OptString("finalURL"),
            KeyId = w.OptString("keyId"),
            SignatureState = w.Enum("signatureState", WarpSignatureStateWire),
            ManifestState = w.Enum("manifestState", WarpManifestStateWire),
            FreshnessState = w.Enum("freshnessState", WarpFreshnessStateWire),
            HtmlState = w.Enum("htmlState", WarpContentStateWire),
            DeclaredHtmlSha256 = w.OptString("declaredHtmlSha256"),
            FetchedHtmlSha256 = w.OptString("fetchedHtmlSha256"),
            Assets = w.Array("assets", DecodeWarpAssetResult).ToList(),
            Conclusion = w.Enum("conclusion", WarpConclusionWire),
            InconclusiveReason = w.OptString("inconclusiveReason"),
            RawManifestJson = w.OptString("rawManifestJSON"),
            LocalHtmlBytes = w.OptByteArray("localHtmlBytes"),
        };
        w.Done();
        return r;
    }

    private static VPNStatus DecodeVpnStatus(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new VPNStatus
        {
            State = w.Enum("state", ConditionStateWire),
            TunnelInterfaces = w.StringArray("tunnelInterfaces").ToList(),
            Evidence = w.String("evidence"),
        };
        w.Done();
        return r;
    }

    private static CaptivePortalStatus DecodeCaptivePortalStatus(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new CaptivePortalStatus
        {
            State = w.Enum("state", ConditionStateWire),
            Evidence = w.String("evidence"),
        };
        w.Done();
        return r;
    }

    private static EncryptedDNSStatus DecodeEncryptedDnsStatus(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new EncryptedDNSStatus
        {
            State = w.Enum("state", ConditionStateWire),
            Evidence = w.String("evidence"),
            ConfiguredTransport = w.OptEnum("configuredTransport", DnsProtocolWire),
        };
        w.Done();
        return r;
    }

    private static ScopedResolver DecodeScopedResolver(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ScopedResolver
        {
            Address = w.String("address"),
            Interface = w.OptString("interface"),
            MatchDomains = w.StringArray("matchDomains").ToList(),
            IsScoped = w.Bool("isScoped"),
            Protocol = w.OptEnum("dnsProtocol", DnsProtocolWire),
            Port = w.OptUInt16("port"),
        };
        // Swift-only (NetworkKeyState.swift:67-77), required there, unrepresentable here.
        w.AcceptUnrepresentedBool("isSupplemental", "isMDNS", "isReachable");
        // C#-only: D100's readable-configuration tri-state (NetworkKeyState.cs:139) has no Swift
        // counterpart, so a vector setting it would be dropped by the twin.
        w.RefusePlatformOnly("doHReadState");
        w.Done();
        return r;
    }

    private static GatewayInfo DecodeGatewayInfo(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new GatewayInfo
        {
            Ip = w.String("ip"),
            IsDNS = w.Bool("isDNS"),
            AdminReachable = w.Enum("adminReachable", ConditionStateWire),
            DhcpPushedDNS = w.StringArray("dhcpPushedDNS").ToList(),
            Detail = w.String("detail"),
        };
        w.Done();
        return r;
    }

    private static NetworkStateReport DecodeNetworkStateReport(JsonNode? n, string f, string p)
    {
        // Every member is required, matching the Swift model's non-optional `let`s. That is what
        // closes the authoring hazard the C# model carries: NetworkStateReport pre-initialises
        // Vpn = No and CaptivePortal / EncryptedDNS = Unknown (NetworkKeyState.cs:151-155), so a
        // partial payload would otherwise inherit a posture nothing measured.
        var w = new Walker(n, f, p);
        var r = new NetworkStateReport
        {
            DnsServers = w.StringArray("dnsServers").ToList(),
            ActiveInterfaces = w.StringArray("activeInterfaces").ToList(),
            VpnActive = w.Bool("vpnActive"),
            CaptivePortalDetected = w.Bool("captivePortalDetected"),
            Vpn = w.Object("vpn", DecodeVpnStatus),
            CaptivePortal = w.Object("captivePortal", DecodeCaptivePortalStatus),
            EncryptedDNS = w.Object("encryptedDNS", DecodeEncryptedDnsStatus),
            ScopedResolvers = w.Array("scopedResolvers", DecodeScopedResolver).ToList(),
            Gateway = w.OptObject("gateway", DecodeGatewayInfo),
        };
        w.Done();
        return r;
    }

    private static CollectorFailure DecodeCollectorFailure(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var kind = w.Enum("kind", CollectorFailureKindWire);
        var status = w.OptInt32("status");
        w.Done();
        // The five HTTP-shaped Swift cases carry an associated `status:`; the other three have no
        // status to carry (ParityVectorLoader.swift:489-506). Mirrored exactly so a vector cannot
        // load on one engine and not the other.
        bool needsStatus = kind is CollectorFailureKind.EntitlementRequired
            or CollectorFailureKind.AuthenticationFailed
            or CollectorFailureKind.RateLimited
            or CollectorFailureKind.ServerError
            or CollectorFailureKind.UnexpectedStatus;
        if (needsStatus && status is null)
        {
            throw new ParityVectorException(
                $"case {f}: sectionInputs key '{p}' could not be decoded — collectorFailure kind " +
                $"'{CollectorFailureKindWire.First(kv => kv.Value == kind).Key}' requires a 'status' member");
        }
        return new CollectorFailure(kind, status);
    }

    private static LeakTestResult DecodeLeakTestResult(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new LeakTestResult
        {
            Result = w.Enum("result", LeakResultWire),
            ObservedResolvers = w.StringArray("observedResolvers").ToList(),
            Signature = w.OptString("signature"),
            PublicKey = w.OptString("publicKey"),
            KeyId = w.OptString("keyId"),
            SignedPayload = w.OptString("signedPayload"),
            SignatureValid = w.OptBool("signatureValid"),
            ErrorReason = w.OptString("errorReason"),
            TransportPinning = w.Enum("transportPinning", CollectorTransportPinningWire),
            SignatureKeyTrusted = w.OptBool("signatureKeyTrusted"),
            CollectorFailure = w.OptObject("collectorFailure", DecodeCollectorFailure),
        };
        // E049 added SignatureVerification to the C# model only; Swift's LeakTestResult
        // (LeakDetector.swift:153-192) has no counterpart, so a vector setting it would be carried
        // on one engine and dropped on the other.
        w.RefusePlatformOnly("signatureVerification");
        w.Done();
        return r;
    }

    private static ExfilAssessment DecodeExfilAssessment(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ExfilAssessment(
            domain: w.String("domain"),
            longestLabel: w.String("longestLabel"),
            labelLength: w.Int32("labelLength"),
            entropy: w.Double("entropyBitsPerChar"),
            beacon: w.Enum("beacon", BeaconClassificationWire),
            beaconEvaluated: w.Bool("beaconEvaluated"),
            flaggedByLists: w.StringArray("flaggedByLists").ToList(),
            suspicion: w.Enum("suspicion", ExfilSuspicionWire),
            rationale: w.StringArray("rationale").ToList());
        // C082 added `listsEvaluated` to the Swift model only (ExfilAnalyzer.swift:32), where it is
        // a non-optional `let` and therefore REQUIRED by the Swift loader. C# has no such member
        // (Forensics.cs:300-320). Refusing it would make every Swift-valid `exfil` payload
        // unloadable here, so it is accepted, type-checked, and listed in UnrepresentedMembers.
        w.AcceptUnrepresentedBool("listsEvaluated");
        w.Done();
        return r;
    }

    private static ConfigChange DecodeConfigChange(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var kind = w.Enum("kind", ConfigChangeKindWire);
        var detail = w.String("detail");
        w.Done();
        return new ConfigChange(kind, detail);
    }

    private static GroundTruthAuthoritative DecodeGroundTruthAuthoritative(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new GroundTruthAuthoritative
        {
            Answers = w.StringArray("answers").ToList(),
            Rcode = w.Int32("rcode"),
            Signed = w.OptBool("signed"),
        };
        w.Done();
        return r;
    }

    private static GroundTruth DecodeGroundTruth(JsonNode? n, string f, string p)
    {
        // WIRE names, not member names: the server contract renames domain → "name" and
        // result → "conclusion" on both platforms (DimensionClient.cs:17-24,
        // DimensionClient.swift:35-39). This is the most likely place for the twins to disagree, so
        // it is spelled out rather than derived.
        var w = new Walker(n, f, p);
        var r = new GroundTruth
        {
            Domain = w.String("name"),
            Type = w.String("type"),
            Result = w.String("conclusion"),
            Authoritative = w.OptObject("authoritative", DecodeGroundTruthAuthoritative),
        };
        w.Done();
        return r;
    }

    // -- Workstream H probe records. Every member is required: each is a non-optional Swift `let`.

    private static ResolverLatency DecodeResolverLatency(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ResolverLatency(
            Status: w.Enum("status", ResolverLatencyStatusWire),
            SampleCount: w.Int32("sampleCount"),
            SuccessCount: w.Int32("successCount"),
            MinMs: w.Double("minMs"),
            MedianMs: w.Double("medianMs"),
            MaxMs: w.Double("maxMs"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static AnswerStability DecodeAnswerStability(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new AnswerStability(
            Status: w.Enum("status", AnswerStabilityStatusWire),
            SampleCount: w.Int32("sampleCount"),
            UsableCount: w.Int32("usableCount"),
            DistinctAnswerSets: w.Int32("distinctAnswerSets"),
            TtlHonored: w.Bool("ttlHonored"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static WildcardSynthesis DecodeWildcardSynthesis(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new WildcardSynthesis(
            Status: w.Enum("status", WildcardSynthesisStatusWire),
            ProbedLabels: w.StringArray("probedLabels").ToList(),
            SyntheticAddresses: w.StringArray("syntheticAddresses").ToList(),
            UsableCount: w.Int32("usableCount"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static DenialOfExistence DecodeDenialOfExistence(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new DenialOfExistence(
            Status: w.Enum("status", DenialStatusWire),
            QueriedName: w.String("queriedName"),
            SoaOwner: w.OptString("soaOwner"),
            HasNSEC: w.Bool("hasNSEC"),
            NegativeKind: w.OptEnum("negativeKind", DenialNegativeKindWire),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static DualStackAudit DecodeDualStackAudit(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new DualStackAudit(
            Status: w.Enum("status", DualStackStatusWire),
            AAddresses: w.StringArray("aAddresses").ToList(),
            AaaaAddresses: w.StringArray("aaaaAddresses").ToList(),
            Dns64Count: w.Int32("dns64Count"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static EDNS0Capability DecodeEdns0Capability(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new EDNS0Capability(
            Status: w.Enum("status", Edns0CapabilityStatusWire),
            UdpPayloadSize: w.OptUInt16("udpPayloadSize"),
            Version: w.OptByte("version"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static VersionBindResult DecodeVersionBindResult(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new VersionBindResult(
            Status: w.Enum("status", VersionBindResultStatusWire),
            Banner: w.OptString("banner"),
            Server: w.String("server"),
            ForwarderOnPublicPath: w.Bool("forwarderOnPublicPath"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static StubEntropyAudit DecodeStubEntropyAudit(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new StubEntropyAudit(
            Status: w.Enum("status", StubEntropyStatusWire),
            SampleCount: w.Int32("sampleCount"),
            DistinctSourcePorts: w.Int32("distinctSourcePorts"),
            DistinctTransactionIDs: w.Int32("distinctTransactionIDs"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static AuthoritativeComparison DecodeAuthoritativeComparison(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new AuthoritativeComparison(
            Status: w.Enum("status", AuthComparisonStatusWire),
            AuthoritativeAnswers: w.StringArray("authoritativeAnswers").ToList(),
            ResolverAnswers: w.StringArray("resolverAnswers").ToList(),
            Nameservers: w.StringArray("nameservers").ToList(),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static SVCBFinding DecodeSvcbFinding(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new SVCBFinding(
            Status: w.Enum("status", SvcbFindingStatusWire),
            PresentAt: w.StringArray("presentAt").ToList(),
            AbsentAt: w.StringArray("absentAt").ToList(),
            FailedAt: w.StringArray("failedAt").ToList(),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static TTLAnomaly DecodeTtlAnomaly(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new TTLAnomaly(
            Status: w.Enum("status", TtlAnomalyStatusWire),
            TtlsByResolver: w.Map("ttlsByResolver", (node, ff, pp) => RequireUInt32(node, ff, pp))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static CNAMEChainAudit DecodeCnameChainAudit(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new CNAMEChainAudit(
            Status: w.Enum("status", CnameChainStatusWire),
            TestChain: w.StringArray("testChain").ToList(),
            ReferenceChains: w.Array("referenceChains",
                    (node, ff, pp) => (IReadOnlyList<string>)RequireStringArray(node, ff, pp).ToList())
                .ToList(),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static EncryptedTransport DecodeEncryptedTransport(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new EncryptedTransport(
            Status: w.Enum("status", EncryptedTransportStatusWire),
            Endpoint: w.String("endpoint"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static OpenResolverFinding DecodeOpenResolverFinding(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new OpenResolverFinding(
            Status: w.Enum("status", OpenResolverStatusWire),
            Target: w.String("target"),
            Recursive: w.Bool("recursive"),
            AmplificationFactor: w.Double("amplificationFactor"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static ECSLeak DecodeEcsLeak(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ECSLeak(
            Status: w.Enum("status", EcsLeakStatusWire),
            ScopePrefixLength: w.OptByte("scopePrefixLength"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    private static ResponseSizeAudit DecodeResponseSizeAudit(JsonNode? n, string f, string p)
    {
        var w = new Walker(n, f, p);
        var r = new ResponseSizeAudit(
            Status: w.Enum("status", ResponseSizeStatusWire),
            SampleCount: w.Int32("sampleCount"),
            MaxBytes: w.Int32("maxBytes"),
            OversizedCount: w.Int32("oversizedCount"),
            Detail: w.String("detail"));
        w.Done();
        return r;
    }

    // MARK: - Enum wire tables
    //
    // Every table is the SWIFT rawValue spelling, because that is what the contract names. They are
    // written out (or derived from an existing pinned mapping) rather than produced by a naming
    // heuristic, for the reason WitnessWireJson.cs:50-54 already gives: the mapping is a wire
    // contract with another implementation, and a heuristic would silently re-spell a member if the
    // CLR identifier were ever renamed. Three of these are NOT first-char-lowercased member names —
    // DNSProtocol, BeaconClassification, ExfilSuspicion and ConfigChangeKind carry display strings —
    // which is exactly why no heuristic is used.

    private static readonly IReadOnlyDictionary<string, DNSResponseStatus> DnsResponseStatusWire =
        DNSResponseStatusJsonConverter.WireNames.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, DNSSECStatus> DnssecStatusWire =
        Enum.GetValues<DNSSECStatus>().ToDictionary(v => v.RawValue(), v => v, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, EvidenceId> EvidenceIdWire =
        Enum.GetValues<EvidenceId>().ToDictionary(v => v.RawValue(), v => v, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, AuthComparisonStatus> AuthComparisonStatusWire =
        Enum.GetValues<AuthComparisonStatus>().ToDictionary(v => v.RawValue(), v => v, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Section> SectionWire =
        new Dictionary<string, Section>(StringComparer.Ordinal)
        {
            ["prophet"] = Section.Prophet, ["mirror"] = Section.Mirror, ["portal"] = Section.Portal,
            ["glutton"] = Section.Glutton, ["cage"] = Section.Cage, ["witness"] = Section.Witness,
        };

    private static readonly IReadOnlyDictionary<string, SectionStatus> SectionStatusWire =
        new Dictionary<string, SectionStatus>(StringComparer.Ordinal)
        {
            ["untested"] = SectionStatus.Untested, ["invalidated"] = SectionStatus.Invalidated,
            ["inconclusive"] = SectionStatus.Inconclusive, ["degraded"] = SectionStatus.Degraded,
            ["positive"] = SectionStatus.Positive, ["negative"] = SectionStatus.Negative,
        };

    private static readonly IReadOnlyDictionary<string, ConditionState> ConditionStateWire =
        new Dictionary<string, ConditionState>(StringComparer.Ordinal)
        {
            ["yes"] = ConditionState.Yes, ["no"] = ConditionState.No, ["unknown"] = ConditionState.Unknown,
        };

    /// Swift <c>DNSProtocol</c> raw values are "DoH"/"DoT" (NetworkKeyState.swift:54-57) — NOT the
    /// lowercased member name.
    private static readonly IReadOnlyDictionary<string, DnsProtocol> DnsProtocolWire =
        new Dictionary<string, DnsProtocol>(StringComparer.Ordinal)
        {
            ["DoH"] = DnsProtocol.Doh, ["DoT"] = DnsProtocol.Dot,
            ["DoQ"] = DnsProtocol.Doq, ["DNSCrypt"] = DnsProtocol.DnsCrypt,
        };

    private static readonly IReadOnlyDictionary<string, LeakResult> LeakResultWire =
        new Dictionary<string, LeakResult>(StringComparer.Ordinal)
        {
            ["confirmed"] = LeakResult.Confirmed, ["notDetected"] = LeakResult.NotDetected,
            ["inconclusive"] = LeakResult.Inconclusive, ["invalid"] = LeakResult.Invalid,
            ["error"] = LeakResult.Error,
        };

    private static readonly IReadOnlyDictionary<string, CollectorTransportPinning> CollectorTransportPinningWire =
        new Dictionary<string, CollectorTransportPinning>(StringComparer.Ordinal)
        {
            ["enforced"] = CollectorTransportPinning.Enforced,
            ["notConfigured"] = CollectorTransportPinning.NotConfigured,
            ["failed"] = CollectorTransportPinning.Failed,
        };

    private static readonly IReadOnlyDictionary<string, CollectorFailureKind> CollectorFailureKindWire =
        new Dictionary<string, CollectorFailureKind>(StringComparer.Ordinal)
        {
            ["entitlementRequired"] = CollectorFailureKind.EntitlementRequired,
            ["authenticationFailed"] = CollectorFailureKind.AuthenticationFailed,
            ["rateLimited"] = CollectorFailureKind.RateLimited,
            ["serverError"] = CollectorFailureKind.ServerError,
            ["unexpectedStatus"] = CollectorFailureKind.UnexpectedStatus,
            ["malformedResponse"] = CollectorFailureKind.MalformedResponse,
            ["collectorIdentityUnverified"] = CollectorFailureKind.CollectorIdentityUnverified,
            ["transportFailure"] = CollectorFailureKind.TransportFailure,
        };

    /// Swift <c>BeaconClassification</c> raw values are DISPLAY strings (BeaconAnalyzer.swift:18-23).
    private static readonly IReadOnlyDictionary<string, BeaconClassification> BeaconClassificationWire =
        new Dictionary<string, BeaconClassification>(StringComparer.Ordinal)
        {
            ["Insufficient data"] = BeaconClassification.InsufficientData,
            ["Highly regular"] = BeaconClassification.HighlyRegular,
            ["Regular"] = BeaconClassification.Regular,
            ["Irregular"] = BeaconClassification.Irregular,
        };

    /// Swift <c>ExfilSuspicion</c> raw values are DISPLAY strings (ExfilAnalyzer.swift:9-14).
    private static readonly IReadOnlyDictionary<string, ExfilSuspicion> ExfilSuspicionWire =
        new Dictionary<string, ExfilSuspicion>(StringComparer.Ordinal)
        {
            ["Inconclusive"] = ExfilSuspicion.Inconclusive,
            ["Benign"] = ExfilSuspicion.Benign,
            ["Suspicious"] = ExfilSuspicion.Suspicious,
            ["Likely DNS exfiltration / tunneling"] = ExfilSuspicion.LikelyExfil,
        };

    /// Swift <c>ConfigChangeKind</c> raw values are DISPLAY strings (Sentinel.swift:39-44), matching
    /// the C# <c>ConfigChangeKindExtensions.Label</c> table (Trust.cs:248-254).
    private static readonly IReadOnlyDictionary<string, ConfigChangeKind> ConfigChangeKindWire =
        new Dictionary<string, ConfigChangeKind>(StringComparer.Ordinal)
        {
            ["Resolvers changed"] = ConfigChangeKind.ResolversChanged,
            ["Active interfaces changed"] = ConfigChangeKind.InterfacesChanged,
            ["VPN state changed"] = ConfigChangeKind.VpnChanged,
            ["Encrypted-DNS state changed"] = ConfigChangeKind.EncryptedDNSChanged,
        };

    private static readonly IReadOnlyDictionary<string, WarpSignatureState> WarpSignatureStateWire =
        new Dictionary<string, WarpSignatureState>(StringComparer.Ordinal)
        {
            ["verified"] = WarpSignatureState.Verified,
            ["untrustedSigner"] = WarpSignatureState.UntrustedSigner,
            ["invalidSignature"] = WarpSignatureState.InvalidSignature,
            ["malformed"] = WarpSignatureState.Malformed,
        };

    private static readonly IReadOnlyDictionary<string, WarpManifestState> WarpManifestStateWire =
        new Dictionary<string, WarpManifestState>(StringComparer.Ordinal)
        {
            ["parsed"] = WarpManifestState.Parsed, ["malformed"] = WarpManifestState.Malformed,
            ["notEvaluated"] = WarpManifestState.NotEvaluated,
        };

    private static readonly IReadOnlyDictionary<string, WarpFreshnessState> WarpFreshnessStateWire =
        new Dictionary<string, WarpFreshnessState>(StringComparer.Ordinal)
        {
            ["fresh"] = WarpFreshnessState.Fresh, ["expired"] = WarpFreshnessState.Expired,
            ["futureDated"] = WarpFreshnessState.FutureDated,
            ["originMismatch"] = WarpFreshnessState.OriginMismatch,
            ["replayNonceMismatch"] = WarpFreshnessState.ReplayNonceMismatch,
            ["notEvaluated"] = WarpFreshnessState.NotEvaluated,
        };

    private static readonly IReadOnlyDictionary<string, WarpContentState> WarpContentStateWire =
        new Dictionary<string, WarpContentState>(StringComparer.Ordinal)
        {
            ["match"] = WarpContentState.Match, ["mismatch"] = WarpContentState.Mismatch,
            ["fetchError"] = WarpContentState.FetchError,
            ["fetchFailedDeclared"] = WarpContentState.FetchFailedDeclared,
            ["notEvaluated"] = WarpContentState.NotEvaluated,
        };

    /// Swift <c>WarpConclusion</c> raw values are the SCREAMING_SNAKE protocol tokens
    /// (WarpEngine.swift:57-61), which the C# member names already spell.
    private static readonly IReadOnlyDictionary<string, WarpConclusion> WarpConclusionWire =
        new Dictionary<string, WarpConclusion>(StringComparer.Ordinal)
        {
            ["TAMPERING_DETECTED"] = WarpConclusion.TAMPERING_DETECTED,
            ["INCONCLUSIVE"] = WarpConclusion.INCONCLUSIVE,
            ["VERIFIED_MATCH"] = WarpConclusion.VERIFIED_MATCH,
        };

    private static readonly IReadOnlyDictionary<string, ResolverLatencyStatus> ResolverLatencyStatusWire =
        new Dictionary<string, ResolverLatencyStatus>(StringComparer.Ordinal)
        {
            ["fast"] = ResolverLatencyStatus.Fast, ["moderate"] = ResolverLatencyStatus.Moderate,
            ["slow"] = ResolverLatencyStatus.Slow, ["degraded"] = ResolverLatencyStatus.Degraded,
            ["unreachable"] = ResolverLatencyStatus.Unreachable,
            ["untested"] = ResolverLatencyStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, AnswerStabilityStatus> AnswerStabilityStatusWire =
        new Dictionary<string, AnswerStabilityStatus>(StringComparer.Ordinal)
        {
            ["stable"] = AnswerStabilityStatus.Stable, ["rotating"] = AnswerStabilityStatus.Rotating,
            ["unstable"] = AnswerStabilityStatus.Unstable,
            ["ttlAnomaly"] = AnswerStabilityStatus.TtlAnomaly,
            ["inconclusive"] = AnswerStabilityStatus.Inconclusive,
            ["untested"] = AnswerStabilityStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, WildcardSynthesisStatus> WildcardSynthesisStatusWire =
        new Dictionary<string, WildcardSynthesisStatus>(StringComparer.Ordinal)
        {
            ["noSynthesis"] = WildcardSynthesisStatus.NoSynthesis,
            ["wildcardZone"] = WildcardSynthesisStatus.WildcardZone,
            ["nxHijack"] = WildcardSynthesisStatus.NxHijack,
            ["inconclusive"] = WildcardSynthesisStatus.Inconclusive,
            ["untested"] = WildcardSynthesisStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, DenialStatus> DenialStatusWire =
        new Dictionary<string, DenialStatus>(StringComparer.Ordinal)
        {
            ["notADenial"] = DenialStatus.NotADenial, ["nsecProven"] = DenialStatus.NsecProven,
            ["soaBacked"] = DenialStatus.SoaBacked, ["bareDenial"] = DenialStatus.BareDenial,
            ["contradictory"] = DenialStatus.Contradictory,
            ["inconclusive"] = DenialStatus.Inconclusive, ["untested"] = DenialStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, DenialNegativeKind> DenialNegativeKindWire =
        new Dictionary<string, DenialNegativeKind>(StringComparer.Ordinal)
        {
            ["nameDoesNotExist"] = DenialNegativeKind.NameDoesNotExist,
            ["typeHasNoData"] = DenialNegativeKind.TypeHasNoData,
        };

    private static readonly IReadOnlyDictionary<string, DualStackStatus> DualStackStatusWire =
        new Dictionary<string, DualStackStatus>(StringComparer.Ordinal)
        {
            ["dualStack"] = DualStackStatus.DualStack, ["ipv4Only"] = DualStackStatus.Ipv4Only,
            ["dns64Synthesis"] = DualStackStatus.Dns64Synthesis,
            ["ipv4Suppressed"] = DualStackStatus.Ipv4Suppressed,
            ["inconclusive"] = DualStackStatus.Inconclusive, ["untested"] = DualStackStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, EDNS0CapabilityStatus> Edns0CapabilityStatusWire =
        new Dictionary<string, EDNS0CapabilityStatus>(StringComparer.Ordinal)
        {
            ["supported"] = EDNS0CapabilityStatus.Supported,
            ["notSupported"] = EDNS0CapabilityStatus.NotSupported,
            ["broken"] = EDNS0CapabilityStatus.Broken,
            ["inconclusive"] = EDNS0CapabilityStatus.Inconclusive,
            ["untested"] = EDNS0CapabilityStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, VersionBindResultStatus> VersionBindResultStatusWire =
        new Dictionary<string, VersionBindResultStatus>(StringComparer.Ordinal)
        {
            ["disclosed"] = VersionBindResultStatus.Disclosed,
            ["withheld"] = VersionBindResultStatus.Withheld,
            ["untested"] = VersionBindResultStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, StubEntropyStatus> StubEntropyStatusWire =
        new Dictionary<string, StubEntropyStatus>(StringComparer.Ordinal)
        {
            ["wellRandomized"] = StubEntropyStatus.WellRandomized,
            ["weakRandomization"] = StubEntropyStatus.WeakRandomization,
            ["insufficientData"] = StubEntropyStatus.InsufficientData,
            ["untested"] = StubEntropyStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, SVCBFindingStatus> SvcbFindingStatusWire =
        new Dictionary<string, SVCBFindingStatus>(StringComparer.Ordinal)
        {
            ["present"] = SVCBFindingStatus.Present, ["absent"] = SVCBFindingStatus.Absent,
            ["divergent"] = SVCBFindingStatus.Divergent,
            ["inconclusive"] = SVCBFindingStatus.Inconclusive,
            ["untested"] = SVCBFindingStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, TTLAnomalyStatus> TtlAnomalyStatusWire =
        new Dictionary<string, TTLAnomalyStatus>(StringComparer.Ordinal)
        {
            ["independentCaching"] = TTLAnomalyStatus.IndependentCaching,
            ["lockstepTTL"] = TTLAnomalyStatus.LockstepTTL,
            ["extremeTTL"] = TTLAnomalyStatus.ExtremeTTL,
            ["inconclusive"] = TTLAnomalyStatus.Inconclusive,
            ["untested"] = TTLAnomalyStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, CNAMEChainStatus> CnameChainStatusWire =
        new Dictionary<string, CNAMEChainStatus>(StringComparer.Ordinal)
        {
            ["consistent"] = CNAMEChainStatus.Consistent, ["noCNAME"] = CNAMEChainStatus.NoCNAME,
            ["divergent"] = CNAMEChainStatus.Divergent,
            ["inconclusive"] = CNAMEChainStatus.Inconclusive,
            ["untested"] = CNAMEChainStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, EncryptedTransportStatus> EncryptedTransportStatusWire =
        new Dictionary<string, EncryptedTransportStatus>(StringComparer.Ordinal)
        {
            ["working"] = EncryptedTransportStatus.Working, ["blocked"] = EncryptedTransportStatus.Blocked,
            ["inconclusive"] = EncryptedTransportStatus.Inconclusive,
            ["untested"] = EncryptedTransportStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, OpenResolverStatus> OpenResolverStatusWire =
        new Dictionary<string, OpenResolverStatus>(StringComparer.Ordinal)
        {
            ["notRecursive"] = OpenResolverStatus.NotRecursive,
            ["recursiveLowAmp"] = OpenResolverStatus.RecursiveLowAmp,
            ["recursiveHighAmp"] = OpenResolverStatus.RecursiveHighAmp,
            ["untested"] = OpenResolverStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, ECSLeakStatus> EcsLeakStatusWire =
        new Dictionary<string, ECSLeakStatus>(StringComparer.Ordinal)
        {
            ["forwarded"] = ECSLeakStatus.Forwarded, ["notForwarded"] = ECSLeakStatus.NotForwarded,
            ["inconclusive"] = ECSLeakStatus.Inconclusive, ["untested"] = ECSLeakStatus.Untested,
        };

    private static readonly IReadOnlyDictionary<string, ResponseSizeStatus> ResponseSizeStatusWire =
        new Dictionary<string, ResponseSizeStatus>(StringComparer.Ordinal)
        {
            ["normal"] = ResponseSizeStatus.Normal, ["oversized"] = ResponseSizeStatus.Oversized,
            ["insufficientData"] = ResponseSizeStatus.InsufficientData,
            ["untested"] = ResponseSizeStatus.Untested,
        };

    // MARK: - Primitive readers

    private static ParityVectorException Bad(string f, string p, string reason) =>
        new($"case {f}: sectionInputs key '{p}' could not be decoded — {reason}");

    private static bool TryGetInt64(JsonNode? n, out long value)
    {
        value = 0;
        if (n is null || n.GetValueKind() != JsonValueKind.Number) return false;
        return n.AsValue().TryGetValue(out value);
    }

    private static JsonObject RequireObject(JsonNode? n, string f, string p) =>
        n as JsonObject ?? throw Bad(f, p, $"expected an object at {p}");

    private static JsonArray RequireArray(JsonNode? n, string f, string p) =>
        n as JsonArray ?? throw Bad(f, p, $"expected an array at {p}");

    private static string RequireString(JsonNode? n, string f, string p) =>
        n?.GetValueKind() == JsonValueKind.String
            ? n.GetValue<string>()
            : throw Bad(f, p, $"expected a string at {p}");

    private static IEnumerable<string> RequireStringArray(JsonNode? n, string f, string p) =>
        RequireArray(n, f, p)
            .Select((x, i) => RequireString(x, f, $"{p}[{i.ToString(CultureInfo.InvariantCulture)}]"))
            .ToList();

    private static bool RequireBool(JsonNode? n, string f, string p) =>
        n?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? n.GetValue<bool>()
            : throw Bad(f, p, $"expected a boolean at {p}");

    private static double RequireDouble(JsonNode? n, string f, string p)
    {
        if (n?.GetValueKind() != JsonValueKind.Number || !n.AsValue().TryGetValue<double>(out var d))
            throw Bad(f, p, $"expected a number at {p}");
        if (!double.IsFinite(d))
            throw Bad(f, p, $"{p} must be a finite number");
        return d;
    }

    private static long RequireInteger(JsonNode? n, string f, string p, long min, long max)
    {
        if (!TryGetInt64(n, out var v))
            throw Bad(f, p, $"expected an integer at {p}");
        if (v < min || v > max)
        {
            throw Bad(f, p, $"{p} is {v.ToString(CultureInfo.InvariantCulture)}, outside the " +
                            $"representable range {min.ToString(CultureInfo.InvariantCulture)}…" +
                            $"{max.ToString(CultureInfo.InvariantCulture)}");
        }
        return v;
    }

    private static int RequireInt32(JsonNode? n, string f, string p) =>
        (int)RequireInteger(n, f, p, int.MinValue, int.MaxValue);

    private static ushort RequireUInt16(JsonNode? n, string f, string p) =>
        (ushort)RequireInteger(n, f, p, ushort.MinValue, ushort.MaxValue);

    private static uint RequireUInt32(JsonNode? n, string f, string p) =>
        (uint)RequireInteger(n, f, p, uint.MinValue, uint.MaxValue);

    private static byte RequireByte(JsonNode? n, string f, string p) =>
        (byte)RequireInteger(n, f, p, byte.MinValue, byte.MaxValue);

    private static T DecodeEnum<T>(JsonNode? n, string f, string p, IReadOnlyDictionary<string, T> table)
        where T : struct, Enum
    {
        var s = RequireString(n, f, p);
        if (table.TryGetValue(s, out var v)) return v;
        var accepted = string.Join(", ", table.Keys.OrderBy(k => k, StringComparer.Ordinal));
        throw Bad(f, p, $"{p} has unrecognised value '{s}' — expected one of: {accepted}");
    }

    /// <summary>
    /// Reads the members of one JSON object, and refuses anything it was not asked for.
    /// </summary>
    /// <remarks>
    /// The refusal is the point. A reader that ignores what it does not recognise turns an
    /// authoring mistake — a misspelled member, a member that exists on only one platform — into a
    /// vector that loads clean while carrying less evidence than its author wrote. That is the
    /// contract's central rule applied below the top level.
    /// </remarks>
    private sealed class Walker
    {
        private readonly JsonObject _o;
        private readonly string _f;
        private readonly string _p;
        private readonly HashSet<string> _read = new(StringComparer.Ordinal);

        internal Walker(JsonNode? node, string f, string p)
        {
            _o = RequireObject(node, f, p);
            _f = f;
            _p = p;
        }

        private string Path(string member) => $"{_p}.{member}";

        private JsonNode? Required(string member)
        {
            _read.Add(member);
            if (!_o.ContainsKey(member))
                throw Bad(_f, _p, $"missing required member '{member}' at {Path(member)}");
            var n = _o[member];
            if (n is null)
                throw Bad(_f, _p, $"null where a value is required at {Path(member)}");
            return n;
        }

        private JsonNode? Optional(string member)
        {
            _read.Add(member);
            return _o.TryGetPropertyValue(member, out var n) ? n : null;
        }

        internal string String(string m) => RequireString(Required(m), _f, Path(m));
        internal bool Bool(string m) => RequireBool(Required(m), _f, Path(m));
        internal double Double(string m) => RequireDouble(Required(m), _f, Path(m));
        internal int Int32(string m) => RequireInt32(Required(m), _f, Path(m));
        internal ushort UInt16(string m) => RequireUInt16(Required(m), _f, Path(m));
        internal uint UInt32(string m) => RequireUInt32(Required(m), _f, Path(m));
        internal byte Byte(string m) => RequireByte(Required(m), _f, Path(m));

        internal string? OptString(string m) { var n = Optional(m); return n is null ? null : RequireString(n, _f, Path(m)); }
        internal bool? OptBool(string m) { var n = Optional(m); return n is null ? null : RequireBool(n, _f, Path(m)); }
        internal int? OptInt32(string m) { var n = Optional(m); return n is null ? null : RequireInt32(n, _f, Path(m)); }
        internal ushort? OptUInt16(string m) { var n = Optional(m); return n is null ? null : RequireUInt16(n, _f, Path(m)); }
        internal byte? OptByte(string m) { var n = Optional(m); return n is null ? null : RequireByte(n, _f, Path(m)); }

        internal T Enum<T>(string m, IReadOnlyDictionary<string, T> table) where T : struct, System.Enum
            => DecodeEnum(Required(m), _f, Path(m), table);

        internal T? OptEnum<T>(string m, IReadOnlyDictionary<string, T> table) where T : struct, System.Enum
        {
            var n = Optional(m);
            return n is null ? null : DecodeEnum(n, _f, Path(m), table);
        }

        internal IEnumerable<string> StringArray(string m) => RequireStringArray(Required(m), _f, Path(m));

        internal T Object<T>(string m, Func<JsonNode?, string, string, T> decode)
            => decode(Required(m), _f, Path(m));

        internal T? OptObject<T>(string m, Func<JsonNode?, string, string, T> decode) where T : class
        {
            var n = Optional(m);
            return n is null ? null : decode(n, _f, Path(m));
        }

        internal IEnumerable<T> Array<T>(string m, Func<JsonNode?, string, string, T> decode)
            => RequireArray(Required(m), _f, Path(m))
                .Select((x, i) => decode(x, _f, $"{Path(m)}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                .ToList();

        internal IEnumerable<T>? OptArray<T>(string m, Func<JsonNode?, string, string, T> decode)
        {
            var n = Optional(m);
            return n is null
                ? null
                : RequireArray(n, _f, Path(m))
                    .Select((x, i) => decode(x, _f, $"{Path(m)}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                    .ToList();
        }

        internal Dictionary<string, T> Map<T>(string m, Func<JsonNode?, string, string, T> decode)
            => ReadMap(Required(m), m, decode);

        internal Dictionary<string, T>? OptMap<T>(string m, Func<JsonNode?, string, string, T> decode)
        {
            var n = Optional(m);
            return n is null ? null : ReadMap(n, m, decode);
        }

        private Dictionary<string, T> ReadMap<T>(JsonNode? node, string m, Func<JsonNode?, string, string, T> decode)
        {
            var obj = RequireObject(node, _f, Path(m));
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            // Ordinal key order so a decode failure inside the map names the same key on both runs.
            foreach (var key in obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal))
                map[key] = decode(obj[key], _f, $"{Path(m)}.{key}");
            return map;
        }

        /// <summary>Swift's <c>[UInt8]</c> encodes as a JSON array of numbers, not base64.</summary>
        internal byte[]? OptByteArray(string m)
        {
            var n = Optional(m);
            if (n is null) return null;
            return RequireArray(n, _f, Path(m))
                .Select((x, i) => RequireByte(x, _f, $"{Path(m)}[{i.ToString(CultureInfo.InvariantCulture)}]"))
                .ToArray();
        }

        /// <summary>
        /// Accept a member the Swift twin declares and this model cannot hold: check its type so
        /// garbage still fails, then discard it. Every member passed here is listed in
        /// <see cref="UnrepresentedMembers"/>, which is where the reason lives.
        /// </summary>
        internal void AcceptUnrepresentedInt(params string[] members)
        {
            foreach (var m in members) _ = OptInt32(m);
        }

        /// <inheritdoc cref="AcceptUnrepresentedInt"/>
        internal void AcceptUnrepresentedBool(params string[] members)
        {
            foreach (var m in members) _ = OptBool(m);
        }

        /// <summary>
        /// Mark members only THIS model has. Present → refused by name, because the Swift twin's
        /// synthesized decoder would silently ignore them and the vector would mean two different
        /// things. Absent → nothing happens.
        /// </summary>
        internal void RefusePlatformOnly(params string[] members)
        {
            foreach (var m in members)
            {
                _read.Add(m);
                if (_o.ContainsKey(m))
                {
                    throw Bad(_f, _p,
                        $"member '{m}' at {Path(m)} exists on only one of the two engines, so a " +
                        "vector using it cannot be expressed in v1; it is refused rather than dropped");
                }
            }
        }

        /// <summary>Refuse every member nobody claimed. Ordinal-first so the name is deterministic.</summary>
        /// <remarks>B017: replaced O(n log n) LINQ sort with a single-pass scan that tracks the
        /// ordinally smallest unread key — same deterministic error name, no intermediate
        /// allocations, O(n).</remarks>
        internal void Done()
        {
            string? stray = null;
            foreach (var kv in _o)
            {
                if (!_read.Contains(kv.Key))
                {
                    if (stray is null || string.Compare(kv.Key, stray, StringComparison.Ordinal) < 0)
                        stray = kv.Key;
                }
            }
            if (stray is not null)
            {
                throw Bad(_f, _p,
                    $"unknown member '{stray}' at {Path(stray)} — the shared shape has no such " +
                    "member, so it is refused rather than dropped");
            }
        }
    }
}
