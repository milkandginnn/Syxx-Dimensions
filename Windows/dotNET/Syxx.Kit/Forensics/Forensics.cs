// Ported into Syxx.Kit from the sibling LyxTools project (mirrors macOS SyxxKit/Forensics).
// Capture/forensics analyzers: beacon-regularity scoring, ad/tracker blocklist classification,
// and the fused DNS-exfil (X6) result. Timestamps are epoch seconds for parity with the CLI.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit
{
    // ---- Beacon analysis ---------------------------------------------------

    public enum BeaconClassification
    {
        InsufficientData,
        HighlyRegular,
        Regular,
        Irregular
    }

    public static class BeaconClassificationExtensions
    {
        public static string Label(this BeaconClassification c) => c switch
        {
            BeaconClassification.InsufficientData => "Insufficient data",
            BeaconClassification.HighlyRegular => "Highly regular",
            BeaconClassification.Regular => "Regular",
            _ => "Irregular"
        };
    }

    public sealed class BeaconScore
    {
        public string ProcessName { get; }
        public string Destination { get; }
        public IReadOnlyList<double> Intervals { get; }
        public double? MeanInterval { get; }
        public double? CoefficientOfVariation { get; }
        public BeaconClassification Classification { get; }

        public BeaconScore(string processName, string destination, IReadOnlyList<double> intervals,
                           double? meanInterval, double? cv, BeaconClassification classification)
        {
            ProcessName = processName;
            Destination = destination;
            Intervals = intervals;
            MeanInterval = meanInterval;
            CoefficientOfVariation = cv;
            Classification = classification;
        }
    }

    public static class BeaconAnalyzer
    {
        /// <summary>Score query-timing regularity. Timestamps are epoch seconds.</summary>
        public static BeaconScore Score(string processName, string destination, IReadOnlyList<double> timestamps)
        {
            var sorted = timestamps.OrderBy(t => t).ToList();
            var intervals = new List<double>();
            for (int i = 1; i < sorted.Count; i++) intervals.Add(sorted[i] - sorted[i - 1]);

            if (intervals.Count < 2)
                return new BeaconScore(processName, destination, intervals, null, null, BeaconClassification.InsufficientData);

            double mean = intervals.Average();
            // B034 — degenerate case: all identical timestamps → all intervals are zero → mean is zero.
            // A zero-mean interval set is not "highly regular"; it's insufficient data. Mirrors macOS
            // BeaconAnalyzer.swift:53-62 line-for-line (mean nil, cv nil, InsufficientData) rather than
            // mapping cv to 0 and manufacturing a HighlyRegular beacon positive.
            if (mean == 0)
                return new BeaconScore(processName, destination, intervals, null, null, BeaconClassification.InsufficientData);
            double variance = intervals.Select(x => Math.Pow(x - mean, 2)).Sum() / intervals.Count;
            double sd = Math.Sqrt(variance);
            double cv = sd / mean;

            return new BeaconScore(processName, destination, intervals, mean, cv, Classify(cv));
        }

        private static BeaconClassification Classify(double cv)
        {
            if (cv < 0.05) return BeaconClassification.HighlyRegular;
            if (cv < 0.20) return BeaconClassification.Regular;
            return BeaconClassification.Irregular;
        }
    }

    // ---- Blocklists --------------------------------------------------------

    public sealed class NamedDomainList
    {
        public string Name { get; }
        public HashSet<string> Domains { get; }

        public NamedDomainList(string name, IEnumerable<string> domains)
        {
            Name = name;
            Domains = new HashSet<string>(domains);
        }

        public static NamedDomainList FromRawText(string name, string rawText)
        {
            var domains = new HashSet<string>();
            foreach (var line in rawText.Split('\n'))
            {
                var norm = Normalize(line);
                if (norm != null) domains.Add(norm);
            }
            return new NamedDomainList(name, domains);
        }

        /// <summary>True when <paramref name="domain"/> or any parent domain is on this list.</summary>
        public bool Matches(string domain)
        {
            var d = domain.ToLowerInvariant();
            if (Domains.Contains(d)) return true;
            var labels = d.Split('.').ToList();
            while (labels.Count > 2)
            {
                labels.RemoveAt(0);
                if (Domains.Contains(string.Join(".", labels))) return true;
            }
            if (labels.Count == 2 && Domains.Contains(string.Join(".", labels))) return true;
            return false;
        }

        private static string? Normalize(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("!", StringComparison.Ordinal)) return null;

            var withoutInline = trimmed.Split('#', 2)[0];
            var tokens = withoutInline.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var candidate = tokens.Length > 0 ? tokens[^1] : withoutInline;
            var cleaned = candidate
                .Replace("0.0.0.0", "", StringComparison.Ordinal)
                .Replace("127.0.0.1", "", StringComparison.Ordinal)
                .Trim('.', ' ', '\t', '\r', '\n')
                .ToLowerInvariant();
            if (!cleaned.Contains('.', StringComparison.Ordinal)) return null;
            return cleaned;
        }
    }

    public sealed class DomainCoverage
    {
        public string Domain { get; }
        public IReadOnlyList<string> CoveredBy { get; }
        public IReadOnlyList<string> MissingFrom { get; }

        public double CoverageRatio
        {
            get
            {
                int total = CoveredBy.Count + MissingFrom.Count;
                return total == 0 ? 0 : (double)CoveredBy.Count / total;
            }
        }

        public DomainCoverage(string domain, IReadOnlyList<string> coveredBy, IReadOnlyList<string> missingFrom)
        {
            Domain = domain;
            CoveredBy = coveredBy;
            MissingFrom = missingFrom;
        }
    }

    public static class BlocklistAnalyzer
    {
        /// <summary>For each observed domain, the lists that flag it (parent-domain aware).</summary>
        public static List<DomainCoverage> Classify(IEnumerable<string> observed, IReadOnlyList<NamedDomainList> lists)
        {
            var result = new List<DomainCoverage>();
            foreach (var domain in observed)
            {
                var coveredBy = lists.Where(l => l.Matches(domain)).Select(l => l.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var missingFrom = lists.Where(l => !l.Matches(domain)).Select(l => l.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
                result.Add(new DomainCoverage(domain.ToLowerInvariant(), coveredBy, missingFrom));
            }
            return result;
        }

        public static Dictionary<string, Dictionary<string, int>> OverlapMatrix(IReadOnlyList<NamedDomainList> lists)
        {
            var matrix = new Dictionary<string, Dictionary<string, int>>();
            foreach (var lhs in lists)
            {
                var row = new Dictionary<string, int>();
                foreach (var rhs in lists) row[rhs.Name] = lhs.Domains.Intersect(rhs.Domains).Count();
                matrix[lhs.Name] = row;
            }
            return matrix;
        }
    }

    public static class SieveBlocklistLoader
    {
        public static List<NamedDomainList> BundledLists() => new List<NamedDomainList>
        {
            new NamedDomainList("NextDNS Default", BundledDomains("nextdns-default")),
            new NamedDomainList("NextDNS Strict", BundledDomains("nextdns-strict")),
            new NamedDomainList("DNS-layer", BundledDomains("dns-layer")),
        };

        // D081: Distinguish "file load failed" from "file loaded but empty/no matches."
        // A bare `null` return would let a missing/corrupt blocklist file silently reduce
        // the analysis to "not on any list" — the caller must be told the list could not
        // be loaded at all.
        public static (bool success, NamedDomainList? list, string? error) LoadFromPath(string path)
        {
            try
            {
                var raw = System.IO.File.ReadAllText(path);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var list = NamedDomainList.FromRawText(name, raw);
                return (true, list, null);
            }
            catch (Exception ex)
            {
                return (false, null, $"Failed to load blocklist from {path}: {ex.Message}");
            }
        }

        private static string[] BundledDomains(string key) => key switch
        {
            "nextdns-default" => new[]
            {
                "doubleclick.net", "googlesyndication.com", "googleadservices.com",
                "google-analytics.com", "googletagmanager.com", "facebook.com",
                "fbcdn.net", "instagram.com", "snapchat.com", "tiktok.com",
                "adsrvr.org", "adnxs.com", "rubiconproject.com", "criteo.com",
                "criteo.net", "casalemedia.com", "moatads.com", "indexww.com",
                "pubmatic.com", "openx.net", "appnexus.com", "contextweb.com",
                "agkn.com", "sharethis.com", "taboola.com", "outbrain.com",
                "exdynsrv.com", "addthis.com", "scorecardresearch.com",
                "quantserve.com", "comscore.com", "chartbeat.com",
                "newrelic.com", "hotjar.com", "fullstory.com",
                "amplitude.com", "mixpanel.com", "segment.io",
                "optimizely.com", "crazyegg.com", "clicktale.net",
                "mouseflow.com", "luckyorange.com", "sessioncam.com",
                "demdex.net", "krxd.net", "bluekai.com", "exelator.com",
                "tapad.com", "media.net", "adroll.com", "tidaltv.com",
            },
            "nextdns-strict" => new[]
            {
                "doubleclick.net", "googlesyndication.com", "googleadservices.com",
                "google-analytics.com", "googletagmanager.com", "facebook.com",
                "fbcdn.net", "instagram.com", "snapchat.com", "tiktok.com",
                "adsrvr.org", "adnxs.com", "rubiconproject.com", "criteo.com",
                "criteo.net", "casalemedia.com", "moatads.com", "indexww.com",
                "pubmatic.com", "openx.net", "appnexus.com", "contextweb.com",
                "agkn.com", "sharethis.com", "taboola.com", "outbrain.com",
                "exdynsrv.com", "addthis.com", "scorecardresearch.com",
                "quantserve.com", "comscore.com", "chartbeat.com",
                "newrelic.com", "hotjar.com", "fullstory.com",
                "amplitude.com", "mixpanel.com", "segment.io",
                "optimizely.com", "crazyegg.com", "clicktale.net",
                "mouseflow.com", "luckyorange.com", "sessioncam.com",
                "demdex.net", "krxd.net", "bluekai.com", "exelator.com",
                "tapad.com", "media.net", "adroll.com", "tidaltv.com",
                "pinterest.com", "linkedin.com", "twitter.com", "x.com",
                "reddit.com", "youtube.com", "ytimg.com", "googlevideo.com",
                "netflix.com", "nflxvideo.net", "hbomax.com", "disneyplus.com",
                "hulu.com", "amazonaws.com", "cloudfront.net", "akamaihd.net",
            },
            "dns-layer" => new[]
            {
                "doubleclick.net", "googlesyndication.com", "googleadservices.com",
                "google-analytics.com", "googletagmanager.com", "facebook.com",
                "fbcdn.net", "adsrvr.org", "adnxs.com", "rubiconproject.com",
                "criteo.com", "moatads.com", "indexww.com", "pubmatic.com",
                "openx.net", "appnexus.com", "contextweb.com", "agkn.com",
                "scorecardresearch.com", "quantserve.com", "comscore.com",
                "newrelic.com", "hotjar.com", "amplitude.com", "mixpanel.com",
                "demdex.net", "krxd.net", "bluekai.com", "exelator.com",
            },
            _ => Array.Empty<string>()
        };
    }

    // ---- Exfil analysis (derivative datapoint X6) --------------------------

    public enum ExfilSuspicion
    {
        Inconclusive,
        Benign,
        Suspicious,
        LikelyExfil
    }

    public static class ExfilSuspicionExtensions
    {
        public static string Label(this ExfilSuspicion s) => s switch
        {
            ExfilSuspicion.Inconclusive => "Inconclusive",
            ExfilSuspicion.Benign => "Benign",
            ExfilSuspicion.Suspicious => "Suspicious",
            _ => "Likely DNS exfiltration / tunneling"
        };
    }

    public sealed class ExfilAssessment
    {
        public string Domain { get; }
        public string LongestLabel { get; }
        public int LabelLength { get; }
        public double EntropyBitsPerChar { get; }
        public BeaconClassification Beacon { get; }
        /// <summary>
        /// B190: tri-state for the beacon leg. <c>false</c> means the cadence signal was NEVER
        /// evaluated (no per-domain timing data supplied), distinct from an evaluated negative
        /// ("no beacon observed"). When <c>false</c>, <see cref="Beacon"/> carries
        /// <see cref="BeaconClassification.InsufficientData"/> as a placeholder and MUST NOT be read
        /// as an evaluated negative: a beaconing-only channel cannot be ruled out. The fusion treats
        /// an unevaluated beacon as an absent signal, so the max achievable signal count is 2.
        /// </summary>
        public bool BeaconEvaluated { get; }
        public IReadOnlyList<string> FlaggedByLists { get; }
        public ExfilSuspicion Suspicion { get; }
        public IReadOnlyList<string> Rationale { get; }

        public ExfilAssessment(string domain, string longestLabel, int labelLength, double entropy,
                               BeaconClassification beacon, bool beaconEvaluated, IReadOnlyList<string> flaggedByLists,
                               ExfilSuspicion suspicion, IReadOnlyList<string> rationale)
        {
            Domain = domain;
            LongestLabel = longestLabel;
            LabelLength = labelLength;
            EntropyBitsPerChar = entropy;
            Beacon = beacon;
            BeaconEvaluated = beaconEvaluated;
            FlaggedByLists = flaggedByLists;
            Suspicion = suspicion;
            Rationale = rationale;
        }
    }

    public static class ExfilAnalyzer
    {
        /// <summary>Shannon entropy in bits per character.</summary>
        public static double ShannonEntropyBitsPerChar(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var counts = new Dictionary<char, int>();
            foreach (var c in s) counts[c] = counts.TryGetValue(c, out var v) ? v + 1 : 1;
            double n = s.Length;
            double h = 0;
            foreach (var kv in counts)
            {
                double p = kv.Value / n;
                h -= p * Math.Log2(p);
            }
            return h;
        }

        /// <summary>Longest label outside the registrable domain (the tunneling payload space).</summary>
        public static string LongestSubdomainLabel(string domain)
        {
            var labels = domain.ToLowerInvariant().Split('.');
            if (labels.Length <= 2) return "";
            return labels.Take(labels.Length - 2).OrderByDescending(l => l.Length).FirstOrDefault() ?? "";
        }

        public static ExfilAssessment Assess(string domain, IReadOnlyList<double>? timestamps = null,
                                             IReadOnlyList<NamedDomainList>? lists = null)
        {
            timestamps ??= Array.Empty<double>();
            lists ??= SieveBlocklistLoader.BundledLists();

            var trimmed = domain.Trim();
            var longest = LongestSubdomainLabel(trimmed);
            var entropy = ShannonEntropyBitsPerChar(longest);
            // B190: distinguish "beacon leg never evaluated" (no timing data) from an evaluated negative.
            // With no timestamps we do not attempt cadence scoring — reporting InsufficientData here
            // would masquerade as "evaluated, no beacon"; the BeaconEvaluated flag keeps the honest
            // tri-state (unknown) intact for downstream consumers.
            bool beaconEvaluated = timestamps.Count > 0;
            var beacon = beaconEvaluated
                ? BeaconAnalyzer.Score("", trimmed, timestamps).Classification
                : BeaconClassification.InsufficientData;
            // L3 — the beacon leg only produced a CONCLUSIVE reading when it was evaluated AND scoring
            // succeeded (not InsufficientData: <3 timing samples or a degenerate/zero-mean series). Only a
            // conclusive leg may back the "no beaconing signal" clean claim below.
            bool beaconConclusive = beaconEvaluated && beacon != BeaconClassification.InsufficientData;
            var flaggedBy = lists.Where(l => l.Matches(trimmed)).Select(l => l.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();

            bool highEntropy = longest.Length >= 20 && entropy >= 3.5;
            // An unevaluated beacon can never be a positive `regular` signal (missing data is not a finding).
            bool regular = beaconEvaluated && (beacon == BeaconClassification.HighlyRegular || beacon == BeaconClassification.Regular);

            var rationale = new List<string>();
            if (highEntropy)
                rationale.Add($"high-entropy {longest.Length}-char label ({entropy:F2} bits/char) — consistent with encoded data");
            if (regular)
                rationale.Add($"regular query cadence ({beacon.Label().ToLowerInvariant()}) — consistent with automated beaconing");
            if (!beaconEvaluated)
                rationale.Add("beacon cadence not evaluated (no per-domain timing data)");
            else if (!beaconConclusive)
                // L3 — timing data existed but was too sparse/degenerate to score; disclose factually rather
                // than letting the benign line claim "no beaconing signal" over an inconclusive beacon leg.
                rationale.Add("beacon cadence not evaluated (insufficient timing samples)");
            if (flaggedBy.Count > 0)
                rationale.Add($"registrable domain flagged by: {string.Join(", ", flaggedBy)}");

            // C009: require at least two of {highEntropy, regular, flaggedBy} for suspicious.
            // A single signal (entropy-only or beacon-only) is insufficient and causes false positives.
            // B190: when the beacon leg is unevaluated, `regular` is forced false above, so the maximum
            // achievable signalCount is 2 of 3 — LikelyExfil (>= 3) is honestly unreachable rather than
            // the leg silently contributing an evaluated negative.
            int signalCount = (highEntropy ? 1 : 0) + (regular ? 1 : 0) + (flaggedBy.Count > 0 ? 1 : 0);

            ExfilSuspicion suspicion;
            if (trimmed.Length == 0)
                suspicion = ExfilSuspicion.Inconclusive;
            else if (signalCount >= 3)
                suspicion = ExfilSuspicion.LikelyExfil;
            else if (signalCount >= 2)
                suspicion = ExfilSuspicion.Suspicious;
            else
            {
                suspicion = ExfilSuspicion.Benign;
                // Do not claim the beacon leg saw nothing when it was never CONCLUSIVELY checked (unevaluated,
                // or evaluated but insufficient/degenerate). L3: the "or beaconing" clause requires a
                // conclusive beacon leg; otherwise the clean claim covers only the encoded-label check.
                rationale.Add(beaconConclusive ? "no encoded-label or beaconing signal" : "no encoded-label signal");
            }

            return new ExfilAssessment(trimmed, longest, longest.Length, entropy, beacon, beaconEvaluated, flaggedBy, suspicion, rationale);
        }
    }
}
