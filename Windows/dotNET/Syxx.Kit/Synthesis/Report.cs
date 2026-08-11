using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/Report.swift. A raw fact several sections
// may observe (your local resolver shows up in Prophet, Mirror, and Portal). The report merges
// them: identical values collapse to one line; a value that DISAGREES between sections is kept and
// flagged, because a datapoint that "changed" between checks is itself meaningful.

public sealed record Datapoint(string Key, string Label, string Value, Section Section);

public sealed record DatapointValue(string Value, IReadOnlyList<Section> Sections);

public sealed record MergedDatapoint(string Key, string Label, IReadOnlyList<DatapointValue> Values)
{
    /// True when sections disagree. For scalar keys (e.g. local_resolver), any value mismatch is a
    /// conflict. For set-valued keys (target_answer), conflict requires non-overlapping sets — a
    /// multi-A/AAAA domain naturally has multiple values and should not false-fire.
    public bool IsConflicting
    {
        get
        {
            if (Values.Count <= 1) return false;
            // Scalar keys: any mismatch is a conflict
            if (Key != "target_answer") return true;
            // Set-valued — B194: conflict when ANY section-set is disjoint from the first
            // (matching macOS Report.swift:48-51). The previous logic inverted the quantifier:
            // it returned "no conflict" as soon as ANY set overlapped, missing disjoint sets
            // later in the list.
            var allSets = Values.Select(v => new HashSet<Section>(v.Sections)).ToList();
            var first = allSets[0];
            for (int i = 1; i < allSets.Count; i++)
                if (!allSets[i].Overlaps(first)) return true;
            return false;
        }
    }
}

public static class DatapointBag
{
    /// De-duplicate by key: identical values collapse (recording every agreeing section); differing
    /// values are preserved as a conflict. First-seen key order is kept so the report is stable.
    public static List<MergedDatapoint> Merge(IReadOnlyList<Datapoint> bag)
    {
        var keyOrder = new List<string>();
        var labels = new Dictionary<string, string>();
        var valuesByKey = new Dictionary<string, List<string>>();
        var sectionsByKeyValue = new Dictionary<string, List<Section>>();

        foreach (var d in bag)
        {
            if (!valuesByKey.ContainsKey(d.Key)) { keyOrder.Add(d.Key); valuesByKey[d.Key] = new(); labels[d.Key] = d.Label; }
            if (!valuesByKey[d.Key].Contains(d.Value)) valuesByKey[d.Key].Add(d.Value);
            var vk = d.Key + "" + d.Value;
            if (!sectionsByKeyValue.TryGetValue(vk, out var secs)) { secs = new(); sectionsByKeyValue[vk] = secs; }
            if (!secs.Contains(d.Section)) secs.Add(d.Section);
        }

        return keyOrder.Select(key =>
        {
            var vals = valuesByKey[key]
                .Select(v => new DatapointValue(v, sectionsByKeyValue.GetValueOrDefault(key + "" + v, new List<Section>())))
                .ToList();
            return new MergedDatapoint(key, labels[key], vals);
        }).ToList();
    }
}

public static class DatapointExtractor
{
    public static List<Datapoint> Extract(SectionInputs inputs)
    {
        var dp = new List<Datapoint>();
        void Add(string key, string label, string value, Section section)
        {
            var v = (value ?? "").Trim();
            if (v.Length > 0) dp.Add(new Datapoint(key, label, v, section));
        }
        string Tri(ConditionState s, string yes, string no) => s == ConditionState.Yes ? yes : (s == ConditionState.No ? no : "unknown");

        if (inputs.Comparison is { } c)
        {
            Add("local_resolver", "Local DNS resolver", c.LocalServer, Section.Mirror);
            foreach (var r in c.LocalRecords.Where(r => r.Type == 1 || r.Type == 28))
                Add("target_answer", "Answer for the tested domain", r.Rdata, Section.Mirror);
        }
        if (inputs.Network is { } n)
        {
            if (n.DnsServers.Count > 0) Add("local_resolver", "Local DNS resolver", n.DnsServers[0], Section.Portal);
            Add("dns_encryption", "DNS encryption", Tri(n.EncryptedDNS.State, "encrypted", "cleartext (Do53)"), Section.Portal);
            Add("vpn", "VPN", Tri(n.Vpn.State, "active", "inactive"), Section.Portal);
            Add("captive_portal", "Captive portal", Tri(n.CaptivePortal.State, "present", "none"), Section.Portal);
        }
        if (inputs.Resolution is { } res)
            foreach (var rec in res.Records.Where(r => r.Type == 1 || r.Type == 28))
                Add("target_answer", "Answer for the tested domain", rec.Rdata, Section.Prophet);
        if (inputs.Dnssec is DNSSECStatus d)
            Add("dnssec", "DNSSEC posture", d.RawValue(), Section.Prophet);
        if (inputs.Leak is { } l)
            foreach (var obs in l.ObservedResolvers)
                Add("observed_resolver", "Resolver that observed your query", obs, Section.Glutton);
        return dp;
    }
}

