using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// W16: binds the C# RRSIG validity-span plausibility guard (B047) to the language-neutral
    /// fixture at <c>corpus/dnssec/validity-span.json</c>, which the Python server test reads too
    /// (and a Swift test can read unchanged).
    ///
    /// The defect this closes was never a wrong value — all three runtimes already carry
    /// 63_072_000. It was enforcement drift: three bare literals with nothing binding them
    /// together, so any one of them could be retyped and only that runtime would notice. These
    /// tests take BOTH the limit and the accept/reject decisions from the shared file, so a
    /// divergence in any runtime shows up as a red test in that runtime.
    ///
    /// A missing, empty, or degenerate corpus THROWS rather than passing vacuously — a green that
    /// asserts nothing is the green that lies.
    /// </summary>
    public class DnssecValiditySpanCorpusTests
    {
        private sealed class Normative
        {
            public long max_validity_span_seconds { get; set; }
        }

        private sealed class SpanCase
        {
            public string name { get; set; } = "";
            public long inception { get; set; }
            public long expiration { get; set; }
            public long now { get; set; }
            public bool window_valid { get; set; }
            public string reason { get; set; } = "";
        }

        private sealed class Fixture
        {
            public Normative normative { get; set; } = new();
            public List<SpanCase> cases { get; set; } = new();
        }

        private static string FixturePath([CallerFilePath] string thisFile = "")
        {
            var dir = Path.GetDirectoryName(thisFile) ?? throw new InvalidOperationException("no caller dir");
            return Path.Combine(Path.GetFullPath(Path.Combine(dir, "..", "..", "..")),
                                "corpus", "dnssec", "validity-span.json");
        }

        private static Fixture Load()
        {
            var path = FixturePath();
            Assert.True(File.Exists(path), $"W16 validity-span fixture missing at {path}");
            var json = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(json),
                "W16 validity-span fixture is empty — refusing to pass vacuously");
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var fixture = JsonSerializer.Deserialize<Fixture>(json, opts);
            Assert.NotNull(fixture);
            return fixture!;
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime AtEpoch(long seconds) => UnixEpoch.AddSeconds(seconds);

        /// <summary>
        /// Requirement 1: the configured limit is exactly the ratified value. This is the assertion
        /// that catches a retyped literal in DNSSECVerifier.cs.
        /// </summary>
        [Fact]
        public void ConfiguredLimitMatchesRatifiedFixtureValue()
        {
            var fixture = Load();
            Assert.Equal(63_072_000L, fixture.normative.max_validity_span_seconds);
            Assert.Equal(fixture.normative.max_validity_span_seconds,
                         (long)DNSSECVerifier.MaxPlausibleValiditySpanSeconds);
        }

        /// <summary>
        /// The corpus must actually exercise both outcomes and both boundary directions, or the
        /// row-driven test below could pass while asserting nothing interesting.
        /// </summary>
        [Fact]
        public void FixtureIsNotDegenerate()
        {
            var fixture = Load();
            Assert.True(fixture.cases.Count >= 8,
                "W16 fixture must carry the full boundary set, not a token row");
            Assert.Contains(fixture.cases, c => c.window_valid);
            Assert.Contains(fixture.cases, c => !c.window_valid);

            long limit = fixture.normative.max_validity_span_seconds;
            long Span(SpanCase c) => (c.expiration - c.inception) & 0xFFFF_FFFFL;

            // Requirement 2 and 3, present as data: a window exactly AT the limit that is accepted,
            // and one second above it that is rejected.
            Assert.Contains(fixture.cases, c => Span(c) == limit && c.window_valid);
            Assert.Contains(fixture.cases, c => Span(c) == limit + 1 && !c.window_valid);

            // Requirement 4, present as data: wrap-adjacent rows where the raw (non-modular)
            // difference is negative, so only mod-2^32 arithmetic yields the intended span.
            Assert.Contains(fixture.cases, c => c.expiration < c.inception && Span(c) == limit && c.window_valid);
            Assert.Contains(fixture.cases, c => c.expiration < c.inception && Span(c) > limit && !c.window_valid);
        }

        /// <summary>
        /// Requirements 2, 3 and 4: every row in the shared fixture — boundary, one-second-over, and
        /// the serial-wrap-adjacent cases — decides the same way in C# as it must in Python and Swift.
        /// </summary>
        [Fact]
        public void EveryFixtureRowMatchesTheGuard()
        {
            var fixture = Load();
            foreach (var c in fixture.cases)
            {
                bool got = DNSSECVerifier.RRSIGWindowCurrentlyValid(
                    unchecked((uint)c.inception), unchecked((uint)c.expiration), AtEpoch(c.now));
                Assert.True(got == c.window_valid,
                    $"W16 row '{c.name}': expected window_valid={c.window_valid}, got {got}. {c.reason}");
            }
        }

        /// <summary>
        /// Failure case (house rule): proves the row assertions above are not vacuous. Nudging the
        /// span of every accepted at-the-limit row one second wider must flip it to rejected — if it
        /// does not, the guard is not the thing deciding these rows.
        /// </summary>
        [Fact]
        public void AtLimitRowsFlipWhenNudgedOneSecondWider()
        {
            var fixture = Load();
            long limit = fixture.normative.max_validity_span_seconds;
            var atLimit = fixture.cases
                .Where(c => ((c.expiration - c.inception) & 0xFFFF_FFFFL) == limit && c.window_valid)
                .ToList();
            Assert.NotEmpty(atLimit);
            foreach (var c in atLimit)
            {
                uint widened = unchecked((uint)c.expiration + 1);
                Assert.False(
                    DNSSECVerifier.RRSIGWindowCurrentlyValid(
                        unchecked((uint)c.inception), widened, AtEpoch(c.now)),
                    $"W16 row '{c.name}' widened by one second must be rejected by the span guard");
            }
        }
    }
}
