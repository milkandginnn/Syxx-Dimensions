using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx cage` — port-53 DNS packet capture (requires Administrator). Body moved verbatim
    /// from the former SyxxCage tool; args[0] is the first real argument (the subcommand token was
    /// stripped by the dispatcher). The guided flow re-execs this unified binary with `cage`.
    ///
    /// <para><b>AUGUST 2026 refactor</b> — the raw-socket open + per-packet parse-build core now
    /// lives in <see cref="Syxx.Kit.CaptureEngine"/> (see
    /// <c>Windows/dotNET/Syxx.Kit/Capture/CaptureEngine.cs</c>) so the same capture core powers the
    /// CLI, the upcoming systemd daemon, and GUI clients of that daemon. This file is the
    /// PRESERVED FALLBACK path — when the daemon socket is not present the GUI spawns this CLI
    /// exactly as before. Behavior is byte-identical to the pre-refactor CLI: same console output
    /// strings, same NDJSON file format, same duration/grace timing, same report-generation pass,
    /// same raw-capture erasure policy. Only the capture core moved out.</para></summary>
    internal static class CageCommand
    {
        public static async Task RunAsync(string[] args)
        {
            if (SyxxConfig.HandleGlobalFlags(args, "syxx cage",
                "Usage: syxx cage [--duration <seconds>] [--keep]   (captures port-53 DNS packets; requires administrator)")) return;
            double duration = 10.0; // Default 10 seconds for quick run
            bool keepRaw = false; // --keep retains the raw NDJSON capture (it embeds process paths)
            string? explicitOutDir = null; // D108: launcher-supplied output dir (avoids cross-token mismatch)
            int idx = 0;
            while (idx < args.Length)
            {
                if (args[idx] == "--duration" || args[idx] == "-d")
                {
                    // F028: double.TryParse accepts "NaN"/"Infinity", and AddSeconds(NaN) throws —
                    // a crafted duration crashed the capture. Only a finite, positive, bounded
                    // value is accepted; anything else keeps the default (same silent-keep
                    // behavior as an unparseable value). 86400s = 24h capture ceiling.
                    if (idx + 1 < args.Length && double.TryParse(args[idx + 1], out double val)
                        && double.IsFinite(val) && val > 0 && val <= 86400)
                    {
                        duration = val;
                        idx++;
                    }
                }
                else if (args[idx] == "--keep") { keepRaw = true; }
                // D108: --out <dir> lets the launcher (GUI or CLI parent) pass its own resolved
                // captures directory. Without this, an elevated child resolves LocalApplicationData
                // under the elevated token (a different user profile), so writes go to the admin's
                // profile while the launcher tails the original user's profile — silent mismatch.
                else if (args[idx] == "--out" && idx + 1 < args.Length)
                {
                    explicitOutDir = args[idx + 1];
                    idx++;
                }
                idx++;
            }

            var startTime = DateTime.Now;
            // D101: invariant culture — non-Gregorian calendars (th-TH Buddhist, ar-SA Umm al-Qura)
            // would otherwise produce unparseable filenames and timestamps.
            string dateStamp = startTime.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

            string outputFilename = $"Syxx Cage Capture {dateStamp}.txt";
            string reportFilename = $"Syxx Cage Capture Report {dateStamp}.txt";
            string queriesFilename = $"Syxx Cage Queries {dateStamp}.json";

            // D108: prefer the launcher-supplied output directory. When running elevated under a
            // different token (UAC over-the-shoulder), LocalApplicationData resolves to the
            // administrator's profile — the launcher's tail would never see the file.
            string capturesDir = explicitOutDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syxx", "captures");
            Directory.CreateDirectory(capturesDir);
            // E077: on Linux the umask default creates 0755 dirs / 0644 files — world-readable.
            // The capture embeds process paths and every DNS query; restrict to owner-only.
            Syxx.Kit.SyxxPaths.EnsurePrivateDir(capturesDir);
            string outputFilePath = Path.Combine(capturesDir, outputFilename);
            string reportFilePath = Path.Combine(capturesDir, reportFilename);
            string queriesFilePath = Path.Combine(capturesDir, queriesFilename);

            Console.WriteLine("==================================================");
            Console.WriteLine("           Syxx Cage DNS Query Capturer           ");
            Console.WriteLine("==================================================");
            Console.WriteLine("Target: Sniffing port 53 DNS Queries (IPv4 + IPv6)...");
            Console.WriteLine($"Duration: {duration} seconds");
            Console.WriteLine($"Output logs will be written to: {outputFilePath}");

            // ──────────────────────────────────────────────────────────────────────────
            // Open the raw sockets via the engine. The CLI preserves the EXACT degraded-state
            // warning text the original emitted (F029 parity) — the engine surfaces failure
            // structurally so the CLI can format it without re-deriving from a caught exception.
            // ──────────────────────────────────────────────────────────────────────────
            var sockets = CaptureEngine.OpenSockets();
            if (sockets.V4Only)
            {
                Console.WriteLine("\n[WARNING] IPv6 raw socket could not be opened — capture is IPv4-only.");
                Console.WriteLine("    IPv6 DNS traffic (increasingly the default on modern networks) will NOT be captured.");
                Console.WriteLine("    Result: DEGRADED — IPv6 coverage missing.");
                if (!string.IsNullOrEmpty(sockets.Failure?.Message))
                    Console.WriteLine($"    Reason: {sockets.Failure.Message}");
            }
            else if (sockets.V6Only)
            {
                Console.WriteLine("\n[WARNING] IPv4 raw socket could not be opened — capture is IPv6-only.");
                Console.WriteLine("    IPv4 DNS traffic will NOT be captured.");
                Console.WriteLine("    Result: DEGRADED — IPv4 coverage missing.");
                if (!string.IsNullOrEmpty(sockets.Failure?.Message))
                    Console.WriteLine($"    Reason: {sockets.Failure.Message}");
            }
            else if (sockets.BothFailed)
            {
                Console.WriteLine("\n[ERROR] Could not open raw socket for DNS packet capture.");
                // E075: platform-aware guidance — the old message only mentioned Windows
                // Administrator and was unexecutable on Linux.
                if (OperatingSystem.IsWindows())
                {
                    Console.WriteLine("    This usually requires Administrator privileges. Open Command Prompt/PowerShell as Administrator and run: syxx cage");
                }
                else
                {
                    Console.WriteLine("    This requires root or CAP_NET_RAW. Run: sudo syxx cage");
                    Console.WriteLine("    (Under snap: sudo snap connect syxx:network-observe && sudo syxx cage)");
                }
                Console.WriteLine("    If you are already elevated, check the error details above for the specific cause.");
                if (!string.IsNullOrEmpty(sockets.Failure?.Message))
                    Console.WriteLine($"    Reason: {sockets.Failure.Message}");
                // E056: under snap confinement, raw sockets require network-observe which is not
                // auto-connected. Detect the snap environment and surface the specific remedy so
                // the user does not see an empty-but-successful capture.
                if (Environment.GetEnvironmentVariable("SNAP") != null)
                {
                    Console.WriteLine("    Snap detected: raw-socket capture requires the network-observe interface.");
                    Console.WriteLine("    Run: sudo snap connect syxx:network-observe");
                    Console.WriteLine("    Result: inconclusive — capture not permitted by confinement.");
                }
                return;
            }

            // E047: defer the [SUCCESS] banner until the first packet is actually captured,
            // mirroring macOS SyxxCage.swift:139-141 which gates the identical string behind
            // `if !captured`. Printing before any packet is read is an eager-success claim
            // ("Capturing packet streams..." asserted before any stream exists) of the exact
            // family the honesty spine names. A `captured` flag set inside the receive branch
            // ensures the banner prints at most once, after real evidence arrives.
            int captured = 0;  // 0 = no packet yet, 1 = first packet seen (thread-safe via Interlocked)
            int totalPackets = 0;

            // Stream each captured record to disk immediately instead of accumulating the whole
            // capture in memory and writing once at the end (W31) — a crash mid-capture no
            // longer loses everything, and memory stays bounded regardless of capture length.
            using var outputStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            // E077: restrict capture file to owner-only before the first byte is written.
            Syxx.Kit.SyxxPaths.RestrictToOwner(outputFilePath);
            using var writer = new StreamWriter(outputStream);
            var writeLock = new object();

            void WriteLine(string line)
            {
                lock (writeLock)
                {
                    writer.WriteLine(line);
                    writer.Flush();
                }
            }

            // ──────────────────────────────────────────────────────────────────────────
            // Drain the engine's record stream. Per-record console output + file write + the
            // deferred [SUCCESS] banner all live HERE — the engine produces records; the CLI
            // owns presentation. Byte-identical to the former CaptureLoop body.
            // ──────────────────────────────────────────────────────────────────────────
            using var session = CaptureEngine.Start(sockets, TimeSpan.FromSeconds(duration));
            await foreach (var record in session.Records)
            {
                // E047: print the deferred [SUCCESS] banner on the first real DNS packet, not
                // before. The engine yields records in arrival order from both sockets merged.
                if (Interlocked.CompareExchange(ref captured, 1, 0) == 0)
                {
                    Console.WriteLine("\n[SUCCESS] Sniffer started. Capturing packet streams...");
                }
                Interlocked.Increment(ref totalPackets);

                string jsonLine = JsonSerializer.Serialize(record);
                string procLabel = record.Process.Name ?? "unknown";
                string qName = record.Packet.Questions.Count > 0 ? record.Packet.Questions[0].QuestionName : "unknown";
                string direction = record.Network.Direction;
                Console.WriteLine($"  • [{record.Timestamp}] {direction}: {qName} (via {procLabel} | Rcode: {record.Packet.Rcode})");
                WriteLine(jsonLine);
            }

            var outcome = await session.Outcome;
            bool captureAborted = outcome is EngineOutcome.Aborted;
            if (outcome is EngineOutcome.Aborted aborted)
            {
                Console.WriteLine($"Capture loop interrupted: {aborted.Reason}");
            }

            // E047: if zero DNS packets were captured, print an explicit honest signal — parity
            // with macOS SyxxCage.swift:152-153 which prints "No packets captured in N second(s)."
            // A silent fallthrough to report generation with no console indication would leave the
            // user uncertain whether the capture ran at all. The report generator already renders
            // STATUS: INCONCLUSIVE honestly on 0 records; this is the console-level counterpart.
            if (Volatile.Read(ref captured) == 0 && !captureAborted)
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                Console.WriteLine($"\n⚠️  No packets captured in {elapsed:F0} second(s).");
            }

            if (captureAborted)
            {
                Console.WriteLine("\n⚠️  CAPTURE ABORTED — one or more capture loops failed before the timer elapsed.");
                Console.WriteLine("   Partial artifacts have been preserved, but the report below is INCONCLUSIVE.");
                Console.WriteLine("   The capture was interrupted, not completed — treat all findings as incomplete.\n");
            }

            Console.WriteLine("\n--------------------------------------------------");
            Console.WriteLine("🔎 GENERATING FORENSIC INTERPRETATION REPORT...");
            Console.WriteLine("--------------------------------------------------");

            // Close the capture file before reporting/deleting — Windows will not delete a file
            // that still has an open handle. (Idempotent: the using-scope dispose is safe.)
            writer.Flush();
            writer.Dispose();
            outputStream.Dispose();
            var reportOutput = DNSCaptureReportGenerator.GenerateReport(
                outputFilePath,
                outputFilePath,
                reportFilePath,
                startTime,
                duration
            );

            Console.WriteLine(reportOutput.Report);

            // C037: Only claim the report was saved when the write actually succeeded.
            // Previously this was printed unconditionally, even when the write was silently
            // swallowed — a critical honesty violation (DELTAS.md §5).
            switch (reportOutput.WriteResult)
            {
                case DNSCaptureReportGenerator.ReportWriteResult.Success:
                    Syxx.Kit.SyxxPaths.RestrictToOwner(reportFilePath);
                    Console.WriteLine($"✅ Diagnosis Report Saved (per-user): {reportFilePath}");
                    break;
                case DNSCaptureReportGenerator.ReportWriteResult.Failure:
                    Console.WriteLine($"⚠️  Report write FAILED: {reportOutput.WriteError}");
                    Console.WriteLine("    The report content was printed above; saving to a fallback path...");
                    var fallbackPath = reportFilePath + ".fallback";
                    try { File.WriteAllText(fallbackPath, reportOutput.Report); Syxx.Kit.SyxxPaths.RestrictToOwner(fallbackPath); Console.WriteLine($"    Fallback report saved (per-user): {fallbackPath}"); }
                    catch { Console.WriteLine("    Fallback write also failed; the report content above is your only record."); }
                    break;
                case DNSCaptureReportGenerator.ReportWriteResult.Unknown:
                    Console.WriteLine("⚠️  Report write status UNKNOWN; attempting to confirm file presence...");
                    if (File.Exists(reportFilePath))
                        Console.WriteLine($"    Report file exists (per-user): {reportFilePath}");
                    else
                        Console.WriteLine("    Report file NOT found; the content printed above is your only record.");
                    break;
            }

            // Raw-data companion (interpretation first, raw evidence second): the user's own captured DNS
            // queries as JSON — a privacy-considered view (process NAME only, no path), always kept, so it
            // survives even when the full forensic dump (with process paths) is erased below.
            bool queriesSaved = false;
            var queriesJson = DNSQueryExport.ArrayJsonFromCaptureFile(outputFilePath);
            if (queriesJson != null)
            {
                try { File.WriteAllText(queriesFilePath, queriesJson); Syxx.Kit.SyxxPaths.RestrictToOwner(queriesFilePath); queriesSaved = true; } catch { }
            }

            // The raw NDJSON holds full process paths (which embed the username) plus every packet.
            // Erase it once the report exists, unless the operator explicitly asked to keep it.
            if (keepRaw)
            {
                Console.WriteLine($"\n🗄  Full forensic capture retained (contains process paths): {outputFilePath}");
            }
            else
            {
                // C039: Verify absence after deletion and report success/failure.
                // Previously `try { } catch { }` swallowed the error and printed "erased"
                // unconditionally — a privacy/honesty violation: sensitive raw capture
                // could remain on disk after an explicit erasure claim.
                bool deletionSucceeded = false;
                try { File.Delete(outputFilePath); deletionSucceeded = true; }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n⚠️  Raw capture deletion FAILED: {ex.Message}");
                    Console.WriteLine($"    The capture file remains on disk: {outputFilePath}");
                    Console.WriteLine("    Remove it manually or check permissions.");
                }
                if (deletionSucceeded && !File.Exists(outputFilePath))
                {
                    Console.WriteLine("\n🧹 Full forensic capture erased after reporting (pass --keep to retain it).");
                }
                else if (deletionSucceeded)
                {
                    Console.WriteLine($"\n⚠️  Deletion returned success but file still exists: {outputFilePath}");
                    Console.WriteLine("    This may indicate a filesystem or permission issue.");
                }
            }
            if (queriesSaved)
            {
                Console.WriteLine($"🗂  Your raw queries saved as JSON (per-user): {queriesFilePath}");
            }
            Console.WriteLine("==================================================");
        }
    }
}