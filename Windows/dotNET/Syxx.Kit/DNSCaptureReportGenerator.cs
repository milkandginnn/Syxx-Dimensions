using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Syxx.Kit
{
    public static class DNSCaptureReportGenerator
    {
        /// Tri-state result for report writes: success / failure / unknown.
        /// Mirrors the project-wide tri-state contract (AGENTS.md: "TRI-STATE, NEVER BOOLEAN").
        public enum ReportWriteResult
        {
            Success,
            Failure,
            Unknown
        }

        /// The return value of GenerateReport: the report text plus a tri-state write result.
        public class ReportOutput
        {
            public string Report { get; }
            public ReportWriteResult WriteResult { get; }
            public string? WriteError { get; }
            public ReportOutput(string report, ReportWriteResult writeResult, string? writeError = null)
            {
                Report = report;
                WriteResult = writeResult;
                WriteError = writeError;
            }
        }

        /// Streams the capture file line-by-line via `File.ReadLines` (lazy enumeration) instead
        /// of `File.ReadAllLines` (which loads the whole file into memory at once) — a long
        /// capture's NDJSON can be large, and the old approach risked OOM on exactly the
        /// longest, most evidence-rich sessions (W31).
        public static ReportOutput GenerateReport(string inputFile, string outputFile, string reportFile, DateTime startTime, double durationSeconds, string iface = "unknown")
        {
            if (!File.Exists(inputFile))
            {
                return new ReportOutput($"Error: Could not read capture file at {inputFile}", ReportWriteResult.Failure, "Input file does not exist");
            }

            int parsedRecords = 0;
            int unparsedCount = 0;

            var domains = new Dictionary<string, int>();
            var parentDomains = new Dictionary<string, int>();
            var processes = new Dictionary<string, int>();
            var recordTypes = new Dictionary<string, int>();
            var statuses = new Dictionary<string, int>();
            var resolvers = new Dictionary<string, int>();
            // C7 — per-process resolver usage (process -> resolver IPs), joined at report time.
            var resolverUsage = new Dictionary<string, HashSet<string>>();

            var longDomains = new Dictionary<string, int>();
            var encodedDomains = new Dictionary<string, int>();

            foreach (var line in File.ReadLines(inputFile))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                DNSCaptureRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize<DNSCaptureRecord>(trimmed);
                }
                catch
                {
                    record = null;
                }

                if (record == null)
                {
                    unparsedCount++;
                    continue;
                }
                parsedRecords++;

                string procName = record.Process.Name ?? "unknown";
                processes[procName] = processes.GetValueOrDefault(procName) + 1;

                string resolverIP = record.Packet.Qr == "Query" ? record.Network.DestIP : record.Network.SourceIP;
                if (!string.IsNullOrEmpty(resolverIP))
                {
                    resolvers[resolverIP] = resolvers.GetValueOrDefault(resolverIP) + 1;
                }

                // C7 — join process <-> resolver, keying the resolver on the PORT-53 side (D063:
                // the QR bit is forgeable, the port is not).
                string portKeyedResolver = record.Network.SourcePort == 53 ? record.Network.SourceIP : record.Network.DestIP;
                if (!string.IsNullOrEmpty(portKeyedResolver))
                {
                    if (!resolverUsage.TryGetValue(procName, out var set)) { set = new HashSet<string>(); resolverUsage[procName] = set; }
                    set.Add(portKeyedResolver);
                }

                foreach (var q in record.Packet.Questions)
                {
                    string? domainName = q.QuestionName;
                    if (domainName == null) continue;
                    domains[domainName] = domains.GetValueOrDefault(domainName) + 1;

                    string? parent = RegistrableDomain(domainName);
                    if (parent != null)
                    {
                        parentDomains[parent] = parentDomains.GetValueOrDefault(parent) + 1;
                    }

                    recordTypes[q.QuestionType] = recordTypes.GetValueOrDefault(q.QuestionType) + 1;

                    if (domainName.Length > 40)
                    {
                        longDomains[domainName] = longDomains.GetValueOrDefault(domainName) + 1;
                    }
                    if (IsEncodedLooking(domainName))
                    {
                        encodedDomains[domainName] = encodedDomains.GetValueOrDefault(domainName) + 1;
                    }
                }

                string rcode = record.Packet.Rcode.ToLowerInvariant();
                statuses[rcode] = statuses.GetValueOrDefault(rcode) + 1;
            }

            double durationMinutes = durationSeconds / 60.0;
            double eventRate = durationMinutes > 0 ? (parsedRecords / durationMinutes) : 0.0;

            string dateStamp = startTime.ToString("dd-MM-yyyy HH.mm.ss", CultureInfo.InvariantCulture);
            string captureDateStr = startTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

            string relOutput = RelativeCapturePath(outputFile);
            string relReport = RelativeCapturePath(reportFile);

            string FormatNumber(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

            string FormatTopList(Dictionary<string, int> dict, int limit)
            {
                var sorted = dict.OrderByDescending(x => x.Value).Take(limit).ToList();
                if (sorted.Count == 0) return "";
                int maxCount = sorted[0].Value;
                int countWidth = maxCount.ToString(CultureInfo.InvariantCulture).Length;
                return string.Join("\n", sorted.Select(x => string.Format(CultureInfo.InvariantCulture, $"{{0,{countWidth}}}  {{1}}", x.Value, x.Key)));
            }

            string topDomainsStr = FormatTopList(domains, 8);
            string topParentDomainsStr = FormatTopList(parentDomains, 8);
            string topProcessesStr = FormatTopList(processes, 8);
            string recordTypesStr = FormatTopList(recordTypes, int.MaxValue);
            string statusesStr = FormatTopList(statuses, int.MaxValue);
            string resolversStr = FormatTopList(resolvers, 8);

            // C7 — per-app resolver attribution. Flag processes using a resolver the system did not
            // configure (hardcoded-DNS bypass). Coverage is UDP:53 only; that limit is stated.
            var systemResolvers = new HashSet<string>(NetworkKeyState.GetSystemDNSServers());
            var attribution = ResolverAttribution.Classify(resolverUsage, systemResolvers);
            string attributionStr = BuildAttribution(attribution);

            long fileSizeBytes = new FileInfo(inputFile).Length;
            double fileSizeMB = fileSizeBytes / 1_048_576.0;
            string fileSizeStr = fileSizeMB >= 1024.0
                ? $"{(fileSizeMB / 1024.0).ToString("F1", CultureInfo.InvariantCulture)} GB"
                : $"{fileSizeMB.ToString("F1", CultureInfo.InvariantCulture)} MB";

            if (parsedRecords == 0)
            {
                string msg = $@"{Header}

                         DIAGNOSTIC REPORT


Date:       {captureDateStr}
Duration:   {durationSeconds.ToString("F1", CultureInfo.InvariantCulture)}s
Interface:  {iface}

STATUS: INCONCLUSIVE
--------------------
No packets were captured. This is NOT evidence of a
quiet network. The capture may have failed (missing
privileges, wrong interface, BPF blocked) or no DNS
traffic occurred during the sampling window.

PARSE SUMMARY
-------------
Output size:         {FormatNumber((int)fileSizeBytes)} bytes
Records parsed:      0
Unparseable lines:   {unparsedCount}

TROUBLESHOOTING
---------------
- Run as Administrator to ensure raw socket access
- Generate DNS traffic during capture (nslookup example.com)
- Increase the capture duration
- Check that the capture interface is correct";
                var writeResult = ReportWriteResult.Unknown;
                string? writeError = null;
                try { File.WriteAllText(reportFile, msg); writeResult = ReportWriteResult.Success; }
                catch (Exception ex) { writeResult = ReportWriteResult.Failure; writeError = ex.Message; }
                return new ReportOutput(msg, writeResult, writeError);
            }

            int longDomainCount = longDomains.Count;
            int encodedDomainCount = encodedDomains.Count;
            var topProcess = processes.OrderByDescending(x => x.Value).FirstOrDefault();
            string topProcessName = topProcess.Key ?? "None";
            int topProcessCount = topProcess.Value;
            double topProcessPct = parsedRecords > 0 ? (double)topProcessCount / parsedRecords * 100.0 : 0.0;

            string interpretation = $"Capture collected {FormatNumber(parsedRecords)} DNS events over {durationSeconds.ToString("F1", CultureInfo.InvariantCulture)}s " +
                $"({eventRate.ToString("F1", CultureInfo.InvariantCulture)} events/min). Top process: {topProcessName} " +
                $"({topProcessCount} queries, {topProcessPct.ToString("F0", CultureInfo.InvariantCulture)}% of traffic).";

            if (longDomainCount > 0)
            {
                interpretation += $"\n\n{longDomainCount} long subdomains detected. Long subdomains are normal " +
                    "for CDN cache keys and telemetry, but also appear in " +
                    "DNS tunneling. Verify the process owner before dismissing.";
            }

            if (encodedDomainCount > 0)
            {
                interpretation += $"\n\n{encodedDomainCount} encoded-looking labels (hex patterns or labels >16 " +
                    "chars). Some legitimate services use generated hostnames; " +
                    "cross-reference with the process list above.";
            }

            string nextSteps = "Review the anomalous indicators against the process list.";
            if (longDomainCount > 0 || encodedDomainCount > 0)
            {
                nextSteps += "\nRun syxx-mirror against flagged domains for cross-vantage comparison.";
            }
            nextSteps += "\nRe-run with --keep to retain raw capture for deeper analysis.";

            string report = $@"{Header}

                         DIAGNOSTIC REPORT


Date:       {captureDateStr}
Duration:   {durationSeconds.ToString("F1", CultureInfo.InvariantCulture)}s
Interface:  {iface}
Output:     {relOutput}
Report:     {relReport}

PARSE SUMMARY
-------------
Records parsed:      {FormatNumber(parsedRecords)}
Unparseable lines:   {unparsedCount}
File size:           {fileSizeStr}{(fileSizeBytes >= 1_073_741_824 ? "  (consider --minify to reduce file size)" : "")}
Event rate:          {eventRate.ToString("F1", CultureInfo.InvariantCulture)} events/min

TOP DOMAINS
-----------
{(string.IsNullOrEmpty(topDomainsStr) ? "None observed" : topDomainsStr)}

TOP PARENT DOMAINS
------------------
{(string.IsNullOrEmpty(topParentDomainsStr) ? "None observed" : topParentDomainsStr)}

RECORD TYPES
------------
{(string.IsNullOrEmpty(recordTypesStr) ? "None observed" : recordTypesStr)}

RESPONSE STATUSES
-----------------
{(string.IsNullOrEmpty(statusesStr) ? "None observed" : statusesStr)}

OBSERVED PROCESSES
------------------
{(string.IsNullOrEmpty(topProcessesStr) ? "None observed" : topProcessesStr)}

RESOLVERS CONTACTED
-------------------
{(string.IsNullOrEmpty(resolversStr) ? "None observed" : resolversStr)}

PER-APP RESOLVER ATTRIBUTION
----------------------------
{attributionStr}

ANOMALOUS INDICATORS
--------------------
Long domains (>40 chars):       {longDomainCount}
Encoded-looking domains:         {encodedDomainCount}

INTERPRETATION
--------------
{interpretation}

NEXT STEPS
----------
{nextSteps}";

            try
            {
                File.WriteAllText(reportFile, report);
                return new ReportOutput(report, ReportWriteResult.Success);
            }
            catch (Exception ex)
            {
                return new ReportOutput(report, ReportWriteResult.Failure, ex.Message);
            }
        }

        // MARK: - Header

        private static string Header
        {
            get
            {
                string eq = new string('=', 72);
                string dash = new string('-', 72);
                return string.Join("\n",
                    eq,
                    "                             Syxx",
                    "                      Cage Query Capture",
                    eq,
                    "          \u00A9 J.J. Warne d/b/a MilkandGin \u00B7 All rights reserved",
                    "              feedback@milkandgin.com \u00B7 https://syxx.app",
                    dash);
            }
        }

        // MARK: - Helpers

        /// Strips the user profile prefix from a path, returning a shorter relative path
        /// suitable for the report (e.g. `AppData\Local\Syxx\captures\...` instead of
        /// `C:\Users\&lt;name&gt;\AppData\Local\Syxx\captures\...`).
        // C7 — render the per-app resolver attribution block (macOS attributionStr twin).
        private static string BuildAttribution(ResolverAttribution.Report attribution)
        {
            if (attribution.Attributions.Count == 0) return "None observed";
            var lines = new List<string>();
            if (attribution.BaselineUnknown)
                lines.Add("System resolver set unknown — cannot judge bypass; process->resolver map only:");
            foreach (var a in attribution.Attributions)
            {
                string mark = a.UsesHardcodedResolver ? "  [OFF-SYSTEM RESOLVER]" : "";
                lines.Add($"{a.Process}: {string.Join(", ", a.Resolvers)}{mark}");
            }
            if (attribution.Flagged.Count > 0)
            {
                lines.Add("");
                lines.Add("Flagged (used a resolver the system did not configure):");
                foreach (var a in attribution.Flagged)
                    lines.Add($"  {a.Process} -> {string.Join(", ", a.OffSystemResolvers)}");
            }
            lines.Add("");
            lines.Add("Coverage: cleartext UDP:53 only. Encrypted DNS (DoH/DoT/DoQ) is not captured, so");
            lines.Add("\"no off-system resolver\" means none over UDP:53, not none at all.");
            return string.Join("\n", lines);
        }

        private static string RelativeCapturePath(string path)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
            {
                string rel = path.Substring(userProfile.Length);
                return rel.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return path;
        }

        /// Common two-level public suffixes where the registrable domain is the last three labels
        /// (e.g. bbc.co.uk, not co.uk). Compact list — not the full PSL, but enough to stop the old
        /// last-two-labels grouping from merging unrelated *.co.uk sites (W31/M32-capture-grouping).
        private static readonly HashSet<string> TwoLevelPublicSuffixes = new HashSet<string>
        {
            "co.uk", "org.uk", "gov.uk", "ac.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk", "sch.uk",
            "com.au", "net.au", "org.au", "edu.au", "gov.au", "id.au",
            "co.nz", "net.nz", "org.nz", "govt.nz", "ac.nz",
            "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp",
            "com.br", "net.br", "org.br", "gov.br",
            "com.cn", "net.cn", "org.cn", "gov.cn",
            "co.in", "net.in", "org.in", "gov.in", "co.za", "org.za", "com.mx", "com.tr", "com.sg",
            "com.hk", "com.tw", "co.kr", "or.kr", "co.il", "com.ar", "com.co", "co.th", "in.th"
        };

        /// Returns the registrable ("parent") domain for a name, public-suffix aware.
        public static string? RegistrableDomain(string name)
        {
            var parts = name.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            if (parts.Length >= 3)
            {
                string lastTwo = string.Join(".", parts.Skip(parts.Length - 2));
                if (TwoLevelPublicSuffixes.Contains(lastTwo))
                {
                    return string.Join(".", parts.Skip(parts.Length - 3));
                }
            }
            return string.Join(".", parts.Skip(parts.Length - 2));
        }

        private static bool IsEncodedLooking(string domain)
        {
            if (Regex.IsMatch(domain, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}"))
            {
                return true;
            }

            var parts = domain.Split('.');
            foreach (var part in parts)
            {
                if (part.Length > 16)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
