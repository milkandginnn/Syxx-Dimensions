using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

public enum StubEntropyStatus { WellRandomized, WeakRandomization, InsufficientData, Untested }

public sealed record StubEntropyAudit(
    StubEntropyStatus Status,
    int SampleCount = 0,
    int DistinctSourcePorts = 0,
    int DistinctTransactionIDs = 0,
    string Detail = "")
{
    public static readonly StubEntropyAudit Untested = new(StubEntropyStatus.Untested, Detail: "not run");
}

public static class StubEntropyProbe
{
    public const int MinSamples = 8;

    public static StubEntropyAudit Audit(List<(ushort xid, ushort sourcePort)> outboundQueries)
    {
        var q = outboundQueries;
        if (q.Count < MinSamples)
        {
            return new StubEntropyAudit(StubEntropyStatus.InsufficientData, SampleCount: q.Count,
                Detail: $"only {q.Count} captured outbound queries (< {MinSamples}) — not enough to judge randomization; unmeasured, not clean");
        }
        var ports = q.Select(x => x.sourcePort).ToList();
        var xids = q.Select(x => x.xid).ToList();
        int distinctPorts = new HashSet<ushort>(ports).Count;
        int distinctXids = new HashSet<ushort>(xids).Count;

        bool fixedPort = distinctPorts == 1;
        bool lowXidEntropy = distinctXids <= Math.Max(1, q.Count / 4);
        bool sequentialXids = IsMostlySequential(xids);

        if (fixedPort || lowXidEntropy || sequentialXids)
        {
            var why = new List<string>();
            if (fixedPort) why.Add($"all {q.Count} queries used one source port");
            if (lowXidEntropy) why.Add($"only {distinctXids} distinct transaction IDs across {q.Count} queries");
            if (sequentialXids) why.Add("transaction IDs are largely sequential");
            return new StubEntropyAudit(StubEntropyStatus.WeakRandomization, SampleCount: q.Count,
                DistinctSourcePorts: distinctPorts, DistinctTransactionIDs: distinctXids,
                Detail: string.Join("; ", why));
        }
        return new StubEntropyAudit(StubEntropyStatus.WellRandomized, SampleCount: q.Count,
            DistinctSourcePorts: distinctPorts, DistinctTransactionIDs: distinctXids,
            Detail: $"{distinctPorts} distinct source ports and {distinctXids} distinct transaction IDs across {q.Count} queries");
    }

    public static StubEntropyAudit Audit(List<DNSCaptureRecord> records)
    {
        var q = new List<(ushort, ushort)>();
        foreach (var rec in records)
        {
            if (!rec.Network.Direction.Contains("outbound", StringComparison.OrdinalIgnoreCase)) continue;
            q.Add((rec.Packet.Xid, rec.Network.SourcePort));
        }
        return Audit(q);
    }

    private static bool IsMostlySequential(List<ushort> values)
    {
        var sorted = new HashSet<ushort>(values).OrderBy(v => v).ToList();
        if (sorted.Count < 4) return false;
        int stepByOne = 0;
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == sorted[i - 1] + 1) stepByOne++;
        }
        return (double)stepByOne / (sorted.Count - 1) >= 0.7;
    }
}
