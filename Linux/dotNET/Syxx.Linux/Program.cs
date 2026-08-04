using System;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Linux;

/// <summary>
/// Syxx CLI for Linux — DNS tampering detection toolkit.
/// Six toolsets: Prophet, Mirror, Portal, Glutton, Cage, Witness.
/// </summary>
public static class Program
{
    private static int _exitCode = 2; // inconclusive by default

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args[0] == "--version" || args[0] == "-V")
        {
            Console.WriteLine($"syxx {SyxxConfig.Version}");
            return 0;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        try
        {
            switch (subcommand)
            {
                case "nslook":
                case "prophet":
                    await RunProphet(subArgs);
                    break;
                case "mirror":
                    await RunMirror(subArgs);
                    break;
                case "portal":
                case "key":
                    await RunPortal(subArgs);
                    break;
                case "glutton":
                case "leak":
                    await RunGlutton(subArgs);
                    break;
                case "cage":
                case "cages":
                    await RunCage(subArgs);
                    break;
                case "witness":
                    await RunWitness(subArgs);
                    break;
                case "warp":
                    await RunWarp(subArgs);
                    break;
                case "route":
                    await RunRoute(subArgs);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown subcommand: {subcommand}");
                    Console.Error.WriteLine("Use 'syxx --help' for usage.");
                    return 3;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 3;
        }

        return _exitCode;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"syxx {SyxxConfig.Version} — DNS tampering detection toolkit");
        Console.WriteLine();
        Console.WriteLine("Usage: syxx <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  nslook, prophet    Query DNS and check DNSSEC");
        Console.WriteLine("  mirror             Compare content from two vantage points");
        Console.WriteLine("  portal, key        Show network state and route classification");
        Console.WriteLine("  glutton, leak      Check for DNS leaks");
        Console.WriteLine("  cage, cages        Capture live DNS traffic");
        Console.WriteLine("  witness            Sign and verify scan evidence");
        Console.WriteLine("  warp               Content integrity comparison");
        Console.WriteLine("  route              Traceroute with IP classification");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --verbose, -v      Detailed output");
        Console.WriteLine("  --json             Machine-readable JSON output");
        Console.WriteLine("  --help, -h         Show this help");
        Console.WriteLine("  --version, -V      Show version");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0=clean, 1=threat, 2=inconclusive, 3=error");
    }

    private static async Task RunProphet(string[] args)
    {
        var domain = args.FirstOrDefault() ?? "syxx.app";
        Console.WriteLine($"Prophet: querying {domain}...");
        Console.WriteLine("(Prophet implementation pending)");
        await Task.CompletedTask;
    }

    private static async Task RunMirror(string[] args)
    {
        var domain = args.FirstOrDefault() ?? "syxx.app";
        Console.WriteLine($"Mirror: comparing {domain}...");
        Console.WriteLine("(Mirror implementation pending)");
        await Task.CompletedTask;
    }

    private static async Task RunPortal(string[] args)
    {
        Console.WriteLine("Portal: analyzing network state...");
        Console.WriteLine("(Portal implementation pending)");
        await Task.CompletedTask;
    }

    private static async Task RunGlutton(string[] args)
    {
        Console.WriteLine("Glutton: checking for DNS leaks...");
        Console.WriteLine("(Glutton implementation pending)");
        await Task.CompletedTask;
    }

    private static async Task RunCage(string[] args)
    {
        var duration = args.Length > 0 && int.TryParse(args[0], out var d) ? d : 30;
        Console.WriteLine($"Cage: capturing DNS traffic for {duration}s...");
        Console.WriteLine("(Cage implementation pending — requires root for raw socket capture)");
        await Task.CompletedTask;
    }

    private static async Task RunWitness(string[] args)
    {
        Console.WriteLine("Witness: signing evidence...");
        Console.WriteLine("(Witness implementation pending)");
        await Task.CompletedTask;
    }

    private static async Task RunWarp(string[] args)
    {
        var domain = args.FirstOrDefault() ?? "syxx.app";
        Console.WriteLine($"Warp: integrity check for {domain}...");
        Console.WriteLine("(Warp implementation pending)");
        await Task.CompletedTask;
    }

    private static async Task RunRoute(string[] args)
    {
        var target = args.FirstOrDefault() ?? "8.8.8.8";
        Console.WriteLine($"Route: tracing to {target}...");
        Console.WriteLine("(Route implementation pending)");
        await Task.CompletedTask;
    }
}