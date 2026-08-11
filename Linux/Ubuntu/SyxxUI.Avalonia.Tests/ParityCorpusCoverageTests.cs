using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Coverage integrity for corpus/parity-vectors (track cross-platform-parity_20260805, Task 6.5).
/// Twin of macOS/Tests/SyxxKitTests/ParityCorpusCoverageTests.swift.
/// </summary>
/// <remarks>
/// <para>Complements <see cref="ParityVectorLoaderTests.CanonicalCorpusLoadsWhole"/>, which counts
/// the corpus with <c>Directory.GetFiles(corpus, "*.json")</c> — the SAME predicate both loaders
/// skip on (ParityVectorLoader.cs:197, ParityVectorLoader.swift:195). A case renamed
/// <c>005_x.json.orig</c> is invisible to that glob for exactly the reason it is invisible to the
/// loaders, so the assertion agrees with the loader about a corpus that shrank.</para>
/// </remarks>
public sealed class ParityCorpusCoverageTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));

    private static string CorpusDir() => Path.Combine(RepoRoot(), "corpus", "parity-vectors");
    private static string ManifestPath() => Path.Combine(RepoRoot(), "corpus", "parity-vectors.manifest");

    /// <summary>Every entry in the corpus directory must BE a case file.</summary>
    [Fact]
    public void CorpusDirectoryHoldsOnlyCaseFiles()
    {
        var dir = CorpusDir();
        var entries = Directory.GetFileSystemEntries(dir);
        Assert.NotEmpty(entries);

        var strays = entries
            .Where(e => !File.Exists(e)
                        || !ParityVectorLoader.TryParseCaseFilename(Path.GetFileName(e), out _, out _))
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"{dir} holds entries that are not PARITY_VECTOR_V1 cases: {string.Join(", ", strays)}. " +
            "Both loaders skip a non-.json entry silently (ParityVectorLoader.cs:197, " +
            "ParityVectorLoader.swift:195), so a case renamed out of its extension stops counting " +
            "on both engines with no message.");
    }

    /// <summary>The corpus must be exactly what the committed manifest says it is.</summary>
    [Fact]
    public void CorpusMatchesTheCommittedManifest()
    {
        var lines = File.ReadAllLines(ManifestPath());
        Assert.StartsWith("caseCount=", lines[0], StringComparison.Ordinal);
        var declared = int.Parse(lines[0]["caseCount=".Length..], CultureInfo.InvariantCulture);
        var expectedIds = lines.Skip(1).Where(l => l.Length > 0).ToList();
        Assert.Equal(declared, expectedIds.Count);

        var cases = ParityVectorLoader.Load(CorpusDir());
        Assert.Equal(expectedIds, cases.Select(c => c.VectorId));
    }

    /// <summary>Every case must reach the engine, counted rather than assumed.</summary>
    [Fact]
    public void EveryCaseReachesTheEngine()
    {
        var cases = ParityVectorLoader.Load(CorpusDir());
        Assert.NotEmpty(cases);

        var evaluated = 0;
        foreach (var c in cases)
        {
            var conclusion = Synthesizer.Conclude(c.Inputs, now: c.PinnedNow);
            Assert.Equal(6, conclusion.SectionStatuses.Count);
            evaluated++;
        }
        Assert.Equal(cases.Count, evaluated);
    }
}
