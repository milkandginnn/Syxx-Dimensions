using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    internal static class SpeedCommand
    {
        public static async Task RunAsync(string[] args)
        {
            if (SyxxConfig.HandleGlobalFlags(args, "syxx speed",
                "Usage: syxx speed [domain]   (ranks all configured resolvers by latency)")) return;
            // F-14 / D324: explicit --help/-h check to prevent the domain parser from
            // treating "--help" as a domain argument. HandleGlobalFlags above already
            // catches -h/--help when they are the ONLY args, but this also catches the
            // case where -h appears alongside a domain (e.g. "syxx speed google.com -h").
            if (args.Contains("--help") || args.Contains("-h"))
            {
                Console.WriteLine("Usage: syxx speed [domain]   (ranks all configured resolvers by latency)");
                Console.WriteLine("  domain defaults to google.com if omitted");
                Environment.ExitCode = 0;
                return;
            }
            string domain = args.Length >= 1 ? args[0] : "google.com";

            Console.WriteLine($"\n  Resolver Speed Benchmark for: {domain}");
            Console.WriteLine(new string('\u2500', 60));

            var systemServers = NetworkKeyState.GetSystemDNSServers();
            if (systemServers.Count == 0)
            {
                Console.WriteLine("  No system DNS resolvers detected.");
                return;
            }

            var knownPublic = new (string Name, string IP)[]
            {
                ("Cloudflare", "1.1.1.1"),
                ("Google", "8.8.8.8"),
                ("Quad9", "9.9.9.9")
            };

            var allServers = new List<(string Label, string IP)>();
            foreach (var server in systemServers)
                allServers.Add((server, server));
            foreach (var pub in knownPublic)
            {
                if (!systemServers.Contains(pub.IP))
                    allServers.Add(($"{pub.Name} ({pub.IP})", pub.IP));
            }

            Console.WriteLine();
            Console.WriteLine($"  {"Resolver",-38} {"Status",-16} {"Median",-10}  Notes");
            Console.WriteLine(new string('\u00B7', 60));

            static string Pad(string s, int n) => s.Length >= n ? s : s + new string(' ', n - s.Length);

            var results = new List<(string Label, string IP, ResolverLatency Latency)>();

            foreach (var entry in allServers)
            {
                var latency = await ResolverLatencyProbe.Probe(domain, server: entry.IP, samples: 4);
                results.Add((entry.Label, entry.IP, latency));
            }

            results.Sort((a, b) =>
            {
                double aScore = a.Latency.Status is ResolverLatencyStatus.Untested or ResolverLatencyStatus.Unreachable
                    ? double.MaxValue : a.Latency.MedianMs;
                double bScore = b.Latency.Status is ResolverLatencyStatus.Untested or ResolverLatencyStatus.Unreachable
                    ? double.MaxValue : b.Latency.MedianMs;
                if (aScore != bScore) return aScore.CompareTo(bScore);
                return b.Latency.SuccessCount.CompareTo(a.Latency.SuccessCount);
            });

            int rank = 1;
            (string Label, string IP, ResolverLatency Latency)? best = null;
            foreach (var result in results)
            {
                string statusIcon = result.Latency.Status switch
                {
                    ResolverLatencyStatus.Fast => "\u26A1 fast",
                    ResolverLatencyStatus.Moderate => "  ok",
                    ResolverLatencyStatus.Slow => "\uD83D\uDC22 slow",
                    ResolverLatencyStatus.Degraded => "\u26A0\uFE0F degraded",
                    ResolverLatencyStatus.Unreachable => "\u274C unreachable",
                    ResolverLatencyStatus.Untested => "\u2014 untested",
                    _ => "?"
                };
                string medianStr = result.Latency.SuccessCount > 0
                    ? $"{result.Latency.MedianMs:F1} ms"
                    : "\u2014";
                string notes = result.Latency.SuccessCount > 0 && result.Latency.SuccessCount < result.Latency.SampleCount
                    ? $"{result.Latency.SuccessCount}/{result.Latency.SampleCount} OK"
                    : result.Latency.Status == ResolverLatencyStatus.Unreachable
                    ? $"{result.Latency.SampleCount} queries lost"
                    : "";
                Console.WriteLine($"  #{rank} {Pad(result.Label, 38)}{Pad(statusIcon, 16)}{Pad(medianStr, 10)}  {notes}");
                if (result.Latency.Status is ResolverLatencyStatus.Fast or ResolverLatencyStatus.Moderate)
                    best ??= result;
                rank++;
            }

            Console.WriteLine(new string('\u2500', 60));
            if (best.HasValue)
                Console.WriteLine($"  Recommended: {best.Value.Label} (median {best.Value.Latency.MedianMs:F1} ms)");
            Console.WriteLine();
        }
    }
}
