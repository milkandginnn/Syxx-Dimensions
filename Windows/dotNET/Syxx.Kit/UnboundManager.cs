using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// Windows/Linux glue for <see cref="UnboundResolver"/>: process lifecycle + the shell-level
/// system-resolver switch. Twin of macOS `UnboundManager`. Kept separate from the pure
/// <see cref="UnboundResolver"/> core (config generation / honesty gate) so that logic stays testable
/// without spawning a process or touching system DNS.
///
/// SERVICE-MODEL DECISION (owner-review): the local resolver runs as an APP-MANAGED background child
/// process tied to the explicit Enable/Disable toggle — NOT an installed Windows Service. Rationale: for
/// a manual toggle, quitting/disabling must restore the user's DNS (the fail-safe direction); a
/// persistent always-on service is the entitlement/tray layer-4 concern. A Windows Service is the natural
/// later enhancement for the always-on form and is deliberately not built here.
///
/// UNVERIFIED-ON-THIS-HOST: this code was authored on macOS with no Windows host available, so the
/// `Set-DnsClientServerAddress` switch and the :53 bind are NOT runtime-verified. The pure core
/// (config generation + honesty gate) IS unit-tested; the platform glue mirrors the verified macOS flow.

/// Windows system-resolver switch via PowerShell `*-DnsClientServerAddress` (ROADMAP.md C4 / WS6-04).
/// Reads need no admin; setting the servers does, and a refusal is surfaced honestly as elevationDenied.
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemResolverSwitcher : ISystemResolverSwitching
{
    /// The InterfaceIndex of the adapter carrying the default IPv4 route, or null.
    private int? PrimaryInterfaceIndex()
    {
        var (outp, code) = Run("powershell", "-NoProfile -Command \"(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Sort-Object RouteMetric | Select-Object -First 1).InterfaceIndex\"");
        if (code != 0) return null;
        return int.TryParse(outp.Trim(), out var idx) ? idx : (int?)null;
    }

    public IReadOnlyList<string>? Snapshot()
    {
        var idx = PrimaryInterfaceIndex();
        if (idx is null) return null;   // D099: "could not determine interface" ≠ "no servers"
        var (outp, code) = Run("powershell",
            $"-NoProfile -Command \"(Get-DnsClientServerAddress -InterfaceIndex {idx} -AddressFamily IPv4 -ErrorAction SilentlyContinue).ServerAddresses -join ','\"");
        if (code != 0) return null;     // D099: PowerShell failure ≠ "no servers"
        if (string.IsNullOrWhiteSpace(outp)) return Array.Empty<string>(); // genuinely no servers
        return outp.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public (bool ok, bool elevationDenied, string? reason) SetServers(IReadOnlyList<string> servers)
    {
        var idx = PrimaryInterfaceIndex();
        if (idx is null) return (false, false, "could not determine the primary network interface");

        // D111: validate every server address before interpolation — a crafted string could
        // terminate the PowerShell -Command quote and execute arbitrary commands under the
        // process's (potentially elevated) token.
        foreach (var server in servers)
        {
            if (!IPAddress.TryParse(server, out _))
                return (false, false, $"refusing to set invalid DNS server address: {server}");
        }

        string cmd = servers.Count == 0
            ? $"Set-DnsClientServerAddress -InterfaceIndex {idx} -ResetServerAddresses"
            : $"Set-DnsClientServerAddress -InterfaceIndex {idx} -ServerAddresses {string.Join(",", servers)}";
        var (outp, code) = Run("powershell", $"-NoProfile -Command \"{cmd}\"");
        if (code == 0) return (true, false, null);
        return (false, IsElevationError(code, outp), string.IsNullOrWhiteSpace(outp) ? $"powershell exited {code}" : outp.Trim());
    }

    public (bool ok, bool elevationDenied, string? reason) Restore(IReadOnlyList<string> servers) => SetServers(servers);

    /// D110: elevation detection via exit code and pre-check, not localized English substrings.
    /// PowerShell exit code 5 = ERROR_ACCESS_DENIED; exit code 1223 = ERROR_CANCELLED (UAC declined).
    /// The broad "administrator"/"permission" substring matches falsely tripped on unrelated output.
    private static bool IsElevationError(int code, string text)
    {
        if (code == 5 || code == 1223) return true;  // Win32 ERROR_ACCESS_DENIED / ERROR_CANCELLED
        // Fall back to narrow, well-known English-only markers from PowerShell cmdlets that
        // don't set a specific exit code for elevation denial.
        return text.Contains("Operation requires privileges", StringComparison.OrdinalIgnoreCase)
            || text.Contains("requires elevation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsElevationError(string text) => IsElevationError(-1, text);

    /// D102: resolve system tools to absolute paths from SystemDirectory, so a PATH-hijacked
    /// powershell.exe cannot be executed with this process's (potentially elevated) token.
    private static string SystemToolPath(string tool)
    {
        if (OperatingSystem.IsWindows() && (tool.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || tool.Equals("netsh", StringComparison.OrdinalIgnoreCase)))
        {
            var sysDir = Environment.SystemDirectory;
            if (tool.Equals("powershell", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(sysDir, "WindowsPowerShell", "v1.0", "powershell.exe");
            return Path.Combine(sysDir, "netsh.exe");
        }
        return tool;
    }

    private static (string outp, int code) Run(string file, string args)
    {
        try
        {
            var resolvedPath = SystemToolPath(file);
            var psi = new ProcessStartInfo(resolvedPath, args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is null) return ("could not start " + resolvedPath, -1);
            // D104: drain stdout and stderr concurrently — reading them sequentially deadlocks
            // when the child fills one pipe while we block on the other.
            var outpTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            Task.WhenAll(outpTask, errTask).Wait(TimeSpan.FromSeconds(30));
            if (!p.WaitForExit(5000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return ("process timed out", -1);
            }
            var outp = outpTask.IsCompleted ? outpTask.Result : "";
            var err = errTask.IsCompleted ? errTask.Result : "";
            return (string.IsNullOrWhiteSpace(outp) ? err : outp, p.ExitCode);
        }
        catch (Exception ex) { return (ex.Message, -1); }
    }
}

/// Owns the running Unbound child process and the pre-switch resolver snapshot. Clean integration point
/// for the Dashboard button / view-model: hold one <see cref="UnboundManager"/>, call Enable/Disable.
public sealed class UnboundManager
{
    private readonly ISystemResolverSwitching _switcher;
    private readonly object _lock = new();
    private Process? _process;
    // D099: null means "no safe restore point was captured" — Disable() must not silently reset.
    private IReadOnlyList<string>? _snapshot;

    public string StateDir { get; }

    public UnboundManager(ISystemResolverSwitching? switcher = null, string? stateDir = null)
    {
        _switcher = switcher ?? (OperatingSystem.IsWindows()
            ? new WindowsSystemResolverSwitcher()
            : new NullResolverSwitcher());
        StateDir = stateDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syxx", "unbound");
    }

    public bool IsRunning { get { lock (_lock) { return _process is { HasExited: false }; } } }

    // MARK: - Enable (Windows/Linux bind :53 directly; no macOS-style pf redirect)

    public async Task<UnboundResult> EnableAsync(ushort port = 53, string sanityDomain = "example.com")
    {
        var unbound = UnboundResolver.LocateBinary();
        if (unbound is null)
            return new UnboundResult(UnboundResultKind.NotInstalled, port, Array.Empty<string>(), UnboundResolver.InstallHint);

        var start = await StartProcessAsync(unbound, port);
        if (!start.started)
            return UnboundResolver.ClassifyEnable(port, Array.Empty<string>(), true, false,
                start.bindElevationDenied, false, false, false, false, false, start.reason);

        var localOk = await LocalResolvesAsync(port, sanityDomain);
        if (!localOk)
        {
            StopProcess();
            return UnboundResolver.ClassifyEnable(port, Array.Empty<string>(), true, true, false, false, false, false, false, false);
        }

        var prior = _switcher.Snapshot();
        lock (_lock) { _snapshot = prior; }

        // D099: refuse to switch DNS when we have no safe restore point — an empty snapshot
        // from a failed read would silently reset the user's static DNS to DHCP on disable.
        if (prior is null)
        {
            StopProcess();
            return new UnboundResult(UnboundResultKind.Failed, port, Array.Empty<string>(),
                "could not read the current DNS servers — refusing to switch without a safe restore point");
        }

        var sw = _switcher.SetServers(new[] { "127.0.0.1" });
        if (!sw.ok)
        {
            StopProcess();
            return UnboundResolver.ClassifyEnable(port, prior, true, true, false, true, false, sw.elevationDenied, false, false, sw.reason);
        }

        // Post-switch sanity through the loopback resolver; auto-revert on failure.
        var postOk = await LocalResolvesAsync(port, sanityDomain);
        if (!postOk)
        {
            _switcher.Restore(prior);
            StopProcess();
            return UnboundResolver.ClassifyEnable(port, prior, true, true, false, true, true, false, false, true);
        }
        return UnboundResolver.ClassifyEnable(port, prior, true, true, false, true, true, false, true, false);
    }

    // MARK: - Disable (failsafe: always restore the exact snapshot)

    public UnboundResult Disable()
    {
        IReadOnlyList<string>? prior;
        lock (_lock) { prior = _snapshot; }
        StopProcess();

        // D099: a null snapshot means the original DNS servers were never readable — restoring
        // them would silently reset static DNS to DHCP. Report failure, never a silent reset.
        if (prior is null)
        {
            return new UnboundResult(UnboundResultKind.Failed, 53, Array.Empty<string>(),
                "could not restore DNS — original servers were never readable (no safe restore point)");
        }

        var restore = _switcher.Restore(prior);
        if (!restore.ok)
        {
            if (restore.elevationDenied)
                return new UnboundResult(UnboundResultKind.ElevationRequired, 53, prior,
                    "restoring the original DNS servers requires administrator rights");
            return new UnboundResult(UnboundResultKind.Failed, 53, prior,
                restore.reason ?? "could not restore the original DNS servers");
        }
        return new UnboundResult(UnboundResultKind.Disabled, 53, prior, null);
    }

    // MARK: - Unprivileged verification (no system switch)

    /// Starts Unbound on a high port, confirms recursion + DNSSEC, stops. Reproducible proof; does NOT
    /// touch system DNS. (Runtime-unverified on this macOS host for the Windows target.)
    public async Task<(bool ok, bool dnssecValidated, string detail)> VerifyAsync(ushort port = 5354)
    {
        var unbound = UnboundResolver.LocateBinary();
        if (unbound is null) return (false, false, UnboundResolver.InstallHint);
        var start = await StartProcessAsync(unbound, port);
        if (!start.started) return (false, false, start.reason ?? "the local resolver process failed to start");
        try
        {
            await LocalResolvesAsync(port, "cloudflare.com"); // warm up
            var pos = await QueryLocalAsync(port, "cloudflare.com", dnssec: true);
            for (int i = 0; i < 8 && (pos.DidFail || pos.Records.Count == 0 || !pos.AuthenticatedData); i++)
            {
                await Task.Delay(700);
                pos = await QueryLocalAsync(port, "cloudflare.com", dnssec: true);
            }
            bool recurses = !pos.DidFail && pos.Records.Count > 0;
            bool adBit = pos.AuthenticatedData;
            var bogus = await QueryLocalAsync(port, "dnssec-failed.org", dnssec: true);
            bool rejectedBogus = bogus.Status == DNSResponseStatus.ServFail || bogus.DidFail;
            bool dnssecValidated = adBit && rejectedBogus;
            var detail = $"recursion: {(recurses ? "ok" : "FAILED")}; DNSSEC AD bit on signed answer: {(adBit ? "yes" : "no")}; bogus zone rejected: {(rejectedBogus ? "yes (SERVFAIL)" : "NO — validation not proven")}";
            return (recurses && dnssecValidated, dnssecValidated, detail);
        }
        finally { StopProcess(); }
    }

    // MARK: - Process lifecycle

    private async Task<(bool started, bool bindElevationDenied, string? reason)> StartProcessAsync(string unbound, ushort port)
    {
        try { Directory.CreateDirectory(StateDir); }
        catch (Exception ex) { return (false, false, "could not create state directory: " + ex.Message); }

        var anchorPath = Path.Combine(StateDir, "root.key");
        var confPath = Path.Combine(StateDir, "unbound.conf");
        var pidPath = Path.Combine(StateDir, "unbound.pid");
        var logPath = Path.Combine(StateDir, "unbound.log");

        var anchorTool = UnboundResolver.LocateHelper("unbound-anchor");
        if (anchorTool is not null)
        {
            try
            {
                var psi = new ProcessStartInfo(anchorTool, $"-a \"{anchorPath}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using var a = Process.Start(psi);
                a?.WaitForExit(8000);
            }
            catch { /* unbound-anchor exits 1 on first fetch; the key file presence is what matters */ }
        }
        if (!File.Exists(anchorPath))
            return (false, false, "could not obtain the DNSSEC root trust anchor (unbound-anchor)");

        var cfg = new UnboundConfig(anchorPath, StateDir, pidPath, logPath, "127.0.0.1", port);
        try { File.WriteAllText(confPath, UnboundResolver.GenerateConfig(cfg)); }
        catch (Exception ex) { return (false, false, "could not write unbound.conf: " + ex.Message); }

        Process p;
        var startInfo = new ProcessStartInfo(unbound, $"-d -c \"{confPath}\"")
        { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        // D104: continuously drain stderr via async event — a healthy unbound logging to stderr
        // would otherwise fill the pipe buffer and block permanently.
        var earlyErrors = new System.Text.StringBuilder();
        try
        {
            var started = Process.Start(startInfo);
            if (started is null) return (false, false, "could not launch unbound");
            p = started;
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) earlyErrors.AppendLine(e.Data); };
            p.BeginErrorReadLine();
        }
        catch (Exception ex) { return (false, false, "could not launch unbound: " + ex.Message); }
        lock (_lock) { _process = p; }

        await Task.Delay(400);
        if (p.HasExited)
        {
            // Give the async reader a moment to flush remaining lines.
            await Task.Delay(100);
            var err = earlyErrors.ToString();
            var low = err.ToLowerInvariant();
            // "address already in use" on the loopback port is a port conflict, NOT elevation.
            bool bindDenied = low.Contains("permission denied", StringComparison.Ordinal) || low.Contains("access is denied", StringComparison.Ordinal);
            lock (_lock) { _process = null; }
            return (false, bindDenied, string.IsNullOrWhiteSpace(err) ? "unbound exited immediately" : err.Trim());
        }
        return (true, false, null);
    }

    private void StopProcess()
    {
        Process? p;
        lock (_lock) { p = _process; _process = null; }
        if (p is null || p.HasExited) return;
        try { p.Kill(entireProcessTree: true); p.WaitForExit(1500); } catch { /* already gone */ }
    }

    // MARK: - Sanity lookups

    private async Task<bool> LocalResolvesAsync(ushort port, string domain)
    {
        for (int i = 0; i < 6; i++)
        {
            var r = await QueryLocalAsync(port, domain, dnssec: false);
            if (!r.DidFail && r.Records.Count > 0) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static async Task<DNSResponse> QueryLocalAsync(ushort port, string domain, bool dnssec)
    {
        // The DO bit is only sent when an EDNS0 OPT record is present (requires a UDP payload size);
        // without it the resolver will not set the AD bit — so DNSSEC queries must advertise one.
        var q = new DNSQuery(domain, type: 1, server: "127.0.0.1", port: port,
            edns0UDPSize: dnssec ? (ushort?)1232 : null, edns0DODNSSEC: dnssec);
        return await DNSResolver.QueryUDPDetailedAsync(q);
    }
}

/// Non-Windows fallback switcher (e.g. this macOS build host, or Linux before a resolvectl twin lands).
/// Honestly refuses rather than pretending to switch — never a fake success.
public sealed class NullResolverSwitcher : ISystemResolverSwitching
{
    public IReadOnlyList<string>? Snapshot() => Array.Empty<string>();
    public (bool ok, bool elevationDenied, string? reason) SetServers(IReadOnlyList<string> servers) =>
        (false, false, "system DNS switching is not implemented for this OS in Syxx.Kit yet");
    // E082: Restore must return failure, not fake success. The old (true, false, null) caused
    // the CLI to print "DISABLED — restored: (DHCP-provided)" when nothing was ever touched.
    public (bool ok, bool elevationDenied, string? reason) Restore(IReadOnlyList<string> servers) =>
        (false, false, "system DNS switching is not implemented for this OS in Syxx.Kit yet");
}
