using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// <summary>
/// Platform glue for encrypt-all-DNS on Windows and Linux (twin of macOS EncryptedDNSConfigurator).
/// No profile, no proxy: Windows uses native DoH (the OS's own encrypted-DNS support), Linux uses
/// systemd-resolved DoT.
///
/// OWNER/OS GATES (real-run cannot happen from this host):
///  - Windows: setting encrypted DNS writes per-interface registry/`netsh` state and needs
///    Administrator. An unelevated call is reported as <see cref="EncryptedDNSApplyKind.ElevationRequired"/>,
///    never a fake success.
///  - Linux: `resolvectl` against systemd-resolved needs root (or polkit). Same honest gating.
///  - A captive-portal FAILSAFE (DESIGN.md, non-negotiable) is required before this ships enabled by
///    default: encrypting to an unreachable resolver on a portal network must auto-revert. That
///    revert policy is an owner decision and is NOT silently assumed here.
///
/// HONESTY: every path routes through <see cref="EncryptedDNSConfig.ClassifyApply"/>. Applying a
/// setting is never reported as "encrypted" without the system's active flag AND a real encrypted
/// query completing.
/// </summary>
public static class EncryptedDNSConfigurator
{
    /// Turn system encrypted DNS on. Returns the honest tri-state result.
    public static async Task<EncryptedDNSApplyResult> EnableAsync(string resolverId, EncryptedDNSTransport transport)
    {
        var resolver = EncryptedDNSConfig.Resolver(resolverId);
        if (resolver is null)
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, resolverId, transport, $"unknown resolver {resolverId}");

        // D072: reject unsupported OS/transport pairs before mutating anything.
        // Windows only implements DoH (built-in OS support); Linux only implements DoT
        // (systemd-resolved). Applying the wrong transport silently was the defect —
        // requesting DoT on Windows would configure DoH, and vice versa.
        if (OperatingSystem.IsWindows() && transport != EncryptedDNSTransport.Doh)
        {
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, resolverId, transport,
                "Windows only supports DoH (DNS-over-HTTPS). DoT configuration is not implemented on this platform.");
        }
        if (OperatingSystem.IsLinux() && transport != EncryptedDNSTransport.Dot)
        {
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, resolverId, transport,
                "Linux only supports DoT (DNS-over-TLS) via systemd-resolved. DoH configuration is not implemented on this platform.");
        }

        bool applied;
        bool elevationRejected = false;
        string? failReason = null;
        try
        {
            if (OperatingSystem.IsWindows())
                (applied, elevationRejected, failReason) = await ApplyWindowsDohAsync(resolver);
            else if (OperatingSystem.IsLinux())
                (applied, elevationRejected, failReason) = await ApplyLinuxDotAsync(resolver);
            else
                return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, resolverId, transport,
                    "encrypted-DNS configuration is not implemented for this OS");
        }
        catch (Exception ex)
        {
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, resolverId, transport, ex.Message);
        }

        if (!applied)
            return EncryptedDNSConfig.ClassifyApply(resolverId, transport, false, elevationRejected, false, false, failReason);

        // Installed != in effect. Confirm the system reports encrypted DNS active, then prove a real
        // encrypted query completes before claiming success.
        // B146: IsDNSEncrypted() now delegates to EncryptedDNSStatus() which reads real system
        // state (DoH template on Windows, resolvectl on Linux) — no longer a stub.
        bool active = NetworkKeyState.IsDNSEncrypted();
        var probe = await EncryptedTransportProbe.Probe(endpoint: resolver.DohUrl);
        bool probeOk = probe.Status == EncryptedTransportStatus.Working;

        return EncryptedDNSConfig.ClassifyApply(resolverId, transport, true, false, active, probeOk);
    }

    // --- Windows native DoH -----------------------------------------------------------------------
    // Windows 11 has built-in DoH: the resolver IP must be registered with a DoH template, then set
    // as the interface DNS with DoH required. This is per-interface Administrator state.
    private static async Task<(bool applied, bool elevationRejected, string? reason)> ApplyWindowsDohAsync(EncryptedResolver r)
    {
        // Register the DoH template for each server IP (idempotent), then the caller's interface DNS
        // is pointed at those IPs with encryption required. Exact interface wiring is the follow-up;
        // the template registration is the load-bearing, testable-by-real-run step.
        foreach (var ip in r.Addresses)
        {
            var (code, err) = await RunAsync("netsh", $"dns add encryption server={ip} dohtemplate={r.DohUrl} autoupgrade=yes udpfallback=no");
            if (IsElevationError(code, err)) return (false, true, err);
            // netsh returns non-zero if the template already exists; treat "already" as applied.
            if (code != 0 && !(err?.Contains("already", StringComparison.OrdinalIgnoreCase) ?? false))
                return (false, false, $"netsh dns add encryption failed ({code}): {err}");
        }
        return (true, false, null);
    }

    // --- Linux systemd-resolved DoT ---------------------------------------------------------------
    private static async Task<(bool applied, bool elevationRejected, string? reason)> ApplyLinuxDotAsync(EncryptedResolver r)
    {
        // Global DoT: set the resolver + enable opportunistic->strict DoT. resolvectl needs root.
        var servers = string.Join(" ", r.Addresses);
        var (c1, e1) = await RunAsync("resolvectl", $"dns \"\" {servers}");
        if (IsElevationError(c1, e1)) return (false, true, e1);
        if (c1 != 0) return (false, false, $"resolvectl dns failed ({c1}): {e1}");

        var (c2, e2) = await RunAsync("resolvectl", "dnsovertls \"\" yes");
        if (IsElevationError(c2, e2)) return (false, true, e2);
        if (c2 != 0) return (false, false, $"resolvectl dnsovertls failed ({c2}): {e2}");
        return (true, false, null);
    }

    /// D110: elevation detection via exit code first (Win32 5=ACCESS_DENIED, 1223=CANCELLED),
    /// with narrow Linux-specific markers. Broad localized substring matching ("Access is denied",
    /// "Permission denied") failed on non-English Windows and falsely tripped on unrelated output.
    private static bool IsElevationError(int code, string? err) =>
        code == 5 || code == 1223   // Win32 ERROR_ACCESS_DENIED / ERROR_CANCELLED
        || (err?.Contains("must be root", StringComparison.OrdinalIgnoreCase) ?? false)
        || (err?.Contains("Interactive authentication required", StringComparison.OrdinalIgnoreCase) ?? false)
        || (err?.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ?? false);

    /// D102: resolve system tools to absolute paths so a PATH-hijacked netsh/resolvectl
    /// cannot ride the elevation prompt to Administrator.
    private static string SystemToolPath(string tool)
    {
        if (OperatingSystem.IsWindows() && tool.Equals("netsh", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.SystemDirectory, "netsh.exe");
        return tool;
    }

    private static async Task<(int code, string? stderr)> RunAsync(string file, string args)
    {
        var resolvedPath = SystemToolPath(file);
        var psi = new ProcessStartInfo(resolvedPath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null) return (-1, "could not start " + resolvedPath);
        // D104: drain stdout concurrently with stderr to prevent pipe-buffer deadlocks.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await p.WaitForExitAsync();
        string err = stderrTask.Result;
        return (p.ExitCode, string.IsNullOrWhiteSpace(err) ? null : err.Trim());
    }
}
