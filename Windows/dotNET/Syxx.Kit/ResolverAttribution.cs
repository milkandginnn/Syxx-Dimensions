using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

/// <summary>
/// C7 — Per-app resolver attribution (macOS ResolverAttribution twin). Joins the process and
/// resolver a capture already holds but never combines, exposing an app with a HARDCODED resolver
/// that ignores the system's DNS settings.
///
/// D063 coverage caveat (disclosed, never implied away): the resolver is keyed on the PORT-53 side
/// (the QR bit is forgeable, the port is not); capture is UDP:53 only, so encrypted/non-53 DNS is
/// invisible and "no bypass observed" means "none over cleartext UDP", not "none at all"; an
/// unattributable packet stays "unknown", never guessed.
/// </summary>
public static class ResolverAttribution
{
    public sealed record Attribution(
        string Process,
        IReadOnlyList<string> Resolvers,
        IReadOnlyList<string> OffSystemResolvers,
        bool UsesHardcodedResolver);

    public sealed record Report(IReadOnlyList<Attribution> Attributions, bool BaselineUnknown)
    {
        public IReadOnlyList<Attribution> Flagged => Attributions.Where(a => a.UsesHardcodedResolver).ToList();
    }

    /// Pure core: classify per-process bypass against the expected system-resolver set. When the
    /// set is EMPTY, no address can be judged off-system → nothing flagged and BaselineUnknown set.
    public static Report Classify(IReadOnlyDictionary<string, HashSet<string>> usage, ISet<string> systemResolvers)
    {
        bool baselineUnknown = systemResolvers.Count == 0;
        var attributions = new List<Attribution>();
        foreach (var process in usage.Keys.OrderBy(k => k, System.StringComparer.Ordinal))
        {
            var resolvers = usage[process];
            var offSystem = baselineUnknown
                ? new List<string>()
                : resolvers.Where(r => !systemResolvers.Contains(r)).OrderBy(r => r, System.StringComparer.Ordinal).ToList();
            attributions.Add(new Attribution(
                process,
                resolvers.OrderBy(r => r, System.StringComparer.Ordinal).ToList(),
                offSystem,
                offSystem.Count > 0));
        }
        return new Report(attributions, baselineUnknown);
    }

    /// Build the per-process resolver usage map from capture records. Resolver = the port-53 side
    /// of each exchange (D063 — NOT the QR bit); an unnamed process is bucketed as "unknown".
    public static Dictionary<string, HashSet<string>> Usage(IEnumerable<DNSCaptureRecord> records)
    {
        var usage = new Dictionary<string, HashSet<string>>();
        foreach (var r in records)
        {
            string process = r.Process.Name ?? "unknown";
            string resolver = r.Network.SourcePort == 53 ? r.Network.SourceIP : r.Network.DestIP;
            if (string.IsNullOrEmpty(resolver)) continue;
            if (!usage.TryGetValue(process, out var set)) { set = new HashSet<string>(); usage[process] = set; }
            set.Add(resolver);
        }
        return usage;
    }
}
