// Offline analyzer for endpoint file/process activity logs (mirrors macOS
// SyxxKit/Forensics/EndpointLogAnalyzer.swift). Line-delimited Endpoint Security JSON.
// Streams by path (StreamReader) with bounded memory — file-activity logs are multi-GB and
// ~99% OPEN/CLOSE reads, which are substring-skipped before any JSON parse.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Syxx.Kit
{
    public static class EndpointLogAnalyzer
    {
        private static readonly HashSet<string> TrustedSigners = new(StringComparer.OrdinalIgnoreCase)
            { "Microsoft Windows", "Microsoft Corporation", "Microsoft® Windows® Operating System",
              "Microsoft® Windows®", "Microsoft", "Microsoft®", "Windows Admin", "Windows® Admin" };

        // ── Windows persistence / unusual-exec markers (C053) ──────────────────────
        private static readonly string[] WindowsPersistenceMarkers =
            { @"\Microsoft\Windows\Start Menu\Programs\Startup", @"\Startup\",
              @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
              @"\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
              @"\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
              @"\CurrentVersion\Run", @"\CurrentVersion\RunOnce",
              @"\Windows\Tasks\", @"\system32\tasks\", @"\scheduletasks\",
              @"\services\",
              @"\Windows\Debug\", @"\Windows\Dump\", @"\Windows\INF\",
              @"\AppData\Roaming\Microsoft\Windows\Start Menu",
              @"\AppData\Roaming\Microsoft\Internet Explorer\Quick Launch" };
        private static readonly string[] WindowsUnusualExecMarkers =
            { @"\Temp\", @"\Temporary", @"\Downloads\", @"\AppData\Local\Temp\",
              @"\AppData\LocalLow\Temp\", @"%TEMP%", @"%TMP%",
              @"\Windows\Temp\", @"\ProgramData\Temp\" };

        // ── Linux persistence / unusual-exec markers (C053) ────────────────────────
        private static readonly string[] LinuxPersistenceMarkers =
            { "/etc/systemd/system/", "/.config/systemd/user/", "/etc/cron.d/",
              "/etc/rc.local", "/etc/init.d/", "/etc/supervisor/conf.d/",
              "/.bashrc", "/.profile", "/etc/cron.daily/", "/etc/cron.hourly/",
              "/etc/crontab", "/var/spool/cron/" };
        private static readonly string[] LinuxUnusualExecMarkers =
            { "/tmp/", "/dev/shm/", "/.local/bin/" };

        // ── Platform-selected markers (C053) ───────────────────────────────────────
        private static readonly bool IsLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static string[] ActivePersistenceMarkers =>
            IsLinux ? LinuxPersistenceMarkers : WindowsPersistenceMarkers;
        private static string[] ActiveUnusualExecMarkers =>
            IsLinux ? LinuxUnusualExecMarkers : WindowsUnusualExecMarkers;

        // C054: All markers are lowercased so the Ordinal comparison against a
        //       lowercased haystack can never miss a mixed-case needle.
        private static readonly string[] SuspiciousArgMarkers =
            { "base64", "curl ", "wget ", "| powershell", "|pwsh", " -enc ", " -e ", " -command ",
              " -c ", "invoke-", "iex(", "iex ", " eval ", "downloadstring", "downloadfile",
              "start-process", " -windowstyle hidden", " -executionpolicy bypass",
              " /c ", " cmd ", " mshta ", " wscript ", " cscript ", " regsvr32 ",
              " certutil ", " bitsadmin ", " powershell ", "pwsh " };

        public static string? Analyze(string path) => Detect(path) switch
        {
            "file" => AnalyzeFile(path),
            "process" => AnalyzeProcess(path),
            _ => null
        };

        public static string? Detect(string path)
        {
            try
            {
                using var reader = new StreamReader(path);
                string? line;
                // C035: separate budgets — invalid/non-JSON lines get their own counter so that
                // a hostile prefix of garbage lines cannot consume the valid-record budget and
                // suppress all later evidence. 400 valid lines OR 4000 raw lines, whichever first.
                int validScanned = 0;
                int rawScanned = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (rawScanned++ > 4000) break;
                    var l = line.Trim();
                    if (l.Length == 0 || !l.StartsWith("{", StringComparison.Ordinal)) continue;
                    if (l.Contains("\"file\":{", StringComparison.Ordinal) || l.Contains("\"file\": {", StringComparison.Ordinal)) return "file";
                    if (l.Contains("ES_EVENT_TYPE_NOTIFY_EXEC", StringComparison.Ordinal) || l.Contains("ES_EVENT_TYPE_NOTIFY_FORK", StringComparison.Ordinal)
                        || l.Contains("ES_EVENT_TYPE_NOTIFY_EXIT", StringComparison.Ordinal)) return "process";
                    if (validScanned++ > 400) break;
                }
            }
            catch { }
            return null;
        }

        // ---- helpers ----

        private static void Bump(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;

        private static string? Str(JsonElement e, string prop) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static (string Signer, int Status) Signing(JsonElement actor)
        {
            if (actor.ValueKind == JsonValueKind.Object && actor.TryGetProperty("signing info (computed)", out var c) && c.ValueKind == JsonValueKind.Object)
            {
                string signer = c.TryGetProperty("signatureSigner", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "Unknown";
                int status = c.TryGetProperty("signatureStatus", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : -1;
                return (signer, status);
            }
            return ("Unknown", -1);
        }

        private static bool Untrusted(string signer, int status) => status != 0 || !TrustedSigners.Contains(signer);
        private static string ShortEvent(string e) => e.Replace("ES_EVENT_TYPE_NOTIFY_", "", StringComparison.Ordinal);

        private static string DirOf(string p)
        {
            int i = p.LastIndexOfAny(new[] { '/', '\\' });
            return i > 0 ? p.Substring(0, i) : p;
        }

        private static List<(string Name, int Count)> Top(Dictionary<string, int> d, int n) =>
            d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n).Select(kv => (kv.Key, kv.Value)).ToList();

        private static void Section(List<string> o, string title, List<(string Name, int Count)> pairs)
        {
            o.Add(title); o.Add(new string('-', title.Length));
            if (pairs.Count == 0) o.Add("  none observed");
            else
            {
                int w = pairs.Max(p => p.Count.ToString(CultureInfo.InvariantCulture).Length);
                foreach (var p in pairs) o.Add($"  {p.Count.ToString(CultureInfo.InvariantCulture).PadLeft(w)}  {p.Name}");
            }
            o.Add("");
        }

        private static void Lst(List<string> o, string title, List<string> items, int limit = 25)
        {
            o.Add(title); o.Add(new string('-', title.Length));
            if (items.Count == 0) o.Add("  none observed");
            else foreach (var it in items.Take(limit)) o.Add($"  {it}");
            if (items.Count > limit) o.Add($"  … and {items.Count - limit} more");
            o.Add("");
        }

        // ---- File events ----

        public static string AnalyzeFile(string path)
        {
            int openClose = 0, mutations = 0;
            var eventTypes = new Dictionary<string, int>();
            var byProcess = new Dictionary<string, int>();
            var byDir = new Dictionary<string, int>();
            var untrustedWriters = new Dictionary<string, int>();
            var persistence = new HashSet<string>();

            using (var reader = new StreamReader(path))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var l = line.Trim();
                    if (l.Length == 0) continue;
                    if (l.Contains("NOTIFY_OPEN", StringComparison.Ordinal) || l.Contains("NOTIFY_CLOSE", StringComparison.Ordinal)) { openClose++; continue; }
                    try
                    {
                        using var doc = JsonDocument.Parse(l);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("event", out var ev) || !root.TryGetProperty("file", out var file)) continue;
                        mutations++;
                        string evt = ShortEvent(ev.GetString() ?? "");
                        Bump(eventTypes, evt);
                        string dest = Str(file, "destination") ?? Str(file, "source") ?? "(unknown)";
                        JsonElement actor = file.TryGetProperty("process", out var p) ? p : default;
                        string procName = Str(actor, "name") ?? "unknown";
                        Bump(byProcess, procName);
                        Bump(byDir, DirOf(dest));
                        var (signer, status) = Signing(actor);
                        if (Untrusted(signer, status)) Bump(untrustedWriters, $"{procName} [{signer}]");
                        // B095: Windows paths are case-insensitive — use OrdinalIgnoreCase so
                        // a log entry with different casing still matches persistence markers.
                        if (ActivePersistenceMarkers.Any(m => dest.Contains(m, StringComparison.OrdinalIgnoreCase)))
                            persistence.Add($"{evt}  {dest}   ⟵ {procName} [{signer}]");
                    }
                    catch { }
                }
            }

            var o = new List<string>
            {
                "Syxx Endpoint Analysis — File events",
                "====================================",
                "",
                $"Source: {path}",
                $"Read events skipped (OPEN/CLOSE): {openClose}",
                $"Mutation events analyzed (CREATE/WRITE/DELETE/RENAME/LINK): {mutations}",
                ""
            };
            Section(o, "Mutation Types", Top(eventTypes, 10));
            Section(o, "Top Mutating Processes", Top(byProcess, 20));
            Lst(o, "⚠️  Writes to Persistence Locations (LaunchAgents/Daemons/cron/etc.)", persistence.OrderBy(x => x, StringComparer.Ordinal).ToList());
            Section(o, "⚠️  Untrusted / Ad-hoc Signers That Modified Files", Top(untrustedWriters, 20));
            Section(o, "Top Written Directories", Top(byDir, 15));
            o.Add("Interpretation");
            o.Add("--------------");
            var interp = new List<string> { $"Analyzed {mutations} file-mutation event(s); ignored {openClose} read-only OPEN/CLOSE events." };
            if (persistence.Count > 0) interp.Add($"{persistence.Count} write(s) landed in persistence locations — inspect the process + file; this is where launch-persistence installs.");
            if (untrustedWriters.Count > 0) interp.Add($"{untrustedWriters.Count} untrusted/ad-hoc signer(s) modified files — confirm each is expected; Microsoft-signed writers are the baseline.");
            if (persistence.Count == 0 && untrustedWriters.Count == 0)
            {
                if (ActivePersistenceMarkers.Length > 0)
                    interp.Add("No writes to persistence locations and no untrusted signers — nothing stands out at the heuristic level.");
                else
                    interp.Add("No untrusted signers — nothing stands out at the heuristic level (persistence markers not available on this platform).");
            }
            foreach (var i in interp) o.Add($"- {i}");
            o.Add("");
            o.Add("Notes\n-----");
            o.Add("- Deterministic heuristic summary; not a malware conclusion. Persistence-path + signer are the strongest first signals.");
            o.Add("- Reproducible and signable with syxx-witness.");
            return string.Join("\n", o);
        }

        // ---- Process events ----

        public static string AnalyzeProcess(string path)
        {
            int execs = 0, forks = 0, exits = 0;
            var byProcess = new Dictionary<string, int>();
            var untrustedExecs = new HashSet<string>();
            var unusualPathExecs = new HashSet<string>();
            var suspiciousArgExecs = new List<string>();

            using (var reader = new StreamReader(path))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var l = line.Trim();
                    if (l.Length == 0) continue;
                    if (l.Contains("NOTIFY_FORK", StringComparison.Ordinal)) { forks++; continue; }
                    if (l.Contains("NOTIFY_EXIT", StringComparison.Ordinal)) { exits++; continue; }
                    if (!l.Contains("NOTIFY_EXEC", StringComparison.Ordinal)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(l);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("process", out var actor)) continue;
                        execs++;
                        string procPath = Str(actor, "path") ?? "(unknown)";
                        string procName = Str(actor, "name") ?? (procPath.Contains('/', StringComparison.Ordinal) ? procPath.Substring(procPath.LastIndexOf('/') + 1) : procPath);
                        Bump(byProcess, procName);
                        var (signer, status) = Signing(actor);
                        if (Untrusted(signer, status)) untrustedExecs.Add($"{procPath} [{signer}]");
                        // B095: Windows paths are case-insensitive — use OrdinalIgnoreCase.
                        if (ActiveUnusualExecMarkers.Any(m => procPath.Contains(m, StringComparison.OrdinalIgnoreCase))) unusualPathExecs.Add($"{procPath} [{signer}]");
                        if (actor.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Array)
                        {
                            var joined = string.Join(" ", args.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.String).Select(a => a.GetString())).ToLowerInvariant();
                            if (SuspiciousArgMarkers.Any(m => joined.Contains(m, StringComparison.Ordinal)))
                                suspiciousArgExecs.Add($"{procName}: {(joined.Length > 120 ? joined.Substring(0, 120) : joined)}");
                        }
                    }
                    catch { }
                }
            }

            var o = new List<string>
            {
                "Syxx Endpoint Analysis — Process events",
                "=======================================",
                "",
                $"Source: {path}",
                $"Launches (EXEC): {execs}   forks: {forks}   exits: {exits}",
                ""
            };
            Section(o, "Top Launched Processes", Top(byProcess, 20));
            Lst(o, "⚠️  Untrusted / Ad-hoc Signed Launches", untrustedExecs.OrderBy(x => x, StringComparer.Ordinal).ToList());
            Lst(o, "⚠️  Launches From Unusual Paths (/tmp, Downloads, Caches)", unusualPathExecs.OrderBy(x => x, StringComparer.Ordinal).ToList());
            Lst(o, "⚠️  Launches With Suspicious Arguments", suspiciousArgExecs);
            o.Add("Interpretation");
            o.Add("--------------");
            var interp = new List<string> { $"Observed {execs} process launch(es)." };
            if (untrustedExecs.Count > 0) interp.Add($"{untrustedExecs.Count} distinct untrusted/ad-hoc launch path(s) — anything not Microsoft-signed executing deserves a look.");
            if (unusualPathExecs.Count > 0) interp.Add("Executions from Temp or Downloads folders observed — classic drop-and-run locations.");
            if (suspiciousArgExecs.Count > 0) interp.Add("Launch arguments contained download/eval/base64/powershell patterns — inspect the full command lines.");
            if (untrustedExecs.Count == 0 && unusualPathExecs.Count == 0 && suspiciousArgExecs.Count == 0)
            {
                if (ActiveUnusualExecMarkers.Length > 0)
                    interp.Add("No untrusted signers, odd paths, or suspicious arguments — nothing stands out at the heuristic level.");
                else
                    interp.Add("No untrusted signers or suspicious arguments — nothing stands out at the heuristic level (unusual-path markers not available on this platform).");
            }
            foreach (var i in interp) o.Add($"- {i}");
            o.Add("");
            o.Add("Notes\n-----");
            o.Add("- Deterministic heuristic summary; not a malware conclusion. Signer + exec path + arguments are the strongest first signals.");
            o.Add("- Reproducible and signable with syxx-witness.");
            return string.Join("\n", o);
        }
    }
}
