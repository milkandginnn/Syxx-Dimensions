using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A5 — continuous monitoring. The load-bearing test here is the FIRST one: a run with no
    // baseline must never read as "nothing changed". Everything else guards that a drift is
    // reported as a reason to re-check rather than as a result Sentinel cannot support.
    public class ContinuousMonitorTests
    {
        private static NetworkConfigSnapshot Snap(
            IEnumerable<string>? resolvers = null,
            IEnumerable<string>? interfaces = null,
            bool vpn = false,
            bool encrypted = false) => new NetworkConfigSnapshot
            {
                Resolvers = (resolvers ?? new[] { "192.168.1.1" }).ToList(),
                ActiveInterfaces = (interfaces ?? new[] { "en0" }).ToList(),
                VpnActive = vpn ? ConditionState.Yes : ConditionState.No,
                DnsEncrypted = encrypted,
            };

        [Fact]
        public void NoBaseline_IsBaselineEstablished_NeverNoChange()
        {
            // The honesty crux: we have never looked before, so we cannot say nothing changed.
            var tick = ContinuousMonitor.Evaluate(previous: null, current: Snap());

            Assert.Equal(MonitorState.BaselineEstablished, tick.State);
            Assert.NotEqual(MonitorState.NoChange, tick.State);
            Assert.Empty(tick.Alerts);
            Assert.False(tick.RecheckRecommended);
            // Must not claim stability.
            Assert.DoesNotContain("no change", tick.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Nothing has been compared yet", tick.Summary);
        }

        [Fact]
        public void IdenticalSnapshots_ReportNoChange_AndRaiseNoAlert()
        {
            var tick = ContinuousMonitor.Evaluate(Snap(), Snap());
            Assert.Equal(MonitorState.NoChange, tick.State);
            Assert.Empty(tick.Changes);
            Assert.Empty(tick.Alerts);
            Assert.False(tick.RecheckRecommended);
        }

        [Fact]
        public void ResolverDrift_RaisesExactlyOneAlert_AndRecommendsARecheck()
        {
            // The plan's acceptance bar: a real config drift between two scans raises one alert.
            var tick = ContinuousMonitor.Evaluate(
                Snap(resolvers: new[] { "192.168.1.1" }),
                Snap(resolvers: new[] { "203.0.113.9" }));

            Assert.Equal(MonitorState.Changed, tick.State);
            Assert.Single(tick.Changes);
            Assert.Single(tick.Alerts);
            Assert.True(tick.RecheckRecommended);
            Assert.Equal(ConfigChangeKind.ResolversChanged, tick.Changes[0].Kind);
            // The old and new resolver must both reach the user.
            Assert.Contains("192.168.1.1", tick.Alerts[0].Detail);
            Assert.Contains("203.0.113.9", tick.Alerts[0].Detail);
        }

        [Fact]
        public void ADriftIsNotAResult_ItMustNotClaimTampering()
        {
            // Sentinel sees configuration, not honesty. A resolver change is a coffee-shop DHCP
            // lease OR an attack; this layer must not pretend to know which.
            var tick = ContinuousMonitor.Evaluate(
                Snap(resolvers: new[] { "192.168.1.1" }),
                Snap(resolvers: new[] { "203.0.113.9" }));

            Assert.Contains("not proof of tampering", tick.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hijacked", tick.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("you are being attacked", tick.Summary, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LosingEncryption_IsAWarning_GainingItIsNot()
        {
            var lost = ContinuousMonitor.Evaluate(Snap(encrypted: true), Snap(encrypted: false));
            var gained = ContinuousMonitor.Evaluate(Snap(encrypted: false), Snap(encrypted: true));

            Assert.Equal(Severity.Warning, lost.Alerts.Single().Severity);
            Assert.Equal(Severity.Info, gained.Alerts.Single().Severity);
            // Asymmetry is the point: only one of these exposes the user.
            Assert.NotEqual(lost.Alerts.Single().Severity, gained.Alerts.Single().Severity);
            Assert.Contains("visible to the network", lost.Alerts.Single().Detail);
        }

        [Fact]
        public void LosingVpn_IsAWarning_GainingItIsNot()
        {
            var lost = ContinuousMonitor.Evaluate(Snap(vpn: true), Snap(vpn: false));
            var gained = ContinuousMonitor.Evaluate(Snap(vpn: false), Snap(vpn: true));

            Assert.Equal(Severity.Warning, lost.Alerts.Single().Severity);
            Assert.Equal(Severity.Info, gained.Alerts.Single().Severity);
        }

        [Fact]
        public void EveryChange_ProducesExactlyOneAlert_NoneSwallowed()
        {
            // Four simultaneous drifts must surface four alerts — a change that is detected but
            // not surfaced is a silently swallowed warning.
            var tick = ContinuousMonitor.Evaluate(
                Snap(resolvers: new[] { "192.168.1.1" }, interfaces: new[] { "en0" }, vpn: true, encrypted: true),
                Snap(resolvers: new[] { "203.0.113.9" }, interfaces: new[] { "en1" }, vpn: false, encrypted: false));

            Assert.Equal(4, tick.Changes.Count);
            Assert.Equal(tick.Changes.Count, tick.Alerts.Count);
            Assert.All(tick.Alerts, a => Assert.False(string.IsNullOrWhiteSpace(a.Headline)));
        }

        [Fact]
        public void Evaluate_RejectsANullCurrentSnapshot_RatherThanGuessing()
        {
            Assert.Throws<ArgumentNullException>(() => ContinuousMonitor.Evaluate(Snap(), null!));
        }
    }
}
