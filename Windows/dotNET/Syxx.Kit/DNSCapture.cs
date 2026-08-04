using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Serialization;

namespace Syxx.Kit
{
    /// D112: Authenticode trust result — whether the binary's signature was verified, not just
    /// whether a certificate subject could be extracted. A stripped, expired, revoked, or forged
    /// signature block still yields a confident-looking subject via CreateFromSignedFile; the
    /// trust result distinguishes verified from extracted-but-unchecked.
    public enum SignatureTrust
    {
        Valid,       // signature verified via WinVerifyTrust / X509Chain
        Invalid,     // signature present but verification failed
        Unsigned,    // no signature block found
        Unverifiable // cannot check (non-Windows, API unavailable, or exception)
    }

    public class ProcessInfoReport
    {
        // Null in every field below means genuinely unknown — never guessed or fabricated. A
        // forensic report that invents "svchost" for every unattributable packet launders
        // whatever process actually made the query (W28); reporting unknown honestly preserves
        // that uncertainty instead of hiding it.
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("signing ID")]
        public string? SigningID { get; set; }

        /// D112: trust result — the SigningID alone is meaningless without knowing whether
        /// the signature was actually verified.
        [JsonPropertyName("signature trust")]
        public SignatureTrust SignatureTrust { get; set; } = SignatureTrust.Unverifiable;

        [JsonPropertyName("pid")]
        public int? Pid { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("attributed")]
        public bool IsAttributed => Pid.HasValue;

