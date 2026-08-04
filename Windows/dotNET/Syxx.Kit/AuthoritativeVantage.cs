using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum AuthComparisonStatus { Diverges, Matches, Inconclusive, Untested }

public sealed record AuthoritativeComparison(
    AuthComparisonStatus Status,
    IReadOnlyList<string>? AuthoritativeAnswers = null,
    IReadOnlyList<string>? ResolverAnswers = null,
    IReadOnlyList<string>? Nameservers = null,
    string Detail = "")
{
    public static readonly AuthoritativeComparison Untested = new(AuthComparisonStatus.Untested, Detail: "not run");
}

public static class AuthoritativeVantage
{
    public const ushort TypeA = 1;
    public const ushort TypeAAAA = 28;
    public const ushort TypeNS = 2;

    public static readonly IReadOnlyList<string> DefaultBootstrapResolvers = ["1.1.1.1", "8.8.8.8", "9.9.9.9"];

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<AuthoritativeComparison> Compare(string domain,
        ushort qtype = TypeA,
        IReadOnlyList<string> resolverAnswers = null,
        IReadOnlyList<string>? bootstrapResolvers = null,
        int maxNameservers = 6,
        int maxServersProbed = 4,
        Func<DNSQuery, Task<DNSResponse>>? query = null,
        bool resolverDidFail = false)
    {
        query ??= DefaultQuery;
        var bootstraps = bootstrapResolvers ?? DefaultBootstrapResolvers;
        var resolverSet = new HashSet<string>((resolverAnswers ?? []).Select(NormalizeAddress));

        var nsHosts = await DiscoverNameservers(domain, bootstraps, maxNameservers, query);
        if (nsHosts.Count == 0)
        {
            return new AuthoritativeComparison(AuthComparisonStatus.Inconclusive,
                ResolverAnswers: [.. resolverSet],
                Detail: $"no authoritative nameservers found for {domain} (delegation lookup failed)");
        }

        var nsIPs = new List<string>();
        foreach (var host in nsHosts)
        {
            foreach (var bs in bootstraps)
            {
                var resp = await query(new DNSQuery(name: host, type: TypeA, server: bs, port: 53));
                nsIPs.AddRange(resp.Records.Where(r => r.Type == TypeA).Select(r => r.Rdata));
            }
            if (nsIPs.Count >= maxServersProbed) break;
        }
        // D018: reject NS IPs that are not public-routable to prevent SSRF to internal hosts
        // via hostile delegation glue (127.0.0.1, 10.x, 169.254.169.254, etc.).
        nsIPs = [.. new HashSet<string>(nsIPs)
            .Where(ip => IPClassifier.Classify(ip) == IPAddressClass.PublicRoutable)
            .Take(maxServersProbed)];
        if (nsIPs.Count == 0)
        {
            return new AuthoritativeComparison(AuthComparisonStatus.Inconclusive,
                ResolverAnswers: [.. resolverSet], Nameservers: nsHosts,
                Detail: "could not resolve authoritative nameserver IPs (or all resolved to non-public addresses)");
        }

        var authSets = new List<HashSet<string>>();
        foreach (var ip in nsIPs)
        {
            var resp = await query(new DNSQuery(name: domain, type: qtype, server: ip, port: 53));
            if (!resp.IsAuthoritative) continue;
            if (resp.Status is not (DNSResponseStatus.Success or DNSResponseStatus.NoData or DNSResponseStatus.NXDomain))
                continue;
            var addrs = new HashSet<string>(resp.Records
                .Where(r => r.Type == qtype)
                .Select(r => NormalizeAddress(r.Rdata)));
            authSets.Add(addrs);
        }
        if (authSets.Count < 2)
        {
            var detail = authSets.Count == 0
                ? "no authoritative server returned an AA-flagged answer"
                : "only 1 authoritative server responded (need ≥2 for quorum)";
            return new AuthoritativeComparison(AuthComparisonStatus.Inconclusive,
                ResolverAnswers: [.. resolverSet], Nameservers: nsHosts, Detail: detail);
        }
        // Majority consensus: pick the answer set shared by the most responders.
        var authSet = authSets.GroupBy(x => x, HashSet<string>.CreateSetComparer())
            .MaxBy(g => g.Count())?.Key ?? authSets[0];

        return Classify(resolverSet, authSet, nsHosts, domain, resolverDidFail);
    }

