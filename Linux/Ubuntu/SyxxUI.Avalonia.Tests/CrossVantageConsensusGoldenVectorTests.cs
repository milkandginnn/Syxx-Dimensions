using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// B281 — Windows half of the SHARED cross-platform golden vector. Loads the SAME checked-in
    /// <c>corpus/b281-consensus/decision-table.json</c> the macOS half
    /// (<c>SyxxKitTests/CrossVantageConsensusGoldenVectorTests.swift</c>) loads, and asserts
    /// <see cref="CrossVantageConsensus.ClassifyPeerConsensus"/> returns the expected outcome for
    /// every row. If the two platforms ever disagree on a row, one suite goes red. A missing/empty
    /// corpus fails the test — a vacuously-green parity test is the green that lies.
    /// </summary>
    public class CrossVantageConsensusGoldenVectorTests
    {
        private sealed class Case
        {
            public string name { get; set; } = "";
            public List<List<string>> vantages { get; set; } = new();
            public List<string> trustedReference { get; set; } = new();
            public string expected { get; set; } = "";
        }
        private sealed class Corpus { public List<Case> cases { get; set; } = new(); }

        private static string CorpusPath([CallerFilePath] string thisFile = "")
        {
            var dir = Path.GetDirectoryName(thisFile) ?? throw new InvalidOperationException("no caller dir");
            return Path.Combine(Path.GetFullPath(Path.Combine(dir, "..", "..", "..")),
                                "corpus", "b281-consensus", "decision-table.json");
        }

        private static CrossVantageOutcome Parse(string s) => s switch
        {
            "match" => CrossVantageOutcome.Match,
            "mismatch" => CrossVantageOutcome.Mismatch,
            "inconclusive" => CrossVantageOutcome.Inconclusive,
            "insufficient" => CrossVantageOutcome.Insufficient,
            "noData" => CrossVantageOutcome.NoData,
            _ => throw new ArgumentException($"unknown expected outcome '{s}'")
        };

        [Fact]
        public void EveryGoldenRowMatchesPolicy()
        {
            var path = CorpusPath();
            Assert.True(File.Exists(path), $"B281 golden vector missing at {path}");
            var json = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(json), "B281 golden vector is empty — refusing to pass vacuously");

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var corpus = JsonSerializer.Deserialize<Corpus>(json, opts);
            Assert.NotNull(corpus);
            Assert.True(corpus!.cases.Count >= 10, "golden vector must carry the full decision table");

            foreach (var c in corpus.cases)
            {
                var vantages = c.vantages.Select(v => new HashSet<string>(v)).ToList();
                var reference = new HashSet<string>(c.trustedReference);
                var got = CrossVantageConsensus.ClassifyPeerConsensus(vantages, reference);
                Assert.True(got == Parse(c.expected),
                    $"B281 golden row '{c.name}': expected {c.expected}, got {got}");
            }
        }
    }
}
