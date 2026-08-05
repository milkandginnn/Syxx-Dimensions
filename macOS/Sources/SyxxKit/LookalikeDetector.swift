import Foundation

/// C6 — Lookalike domain detection. Flags names in the live query stream that impersonate a
/// known brand through (a) homoglyph substitution (Cyrillic/Greek letters that render like
/// Latin ones — `pаypal.com` with a Cyrillic а), or (b) typosquatting within one edit of a
/// brand (`gooogle.com`, `paypa1.com`). Deterministic and explainable — no ML, no scoring
/// black box: every alert names the brand it matched and how.
///
/// Honesty posture: this catches DECEPTIVE-NAME shapes, it does not assert the domain is
/// malicious. An IDN homoglyph or a near-miss typo is a `suspicious` finding a human confirms,
/// never a result. A name that is genuinely the brand (or unrelated) produces no finding.
public enum LookalikeDetector {

    /// The brands worth impersonating — high-value login / payment / mail targets. Registrable
    /// (eTLD+1) form. Extendable; kept small and legible on purpose.
    public static let defaultBrands: [String] = [
        "google.com", "paypal.com", "apple.com", "microsoft.com", "amazon.com",
        "facebook.com", "netflix.com", "instagram.com", "chase.com", "wellsfargo.com",
        "bankofamerica.com", "coinbase.com", "binance.com", "icloud.com", "outlook.com",
    ]

    public enum Reason: String, Codable, Sendable {
        case homoglyph       // non-Latin characters that render like the brand's Latin letters
        case typosquat       // within one edit of the brand (insertion/deletion/substitution/transposition)
        case mixedScript     // the label mixes scripts (Latin + Cyrillic/Greek) — a spoofing hallmark
    }

    public struct Finding: Codable, Sendable, Equatable {
        public let queried: String     // the domain as observed (may be an xn-- A-label)
        public let display: String     // the decoded Unicode form a user would see
        public let brand: String       // the brand it impersonates
        public let reason: Reason
        public let detail: String
    }

    // MARK: - Confusable skeleton

    /// A minimal confusables table: characters that render like a Latin letter map to it. Sourced
    /// from the common subset of the Unicode confusables data (Cyrillic + Greek + a few digits/
    /// symbols). Not exhaustive — a documented, legible subset that covers the high-frequency
    /// homoglyph attacks. Extend as real captures surface new ones.
    private static let confusables: [Character: Character] = [
        // Cyrillic → Latin
        "а": "a", "е": "e", "о": "o", "р": "p", "с": "c", "х": "x", "у": "y", "ѕ": "s",
        "і": "i", "ј": "j", "к": "k", "н": "h", "м": "m", "т": "t", "в": "b", "г": "r",
        // Greek → Latin
        "ο": "o", "α": "a", "ρ": "p", "ε": "e", "ν": "v", "τ": "t", "ι": "i", "κ": "k",
        // digit / symbol homoglyphs
        "0": "o", "1": "l", "5": "s", "3": "e",
    ]

    /// The confusable-folded skeleton of a string: every character mapped to its Latin lookalike
    /// (when known) and lowercased. Two names with the same skeleton render alike.
    public static func skeleton(_ s: String) -> String {
        String(s.lowercased().map { confusables[$0] ?? $0 })
    }

    /// True when `s` contains any character our confusables table maps away from ASCII — i.e. a
    /// non-Latin glyph masquerading as Latin.
    public static func hasHomoglyph(_ s: String) -> Bool {
        s.lowercased().contains { ch in
            guard let mapped = confusables[ch] else { return false }
            return ch != mapped && !ch.isASCII
        }
    }

    /// True when the label mixes Latin letters with Cyrillic or Greek — a strong spoof signal
    /// independent of whether we happen to have the glyph in the confusables table.
    public static func isMixedScript(_ label: String) -> Bool {
        var hasLatin = false, hasOther = false
        for scalar in label.unicodeScalars {
            let v = scalar.value
            if (v >= 0x41 && v <= 0x5A) || (v >= 0x61 && v <= 0x7A) { hasLatin = true }
            else if (v >= 0x0400 && v <= 0x04FF) || (v >= 0x0370 && v <= 0x03FF) { hasOther = true }
        }
        return hasLatin && hasOther
    }

    // MARK: - Edit distance (bounded)

