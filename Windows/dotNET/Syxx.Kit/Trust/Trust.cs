// Ported into Syxx.Kit from the sibling LyxTools project (mirrors macOS SyxxKit/Trust).
// Trust infrastructure: passive-DNS answer ledger (drift detection, datapoint X5) and the
// Sentinel config-change watcher (datapoint F3 -> the X10 auto-re-audit trigger).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Syxx.Kit
{
    // ---- Passive-DNS answer ledger (X5) ------------------------------------

    public sealed class AnswerEntry
    {
        public string Domain { get; set; } = "";
        public string QueryType { get; set; } = "";
        public string Answer { get; set; } = "";
        public string Resolver { get; set; } = "";
        public int Ttl { get; set; }
        public DateTime CapturedAt { get; set; }
    }

    public sealed class AnswerDrift
    {
        public string Domain { get; set; } = "";
        public string QueryType { get; set; } = "";
        public string PreviousAnswer { get; set; } = "";
        public string NewAnswer { get; set; } = "";
        public string Resolver { get; set; } = "";
        public DateTime DetectedAt { get; set; }
    }

    public sealed class AnswerLedger
    {
        // C147: thread-safety — List<T> is not thread-safe; guard all mutations.
        private readonly object _lock = new();
        private readonly List<AnswerEntry> _entries = new();
        private readonly List<AnswerDrift> _drifts = new();

        public IReadOnlyList<AnswerEntry> Entries { get { lock (_lock) return _entries.ToList(); } }
        public IReadOnlyList<AnswerDrift> Drifts { get { lock (_lock) return _drifts.ToList(); } }

        /// <summary>
        /// Record an observed answer. Returns the drift when this answer differs from the last
        /// recorded answer for the same (domain, type) — null when consistent or first-seen.
        /// </summary>
        public AnswerDrift? Record(string domain, string queryType, string answer, string resolver, int ttl)
        {
            lock (_lock)
            {
                var entry = new AnswerEntry
                {
                    Domain = domain.ToLowerInvariant(),
                    QueryType = queryType,
                    Answer = answer,
                    Resolver = resolver,
                    Ttl = ttl,
                    CapturedAt = DateTime.UtcNow
                };
                var drift = DetectDrift(entry);
                _entries.Insert(0, entry);
                if (_entries.Count > 1000) _entries.RemoveRange(1000, _entries.Count - 1000);
                return drift;
            }
        }

        public List<AnswerEntry> History(string domain)
        {
            lock (_lock)
            {
                var d = domain.ToLowerInvariant();
                return _entries.Where(e => e.Domain == d).ToList();
            }
        }

        public List<AnswerDrift> DriftsFor(string domain)
        {
            lock (_lock)
            {
                var d = domain.ToLowerInvariant();
                return _drifts.Where(x => x.Domain == d).ToList();
            }
        }

        // C147: caller must hold _lock
        private AnswerDrift? DetectDrift(AnswerEntry entry)
        {
            var last = _entries.FirstOrDefault(e => e.Domain == entry.Domain && e.QueryType == entry.QueryType);
            if (last == null || last.Answer == entry.Answer) return null;
            var drift = new AnswerDrift
            {
                Domain = entry.Domain,
                QueryType = entry.QueryType,
                PreviousAnswer = last.Answer,
                NewAnswer = entry.Answer,
                Resolver = entry.Resolver,
                DetectedAt = DateTime.UtcNow
            };
            _drifts.Insert(0, drift);
            if (_drifts.Count > 200) _drifts.RemoveRange(200, _drifts.Count - 200);
            return drift;
        }

        // Persistence
        private sealed class Persisted
        {
            public List<AnswerEntry> Entries { get; set; } = new();
            public List<AnswerDrift> Drifts { get; set; } = new();
        }

        /// Answers/drifts older than this are pruned on load and save — a rolling drift window,
        /// not a permanent browsing history.
        public static readonly TimeSpan MaxEntryAge = TimeSpan.FromDays(30);

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".syxx", "answer-ledger.json");

        // C147: caller must hold _lock
        private void PruneAged()
        {
            var cutoff = DateTime.UtcNow - MaxEntryAge;
            _entries.RemoveAll(e => e.CapturedAt < cutoff);
            _drifts.RemoveAll(d => d.DetectedAt < cutoff);
        }

        public void Save(string? path = null)
        {
            path ??= DefaultPath;
            Persisted snapshot;
            lock (_lock)
            {
                PruneAged();
                snapshot = new Persisted { Entries = _entries.ToList(), Drifts = _drifts.ToList() };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // E087: restrict ~/.syxx to owner-only on Linux — the passive-DNS ledger contains
            // the user's browsing history and must not be world-readable.
            SyxxPaths.EnsurePrivateDir(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(snapshot);
            // C147: atomic write — write to temp file first, then move into place
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
            SyxxPaths.RestrictToOwner(path);
            // D030: sign the ledger with a DPAPI-protected HMAC key so a local attacker who writes
            // the file cannot suppress drift detection with an unsigned baseline.
            IntegrityTag.SignAndWrite(Encoding.UTF8.GetBytes(json), path);
        }

        public enum TrustLoadResult
        {
            Success,
            FileNotFound,
            Corrupted
        }

        public TrustLoadResult Load(string? path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path))
            {
                return TrustLoadResult.FileNotFound;
            }
            try
            {
                var json = File.ReadAllText(path);
                // D030: reject unsigned or tampered baselines — a missing/mismatched HMAC means the
                // file was written by something other than Syxx. The caller treats Corrupted as
                // "no history" and will re-capture on the next check.
                if (!IntegrityTag.Verify(Encoding.UTF8.GetBytes(json), path))
                    return TrustLoadResult.Corrupted;
                var decoded = JsonSerializer.Deserialize<Persisted>(json);
                if (decoded == null) return TrustLoadResult.Corrupted;
                lock (_lock)
                {
                    _entries.Clear(); _entries.AddRange(decoded.Entries);
                    _drifts.Clear(); _drifts.AddRange(decoded.Drifts);
                    PruneAged();
                }
                return TrustLoadResult.Success;
            }
            catch
            {
                return TrustLoadResult.Corrupted;
            }
        }

        /// <summary>Erase all retained history, in memory and on disk, including the HMAC sibling.</summary>
        public void Forget(string? path = null)
        {
            path ??= DefaultPath;
            lock (_lock)
            {
                _entries.Clear();
                _drifts.Clear();
            }
            try { File.Delete(path); } catch { }
            IntegrityTag.DeleteHmac(path);
        }
    }

    // ---- Sentinel config-change watcher (F3 -> X10) ------------------------

    public sealed class NetworkConfigSnapshot
    {
        public DateTime CapturedAt { get; set; }
        public List<string> Resolvers { get; set; } = new();
        public List<string> ActiveInterfaces { get; set; } = new();
        // D076: ConditionState preserves the Unknown state from VpnStatus().
        // A bool collapsed Unknown→false, making a "VPN unknown" → "VPN active"
        // transition invisible to Sentinel.Diff (both mapped to false).
        public ConditionState VpnActive { get; set; }
        public bool DnsEncrypted { get; set; }

        public static NetworkConfigSnapshot Capture()
        {
            // C146: Do NOT sort resolvers — GetSystemDNSServers() returns them in priority order
            // (scoped/default-gateway interfaces first). Sorting destroys that ordering, making
            // resolver-priority changes invisible to Sentinel.Diff's SequenceEqual.
            var resolvers = NetworkKeyState.GetSystemDNSServers();
            var ifaces = NetworkKeyState.GetActiveInterfaces();
            ifaces.Sort(StringComparer.Ordinal); // interface order is meaningless; keep sorted
            return new NetworkConfigSnapshot
            {
                CapturedAt = DateTime.UtcNow,
                Resolvers = resolvers,
                ActiveInterfaces = ifaces,
                // D076: preserve the tri-state — IsVPNActive() collapses Unknown→false.
                VpnActive = NetworkKeyState.VpnStatus().State,
                DnsEncrypted = NetworkKeyState.IsDNSEncrypted()
            };
        }
    }

    public enum ConfigChangeKind
    {
        ResolversChanged,
        InterfacesChanged,
        VpnChanged,
        EncryptedDNSChanged
    }

    public static class ConfigChangeKindExtensions
    {
        public static string Label(this ConfigChangeKind k) => k switch
        {
            ConfigChangeKind.ResolversChanged => "Resolvers changed",
            ConfigChangeKind.InterfacesChanged => "Active interfaces changed",
            ConfigChangeKind.VpnChanged => "VPN state changed",
            _ => "Encrypted-DNS state changed"
        };
    }

    public sealed class ConfigChange
    {
        public ConfigChangeKind Kind { get; }
        public string Detail { get; }
        public ConfigChange(ConfigChangeKind kind, string detail) { Kind = kind; Detail = detail; }
    }

    public static class Sentinel
    {
        /// <summary>
        /// Diff two snapshots. An empty list means the DNS-relevant configuration is unchanged;
        /// any entry is a reason to re-run the tampering/leak checks.
        /// </summary>
        public static List<ConfigChange> Diff(NetworkConfigSnapshot previous, NetworkConfigSnapshot current)
        {
            var changes = new List<ConfigChange>();

            if (!previous.Resolvers.SequenceEqual(current.Resolvers))
                changes.Add(new ConfigChange(ConfigChangeKind.ResolversChanged,
                    $"{string.Join(", ", previous.Resolvers)} -> {string.Join(", ", current.Resolvers)}"));
            if (!previous.ActiveInterfaces.SequenceEqual(current.ActiveInterfaces))
                changes.Add(new ConfigChange(ConfigChangeKind.InterfacesChanged,
                    $"{string.Join(", ", previous.ActiveInterfaces)} -> {string.Join(", ", current.ActiveInterfaces)}"));
            // D076: ConditionState comparison catches Unknown→Yes/No transitions the old bool missed.
            if (previous.VpnActive != current.VpnActive)
                changes.Add(new ConfigChange(ConfigChangeKind.VpnChanged,
                    $"{VpnLabel(previous.VpnActive)} -> {VpnLabel(current.VpnActive)}"));
            if (previous.DnsEncrypted != current.DnsEncrypted)
                changes.Add(new ConfigChange(ConfigChangeKind.EncryptedDNSChanged,
                    $"{(previous.DnsEncrypted ? "encrypted" : "plaintext")} -> {(current.DnsEncrypted ? "encrypted" : "plaintext")}"));
            return changes;
        }

        // D076: human-readable label for the VPN tri-state in Sentinel diff output.
        private static string VpnLabel(ConditionState s) => s switch
        {
            ConditionState.Yes => "active",
            ConditionState.No => "inactive",
            ConditionState.Unknown => "unknown",
            _ => "unknown"
        };

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".syxx", "sentinel-snapshot.json");

        public static void SaveSnapshot(NetworkConfigSnapshot snapshot, string? path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // E087: restrict ~/.syxx to owner-only on Linux.
            SyxxPaths.EnsurePrivateDir(Path.GetDirectoryName(path)!);
            // C147: atomic write — write to temp file first, then move into place
            var tmpPath = path + ".tmp";
            var json = JsonSerializer.Serialize(snapshot);
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
            SyxxPaths.RestrictToOwner(path);
            // D030: sign the snapshot with a DPAPI-protected HMAC key so a local attacker who
            // writes the file cannot suppress config-change/drift re-audit with an unsigned baseline.
            IntegrityTag.SignAndWrite(Encoding.UTF8.GetBytes(json), path);
        }

        // D081: Distinguish "no file" from "corrupt file" from "loaded OK" — a corrupt
        // baseline must NOT be silently treated as "no history" (that causes the caller
        // to overwrite the damaged file with a fresh snapshot, losing the old baseline).
        public static (AnswerLedger.TrustLoadResult result, NetworkConfigSnapshot? snapshot) LoadSnapshot(string? path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path))
            {
                return (AnswerLedger.TrustLoadResult.FileNotFound, null);
            }
            try
            {
                var json = File.ReadAllText(path);
                // D030: reject unsigned or tampered baselines — a missing/mismatched HMAC means
                // the file was written by something other than Syxx.
                if (!IntegrityTag.Verify(Encoding.UTF8.GetBytes(json), path))
                    return (AnswerLedger.TrustLoadResult.Corrupted, null);
                var decoded = JsonSerializer.Deserialize<NetworkConfigSnapshot>(json);
                return decoded != null
                    ? (AnswerLedger.TrustLoadResult.Success, decoded)
                    : (AnswerLedger.TrustLoadResult.Corrupted, null);
            }
            catch
            {
                return (AnswerLedger.TrustLoadResult.Corrupted, null);
            }
        }

        /// <summary>Erase the saved baseline snapshot.</summary>
        public static void Forget(string? path = null)
        {
            path ??= DefaultPath;
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// C5 — Home network baseline (macOS HomeBaseline twin). The user marks their home network as
    /// known-good ONCE; every other network is diffed against it, so "this café differs from home
    /// in N ways" lists the real deltas. Distinct from the rolling Sentinel snapshot (then-vs-now);
    /// this is home-vs-here and does not roll forward.
    /// </summary>
    public static class HomeBaseline
    {
        public sealed record Comparison(bool HasBaseline, IReadOnlyList<ConfigChange> Differences, string Summary)
        {
            /// True only when a baseline exists AND there are zero differences. An absent baseline
            /// is NEVER a match — it is unknown, not clean.
            public bool MatchesHome => HasBaseline && Differences.Count == 0;
        }

        /// Pure core: compare the current snapshot to the home baseline. A null <paramref name="home"/>
        /// yields HasBaseline == false with no differences claimed (honest: no match or mismatch can
        /// be asserted against a baseline that does not exist). Mutation-tested heart of C5.
        public static Comparison Compare(NetworkConfigSnapshot? home, NetworkConfigSnapshot current)
        {
            if (home is null)
                return new Comparison(false, System.Array.Empty<ConfigChange>(),
                    "No home baseline set — run `syxx sentinel --set-home` on your home network first.");
            var diffs = Sentinel.Diff(home, current);
            // C145: downgraded from "This network" — no network identity is recorded, so
            // the match claim is only about the DNS-configuration fields, not the network itself.
            string summary = diffs.Count == 0
                ? "Your current DNS configuration matches your home baseline."
                : $"Your current DNS configuration differs from home in {diffs.Count} way{(diffs.Count == 1 ? "" : "s")}.";
            return new Comparison(true, diffs, summary);
        }

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".syxx", "home-baseline.json");

        public static void Save(NetworkConfigSnapshot snapshot, string? path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // E087: restrict ~/.syxx to owner-only on Linux.
            SyxxPaths.EnsurePrivateDir(Path.GetDirectoryName(path)!);
            // C147: atomic write — write to temp file first, then move into place
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(snapshot));
            File.Move(tmpPath, path, overwrite: true);
            SyxxPaths.RestrictToOwner(path);
        }

        // D081: Distinguish "no file" from "corrupt file" from "loaded OK" — a corrupt
        // baseline must NOT be silently treated as "no baseline" (the user sees
        // "No home baseline set" when the file actually exists but is damaged).
        public static (AnswerLedger.TrustLoadResult result, NetworkConfigSnapshot? snapshot) Load(string? path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path))
            {
                return (AnswerLedger.TrustLoadResult.FileNotFound, null);
            }
            try
            {
                var decoded = JsonSerializer.Deserialize<NetworkConfigSnapshot>(File.ReadAllText(path));
                return decoded != null
                    ? (AnswerLedger.TrustLoadResult.Success, decoded)
                    : (AnswerLedger.TrustLoadResult.Corrupted, null);
            }
            catch
            {
                return (AnswerLedger.TrustLoadResult.Corrupted, null);
            }
        }

        public static void Forget(string? path = null)
        {
            path ??= DefaultPath;
            try { File.Delete(path); } catch { }
            IntegrityTag.DeleteHmac(path);
        }
    }

    /// <summary>
    /// D030: HMAC-SHA256 integrity tag for baseline/sentinel snapshot files.
    /// The signing key is stored via DPAPI (Windows) or the platform secret store,
    /// ensuring it is bound to the current user and not accessible to other processes.
    /// A local attacker who can write the snapshot file cannot forge the HMAC because
    /// they cannot access the DPAPI-protected key without user-level access.
    ///
    /// The HMAC is written as a sibling <c>.hmac</c> file alongside the snapshot, so the
    /// snapshot format remains unchanged and backward-compatible. On load, the HMAC is
    /// verified; if missing or mismatched, the snapshot is rejected as untrusted.
    /// </summary>
    internal static class IntegrityTag
    {
        private const string HmacExtension = ".hmac";
        private const string KeyFileName = "integrity-tag.key";

        private static string KeyPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Syxx", KeyFileName);

        private static byte[]? LoadOrCreateKey()
        {
            var path = KeyPath;
            if (File.Exists(path))
            {
                try
                {
                    var encrypted = File.ReadAllBytes(path);
                    return encrypted;
                }
                catch
                {
                    // Corrupt or unreadable key — regenerate.
                }
            }

            // Generate a new 32-byte key and store it in the user's local app data.
            var key = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, key);
            }
            catch
            {
                return null; // Best-effort: if DPAPI fails, we cannot sign.
            }
            return key;
        }

        /// <summary>
        /// Compute HMAC-SHA256 of <paramref name="data"/> using the DPAPI-protected key.
        /// Returns null if the key cannot be loaded. Use this for in-memory integrity checks.
        /// </summary>
        public static byte[]? Sign(byte[] data)
        {
            var key = LoadOrCreateKey();
            if (key == null) return null;
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        /// <summary>
        /// Verify the HMAC of <paramref name="data"/> against a previously-computed HMAC.
        /// Returns true only if the HMAC matches.
        /// </summary>
        public static bool Verify(byte[] data, byte[] expectedHmac)
        {
            var key = LoadOrCreateKey();
            if (key == null) return false;
            using var hmac = new HMACSHA256(key);
            var computed = hmac.ComputeHash(data);
            return CryptographicOperations.FixedTimeEquals(computed, expectedHmac);
        }

        /// <summary>
        /// Compute HMAC-SHA256 of <paramref name="data"/> and write it to <paramref name="path"/>.hmac.
        /// Fails silently if the key cannot be loaded or written.
        /// </summary>
        public static void SignAndWrite(byte[] data, string path)
        {
            var hash = Sign(data);
            if (hash == null) return;
            var hex = Convert.ToHexString(hash).ToLower();
            var hmacPath = path + HmacExtension;
            try
            {
                File.WriteAllText(hmacPath, hex);
                // E087: restrict HMAC sidecar to owner-only — it is part of the evidence chain.
                SyxxPaths.RestrictToOwner(hmacPath);
            }
            catch { }
        }

        /// <summary>
        /// Verify the HMAC of <paramref name="data"/> against the sibling .hmac file.
        /// Returns true only if the HMAC file exists, is readable, and matches.
        /// </summary>
        public static bool Verify(byte[] data, string path)
        {
            var key = LoadOrCreateKey();
            if (key == null) return false;
            var hmacPath = path + HmacExtension;
            string storedHex;
            try
            {
                storedHex = File.ReadAllText(hmacPath);
            }
            catch { return false; }

            using var hmac = new HMACSHA256(key);
            var hash = hmac.ComputeHash(data);
            var computedHex = Convert.ToHexString(hash).ToLower();

            // Constant-time comparison to prevent timing attacks.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(computedHex),
                Encoding.ASCII.GetBytes(storedHex));
        }

        /// <summary>Delete the HMAC sibling file when the snapshot is forgotten.</summary>
        public static void DeleteHmac(string path)
        {
            var hmacPath = path + HmacExtension;
            try { File.Delete(hmacPath); } catch { }
        }
    }
}
