using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
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
                    SourceIP = "8.8.8.8",
                    DestIP = "10.0.0.5",
                    SourcePort = 53,
                    DestPort = 54321,
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
                                                      Resolvers = resolvers,
                                                      ActiveInterfaces = ifaces ?? new() { "en0" },
                                                      VpnActive = vpn ? ConditionState.Yes : ConditionState.No,
                                                      DnsEncrypted = enc
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

        /// AMENDED DELIBERATELY (E1, Aug 2026 — house rule 4). The assertion is unchanged: a
        /// resolver whose transport WAS read and found unencrypted must report No, with "plain"
        /// evidence. What changed is that the fixture now states that the read happened, instead of
        /// inheriting it from ScopedResolver.DoHReadState's default.
        ///
        /// That default used to be No, so this test was silently asserting over a defaulted claim —
        /// "we read the configuration and found no encryption" obtained by leaving a field alone.
        /// The default is now Unknown, so the read has to be declared. Without this line the honest
        /// answer is Unknown, and the test would be pinning an unearned measurement.
        [Fact]
        public void EncryptedDNSStatusNoForPlainUDP()
        {
            var scoped = new List<ScopedResolver>
            {
                new() { Address = "8.8.8.8", IsScoped = false, DoHReadState = ConditionState.No }
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

        // P-22: the two tests that used to sit here hand-wrote `resolvectl` output formats that
        // systemd-resolved has never emitted (`DNSOverHTTPS: yes` and `DNS Over TLS: yes`). They
        // were green against a parser that could not read a single real machine. They have been
        // deleted, not softened, and replaced by ResolvectlCorpusTests below, which drives the
        // parser from corpus/resolvectl-status/ — fixtures whose shape is traceable to systemd's
        // own printing code, each carrying its source URL and its provenance marker.
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
                EncryptedDnsEnabled = true,
                EncryptedDnsResolverId = "quad9",
                EncryptedDnsTransport = EncryptedDNSTransport.Dot,
                ScanResolverId = "google",
                NotificationsEnabled = false,
                TelemetryEnabled = false
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
    /// P-06 / P-22 — the Linux resolver-truth corpus.
    ///
    /// Loads corpus/resolvectl-status/**, which replaces two tests that hand-wrote `resolvectl`
    /// formats that do not exist. A missing fixture THROWS: a vacuously-green corpus test is
    /// exactly the green-that-lies this corpus was created to end.
    ///
    /// Every fixture is currently DERIVED-FROM-DOCUMENTATION — reconstructed from systemd's own
    /// printing code and manpages, not captured from a running host. See
    /// corpus/resolvectl-status/PENDING-OWNER-VERIFICATION.md.
    public class ResolvectlCorpusTests
    {
        private static string CorpusRoot([CallerFilePath] string thisFile = "")
        {
            // thisFile = <repo>/Windows/dotNET/Syxx.Tests/ConfigAndPolicyTests.cs
            var dir = Path.GetDirectoryName(thisFile)
                      ?? throw new InvalidOperationException("Cannot resolve source directory.");
            return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "corpus", "resolvectl-status"));
        }

        /// Reads a fixture and strips its leading `#` provenance header, so the parser sees exactly
        /// what the tool prints.
        private static string Fixture(string relativePath)
        {
            var path = Path.Combine(CorpusRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"resolvectl corpus fixture not found at {path}. The corpus is missing — this test " +
                    "must never pass without it.", path);
            var text = File.ReadAllText(path);
            if (text.Trim().Length == 0)
                throw new InvalidOperationException($"resolvectl corpus fixture {path} is empty.");
            return StripHeader(text);
        }

        private static string StripHeader(string text)
        {
            var lines = text.Split('\n');
            int i = 0;
            while (i < lines.Length && lines[i].TrimStart().StartsWith("#", StringComparison.Ordinal)) i++;
            return string.Join("\n", lines.Skip(i));
        }

        // ---- The four P-06 parser defects, one test each ----------------------------------

        /// ★ Defect 1 — the Global block was unreachable, and it is where resolved.conf DNS lives.
        /// The replaced parser skipped every line before the first `Link` header, returned zero
        /// resolvers on this machine, fell through to /etc/resolv.conf and adopted the stub.
        [Fact]
        public void GlobalBlockResolversAreDiscovered()
        {
            var resolvers = NetworkKeyState.ParseResolvectlStatus(Fixture("fixtures/systemd-249-global-only.txt"));

            Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, resolvers.Select(r => r.Address).ToArray());
            Assert.All(resolvers, r => Assert.Null(r.Interface));      // Global is not link-scoped
            Assert.All(resolvers, r => Assert.False(r.IsScoped));
        }

        /// ★ Defect 4 — a multi-server row is space-separated, not one opaque token. The replaced
        /// parser added `192.168.1.1 8.8.8.8` as a single entry, which parses as no valid address,
        /// so the link contributed zero resolvers.
        [Fact]
        public void MultiServerRowYieldsEveryServer()
        {
            var resolvers = NetworkKeyState.ParseResolvectlStatus(Fixture("fixtures/systemd-249-stub-dot-off.txt"));

            Assert.Equal(new[] { "192.168.1.1", "8.8.8.8" }, resolvers.Select(r => r.Address).ToArray());
            Assert.All(resolvers, r => Assert.Equal("wlp3s0", r.Interface));
            // Links whose Current Scopes is "none" contribute nothing.
            Assert.DoesNotContain(resolvers, r => r.Interface == "enp0s31f6" || r.Interface == "docker0");
            // `+DefaultRoute` means this link carries the default route, so its resolvers are not scoped.
            Assert.All(resolvers, r => Assert.False(r.IsScoped));
            // `-DNSOverTLS` is a real read with a definite answer: not encrypted, and we know it.
            Assert.All(resolvers, r => Assert.Null(r.Protocol));
            Assert.All(resolvers, r => Assert.Equal(ConditionState.No, r.DoHReadState));
            Assert.All(resolvers, r => Assert.Equal(new[] { "lan" }, r.MatchDomains.ToArray()));
        }

        /// Defects 2 and 3 — the DoT branch matched a literal systemd has never printed, and the
        /// DoH branch was dead by construction (systemd-resolved implements no DoH at all).
        [Fact]
        public void StrictDnsOverTlsIsDetectedFromProtocolsToken()
        {
            var resolvers = NetworkKeyState.ParseResolvectlStatus(Fixture("fixtures/systemd-249-dot-strict.txt"));

            Assert.All(resolvers, r => Assert.Equal(DnsProtocol.Dot, r.Protocol));
            Assert.All(resolvers, r => Assert.Equal((ushort)853, r.Port));
            Assert.Contains(resolvers, r => r.Address == "9.9.9.9");
            Assert.Contains(resolvers, r => r.Address == "2620:fe::fe");

            // The `#dns.quad9.net` suffix is the DoT certificate name, NOT a match domain. The
            // replaced parser filed it as one, making every DoT resolver look domain-scoped.
            Assert.All(resolvers, r => Assert.Empty(r.MatchDomains));

            // `Fallback DNS Servers` are systemd's compiled-in last resort, used only when nothing
            // else is configured. Reporting them would attribute queries to servers that are not
            // answering them.
            Assert.DoesNotContain(resolvers, r => r.Address == "8.8.8.8" || r.Address == "8.8.4.4");
        }

        /// Opportunistic DoT silently falls back to cleartext and the client cannot tell which
        /// happened. Encrypted would be a fabricated assurance; plaintext a fabricated alarm.
        [Fact]
        public void OpportunisticDnsOverTlsIsUnknownNotEncryptedAndNotPlaintext()
        {
            var resolvers = NetworkKeyState.ParseResolvectlStatus(Fixture("fixtures/systemd-249-dot-opportunistic.txt"));

            Assert.NotEmpty(resolvers);
            Assert.All(resolvers, r => Assert.Null(r.Protocol));
            Assert.All(resolvers, r => Assert.Equal(ConditionState.Unknown, r.DoHReadState));

            var status = NetworkKeyState.EncryptedDNSStatusCore(
                resolvers.Select(r => r.Address).ToList(), resolvers);
            Assert.Equal(ConditionState.Unknown, status.State);
        }

        /// The older per-setting-line generation (systemd v245, Ubuntu 20.04 LTS) is still live.
        /// Its continuation lines are the trap: `DNSSEC NTA:` continues onto unlabelled lines, and
        /// a parser that treats any unlabelled line as more DNS servers ingests reverse-DNS zones
        /// as resolvers.
        [Fact]
        public void LegacySettingLineGenerationIsParsedAndContinuationsAreAttributed()
        {
            var resolvers = NetworkKeyState.ParseResolvectlStatus(
                Fixture("fixtures/systemd-245-legacy-setting-lines.txt"));

            Assert.Equal(new[] { "192.168.1.1", "8.8.8.8" }, resolvers.Select(r => r.Address).ToArray());
            Assert.DoesNotContain(resolvers, r => r.Address.Contains("in-addr.arpa", StringComparison.Ordinal));
            Assert.All(resolvers, r => Assert.Null(r.Protocol));
            Assert.All(resolvers, r => Assert.Equal(ConditionState.No, r.DoHReadState));
        }

        /// Wrapped continuation lines are more DNS servers; `Current Scopes` must be tokenised,
        /// because "mDNS/IPv4" contains the letters "DNS" but is not a unicast-DNS scope.
        [Fact]
        public void WrappedServerRowsAreCollectedAndMdnsOnlyScopeIsNotDns()
        {
            var resolvers = NetworkKeyState.ParseResolvectlStatus(
                Fixture("fixtures/systemd-249-wrapped-and-mdns-scope.txt"));

            Assert.Equal(
                new[] { "192.168.1.1", "fd00::1", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888" },
                resolvers.Select(r => r.Address).ToArray());
            Assert.Equal((ushort)5353, resolvers.Single(r => r.Address == "2001:4860:4860::8888").Port);
            Assert.All(resolvers, r => Assert.Equal(new[] { "lan", "corp.example" }, r.MatchDomains.ToArray()));
            Assert.DoesNotContain(resolvers, r => r.Interface == "virbr0");
        }

        /// The P-22 pin itself: the two literals the replaced parser looked for appear in NOTHING
        /// this corpus contains, including any real capture the owner adds. If a future capture
        /// ever contains one, this test fails and the deletion above must be revisited — which is
        /// the point of asserting it rather than asserting it in prose.
        [Fact]
        public void DeadBranchLiteralsAppearInNoRealShapedOutput()
        {
            var files = CorpusFiles().ToList();
            Assert.NotEmpty(files);
            foreach (var file in files)
            {
                // The provenance headers discuss these literals by name; what must not contain
                // them is the output itself.
                var body = StripHeader(File.ReadAllText(file));
                Assert.DoesNotContain("DNS Over TLS:", body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("DNSOverHTTPS", body, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// The corpus must never claim to have been witnessed when it has not been. Each fixture
        /// declares exactly one provenance marker, and CAPTURED-FROM-REAL-HOST may only be claimed
        /// by a file that actually came off a machine (captured/*.pack.txt).
        [Fact]
        public void EveryFixtureDeclaresItsProvenanceHonestly()
        {
            Assert.True(File.Exists(Path.Combine(CorpusRoot(), "PENDING-OWNER-VERIFICATION.md")),
                "The corpus must record what has not been verified against a real host.");

            foreach (var file in CorpusFiles().Where(f => !f.Contains(Path.Combine("corpus", "resolvectl-status", "captured"), StringComparison.Ordinal)))
            {
                var text = File.ReadAllText(file);
                bool derived = text.Contains("DERIVED-FROM-DOCUMENTATION", StringComparison.Ordinal);
                bool captured = text.Contains("CAPTURED-FROM-REAL-HOST", StringComparison.Ordinal);
                Assert.True(derived ^ captured,
                    $"{file} must declare exactly one provenance marker. A fixture whose shape nobody " +
                    "can point to a source for is the defect this corpus exists to prevent.");
                Assert.False(captured,
                    $"{file} claims CAPTURED-FROM-REAL-HOST but lives outside captured/. Only a real " +
                    "pack may make that claim.");
            }
        }

        /// Real captures, when the owner supplies them. Absent one, this asserts nothing about a
        /// real machine and does not pretend to — the honest statement of that is in
        /// PENDING-OWNER-VERIFICATION.md and in EveryFixtureDeclaresItsProvenanceHonestly above.
        [Fact]
        public void CapturedPacksParseAndAgreeWithResolvConf()
        {
            var dir = Path.Combine(CorpusRoot(), "captured");
            var packs = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.pack.txt") : Array.Empty<string>();

            foreach (var pack in packs)
            {
                var text = File.ReadAllText(pack);
                var status = Section(text, "--- STATUS", "--- RESOLVCONF");
                var resolvConf = Section(text, "--- RESOLVCONF", "--- MODE");

                Assert.True(status.Contains("Global", StringComparison.Ordinal),
                    $"{pack}: expected a Global section. Its absence means systemd-resolved is not " +
                    "running and this pack is INCONCLUSIVE, not a pass.");
                Assert.Contains("Link ", status, StringComparison.Ordinal);

                var reading = NetworkKeyState.ReadLinuxResolvers(status, resolvConf);

                // The declared expectation: a running systemd-resolved names its upstream servers,
                // so the reading must come from resolvectl and never silently from the stub.
                Assert.NotEqual(LinuxResolverSource.Undetermined, reading.Source);
                if (reading.Source == LinuxResolverSource.SystemdStubOnly)
                    Assert.Empty(reading.Resolvers);
                else
                    Assert.NotEmpty(reading.Resolvers);
                Assert.DoesNotContain(reading.Resolvers, r => r.Address == "127.0.0.53" || r.Address == "127.0.0.54");
                Assert.NotEmpty(reading.Evidence);
            }
        }

        private static string Section(string text, string start, string end)
        {
            int s = text.IndexOf(start, StringComparison.Ordinal);
            if (s < 0) return string.Empty;
            s += start.Length;
            int e = text.IndexOf(end, s, StringComparison.Ordinal);
            return e < 0 ? text[s..] : text[s..e];
        }

        private static IEnumerable<string> CorpusFiles()
            => Directory.GetFiles(CorpusRoot(), "*", SearchOption.AllDirectories)
                        .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f, StringComparer.Ordinal);

        // ---- The stub: what happens when resolvectl tells us nothing --------------------------

        /// ★ The consequence the seat exists to remove. /etc/resolv.conf on a stock Ubuntu Desktop
        /// names only the systemd stub, which forwards to upstream servers it does not disclose.
        /// The honest outcome is neither "the stub is your resolver" nor a substituted guess: it is
        /// undetermined, with evidence.
        [Fact]
        public void StubOnlyResolvConfIsUndeterminedNotAMeasuredResolver()
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(null, Fixture("resolvconf/stub.conf"));

            Assert.Equal(LinuxResolverSource.SystemdStubOnly, reading.Source);
            Assert.Empty(reading.Resolvers);
            Assert.Contains("127.0.0.53", reading.Evidence, StringComparison.Ordinal);
            Assert.Contains("undetermined", reading.Evidence, StringComparison.OrdinalIgnoreCase);

            // And nothing downstream may turn that into a result.
            var status = NetworkKeyState.EncryptedDNSStatusCore(
                reading.Resolvers.Select(r => r.Address).ToList(), reading.Resolvers);
            Assert.Equal(ConditionState.Unknown, status.State);
        }

        /// A loopback resolver that is NOT the systemd stub is a resolver the user genuinely runs
        /// (unbound, dnsmasq, stubby, pi-hole) and IS the server answering their queries. Blanket
        /// loopback exclusion would blind the comparison and leak probes on exactly the machines
        /// whose owners took the most care.
        [Fact]
        public void NonStubLoopbackResolverIsKept()
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(null, Fixture("resolvconf/local-resolver.conf"));

            Assert.Equal(LinuxResolverSource.ResolvConf, reading.Source);
            Assert.Equal(new[] { "127.0.0.1" }, reading.Resolvers.Select(r => r.Address).ToArray());

            // ...and its upstream still cannot be vouched for, so encryption stays Unknown.
            var status = NetworkKeyState.EncryptedDNSStatusCore(new[] { "127.0.0.1" }, reading.Resolvers);
            Assert.Equal(ConditionState.Unknown, status.State);
        }

        /// resolv.conf carries no transport information, so a resolver sourced from it must be
        /// Unknown, never a silent "plaintext".
        [Fact]
        public void ResolvConfResolversCarryUnknownTransport()
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(null, Fixture("resolvconf/uplink.conf"));

            Assert.Equal(LinuxResolverSource.ResolvConf, reading.Source);
            Assert.Equal(new[] { "192.168.1.1", "8.8.8.8" }, reading.Resolvers.Select(r => r.Address).ToArray());
            Assert.All(reading.Resolvers, r => Assert.Equal(ConditionState.Unknown, r.DoHReadState));
            Assert.Equal(ConditionState.Unknown,
                NetworkKeyState.EncryptedDNSStatusCore(
                    reading.Resolvers.Select(r => r.Address).ToList(), reading.Resolvers).State);
        }

        /// resolvectl is authoritative when it answers: the stub in resolv.conf must not displace
        /// the real upstream servers it reported.
        [Fact]
        public void ResolvectlWinsOverStubResolvConf()
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(
                Fixture("fixtures/systemd-249-global-only.txt"),
                Fixture("resolvconf/stub.conf"));

            Assert.Equal(LinuxResolverSource.Resolvectl, reading.Source);
            Assert.Equal(new[] { "1.1.1.1", "1.0.0.1" }, reading.Resolvers.Select(r => r.Address).ToArray());
        }

        /// Neither source readable is Undetermined — not an empty clean result.
        [Fact]
        public void NothingReadableIsUndetermined()
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(null, null);
            Assert.Equal(LinuxResolverSource.Undetermined, reading.Source);
            Assert.Empty(reading.Resolvers);
            Assert.NotEmpty(reading.Evidence);
        }
    }

    /// T-01 — ConditionState was the one tri-state with no JSON converter, so it serialised as its
    /// ORDINAL NUMBER. Trust.NetworkConfigSnapshot.VpnActive is persisted with that encoding and
    /// compared across samples by ContinuousMonitor, so the field's meaning was positional.
    public class ConditionStateWireTests
    {
        private sealed class Holder
        {
            public ConditionState State { get; set; }
        }

        [Theory]
        [InlineData(ConditionState.Yes, "yes")]
        [InlineData(ConditionState.No, "no")]
        [InlineData(ConditionState.Unknown, "unknown")]
        public void SerialisesAsTheSwiftRawValue(ConditionState state, string expected)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new Holder { State = state });
            Assert.Contains($"\"{expected}\"", json, StringComparison.Ordinal);
            Assert.Equal(state, System.Text.Json.JsonSerializer.Deserialize<Holder>(json)!.State);
        }

        [Fact]
        public void UnknownIsNotCollapsedIntoNoOnRoundTrip()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new Holder { State = ConditionState.Unknown });
            Assert.DoesNotContain("\"no\"", json, StringComparison.Ordinal);
            Assert.Equal(ConditionState.Unknown,
                System.Text.Json.JsonSerializer.Deserialize<Holder>(json)!.State);
        }

        /// Baselines written before the converter existed hold the bare ordinal. A failed read is
        /// reported as a CORRUPTED baseline, i.e. a visible loss of the user's home baseline, so
        /// the legacy encoding must still load.
        [Theory]
        [InlineData(0, ConditionState.Yes)]
        [InlineData(1, ConditionState.No)]
        [InlineData(2, ConditionState.Unknown)]
        public void LegacyOrdinalBaselinesStillLoad(int ordinal, ConditionState expected)
        {
            var decoded = System.Text.Json.JsonSerializer.Deserialize<Holder>($"{{\"State\":{ordinal}}}");
            Assert.Equal(expected, decoded!.State);
        }

        /// An unrecognised value is corrupt data, not Unknown. Folding it into Unknown would
        /// manufacture a tri-state answer out of a parse failure.
        [Fact]
        public void UnrecognisedValueThrowsRatherThanBecomingUnknown()
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(
                () => System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"State\":\"maybe\"}"));
            Assert.ThrowsAny<System.Text.Json.JsonException>(
                () => System.Text.Json.JsonSerializer.Deserialize<Holder>("{\"State\":7}"));
        }
    }

    /// P-07 — LinkLocal must NOT be excluded from usable resolvers, aligning with the macOS twin.
    /// RFC 8106 RDNSS permits link-local resolver addresses and home routers commonly advertise
    /// fe80:: resolvers, so a blanket exclusion drops the real system resolver.
    public class UsableDnsServerParityTests
    {
        [Theory]
        [InlineData("fe80::1")]                 // RFC 8106 RDNSS — a router-advertised resolver
        [InlineData("fe80::1%eth0")]            // ...as some tools render it (scope-id form)
        [InlineData("169.254.1.1")]             // IPv4 link-local
        public void LinkLocalResolversSurviveDiscovery(string address)
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(null, $"nameserver {address}\n");

            // A scope-id suffix is not a parseable address, so it is classified Invalid and drops.
            // The plain forms must survive; that is the P-07 alignment.
            if (address.Contains('%', StringComparison.Ordinal))
                return;

            Assert.Equal(new[] { address }, reading.Resolvers.Select(r => r.Address).ToArray());
        }

        [Theory]
        [InlineData("0.0.0.0")]                 // Unspecified
        [InlineData("224.0.0.251")]             // Multicast (mDNS)
        [InlineData("192.0.2.1")]               // Documentation
        [InlineData("fec0:0:0:ffff::1")]        // deprecated site-local — the Windows placeholder
        [InlineData("not-an-address")]          // Invalid
        public void NonResolverAddressesAreStillExcluded(string address)
        {
            var reading = NetworkKeyState.ReadLinuxResolvers(null, $"nameserver {address}\n");
            Assert.Empty(reading.Resolvers);
        }
    }

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
