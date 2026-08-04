using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// <summary>
    /// Why a trace ended. Callers MUST distinguish these — a truncated trace is not a completed
    /// trace, and the interception hint must NOT be shown for a trace Syxx itself killed.
    /// </summary>
    public enum TraceStatus { Completed, TruncatedByDeadline, LaunchFailed, NoOutput }

    /// <summary>
    /// The outcome of one traceroute. `Hops` may be non-empty even for non-Completed statuses
    /// (a deadline truncation preserves partial hops — real evidence, just incomplete).
    /// </summary>
    public sealed record TraceResult(
        IReadOnlyList<string> Hops,
        TraceStatus Status,
        string? Diagnostic);

    /// <summary>
    /// Single source of truth for the system traceroute wrapper shared by the <c>syxx route</c>
    /// command and the guided Portal flow (and, on a Windows host, the GUI Portal surface). Runs
    /// <c>tracert</c> on Windows, <c>traceroute</c> on Linux/macOS, and returns the real,
    /// trimmed, non-empty output lines.
    ///
    /// Honesty: an empty return means the tool could not launch, timed out before any hop, or
    /// produced no output. Callers MUST treat an empty result as a failure/inconclusive path,
    /// never as a clean or complete trace. Partial output (some hops before a deadline) is
    /// preserved and returned — a partial path is still real evidence.
    /// </summary>
    public static class TracerouteRunner
    {
        /// <param name="target">Hostname or IP to trace (callers are expected to validate it first).</param>
        /// <param name="maxHops">Maximum TTL (<c>-h</c>). Default 30.</param>
        /// <param name="waitMs">Per-probe wait in milliseconds (<c>-w</c>) so a silent hop does not
        /// stall the trace. Default 1000.</param>
        /// <returns>A TraceResult distinguishing completed from truncated from failed traces.</returns>
        public static async Task<TraceResult> TraceAsync(string target, int maxHops = 30, int waitMs = 1000)
        {
            var lines = new List<string>();
            try
            {
                var (fileName, args) = GetTracerouteCommand(target, maxHops, waitMs);
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // C152: capture stderr for diagnostics
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process is null)
                    return new TraceResult(lines, TraceStatus.LaunchFailed, "Could not launch traceroute");

                // Overall deadline so a wall of unresponsive hops cannot hang the caller. Each probe
                // is already capped at waitMs; allow three probes per hop plus a small slack. Stream
                // hops as they arrive so partial output survives a timeout (never erase real hops).
                long deadlineMs = (long)maxHops * waitMs * 3 + 3000;
                var deadline = Task.Delay(TimeSpan.FromMilliseconds(deadlineMs));
                bool truncatedByDeadline = false;
                while (true)
                {
                    var readLine = process.StandardOutput.ReadLineAsync();
                    if (await Task.WhenAny(readLine, deadline) == deadline)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        truncatedByDeadline = true;
                        break; // partial hops already collected in `lines`
                    }
                    string? line = await readLine;
                    if (line == null) break; // EOF: tracert finished
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) lines.Add(trimmed);
                }

                // C152: read any stderr diagnostics (e.g. "Unable to resolve target system name")
                string? stderr = null;
                try { stderr = await process.StandardError.ReadToEndAsync(); } catch { }
                if (string.IsNullOrWhiteSpace(stderr)) stderr = null;

                if (lines.Count == 0)
                    return new TraceResult(lines, truncatedByDeadline ? TraceStatus.TruncatedByDeadline : TraceStatus.NoOutput, stderr);
                if (truncatedByDeadline)
                    return new TraceResult(lines, TraceStatus.TruncatedByDeadline,
                        $"Syxx stopped the trace after {deadlineMs / 1000} seconds — the path beyond hop {lines.Count} was not measured");
                return new TraceResult(lines, TraceStatus.Completed, stderr);
            }
            catch (Exception ex)
            {
                return new TraceResult(lines, TraceStatus.LaunchFailed, ex.Message);
            }
        }

        /// <summary>
        /// Selects the platform's traceroute binary and builds its argument string.
        ///
        /// This assembly ships on Windows AND Linux (the Ubuntu Avalonia client and the
        /// linux-x64/linux-arm64 CLI RIDs both reference Syxx.Kit), so the binary cannot be
        /// hardcoded: `tracert` does not exist on Linux and the call would fail to launch,
        /// returning an empty list that a caller could mistake for a completed trace.
        ///
        /// UNIT MISMATCH, and the reason this is a method rather than an inline ternary:
        /// `tracert -w` takes MILLISECONDS, `traceroute -w` takes SECONDS. Passing the raw
        /// millisecond value to traceroute would set a per-probe wait of 1000 SECONDS and hang
        /// the trace far past the caller's deadline. The value is converted here, with a floor
        /// of 1 second because traceroute rejects 0.
        /// </summary>
        private static (string fileName, string args) GetTracerouteCommand(
            string target, int maxHops, int waitMs)
        {
            if (OperatingSystem.IsWindows())
            {
                // -d: skip reverse-DNS on each hop (faster, and avoids a second lookup path
                // that would itself be subject to the tampering this tool is investigating).
                return ("tracert", $"-d -h {maxHops} -w {waitMs} {target}");
            }

            // Linux and macOS. -n is the equivalent of Windows' -d.
            int waitSeconds = Math.Max(1, waitMs / 1000);
            return ("traceroute", $"-n -m {maxHops} -w {waitSeconds} {target}");
        }
    }
}
