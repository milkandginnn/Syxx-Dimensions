import Foundation

// ROADMAP Workstream H · supports datapoint A7 (ASN/cohort) in a dependency-free v1, and
// dampens datapoint A2's CDN false-positive.
//
// A2 (authoritative-vs-resolver) legitimately sees DIFFERENT addresses from the recursive path
// vs the authoritative NS for any CDN/geo-routed domain — most of the web. Without context that
// reads as "diverges" on every CDN site. This maps an IPv4 address to a coarse provider using a
// small, curated table of well-known DEDICATED CDN/cloud blocks, so A2 can recognise "different
// IPs, same provider" as benign geo-routing.
//
// Deliberately conservative for a security tool:
//   • Only DEDICATED CDN/edge ranges are listed — never broad cloud /8s that an attacker could
//     also sit in. A miss (unknown provider) is safe; a false "same provider" must be hard to hit.
//   • The cohort NEVER upgrades a result to "clean" — at most it softens `diverges` to
//     `inconclusive`, which is still surfaced for review. A stale/incorrect entry therefore
//     lowers confidence, it cannot manufacture a false all-clear.
//   • The table is a point-in-time (2026) curated subset, not authoritative. Treat as a heuristic.

public enum NetworkCohort {
    /// (networkBase, prefixBits, provider). IPv4 only in v1.
    ///
    /// Sourced from each provider's OFFICIAL published edge list (Cloudflare `ips-v4`, Fastly's
    /// public IP ranges, AWS `ip-ranges.json` CLOUDFRONT service, plus well-known Akamai/Google
    /// serving blocks and Imperva/StackPath edge), current as of 2026-07. Dedicated edge only — no
    /// broad cloud /8s. A missing entry is safe (unknown → no cohort claim); the cost of a gap is a
    /// benign CDN answer read as `diverges`, so completeness here directly reduces A2 false positives.
    private static let ranges: [(UInt32, Int, String)] = buildRanges([
        // Cloudflare — full official ips-v4 list (dedicated anycast edge).
        ("173.245.48.0", 20, "Cloudflare"), ("103.21.244.0", 22, "Cloudflare"),
        ("103.22.200.0", 22, "Cloudflare"), ("103.31.4.0", 22, "Cloudflare"),
        ("141.101.64.0", 18, "Cloudflare"), ("108.162.192.0", 18, "Cloudflare"),
        ("190.93.240.0", 20, "Cloudflare"), ("188.114.96.0", 20, "Cloudflare"),
        ("197.234.240.0", 22, "Cloudflare"), ("198.41.128.0", 17, "Cloudflare"),
        ("162.158.0.0", 15, "Cloudflare"), ("104.16.0.0", 13, "Cloudflare"),
        ("104.24.0.0", 14, "Cloudflare"), ("172.64.0.0", 13, "Cloudflare"),
        ("131.0.72.0", 22, "Cloudflare"),
        // Fastly — published ranges.
        ("23.235.32.0", 20, "Fastly"), ("43.249.72.0", 22, "Fastly"),
        ("104.156.80.0", 20, "Fastly"), ("140.248.64.0", 18, "Fastly"),
        ("140.248.128.0", 17, "Fastly"), ("146.75.0.0", 16, "Fastly"),
        ("151.101.0.0", 16, "Fastly"), ("157.52.64.0", 18, "Fastly"),
        ("167.82.0.0", 17, "Fastly"), ("172.111.64.0", 18, "Fastly"),
        ("185.31.16.0", 22, "Fastly"), ("199.27.72.0", 21, "Fastly"),
        ("199.232.0.0", 16, "Fastly"),
        // Akamai — well-known dedicated edge blocks.
        ("104.64.0.0", 10, "Akamai"), ("23.192.0.0", 11, "Akamai"), ("23.32.0.0", 11, "Akamai"),
        ("23.0.0.0", 12, "Akamai"), ("2.16.0.0", 13, "Akamai"), ("184.24.0.0", 13, "Akamai"),
        ("184.50.0.0", 15, "Akamai"), ("96.16.0.0", 15, "Akamai"), ("95.100.0.0", 15, "Akamai"),
        ("72.246.0.0", 15, "Akamai"), ("88.221.0.0", 16, "Akamai"),
        // Google — serving/edge blocks (not the broad GCP 34/8 · 35/8 compute space).
        ("142.250.0.0", 15, "Google"), ("172.217.0.0", 16, "Google"), ("172.253.0.0", 16, "Google"),
        ("173.194.0.0", 16, "Google"), ("74.125.0.0", 16, "Google"), ("108.177.0.0", 17, "Google"),
        ("209.85.128.0", 17, "Google"), ("216.58.192.0", 19, "Google"), ("216.239.32.0", 19, "Google"),
        ("64.233.160.0", 19, "Google"), ("66.249.64.0", 19, "Google"), ("72.14.192.0", 18, "Google"),
        // AWS CloudFront — CLOUDFRONT-designated edge ranges from ip-ranges.json.
        ("13.32.0.0", 15, "CloudFront"), ("13.224.0.0", 14, "CloudFront"), ("13.249.0.0", 16, "CloudFront"),
        ("18.64.0.0", 14, "CloudFront"), ("18.160.0.0", 15, "CloudFront"), ("52.84.0.0", 15, "CloudFront"),
        ("54.182.0.0", 16, "CloudFront"), ("54.192.0.0", 16, "CloudFront"), ("54.230.0.0", 16, "CloudFront"),
        ("65.8.0.0", 16, "CloudFront"), ("65.9.0.0", 16, "CloudFront"), ("99.84.0.0", 16, "CloudFront"),
        ("108.138.0.0", 15, "CloudFront"), ("108.156.0.0", 14, "CloudFront"), ("130.176.0.0", 16, "CloudFront"),
        ("143.204.0.0", 16, "CloudFront"), ("205.251.192.0", 19, "CloudFront"),
        // Imperva / Incapsula — published edge.
        ("45.60.0.0", 16, "Imperva"), ("107.154.0.0", 16, "Imperva"), ("192.230.64.0", 18, "Imperva"),
        ("198.143.32.0", 19, "Imperva"), ("199.83.128.0", 21, "Imperva"),
        // StackPath / Highwinds (MaxCDN).
        ("151.139.0.0", 16, "StackPath"), ("205.185.192.0", 18, "StackPath"),
    ])

