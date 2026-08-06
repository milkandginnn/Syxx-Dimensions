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
    /// K-20: why a name was rejected, so callers can say what went wrong instead of dropping it
    /// silently. Twin of the macOS <c>DomainCanonical.Error</c> enum. A bare null told an upstream
    /// caller only THAT a domain vanished, never why — an unexplained silent drop.
    /// </summary>
    public abstract record DomainCanonicalError
    {
        /// <summary>Input was empty or entirely whitespace.</summary>
        public sealed record Empty : DomainCanonicalError
        {
            public override string ToString() => "domain is empty";
        }

        /// <summary>Encoded A-label form exceeded the 253-byte wire limit.</summary>
        public sealed record TooLong(int Length) : DomainCanonicalError
        {
            public override string ToString() => $"domain length {Length} exceeds 253-char limit";
        }

        /// <summary>Input could not be IDNA/punycode encoded.</summary>
        public sealed record InvalidName(string Name) : DomainCanonicalError
        {
            public override string ToString() => $"invalid domain name: {Name}";
        }
    }

    /// <summary>
    /// Canonicalize a domain string: lowercase, IDNA/punycode, no trailing dot.
    /// Returns null on failure (convenience for non-throwing callers).
    /// Callers that need the rejection reason should use <see cref="TryCanonicalize"/>.
    /// </summary>
    public static string? CanonicalizeOrNull(string raw) => TryCanonicalize(raw, out _);

    /// <summary>
    /// Canonicalize a domain string, reporting WHY it was rejected.
    /// Twin of the throwing macOS <c>DomainCanonical.canonicalize(_:)</c>; C# uses the try-pattern
    /// rather than exceptions because every existing caller here is on a non-throwing path.
    /// </summary>
    /// <param name="raw">Raw user input (e.g. <c>"Example.com."</c>, <c>"münchen.de"</c>).</param>
    /// <param name="error">The rejection reason on failure; <c>null</c> on success.</param>
    /// <returns>Canonical ASCII form (lowercase, punycode, no trailing dot), or <c>null</c>.</returns>
    public static string? TryCanonicalize(string raw, out DomainCanonicalError? error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = new DomainCanonicalError.Empty();
            return null;
        }
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
            error = new DomainCanonicalError.InvalidName(trimmed);
            return null;
        }

        // F007: enforce 253-byte limit on the ENCODED A-label form, not pre-IDNA length.
        // IDNA can lengthen a name so measuring before encoding can accept names too long for the wire.
        var encodedLength = System.Text.Encoding.UTF8.GetByteCount(ascii);
        if (encodedLength > 253)
        {
            error = new DomainCanonicalError.TooLong(encodedLength);
            return null;
        }

        // IdnMapping.GetAscii does NOT case-fold pure-ASCII labels (e.g. "EXAMPLE.COM" passes
        // through unchanged), so lowercase here to actually enforce the documented canonical form.
        // Punycode "xn--" labels are already lowercase, so this is safe for encoded names too.
        error = null;
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
