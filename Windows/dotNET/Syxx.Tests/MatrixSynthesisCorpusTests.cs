using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Task 1.1 (review-fixes_20260812, wave 1): the SHARED SYNTHESIS FIXTURE SET —
/// <c>corpus/matrix/synthesis/sh-*.json</c>. Six scenarios that the whole wave's defects
/// (W01-W03, W20, M02, M09, M10) are designed against, in one evidence-scoring pipeline:
///
///   sh-1 hard-excluded threat            → W02 (hardExcluded suppressions are an overhang, never clean)
///   sh-2 every section timed out         → W03 (unmeasured evidence never reads clean)
///   sh-3 non-usable section observed     → W20/M02 (B162 guard: unusable output scores nothing)
///   sh-4 only firing indicators contradict → M09 (contradiction never becomes support)
///   sh-5 thin-but-conclusive section     → M02/M09/M10 (usable-thin output still scores in full)
///   sh-6 unmappable rules in the spec    → M10/N1/B156 (denominator composition; clean stays unreachable)
///
/// Fixture file shape (JSON):
///   { "name": ..., "spec": "default" | &lt;MatrixSpec json&gt;, "input": &lt;EvidenceBundle json&gt;,
///     "expected": &lt;DeterminativeResult json&gt;, "why": ... }
///
/// Both platforms must load the SAME files and produce byte-identical (whitespace-normalised)
/// DeterminativeResult JSON — that is the Task 1.8 parity contract.
///
/// A missing / empty fixture set THROWS — a vacuously-green corpus test is a green that lies.
/// </summary>
public class MatrixSynthesisCorpusTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Windows/dotNET/Syxx.Tests/MatrixSynthesisCorpusTests.cs
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("Cannot resolve source directory.");
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    private static string CorpusDir() => Path.Combine(RepoRoot(), "corpus", "matrix", "synthesis");

    private static string NormalizeWhitespace(string s) => Regex.Replace(s, @"\s+", "");

    private static List<string> FixtureFiles()
    {
        var dir = CorpusDir();
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"Shared synthesis fixture set not found at {dir}. Task 1.1 must build it — " +
                "an absent corpus is not a passing test.");
        }

        var files = Directory.GetFiles(dir, "sh-*.json").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"{dir} contains no sh-*.json fixtures. An empty fixture set is a green that lies.");
        }

        return files;
    }

    private static (MatrixDeterminativeEngine Engine, EvidenceBundle Bundle, string ExpectedRaw) LoadFixture(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;

        var name = root.GetProperty("name").GetString();
        if (string.IsNullOrWhiteSpace(name)) { throw new InvalidOperationException($"{file}: 'name' is missing."); }
        if (string.IsNullOrWhiteSpace(root.GetProperty("why").GetString()))
        {
            throw new InvalidOperationException($"{file}: 'why' is missing — every fixture must record what it pins.");
        }

        var engine = root.GetProperty("spec").ValueKind == JsonValueKind.String
            ? SyxxMatrixIntegration.MakeEngine()
            : new MatrixDeterminativeEngine(
                JsonSerializer.Deserialize<MatrixSpec>(root.GetProperty("spec").GetRawText())
                    ?? throw new InvalidOperationException($"{file}: spec failed to parse."));

        var bundle = JsonSerializer.Deserialize<EvidenceBundle>(root.GetProperty("input").GetRawText())
            ?? throw new InvalidOperationException($"{file}: input failed to parse.");
        bundle.Validate();

        // Raw text captured while the document is still alive — the caller outlives the using block.
        return (engine, bundle, root.GetProperty("expected").GetRawText());
    }

    /// <summary>
    /// Every fixture drives the engine and the frozen expected DeterminativeResult must match
    /// whitespace-normalised — the cross-platform parity contract (Task 1.8).
    /// </summary>
    [Fact]
    public void EverySynthesisFixtureReproducesItsFrozenExpectedResult()
    {
        foreach (var file in FixtureFiles())
        {
            var (engine, bundle, expectedRaw) = LoadFixture(file);
            var actual = engine.Evaluate(bundle);
            var actualJson = JsonSerializer.Serialize(actual);

            Assert.True(
                NormalizeWhitespace(expectedRaw) == NormalizeWhitespace(actualJson),
                $"{Path.GetFileName(file)}: engine output drifted from the frozen expected result.");
        }
    }

    /// <summary>
    /// Phase verification, made mechanical: no fixture in this set may yield an affirmative clean
    /// from unmeasured, contradicted, or suppressed evidence. By construction every scenario here
    /// is one where clean is the DANGEROUS answer, so no fixture may ever return Clean.
    /// </summary>
    [Fact]
    public void NoFixtureYieldsAffirmativeCleanFromUnmeasuredOrSuppressedEvidence()
    {
        foreach (var file in FixtureFiles())
        {
            var (engine, bundle, _) = LoadFixture(file);
            var actual = engine.Evaluate(bundle);
            Assert.True(
                actual.State != DeterminativeState.Clean,
                $"{Path.GetFileName(file)}: clean must be unreachable in this fixture set.");
        }
    }

    /// <summary>
    /// Scenario-level invariants, independent of the frozen numbers: if a frozen expected value
    /// is ever regenerated by a regressed engine, these asserts fail on MEANING (state / tier /
    /// suppression reason) rather than on a single drifted decimal.
    /// </summary>
    [Fact]
    public void ScenarioInvariantsHold()
    {
        foreach (var file in FixtureFiles())
        {
            var fileName = Path.GetFileName(file);
            var (engine, bundle, _) = LoadFixture(file);
            var actual = engine.Evaluate(bundle);

            switch (fileName)
            {
                case "sh-1-hard-excluded-threat.json":
                    // BOTH the threat and clean are hard-excluded by the same conflicting evidence
                    // (authoritativeMismatch excludes clean; trustedWitnessOK excludes dnsSpoofing) —
                    // conflicting hard proofs from two sections is exactly why the run abstains.
                    var excluded = Assert.Single(actual.Suppressed,
                        s => s.Id.Value == "dnsSpoofing"
                             && s.Reason == SuppressedDiagnosis.SuppressionReason.HardExcluded);
                    Assert.Empty(actual.Diagnoses);
                    break;

                case "sh-2-all-sections-timed-out.json":
                    Assert.Equal(DeterminativeState.Untested, actual.State);
                    Assert.Empty(actual.Diagnoses);
                    break;

                case "sh-3-nonusable-section-observed-indicator.json":
                    var gated = Assert.Single(actual.Suppressed, s => s.Id.Value == "dnsSpoofing");
                    Assert.Equal(SuppressedDiagnosis.SuppressionReason.InvalidatedRequiredSection, gated.Reason);
                    Assert.Empty(actual.Diagnoses);
                    break;

                case "sh-4-only-firing-indicators-contradict.json":
                    Assert.Empty(actual.Diagnoses);
                    Assert.Empty(actual.Suppressed);
                    break;

                case "sh-5-thin-but-conclusive-section.json":
                    var conclusive = Assert.Single(actual.Diagnoses, d => d.Id.Value == "dnsSpoofing");
                    Assert.Equal(DiagnosisConfidence.Conclusive, conclusive.Confidence);
                    break;

                case "sh-6-unmappable-rules-in-spec.json":
                    Assert.Empty(actual.Diagnoses);
                    Assert.Empty(actual.Suppressed);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown fixture scenario: {fileName}");
            }
        }
    }
}
