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
