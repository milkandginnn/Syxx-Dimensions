using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Syxx.Kit;

public static class MonitorReportAnalyzer
{
    public sealed record Analysis(
        int RecordCount,
        int OutboundQueries,
        int InboundResponses,
        StubEntropyAudit StubEntropy,
        ResponseSizeAudit ResponseSize,
        Conclusion Conclusion);

    public static List<DNSCaptureRecord> ParseNDJSON(string text)
    {
        var records = new List<DNSCaptureRecord>();
        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.Length == 0) continue;
            try
            {
                var rec = JsonSerializer.Deserialize<DNSCaptureRecord>(s);
                if (rec is not null) records.Add(rec);
            }
            catch { }
        }
        return records;
    }

    public static SectionInputs SectionInputsFrom(List<DNSCaptureRecord> records)
    {
        var inputs = new SectionInputs();
        inputs.StubEntropy = StubEntropyProbe.Audit(records);
        inputs.ResponseSize = ResponseSizeProbe.Audit(records);
        inputs.Exfil = ExfilAssessments(records);
        return inputs;
    }

    public static Analysis Analyze(string ndjson, DateTimeOffset? now = null)
    {
        var records = ParseNDJSON(ndjson);
        var inputs = SectionInputsFrom(records);
        int outbound = records.Count(r => r.Network.Direction.Contains("outbound", StringComparison.OrdinalIgnoreCase));
        return new Analysis(
            RecordCount: records.Count,
            OutboundQueries: outbound,
            InboundResponses: records.Count - outbound,
            StubEntropy: inputs.StubEntropy ?? StubEntropyAudit.Untested,
            ResponseSize: inputs.ResponseSize ?? ResponseSizeAudit.Untested,
            Conclusion: Synthesizer.Conclude(inputs, now: now));
    }

    internal static List<ExfilAssessment>? ExfilAssessments(List<DNSCaptureRecord> records)
    {
        var byDomain = new Dictionary<string, List<DateTime>>();
        var order = new List<string>();
        foreach (var rec in records)
        {
            if (!rec.Network.Direction.Contains("outbound", StringComparison.OrdinalIgnoreCase)) continue;
            if (rec.Packet.Questions.Count == 0) continue;
            var q = rec.Packet.Questions[0].QuestionName;
            if (string.IsNullOrEmpty(q)) continue;
            if (!byDomain.ContainsKey(q)) { byDomain[q] = []; order.Add(q); }
            var ts = ParseTimestamp(rec.Timestamp);
            if (ts.HasValue) byDomain[q].Add(ts.Value);
        }
        if (order.Count == 0) return null;
        return order.Select(d =>
        {
            var timestamps = byDomain[d];
            return ExfilAnalyzer.Assess(d, timestamps.Select(t => (t - DateTime.UnixEpoch).TotalSeconds).ToList());
        }).ToList();
    }

    private static DateTime? ParseTimestamp(string s)
    {
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;
        return null;
    }
}
