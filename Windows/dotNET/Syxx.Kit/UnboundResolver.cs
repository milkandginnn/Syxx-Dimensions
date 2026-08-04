using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// Local validating resolver ("be your own root of trust") — the non-gated, manual analog of the
/// entitlement-gated always-on toggle in DESIGN.md layers 3–4, and the Windows/Linux twin of macOS
/// `UnboundResolver`/`UnboundManager`. Syxx runs a local, recursive, DNSSEC-validating, caching
/// **Unbound** instance on `127.0.0.1` and — with an explicit user action — points the system resolver
/// at it via `Set-DnsClientServerAddress` (Windows) / `resolvectl` (Linux). No special OS entitlement.
///
/// HONESTY SPINE (a Syxx feature, so absolute): never report "Unbound is protecting your DNS" because a
/// process was spawned or a config written. `ClassifyEnable` yields Enabled ONLY when the local resolver
/// actually answered a real query AND the system was switched to it AND a post-switch sanity lookup
/// succeeded. Anything short is a specific honest state — NotInstalled / ElevationRequired / Failed /
/// Inconclusive — never a reassuring green.
///
/// PLATFORM NOTE: unlike macOS (where mDNSResponder permanently owns 127.0.0.1:53, forcing an alternate
/// port + pf redirect), Windows and Linux have no protected daemon squatting :53, so Unbound binds :53
/// directly here — the macOS pf-redirect mechanism is deliberately NOT mirrored.
///
/// TODO(dev-ID): the always-on auto-toggle + in-flow failsafe (DESIGN.md layer 4) is a separate,
/// macOS-first NetworkExtension concern; the Windows equivalent (tray + WFP/service) is a later scope.
/// This manual path is the entitlement-free version that ships now.
public enum UnboundResultKind { Enabled, Disabled, NotInstalled, ElevationRequired, Failed, Inconclusive }

/// Tri-state result — never a bare bool. A switch that "returned" but is unconfirmed is not success.
public sealed record UnboundResult(
    UnboundResultKind Kind,
    ushort Port = 53,
    IReadOnlyList<string>? PriorServers = null,
    string? Message = null)
{
    public bool IsEnabled => Kind == UnboundResultKind.Enabled;
}

/// Options for the generated unbound.conf. A local recursive, DNSSEC-validating, caching resolver.
public sealed record UnboundConfig(
    string RootAnchorPath,
    string WorkingDir,
    string PidFile,
    string LogFile,
    string ListenAddress = "127.0.0.1",
    ushort Port = 53,
    // null = full recursion (v1 default). When set, forward-over-DoT to this resolver instead — the
    // DESIGN.md:326-328 "forward-over-DoT to a resolver profile" end of the spectrum. v1 ships null.
    EncryptedResolver? ForwardOverDoT = null);

/// The seam the future entitlement-gated implementation slots into. The shell-level switch
/// (`WindowsSystemResolverSwitcher`) is the entitlement-free v1.
public interface ISystemResolverSwitching
{
    /// Returns the current DNS server list. Null means "could not read" (no safe restore point);
    /// empty means "genuinely no DNS servers configured" (D099).
    IReadOnlyList<string>? Snapshot();
    (bool ok, bool elevationDenied, string? reason) SetServers(IReadOnlyList<string> servers);
    (bool ok, bool elevationDenied, string? reason) Restore(IReadOnlyList<string> servers);
}

public static class UnboundResolver
{
    public const string InstallHint =
        "Unbound is not installed. Install it (e.g. `choco install unbound` on Windows, `apt install unbound` on Linux).";

    // MARK: - Config generation (pure, testable — twin of the Swift generateConfig)