    /// The provider owning `ip`, or nil if not in the curated table (or not IPv4).
    public static func provider(forIPv4 ip: String) -> String? {
        guard let addr = ipv4ToUInt32(ip) else { return nil }
        for (base, prefix, name) in ranges where prefix > 0 {
            let mask: UInt32 = prefix == 32 ? .max : ~(UInt32.max >> UInt32(prefix))
            if (addr & mask) == (base & mask) { return name }
        }
        return nil
    }

    /// If EVERY address in both sets maps to the SAME single provider, return it — else nil.
    /// Conservative: any unknown address, or a mix of providers, yields nil (no cohort claim).
    public static func sharedProvider(_ a: [String], _ b: [String]) -> String? {
        let all = a + b
        guard !all.isEmpty else { return nil }
        var found: String? = nil
        for ip in all {
            guard let p = provider(forIPv4: ip) else { return nil }
            if found == nil { found = p } else if found != p { return nil }
        }
        return found
    }

    // MARK: - IPv4 helpers

    static func ipv4ToUInt32(_ ip: String) -> UInt32? {
        let parts = ip.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count == 4 else { return nil }
        var result: UInt32 = 0
        for p in parts {
            guard let octet = UInt8(p) else { return nil }
            result = (result << 8) | UInt32(octet)
        }
        return result
    }

    private static func buildRanges(_ raw: [(String, Int, String)]) -> [(UInt32, Int, String)] {
        raw.compactMap { (cidr, prefix, name) in
            guard let base = ipv4ToUInt32(cidr) else { return nil }
            return (base, prefix, name)
        }
    }
}
