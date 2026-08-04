using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum DualStackStatus { DualStack, Ipv4Only, Dns64Synthesis, Ipv4Suppressed, Inconclusive, Untested }

public sealed record DualStackAudit(
    DualStackStatus Status,
    IReadOnlyList<string>? AAddresses = null,
    IReadOnlyList<string>? AaaaAddresses = null,
    int Dns64Count = 0,
    string Detail = "")
{
    public static readonly DualStackAudit Untested = new(DualStackStatus.Untested, Detail: "not run");
}

public static class DualStackProbe
{
    public const ushort TypeA = 1;
    public const ushort TypeAAAA = 28;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<DualStackAudit> Probe(string domain,
        string server = "1.1.1.1",
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        var a = await query(new DNSQuery(name: domain, type: TypeA, server: server, port: 53));
        var aaaa = await query(new DNSQuery(name: domain, type: TypeAAAA, server: server, port: 53));
        return Classify(a.Status, a.Records.Where(r => r.Type == TypeA).Select(r => r.Rdata).ToList(),
                        aaaa.Status, aaaa.Records.Where(r => r.Type == TypeAAAA).Select(r => r.Rdata).ToList());
    }

    public static DualStackAudit Classify(DNSResponseStatus aStatus, List<string> aAddrs,
        DNSResponseStatus aaaaStatus, List<string> aaaaAddrs)
    {
        bool aOk = aStatus == DNSResponseStatus.Success && aAddrs.Count > 0;
        bool aaaaOk = aaaaStatus == DNSResponseStatus.Success && aaaaAddrs.Count > 0;
        bool aDenied = aStatus == DNSResponseStatus.NXDomain || aStatus == DNSResponseStatus.NoData;
        bool aaaaDenied = aaaaStatus == DNSResponseStatus.NXDomain || aaaaStatus == DNSResponseStatus.NoData;
        var dns64 = aaaaAddrs.Where(IsDNS64).ToList();

        DualStackAudit Make(DualStackStatus s, string d) =>
            new(s, AAddresses: aAddrs, AaaaAddresses: aaaaAddrs, Dns64Count: dns64.Count, Detail: d);

        if (!aOk && !aaaaOk && !aDenied && !aaaaDenied)
            return Make(DualStackStatus.Inconclusive, "neither A nor AAAA produced a usable answer — cannot audit dual-stack");

        if (aaaaOk && dns64.Count == aaaaAddrs.Count)
            return Make(DualStackStatus.Dns64Synthesis, $"all {aaaaAddrs.Count} AAAA answer(s) are in 64:ff9b::/96 — NAT64/DNS64 synthesis (benign)");

        if (aOk && aaaaOk)
            return Make(DualStackStatus.DualStack, "A and AAAA both resolve — normal dual-stack");

        if (aOk && aaaaDenied)
            return Make(DualStackStatus.Ipv4Only, "A resolves; AAAA is a genuine negative — IPv4-only name (benign)");

        // Only a GENUINE A negative (NXDomain/NoData) alongside a working AAAA is a selective-IPv4-
        // blocking signal. An A *failure* (Timeout/TransportError/ServFail) is a missing datum, not a
        // result — it must fall through to Inconclusive (§1). [B038]
        if (aaaaOk && aDenied)
            return Make(DualStackStatus.Ipv4Suppressed, $"AAAA resolves but A does not ({aStatus}) — the IPv4 record is missing while IPv6 works; corroborate for selective blocking");

        return Make(DualStackStatus.Inconclusive, $"A ({aStatus}) / AAAA ({aaaaStatus}) pairing is ambiguous");
    }

    public static bool IsDNS64(string addr)
    {
        if (!IPAddress.TryParse(addr, out var ip)) return false;
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;
        byte[] bytes = ip.GetAddressBytes();
        if (bytes.Length != 16) return false;
        byte[] prefix = [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];
        return bytes.Take(12).SequenceEqual(prefix);
    }
}