    /// Damerau-Levenshtein distance in its OPTIMAL STRING ALIGNMENT (OSA) form, capped: returns
    /// `cap + 1` the moment it is exceeded so a pair of long unrelated names is cheap to reject.
    /// B140: the recurrence includes the adjacent-transposition term
    /// (d[i-2][j-2] + 1 when x[i-1]==y[j-2] && x[i-2]==y[j-1]). Without it, an adjacent swap like
    /// "payapl" had distance 2, exceeded the cap of 1, and returned no suspicious finding for a
    /// documented typosquat shape.
    ///
    /// This is OSA, not unrestricted Damerau-Levenshtein: each substring may be edited at most once,
    /// so a transposed pair is never edited again afterwards. The two variants differ on inputs such
    /// as "ca" -> "abc" (OSA 3, unrestricted 2). K-18 ported this exact variant to the Windows twin
    /// (`Syxx.Kit/LookalikeDetector.cs`), which until 2026-08-04 used plain Levenshtein over two
    /// rolling rows and so could not carry the transposition term at all.
    public static func editDistance(_ a: String, _ b: String, cap: Int) -> Int {
        let x = Array(a), y = Array(b)
        if abs(x.count - y.count) > cap { return cap + 1 }
        let m = x.count, n = y.count
        // An empty operand makes the `1...m` / `1...n` loops below a range precondition failure.
        // Distance from the empty string is just the other length; return it directly.
        if m == 0 { return n }
        if n == 0 { return m }
        // Two rows are insufficient for transposition (needs i-2, j-2); keep a full matrix.
        var d = [[Int]](repeating: [Int](repeating: 0, count: n + 1), count: m + 1)
        for i in 0...m { d[i][0] = i }
        for j in 0...n { d[0][j] = j }
        for i in 1...m {
            var rowMin = d[i][0]
            for j in 1...n {
                let cost = x[i-1] == y[j-1] ? 0 : 1
                d[i][j] = Swift.min(d[i-1][j] + 1, d[i][j-1] + 1, d[i-1][j-1] + cost)
                // B140: adjacent transposition — the defining Damerau term.
                if i > 1 && j > 1 && x[i-1] == y[j-2] && x[i-2] == y[j-1] {
                    d[i][j] = Swift.min(d[i][j], d[i-2][j-2] + 1)
                }
                rowMin = Swift.min(rowMin, d[i][j])
            }
            if rowMin > cap { return cap + 1 }
        }
        return d[m][n]
    }

    // MARK: - Registrable-domain extraction

    /// The eTLD+1-ish registrable form: the last two labels. Good enough for the brand list
    /// (all `brand.tld`); multi-part suffixes (`co.uk`) are a known limitation surfaced honestly
    /// rather than guessed. Returns lowercased.
    public static func registrable(_ domain: String) -> String {
        let labels = domain.lowercased().split(separator: ".").map(String.init)
        guard labels.count >= 2 else { return domain.lowercased() }
        return labels.suffix(2).joined(separator: ".")
    }

    // MARK: - Analysis

    /// Analyse one observed domain against the brand list. Returns the strongest single finding,
    /// or nil when the name is the brand itself or bears no resemblance.
    public static func analyze(_ observed: String, brands: [String] = defaultBrands) -> Finding? {
        let display = decoded(observed)
        let reg = registrable(display)
        let regSkeleton = skeleton(reg)

        for brand in brands {
            // Note: the genuine brand is excluded WITHOUT a fast-path `reg == brand` guard — each
            // branch below already excludes an exact match (homoglyph branches require `reg != brand`
            // or a non-ASCII glyph; the typosquat branch requires distance EXACTLY 1, so distance 0
            // cannot match). Keeping the exclusion inside the branches means a single test can pin it
            // (a redundant short-circuit would mask a widened typosquat threshold).
            let firstLabelDisplay = display.split(separator: ".").first.map(String.init) ?? display
            let brandFirst = brand.split(separator: ".").first.map(String.init) ?? brand

            // 1. Homoglyph: the folded skeleton equals the brand but the raw name is not the brand.
            if regSkeleton == brand && reg != brand {
                let mixed = isMixedScript(firstLabelDisplay)
                return Finding(queried: observed, display: display, brand: brand,
                               reason: mixed ? .mixedScript : .homoglyph,
                               detail: "The name renders like \(brand) but uses look-alike characters (\(mixed ? "mixed scripts" : "confusable glyphs")). This is how phishing pages impersonate a brand.")
            }

            // 2. Homoglyph present + skeleton within one edit of the brand — a near-miss spoof.
            if hasHomoglyph(firstLabelDisplay),
               editDistance(regSkeleton, brand, cap: 1) <= 1 {
                return Finding(queried: observed, display: display, brand: brand,
                               reason: .homoglyph,
                               detail: "The name uses look-alike characters and is one edit from \(brand).")
            }

            // 3. Pure-ASCII typosquat: within one edit of the brand's registrable form, and the
            //    first label is close to the brand's first label (avoids matching unrelated TLD
            //    swaps of common words). The brand itself is excluded above.
            if reg.allSatisfy({ $0.isASCII }),
               editDistance(reg, brand, cap: 1) == 1,
               editDistance(brandFirst, firstLabelDisplay, cap: 2) <= 2 {
                return Finding(queried: observed, display: display, brand: brand,
                               reason: .typosquat,
                               detail: "The name is one character off \(brand) — a typosquat a user could easily misread.")
            }
        }
        return nil
    }

    /// Decode any `xn--` labels to their Unicode display form; labels that fail to decode are
    /// left as-is (an undecodable A-label is itself surfaced, not dropped).
    public static func decoded(_ domain: String) -> String {
        domain.split(separator: ".", omittingEmptySubsequences: false).map { label -> String in
            let l = String(label)
            guard l.lowercased().hasPrefix("xn--") else { return l }
            let payload = String(l.dropFirst(4))
            return DNSResolver.punycodeDecode(payload) ?? l
        }.joined(separator: ".")
    }
}
