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
        foreach (var a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal))
                outPath = a["--out=".Length..];
        }

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
