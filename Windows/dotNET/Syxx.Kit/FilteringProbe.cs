using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// <summary>
/// C2 — Filtering verification (macOS FilteringProbe twin). "Is my Pi-hole / NextDNS / AdGuard
/// actually working?" — enforcement + bypass, tri-state honest: an unreadable probe never reads
/// as a working filter.
/// </summary>
public static class FilteringProbe
{
    /// Domains on the standard default blocklists (StevenBlack / AdGuard base); when unfiltered
    /// they resolve to real public addresses, so a public answer is decisive evidence the filter
    /// did not act.
    public static readonly string[] ProbeDomains =
        { "doubleclick.net", "googlesyndication.com", "googleadservices.com" };

    public enum DomainOutcome { Sinkholed, Resolved, Unknown }
    public enum Enforcement { Active, Inactive, Partial, Inconclusive }

    public sealed record Result(
        Enforcement Enforcement,
        IReadOnlyDictionary<string, DomainOutcome> PerDomain,
        ConditionState DirectBypassOpen,
        string Detail);

    /// Pure classifier for one answer — the mutation-tested core.
    public static DomainOutcome Classify(DNSResponse response)
    {
        switch (response.Status)
        {
            case DNSResponseStatus.NXDomain:
                return DomainOutcome.Sinkholed;
            case DNSResponseStatus.Success:
                var addrs = response.Records
                    .Where(r => r.Type == 1 || r.Type == 28)
                    .Select(r => r.Rdata).ToList();
                if (addrs.Count == 0) return DomainOutcome.Sinkholed; // NODATA for an ad domain = filtered
                // A public address means the query escaped the filter. Anything else — 0.0.0.0,
                // ::, loopback, private redirect — is a sinkhole answer.
                bool anyPublic = addrs.Any(a => IPClassifier.Classify(a) == IPAddressClass.PublicRoutable);
                return anyPublic ? DomainOutcome.Resolved : DomainOutcome.Sinkholed;
            case DNSResponseStatus.NoData:
                return DomainOutcome.Sinkholed;
            default:
                return DomainOutcome.Unknown;
        }
    }

    /// Pure aggregation — the mutation-tested core.
    public static Enforcement Aggregate(IReadOnlyList<DomainOutcome> outcomes)
    {
        var readable = outcomes.Where(o => o != DomainOutcome.Unknown).ToList();
        if (readable.Count == 0) return Enforcement.Inconclusive;
        int sinkholed = readable.Count(o => o == DomainOutcome.Sinkholed);
        if (sinkholed == readable.Count) return Enforcement.Active;
        if (sinkholed == 0) return Enforcement.Inactive;
        return Enforcement.Partial;
    }

    /// Live probe through the system resolver, plus the direct-bypass canary via
    /// <paramref name="bypassResolver"/>.
    public static async Task<Result> ProbeAsync(string resolver, string bypassResolver = "8.8.8.8")
    {
        var perDomain = new Dictionary<string, DomainOutcome>();
        foreach (var domain in ProbeDomains)
        {
            var resp = await DNSResolver.QueryUDPDetailedAsync(new DNSQuery(domain, 1, resolver, 53));
            perDomain[domain] = Classify(resp);
        }
        var enforcement = Aggregate(ProbeDomains.Select(d => perDomain[d]).ToList());

        var canary = await DNSResolver.QueryUDPDetailedAsync(new DNSQuery(ProbeDomains[0], 1, bypassResolver, 53));
        ConditionState directBypassOpen = Classify(canary) switch
        {
            DomainOutcome.Resolved => ConditionState.Yes,
            DomainOutcome.Sinkholed => ConditionState.No,
            _ => ConditionState.Unknown,
        };

        int s = perDomain.Values.Count(o => o == DomainOutcome.Sinkholed);
        int r = perDomain.Values.Count(o => o == DomainOutcome.Resolved);
        int u = perDomain.Values.Count(o => o == DomainOutcome.Unknown);
        return new Result(enforcement, perDomain, directBypassOpen,
            $"Blocklist probes via {resolver}: {s} sinkholed / {r} resolved / {u} unreadable of {ProbeDomains.Length}");
    }
}