public sealed record SynthesisReport(Conclusion Conclusion, IReadOnlyList<MergedDatapoint> Datapoints)
{
    public IReadOnlyList<MergedDatapoint> Conflicts => Datapoints.Where(d => d.IsConflicting).ToList();

    public string RenderText()
    {
        var det = Conclusion.Determination;
        var o = new List<string>
        {
            "SYXX — DNS analysis report",
        };

        // Result line from the determination
        if (det is not null)
        {
            // Untested (nothing ran) is a DISTINCT state from Inconclusive (ran, valid data,
            // ambiguous). Collapsing either into the other — or into a bare "UNKNOWN" — is the
            // conflation the engine's CRITICAL DISTINCTION comment forbids. Mirrors macOS
            // Report.swift:135-139 case-for-case.
            var stateStr = det.State switch
            {
                DeterminativeState.Clean => "CLEAN",
                DeterminativeState.ThreatDetected => $"THREAT DETECTED: {string.Join(", ", det.Diagnoses.Select(d => d.DisplayName))}",
                DeterminativeState.Inconclusive => "INCONCLUSIVE",
                DeterminativeState.Untested => "NOT YET TESTED",
                _ => "UNKNOWN",
            };
            o.Add($"Conclusion: {stateStr}");
            if (det.Diagnoses.Count > 0)
            {
                var top = det.Diagnoses[0];
                // B196: render only the display name and confidence rawValue — gate-only
                // engine metrics (fit/coverage) are uncalibrated expert constants and must
                // not be presented as percentages to users.
                o.Add($"  Top diagnosis: {top.DisplayName} ({top.Confidence.ToString().ToLowerInvariant()})");
            }
            if (det.Suppressed.Count > 0)
            {
                o.Add("  Unverified signals:");
                foreach (var s in det.Suppressed)
                    // B196: use rawValue (camelCase) for parity with macOS Report.swift:148-149.
                    o.Add($"    - {s.DisplayName} ({s.Reason.ToString().ToLowerInvariant()})");
            }
        }
        else
        {
            o.Add("Conclusion: UNFINISHED (engine could not evaluate)");
        }

        o.Add("");
        o.Add("FACTS (de-duplicated across all checks)");
        foreach (var d in Datapoints)
        {
            if (d.IsConflicting)
            {
                var variants = string.Join("  |  ", d.Values.Select(v => $"{v.Value} [{string.Join(",", v.Sections.Select(s => s.ToString().ToLowerInvariant()))}]"));
                o.Add($"  ⚠ {d.Label}: DISCREPANCY — {variants}");
            }
            else if (d.Values.Count > 0)
            {
                var only = d.Values[0];
                o.Add($"  • {d.Label}: {only.Value}  [{string.Join(",", only.Sections.Select(s => s.ToString().ToLowerInvariant()))}]");
            }
        }
        if (Conclusion.Evidence.Count > 0)
        {
            o.Add(""); o.Add("WHY");
            o.AddRange(Conclusion.Evidence.Select(e => $"  • [{e.Section.ToString().ToLowerInvariant()}] {e.Observation}"));
        }
        o.Add(""); o.Add("SECTIONS");
        o.AddRange(Conclusion.SectionStatuses.Select(s => $"  • {s.Section.ToString().ToLowerInvariant()}: {s.Status.ToString().ToLowerInvariant()}"));
        // B196: render advisories (EXPOSURE FINDINGS) — parity with macOS Report.swift:176-180.
        if (Conclusion.Advisories.Count > 0)
        {
            o.Add(""); o.Add("EXPOSURE FINDINGS");
            foreach (var a in Conclusion.Advisories)
                o.Add($"  • [{a.AdvisoryId.ToString().ToLowerInvariant()}] {a.Title}: {a.Detail}");
        }
        o.Add(""); o.Add("NEXT STEPS");
        o.Add("  • Review the findings above and verify with a second vantage point if any section is inconclusive.");
        o.Add("  • Re-run Syxx after changing network conditions (VPN, different Wi-Fi) if threats are detected.");
        return string.Join("\n", o);
    }
}

public static class ReportBuilder
{
    /// Full report: synthesize the result AND compile the de-duplicated datapoint view.
    public static SynthesisReport Build(SectionInputs inputs, SynthesisEngine.Config? config = null, DateTimeOffset? now = null)
    {
        var conclusion = Synthesizer.Conclude(inputs, config, now);
        var merged = DatapointBag.Merge(DatapointExtractor.Extract(inputs));
        return new SynthesisReport(conclusion, merged);
    }
}
