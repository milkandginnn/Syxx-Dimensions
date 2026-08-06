using System.Text.Json;
using System.Text.Json.Nodes;
using Syxx.Kit;

namespace Syxx.Commands
{
/// <summary>
/// Phase 4.2: emit a canonical behavioral transcript from the production synthesizer.
/// Hidden CI subcommand — not advertised in user help until owner-approved.
/// </summary>
internal static class ParityTranscriptCommand
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.FromUnixTimeSeconds(1_752_268_477);

    public static int Run(string[] args)
    {
        var outPath = "transcript.net-win-x64.json";
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

        // Corpus mode is entered only by --vectors. With the flag absent every byte below is the
        // pre-track single-vector path, so the existing CI job cannot regress while Phase 2 is built
        // (plan Task 2.4).
        if (vectorsDir is not null)
            return RunCorpus(vectorsDir, listOnly);

        var inputs = new SectionInputs();
        var conclusion = Synthesizer.Conclude(inputs, now: FixedNow);

        var diagnoses = (conclusion.Determination?.Diagnoses ?? [])
            .Select(d => d.Id.Value)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var untested = conclusion.SectionStatuses.Count(s => s.Status == SectionStatus.Untested);
        var total = conclusion.SectionStatuses.Count;

        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["vectorId"] = "empty-section-inputs",
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

        var json = root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        // Force sorted keys via re-parse (JsonObject insertion order is already controlled above).
        try
        {
            File.WriteAllText(outPath, json + "\n");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"parity-transcript: write failed: {ex.Message}");
            return 3;
        }
        return 0;
    }

    /// <summary>
    /// <c>--vectors=&lt;dir&gt;</c>. Only <c>--list-only</c> is implemented here: it loads the corpus
    /// and prints <c>caseCount=&lt;n&gt;</c> followed by one vectorId per line, in case order.
    /// </summary>
    /// <remarks>
    /// <para>That output is diffed BYTE-FOR-BYTE against the Swift twin's, so it is written with
    /// explicit <c>\n</c> through <see cref="Console.Out"/>: <c>Console.WriteLine</c> would emit
    /// CRLF on Windows and the two legs would differ on line endings alone.</para>
    ///
    /// <para><c>--vectors</c> WITHOUT <c>--list-only</c> exits non-zero rather than falling back to
    /// the single-vector path. A silent fallback would write a one-record transcript from a
    /// 31-case corpus and exit 0 — a run that measured almost nothing, reported as success.</para>
    /// </remarks>
    private static int RunCorpus(string vectorsDir, bool listOnly)
    {
        if (!listOnly)
        {
            Console.Error.WriteLine("parity-transcript: full-corpus emission arrives with Phase 2");
            return 3;
        }

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

        var sb = new System.Text.StringBuilder();
        sb.Append("caseCount=")
          .Append(cases.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
          .Append('\n');
        foreach (var c in cases) sb.Append(c.VectorId).Append('\n');
        Console.Out.Write(sb.ToString());
        Console.Out.Flush();
        return 0;
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
}
}
