using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// Local-resolver honesty proof (twin of macOS UnboundResolverTests). The app may say "Unbound is
    /// protecting your DNS" ONLY when the local resolver actually answered a real query AND the system
    /// was switched to it AND a post-switch sanity lookup succeeded. A process merely spawned, or a
    /// `Set-DnsClientServerAddress` call that merely returned, must never render as protection.
    public class UnboundResolverTests
    {
        private static UnboundResult Classify(
            bool binary = true, bool started = true, bool bindDenied = false, bool local = true,
            bool switched = true, bool switchDenied = false, bool postOk = true, bool reverted = false)
            => UnboundResolver.ClassifyEnable(
                port: 53, priorServers: new[] { "9.9.9.9" }, binaryFound: binary, processStarted: started,
                bindElevationDenied: bindDenied, localResolves: local, systemSwitched: switched,
                switchElevationDenied: switchDenied, postSwitchSanityOk: postOk, autoReverted: reverted);

        [Fact]
        public void AllConfirmedIsEnabled()
        {
            var r = Classify();
            Assert.True(r.IsEnabled);
            Assert.Equal(UnboundResultKind.Enabled, r.Kind);
        }

        [Fact]
        public void NoBinaryIsNotInstalled() =>
            Assert.Equal(UnboundResultKind.NotInstalled, Classify(binary: false).Kind);

        [Fact]
        public void BindDeniedIsElevationRequired() =>
            Assert.Equal(UnboundResultKind.ElevationRequired, Classify(started: false, bindDenied: true).Kind);

        [Fact]
        public void StartFailureIsFailed() =>
            Assert.Equal(UnboundResultKind.Failed, Classify(started: false, bindDenied: false).Kind);

        [Fact]
        public void StartedButLocalDoesNotResolveIsInconclusive()
        {
            var r = Classify(local: false);
            Assert.False(r.IsEnabled);
            Assert.Equal(UnboundResultKind.Inconclusive, r.Kind);
        }

        [Fact]
        public void LocalWorksButNotSwitchedIsInconclusive()
        {
            var r = Classify(switched: false);
            Assert.False(r.IsEnabled);
            Assert.Equal(UnboundResultKind.Inconclusive, r.Kind);
        }

        [Fact]
        public void SwitchDeniedIsElevationRequired() =>
            Assert.Equal(UnboundResultKind.ElevationRequired, Classify(switched: false, switchDenied: true).Kind);

        // The failsafe: switched but post-switch sanity failed -> auto-reverted -> honest Failed, NOT Enabled.
        [Fact]
        public void PostSwitchSanityFailIsFailedNotEnabled()
        {
            var r = Classify(postOk: false, reverted: true);
            Assert.False(r.IsEnabled);
            Assert.Equal(UnboundResultKind.Failed, r.Kind);
        }

        [Fact]
        public void ConfigIsFullRecursionWithDNSSECByDefault()
        {
            var conf = UnboundResolver.GenerateConfig(new UnboundConfig(
                RootAnchorPath: "/x/root.key", WorkingDir: "/x", PidFile: "/x/p", LogFile: "/x/l", Port: 53));
            Assert.Contains("interface: 127.0.0.1@53", conf);
            Assert.Contains("auto-trust-anchor-file: \"/x/root.key\"", conf);
            Assert.Contains("access-control: 0.0.0.0/0 refuse", conf);
            Assert.Contains("username: \"\"", conf);       // parity with macOS: don't drop to a nonexistent user
            Assert.DoesNotContain("forward-zone", conf);   // v1 default is full recursion
        }

        [Fact]
        public void ConfigForwardOverDoTIsOptIn()
        {
            var r = EncryptedDNSConfig.Catalog[0]; // cloudflare
            var conf = UnboundResolver.GenerateConfig(new UnboundConfig(
                RootAnchorPath: "/x/root.key", WorkingDir: "/x", PidFile: "/x/p", LogFile: "/x/l",
                Port: 53, ForwardOverDoT: r));
            Assert.Contains("forward-zone", conf);
            Assert.Contains("forward-tls-upstream: yes", conf);
            Assert.Contains("one.one.one.one", conf);
        }
    }
}
