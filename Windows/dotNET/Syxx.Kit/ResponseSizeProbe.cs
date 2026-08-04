using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

public enum ResponseSizeStatus { Normal, Oversized, InsufficientData, Untested }

public sealed record ResponseSizeAudit(
    ResponseSizeStatus Status,
    int SampleCount = 0,
    int MaxBytes = 0,
    int OversizedCount = 0,
    string Detail = "")
{
    public static readonly ResponseSizeAudit Untested = new(ResponseSizeStatus.Untested, Detail: "not run");
}

public static class ResponseSizeProbe
{
    public const int MinSamples = 8;
    public const int LargeThreshold = 2048;

    public static ResponseSizeAudit Audit(List<int> responseSizes)
    {
        var sized = responseSizes.Where(s => s > 0).ToList();
        if (sized.Count < MinSamples)
        {
            return new ResponseSizeAudit(ResponseSizeStatus.InsufficientData, SampleCount: sized.Count,
                Detail: $"only {sized.Count} sized responses (< {MinSamples}) — not enough to judge; unmeasured, not clean");
        }
        int maxB = sized.Count > 0 ? sized.Max() : 0;
        int over = sized.Count(s => s >= LargeThreshold);
        bool oversized = over >= Math.Max(2, sized.Count / 5);
        if (oversized)
        {
            return new ResponseSizeAudit(ResponseSizeStatus.Oversized, SampleCount: sized.Count, MaxBytes: maxB, OversizedCount: over,
                Detail: $"{over} of {sized.Count} responses were >= {LargeThreshold} bytes (max {maxB}) — a sustained large-response pattern consistent with amplification or covert DNS tunnelling");
        }
        return new ResponseSizeAudit(ResponseSizeStatus.Normal, SampleCount: sized.Count, MaxBytes: maxB, OversizedCount: over,
            Detail: $"response sizes within the ordinary range (max {maxB} bytes across {sized.Count} responses)");
    }

    public static ResponseSizeAudit Audit(List<DNSCaptureRecord> records)
    {
        var sizes = new List<int>();
        foreach (var rec in records)
        {
            if (!rec.Network.Direction.Contains("inbound", StringComparison.OrdinalIgnoreCase)) continue;
            sizes.Add(rec.Packet.PayloadBytes);
        }
        return Audit(sizes);
    }
}
