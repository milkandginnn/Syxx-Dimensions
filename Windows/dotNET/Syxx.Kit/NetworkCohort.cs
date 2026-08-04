using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Syxx.Kit;

public enum NetworkCohortResultStatus { SameNetwork, DifferentNetwork, Untested }

public sealed record NetworkCohortResult(
    NetworkCohortResultStatus Status,
    string? Provider = null,
    string Detail = "")
{
    public static readonly NetworkCohortResult Untested = new(NetworkCohortResultStatus.Untested, Detail: "not run");
}

public static class NetworkCohort
{
    private static readonly (uint baseAddr, int prefix, string name)[] Ranges = BuildRanges([
        // Cloudflare — full official ips-v4 list (dedicated anycast edge).
        ("173.245.48.0", 20, "Cloudflare"), ("103.21.244.0", 22, "Cloudflare"),
        ("103.22.200.0", 22, "Cloudflare"), ("103.31.4.0", 22, "Cloudflare"),
        ("141.101.64.0", 18, "Cloudflare"), ("108.162.192.0", 18, "Cloudflare"),
        ("190.93.240.0", 20, "Cloudflare"), ("188.114.96.0", 20, "Cloudflare"),
        ("197.234.240.0", 22, "Cloudflare"), ("198.41.128.0", 17, "Cloudflare"),
        ("162.158.0.0", 15, "Cloudflare"), ("104.16.0.0", 13, "Cloudflare"),
        ("104.24.0.0", 14, "Cloudflare"), ("172.64.0.0", 13, "Cloudflare"),
        ("131.0.72.0", 22, "Cloudflare"),
        // Fastly — published ranges.
        ("23.235.32.0", 20, "Fastly"), ("43.249.72.0", 22, "Fastly"),
        ("104.156.80.0", 20, "Fastly"), ("140.248.64.0", 18, "Fastly"),
        ("140.248.128.0", 17, "Fastly"), ("146.75.0.0", 16, "Fastly"),
        ("151.101.0.0", 16, "Fastly"), ("157.52.64.0", 18, "Fastly"),
        ("167.82.0.0", 17, "Fastly"), ("172.111.64.0", 18, "Fastly"),
        ("185.31.16.0", 22, "Fastly"), ("199.27.72.0", 21, "Fastly"),
        ("199.232.0.0", 16, "Fastly"),
        // Akamai — well-known dedicated edge blocks.
        ("104.64.0.0", 10, "Akamai"), ("23.192.0.0", 11, "Akamai"), ("23.32.0.0", 11, "Akamai"),
        ("23.0.0.0", 12, "Akamai"), ("2.16.0.0", 13, "Akamai"), ("184.24.0.0", 13, "Akamai"),
        ("184.50.0.0", 15, "Akamai"), ("96.16.0.0", 15, "Akamai"), ("95.100.0.0", 15, "Akamai"),
        ("72.246.0.0", 15, "Akamai"), ("88.221.0.0", 16, "Akamai"),
        // Google — serving/edge blocks (not the broad GCP 34/8 · 35/8 compute space).
        ("142.250.0.0", 15, "Google"), ("172.217.0.0", 16, "Google"), ("172.253.0.0", 16, "Google"),
        ("173.194.0.0", 16, "Google"), ("74.125.0.0", 16, "Google"), ("108.177.0.0", 17, "Google"),
        ("209.85.128.0", 17, "Google"), ("216.58.192.0", 19, "Google"), ("216.239.32.0", 19, "Google"),
        ("64.233.160.0", 19, "Google"), ("66.249.64.0", 19, "Google"), ("72.14.192.0", 18, "Google"),
        // AWS CloudFront — CLOUDFRONT-designated edge ranges from ip-ranges.json.
        ("13.32.0.0", 15, "CloudFront"), ("13.224.0.0", 14, "CloudFront"), ("13.249.0.0", 16, "CloudFront"),
        ("18.64.0.0", 14, "CloudFront"), ("18.160.0.0", 15, "CloudFront"), ("52.84.0.0", 15, "CloudFront"),
        ("54.182.0.0", 16, "CloudFront"), ("54.192.0.0", 16, "CloudFront"), ("54.230.0.0", 16, "CloudFront"),
        ("65.8.0.0", 16, "CloudFront"), ("65.9.0.0", 16, "CloudFront"), ("99.84.0.0", 16, "CloudFront"),
        ("108.138.0.0", 15, "CloudFront"), ("108.156.0.0", 14, "CloudFront"), ("130.176.0.0", 16, "CloudFront"),
        ("143.204.0.0", 16, "CloudFront"), ("205.251.192.0", 19, "CloudFront"),
        // Imperva / Incapsula — published edge.
        ("45.60.0.0", 16, "Imperva"), ("107.154.0.0", 16, "Imperva"), ("192.230.64.0", 18, "Imperva"),
        ("198.143.32.0", 19, "Imperva"), ("199.83.128.0", 21, "Imperva"),
        // StackPath / Highwinds (MaxCDN).
        ("151.139.0.0", 16, "StackPath"), ("205.185.192.0", 18, "StackPath"),
    ]);

    public static string? ProviderForIPv4(string ip)
    {
        var addr = IPv4ToUInt32(ip);
        if (addr is null) return null;
        foreach (var (baseAddr, prefix, name) in Ranges)
        {
            if (prefix <= 0) continue;
            uint mask = prefix == 32 ? uint.MaxValue : ~(uint.MaxValue >> prefix);
            if ((addr.Value & mask) == (baseAddr & mask))
                return name;
        }
        return null;
    }

    public static string? SharedProvider(List<string> a, List<string> b)
    {
        var all = a.Concat(b).ToList();
        if (all.Count == 0) return null;
        string? found = null;
        foreach (var ip in all)
        {
            var p = ProviderForIPv4(ip);
            if (p is null) return null;
            if (found is null) found = p;
            else if (found != p) return null;
        }
        return found;
    }

    private static uint? IPv4ToUInt32(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return null;
        if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
        var bytes = addr.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        return BitConverter.ToUInt32(bytes);
    }

    private static (uint, int, string)[] BuildRanges((string cidr, int prefix, string name)[] raw)
    {
        var list = new List<(uint, int, string)>();
        foreach (var (cidr, prefix, name) in raw)
        {
            var addr = IPv4ToUInt32(cidr);
            if (addr is null) continue;
            list.Add((addr.Value, prefix, name));
        }
        return [.. list];
    }
}