    public static AuthoritativeComparison Classify(HashSet<string> resolverSet, HashSet<string> authSet,
        List<string> nameservers, string domain, bool resolverDidFail = false)
    {
        var auth = authSet.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var res = resolverSet.OrderBy(x => x, StringComparer.Ordinal).ToList();

        AuthoritativeComparison Make(AuthComparisonStatus s, string d) =>
            new(s, AuthoritativeAnswers: auth, ResolverAnswers: res, Nameservers: nameservers, Detail: d);

        if (resolverSet.Count == 0 && authSet.Count == 0)
        {
            // B036: if the resolver query itself failed (timeout/transport error), we cannot
            // conclude "matches" — the empty set might mean "no data" or "no response."
            if (resolverDidFail)
                return Make(AuthComparisonStatus.Inconclusive, "resolver query failed — cannot confirm consistency with authoritative empty answer");
            return Make(AuthComparisonStatus.Matches, "both the recursive resolver and authoritative report no address — consistent");
        }

        if (resolverSet.Count == 0)
            return Make(AuthComparisonStatus.Inconclusive, $"recursive returned no address though authoritative serves {authSet.Count} — recursive lookup incomplete");

        if (authSet.Count == 0)
            return Make(AuthComparisonStatus.Diverges, $"recursive returned {resolverSet.Count} address(es) the authoritative NS does not serve at all (authoritative negative) — likely injected answer");

        if (resolverSet.IsSubsetOf(authSet))
            return Make(AuthComparisonStatus.Matches, "every recursive address is served by the authoritative NS — consistent with ground truth");

        if (!resolverSet.Overlaps(authSet))
        {
            var provider = NetworkCohort.SharedProvider(resolverSet.ToList(), authSet.ToList());
            if (provider is not null)
                return Make(AuthComparisonStatus.Inconclusive, $"recursive and authoritative addresses differ but are all in {provider} ranges — CDN/geo routing, not a rewrite");
            return Make(AuthComparisonStatus.Diverges, "recursive addresses share nothing with the authoritative set — possible upstream rewrite (corroborate: CDN/geo routing can also cause this)");
        }

        return Make(AuthComparisonStatus.Inconclusive, "recursive and authoritative overlap only partially — consistent with CDN/geo-routed answers; inconclusive without ASN corroboration");
    }

    internal static async Task<List<string>> DiscoverNameservers(string domain, IReadOnlyList<string> bootstraps, int cap,
        Func<DNSQuery, Task<DNSResponse>> query)
    {
        var candidate = domain.EndsWith(".", StringComparison.Ordinal) ? domain[..^1] : domain;
        for (int i = 0; i < 8; i++)
        {
            var labels = candidate.Split('.');
            if (labels.Length < 2) break;

            var nsSets = new List<HashSet<string>>();
            foreach (var bs in bootstraps)
            {
                var resp = await query(new DNSQuery(name: candidate, type: TypeNS, server: bs, port: 53));
                var hosts = new HashSet<string>(resp.Records
                    .Where(r => r.Type == TypeNS)
                    .Select(r => NormalizeName(r.Rdata))
                    .Where(n => n.Length > 0));
                if (hosts.Count > 0)
                    nsSets.Add(hosts);
            }

            if (nsSets.Count > 0)
            {
                var agreed = MajorityConsensus(nsSets);
                if (agreed.Count > 0)
                    return [.. agreed.OrderBy(x => x, StringComparer.Ordinal).Take(cap)];
            }

            candidate = string.Join(".", labels.Skip(1));
        }
        return [];
    }

    internal static HashSet<string> MajorityConsensus(List<HashSet<string>> sets)
    {
        if (sets.Count == 0) return [];
        if (sets.Count == 1) return []; // C007: single responder cannot provide ground truth
        var counts = new Dictionary<string, int>();
        foreach (var s in sets)
            foreach (var host in s)
                counts[host] = counts.GetValueOrDefault(host) + 1;
        return new HashSet<string>(counts.Where(kv => kv.Value >= 2).Select(kv => kv.Key));
    }

    internal static string NormalizeName(string s)
    {
        var n = s.Trim();
        if (n.EndsWith(".", StringComparison.Ordinal)) n = n[..^1];
        return n.ToLowerInvariant();
    }

    internal static string NormalizeAddress(string s) => s.Trim().ToLowerInvariant();
}
