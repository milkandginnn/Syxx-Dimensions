using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Syxx.Kit;

namespace Syxx.Commands
{
/// <summary>
/// Emit a canonical behavioral transcript from the production synthesizer.
/// Hidden CI subcommand — not advertised in user help until owner-approved.
/// </summary>
/// <remarks>
/// <para>Two invocations, ONE record builder (<see cref="Record"/>):</para>
/// <list type="bullet">
/// <item><description>no <c>--vectors</c> — one record for an empty <see cref="SectionInputs"/> at
/// the fixed epoch, written bare. This is the shape the existing CI job consumes.</description></item>
/// <item><description><c>--vectors=&lt;dir&gt;</c> — one record per corpus case, each concluded at
/// its own pinned clock, inside the transcript envelope.</description></item>
/// </list>
///
/// <para>The builder is shared rather than duplicated because a second copy is how the two shapes
/// drift: a key added to one path and not the other produces a corpus transcript that disagrees
/// with the single-vector transcript about what a record even is, and the cross-platform diff would
/// blame the engines for it. Twin of <c>macOS/Sources/SyxxCLI/Commands/ParityTranscript.swift:41</c>.</para>
/// </remarks>
internal static class ParityTranscriptCommand
{
    /// Fixed epoch so single-vector transcripts are byte-stable across hosts. Corpus cases ignore
    /// it entirely — each carries its own `pinnedUnixSeconds`.
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.FromUnixTimeSeconds(1_752_268_477);

    private const string DefaultOutPath = "transcript.net-win-x64.json";

    public static int Run(string[] args)
    {
        var outPath = DefaultOutPath;
        string? vectorsDir = null;
        var listOnly = false;
        foreach (var a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal))
                outPath = a["--out=".Length..];
            else if (a.StartsWith("--vectors=", StringComparison.Ordinal))
                vectorsDir = a["--vectors=".Length..];
            else if (string.Equals(a, "--list-only", StringComparison.Ordinal))
                listOnly = true;
        }

        // Corpus mode is entered only by --vectors. With the flag absent the single-vector path
        // below emits exactly the bytes it emitted before the track, so the existing CI job cannot
        // regress while the corpus path is built (plan Task 2.4).
        if (vectorsDir is not null)
            return RunCorpus(vectorsDir, listOnly, outPath);

