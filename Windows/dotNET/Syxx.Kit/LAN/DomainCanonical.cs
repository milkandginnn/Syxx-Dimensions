using System;
using System.Text;

namespace Syxx.Kit;

/// <summary>
/// Domain canonicalization used at the Dimension client boundary (C004).
/// Not all domain entry points in the codebase route through here; callers in ProbePlan
/// and the CLI pass raw user input directly to DNS queries.
/// Enforces lowercase, IDNA/punycode encoding, no trailing dot, and rejects names
/// longer than 253 bytes in encoded A-label form.
/// </summary>
public static class DomainCanonical
{
    /// <summary>
    /// Canonicalize a domain string: lowercase, IDNA/punycode, no trailing dot.
    /// Returns null on failure (convenience for non-throwing callers).
    /// </summary>
    public static string? CanonicalizeOrNull(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        // Strip trailing dot before punycode encoding.
        var noDot = trimmed.EndsWith(".", StringComparison.Ordinal) ? trimmed[..^1] : trimmed;

        string ascii;
        try
        {
            ascii = DNSResolver.ToAsciiName(noDot);
        }
        catch
        {
            return null;
        }

        // F007: enforce 253-byte limit on the ENCODED A-label form, not pre-IDNA length.
        // IDNA can lengthen a name so measuring before encoding can accept names too long for the wire.
        if (System.Text.Encoding.UTF8.GetByteCount(ascii) > 253) return null;

        // IdnMapping.GetAscii does NOT case-fold pure-ASCII labels (e.g. "EXAMPLE.COM" passes
        // through unchanged), so lowercase here to actually enforce the documented canonical form.
        // Punycode "xn--" labels are already lowercase, so this is safe for encoded names too.
        return ascii.ToLowerInvariant();
    }

    /// <summary>Returns true if two domain strings refer to the same canonical name.</summary>
    public static bool AreEquivalent(string a, string b)
    {
        var ca = CanonicalizeOrNull(a);
        var cb = CanonicalizeOrNull(b);
        return ca is not null && cb is not null &&
               string.Equals(ca, cb, StringComparison.Ordinal);
    }
}
