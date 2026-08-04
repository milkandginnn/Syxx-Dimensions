using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum ResolverLatencyStatus { Fast, Moderate, Slow, Degraded, Unreachable, Untested }

public sealed record ResolverLatency(
    ResolverLatencyStatus Status,
    int SampleCount = 0,
    int SuccessCount = 0,
    double MinMs = 0,
    double MedianMs = 0,
    double MaxMs = 0,
    string Detail = "")
{
    public static readonly ResolverLatency Untested = new(ResolverLatencyStatus.Untested, Detail: "not run");
}

public static class ResolverLatencyProbe
{
    public const ushort TypeA = 1;
    public const double FastThreshold = 50;
    public const double ModerateThreshold = 150;
    public const double SlowThreshold = 150;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<ResolverLatency> Probe(string domain,
        ushort qtype = 1,
        string server = "1.1.1.1",
        int samples = 4,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        int count = Math.Max(1, Math.Min(samples, 10));
        var latenciesMs = new List<double>();
        for (int i = 0; i < count; i++)
        {
            var sw = Stopwatch.StartNew();
            var resp = await query(new DNSQuery(name: domain, type: qtype, server: server, port: 53));
            sw.Stop();
            switch (resp.Status)
            {
                case DNSResponseStatus.Success:
                case DNSResponseStatus.NoData:
                case DNSResponseStatus.NXDomain:
                case DNSResponseStatus.ServFail:
                case DNSResponseStatus.Refused:
                    latenciesMs.Add(sw.Elapsed.TotalMilliseconds);
                    break;
            }
        }
        return Classify(latenciesMs, attempted: count);
    }

    public static ResolverLatency Classify(List<double> latenciesMs, int attempted)
    {
        if (latenciesMs.Count == 0)
        {
            return new ResolverLatency(ResolverLatencyStatus.Unreachable, SampleCount: attempted, SuccessCount: 0,
                Detail: $"no response in {attempted} queries — resolver unreachable");
        }

        var sorted = latenciesMs.OrderBy(v => v).ToList();
        double median = sorted[sorted.Count / 2];
        double minMs = sorted[0];
        double maxMs = sorted[^1];
        int success = latenciesMs.Count;
        bool degraded = success < attempted;

        var byMedian = median < FastThreshold
            ? ResolverLatencyStatus.Fast
            : (median < ModerateThreshold ? ResolverLatencyStatus.Moderate : ResolverLatencyStatus.Slow);
        var status = degraded ? ResolverLatencyStatus.Degraded : byMedian;
        var detail = degraded
            ? $"{success}/{attempted} queries answered ({attempted - success} lost); median {Rounded(median)} ms — degraded/flaky path"
            : $"median {Rounded(median)} ms (min {Rounded(minMs)} / max {Rounded(maxMs)}) over {success} samples";

        return new ResolverLatency(status, SampleCount: attempted, SuccessCount: success,
            MinMs: minMs, MedianMs: median, MaxMs: maxMs, Detail: detail);
    }

    private static string Rounded(double ms) => Math.Round(ms * 10).ToString("F1", CultureInfo.InvariantCulture);
}
