using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// F042: Loader for corpus/matrix/** — the frozen spec oracle and three golden vectors for the
/// matrix determinative engine. Before this file, no test on any platform loaded these vectors,
/// so the ~100 hand-transcribed weights and thresholds across SyxxMatrixIntegration.cs had
/// zero mechanical cross-check against the shared oracle.
///
/// Twin of macOS/Tests/SyxxKitTests/MatrixGoldenCorpusTests.swift.
/// Both platforms must assert:
/// 1. DefaultSpecJson() equals default-spec.frozen.json byte-for-byte
/// 2. Each golden vector in corpus/matrix/vectors/*.json drives Evaluate() and produces the
///    expected DeterminativeResult JSON.
///
/// A missing / empty fixture THROWS — a vacuously-green test is the green-that-lies.
/// </summary>
public class MatrixGoldenCorpusTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Windows/dotNET/Syxx.Tests/MatrixGoldenCorpusTests.cs
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("Cannot resolve source directory.");
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    private static string NormalizeWhitespace(string s) =>
        Regex.Replace(s, @"\s+", "");

    /// <summary>
    /// Assert that DefaultSpecJson() equals default-spec.frozen.json byte-for-byte.
    /// A transcription typo in any of the ~100 constants is caught here, not in production.
    /// </summary>
    [Fact]
    public void DefaultSpecMatchesFrozenOracle()
    {
        var oraclePath = Path.Combine(RepoRoot(), "corpus", "matrix", "default-spec.frozen.json");
        if (!File.Exists(oraclePath))
            throw new FileNotFoundException(
                $"Matrix frozen spec oracle not found at {oraclePath}. The oracle is missing.", oraclePath);

        var oracleString = NormalizeWhitespace(File.ReadAllText(oraclePath));
        var specString = NormalizeWhitespace(SyxxMatrixIntegration.DefaultSpecJson(prettyPrinted: true));

        Assert.Equal(oracleString, specString);
    }

    /// <summary>
    /// F-04: knownSections must be emitted in ordinal-sorted order.
    ///
    /// It is backed by a set, which has no order, so serializing it directly leaks hash order into
    /// the bytes — and SectionId is a record struct over string, whose hash .NET randomizes per
    /// process. Without a sort the emitted spec differs between launches, which makes it unusable
    /// as a frozen oracle and unstable under any hash, signature, or byte-comparison taken over it.
    ///
    /// Asserting SORTED rather than merely STABLE is deliberate: two serializations inside one
    /// process share a hash seed and would agree even while the defect was present.
    ///
    /// Twin: MatrixGoldenCorpusTests.testDefaultSpecKnownSectionsAreOrdinalSorted (Swift).
    /// </summary>
    [Fact]
    public void DefaultSpecKnownSectionsAreOrdinalSorted()
    {
        var spec = SyxxMatrixIntegration.DefaultSpecJson(prettyPrinted: false);

        var match = Regex.Match(spec, "\"knownSections\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]");
        Assert.True(match.Success, $"knownSections array not found in emitted spec: {spec[..Math.Min(200, spec.Length)]}");

        var emitted = match.Groups["body"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().Trim('"'))
            .ToList();

        Assert.NotEmpty(emitted);
        Assert.Equal(emitted.OrderBy(s => s, StringComparer.Ordinal).ToList(), emitted);
    }

    // ------------------------------------------------------------------ golden vectors (Task 8.9)
    // F-07: this file's header promised vector evaluation on both platforms and implemented it on
    // neither's C# side. Ported from the Swift twin 2026-08-05; also the proof that the F-09 B156
    // coverage port leaves both engines agreeing on the shared vectors.

    /// Vectors in corpus/matrix/vectors/ that are NOT engine input/expected pairs, with the reason.
    /// Named explicitly rather than schema-sniffed: a new vector with an unrecognised shape must
    /// fail this suite, not be silently skipped into a coverage hole.
    private static readonly HashSet<string> NonEngineVectors = new()
    {
        // A round6 helper table (banker's rounding boundary ties): {name, spec, values, why},
        // no input/expected pair, so it does not drive Evaluate().
        "gv-10-rounding.json",
    };

    private static string VectorsDir() => Path.Combine(RepoRoot(), "corpus", "matrix", "vectors");

    private static List<string> JsonFiles()
    {
        var dir = VectorsDir();
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"Matrix golden vectors not found at {dir}.");
        var files = Directory.GetFiles(dir, "*.json").Select(Path.GetFileName).Cast<string>()
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("Matrix golden corpus contains no vectors — refusing to run vacuously.");
        return files;
    }

    /// Twin of MatrixGoldenCorpusTests.testEveryVectorFileIsClassified (Swift).
    [Fact]
    public void EveryVectorFileIsClassified()
    {
        foreach (var file in JsonFiles().Where(f => !NonEngineVectors.Contains(f)))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(VectorsDir(), file)));
            Assert.True(doc.RootElement.TryGetProperty("input", out _),
                $"{file}: unclassified vector shape — needs `input`, or list it in NonEngineVectors");
            Assert.True(doc.RootElement.TryGetProperty("expected", out _),
                $"{file}: unclassified vector shape — needs `expected`, or list it in NonEngineVectors");
        }
    }

    /// Twin of MatrixGoldenCorpusTests.testGoldenVectorsEvaluateToExpectedResults (Swift).
    [Fact]
    public void GoldenVectorsEvaluateToExpectedResults()
    {
        var engineFiles = JsonFiles().Where(f => !NonEngineVectors.Contains(f)).ToList();
        Assert.NotEmpty(engineFiles);

        var engine = SyxxMatrixIntegration.MakeEngine();
        var evaluated = 0;

        foreach (var file in engineFiles)
        {
            var vector = System.Text.Json.JsonSerializer.Deserialize<GoldenVector>(
                File.ReadAllText(Path.Combine(VectorsDir(), file)))
                ?? throw new InvalidOperationException($"{file}: deserialized to null");

            var result = engine.Evaluate(vector.Input!.ToEvidenceBundle());

            Assert.True(Enum.TryParse<DeterminativeState>(vector.Expected!.State, true, out var expectedState),
                $"{file}: unknown expected state '{vector.Expected.State}'");
            Assert.Equal(expectedState, result.State);
            Assert.Equal(vector.Expected.Diagnoses!.Count, result.Diagnoses.Count);
            Assert.Equal(
                vector.Expected.Diagnoses.Select(d => d.Id!).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                result.Diagnoses.Select(d => d.Id.Value).OrderBy(s => s, StringComparer.Ordinal).ToList());
            evaluated++;
        }

        // A suite that cannot say how many cases it ran is not evidence when it is green.
        Assert.Equal(engineFiles.Count, evaluated);
    }

    // DTOs matching the corpus as it ships: key is `section`/`indicator`, not `id`.
    private sealed class GoldenVector
    {
        [System.Text.Json.Serialization.JsonPropertyName("input")] public InputDto? Input { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expected")] public ExpectedDto? Expected { get; set; }
    }
    private sealed class InputDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("sections")] public List<SectionDto> Sections { get; set; } = new();
        [System.Text.Json.Serialization.JsonPropertyName("indicators")] public List<IndicatorDto> Indicators { get; set; } = new();

        public EvidenceBundle ToEvidenceBundle() => new(
            Sections.Select(s => new SectionResult(
                new SectionId(s.Section!),
                Enum.TryParse<EvidenceUsability>(s.Status, true, out var u) ? u : EvidenceUsability.Untested,
                Trusted: s.Trusted ?? true)).ToList(),
            Indicators.Select(i => new ObservedIndicator(
                new IndicatorId(i.Indicator!),
                new SectionId(i.Section!),
                i.ObservedStrength,
                Quality: i.Quality ?? 1.0,
                Contradictory: i.Contradictory ?? false)).ToList());
    }
    private sealed class SectionDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("section")] public string? Section { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("status")] public string? Status { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("trusted")] public bool? Trusted { get; set; }
    }
    private sealed class IndicatorDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("indicator")] public string? Indicator { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("section")] public string? Section { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("observedStrength")] public double? ObservedStrength { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("quality")] public double? Quality { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("contradictory")] public bool? Contradictory { get; set; }
    }
    private sealed class ExpectedDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("state")] public string? State { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("diagnoses")] public List<DiagnosisDto>? Diagnoses { get; set; }
    }
    private sealed class DiagnosisDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string? Id { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("confidence")] public string? Confidence { get; set; }
    }
}
