using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum CNAMEChainStatus { Consistent, NoCNAME, Divergent, Inconclusive, Untested }

public sealed record CNAMEChainAudit(
    CNAMEChainStatus Status,
    IReadOnlyList<string>? TestChain = null,
    IReadOnlyList<IReadOnlyList<string>>? ReferenceChains = null,
    string Detail = "")
{
    public static readonly CNAMEChainAudit Untested = new(CNAMEChainStatus.Untested, Detail: "not run");
}

public static class CNAMEChainProbe
{
    public const ushort TypeA = 1;
    public const ushort TypeCNAME = 5;
    public const int MaxHops = 16;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<CNAMEChainAudit> Probe(string domain,
        string resolverUnderTest,
        IReadOnlyList<string>? references = null,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        references ??= ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

        async Task<List<string>> ChainAt(string server)
        {
            var resp = await query(new DNSQuery(name: domain, type: TypeA, server: server, port: 53));
            if (resp.Status is not (DNSResponseStatus.Success or DNSResponseStatus.NoData)) return [];
            return ReconstructChain(domain, resp.Records);
        }

        var testChain = await ChainAt(resolverUnderTest);
        var refChains = new List<List<string>>();
        bool skipped = false;
        foreach (var refServer in references)
        {
            if (refServer == resolverUnderTest) { skipped = true; continue; }
            var c = await ChainAt(refServer);
            if (c.Count > 0) refChains.Add(c);
        }
        // B311: when the resolver under test matched a reference, we lost one reference,
        // leaving only 2 for consensus. Add a supplementary reference to restore quorum.
        if (skipped && refChains.Count < 2)
        {
            const string supplementary = "208.67.222.222"; // OpenDNS
            if (!references.Contains(supplementary) && resolverUnderTest != supplementary)
            {
                var c = await ChainAt(supplementary);
                if (c.Count > 0) refChains.Add(c);
            }
        }
        return Classify(testChain, refChains);
    }

    public static List<string> ReconstructChain(string domain, List<DNSRecord> records)
    {
        var cnameMap = new Dictionary<string, string>();
        foreach (var r in records.Where(r => r.Type == TypeCNAME))
            cnameMap[Normalize(r.Name)] = Normalize(r.Rdata);

        var chain = new List<string> { Normalize(domain) };
        var seen = new HashSet<string> { Normalize(domain) };
        var current = Normalize(domain);
        while (cnameMap.TryGetValue(current, out var target) && chain.Count < MaxHops)
        {
            if (!seen.Add(target)) break;
            chain.Add(target);
            current = target;
        }
        return chain;
    }

    public static CNAMEChainAudit Classify(List<string> testChain, List<List<string>> referenceChains)
    {
        CNAMEChainAudit Make(CNAMEChainStatus s, string d) =>
            new(s, TestChain: testChain, ReferenceChains: [.. referenceChains], Detail: d);

        if (testChain.Count == 0 || referenceChains.Count == 0)
            return Make(CNAMEChainStatus.Inconclusive, "insufficient usable answers to compare CNAME chains");

        string Canon(List<string> c) => string.Join(" → ", c);

        if (testChain.Count <= 1 && referenceChains.All(c => c.Count <= 1))
            return Make(CNAMEChainStatus.NoCNAME, "no CNAME chain for this name (direct A/AAAA) at any vantage");

        var refCanons = new HashSet<string>(referenceChains.Select(Canon));
        if (refCanons.Contains(Canon(testChain)))
            return Make(CNAMEChainStatus.Consistent, "the resolver's CNAME chain matches a trusted reference — consistent");

        if (refCanons.Count == 1)
            return Make(CNAMEChainStatus.Divergent, $"trusted resolvers agree on the CNAME chain but this resolver returns a different one ({Canon(testChain)}) — outlier; corroborate (a split-horizon/geo edge case can also cause this)");

        return Make(CNAMEChainStatus.Inconclusive, "trusted resolvers disagree on the CNAME chain (CDN/geo variance) — no consensus to compare against");
    }

    private static string Normalize(string s)
    {
        var n = s.Trim().ToLowerInvariant();
        if (n.EndsWith(".", StringComparison.Ordinal)) n = n[..^1];
        return n;
    }
}
