using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx leak` — VPN DNS leak test. Body moved verbatim from the former SyxxLeak tool;
    /// args[0] is the first real argument (the subcommand token was stripped by the dispatcher).</summary>
    internal static class LeakCommand
    {
        public static async Task RunAsync(string[] args)
        {
            // E012: emit a diagnostic when UTF-8 encoding fails on legacy console hosts so
            // mojibake in evidence output (IDN domains, dashes, arrows) is explainable.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warning] Console UTF-8 encoding unavailable: {ex.Message}");
            }
            if (SyxxConfig.HandleGlobalFlags(args, "syxx leak",
                "Usage: syxx leak [--domain <base>] [--servers <ip...>] [--collector <url>] [--token <bearer>] [--key <license>]")) return;
            string baseDomain = "leak.syxx.app";
            var servers = NetworkKeyState.GetSystemDNSServers();
            string? collectorURL = null;
            string? authToken = null;
            string? licenseKey = null;

            int idx = 0;
            while (idx < args.Length)
            {
                string arg = args[idx];
                if (arg == "--domain" || arg == "-d")
                {
                    if (idx + 1 < args.Length)
                    {
                        baseDomain = args[idx + 1];
                        idx++;
                    }
                }
                else if (arg == "--servers" || arg == "-s")
                {
                    servers.Clear();
                    while (idx + 1 < args.Length && !args[idx + 1].StartsWith("-"))
                    {
                        servers.Add(args[idx + 1]);
                        idx++;
                    }
                }
                else if (arg == "--collector" || arg == "-c")
                {
                    if (idx + 1 < args.Length)
                    {
                        collectorURL = args[idx + 1];
                        idx++;
                    }
                }
                else if (arg == "--token" || arg == "-t")
                {
                    if (idx + 1 < args.Length)
                    {
                        authToken = args[idx + 1];
                        idx++;
                    }
                }
                else if (arg == "--key" || arg == "-k")
                {
                    if (idx + 1 < args.Length)
                    {
                        licenseKey = args[idx + 1];
                        idx++;
                    }
                }
                idx++;
            }

            // Accept secrets from the environment so they need not appear in argv (visible via
            // Task Manager/WMI to other processes). Explicit flags still win; env is preferred.
            authToken ??= Environment.GetEnvironmentVariable("SYXX_BEARER_TOKEN");
            licenseKey ??= Environment.GetEnvironmentVariable("SYXX_LICENSE_KEY");

            if (servers.Count == 0)
            {
                Console.WriteLine("INCONCLUSIVE: No system DNS resolvers were detected. Refusing to fall back to public resolvers for a leak test.");
                Environment.ExitCode = 2;
                return;
            }

            // Collector-confirmed path.
            if (collectorURL != null)
            {
                // Paid route: a bearer session token or a license key is required. Fall back
                // to the license key already stored by activation if neither flag was given.
                if (authToken == null && licenseKey == null)
                {
                    licenseKey = LicenseManager.GetStoredLicenseKey();
                    if (licenseKey != null)
                    {
                        Console.WriteLine("Using stored Syxx Pro license key for collector authentication.");
                    }
                }
                if (authToken == null && licenseKey == null)
                {
                    Console.WriteLine("ERROR: The collector API is a paid route. Pass --token <bearer-token> or --key <license-key> (or activate a license first).");
                    // E074: usage/operational error is 3, not 1 (which means THREAT under the shared contract).
                    Environment.ExitCode = 3;
                    return;
                }

                Console.WriteLine($"Running collector-confirmed leak test via {collectorURL} ...");
                Console.WriteLine($"Base domain : {baseDomain}");
                Console.WriteLine($"Resolvers   : {string.Join(", ", servers)}");

                var result = await LeakDetector.RunCollectorTestAsync(
                    baseDomain,
                    servers,
                    collectorURL,
                    authToken,
                    licenseKey
                );

                // E074: exit codes follow the shared contract (macOS SyxxCLI.swift:7):
                // 0=clean (no leak), 1=threat (leak confirmed), 2=inconclusive, 3=error.
                switch (result.Result)
                {
                    case LeakResult.Confirmed:
                        Console.WriteLine($"\n{result.ObservedResolvers.Count} resolver(s) observed");
                        Environment.ExitCode = 1; // E074: leak confirmed is a threat, not clean (0).
                        break;
                    case LeakResult.NotDetected:
                        Console.WriteLine("\n1 resolver observed — verify it matches your provider");
                        Environment.ExitCode = 0; // E074: no leak detected — clean.
                        break;
                    default:
                        Console.WriteLine("\nINCONCLUSIVE");
                        Environment.ExitCode = 2;
                        break;
                }

                if (result.ObservedResolvers.Count > 0)
                {
                    Console.WriteLine("Observed resolvers (collector-attested):");
                    foreach (var r in result.ObservedResolvers)
                        Console.WriteLine($"  {r}");
                }
                if (result.Signature != null)
                {
                    Console.WriteLine($"Collector receipt (Ed25519-signed; verifiable with public key — keep for disputes): {result.Signature}");
                    Console.WriteLine("  Note: this is a chain-of-custody receipt, not a client-verifiable signature.");
                }
                else
                {
                    Console.WriteLine("WARNING: No collector receipt returned — this result is not attributable evidence.");
                }
                if (result.ErrorReason != null)
                    Console.WriteLine($"Reason: {result.ErrorReason}");
                return;
            }

            // Local-only path (no collector).
            string target = LeakDetector.GenerateTestSubdomain(baseDomain);
            Console.WriteLine($"Generated leak test subdomain: {target}");
            Console.WriteLine($"Querying upstream DNS servers: {string.Join(", ", servers)}");

            var localResults = await LeakDetector.TriggerQueriesAsync(target, servers);

            Console.WriteLine("\nQuery Trigger Results:");
            foreach (var res in localResults)
                Console.WriteLine($"  {res.Key}: {(res.Value ? "PROBE SENT" : "FAILED / UNREACHABLE")}");

            // Only conclude from what actually happened: if no probe reached a resolver, the test did
            // not run — never imply a dispatched state the premises contradict.
            if (localResults.Values.Any(sent => sent))
            {
                Console.WriteLine("\nINCONCLUSIVE (no collector): DNS resolver probes were dispatched but collector-side confirmation was not requested. Pass --collector <URL> with --token <bearer-token> or --key <license-key> for a verified result.");
                Environment.ExitCode = 2;
            }
            else
            {
                Console.WriteLine("\nINCONCLUSIVE — all probe dispatches FAILED, so the leak test did not run and nothing was measured. This is NOT a clean result. Cause: your system resolvers are unreachable (no connectivity, a VPN/firewall blocking DNS, or a captive portal). Restore DNS connectivity and re-run; add --collector <URL> --key <license> for a verified result.");
                // E074: "nothing measured" is an error (3), not a threat (1). Under the shared
                // contract 1=THREAT — reading "nothing measured" as "threat" is the exact
                // inversion doctrine #2 forbids.
                Environment.ExitCode = 3;
            }
        }
    }
}
