using System;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum ECSLeakStatus { Forwarded, NotForwarded, Inconclusive, Untested }

public sealed record ECSLeak(
    ECSLeakStatus Status,
    byte? ScopePrefixLength = null,
    string Detail = "")
{
    public static readonly ECSLeak Untested = new(ECSLeakStatus.Untested, Detail: "not run");
}

public static class ECSProbe
{
    public const ushort TypeA = 1;
    public const string TestSubnetV4 = "203.0.113.0";

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<ECSLeak> Probe(string domain = "google.com",
        string server = "",
        string testSubnet = TestSubnetV4,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        var resp = await query(new DNSQuery(name: domain, type: TypeA, server: server,
            edns0UDPSize: 4096, edns0ClientSubnetV4: testSubnet));
        if (DidFail(resp) || resp.Edns is null)
        {
            return new ECSLeak(ECSLeakStatus.Inconclusive,
                Detail: $"no EDNS/OPT echoed for the ECS query ({resp.Status}) — cannot assess ECS forwarding");
        }
        if (resp.Edns.EcsScopePrefixLength is byte scope && scope > 0)
        {
            return new ECSLeak(ECSLeakStatus.Forwarded, ScopePrefixLength: scope,
                Detail: $"resolver echoed an ECS scope of /{scope} — it forwards your client subnet to authoritative servers (privacy exposure)");
        }
        return new ECSLeak(ECSLeakStatus.NotForwarded, ScopePrefixLength: resp.Edns.EcsScopePrefixLength,
            Detail: "resolver echoed EDNS but applied no ECS scope — it strips/ignores Client Subnet (privacy-preserving)");
    }

    private static bool DidFail(DNSResponse r) =>
        r.Status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError
                 or DNSResponseStatus.MalformedResponse or DNSResponseStatus.ResponseIdMismatch;
}
