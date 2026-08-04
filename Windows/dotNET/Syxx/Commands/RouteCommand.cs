using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx route` — traceroute path analysis. Body moved from the former SyxxRoute tool;
    /// the inline `tracert` block is replaced by the shared Syxx.Kit.TracerouteRunner. args[0] is the
    /// first real argument (the subcommand token was stripped by the dispatcher).</summary>
    internal static class RouteCommand
    {
        public static async Task RunAsync(string[] args)
        {
            if (SyxxConfig.HandleGlobalFlags(args, "syxx route", "Usage: syxx route <target>   (traceroute path analysis)")) return;
            string target = "8.8.8.8";
            if (args.Length >= 1)
            {
                target = args[0];
            }

            Console.WriteLine($"Analyzing route path to: {target}");
            Console.WriteLine("Traversing route path hops...");
            if (!IsSafeTraceTarget(target))
            {
                Console.WriteLine("Error: target must be a plain hostname or IP address.");
                return;
            }

            // Shared traceroute wrapper (single source of truth). An empty result is an honest
            // failure/timeout — never a clean or complete path.
            var traceResult = await TracerouteRunner.TraceAsync(target, maxHops: 15, waitMs: 1000);
            if (traceResult.Hops.Count == 0)
            {
                // C152: label the specific failure reason
                var reason = traceResult.Status switch
                {
                    TraceStatus.LaunchFailed => "failed to launch",
                    TraceStatus.TruncatedByDeadline => "timed out before any hop responded",
                    _ => "produced no output"
                };
                Console.WriteLine($"Traceroute {reason}.");
                if (traceResult.Diagnostic != null)
                    Console.WriteLine($"Diagnostic: {traceResult.Diagnostic}");
                return;
            }

            Console.WriteLine("\nTrace Route Hops:");
            foreach (var hop in traceResult.Hops)
            {
                Console.WriteLine($"  {hop}");
            }
            // C152: show the truncation notice if Syxx killed the trace
            if (traceResult.Status == TraceStatus.TruncatedByDeadline)
            {
                Console.WriteLine($"\n⚠️ {traceResult.Diagnostic}");
            }
        }

        static bool IsSafeTraceTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.StartsWith("-") || target.Any(char.IsWhiteSpace))
            {
                return false;
            }
            return IPAddress.TryParse(target, out _) || Uri.CheckHostName(target) == UriHostNameType.Dns;
        }
    }
}
