using System;

namespace Syxx.Kit;

/// <summary>
/// The gate every client must pass a SERVER-SUPPLIED url through before handing it to the OS opener
/// (`xdg-open` / `NSWorkspace.open` / `ShellExecute`).
///
/// Why this exists: the MOTD/startup response is remote content. An unguarded open means a
/// compromised or spoofed response can hand the desktop a `file://`, `smb://`, or attacker-chosen
/// destination and the OS will act on it. macOS `BroadcastPanelView.swift:39` currently opens the
/// broadcast URL with no scheme or host check (delta D045) — the Linux client does not inherit that,
/// and this shared primitive is what the other two clients should adopt to close it.
///
/// Policy, deliberately strict (allowlist, not denylist):
///  • HTTPS only. Plain http is refused: MOTD content is fetched over TLS or not at all.
///  • Host must be an allowlisted domain or one of its subdomains.
///  • Anything unparseable, credential-bearing (user:pass@), or non-default-port is refused.
///  • Refusal is a TRI-STATE result the caller must surface, never a silent no-op — a link that
///    does nothing reads as a broken app; a link that says why reads as a careful one.
/// </summary>
public static class RemoteUrlPolicy
{
    /// The product's own origins. Subdomains are permitted so `linmotd`/`macmotd`/`winmotd`.syxx.app
    /// resolve without a per-host entry, but an unrelated registrable domain never does.
    public static readonly string[] DefaultAllowedDomains = { "syxx.app", "milkandgin.com" };

    public enum Decision { Allowed, RefusedScheme, RefusedHost, RefusedMalformed }

    public sealed record Result(Decision Decision, string? Reason)
    {
        public bool IsAllowed => Decision == Decision.Allowed;
    }

    /// Pure core. No I/O, no opening — just the result, so it is testable and mutation-provable.
    public static Result Evaluate(string? url, string[]? allowedDomains = null)
    {
        allowedDomains ??= DefaultAllowedDomains;

        if (string.IsNullOrWhiteSpace(url))
            return new Result(Decision.RefusedMalformed, "The link was empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new Result(Decision.RefusedMalformed, "The link could not be read as a web address.");

        // HTTPS only — this is the load-bearing check. `file:`, `smb:`, `javascript:`, and plain
        // `http:` all land here.
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return new Result(Decision.RefusedScheme,
                $"Syxx only opens https links; this one was “{uri.Scheme}”.");

        // Embedded credentials are a phishing shape, never legitimate for our own content.
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return new Result(Decision.RefusedMalformed, "The link carried embedded credentials.");

        if (!uri.IsDefaultPort)
            return new Result(Decision.RefusedMalformed, "The link used a non-standard port.");

        string host = uri.Host;
        foreach (var domain in allowedDomains)
        {
            // Exact match, or a true subdomain (the leading dot prevents `evilsyxx.app` matching).
            if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return new Result(Decision.Allowed, null);
        }

        return new Result(Decision.RefusedHost, $"Syxx only opens its own pages; “{host}” is not one.");
    }

    /// <summary>
    /// Authorizes a URL for OS-level navigation or WebView load. Returns the absolute
    /// <see cref="Uri"/> only when the policy allows it; otherwise returns null and never
    /// a partially-validated URI. Every checkout, trial, update, broadcast, and remote-content
    /// open path must go through this (or <see cref="Evaluate"/>) so refusal cannot fall
    /// through to <c>Launcher.LaunchUriAsync</c> / WebView navigation.
    /// </summary>
    /// <remarks>
    /// Pure: no I/O. Callers that bypass this and construct a <see cref="Uri"/> directly
    /// are the Phase 2.2 mutation target — the navigation-gate tests turn red if the gate
    /// is removed or always-allows.
    /// </remarks>
    public static Uri? AuthorizeForNavigation(string? url, out Result evaluation, string[]? allowedDomains = null)
    {
        evaluation = Evaluate(url, allowedDomains);
        if (!evaluation.IsAllowed)
            return null;

        // Evaluate already required an absolute https URI; re-parse for the typed return.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            evaluation = new Result(Decision.RefusedMalformed, "The link could not be read as a web address.");
            return null;
        }

        return uri;
    }

    /// <summary>
    /// Convenience overload when the caller does not need the refusal reason.
    /// </summary>
    public static Uri? AuthorizeForNavigation(string? url, string[]? allowedDomains = null)
        => AuthorizeForNavigation(url, out _, allowedDomains);
}