        public static ProcessInfoReport Unknown => new ProcessInfoReport();
    }

    public class DNSQuestionReport
    {
        [JsonPropertyName("Question Name")]
        public string QuestionName { get; set; } = "";

        [JsonPropertyName("Question Class")]
        public string QuestionClass { get; set; } = "";

        [JsonPropertyName("Question Type")]
        public string QuestionType { get; set; } = "";
    }

    public class DNSAnswerReport
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("Type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("TTL")]
        public uint Ttl { get; set; }

        [JsonPropertyName("Rdata")]
        public string Rdata { get; set; } = "";
    }

    public class DNSPacketReport
    {
        [JsonPropertyName("Opcode")]
        public string Opcode { get; set; } = "";

        [JsonPropertyName("QR")]
        public string Qr { get; set; } = "";

        [JsonPropertyName("Questions")]
        public List<DNSQuestionReport> Questions { get; set; } = new List<DNSQuestionReport>();

        [JsonPropertyName("Answers")]
        public List<DNSAnswerReport> Answers { get; set; } = new List<DNSAnswerReport>();

        [JsonPropertyName("RA")]
        public string Ra { get; set; } = "";

        [JsonPropertyName("Rcode")]
        public string Rcode { get; set; } = "";

        [JsonPropertyName("RD")]
        public string Rd { get; set; } = "";

        [JsonPropertyName("XID")]
        public ushort Xid { get; set; }

        [JsonPropertyName("TC")]
        public string Tc { get; set; } = "";

        [JsonPropertyName("AA")]
        public string Aa { get; set; } = "";

        [JsonPropertyName("Payload Bytes")]
        public int PayloadBytes { get; set; }
    }

    /// The IP/UDP transport context a DNS packet arrived in — the piece the old capture omitted
    /// entirely. Without resolver IP, ports, and direction, a forensic report cannot show that
    /// DNS traffic went to an unexpected/hijacked resolver (W26).
    public class NetworkContextReport
    {
        [JsonPropertyName("IP Version")]
        public int IpVersion { get; set; }

        // A `= ""` initialiser does NOT protect these: an explicit JSON `null` overwrites it during
        // deserialization, so a hostile/truncated capture lands a null here and every downstream
        // `.Contains(...)` throws. Coalesce in the setter — once, at the boundary — rather than
        // `?.`-ing four call sites and waiting for a fifth to forget. (Found by
        // MonitorReportAnalyzerTests: `{"Network":{"Direction":null}}` NRE'd StubEntropyProbe:60.)
        private string _sourceIP = "";
        [JsonPropertyName("Source IP")]
        public string SourceIP { get => _sourceIP; set => _sourceIP = value ?? ""; }

        private string _destIP = "";
        [JsonPropertyName("Dest IP")]
        public string DestIP { get => _destIP; set => _destIP = value ?? ""; }

        [JsonPropertyName("Source Port")]
        public ushort SourcePort { get; set; }

        [JsonPropertyName("Dest Port")]
        public ushort DestPort { get; set; }

        private string _direction = "";
        [JsonPropertyName("Direction")]
        public string Direction { get => _direction; set => _direction = value ?? ""; }
    }

    public class DNSCaptureRecord
    {
        // Same hazard as NetworkContextReport: an explicit JSON `null` beats the `= new ...`
        // initialiser, so `{"Packet":null}` yields a null sub-object and NREs on first access.
        private ProcessInfoReport _process = new ProcessInfoReport();
        [JsonPropertyName("Process")]
        public ProcessInfoReport Process { get => _process; set => _process = value ?? new ProcessInfoReport(); }

        private string _timestamp = "";
        [JsonPropertyName("Timestamp")]
        public string Timestamp { get => _timestamp; set => _timestamp = value ?? ""; }

        private DNSPacketReport _packet = new DNSPacketReport();
        [JsonPropertyName("Packet")]
        public DNSPacketReport Packet { get => _packet; set => _packet = value ?? new DNSPacketReport(); }

        private NetworkContextReport _network = new NetworkContextReport();
        [JsonPropertyName("Network")]
        public NetworkContextReport Network { get => _network; set => _network = value ?? new NetworkContextReport(); }
    }

    /// <summary>
    /// The user-facing raw-queries export — the "queries in JSON" companion to the interpreted report
    /// (interpretation first, raw evidence second). A privacy-considered view of each captured DNS packet:
    /// the query essentials plus answers, keyed by process NAME only — never the full process path/signing
    /// ID the forensic dump retains — so a user can keep and analyse their own DNS activity without the
    /// sensitive process-path metadata that motivates erasing the full capture.
    /// </summary>
    public class DNSQueryExport
    {
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("direction")] public string Direction { get; set; } = "";
        [JsonPropertyName("domain")] public string Domain { get; set; } = "unknown";
        [JsonPropertyName("type")] public string Type { get; set; } = "unknown";
        [JsonPropertyName("class")] public string RecordClass { get; set; } = "IN";
        [JsonPropertyName("rcode")] public string Rcode { get; set; } = "";
        [JsonPropertyName("transactionId")] public ushort TransactionId { get; set; }
        [JsonPropertyName("resolver")] public string Resolver { get; set; } = "";
        [JsonPropertyName("process")] public string? Process { get; set; }
        [JsonPropertyName("answers")] public List<DNSAnswerReport> Answers { get; set; } = new List<DNSAnswerReport>();

        public static DNSQueryExport FromRecord(DNSCaptureRecord record)
        {
            var q = record.Packet.Questions.Count > 0 ? record.Packet.Questions[0] : null;
            return new DNSQueryExport
            {
                Timestamp = record.Timestamp,
                Direction = record.Network.Direction,
                Domain = q?.QuestionName ?? "unknown",
                Type = q?.QuestionType ?? "unknown",
                RecordClass = string.IsNullOrEmpty(q?.QuestionClass) ? "IN" : q!.QuestionClass,
                Rcode = record.Packet.Rcode,
                TransactionId = record.Packet.Xid,
                Resolver = record.Network.SourcePort == 53 ? record.Network.SourceIP : record.Network.DestIP,
                Process = record.Process.Name,
                Answers = record.Packet.Answers,
            };
        }

        /// Read a JSONL capture file (one DNSCaptureRecord per line) → pretty JSON array of DNSQueryExport.
        /// Malformed lines are skipped best-effort. Returns null only when the file can't be read.
        public static string? ArrayJsonFromCaptureFile(string path)
        {
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(path); } catch { return null; }
            var exports = new List<DNSQueryExport>();
            foreach (var raw in lines)
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;
                try
                {
                    var rec = System.Text.Json.JsonSerializer.Deserialize<DNSCaptureRecord>(s);
                    if (rec != null) exports.Add(FromRecord(rec));
                }
                catch { }
            }
            try { return System.Text.Json.JsonSerializer.Serialize(exports, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); }
            catch { return null; }
        }
    }

    public class IPUDPContext
    {
        public int IpVersion { get; set; }
        public string SourceIP { get; set; } = "";
        public string DestIP { get; set; } = "";
        public ushort SourcePort { get; set; }
        public ushort DestPort { get; set; }
        public int PayloadOffset { get; set; }
    }

    public static class DNSCapture
    {
        private static readonly Dictionary<uint, string> RecordTypeNames = new Dictionary<uint, string>
        {
            { 1, "A" }, { 2, "NS" }, { 5, "CNAME" }, { 6, "SOA" }, { 12, "PTR" }, { 15, "MX" },
            { 16, "TXT" }, { 28, "AAAA" }, { 33, "SRV" }, { 43, "DS" }, { 46, "RRSIG" }, { 48, "DNSKEY" }
        };

        // MARK: - IP/UDP header parsing (W30 / M29-equivalent)

        /// Parses the IP header (respecting the real IHL for IPv4 instead of assuming 20 bytes,
        /// and supporting the fixed 40-byte IPv6 base header) plus the following UDP header, and
        /// returns the offset where the DNS payload begins. Extension headers between an IPv6
        /// base header and UDP are not walked; a non-UDP "next header" is reported as
        /// unparseable rather than guessed at.
        public static IPUDPContext? ParseIPUDPHeader(byte[] data)
        {
            if (data.Length < 1) return null;
            int version = data[0] >> 4;

            if (version == 4)
            {
                if (data.Length < 20) return null;
                int ihl = (data[0] & 0x0F) * 4;
                if (ihl < 20 || data.Length < ihl + 8) return null;
                byte protocolNumber = data[9];
                if (protocolNumber != 17) return null;

                // KDNS-13: validate UDP length field and ensure DNS payload >= 12 bytes
                ushort udpLen = (ushort)((data[ihl + 4] << 8) | data[ihl + 5]);
                if (udpLen < 8 || data.Length < ihl + udpLen) return null;
                int dnsPayloadLen = data.Length - (ihl + 8);
                if (dnsPayloadLen < 12) return null;

                string srcIP = $"{data[12]}.{data[13]}.{data[14]}.{data[15]}";
                string dstIP = $"{data[16]}.{data[17]}.{data[18]}.{data[19]}";

                int udpStart = ihl;
                ushort srcPort = (ushort)((data[udpStart] << 8) | data[udpStart + 1]);
                ushort dstPort = (ushort)((data[udpStart + 2] << 8) | data[udpStart + 3]);

                return new IPUDPContext
                {
                    IpVersion = 4,
                    SourceIP = srcIP,
                    DestIP = dstIP,
                    SourcePort = srcPort,
                    DestPort = dstPort,
                    PayloadOffset = ihl + 8
                };
            }
            else if (version == 6)
            {
                if (data.Length < 40 + 8) return null;
                byte nextHeader = data[6];
                if (nextHeader != 17) return null;

                // KDNS-13: validate DNS payload >= 12 bytes on the IPv6 path too
                int dnsPayloadLen = data.Length - (40 + 8);
                if (dnsPayloadLen < 12) return null;

                string GroupHex(int offset) => ((data[offset] << 8) | data[offset + 1]).ToString("x", CultureInfo.InvariantCulture);
                var srcGroups = new List<string>();
                var dstGroups = new List<string>();
                for (int i = 0; i < 8; i++) srcGroups.Add(GroupHex(8 + i * 2));
                for (int i = 0; i < 8; i++) dstGroups.Add(GroupHex(24 + i * 2));

                int udpStart = 40;
                ushort srcPort = (ushort)((data[udpStart] << 8) | data[udpStart + 1]);
                ushort dstPort = (ushort)((data[udpStart + 2] << 8) | data[udpStart + 3]);

                return new IPUDPContext
                {
                    IpVersion = 6,
                    SourceIP = string.Join(":", srcGroups),
                    DestIP = string.Join(":", dstGroups),
                    SourcePort = srcPort,
                    DestPort = dstPort,
                    PayloadOffset = 40 + 8
                };
            }
            return null;
        }

        // MARK: - DNS payload parsing

        public static DNSPacketReport? ParseDNSPacket(byte[] data)
        {
            if (data.Length <= 12) return null;

            ushort xid = (ushort)((data[0] << 8) | data[1]);
            ushort flags = (ushort)((data[2] << 8) | data[3]);

            string qr = (flags & 0x8000) != 0 ? "Response" : "Query";
            int opcodeVal = (flags & 0x7800) >> 11;
            string opcode = opcodeVal == 0 ? "Standard" : "Inverse";
            string aa = (flags & 0x0400) != 0 ? "Authoritative" : "Non-Authoritative";
            string tc = (flags & 0x0200) != 0 ? "Truncated" : "Non-Truncated";
            string rd = (flags & 0x0100) != 0 ? "Recursion desired" : "No recursion desired";
            string ra = (flags & 0x0080) != 0 ? "Recursion available" : "No recursion available";
            int rcodeVal = flags & 0x000F;
            string rcode = rcodeVal switch
            {
                0 => "No error",
                2 => "ServFail",
                3 => "NXDomain",
                5 => "Refused",
                _ => $"Error({rcodeVal})"
            };

            int qdCount = (data[4] << 8) | data[5];
            int anCount = (data[6] << 8) | data[7];

            int offset = 12;
            var questions = new List<DNSQuestionReport>();
            for (int i = 0; i < qdCount; i++)
            {
                if (offset >= data.Length) break;
                (string Name, int NextOffset)? nameResult = TryParseName(data, offset);
                if (nameResult == null) break;
                offset = nameResult.Value.NextOffset;

                if (offset + 4 > data.Length) break;
                uint qTypeVal = (uint)((data[offset] << 8) | data[offset + 1]);
                uint qClassVal = (uint)((data[offset + 2] << 8) | data[offset + 3]);
                offset += 4;

                questions.Add(new DNSQuestionReport
                {
                    QuestionName = nameResult.Value.Name,
                    QuestionClass = qClassVal == 1 ? "IN" : qClassVal.ToString(CultureInfo.InvariantCulture),
                    QuestionType = RecordTypeNames.TryGetValue(qTypeVal, out var qtn) ? qtn : qTypeVal.ToString(CultureInfo.InvariantCulture)
                });
            }

            var answers = new List<DNSAnswerReport>();
            for (int i = 0; i < anCount; i++)
            {
                if (offset >= data.Length) break;
                (string Name, int NextOffset)? nameResult = TryParseName(data, offset);
                if (nameResult == null) break;
                offset = nameResult.Value.NextOffset;

                if (offset + 10 > data.Length) break;
                uint type = (uint)((data[offset] << 8) | data[offset + 1]);
                uint ttl = (uint)((data[offset + 4] << 24) | (data[offset + 5] << 16) | (data[offset + 6] << 8) | data[offset + 7]);
                int rdLength = (data[offset + 8] << 8) | data[offset + 9];
                offset += 10;
                if (offset + rdLength > data.Length) break;

                string rdata = FormatRData(data, offset, rdLength, type);
                answers.Add(new DNSAnswerReport
                {
                    Name = nameResult.Value.Name,
                    Type = RecordTypeNames.TryGetValue(type, out var tn) ? tn : type.ToString(CultureInfo.InvariantCulture),
                    Ttl = ttl,
                    Rdata = rdata
                });
                offset += rdLength;
            }

            return new DNSPacketReport
            {
                Opcode = opcode,
                Qr = qr,
                Questions = questions,
                Answers = answers,
                Ra = ra,
                Rcode = rcode,
                Rd = rd,
                Xid = xid,
                Tc = tc,
                Aa = aa,
                PayloadBytes = data.Length
            };
        }

        private static string FormatRData(byte[] data, int offset, int rdLength, uint type)
        {
            if (type == 1 && rdLength == 4)
            {
                return $"{data[offset]}.{data[offset + 1]}.{data[offset + 2]}.{data[offset + 3]}";
            }
            if (type == 28 && rdLength == 16)
            {
                var groups = new List<string>();
                for (int j = 0; j < 16; j += 2)
                {
                    int val = (data[offset + j] << 8) | data[offset + j + 1];
                    groups.Add(val.ToString("x", CultureInfo.InvariantCulture));
                }
                return string.Join(":", groups);
            }
            var sb = new StringBuilder();
            for (int j = 0; j < rdLength; j++) sb.Append(data[offset + j].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static (string Name, int NextOffset)? TryParseName(byte[] data, int offset)
        {
            var parts = new List<string>();
            int current = offset;
            bool jumped = false;
            int nextOffset = offset;
            var visited = new HashSet<int>();

            while (current < data.Length)
            {
                if (!visited.Add(current)) return null; // compression loop
                int len = data[current];
                if (len == 0)
                {
                    if (!jumped) nextOffset = current + 1;
                    return (string.Join(".", parts), nextOffset);
                }
                else if ((len & 0xC0) == 0xC0)
                {
                    if (current + 1 >= data.Length) return null;
                    int pointer = ((len & 0x3F) << 8) | data[current + 1];
                    if (pointer >= data.Length) return null;
                    if (!jumped)
                    {
                        nextOffset = current + 2;
                        jumped = true;
                    }
                    current = pointer;
                }
                else
                {
                    if ((len & 0xC0) != 0 || current + 1 + len > data.Length) return null;
                    string label = Encoding.UTF8.GetString(data, current + 1, len);
                    parts.Add(label);
                    current += len + 1;
                    if (!jumped) nextOffset = current;
                }
            }
            return null;
        }

        // MARK: - Process attribution (W27/W28/W29; E035/E036/E046)

        /// D067: bounded PID cache with LRU-style eviction when at capacity. Stale entries older
        /// than CacheTTL are evicted on access; when the cache exceeds MaxPidCacheEntries, the
        /// oldest entries are removed. This prevents unbounded growth on systems that rapidly
        /// cycle through many short-lived process PIDs.
        private class CacheEntry
        {
            public ProcessInfoReport Report = ProcessInfoReport.Unknown;
            public DateTime CachedAt;
        }

        private const int MaxPidCacheEntries = 1024;
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<int, CacheEntry> PidCache = new Dictionary<int, CacheEntry>();
        private static readonly TimeSpan CacheTTL = TimeSpan.FromSeconds(5);

        // E036/E046: the old path shelled out to `cmd.exe /c netstat -ano | findstr /R ":{port} .*UDP"`
        // once per unique port — and the regex could never match real netstat output ("UDP" precedes
        // the port), so EVERY lookup returned Unknown. Attribution now reads the kernel's UDP
        // owner table in-process via GetExtendedUdpTable, snapshotted at (near) capture time.
        // SnapshotTTL is the honest bound on the port→pid race window; a port absent from a fresh
        // snapshot resolves to Unknown, never a guessed identity.
        private static readonly TimeSpan SnapshotTTL = TimeSpan.FromSeconds(1);
        // A port seen but missing from the cached snapshot (the common case for a client's FIRST
        // query: socket created after the snapshot) triggers one re-snapshot, rate-limited to this.
        private static readonly TimeSpan SnapshotRetryFloor = TimeSpan.FromMilliseconds(50);
        private static Dictionary<ushort, int>? UdpOwnerSnapshot;
        private static DateTime UdpOwnerSnapshotAt = DateTime.MinValue;

        /// Resolves process info for the local port a captured packet used, from an in-process
        /// `GetExtendedUdpTable` snapshot taken during the capture loop (no cmd.exe/netstat
        /// shell-out, no per-port process spawn). A port the kernel table does not attribute —
        /// or any snapshot failure — returns `ProcessInfoReport.Unknown` explicitly; `Pid`
        /// stays null so `IsAttributed` remains honestly false.
        public static ProcessInfoReport GetProcessInfo(ushort port)
        {
            int pid;
            lock (CacheLock)
            {
                if (!TryLookupOwnerLocked(port, out pid))
                {
                    // The socket may have been bound after the cached snapshot was taken —
                    // exactly the packets a capture most wants attributed. Retake once
                    // (rate-limited) before concluding Unknown.
                    if (DateTime.UtcNow - UdpOwnerSnapshotAt <= SnapshotRetryFloor)
                    {
                        return ProcessInfoReport.Unknown;
                    }
                    RefreshSnapshotLocked();
                    if (!TryLookupOwnerLocked(port, out pid))
                    {
                        return ProcessInfoReport.Unknown;
                    }
                }
            }
            return GetProcessInfoForPid(pid);
        }

        /// Caller must hold CacheLock. Refreshes the snapshot when stale, then looks up `port`.
        private static bool TryLookupOwnerLocked(ushort port, out int pid)
        {
            if (UdpOwnerSnapshot == null || DateTime.UtcNow - UdpOwnerSnapshotAt >= SnapshotTTL)
            {
                RefreshSnapshotLocked();
            }
            return UdpOwnerSnapshot!.TryGetValue(port, out pid);
        }

        /// Caller must hold CacheLock.
        private static void RefreshSnapshotLocked()
        {
            UdpOwnerSnapshot = SnapshotUdpPortOwners();
            UdpOwnerSnapshotAt = DateTime.UtcNow;
        }

        private static ProcessInfoReport GetProcessInfoForPid(int pid)
        {
            lock (CacheLock)
            {
                if (PidCache.TryGetValue(pid, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTTL)
                {
                    return cached.Report;
                }
            }
            var report = GetProcessDetails(pid);
            lock (CacheLock)
            {
                // D067: evict stale entries before insert so the cache cannot grow unbounded.
                // Remove entries older than CacheTTL to free space, then evict oldest if still
                // over capacity. This is O(n) but the cache is bounded and the operation is rare.
                if (PidCache.Count >= MaxPidCacheEntries)
                {
                    var cutoff = DateTime.UtcNow.Add(-CacheTTL);
                    var stale = PidCache.Where(kvp => kvp.Value.CachedAt < cutoff).Select(kvp => kvp.Key).ToList();
                    foreach (var k in stale) PidCache.Remove(k);

                    // If still over capacity after removing stale entries, remove the oldest.
                    if (PidCache.Count >= MaxPidCacheEntries)
                    {
                        var oldest = PidCache.OrderBy(kvp => kvp.Value.CachedAt).FirstOrDefault();
                        if (oldest.Key != pid) PidCache.Remove(oldest.Key);
                    }
                }
                PidCache[pid] = new CacheEntry { Report = report, CachedAt = DateTime.UtcNow };
            }
            return report;
        }

        // MARK: GetExtendedUdpTable snapshot (iphlpapi)

        private const int AF_INET = 2;
        private const int AF_INET6 = 23;
        private const int UDP_TABLE_OWNER_PID = 1;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize,
                                                       bool bOrder, int ulAf, int tableClass,
                                                       uint reserved);

        /// One merged v4+v6 kernel snapshot of local UDP port → owning pid. Any failure yields an
        /// empty map (→ Unknown attribution downstream) — attribution must never break capture.
        /// A port bound by two different pids (SO_REUSEADDR et al.) is recorded as ambiguous
        /// (pid -1), which `GetProcessDetails` resolves to Unknown rather than picking one.
        private static Dictionary<ushort, int> SnapshotUdpPortOwners()
        {
            var map = new Dictionary<ushort, int>();
            if (!OperatingSystem.IsWindows()) return map;   // capture itself is Windows-only
            // MIB_UDPROW_OWNER_PID: dwLocalAddr@0, dwLocalPort@4, dwOwningPid@8 (12 bytes).
            AppendUdpOwners(AF_INET, rowSize: 12, portOffset: 4, pidOffset: 8, map);
            // MIB_UDP6ROW_OWNER_PID: ucLocalAddr[16]@0, dwLocalScopeId@16, dwLocalPort@20,
            // dwOwningPid@24 (28 bytes).
            AppendUdpOwners(AF_INET6, rowSize: 28, portOffset: 20, pidOffset: 24, map);
            return map;
        }

        private static void AppendUdpOwners(int family, int rowSize, int portOffset, int pidOffset,
                                            Dictionary<ushort, int> map)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int size = 0;
                uint result = GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, UDP_TABLE_OWNER_PID, 0);
                if (result != ERROR_INSUFFICIENT_BUFFER || size <= 4) return;
                buffer = Marshal.AllocHGlobal(size);
                result = GetExtendedUdpTable(buffer, ref size, false, family, UDP_TABLE_OWNER_PID, 0);
                if (result != 0) return;
                int count = Marshal.ReadInt32(buffer);
                // Never walk past the bytes the API actually filled.
                if (count <= 0 || (long)count * rowSize > (long)size - 4) return;
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(buffer, 4 + i * rowSize);
                    ushort port = PortFromUdpTableEntry((uint)Marshal.ReadInt32(row, portOffset));
                    int pid = Marshal.ReadInt32(row, pidOffset);
                    if (map.TryGetValue(port, out int existing) && existing != pid)
                    {
                        map[port] = -1;   // ambiguous owner: report Unknown, never pick one
                    }
                    else
                    {
                        map[port] = pid;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* a failed snapshot reads as Unknown; capture continues */ }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
        }

        /// `dwLocalPort` in the MIB_UDP*ROW_OWNER_PID structs carries the port in network byte
        /// order in its low 16 bits (upper bytes undefined). Pure so the byte math is testable.
        internal static ushort PortFromUdpTableEntry(uint dwLocalPort)
        {
            return (ushort)(((dwLocalPort & 0xFF) << 8) | ((dwLocalPort >> 8) & 0xFF));
        }

        private static ProcessInfoReport GetProcessDetails(int pid)
        {
            // pid 0 is the unowned/System-Idle sentinel and -1 our ambiguous-owner marker:
            // neither identifies a real originator.
            if (pid <= 0) return ProcessInfoReport.Unknown;
            string? name = null;
            string? path = null;
            try
            {
                using (var proc = Process.GetProcessById(pid))
                {
                    name = proc.ProcessName;
                    try { path = proc.MainModule?.FileName; }
                    catch (OperationCanceledException) { throw; }
                    catch { /* SYSTEM services / missing SeDebugPrivilege: keep the name (E035) */ }
                }
            }
            catch (ArgumentException) { }
            catch (OperationCanceledException) { throw; }
            catch { }

            return BuildDetailsReport(pid, name, path);
        }

        /// E035: a null path (SYSTEM services, denied module access) must NOT discard the
        /// pid+name that were successfully obtained — partial attribution mirrors macOS
        /// `pidToProcessInfo`. `Unknown` is reserved for "no identity could be established
        /// at all." Split out pure so the decision table is testable cross-platform.
        internal static ProcessInfoReport BuildDetailsReport(int pid, string? name, string? path)
        {
            if (name == null && path == null)
            {
                return ProcessInfoReport.Unknown;
            }
            var (signingId, trust) = path != null ? SigningIdentifier(path) : (null, SignatureTrust.Unverifiable);
            return new ProcessInfoReport
            {
                Path = path,
                SigningID = signingId,
                SignatureTrust = trust,
                Pid = pid,
                Name = name ?? FileNameWithoutExtension(path!)
            };
        }

        /// Last path component minus extension, splitting on BOTH separators explicitly so the
        /// derivation is deterministic regardless of the host the code (or its tests) runs on.
        private static string FileNameWithoutExtension(string path)
        {
            int slash = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            string file = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = file.LastIndexOf('.');
            return dot > 0 ? file.Substring(0, dot) : file;
        }

        /// D112: reads the Authenticode signing certificate subject AND verifies it via
        /// X509Chain.Build. Returns (subject, trust) so the forensic report carries both
        /// the signer identity and whether that identity was actually verified.
        /// Previously, CreateFromSignedFile extracted the subject without any verification —
        /// a tampered binary with a valid-looking cert block would appear trustworthy.
        private static (string? subject, SignatureTrust trust) SigningIdentifier(string executablePath)
        {
            if (!OperatingSystem.IsWindows())
                return (null, SignatureTrust.Unverifiable);
            try
            {
                using var cert = X509Certificate.CreateFromSignedFile(executablePath);
                // Verify the certificate chain — this checks expiry, revocation, and trust root.
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
                bool chainValid = chain.Build(new X509Certificate2(cert));
                var trust = chainValid ? SignatureTrust.Valid : SignatureTrust.Invalid;
                return (cert.Subject, trust);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // No signature block at all — genuinely unsigned.
                return (null, SignatureTrust.Unsigned);
            }
            catch
            {
                // Could not check — unverifiable, not unsigned.
                return (null, SignatureTrust.Unverifiable);
            }
        }
    }
}
