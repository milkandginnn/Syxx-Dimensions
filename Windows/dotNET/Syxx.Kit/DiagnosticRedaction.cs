using System.Text.RegularExpressions;

namespace Syxx.Kit;

/// <summary>
/// Strips user-identifying data from diagnostic text before it leaves the device.
///
/// <para>WHY IT LIVES IN THE KIT. Three .NET surfaces report diagnostics — the WinUI client, the
/// CLI, and the Avalonia client on Linux — and each needs identical redaction. Implemented per-sink
/// it would drift, and the drift would be invisible: the platform with the weaker regex leaks, and
/// nothing fails. One implementation, one test suite, three consumers. It also has to live here
/// because <c>SyxxUI</c> cannot be referenced from the test project (WinUI does not build on a
/// non-Windows host), so a redaction implemented there is a redaction nobody can test.</para>
///
/// <para>WHAT IS AT STAKE. This product's entire promise is that your DNS queries stay yours. A
/// queried domain reaching an issue tracker is not a bug report — it is the harm the product exists
/// to prevent, arriving disguised as ordinary telemetry, where nothing would flag it. Resolver IPs
/// identify an ISP or an employer. Home paths carry the account name.</para>
///
/// <para>DELIBERATELY OVER-MATCHING. The patterns are loose. Over-matching costs a redacted token
/// in a bug report; under-matching costs a privacy incident. That trade is not close, so the
/// patterns err toward stripping. Machine-readable diagnostic tokens (<c>probe_deadline</c>,
/// <c>attempt 2 of 2</c>, error-class names) contain no dots or colons and survive intact — which
/// is the property that keeps engineers using the sink instead of routing around it.</para>
/// </summary>
public static class DiagnosticRedaction
{
    private const RegexOptions Opts =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Ipv4 = new(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", Opts);
    private static readonly Regex Ipv6 = new(@"\b(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}\b", Opts);
    private static readonly Regex Domain =
        new(@"\b[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9-]{1,63})+\b", Opts);
    private static readonly Regex WindowsUserPath = new(@"[A-Za-z]:\\Users\\[^\\\s]+", Opts);
    private static readonly Regex UnixUserPath = new(@"/(?:home|Users)/[^/\s]+", Opts);
    // B224: license-key pattern — runs of 4+ hyphen-separated alphanumeric groups of length >= 4.
    // Keygen keys are grouped alphanumerics separated by hyphens; they contain no dots or colons,
    // so they match neither the Domain nor the IP patterns. Over-matching is deliberate (§over-matching).
    private static readonly Regex LicenseKey =
        new(@"\b(?:[A-Za-z0-9]{4,}-){3,}[A-Za-z0-9]{4,}\b", Opts);

    /// Redact in a fixed order. IPv4 first so dotted quads are not caught by the domain pattern;
    /// paths last so a home directory containing a dotted name is already partly handled.
    public static string Apply(string? s)
    {
        if (string.IsNullOrEmpty(s)) { return s ?? string.Empty; }
        var o = Ipv4.Replace(s, "<ip>");
        o = Ipv6.Replace(o, "<ip6>");
        o = Domain.Replace(o, "<domain>");
        o = LicenseKey.Replace(o, "<license-key>");
        o = WindowsUserPath.Replace(o, @"C:\Users\<user>");
        o = UnixUserPath.Replace(o, "/home/<user>");
        return o;
    }
}