    public static string GenerateConfig(UnboundConfig c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Syxx-generated local validating resolver — do not hand-edit; regenerated on enable.");
        sb.AppendLine("# v1: full recursion + DNSSEC validation + caching. Forwarding is a later enhancement.");
        sb.AppendLine("server:");
        sb.AppendLine("    verbosity: 1");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    interface: {c.ListenAddress}@{c.Port}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    port: {c.Port}");
        sb.AppendLine("    do-ip4: yes");
        sb.AppendLine("    do-ip6: yes");
        sb.AppendLine("    do-udp: yes");
        sb.AppendLine("    do-tcp: yes");
        // Only loopback may query this resolver — never an open resolver on the network.
        sb.AppendLine("    access-control: 127.0.0.0/8 allow");
        sb.AppendLine("    access-control: ::1 allow");
        sb.AppendLine("    access-control: 0.0.0.0/0 refuse");
        sb.AppendLine("    access-control: ::/0 refuse");
        sb.AppendLine("    hide-identity: yes");
        sb.AppendLine("    hide-version: yes");
        sb.AppendLine("    harden-glue: yes");
        sb.AppendLine("    harden-dnssec-stripped: yes");
        sb.AppendLine("    harden-referral-path: yes");
        sb.AppendLine("    qname-minimisation: yes");
        sb.AppendLine("    prefetch: yes");
        sb.AppendLine("    cache-min-ttl: 0");
        // DNSSEC validation: this line makes it a *trust anchor*, not just a cache.
        sb.AppendLine(CultureInfo.InvariantCulture, $"    auto-trust-anchor-file: \"{c.RootAnchorPath}\"");
        // Run in the launching context's privileges; do not drop to a (possibly nonexistent) 'unbound'
        // user or chroot (keeps the trust-anchor path valid). Parity with the macOS config.
        sb.AppendLine("    username: \"\"");
        sb.AppendLine("    chroot: \"\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    directory: \"{c.WorkingDir}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    pidfile: \"{c.PidFile}\"");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    logfile: \"{c.LogFile}\"");
        if (c.ForwardOverDoT is { } fwd)
        {
            sb.AppendLine("forward-zone:");
            sb.AppendLine("    name: \".\"");
            sb.AppendLine("    forward-tls-upstream: yes");
            foreach (var ip in fwd.Addresses)
                sb.AppendLine(CultureInfo.InvariantCulture, $"    forward-addr: {ip}@853#{fwd.DotHost}");
        }
        return sb.ToString();
    }

    // MARK: - Honesty gate (pure, exhaustive — twin of the Swift classifyEnable)

    public static UnboundResult ClassifyEnable(
        ushort port,
        IReadOnlyList<string> priorServers,
        bool binaryFound,
        bool processStarted,
        bool bindElevationDenied,
        bool localResolves,
        bool systemSwitched,
        bool switchElevationDenied,
        bool postSwitchSanityOk,
        bool autoReverted,
        string? failureReason = null)
    {
        if (!binaryFound)
            return new UnboundResult(UnboundResultKind.NotInstalled, port, priorServers, InstallHint);
        if (!processStarted)
        {
            if (bindElevationDenied)
                return new UnboundResult(UnboundResultKind.ElevationRequired, port, priorServers,
                    $"binding the local resolver to port {port} requires administrator rights");
            return new UnboundResult(UnboundResultKind.Failed, port, priorServers,
                failureReason ?? "the local resolver process failed to start");
        }
        // Launched != resolving.
        if (!localResolves)
            return new UnboundResult(UnboundResultKind.Inconclusive, port, priorServers,
                "the local resolver started but did not answer a test query yet");
        if (!systemSwitched)
        {
            if (switchElevationDenied)
                return new UnboundResult(UnboundResultKind.ElevationRequired, port, priorServers,
                    failureReason ?? "changing the system DNS server requires administrator rights");
            return new UnboundResult(UnboundResultKind.Inconclusive, port, priorServers,
                "the local resolver is running but the system DNS was not switched to it");
        }
        // Switched != working: the post-switch sanity lookup is the failsafe gate.
        if (!postSwitchSanityOk)
        {
            var reason = autoReverted
                ? (failureReason ?? "the system could not resolve through the local resolver after switching; the original DNS servers were restored")
                : (failureReason ?? "the system could not resolve through the local resolver after switching");
            return new UnboundResult(UnboundResultKind.Failed, port, priorServers, reason);
        }
        return new UnboundResult(UnboundResultKind.Enabled, port, priorServers, null);
    }

    // MARK: - Binary location (null = not installed)

    /// DECISION (owner-review): Syxx REQUIRES a pre-installed Unbound (Chocolatey on Windows, apt on
    /// Linux) rather than vendoring a binary — same rationale as macOS: no vendoring precedent, and a
    /// bundled resolver would need its own signing + patch cadence. `NotInstalled` is surfaced honestly.
    public static string? LocateBinary() => Locate(OperatingSystem.IsWindows() ? "unbound.exe" : "unbound");
    public static string? LocateHelper(string name) =>
        Locate(OperatingSystem.IsWindows() ? name + ".exe" : name);

    private static string? Locate(string name)
    {
        IEnumerable<string> candidates;
        if (OperatingSystem.IsWindows())
        {
            candidates = new[]
            {
                @"C:\Program Files\Unbound\" + name,
                @"C:\Program Files (x86)\Unbound\" + name,
                @"C:\ProgramData\chocolatey\bin\" + name,
            };
        }
        else
        {
            candidates = new[]
            {
                "/usr/sbin/" + name, "/usr/bin/" + name, "/usr/local/sbin/" + name, "/opt/homebrew/sbin/" + name,
            };
        }
        foreach (var path in candidates)
            if (File.Exists(path)) return path;

        // Fall back to PATH.
        var finder = OperatingSystem.IsWindows() ? "where" : "which";
        try
        {
            var psi = new ProcessStartInfo(finder, name)
            { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                var first = outp.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
                if (!string.IsNullOrEmpty(first) && File.Exists(first)) return first;
            }
        }
        catch { /* finder missing — fall through */ }
        return null;
    }
}
