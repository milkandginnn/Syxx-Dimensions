using System;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx filter` — C2 filtering verification (macOS SyxxFilter twin). Verifies a DNS
    /// blocklist (Pi-hole / NextDNS / AdGuard / router) is actually enforcing, and whether it can
    /// be bypassed. Tri-state honest: an unreadable probe never reads as a working filter.</summary>
    internal static class FilterCommand
    {
        public static async Task RunAsync(string[] args)
        {
            if (SyxxConfig.HandleGlobalFlags(args, "syxx filter",
                "Usage: syxx filter [@resolver]   (verifies DNS blocklist enforcement + bypass paths)")) return;

            string resolver = args.FirstOrDefault(a => a.StartsWith('@'))?.Substring(1)
                              ?? NetworkKeyState.GetSystemDNSServers().FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(resolver))
            {
                Console.WriteLine("[INCONCLUSIVE] No system resolver could be determined and none was given (@resolver). The filter check did not run.");
                return;
            }

            Console.WriteLine($"Probing blocklist enforcement via {resolver}...");
            var result = await FilteringProbe.ProbeAsync(resolver);

            Console.WriteLine("\n--- DNS Filtering Verification ---");
            foreach (var domain in FilteringProbe.ProbeDomains)
            {
                var outcome = result.PerDomain.TryGetValue(domain, out var o) ? o : FilteringProbe.DomainOutcome.Unknown;
                string label = outcome switch
                {
                    FilteringProbe.DomainOutcome.Sinkholed => "[BLOCKED]   filter acted",
                    FilteringProbe.DomainOutcome.Resolved => "[RESOLVED]  got through — the filter did not act",
                    _ => "[NO READING] query failed — not evidence either way",
                };
                Console.WriteLine($"  {domain}: {label}");
            }

            Console.WriteLine("\nEnforcement:");
            Console.WriteLine(result.Enforcement switch
            {
                FilteringProbe.Enforcement.Active => "  [ACTIVE] Every readable probe came back sinkholed — a DNS filter is enforcing on this resolver.",
                FilteringProbe.Enforcement.Inactive => "  [NOT FILTERING] Every readable probe resolved publicly. No DNS filtering was observed on this resolver.",
                FilteringProbe.Enforcement.Partial => "  [LEAKY] Some probes were blocked, some got through. The blocklist is enforcing but incomplete for these domains.",
                _ => "  [INCONCLUSIVE] No probe produced a readable answer. Whether a filter is working could not be determined.",
            });

            Console.WriteLine("\nBypass paths:");
            Console.WriteLine(result.DirectBypassOpen switch
            {
                ConditionState.Yes => "  [OPEN] A direct query to 8.8.8.8 over plain UDP:53 resolved a blocked domain — an app with a hardcoded resolver can bypass the filter.",
                ConditionState.No => "  [COVERED] Direct external UDP:53 queries are blocked or also filtered — hardcoded-resolver apps cannot slip past.",
                _ => "  [NO READING] The direct-resolver canary did not complete; the bypass question is unanswered.",
            });

            var doh = await EncryptedTransportProbe.Probe();
            Console.WriteLine(doh.Status switch
            {
                EncryptedTransportStatus.Working => "  [OPEN] A public DoH endpoint is reachable — a browser using its own encrypted DNS can sidestep the filter.",
                EncryptedTransportStatus.Blocked => "  [COVERED] Public DoH is blocked on this network — browsers cannot silently sidestep the filter that way.",
                _ => "  [NO READING] The DoH sidestep check did not complete; that path is unverified.",
            });
            Console.WriteLine($"\n{result.Detail}");
        }
    }
}
