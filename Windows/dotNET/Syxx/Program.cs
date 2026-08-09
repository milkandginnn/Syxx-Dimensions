using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Sentry;
using Syxx.Kit;
using Syxx.Commands;
// ParityTranscriptCommand lives in Syxx.Commands

namespace Syxx
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // E073: seed exit code to 2 (inconclusive) so any command that exits without
            // explicitly setting a result reads as "inconclusive" rather than "clean" (0).
            // This matches the macOS contract: SyxxCLI.swift:8 — 0=clean, 1=threat,
            // 2=inconclusive, 3=error. Commands that succeed set ExitCode=0 explicitly;
            // commands that detect threats set ExitCode=1; errors set ExitCode=3.
            // A command that falls through without deciding (cage with no socket, route
            // with no hops, mirror with no vantage) must not read as clean.
            Environment.ExitCode = 2;

            // Sentry crash/exception reporting — best-effort, disabled when no DSN is set.
            using var sentry = SentrySdk.Init(o =>
            {
                var dsn = SyxxConfig.SentryDsn;
                o.Dsn = dsn ?? "";
                if (string.IsNullOrEmpty(o.Dsn)) return;
                o.Environment = SyxxConfig.BuildEnvironment;
                o.Release = $"syxx-cli-windows@{SyxxConfig.Version}";
                o.AttachStacktrace = true;
                o.SendDefaultPii = false;
            });

            // Connect the core's vendor-neutral sink to Sentry. Without this every
            // SyxxDiagnostics.Capture in Syxx.Kit finds a null sink and drops silently — the CLI
            // initialised Sentry but reported nothing but unhandled crashes.
            Syxx.Telemetry.SentryDiagnosticSink.Install(surface: "cli",
                platform: Syxx.Telemetry.OTelBootstrap.CurrentOsType());

            // Register the OTel provider. Without a registered listener the Kit's ActivitySource is
            // a silent no-op — StartActivity returns null and every span the CLI produces vanishes.
            // `using` matters: Dispose runs Shutdown, which DRAINS the batch processor. A CLI
            // usually exits well inside the 5s batch interval, so without this the spans of a short
            // command are simply lost. Service name is the ARTIFACT (`syxx-cli`, one binary for
            // win-x64/linux-x64/linux-arm64); where it ran is os.type.
            using var tracerProvider =
                Syxx.Telemetry.OTelBootstrap.Register(serviceName: "syxx-cli", surface: "cli");

            // E012: emit a diagnostic when UTF-8 encoding fails on legacy console hosts so
            // mojibake in evidence output (IDN domains, dashes, arrows) is explainable.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warning] Console UTF-8 encoding unavailable: {ex.Message}");
            }

            // Unified binary: dispatch a subcommand, or fall through to the interactive guided mode.
            // C# Main(args) does NOT include the program name, so args[0] is the subcommand token and
            // args[1..] are that subcommand's own arguments — exactly what each command's args[0]-based
            // parsing expects (there is no off-by-one to correct).
            if (args.Length == 0 || string.Equals(args[0], "guided", StringComparison.OrdinalIgnoreCase))
            {
                await RunInteractiveAsync();
                Environment.ExitCode = 0; // E073: interactive mode completed without error
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "nslook":    await NslookCommand.RunAsync(args[1..]); break;
                case "mirror":    await MirrorCommand.RunAsync(args[1..]); break;
                case "leak":      await LeakCommand.RunAsync(args[1..]); break;
                case "witness":   await WitnessCommand.RunAsync(args[1..]); break;
                case "key":       await KeyCommand.RunAsync(args[1..]); break;
                case "route":     await RouteCommand.RunAsync(args[1..]); break;
                case "lan":       await LanCommand.RunAsync(args[1..]); break;
                case "forensics": await ForensicsCommand.RunAsync(args[1..]); break;
                case "sentinel":  await SentinelCommand.RunAsync(args[1..]); break;
                case "cage":      await CageCommand.RunAsync(args[1..]); break;
                case "warp":      await WarpCommand.RunAsync(args[1..]); break;
                case "speed":     await SpeedCommand.RunAsync(args[1..]); break;
                case "unbound":   await UnboundCommand.RunAsync(args[1..]); break;
                case "filter":    await FilterCommand.RunAsync(args[1..]); break;
                case "firstrun":  await FirstRunCommand.RunAsync(args[1..]); break;
                // Phase 4.2: CI-only behavioral transcript emitter (production Synthesizer path).
                case "parity-transcript":
                    Environment.ExitCode = ParityTranscriptCommand.Run(args[1..]);
                    break;
                case "--version":
                case "-v":
                    Console.WriteLine($"syxx {SyxxConfig.Version}");
                    Environment.ExitCode = 0; // E073: --version is not a security conclusion
                    break;
                // F-14: -V / --verbose at top level sets the verbose flag. Unlike --version
                // and --help this does NOT exit — it just enables verbose output for the
                // subcommand that follows.
                case "--verbose":
                case "-V":
                    SyxxConfig.IsVerbose = true;
                    // If there is a subcommand after --verbose, dispatch it.
                    if (args.Length > 1)
                    {
                        var rest = args[1..];
                        // Recurse-like: re-dispatch the subcommand from rest[0].
                        switch (rest[0].ToLowerInvariant())
                        {
                            case "nslook":    await NslookCommand.RunAsync(rest[1..]); break;
                            case "mirror":    await MirrorCommand.RunAsync(rest[1..]); break;
                            case "leak":      await LeakCommand.RunAsync(rest[1..]); break;
                            case "witness":   await WitnessCommand.RunAsync(rest[1..]); break;
                            case "key":       await KeyCommand.RunAsync(rest[1..]); break;
                            case "route":     await RouteCommand.RunAsync(rest[1..]); break;
                            case "lan":       await LanCommand.RunAsync(rest[1..]); break;
                            case "forensics": await ForensicsCommand.RunAsync(rest[1..]); break;
                            case "sentinel":  await SentinelCommand.RunAsync(rest[1..]); break;
                            case "cage":      await CageCommand.RunAsync(rest[1..]); break;
                            case "warp":      await WarpCommand.RunAsync(rest[1..]); break;
                            case "speed":     await SpeedCommand.RunAsync(rest[1..]); break;
                            case "unbound":   await UnboundCommand.RunAsync(rest[1..]); break;
                            case "filter":    await FilterCommand.RunAsync(rest[1..]); break;
                            case "firstrun":  await FirstRunCommand.RunAsync(rest[1..]); break;
                            case "parity-transcript":
                                Environment.ExitCode = ParityTranscriptCommand.Run(rest[1..]);
                                break;
                            default:
                                Console.WriteLine($"Unknown command: {rest[0]}");
                                Console.WriteLine("Run 'syxx --help' for a list of commands.");
                                Environment.ExitCode = 3;
                                break;
                        }
                    }
                    else
                    {
                        // --verbose with no subcommand — enter interactive mode with verbose.
                        await RunInteractiveAsync();
                        Environment.ExitCode = 0;
                    }
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.ExitCode = 0; // E073: --help is not a security conclusion
                    break;
                default:
                    // B253: was 1, which under the shared exit-code contract (macOS
                    // SyxxCLI.swift:7 — 0=clean, 1=threat, 2=inconclusive, 3=error) means THREAT
                    // DETECTED. A typo'd subcommand is an operational error, not a finding about
                    // the network. macOS already used 3 here (SyxxCLI.swift:52); this is the parity
                    // half.
                    Console.WriteLine($"Unknown command: {args[0]}");
                    Console.WriteLine("Run 'syxx --help' for a list of commands.");
                    Environment.ExitCode = 3;
                    break;
            }
        }

        /// Prints the top-level help listing every subcommand. Mirrors the macOS `syxx --help`.
        static void PrintHelp()
        {
            Console.WriteLine("Usage: syxx [subcommand] [options]");
            Console.WriteLine();
            Console.WriteLine("Subcommands:");
            Console.WriteLine("  nslook     DNS lookup (type, resolver)");
            Console.WriteLine("  mirror     Vantage comparison (local vs public)");
            Console.WriteLine("  leak       VPN DNS leak test");
            Console.WriteLine("  witness    Signed evidence (keygen, sign, verify)");
            Console.WriteLine("  key        Network state (resolver, interface, VPN)");
            Console.WriteLine("  route      Traceroute to a target");
            Console.WriteLine("  lan        ARP/LAN map");
            Console.WriteLine("  forensics  DNS forensics analysis");
            Console.WriteLine("  sentinel   Config drift detection");
            // E083: "administrator" is Windows-only; Linux uses root/sudo.
            Console.WriteLine($"  cage       Packet capture (requires {(OperatingSystem.IsWindows() ? "administrator" : "root (sudo)")})");
            Console.WriteLine("  warp       Content integrity check");
            Console.WriteLine("  speed      Resolver speed benchmark (ranks all resolvers by latency)");
            Console.WriteLine("  unbound    Local DNSSEC-validating resolver (verify | status | enable | disable)");
            Console.WriteLine("  filter     DNS blocklist / Pi-hole enforcement + bypass check");
            Console.WriteLine("  firstrun   Five-minute first-run scan \u2014 runs all disciplines, grades the network");
            Console.WriteLine();
            Console.WriteLine("Without a subcommand, enters interactive guided mode.");
            Console.WriteLine("Examples:");
            Console.WriteLine("  syxx nslook google.com MX @8.8.8.8");
            Console.WriteLine("  syxx mirror google.com");
            Console.WriteLine("  syxx route 8.8.8.8");
            Console.WriteLine("  syxx cage --duration 10");
        }

        static async Task RunInteractiveAsync()
        {
            Console.WriteLine(@"
                                         kmOQO
                                    LLQCUXh#
                                 X]]>ii?<!Ykkk#
                             dv][!i\     c]+1bbbh
                           j]]!,           r?]dddwmk
                       $/][l'              O]>pqqd/]OZp%
                    q)]]l`                W?]]wwwd   z]J0w%
                  0]-l'                  \?][mmmm       L[zQd
                  C[,                 q[??]COOOa          uQw
                  J]:               (????JQQZ             vLq
                  C]:            v_--?fCCw                vLq
                  C]:          [+__cU0tuZZO0wh            cLw
                  J[:        {~\wbdfYZZZOOO0QLLo          cCw
                  J]:      $udbdZtmm]>;/}]]]-~~JCd        vCZ
                  J[:     &ddddtZh         u?????(#       vJm
                  C];    &qppp/Z             {??---       uJm
                  J];    *qqqLt               -----(      vUm
                  C];    dwwwJL               x---_]      cUm
                  U];    dwwmZU               j-___?      vUZ
                  C];    omZZZC               _____<      cYm
                  L]?(   #ZOOO00             r_____      0vYm
                    01]?) B000QLLm         Or___+_    JzYCo
                       $(]]}OQLLCCCJJCQJUYY_____-  YXYLC
                          ]|??LCCCJJUUUYY-____-8cYYQ]
                             X/--jzcr}______(nYY0
                                 f--ij   hnYYm
                                    r-_1UUq
                                       X$

          Syxx
          A DNS investigative and protection suite.
          © J.J. Warne d/b/a MilkandGin
          All rights reserved.
          feedback@milkandgin.com
");

            string accountID = SyxxConfig.AccountID;
            bool isPro = false;

            string? storedKey = LicenseManager.GetStoredLicenseKey();
            if (!string.IsNullOrEmpty(storedKey))
            {
                Console.WriteLine("🔑 Local license key found. Verifying with Keygen...");
                // E050: use the detailed result so a network timeout on the censored network
                // being investigated is not silently treated as an invalid key.
                var startupResult = await LicenseManager.ValidateLicenseDetailedAsync(storedKey, accountID);
                switch (startupResult)
                {
                    case LicenseValidationResult.Valid:
                        isPro = true;
                        break;
                    case LicenseValidationResult.Invalid:
                        isPro = false;
                        Console.WriteLine("⚠️  Stored license key is invalid or expired. Running in Lite mode.");
                        break;
                    case LicenseValidationResult.Timeout:
                    case LicenseValidationResult.NetworkError:
                        isPro = false;
                        Console.WriteLine("⚠️  Could not reach the license service — stored key was not checked. Running in Lite mode for this session.");
                        break;
                }
            }

            var inputs = new SectionInputs();
            bool running = true;
            while (running)
            {
                Console.WriteLine("==================================================================================");
                Console.WriteLine($"License status: {(isPro ? "🟢 PRO ACTIVATED" : "🔴 LITE VERSION")}");
                Console.WriteLine("----------------------------------------------------------------------------------");
                Console.WriteLine("Choose a check:");
                Console.WriteLine("  [S] Scan         First-run scan — runs everything that needs no setup, then grades this network.");
                Console.WriteLine("  [1] Prophet      Tests your DNS resolver for accuracy.");
                Console.WriteLine("  [2] Mirror       Tests downloaded web content against a remote copy to ensure it matches.");
                Console.WriteLine("  [3] Portal       Tests your local LAN state and the route taken to a chosen domain.");
                Console.WriteLine("  [4] Glutton      A DNS leak test — diagnoses vulnerable VPN connections.");
                Console.WriteLine("  [5] Cage         DNS capture & evaluation — processes behind queries, beaconing & attacks.");
                Console.WriteLine("  [6] Witness      Trust analysis for your home or office computer environment.");
                Console.WriteLine("  [C] Conclusions  Synthesize a unified conclusion from all completed checks.");
                Console.WriteLine("  [L] License      Register / manage your license key.");
                Console.WriteLine("  [Q] Quit");
                Console.WriteLine("==================================================================================");
                Console.Write("Your choice (S / 1-6 / C / L / Q): ");

                // E080: Console.ReadLine() returns null at EOF (no terminal, /dev/null, snap
                // desktop launcher, CI/cron). The old code treated null as "" which fell to
                // "Invalid choice" and looped forever at full CPU. Exit honestly instead.
                string? rawInput = Console.ReadLine();
                if (rawInput is null)
                {
                    Console.Error.WriteLine("syxx: no terminal attached — run a subcommand, e.g. 'syxx firstrun'.");
                    Environment.ExitCode = 3;
                    return;
                }
                string choice = rawInput.Trim().ToUpper();

                switch (choice)
                {
                    case "S":
                    case "SCAN":
                        await RunFirstScanAsync(inputs);
                        break;
                    case "1":
                    case "PROPHET":
                        await RunProphetAsync(PromptDomain("What is domain of your choosing? (Ex. github.com, reddit.com, or syxx.app) The Prophet asks your resolver for its answer and weighs whether that answer is true. (Choose a domain. The Prophet holds your resolver's answer up against what the domain's own authorities attest, and against names that ought not exist, to judge whether it may be believed.)"), inputs);
                        break;
                    case "2":
                    case "MIRROR":
                        await RunMirrorAsync(PromptDomain("In the mirror you shall see the domain or site of web for which you are endeared: The light of the mirror breaks the bonds of deception, revealing whether the site you receive is the same one shown to all. (The Mirror compares cryptographic hashes of the page as your network delivers it against a copy fetched through a clean, independent path, confirming that what you receive is the content served to everyone else.)", "Enter the website/URL to mirror-check (e.g. example.com): "), inputs);
                        break;
                    case "3":
                    case "PORTAL":
                        await RunPortalAsync(PromptDomain("Of all domains, to which shall the Portal deliver you today? The Portal traces the course your traffic must follow to reach that far host. Bearing you all the way, it delivers you along the traced route in less than a blink. (A traceroute follows the packets sent from your device to the domain or IP you chose, mapping the nodes, or hops, they pass through. If traffic is being redirected, it can help reveal where. If the last hop falls short of your domain, it may have been blocked by a firewall or swept into a web proxy.)"), inputs);
                        break;
                    case "4":
                    case "GLUTTON":
                        await RunGluttonAsync(inputs);
                        break;
                    case "5":
                    case "CAGE":
                        await RunCageAsync(inputs);
                        break;
                    case "6":
                    case "WITNESS":
                        await RunWitnessAsync(inputs);
                        break;
                    case "C":
                    case "CONCLUSIONS":
                    case "CONCLUSION":
                        ShowConclusions(inputs);
                        break;
                    case "L":
                        Console.WriteLine("\nEnter, ye, your key of license, and allow the good fortunes of the realm to draw you in: (Your license key unlocks the app's Pro checks and the features anchored to the Dimension. It is validated with the licensing service and then kept on this machine, and it never travels alongside your DNS data.) By this separation, your network findings remain unbound from your identity.");
                        Console.Write("Enter your Keygen.sh License Key: ");
                        string key = Console.ReadLine()?.Trim() ?? "";
                        if (string.IsNullOrEmpty(key)) { Console.WriteLine("Error: Key cannot be empty."); continue; }
                        Console.WriteLine("Verifying license with Keygen...");
                        // D043: a network failure is NOT an invalid key — the two must read
                        // differently. D042: a save failure must never print an unqualified success.
                        var licValidation = await LicenseManager.ValidateLicenseDetailedAsync(key, accountID);
                        switch (licValidation)
                        {
                            case LicenseValidationResult.Valid:
                                bool licSaved = true;
                                try { LicenseManager.SaveLicenseKey(key); }
                                catch { licSaved = false; }
                                isPro = true;
                                Console.WriteLine(licSaved
                                    ? "[SUCCESS] License key activated. Pro features unlocked."
                                    : "[WARNING] License verified and Pro is unlocked for this session, but the key could not be saved on this device. You will need to enter it again next launch.");
                                break;
                            case LicenseValidationResult.Invalid:
                                Console.WriteLine("[FAILED] The license key is invalid, expired, or locked.");
                                break;
                            default: // Timeout / NetworkError
                                Console.WriteLine("[NETWORK] Could not reach the license service, so your key was not checked - this says nothing about whether the key is valid. Check your connection and try again.");
                                break;
                        }
                        break;
                    case "Q":
                    case "QUIT":
                    case "EXIT":
                        Console.WriteLine("\nThank you for using Syxx. Goodbye!");
                        running = false;
                        break;
                    default:
                        Console.WriteLine("Invalid choice. Enter 1-6, C, L, or Q.");
                        break;
                }
            }
        }

        static string PromptDomain(string why, string prompt = "Enter the domain to check (e.g. google.com): ")
        {
            Console.WriteLine("\n" + why);
            Console.Write(prompt);
            string raw = Console.ReadLine()?.Trim() ?? "";
            // F007: canonicalize at the CLI boundary so DNS queries always receive a valid A-label.
            string? canonical = DomainCanonical.CanonicalizeOrNull(raw);
            if (canonical is null && raw.Length > 0)
            {
                Console.WriteLine("Error: Invalid domain '{0}' — could not canonicalize. Please enter a valid domain.", raw);
                return PromptDomain(why, prompt); // Retry on invalid input
            }
            return canonical ?? raw;
        }

        // MARK: - Conclusions (C option)

        /// Synthesizes a result from all completed checks and displays it — only when the
        /// user explicitly asks. No auto-result between runs.
        // A4 — the five-minute first run. One keystroke, no configuration: orchestrate the
        // disciplines that need no input, then render the A3 grade + A1 action. The scan itself
        // lives in the Kit (FirstRunScan) so the GUI shares it; this is only its console surface.
        static async Task RunFirstScanAsync(SectionInputs inputs)
        {
            Console.WriteLine();
            Console.WriteLine("==================================================================================");
            Console.WriteLine("FIRST-RUN SCAN — checking this network end to end. No setup required.");
            Console.WriteLine("----------------------------------------------------------------------------------");

            var progress = new Progress<string>(msg => Console.WriteLine($"  {msg}"));
            FirstRunResult result;
            try
            {
                result = await FirstRunScan.RunAsync(progress: progress);
            }
            catch (Exception ex)
            {
                // An aborted scan is INCONCLUSIVE, never a pass. Say so plainly and stop.
                Console.WriteLine($"\n[INCONCLUSIVE] The scan could not complete: {ex.Message}");
                Console.WriteLine("This is not evidence that the network is clean — nothing was measured.");
                Console.WriteLine("----------------------------------------------------------------------------------");
                Console.Write("Press Enter to return to the menu.");
                Console.ReadLine();
                return;
            }

            // NOTE: FirstRunScan builds its own SectionInputs internally, so this scan's evidence
            // does NOT merge back into the interactive session — [C] Conclusions will still report
            // on whatever the individual checks gathered. Threading the scan's inputs back is owed;
            // until then the scan is self-contained and its grade stands on its own evidence.
            var card = result.Card;
            var action = ResultActions.For(result.Conclusion);

            Console.WriteLine();
            Console.WriteLine($"🎓 GRADE: {card.GradeLabel}: {card.Headline}");
            foreach (var reason in card.TopReasons)
                Console.WriteLine($"     • {reason}");
            Console.WriteLine();
            Console.WriteLine($"   What this means: {action.Meaning}");
            Console.WriteLine($"   Do this: {action.PrimaryAction}");
            Console.WriteLine();
            Console.WriteLine($"   Checks that produced evidence: {(result.Ran.Count == 0 ? "(none)" : string.Join(", ", result.Ran))}");
            if (result.Skipped.Count > 0)
                Console.WriteLine($"   Could not measure: {string.Join(", ", result.Skipped)}, the grade accounts for this.");
            Console.WriteLine("----------------------------------------------------------------------------------");
            Console.Write("Press Enter to return to the menu.");
            Console.ReadLine();
        }

        static void ShowConclusions(SectionInputs inputs)
        {
            Console.WriteLine();
            Console.WriteLine("==================================================================================");
            Console.WriteLine("CONCLUSIONS — SYNTHESIS CONCLUSION");
            Console.WriteLine("----------------------------------------------------------------------------------");
            var conclusion = Synthesizer.Conclude(inputs);
            // A3 — the glanceable report card. Ungraded ("—") when the run could not be measured.
            var card = ReportCards.For(conclusion);
            Console.WriteLine($"🎓 GRADE: {card.GradeLabel}: {card.Headline}");
            foreach (var reason in card.TopReasons)
                Console.WriteLine($"     • {reason}");
            Console.WriteLine();
            var det = conclusion.Determination;
            // UNTESTED (nothing was measured) and UNFINISHED (the engine did not produce a result
            // at all) are different failures and must not share a label; INCONCLUSIVE is a third,
            // distinct state meaning the checks ran on valid data that stayed ambiguous. Mirrors
            // macOS SyxxCLI.swift:181-189 case-for-case, including exit codes.
            string resultStr = det switch
            {
                { State: DeterminativeState.Untested } => "UNTESTED",
                { State: DeterminativeState.Inconclusive } => "INCONCLUSIVE",
                { State: DeterminativeState.Clean } => "CLEAN",
                { State: DeterminativeState.ThreatDetected } =>
                    $"THREAT: {string.Join(", ", det.Diagnoses.Select(d => d.DisplayName))}",
                _ => "UNFINISHED",
            };
            Console.WriteLine($"📊 CONCLUSION: {resultStr}");
            if (det?.Diagnoses.FirstOrDefault() is { } top)
            {
                Console.WriteLine($"   Top diagnosis: {top.DisplayName} ({top.Confidence}) — fit {top.Fit:P0}, coverage {top.Coverage:P0}");
            }
            // A1 — the "so what": what it means + the one action that matters.
            var va = ResultActions.For(conclusion);
            Console.WriteLine($"   What this means: {va.Meaning}");
            Console.WriteLine($"   Do this: {va.PrimaryAction}");
            // A2 — plain-language "why we think so" over the Dempster-Shafer synthesis.
            var expl = PlainExplanations.For(conclusion);
            Console.WriteLine($"   Why we think so: {expl.Narrative}");
            Console.WriteLine("   Key Observations:");
            if (conclusion.Evidence.Count == 0)
                Console.WriteLine("     (No evidence collected yet — run checks first to gather network intelligence)");
            else
                foreach (var ev in conclusion.Evidence)
                    Console.WriteLine($"     - [{ev.Section.ToString().ToUpper()}] {ev.Observation}");
            if (det?.Suppressed is { Count: > 0 })
            {
                Console.WriteLine("   ⚠️ UNVERIFIED SIGNALS:");
                foreach (var s in det.Suppressed)
                    Console.WriteLine($"     - {s.DisplayName} [{s.Reason}]: {s.Detail}");
            }
        }

        // 1. Prophet — DNS Resolution & Protocol Integrity
        static async Task RunProphetAsync(string domain, SectionInputs inputs)
        {
            if (string.IsNullOrEmpty(domain)) { Console.WriteLine("Error: Domain name cannot be empty."); return; }
            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine($"🔮 PROPHET — is your resolver telling the truth for: {domain}");
            Console.WriteLine("----------------------------------------------------------------------------------");

            var servers = NetworkKeyState.GetSystemDNSServers();
            string? localResolver = servers.FirstOrDefault();
            if (localResolver == null)
            {
                // E079: refuse to substitute a public resolver — measuring 8.8.8.8 and labelling
                // it as the user's resolver produces a false-clean report on a hijacked network.
                Console.WriteLine("INCONCLUSIVE: No system DNS resolvers were detected. Refusing to fall back to public resolvers.");
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }
            var encryption = NetworkKeyState.EncryptedDNSStatus();
            Console.WriteLine($"• Current local DNS server: {localResolver}");
            Console.WriteLine(encryption.State switch
            {
                ConditionState.Yes => "• System DNS encryption: 🔒 Encrypted",
                ConditionState.No => "• System DNS encryption: 🔓 Unencrypted (standard DNS)",
                _ => $"• System DNS encryption: ❓ Unknown ({encryption.Evidence})"
            });

            var budget = ProbeBudget.DefaultProphet;

            var response = await ProbeRunner.RunAsync<DNSResponse>("queryA", budget, () =>
                DNSResolver.QueryUDPDetailedAsync(new DNSQuery(domain, 1, localResolver)))
                ?? new DNSResponse { Status = DNSResponseStatus.Timeout, ErrorMessage = "Probe timed out or failed" };
            if (response.Records.Count == 0)
                Console.WriteLine($"\n❌ No A records returned ({response.Status}). Not evidence of safety — compare the typed status.");
            else
            {
                Console.WriteLine("\nResolved A records:");
                foreach (var rec in response.Records) Console.WriteLine($"  • {rec.Rdata} (TTL {rec.Ttl}s)");
            }

            var aaaaResp = await ProbeRunner.RunAsync<DNSResponse>("queryAAAA", budget, () =>
                DNSResolver.QueryUDPDetailedAsync(new DNSQuery(domain, 28, localResolver)))
                ?? new DNSResponse { Status = DNSResponseStatus.Timeout, ErrorMessage = "Probe timed out or failed" };
            if (aaaaResp.Records.Count > 0)
            {
                Console.WriteLine("Resolved AAAA records:");
                foreach (var rec in aaaaResp.Records) Console.WriteLine($"  • {rec.Rdata} (TTL {rec.Ttl}s)");
            }

            Console.WriteLine("\n• DNSSEC protocol integrity:");
            var dnssec = await DNSSECVerifier.CheckStatusAsync(domain, localResolver); // DNSSECStatus is enum (value type) — not ProbeRunner-wrappable
            Console.WriteLine($"  {DNSSECVerifier.Describe(dnssec)}");

            // Workstream H probes
            inputs.Resolution = response;
            inputs.Dnssec = dnssec;
            inputs.NxCanary = await ProbeRunner.RunAsync<DNSCanaries.CanaryResult>("nxCanary", budget, () =>
                DNSCanaries.NxdomainHijackAsync(localResolver));
            inputs.DnssecCanary = await ProbeRunner.RunAsync<DNSCanaries.CanaryResult>("dnssecCanary", budget, () =>
                DNSCanaries.DnssecControlAsync(localResolver));
            if (inputs.NxCanary is not null) Console.WriteLine($"• NXDOMAIN canary   : {inputs.NxCanary.Status} — {inputs.NxCanary.Detail}");
            if (inputs.DnssecCanary is not null) Console.WriteLine($"• DNSSEC canary     : {inputs.DnssecCanary.Status} — {inputs.DnssecCanary.Detail}");
            inputs.Latency = await ProbeRunner.RunAsync<ResolverLatency>("latency", budget, () =>
                ResolverLatencyProbe.Probe(domain: domain, qtype: 1, server: localResolver));
            inputs.Stability = await ProbeRunner.RunAsync<AnswerStability>("stability", budget, () =>
                AnswerStabilityProbe.Probe(domain: domain, qtype: 1, server: localResolver));
            inputs.WildcardSynthesis = await ProbeRunner.RunAsync<WildcardSynthesis>("wildcard", budget, () =>
                WildcardSynthesisProbe.Probe(domain: domain, resolverUnderTest: localResolver));
            inputs.Denial = await ProbeRunner.RunAsync<DenialOfExistence>("denial", budget, () =>
                DenialOfExistenceProbe.Probe(name: domain, qtype: 1, server: localResolver));
            inputs.DualStack = await ProbeRunner.RunAsync<DualStackAudit>("dualStack", budget, () =>
                DualStackProbe.Probe(domain: domain, server: localResolver));
            inputs.Edns0 = await ProbeRunner.RunAsync<EDNS0Capability>("edns0", budget, () =>
                EDNS0Probe.Probe(domain: domain, server: localResolver));
            inputs.VersionBind = await ProbeRunner.RunAsync<VersionBindResult>("versionBind", budget, () =>
                VersionBindProbe.Probe(server: localResolver));
            if (inputs.VersionBind is not null)
            {
                switch (inputs.VersionBind.Status)
                {
                    case VersionBindResultStatus.Disclosed:
                        Console.WriteLine($"\n• Resolver fingerprint (version.bind): {inputs.VersionBind.Banner ?? "—"}"
                            + (inputs.VersionBind.ForwarderOnPublicPath
                                ? "  ⚠️ a resolver expected to be public is self-identifying as local-forwarder software — something local is on the path"
                                : ""));
                        break;
                    case VersionBindResultStatus.Withheld:
                        Console.WriteLine("\n• Resolver fingerprint (version.bind): withheld (hardened / public resolver) — no signal, not a clean result on its own");
                        break;
                    default:
                        Console.WriteLine("\n• Resolver fingerprint (version.bind): could not be obtained — unmeasured, not a clean result");
                        break;
                }
            }

            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        // 2. Mirror — Tampering & Censorship Detection
        static async Task RunMirrorAsync(string target, SectionInputs inputs)
        {
            if (string.IsNullOrEmpty(target)) { Console.WriteLine("Error: Target cannot be empty."); return; }
            string domain = HostOnly(target);
            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine($"🪞 MIRROR — does what you receive match independent copies: {domain}");
            Console.WriteLine("----------------------------------------------------------------------------------");

            var servers = NetworkKeyState.GetSystemDNSServers();
            string? localResolver = servers.FirstOrDefault();
            if (localResolver == null)
            {
                // E079: refuse to substitute a public resolver — a vantage comparison against
                // 8.8.8.8 labelled as the user's resolver produces false-secure results.
                Console.WriteLine("INCONCLUSIVE: No system DNS resolvers were detected. Refusing to fall back to public resolvers.");
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }
            var budget = ProbeBudget.DefaultMirror;

            // Vantage comparison — throws on transport failure; caught inside the closure returns null.
            var result = await ProbeRunner.RunAsync<ComparisonResult>("vantage", budget, async () =>
            {
                try { return await VantageComparer.CompareVantagePointsAsync(domain, localServer: localResolver); }
                catch { throw; }
            });
            inputs.Comparison = result;
            inputs.TargetDomain = domain;   // Stage-5 answer-matrix query context (type defaults to A)

            if (result is null)
            {
                Console.WriteLine("❌ Vantage comparison failed — no result returned.");
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }

            // A2: discover the delegation via TRUSTED resolvers independent of the one under test (B031)
            var bootstrapResolvers = AuthoritativeVantage.DefaultBootstrapResolvers.Where(r => r != localResolver).ToList();
            inputs.Authoritative = await ProbeRunner.RunAsync<AuthoritativeComparison>("authoritative", budget, () =>
                AuthoritativeVantage.Compare(domain: domain, resolverAnswers: result.LocalRecords.Select(r => r.Rdata).ToList(), bootstrapResolvers: bootstrapResolvers, resolverDidFail: result.LocalOutcome.DidFail));
            // A2 Pro corroboration (E004): when the Authoritative comparison shows divergence AND the
            // user has Pro, ask the Dimension server for untampered ground truth.
            if (inputs.Authoritative?.Status == AuthComparisonStatus.Diverges)
            {
                var lic = await LicenseManager.IsFeatureUnlockedAsync(); // tuple — not ProbeRunner-wrappable
                if (lic.Unlocked)
                {
                    string? storedKey = LicenseManager.GetStoredLicenseKey();
                    inputs.ServerGroundTruth = await ProbeRunner.RunAsync<GroundTruth>("groundTruth", budget, async () =>
                        (await DimensionClient.AuthoritativeAsync(SyxxConfig.DimensionBaseURL, domain, "A", storedKey))!);
                    if (inputs.ServerGroundTruth != null)
                        Console.WriteLine("• Dimension ground truth : server-confirmed reference obtained ✅");
                }
            }
            // A3: SVCB/HTTPS record cross-vantage check.
            var resolversForA3 = new[] { localResolver, "1.1.1.1", "8.8.8.8", "9.9.9.9" }
                .Where(r => r != localResolver).Prepend(localResolver).ToList();
            inputs.Svcb = await ProbeRunner.RunAsync<SVCBFinding>("svcb", budget, () =>
                SVCBProbe.Probe(domain: domain, resolvers: resolversForA3));
            inputs.TtlAnomaly = await ProbeRunner.RunAsync<TTLAnomaly>("ttl", budget, () =>
                TTLAnomalyProbe.Probe(domain: domain, resolvers: resolversForA3));
            inputs.CnameChain = await ProbeRunner.RunAsync<CNAMEChainAudit>("cname", budget, () =>
                CNAMEChainProbe.Probe(domain: domain, resolverUnderTest: localResolver));

            string Describe(ResolverQueryOutcome o)
            {
                if (o.DidFail) return $"failed: {o.Status}";
                string ips = string.Join(", ", o.Records.Select(r => r.Rdata));
                return string.IsNullOrEmpty(ips) ? $"no A records ({o.Status})" : ips;
            }
            Console.WriteLine("Comparing local answers against public vantage points:");
            Console.WriteLine($"  - Local resolver: [{Describe(result.LocalOutcome)}]");
            foreach (var res in result.PublicResolverOutcomes.OrderBy(r => r.Key))
                Console.WriteLine($"  - {res.Key}: [{Describe(res.Value)}]");

            Console.WriteLine("\n🔮 TAMPERING ADVISORY:");
            if (result.IsInconclusive)
            {
                Console.WriteLine("⚠️  INCONCLUSIVE — no public vantage point returned a usable baseline. Not evidence of safety.");
                // E073: exit 2 (inconclusive) — already the seeded default, but explicit for clarity.
                Environment.ExitCode = 2;
            }
            else if (result.IsRedirectionDetected)
            {
                bool blocking = result.LocalRecords.Any(rec => IPClassifier.IsBlockingAnswer(rec.Rdata));
                Console.WriteLine(blocking || result.LocalRecords.Count == 0
                    ? "⚠️  CENSORED/BLOCKED — local network returned an internal/invalid IP while public servers resolved it."
                    : "🚨 MALICIOUSLY REDIRECTED — local resolver returned a real but wrong IP. DO NOT enter credentials.");
                // E073: exit 1 (threat detected) — redirection or censorship is a positive finding.
                Environment.ExitCode = 1;
            }
            else
            {
                Console.WriteLine(result.IsDegraded
                    ? "⚠️  No tampering detected, but the baseline was DEGRADED."
                    : $"✅ SECURE — no answer tampering, censorship, or redirection detected for {domain}.");
                // E073: exit 0 (clean) only when the advisory is affirmative.
                Environment.ExitCode = result.IsDegraded ? 2 : 0;
            }

            Console.WriteLine("\n• Web content integrity (signed manifest verified against your vantage):");
            var check = await LicenseManager.IsFeatureUnlockedAsync();
            if (!check.Unlocked)
                Console.WriteLine($"  🔒 {check.Message} — content mirroring is a server-anchored Pro feature ([L] to unlock).");
            else
            {
                string origin = target.Contains("://") ? target : $"https://{domain}";
                string? licenseKey = LicenseManager.GetStoredLicenseKey();
                // WARP_MANIFEST_V1: fetch the signed manifest and verify content bytes from this vantage.
                var warp = await ProbeRunner.RunAsync<WarpManifestResult>("warp", budget, () =>
                    WarpEngine.RunVantageCheckAsync(origin, SyxxConfig.DimensionBaseURL, licenseKey));
                inputs.WarpManifest = warp;
                if (warp is null)
                    Console.WriteLine($"  ❌ Content mirror failed.");
                else if (warp.Conclusion == WarpConclusion.VERIFIED_MATCH)
                    Console.WriteLine("  ✅ VERIFIED_MATCH — a pinned, fresh signed manifest matches the bytes your vantage fetched.");
                else if (warp.Conclusion == WarpConclusion.TAMPERING_DETECTED)
                    Console.WriteLine("  🚨 TAMPERING_DETECTED — a verified, fresh manifest disagrees byte-for-byte with your vantage (or the signature is invalid).");
                else
                    Console.WriteLine($"  ⚠️  INCONCLUSIVE — {warp.InconclusiveReason ?? "untrusted/unpinned signer, stale freshness, or unfetchable content"}.");
            }
            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        // 3. Portal — Network & Path Diagnostics
        static async Task RunPortalAsync(string domain, SectionInputs inputs)
        {
            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine($"🌀 PORTAL — your local network, and the path to {(string.IsNullOrEmpty(domain) ? "(no domain)" : domain)}");
            Console.WriteLine("----------------------------------------------------------------------------------");

            var resolvers = NetworkKeyState.GetSystemDNSServers();
            var interfaces = NetworkKeyState.GetActiveInterfaces();
            var vpn = NetworkKeyState.VpnStatus();
            Console.WriteLine($"• Resolvers        : {(resolvers.Count == 0 ? "(none)" : string.Join(", ", resolvers))}");
            Console.WriteLine($"• Active interfaces: {string.Join(", ", interfaces)}");
            Console.WriteLine($"• VPN              : {TriLabel(vpn.State)} ({vpn.Evidence})");

            var budget = ProbeBudget.DefaultPortal;

            var report = await NetworkKeyState.GenerateReportAsync(); // local call with internal timeouts — not ProbeRunner-wrapped
            inputs.Network = report;
            inputs.EncryptedTransport = await ProbeRunner.RunAsync<EncryptedTransport>("encryptedTransport", budget, () =>
                EncryptedTransportProbe.Probe());

            // Open Resolver & Amplification exposure — scoped to YOUR OWN network devices only.
            if (resolvers.FirstOrDefault(IsOwnNetworkAddress) is string ownResolver)
            {
                inputs.OpenResolver = await ProbeRunner.RunAsync<OpenResolverFinding>("openResolver", budget, () =>
                    OpenResolverProbe.Probe(target: ownResolver));
                switch (inputs.OpenResolver?.Status)
                {
                    case OpenResolverStatus.RecursiveHighAmp:
                        Console.WriteLine($"• Open resolver    : 🚨 {inputs.OpenResolver.Detail}"); break;
                    case OpenResolverStatus.RecursiveLowAmp:
                        Console.WriteLine($"• Open resolver    : ⚠️ {inputs.OpenResolver.Detail}"); break;
                    case OpenResolverStatus.NotRecursive:
                        Console.WriteLine($"• Open resolver    : ✓ your LAN resolver ({ownResolver}) does not openly recurse for us"); break;
                    case OpenResolverStatus.Untested:
                        Console.WriteLine($"• Open resolver    : (unmeasured — {ownResolver} did not respond)"); break;
                }
            }
            else
            {
                Console.WriteLine("• Open resolver    : (skipped — your configured resolver is public; this check applies to your own LAN devices)");
            }

            try
            {
                var snapshot = new LANCartographer().Snapshot();
                var resolverSet = new HashSet<string>(resolvers);
                // E085: surface IsPartial — a failed ARP scan must not be reported as "0 devices".
                if (snapshot.IsPartial)
                {
                    Console.WriteLine($"• On-link devices  : UNTESTED — ARP table could not be fully read ({snapshot.PartialReason ?? "unknown reason"})");
                }
                else
                {
                    Console.WriteLine($"• On-link devices  : {snapshot.Summary.DeviceCount}");
                }
                foreach (var d in snapshot.Devices)
                {
                    string flag = resolverSet.Contains(d.IPAddress) ? "  ⟵ one of your DNS resolvers" : "";
                    Console.WriteLine($"    {d.IPAddress}\t{d.MacAddress}\t[{d.AddressKind.Label()}]{flag}");
                }
            }
            catch (Exception ex) { Console.WriteLine($"• LAN scan unavailable: {ex.Message}"); }

            if (string.IsNullOrEmpty(domain) || !IsSafeTraceTarget(domain))
            {
                Console.WriteLine("\n(No valid domain given — skipping route trace.)");
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }

            Console.WriteLine($"\n• Route to {domain}:");
            // Shared traceroute wrapper (single source of truth). An empty result is an honest
            // failure/timeout — never a clean or complete path.
            var traceResult = await TracerouteRunner.TraceAsync(domain, maxHops: 15, waitMs: 1000);
            if (traceResult.Hops.Count == 0)
            {
                // C152: label the specific failure reason
                var reason = traceResult.Status switch
                {
                    TraceStatus.LaunchFailed => "could not launch",
                    TraceStatus.TruncatedByDeadline => "timed out before any hop responded",
                    _ => "produced no output"
                };
                Console.WriteLine($"    (No hops returned — the trace {reason}.)");
                if (traceResult.Diagnostic != null)
                    Console.WriteLine($"    Diagnostic: {traceResult.Diagnostic}");
            }
            else
            {
                foreach (var hop in traceResult.Hops) Console.WriteLine($"    {hop}");
                // C152: show the truncation notice if Syxx killed the trace
                if (traceResult.Status == TraceStatus.TruncatedByDeadline)
                {
                    Console.WriteLine($"    ⚠️ {traceResult.Diagnostic}");
                }
            }
            // C152: suppress the interception hint unless the trace completed naturally
            if (traceResult.Status == TraceStatus.Completed)
                Console.WriteLine("💡 If the trace stops early, the last responding hop is where interception/blocking begins.");
            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        // 4. Glutton — Privacy Leak Detection
        static async Task RunGluttonAsync(SectionInputs inputs)
        {
            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine("🍽️  GLUTTON — is your VPN/tunnel actually keeping DNS private?");
            Console.WriteLine("----------------------------------------------------------------------------------");

            var check = await LicenseManager.IsFeatureUnlockedAsync(); // tuple — not ProbeRunner-wrappable
            Console.WriteLine($"• Feature status: {check.Message}");
            if (!check.Unlocked)
            {
                Console.WriteLine("💡 Server-anchored feature. Register a license via [L] to unlock. Subscriptions are $3.95/month.");
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }

            var vpn = NetworkKeyState.VpnStatus();
            Console.WriteLine($"• VPN status: {TriLabel(vpn.State)} — {vpn.Evidence}");

            var servers = NetworkKeyState.GetSystemDNSServers();
            if (servers.Count == 0)
            {
                Console.WriteLine("⚠️  INCONCLUSIVE: No system DNS resolvers detected; refusing to fall back to public resolvers.");
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }
            var budget = ProbeBudget.DefaultGlutton;

            string subdomain = LeakDetector.GenerateTestSubdomain("leak.syxx.app");
            Console.WriteLine($"• Test label: {subdomain}");
            inputs.Ecs = await ProbeRunner.RunAsync<ECSLeak>("ecs", budget, () =>
                ECSProbe.Probe(server: servers.FirstOrDefault() ?? "1.1.1.1"));
            var results = await ProbeRunner.RunAsync<Dictionary<string, bool>>("triggerQueries", budget, () =>
                LeakDetector.TriggerQueriesAsync(subdomain, servers)) ?? new Dictionary<string, bool>();
            foreach (var res in results) Console.WriteLine($"  - Probe via {res.Key}: {(res.Value ? "sent" : "failed")}");

            if (results.Any(r => r.Value))
            {
                inputs.Leak = new LeakTestResult { Result = LeakResult.Inconclusive, ErrorReason = "Local check only; use collector for confirmed result." };
                Console.WriteLine("\n💡 This local client dispatched signed probes. Confirmation of which resolver forwarded them");
                Console.WriteLine("   requires the collector: syxx leak --collector <url> --key <license>.");
            }
            else
            {
                inputs.Leak = new LeakTestResult { Result = LeakResult.Inconclusive, ErrorReason = "All probe dispatches failed — system resolvers unreachable; the leak test did not run." };
                Console.WriteLine("\n🚨 All probe dispatches FAILED — the leak test could not run, so nothing was measured.");
                Console.WriteLine("   💡 This is NOT a clean result. Cause: your system resolvers are unreachable — no connectivity,");
                Console.WriteLine("      a VPN/firewall blocking DNS, or a captive portal intercepting it.");
                Console.WriteLine("   💡 To perfect the test: restore DNS connectivity (or disable the blocking VPN/firewall) and re-run;");
                Console.WriteLine("      add --collector <url> --key <license> for a verified result.");
            }
            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        // 5. Cage — Capture, Forensics & Attribution
        static async Task RunCageAsync(SectionInputs inputs)
        {
            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine("🪤 CAGE — what DNS traffic is flowing, and is any of it hostile?");
            Console.WriteLine("----------------------------------------------------------------------------------");

            // The chief purpose of Cage is a live DNS-query capture. Ask how long to capture FIRST, then
            // run the real port-53 capture via the privileged SyxxCage tool and feed its NDJSON into the
            // engine, so the guided result actually reflects the capture — exactly what the standalone does.
            int duration = PromptCaptureDuration();

            // Run the capture (elevating via UAC when we are not already Administrator). The child prints
            // its own live readout + forensic report. On any failure it returns null and has already
            // explained why — an unavailable capture leaves Cage UNTESTED, never falsely clean.
            string? capturePath = await RunCageCaptureAsync(duration);
            if (capturePath is null)
            {
                Console.WriteLine("----------------------------------------------------------------------------------\n");
                return;
            }

            // Feed the capture-sourced datapoints (F3/F9/exfil) into the determinative engine.
            IngestCapture(capturePath, inputs);
            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        /// Prompt for a packet-capture duration in seconds. Enter alone defaults to 60; an optional
        /// trailing "s" is accepted; values below 1 or above 9000 re-prompt.
        static int PromptCaptureDuration()
        {
            Console.WriteLine("\nAnd you are nonetheless asked before closing the cage for how long you wish it sealed. (Enter the duration of the packet capture in seconds, e.g. 15s, up to 9000s (2.5 hours). While closed, surf the web and use your apps as normal. You must produce DNS queries in order to capture and analyze them.)");
            while (true)
            {
                Console.Write("> ");
                string raw = Console.ReadLine()?.Trim() ?? "";
                if (raw.Length == 0) return 60;
                string s = raw.ToLowerInvariant();
                if (s.EndsWith("s")) s = s.Substring(0, s.Length - 1);
                s = s.Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 9000)
                    return n;
                Console.WriteLine("Please enter a whole number of seconds from 1 to 9000 (e.g. 60).");
            }
        }

        /// Locate and run the privileged SyxxCage capture for `durationSeconds`, elevating via UAC when we
        /// are not already Administrator. Returns the path to the raw NDJSON capture it produced (retained
        /// via --keep), or null when the capture could not run — in which case it has already printed an
        /// honest UNTESTED explanation. We never fabricate a result: an unavailable capture leaves Cage
        /// untested.
        static async Task<string?> RunCageCaptureAsync(int durationSeconds)
        {
            // Re-exec THIS unified binary with the `cage` subcommand. The standalone SyxxCage.exe no
            // longer exists after consolidation — the capture now lives as `syxx cage`.
            string? selfExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(selfExe))
            {
                Console.WriteLine("⚠️  Could not locate this program to launch the capture, so no packets were captured — Cage stays UNTESTED (this is NOT a clean result).");
                return null;
            }
            string capturesDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syxx", "captures");
            System.IO.Directory.CreateDirectory(capturesDir);
            // E077: restrict captures directory to owner-only on Linux.
            Syxx.Kit.SyxxPaths.EnsurePrivateDir(capturesDir);
            DateTime start = DateTime.Now;

            // D108: pass --out with OUR resolved captures directory. Without this, the elevated
            // child resolves LocalApplicationData under the elevated token (a different user
            // profile), so the child writes there while we search our own profile and find nothing.
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = selfExe,
                Arguments = $"cage --duration {durationSeconds} --keep --out \"{capturesDir}\""
            };
            if (IsElevated())
            {
                psi.UseShellExecute = false; // inherit this console for the live readout
            }
            else if (OperatingSystem.IsWindows())
            {
                Console.WriteLine("• Live packet capture needs Administrator rights — accept the elevation prompt to continue (the capture opens in an elevated window).");
                psi.UseShellExecute = true;
                psi.Verb = "runas"; // UAC elevation
            }
            else
            {
                // E076: on Linux/macOS, do not attempt self-elevation via runas (Win32Exception 1155).
                // Tell the user to re-run with sudo instead.
                Console.WriteLine("• Live packet capture needs root or CAP_NET_RAW privileges.");
                Console.WriteLine("  Run: sudo syxx cage");
                Console.WriteLine("  (Under snap: sudo snap connect syxx:network-observe && sudo syxx cage)");
                Console.WriteLine("⚠️  Live capture could not start — insufficient privileges. No packets were captured — Cage stays UNTESTED (this is NOT a clean result).");
                return null;
            }
            try
            {
                using var p = System.Diagnostics.Process.Start(psi);
                if (p is null)
                {
                    Console.WriteLine("⚠️  Live capture could not start. No packets were captured — Cage stays UNTESTED (this is NOT a clean result).");
                    return null;
                }
                await p.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                // E076: never surface a raw Win32Exception number (e.g. "Unknown error: 1155")
                // as the user-facing reason — translate it to actionable guidance.
                string reason = ex is System.ComponentModel.Win32Exception
                    ? $"elevation failed ({ex.Message}). On Linux, run: sudo syxx cage"
                    : ex.Message;
                Console.WriteLine($"⚠️  Live capture could not start: {reason}. No packets were captured — Cage stays UNTESTED (this is NOT a clean result).");
                return null;
            }

            string? path = NewestCaptureFile(capturesDir, start);
            if (path is null)
            {
                Console.WriteLine("⚠️  The capture produced no readable file, so nothing was measured — Cage stays UNTESTED (this is NOT a clean result).");
                return null;
            }
            return path;
        }

        /// Ingest a Syxx Cage Capture NDJSON file into the determinative engine, populating only the
        /// capture-sourced datapoints (F3/F9/exfil). A passive capture never yields a clean bill of health.
        static void IngestCapture(string path, SectionInputs inputs)
        {
            try
            {
                string text = System.IO.File.ReadAllText(path);
                var records = MonitorReportAnalyzer.ParseNDJSON(text);
                if (records.Count == 0)
                {
                    Console.WriteLine("⚠️  No capture records parsed from that file. Nothing was measured — NOT a clean network, just no data.");
                    return;
                }
                var capInputs = MonitorReportAnalyzer.SectionInputsFrom(records);
                inputs.StubEntropy = capInputs.StubEntropy;
                inputs.ResponseSize = capInputs.ResponseSize;
                inputs.Exfil = capInputs.Exfil;
                int outbound = records.Count(r => r.Network.Direction.Contains("outbound", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"\n📥 Ingested {records.Count} records ({outbound} queries, {records.Count - outbound} responses)");
                if (inputs.StubEntropy is StubEntropyAudit se) Console.WriteLine($"• F3 stub/OS randomization : {se.Status} — {se.Detail}");
                if (inputs.ResponseSize is ResponseSizeAudit rs) Console.WriteLine($"• F9 response size         : {rs.Status} — {rs.Detail}");
                var capConclusion = Synthesizer.Conclude(capInputs);
                var capDet = capConclusion.Determination;
                if (capDet is not null)
                {
                    var capResult = capDet.State switch
                    {
                        DeterminativeState.ThreatDetected => $"THREAT: {string.Join(", ", capDet.Diagnoses.Select(d => d.DisplayName))}",
                        DeterminativeState.Inconclusive => "INCONCLUSIVE",
                        DeterminativeState.Clean => "CLEAN",
                        // A capture that measured nothing is UNTESTED, never a bare "UNKNOWN"
                        // and never folded into INCONCLUSIVE.
                        DeterminativeState.Untested => "UNTESTED",
                        _ => "UNKNOWN",
                    };
                    Console.WriteLine($"\n📊 ENGINE CONCLUSION (capture-only): {capResult}");
                    if (capDet.Diagnoses.FirstOrDefault() is { } capTop)
                        Console.WriteLine($"   Top diagnosis: {capTop.DisplayName} ({capTop.Confidence})");
                }
                else
                {
                    Console.WriteLine("\n📊 ENGINE CONCLUSION (capture-only): UNFINISHED");
                }
                if (capConclusion.Evidence.Count == 0)
                    Console.WriteLine("   (no engine evidence surfaced from this capture)");
                else
                    foreach (var ev in capConclusion.Evidence) Console.WriteLine($"   - [{ev.Section.ToString().ToUpper()}] {ev.Observation}");
                if (capDet?.Suppressed is { Count: > 0 })
                {
                    Console.WriteLine("   ⚠️ UNVERIFIED SIGNALS:");
                    foreach (var s in capDet.Suppressed)
                        Console.WriteLine($"     - {s.DisplayName} [{s.Reason}]");
                }
                Console.WriteLine("\n💡 A passive capture carries no active cross-vantage premises (authoritative divergence,");
                Console.WriteLine("   authenticated denial, redirection, collector-confirmed leak), so those sections stay");
                Console.WriteLine("   untested — the engine cannot issue a clean bill of health from a capture alone.");
            }
            catch (Exception ex) { Console.WriteLine($"❌ Could not read report: {ex.Message}"); }
        }

        /// True when this process is running with an elevated (Administrator/root) token.
        /// E076: platform-switched — Windows uses WindowsIdentity; Linux/macOS uses geteuid().
        /// The old code threw PlatformNotSupportedException on Linux, which was caught as
        /// false — making the guided Cage always take the elevation branch on the ship-first platform.
        static bool IsElevated()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(id);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
            // Linux/macOS: check effective UID. 0 = root.
            try
            {
                return LibcNative.GetEuid() == 0;
            }
            catch { return false; }
        }

        /// Minimal libc P/Invoke for POSIX elevation checks. E076: replaces the Windows-only
        /// WindowsIdentity path that threw PlatformNotSupportedException on Linux.
        static class LibcNative
        {
            [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
            private static extern uint geteuid();

            public static uint GetEuid()
            {
                if (OperatingSystem.IsWindows()) return 0; // not meaningful on Windows
                return geteuid();
            }
        }

        /// The newest raw NDJSON capture file ("Syxx Cage Capture <stamp>.txt", excluding the human-readable
        /// "…Report…" file) written at or after `start`. Null when none is found.
        static string? NewestCaptureFile(string dir, DateTime start)
        {
            try
            {
                if (!System.IO.Directory.Exists(dir)) return null;
                return System.IO.Directory.EnumerateFiles(dir, "Syxx Cage Capture *.txt")
                    .Where(f => !System.IO.Path.GetFileName(f).Contains("Report"))
                    .Select(f => new System.IO.FileInfo(f))
                    .Where(fi => fi.LastWriteTime >= start.AddSeconds(-2)) // small clock-skew tolerance
                    .OrderByDescending(fi => fi.LastWriteTime)
                    .Select(fi => fi.FullName)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        // 6. Witness — Trust Infrastructure
        static async Task RunWitnessAsync(SectionInputs inputs)
        {
            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine("👁️  WITNESS — can this environment, and this app, be trusted?");
            Console.WriteLine("----------------------------------------------------------------------------------");

            var lic = await LicenseManager.IsFeatureUnlockedAsync();
            Console.WriteLine($"• App license : {(lic.Unlocked ? "🟢 PRO (verified with Keygen)" : "🔴 LITE")}");

            var current = NetworkConfigSnapshot.Capture();
            Console.WriteLine("• Environment snapshot:");
            Console.WriteLine($"    resolvers          : {(current.Resolvers.Count == 0 ? "(none)" : string.Join(", ", current.Resolvers))}");
            Console.WriteLine($"    active interfaces  : {string.Join(", ", current.ActiveInterfaces)}");
            Console.WriteLine($"    VPN active         : {(current.VpnActive == ConditionState.Yes ? "yes" : "no")}");
            Console.WriteLine($"    DNS encrypted      : {(current.DnsEncrypted ? "yes" : "no")}");

            var (loadResult, previous) = Sentinel.LoadSnapshot();
            if (loadResult == AnswerLedger.TrustLoadResult.Corrupted)
            {
                Console.WriteLine("• ⚠️ Prior baseline file is corrupted — cannot compare. The corrupted file has been left intact.");
            }
            else if (previous != null)
            {
                var changes = Sentinel.Diff(previous, current);
                // C148: config changes are NOT answer drifts — do not fabricate AnswerDrift records
                // from configuration changes. The ConfigChange list below already reports them.
                inputs.Drifts = new List<AnswerDrift>();
                if (changes.Count == 0)
                    Console.WriteLine("• Change since last Witness run: ✅ none — environment is stable.");
                else
                {
                    Console.WriteLine("• 🚨 Change since last Witness run — re-run Prophet/Mirror/Glutton:");
                    foreach (var c in changes) Console.WriteLine($"    • {c.Kind.Label()}: {c.Detail}");
                }
            }
            else
            {
                inputs.Drifts = new List<AnswerDrift>();
                Console.WriteLine("• No prior baseline — this snapshot is now the trusted baseline.");
            }
            Sentinel.SaveSnapshot(current);
            Console.WriteLine("\n💡 To sign a tamper-evident evidence snapshot of a comparison, use: syxx witness create <domain> --key <key>");
            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        // Shared helpers
        static string TriLabel(ConditionState s) => s switch
        {
            ConditionState.Yes => "🔴 active/yes",
            ConditionState.No => "🟢 inactive/no",
            _ => "❓ unknown"
        };

        static string HostOnly(string target)
        {
            string s = target;
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            return s;
        }

        static bool IsSafeTraceTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.StartsWith("-") || target.Any(char.IsWhiteSpace)) return false;
            if (target.EndsWith(".")) target = target.Substring(0, target.Length - 1);
            return IPAddress.TryParse(target, out _) || Uri.CheckHostName(target) == UriHostNameType.Dns;
        }

        /// True when `ip` is one of the user's OWN network addresses — the only resolvers the open-resolver
        /// probe is allowed to target. Public resolvers are out of scope.
        static bool IsOwnNetworkAddress(string ip)
        {
            var kind = IPClassifier.Classify(ip);
            return kind is IPAddressClass.PrivateUse or IPAddressClass.Loopback or IPAddressClass.LinkLocal;
        }
    }
}
