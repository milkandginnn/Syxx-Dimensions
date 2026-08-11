using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
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
            Assert.Equal(DNSResponseStatus.ServFail, DNSResolver.ParseResponsePacketDetailed(PacketWithRCode(2)).Status);
            Assert.Equal(DNSResponseStatus.NXDomain, DNSResolver.ParseResponsePacketDetailed(PacketWithRCode(3)).Status);
            Assert.Equal(DNSResponseStatus.Refused, DNSResolver.ParseResponsePacketDetailed(PacketWithRCode(5)).Status);
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
                0x12, 0x34, 0x81, 0x80,  // XID, Flags (QR+AA+RD+RA)
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
}
