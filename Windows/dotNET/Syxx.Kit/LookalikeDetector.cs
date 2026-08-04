using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Syxx.Kit;

/// <summary>
/// C6 — Lookalike domain detection (macOS LookalikeDetector twin). Flags names in the query
/// stream impersonating a known brand via homoglyph substitution or one-edit typosquatting.
/// Deterministic and explainable — every alert names the brand and how. A deceptive-name shape
/// is a `suspicious` finding a human confirms, never a malice result.
/// </summary>
public static class LookalikeDetector
{
    public static readonly string[] DefaultBrands =
    {
        "google.com", "paypal.com", "apple.com", "microsoft.com", "amazon.com",
        "facebook.com", "netflix.com", "instagram.com", "chase.com", "wellsfargo.com",
        "bankofamerica.com", "coinbase.com", "binance.com", "icloud.com", "outlook.com",
    };

    public enum Reason { Homoglyph, Typosquat, MixedScript }

    public sealed record Finding(string Queried, string Display, string Brand, Reason Reason, string Detail);

    // Minimal, documented confusables subset (Cyrillic + Greek + digit/symbol homoglyphs) —
    // matches the macOS table character-for-character.
    private static readonly Dictionary<char, char> Confusables = new()
    {
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c', ['х'] = 'x', ['у'] = 'y', ['ѕ'] = 's',
        ['і'] = 'i', ['ј'] = 'j', ['к'] = 'k', ['н'] = 'h', ['м'] = 'm', ['т'] = 't', ['в'] = 'b', ['г'] = 'r',
        ['ο'] = 'o', ['α'] = 'a', ['ρ'] = 'p', ['ε'] = 'e', ['ν'] = 'v', ['τ'] = 't', ['ι'] = 'i', ['κ'] = 'k',
        ['0'] = 'o', ['1'] = 'l', ['5'] = 's', ['3'] = 'e',
    };

    /// The confusable-folded skeleton: every char mapped to its Latin lookalike, lowercased.
    public static string Skeleton(string s)
        => new string(s.ToLowerInvariant().Select(c => Confusables.TryGetValue(c, out var m) ? m : c).ToArray());

    /// True when s contains a non-ASCII glyph masquerading as Latin.
    public static bool HasHomoglyph(string s)
        => s.ToLowerInvariant().Any(c => Confusables.TryGetValue(c, out var m) && c != m && c > 127);

    /// True when a label mixes Latin with Cyrillic or Greek — a spoof hallmark.
    public static bool IsMixedScript(string label)
    {
        bool hasLatin = false, hasOther = false;
        foreach (var c in label)
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) hasLatin = true;
            else if ((c >= 0x0400 && c <= 0x04FF) || (c >= 0x0370 && c <= 0x03FF)) hasOther = true;
        }
        return hasLatin && hasOther;
    }

    /// Damerau-ish (Levenshtein) distance, capped: returns cap+1 the moment it is exceeded.
    public static int EditDistance(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        var curr = new int[b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, curr[j]);
            }
            if (rowMin > cap) return cap + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// eTLD+1-ish registrable form (last two labels), lowercased. Multi-part suffixes (co.uk)
    /// are a known limitation, surfaced honestly rather than guessed.
    public static string Registrable(string domain)
    {
        var labels = domain.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);
        return labels.Length >= 2 ? string.Join('.', labels[^2], labels[^1]) : domain.ToLowerInvariant();
    }

    /// Decode any xn-- labels to Unicode; undecodable labels are left as-is (surfaced, not dropped).
    public static string Decoded(string domain)
    {
        var idn = new IdnMapping();
        return string.Join('.', domain.Split('.').Select(label =>
        {
            if (!label.ToLowerInvariant().StartsWith("xn--", StringComparison.Ordinal)) return label;
            try { return idn.GetUnicode(label); }
            catch (ArgumentException) { return label; }   // malformed A-label — surface it verbatim
        }));
    }

    /// Analyse one observed domain against the brand list. Returns the strongest single finding,
    /// or null when the name is the brand itself or bears no resemblance.
    public static Finding? Analyze(string observed, string[]? brands = null)
    {
        brands ??= DefaultBrands;
        string display = Decoded(observed);
        string reg = Registrable(display);
        string regSkeleton = Skeleton(reg);
        string firstLabelDisplay = display.Split('.').FirstOrDefault() ?? display;

        foreach (var brand in brands)
        {
            // The genuine brand is excluded WITHOUT a fast-path guard — each branch below already
            // excludes an exact match (parity with macOS: a redundant short-circuit would mask a
            // widened typosquat threshold from the tests).
            string brandFirst = brand.Split('.').FirstOrDefault() ?? brand;

            // 1. Homoglyph: folded skeleton equals the brand but the raw name is not the brand.
            if (regSkeleton == brand && reg != brand)
            {
                bool mixed = IsMixedScript(firstLabelDisplay);
                return new Finding(observed, display, brand,
                    mixed ? Reason.MixedScript : Reason.Homoglyph,
                    $"The name renders like {brand} but uses look-alike characters ({(mixed ? "mixed scripts" : "confusable glyphs")}). This is how phishing pages impersonate a brand.");
            }

            // 2. Homoglyph present + skeleton within one edit of the brand — a near-miss spoof.
            if (HasHomoglyph(firstLabelDisplay) && EditDistance(regSkeleton, brand, 1) <= 1)
            {
                return new Finding(observed, display, brand, Reason.Homoglyph,
                    $"The name uses look-alike characters and is one edit from {brand}.");
            }

            // 3. Pure-ASCII typosquat: distance EXACTLY 1 from the brand (0 is the brand itself),
            //    with the first label close to the brand's first label.
            if (reg.All(c => c <= 127)
                && EditDistance(reg, brand, 1) == 1
                && EditDistance(brandFirst, firstLabelDisplay, 2) <= 2)
            {
                return new Finding(observed, display, brand, Reason.Typosquat,
                    $"The name is one character off {brand} — a typosquat a user could easily misread.");
            }
        }
        return null;
    }
}
