using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum VersionBindResultStatus { Disclosed, Withheld, Untested }

public sealed record VersionBindResult(
    VersionBindResultStatus Status,
    string? Banner = null,
    string Server = "",
    bool ForwarderOnPublicPath = false,
    string Detail = "")
{
    public static readonly VersionBindResult Untested = new(VersionBindResultStatus.Untested, Detail: "not run");
}

public static class VersionBindProbe
{
    public const ushort TypeTXT = 16;
    public const ushort ClassCHAOS = 3;

    private static readonly string[] ForwarderSignatures = ["dnsmasq", "pi-hole", "pihole", "openwrt", "ubios", "unifi"];

    private static readonly HashSet<string> KnownPublicResolvers =
    [
        "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "149.112.112.112",
        "208.67.222.222", "208.67.220.220", "94.140.14.14", "94.140.15.15", "76.76.2.0", "76.76.10.0"
    ];

    public static bool IsKnownPublicResolver(string ip) => KnownPublicResolvers.Contains(ip);

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<VersionBindResult> Probe(string server,
        bool? expectedPublicResolver = null,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        var resp = await query(new DNSQuery(name: "version.bind", type: TypeTXT, server: server, qclass: ClassCHAOS));
        bool isPublic = expectedPublicResolver ?? IsKnownPublicResolver(server);
        return Classify(resp, server, isPublic);
    }

    public static VersionBindResult Classify(DNSResponse response, string server, bool expectedPublicResolver)
    {
        if (DidFail(response))
        {
            return new VersionBindResult(VersionBindResultStatus.Untested, Server: server,
                Detail: $"the CHAOS version.bind query got no usable response ({response.Status}) — resolver/path unreachable; no fingerprint obtained");
        }
        var txt = response.Records.FirstOrDefault(r => r.Type == TypeTXT);
        if (txt is null || !DecodeTXTBanner(txt.Rdata, out var banner) || string.IsNullOrEmpty(banner))
        {
            return new VersionBindResult(VersionBindResultStatus.Withheld, Server: server,
                Detail: "resolver did not disclose a version.bind banner (refused or empty) — normal for a hardened / public resolver; no signal");
        }
        bool forwarder = expectedPublicResolver && MatchesForwarderSignature(banner);
        return new VersionBindResult(VersionBindResultStatus.Disclosed, Banner: banner, Server: server,
            ForwarderOnPublicPath: forwarder,
            Detail: forwarder
                ? $"a resolver expected to be a public recursive answered with a local-forwarder banner ({banner}) — something local is answering on the path"
                : $"resolver version banner: {banner}");
    }

    private static bool MatchesForwarderSignature(string banner)
    {
        var low = banner.ToLowerInvariant();
        return ForwarderSignatures.Any(s => low.Contains(s, StringComparison.Ordinal));
    }

    private static bool DecodeTXTBanner(string hexRData, out string? banner)
    {
        banner = null;
        var bytes = HexToBytes(hexRData);
        if (bytes is null || bytes.Length == 0) return false;
        int i = 0;
        var parts = new List<string>();
        while (i < bytes.Length)
        {
            int len = bytes[i]; i++;
            if (len <= 0) continue;
            if (i + len > bytes.Length) break;
            var slice = bytes.AsSpan(i, len);
            parts.Add(System.Text.Encoding.UTF8.GetString(slice));
            i += len;
        }
        var joined = string.Join(" ", parts).Trim();
        if (string.IsNullOrEmpty(joined)) return false;
        banner = joined;
        return true;
    }

    private static byte[]? HexToBytes(string hex)
    {
        if (hex.Length % 2 != 0) return null;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                return null;
        }
        return bytes;
    }

    private static bool DidFail(DNSResponse r) =>
        r.Status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError
                 or DNSResponseStatus.MalformedResponse or DNSResponseStatus.ResponseIdMismatch;
}
