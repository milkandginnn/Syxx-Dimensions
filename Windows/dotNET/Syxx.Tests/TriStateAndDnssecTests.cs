using System;
using System.Collections.Generic;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// with the macOS SyxxKit twin and the Python server.
    public class TriStateResultTests
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

        // -----------------------------------------------------------------------------------------
        // D1.2 / C059 + C125 — zone binding and DNSKEY field validation.
        // Twin of macOS DNSSECVerifier.swift:100-157. Without these the signature-stripping canary
        // is defeated by construction: DNSKEY and RRSIG records are public data, so an on-path
        // attacker can strip a zone's real signatures and append a genuine, in-window pair harvested
        // from any other signed zone.
        // -----------------------------------------------------------------------------------------

        /// DNSKEY rdata: flags(2) protocol(1) algorithm(1) publicKey(var). Defaults are a valid
        /// zone-signing key (flags 0x0101 → ZONE bit set, protocol 3, algorithm 13).
        private static DNSRecord DnskeyFor(string owner, ushort flags = 0x0101,
                                           byte protocolField = 3, byte algorithm = 13) =>
            new DNSRecord
            {
                Name = owner, Type = 48, Ttl = 3600,
                Rdata = Convert.ToHexString(new byte[]
                {
                    (byte)(flags >> 8), (byte)flags, protocolField, algorithm, 0xab, 0xcd, 0xef
                }).ToLower()
            };

        /// RRSIG rdata with an explicit signer name encoded as DNS labels after the key tag.
        private static string RrsigHexSigned(uint inception, uint expiration, string signer, ushort typeCovered = 48)
        {
            var b = new List<byte> { (byte)(typeCovered >> 8), (byte)typeCovered, 0x08, 0x02, 0x00, 0x00, 0x01, 0x2C };
            b.AddRange(new byte[] { (byte)(expiration >> 24), (byte)(expiration >> 16), (byte)(expiration >> 8), (byte)expiration });
            b.AddRange(new byte[] { (byte)(inception >> 24), (byte)(inception >> 16), (byte)(inception >> 8), (byte)inception });
            b.AddRange(new byte[] { 0x12, 0x34 });                        // key tag
            foreach (var label in signer.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                b.Add((byte)label.Length);
                b.AddRange(System.Text.Encoding.UTF8.GetBytes(label));
            }
            b.Add(0x00);                                                  // root terminator
            return Convert.ToHexString(b.ToArray()).ToLower();
        }

        private static DNSRecord RrsigFor(string owner, string signer, uint inception, uint expiration) =>
            new DNSRecord
            {
                Name = owner, Type = 46, Ttl = 3600,
                Rdata = RrsigHexSigned(inception, expiration, signer)
            };

        [Fact]
        public void ClassifyRejectsADnskeyHarvestedFromAnUnrelatedZone()
        {
            // The attack C059 exists to stop. Every record here is genuine and in-window — it just
            // belongs to a different zone. Before zone binding this reported
            // RecordsPresentUnverified, i.e. "signature window valid", for a zone whose signatures
            // had been removed on the path.
            var records = new List<DNSRecord>
            {
                DnskeyFor("evil.com"),
                RrsigFor("evil.com", "evil.com", 1_000_000, 2_000_000)
            };
            Assert.Equal(DNSSECStatus.Unsigned,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "example.com"));

            // Not vacuous: the very same records ARE a valid observation of evil.com itself.
            Assert.Equal(DNSSECStatus.RecordsPresentUnverified,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "evil.com"));
        }

        [Fact]
        public void ClassifyRejectsAnRrsigSignedByAnUnrelatedZone()
        {
            // Half of the attack: the DNSKEY owner is right, but the covering RRSIG names an
            // unrelated signer. DNSKEY present with no admissible covering RRSIG is INCONCLUSIVE —
            // never "unsigned" (we did see a key) and never a valid signature window.
            var records = new List<DNSRecord>
            {
                DnskeyFor("example.com"),
                RrsigFor("example.com", "evil.com", 1_000_000, 2_000_000)
            };
            Assert.Equal(DNSSECStatus.Inconclusive,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "example.com"));
        }

        [Fact]
        public void ClassifyAcceptsAnAncestorZoneAsSigner()
        {
            // The guard must not over-reject: a parent zone legitimately signs a child name.
            var records = new List<DNSRecord>
            {
                DnskeyFor("www.example.com"),
                RrsigFor("www.example.com", "example.com", 1_000_000, 2_000_000)
            };
            Assert.Equal(DNSSECStatus.RecordsPresentUnverified,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "www.example.com"));
        }

        [Fact]
        public void ClassifyOwnerComparisonIgnoresCaseAndTrailingDot()
        {
            // DNS names are case-insensitive and the wire form carries the root dot. Binding must
            // not reject an honest answer over presentation.
            var records = new List<DNSRecord>
            {
                DnskeyFor("Example.COM."),
                RrsigFor("Example.COM.", "example.com", 1_000_000, 2_000_000)
            };
            Assert.Equal(DNSSECStatus.RecordsPresentUnverified,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "example.com"));
        }

        [Theory]
        // RFC 4034 2.1.2 — Protocol MUST be 3.
        [InlineData((ushort)0x0101, (byte)2, (byte)13)]
        // RFC 4034 2.1.1 — ZONE flag (0x0100) must be set; 0x0001 is a key with the bit clear.
        [InlineData((ushort)0x0001, (byte)3, (byte)13)]
        // RFC 4034 2.1.3 — algorithm 0 is reserved and cannot support a conclusion.
        [InlineData((ushort)0x0101, (byte)3, (byte)0)]
        public void ClassifyRejectsStructurallyInvalidDnskeys(ushort flags, byte protocolField, byte algorithm)
        {
            var records = new List<DNSRecord>
            {
                DnskeyFor("example.com", flags, protocolField, algorithm),
                RrsigFor("example.com", "example.com", 1_000_000, 2_000_000)
            };
            Assert.Equal(DNSSECStatus.Unsigned,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "example.com"));
        }

        [Fact]
        public void ClassifyAcceptsAStructurallyValidDnskey()
        {
            // Control for the theory above: the same shape with all three fields correct passes.
            var records = new List<DNSRecord>
            {
                DnskeyFor("example.com"),
                RrsigFor("example.com", "example.com", 1_000_000, 2_000_000)
            };
            Assert.Equal(DNSSECStatus.RecordsPresentUnverified,
                DNSSECVerifier.Classify(records, AtEpoch(1_500_000), "example.com"));
        }

        [Fact]
        public void ParseSignerNameReadsLabelsAndRejectsShortRdata()
        {
            Assert.Equal("example.com",
                DNSSECVerifier.ParseSignerName(RrsigHexSigned(1_000_000, 2_000_000, "example.com")));
            // A bare root label parses as ".", which IsSignerValid then refuses to accept for a
            // specific zone — the root cannot vouch for example.com here.
            Assert.Equal(".", DNSSECVerifier.ParseSignerName(RrsigHexSigned(1_000_000, 2_000_000, "")));
            Assert.Null(DNSSECVerifier.ParseSignerName("0001"));
            Assert.Null(DNSSECVerifier.ParseSignerName("zznothex"));
        }

        [Fact]
        public void IsSignerValidAcceptsSelfAndAncestorsOnly()
        {
            Assert.True(DNSSECVerifier.IsSignerValid("example.com", "example.com"));
            Assert.True(DNSSECVerifier.IsSignerValid("www.example.com", "example.com"));
            Assert.True(DNSSECVerifier.IsSignerValid("a.b.example.com", "example.com"));
            Assert.False(DNSSECVerifier.IsSignerValid("example.com", "evil.com"));
            Assert.False(DNSSECVerifier.IsSignerValid("example.com", "www.example.com"));  // child, not ancestor
            Assert.False(DNSSECVerifier.IsSignerValid("example.com", "."));                // bare root
            Assert.False(DNSSECVerifier.IsSignerValid("notexample.com", "example.com"));   // suffix, not a label boundary
        }
    }

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
}
