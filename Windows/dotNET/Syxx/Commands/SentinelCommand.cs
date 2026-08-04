using System;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx sentinel` — DNS-config drift detection. Body moved verbatim from the former
    /// SyxxSentinel tool (which was synchronous); wrapped in a Task-returning RunAsync so the
    /// dispatcher can await it uniformly. args[0] is the first real argument.</summary>
    internal static class SentinelCommand
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

            if (SyxxConfig.HandleGlobalFlags(args, "syxx sentinel",
                "Usage: syxx sentinel [--reset] [--forget] [--set-home] [--home]   (detect changes to your DNS-relevant network configuration; compare against your home network)")) return;

            if (args.Contains("--forget"))
            {
                Sentinel.Forget();
                HomeBaseline.Forget();
                new AnswerLedger().Forget();
                // E083: resolve the actual path instead of showing a Windows-only environment
                // variable token that Linux users cannot find.
                string syxxDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".syxx");
                Console.WriteLine($"🧹 Forgotten: Sentinel baseline, home baseline, and the passive-DNS answer ledger erased ({syxxDir}).");
                return;
            }

            var current = NetworkConfigSnapshot.Capture();

            Console.WriteLine("Current DNS configuration:");
            Console.WriteLine($"  resolvers          : {(current.Resolvers.Count == 0 ? "(none detected)" : string.Join(", ", current.Resolvers))}");
            Console.WriteLine($"  active interfaces  : {string.Join(", ", current.ActiveInterfaces)}");
            Console.WriteLine($"  VPN active         : {(current.VpnActive == ConditionState.Yes ? "yes" : "no")}");
            Console.WriteLine($"  DNS encrypted      : {(current.DnsEncrypted ? "yes" : "no")}");

            // C5 — home network baseline.
            if (args.Contains("--set-home"))
            {
                HomeBaseline.Save(current);
                Console.WriteLine("\n🏠 Saved this network as your home baseline. On other networks, run `syxx sentinel --home` to see how they differ.");
                return;
            }
            if (args.Contains("--home"))
            {
                var (homeLoadResult, homeBaseline) = HomeBaseline.Load();
                if (homeLoadResult == AnswerLedger.TrustLoadResult.Corrupted)
                {
                    Console.WriteLine("\n⚠️  The home baseline file (~/.syxx/home-baseline.json) appears to be corrupted and cannot be read.");
                    Console.WriteLine("    Please check the file or re-run `syxx sentinel --set-home` on your home network to recreate it.");
                    return;
                }
                var comparison = HomeBaseline.Compare(homeBaseline, current);
                if (!comparison.HasBaseline)
                {
                    Console.WriteLine($"\n{comparison.Summary}");
                }
                else if (comparison.MatchesHome)
                {
                    Console.WriteLine($"\n🏠 {comparison.Summary}");
                }
                else
                {
                    Console.WriteLine($"\n🏠 {comparison.Summary}");
                    foreach (var c in comparison.Differences)
                        Console.WriteLine($"  • {c.Kind.Label()}: {c.Detail}");
                }
                return;
            }

            if (args.Contains("--reset"))
            {
                Sentinel.SaveSnapshot(current);
                Console.WriteLine("\nBaseline reset. Future runs will compare against this snapshot.");
                return;
            }

            var (loadResult, previous) = Sentinel.LoadSnapshot();
            if (loadResult == AnswerLedger.TrustLoadResult.Corrupted)
            {
                Console.WriteLine("\n⚠️  The saved baseline file (~/.syxx/sentinel-snapshot.json) appears to be corrupted and cannot be read.");
                Console.WriteLine("    The file has been left intact. Run `syxx sentinel --reset` to establish a new baseline.");
                return;
            }
            if (previous == null)
            {
                Sentinel.SaveSnapshot(current);
                Console.WriteLine("\nNo prior baseline — saved this snapshot as the baseline. Run again after your network changes.");
                return;
            }

            var changes = Sentinel.Diff(previous, current);
            if (changes.Count == 0)
            {
                // C149: drop "stable" and the green check — a no-change observation is not a
                // clean conclusion; it only describes what Syxx could read on this run.
                Console.WriteLine("\nNo change to the DNS configuration Syxx can read, since the last run.");
            }
            else
            {
                Console.WriteLine("\n🚨 Configuration change detected — you should re-run tampering/leak checks:");
                foreach (var c in changes)
                    Console.WriteLine($"  • {c.Kind.Label()}: {c.Detail}");
                Console.WriteLine("\n  Suggested: syxx (Option A vantage compare), syxx leak --collector <url>, syxx lan");
            }
            // Roll the baseline forward so each run reports deltas since the previous run.
            Sentinel.SaveSnapshot(current);
        }
    }
}
