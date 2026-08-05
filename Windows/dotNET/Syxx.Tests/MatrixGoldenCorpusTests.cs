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
}
