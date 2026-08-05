using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// K-07: the member order previously placed `Diverges` before `Matches` — the reverse of the Swift
/// twin (`AuthoritativeComparison.Status`: matches, diverges, inconclusive, untested). Nothing
/// serialized this by ordinal yet, so the divergence was latent, but System.Text.Json emits a bare
/// enum as its NUMERIC value by default: the first thing to serialize this type would have put `0`
/// on the wire meaning `Diverges` here and `matches` on macOS — a silent inversion of the exact
/// finding this type exists to carry. Fixed both ways deliberately: the member order now matches
/// Swift, AND the wire form is pinned explicitly to Swift's lowercase raw values by the converter
/// below, so ordinal drift can never reach the wire again. Explicit beats positional.
[JsonConverter(typeof(AuthComparisonStatusJsonConverter))]
public enum AuthComparisonStatus { Matches, Diverges, Inconclusive, Untested }

public static class AuthComparisonStatusExtensions
{
    /// The wire form — identical to the Swift twin's `String` raw values.
    public static string RawValue(this AuthComparisonStatus s) => s switch
    {
        AuthComparisonStatus.Matches => "matches",
        AuthComparisonStatus.Diverges => "diverges",
        AuthComparisonStatus.Inconclusive => "inconclusive",
        AuthComparisonStatus.Untested => "untested",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };
}

public sealed class AuthComparisonStatusJsonConverter : JsonConverter<AuthComparisonStatus>
{
    public override AuthComparisonStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "matches" => AuthComparisonStatus.Matches,
            "diverges" => AuthComparisonStatus.Diverges,
            "inconclusive" => AuthComparisonStatus.Inconclusive,
            "untested" => AuthComparisonStatus.Untested,
            var s => throw new JsonException($"unknown AuthComparisonStatus '{s}'"),
        };

    public override void Write(Utf8JsonWriter writer, AuthComparisonStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.RawValue());
}

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
            // K-16 / C015 (macOS twin parity): a same-CDN-provider observation ANNOTATES the
            // divergence but does NOT erase it. A /13 CDN block (Cloudflare, Akamai) spans millions
            // of IPs — an attacker sitting in the same provider's address space is not evidence of
            // benign geo-routing. This branch previously returned Inconclusive, which fell through
            // `Synthesizer`'s `default:` and emitted NO evidence item at all: the finding did not
            // merely soften, it disappeared. The `Diverges` result is retained; the provider is
            // surfaced as a corroboration note for the synthesis layer to weigh.
            var provider = NetworkCohort.SharedProvider(resolverSet.ToList(), authSet.ToList());
            if (provider is not null)
                return Make(AuthComparisonStatus.Diverges, $"recursive and authoritative addresses are disjoint but both lie in {provider} ranges — same-provider softening noted; divergence retained (CDN/geo routing possible but attacker may share the provider's ASN)");
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
