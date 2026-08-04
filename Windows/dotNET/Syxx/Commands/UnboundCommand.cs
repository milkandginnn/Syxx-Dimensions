using System;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// `syxx unbound [verify|status|enable|disable]` — local DNSSEC-validating resolver. Twin of the
    /// macOS SyxxUnbound CLI. `verify` is the reproducible, admin-free proof; `enable`/`disable` perform
    /// the real system-resolver switch and need Administrator rights.
    ///
    /// NOTE (copy-gate): the human-readable strings below are PROPOSED wording pending owner approval.
    internal static class UnboundCommand
    {
        public static async Task RunAsync(string[] args)
        {
            if (SyxxConfig.HandleGlobalFlags(args, "syxx unbound",
                "Usage: syxx unbound [verify|status|enable|disable]   (local DNSSEC-validating resolver)")) return;
            string sub = args.Length >= 1 ? args[0].ToLowerInvariant() : "verify";

            switch (sub)
            {
                case "verify": await RunVerify(); break;
                case "status": RunStatus(); break;
                case "enable": await RunEnable(); break;
                case "disable": RunDisable(); break;
                default: Console.WriteLine($"Unknown 'unbound' action '{sub}'. Use: verify | status | enable | disable"); break;
            }
        }

        private static void RunStatus()
        {
            Console.WriteLine("\n  Local resolver (Unbound)");
            Console.WriteLine(new string('─', 52));
            var path = UnboundResolver.LocateBinary();
            Console.WriteLine(path is not null ? $"  Binary        : found ({path})" : "  Binary        : NOT INSTALLED");
            if (path is null) Console.WriteLine("  " + UnboundResolver.InstallHint);
            var servers = NetworkKeyState.GetSystemDNSServers();
            Console.WriteLine($"  System DNS    : {(servers.Count == 0 ? "(none reported)" : string.Join(", ", servers))}");
            bool usingLocal = servers.Contains("127.0.0.1") || servers.Contains("::1");
            Console.WriteLine($"  Points local? : {(usingLocal ? "yes (127.0.0.1 present)" : "no")}");
            Console.WriteLine(new string('─', 52) + "\n");
        }

        private static async Task RunVerify()
        {
            Console.WriteLine("\n  Verifying local Unbound (recursion + DNSSEC, unprivileged high port)");
            Console.WriteLine(new string('─', 52));
            var mgr = new UnboundManager();
            var result = await mgr.VerifyAsync(5354);
            Console.WriteLine("  " + result.detail);
            Console.WriteLine(new string('─', 52));
            Console.WriteLine(result.ok
                ? "  RESULT: PASS — the local resolver recurses and validates DNSSEC.\n"
                : "  RESULT: FAIL — see detail above (honest failure, not a fabricated pass).\n");
        }

        private static async Task RunEnable()
        {
            Console.WriteLine("\n  Enabling local Unbound as the system resolver…");
            // E083: "Administrator" is Windows-only; Linux uses root/sudo.
            Console.WriteLine($"  (requires {(OperatingSystem.IsWindows() ? "Administrator" : "root (sudo)")}; snapshot + auto-revert failsafe active)");
            var mgr = new UnboundManager();
            PrintResult(await mgr.EnableAsync());
        }

        private static void RunDisable()
        {
            Console.WriteLine("\n  Disabling local Unbound; restoring the original system resolver…");
            var mgr = new UnboundManager();
            PrintResult(mgr.Disable());
        }

        private static void PrintResult(UnboundResult r)
        {
            switch (r.Kind)
            {
                case UnboundResultKind.Enabled:
                    Console.WriteLine($"  ENABLED — system DNS now points at 127.0.0.1:{r.Port}.");
                    Console.WriteLine($"  Prior servers (restored on disable): {(r.PriorServers is null || r.PriorServers.Count == 0 ? "(DHCP-provided)" : string.Join(", ", r.PriorServers))}");
                    break;
                case UnboundResultKind.Disabled:
                    Console.WriteLine($"  DISABLED — restored: {(r.PriorServers is null || r.PriorServers.Count == 0 ? "(DHCP-provided)" : string.Join(", ", r.PriorServers))}");
                    break;
                case UnboundResultKind.NotInstalled:
                    Console.WriteLine($"  NOT INSTALLED — {r.Message}");
                    break;
                case UnboundResultKind.ElevationRequired:
                    Console.WriteLine($"  ADMIN REQUIRED — {r.Message}. Re-run from an elevated prompt.");
                    break;
                case UnboundResultKind.Failed:
                    Console.WriteLine($"  FAILED — {r.Message}");
                    break;
                case UnboundResultKind.Inconclusive:
                    Console.WriteLine($"  INCONCLUSIVE — {r.Message}");
                    break;
            }
            Console.WriteLine();
        }
    }
}
