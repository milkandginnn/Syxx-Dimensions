using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum WildcardSynthesisStatus { NoSynthesis, WildcardZone, NxHijack, Inconclusive, Untested }

public sealed record WildcardSynthesis(
    WildcardSynthesisStatus Status,
    IReadOnlyList<string>? ProbedLabels = null,
    IReadOnlyList<string>? SyntheticAddresses = null,
    int UsableCount = 0,
    string Detail = "")
{
    public static readonly WildcardSynthesis Untested = new(WildcardSynthesisStatus.Untested, Detail: "not run");
}

public static class WildcardSynthesisProbe
{
    public const ushort TypeA = 1;

    public sealed record LabelObservation(
        string Label,
        IReadOnlyList<string> TestAddrs,
        bool TestHasAnswer,
        bool RefHasAnswer,
        bool RefDenies);

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<WildcardSynthesis> Probe(string domain,
        string resolverUnderTest,
        string reference = "1.1.1.1",
        int count = 3,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        int n = Math.Max(1, Math.Min(count, 5));
        var obs = new List<LabelObservation>();
        for (int i = 0; i < n; i++)
        {
            var name = $"{RandomLabel()}.{domain}";
            var testResp = await query(new DNSQuery(name: name, type: TypeA, server: resolverUnderTest, port: 53));
            var refResp = await query(new DNSQuery(name: name, type: TypeA, server: reference, port: 53));

            if (!IsDefinitive(testResp) || !IsDefinitive(refResp)) continue;

            var testAddrs = ARecords(testResp);
            obs.Add(new LabelObservation(
                Label: name,
                TestAddrs: testAddrs,
                TestHasAnswer: testResp.Status == DNSResponseStatus.Success && testAddrs.Count > 0,
                RefHasAnswer: refResp.Status == DNSResponseStatus.Success && ARecords(refResp).Count > 0,
                RefDenies: refResp.Status == DNSResponseStatus.NXDomain || refResp.Status == DNSResponseStatus.NoData));
        }
        return Classify(obs);
    }

    public static WildcardSynthesis Classify(List<LabelObservation> obs)
    {
        var labels = obs.Select(o => o.Label).ToList();

        WildcardSynthesis Make(WildcardSynthesisStatus s, List<string> synthetic, string d)
        {
            return new WildcardSynthesis(s, ProbedLabels: labels,
                SyntheticAddresses: [.. new HashSet<string>(synthetic).OrderBy(x => x, StringComparer.Ordinal)], UsableCount: obs.Count, Detail: d);
        }

        if (obs.Count == 0)
            return Make(WildcardSynthesisStatus.Inconclusive, [], "no label produced a usable cross-vantage comparison — reference unreachable or all transport-failed");

        var hijacks = obs.Where(o => o.RefDenies && o.TestHasAnswer).ToList();
        if (hijacks.Count > 0)
        {
            var synth = hijacks.SelectMany(o => o.TestAddrs).ToList();
            return Make(WildcardSynthesisStatus.NxHijack, synth,
                $"resolver invented address(es) for {hijacks.Count} name(s) a trusted resolver reports do not exist — NXDOMAIN hijack (block/ad page)");
        }

        if (obs.All(o => o.RefHasAnswer && o.TestHasAnswer))
            return Make(WildcardSynthesisStatus.WildcardZone, [], "random subdomains resolve at both vantages — the zone uses a wildcard (benign)");

        if (obs.All(o => o.RefDenies && !o.TestHasAnswer))
            return Make(WildcardSynthesisStatus.NoSynthesis, [], "random nonexistent subdomains are denied at both vantages — honest negative");

        return Make(WildcardSynthesisStatus.Inconclusive, [], $"mixed cross-vantage outcomes across {obs.Count} labels — no consistent wildcard or hijack signal");
    }

    private static bool IsDefinitive(DNSResponse r) =>
        r.Status is DNSResponseStatus.Success or DNSResponseStatus.NXDomain or DNSResponseStatus.NoData;

    private static List<string> ARecords(DNSResponse r) =>
        r.Records.Where(rr => rr.Type == TypeA).Select(rr => rr.Rdata.Trim().ToLowerInvariant()).ToList();

    private static string RandomLabel()
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        var chars = new char[12];
        for (int i = 0; i < 12; i++)
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
    }
}
