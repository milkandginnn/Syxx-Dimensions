using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// F-01 / B174 — when the system resolver is undetectable, Syxx substitutes a public resolver.
    /// Mirror's whole method is a local-versus-public comparison, so under substitution local IS
    /// public and the comparison degenerates into comparing a resolver against itself. The Mirror
    /// section must therefore be capped at Degraded and can never come back clean on that evidence.
    ///
    /// This existed only on the Swift side (Synthesizer.swift:82 and :772). Windows and Linux had
    /// no substituted-resolver input and no cap, so the same run could return a clean Mirror
    /// section from evidence macOS treats as too degenerate to conclude from. Neither platform had
    /// a test for the cap.
    ///
    /// Twin of macOS/Tests/SyxxKitTests/ResolverSubstitutedCapTests.swift.
    /// </summary>
    public class ResolverSubstitutedCapTests
    {
        private static readonly DateTimeOffset FixedNow =
            DateTimeOffset.FromUnixTimeSeconds(1_752_268_477);

        /// A comparison that reaches Mirror's Negative branch: not inconclusive, no redirection,
        /// not degraded, and with local records present so the B136 "compared nothing" guard does
        /// not divert it to Inconclusive (Synthesizer.cs:441).
        private static SectionInputs InputsWithCleanMirror() => new()
        {
            Comparison = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord>
                {
                    new() { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" },
                },
                LocalOutcome = new ResolverQueryOutcome
                {
                    Status = DNSResponseStatus.Success,
                    Records = new List<DNSRecord>
                    {
                        new() { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" },
                    },
                },
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
                {
                    ["Cloudflare"] = new()
                    {
                        Status = DNSResponseStatus.Success,
                        Records = new List<DNSRecord>
                        {
                            new() { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" },
                        },
                    },
                },
                Result = VantageComparisonResult.Consistent,
            },
        };

        private static SectionStatus MirrorStatus(Conclusion c) =>
            c.SectionStatuses.Single(s => s.Section == Section.Mirror).Status;

        [Fact]
        public void ResolverSubstituted_CapsMirrorAtDegraded()
        {
            // Precondition, asserted rather than assumed: without substitution these inputs really
            // do produce a clean Mirror. If this ever stops holding, the cap assertion below would
            // pass vacuously and prove nothing.
            var uncapped = Synthesizer.Conclude(InputsWithCleanMirror(), now: FixedNow);
            Assert.Equal(SectionStatus.Negative, MirrorStatus(uncapped));

            var inputs = InputsWithCleanMirror();
            inputs.ResolverSubstituted = true;
            var capped = Synthesizer.Conclude(inputs, now: FixedNow);

            Assert.Equal(SectionStatus.Degraded, MirrorStatus(capped));
        }

        /// The cap lowers a clean Mirror; it must not raise or otherwise disturb a section that is
        /// already at or below Degraded, which would be the cap inventing severity.
        [Fact]
        public void ResolverSubstituted_DoesNotDisturbANonCleanMirror()
        {
            var inputs = InputsWithCleanMirror();
            inputs.Comparison = null; // Mirror has nothing to compare
            var baseline = MirrorStatus(Synthesizer.Conclude(inputs, now: FixedNow));

            var substituted = InputsWithCleanMirror();
            substituted.Comparison = null;
            substituted.ResolverSubstituted = true;

            Assert.Equal(baseline, MirrorStatus(Synthesizer.Conclude(substituted, now: FixedNow)));
        }
    }
}
