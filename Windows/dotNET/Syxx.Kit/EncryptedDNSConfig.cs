using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

/// <summary>
/// Encrypt-all-DNS: the portable, testable core (twin of macOS SyxxKit/EncryptedDNSConfig.swift).
/// Syxx configures the SYSTEM to send every DNS query over DoH/DoT using each OS's programmatic API
/// — no downloaded profile, no local proxy. Windows uses native DoH (netsh/registry templates);
/// Linux uses systemd-resolved DoT. The OS glue lives in the platform configurators; this holds the
/// resolver catalog and the honesty-critical apply classification.
///
/// HONESTY SPINE: the app must NEVER say "your DNS is encrypted" because a configuration command
/// returned 0. Installed is not in-effect. <see cref="ClassifyApply"/> yields Enabled only when the
/// apply succeeded AND the system reports it active AND a real encrypted query completed.
/// </summary>
public sealed record EncryptedResolver(
    string Id, string DisplayName, string DohUrl, string DotHost, IReadOnlyList<string> Addresses);

public enum EncryptedDNSTransport { Doh, Dot }

public enum EncryptedDNSApplyKind { Enabled, ElevationRequired, Failed, Inconclusive }

/// Tri-state (never a bool): a save that "worked" but is unconfirmed is not success.
public sealed record EncryptedDNSApplyResult(
    EncryptedDNSApplyKind Kind, string? ResolverId = null,
    EncryptedDNSTransport Transport = EncryptedDNSTransport.Doh, string? Reason = null)
{
    public bool IsEnabled => Kind == EncryptedDNSApplyKind.Enabled;
}

public static class EncryptedDNSConfig
{
    /// Curated public no-log resolvers with published DoH+DoT endpoints. Default is Cloudflare for
    /// compatibility/latency; the user may pick another or supply a custom one. Syxx does not
    /// silently pin one — choosing whom to trust is the user's call.
    public static readonly IReadOnlyList<EncryptedResolver> Catalog = new[]
    {
        new EncryptedResolver("cloudflare", "Cloudflare (1.1.1.1)",
            "https://cloudflare-dns.com/dns-query", "one.one.one.one",
            new[] { "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001" }),
        new EncryptedResolver("quad9", "Quad9 (9.9.9.9)",
            "https://dns.quad9.net/dns-query", "dns.quad9.net",
            new[] { "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9" }),
        new EncryptedResolver("google", "Google (8.8.8.8)",
            "https://dns.google/dns-query", "dns.google",
            new[] { "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844" }),
    };

    public const string DefaultResolverId = "cloudflare";

    public static EncryptedResolver? Resolver(string id) => Catalog.FirstOrDefault(r => r.Id == id);

    /// The honesty gate. Only all-three-true is Enabled; anything short is Inconclusive, never green.
    public static EncryptedDNSApplyResult ClassifyApply(
        string resolverId, EncryptedDNSTransport transport,
        bool applySucceeded, bool elevationRejected,
        bool systemReportsActive, bool encryptedProbeCompleted, string? failureReason = null)
    {
        if (elevationRejected)
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.ElevationRequired, resolverId, transport);
        if (!applySucceeded)
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, resolverId, transport,
                failureReason ?? "the system rejected the configuration");
        if (systemReportsActive && encryptedProbeCompleted)
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Enabled, resolverId, transport);
        if (!systemReportsActive)
            return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Inconclusive, resolverId, transport,
                "the configuration applied but the system does not report it active yet");
        return new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Inconclusive, resolverId, transport,
            "the configuration is active but no encrypted query has completed to confirm it");
    }
}
