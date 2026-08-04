using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // C6 — lookalike domain detection (parity with macOS LookalikeDetectorTests). Each test names
    // its fail state; the changelog mutation matrix demonstrates each reds under the defect.
    public class LookalikeDetectorTests
    {
        // Acceptance bar: a Cyrillic-homoglyph impersonation of a brand must alert.
        // Fail state: don't fold confusables -> the spoof reads clean.
        [Fact]
        public void CyrillicHomoglyphOfBrandIsFlagged()
        {
            var f = LookalikeDetector.Analyze("p\u0430ypal.com"); // Cyrillic a
            Assert.NotNull(f);
            Assert.Equal("paypal.com", f!.Brand);
            Assert.Equal(LookalikeDetector.Reason.MixedScript, f.Reason);
        }

        // A digit-for-letter confusable ("paypa1.com") is a homoglyph too.
        [Fact]
        public void DigitHomoglyphIsFlagged()
        {
            var f = LookalikeDetector.Analyze("paypa1.com");
            Assert.NotNull(f);
            Assert.Equal("paypal.com", f!.Brand);
            Assert.Equal(LookalikeDetector.Reason.Homoglyph, f.Reason);
        }

        // ASCII typosquat one edit from the brand.
        [Fact]
        public void TyposquatIsFlagged()
        {
            var f = LookalikeDetector.Analyze("gooogle.com");
            Assert.NotNull(f);
            Assert.Equal("google.com", f!.Brand);
            Assert.Equal(LookalikeDetector.Reason.Typosquat, f.Reason);
        }

        // The genuine brand never flags itself. Load-bearing guard: typosquat branch requires
        // distance EXACTLY 1. Fail state: widen to <= 1 -> real logins false-alarm.
        [Fact]
        public void GenuineBrandIsNotFlagged()
        {
            Assert.Null(LookalikeDetector.Analyze("paypal.com"));
            Assert.Null(LookalikeDetector.Analyze("www.google.com"));
        }

        // Unrelated domains never match a brand (false-positive direction).
        [Fact]
        public void UnrelatedDomainIsNotFlagged()
        {
            Assert.Null(LookalikeDetector.Analyze("example.com"));
            Assert.Null(LookalikeDetector.Analyze("wikipedia.org"));
            Assert.Null(LookalikeDetector.Analyze("news.ycombinator.com"));
        }

        [Fact]
        public void SkeletonFoldsConfusables()
        {
            Assert.Equal("paypal", LookalikeDetector.Skeleton("p\u0430yp\u0430l"));
            Assert.Equal("google", LookalikeDetector.Skeleton("g00gle"));
        }

        [Fact]
        public void MixedScriptDetection()
        {
            Assert.True(LookalikeDetector.IsMixedScript("p\u0430ypal"));
            Assert.False(LookalikeDetector.IsMixedScript("paypal"));
            Assert.False(LookalikeDetector.IsMixedScript("\u0440\u0430\u0439"));
        }

        [Fact]
        public void EditDistanceRespectsCap()
        {
            Assert.Equal(0, LookalikeDetector.EditDistance("google", "google", 1));
            Assert.Equal(1, LookalikeDetector.EditDistance("google", "gooogle", 1));
            Assert.Equal(2, LookalikeDetector.EditDistance("google", "yahoosearch", 1));
        }
    }


    // C7 — per-app resolver attribution (parity with macOS ResolverAttributionTests). Each test
    // names its fail state; the changelog mutation matrix demonstrates each reds under the defect.
    public class ResolverAttributionTests
    {
        // Acceptance bar: an app querying a resolver the system did NOT configure is flagged.
        // Fail state: don't subtract the system set -> the bypass app reads clean.
        [Fact]
        public void HardcodedResolverIsFlagged()
        {
            var usage = new Dictionary<string, HashSet<string>>
            {
                ["Chrome"] = new() { "8.8.8.8" },
                ["Safari"] = new() { "192.168.1.1" },
            };
            var r = ResolverAttribution.Classify(usage, new HashSet<string> { "192.168.1.1" });
            var chrome = r.Attributions.First(a => a.Process == "Chrome");
            Assert.True(chrome.UsesHardcodedResolver);
            Assert.Equal(new[] { "8.8.8.8" }, chrome.OffSystemResolvers);
            Assert.Equal(new[] { "Chrome" }, r.Flagged.Select(a => a.Process).ToArray());
        }

        // An app using ONLY the system resolver is not flagged.
        [Fact]
        public void SystemResolverOnlyIsNotFlagged()
        {
            var usage = new Dictionary<string, HashSet<string>> { ["Safari"] = new() { "192.168.1.1" } };
            var r = ResolverAttribution.Classify(usage, new HashSet<string> { "192.168.1.1", "192.168.1.2" });
            Assert.False(r.Attributions[0].UsesHardcodedResolver);
            Assert.Empty(r.Flagged);
        }

        // No baseline -> coverage-limited, never a clean or a fabricated flag.
        // Fail state: flag off-system with an empty baseline -> every app "bypasses" nothing.
        [Fact]
        public void EmptyBaselineIsUnknownNeverFlagged()
        {
            var usage = new Dictionary<string, HashSet<string>> { ["Chrome"] = new() { "8.8.8.8" } };
            var r = ResolverAttribution.Classify(usage, new HashSet<string>());
            Assert.True(r.BaselineUnknown);
            Assert.Empty(r.Flagged);
            Assert.Empty(r.Attributions[0].OffSystemResolvers);
        }

        // usage() keys the resolver on the PORT-53 side, not the QR bit (D063 forgeable direction).
        // Fail state: key on QR -> a forged QR bit misattributes the resolver.
        [Fact]
        public void UsageKeysResolverOnPort53NotQrBit()
        {
            var rec = new DNSCaptureRecord
            {
                Process = new ProcessInfoReport { Name = "curl", Pid = 900 },
                Timestamp = "t",
                Packet = new DNSPacketReport { Qr = "Query" },   // forged QR on an inbound packet
                Network = new NetworkContextReport
                {
                    SourceIP = "8.8.8.8", DestIP = "10.0.0.5", SourcePort = 53, DestPort = 54321,
                    Direction = "Inbound Response"
                }
            };
            var usage = ResolverAttribution.Usage(new[] { rec });
            Assert.Equal(new HashSet<string> { "8.8.8.8" }, usage["curl"]);
        }
    }


    // C5 — home network baseline (parity with macOS HomeBaselineTests). Each test names its fail
    // state; the changelog mutation matrix demonstrates each reds under the defect.
    public class HomeBaselineTests
    {
        private static NetworkConfigSnapshot Snap(List<string> resolvers, bool vpn = false, bool enc = false,
                                                  List<string>? ifaces = null) => new()
        {
            Resolvers = resolvers, ActiveInterfaces = ifaces ?? new() { "en0" },
            VpnActive = vpn ? ConditionState.Yes : ConditionState.No, DnsEncrypted = enc
        };

        // Acceptance bar: a different network lists the REAL deltas from home.
        // Fail state: don't diff -> "matches home" on a network that plainly differs.
        [Fact]
        public void DifferentNetworkListsDeltas()
        {
            var home = Snap(new() { "192.168.1.1" }, enc: true);
            var cafe = Snap(new() { "10.0.0.1" }, enc: false);
            var c = HomeBaseline.Compare(home, cafe);
            Assert.True(c.HasBaseline);
            Assert.False(c.MatchesHome);
            Assert.Equal(2, c.Differences.Count);
            Assert.Contains("2 ways", c.Summary);
        }

        [Fact]
        public void IdenticalNetworkMatchesHome()
        {
            var home = Snap(new() { "192.168.1.1" }, enc: true);
            var c = HomeBaseline.Compare(home, home);
            Assert.True(c.MatchesHome);
            Assert.Empty(c.Differences);
        }

        // No baseline -> cannot judge, NEVER "matches home".
        // Fail state: MatchesHome true when home is null -> un-profiled machine claims every net is home.
        [Fact]
        public void NoBaselineIsUnknownNeverMatch()
        {
            var c = HomeBaseline.Compare(null, Snap(new() { "10.0.0.1" }));
            Assert.False(c.HasBaseline);
            Assert.False(c.MatchesHome);
            Assert.Empty(c.Differences);
            Assert.Contains("No home baseline", c.Summary);
        }

        [Fact]
        public void SingleDeltaSummarySingular()
        {
            var home = Snap(new() { "192.168.1.1" });
            var current = Snap(new() { "192.168.1.1" }, vpn: true);
            var c = HomeBaseline.Compare(home, current);
            Assert.Single(c.Differences);
            Assert.Contains("1 way.", c.Summary);
        }
    }


    // D045-class guard: server-supplied URLs must be gated before the OS opener sees them.
    // Each test names its fail state; the changelog mutation matrix demonstrates each reds.
    public class RemoteUrlPolicyTests
    {
        // Our own MOTD origins are allowed (exact domain and subdomain).
        [Theory]
        [InlineData("https://syxx.app/whats-new")]
        [InlineData("https://linmotd.syxx.app/")]
        [InlineData("https://macmotd.syxx.app/")]
        [InlineData("https://winmotd.syxx.app/")]
        public void OwnHttpsOriginsAllowed(string url)
            => Assert.True(RemoteUrlPolicy.Evaluate(url).IsAllowed, $"{url} is our own https origin");

        // The load-bearing check: a non-https scheme is refused. A compromised MOTD response
        // handing the desktop a file:// or smb:// URI is the attack this closes.
        // Fail state: drop the scheme check -> these pass and the OS opener acts on them.
        [Theory]
        [InlineData("file:///etc/passwd")]
        [InlineData("smb://attacker/share")]
        [InlineData("javascript:alert(1)")]
        [InlineData("http://syxx.app/")]     // plaintext is refused even on our own host
        public void NonHttpsSchemeRefused(string url)
        {
            var r = RemoteUrlPolicy.Evaluate(url);
            Assert.False(r.IsAllowed);
            Assert.NotNull(r.Reason);
        }

        // A foreign host is refused even over https.
        // Fail state: allow any https -> a spoofed response redirects the user anywhere.
        [Fact]
        public void ForeignHostRefused()
        {
            var r = RemoteUrlPolicy.Evaluate("https://evil.example.com/");
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedHost, r.Decision);
        }

        // The suffix trap: `evilsyxx.app` must NOT match the `syxx.app` allowlist entry.
        // Fail state: use EndsWith(domain) without the leading dot -> this passes and a
        // lookalike domain is treated as our own.
        [Fact]
        public void LookalikeSuffixHostRefused()
        {
            Assert.False(RemoteUrlPolicy.Evaluate("https://evilsyxx.app/").IsAllowed);
            Assert.False(RemoteUrlPolicy.Evaluate("https://syxx.app.evil.com/").IsAllowed);
        }

        // Credential-bearing and odd-port URLs are phishing shapes, never our own content.
        [Fact]
        public void CredentialsAndNonDefaultPortRefused()
        {
            Assert.False(RemoteUrlPolicy.Evaluate("https://user:pass@syxx.app/").IsAllowed);
            Assert.False(RemoteUrlPolicy.Evaluate("https://syxx.app:8443/").IsAllowed);
        }

        // Empty/garbage input is refused with a reason, never treated as openable.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a url")]
        public void MalformedRefused(string? url)
        {
            var r = RemoteUrlPolicy.Evaluate(url);
            Assert.False(r.IsAllowed);
            Assert.Equal(RemoteUrlPolicy.Decision.RefusedMalformed, r.Decision);
        }

        // Phase 2.2: AuthorizeForNavigation is the single entry every UI open path must use.
        // Evaluate-allowed does not by itself hand the caller a Uri; Authorize does, and only then.
        [Fact]
        public void AuthorizeForNavigation_Allowed_ReturnsAbsoluteHttpsUri()
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation("https://syxx.app/download", out var eval);
            Assert.True(eval.IsAllowed);
            Assert.NotNull(uri);
            Assert.Equal("https", uri!.Scheme, ignoreCase: true);
            Assert.Equal("syxx.app", uri.Host, ignoreCase: true);
        }

        [Fact]
        public void AuthorizeForNavigation_Refused_ReturnsNull_NotPartialUri()
        {
            var uri = RemoteUrlPolicy.AuthorizeForNavigation("https://evil.example.com/", out var eval);
            Assert.False(eval.IsAllowed);
            Assert.Null(uri);
        }
    }


    // E061 / B034 parity: ScopedResolver transport + encrypted-DNS tri-state core (macOS SyxxKitTests).
    public class NetworkKeyStateTests
    {
        [Fact]
        public void EncryptedDNSStatusYesForConfiguredDoH()
        {
            var scoped = new List<ScopedResolver>
            {
                new() { Address = "1.1.1.1", Protocol = DnsProtocol.Doh, Port = 443, IsScoped = true }
            };
            var result = NetworkKeyState.EncryptedDNSStatusCore(new[] { "1.1.1.1" }, scoped);
            Assert.Equal(ConditionState.Yes, result.State);
            Assert.Contains("DoH", result.Evidence);
        }

        [Fact]
        public void EncryptedDNSStatusUnknownForTunnelOnlyResolvers()
        {
            var scoped = new List<ScopedResolver>
            {
                new() { Address = "10.0.0.1", Interface = "utun4", IsScoped = true }
            };
            var result = NetworkKeyState.EncryptedDNSStatusCore(new[] { "10.0.0.1" }, scoped);
            Assert.Equal(ConditionState.Unknown, result.State);
            Assert.Contains("tunneled", result.Evidence, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EncryptedDNSStatusUnknownForLoopbackProxy()
        {
            var result = NetworkKeyState.EncryptedDNSStatusCore(new[] { "127.0.0.1" }, Array.Empty<ScopedResolver>());
            Assert.Equal(ConditionState.Unknown, result.State);
            Assert.Contains("loopback", result.Evidence, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EncryptedDNSStatusNoForPlainUDP()
        {
            var scoped = new List<ScopedResolver>
            {
                new() { Address = "8.8.8.8", IsScoped = false }
            };
            var result = NetworkKeyState.EncryptedDNSStatusCore(new[] { "8.8.8.8" }, scoped);
            Assert.Equal(ConditionState.No, result.State);
            Assert.Contains("plain", result.Evidence, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EncryptedDNSStatusUnknownForEmptyServers()
        {
            var result = NetworkKeyState.EncryptedDNSStatusCore(Array.Empty<string>(), Array.Empty<ScopedResolver>());
            Assert.Equal(ConditionState.Unknown, result.State);
            Assert.Contains("No resolvers", result.Evidence);
        }

        [Fact]
        public void ParseResolvectlStatusCapturesDoHPerLink()
        {
            const string sample = """
                Link 2 (wlan0)
                    Current Scopes: DNS
                         DNS Servers: 1.1.1.1#cloudflare-dns.com
                      DNSOverHTTPS: yes
                """;
            var resolvers = NetworkKeyState.ParseResolvectlStatus(sample);
            Assert.Single(resolvers);
            Assert.Equal("1.1.1.1", resolvers[0].Address);
            Assert.Equal("wlan0", resolvers[0].Interface);
            Assert.Equal(DnsProtocol.Doh, resolvers[0].Protocol);
            Assert.Equal((ushort)443, resolvers[0].Port);
            Assert.Contains("cloudflare-dns.com", resolvers[0].MatchDomains);
        }

        [Fact]
        public void ParseResolvectlStatusCapturesDoTPerLink()
        {
            const string sample = """
                Link 3 (eth0)
                         DNS Servers: 9.9.9.9
                      DNS Over TLS: yes
                """;
            var resolvers = NetworkKeyState.ParseResolvectlStatus(sample);
            Assert.Single(resolvers);
            Assert.Equal("9.9.9.9", resolvers[0].Address);
            Assert.Equal("eth0", resolvers[0].Interface);
            Assert.Equal(DnsProtocol.Dot, resolvers[0].Protocol);
            Assert.Equal((ushort)853, resolvers[0].Port);
        }
    }


    // Encrypt-all-DNS honesty proof (parity with macOS EncryptedDNSConfigTests). The app may say
    // "your DNS is encrypted" ONLY when apply succeeded, the system reports active, AND a real
    // encrypted query completed. Installed-but-unconfirmed must never read as protection.
    public class EncryptedDNSConfigTests
    {
        private static EncryptedDNSApplyResult Classify(bool apply = true, bool elev = false, bool active = true, bool probe = true)
            => EncryptedDNSConfig.ClassifyApply("cloudflare", EncryptedDNSTransport.Doh, apply, elev, active, probe);

        [Fact]
        public void AllThreeTrueIsEnabled() => Assert.True(Classify().IsEnabled);

        // Fail state: report Enabled without the probe -> installed==in-effect false-secure.
        [Fact]
        public void SaveOkButNotActiveIsInconclusive()
        {
            var r = Classify(active: false);
            Assert.False(r.IsEnabled);
            Assert.Equal(EncryptedDNSApplyKind.Inconclusive, r.Kind);
        }

        [Fact]
        public void ActiveButProbeDidNotCompleteIsInconclusive()
        {
            var r = Classify(probe: false);
            Assert.False(r.IsEnabled);
            Assert.Equal(EncryptedDNSApplyKind.Inconclusive, r.Kind);
        }

        [Fact]
        public void ApplyFailedIsFailedNotEnabled()
        {
            var r = Classify(apply: false);
            Assert.False(r.IsEnabled);
            Assert.Equal(EncryptedDNSApplyKind.Failed, r.Kind);
        }

        [Fact]
        public void ElevationRejectionIsItsOwnState()
            => Assert.Equal(EncryptedDNSApplyKind.ElevationRequired, Classify(apply: false, elev: true).Kind);

        [Fact]
        public void CatalogWellFormedAndDefaultExists()
        {
            Assert.NotNull(EncryptedDNSConfig.Resolver(EncryptedDNSConfig.DefaultResolverId));
            foreach (var r in EncryptedDNSConfig.Catalog)
            {
                Assert.StartsWith("https://", r.DohUrl);
                Assert.False(string.IsNullOrEmpty(r.DotHost));
                Assert.NotEmpty(r.Addresses);
            }
        }
    }


    // G-4 settings store (parity with macOS SyxxSettingsTests). Defaults safe, corrupt file falls
    // back, round trip preserves every field.
    public class SyxxSettingsTests
    {
        private static string TempPath() =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "syxx-settings-" + System.Guid.NewGuid().ToString("N"), "settings.json");

        [Fact]
        public void DefaultsAreSafeRestingState()
        {
            var d = new SyxxSettings();
            Assert.False(d.EncryptedDnsEnabled);   // never on without the user asking
            Assert.Equal(EncryptedDNSConfig.DefaultResolverId, d.EncryptedDnsResolverId);
            Assert.Null(d.ScanResolverId);
        }

        [Fact]
        public void RoundTripPreservesEveryField()
        {
            var path = TempPath();
            var s = new SyxxSettings
            {
                EncryptedDnsEnabled = true, EncryptedDnsResolverId = "quad9",
                EncryptedDnsTransport = EncryptedDNSTransport.Dot, ScanResolverId = "google",
                NotificationsEnabled = false, TelemetryEnabled = false
            };
            Assert.True(SyxxSettingsStore.Save(s, path));
            var loaded = SyxxSettingsStore.Load(path);
            Assert.Equal(s.EncryptedDnsEnabled, loaded.EncryptedDnsEnabled);
            Assert.Equal(s.EncryptedDnsResolverId, loaded.EncryptedDnsResolverId);
            Assert.Equal(s.EncryptedDnsTransport, loaded.EncryptedDnsTransport);
            Assert.Equal(s.ScanResolverId, loaded.ScanResolverId);
            Assert.Equal(s.NotificationsEnabled, loaded.NotificationsEnabled);
            Assert.Equal(s.TelemetryEnabled, loaded.TelemetryEnabled);
        }

        [Fact]
        public void CorruptFileFallsBackToDefaults()
        {
            var path = TempPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, "{ not valid json");
            var loaded = SyxxSettingsStore.Load(path);
            Assert.False(loaded.EncryptedDnsEnabled);   // = defaults, not a throw
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // E-7 offline grace predicate — LicenseManager.OfflineGraceApplies
    // ─────────────────────────────────────────────────────────────────────────────
    public class OfflineGracePredicateTests
    {
        private const long DayMs = 24L * 60 * 60 * 1000;

        /// Grace applies when lastValidMs is 5 days ago (within the 30-day window).
        ///
        /// Mutation: remove the `<= graceWindowMs` check in OfflineGraceApplies → returns false
        /// for any lastValidMs, so a recent (5-day) lastValid still returns false → test REDs.
        [Fact]
        public void WithinWindow_ReturnsTrue()
        {
            long now = 1_000_000_000_000L;
            long last = now - 5 * DayMs;   // 5 days ago — inside 30-day window
            Assert.True(LicenseManager.OfflineGraceApplies(now, last),
                "grace must apply when last validation is 5 days ago");
        }

        /// Grace must NOT apply when last validation is 31 days ago (outside window).
        ///
        /// Mutation: remove the `nowMs - lastValidMs <= graceWindowMs` predicate → always returns
        /// true → test REDs because 31-day-ago last should be outside the window.
        [Fact]
        public void OutsideWindow_ReturnsFalse()
        {
            long now = 1_000_000_000_000L;
            long last = now - 31 * DayMs;  // 31 days ago — outside 30-day window
            Assert.False(LicenseManager.OfflineGraceApplies(now, last),
                "grace must NOT apply when last validation is 31 days ago");
        }

        /// No recorded validation — grace must never apply.
        ///
        /// Mutation: remove the null guard in OfflineGraceApplies → null treated as 0 (distantPast)
        /// could pass the window check on an overflow → test REDs with a null argument.
        [Fact]
        public void NullLastValid_ReturnsFalse()
        {
            long now = 1_000_000_000_000L;
            Assert.False(LicenseManager.OfflineGraceApplies(now, null),
                "grace must NOT apply when no prior validation has been recorded");
        }

        /// Grace applies exactly at the 30-day boundary (inclusive).
        [Fact]
        public void ExactlyAtBoundary_ReturnsTrue()
        {
            long now = 1_000_000_000_000L;
            long graceMs = 30L * 24 * 60 * 60 * 1000;
            long last = now - graceMs;  // exactly 30 days ago
            Assert.True(LicenseManager.OfflineGraceApplies(now, last),
                "grace must apply at exactly the 30-day boundary (inclusive)");
        }
    }

    // =========================================================================
    //  VPN prefix parity (Aug 2026 Linux catch-up)
    //
    //  VpnStatus() previously dispatched only on NetworkInterfaceType.Tunnel/Ppp, which works on
    //  Windows but not on Linux — .NET reports wg0/tun0/tap0/ppp0/ipsec0 as Ethernet or Unknown
    //  on Linux (the kernel's ARPHRD_NONE for virtual software interfaces does not map to .NET's
    //  Tunnel enum). The August 2026 fix added a Linux branch to VpnStatus() that uses interface
    //  NAME prefix matching (the same approach macOS uses). These tests pin the prefix set so a
    //  dropped prefix can silently re-introduce the B030 false-negative class the Glutton leak
    //  test was written to refuse.
    // =========================================================================
    public class VpnPrefixParityTests
    {
        [Theory]
        [InlineData("tun0")]      // classic TUN VPNs (OpenVPN --dev tun)
        [InlineData("tun1")]
        [InlineData("tap0")]       // TAP (OpenVPN --dev tap, bridged)
        [InlineData("wg0")]       // WireGuard
        [InlineData("wg1")]
        [InlineData("wg-home")]    // WireGuard with named interface
        [InlineData("ppp0")]       // PPP (PPTP, L2TP, pppd)
        [InlineData("ipsec0")]     // IPsec (Libreswan/strongSwan XFRM)
        [InlineData("gpd0")]      // generic-fallback prefix
        [InlineData("utun0")]      // macOS-style utun parity (rare on Linux but should match)
        public void IsTunnelInterface_MatchesVpnInterfaceNames(string name)
        {
            Assert.True(NetworkKeyState.IsTunnelInterface(name),
                $"A VPN interface named '{name}' must be classified as a tunnel on Linux. " +
                "If this fails, the prefix was dropped and an active VPN will silently read as 'no VPN detected', " +
                "gating the Glutton leak test off — the B030 false-negative class.");
        }

        [Theory]
        [InlineData("eth0")]
        [InlineData("wlan0")]
        [InlineData("enp0s3")]
        [InlineData("wlp3s0")]
        [InlineData("lo")]
        [InlineData("docker0")]
        [InlineData("br-abcdef")]
        [InlineData("veth0a1b2c3")]
        [InlineData("virbr0")]
        public void IsTunnelInterface_RejectsNonVpnInterfaceNames(string name)
        {
            Assert.False(NetworkKeyState.IsTunnelInterface(name),
                $"A non-VPN interface named '{name}' must NOT be classified as a tunnel — the prefix set is too broad, " +
                "which would fabricate a VPN-tunnel claim about an Ethernet/WiFi/bridge/veth/loopback adapter.");
        }

        [Theory]
        [InlineData("TUN0")]       // case-insensitive (StartsWith with OrdinalIgnoreCase)
        [InlineData("WG0")]
        [InlineData("PPP0")]
        [InlineData("Tun0")]
        [InlineData("Wg-Home")]
        public void IsTunnelInterface_IsCaseInsensitive(string name)
        {
            // Parity with macOS NetworkKeyState.swift:462, which uses hasPrefix (case-sensitive in
            // Swift, but Linux interface names are conventionally lowercase). The C# helper is
            // explicitly OrdinalIgnoreCase so a custom uTun / wg-Home interface still matches.
            Assert.True(NetworkKeyState.IsTunnelInterface(name));
        }
    }
}
