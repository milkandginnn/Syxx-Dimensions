using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// P0.2/P0.3 — the ENGINE-SIDE half of the STAGE0 golden-vector harness. This mirrors the Swift
/// `Stage0GoldenCorpusTests.swift` structure exactly: same seed corpus, `greensAtPhase`,
/// `knownFalseSecureUntilGate`, phase-reachable exact assertions, and the universal never-false-secure
/// invariant. xUnit has no strict expected-failure primitive, so known-open false-secure vectors use a
/// RED ratchet: they currently MUST read CLEAN; once a gate lands, this test fails until the flag is
/// removed.
public sealed class Stage0GoldenCorpusTests
{
    private const int EnginePhase = 1;

    private sealed record SeedEvidence(
        Section Section,
        EvidenceId EvidenceId,
        string? Source = null,
        string? ClusterId = null)
    {
        public EvidenceItem Item => EvidenceItem.Create(Section, EvidenceId, "o", "r", Source);
    }

    // P2a: the Kit's Verdict → Diagnosis+Conclusivity split means "inconclusive" is no longer a
    // Hypothesis value; the corpus vocabulary (clean / inconclusive / <threat>) is parsed into this
    // tri-state instead, and asserted through ConclusionTestSemantics so no expectation is weakened.
    private enum ExpectedOutcome { Clean, Inconclusive, Threat }

    private sealed record Expected(ExpectedOutcome Outcome, Hypothesis Threat = default);

    private sealed record Seed(
        string Id,
        string Label,
        IReadOnlyList<SeedEvidence> Evidence,
        IReadOnlyList<(Section Section, SectionStatus Status)> Statuses,
        int GreensAtPhase,
        Expected ExpectedVerdict,
        bool ExpectedReferral,
        bool KnownFalseSecureUntilGate = false)
    {
        public bool Tampering => !Label.StartsWith("benign", StringComparison.Ordinal);
    }

    // WS-B1: the seed corpus is loaded from corpus/stage0/seeds.json — the SINGLE SOURCE OF TRUTH
    // shared line-for-line with the Swift harness (macOS/Tests/SyxxKitTests/Stage0GoldenCorpusTests.swift).
    // Do NOT re-hand-encode vectors here; edit the JSON. A missing / empty / malformed corpus THROWS: a
    // silently-empty seed list would make the never-false-secure invariant pass vacuously (a green-that-lies),
    // which the honesty spine forbids.

    private sealed record CorpusDto(List<VectorDto>? Vectors);

    private sealed record VectorDto(
        string Id,
        string Label,
        List<EvidenceDto>? Evidence,
        Dictionary<string, string>? SectionStatuses,
        int GreensAtPhase,
        string ExpectedVerdict,
        bool ExpectedReferral,
        bool KnownFalseSecureUntilGate = false);

