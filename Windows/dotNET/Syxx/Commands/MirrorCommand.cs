using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx mirror` — vantage comparison. Body moved verbatim from the former SyxxMirror
    /// tool; args[0] is the first real argument (the subcommand token was stripped by the dispatcher).</summary>
    internal static class MirrorCommand
    {
        public static async Task RunAsync(string[] args)
        {
            // B253: shared contract with macOS SyxxMirrorCLI — 0=clean, 1=threat, 2=inconclusive, 3=error.
            if (SyxxConfig.HandleGlobalFlags(args, "syxx mirror", "Usage: syxx mirror <domain> [--local <resolver>]"))
            {
                Environment.ExitCode = 0;
                return;
            }
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: syxx mirror <domain> [--local local_resolver]");
                Environment.ExitCode = 3;
                return;
            }

            string targetDomain = args[0];
            string localResolver = "127.0.0.1";

            // Default to first detected system resolver
            var systemServers = NetworkKeyState.GetSystemDNSServers();
            if (systemServers.Count > 0)
            {
                localResolver = systemServers[0];
            }

            int idx = 1;
            while (idx < args.Length)
            {
                string arg = args[idx];
                if (arg == "--local" || arg == "-l")
                {
                    if (idx + 1 < args.Length)
                    {
                        localResolver = args[idx + 1];
                        idx++;
                    }
                }
                idx++;
            }

            Console.WriteLine($"Comparing resolution for: {targetDomain}");
            Console.WriteLine($"Local resolver: {localResolver}");

            try
            {
                var result = await VantageComparer.CompareVantagePointsAsync(targetDomain, localServer: localResolver);

                static string DescribeOutcome(ResolverQueryOutcome outcome)
                {
                    if (outcome.DidFail)
                    {
                        return $"Query failed: {outcome.Status}" + (string.IsNullOrWhiteSpace(outcome.ErrorMessage) ? "" : $" ({outcome.ErrorMessage})");
                    }
                    string rdataStrs = string.Join(", ", outcome.Records.Select(r => r.Rdata));
                    return string.IsNullOrEmpty(rdataStrs) ? $"No A records ({outcome.Status})" : rdataStrs;
                }

                Console.WriteLine("\nLocal records:");
                if (result.LocalRecords.Count == 0 || result.LocalOutcome.DidFail)
                {
                    Console.WriteLine($"  {DescribeOutcome(result.LocalOutcome)}");
                }
                else
                {
                    foreach (var rec in result.LocalRecords)
                    {
                        Console.WriteLine($"  {rec.Rdata} (TTL: {rec.Ttl})");
                    }
                }

                Console.WriteLine("\nPublic Vantage Points:");
                foreach (var res in result.PublicResolverOutcomes.OrderBy(r => r.Key))
                {
                    string resolverIp = VantageComparer.PublicResolvers.TryGetValue(res.Key, out var ip) ? ip : "";
                    Console.WriteLine($"  {res.Key} (Resolver: {resolverIp}): [{DescribeOutcome(res.Value)}]");
                }

                Console.WriteLine("\n-------------------------------------------");
                if (result.IsInconclusive)
                {
                    Console.WriteLine("⚠️  INCONCLUSIVE: Public vantage points or the local resolver did not return a usable baseline. This is not evidence that the traffic is secure.");
                    Environment.ExitCode = 2;
                }
                else if (result.IsRedirectionDetected)
                {
                    Console.WriteLine("⚠️  WARNING: DNS Tampering/Redirection detected! Local results differ from independent vantage points.");
                    Environment.ExitCode = 1;
                }
                else
                {
                    Console.WriteLine(result.IsDegraded
                        ? "⚠️  DEGRADED: No tampering detected, but one or more vantage points failed or returned no usable records."
                        : "✅  SUCCESS: No DNS tampering detected. Local results match public vantage points.");
                    // Degraded with no tampering is still completed non-threat (0), matching macOS.
                    Environment.ExitCode = 0;
                }
                Console.WriteLine("-------------------------------------------");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error comparing vantage points: {ex.Message}");
                Environment.ExitCode = 3;
            }
        }
    }
}
