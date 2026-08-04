// Offline DNS-log translator (mirrors macOS SyxxKit/Forensics/DNSLogAnalyzer.swift).
// Parses a PROCESS/PACKET text capture-log format and applies a deterministic heuristic ruleset
// to produce a human report — process attribution, record types, and reply Rcodes included,
// unlike the JSON-only fallback of the original script. Fully offline; no model.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Syxx.Kit
{
    public sealed class DNSLogRecord
    {
        public string? Process { get; init; }
        public string? ProcessPath { get; init; }
        public string? SigningID { get; init; }
        public int? Pid { get; init; }
        public bool IsReply { get; init; }
        public string? Rcode { get; init; }
        public string? QuestionName { get; init; }
        public string? QuestionType { get; init; }
        public List<string> Answers { get; init; } = new();
    }

    public static class CaptureLogParser
    {
        public static List<DNSLogRecord> Parse(string text)
        {
            var records = new List<DNSLogRecord>();
            var lines = text.Split('\n');
            int i = 0, n = lines.Length;

            while (i < n)
            {
                if (!lines[i].Trim().EndsWith("PROCESS:", StringComparison.Ordinal)) { i++; continue; }
                i++;

                while (i < n && lines[i].Trim() != "{") i++;
                if (i < n) i++; // skip "{"
                string? name = null, path = null, signingID = null;
                int? pid = null;
                while (i < n)
                {
                    var l = lines[i].Trim();
                    if (l == "}") { i++; break; }
                    var kv = ParseKV(l);
                    if (kv != null)
                    {
                        switch (kv.Value.Key)
                        {
                            case "name": name = kv.Value.Value; break;
                            case "path": path = kv.Value.Value; break;
                            case "signing ID": signingID = kv.Value.Value; break;
                            case "pid": if (int.TryParse(kv.Value.Value, out var p)) pid = p; break;
                        }
                    }
                    i++;
                }

                while (i < n)
                {
                    var l = lines[i].Trim();
                    if (l.StartsWith("PACKET:", StringComparison.Ordinal) || l.EndsWith("PROCESS:", StringComparison.Ordinal)) break;
                    i++;
                }
                if (i >= n || !lines[i].Trim().StartsWith("PACKET:", StringComparison.Ordinal)) continue;
                i++;

                bool isReply = false;
                string? rcode = null, qName = null, qType = null;
                var answers = new List<string>();
                string section = "";
                while (i < n)
                {
                    var l = lines[i].Trim();
                    if (l.EndsWith("PROCESS:", StringComparison.Ordinal) || l.StartsWith("method ", StringComparison.Ordinal)) break;
                    if (l.StartsWith("QR:", StringComparison.Ordinal)) isReply = l.Contains("Reply", StringComparison.Ordinal);
                    else if (l.StartsWith("Rcode:", StringComparison.Ordinal)) rcode = l.Substring("Rcode:".Length).Trim();
                    else if (l.StartsWith("Question (", StringComparison.Ordinal)) section = "question";
                    else if (l.StartsWith("Answer (", StringComparison.Ordinal)) section = "answer";
                    else if (l.StartsWith("Authority (", StringComparison.Ordinal)) section = "authority";
                    else if (l.StartsWith("Additional records (", StringComparison.Ordinal)) section = "additional";
                    else if (l.StartsWith("Xid:", StringComparison.Ordinal) || l.StartsWith("Opcode:", StringComparison.Ordinal) || l.StartsWith("AA:", StringComparison.Ordinal)
                             || l.StartsWith("TC:", StringComparison.Ordinal) || l.StartsWith("RD:", StringComparison.Ordinal) || l.StartsWith("RA:", StringComparison.Ordinal) || l.StartsWith("Server:", StringComparison.Ordinal))
                    {
                        // header noise
                    }
                    else if (l.Length > 0)
                    {
                        if (section == "question" && qName == null)
                        {
                            var (qn, qt) = ParseQuestion(l);
                            qName = qn; qType = qt;
                        }
                        else if (section == "answer") answers.Add(l);
                    }
                    i++;
                }

                records.Add(new DNSLogRecord
                {
                    Process = name, ProcessPath = path, SigningID = signingID, Pid = pid,
                    IsReply = isReply, Rcode = rcode, QuestionName = qName, QuestionType = qType, Answers = answers
                });
            }
            return records;
        }

        private static (string Key, string Value)? ParseKV(string line)
        {
            var l = line.EndsWith(";", StringComparison.Ordinal) ? line.Substring(0, line.Length - 1) : line;
            int idx = l.IndexOf(" = ", StringComparison.Ordinal);
            if (idx < 0) return null;
            return (Unquote(l.Substring(0, idx)), Unquote(l.Substring(idx + 3)));
        }

        private static string Unquote(string s)
        {
            var t = s.Trim();
            if (t.Length >= 2 && t.StartsWith("\"", StringComparison.Ordinal) && t.EndsWith("\"", StringComparison.Ordinal)) t = t.Substring(1, t.Length - 2);
            return t;
        }

        private static (string? Name, string? Type) ParseQuestion(string line)
        {
            int idx = line.IndexOf(" IN ", StringComparison.Ordinal);
            if (idx < 0) { var only = line.Trim(); return (only.Length == 0 ? null : only, null); }
            var name = line.Substring(0, idx).Trim();
            var rest = line.Substring(idx + 4).Trim();
            var type = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return (name.Length == 0 ? null : name, type);
        }
    }

    public sealed class DNSLogAnalysis
    {
        public int RecordCount { get; init; }
        public int UniqueDomains { get; init; }
        public int UniqueRegistrable { get; init; }
        public List<(string Name, int Count)> DomainCounts { get; init; } = new();
        public List<(string Name, int Count)> RegistrableCounts { get; init; } = new();
        public List<(string Name, int Count)> TypeCounts { get; init; } = new();
        public List<(string Name, int Count)> StatusCounts { get; init; } = new();
        public List<(string Name, int Count)> ProcessCounts { get; init; } = new();
        public List<string> LongDomains { get; init; } = new();
        public List<string> EncodedDomains { get; init; } = new();
        public List<string> ExfilCandidates { get; init; } = new();
        /// C6 — domains impersonating a known brand (homoglyph / typosquat).
        public List<LookalikeDetector.Finding> LookalikeFindings { get; init; } = new();
        public List<string> Interpretation { get; init; } = new();
    }

    public static class DNSLogAnalyzer
    {
        public static DNSLogAnalysis Analyze(List<DNSLogRecord> records)
        {
            var domain = new Dictionary<string, int>();
            var registrable = new Dictionary<string, int>();
            var type = new Dictionary<string, int>();
            var status = new Dictionary<string, int>();
            var process = new Dictionary<string, int>();
            var longDomains = new HashSet<string>();
            var encoded = new HashSet<string>();

            void Bump(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var v) ? v + 1 : 1;

            foreach (var r in records)
            {
                if (!string.IsNullOrEmpty(r.Process)) Bump(process, r.Process!);
                if (!string.IsNullOrEmpty(r.QuestionType) && r.QuestionType != "?????") Bump(type, r.QuestionType!.ToUpperInvariant());
                if (r.IsReply && !string.IsNullOrEmpty(r.Rcode)) Bump(status, StatusBucket(r.Rcode!));

                if (string.IsNullOrEmpty(r.QuestionName)) continue;
                var d = NormalizeDomain(r.QuestionName!);
                if (d == null) continue;
                Bump(domain, d);
                var reg = DNSCaptureReportGenerator.RegistrableDomain(d);
                if (reg != null) Bump(registrable, reg);
                var labels = d.Split('.');
                if (d.Length >= 100 || labels.Any(l => l.Length >= 48)) longDomains.Add(d);
                if (labels.Any(LooksEncodedLabel)) encoded.Add(d);
            }

            var exfil = new List<string>();
            var lookalikes = new List<LookalikeDetector.Finding>();
            foreach (var d in domain.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var a = ExfilAnalyzer.Assess(d);
                if (a.Suspicion == ExfilSuspicion.LikelyExfil || a.Suspicion == ExfilSuspicion.Suspicious) exfil.Add(d);
                // C6 — flag brand-impersonating names in the observed stream.
                var la = LookalikeDetector.Analyze(d);
                if (la != null) lookalikes.Add(la);
            }

            var statusPairs = Top(status, 15);
            var interp = Interpret(records.Count, Top(domain, 1).FirstOrDefault(), statusPairs, type,
                                   longDomains.Count, encoded.Count, exfil.Count, lookalikes.Count);

            return new DNSLogAnalysis
            {
                RecordCount = records.Count,
                UniqueDomains = domain.Count,
                UniqueRegistrable = registrable.Count,
                DomainCounts = Top(domain, 20),
                RegistrableCounts = Top(registrable, 20),
                TypeCounts = Top(type, 15),
                StatusCounts = statusPairs,
                ProcessCounts = Top(process, 20),
                LongDomains = longDomains.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                EncodedDomains = encoded.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                ExfilCandidates = exfil,
                LookalikeFindings = lookalikes,
                Interpretation = interp
            };
        }

        public static string Render(DNSLogAnalysis a, string source)
        {
            var outp = new List<string>();
            void Section(string title, List<(string Name, int Count)> pairs)
            {
                outp.Add(title); outp.Add(new string('-', title.Length));
                if (pairs.Count == 0) outp.Add("  none observed");
                else
                {
                    int w = pairs.Max(p => p.Count.ToString(CultureInfo.InvariantCulture).Length);
                    foreach (var p in pairs) outp.Add($"  {p.Count.ToString(CultureInfo.InvariantCulture).PadLeft(w)}  {p.Name}");
                }
                outp.Add("");
            }
            void List(string title, List<string> items)
            {
                outp.Add(title); outp.Add(new string('-', title.Length));
                if (items.Count == 0) outp.Add("  none observed");
                else foreach (var it in items.Take(20)) outp.Add($"  {it}");
                outp.Add("");
            }

            outp.Add("Syxx DNS Log Analysis");
            outp.Add("=====================");
            outp.Add("");
            outp.Add($"Source: {source}");
            outp.Add($"Parsed records: {a.RecordCount}");
            outp.Add($"Unique domains: {a.UniqueDomains}  |  Unique registrable domains: {a.UniqueRegistrable}");
            outp.Add("");
            Section("Top Domains", a.DomainCounts);
            Section("Top Registrable Domains", a.RegistrableCounts);
            Section("Record Types", a.TypeCounts);
            Section("Reply Rcodes / Statuses", a.StatusCounts);
            Section("Observed Processes", a.ProcessCounts);
            List("Long Domains", a.LongDomains);
            List("Encoded / High-Entropy Labels", a.EncodedDomains);
            List("Tunneling / Exfil Candidates (entropy + blocklist)", a.ExfilCandidates);
            // C6 — brand-impersonation findings, each naming the brand and how it was spoofed.
            if (a.LookalikeFindings.Count > 0)
            {
                outp.Add("Lookalike / Brand-Impersonation Domains");
                outp.Add(new string('-', "Lookalike / Brand-Impersonation Domains".Length));
                foreach (var f in a.LookalikeFindings)
                    outp.Add($"- {f.Display}  ->  impersonates {f.Brand}  [{f.Reason.ToString().ToLowerInvariant()}]");
                outp.Add("");
            }
            outp.Add("Interpretation");
            outp.Add("--------------");
            foreach (var line in a.Interpretation) outp.Add($"- {line}");
            outp.Add("");
            outp.Add("Notes");
            outp.Add("-----");
            outp.Add("- Deterministic heuristic summary of DNS activity, not a definitive conclusion.");
            outp.Add("- Process attribution + repeated captures are the strongest confirmation steps.");
            outp.Add("- This output is reproducible and can be signed as evidence with syxx-witness.");
            return string.Join("\n", outp);
        }

        // Heuristics (ported + improved)

        internal static string? NormalizeDomain(string input)
        {
            var s = input.Trim().Trim('.', ',', ';', ':', '[', ']', '(', ')', '{', '}', '<', '>', '"', '\'').ToLowerInvariant();
            if (s.StartsWith("*.", StringComparison.Ordinal)) s = s.Substring(2);
            s = s.TrimEnd('.');
            if (s.Length == 0 || !s.Contains('.', StringComparison.Ordinal) || s.Length > 253) return null;
            var labels = s.Split('.');
            if (labels.Any(l => l.Length == 0 || l.Length > 63)) return null;
            foreach (var c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-')) return null;
            return s;
        }

        internal static bool LooksEncodedLabel(string label)
        {
            if (label.Length < 24) return false;
            int alnum = label.Count(char.IsLetterOrDigit);
            int digits = label.Count(char.IsDigit);
            if ((double)alnum / Math.Max(label.Length, 1) < 0.85) return false;
            return ExfilAnalyzer.ShannonEntropyBitsPerChar(label) >= 3.6 || digits >= 8;
        }

        internal static string StatusBucket(string rcode)
        {
            var t = rcode.ToLowerInvariant();
            if (t.Contains("name error", StringComparison.OrdinalIgnoreCase) || t.Contains("nxdomain", StringComparison.OrdinalIgnoreCase)) return "nxdomain";
            if (t.Contains("server failure", StringComparison.OrdinalIgnoreCase) || t.Contains("servfail", StringComparison.OrdinalIgnoreCase)) return "servfail";
            if (t.Contains("refused", StringComparison.OrdinalIgnoreCase)) return "refused";
            if (t.Contains("no error", StringComparison.OrdinalIgnoreCase) || t.Contains("noerror", StringComparison.OrdinalIgnoreCase) || t.Contains("success", StringComparison.OrdinalIgnoreCase)) return "successful";
            return t.Length > 60 ? t.Substring(0, 60) : t;
        }

        private static List<(string Name, int Count)> Top(Dictionary<string, int> dict, int n) =>
            dict.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(n).Select(kv => (kv.Key, kv.Value)).ToList();

        private static List<string> Interpret(int recordCount, (string Name, int Count) domainTop,
                                              List<(string Name, int Count)> statusPairs, Dictionary<string, int> typeCounts,
                                              int longCount, int encodedCount, int exfilCount, int lookalikeCount = 0)
        {
            var interp = new List<string> { $"Parsed {recordCount} DNS event record(s) from the log." };
            if (domainTop.Name != null) interp.Add($"Most-queried domain: {domainTop.Name} ({domainTop.Count} event(s)).");
            int totalStatus = statusPairs.Sum(p => p.Count);
            int failures = statusPairs.Where(p => p.Name is "nxdomain" or "servfail" or "refused").Sum(p => p.Count);
            if (totalStatus > 0 && (double)failures / totalStatus >= 0.25)
                interp.Add($"A substantial share of replies were failures/refusals ({failures}/{totalStatus}) — possible blocked tracking, broken config, captive portal, or retry-heavy software.");
            if (typeCounts.ContainsKey("TXT")) interp.Add("TXT lookups present — common for verification/service discovery; repeated high-volume TXT deserves attribution.");
            if (typeCounts.ContainsKey("HTTPS") || typeCounts.ContainsKey("SVCB")) interp.Add("HTTPS/SVCB records present — normal for modern browsers/CDNs.");
            if (longCount > 0) interp.Add("Unusually long names observed — often CDN keys, but worth checking for tunneling/telemetry.");
            if (encodedCount > 0) interp.Add("High-entropy/encoded-looking labels observed — confirm the owning process before treating as suspicious.");
            if (exfilCount > 0) interp.Add($"{exfilCount} domain(s) flagged as tunneling/exfil candidates by the fused entropy+blocklist check.");
            if (lookalikeCount > 0) interp.Add($"{lookalikeCount} domain(s) impersonate a known brand (look-alike characters or a one-character typo) — a phishing/credential-theft hallmark; confirm the owning process.");
            return interp;
        }
    }
}