    private sealed record EvidenceDto(
        string Section,
        string EvidenceId,
        string? Source = null,
        string? ClusterId = null);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string CorpusPath([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Windows/Syxx.Tests/Stage0GoldenCorpusTests.cs (compile-time source path,
        // the exact twin of the Swift harness's #filePath resolution).
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("Stage-0 corpus: cannot resolve source directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", ".."));
        var path = Path.Combine(repoRoot, "corpus", "stage0", "seeds.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Stage-0 golden corpus not found at {path}. WS-B1 single source of truth is missing.", path);
        return path;
    }

    private static string Normalize(string s) => s.Replace("_", string.Empty).ToLowerInvariant();

    // Maps a Swift-rawValue JSON enum string onto the C# PascalCase enum by normalizing (lowercase,
    // strip underscores). e.g. "interception_onPath" -> InterceptionOnPath, "dnssecCanaryStripped" ->
    // DnssecCanaryStripped, "positive" -> Positive. Throws on an unknown value — never a silent default.
    private static T ParseEnum<T>(string value, string id) where T : struct, Enum
    {
        var norm = Normalize(value);
        foreach (var name in Enum.GetNames<T>())
            if (Normalize(name) == norm) return Enum.Parse<T>(name);
        throw new InvalidOperationException(
            $"corpus vector '{id}': unknown {typeof(T).Name} value '{value}'.");
    }

    private static IReadOnlyList<(Section Section, SectionStatus Status)> BuildStatuses(
        string id, IReadOnlyDictionary<string, string>? map)
    {
        if (map is null || map.Count == 0)
            throw new InvalidOperationException($"corpus vector '{id}': missing sectionStatuses.");
        var parsed = map.ToDictionary(kv => ParseEnum<Section>(kv.Key, id), kv => ParseEnum<SectionStatus>(kv.Value, id));
        // Order by the canonical enum order (Prophet..Witness) — reproduces the old With(allNeg, ...) shape.
        return Enum.GetValues<Section>()
            .Select(section => parsed.TryGetValue(section, out var status)
                ? (section, status)
                : throw new InvalidOperationException($"corpus vector '{id}': sectionStatuses missing section '{section}'."))
            .ToList();
    }

    private static Seed FromDto(VectorDto v)
    {
        var evidence = (v.Evidence ?? new List<EvidenceDto>())
            .Select(e => new SeedEvidence(
                ParseEnum<Section>(e.Section, v.Id),
                ParseEnum<EvidenceId>(e.EvidenceId, v.Id),
                e.Source,
                e.ClusterId))
            .ToList();
        var seed = new Seed(
            v.Id,
            v.Label,
            evidence,
            BuildStatuses(v.Id, v.SectionStatuses),
            v.GreensAtPhase,
            ParseExpected(v.ExpectedVerdict, v.Id),
            v.ExpectedReferral,
            v.KnownFalseSecureUntilGate);
        return seed;
    }

    private static Expected ParseExpected(string value, string id) => Normalize(value) switch
    {
        "clean" => new Expected(ExpectedOutcome.Clean),
        "inconclusive" => new Expected(ExpectedOutcome.Inconclusive),
        _ => new Expected(ExpectedOutcome.Threat, ParseEnum<Hypothesis>(value, id)),
    };

    private static IReadOnlyList<Seed> Seeds()
    {
        var json = File.ReadAllText(CorpusPath());
        var file = JsonSerializer.Deserialize<CorpusDto>(json, JsonOpts)
                   ?? throw new InvalidOperationException("Stage-0 golden corpus deserialized to null.");
        if (file.Vectors is null || file.Vectors.Count == 0)
            throw new InvalidOperationException("Stage-0 golden corpus contains no vectors — refusing to run vacuously.");
        return file.Vectors.Select(FromDto).ToList();
    }

    private static Conclusion Run(Seed seed)
    {
        var statuses = seed.Statuses.Select(s => new SectionStatusEntry(s.Section, s.Status)).ToList();
        return SynthesisEngine.Synthesize(seed.Evidence.Select(e => e.Item).ToList(), statuses);
    }

    [Fact]
    public void NeverFalseSecureUniversalInvariant()
    {
        foreach (var seed in Seeds().Where(seed => seed.Tampering))
        {
            Assert.NotEqual(ExpectedOutcome.Clean, seed.ExpectedVerdict.Outcome);

            var actual = Run(seed);
            if (seed.KnownFalseSecureUntilGate)
            {
                // RED ratchet: this vector currently reads committed-clean (a documented false
                // secure); when the gate lands this fails until the flag is removed from the corpus.
                Assert.True(actual.ReadsClean(), $"vector '{seed.Id}': ratchet expects the known false-secure");
            }
            else
            {
                Assert.False(actual.ReadsClean(),
                    $"vector '{seed.Id}': a tampering vector must NEVER read committed-clean");
            }
        }
    }

    [Fact]
    public void Phase1ReachableSeedsMatchExactVerdict()
    {
        foreach (var seed in Seeds().Where(seed => seed.GreensAtPhase <= EnginePhase && !seed.KnownFalseSecureUntilGate))
        {
            var result = Run(seed);
            switch (seed.ExpectedVerdict.Outcome)
            {
                case ExpectedOutcome.Clean:
                    Assert.True(result.ReadsClean(), $"vector '{seed.Id}': expected committed-clean, got {result.Diagnosis}/{result.Conclusivity}");
                    break;
                case ExpectedOutcome.Inconclusive:
                    Assert.True(result.IsInconclusive(), $"vector '{seed.Id}': expected inconclusive, got {result.Diagnosis}/{result.Conclusivity}");
                    break;
                case ExpectedOutcome.Threat:
                    Assert.True(result.Names(seed.ExpectedVerdict.Threat),
                        $"vector '{seed.Id}': expected {seed.ExpectedVerdict.Threat}, got {result.Diagnosis}/{result.Conclusivity}");
                    break;
            }
            Assert.Equal(seed.ExpectedReferral, result.ReferralTriggered);
        }
    }

    [Fact]
    public void CorpusContainsClusteredEvidence_ScaffoldingForFutureEngineUse()
    {
        // The corpus seeds carry ClusterId metadata for future within-cluster combination
        // rules (P0.4 bake-off). The engine does not yet consume ClusterId — this test
        // verifies the corpus scaffolding exists so the engine can wire it when ready.
        var clustered = Seeds().SelectMany(seed => seed.Evidence).Where(item => item.ClusterId is not null).ToList();
        // xunit's Assert.NotEmpty has no message overload; Assert.True does.
        Assert.True(clustered.Count > 0,
            "Corpus must contain evidence items with ClusterId set (scaffolding for P0.4 within-cluster combination)");
    }
}
