using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum AnswerStabilityStatus { Stable, Rotating, Unstable, TtlAnomaly, Inconclusive, Untested }

public sealed record AnswerStability(
    AnswerStabilityStatus Status,
    int SampleCount = 0,
    int UsableCount = 0,
    int DistinctAnswerSets = 0,
    bool TtlHonored = true,
    string Detail = "")
{
    public static readonly AnswerStability Untested = new(AnswerStabilityStatus.Untested, Detail: "not run");
}

public static class AnswerStabilityProbe
{
    public const ushort TypeA = 1;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<AnswerStability> Probe(string domain,
        ushort qtype = 1,
        string server = "1.1.1.1",
        int samples = 3,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        int count = Math.Max(2, Math.Min(samples, 8));
        var sets = new List<HashSet<string>>();
        var minTTLs = new List<uint>();
        for (int i = 0; i < count; i++)
        {
            var resp = await query(new DNSQuery(name: domain, type: qtype, server: server, port: 53));
            if (resp.Status != DNSResponseStatus.Success) continue;
            var addrs = resp.Records.Where(r => r.Type == qtype).ToList();
            var set = new HashSet<string>(addrs.Select(r => r.Rdata.Trim().ToLowerInvariant()));
            if (set.Count == 0) continue;
            sets.Add(set);
            minTTLs.Add(addrs.Min(r => r.Ttl));
        }
        return Classify(sets, minTTLs, attempted: count);
    }

    public static AnswerStability Classify(List<HashSet<string>> sets, List<uint> minTTLs, int attempted)
    {
        AnswerStability Make(AnswerStabilityStatus s, bool ttlHonored, string d)
        {
            return new AnswerStability(s, SampleCount: attempted, UsableCount: sets.Count,
                DistinctAnswerSets: new HashSet<string>(sets.Select(s2 => string.Join(",", s2.OrderBy(x => x, StringComparer.Ordinal)))).Count,
                TtlHonored: ttlHonored, Detail: d);
        }

        if (sets.Count < 2)
        {
            return Make(AnswerStabilityStatus.Inconclusive, true,
                $"only {sets.Count} usable answer(s) in {attempted} samples — cannot assess stability");
        }

        // B078: pairwise check — every adjacent pair must be non-increasing.
        // A single endpoint-only check (Last <= First) can miss mid-sequence regressions.
        bool ttlHonored = true;
        for (int i = 1; i < minTTLs.Count; i++)
        {
            if (minTTLs[i] > minTTLs[i - 1])
            {
                ttlHonored = false;
                break;
            }
        }
        var distinct = new HashSet<string>(sets.Select(s => string.Join(",", s.OrderBy(x => x, StringComparer.Ordinal))));

        if (distinct.Count == 1)
        {
            return ttlHonored
                ? Make(AnswerStabilityStatus.Stable, true, $"identical answer set across {sets.Count} samples; TTL honored")
                : Make(AnswerStabilityStatus.TtlAnomaly, false, "answer set stable but TTL rose across samples — cache/rewrite anomaly");
        }

        var common = sets.Skip(1).Aggregate(new HashSet<string>(sets[0]), (h, s) => { h.IntersectWith(s); return h; });
        if (common.Count > 0)
        {
            return Make(AnswerStabilityStatus.Rotating, ttlHonored,
                $"answer sets vary but share {common.Count} address(es) — round-robin / load-balancing");
        }

        return Make(AnswerStabilityStatus.Unstable, ttlHonored,
            "answer sets change with no shared address across samples — unstable/flapping (corroborate)");
    }
}
