using System;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx key` — network DNS/key state. Body moved verbatim from the former SyxxKey tool;
    /// args[0] is the first real argument (the subcommand token was stripped by the dispatcher).</summary>
    internal static class KeyCommand
    {
        public static async Task RunAsync(string[] args)
        {
            if (SyxxConfig.HandleGlobalFlags(args, "syxx key",
                "Usage: syxx key   (reports resolver / interface / VPN / captive-portal / encrypted-DNS state)")) return;
            Console.WriteLine("Gathering resolver, proxy, interface, VPN, and captive-portal state...");

            var report = await NetworkKeyState.GenerateReportAsync();

            Console.WriteLine("\n--- Network DNS & Key State ---");
            Console.WriteLine("System DNS Resolvers:");
            if (report.DnsServers.Count == 0)
            {
                Console.WriteLine("  None detected");
            }
            else
            {
                foreach (var server in report.DnsServers)
                {
                    Console.WriteLine($"  - {server}");
                }
            }

            if (report.ScopedResolvers.Count > 0)
            {
                Console.WriteLine("\nScoped/Supplemental Resolvers:");
                foreach (var resolver in report.ScopedResolvers)
                {
                    string descriptor = resolver.Address;
                    if (resolver.Interface != null) descriptor += $" (interface: {resolver.Interface})";
                    if (resolver.IsScoped) descriptor += " [scoped]";
                    if (resolver.MatchDomains.Count > 0) descriptor += $" [domains: {string.Join(", ", resolver.MatchDomains)}]";
                    Console.WriteLine($"  - {descriptor}");
                }
            }

            Console.WriteLine("\nActive Interfaces:");
            foreach (var iface in report.ActiveInterfaces)
            {
                Console.WriteLine($"  - {iface}");
            }

            Console.WriteLine("\nVPN Tunnel Status:");
            Console.WriteLine($"  {Describe(report.Vpn.State, "🔴 ACTIVE", "🟢 INACTIVE", "❓ UNKNOWN")}");
            Console.WriteLine($"  Evidence: {report.Vpn.Evidence}");

            Console.WriteLine("\nCaptive Portal Status:");
            Console.WriteLine($"  {Describe(report.CaptivePortal.State, "⚠️  DETECTED", "✅ CLEAR", "❓ UNKNOWN")}");
            Console.WriteLine($"  Evidence: {report.CaptivePortal.Evidence}");

            Console.WriteLine("\nEncrypted DNS Status:");
            Console.WriteLine($"  {Describe(report.EncryptedDNS.State, "🔒 ENCRYPTED", "🔓 UNENCRYPTED", "❓ UNKNOWN")}");
            Console.WriteLine($"  Evidence: {report.EncryptedDNS.Evidence}");

            // C1 — gateway/router audit. The detail string is the probe's own composed evidence;
            // a failed detection prints its honest reason, never a fabricated gateway.
            Console.WriteLine("\nGateway / Router:");
            Console.WriteLine($"  {(report.Gateway is { } gw ? gw.Detail : "Not probed in this run.")}");
        }

        static string Describe(ConditionState state, string yes, string no, string unknown) => state switch
        {
            ConditionState.Yes => yes,
            ConditionState.No => no,
            _ => unknown
        };
    }
}
