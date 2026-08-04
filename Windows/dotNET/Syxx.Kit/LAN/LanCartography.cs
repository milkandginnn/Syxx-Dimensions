// Ported into Syxx.Kit from the sibling LyxTools project (mirrors macOS SyxxKit/LAN).
// LAN cartography: discover on-link devices to surface a rogue DNS/DHCP server.
//
// Windows differs from macOS in the `arp` output format, so the parser here is
// Windows-specific while the classification / snapshot-lifecycle / snapshot-diff logic
// mirrors the Swift side one-for-one so both platforms produce the same shapes.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Syxx.Kit
{
    public enum LANAddressKind
    {
        PrivateUnicast,
        LinkLocal,
        Multicast,
        Loopback,
        Broadcast,
        Other
    }

    public static class LANAddressKindExtensions
    {
        public static string Label(this LANAddressKind kind) => kind switch
        {
            LANAddressKind.PrivateUnicast => "Private unicast",
            LANAddressKind.LinkLocal => "Link-local",
            LANAddressKind.Multicast => "Multicast",
            LANAddressKind.Loopback => "Loopback",
            LANAddressKind.Broadcast => "Broadcast",
            _ => "Other"
        };

        public static bool IsProbeable(this LANAddressKind kind) => kind switch
        {
            LANAddressKind.PrivateUnicast => true,
            LANAddressKind.LinkLocal => true,
            LANAddressKind.Other => true,
            _ => false
        };
    }

    public static class NetworkAddressClassifier
    {
        public static LANAddressKind KindForIPv4(string address)
        {
            var octets = address.Split('.');
            if (octets.Length != 4) return LANAddressKind.Other;
            var b = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(octets[i], out b[i]) || b[i] < 0 || b[i] > 255)
                    return LANAddressKind.Other;
            }

            if (b[0] == 255 && b[1] == 255 && b[2] == 255 && b[3] == 255) return LANAddressKind.Broadcast;
            if (b[0] == 127) return LANAddressKind.Loopback;
            if (b[0] >= 224 && b[0] <= 239) return LANAddressKind.Multicast;
            if (b[0] == 169 && b[1] == 254) return LANAddressKind.LinkLocal;
            if (b[0] == 10) return LANAddressKind.PrivateUnicast;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return LANAddressKind.PrivateUnicast;
            if (b[0] == 192 && b[1] == 168) return LANAddressKind.PrivateUnicast;
            return LANAddressKind.Other;
        }
    }

    public enum ARPEntryState
    {
        Reachable,
        Incomplete,
        Permanent,
        Unknown
    }

    public sealed class ARPEntry
    {
        public string? Host { get; }
        public string IPAddress { get; }
        public string MacAddress { get; }
        public string Interface { get; }
        public ARPEntryState State { get; }
        public string RawLine { get; }

        public string Id => $"{Interface}|{IPAddress}|{MacAddress}";

        public ARPEntry(string? host, string ipAddress, string macAddress, string iface, ARPEntryState state, string rawLine)
        {
            Host = host;
            IPAddress = ipAddress;
            MacAddress = macAddress;
            Interface = iface;
            State = state;
            RawLine = rawLine;
        }
    }

    /// Parses Windows `arp -a` output. Format:
    ///   Interface: 192.168.1.5 --- 0xNN
    ///     Internet Address      Physical Address      Type
    ///     192.168.1.1           aa-bb-cc-dd-ee-ff     dynamic
    public sealed class ARPParseResult
    {
        public IReadOnlyList<ARPEntry> Entries { get; }
        public int CandidateCount { get; }
        public int RejectedCandidateCount { get; }

        public ARPParseResult(
            IReadOnlyList<ARPEntry> entries,
            int candidateCount,
            int rejectedCandidateCount)
        {
            Entries = entries.ToList().AsReadOnly();
            CandidateCount = candidateCount;
            RejectedCandidateCount = rejectedCandidateCount;
        }
    }

    public static class WindowsArpParser
    {
        public static List<ARPEntry> Parse(string output)
        {
            return ParseWithDiagnostics(output).Entries.ToList();
        }

        public static ARPParseResult ParseWithDiagnostics(string output)
        {
            var entries = new List<ARPEntry>();
            string currentInterface = "unknown";
            int candidateCount = 0;
            int rejectedCandidateCount = 0;

            foreach (var raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (TryParseInterfaceHeader(line, out string? interfaceAddress))
                {
                    currentInterface = interfaceAddress;
                    continue;
                }

                bool isCandidate = LooksLikeCandidateRow(line);
                var entry = ParseRow(line, currentInterface);
                if (entry != null)
                {
                    candidateCount += 1;
                    entries.Add(entry);
                }
                else if (isCandidate)
                {
                    candidateCount += 1;
                    rejectedCandidateCount += 1;
                }
            }
            return new ARPParseResult(entries, candidateCount, rejectedCandidateCount);
        }

        private static bool TryParseInterfaceHeader(string line, out string interfaceAddress)
        {
            interfaceAddress = "";
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int separatorIndex = Array.IndexOf(tokens, "---");
            if (separatorIndex < 1) return false;

            string candidate = tokens[separatorIndex - 1];
            if (!IsDottedQuad(candidate)) return false;

            interfaceAddress = candidate;
            return true;
        }

        private static ARPEntry? ParseRow(string line, string iface)
        {
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) return null;

            string ip = tokens[0];
            if (!IsDottedQuad(ip)) return null;

            string mac = NormalizeMac(tokens[1]);
            if (mac.Length == 0) return null;

            string type = tokens.Length >= 3 ? tokens[2].ToLowerInvariant() : "";
            ARPEntryState state = type switch
            {
                "dynamic" => ARPEntryState.Reachable,
                "static" => ARPEntryState.Permanent,
                "invalid" => ARPEntryState.Incomplete,
                _ => ARPEntryState.Unknown
            };

            return new ARPEntry(host: null, ipAddress: ip, macAddress: mac, iface: iface, state: state, rawLine: line);
        }

        private static bool LooksLikeCandidateRow(string line)
        {
            string firstToken = line
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            return firstToken.Count(character => character == '.') == 3;
        }

        // E085: internal so LinuxArpParser can reuse this check.
        internal static bool IsDottedQuadInternal(string value)
        {
            string[] octets = value.Split('.');
            if (octets.Length != 4) return false;
            return octets.All(octet =>
                int.TryParse(octet, out int parsed) &&
                parsed >= byte.MinValue &&
                parsed <= byte.MaxValue);
        }

        private static bool IsDottedQuad(string value) => IsDottedQuadInternal(value);

        /// Windows renders MACs as aa-bb-cc-dd-ee-ff; normalize to lowercase colon form so
        /// snapshots diff cleanly and match the macOS representation.
        internal static string NormalizeMac(string token)
        {
            string t = token.Replace('-', ':').ToLowerInvariant();
            var groups = t.Split(':');
            if (groups.Length != 6) return "";
            foreach (var g in groups)
            {
                if (g.Length == 0 || g.Length > 2) return "";
                foreach (var c in g)
                    if (!Uri.IsHexDigit(c)) return "";
            }
            return string.Join(":", groups.Select(g => g.PadLeft(2, '0')));
        }
    }

    // E085: Linux ARP parser. `ip neigh show` is always present on modern Ubuntu (no net-tools
    // needed) and produces a machine-readable format. Falls back to `arp -a` Linux format
    // (? (192.168.1.1) at aa:bb:cc:dd:ee:ff [ether] on eth0) for distros that still ship net-tools.
    public static class LinuxArpParser
    {
        public static List<ARPEntry> Parse(string output)
        {
            return ParseWithDiagnostics(output).Entries.ToList();
        }

        public static ARPParseResult ParseWithDiagnostics(string output)
        {
            var entries = new List<ARPEntry>();
            int candidateCount = 0;
            int rejectedCandidateCount = 0;

            foreach (var raw in output.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                bool isCandidate = LooksLikeCandidateRow(line);
                var entry = TryParseIpNeigh(line) ?? TryParseLinuxArp(line);
                if (entry != null)
                {
                    candidateCount++;
                    entries.Add(entry);
                }
                else if (isCandidate)
                {
                    candidateCount++;
                    rejectedCandidateCount++;
                }
            }
            return new ARPParseResult(entries, candidateCount, rejectedCandidateCount);
        }

        // ip neigh show format: "192.168.1.1 dev eth0 lladdr aa:bb:cc:dd:ee:ff REACHABLE"
        private static ARPEntry? TryParseIpNeigh(string line)
        {
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) return null;

            string ip = tokens[0];
            if (!WindowsArpParser.IsDottedQuadInternal(ip)) return null;

            string iface = "unknown";
            string mac = "";
            ARPEntryState state = ARPEntryState.Unknown;

            for (int i = 1; i < tokens.Length; i++)
            {
                switch (tokens[i].ToLowerInvariant())
                {
                    case "dev" when i + 1 < tokens.Length:
                        iface = tokens[++i];
                        break;
                    case "lladdr" when i + 1 < tokens.Length:
                        mac = NormalizeMacColon(tokens[++i]);
                        break;
                    case "reachable":
                        state = ARPEntryState.Reachable;
                        break;
                    case "permanent":
                        state = ARPEntryState.Permanent;
                        break;
                    case "stale":
                        state = ARPEntryState.Reachable; // stale entries are still reachable
                        break;
                    case "delay":
                        state = ARPEntryState.Reachable;
                        break;
                    case "probe":
                        state = ARPEntryState.Incomplete;
                        break;
                    case "failed":
                        state = ARPEntryState.Incomplete;
                        break;
                    case "incomplete":
                        state = ARPEntryState.Incomplete;
                        break;
                    case "noarp":
                        state = ARPEntryState.Permanent;
                        break;
                }
            }

            if (mac.Length == 0 && state != ARPEntryState.Incomplete) return null;
            return new ARPEntry(host: null, ipAddress: ip, macAddress: mac, iface: iface, state: state, rawLine: line);
        }

        // Linux arp -a format: "? (192.168.1.1) at aa:bb:cc:dd:ee:ff [ether] on eth0"
        // or: "hostname (192.168.1.2) at aa:bb:cc:dd:ee:ff [ether] on eth0"
        private static ARPEntry? TryParseLinuxArp(string line)
        {
            // Find IP in parentheses
            int openParen = line.IndexOf('(', StringComparison.Ordinal);
            int closeParen = line.IndexOf(')', StringComparison.Ordinal);
            if (openParen < 0 || closeParen <= openParen) return null;

            string ip = line.Substring(openParen + 1, closeParen - openParen - 1);
            if (!WindowsArpParser.IsDottedQuadInternal(ip)) return null;

            // Extract hostname (everything before the parenthesis)
            string? host = openParen > 0 ? line.Substring(0, openParen).Trim() : null;
            if (host == "?") host = null;

            // Find MAC after "at "
            string remainder = line.Substring(closeParen + 1);
            int atIdx = remainder.IndexOf("at ", StringComparison.Ordinal);
            if (atIdx < 0) return null;
            string afterAt = remainder.Substring(atIdx + 3);
            var tokens = afterAt.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return null;

            string mac = NormalizeMacColon(tokens[0]);
            if (mac.Length == 0) return null;

            // Interface is the last token (after "[ether]" or "[type]")
            string iface = "unknown";
            if (tokens.Length >= 3 && tokens[tokens.Length - 2].StartsWith("[", StringComparison.Ordinal))
            {
                iface = tokens[tokens.Length - 1];
            }

            return new ARPEntry(host: host, ipAddress: ip, macAddress: mac, iface: iface, state: ARPEntryState.Reachable, rawLine: line);
        }

        private static bool LooksLikeCandidateRow(string line)
        {
            // ip neigh: first token is an IP
            var firstToken = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (firstToken.Count(c => c == '.') == 3) return true;
            // arp -a: contains "(ip)"
            return line.Contains("(", StringComparison.Ordinal) && line.Contains(")", StringComparison.Ordinal);
        }

        private static string NormalizeMacColon(string token)
        {
            // Linux already uses colon-separated lowercase MACs
            return WindowsArpParser.NormalizeMac(token);
        }
    }

    public sealed class LANDevice
    {
        public string IPAddress { get; }
        public string MacAddress { get; }
        public string Interface { get; }
        public string? Host { get; }
        public ARPEntryState State { get; }
        public LANAddressKind AddressKind { get; }
        public IReadOnlyList<string> Notes { get; }
        public string Id => $"{Interface}|{MacAddress}|{IPAddress}";

        public LANDevice(ARPEntry entry)
        {
            IPAddress = entry.IPAddress;
            MacAddress = entry.MacAddress;
            Interface = entry.Interface;
            Host = entry.Host;
            State = entry.State;
            AddressKind = NetworkAddressClassifier.KindForIPv4(entry.IPAddress);
            Notes = BuildNotes(entry);
        }

        private static List<string> BuildNotes(ARPEntry entry)
        {
            var notes = new List<string>();
            if (entry.State == ARPEntryState.Incomplete) notes.Add("Incomplete ARP entry");
            if (entry.MacAddress.ToLowerInvariant() == "ff:ff:ff:ff:ff:ff") notes.Add("Broadcast MAC");
            var kind = NetworkAddressClassifier.KindForIPv4(entry.IPAddress);
            if (!kind.IsProbeable()) notes.Add(kind.Label());
            if (entry.Host == null) notes.Add("No hostname");
            return notes;
        }
    }

    public sealed class LANSnapshotSummary
    {
        public int DeviceCount { get; }
        public int ReachableCount { get; }
        public int IncompleteCount { get; }
        public IReadOnlyList<string> Interfaces { get; }

        public LANSnapshotSummary(int deviceCount, int reachableCount, int incompleteCount, IReadOnlyList<string> interfaces)
        {
            DeviceCount = deviceCount;
            ReachableCount = reachableCount;
            IncompleteCount = incompleteCount;
            Interfaces = interfaces;
        }
    }

    public enum LANSnapshotStatus
    {
        Complete,
        Partial,
        Unreadable
    }

    public sealed class LANSnapshot
    {
        public DateTime CapturedAt { get; }
        public IReadOnlyList<LANDevice> Devices { get; }
        public LANSnapshotSummary Summary { get; }
        public LANSnapshotStatus Status { get; }
        public string? Reason { get; }
        public int RejectedCandidateCount { get; }

        // E085: convenience aliases so LanCommand and Program.cs can check partial status
        // without knowing the enum. IsPartial is true for both Partial and Unreadable —
        // any snapshot that is not Complete must never be reported as "empty LAN".
        public bool IsPartial => Status != LANSnapshotStatus.Complete;
        public string? PartialReason => Reason;

        private LANSnapshot(
            IReadOnlyList<LANDevice> devices,
            DateTime capturedAt,
            LANSnapshotStatus status,
            string? reason,
            int rejectedCandidateCount)
        {
            CapturedAt = capturedAt;
            Devices = devices;
            Status = status;
            Reason = reason;
            RejectedCandidateCount = rejectedCandidateCount;
            Summary = new LANSnapshotSummary(
                deviceCount: devices.Count,
                reachableCount: devices.Count(d => d.State == ARPEntryState.Reachable || d.State == ARPEntryState.Permanent),
                incompleteCount: devices.Count(d => d.State == ARPEntryState.Incomplete),
                interfaces: devices.Select(d => d.Interface).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList());
        }

        public static LANSnapshot Complete(
            IReadOnlyList<LANDevice> devices,
            DateTime? capturedAt = null)
        {
            return Create(
                devices,
                LANSnapshotStatus.Complete,
                reason: null,
                rejectedCandidateCount: 0,
                capturedAt);
        }

        public static LANSnapshot Partial(
            IReadOnlyList<LANDevice> devices,
            string reason,
            int rejectedCandidateCount = 0,
            DateTime? capturedAt = null)
        {
            return Create(
                devices,
                LANSnapshotStatus.Partial,
                reason,
                rejectedCandidateCount,
                capturedAt);
        }

        public static LANSnapshot Unreadable(
            string reason,
            int rejectedCandidateCount = 0,
            DateTime? capturedAt = null)
        {
            return Create(
                Array.Empty<LANDevice>(),
                LANSnapshotStatus.Unreadable,
                reason,
                rejectedCandidateCount,
                capturedAt);
        }

        public static LANSnapshot Create(
            IReadOnlyList<LANDevice> devices,
            LANSnapshotStatus status,
            string? reason,
            int rejectedCandidateCount = 0,
            DateTime? capturedAt = null)
        {
            ArgumentNullException.ThrowIfNull(devices);
            IReadOnlyList<LANDevice> stableDevices = devices.ToList().AsReadOnly();
            if (rejectedCandidateCount < 0)
                throw new ArgumentOutOfRangeException(nameof(rejectedCandidateCount));
            if (!Enum.IsDefined(status))
                throw new ArgumentOutOfRangeException(nameof(status));

            bool hasReason = !string.IsNullOrWhiteSpace(reason);
            switch (status)
            {
                case LANSnapshotStatus.Complete
                    when stableDevices.Count == 0 || reason != null || rejectedCandidateCount != 0:
                    throw new ArgumentException(
                        "A complete LAN snapshot requires devices and cannot carry a reason or rejected candidates.");
                case LANSnapshotStatus.Partial when stableDevices.Count == 0 || !hasReason:
                    throw new ArgumentException(
                        "A partial LAN snapshot requires captured devices and a reason.");
                case LANSnapshotStatus.Unreadable when stableDevices.Count != 0 || !hasReason:
                    throw new ArgumentException(
                        "An unreadable LAN snapshot cannot carry devices and requires a reason.");
            }

            return new LANSnapshot(
                stableDevices,
                capturedAt ?? DateTime.UtcNow,
                status,
                hasReason ? reason!.Trim() : null,
                rejectedCandidateCount);
        }
    }

    public sealed class DuplicateMacBinding
    {
        public string MacAddress { get; }
        public IReadOnlyList<string> IPAddresses { get; }
        public IReadOnlyList<string> Interfaces { get; }

        public DuplicateMacBinding(
            string macAddress,
            IReadOnlyList<string> ipAddresses,
            IReadOnlyList<string> interfaces)
        {
            MacAddress = macAddress;
            IPAddresses = ipAddresses;
            Interfaces = interfaces;
        }
    }

    public sealed class LANDiff
    {
        public IReadOnlyList<LANDevice> NewDevices { get; }
        public IReadOnlyList<LANDevice> MissingDevices { get; }
        public IReadOnlyList<string> ChangedAddresses { get; }
        public IReadOnlyList<string> ChangedInterfaces { get; }
        public IReadOnlyList<DuplicateMacBinding> DuplicateMacBindings { get; }

        public bool HasChanges =>
            NewDevices.Count > 0 ||
            MissingDevices.Count > 0 ||
            ChangedAddresses.Count > 0 ||
            ChangedInterfaces.Count > 0 ||
            DuplicateMacBindings.Count > 0;

        public LANDiff(IReadOnlyList<LANDevice> newDevices, IReadOnlyList<LANDevice> missingDevices,
                       IReadOnlyList<string> changedAddresses, IReadOnlyList<string> changedInterfaces,
                       IReadOnlyList<DuplicateMacBinding> duplicateMacBindings)
        {
            NewDevices = newDevices;
            MissingDevices = missingDevices;
            ChangedAddresses = changedAddresses;
            ChangedInterfaces = changedInterfaces;
            DuplicateMacBindings = duplicateMacBindings;
        }
    }

    public sealed class CommandResult
    {
        public string Stdout { get; }
        public string Stderr { get; }
        public int? ExitCode { get; }
        public bool TimedOut { get; }

        public CommandResult(string stdout, string stderr, int? exitCode, bool timedOut)
        {
            Stdout = stdout;
            Stderr = stderr;
            ExitCode = exitCode;
            TimedOut = timedOut;
        }
    }

    public interface ICommandRunner
    {
        CommandResult Run(string executable, IReadOnlyList<string> arguments, int timeoutMs);
    }

    public sealed class SystemCommandRunner : ICommandRunner
    {
        public CommandResult Run(string executable, IReadOnlyList<string> arguments, int timeoutMs)
        {
            if (timeoutMs <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in arguments) psi.ArgumentList.Add(a);

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            object stdoutLock = new();
            object stderrLock = new();
            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data == null) return;
                lock (stdoutLock) stdout.AppendLine(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data == null) return;
                lock (stderrLock) stderr.AppendLine(eventArgs.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException($"Unable to launch {executable}.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            bool timedOut = !process.WaitForExit(timeoutMs);
            if (timedOut)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException ||
                    ex is NotSupportedException ||
                    ex is Win32Exception)
                {
                    // The process may have exited or denied termination. The bounded wait below
                    // still returns the captured result with TimedOut set.
                }

                if (process.WaitForExit(500))
                    process.WaitForExit();
            }
            else
            {
                // Flush asynchronous OutputDataReceived/ErrorDataReceived callbacks.
                process.WaitForExit();
            }

            string capturedStdout;
            string capturedStderr;
            lock (stdoutLock) capturedStdout = stdout.ToString();
            lock (stderrLock) capturedStderr = stderr.ToString();
            int? exitCode = !timedOut && process.HasExited ? process.ExitCode : null;
            return new CommandResult(capturedStdout, capturedStderr, exitCode, timedOut);
        }
    }

    public sealed class LANCartographer
    {
        private readonly ICommandRunner _runner;

        public LANCartographer(ICommandRunner? runner = null)
        {
            _runner = runner ?? new SystemCommandRunner();
        }

        /// <param name="maxDevices">Maximum devices to include (0 = unlimited). When exceeded the snapshot is flagged partial.</param>
        /// <param name="timeoutMs">Wall-clock deadline in milliseconds. A timeout is partial only when rows were captured; otherwise it is unreadable.</param>
        public LANSnapshot Snapshot(int maxDevices = 0, int timeoutMs = 10_000)
        {
            if (timeoutMs <= 0)
                return LANSnapshot.Unreadable("timeout must be positive");

            // E085: on Linux, prefer `ip neigh show` (always present on modern Ubuntu, no
            // net-tools needed). Fall back to `arp -a` with the Linux parser. On Windows,
            // the existing `arp -a` + WindowsArpParser is correct.
            bool isLinux = OperatingSystem.IsLinux();
            CommandResult command;
            try
            {
                if (isLinux)
                {
                    try
                    {
                        command = _runner.Run("ip", new[] { "neigh", "show" }, timeoutMs);
                    }
                    catch
                    {
                        // ip not available — fall back to arp -a (may not be installed either)
                        command = _runner.Run("arp", new[] { "-a" }, timeoutMs);
                    }
                }
                else
                {
                    command = _runner.Run("arp", new[] { "-a" }, timeoutMs);
                }
            }
            catch (Exception ex)
            {
                string reason = string.IsNullOrWhiteSpace(ex.Message)
                    ? "ARP/neighbour command could not be executed"
                    : ex.Message;
                return LANSnapshot.Unreadable(reason);
            }

            // E085: use the platform-appropriate parser.
            ARPParseResult parseResult = isLinux
                ? LinuxArpParser.ParseWithDiagnostics(command.Stdout)
                : WindowsArpParser.ParseWithDiagnostics(command.Stdout);
            var devices = parseResult.Entries
                .Select(e => new LANDevice(e))
                .OrderBy(d => d.Interface, StringComparer.Ordinal)
                .ThenBy(d => d.IPAddress, Comparer<string>.Create(CompareAddresses))
                .ToList();

            if (command.TimedOut)
                return devices.Count > 0
                    ? LANSnapshot.Partial(
                        devices,
                        "arp command timed out before completion",
                        parseResult.RejectedCandidateCount)
                    : LANSnapshot.Unreadable(
                        "arp command timed out before completion",
                        parseResult.RejectedCandidateCount);

            if (command.ExitCode != 0)
                return devices.Count > 0
                    ? LANSnapshot.Partial(
                        devices,
                        CommandFailureReason(command),
                        parseResult.RejectedCandidateCount)
                    : LANSnapshot.Unreadable(
                        CommandFailureReason(command),
                        parseResult.RejectedCandidateCount);

            if (devices.Count == 0)
                return LANSnapshot.Unreadable(
                    "arp output contained no parseable neighbour rows",
                    parseResult.RejectedCandidateCount);

            if (maxDevices > 0 && devices.Count > maxDevices)
                return LANSnapshot.Partial(
                    devices.Take(maxDevices).ToList(),
                    $"snapshot limited to {maxDevices} devices",
                    parseResult.RejectedCandidateCount);

            if (parseResult.RejectedCandidateCount > 0)
                return LANSnapshot.Partial(
                    devices,
                    ParserRejectionReason(parseResult.RejectedCandidateCount),
                    parseResult.RejectedCandidateCount);

            return LANSnapshot.Complete(devices);
        }

        private static string CommandFailureReason(CommandResult command)
        {
            string diagnostic = string.IsNullOrWhiteSpace(command.Stderr)
                ? command.Stdout.Trim()
                : command.Stderr.Trim();
            string reason = command.ExitCode is int exitCode
                ? $"arp command exited with code {exitCode}"
                : "arp command did not report an exit code";
            return diagnostic.Length == 0 ? reason : $"{reason}: {diagnostic}";
        }

        private static string ParserRejectionReason(int rejectedCandidateCount)
        {
            string noun = rejectedCandidateCount == 1 ? "row" : "rows";
            return $"arp parser rejected {rejectedCandidateCount} candidate {noun}";
        }

        public static LANDiff Diff(LANSnapshot previous, LANSnapshot current)
        {
            var previousIDs = new HashSet<string>(
                previous.Devices.Select(d => d.Id),
                StringComparer.OrdinalIgnoreCase);
            var currentIDs = new HashSet<string>(
                current.Devices.Select(d => d.Id),
                StringComparer.OrdinalIgnoreCase);
            bool canInferAbsence =
                previous.Status == LANSnapshotStatus.Complete &&
                current.Status == LANSnapshotStatus.Complete;

            var newDevices = previous.Status == LANSnapshotStatus.Complete
                ? current.Devices.Where(d => !previousIDs.Contains(d.Id)).ToList()
                : new List<LANDevice>();
            var missingDevices = canInferAbsence
                ? previous.Devices.Where(d => !currentIDs.Contains(d.Id)).ToList()
                : new List<LANDevice>();

            var changedAddresses = new List<string>();
            var changedInterfaces = new List<string>();
            if (canInferAbsence)
            {
                var previousByMac = previous.Devices
                    .Select(d => (Device: d, Mac: WindowsArpParser.NormalizeMac(d.MacAddress)))
                    .Where(item =>
                        item.Device.State != ARPEntryState.Incomplete &&
                        item.Mac.Length > 0)
                    .GroupBy(item => item.Mac, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Device).ToList(),
                        StringComparer.OrdinalIgnoreCase);
                var currentByMac = current.Devices
                    .Select(d => (Device: d, Mac: WindowsArpParser.NormalizeMac(d.MacAddress)))
                    .Where(item =>
                        item.Device.State != ARPEntryState.Incomplete &&
                        item.Mac.Length > 0)
                    .GroupBy(item => item.Mac, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(item => item.Device).ToList(),
                        StringComparer.OrdinalIgnoreCase);

                foreach (string macAddress in currentByMac.Keys.OrderBy(x => x, StringComparer.Ordinal))
                {
                    if (!previousByMac.TryGetValue(macAddress, out List<LANDevice>? oldBindings)) continue;
                    List<LANDevice> newBindings = currentByMac[macAddress];
                    if (oldBindings.Count != 1 || newBindings.Count != 1) continue;

                    LANDevice old = oldBindings[0];
                    LANDevice device = newBindings[0];
                    if (!string.Equals(old.IPAddress, device.IPAddress, StringComparison.OrdinalIgnoreCase))
                        changedAddresses.Add($"{device.MacAddress} moved {old.IPAddress} -> {device.IPAddress}");
                    if (!string.Equals(old.Interface, device.Interface, StringComparison.Ordinal))
                        changedInterfaces.Add($"{device.MacAddress} moved {old.Interface} -> {device.Interface}");
                }
            }

            return new LANDiff(
                newDevices,
                missingDevices,
                changedAddresses,
                changedInterfaces,
                DuplicateBindings(current.Devices));
        }

        private static IReadOnlyList<DuplicateMacBinding> DuplicateBindings(
            IReadOnlyList<LANDevice> devices)
        {
            return devices
                .Select(device => (
                    Device: device,
                    Mac: WindowsArpParser.NormalizeMac(device.MacAddress)))
                .Where(item =>
                    item.Device.State != ARPEntryState.Incomplete &&
                    item.Mac.Length > 0)
                .GroupBy(item => item.Mac, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DuplicateMacBinding(
                    macAddress: group.Key.ToLowerInvariant(),
                    ipAddresses: group.Select(item => item.Device.IPAddress)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(address => address, Comparer<string>.Create(CompareAddresses))
                        .ToList(),
                    interfaces: group.Select(item => item.Device.Interface)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(iface => iface, StringComparer.Ordinal)
                        .ToList()))
                .Where(binding => binding.IPAddresses.Count > 1)
                .OrderBy(binding => binding.MacAddress, StringComparer.Ordinal)
                .ToList();
        }

        private static int CompareAddresses(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            if (IPAddress.TryParse(left, out IPAddress? leftAddress) &&
                IPAddress.TryParse(right, out IPAddress? rightAddress) &&
                leftAddress.AddressFamily == AddressFamily.InterNetwork &&
                rightAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] leftBytes = leftAddress.GetAddressBytes();
                byte[] rightBytes = rightAddress.GetAddressBytes();
                for (int index = 0; index < leftBytes.Length; index++)
                {
                    int comparison = leftBytes[index].CompareTo(rightBytes[index]);
                    if (comparison != 0) return comparison;
                }
                return 0;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }
    }
}
