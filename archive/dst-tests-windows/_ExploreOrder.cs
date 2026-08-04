using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

public sealed class _ExploreOrder
{
    private static SectionStatusEntry SE(Section s, SectionStatus st) => new(s, st);

    // Build a family of fixtures with 3+ supports each, varying which sections are clean/positive
    // and which threat evidence is present, to hunt for a combine-order-sensitive rounded output.
    private static IEnumerable<(string id, List<EvidenceItem> ev, List<SectionStatusEntry> st)> Fixtures()
    {
        var threatEv = new (string tag, EvidenceId id, Section sec, string? src)[]
        {
            ("nx", EvidenceId.NxCanaryHijack, Section.Prophet, "canaries"),
            ("dnssec", EvidenceId.DnssecCanaryStripped, Section.Prophet, "canaries"),
            ("redir", EvidenceId.Redirection, Section.Mirror, null),
        };
        var sections = Enum.GetValues<Section>().ToArray();
        // vary the number of clean (Negative) sections 2..6 and whether Prophet/Mirror are Positive
        for (int cleanCount = 2; cleanCount <= 6; cleanCount++)
        for (int threatMask = 1; threatMask < 8; threatMask++)
        {
            var ev = new List<EvidenceItem>();
            for (int i = 0; i < threatEv.Length; i++)
                if ((threatMask & (1 << i)) != 0)
                    ev.Add(EvidenceItem.Create(threatEv[i].sec, threatEv[i].id, "o", "r", threatEv[i].src));
            var st = new List<SectionStatusEntry>();
            for (int i = 0; i < sections.Length; i++)
            {
                var sec = sections[i];
                bool posProphet = sec == Section.Prophet && ev.Any(e => e.Section == Section.Prophet);
                bool posMirror = sec == Section.Mirror && ev.Any(e => e.Section == Section.Mirror);
                if (posProphet || posMirror) st.Add(SE(sec, SectionStatus.Positive));
                else st.Add(SE(sec, i < cleanCount ? SectionStatus.Negative : SectionStatus.Untested));
            }
            yield return ($"c{cleanCount}_t{threatMask}", ev, st);
        }
    }

    private static string Row(Conclusion c)
    {
        var bel = string.Join(",", c.Belief.Select(b => $"{(int)Math.Round(b.Probability*10000)}"));
        return $"{c.Diagnosis}|{c.Confidence}|{c.ReferralTriggered}|{bel}|unc={c.Uncertainty:R}|k={c.Conflict:R}";
    }

    [Fact]
    public void Sweep()
    {
        var lines = new List<string>();
        foreach (var (id, ev, st) in Fixtures())
        {
            var c = SynthesisEngine.Synthesize(ev, st);
            lines.Add($"{id} :: {Row(c)}");
        }
        var tag = Environment.GetEnvironmentVariable("SWEEP_TAG") ?? "normal";
        System.IO.File.WriteAllLines($"/tmp/sweep_{tag}.txt", lines);
    }
}
