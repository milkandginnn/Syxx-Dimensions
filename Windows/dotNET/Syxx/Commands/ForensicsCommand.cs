using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx forensics` — DNS log/beacon/exfil analysis. Body moved verbatim from the former
    /// SyxxForensics tool (which was synchronous); wrapped in a Task-returning RunAsync so the
    /// dispatcher can await it uniformly. args[0] is the first real argument.</summary>
    internal static class ForensicsCommand
    {
        public static Task RunAsync(string[] args)
        {
            Run(args);
            return Task.CompletedTask;
        }

        static void Run(string[] args)
        {
            // E012: emit a diagnostic when UTF-8 encoding fails on legacy console hosts so
            // mojibake in evidence output (IDN domains, dashes, arrows) is explainable.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warning] Console UTF-8 encoding unavailable: {ex.Message}");
            }

            if (SyxxConfig.HandleGlobalFlags(args, "syxx forensics",
                "Usage: syxx forensics [--lists] [--classify <domain...>] [--exfil <domain> [--times t1,t2,...]] [--beacon t1,t2,...]")) return;

            if (args.Length < 1) { PrintUsage(); return; }

            switch (args[0])
            {
                case "--lists": ShowLists(); break;
                case "--classify": Classify(args.Skip(1).ToArray()); break;
                case "--beacon": Beacon(args.Skip(1).ToArray()); break;
                case "--exfil": Exfil(args.Skip(1).ToArray()); break;
                case "--analyze": Analyze(args.Skip(1).ToArray()); break;
                default: PrintUsage(); break;
            }
        }

        static void ShowLists()
        {
            Console.WriteLine("Bundled ad/tracker blocklists:");
            foreach (var list in SieveBlocklistLoader.BundledLists())
                Console.WriteLine($"  {list.Name}: {list.Domains.Count} domains");
        }

        static void Classify(string[] domains)
        {
            if (domains.Length == 0) { Console.WriteLine("Provide one or more domains to classify."); return; }
            var lists = SieveBlocklistLoader.BundledLists();
            var coverage = BlocklistAnalyzer.Classify(domains, lists);
            Console.WriteLine($"Domain classification against {lists.Count} bundled lists:");
            foreach (var c in coverage)
            {
                if (c.CoveredBy.Count == 0)
                    Console.WriteLine($"  {c.Domain}: not on any bundled list");
                else
                    Console.WriteLine($"  {c.Domain}: flagged by {string.Join(", ", c.CoveredBy)}");
            }
        }

        static void Beacon(string[] parts)
        {
            var times = ParseTimes(parts.FirstOrDefault() ?? "");
            if (times.Count < 2) { Console.WriteLine("Provide at least 2 comma-separated epoch timestamps."); return; }
            var score = BeaconAnalyzer.Score("cli", "cli", times);
            Console.WriteLine($"Beacon classification: {score.Classification.Label()}");
            if (score.MeanInterval.HasValue) Console.WriteLine($"  mean interval: {score.MeanInterval.Value:F2}s");
            if (score.CoefficientOfVariation.HasValue) Console.WriteLine($"  coefficient of variation: {score.CoefficientOfVariation.Value:F4}");
        }

        static void Exfil(string[] parts)
        {
            if (parts.Length == 0) { Console.WriteLine("Provide a domain to assess."); return; }
            string domain = parts[0];
            var times = new List<double>();
            int tIdx = Array.IndexOf(parts, "--times");
            if (tIdx >= 0 && tIdx + 1 < parts.Length) times = ParseTimes(parts[tIdx + 1]);

            var a = ExfilAnalyzer.Assess(domain, times);
            Console.WriteLine($"Exfil assessment for {a.Domain}:");
            Console.WriteLine($"  result: {a.Suspicion.Label()}");
            Console.WriteLine($"  longest subdomain label: {a.LabelLength} chars, {a.EntropyBitsPerChar:F2} bits/char");
            Console.WriteLine($"  cadence: {a.Beacon.Label()}");
            if (a.FlaggedByLists.Count > 0) Console.WriteLine($"  blocklists: {string.Join(", ", a.FlaggedByLists)}");
            foreach (var r in a.Rationale) Console.WriteLine($"  • {r}");
        }

        static void Analyze(string[] parts)
        {
            if (parts.Length == 0)
            {
                Console.WriteLine("Provide a capture query log or endpoint activity log to analyze.");
                return;
            }
            string path = parts[0];
            // Endpoint activity logs can be gigabytes — detect + stream by path.
            var endpoint = EndpointLogAnalyzer.Analyze(path);
            if (endpoint != null) { Console.WriteLine(endpoint); return; }
            // Capture query logs are small; safe to read whole.
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception ex) { Console.WriteLine($"Could not read file: {ex.Message}"); return; }
            var records = CaptureLogParser.Parse(text);
            if (records.Count == 0)
            {
                Console.WriteLine($"No records parsed from {path}. (Expected a capture query log or endpoint activity log.)");
                return;
            }
            Console.WriteLine(DNSLogAnalyzer.Render(DNSLogAnalyzer.Analyze(records), path));
        }

        static List<double> ParseTimes(string s)
        {
            var result = new List<double>();
            foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    result.Add(v);
            return result;
        }

        static void PrintUsage()
        {
            Console.WriteLine(@"Usage: syxx forensics <command>

Commands:
  --lists                          Show bundled ad/tracker blocklists and their sizes
  --classify <domain...>           Flag domains that appear on a bundled blocklist
  --beacon t1,t2,t3,...            Score query-timing regularity (epoch seconds)
  --exfil <domain> [--times t1,t2,...]
                                   Fused DNS-tunneling assessment (entropy + cadence + lists)
  --analyze <logfile>              Translate a capture query log into a full report
                                   (processes, record types, Rcodes, tunneling candidates)

Examples:
  syxx forensics --classify doubleclick.net example.com
  syxx forensics --beacon 1000,1060,1120,1180
  syxx forensics --exfil aGVsbG8gd29ybGQ.tunnel.evil.net --times 1000,1060,1120");
        }
    }
}
