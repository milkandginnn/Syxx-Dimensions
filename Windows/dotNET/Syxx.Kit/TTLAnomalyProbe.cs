using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum TTLAnomalyStatus { IndependentCaching, LockstepTTL, ExtremeTTL, Inconclusive, Untested }

public sealed record TTLAnomaly(
    TTLAnomalyStatus Status,
    IReadOnlyDictionary<string, uint>? TtlsByResolver = null,
    string Detail = "")
{
    public static readonly TTLAnomaly Untested = new(TTLAnomalyStatus.Untested, Detail: "not run");
}

public static class TTLAnomalyProbe
{
    public const ushort TypeA = 1;
    public const uint ExtremeTTLSeconds = 604800;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<TTLAnomaly> Probe(string domain,
        ushort qtype = TypeA,
        IReadOnlyList<string>? resolvers = null,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        resolvers ??= ["1.1.1.1", "8.8.8.8", "9.9.9.9"];
        var ttls = new Dictionary<string, uint>();
        foreach (var server in resolvers)
        {
            var resp = await query(new DNSQuery(name: domain, type: qtype, server: server, port: 53));
            if (resp.Status != DNSResponseStatus.Success) continue;
            var answer = resp.Records.Where(r => r.Type == qtype).ToList();
            if (answer.Count == 0) continue;
            ttls[server] = answer.Min(r => r.Ttl);
        }
        return Classify(ttls);
    }

    public static TTLAnomaly Classify(Dictionary<string, uint> ttlsByResolver)
    {
        TTLAnomaly Make(TTLAnomalyStatus s, string d) =>
            new(s, TtlsByResolver: ttlsByResolver, Detail: d);

        var values = ttlsByResolver.Values.ToList();
        if (values.Count < 2)
            return Make(TTLAnomalyStatus.Inconclusive, $"only {values.Count} resolver(s) answered — cannot compare TTLs across vantages");

        if (values.Max() > ExtremeTTLSeconds)
            return Make(TTLAnomalyStatus.ExtremeTTL, $"a resolver reports TTL {values.Max()}s (> 7 days) — implausibly pinned answer; possible stale/replay");

        var nonZero = values.Where(v => v > 0).ToList();
        if (nonZero.Count >= 3 && new HashSet<uint>(nonZero).Count == 1)
            return Make(TTLAnomalyStatus.LockstepTTL, $"{nonZero.Count} independent resolvers report the identical TTL {nonZero[0]}s — not independent caching (shared upstream or on-path injection); corroborate");

        return Make(TTLAnomalyStatus.IndependentCaching, $"TTLs vary across resolvers ({string.Join(", ", values.OrderBy(v => v).Select(v => $"{v}s"))}) — normal independent caching");
    }
}
