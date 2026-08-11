import Foundation

/// Domain canonicalization used at the Dimension client boundary (C004).
/// Not all domain entry points in the codebase route through here; callers in `ProbePlan`
/// and the CLI pass raw user input directly to DNS queries.
///
/// Rules enforced:
/// 1. Lowercase (IDNA/punycode normalizes casing; ASCII domains are lowered explicitly).
/// 2. IDNA/punycode encoding (non-ASCII labels → `xn--` A-labels).
/// 3. No trailing dot (stripped if present; never stored or compared with it).
/// 4. Rejects empty, too-long (>253 A-label chars after encoding), or non-encodable input.
public enum DomainCanonical {

    public enum Error: Swift.Error, CustomStringConvertible {
        case empty
        case tooLong(Int)
        case invalidName(String)

        public var description: String {
            switch self {
            case .empty: return "domain is empty"
            case .tooLong(let len): return "domain length \(len) exceeds 253-char limit"
            case .invalidName(let name): return "invalid domain name: \(name)"
            }
        }
    }

    /// Canonicalize a domain string.
    ///
    /// - Parameter raw: raw user input (e.g. `"Example.com."`, `"münchen.de"`).
    /// - Returns: canonical ASCII form (lowercase, punycode, no trailing dot).
    /// - Throws: `DomainCanonical.Error` if the name cannot be normalized.
    public static func canonicalize(_ raw: String) throws -> String {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw Error.empty }

        // Strip trailing dot before punycode encoding so it doesn't interfere.
        let noDot = trimmed.hasSuffix(".") ? String(trimmed.dropLast()) : trimmed

        // Use the resolver's IDNA/punycode normalizer.
        let ascii: String
        do {
            ascii = try DNSResolver.toASCIIName(noDot)
        } catch {
            throw Error.invalidName(trimmed)
        }

        // F007: enforce 253-byte limit on the ENCODED A-label form, not pre-IDNA UTF-8.
        // IDNA can lengthen a name (e.g. "münchen" → "xn--mnchen-3ya") so measuring before
        // encoding can accept names that are illegal on the wire.
        guard ascii.utf8.count <= 253 else { throw Error.tooLong(ascii.utf8.count) }

        // `toASCIIName` returns pure-ASCII input un-lowercased (it only transforms non-ASCII
        // labels), so lowercase here to actually enforce the documented canonical form and stay
        // in parity with Windows `DomainCanonical.CanonicalizeOrNull`. Done at the canonicalization
        // boundary, NOT in `toASCIIName`, so case-sensitive DNS callers (e.g. 0x20 encoding) are
        // unaffected. "xn--" A-labels are already lowercase, so this is safe for encoded names.
        return ascii.lowercased()
    }

    /// Canonicalize and return `nil` on failure (convenience for non-throwing callers).
    public static func canonicalizeOrNil(_ raw: String) -> String? {
        try? canonicalize(raw)
    }

    /// Returns `true` if two domain strings refer to the same canonical name.
    public static func areEquivalent(_ a: String, _ b: String) -> Bool {
        guard let ca = canonicalizeOrNil(a), let cb = canonicalizeOrNil(b) else { return false }
        return ca.utf8.elementsEqual(cb.utf8)
    }
}
