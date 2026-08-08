using System;
using System.Net;
using System.Net.Sockets;

namespace Syxx.Kit
{
    public enum IPAddressClass
    {
        PublicRoutable,
        PrivateUse,
        Loopback,
        LinkLocal,
        CarrierGradeNAT,
        Documentation,
        Multicast,
        Unspecified,
        Reserved,
        /// B303: RFC 2544 benchmarking range (198.18.0.0/15) — distinct from PrivateUse.
        Benchmarking,
        /// B304: Teredo tunnel addresses (2001::/32) — tunneled traffic has unpredictable routing.
        Teredo,
        /// B304: 6to4 tunnel addresses (2002::/16) — tunneled traffic has unpredictable routing.
        SixToFour,
        Invalid
    }

    public static class IPClassifier
    {
        public static IPAddressClass Classify(string value)
        {
            if (!IPAddress.TryParse(value, out var address))
            {
                return IPAddressClass.Invalid;
            }

            var bytes = address.GetAddressBytes();
            return address.AddressFamily == AddressFamily.InterNetwork
                ? ClassifyIPv4(bytes)
                : ClassifyIPv6(bytes);
        }

        public static bool IsBlockingAnswer(string value)
        {
            return Classify(value) switch
            {
                IPAddressClass.PrivateUse => true,
                IPAddressClass.Loopback => true,
                IPAddressClass.LinkLocal => true,
                IPAddressClass.CarrierGradeNAT => true,
                IPAddressClass.Documentation => true,
                IPAddressClass.Multicast => true,
                IPAddressClass.Unspecified => true,
                IPAddressClass.Reserved => true,
                IPAddressClass.Benchmarking => true, // B303: 198.18.0.0/15 is a blocking answer
                IPAddressClass.Invalid => true,
                _ => false
            };
        }

        private static IPAddressClass ClassifyIPv4(byte[] ip)
        {
            if (ip[0] == 0) return IPAddressClass.Unspecified;
            if (ip[0] == 10) return IPAddressClass.PrivateUse;
            if (ip[0] == 127) return IPAddressClass.Loopback;
            if (ip[0] == 169 && ip[1] == 254) return IPAddressClass.LinkLocal;
            if (ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31) return IPAddressClass.PrivateUse;
            if (ip[0] == 192 && ip[1] == 168) return IPAddressClass.PrivateUse;
            if (ip[0] == 100 && ip[1] >= 64 && ip[1] <= 127) return IPAddressClass.CarrierGradeNAT;
            if (ip[0] == 192 && ip[1] == 0 && ip[2] == 0) return IPAddressClass.Reserved;
            if (ip[0] == 192 && ip[1] == 0 && ip[2] == 2) return IPAddressClass.Documentation;
            if (ip[0] == 192 && ip[1] == 88 && ip[2] == 99) return IPAddressClass.Documentation;
            // B303: RFC 2544 benchmarking range — semantically distinct from PrivateUse.
            if (ip[0] == 198 && (ip[1] == 18 || ip[1] == 19)) return IPAddressClass.Benchmarking;
            if (ip[0] == 198 && ip[1] == 51 && ip[2] == 100) return IPAddressClass.Documentation;
            if (ip[0] == 203 && ip[1] == 0 && ip[2] == 113) return IPAddressClass.Documentation;
            if (ip[0] >= 224 && ip[0] <= 239) return IPAddressClass.Multicast;
            if (ip[0] >= 240) return IPAddressClass.Reserved;
            return IPAddressClass.PublicRoutable;
        }

        private static IPAddressClass ClassifyIPv6(byte[] ip)
        {
            if (Array.TrueForAll(ip, b => b == 0)) return IPAddressClass.Unspecified;
            bool firstFifteenZero = true;
            for (int i = 0; i < 15; i++)
            {
                if (ip[i] != 0)
                {
                    firstFifteenZero = false;
                    break;
                }
            }
            if (firstFifteenZero && ip[15] == 1) return IPAddressClass.Loopback;
            // C013: IPv4-mapped IPv6 (::ffff:0:0/96, RFC 4291 §2.5.5.2). The embedded IPv4
            // address keeps its own class — ::ffff:127.0.0.1 IS loopback, ::ffff:10.0.0.1 IS
            // private. Without this branch a mapped sinkhole/blocking answer fell through to
            // PublicRoutable and was never flagged.
            bool mappedPrefix = true;
            for (int i = 0; i < 10; i++)
            {
                if (ip[i] != 0) { mappedPrefix = false; break; }
            }
            if (mappedPrefix && ip[10] == 0xff && ip[11] == 0xff)
            {
                return ClassifyIPv4(new[] { ip[12], ip[13], ip[14], ip[15] });
            }
            if (ip[0] == 0xfe && (ip[1] & 0xc0) == 0x80) return IPAddressClass.LinkLocal;
            // Deprecated site-local fec0::/10 (RFC 3879). Windows still seeds the well-known
            // placeholder resolvers fec0:0:0:ffff::1/2/3 into adapter DNS lists; treat them as
            // Reserved so they are filtered out instead of masquerading as public resolvers.
            if (ip[0] == 0xfe && (ip[1] & 0xc0) == 0xc0) return IPAddressClass.Reserved;
            if ((ip[0] & 0xfe) == 0xfc) return IPAddressClass.PrivateUse;
            if (ip[0] == 0xff) return IPAddressClass.Multicast;
            if (ip[0] == 0x20 && ip[1] == 0x01 && ip[2] == 0x0d && ip[3] == 0xb8) return IPAddressClass.Documentation;
            // B304: Teredo (2001::/32) and 6to4 (2002::/16) tunnel addresses — tunneled traffic
            // follows unpredictable routing paths, causing false leak signals when classified as
            // PublicRoutable. These are non-blocking but flagged for diagnostic context.
            if (ip[0] == 0x20 && ip[1] == 0x01 && ip[2] == 0x00 && ip[3] == 0x00) return IPAddressClass.Teredo;
            if (ip[0] == 0x20 && ip[1] == 0x02) return IPAddressClass.SixToFour;
            return IPAddressClass.PublicRoutable;
        }
    }
}
