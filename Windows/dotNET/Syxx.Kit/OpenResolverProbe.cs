using System;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum OpenResolverStatus { NotRecursive, RecursiveLowAmp, RecursiveHighAmp, Untested }

public sealed record OpenResolverFinding(
    OpenResolverStatus Status,
    string Target = "",
    bool Recursive = false,
    double AmplificationFactor = 0,
    string Detail = "")
{
    public static readonly OpenResolverFinding Untested = new(OpenResolverStatus.Untested, Detail: "not run");
}

public static class OpenResolverProbe
{
    public const ushort TypeA = 1;
    public const ushort TypeANY = 255;
    public const ushort TypeDNSKEY = 48;
    public const double HighAmpThreshold = 10.0;
    public const string RecursionProbeDomain = "example.com";
    public const string AmplificationDomain = "cloudflare.com";

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<OpenResolverFinding> Probe(string target,
        string recursionDomain = RecursionProbeDomain,
        string amplificationDomain = AmplificationDomain,
        ushort ampType = TypeDNSKEY,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        var rec = await query(new DNSQuery(name: recursionDomain, type: TypeA, server: target));
        if (rec.DidFail)
        {
            return new OpenResolverFinding(OpenResolverStatus.Untested, Target: target,
                Detail: $"target did not answer ({rec.Status}) — could not assess; unmeasured, not clean");
        }
        bool recursive = rec.Status == DNSResponseStatus.Success && rec.Records.Count > 0;
        if (!recursive)
        {
            return new OpenResolverFinding(OpenResolverStatus.NotRecursive, Target: target, Recursive: false,
                Detail: "target did not recursively resolve an external name for us — not acting as an open recursive resolver");
        }
        int queryBytes = DNSResolver.MakeQueryPacket(amplificationDomain, ampType).Length;
        var amp = await query(new DNSQuery(name: amplificationDomain, type: ampType, server: target));
        if (amp.DidFail)
        {
            return new OpenResolverFinding(OpenResolverStatus.Untested, Target: target, Recursive: true,
                Detail: $"amplification probe failed ({amp.Status}) — factor unmeasured, not clean");
        }
        return Classify(target, AmplificationFactor(queryBytes, amp));
    }

    public static double AmplificationFactor(int? queryBytes, DNSResponse response)
    {
        if (queryBytes is int qb && qb > 0 && response.ResponseByteLength is int r && r > 0)
            return (double)r / qb;
        return 0;
    }

    public static OpenResolverFinding Classify(string target, double factor)
    {
        if (factor >= HighAmpThreshold)
        {
            return new OpenResolverFinding(OpenResolverStatus.RecursiveHighAmp, Target: target, Recursive: true,
                AmplificationFactor: factor,
                Detail: $"acts as a recursive resolver with a ~{factor.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}× amplification factor. If it is also reachable from the internet — the condition that makes it an open resolver / DDoS reflector, confirmable only from outside via Dimension — it is an exposure risk.");
        }
        return new OpenResolverFinding(OpenResolverStatus.RecursiveLowAmp, Target: target, Recursive: true,
            AmplificationFactor: factor,
            Detail: $"acts as a recursive resolver (amplification factor ~{factor.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}×). Whether it is reachable from the internet — what would make it an open resolver — needs the Dimension outside-in check to confirm.");
    }

    // C119: removed local DidFail that shadowed the shared DNSResponse.DidFail property.
    // The local helper omitted Truncated, ServFail, and Refused — causing REFUSED/SERVFAIL
    // to fall through as NotRecursive instead of Untested. Use rec.DidFail / amp.DidFail directly.
}
