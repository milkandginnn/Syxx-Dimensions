using System;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>
    /// CLI entry point for the first-run scan (Phase A-4). Byte-parity twin of macOS
    /// FirstRunCommand in SyxxCLI/Synthesis/FirstRunScan.swift.
    /// </summary>
    internal static class FirstRunCommand
    {
        public static async Task RunAsync(string[] args)
        {
            // E088: guard --help/--version like every other command. Without this,
            // `syxx firstrun -h` silently runs the full multi-minute scan instead of printing help.
            if (SyxxConfig.HandleGlobalFlags(args, "syxx firstrun",
                "Usage: syxx firstrun [--domain <d>]   (five-minute first-run scan — runs all disciplines, grades the network)")) return;

            string? domain = null;

            // Parse --domain / -d; reject unknown flags (exit 3) so a typo doesn't
            // silently trigger a multi-minute scan.
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if ((arg == "--domain" || arg == "-d") && i + 1 < args.Length)
                {
                    domain = args[i + 1];
                    i++;
                }
                else if (arg.StartsWith("-"))
                {
                    Console.Error.WriteLine($"Error: Unknown option '{args[i]}'");
                    Console.Error.WriteLine("Usage: syxx firstrun [--domain <d>]");
                    Environment.ExitCode = 3;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(domain))
            {
                string? canonical = DomainCanonical.CanonicalizeOrNull(domain);
                if (canonical is null)
                {
                    Console.WriteLine($"Error: Invalid domain '{domain}' \u2014 could not canonicalize");
                    return;
                }
                domain = canonical;
            }

            // E084: build the banner box from a single width constant so content rows always
            // match the border. The old literals were 2–3 cells short, breaking the right edge.
            const int boxWidth = 69;
            int innerWidth = boxWidth - 2; // space between the ║ borders
            string topBorder = "\u2554" + new string('\u2550', innerWidth) + "\u2557";
            string botBorder = "\u255A" + new string('\u2550', innerWidth) + "\u255D";
            string PadLine(string text) => "\u2551" + text.PadRight(innerWidth) + "\u2551";

            Console.WriteLine(topBorder);
            Console.WriteLine(PadLine("                    Syxx First-Run Scan"));
            Console.WriteLine(PadLine("       One-button full scan \u2014 graded result before config"));
            Console.WriteLine(PadLine("   \u00A9 J.J. Warne d/b/a MilkandGin \u00B7 feedback@milkandgin.com"));
            Console.WriteLine(botBorder);
            Console.WriteLine();

            var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));

            Console.WriteLine("\uD83D\uDD0D Starting first-run scan...");
            Console.WriteLine();

            FirstRunResult result;
            try
            {
                result = await FirstRunScan.RunAsync(domain: domain, progress: progress);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[INCONCLUSIVE] The scan could not complete: {ex.Message}");
                Console.WriteLine("This is not evidence that the network is clean \u2014 nothing was measured.");
                Console.WriteLine("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            Console.WriteLine("  RESULTS");
            Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
            Console.WriteLine();

            // Grade
            string gradeEmoji = result.Card.Grade switch
            {
                Grade.A or Grade.B => "\uD83D\uDFE2",
                Grade.C => "\uD83D\uDFE1",
                Grade.D => "\uD83D\uDFE0",
                Grade.F => "\uD83D\uDD34",
                _ => "\u26AA"
            };
            Console.WriteLine($"  Grade: {gradeEmoji} {result.Card.GradeLabel}");
            Console.WriteLine();
            Console.WriteLine($"  {result.Card.Headline}");
            Console.WriteLine();

            // Top reasons
            if (result.Card.TopReasons.Count > 0)
            {
                Console.WriteLine("  Top reasons:");
                foreach (var reason in result.Card.TopReasons)
                    Console.WriteLine($"    \u2022 {reason}");
                Console.WriteLine();
            }

            // Ran/skipped
            Console.WriteLine($"  Disciplines ran: {(result.Ran.Count == 0 ? "(none)" : string.Join(", ", result.Ran))}");
            if (result.Skipped.Count > 0)
                Console.WriteLine($"  Disciplines skipped: {string.Join(", ", result.Skipped)}");
            Console.WriteLine();

            // Result action
            var action = ResultActions.For(result.Conclusion);
            Console.WriteLine($"  Meaning: {action.Meaning}");
            Console.WriteLine();
            Console.WriteLine($"  Action:  {action.PrimaryAction}");
            if (action.Qualified)
            {
                Console.WriteLine();
                Console.WriteLine($"  \u26A0\uFE0F  {ResultActions.ConfidenceCaveat}");
            }
            Console.WriteLine();

            // Plain-language explanation
            var explanation = PlainExplanations.For(result.Conclusion);
            Console.WriteLine($"  Explanation: {explanation.Narrative}");
            Console.WriteLine();

            // Suppressed / overhang signals
            var det = result.Conclusion.Determination;
            if (det?.Suppressed is { Count: > 0 })
            {
                Console.WriteLine("  ⚠️ UNVERIFIED SIGNALS:");
                foreach (var s in det.Suppressed)
                    Console.WriteLine($"     - {s.DisplayName} [{s.Reason}]: {s.Detail}");
            }
            Console.WriteLine();
            Console.WriteLine("\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        }
    }
}
