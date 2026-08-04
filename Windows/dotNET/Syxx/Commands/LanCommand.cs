using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx lan` — ARP/LAN map. Body moved verbatim from the former SyxxLan tool (which was
    /// synchronous); wrapped in a Task-returning RunAsync so the dispatcher can await it uniformly.
    /// args[0] is the first real argument (the subcommand token was stripped by the dispatcher).</summary>
    internal static class LanCommand
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

            if (SyxxConfig.HandleGlobalFlags(args, "syxx lan",
                "Usage: syxx lan   (map on-link devices from the ARP table and flag your DNS resolver)")) return;

            Console.WriteLine("Mapping on-link devices (ARP cache)...");

            LANSnapshot snapshot;
            try
            {
                snapshot = new LANCartographer().Snapshot();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read the ARP table: {ex.Message}");
                return;
            }

            // Cross-reference against the system resolvers so a resolver that is itself an on-link
            // device is called out — the first step toward locating a rogue DNS box (datapoint X1).
            HashSet<string> resolverIPs;
            try
            {
                resolverIPs = new HashSet<string>(NetworkKeyState.GetSystemDNSServers());
            }
            catch
            {
                resolverIPs = new HashSet<string>();
            }

            // C155: check IsPartial BEFORE Devices.Count — a failed scan must not be reported as
            // an empty LAN. The message must distinguish "no devices" from "scan failed".
            if (snapshot.IsPartial)
            {
                Console.WriteLine($"Syxx could not fully read the ARP table — this is not evidence that your LAN is empty. ({snapshot.PartialReason ?? "unknown reason"})");
                if (snapshot.Devices.Count > 0)
                {
                    Console.WriteLine($"\nPartial result: {snapshot.Devices.Count} device(s) were detected before the scan ended:");
                }
                else
                {
                    return;
                }
            }
            else if (snapshot.Devices.Count == 0)
            {
                Console.WriteLine("No devices found in the ARP cache. (Try generating traffic first, e.g. ping your gateway.)");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Found {snapshot.Summary.DeviceCount} device(s) across interface(s): {string.Join(", ", snapshot.Summary.Interfaces)}");
            Console.WriteLine($"  reachable/permanent: {snapshot.Summary.ReachableCount}   incomplete: {snapshot.Summary.IncompleteCount}");
            Console.WriteLine("----------------------------------------------------------------------------------");

            foreach (var d in snapshot.Devices)
            {
                bool isResolver = resolverIPs.Contains(d.IPAddress);
                string flag = isResolver ? "  ⟵ THIS DEVICE IS ONE OF YOUR DNS RESOLVERS" : "";
                Console.WriteLine($"  {d.IPAddress,-16} {d.MacAddress,-18} {d.AddressKind.Label(),-15} [{d.State}] on {d.Interface}{flag}");
                if (d.Notes.Count > 0)
                {
                    Console.WriteLine($"      notes: {string.Join("; ", d.Notes)}");
                }
            }
            Console.WriteLine("----------------------------------------------------------------------------------");

            var resolversOnLan = snapshot.Devices.Where(d => resolverIPs.Contains(d.IPAddress)).ToList();
            if (resolversOnLan.Count > 0)
            {
                Console.WriteLine("\n🔎 Derived: your DNS resolver is a device on this LAN:");
                foreach (var d in resolversOnLan)
                {
                    Console.WriteLine($"   {d.IPAddress} is at MAC {d.MacAddress} ({d.AddressKind.Label()}). Confirm this is your intended router/resolver.");
                }
            }
            else if (resolverIPs.Count > 0)
            {
                Console.WriteLine("\n🔎 Derived: none of your configured resolvers appear as on-link devices — they are reached through the gateway (off-LAN).");
            }
        }
    }
}