        return Write(Record(new SectionInputs(), "empty-section-inputs", FixedNow), outPath);
    }

    /// <summary>
    /// One canonical record — the single shared builder for both the single-vector path and the
    /// corpus path.
    /// </summary>
    private static JsonObject Record(SectionInputs inputs, string vectorId, DateTimeOffset now)
    {
        var conclusion = Synthesizer.Conclude(inputs, now: now);

        // Ordinal, per the spec's "Output record" ordering rule: Dictionary and Set iteration order
        // differs between the two runtimes, so anything reaching output is explicitly sorted, and
        // the comparison must be ordinal rather than locale-aware on both sides.
        var diagnoses = (conclusion.Determination?.Diagnoses ?? [])
            .Select(d => d.Id.Value)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var untested = conclusion.SectionStatuses.Count(s => s.Status == SectionStatus.Untested);
        var total = conclusion.SectionStatuses.Count;

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["vectorId"] = vectorId,
            ["evidenceCompleteness"] = new JsonObject
            {
                ["untestedSections"] = untested,
                ["totalSections"] = total,
                ["observedEvidenceCount"] = conclusion.Evidence.Count,
            },
            ["determinationState"] = MapState(conclusion.Determination?.State),
            ["diagnosisIds"] = new JsonArray(diagnoses.Select(d => JsonValue.Create(d)).ToArray()),
            ["hasHardGatedSection"] = conclusion.HasHardGatedSection,
            ["confidencePresent"] = false,
            ["engine"] = "Synthesizer.conclude",
        };
    }

    /// <summary>Serialize and write, or report why not.</summary>
    /// <remarks>
    /// Keys are emitted in the insertion order established above, NOT sorted. The Swift twin
    /// serializes with <c>.sortedKeys</c>, so the two transcripts are not byte-identical and the
    /// cross-platform comparison normalises with <c>jq --sort-keys</c> (spec, "Output record").
    /// Recorded here because the previous comment on this line claimed the opposite — that a
    /// re-parse forced sorted keys — and nothing in this method ever did that.
    /// </remarks>
    private static int Write(JsonNode payload, string outPath)
    {
        var json = payload.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        try
        {
            var escaped = EscapeForwardSlashes(Encoding.UTF8.GetBytes(json + "\n"));
            File.WriteAllBytes(outPath, escaped);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"parity-transcript: write failed: {ex.Message}");
            return 3;
        }
        return 0;
    }

    /// <summary>
    /// Corpus mode. <c>--list-only</c> proves both runtimes read one directory into the same case
    /// count and the same sorted id list (Phase 1 verification); without it, one canonical record is
    /// emitted per case inside the transcript envelope (plan Task 2.1).
    /// </summary>
    /// <remarks>
    /// <para><c>evaluated</c> is written from a counter incremented per case actually concluded —
    /// never from the input list's length. Those two numbers are equal only when nothing went wrong,
    /// and taking the length would make them equal by construction, which is exactly how a harness
    /// comes to report full coverage of a corpus it partly skipped. The emitter refuses to write at
    /// all when the counter disagrees with the case count, or when it is zero.</para>
    ///
    /// <para>The <c>--list-only</c> stdout is diffed BYTE-FOR-BYTE against the Swift twin's, so it
    /// is written with explicit <c>\n</c> through <see cref="Console.Out"/>:
    /// <c>Console.WriteLine</c> would emit CRLF on Windows and the two legs would differ on line
    /// endings alone.</para>
    /// </remarks>
    private static int RunCorpus(string vectorsDir, bool listOnly, string outPath)
    {
        IReadOnlyList<LoadedCase> cases;
        try
        {
            cases = ParityVectorLoader.Load(vectorsDir);
        }
        catch (ParityVectorException ex)
        {
            Console.Error.WriteLine($"parity-transcript: {ex.Message}");
            return 3;
        }

        if (listOnly)
        {
            var sb = new StringBuilder();
            sb.Append("caseCount=")
              .Append(cases.Count.ToString(CultureInfo.InvariantCulture))
              .Append('\n');
            foreach (var c in cases) sb.Append(c.VectorId).Append('\n');
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return 0;
        }

        var evaluated = 0;
        var records = new JsonArray();
        foreach (var c in cases)
        {
            JsonObject record;
            try
            {
                record = Record(c.Inputs, c.VectorId, c.PinnedNow);
            }
            catch (Exception ex)
            {
                // A case the engine cannot conclude is named and NOT counted, so the guard below
                // refuses the whole transcript. Swift traps here instead; a named refusal carries
                // the same "no partial success" meaning and tells CI which case did it.
                Console.Error.WriteLine(
                    $"parity-transcript: case {c.CaseFile}: the engine could not conclude it — {ex.Message}");
                continue;
            }
            records.Add(record);
            evaluated++;
        }

        if (evaluated != cases.Count || evaluated == 0)
        {
            Console.Error.WriteLine(
                $"parity-transcript: evaluated {evaluated.ToString(CultureInfo.InvariantCulture)} of " +
                $"{cases.Count.ToString(CultureInfo.InvariantCulture)} cases — " +
                "refusing to write a transcript that does not cover its corpus");
            return 3;
        }

        // ── fingerprint ──────────────────────────────────────────────────────
        // SHA-256(caseCount || SHA-256(vector[0].sectionInputs) || ... ||
        //         SHA-256(conclusion[0].json) || ...)
        // Vector hashes in transcript order; conclusion hashes sorted by caseFile.
        // Twin of macOS/Sources/SyxxCLI/Commands/ParityTranscript.swift:125-143.
        var vectorHashes = new List<byte[]>();
        foreach (var c in cases)
        {
            var casePath = Path.Combine(vectorsDir, c.CaseFile);
            try
            {
                var rawBytes = File.ReadAllBytes(casePath);
                var root = JsonNode.Parse(rawBytes);
                if (root is JsonObject top
                    && top.TryGetPropertyValue("sectionInputs", out var sectionInputs)
                    && sectionInputs is not null)
                {
                    vectorHashes.Add(SHA256.HashData(SerializeWithSortedKeys(sectionInputs)));
                }
            }
            catch
            {
                // Skip — fingerprint degrades gracefully when a case file is unreadable.
            }
        }

        var conclusionPairs = new List<(string CaseFile, byte[] Hash)>();
        for (int i = 0; i < cases.Count; i++)
        {
            conclusionPairs.Add((
                cases[i].CaseFile,
                SHA256.HashData(SerializeWithSortedKeys(records[i]))));
        }
        conclusionPairs.Sort((a, b) => string.CompareOrdinal(a.CaseFile, b.CaseFile));

        // Build the preimage buffer: caseCount UTF-8 + each vector hash + each conclusion hash.
        var buf = new List<byte>();
        buf.AddRange(Encoding.UTF8.GetBytes(
            cases.Count.ToString(CultureInfo.InvariantCulture)));
        foreach (var h in vectorHashes) buf.AddRange(h);
        foreach (var pair in conclusionPairs) buf.AddRange(pair.Hash);

        var fingerprintHex = Convert.ToHexString(SHA256.HashData(buf.ToArray()))
            .ToLowerInvariant();

        var envelope = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["caseCount"] = cases.Count,
            ["evaluated"] = evaluated,
            ["fingerprint"] = fingerprintHex,
            ["records"] = records,
        };
        return Write(envelope, outPath);
    }

    private static string MapState(DeterminativeState? state) => state switch
    {
        DeterminativeState.Clean => "clean",
        DeterminativeState.ThreatDetected => "threatDetected",
        DeterminativeState.Inconclusive => "inconclusive",
        DeterminativeState.Untested => "untested",
        null => "none",
        _ => "none",
    };

    private static readonly JavaScriptEncoder SwiftCompatibleEncoder =
        JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>
    /// Serialize a <see cref="JsonNode"/> to UTF-8 bytes with sorted keys, so the fingerprint
    /// is byte-stable across platforms regardless of insertion order. Twin of the Swift
    /// <c>JSONSerialization.data(withJSONObject:options: [.sortedKeys])</c> call in
    /// <c>ParityTranscript.swift</c>.
    /// </summary>
    private static byte[] SerializeWithSortedKeys(JsonNode? node)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = SwiftCompatibleEncoder,
        });
        WriteSorted(node, writer);
        writer.Flush();

        // Swift's NSJSONSerialization escapes "/" as "\/" — Utf8JsonWriter does not.
        // Post-process: replace "/" with "\/" (but not "\/" that is already escaped).
        var bytes = stream.ToArray();
        bytes = EscapeForwardSlashes(bytes);
        // Swift's JSONSerialization escapes non-ASCII as \uXXXX — UnsafeRelaxedJsonEscaping does not.
        bytes = EscapeNonAscii(bytes);
        return bytes;
    }

    /// <summary>
    /// Replace every "/" with "\/" in the UTF-8 buffer, matching Swift's
    /// NSJSONSerialization escaping behaviour. Only "/" inside JSON string values
    /// are affected, which is all of them since JSON structure never contains "/".
    /// </summary>
    private static byte[] EscapeForwardSlashes(byte[] utf8)
    {
        // Count how many "/" characters need escaping
        var slashCount = 0;
        for (int i = 0; i < utf8.Length; i++)
        {
            if (utf8[i] == '/' && !IsAlreadyEscaped(utf8, i))
                slashCount++;
        }

        if (slashCount == 0) return utf8;

        var result = new byte[utf8.Length + slashCount];
        var dst = 0;
        for (int src = 0; src < utf8.Length; src++)
        {
            if (utf8[src] == '/' && !IsAlreadyEscaped(utf8, src))
            {
                result[dst++] = (byte)'\\';
                result[dst++] = (byte)'/';
            }
            else
            {
                result[dst++] = utf8[src];
            }
        }
        return result;
    }

    private static bool IsAlreadyEscaped(byte[] utf8, int slashIndex)
    {
        var backslashCount = 0;
        for (var i = slashIndex - 1; i >= 0 && utf8[i] == '\\'; i--)
            backslashCount++;
        return (backslashCount % 2) == 1;
    }

    /// <summary>
    /// Escape non-ASCII bytes (≥ 0x80) as \uXXXX hex sequences, matching Swift's
    /// JSONSerialization behaviour. UnsafeRelaxedJsonEscaping passes non-ASCII
    /// through as raw UTF-8 bytes; Swift always escapes them.
    /// </summary>
    private static byte[] EscapeNonAscii(byte[] utf8)
    {
        // Count how many non-ASCII characters need escaping
        var extraBytes = 0;
        var i = 0;
        while (i < utf8.Length)
        {
            if (utf8[i] < 0x80)
            {
                i++;
                continue;
            }
            // Multi-byte UTF-8 sequence — will expand to 6 bytes (\uXXXX)
            var cp = DecodeUtf8(utf8, i, out var len);
            i += len;
            extraBytes += cp <= 0xFFFF ? 6 - len : 12 - len; // surrogate pair for supplementary
        }

        if (extraBytes == 0) return utf8;

        var result = new byte[utf8.Length + extraBytes];
        var dst = 0;
        var src = 0;
        while (src < utf8.Length)
        {
            if (utf8[src] < 0x80)
            {
                result[dst++] = utf8[src++];
                continue;
            }
            var cp = DecodeUtf8(utf8, src, out var len);
            src += len;
            if (cp <= 0xFFFF)
            {
                WriteHexEscape(result, ref dst, cp);
            }
            else
            {
                // Supplementary character → surrogate pair
                cp -= 0x10000;
                WriteHexEscape(result, ref dst, 0xD800 | (cp >> 10));
                WriteHexEscape(result, ref dst, 0xDC00 | (cp & 0x3FF));
            }
        }
        return result;
    }

    private static int DecodeUtf8(byte[] utf8, int start, out int len)
    {
        var b0 = utf8[start];
        if ((b0 & 0xE0) == 0xC0) { len = 2; return ((b0 & 0x1F) << 6) | (utf8[start + 1] & 0x3F); }
        if ((b0 & 0xF0) == 0xE0) { len = 3; return ((b0 & 0x0F) << 12) | ((utf8[start + 1] & 0x3F) << 6) | (utf8[start + 2] & 0x3F); }
        len = 4;
        return ((b0 & 0x07) << 18) | ((utf8[start + 1] & 0x3F) << 12) | ((utf8[start + 2] & 0x3F) << 6) | (utf8[start + 3] & 0x3F);
    }

    private static void WriteHexEscape(byte[] result, ref int dst, int cp)
    {
        result[dst++] = (byte)'\\';
        result[dst++] = (byte)'u';
        result[dst++] = HexDigit(cp >> 12);
        result[dst++] = HexDigit((cp >> 8) & 0xF);
        result[dst++] = HexDigit((cp >> 4) & 0xF);
        result[dst++] = HexDigit(cp & 0xF);
    }

    private static byte HexDigit(int v) => (byte)(v < 10 ? '0' + v : 'a' + v - 10);

    private static void WriteSorted(JsonNode? node, Utf8JsonWriter writer)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var kvp in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteSorted(kvp.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonArray arr:
                writer.WriteStartArray();
                foreach (var item in arr)
                    WriteSorted(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValue val:
                WriteValue(val, writer);
                break;
            case null:
                writer.WriteNullValue();
                break;
        }
    }

    /// <summary>
    /// Write a <see cref="JsonValue"/> with Swift-compatible number formatting.
    /// Swift's NSJSONSerialization parses numbers into NSNumber (double) and re-serializes
    /// them with full IEEE 754 precision (e.g. 0.97 → 0.96999999999999997).
    /// System.Text.Json preserves the original text, so we round-trip through double
    /// when the value contains a decimal point or exponent.
    /// </summary>
    private static void WriteValue(JsonValue val, Utf8JsonWriter writer)
    {
        if (val.TryGetValue<decimal>(out var dec))
        {
            // decimal.TryGetValue succeeds for any JSON number.
            // Check if it's a floating-point value by examining the raw text.
            var raw = val.ToString();
            if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E'))
            {
                // Floating-point: round-trip through double for Swift-compatible representation.
                if (val.TryGetValue<double>(out var d))
                {
                    if (!double.IsFinite(d))
                        throw new JsonException($"Floating-point value {d} is not finite and cannot be serialized to JSON.");
                    var formatted = d.ToString("G17", CultureInfo.InvariantCulture);
                    // Swift's JSONSerialization uses lowercase 'e' for exponents
                    formatted = formatted.Replace('E', 'e');
                    writer.WriteRawValue(formatted, skipInputValidation: true);
                    return;
                }
            }
            // Integer: write using the raw JSON text (preserving exact representation).
            val.WriteTo(writer);
            return;
        }
        // Non-number (string, bool, null): write normally.
        val.WriteTo(writer);
    }
}
}
