using System;
using System.Collections.Generic;
using System.Text;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
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
}
