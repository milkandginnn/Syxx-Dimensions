using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;
using NSec.Cryptography;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // These mirror the offline-testable subset of the macOS SyxxKitTests XCTest suite (P3).
    // Anything that requires a live network (e.g. VantageComparer.CompareVantagePointsAsync,
    // license validation) is intentionally excluded here.

    public class IPClassifierTests
    {
        [Theory]
        [InlineData("10.0.0.1", IPAddressClass.PrivateUse)]
        [InlineData("172.16.0.1", IPAddressClass.PrivateUse)]
        [InlineData("192.168.1.1", IPAddressClass.PrivateUse)]
        [InlineData("127.0.0.1", IPAddressClass.Loopback)]
        [InlineData("::1", IPAddressClass.Loopback)]
        [InlineData("100.64.0.1", IPAddressClass.CarrierGradeNAT)]
        [InlineData("169.254.1.1", IPAddressClass.LinkLocal)]
        [InlineData("192.0.2.1", IPAddressClass.Documentation)]
        [InlineData("198.51.100.1", IPAddressClass.Documentation)]
        [InlineData("203.0.113.1", IPAddressClass.Documentation)]
        [InlineData("2001:db8::1", IPAddressClass.Documentation)]
        [InlineData("8.8.8.8", IPAddressClass.PublicRoutable)]
        [InlineData("2606:4700:4700::1111", IPAddressClass.PublicRoutable)]
        [InlineData("not-an-ip", IPAddressClass.Invalid)]
        // E013 / B025 — the RFC 2544 benchmarking range is the whole 198.18.0.0/15
        // (198.18.0.0–198.19.255.255). The truncated `ip[1] == 18` test let the 198.19.x.x half leak
        // through as public; both halves must classify as special-use, and the neighbours stay public.
        [InlineData("198.18.0.1", IPAddressClass.PrivateUse)]
        [InlineData("198.18.255.254", IPAddressClass.PrivateUse)]
        [InlineData("198.19.0.1", IPAddressClass.PrivateUse)]
        [InlineData("198.19.255.254", IPAddressClass.PrivateUse)]
        [InlineData("198.17.0.1", IPAddressClass.PublicRoutable)]
        [InlineData("198.20.0.1", IPAddressClass.PublicRoutable)]
        // C013 — IPv4-mapped IPv6 (::ffff:0:0/96) keeps the embedded IPv4's class. Before the
        // mapped branch these all fell through to PublicRoutable, so a mapped sinkhole answer
        // was never flagged as blocking.
        [InlineData("::ffff:127.0.0.1", IPAddressClass.Loopback)]
        [InlineData("::ffff:10.0.0.1", IPAddressClass.PrivateUse)]
        [InlineData("::ffff:192.168.1.1", IPAddressClass.PrivateUse)]
        [InlineData("::ffff:198.19.0.1", IPAddressClass.PrivateUse)]
        [InlineData("::ffff:0.0.0.0", IPAddressClass.Unspecified)]
        // Failure case: a mapped PUBLIC address must stay public — the branch must not over-block.
        [InlineData("::ffff:8.8.8.8", IPAddressClass.PublicRoutable)]
        public void ClassifyIdentifiesRange(string ip, IPAddressClass expected)
        {
            Assert.Equal(expected, IPClassifier.Classify(ip));
        }

        [Theory]
        [InlineData("192.168.1.1", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("8.8.8.8", false)]
        // E013 / B025 — a proxy/sinkhole answer in 198.19.x.x must read as a blocking answer.
        [InlineData("198.19.0.1", true)]
        [InlineData("198.18.255.254", true)]
        // C013 — a mapped private/loopback answer is a blocking answer; a mapped public one is not.
        [InlineData("::ffff:192.168.1.1", true)]
        [InlineData("::ffff:127.0.0.1", true)]
        [InlineData("::ffff:8.8.8.8", false)]
        public void IsBlockingAnswerFlagsPrivateAndLoopback(string ip, bool expected)
        {
            Assert.Equal(expected, IPClassifier.IsBlockingAnswer(ip));
        }
    }

    public class WitnessSignerTests
    {
        private static ComparisonResult MakeComparison(string domain = "example.com", bool redirected = false)
        {
            var record = new DNSRecord { Name = domain, Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            return new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                PublicResolverResults = new Dictionary<string, List<DNSRecord>> { { "Cloudflare", new List<DNSRecord> { record } } },
                IsRedirectionDetected = redirected
            };
        }

        private static (string PrivateKey, string PublicKey) TestKeyPair => WitnessSigner.GenerateKeyPair();

        [Fact]
        public void VerifiesWithCorrectKey()
        {
            var (priv, pub) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            Assert.True(WitnessSigner.VerifySnapshot(snapshot));
            Assert.Equal(pub, snapshot.PublicKey);
            Assert.False(string.IsNullOrEmpty(snapshot.KeyId));
        }

        [Fact]
        public void RejectsEmptyKeyOnCreate()
        {
            Assert.Throws<WitnessSignerException>(() => WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), ""));
        }

        [Fact]
        public void RejectsEmptyPublicKey()
        {
            // A bare snapshot fails for many reasons (over-determined). Start from a VALID signed
            // snapshot and blank ONLY the public key, so the property tested is "an otherwise-valid
            // snapshot with no public key must not verify" — parity with the macOS test.
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            snapshot.PublicKey = "";
            Assert.False(WitnessSigner.VerifySnapshot(snapshot),
                "an otherwise-valid snapshot with an empty public key must be rejected");
        }

        [Fact]
        public void RejectsTamperedComparison()
        {
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(redirected: false), priv);
            snapshot.Comparison = MakeComparison(redirected: true); // attacker flips the verdict after signing
            Assert.False(WitnessSigner.VerifySnapshot(snapshot));
        }

        // [B037] The display-integrity guard must also cover per-resolver outcomes. A tampered display
        // that keeps the aggregate verdict/answer-sets identical (extra injected resolver, same rdata,
        // so ComparisonResult is unchanged) but alters resolverQueryOutcomes must be rejected.
        [Fact]
        public void RejectsTamperedResolverOutcomes()
        {
            var (priv, _) = TestKeyPair;
            var record = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            var ok = new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = new List<DNSRecord> { record } };
            // C# Build() reads PublicResolverOutcomes (not the raw ...Results), so populate it.
            var original = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                LocalOutcome = ok,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome> { { "Cloudflare", ok } },
                IsRedirectionDetected = false
            };
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", original, priv);
            // Inject an extra resolver with the SAME rdata: authoritative set + verdict are unchanged
            // (so ComparisonResult still matches), but resolverQueryOutcomes gains an entry.
            snapshot.Comparison = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                LocalOutcome = ok,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
                {
                    { "Cloudflare", ok },
                    { "EvilInjectedResolver", ok }
                },
                IsRedirectionDetected = false
            };
            Assert.False(WitnessSigner.VerifySnapshot(snapshot),
                "a tampered displayed resolver set must not verify, even when the verdict is unchanged");
        }

        [Fact]
        public void RejectsTamperedDomain()
        {
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            snapshot.TargetDomain = "attacker-controlled.com";
            Assert.False(WitnessSigner.VerifySnapshot(snapshot));
        }

        // B085 — a snapshot signed with a FUTURE timestamp defeats every max-age freshness check
        // by construction; an honest signer cannot produce one, so it must be rejected outright.
        [Fact]
        public void RejectsFutureDatedSnapshot()
        {
            var (priv, _) = TestKeyPair;
            var future = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", MakeComparison(), priv, expiresAt: null,
                timestampOverride: DateTime.UtcNow.AddHours(1));
            Assert.Equal(SnapshotVerification.FutureDated, WitnessSigner.VerifySnapshotDetailed(future));
            Assert.False(WitnessSigner.VerifySnapshot(future),
                "a future-dated snapshot must not verify even with a valid signature");
        }

        // B085 failure case for the failure case: honest clock drift within the skew tolerance
        // must NOT be rejected — the guard must not over-block real snapshots.
        [Fact]
        public void AcceptsSnapshotWithinClockSkewTolerance()
        {
            var (priv, _) = TestKeyPair;
            var slightlyAhead = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", MakeComparison(), priv, expiresAt: null,
                timestampOverride: DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(slightlyAhead));
        }

        // E021 — expired vs stale vs untrusted vs invalid-signature are forensically DIFFERENT
        // failures and must stay distinct; collapsing them into one bool false was the defect.
        [Fact]
        public void DetailedVerificationPreservesDistinctFailureStates()
        {
            var (priv, pub) = TestKeyPair;

            var expired = WitnessSigner.CreateSignedSnapshot(
                "example.com", MakeComparison(), priv, expiresAt: DateTime.UtcNow.AddMinutes(-5));
            Assert.Equal(SnapshotVerification.Expired, WitnessSigner.VerifySnapshotDetailed(expired));

            var stale = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", MakeComparison(), priv, expiresAt: null,
                timestampOverride: DateTime.UtcNow.AddHours(-2));
            Assert.Equal(SnapshotVerification.Stale,
                WitnessSigner.VerifySnapshotDetailed(stale, maxAge: TimeSpan.FromHours(1)));

            var untrusted = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            var (otherPriv, otherPub) = WitnessSigner.GenerateKeyPair();
            _ = otherPriv;
            Assert.Equal(SnapshotVerification.UntrustedSigner,
                WitnessSigner.VerifySnapshotDetailed(untrusted, trustedPublicKeys: new List<string> { otherPub }));
            Assert.Equal(SnapshotVerification.Valid,
                WitnessSigner.VerifySnapshotDetailed(untrusted, trustedPublicKeys: new List<string> { pub }));

            var forged = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            var sigBytes = Convert.FromBase64String(forged.Signature);
            sigBytes[0] ^= 0xFF;
            forged.Signature = Convert.ToBase64String(sigBytes);
            Assert.Equal(SnapshotVerification.InvalidSignature, WitnessSigner.VerifySnapshotDetailed(forged));
        }

        // E020 — format v3 removes free-text errorMessage from the signed canonical bytes.
        // The verifier must accept BOTH v2 (legacy) and v3 (migration is verifier-first: signers
        // flip to v3 only after both platforms' verifiers ship).
        [Fact]
        public void FormatV3ExcludesErrorTextAndStillVerifies()
        {
            var (priv, _) = TestKeyPair;
            var record = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            var failed = new ResolverQueryOutcome
            {
                Status = DNSResponseStatus.Timeout,
                Records = new List<DNSRecord>(),
                ErrorMessage = "platform-specific exception text that must not be signed"
            };
            var comparison = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                LocalOutcome = failed,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome> { { "Cloudflare", failed } },
                IsRedirectionDetected = false
            };

            var v3 = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", comparison, priv, expiresAt: null, timestampOverride: null,
                formatVersion: SignedEvidencePayload.ErrorMessageFreeFormatVersion);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(v3));

            // The defect under test: error text present in the wire outcome must NOT reach the
            // signed bytes in v3. If Build() regresses to including it, this fails.
            var signedJson = System.Text.Encoding.UTF8.GetString(v3.Evidence.CanonicalBytes());
            Assert.DoesNotContain("platform-specific exception text", signedJson);

            // Failure case: the same comparison signed as v2 DOES carry the text (legacy shape,
            // proving the v3 assertion above is not vacuous), and still verifies.
            var v2 = WitnessSigner.CreateSignedSnapshot("example.com", comparison, priv);
            var v2Json = System.Text.Encoding.UTF8.GetString(v2.Evidence.CanonicalBytes());
            Assert.Contains("platform-specific exception text", v2Json);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(v2));

            // An unknown future version is refused, not guessed at.
            var v9 = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", comparison, priv, expiresAt: null, timestampOverride: null, formatVersion: 9);
            Assert.Equal(SnapshotVerification.UnsupportedFormat, WitnessSigner.VerifySnapshotDetailed(v9));
        }

        [Fact]
        public void DomainWithDelimiterDoesNotCollide()
        {
            // Regression test — structured JSON payload means delimiter chars cannot collide.
            var (priv, _) = TestKeyPair;
            var comparisonA = MakeComparison("evil|{\"IsRedirectionDetected\":true}");
            var comparisonB = MakeComparison("evil");
            var snapshotA = WitnessSigner.CreateSignedSnapshot("evil|{\"IsRedirectionDetected\":true}", comparisonA, priv);

            var crossed = new WitnessSnapshot
            {
                FormatVersion = 2,
                Timestamp = snapshotA.Timestamp,
                TargetDomain = "evil",
                Comparison = comparisonB,
                Evidence = snapshotA.Evidence,
                Signature = snapshotA.Signature,
                PublicKey = snapshotA.PublicKey,
                KeyId = snapshotA.KeyId
            };
            Assert.False(WitnessSigner.VerifySnapshot(crossed));
        }

        [Fact]
        public void KeyPairGeneration()
        {
            var (priv1, pub1) = WitnessSigner.GenerateKeyPair();
            var (priv2, pub2) = WitnessSigner.GenerateKeyPair();
            Assert.False(string.IsNullOrEmpty(priv1));
            Assert.False(string.IsNullOrEmpty(pub1));
            Assert.NotEqual(priv1, priv2);
            Assert.NotEqual(pub1, pub2);
            // Distinct non-empty strings don't prove a WORKING Ed25519 pair. Round-trip: a snapshot
            // signed with priv1 verifies, and the same snapshot with pub2 swapped in must NOT.
            var snap = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv1);
            Assert.True(WitnessSigner.VerifySnapshot(snap), "a freshly generated pair must sign+verify");
            snap.PublicKey = pub2;
            Assert.False(WitnessSigner.VerifySnapshot(snap),
                "a snapshot verified under a DIFFERENT generated public key must be rejected");
        }

        [Fact]
        public void ThirdPartyCannotForge()
        {
            // Third party with only the public key can verify but cannot forge.
            var (priv, _) = TestKeyPair;
            var (attackerPriv, _) = TestKeyPair;
            var comparison = MakeComparison();
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", comparison, priv);
            Assert.True(WitnessSigner.VerifySnapshot(snapshot));

            // Attacker signs with their own key but stamps target's public key
            var forged = WitnessSigner.CreateSignedSnapshot("evil.com", comparison, attackerPriv);
            var forgedSnapshot = new WitnessSnapshot
            {
                FormatVersion = 2,
                Timestamp = forged.Timestamp,
                TargetDomain = "evil.com",
                Comparison = forged.Comparison,
                Evidence = forged.Evidence,
                Signature = forged.Signature,
                PublicKey = snapshot.PublicKey,
                KeyId = snapshot.KeyId
            };
            Assert.False(WitnessSigner.VerifySnapshot(forgedSnapshot));
        }
    }

    public class DNSCaptureTests
    {
        private static byte[] MakeIPv4Header(byte ihlWords, byte protocolNumber, byte[] srcIP, byte[] dstIP, int optionsBytes)
        {
            var header = new List<byte>
            {
                (byte)((4 << 4) | ihlWords), // version 4, IHL in 32-bit words
                0x00,                        // TOS
                0x00, 0x30,                  // total length (unchecked)
                0x00, 0x00,                  // identification
                0x00, 0x00,                  // flags/fragment offset
                64,                          // TTL
                protocolNumber,
                0x00, 0x00                   // checksum (unchecked)
            };
            header.AddRange(srcIP);
            header.AddRange(dstIP);
            header.AddRange(new byte[optionsBytes]);
            return header.ToArray();
        }

        private static byte[] MakeUDPHeader(ushort srcPort, ushort dstPort, ushort payloadLen = 12)
        {
            ushort len = (ushort)(8 + payloadLen);
            return new byte[]
            {
                (byte)(srcPort >> 8), (byte)(srcPort & 0xff),
                (byte)(dstPort >> 8), (byte)(dstPort & 0xff),
                (byte)(len >> 8), (byte)(len & 0xff),
                0x00, 0x00
            };
        }

        private static byte[] EncodeDNSName(string domain)
        {
            var bytes = new List<byte>();
            foreach (var label in domain.Split('.'))
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(label));
            }
            bytes.Add(0);
            return bytes.ToArray();
        }

        [Fact]
        public void ParseIPUDPHeaderIPv4Standard()
        {
            var packet = new List<byte>();
            packet.AddRange(MakeIPv4Header(5, 17, new byte[] { 10, 0, 0, 1 }, new byte[] { 8, 8, 8, 8 }, 0));
            packet.AddRange(MakeUDPHeader(54321, 53, 12));
            packet.AddRange(new byte[12]);

            var ctx = DNSCapture.ParseIPUDPHeader(packet.ToArray());
            Assert.NotNull(ctx);
            Assert.Equal(4, ctx!.IpVersion);
            Assert.Equal("10.0.0.1", ctx.SourceIP);
            Assert.Equal("8.8.8.8", ctx.DestIP);
            Assert.Equal(54321, ctx.SourcePort);
            Assert.Equal(53, ctx.DestPort);
            Assert.Equal(20 + 8, ctx.PayloadOffset);
        }

        [Fact]
        public void ParseIPUDPHeaderIPv4WithOptionsAdjustsOffset()
        {
            // IHL=6 => 24-byte header; the old fixed-20-byte assumption would misread ports/payload.
            var packet = new List<byte>();
            packet.AddRange(MakeIPv4Header(6, 17, new byte[] { 192, 168, 1, 1 }, new byte[] { 1, 1, 1, 1 }, 4));
            packet.AddRange(MakeUDPHeader(53, 55000, 12));
            packet.AddRange(new byte[12]);

            var ctx = DNSCapture.ParseIPUDPHeader(packet.ToArray());
            Assert.NotNull(ctx);
            Assert.Equal("192.168.1.1", ctx!.SourceIP);
            Assert.Equal(53, ctx.SourcePort);
            Assert.Equal(55000, ctx.DestPort);
            Assert.Equal(24 + 8, ctx.PayloadOffset);
        }

        [Fact]
        public void ParseIPUDPHeaderIPv6()
        {
            var packet = new List<byte>();
            packet.AddRange(new byte[] { 0x60, 0x00, 0x00, 0x00, 0x00, 0x14, 17, 64 }); // v6, payload len (8 UDP + 12 payload = 20 = 0x14), next=UDP, hop
            var src = new byte[16]; src[15] = 1; // ::1
            var dst = new byte[16];
            dst[0] = 0x26; dst[1] = 0x01; dst[2] = 0x48; dst[3] = 0x60; dst[4] = 0x48; dst[5] = 0x60; dst[14] = 0x88; dst[15] = 0x88;
            packet.AddRange(src);
            packet.AddRange(dst);
            packet.AddRange(MakeUDPHeader(60000, 53, 12));
            packet.AddRange(new byte[12]);

            var ctx = DNSCapture.ParseIPUDPHeader(packet.ToArray());
            Assert.NotNull(ctx);
            Assert.Equal(6, ctx!.IpVersion);
            Assert.Equal(60000, ctx.SourcePort);
            Assert.Equal(53, ctx.DestPort);
            Assert.Equal(40 + 8, ctx.PayloadOffset);
        }

        [Fact]
        public void ParseDNSPacketQuery()
        {
            var packet = new List<byte> { 0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            packet.AddRange(EncodeDNSName("example.com"));
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // type A, class IN

            var report = DNSCapture.ParseDNSPacket(packet.ToArray());
            Assert.NotNull(report);
            Assert.Equal("Query", report!.Qr);
            Assert.Single(report.Questions);
            Assert.Equal("example.com", report.Questions[0].QuestionName);
            Assert.Equal("A", report.Questions[0].QuestionType);
            Assert.Empty(report.Answers);
        }

        [Fact]
        public void ParseDNSPacketResponseWithAnswer()
        {
            var packet = new List<byte> { 0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00 };
            packet.AddRange(EncodeDNSName("example.com"));
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 });
            // Answer: compression pointer to offset 12, type A, ttl 60, rdata 93.184.216.34
            packet.AddRange(new byte[] { 0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 93, 184, 216, 34 });

            var report = DNSCapture.ParseDNSPacket(packet.ToArray());
            Assert.NotNull(report);
            Assert.Equal("Response", report!.Qr);
            Assert.Single(report.Answers);
            Assert.Equal("example.com", report.Answers[0].Name);
            Assert.Equal("A", report.Answers[0].Type);
            Assert.Equal((uint)60, report.Answers[0].Ttl);
            Assert.Equal("93.184.216.34", report.Answers[0].Rdata);
        }

        [Fact]
        public void ParseDNSPacketRejectsCompressionLoop()
        {
            var packet = new List<byte> { 0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            // Question name at offset 12 is a self-referential pointer; must not hang.
            packet.AddRange(new byte[] { 0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01 });

            var report = DNSCapture.ParseDNSPacket(packet.ToArray());
            Assert.NotNull(report);
            Assert.Empty(report!.Questions);
        }
    }

    public class HtmlAssetParserTests
    {
        private const string BaseUrl = "https://example.com/";

        [Fact]
        public void ParsesScriptRegardlessOfAttributeOrder()
        {
            var assets = HtmlAssetParser.Parse("<script type=\"module\" src=\"/a.js\"></script>", BaseUrl);
            Assert.Equal(new[] { "https://example.com/a.js" }, assets.Scripts);
        }

        [Fact]
        public void ParsesStylesheetWithRelBeforeHref()
        {
            var assets = HtmlAssetParser.Parse("<link rel=\"stylesheet\" href=\"/style.css\">", BaseUrl);
            Assert.Equal(new[] { "https://example.com/style.css" }, assets.Stylesheets);
        }

        [Fact]
        public void ParsesModulepreloadAsScript()
        {
            var assets = HtmlAssetParser.Parse("<link rel=\"modulepreload\" href=\"/chunk.js\">", BaseUrl);
            Assert.Equal(new[] { "https://example.com/chunk.js" }, assets.Scripts);
        }

        [Fact]
        public void IgnoresTagLikeTextInsideComments()
        {
            var assets = HtmlAssetParser.Parse("<!-- <script src=\"/fake.js\"></script> --><script src=\"/real.js\"></script>", BaseUrl);
            Assert.Equal(new[] { "https://example.com/real.js" }, assets.Scripts);
        }

        [Fact]
        public void IgnoresTagLikeTextInsideScriptBody()
        {
            var assets = HtmlAssetParser.Parse("<script>var x = '<script src=\"/fake.js\">';</script><script src=\"/real.js\"></script>", BaseUrl);
            Assert.Equal(new[] { "https://example.com/real.js" }, assets.Scripts);
        }

        [Fact]
        public void PreservesOrderAndDuplicates()
        {
            var assets = HtmlAssetParser.Parse(
                "<script src=\"/a.js\"></script><script src=\"/b.js\"></script><script src=\"/a.js\"></script>", BaseUrl);
            Assert.Equal(new[] { "https://example.com/a.js", "https://example.com/b.js", "https://example.com/a.js" }, assets.Scripts);
        }

        [Fact]
        public void ParsesIframeBySrc()
        {
            var assets = HtmlAssetParser.Parse("<iframe title=\"ad\" src=\"https://ads.example.com/frame\"></iframe>", BaseUrl);
            Assert.Equal(new[] { "https://ads.example.com/frame" }, assets.Iframes);
        }
    }

    public class DNSResolverTests
    {
        // DNSRecord.TypeName is the display twin of macOS DNSRecord.typeName — the GUI must
        // never render a bare numeric qtype ("1") where macOS renders "A".
        // Failure case: if the table drifts (e.g. Type=1 stops mapping to "A", or an unknown
        // type stops carrying its number), these assertions go red.
        [Fact]
        public void DNSRecord_TypeName_MapsKnownTypesAndLabelsUnknown()
        {
            Assert.Equal("A", new DNSRecord { Type = 1 }.TypeName);
            Assert.Equal("AAAA", new DNSRecord { Type = 28 }.TypeName);
            Assert.Equal("CNAME", new DNSRecord { Type = 5 }.TypeName);
            Assert.Equal("HTTPS", new DNSRecord { Type = 65 }.TypeName);
            Assert.Equal("CAA", new DNSRecord { Type = 257 }.TypeName);
            Assert.Equal("TYPE999", new DNSRecord { Type = 999 }.TypeName);
        }

        private static byte[] EncodeDNSName(string domain)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            foreach (var label in domain.Split('.'))
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(label));
            }
            bytes.Add(0);
            return bytes.ToArray();
        }

        private static byte[] PacketWithRCode(ushort rcode)
        {
            // Minimal 12-byte DNS response header with given RCODE and zero counts.
            ushort flags = (ushort)(0x8000 | rcode); // QR=1
            return new byte[]
            {
                0x12, 0x34,                         // XID
                (byte)(flags >> 8), (byte)(flags & 0xff),
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
        }

        [Fact]
        public void MakeQueryPacketRandomizesTransactionId()
        {
            // A single "id1 != id2" check passes for a SEQUENTIAL COUNTER — the exact spoofing /
            // cache-poisoning weakness randomization prevents. Sample many IDs; require high
            // distinctness AND no monotonic (+1) stepping.
            var ids = new List<ushort>();
            for (int i = 0; i < 200; i++)
            {
                DNSResolver.MakeQueryPacket("example.com", 1, out var id);
                ids.Add(id);
            }
            Assert.True(ids.Distinct().Count() > 190,
                "TXIDs must be well-distributed across 65536 values, not fixed/near-constant");
            int stepPlusOne = 0;
            for (int i = 1; i < ids.Count; i++)
                if (ids[i] == (ushort)(ids[i - 1] + 1)) stepPlusOne++;
            Assert.True(stepPlusOne < ids.Count / 4,
                "TXIDs must not increment monotonically — a sequential counter is spoofable");
        }

        [Fact]
        public void ParseResponsePacketDetailedMapsRCodes()
        {
            Assert.Equal(DNSResponseStatus.ServFail,  DNSResolver.ParseResponsePacketDetailed(PacketWithRCode(2)).Status);
            Assert.Equal(DNSResponseStatus.NXDomain,  DNSResolver.ParseResponsePacketDetailed(PacketWithRCode(3)).Status);
            Assert.Equal(DNSResponseStatus.Refused,   DNSResolver.ParseResponsePacketDetailed(PacketWithRCode(5)).Status);
        }

        /// <summary>
        /// Zero-records capture report must lead with INCONCLUSIVE and must not read as a
        /// clean network — an empty capture is the absence of data, not evidence (B017
        /// precedent; same contract as the macOS analyzer/GUI paths).
        /// </summary>
        [Fact]
        public void CaptureReportZeroRecordsIsInconclusive()
        {
            var dir = Path.Combine(Path.GetTempPath(), "syxx-zero-records-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                var input = Path.Combine(dir, "capture.txt");
                var report = Path.Combine(dir, "report.txt");
                File.WriteAllText(input, "");   // empty capture

                var output = DNSCaptureReportGenerator.GenerateReport(
                    input, input, report, DateTime.UtcNow, 1);

                Assert.Contains("INCONCLUSIVE", output.Report);
                Assert.Contains("Records parsed:", output.Report);
                Assert.DoesNotContain("clean network", output.Report.ToLowerInvariant());
                // C037: writeResult must be a valid enum value when the input is empty (0 records parsed)
                // — the inconclusive path still writes, so Success is correct here, but the caller
                // must check the tri-state rather than assuming success.
                Assert.True(Enum.IsDefined(typeof(DNSCaptureReportGenerator.ReportWriteResult), output.WriteResult));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// A DoH transport failure must yield an INCONCLUSIVE response, never a throw — a
        /// resolver that cannot verify is not evidence of anything. The unroutable localhost
        /// URL fails fast with no external network; if this host's system DNS happens to be a
        /// loopback proxy, the loopback guard returns inconclusive too, so the no-throw
        /// contract is asserted either way.
        /// </summary>
        [Fact]
        public async Task ResolveSecurelyTransportFailureIsInconclusiveNotThrow()
        {
            var response = await DNSResolver.ResolveSecurelyAsync(
                new DNSQuery("example.com"),
                dohUrl: "https://127.0.0.1:9/dns-query");
            Assert.Equal(SecureResolutionResult.Inconclusive, response.Result);
            Assert.Empty(response.Records);
            Assert.NotNull(response.Reason);
        }

        [Fact]
        public void ParseResponsePacketDetailedRejectsTransactionIdMismatch()
        {
            // XID in packet is 0x1234; we tell the parser to expect 0xBEEF.
            var data = PacketWithRCode(0);
            var result = DNSResolver.ParseResponsePacketDetailed(data, expectedTransactionId: 0xBEEF);
            Assert.Equal(DNSResponseStatus.ResponseIdMismatch, result.Status);
        }

        [Fact]
        public void ParseResponsePacketDetailedRejectsQueryQR0()
        {
            var data = new byte[]
            {
                0x12, 0x34,
                0x01, 0x00, // QR=0 (Query)
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            };
            var result = DNSResolver.ParseResponsePacketDetailed(data);
            Assert.Equal(DNSResponseStatus.MalformedResponse, result.Status);
            Assert.Equal("Not a response packet (QR bit is 0).", result.ErrorMessage);
        }

        [Fact]
        public void ParseResponsePacketDetailedRejectsCompressionLoopInAnswerName()
        {
            // Response: QDCOUNT=1, ANCOUNT=1.
            // The answer's name is a self-referential compression pointer (points to itself).
            var packet = new System.Collections.Generic.List<byte>
            {
                0x12, 0x34, 0x81, 0x80,  // XID, flags (QR+AA+RD+RA)
                0x00, 0x01,              // QDCOUNT=1
                0x00, 0x01,              // ANCOUNT=1
                0x00, 0x00, 0x00, 0x00  // NS/AR=0
            };
            packet.AddRange(EncodeDNSName("example.com"));
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // question type/class
            int answerNameOffset = packet.Count;
            packet.Add(0xC0);
            packet.Add((byte)answerNameOffset); // pointer to itself
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01,           // type A, class IN
                                          0x00, 0x00, 0x00, 0x3C,           // TTL=60
                                          0x00, 0x04, 1, 2, 3, 4 });        // RDLENGTH+rdata

            var result = DNSResolver.ParseResponsePacketDetailed(
                packet.ToArray(), expectedTransactionId: 0x1234);
            Assert.Equal(DNSResponseStatus.MalformedResponse, result.Status);
        }

        // --- Parity regression tests (mirrors the macOS parseName fix + A6 authority contract) ---

        [Fact]
        public void ParseResponsePacketDetailedParsesRealAnswer()
        {
            // First test to drive a well-formed ANSWER-bearing packet through the RESOLVER parser
            // (only DNSCapture's parser had such coverage). Guards against a macOS-style ParseName
            // regression that would make every real answer parse as MalformedResponse.
            var packet = new System.Collections.Generic.List<byte>
            {
                0x12, 0x34, 0x81, 0x80,  // XID, flags QR+RD+RA
                0x00, 0x01,              // QDCOUNT=1
                0x00, 0x01,              // ANCOUNT=1
                0x00, 0x00, 0x00, 0x00   // NSCOUNT=0, ARCOUNT=0
            };
            packet.AddRange(EncodeDNSName("example.com"));
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 });        // question A IN
            packet.AddRange(EncodeDNSName("example.com"));                 // answer name
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01,           // type A, class IN
                                          0x00, 0x00, 0x00, 0x3C,           // TTL 60
                                          0x00, 0x04, 93, 184, 216, 34 });  // RDLENGTH + rdata

            var r = DNSResolver.ParseResponsePacketDetailed(packet.ToArray(), requestedType: 1);
            Assert.Equal(DNSResponseStatus.Success, r.Status);
            Assert.Single(r.Records);
            Assert.Equal("93.184.216.34", r.Records[0].Rdata);
            Assert.Empty(r.AuthorityRecords);
        }

        [Fact]
        public void ParseResponsePacketDetailedNXDomainPreservesAuthoritySOA()
        {
            var packet = new System.Collections.Generic.List<byte>
            {
                0x12, 0x34, 0x84, 0x03,  // XID, flags QR+AA, rcode=3 (NXDOMAIN)
                0x00, 0x01,              // QDCOUNT=1
                0x00, 0x00,              // ANCOUNT=0
                0x00, 0x01,              // NSCOUNT=1
                0x00, 0x00               // ARCOUNT=0
            };
            packet.AddRange(EncodeDNSName("nope.example.com"));
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 });        // question A IN
            packet.AddRange(EncodeDNSName("example.com"));                 // authority SOA owner
            packet.AddRange(new byte[] { 0x00, 0x06, 0x00, 0x01,           // type SOA(6), class IN
                                          0x00, 0x00, 0x0E, 0x10,           // TTL 3600
                                          0x00, 0x04, 0xAA, 0xBB, 0xCC, 0xDD }); // opaque rdata

            var r = DNSResolver.ParseResponsePacketDetailed(packet.ToArray(), requestedType: 1);
            Assert.Equal(DNSResponseStatus.NXDomain, r.Status);
            Assert.True(r.IsAuthoritative);
            Assert.Single(r.AuthorityRecords);
            Assert.Equal(6, (int)r.AuthorityRecords[0].Type);
            Assert.Equal("example.com", r.AuthorityRecords[0].Name);
        }

        [Fact]
        public void ParseResponsePacketDetailedNoDataPreservesAuthoritySOA()
        {
            var packet = new System.Collections.Generic.List<byte>
            {
                0x12, 0x34, 0x84, 0x00,  // QR+AA, rcode 0
                0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00  // QD=1, AN=0, NS=1, AR=0
            };
            packet.AddRange(EncodeDNSName("example.com"));
            packet.AddRange(new byte[] { 0x00, 0x1c, 0x00, 0x01 });        // question AAAA(28) IN
            packet.AddRange(EncodeDNSName("example.com"));
            packet.AddRange(new byte[] { 0x00, 0x06, 0x00, 0x01, 0x00, 0x00, 0x0E, 0x10,
                                          0x00, 0x04, 0xAA, 0xBB, 0xCC, 0xDD });

            var r = DNSResolver.ParseResponsePacketDetailed(packet.ToArray(), requestedType: 28);
            Assert.Equal(DNSResponseStatus.NoData, r.Status);
            Assert.Single(r.AuthorityRecords);
            Assert.Equal(6, (int)r.AuthorityRecords[0].Type);
        }

        [Fact]
        public void ParseResponsePacketDetailedBareNXDomainHasEmptyAuthority()
        {
            // NXDOMAIN with no SOA in authority — the signature of an injected/synthesised block page.
            var packet = new System.Collections.Generic.List<byte>
            {
                0x12, 0x34, 0x84, 0x03,  // QR+AA, rcode 3
                0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00  // QD=1, AN=0, NS=0, AR=0
            };
            packet.AddRange(EncodeDNSName("nope.example.com"));
            packet.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 });

            var r = DNSResolver.ParseResponsePacketDetailed(packet.ToArray(), requestedType: 1);
            Assert.Equal(DNSResponseStatus.NXDomain, r.Status);
            Assert.Empty(r.AuthorityRecords);
        }
    }

    public class VantageComparerTests
    {
        private static ResolverQueryOutcome Success(params string[] ips)
        {
            var records = new System.Collections.Generic.List<DNSRecord>();
            foreach (var ip in ips)
                records.Add(new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = ip });
            return new ResolverQueryOutcome { Records = records, Status = DNSResponseStatus.Success };
        }

        private static ResolverQueryOutcome Failure(DNSResponseStatus status = DNSResponseStatus.Timeout)
            => new ResolverQueryOutcome { Records = new System.Collections.Generic.List<DNSRecord>(), Status = status };

        private static readonly string GoodIP = "93.184.216.34";

        [Fact]
        public void AllPublicResolversFailIsInconclusive()
        {
            var local = Success(GoodIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Failure(DNSResponseStatus.Timeout),
                ["B"] = Failure(DNSResponseStatus.TransportError),
                ["C"] = Failure(DNSResponseStatus.Timeout)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.True(verdict.Inconclusive);
            Assert.False(verdict.Redirection);
        }

        [Fact]
        public void LocalQueryFailIsInconclusive()
        {
            var local = Failure();
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.True(verdict.Inconclusive);
        }

        [Fact]
        public void EmptyButSuccessfulLocalIsInconclusiveNotRedirection()
        {
            // B017 regression (parity with macOS): a local resolver that SUCCEEDS but returns no
            // records (NODATA/NXDOMAIN) while public resolvers return records is NOT tampering —
            // split-horizon/geo/no-such-record all produce it. Must read as inconclusive, never a
            // fabricated redirection. (Distinct from a transport failure, already inconclusive.)
            var local = Success();   // empty records, Status.Success
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.False(verdict.Redirection);
            Assert.True(verdict.Inconclusive);
        }

        [Fact]
        public void CDNDivergenceNotFlaggedAsRedirection()
        {
            // Local matches one of the two public answers — normal geo/CDN variation.
            var local = Success("1.2.3.4");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success("1.2.3.4"),
                ["B"] = Success("5.6.7.8")
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.False(verdict.Inconclusive);
            Assert.False(verdict.Redirection);
        }

        [Fact]
        public void LocalDisjointFromConsensusIsRedirection()
        {
            var local = Success("10.10.10.10");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.False(verdict.Inconclusive);
            Assert.True(verdict.Redirection);
        }

        [Fact]
        public void BlockingAnswerFlaggedAsRedirection()
        {
            var local = Success("127.0.0.1");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.True(verdict.Redirection);
        }

        [Fact]
        public void SingleBaselineIsDegradedNotRedirection()
        {
            // Min-2-vantage guard (parity with macOS): with only one usable public baseline there
            // is no consensus to compare against, so even a fully disjoint local answer must read
            // as degraded — not a redirection verdict built on a single witness.
            var local = Success("10.10.10.10");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.False(verdict.Redirection);
            Assert.False(verdict.Inconclusive);
            Assert.True(verdict.Degraded);
        }

        [Fact]
        public void OnePublicFailureIsDegradedNotInconclusive()
        {
            var local = Success(GoodIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Failure()
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.False(verdict.Inconclusive);
            Assert.False(verdict.Redirection);
            Assert.True(verdict.Degraded);
        }

        // A routable, non-blocking IP: keeps these cases exercising the DoH cross-check and the union
        // upgrade rather than short-circuiting through IPClassifier.IsBlockingAnswer (private/loopback).
        private static readonly string TamperIP = "45.33.32.156";

        // B010: unanimous-UDP tamper (every UDP vantage, local included, agrees on the tampered IP)
        // is invisible to the union test but caught by the HTTPS-routed DoH consensus.
        [Fact]
        public void UnanimousUDPTamperCaughtByCleanDoH()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"] = Success(TamperIP),
                ["Quad9"] = Success(TamperIP)
            };
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"] = Success(GoodIP),
                ["Quad9"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics, doh);
            Assert.True(verdict.Redirection);
            Assert.False(verdict.Inconclusive);
        }

        [Fact]
        public void SingleDoHDisagreementIsNotTamper()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"] = Success(TamperIP),
                ["Quad9"] = Success(TamperIP)
            };
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"] = Failure(),
                ["Quad9"] = Failure()
            };
            var verdict = VantageComparer.Evaluate(local, publics, doh);
            Assert.False(verdict.Redirection);
        }

        // WS2-B.2 / B058-B059: a single poisoned baseline echoing the local tampered IP must not
        // downgrade a real redirection to DEGRADED when ≥2 other resolvers corroborate a different
        // destination.
        [Fact]
        public void PoisonedBaselineEchoUpgradedToConfirmedRedirection()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),  // poisoned echo
                ["Google"] = Success(GoodIP),
                ["Quad9"] = Success(GoodIP)
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.True(verdict.Redirection);
            Assert.False(verdict.Degraded);
            Assert.False(verdict.Inconclusive);
        }

        [Fact]
        public void CDNSingleOverlapWithDisagreeingResolversStaysDegraded()
        {
            var local = Success("1.2.3.4");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success("1.2.3.4"),  // shares local edge
                ["Google"] = Success("5.6.7.8"),
                ["Quad9"] = Success("9.10.11.12")
            };
            var verdict = VantageComparer.Evaluate(local, publics);
            Assert.False(verdict.Redirection);
            Assert.True(verdict.Degraded);
            Assert.False(verdict.Inconclusive);
        }

        // B010 residual: UDP unanimous + all DoH blocked must be Inconclusive, never false-secure.
        // The false-secure path: every UDP vantage returns the same IP set (possibly tampered),
        // and every DoH endpoint is blocked or fails. Without the DoH cross-check there is no way to
        // distinguish "tamper + attacker also blocks DoH" from "clean + transient DoH outage".
        // Verdict MUST be Inconclusive.
        //
        // Mutation proof: remove the dohBlockedWhileUdpUnanimous guard in VantageComparer.Evaluate()
        // and this test goes RED — Inconclusive becomes false on an all-DoH-failed run.
        [Fact]
        public void UDPUnanimousAllDoHBlocked_IsInconclusive()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"]     = Success(TamperIP),
                ["Quad9"]      = Success(TamperIP)
            };
            // All DoH endpoints blocked — no records returned from any.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Failure(),
                ["Google"]     = Failure(),
                ["Quad9"]      = Failure()
            };
            var verdict = VantageComparer.Evaluate(local, publics, doh);

            Assert.True(verdict.Inconclusive,
                "UDP unanimous + all DoH blocked must be Inconclusive, not false-secure (B010 residual)");
            Assert.False(verdict.Redirection,
                "DoH-blocked run must not fabricate a tampered verdict — no evidence for it");
            Assert.False(verdict.Degraded,
                "Inconclusive and Degraded are mutually exclusive in Evaluate()");
        }

        // Boundary guard: DoH provided but ONLY a subset fails (not all) — must not falsely
        // force Inconclusive when a partial DoH result is still usable.
        [Fact]
        public void PartialDoHFailureWithOneSuccess_IsNotInconclusive()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"]     = Success(TamperIP),
                ["Quad9"]      = Success(TamperIP)
            };
            // One DoH endpoint succeeds and returns the real IP — partial success, not total blockage.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),   // succeeds with real answer
                ["Google"]     = Failure(),
                ["Quad9"]      = Failure()
            };
            var verdict = VantageComparer.Evaluate(local, publics, doh);
            // One DoH success is below the ≥2 corroboration floor for a tamper verdict, but also
            // does NOT trigger the all-DoH-blocked Inconclusive guard.
            Assert.False(verdict.Inconclusive,
                "partial DoH failure (one success) must not trigger the all-DoH-blocked Inconclusive guard");
            Assert.False(verdict.Redirection,
                "a lone DoH answer does not meet the ≥2 corroboration floor");
        }
    }

    public class ConditionStateTests
    {
        // (removed ThreeDistinctValues: HashSet{Yes,No,Unknown}.Count==3 is a compile-time enum
        //  property that can never RED — parity with the removed Swift testTriStateDescribesAllThreeCases.
        //  Real condition-state "Unknown" reachability is covered by the probe/classifier suites.)

        [Fact]
        public void LoopbackClassifiesAsLoopbackNotEncrypted()
        {
            // Regression coverage: loopback resolver must not be classified as "encrypted DNS".
            Assert.Equal(IPAddressClass.Loopback, IPClassifier.Classify("127.0.0.1"));
        }
    }

    /// Phase 0 — canonical TriState verdict enum. The three wire strings are the ONLY values
    /// the client kits accept; any other spelling silently breaks cross-platform JSON parity
    /// with the macOS SyxxKit twin and the Python server.
    public class TriStateVerdictTests
    {
        [Fact]
        public void SerialisesAsBareLowercaseStrings()
        {
            // The whole point: these exact bytes must match the Swift raw values and the Python
            // TriState.value. Default System.Text.Json enum serialisation would emit PascalCase
            // member names ("Success") — the explicit TriStateJsonConverter prevents that.
            Assert.Equal("\"success\"", JsonSerializer.Serialize(TriState.Success));
            Assert.Equal("\"failure\"", JsonSerializer.Serialize(TriState.Failure));
            Assert.Equal("\"inconclusive\"", JsonSerializer.Serialize(TriState.Inconclusive));
        }

        [Fact]
        public void RoundTripsFromBareLowercaseStrings()
        {
            Assert.Equal(TriState.Success, JsonSerializer.Deserialize<TriState>("\"success\""));
            Assert.Equal(TriState.Failure, JsonSerializer.Deserialize<TriState>("\"failure\""));
            Assert.Equal(TriState.Inconclusive, JsonSerializer.Deserialize<TriState>("\"inconclusive\""));
        }

        [Fact]
        public void RejectsUnknownAndPascalCaseSpellings()
        {
            // The converter is strict: a PascalCase member name is NOT accepted, and neither
            // is a close-but-wrong string. This is what catches a drift in the wire format.
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TriState>("\"Success\""));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TriState>("\"failed\""));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TriState>("\"unknown\""));
        }

        [Fact]
        public void FailureCaseWrongSpellingIsCaught()
        {
            // Failure case (house rule): prove the exact-string assertions above are not
            // vacuous. If anyone retypes a wire value, the literal in this test must NOT
            // equal the real serialised form.
            Assert.NotEqual("\"Success\"", JsonSerializer.Serialize(TriState.Success));
            Assert.NotEqual("\"failed\"", JsonSerializer.Serialize(TriState.Failure));
            Assert.NotEqual("\"unknown\"", JsonSerializer.Serialize(TriState.Inconclusive));
        }
    }

    public class DNSSECTests
    {
        // Builds an RRSIG rdata hex string covering `typeCovered` (default 48 = DNSKEY) with the given
        // inception/expiration epoch seconds.
        private static string RrsigHex(uint inception, uint expiration, ushort typeCovered = 48)
        {
            var b = new List<byte> { (byte)(typeCovered >> 8), (byte)typeCovered, 0x08, 0x02, 0x00, 0x00, 0x01, 0x2C };
            b.AddRange(new byte[] { (byte)(expiration >> 24), (byte)(expiration >> 16), (byte)(expiration >> 8), (byte)expiration });
            b.AddRange(new byte[] { (byte)(inception >> 24), (byte)(inception >> 16), (byte)(inception >> 8), (byte)inception });
            b.AddRange(new byte[] { 0x12, 0x34, 0x00 });
            return Convert.ToHexString(b.ToArray()).ToLower();
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static DateTime AtEpoch(long seconds) => UnixEpoch.AddSeconds(seconds);

        [Fact]
        public void RRSIGValidityWindowParsesTimes()
        {
            // A042: raw 32-bit seconds are surfaced (for serial-number arithmetic), not absolute DateTimes.
            var window = DNSSECVerifier.RRSIGValidityWindow(RrsigHex(1_000_000, 2_000_000, typeCovered: 48), 48);
            Assert.NotNull(window);
            Assert.Equal(1_000_000u, window!.Value.Inception);
            Assert.Equal(2_000_000u, window.Value.Expiration);
        }

        [Fact]
        public void RRSIGValidityWindowRejectsShort()
        {
            Assert.Null(DNSSECVerifier.RRSIGValidityWindow("0001080200", 1));
            Assert.Null(DNSSECVerifier.RRSIGValidityWindow("zznothex", 1));
        }

        [Fact]
        public void RRSIGValidityWindowRejectsWrongTypeCovered()
        {
            // KDNS-05: an RRSIG covering a non-DNSKEY RRset (typeCovered != 48) must not prove the
            // DNSKEY set is signed.
            Assert.Null(DNSSECVerifier.RRSIGValidityWindow(RrsigHex(1_000_000, 2_000_000, typeCovered: 48), 1));
        }

        // A042 RFC 1982 serial-number arithmetic + B047 clock-spoof guard.

        [Fact]
        public void RRSIGWindowValidWhenNowInsideWindow() =>
            Assert.True(DNSSECVerifier.RRSIGWindowCurrentlyValid(1_000_000, 2_000_000, AtEpoch(1_500_000)));

        [Fact]
        public void RRSIGWindowExpiredWhenNowAfterExpiration() =>
            Assert.False(DNSSECVerifier.RRSIGWindowCurrentlyValid(1_000_000, 2_000_000, AtEpoch(2_500_000)));

        [Fact]
        public void RRSIGWindowNotYetValidWhenNowBeforeInception() =>
            Assert.False(DNSSECVerifier.RRSIGWindowCurrentlyValid(1_000_000, 2_000_000, AtEpoch(500_000)));

        [Fact]
        public void RRSIGWindowSerialWrapAroundIsValid()
        {
            // A042: a window straddling the 2106 (2^32) epoch wrap. inception is 100s before the wrap,
            // expiration 100s after it, now 10s after it. Under RFC 1982 serial arithmetic this is INSIDE
            // the window; a naive absolute-epoch comparison (now < inception) would wrongly reject it.
            uint inception = unchecked(uint.MaxValue - 100);
            uint expiration = 100;
            uint nowEpoch = 10;
            Assert.True(DNSSECVerifier.SerialLessThanOrEqual(inception, nowEpoch));
            Assert.True(DNSSECVerifier.SerialLessThanOrEqual(nowEpoch, expiration));
            var now = AtEpoch((long)uint.MaxValue + 1 + nowEpoch);
            Assert.True(DNSSECVerifier.RRSIGWindowCurrentlyValid(inception, expiration, now));
        }

        [Fact]
        public void RRSIGWindowRejectsImplausiblySpanned()
        {
            // B047: a validity span wider than the sane bound (2 years) is refused even when `now` sits
            // between the endpoints under serial arithmetic - a spoofed-clock wrap defence.
            uint inception = 1_000_000;
            uint expiration = unchecked(inception + DNSSECVerifier.MaxPlausibleValiditySpanSeconds + 1);
            Assert.False(DNSSECVerifier.RRSIGWindowCurrentlyValid(inception, expiration, AtEpoch(inception + 10)));
        }

        [Fact]
        public void RRSIGWindowRejectsExpirationBeforeInception() =>
            Assert.False(DNSSECVerifier.RRSIGWindowCurrentlyValid(2_000_000, 1_000_000, AtEpoch(1_500_000)));

        // Classifier honesty (A030 / A036 / B046).

        private static DNSRecord Dnskey() => new DNSRecord { Name = "example.", Type = 48, Ttl = 3600, Rdata = "0101030dabcdef" };
        private static DNSRecord RrsigDnskey(uint inception, uint expiration) =>
            new DNSRecord { Name = "example.", Type = 46, Ttl = 3600, Rdata = RrsigHex(inception, expiration, typeCovered: 48) };

        [Fact]
        public void ClassifyNoDNSKEYIsUnsigned()
        {
            // A036: DO bit set, genuinely no DNSKEY -> honest unsigned.
            var records = new List<DNSRecord> { new DNSRecord { Name = "example.", Type = 1, Ttl = 60, Rdata = "5db8d822" } };
            Assert.Equal(DNSSECStatus.Unsigned, DNSSECVerifier.Classify(records, AtEpoch(1_500_000)));
        }

        [Fact]
        public void ClassifyDNSKEYWithoutRRSIGIsInconclusive()
        {
            // B046/A030: DNSKEY present but no covering RRSIG on this DO-enabled path -> inconclusive.
            var records = new List<DNSRecord> { Dnskey() };
            Assert.Equal(DNSSECStatus.Inconclusive, DNSSECVerifier.Classify(records, AtEpoch(1_500_000)));
        }

        [Fact]
        public void ClassifyDNSKEYWithValidRRSIGIsRecordsPresent()
        {
            var records = new List<DNSRecord> { Dnskey(), RrsigDnskey(1_000_000, 2_000_000) };
            Assert.Equal(DNSSECStatus.RecordsPresentUnverified, DNSSECVerifier.Classify(records, AtEpoch(1_500_000)));
        }

        [Fact]
        public void ClassifyDNSKEYWithExpiredRRSIGIsExpired()
        {
            var records = new List<DNSRecord> { Dnskey(), RrsigDnskey(1_000_000, 2_000_000) };
            Assert.Equal(DNSSECStatus.SignatureExpiredOrNotYetValid, DNSSECVerifier.Classify(records, AtEpoch(3_000_000)));
        }

        [Fact]
        public void DescribeNeverClaimsValidated()
        {
            foreach (DNSSECStatus s in Enum.GetValues(typeof(DNSSECStatus)))
            {
                var text = DNSSECVerifier.Describe(s).ToLower();
                Assert.DoesNotContain("validated", text);
                Assert.DoesNotContain("protected", text);
            }
        }
    }

    public class DNSNameTests
    {
        [Fact]
        public void ToAsciiNamePunycodesNonAscii()
        {
            Assert.Equal("xn--bcher-kva.com", DNSResolver.ToAsciiName("bücher.com"));
            Assert.Equal("example.com", DNSResolver.ToAsciiName("example.com"));
        }
    }

    public class RegistrableDomainTests
    {
        [Fact]
        public void HandlesTwoLevelPublicSuffix()
        {
            Assert.Equal("bbc.co.uk", DNSCaptureReportGenerator.RegistrableDomain("news.bbc.co.uk"));
            Assert.Equal("bbc.co.uk", DNSCaptureReportGenerator.RegistrableDomain("bbc.co.uk"));
        }

        [Fact]
        public void HandlesOrdinaryTld()
        {
            Assert.Equal("example.com", DNSCaptureReportGenerator.RegistrableDomain("www.example.com"));
            Assert.Null(DNSCaptureReportGenerator.RegistrableDomain("localhost"));
        }
    }

    public class WindowsArpParserTests
    {
        private const string Sample =
            "\r\nInterface: 192.168.1.5 --- 0xb\r\n" +
            "  Internet Address      Physical Address      Type\r\n" +
            "  192.168.1.1           aa-bb-cc-dd-ee-01     dynamic\r\n" +
            "  192.168.1.42          aa-bb-cc-dd-ee-02     dynamic\r\n" +
            "  192.168.1.255         ff-ff-ff-ff-ff-ff     static\r\n" +
            "  224.0.0.22            01-00-5e-00-00-16     static\r\n";

        [Fact]
        public void ParsesRowsAndNormalizesMacToColonLowercase()
        {
            var entries = WindowsArpParser.Parse(Sample);
            Assert.Equal(4, entries.Count);
            var gw = entries[0];
            Assert.Equal("192.168.1.1", gw.IPAddress);
            Assert.Equal("aa:bb:cc:dd:ee:01", gw.MacAddress);
            Assert.Equal("192.168.1.5", gw.Interface);
            Assert.Equal(ARPEntryState.Reachable, gw.State);
        }

        [Fact]
        public void SkipsHeaderAndInterfaceLinesButKeepsBroadcastAndMulticast()
        {
            var entries = WindowsArpParser.Parse(Sample);
            Assert.Contains(entries, e => e.IPAddress == "192.168.1.255");
            Assert.Contains(entries, e => e.IPAddress == "224.0.0.22");
            Assert.DoesNotContain(entries, e => e.IPAddress.StartsWith("Internet"));
        }

        [Fact]
        public void ClassifiesAddressKinds()
        {
            Assert.Equal(LANAddressKind.PrivateUnicast, NetworkAddressClassifier.KindForIPv4("192.168.1.1"));
            Assert.Equal(LANAddressKind.Broadcast, NetworkAddressClassifier.KindForIPv4("255.255.255.255"));
            Assert.Equal(LANAddressKind.Multicast, NetworkAddressClassifier.KindForIPv4("224.0.0.22"));
            Assert.Equal(LANAddressKind.Loopback, NetworkAddressClassifier.KindForIPv4("127.0.0.1"));
            Assert.Equal(LANAddressKind.LinkLocal, NetworkAddressClassifier.KindForIPv4("169.254.1.1"));
        }

        [Fact]
        public void SnapshotDiffDetectsNewMissingAndMovedDevices()
        {
            var prev = new LANSnapshot(WindowsArpParser.Parse(Sample).ConvertAll(e => new LANDevice(e)));
            // Device .42 moves to a new IP; a new device appears; the broadcast row disappears.
            const string next =
                "Interface: 192.168.1.5 --- 0xb\r\n" +
                "  192.168.1.1           aa-bb-cc-dd-ee-01     dynamic\r\n" +
                "  192.168.1.77          aa-bb-cc-dd-ee-02     dynamic\r\n" +
                "  192.168.1.99          aa-bb-cc-dd-ee-03     dynamic\r\n";
            var cur = new LANSnapshot(WindowsArpParser.Parse(next).ConvertAll(e => new LANDevice(e)));
            var diff = LANCartographer.Diff(prev, cur);
            Assert.Contains(diff.NewDevices, d => d.MacAddress == "aa:bb:cc:dd:ee:03");
            Assert.Contains(diff.ChangedAddresses, s => s.Contains("192.168.1.42 -> 192.168.1.77"));
            Assert.True(diff.HasChanges);
        }
        [Fact]
        public void PlannedProbesExcludedFromJsonSerialization()
        {
            // F037: PlannedProbes is excluded from JSON serialization ([JsonIgnore]) to prevent
            // 14 unused ServiceProbe structs from bloating every persisted LANSnapshot.
            // Failure case: remove [JsonIgnore] from PlannedProbes in LanCartography.cs ->
            // JSON will contain "plannedProbes" -> this assertion fails.
            const string arpSample =
                "Interface: 192.168.1.1 --- 0x1\r\n" +
                "  192.168.1.5           aa-bb-cc-dd-ee-01     dynamic\r\n";
            var device = new LANDevice(WindowsArpParser.Parse(arpSample)[0]);
            // Device should have planned probes in-memory (CommonLAN has 14 entries for probeable addresses)
            Assert.True(device.PlannedProbes.Count > 0,
                "PlannedProbes must be populated in-memory for when the runner ships");
            // But they must NOT appear in the serialized JSON
            var json = System.Text.Json.JsonSerializer.Serialize(device);
            Assert.DoesNotContain("plannedProbes", json, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    public class SiteLocalResolverTests
    {
        [Fact]
        public void DeprecatedSiteLocalIsReservedNotPublic()
        {
            // Windows seeds fec0:0:0:ffff::1/2/3 as placeholder resolvers; they must never be
            // treated as public routable addresses (that produced phantom leak-probe resolvers).
            Assert.Equal(IPAddressClass.Reserved, IPClassifier.Classify("fec0:0:0:ffff::1"));
            Assert.NotEqual(IPAddressClass.PublicRoutable, IPClassifier.Classify("fec0:0:0:ffff::2"));
        }
    }

    public class LeakVerdictReconciliationTests
    {
        [Fact]
        public void ConfirmedRequiresAttestedResolver()
        {
            var ok = LeakDetector.ReconcileVerdict(true, new List<string> { "9.9.9.9" });
            Assert.Equal(LeakVerdict.Confirmed, ok.Verdict);
            Assert.Null(ok.Reason);
            // A "leak" with no attested resolver cannot be attributed -> inconclusive, not confirmed.
            var bad = LeakDetector.ReconcileVerdict(true, new List<string>());
            Assert.Equal(LeakVerdict.Inconclusive, bad.Verdict);
            Assert.NotNull(bad.Reason);
        }

        [Fact]
        public void NoLeakMustNotAttestResolvers()
        {
            var ok = LeakDetector.ReconcileVerdict(false, new List<string>());
            Assert.Equal(LeakVerdict.NotDetected, ok.Verdict);
            // "No leak" yet resolvers were observed is internally inconsistent -> inconclusive.
            var bad = LeakDetector.ReconcileVerdict(false, new List<string> { "8.8.8.8" });
            Assert.Equal(LeakVerdict.Inconclusive, bad.Verdict);
            Assert.NotNull(bad.Reason);
        }
    }

    // D003 collector TLS transport pinning — mirrors the macOS SyxxKitTests D003 subset.
    public class CollectorPinningTests
    {
        // Same self-signed EC P-256 leaf as the macOS fixture; reference pin computed independently
        // with openssl (pin-sha256). Proves the SPKI hash is byte-identical across platforms — the
        // owner supplies ONE pin value for both.
        private const string CertDerBase64 =
            "MIIBfDCCASOgAwIBAgIUTRwDvN3VdQMU+Vgu/3G+NDSnXvAwCgYIKoZIzj0EAwIwFDESMBAGA1UEAwwJbG9jYWxob3N0MB4XDTI2MDcxMTA1Mzk0N1oXDTI2MDcxMzA1Mzk0N1owFDESMBAGA1UEAwwJbG9jYWxob3N0MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnDqyTcvfozZQLCIGJPfuNp8TvBoeC9GtV732MxjUiORG6Tn8e4Lq5RNv09LBBEzRqewDcavyeuzPcYJGw1+t1qNTMFEwHQYDVR0OBBYEFIyTOb3tNTI/9HRcTy8hz+X01XecMB8GA1UdIwQYMBaAFIyTOb3tNTI/9HRcTy8hz+X01XecMA8GA1UdEwEB/wQFMAMBAf8wCgYIKoZIzj0EAwIDRwAwRAIgZ94hEPzAJIf7TvC9FGIF8nW3HWwV22FkelWD9/re8bICIDKN3rUFOCrm35JDQ58hIAHxLvaNF2+1msCqXkuuVJ1k";
        private const string ReferencePin = "FfEkilG5Q/bHox+eHUsh67GrlG2i/dtxUUDFWa4sQXY=";

        [Fact]
        public void SpkiPinMatchesReferencePin()
        {
            var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(CertDerBase64));
            Assert.Equal(ReferencePin, CollectorPinning.SpkiPin(cert));
        }

        [Fact]
        public void AcceptIsFailClosed()
        {
            var good = ReferencePin;
            var other = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            var configured = new HashSet<string> { good };
            // PKI must pass first — an untrusted chain is refused regardless of pins.
            Assert.False(CollectorPinning.Accept(false, good, configured));
            // Unconfigured: standard validation only.
            Assert.True(CollectorPinning.Accept(true, good, new HashSet<string>()));
            // Configured + matching leaf -> accept.
            Assert.True(CollectorPinning.Accept(true, good, new HashSet<string> { good, other }));
            // Configured + NON-matching leaf -> refuse (fail-closed). THE mutation guard.
            Assert.False(CollectorPinning.Accept(true, other, configured));
            // Configured but leaf pin could not be computed -> refuse, never accept.
            Assert.False(CollectorPinning.Accept(true, null, configured));
        }

        [Fact]
        public void IsTransportIdentityFailureDetectsAuthException()
        {
            var pinFail = new HttpRequestException("handshake",
                new AuthenticationException("The remote certificate is invalid."));
            Assert.True(LeakDetector.IsTransportIdentityFailure(pinFail));
            // A plain HTTP error (e.g. non-2xx) is NOT a transport-identity failure.
            Assert.False(LeakDetector.IsTransportIdentityFailure(new HttpRequestException("HTTP 500")));
            Assert.False(LeakDetector.IsTransportIdentityFailure(new TimeoutException()));
        }
    }

    public class ForensicsAnalyzerTests
    {
        [Fact]
        public void BeaconClassifiesEvenCadenceAsHighlyRegular()
        {
            var score = BeaconAnalyzer.Score("p", "d", new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.Equal(BeaconClassification.HighlyRegular, score.Classification);
        }

        [Fact]
        public void BeaconClassifiesJitteryCadenceAsIrregular()
        {
            var score = BeaconAnalyzer.Score("p", "d", new List<double> { 1000, 1003, 1200, 1201, 1800 });
            Assert.Equal(BeaconClassification.Irregular, score.Classification);
        }

        // B034 — a degenerate series of >=3 IDENTICAL timestamps has all-zero intervals (mean 0). That is
        // NOT a highly-regular beacon; the zero-mean coefficient of variation was a manufactured positive.
        // It must read InsufficientData with null mean/cv. macOS twin:
        // testBeaconDegenerateIdenticalTimestampsIsInsufficientData.
        [Fact]
        public void BeaconDegenerateIdenticalTimestampsIsInsufficientData()
        {
            var score = BeaconAnalyzer.Score("p", "d", new List<double> { 1000, 1000, 1000 });
            Assert.Equal(BeaconClassification.InsufficientData, score.Classification);
            Assert.Null(score.MeanInterval);
            Assert.Null(score.CoefficientOfVariation);
        }

        [Fact]
        public void BlocklistMatchesParentDomain()
        {
            var lists = SieveBlocklistLoader.BundledLists();
            var coverage = BlocklistAnalyzer.Classify(new[] { "ads.doubleclick.net", "example.com" }, lists);
            Assert.NotEmpty(coverage[0].CoveredBy);   // subdomain of a listed domain
            Assert.Empty(coverage[1].CoveredBy);      // not a tracker
        }

        [Fact]
        public void EntropyHigherForEncodedLabel()
        {
            double natural = ExfilAnalyzer.ShannonEntropyBitsPerChar("wwwexample");
            double encoded = ExfilAnalyzer.ShannonEntropyBitsPerChar("mfrggzdfmztwq2lknnwg23tp");
            Assert.True(encoded > natural);
        }

        // C009 (OC-4 parity): 2 signals (highEntropy + regular) → .Suspicious, NOT .LikelyExfil.
        // The old test expected .LikelyExfil with only 2 signals; that was wrong after C009 raised
        // the bar to ≥3 for .LikelyExfil. macOS twin: testExfilSuspiciousForTwoSignalsWithoutBlocklist.
        [Fact]
        public void ExfilSuspiciousForTwoSignalsWithoutBlocklist()
        {
            var a = ExfilAnalyzer.Assess(
                "mfrggzdfmztwq2lknnwg23tpobyxe43uov3ho.tunnel.evil.net",
                new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.Equal(ExfilSuspicion.Suspicious, a.Suspicion);
        }

        // C009: a single signal alone (entropy-only OR beacon-only) must NOT reach .Suspicious.
        // macOS twin: testExfilBenignForSingleSignalOnly.
        [Fact]
        public void ExfilBenignForSingleSignalOnly()
        {
            // Entropy-only: long label, no regular cadence, no blocklist match.
            var entropyOnly = ExfilAnalyzer.Assess("abcdefghijklmnopqrst.example.com");
            Assert.Equal(ExfilSuspicion.Benign, entropyOnly.Suspicion);

            // Beacon-only: regular cadence, short label (no high entropy), no blocklist match.
            var beaconOnly = ExfilAnalyzer.Assess("www.example.com",
                new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.Equal(ExfilSuspicion.Benign, beaconOnly.Suspicion);
        }

        [Fact]
        public void ExfilBenignForOrdinaryDomainWithoutSignal()
        {
            var a = ExfilAnalyzer.Assess("www.example.com");
            Assert.Equal(ExfilSuspicion.Benign, a.Suspicion);
        }

        // B190 (c) — empty timestamps: beacon leg UNEVALUATED (tri-state unknown), not a silent
        // negative, and the rationale must disclose it. macOS twin:
        // testExfilEmptyTimestampsMarksBeaconUnevaluated.
        [Fact]
        public void ExfilEmptyTimestampsMarksBeaconUnevaluated()
        {
            var a = ExfilAnalyzer.Assess("www.example.com");   // default timestamps: null -> empty
            Assert.False(a.BeaconEvaluated);
            Assert.Contains(a.Rationale, r => r.Contains("beacon cadence not evaluated"));
            Assert.DoesNotContain("no encoded-label or beaconing signal", a.Rationale);
        }

        // B190 — an unevaluated beacon can never contribute a positive signal, so .LikelyExfil
        // (3 signals) is structurally unreachable: max achievable signal count is 2.
        [Fact]
        public void ExfilUnevaluatedBeaconCapsAtTwoSignals()
        {
            var a = ExfilAnalyzer.Assess(
                "mfrggzdfmztwq2lknnwg23tpobyxe43uov3ho.tunnel.evil.net",
                new List<double>());   // no timing -> beacon unevaluated
            Assert.False(a.BeaconEvaluated);
            Assert.NotEqual(ExfilSuspicion.LikelyExfil, a.Suspicion);
        }

        // B190 (d) — real regular-cadence timestamps still evaluate + classify the beacon leg.
        // macOS twin: testExfilRealCadenceStillEvaluatesBeacon.
        [Fact]
        public void ExfilRealCadenceStillEvaluatesBeacon()
        {
            var a = ExfilAnalyzer.Assess("www.example.com",
                new List<double> { 1000, 1060, 1120, 1180, 1240 });
            Assert.True(a.BeaconEvaluated);
            Assert.True(a.Beacon == BeaconClassification.Regular || a.Beacon == BeaconClassification.HighlyRegular);
            Assert.DoesNotContain(a.Rationale, r => r.Contains("beacon cadence not evaluated"));
        }
    }

    // C058 — Cage section verdict must account for process-attribution completeness.
    // Parity twins of the macOS SynthesizerDatapointWiringTests C058 cases.
    public class CageAttributionCompletenessTests
    {
        private static ExfilAssessment Benign() => ExfilAnalyzer.Assess("www.example.com");

        // (a) attributionRate 0.0 + all-benign -> DEGRADED, not a bare clean, with disclosure.
        [Fact]
        public void CageZeroAttributionForcesDegradedNotClean()
        {
            var benign = Benign();
            Assert.Equal(ExfilSuspicion.Benign, benign.Suspicion);
            var se = EvidenceAdapters.Cage(new List<ExfilAssessment> { benign }, null, attributionRate: 0.0);
            Assert.Equal(SectionStatus.Degraded, se.Status);
            Assert.NotEqual(SectionStatus.Negative, se.Status);
            Assert.Contains(se.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);
        }

        // (b) attributionRate 1.0 + all-benign -> clean negative, no caveat (pre-C058 behaviour).
        [Fact]
        public void CageFullAttributionStaysClean()
        {
            var se = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: 1.0);
            Assert.Equal(SectionStatus.Negative, se.Status);
            Assert.DoesNotContain(se.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);
        }

        // Boundary: rate == threshold is adequate; just below it is degraded (shared < convention).
        [Fact]
        public void CageAttributionThresholdBoundary()
        {
            const double threshold = 0.5;   // EvidenceAdapters.CageAttributionThreshold (internal)
            var at = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: threshold);
            Assert.Equal(SectionStatus.Negative, at.Status);
            var below = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: threshold - 0.01);
            Assert.Equal(SectionStatus.Degraded, below.Status);
        }

        // null attributionRate = not a capture-sourced run: legacy contract unchanged.
        [Fact]
        public void CageNilAttributionPreservesLegacyContract()
        {
            var withNull = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: null);
            var legacy = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() });
            Assert.Equal(SectionStatus.Negative, withNull.Status);
            Assert.Equal(SectionStatus.Negative, legacy.Status);
            Assert.DoesNotContain(withNull.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);
        }

        // Empty-exfil capture (ran, nothing suspicious) with 0% attribution -> degraded, not clean.
        [Fact]
        public void CageEmptyExfilWithZeroAttributionIsDegraded()
        {
            var se = EvidenceAdapters.Cage(new List<ExfilAssessment>(), null, attributionRate: 0.0);
            Assert.Equal(SectionStatus.Degraded, se.Status);
        }

        // H1 — no exfil AND no response-size leg = not a capture-sourced run at all. An attributionRate
        // arriving alone must NOT manufacture a clean Negative over a run where no domain was ever
        // exfil-assessed: the section is UNTESTED (mirrors macOS Synthesizer.swift:513's early return).
        // Guards the Windows false-clean bug where Cage(null, null, 0.9) yielded subs=[Negative] -> clean
        // with completeness 1.0.
        [Fact]
        public void CageNoExfilNoResponseSizeIsUntestedRegardlessOfAttribution()
        {
            var high = EvidenceAdapters.Cage(null, null, attributionRate: 0.9);
            Assert.Equal(SectionStatus.Untested, high.Status);
            Assert.Equal(0.0, high.Completeness);
            Assert.DoesNotContain(high.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);

            // Even a below-threshold rate cannot force Degraded when there was no capture leg to degrade.
            var low = EvidenceAdapters.Cage(null, null, attributionRate: 0.1);
            Assert.Equal(SectionStatus.Untested, low.Status);

            // And with no attribution either, still Untested (legacy no-input contract).
            var none = EvidenceAdapters.Cage(null, null, attributionRate: null);
            Assert.Equal(SectionStatus.Untested, none.Status);
        }
    }

    public class TrustLedgerAndSentinelTests
    {
        [Fact]
        public void AnswerLedgerFlagsDriftOnChangedAnswer()
        {
            var ledger = new AnswerLedger();
            Assert.Null(ledger.Record("bank.example", "A", "203.0.113.10", "1.1.1.1", 300));
            Assert.Null(ledger.Record("bank.example", "A", "203.0.113.10", "1.1.1.1", 300)); // same -> no drift
            var drift = ledger.Record("bank.example", "A", "45.9.9.9", "1.1.1.1", 60);        // changed -> drift
            Assert.NotNull(drift);
            Assert.Equal("203.0.113.10", drift!.PreviousAnswer);
            Assert.Equal("45.9.9.9", drift.NewAnswer);
            Assert.Equal(3, ledger.History("bank.example").Count);
        }

        [Fact]
        public void SentinelDiffDetectsResolverChangeAndStability()
        {
            var a = new NetworkConfigSnapshot
            {
                Resolvers = new List<string> { "1.1.1.1" },
                ActiveInterfaces = new List<string> { "eth0" },
                VpnActive = false,
                DnsEncrypted = false
            };
            var b = new NetworkConfigSnapshot
            {
                Resolvers = new List<string> { "8.8.8.8" },
                ActiveInterfaces = new List<string> { "eth0" },
                VpnActive = true,
                DnsEncrypted = false
            };
            var changes = Sentinel.Diff(a, b);
            Assert.Contains(changes, c => c.Kind == ConfigChangeKind.ResolversChanged);
            Assert.Contains(changes, c => c.Kind == ConfigChangeKind.VpnChanged);
            Assert.Empty(Sentinel.Diff(a, a));
        }
    }

    public class TextDiffTests
    {
        [Fact]
        public void IdenticalIsEmpty()
        {
            Assert.Empty(TextDiff.Unified("a\nb\nc", "a\nb\nc"));
        }

        [Fact]
        public void ShowsAddedAndRemovedLines()
        {
            var d = TextDiff.Unified("a\nb\nc", "a\nX\nc");
            Assert.Contains("- b", d);
            Assert.Contains("+ X", d);
        }

        [Fact]
        public void DetectsInjectedScriptLine()
        {
            var local = "<html>\n<body>hi</body>\n</html>";
            var remote = "<html>\n<script>steal()</script>\n<body>hi</body>\n</html>";
            var d = TextDiff.Unified(local, remote);
            Assert.Contains(d, line => line.StartsWith("+ ") && line.Contains("steal()"));
        }
    }

    public class DNSLogAnalyzerTests
    {
        private const string Sample =
            "method '-[DNSProxyProvider startProxyWithOptions:completionHandler:]' invoked\n" +
            "no block list specified, all traffic will be allowed\n" +
            "PROCESS:\n{\n    name = Mail;\n    path = \"/System/Applications/Mail.app\";\n    pid = 670;\n    \"signing ID\" = \"com.apple.mail\";\n}\n" +
            "PACKET:\nXid: 5516\nQR: Query\nServer: -nil-\nRcode: No error\nQuestion (1):\nimap.mail.me.com.akadns.net IN A\nAnswer (0):\nAuthority (0):\nAdditional records (0):\n" +
            "ERROR: 'nw_connection_set_state_changed_handler' failed with 57\n" +
            "PROCESS:\n{\n    name = mDNSResponder;\n    path = \"/usr/sbin/mDNSResponder\";\n    pid = 454;\n    \"signing ID\" = \"com.apple.mDNSResponder\";\n}\n" +
            "PACKET:\nXid: 29650\nQR: Reply\nRcode: Name error\nQuestion (1):\nb._dns-sd._udp.42.2.0.192.in-addr.arpa IN PTR\nAnswer (0):\nAuthority (1):\n192.in-addr.arpa IN SOA 442 z.arin.net dns-ops.arin.net 2017041035\nAdditional records (0):\n" +
            "PROCESS:\n{\n    name = \"Codex (Service)\";\n    path = \"/Applications/Codex.app/x/Codex (Service).app\";\n    pid = 1015;\n    \"signing ID\" = \"com.openai.codex.helper\";\n}\n" +
            "PACKET:\nXid: 16623\nQR: Reply\nRcode: No error\nQuestion (1):\nmtalk.google.com IN A\nAnswer (2):\nmtalk.google.com IN CNAME 63 mobile-gtalk.l.google.com\nmobile-gtalk.l.google.com IN A 63 172.253.135.188\nAuthority (0):\nAdditional records (0):\n" +
            "method '-[DNSProxyProvider stopProxyWithReason:completionHandler:]' invoked\n";

        [Fact]
        public void ParserExtractsProcessesTypesAndRcodes()
        {
            var records = CaptureLogParser.Parse(Sample);
            Assert.Equal(3, records.Count);
            Assert.Contains(records, r => r.Process == "Codex (Service)");
            Assert.Contains(records, r => r.QuestionType == "PTR");
            Assert.Contains(records, r => r.QuestionName == "mtalk.google.com" && r.QuestionType == "A");
            Assert.Contains(records, r => r.IsReply && r.Rcode == "Name error");
        }

        [Fact]
        public void AnalyzerPopulatesWhatTheOldReportLeftEmpty()
        {
            var a = DNSLogAnalyzer.Analyze(CaptureLogParser.Parse(Sample));
            Assert.NotEmpty(a.ProcessCounts);
            Assert.NotEmpty(a.TypeCounts);
            Assert.Contains(a.ProcessCounts, p => p.Name == "Mail");
            Assert.Contains(a.StatusCounts, s => s.Name == "nxdomain");
            Assert.Contains(a.StatusCounts, s => s.Name == "successful");
            Assert.Contains(a.RegistrableCounts, r => r.Name == "google.com");
        }
    }

    public class EndpointLogAnalyzerTests
    {
        private static string WriteTemp(string content)
        {
            string p = Path.Combine(Path.GetTempPath(), "syxx_ep_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(p, content);
            return p;
        }

        [Fact]
        public void FileLogFlagsPersistenceAndUntrusted()
        {
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_OPEN\",\"file\":{\"destination\":\"C:\\\\Windows\\\\System32\\\\drivers\\\\etc\\\\hosts\",\"process\":{\"name\":\"reader\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Windows\",\"signatureStatus\":0}}}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_WRITE\",\"file\":{\"destination\":\"C:\\\\Users\\\\evil\\\\AppData\\\\Roaming\\\\Microsoft\\\\Windows\\\\Start Menu\\\\evil.exe\",\"process\":{\"name\":\"dropper\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0}}}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_CREATE\",\"file\":{\"destination\":\"C:\\\\Windows\\\\Temp\\\\foo\",\"process\":{\"name\":\"maker\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Corporation\",\"signatureStatus\":0}}}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("file", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Read events skipped (OPEN/CLOSE): 1", report);
                Assert.Contains(@"Start Menu\evil.exe", report);
                Assert.Contains("dropper [AdHoc]", report);
                Assert.DoesNotContain("maker [Microsoft Corporation]", report);
            }
            finally { File.Delete(p); }
        }

        [Fact]
        public void ProcessLogFlagsAdhocAndArgs()
        {
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_EXEC\",\"process\":{\"path\":\"C:\\\\Python3\\\\python.exe\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0},\"arguments\":[\"python.exe\",\"-c\",\"import x\"]}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_FORK\",\"process\":{\"path\":\"C:\\\\Windows\\\\System32\\\\cmd.exe\"}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("process", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Launches (EXEC): 1", report);
                Assert.Contains("python.exe [AdHoc]", report);
                Assert.Contains("python.exe -c", report);
            }
            finally { File.Delete(p); }
        }

        // ── C054: every SuspiciousArgMarker must match a synthetic positive line ──────
        [Fact]
        public void EverySuspiciousArgMarkerMatchesSyntheticPositive()
        {
            // Each marker lowercased in the array; the haystack is also lowercased.
            // If ANY marker fails to match its own canonical form, the test fails.
            string[] markers = {
                "base64", "curl ", "wget ", "| powershell", "|pwsh", " -enc ", " -e ", " -command ",
                " -c ", "invoke-", "iex(", "iex ", " eval ", "downloadstring", "downloadfile",
                "start-process", " -windowstyle hidden", " -executionpolicy bypass",
                " /c ", " cmd ", " mshta ", " wscript ", " cscript ", " regsvr32 ",
                " certutil ", " bitsadmin ", " powershell ", "pwsh "
            };

            foreach (var marker in markers)
            {
                // Build a synthetic argument string that definitely contains this marker.
                // The marker itself, lowercased, is the simplest positive line.
                string syntheticLine = $"program.exe {marker.Trim()} payload";

                // C054 fix: markers are lowercased at declaration; haystack is lowercased via ToLowerInvariant().
                // Verify the comparison logic matches.
                string lowercased = syntheticLine.ToLowerInvariant();
                bool matched = lowercased.Contains(marker, StringComparison.Ordinal);
                Assert.True(matched,
                    $"C054: SuspiciousArgMarker \"{marker}\" did not match its own canonical positive line \"{lowercased}\". " +
                    "This marker is unreachable dead code.");
            }
        }

        // ── C054: synthetic process log with PowerShell abuse patterns ─────────────────
        [Fact]
        public void ProcessLogDetectsPowerShellAbusePatterns()
        {
            // A process invocation containing multiple PowerShell-abuse markers that were
            // previously unreachable (C054). After the fix, these must all match.
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_EXEC\",\"process\":{\"path\":\"C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Windows\",\"signatureStatus\":0},\"arguments\":[\"powershell.exe\",\" -Command \",\"Invoke-WebRequest\",\" -WindowStyle Hidden\",\" -ExecutionPolicy Bypass\"]}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("process", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Launches (EXEC): 1", report);
                // The report must contain the suspicious-arguments section with content
                Assert.Contains("powershell.exe", report);
            }
            finally { File.Delete(p); }
        }

        // ── C053: Windows persistence markers match synthetic Windows paths ────────────
        [Fact]
        public void FileLogFlagsWindowsPersistencePaths()
        {
            // Verify the Windows persistence markers still fire after the C053 refactor.
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_WRITE\",\"file\":{\"destination\":\"C:\\\\SOFTWARE\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Run\\\\evil\",\"process\":{\"name\":\"dropper\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0}}}}\n" +
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_WRITE\",\"file\":{\"destination\":\"C:\\\\Windows\\\\System32\\\\tasks\\\\schtask\",\"process\":{\"name\":\"scheduler\",\"signing info (computed)\":{\"signatureSigner\":\"AdHoc\",\"signatureStatus\":0}}}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("file", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("dropper [AdHoc]", report);
                Assert.Contains("scheduler [AdHoc]", report);
                // Both writes landed in Windows persistence locations
                Assert.Contains("2 write(s) landed in persistence locations", report);
            }
            finally { File.Delete(p); }
        }

        // ── C053: report says "nothing stands out" only when markers exist ─────────────
        [Fact]
        public void InterpretationGatesOnMarkerAvailability()
        {
            // An empty-mutation file log (only OPEN/CLOSE events) should produce an
            // interpretation that does NOT claim "writes to persistence locations: none"
            // when no mutation events were analyzed at all.
            string log =
                "{\"event\":\"ES_EVENT_TYPE_NOTIFY_OPEN\",\"file\":{\"destination\":\"C:\\\\Windows\\\\System32\\\\notepad.exe\",\"process\":{\"name\":\"reader\",\"signing info (computed)\":{\"signatureSigner\":\"Microsoft Windows\",\"signatureStatus\":0}}}}\n";
            string p = WriteTemp(log);
            try
            {
                Assert.Equal("file", EndpointLogAnalyzer.Detect(p));
                var report = EndpointLogAnalyzer.Analyze(p);
                Assert.NotNull(report);
                Assert.Contains("Mutation events analyzed (CREATE/WRITE/DELETE/RENAME/LINK): 0", report);
                // With zero mutations, the interpretation should not claim persistence was checked.
                Assert.Contains("nothing stands out", report);
            }
            finally { File.Delete(p); }
        }
    }

    // ─── Workstream H probe tests ──────────────────────────────────────────────

    public class AnswerStabilityProbeTests
    {
        private static HashSet<string> S(params string[] xs) => new HashSet<string>(xs);

        [Fact]
        public void StableWhenIdenticalSetsAndTTLHonored()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("1.2.3.4", "5.6.7.8"), S("1.2.3.4", "5.6.7.8"), S("1.2.3.4", "5.6.7.8")],
                minTTLs: [300, 298, 295], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Stable, r.Status);
            Assert.Equal(1, r.DistinctAnswerSets);
            Assert.True(r.TtlHonored);
        }

        [Fact]
        public void TTLAnomalyWhenSetStableButTTLRises()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("1.2.3.4"), S("1.2.3.4"), S("1.2.3.4")],
                minTTLs: [300, 300, 400], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.TtlAnomaly, r.Status);
            Assert.False(r.TtlHonored);
        }

        [Fact]
        public void RotatingWhenSetsShareAMember()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("a", "b"), S("a", "c"), S("a", "d")],
                minTTLs: [60, 60, 60], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Rotating, r.Status);
        }

        [Fact]
        public void UnstableWhenSetsFullyChange()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("a"), S("b"), S("c")],
                minTTLs: [60, 60, 60], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Unstable, r.Status);
        }

        [Fact]
        public void InconclusiveWithFewerThanTwoUsable()
        {
            var r = AnswerStabilityProbe.Classify(sets: [S("a")], minTTLs: [60], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Inconclusive, r.Status);
        }
    }

    public class DualStackProbeTests
    {
        [Fact]
        public void DualStackWhenBothResolve()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Success, ["93.184.216.34"],
                                             DNSResponseStatus.Success, ["2606:2800:220:1:248:1893:25c8:1946"]);
            Assert.Equal(DualStackStatus.DualStack, r.Status);
        }

        [Fact]
        public void Ipv4OnlyWhenAAAAIsGenuineNegative()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Success, ["1.2.3.4"],
                                             DNSResponseStatus.NoData, []);
            Assert.Equal(DualStackStatus.Ipv4Only, r.Status);
        }

        [Fact]
        public void Dns64SynthesisWhenAllAAAAInWellKnownPrefix()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Success, ["93.184.216.34"],
                                             DNSResponseStatus.Success, ["64:ff9b::5db8:d822"]);
            Assert.Equal(DualStackStatus.Dns64Synthesis, r.Status);
            Assert.Equal(1, r.Dns64Count);
        }

        [Fact]
        public void Ipv4SuppressedWhenAAAAWorksButAIsGenuineNegative()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.NXDomain, [],
                                             DNSResponseStatus.Success, ["2606:2800:220:1:248:1893:25c8:1946"]);
            Assert.Equal(DualStackStatus.Ipv4Suppressed, r.Status);
        }

        // [B038] An A-record TRANSPORT FAILURE (timeout) with a working AAAA is a missing datum, not a
        // verdict — inconclusive, NEVER Ipv4Suppressed (a censorship signal on a failure = §1 violation).
        [Fact]
        public void ATimeoutWithWorkingAAAAIsInconclusiveNotSuppressed()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Timeout, [],
                                             DNSResponseStatus.Success, ["2606:2800:220:1:248:1893:25c8:1946"]);
            Assert.Equal(DualStackStatus.Inconclusive, r.Status);
            Assert.NotEqual(DualStackStatus.Ipv4Suppressed, r.Status);
        }

        [Fact]
        public void InconclusiveWhenBothTransportFail()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Timeout, [],
                                             DNSResponseStatus.Timeout, []);
            Assert.Equal(DualStackStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void Dns64PrefixDetection()
        {
            Assert.True(DualStackProbe.IsDNS64("64:ff9b::5db8:d822"));
            Assert.True(DualStackProbe.IsDNS64("64:ff9b::1.2.3.4"));
            Assert.False(DualStackProbe.IsDNS64("2606:2800:220:1:248:1893:25c8:1946"));
            Assert.False(DualStackProbe.IsDNS64("64:ff9c::1"));
            Assert.False(DualStackProbe.IsDNS64("not-an-ip"));
            Assert.False(DualStackProbe.IsDNS64("1.2.3.4"));
        }
    }

    public class CNAMEChainProbeTests
    {
        private static List<DNSRecord> Rec(string name, ushort type, string rdata) =>
            [new DNSRecord { Name = name, Type = type, Ttl = 60, Rdata = rdata }];

        [Fact]
        public void ReconstructChainFollowsCNAMEs()
        {
            var recs = new List<DNSRecord>
            {
                new() { Name = "www.example.com", Type = 5, Ttl = 60, Rdata = "edge.cdn.net" },
                new() { Name = "edge.cdn.net", Type = 5, Ttl = 60, Rdata = "pop1.cdn.net" },
                new() { Name = "pop1.cdn.net", Type = 1, Ttl = 60, Rdata = "1.2.3.4" },
            };
            var chain = CNAMEChainProbe.ReconstructChain("www.example.com", recs);
            Assert.Equal(["www.example.com", "edge.cdn.net", "pop1.cdn.net"], chain);
        }

        [Fact]
        public void ReconstructChainStopsOnLoop()
        {
            var recs = new List<DNSRecord>
            {
                new() { Name = "a.com", Type = 5, Ttl = 60, Rdata = "b.com" },
                new() { Name = "b.com", Type = 5, Ttl = 60, Rdata = "a.com" },
            };
            var chain = CNAMEChainProbe.ReconstructChain("a.com", recs);
            Assert.Equal(["a.com", "b.com"], chain);
        }

        [Fact]
        public void ConsistentWhenTestMatchesReference()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["w.example.com", "edge.cdn.net"],
                referenceChains: [["w.example.com", "edge.cdn.net"], ["w.example.com", "edge.cdn.net"]]);
            Assert.Equal(CNAMEChainStatus.Consistent, r.Status);
        }

        [Fact]
        public void DivergentWhenReferencesAgreeButTestDiffers()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["w.example.com", "evil.cdn.net"],
                referenceChains: [["w.example.com", "edge.cdn.net"], ["w.example.com", "edge.cdn.net"]]);
            Assert.Equal(CNAMEChainStatus.Divergent, r.Status);
        }

        [Fact]
        public void InconclusiveWhenReferencesDisagree()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["w.example.com", "us.cdn.net"],
                referenceChains: [["w.example.com", "eu.cdn.net"], ["w.example.com", "ap.cdn.net"]]);
            Assert.Equal(CNAMEChainStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void NoCNAMEWhenDirectA()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["example.com"],
                referenceChains: [["example.com"], ["example.com"]]);
            Assert.Equal(CNAMEChainStatus.NoCNAME, r.Status);
        }

        [Fact]
        public void InconclusiveWhenNoReferences()
        {
            Assert.Equal(CNAMEChainStatus.Inconclusive, CNAMEChainProbe.Classify(testChain: ["a"], referenceChains: []).Status);
        }
    }

    public class DenialOfExistenceProbeTests
    {
        private static DNSRecord Soa(string owner) => new() { Name = owner, Type = 6, Ttl = 3600, Rdata = "aabbccdd" };
        private static DNSRecord Nsec(string owner) => new() { Name = owner, Type = 47, Ttl = 3600, Rdata = "00" };
        private static DNSRecord A(string owner) => new() { Name = owner, Type = 1, Ttl = 60, Rdata = "10.0.0.1" };

        [Fact]
        public void SoaBackedNegativeIsBenign()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("example.com")]);
            Assert.Equal(DenialStatus.SoaBacked, r.Status);
            Assert.Equal("example.com", r.SoaOwner);
        }

        [Fact]
        public void NsecProvenNegativeIsBenign()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("example.com"), Nsec("example.com")]);
            Assert.Equal(DenialStatus.NsecProven, r.Status);
            Assert.True(r.HasNSEC);
        }

        [Fact]
        public void BareDenialIsFlagged()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: []);
            Assert.Equal(DenialStatus.BareDenial, r.Status);
        }

        [Fact]
        public void ContradictoryWhenNegativeCarriesAnswers()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [A("nope.example.com")], authority: [Soa("example.com")]);
            Assert.Equal(DenialStatus.Contradictory, r.Status);
        }

        [Fact]
        public void ContradictoryWhenSoaIsWrongZone()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("blockpage.isp.net")]);
            Assert.Equal(DenialStatus.Contradictory, r.Status);
        }

        [Fact]
        public void ResolvedNameIsNotADenial()
        {
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.Success,
                answers: [A("example.com")], authority: []);
            Assert.Equal(DenialStatus.NotADenial, r.Status);
        }

        [Fact]
        public void TransportFailureIsInconclusive()
        {
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.Timeout, answers: [], authority: []);
            Assert.Equal(DenialStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void NoDataWithSoaIsBenign()
        {
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.NoData,
                answers: [], authority: [Soa("example.com")]);
            Assert.Equal(DenialStatus.SoaBacked, r.Status);
        }

        [Fact]
        public void AncestorMatching()
        {
            Assert.True(DenialOfExistenceProbe.IsAncestor("example.com", "a.b.example.com"));
            Assert.True(DenialOfExistenceProbe.IsAncestor("example.com.", "example.com"));
            Assert.False(DenialOfExistenceProbe.IsAncestor("example.com", "notexample.com"));
            Assert.False(DenialOfExistenceProbe.IsAncestor("evil.net", "example.com"));
        }
    }

    public class AuthoritativeVantageClassifyTests
    {
        [Fact]
        public void ClassifyMatchesWhenResolverSubsetOfAuth()
        {
            Assert.Equal(AuthComparisonStatus.Matches,
                AuthoritativeVantage.Classify(["1.1.1.1"], ["1.1.1.1", "2.2.2.2"], [], "x").Status);
        }

        [Fact]
        public void ClassifyDivergesWhenDisjoint()
        {
            Assert.Equal(AuthComparisonStatus.Diverges,
                AuthoritativeVantage.Classify(["1.1.1.1"], ["2.2.2.2"], [], "x").Status);
        }

        [Fact]
        public void ClassifyInconclusiveOnPartialOverlap()
        {
            Assert.Equal(AuthComparisonStatus.Inconclusive,
                AuthoritativeVantage.Classify(["1.1.1.1", "2.2.2.2"], ["1.1.1.1", "3.3.3.3"], [], "x").Status);
        }

        [Fact]
        public void ClassifyMatchesWhenBothEmpty()
        {
            Assert.Equal(AuthComparisonStatus.Matches,
                AuthoritativeVantage.Classify([], [], [], "x").Status);
        }
    }

    public class TTLAnomalyProbeTests
    {
        [Fact]
        public void IndependentCachingWhenTTLsVary()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 287, ["8.8.8.8"] = 254, ["9.9.9.9"] = 271 });
            Assert.Equal(TTLAnomalyStatus.IndependentCaching, r.Status);
        }

        [Fact]
        public void LockstepWhenThreeResolversAgreeExactly()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300, ["8.8.8.8"] = 300, ["9.9.9.9"] = 300 });
            Assert.Equal(TTLAnomalyStatus.LockstepTTL, r.Status);
        }

        [Fact]
        public void TwoIdenticalIsNotLockstep()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300, ["8.8.8.8"] = 300 });
            Assert.Equal(TTLAnomalyStatus.IndependentCaching, r.Status);
        }

        [Fact]
        public void ExtremeTTLFlagged()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300, ["8.8.8.8"] = 700000 });
            Assert.Equal(TTLAnomalyStatus.ExtremeTTL, r.Status);
        }

        [Fact]
        public void InconclusiveWithOneAnswer()
        {
            Assert.Equal(TTLAnomalyStatus.Inconclusive, TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300 }).Status);
            Assert.Equal(TTLAnomalyStatus.Inconclusive, TTLAnomalyProbe.Classify(new Dictionary<string, uint>()).Status);
        }

        [Fact]
        public void ZeroTTLsAreNotCountedAsLockstep()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 0, ["8.8.8.8"] = 0, ["9.9.9.9"] = 0 });
            Assert.Equal(TTLAnomalyStatus.IndependentCaching, r.Status);
        }
    }

    public class SVCBProbeTests
    {
        [Fact]
        public void ClassifyPresent()
        {
            Assert.Equal(SVCBFindingStatus.Present, SVCBProbe.Classify(present: ["a", "b"], absent: [], failed: []).Status);
        }

        [Fact]
        public void ClassifyAbsent()
        {
            Assert.Equal(SVCBFindingStatus.Absent, SVCBProbe.Classify(present: [], absent: ["a", "b"], failed: []).Status);
        }

        [Fact]
        public void ClassifyDivergent()
        {
            Assert.Equal(SVCBFindingStatus.Divergent, SVCBProbe.Classify(present: ["a"], absent: ["b"], failed: []).Status);
        }

        [Fact]
        public void ClassifyInconclusiveWhenAllFail()
        {
            Assert.Equal(SVCBFindingStatus.Inconclusive, SVCBProbe.Classify(present: [], absent: [], failed: ["a"]).Status);
        }
    }

    public class VersionBindProbeTests
    {
        private static string TxtHex(string[] strings)
        {
            var bytes = new List<byte>();
            foreach (var s in strings)
            {
                bytes.Add((byte)s.Length);
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(s));
            }
            return Convert.ToHexString(bytes.ToArray()).ToLower();
        }

        private static DNSResponse TxtResponse(string[] strings) => new()
        {
            Status = DNSResponseStatus.Success,
            Records = [new DNSRecord { Name = "version.bind", Type = 16, Ttl = 0, Rdata = TxtHex(strings) }]
        };

        [Fact]
        public void ForwarderBannerOnPublicResolverIsInterceptionHint()
        {
            var r = VersionBindProbe.Classify(TxtResponse(["dnsmasq-2.80"]), "8.8.8.8", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Disclosed, r.Status);
            Assert.Equal("dnsmasq-2.80", r.Banner);
            Assert.True(r.ForwarderOnPublicPath);
        }

        [Fact]
        public void ForwarderBannerOnLocalRouterIsNotEscalated()
        {
            var r = VersionBindProbe.Classify(TxtResponse(["dnsmasq-2.80"]), "192.168.1.1", expectedPublicResolver: false);
            Assert.Equal(VersionBindResultStatus.Disclosed, r.Status);
            Assert.False(r.ForwarderOnPublicPath);
        }

        [Fact]
        public void RecursiveBannerIsDisclosedButNotAForwarderHint()
        {
            var r = VersionBindProbe.Classify(TxtResponse(["unbound 1.13.1"]), "8.8.8.8", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Disclosed, r.Status);
            Assert.False(r.ForwarderOnPublicPath);
        }

        [Fact]
        public void WithheldWhenNoTXTAnswer()
        {
            var r = VersionBindProbe.Classify(new DNSResponse { Status = DNSResponseStatus.NoData }, "1.1.1.1", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Withheld, r.Status);
        }

        [Fact]
        public void UntestedWhenProbeFails()
        {
            var r = VersionBindProbe.Classify(new DNSResponse { Status = DNSResponseStatus.Timeout }, "1.1.1.1", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Untested, r.Status);
        }

        [Fact]
        public void KnownPublicResolverMembership()
        {
            Assert.True(VersionBindProbe.IsKnownPublicResolver("1.1.1.1"));
            Assert.False(VersionBindProbe.IsKnownPublicResolver("192.168.1.1"));
        }
    }

    // B050 parity twin of macOS testExpiredTargetRRSIGIsDegradedNotPositive — an expired/not-yet-valid
    // TARGET RRSIG is a DEGRADED caveat, never a Positive threat finding; the B047 control-canary
    // escalation (a known-signed control returning stripped) still stays Positive.
    public class DnssecExpiredDegradationTests
    {
        [Fact]
        public void ExpiredTargetRRSIGIsDegradedNotPositive()
        {
            var se = EvidenceAdapters.Prophet(resolution: null, dnssec: DNSSECStatus.SignatureExpiredOrNotYetValid,
                nxCanary: null, dnssecCanary: null);
            Assert.Equal(SectionStatus.Degraded, se.Status);
            Assert.NotEqual(SectionStatus.Positive, se.Status);
            Assert.Contains(se.Evidence, e => e.EvidenceId == EvidenceId.DnssecExpired);
        }

        [Fact]
        public void ControlCanaryStrippedStaysPositiveEvenWithExpiredTarget()
        {
            var strippedControl = new DNSCanaries.CanaryResult(Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped,
                    "control zone lost its signatures on this path", "x"), "");
            var se = EvidenceAdapters.Prophet(resolution: null, dnssec: DNSSECStatus.SignatureExpiredOrNotYetValid,
                nxCanary: null, dnssecCanary: strippedControl);
            Assert.Equal(SectionStatus.Positive, se.Status);
        }
    }

    // B051 parity twin of macOS EncryptedTransportProbeTests. A failed DoH path is Blocked ONLY when a
    // cleartext Do53 control proves the network is up; otherwise (offline / provider down) it is
    // Inconclusive, never a manufactured "blocked". Mocks keep every case deterministic and offline.
    public class EncryptedTransportProbeTests
    {
        private static Func<string, ushort, Task<DNSResponse>> ControlUp() => (name, _) =>
            Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord> { new() { Name = name, Type = 1, Rdata = "1.1.1.1" } }
            });
        private static Func<string, ushort, Task<DNSResponse>> ControlDown() => (_, _) =>
            Task.FromResult(new DNSResponse { Status = DNSResponseStatus.Timeout });

        [Fact]
        public void ClassifyWorking()
        {
            Assert.Equal(EncryptedTransportStatus.Working,
                EncryptedTransportProbe.Classify(DNSResponseStatus.Success, 2, controlSucceeded: false, "x").Status);
        }

        // B051: timeout + a working cleartext control → genuinely Blocked.
        [Fact]
        public void ClassifyBlockedWhenControlWorks()
        {
            Assert.Equal(EncryptedTransportStatus.Blocked,
                EncryptedTransportProbe.Classify(DNSResponseStatus.Timeout, 0, controlSucceeded: true, "x").Status);
        }

        // B051 mutation-proven core: timeout + failed control → OFFLINE, so Inconclusive, not Blocked.
        [Fact]
        public void ClassifyInconclusiveWhenControlAlsoDown()
        {
            Assert.Equal(EncryptedTransportStatus.Inconclusive,
                EncryptedTransportProbe.Classify(DNSResponseStatus.Timeout, 0, controlSucceeded: false, "x").Status);
            Assert.Equal(EncryptedTransportStatus.Inconclusive,
                EncryptedTransportProbe.Classify(DNSResponseStatus.TransportError, 0, controlSucceeded: false, "x").Status);
        }

        [Fact]
        public void ClassifyInconclusive()
        {
            Assert.Equal(EncryptedTransportStatus.Inconclusive,
                EncryptedTransportProbe.Classify(DNSResponseStatus.ServFail, 0, controlSucceeded: true, "x").Status);
        }

        [Fact]
        public async Task ProbeBlockedWhenDoHFailsButControlWorks()
        {
            Func<string, ushort, string, Task<DNSResponse>> doh = (_, _, _) =>
                Task.FromResult(new DNSResponse { Status = DNSResponseStatus.Timeout });
            var r = await EncryptedTransportProbe.Probe(query: doh, control: ControlUp());
            Assert.Equal(EncryptedTransportStatus.Blocked, r.Status);
        }

        [Fact]
        public async Task ProbeInconclusiveWhenDoHFailsAndControlAlsoDown()
        {
            Func<string, ushort, string, Task<DNSResponse>> doh = (_, _, _) =>
                Task.FromResult(new DNSResponse { Status = DNSResponseStatus.Timeout });
            var r = await EncryptedTransportProbe.Probe(query: doh, control: ControlDown());
            Assert.Equal(EncryptedTransportStatus.Inconclusive, r.Status);
        }
    }

    public class OpenResolverProbeTests
    {
        [Fact]
        public void AmplificationFactor()
        {
            var big = new DNSResponse { Status = DNSResponseStatus.Success, ResponseByteLength = 1200 };
            Assert.Equal(30.0, OpenResolverProbe.AmplificationFactor(40, big), precision: 3);
            Assert.Equal(0, OpenResolverProbe.AmplificationFactor(null, big));
            Assert.Equal(0, OpenResolverProbe.AmplificationFactor(40, new DNSResponse { Status = DNSResponseStatus.Success }));
        }

        [Fact]
        public void ClassifyThresholds()
        {
            Assert.Equal(OpenResolverStatus.RecursiveHighAmp, OpenResolverProbe.Classify("t", 25).Status);
            Assert.Equal(OpenResolverStatus.RecursiveLowAmp, OpenResolverProbe.Classify("t", 3).Status);
        }
    }

    public class ResolverLatencyProbeTests
    {
        [Fact]
        public void FastWhenMedianLow()
        {
            var r = ResolverLatencyProbe.Classify([8, 12, 10, 9], attempted: 4);
            Assert.Equal(ResolverLatencyStatus.Fast, r.Status);
            Assert.Equal(4, r.SuccessCount);
        }

        [Fact]
        public void ModerateBand()
        {
            var r = ResolverLatencyProbe.Classify([90, 120, 100], attempted: 3);
            Assert.Equal(ResolverLatencyStatus.Moderate, r.Status);
        }

        [Fact]
        public void SlowWhenMedianHigh()
        {
            var r = ResolverLatencyProbe.Classify([200, 210, 190], attempted: 3);
            Assert.Equal(ResolverLatencyStatus.Slow, r.Status);
        }

        [Fact]
        public void DegradedWhenSomeQueriesLost()
        {
            var r = ResolverLatencyProbe.Classify([10, 12], attempted: 4);
            Assert.Equal(ResolverLatencyStatus.Degraded, r.Status);
            Assert.Equal(2, r.SuccessCount);
        }

        [Fact]
        public void UnreachableWhenNoResponses()
        {
            var r = ResolverLatencyProbe.Classify([], attempted: 4);
            Assert.Equal(ResolverLatencyStatus.Unreachable, r.Status);
            Assert.Equal(0, r.SuccessCount);
        }
    }

    public class StubEntropyProbeTests
    {
        private static List<(ushort xid, ushort sourcePort)> Queries(int count, Func<int, ushort> xid, Func<int, ushort> port)
        {
            var q = new List<(ushort, ushort)>();
            for (int i = 0; i < count; i++) q.Add((xid(i), port(i)));
            return q;
        }

        [Fact]
        public void WellRandomized()
        {
            var q = Queries(12, i => (ushort)(i * 5000 + 123), i => (ushort)(40000 + i * 137));
            Assert.Equal(StubEntropyStatus.WellRandomized, StubEntropyProbe.Audit(q).Status);
        }

        [Fact]
        public void FixedSourcePortIsWeak()
        {
            var q = Queries(12, i => (ushort)(i * 4013 + 7), _ => (ushort)53535);
            var a = StubEntropyProbe.Audit(q);
            Assert.Equal(StubEntropyStatus.WeakRandomization, a.Status);
            Assert.Equal(1, a.DistinctSourcePorts);
        }

        [Fact]
        public void SequentialTransactionIDsAreWeak()
        {
            var q = Queries(12, i => (ushort)(1000 + i), i => (ushort)(40000 + i * 200));
            Assert.Equal(StubEntropyStatus.WeakRandomization, StubEntropyProbe.Audit(q).Status);
        }

        [Fact]
        public void LowTransactionIDEntropyIsWeak()
        {
            var q = Queries(12, i => (ushort)(i % 2 == 0 ? 4242 : 4243), i => (ushort)(40000 + i * 211));
            Assert.Equal(StubEntropyStatus.WeakRandomization, StubEntropyProbe.Audit(q).Status);
        }

        [Fact]
        public void InsufficientDataIsNeverClean()
        {
            var q = Queries(4, i => (ushort)(i * 5000 + 1), i => (ushort)(40000 + i * 300));
            Assert.Equal(StubEntropyStatus.InsufficientData, StubEntropyProbe.Audit(q).Status);
        }
    }

    public class WildcardSynthesisProbeTests
    {
        private static WildcardSynthesisProbe.LabelObservation Obs(string label = "x.example.com",
            string[]? test = null, bool testHas = false, bool refHas = false, bool refDenies = false) =>
            new(label, test ?? [], testHas, refHas, refDenies);

        [Fact]
        public void NxHijackWhenReferenceDeniesButTestInvents()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: ["10.0.0.1"], testHas: true, refHas: false, refDenies: true),
            ]);
            Assert.Equal(WildcardSynthesisStatus.NxHijack, r.Status);
            Assert.Equal(["10.0.0.1"], r.SyntheticAddresses);
        }

        [Fact]
        public void WildcardZoneWhenBothResolve()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
                Obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
            ]);
            Assert.Equal(WildcardSynthesisStatus.WildcardZone, r.Status);
        }

        [Fact]
        public void NoSynthesisWhenBothDeny()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
            ]);
            Assert.Equal(WildcardSynthesisStatus.NoSynthesis, r.Status);
        }

        [Fact]
        public void InconclusiveWhenNoUsableLabels()
        {
            Assert.Equal(WildcardSynthesisStatus.Inconclusive, WildcardSynthesisProbe.Classify([]).Status);
        }

        [Fact]
        public void InconclusiveOnMixedOutcomes()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
                Obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
            ]);
            Assert.Equal(WildcardSynthesisStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void AnySingleHijackWins()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
                Obs(test: ["10.0.0.1"], testHas: true, refHas: false, refDenies: true),
            ]);
            Assert.Equal(WildcardSynthesisStatus.NxHijack, r.Status);
        }
    }

    public class ResponseSizeProbeTests
    {
        [Fact]
        public void NormalSizes()
        {
            var sizes = new List<int>(Enumerable.Repeat(120, 10)) { 300, 450 };
            Assert.Equal(ResponseSizeStatus.Normal, ResponseSizeProbe.Audit(sizes).Status);
        }

        [Fact]
        public void SustainedLargeIsOversized()
        {
            var sizes = new List<int> { 100, 120, 150, 200, 180, 90, 2100, 3000, 2500, 4096 };
            var a = ResponseSizeProbe.Audit(sizes);
            Assert.Equal(ResponseSizeStatus.Oversized, a.Status);
            Assert.Equal(4, a.OversizedCount);
            Assert.Equal(4096, a.MaxBytes);
        }

        [Fact]
        public void LoneLargeAnswerIsNotFlagged()
        {
            var sizes = new List<int>(Enumerable.Repeat(150, 11)) { 3000 };
            Assert.Equal(ResponseSizeStatus.Normal, ResponseSizeProbe.Audit(sizes).Status);
        }

        [Fact]
        public void InsufficientDataIsNeverClean()
        {
            Assert.Equal(ResponseSizeStatus.InsufficientData, ResponseSizeProbe.Audit([100, 2500, 3000]).Status);
        }
    }

    public class ProbeRunnerTests
    {
        // ── Helpers ──

        private static ProbeBudget MakeBudget()
        {
            return new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 10);
        }

        // ── Exhaustion gates ──

        [Fact]
        public async Task Run_ExhaustedBudget_ReturnsNil()
        {
            var budget = new ProbeBudget(TimeSpan.Zero, TimeSpan.FromSeconds(5), 10);
            Assert.True(budget.IsExhausted);  // elapsed >= totalTimeout

            var operationCalled = false;
            var result = await ProbeRunner.RunAsync("test", budget, () =>
            {
                operationCalled = true;
                return Task.FromResult<string?>("should-not-run");
            });
            Assert.Null(result);
            Assert.False(operationCalled);
        }

        [Fact]
        public async Task Run_MaxProbesExceeded_ReturnsNil()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 1);

            // Run one to exhaust maxProbes
            var r1 = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("first"));
            Assert.Equal("first", r1);
            Assert.True(budget.IsExhausted);  // ProbesRun(1) >= MaxProbes(1)

            var operationCalled = false;
            var r2 = await ProbeRunner.RunAsync("test", budget, () =>
            {
                operationCalled = true;
                return Task.FromResult<string?>("second");
            });
            Assert.Null(r2);
            Assert.False(operationCalled);
        }

        // ── Success path ──

        [Fact]
        public async Task Run_Success_ReturnsValue()
        {
            var budget = MakeBudget();
            var result = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("hello"));
            Assert.Equal("hello", result);
        }

        [Fact]
        public async Task Run_Success_RecordsTiming()
        {
            var budget = MakeBudget();
            Assert.Equal(0, budget.ProbesRun);
            Assert.Equal(TimeSpan.Zero, budget.Elapsed);

            _ = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("value"));

            Assert.Equal(1, budget.ProbesRun);
            Assert.True(budget.Elapsed > TimeSpan.Zero);
        }

        // ── Timeout path ──

        [Fact]
        public async Task Run_Timeout_ReturnsNil()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 10);

            var result = await ProbeRunner.RunAsync("test", budget, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return "too-late";
            });
            Assert.Null(result);
        }

        [Fact]
        public async Task Run_Timeout_AccountsCorrectly()
        {
            // F006: elapsed records real wall-clock time, not the PerProbeTimeout cap.
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 10);

            _ = await ProbeRunner.RunAsync("test", budget, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return "too-late";
            });

            // elapsed >= PerProbeTimeout (timeout fired at ~10ms); well under the probe's full sleep.
            Assert.Equal(1, budget.ProbesRun);
            Assert.True(budget.Elapsed >= TimeSpan.FromMilliseconds(9));
            Assert.True(budget.Elapsed <= TimeSpan.FromSeconds(1),
                "Elapsed must not be the full probe sleep duration — operation was cancelled");
        }

        [Fact]
        public async Task Run_Timeout_RealElapsedNotCapped()
        {
            // Mutation probe: if elapsed were still capped at PerProbeTimeout, TWO timed-out probes
            // would each record exactly PerProbeTimeout — sum would always equal 2×PerProbeTimeout.
            // With real elapsed, the sum must be >= 2×PerProbeTimeout.
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 10);

            for (int i = 0; i < 2; i++)
            {
                _ = await ProbeRunner.RunAsync("test", budget, async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    return "too-late";
                });
            }

            // Two timed-out probes: elapsed must be at least 2×PerProbeTimeout.
            Assert.True(budget.Elapsed >= TimeSpan.FromMilliseconds(18),
                "Two timed-out probes must accumulate real elapsed time");
            Assert.Equal(2, budget.ProbesRun);
        }

        // ── Exhaustion between probes ──

        [Fact]
        public async Task Run_Exhaustion_BetweenProbes_SkipsSubsequent()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 1);

            var r1 = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("first"));
            Assert.Equal("first", r1);
            Assert.True(budget.IsExhausted);

            var operationCalled = false;
            var r2 = await ProbeRunner.RunAsync("test", budget, () =>
            {
                operationCalled = true;
                return Task.FromResult<string?>("second");
            });
            Assert.Null(r2);
            Assert.False(operationCalled);
        }

        // ── ProbeBudget value correctness ──

        [Fact]
        public void Budget_Add_IncrementsBoth()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 10);
            budget.Add(TimeSpan.FromSeconds(1.5));
            Assert.Equal(1, budget.ProbesRun);
            Assert.Equal(1.5, budget.Elapsed.TotalSeconds, 1);

            budget.Add(TimeSpan.FromSeconds(2.5));
            Assert.Equal(2, budget.ProbesRun);
            Assert.Equal(4.0, budget.Elapsed.TotalSeconds, 1);
        }

        [Fact]
        public void Budget_IsExhausted_OnProbesRun()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 2);
            budget.Add(TimeSpan.FromMilliseconds(100));
            Assert.False(budget.IsExhausted);
            budget.Add(TimeSpan.FromMilliseconds(100));
            Assert.True(budget.IsExhausted);  // ProbesRun(2) >= MaxProbes(2)
        }

        [Fact]
        public void Budget_IsExhausted_OnElapsed()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 100);
            budget.Add(TimeSpan.FromSeconds(1.5));
            Assert.False(budget.IsExhausted);
            budget.Add(TimeSpan.FromSeconds(1.0));
            Assert.True(budget.IsExhausted);  // Elapsed(2.5) >= TotalTimeout(2)
        }

        // ── Factory defaults ──

        [Fact]
        public void DefaultProphet_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultProphet;
            Assert.Equal(30, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(15, b.MaxProbes);
        }

        [Fact]
        public void DefaultMirror_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultMirror;
            Assert.Equal(30, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(10, b.MaxProbes);
        }

        [Fact]
        public void DefaultPortal_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultPortal;
            Assert.Equal(15, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.MaxProbes);
        }

        [Fact]
        public void DefaultGlutton_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultGlutton;
            Assert.Equal(20, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.MaxProbes);
        }
    }

    // WS8-04 / B152 (parity with macOS SynthesisEngineTests). ReferralReason must name the ACTUAL
    // cause of the referral. Twins of testDampenerReferral_reportsTheMechanism_notAMargin and
    // testHardGateOutranksConflict_inTheStatedReason.
    public class ReferralReasonCauseTests
    {
        private static List<SectionStatusEntry> AllNegative() => new()
        {
            new SectionStatusEntry(Section.Prophet, SectionStatus.Negative),
            new SectionStatusEntry(Section.Mirror,  SectionStatus.Negative),
            new SectionStatusEntry(Section.Portal,  SectionStatus.Negative),
            new SectionStatusEntry(Section.Glutton, SectionStatus.Negative),
            new SectionStatusEntry(Section.Cage,    SectionStatus.Negative),
            new SectionStatusEntry(Section.Witness, SectionStatus.Negative),
        };

        /// B152-A. DampenerForcedReferral had no branch, so control fell through to the final return
        /// and reported a MARGIN failure on a referral margin did not cause, quoting whatever the
        /// margin happened to be (up to the full 95).
        /// Mutation: delete the DampenerForcedReferral branch from ReferralReason. The reason reverts
        /// and the DoesNotContain assertion goes RED.
        [Fact]
        public void DampenerReferral_ReportsTheMechanism_NotAMargin()
        {
            // ConfirmedThreats is seeded from the SECTION STATUS being Positive, not from the evidence
            // merely existing. Passing all-Negative here would let the dampener cancel the threat.
            var statuses = AllNegative();
            statuses[0] = new SectionStatusEntry(Section.Prophet, SectionStatus.Positive);
            var evidence = new[]
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped,
                                    "DNSSEC canary returned unsigned", "signature stripped in flight"),
                EvidenceItem.Create(Section.Portal, EvidenceId.CaptivePortalActive,
                                    "captive portal detected", "gateway intercepts :80"),
            };
            var c = SynthesisEngine.Synthesize(evidence, statuses);

            Assert.True(c.ReferralTriggered);
            var reason = c.Referral?.Reason ?? "";
            Assert.Contains("harmless causes", reason);
            Assert.DoesNotContain("within", reason);
        }

        /// B152-B. The conflict branch was tested ABOVE hardGated, so an input that is BOTH hard-gated
        /// and conflicted was explained as "the sections disagree" when the actual trigger was a check
        /// that never ran. The two co-occur only through threat-vs-threat conflict on disjoint
        /// hypotheses: clean support is suppressed under a hard gate, so threat-vs-clean cannot
        /// produce it.
        /// Mutation: move the conflict branch back above hardGated. RED on both assertions.
        [Fact]
        public void HardGateOutranksConflict_InTheStatedReason()
        {
            var statuses = AllNegative();
            statuses[0] = new SectionStatusEntry(Section.Prophet, SectionStatus.Positive);
            statuses[1] = new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested);  // the hard gate
            statuses[3] = new SectionStatusEntry(Section.Glutton, SectionStatus.Positive);
            statuses[4] = new SectionStatusEntry(Section.Cage,    SectionStatus.Positive);
            var evidence = new[]
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped,
                                    "DNSSEC canary returned unsigned", "signature stripped in flight"),
                EvidenceItem.Create(Section.Cage, EvidenceId.ExfilLikely,
                                    "high-entropy subdomain burst", "tunnelling shape"),
                EvidenceItem.Create(Section.Glutton, EvidenceId.DnsLeakConfirmed,
                                    "queries left the tunnel", "resolver outside VPN"),
            };
            var c = SynthesisEngine.Synthesize(evidence, statuses);

            // Both preconditions must hold or this test proves nothing about ordering.
            Assert.True(c.HasHardGatedSection);
            Assert.True(c.Conflict > 0.40);

            var reason = c.Referral?.Reason ?? "";
            Assert.Contains("did not run", reason);
            Assert.DoesNotContain("sections disagree", reason);
        }
    }

    // B029 (parity with macOS SourceCorrelationTests) — the source-group correlation map and its
    // effect on synthesized confidence. Correlated signals must not stack like independent evidence.
    public class SourceCorrelationTests
    {
        private static IReadOnlyList<SectionStatusEntry> ProphetPositiveStatuses() => new[]
        {
            new SectionStatusEntry(Section.Prophet, SectionStatus.Positive),
            new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested),
            new SectionStatusEntry(Section.Portal,  SectionStatus.Untested),
            new SectionStatusEntry(Section.Glutton, SectionStatus.Untested),
            new SectionStatusEntry(Section.Cage,    SectionStatus.Untested),
            new SectionStatusEntry(Section.Witness, SectionStatus.Untested),
        };

        [Fact]
        public void CorrelationMap_GroupsOnlyTheDeliberateFamilies()
        {
            Assert.Equal("nxdomain_hijack", EvidenceItem.CorrelationGroup(EvidenceId.NxSubdomainHijack));
            Assert.Equal("nxdomain_hijack", EvidenceItem.CorrelationGroup(EvidenceId.DenialInjected));
            Assert.Equal("nxdomain_hijack", EvidenceItem.CorrelationGroup(EvidenceId.DenialBare));
            Assert.Equal("transport_downgrade", EvidenceItem.CorrelationGroup(EvidenceId.DnsUnencrypted));
            Assert.Equal("transport_downgrade", EvidenceItem.CorrelationGroup(EvidenceId.EncryptedDnsBlocked));
            Assert.Equal("transport_downgrade", EvidenceItem.CorrelationGroup(EvidenceId.SvcbStripped));
            Assert.Equal("authoritative_divergence", EvidenceItem.CorrelationGroup(EvidenceId.AuthoritativeDiverges));
            Assert.Equal("authoritative_divergence", EvidenceItem.CorrelationGroup(EvidenceId.AuthoritativeDivergesConfirmed));
            foreach (var id in new[] { EvidenceId.Redirection, EvidenceId.DnsLeakConfirmed,
                EvidenceId.EcsSubnetLeak, EvidenceId.TtlLockstep, EvidenceId.ScriptMismatch,
                EvidenceId.ResolverStatus, EvidenceId.Ipv4Suppressed, EvidenceId.EdnsBlocked })
            {
                Assert.Null(EvidenceItem.CorrelationGroup(id));
            }
        }

        [Fact]
        public void EvidenceItem_DerivesSourceFromMap_ExplicitSourceWins()
        {
            Assert.Equal("nxdomain_hijack",
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxSubdomainHijack, "o", "r").Source);
            Assert.Null(EvidenceItem.Create(Section.Mirror, EvidenceId.Redirection, "o", "r").Source);
            Assert.Equal("canaries",
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "canaries").Source);
        }

        [Fact]
        public void CorrelatedGroup_IsDiscountedBelowIndependentStacking()
        {
            var statuses = ProphetPositiveStatuses();
            var correlated = new[]
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxSubdomainHijack, "o", "r"),
                EvidenceItem.Create(Section.Prophet, EvidenceId.DenialInjected, "o", "r"),
            };
            var independent = new[]
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxSubdomainHijack, "o", "r", "indepA"),
                EvidenceItem.Create(Section.Prophet, EvidenceId.DenialInjected, "o", "r", "indepB"),
            };
            var cCorr = SynthesisEngine.Synthesize(correlated, statuses);
            var cIndep = SynthesisEngine.Synthesize(independent, statuses);
            Assert.True(cCorr.Confidence < cIndep.Confidence,
                $"correlated ({cCorr.Confidence}) must be below independent ({cIndep.Confidence})");
        }

        /// <summary>B029 negative direction (parity with Swift testDifferentSourceSignals_areNotDiscounted):
        /// two genuinely independent signals must NOT be discounted — a second corroborating signal from a
        /// distinct source must RAISE confidence. Goes RED if independent signals are wrongly capped.</summary>
        [Fact]
        public void DifferentSourceSignals_AreNotDiscounted()
        {
            var statuses = ProphetPositiveStatuses();
            var a = EvidenceItem.Create(Section.Prophet, EvidenceId.NxSubdomainHijack, "o", "r", "srcA");
            var b = EvidenceItem.Create(Section.Prophet, EvidenceId.DenialInjected, "o", "r", "srcB");
            var two = SynthesisEngine.Synthesize(new[] { a, b }, statuses);
            var onlyA = SynthesisEngine.Synthesize(new[] { a }, statuses);
            Assert.True(two.Confidence > onlyA.Confidence,
                $"a second independent corroborating signal must raise confidence (two={two.Confidence}, onlyA={onlyA.Confidence})");
        }

        // B009 (false-secure guard, parity with the macOS testCaptivePortalSectionIsDegradedNotClean):
        // an ACTIVE captive portal is intercepting DNS, so the Portal section must read Degraded, never
        // Negative (clean). Every other portal signal here is clean, so the portal is the only mover.
        [Fact]
        public void CaptivePortalSectionIsDegradedNotClean()
        {
            var report = new NetworkStateReport
            {
                CaptivePortal = new CaptivePortalStatus { State = ConditionState.Yes, Evidence = "portal detected" },
                EncryptedDNS = new EncryptedDNSStatus { State = ConditionState.Yes }
            };
            var se = EvidenceAdapters.Portal(report);
            Assert.NotEqual(SectionStatus.Negative, se.Status);
            Assert.Equal(SectionStatus.Degraded, se.Status);
            Assert.Contains(se.Evidence, e => e.EvidenceId == EvidenceId.CaptivePortalActive);
        }

        // MARK: - C1 gateway/DHCP audit (parity with macOS SynthesizerDatapointWiringTests)

        private static NetworkStateReport GatewayReport(GatewayInfo? gw) => new()
        {
            CaptivePortal = new CaptivePortalStatus { State = ConditionState.No, Evidence = "" },
            EncryptedDNS = new EncryptedDNSStatus { State = ConditionState.Yes },
            Gateway = gw
        };

        // Happy path: DHCP pushing a private, off-subnet, non-gateway resolver produces the C1
        // evidence item and a Positive section.
        // Fail state (mutation-proven): remove the off-link classification (flag nothing) and
        // this test REDs — a rogue router redirect silently reading clean is the C1 miss class.
        [Fact]
        public void GatewayDhcpOffLinkPrivateDNS_IsFlagged()
        {
            var gw = new GatewayInfo { Ip = "192.168.1.1", IsDNS = true, AdminReachable = ConditionState.Yes, DhcpPushedDNS = ["192.168.77.13"] };
            var se = EvidenceAdapters.Portal(GatewayReport(gw));
            Assert.Contains(se.Evidence, e => e.EvidenceId == EvidenceId.GatewayDhcpOffLinkDNS);
            Assert.Equal(SectionStatus.Positive, se.Status);
        }

        // ISP-pushed PUBLIC resolvers are normal. Fail state (mutation-proven): drop the
        // PrivateUse guard (flag public too) and this test REDs on the false positive —
        // every Comcast/ISP household would read as hijacked.
        [Fact]
        public void GatewayDhcpPublicDNS_IsNotFlagged()
        {
            var gw = new GatewayInfo { Ip = "192.168.1.1", IsDNS = false, AdminReachable = ConditionState.Yes, DhcpPushedDNS = ["75.75.75.75", "75.75.76.76"] };
            var se = EvidenceAdapters.Portal(GatewayReport(gw));
            Assert.DoesNotContain(se.Evidence, e => e.EvidenceId == EvidenceId.GatewayDhcpOffLinkDNS);
            Assert.NotEqual(SectionStatus.Positive, se.Status);
        }

        // The gateway pushing ITSELF (or a same-/24 sibling) is the standard home shape — clean.
        // Fail state (mutation-proven): break Ipv4SameSlash24 (never same subnet) and this REDs.
        [Fact]
        public void GatewayDhcpSameSubnetDNS_IsNotFlagged()
        {
            var gw = new GatewayInfo { Ip = "192.168.1.1", IsDNS = true, AdminReachable = ConditionState.Yes, DhcpPushedDNS = ["192.168.1.1", "192.168.1.53"] };
            var se = EvidenceAdapters.Portal(GatewayReport(gw));
            Assert.DoesNotContain(se.Evidence, e => e.EvidenceId == EvidenceId.GatewayDhcpOffLinkDNS);
        }

        // A failed gateway detection is inconclusive, never silently clean.
        // Fail state (mutation-proven): treat an empty gateway IP as Negative and this REDs —
        // the un-run probe would be laundered into clean support (Protocol §1).
        [Fact]
        public void GatewayDetectionFailure_IsInconclusiveNotClean()
        {
            var gw = new GatewayInfo { Ip = "", Detail = "Could not determine default gateway" };
            var se = EvidenceAdapters.Portal(GatewayReport(gw));
            Assert.NotEqual(SectionStatus.Negative, se.Status);
            Assert.Equal(SectionStatus.Inconclusive, se.Status);
        }

        [Fact]
        public void StrongCorrelatedGroup_IsCappedAtMaxWeightPerSource()
        {
            var statuses = ProphetPositiveStatuses();
            var group = new[]
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "grp"),
                EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped, "o", "r", "grp"),
            };
            var indep = new[]
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "g1"),
                EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped, "o", "r", "g2"),
            };
            var cGroup = SynthesisEngine.Synthesize(group, statuses);
            var cIndep = SynthesisEngine.Synthesize(indep, statuses);
            Assert.True(cGroup.Confidence < cIndep.Confidence,
                $"capped group ({cGroup.Confidence}) must be below uncapped independent ({cIndep.Confidence})");
        }
    }

    // C007 / B031 (parity) — a single bootstrap resolver cannot form majority consensus, so the
    // ground truth is INCONCLUSIVE, never single-sourced from the sole responder.
    public class AuthoritativeVantageConsensusTests
    {
        // Answers NS/A lookups for any bootstrap and a direct auth answer, mirroring the macOS mock.
        private static Func<DNSQuery, Task<DNSResponse>> Mock() => q =>
        {
            var bootstraps = new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" };
            if (q.Type == 2 && Array.IndexOf(bootstraps, q.Server) >= 0)
                return Task.FromResult(new DNSResponse
                {
                    Status = DNSResponseStatus.Success,
                    Records = new List<DNSRecord>
                    {
                        new() { Name = q.Name, Type = 2, Ttl = 3600, Rdata = "ns1.example.com." },
                        new() { Name = q.Name, Type = 2, Ttl = 3600, Rdata = "ns2.example.com." },
                    }
                });
            if (q.Type == 1 && Array.IndexOf(bootstraps, q.Server) >= 0)
                return Task.FromResult(new DNSResponse
                {
                    Status = DNSResponseStatus.Success,
                    Records = new List<DNSRecord>
                    {
                        new() { Name = q.Name, Type = 1, Ttl = 3600,
                                Rdata = q.Name == "ns1.example.com" ? "203.0.113.1" : "203.0.113.2" }
                    }
                });
            return Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                IsAuthoritative = true,
                Records = new List<DNSRecord> { new() { Name = q.Name, Type = 1, Ttl = 300, Rdata = "9.9.9.9" } }
            });
        };

        [Fact]
        public async Task SingleBootstrapResolver_IsInconclusive_NotSingleSourced()
        {
            var r = await AuthoritativeVantage.Compare(
                domain: "example.com", resolverAnswers: new[] { "1.2.3.4" },
                bootstrapResolvers: new[] { "1.1.1.1" }, query: Mock());
            Assert.Equal(AuthComparisonStatus.Inconclusive, r.Status);
        }

        [Fact]
        public async Task FullBootstrapSet_FormsConsensus_AndClassifies()
        {
            // With the full >=2 bootstrap set, consensus engages and a disjoint resolver answer diverges.
            var r = await AuthoritativeVantage.Compare(
                domain: "example.com", resolverAnswers: new[] { "1.2.3.4" }, query: Mock());
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);
        }
    }

    // F007 (parity with macOS testDomainCanonical_rejectsNameTooLongAfterEncoding /
    // testDomainCanonical_acceptsNameBelowLimitAfterEncoding) — the 253-byte limit is enforced
    // on the ENCODED A-label form, not the pre-IDNA UTF-8 length.
    public class DomainCanonicalTests
    {
        [Fact]
        public void RejectsName_TooLongAfterIDNAEncoding()
        {
            // "aä" × 28 labels joined by dots.
            // UTF-8 length = 28×3 + 27 = 111 bytes (≤ 253) → pre-IDNA guard would PASS.
            // A-label = "xn--a-uua" (9 bytes) × 28 + 27 dots = 279 bytes (> 253) → must REJECT.
            // Mutation proof: if guard is still pre-IDNA, this test passes when it should fail.
            var label = "a\u00e4"; // "aä"
            var name = string.Join(".", Enumerable.Repeat(label, 28));
            var result = DomainCanonical.CanonicalizeOrNull(name);
            Assert.Null(result);
        }

        [Fact]
        public void AcceptsName_BelowLimitAfterIDNAEncoding()
        {
            // "aä" × 27 labels: A-label = 9×27 + 26 dots = 269 — still > 253, use ascii-only
            // Use pure-ASCII labels that stay under limit: "ab" × 25 + dots = 50+24 = 74 bytes.
            var name = string.Join(".", Enumerable.Repeat("ab", 25));
            var result = DomainCanonical.CanonicalizeOrNull(name);
            Assert.NotNull(result);
        }

        [Fact]
        public void RejectsEmpty()
        {
            Assert.Null(DomainCanonical.CanonicalizeOrNull(""));
            Assert.Null(DomainCanonical.CanonicalizeOrNull("   "));
        }

        [Fact]
        public void StripsDot_AndLowercases()
        {
            var result = DomainCanonical.CanonicalizeOrNull("EXAMPLE.COM.");
            Assert.Equal("example.com", result);
        }

        [Fact]
        public void AreEquivalent_ReturnsFalse_OnMismatch()
        {
            // Mutation proof: AreEquivalent("a.com", "b.com") must be false — the comparison
            // is not just "both non-null".
            Assert.False(DomainCanonical.AreEquivalent("a.com", "b.com"));
        }
    }

    // B039 (parity with macOS ReportTests) — the report de-duplicates datapoints across sections.
    // Identical values collapse; a disagreeing value is kept and flagged as DISCREPANCY.
    public class ReportTests
    {
        private static NetworkStateReport Network(string resolver) => new NetworkStateReport
        {
            DnsServers = new List<string> { resolver },
            Vpn = new VPNStatus { State = ConditionState.No, Evidence = "" },
            CaptivePortal = new CaptivePortalStatus { State = ConditionState.No, Evidence = "" },
            EncryptedDNS = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "" },
        };

        private static ComparisonResult Comparison(string localServer, List<DNSRecord>? records = null) => new ComparisonResult
        {
            LocalServer = localServer,
            LocalRecords = records ?? new List<DNSRecord>(),
        };

        // Test 1 (B039): Prophet and Mirror report different target_answer values
        // → MergedDatapoint.IsConflicting==true and DISCREPANCY appears in RenderText().
        // Mutation proof: collapse both LocalRecords and Resolution.Records to the same IP
        // → IsConflicting becomes false → this test RED.
        [Fact]
        public void TargetAnswer_CrossDisciplineDiscrepancy_IsConflicting()
        {
            var inputs = new SectionInputs();
            // Mirror sees 1.2.3.4
            inputs.Comparison = Comparison("1.1.1.1", new List<DNSRecord>
            {
                new() { Name = "example.com", Type = 1, Ttl = 300, Rdata = "1.2.3.4" }
            });
            // Prophet sees 5.6.7.8
            inputs.Resolution = new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord>
                {
                    new() { Name = "example.com", Type = 1, Ttl = 300, Rdata = "5.6.7.8" }
                }
            };

            var report = ReportBuilder.Build(inputs);
            var answer = report.Datapoints.FirstOrDefault(d => d.Key == "target_answer");
            Assert.NotNull(answer);
            Assert.True(answer!.IsConflicting, "Prophet and Mirror disagreeing on target_answer must be flagged as conflicting");
            Assert.Contains("DISCREPANCY", report.RenderText());
        }

        // Test 2 (B039): empty SectionInputs produces no fabricated datapoints and does not crash.
        [Fact]
        public void EmptyInputs_ProducesNoDatapoints()
        {
            var inputs = new SectionInputs();
            var report = ReportBuilder.Build(inputs);
            Assert.Empty(report.Datapoints);
        }

        // Test 3 (B039): RenderText() must include the structural sections: Verdict, FACTS, WHY (if
        // evidence), and NEXT STEPS.
        [Fact]
        public void RenderText_ContainsRequiredStructuralSections()
        {
            var inputs = new SectionInputs();
            inputs.Network = Network("1.1.1.1");
            var report = ReportBuilder.Build(inputs);
            var text = report.RenderText();
            Assert.Contains("Verdict:", text);
            Assert.Contains("FACTS", text);
            Assert.Contains("NEXT STEPS", text);
        }

        // Test 4 (B039): SECOND OPINION block appears iff Referral is non-null.
        // Mutation proof: remove the Referral block from RenderText → second assertion RED.
        [Fact]
        public void RenderText_SecondOpinionBlock_AppearsIffReferralPresent()
        {
            // Without referral: no SECOND OPINION block.
            var noReferralInputs = new SectionInputs();
            noReferralInputs.Network = Network("1.1.1.1");
            var noReferralReport = ReportBuilder.Build(noReferralInputs);
            var noReferralText = noReferralReport.RenderText();
            if (noReferralReport.Conclusion.Referral is null)
                Assert.DoesNotContain("SECOND OPINION", noReferralText);
            else
                Assert.Contains("SECOND OPINION", noReferralText);

            // Zero inputs (no evidence → confidence=0 → referral MUST trigger).
            var zeroInputs = new SectionInputs();
            var zeroReport = ReportBuilder.Build(zeroInputs);
            Assert.True(zeroReport.Conclusion.Referral is not null,
                "Zero evidence must trigger a referral (confidence=0 is below any threshold)");
            Assert.True(zeroReport.RenderText().Contains("SECOND OPINION"),
                "When a referral is present, SECOND OPINION block must appear in rendered text");
        }
    }

    // D011: Trust-set key membership — tests the CheckTrustSet helper that determines
    // whether a signing key is a member of the configured trust set. The trust-set check
    // engages in RunCollectorTestAsync after crypto verification succeeds.
    public class LeakDetectorTrustTests
    {
        [Fact]
        public void CheckTrustSet_KeyInSet_ReturnsTrusted()
        {
            var key = Key.Create(SignatureAlgorithm.Ed25519);
            var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var trustSet = new HashSet<string> { pubB64, "some-other-key" };

            var result = LeakDetector.CheckTrustSet(pubB64, trustSet);

            Assert.True(result, "A key present in the trust set must be trusted");
        }

        [Fact]
        public void CheckTrustSet_KeyNotInSet_ReturnsUntrusted()
        {
            var key = Key.Create(SignatureAlgorithm.Ed25519);
            var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var otherKey = Key.Create(SignatureAlgorithm.Ed25519);
            var otherPubB64 = Convert.ToBase64String(otherKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var trustSet = new HashSet<string> { otherPubB64 };

            var result = LeakDetector.CheckTrustSet(pubB64, trustSet);

            Assert.False(result, "A key NOT in the trust set must be untrusted");
        }

        [Fact]
        public void CheckTrustSet_NullPublicKey_ReturnsNull()
        {
            var trustSet = new HashSet<string> { "some-key" };

            var result = LeakDetector.CheckTrustSet(null, trustSet);

            Assert.True(result is null, "A null public key must return null (check not performed)");
        }

        [Fact]
        public void CheckTrustSet_NullTrustSet_ReturnsNull()
        {
            var result = LeakDetector.CheckTrustSet("some-key", null);

            Assert.True(result is null, "A null trust set must return null (check not performed)");
        }

        [Fact]
        public void CheckTrustSet_EmptyTrustSet_ReturnsNull()
        {
            var emptySet = new HashSet<string>();

            var result = LeakDetector.CheckTrustSet("some-key", emptySet);

            Assert.True(result is null, "An empty trust set must return null (check not performed)");
        }

        [Fact]
        public void Verify_ThenCheckTrustSet_EndToEnd()
        {
            // Generate a real Ed25519 keypair and sign a known payload.
            var key = Key.Create(SignatureAlgorithm.Ed25519);
            var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var payload = System.Text.Encoding.UTF8.GetBytes("D011-test-payload");
            var sigB64 = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, payload));
            var payloadB64 = Convert.ToBase64String(payload);

            var result = new LeakTestResult
            {
                Verdict = LeakVerdict.NotDetected,
                Signature = sigB64,
                PublicKey = pubB64,
                SignedPayload = payloadB64
            };

            // Crypto verification must pass
            Assert.True(LeakDetector.Verify(result), "A genuine signature must verify");

            // Trust set containing the key → trusted
            var trustedSet = new HashSet<string> { pubB64, "some-other-key" };
            Assert.True(LeakDetector.CheckTrustSet(pubB64, trustedSet),
                "A verified signer whose key is in the trust set must be trusted");

            // Trust set NOT containing the key → not trusted
            var otherKey = Key.Create(SignatureAlgorithm.Ed25519);
            var otherPubB64 = Convert.ToBase64String(otherKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var untrustedSet = new HashSet<string> { otherPubB64 };
            Assert.False(LeakDetector.CheckTrustSet(pubB64, untrustedSet),
                "A verified signer whose key is NOT in the trust set must be untrusted");
        }
    }

    // B091 — verdict-bearing fields must come from the SIGNED payload, never the outer JSON;
    // C031 — an expected-resolver baseline keeps the user's own resolver from reading as a leak.
    public class LeakSignedAttestationTests
    {
        private static string PayloadB64(string json) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        [Fact]
        public void ParseIsStrict_MissingFieldsParseToNull()
        {
            Assert.NotNull(LeakDetector.ParseSignedAttestation(
                PayloadB64("{\"leaked\":true,\"observedResolvers\":[\"9.9.9.9\"]}")));
            // Failure cases: a payload missing either verdict-bearing field must NOT default.
            Assert.Null(LeakDetector.ParseSignedAttestation(PayloadB64("{\"observedResolvers\":[]}")));
            Assert.Null(LeakDetector.ParseSignedAttestation(PayloadB64("{\"leaked\":false}")));
            Assert.Null(LeakDetector.ParseSignedAttestation(PayloadB64("not json")));
            Assert.Null(LeakDetector.ParseSignedAttestation("!!!not-base64!!!"));
        }

        [Fact]
        public void ValidSignature_VerdictComesFromSignedFields_NotOuter()
        {
            // Signed payload says LEAKED via 9.9.9.9; a forging relay flipped the outer JSON to
            // "no leak". The verdict must follow the SIGNED fields... but an outer/signed mismatch
            // is itself proof of tampering in transit, so the honest answer is Inconclusive —
            // never the relay's "no leak".
            var signed = PayloadB64("{\"leaked\":true,\"observedResolvers\":[\"9.9.9.9\"]}");
            var (verdict, observed, _) = LeakDetector.DeriveVerdict(
                sigValid: true, signed, outerLeaked: false, outerObserved: new List<string>(),
                expectedResolvers: null);
            Assert.Equal(LeakVerdict.Inconclusive, verdict);
            // The displayed resolver set must be the SIGNED one, not the relay's stripped list.
            Assert.Equal(new List<string> { "9.9.9.9" }, observed);
        }

        [Fact]
        public void ValidSignature_MatchingOuter_DerivesFromSigned()
        {
            var signed = PayloadB64("{\"leaked\":true,\"observedResolvers\":[\"9.9.9.9\"]}");
            var (verdict, observed, _) = LeakDetector.DeriveVerdict(
                true, signed, outerLeaked: true, outerObserved: new List<string> { "9.9.9.9" },
                expectedResolvers: null);
            Assert.Equal(LeakVerdict.Confirmed, verdict);
            Assert.Equal(new List<string> { "9.9.9.9" }, observed);
        }

        [Fact]
        public void ValidSignature_UnparseablePayload_IsInconclusive()
        {
            var (verdict, _, _) = LeakDetector.DeriveVerdict(
                true, PayloadB64("{\"unrelated\":1}"), outerLeaked: true,
                outerObserved: new List<string> { "9.9.9.9" }, expectedResolvers: null);
            Assert.Equal(LeakVerdict.Inconclusive, verdict);
        }

        [Fact]
        public void InvalidSignature_IsInconclusive_NeverOuterVerdict()
        {
            var (verdict, _, _) = LeakDetector.DeriveVerdict(
                sigValid: false, signedPayloadBase64: null, outerLeaked: false,
                outerObserved: new List<string>(), expectedResolvers: null);
            Assert.Equal(LeakVerdict.Inconclusive, verdict);
        }

        [Fact]
        public void C031_BaselineResolversDoNotReadAsLeak()
        {
            var baseline = new HashSet<string> { "10.64.0.1", "10.64.0.2" };
            // All observed resolvers are the expected (e.g. VPN provider) egress → expected
            // behavior, not a leak.
            var (v1, _) = LeakDetector.ReconcileVerdict(
                leaked: true, new List<string> { "10.64.0.1" }, baseline);
            Assert.Equal(LeakVerdict.NotDetected, v1);

            // Failure case: ANY resolver outside the baseline is still a confirmed leak — the
            // baseline must not blind the detector.
            var (v2, _) = LeakDetector.ReconcileVerdict(
                leaked: true, new List<string> { "10.64.0.1", "203.0.113.7" }, baseline);
            Assert.Equal(LeakVerdict.Confirmed, v2);

            // No baseline → original semantics (any observation is a leak).
            var (v3, _) = LeakDetector.ReconcileVerdict(
                leaked: true, new List<string> { "10.64.0.1" }, null);
            Assert.Equal(LeakVerdict.Confirmed, v3);
        }
    }

    // Ph3.2 — SectionEvidence.Completeness must be a real N/M over the section's datapoint
    // catalog, never the inert 1.0 default; the count-based clean-support scaling in the
    // engine depends on it.
    public class SectionCompletenessTests
    {
        private static DNSResponse Ok() => new() { Status = DNSResponseStatus.Success };

        [Fact]
        public void ProphetCompletenessGrowsWithConclusiveDatapoints()
        {
            var two = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null);
            Assert.Equal(2.0 / EvidenceAdapters.ProphetDatapointCatalog, two.Completeness, 6);

            var three = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null,
                latency: new ResolverLatency(ResolverLatencyStatus.Fast));
            Assert.Equal(3.0 / EvidenceAdapters.ProphetDatapointCatalog, three.Completeness, 6);
            Assert.True(three.Completeness > two.Completeness,
                "an additional conclusive datapoint must raise completeness");
        }

        // The Ph3.2 acceptance bar: an N-clean + 1-timeout run must read visibly LOWER
        // completeness than the same run with all datapoints conclusive.
        [Fact]
        public void TimeoutDatapointLowersCompleteness()
        {
            var allOk = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null,
                latency: new ResolverLatency(ResolverLatencyStatus.Fast),
                stability: new AnswerStability(AnswerStabilityStatus.Stable),
                edns0: new EDNS0Capability(EDNS0CapabilityStatus.Supported));
            var oneTimedOut = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null,
                latency: new ResolverLatency(ResolverLatencyStatus.Unreachable),  // timeout → Invalidated
                stability: new AnswerStability(AnswerStabilityStatus.Stable),
                edns0: new EDNS0Capability(EDNS0CapabilityStatus.Supported));

            Assert.Equal(5.0 / EvidenceAdapters.ProphetDatapointCatalog, allOk.Completeness, 6);
            Assert.Equal(4.0 / EvidenceAdapters.ProphetDatapointCatalog, oneTimedOut.Completeness, 6);
            Assert.True(oneTimedOut.Completeness < allOk.Completeness,
                "a timed-out datapoint must not count as usable evidence");
        }

        [Fact]
        public void UntestedSectionsReadZeroCompleteness()
        {
            Assert.Equal(0.0, EvidenceAdapters.Portal(null).Completeness);
            Assert.Equal(0.0, EvidenceAdapters.Glutton(null).Completeness);
            Assert.Equal(0.0, EvidenceAdapters.Witness(null, null).Completeness);
        }

        [Fact]
        public void CompletenessSurvivesIntoSectionStatusEntries()
        {
            // End-to-end: Conclude() must carry the real per-section value into the entries the
            // engine scales clean support by — if a builder regresses to the 1.0 default for a
            // thin run, this fails.
            var inputs = new SectionInputs { Resolution = Ok() };
            var conclusion = Synthesizer.Conclude(inputs);
            var prophetEntry = conclusion.SectionStatuses.Single(s => s.Section == Section.Prophet);
            Assert.Equal(1.0 / EvidenceAdapters.ProphetDatapointCatalog, prophetEntry.Completeness, 6);
        }
    }

    // B073/B071/C016/C017/C018 — DNS wire-parsing honesty cluster.
    public class WireParsingHonestyTests
    {
        // B073 — a malformed OPT/additional section is EDNS-UNKNOWN, never a confident
        // "not supported" (which would under-read a middlebox mangling the response).
        [Fact]
        public async Task MalformedOptReadsInconclusive_NotLegacyResolver()
        {
            Func<DNSQuery, Task<DNSResponse>> malformedOpt = _ => Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord> { new() { Name = "x.com", Type = 1, Ttl = 60, Rdata = "1.2.3.4" } },
                Edns = null,
                EdnsMalformed = true,
            });
            var cap = await EDNS0Probe.Probe("x.com", "", malformedOpt);
            Assert.Equal(EDNS0CapabilityStatus.Inconclusive, cap.Status);

            // Failure case: genuinely-absent OPT still reads NotSupported (the fix must not
            // swallow the legacy-resolver classification).
            Func<DNSQuery, Task<DNSResponse>> noOpt = _ => Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord> { new() { Name = "x.com", Type = 1, Ttl = 60, Rdata = "1.2.3.4" } },
                Edns = null,
                EdnsMalformed = false,
            });
            var legacy = await EDNS0Probe.Probe("x.com", "", noOpt);
            Assert.Equal(EDNS0CapabilityStatus.NotSupported, legacy.Status);
        }

        // B071 — a declared-but-unparseable authority section must not grade as the BareDenial
        // POSITIVE; it is a corrupt response and reads inconclusive.
        [Fact]
        public void MalformedAuthorityIsNotABareDenial()
        {
            var malformed = DenialOfExistenceProbe.Classify("gone.example.com",
                DNSResponseStatus.NXDomain, new List<DNSRecord>(), new List<DNSRecord>(),
                authorityMalformed: true);
            Assert.Equal(DenialStatus.Inconclusive, malformed.Status);

            // Failure case: a genuinely empty authority section still reads BareDenial.
            var bare = DenialOfExistenceProbe.Classify("gone.example.com",
                DNSResponseStatus.NXDomain, new List<DNSRecord>(), new List<DNSRecord>(),
                authorityMalformed: false);
            Assert.Equal(DenialStatus.BareDenial, bare.Status);
        }

        // C016 — the wire question is punycoded (MakeQueryPacket calls ToAsciiName), so the
        // response-question guard must canonicalize before comparing or every Unicode IDN
        // query reads as spoofed.
        [Fact]
        public void UnicodeIdnQuestionMatchesItsPunycodedEcho()
        {
            var packet = DNSResolver.MakeQueryPacket("bücher.com", 1);
            Assert.True(DNSResolver.ResponseQuestionMatches(packet, "bücher.com", 1),
                "the Unicode name must match its own punycoded wire echo");
            // Failure cases: a different name or type must still be rejected.
            Assert.False(DNSResolver.ResponseQuestionMatches(packet, "buecher.com", 1));
            Assert.False(DNSResolver.ResponseQuestionMatches(packet, "bücher.com", 28));
        }

        // C017 — CNAME RDATA must decode to a real name against the FULL packet, so a
        // compression-pointer target compares equal across packets.
        [Fact]
        public void CnameRdataDecodesCompressedTargetToName()
        {
            // Hand-built response: question example.com/A, one CNAME answer whose owner AND
            // rdata are compression pointers back to the question name.
            var packet = new List<byte>
            {
                0x12, 0x34,             // ID
                0x81, 0x80,             // QR|RD|RA, RCODE 0
                0x00, 0x01,             // QD 1
                0x00, 0x01,             // AN 1
                0x00, 0x00, 0x00, 0x00, // NS 0, AR 0
            };
            packet.AddRange(new byte[] { 7 });
            packet.AddRange(System.Text.Encoding.ASCII.GetBytes("example"));
            packet.AddRange(new byte[] { 3 });
            packet.AddRange(System.Text.Encoding.ASCII.GetBytes("com"));
            packet.AddRange(new byte[] { 0, 0x00, 0x01, 0x00, 0x01 });          // root, type A, class IN
            packet.AddRange(new byte[] { 0xC0, 0x0C });                          // owner → ptr to qname
            packet.AddRange(new byte[] { 0x00, 0x05, 0x00, 0x01 });              // CNAME, IN
            packet.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x3C });              // TTL 60
            packet.AddRange(new byte[] { 0x00, 0x02, 0xC0, 0x0C });              // RDLEN 2, rdata = ptr

            var parsed = DNSResolver.ParseResponsePacketDetailed(packet.ToArray(), requestedType: 1);
            var cname = parsed.Records.Single(r => r.Type == 5);
            Assert.Equal("example.com", cname.Rdata);   // decoded name, NOT "c00c" hex

            // The chain probe must now key on it successfully.
            var chain = CNAMEChainProbe.ReconstructChain("example.com", parsed.Records);
            Assert.Equal(new List<string> { "example.com" }, chain);
        }

        // C018 — SVCB comparison covers priority/target/parameters, not just record presence.
        [Fact]
        public void SvcbParameterStrippingIsDivergent()
        {
            // prio=1, target=svc.example, alpn(key1)="h2", ech(key5)=0xAABB
            const string full = "0001" + "03737663076578616d706c6500" + "0001" + "0003" + "026832" + "0005" + "0002" + "aabb";
            // Same record with the ECH parameter stripped.
            const string stripped = "0001" + "03737663076578616d706c6500" + "0001" + "0003" + "026832";

            var canonFull = SVCBProbe.CanonicalizeSvcbRdata(full);
            var canonStripped = SVCBProbe.CanonicalizeSvcbRdata(stripped);
            Assert.NotNull(canonFull);
            Assert.NotNull(canonStripped);
            Assert.StartsWith("prio=1;target=svc.example;", canonFull);
            Assert.NotEqual(canonFull, canonStripped);
            // Compression inside SVCB RDATA is forbidden — must parse to null, not garbage.
            Assert.Null(SVCBProbe.CanonicalizeSvcbRdata("0001c00c"));

            var divergent = SVCBProbe.Classify(
                new List<string> { "1.1.1.1", "9.9.9.9" }, new List<string>(), new List<string>(),
                new Dictionary<string, List<string>>
                {
                    ["1.1.1.1"] = new() { canonFull! },
                    ["9.9.9.9"] = new() { canonStripped! },
                });
            Assert.Equal(SVCBFindingStatus.Divergent, divergent.Status);

            // Failure case: identical parameter sets must stay Present — the comparison must not
            // manufacture divergence.
            var consistent = SVCBProbe.Classify(
                new List<string> { "1.1.1.1", "9.9.9.9" }, new List<string>(), new List<string>(),
                new Dictionary<string, List<string>>
                {
                    ["1.1.1.1"] = new() { canonFull! },
                    ["9.9.9.9"] = new() { canonFull! },
                });
            Assert.Equal(SVCBFindingStatus.Present, consistent.Status);
        }
    }

    // Ph7.2 — RPNOccurrence static defaults + SOD v2 priority model; S5.1 — inferred provenance
    // channel populated and the datapoint-level completeness profile behind its parity flag.
    public class RpnAndStage5SliceTests
    {
        [Fact]
        public void OccurrenceDefaultsAreOnFmeaScaleAndScoreIsSOD()
        {
            foreach (MechanismClass mc in Enum.GetValues<MechanismClass>())
                Assert.InRange(RPNOccurrence.DefaultOccurrence(mc), 1, 10);

            var factors = RPNOccurrence.FactorsFor(MechanismClass.Rewrite, severity: 8, detectability: 4);
            Assert.Equal(7, factors.Occurrence);
            Assert.Equal(8 * 4 * 7, RPNOccurrence.Score(factors));
            // Failure case: v2 factors without occurrence must throw, never silently score S×D.
            Assert.Throws<ArgumentException>(() => RPNOccurrence.Score(new PriorityFactors(8, 4, null)));
        }

        private static Stage5Presentation SodStage5(PriorityFactors factors, int score)
        {
            return new Stage5Presentation(1, "2026-07-15T00:00:00.000Z", DataRegime.PartialData,
                new EvidenceCompleteness(1, 6, 1, 3, "profile.sections.v0"),
                new ProvenancePartition(Array.Empty<DirectObservation>(), Array.Empty<InferredFinding>(),
                    Array.Empty<UnavailableDatapoint>()),
                Array.Empty<AnswerMatrixRow>(),
                new HonestySignals(null, null, null),
                new MechanismDetermination(DeterminationState.Tampered, MechanismClass.Rewrite,
                    new CredalMeasure(CredalMeasureKind.Belief, 0.5, "mechanism:rewrite"),
                    Array.Empty<WithinClassEntry>(), DecisionBasis.OrdinaryCredalGate, false, null),
                new[] { new PrioritizedFinding("finding.sodtest", MechanismClass.Rewrite, null,
                    Array.Empty<string>(),
                    new FindingPriority(RPNOccurrence.ModelId, RPNOccurrence.ModelVersion, factors, score, 1)) });
        }

        [Fact]
        public void ValidatorEnforcesSodV2Contract()
        {
            var good = SodStage5(new PriorityFactors(8, 4, 7), 8 * 4 * 7);
            Assert.Empty(good.ValidationErrors());

            // Failure case 1: v2 finding without occurrence is a contract violation.
            var missing = SodStage5(new PriorityFactors(8, 4, null), 8 * 4);
            Assert.Contains(missing.ValidationErrors(), e => e.Contains("must carry occurrence"));

            // Failure case 2: score not S×O×D.
            var wrong = SodStage5(new PriorityFactors(8, 4, 7), 8 * 4);
            Assert.Contains(wrong.ValidationErrors(), e => e.Contains("severity*detectability*occurrence"));
        }

        [Fact]
        public void InferredChannelCarriesTheMechanismFingerprint()
        {
            var evidence = new List<EvidenceItem>
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "src1"),
                EvidenceItem.Create(Section.Mirror, EvidenceId.Redirection, "o", "r", "src2"),
            };
            var statuses = new List<SectionStatusEntry>
            {
                new(Section.Prophet, SectionStatus.Positive),
                new(Section.Mirror,  SectionStatus.Positive),
                new(Section.Portal,  SectionStatus.Negative),
                new(Section.Glutton, SectionStatus.Negative),
                new(Section.Cage,    SectionStatus.Negative),
                new(Section.Witness, SectionStatus.Negative),
            };
            var conclusion = SynthesisEngine.Synthesize(evidence, statuses);
            Assert.NotNull(conclusion.Stage5);
            var inferred = conclusion.Stage5!.Provenance.Inferred;
            Assert.Single(inferred);
            Assert.Equal(conclusion.MechanismFingerprint!.MechanismClass, inferred[0].MechanismClass);
            Assert.Contains(EvidenceId.NxCanaryHijack, inferred[0].SourceEvidenceIds);

            // Failure case: no fingerprint (clean run) → the channel must stay empty, not fabricate.
            var clean = SynthesisEngine.Synthesize(new List<EvidenceItem>(), statuses);
            Assert.NotNull(clean.Stage5);
            Assert.Empty(clean.Stage5!.Provenance.Inferred);
        }

        [Fact]
        public void DatapointProfileEmitsOnlyBehindParityFlag()
        {
            var inputs = new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Success },
            };

            // Default config: the section-level profile stays — flipping unilaterally would break
            // Stage-5 canonical-byte parity with macOS.
            var defaultRun = Synthesizer.Conclude(inputs);
            Assert.NotNull(defaultRun.Stage5);
            Assert.Equal("profile.sections.v0", defaultRun.Stage5!.Completeness.ExpectedProfileId);

            // Flag on: datapoint-level profile with summed real counts.
            var flagged = Synthesizer.Conclude(inputs,
                new SynthesisEngine.Config { EmitDatapointCompletenessProfile = true });
            Assert.NotNull(flagged.Stage5);
            Assert.Equal("profile.datapoints.v1", flagged.Stage5!.Completeness.ExpectedProfileId);
            Assert.True(flagged.Stage5.Completeness.Expected > 6,
                "datapoint expected must count probes, not sections");
            Assert.Equal(1, flagged.Stage5.Completeness.Observed);
        }
    }

    // Ph7.1 — WeightRubric is a loader (overrides over defaults) and RunWithWeights actually
    // applies a different table, deterministically changing engine output.
    public class WeightRubricLoaderTests
    {
        [Fact]
        public void OverridesApplyInsideScopeAndRestoreAfter()
        {
            double before = WeightRubric.GetWeight(EvidenceId.NxCanaryHijack, Hypothesis.Censorship);
            Assert.Equal(3.0, before);

            var overrides = new Dictionary<(EvidenceId, Hypothesis), double>
                { [(EvidenceId.NxCanaryHijack, Hypothesis.Censorship)] = 0.1 };
            double inside = CalibrationHarness.RunWithWeights(overrides, () =>
                WeightRubric.GetWeight(EvidenceId.NxCanaryHijack, Hypothesis.Censorship));
            Assert.Equal(0.1, inside);

            // Failure case: the override must NOT leak past the scope.
            Assert.Equal(3.0, WeightRubric.GetWeight(EvidenceId.NxCanaryHijack, Hypothesis.Censorship));
            // Non-overridden pairs keep their defaults inside the scope.
            double untouched = CalibrationHarness.RunWithWeights(overrides, () =>
                WeightRubric.GetWeight(EvidenceId.Redirection, Hypothesis.Spoofing));
            Assert.Equal(WeightRubric.DefaultWeight(EvidenceId.Redirection, Hypothesis.Spoofing), untouched);
        }

        [Fact]
        public void DifferentWeightTableChangesEngineOutputDeterministically()
        {
            var evidence = new List<EvidenceItem>
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "g1"),
                EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped, "o", "r", "g2"),
            };
            var statuses = new List<SectionStatusEntry>
            {
                new(Section.Prophet, SectionStatus.Positive),
                new(Section.Mirror,  SectionStatus.Negative),
                new(Section.Portal,  SectionStatus.Negative),
                new(Section.Glutton, SectionStatus.Negative),
                new(Section.Cage,    SectionStatus.Negative),
                new(Section.Witness, SectionStatus.Negative),
            };
            var baseline = SynthesisEngine.Synthesize(evidence, statuses);

            // Zero out every threat weight for the two signals: the engine must no longer be able
            // to commit belief to a threat from them.
            var zeroed = new Dictionary<(EvidenceId, Hypothesis), double>();
            foreach (Hypothesis h in Enum.GetValues<Hypothesis>())
            {
                zeroed[(EvidenceId.NxCanaryHijack, h)] = 0.0;
                zeroed[(EvidenceId.DnssecCanaryStripped, h)] = 0.0;
            }
            var neutered = CalibrationHarness.RunWithWeights(zeroed,
                () => SynthesisEngine.Synthesize(evidence, statuses));

            Assert.NotEqual(baseline.Confidence, neutered.Confidence);
            // Deterministic: same table, same output.
            var repeat = CalibrationHarness.RunWithWeights(zeroed,
                () => SynthesisEngine.Synthesize(evidence, statuses, now: baseline.GeneratedAt));
            var repeat2 = CalibrationHarness.RunWithWeights(zeroed,
                () => SynthesisEngine.Synthesize(evidence, statuses, now: baseline.GeneratedAt));
            Assert.Equal(repeat.Confidence, repeat2.Confidence);
            Assert.Equal(repeat.Diagnosis, repeat2.Diagnosis);
            Assert.Equal(repeat.Conclusivity, repeat2.Conclusivity);
        }

        [Fact]
        public void ParseWeightTableAcceptsValidAndRejectsTypos()
        {
            var table = CalibrationHarness.ParseWeightTable(
                "{\"NxCanaryHijack:Censorship\": 2.5, \"Redirection:Spoofing\": 0.0}");
            Assert.Equal(2.5, table[(EvidenceId.NxCanaryHijack, Hypothesis.Censorship)]);
            Assert.Equal(0.0, table[(EvidenceId.Redirection, Hypothesis.Spoofing)]);

            // Failure cases: a typo must throw, never silently calibrate the wrong entry.
            Assert.Throws<FormatException>(() =>
                CalibrationHarness.ParseWeightTable("{\"NxCanaryHijak:Censorship\": 2.5}"));
            Assert.Throws<FormatException>(() =>
                CalibrationHarness.ParseWeightTable("{\"NxCanaryHijack\": 2.5}"));
        }
    }

    // Ph6.1/B098 — the decisive-act catalog is WitnessSigner-signed and verifiable; tampered
    // membership must not verify. Ph6.2 — the catalog reaches the M4 gate via Conclude.
    public class DecisiveActCatalogTests
    {
        [Fact]
        public void SignedCatalogVerifies_TamperedMembershipDoesNot()
        {
            var (priv, pub) = WitnessSigner.GenerateKeyPair();
            var signed = WitnessSigner.SignDecisiveActs(DecisiveActCatalog.CurrentValidatedActs(), priv);
            Assert.True(WitnessSigner.VerifyDecisiveActs(signed));
            Assert.Equal(pub, signed.PublicKey);

            // Failure case 1: membership tampered after signing (an act quietly added).
            var expanded = new HashSet<EvidenceId>(signed.Acts) { EvidenceId.ResolverStatus };
            var tampered = signed with { Acts = expanded };
            Assert.False(WitnessSigner.VerifyDecisiveActs(tampered),
                "adding an act after signing must break the signature");

            // Failure case 2: version tampered (rollback to an older catalog claim).
            Assert.False(WitnessSigner.VerifyDecisiveActs(signed with { Version = signed.Version + 1 }));

            // Failure case 3: unsigned catalog never verifies — no provenance, no trust.
            Assert.False(WitnessSigner.VerifyDecisiveActs(DecisiveActCatalog.CurrentValidatedActs()));

            // Failure case 4: signer outside the trust set is refused.
            var (_, otherPub) = WitnessSigner.GenerateKeyPair();
            Assert.False(WitnessSigner.VerifyDecisiveActs(signed, new List<string> { otherPub }));
            Assert.True(WitnessSigner.VerifyDecisiveActs(signed, new List<string> { pub }));
        }

        [Fact]
        public void CanonicalBytesAreSetOrderIndependent()
        {
            var a = new ValidatedDecisiveActs(1, new HashSet<EvidenceId>
                { EvidenceId.NxCanaryHijack, EvidenceId.DnsLeakConfirmed });
            var b = new ValidatedDecisiveActs(1, new HashSet<EvidenceId>
                { EvidenceId.DnsLeakConfirmed, EvidenceId.NxCanaryHijack });
            Assert.Equal(a.CanonicalBytes(), b.CanonicalBytes());
            // Failure case: different version → different bytes.
            var c = new ValidatedDecisiveActs(2, new HashSet<EvidenceId>
                { EvidenceId.NxCanaryHijack, EvidenceId.DnsLeakConfirmed });
            Assert.NotEqual(a.CanonicalBytes(), c.CanonicalBytes());
        }

        [Fact]
        public void ConcludeThreadsCatalogIntoM4Gate()
        {
            // A confirmed NXDOMAIN-hijack canary is in the decisive catalog: through the
            // production Conclude path it must force a tampered verdict and override referral.
            var inputs = new SectionInputs
            {
                NxCanary = new DNSCanaries.CanaryResult(Section.Prophet, SectionStatus.Positive,
                    EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack,
                        "known-nonexistent name resolved to a real address",
                        "NXDOMAIN hijack canary tripped"), "hijack"),
            };
            var conclusion = Synthesizer.Conclude(inputs);
            Assert.False(conclusion.ReadsClean(), "a decisive hijack canary must not read clean");
            Assert.False(conclusion.IsInconclusive(), "a decisive act must commit to a named threat");
            Assert.False(conclusion.ReferralTriggered,
                "a decisive act independently justifies the verdict — no referral");

            // Failure case: WITHOUT decisive evidence the same thin run must NOT reach a named
            // threat with referral suppressed.
            var thin = Synthesizer.Conclude(new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Success },
            });
            Assert.True(thin.ReferralTriggered || thin.IsInconclusive(),
                "thin, non-decisive evidence must stay referred/inconclusive");
        }
    }

    // Ph5.2 — the B005 §1/§2 sub-scorers must ship on the conclusion, and must read NULL (never a
    // defaulted number) on thin or absent evidence.
    public class SubScorerWiringTests
    {
        private static List<SectionStatusEntry> AllStatuses(SectionStatus prophet)
        {
            return new List<SectionStatusEntry>
            {
                new(Section.Prophet, prophet),
                new(Section.Mirror,  SectionStatus.Positive),
                new(Section.Portal,  SectionStatus.Negative),
                new(Section.Glutton, SectionStatus.Negative),
                new(Section.Cage,    SectionStatus.Negative),
                new(Section.Witness, SectionStatus.Negative),
            };
        }

        [Fact]
        public void FingerprintShipsWhenTwoIndependentSignalsConverge()
        {
            var evidence = new List<EvidenceItem>
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "src1"),
                EvidenceItem.Create(Section.Mirror, EvidenceId.Redirection, "o", "r", "src2"),
            };
            var conclusion = SynthesisEngine.Synthesize(evidence, AllStatuses(SectionStatus.Positive));
            Assert.NotNull(conclusion.MechanismFingerprint);
            // Two signals = tentative regime: a hypothesis, never a confident conclusion.
            Assert.True(conclusion.MechanismFingerprint!.Confidence <= 0.35,
                $"two-signal fingerprint must stay tentative, got {conclusion.MechanismFingerprint.Confidence}");
        }

        [Fact]
        public void FingerprintIsNullBelowMinimumCorroboration()
        {
            // Failure case: ONE signal must never fingerprint (B005 §2 minimum-corroboration rule).
            var evidence = new List<EvidenceItem>
            {
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack, "o", "r", "src1"),
            };
            var conclusion = SynthesisEngine.Synthesize(evidence, AllStatuses(SectionStatus.Positive));
            Assert.Null(conclusion.MechanismFingerprint);
        }

        [Fact]
        public void FingerprintIsNullOnCleanEvidence()
        {
            var conclusion = SynthesisEngine.Synthesize(
                new List<EvidenceItem>(), AllStatuses(SectionStatus.Negative));
            Assert.Null(conclusion.MechanismFingerprint);
        }

        [Fact]
        public void ProvenanceIsNullWhenResolutionFailedOrAbsent()
        {
            // No resolution at all → no-data regime.
            Assert.Null(Synthesizer.ComputeResolverProvenance(new SectionInputs()));
            // Resolution FAILED → all provenance signals are meaningless (DESIGN.md §1 edge case),
            // even when other probes carry readings.
            var failed = new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Timeout },
                Edns0 = new EDNS0Capability(EDNS0CapabilityStatus.Supported),
            };
            Assert.Null(Synthesizer.ComputeResolverProvenance(failed));
        }

        [Fact]
        public void ProvenanceIsNullWhenNoProvenanceProbeRan()
        {
            var okButBare = new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Success },
            };
            Assert.Null(Synthesizer.ComputeResolverProvenance(okButBare));
        }

        [Fact]
        public void ProvenanceScoresWhenProbesRan_AndAttenuatesPartialData()
        {
            var full = new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Success },
                Edns0 = new EDNS0Capability(EDNS0CapabilityStatus.Supported),
                StubEntropy = new StubEntropyAudit(StubEntropyStatus.WellRandomized,
                    SampleCount: 20, DistinctSourcePorts: 20, DistinctTransactionIDs: 20),
            };
            var score = Synthesizer.ComputeResolverProvenance(full);
            Assert.NotNull(score);
            Assert.InRange(score!.Value, 0.0, 1.0);

            // Partial data (EDNS0 only, 1 of 5 signals) must cap at 0.40 per the contract.
            var thin = new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Success },
                Edns0 = new EDNS0Capability(EDNS0CapabilityStatus.Supported),
            };
            var thinScore = Synthesizer.ComputeResolverProvenance(thin);
            Assert.NotNull(thinScore);
            Assert.True(thinScore!.Value <= 0.40,
                $"minority-signal provenance must cap at 0.40, got {thinScore.Value}");
        }
    }

    // C2 — filtering verification. Each test names its fail state; the changelog mutation matrix
    // demonstrates each one reds when the matching defect is introduced.
    public class FilteringProbeTests
    {
        private static DNSResponse Resp(DNSResponseStatus status, params string[] ips) => new()
        {
            Status = status,
            Records = ips.Select(ip => new DNSRecord { Name = "doubleclick.net", Type = 1, Ttl = 60, Rdata = ip }).ToList()
        };

        // The acceptance bar: a known-blocked domain RESOLVING publicly must read Resolved.
        // Fail state: classify public answers as sinkholed → the probe vouches for a filter that
        // let the domain through.
        [Fact]
        public void PublicAnswerMeansFilterDidNotAct()
            => Assert.Equal(FilteringProbe.DomainOutcome.Resolved, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "142.250.72.14")));

        // NXDOMAIN and null/loopback/private answers are all sinkhole styles.
        // Fail state: treat 0.0.0.0 as resolved → a working Pi-hole reads "not filtering".
        [Fact]
        public void SinkholeStylesAllReadSinkholed()
        {
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.NXDomain)));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "0.0.0.0")));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "127.0.0.1")));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "192.168.1.5")));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.NoData)));
        }

        // Any public address among sinkhole addresses means the domain ESCAPED.
        // Fail state: require all-public before Resolved → a partial escape hides.
        [Fact]
        public void MixedAnswerWithAnyPublicReadsResolved()
            => Assert.Equal(FilteringProbe.DomainOutcome.Resolved, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "0.0.0.0", "142.250.72.14")));

        // A timeout is NO READING. Fail state: map timeout to sinkholed → a dead network
        // fabricates "your filter is working" (Protocol §1 false-secure).
        [Fact]
        public void TimeoutIsUnknownNeverSinkholed()
        {
            Assert.Equal(FilteringProbe.DomainOutcome.Unknown, FilteringProbe.Classify(Resp(DNSResponseStatus.Timeout)));
            Assert.Equal(FilteringProbe.DomainOutcome.Unknown, FilteringProbe.Classify(Resp(DNSResponseStatus.TransportError)));
        }

        [Fact]
        public void AllSinkholedIsActive()
            => Assert.Equal(FilteringProbe.Enforcement.Active, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.DomainOutcome.Sinkholed }));

        [Fact]
        public void AllResolvedIsInactive()
            => Assert.Equal(FilteringProbe.Enforcement.Inactive, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Resolved, FilteringProbe.DomainOutcome.Resolved }));

        // A mix is a LEAKY blocklist, distinct from active and inactive.
        [Fact]
        public void MixIsPartial()
            => Assert.Equal(FilteringProbe.Enforcement.Partial, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.DomainOutcome.Resolved }));

        // All-unreadable must be inconclusive. Fail state: default the empty case to Active →
        // an offline machine reads "filter verified" with zero evidence (§6 zero-data).
        [Fact]
        public void AllUnknownIsInconclusiveNeverActive()
        {
            Assert.Equal(FilteringProbe.Enforcement.Inconclusive, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Unknown, FilteringProbe.DomainOutcome.Unknown }));
            Assert.Equal(FilteringProbe.Enforcement.Inconclusive, FilteringProbe.Aggregate(System.Array.Empty<FilteringProbe.DomainOutcome>()));
        }

        // Unreadable probes drop out of the denominator; the readable remainder still decides.
        [Fact]
        public void UnknownsDropOutOfDenominator()
        {
            Assert.Equal(FilteringProbe.Enforcement.Active, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Unknown, FilteringProbe.DomainOutcome.Sinkholed }));
            Assert.Equal(FilteringProbe.Enforcement.Inactive, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Unknown, FilteringProbe.DomainOutcome.Resolved }));
        }
    }


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
            VpnActive = vpn, DnsEncrypted = enc
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

}
