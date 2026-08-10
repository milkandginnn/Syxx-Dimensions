import Foundation

/// DNSSEC Key representation.
public struct DNSKEYRecord: Codable, Sendable {
    public let flags: UInt16
    public let protocolNum: UInt8
    public let algorithm: UInt8
    public let publicKey: String

    public init(flags: UInt16, protocolNum: UInt8, algorithm: UInt8, publicKey: String) {
        self.flags = flags
        self.protocolNum = protocolNum
        self.algorithm = algorithm
        self.publicKey = publicKey
    }
}

/// DNSSEC Signature representation.
public struct RRSIGRecord: Codable, Sendable {
    public let typeCovered: UInt16
    public let algorithm: UInt8
    public let labels: UInt8
    public let originalTTL: UInt32
    public let signatureExpiration: Date
    public let signatureInception: Date
    public let keyTag: UInt16
    public let signerName: String
    public let signature: String
}

/// The DNSSEC posture of a domain, as far as this client can determine it.
///
/// Deliberately does **not** include a "validated / protected" case: this client observes
/// DNSKEY/RRSIG records and checks RRSIG validity *windows*, but does not perform full
/// cryptographic RRSIG-over-RRset signature verification against a trust anchor. Reporting
/// "protected" on the basis of record presence would be a false-secure claim (the exact class
/// of defect this project exists to avoid), so the strongest positive state we emit is
/// `recordsPresentUnverified`. Full chain validation is tracked as proposal A2.
public enum DNSSECStatus: String, Codable, Sendable {
    /// No DNSKEY and/or RRSIG records were observed — the zone appears unsigned.
    case unsigned
    /// DNSKEY and RRSIG records are present and at least one RRSIG is within its validity
    /// window, but the signatures were not cryptographically verified by this client.
    case recordsPresentUnverified
    /// RRSIG records are present but every one observed is expired or not yet valid — a replay
    /// or stale-signature signal (or a broken signer).
    case signatureExpiredOrNotYetValid
    /// The DNSSEC records could not be fetched (timeout, transport error, blocked).
    case inconclusive

    /// Human-readable line for CLI output that never overclaims.
    public var description: String {
        switch self {
        case .unsigned:
            return "NO DNSSEC RECORDS OBSERVED (zone appears unsigned)"
        case .recordsPresentUnverified:
            return "DNSSEC RECORDS OBSERVED, signature window valid (cryptographic validation not performed by this client)"
        case .signatureExpiredOrNotYetValid:
            return "DNSSEC RECORDS OBSERVED but all RRSIGs are expired/not-yet-valid (stale or broken signing)"
        case .inconclusive:
            return "DNSSEC status INCONCLUSIVE (records could not be retrieved)"
        }
    }

    /// Ph6.2: true for `recordsPresentUnverified` — a posture that should NOT count toward the
    /// decisive DNSSEC quorum because the signatures were not cryptographically validated.
    public var isUnverified: Bool { self == .recordsPresentUnverified }
}

public enum DNSSECVerifier {

    /// Returns a typed, honest DNSSEC posture for `domain`, including an RRSIG validity-window
    /// (inception/expiration) check — which the old presence-only path never performed.
    ///
    /// A030/A036: the query sets the EDNS DO bit and asks for DNSKEY (type 48) once. The RRSIG that
    /// covers the DNSKEY RRset is delivered in that same DO-enabled answer — so there is no separate
    /// (and unanswered) bare QTYPE=RRSIG query, which is why the old path reported every signed zone
    /// as unsigned.
    public static func checkStatus(domain: String, resolver: String, now: Date = Date()) async -> DNSSECStatus {
        let response = await DNSResolver.queryUDPDetailed(
            DNSQuery(name: domain, type: 48, server: resolver, edns0UDPSize: 1232, edns0DODNSSEC: true))
        if response.didNotSucceed { return .inconclusive }
        return classify(records: response.records, now: now, queriedName: domain)
    }

    /// Pure, testable classifier over the records of a DO-enabled DNSKEY response. Kept separate from
    /// the network call so every posture (unsigned / stripped / valid / expired) can be exercised
    /// deterministically and so a DoH or authoritative caller can reuse the same honesty rules.
    ///
    /// C059: `queriedName` binds DNSSEC records to the queried zone. DNSKEY records whose owner
    /// does not match the queried name are rejected (an on-path attacker cannot substitute a valid
    /// DNSKEY from an unrelated signed zone). RRSIG records must also belong to the queried zone
    /// and carry a signer name that is the queried zone or an ancestor. Without this binding,
    /// appending a valid-window DNSKEY+RRSIG pair from any signed zone defeats the stripping
    /// canary.
    ///
    /// C125: validates DNSKEY Protocol (must be 3, RFC 4034 §2.1.2), ZONE flag (bit 7, §2.1.1),
    /// and algorithm (0 is reserved, §2.1.3). A DNSKEY failing these checks does not count toward
    /// dnskeyPresent.
    static func classify(records: [DNSRecord], now: Date, queriedName: String = "") -> DNSSECStatus {
        let normalizedQuery = normalizeDomainName(queriedName)

        // C059 + C125: accept only DNSKEY records whose owner name matches the queried zone AND
        // whose Protocol, ZONE flag, and algorithm are valid per RFC 4034.
        let boundDNSKEYs = records.filter { r in
            guard r.type == 48 else { return false }
            if !normalizedQuery.isEmpty && normalizeDomainName(r.name) != normalizedQuery {
                return false
            }
            // C125: validate DNSKEY rdata fields (flags, protocol, algorithm).
            // DNSKEY rdata layout: flags(2) protocol(1) algorithm(1) publicKey(var).
            guard let bytes = hexToBytes(r.rdata), bytes.count >= 4 else { return false }
            let flags = (UInt16(bytes[0]) << 8) | UInt16(bytes[1])
            let protocol_ = bytes[2]
            let algorithm = bytes[3]
            // RFC 4034 §2.1.2: Protocol MUST be 3.
            guard protocol_ == 3 else { return false }
            // B295: RFC 4034 §2.1.1: bit 7 (ZONE flag, 0x0100) must be set for a zone-signing key.
            // However, KSK-only keys set the SEP flag (bit 15, 0x0001) without the ZONE flag.
            // A KSK-only DNSKEY (SEP=1, ZONE=0) is valid per RFC 4034 and must not be rejected.
            // Accept if either ZONE or SEP is set.
            guard (flags & 0x0100) != 0 || (flags & 0x0001) != 0 else { return false }
            // RFC 4034 §2.1.3: algorithm 0 is reserved and cannot support a conclusion.
            guard algorithm != 0 else { return false }
            return true
        }
        let dnskeyPresent = !boundDNSKEYs.isEmpty
        // A036: with the DO bit set, genuinely no DNSKEY is an honest "unsigned" observation.
        if !dnskeyPresent { return .unsigned }

        // RRSIGs whose typeCovered matches the DNSKEY RRset (48) (KDNS-05) AND whose owner
        // matches the queried zone AND whose signer name is the queried zone or an ancestor.
        let windows = records
            .filter { r in
                guard r.type == 46 else { return false }
                // C059: RRSIG owner must match queried zone.
                if !normalizedQuery.isEmpty && normalizeDomainName(r.name) != normalizedQuery {
                    return false
                }
                // C059: RRSIG signer name must be the queried zone or an ancestor.
                if !normalizedQuery.isEmpty,
                   let signer = parseSignerName(rdataHex: r.rdata),
                   !isSignerValid(for: normalizedQuery, signer: normalizeDomainName(signer)) {
                    return false
                }
                return true
            }
            .compactMap { rrsigValidityWindow(rdataHex: $0.rdata, expectedTypeCovered: 48) }

        // B046/A030: DNSKEY present but the DO-enabled answer carried no covering RRSIG — the
        // signatures were stripped on this path, or the resolver ignored the DO bit / returned no
        // OPT. We can neither confirm signing nor call the zone cleanly unsigned, so this is
        // INCONCLUSIVE. Never "signed", and never a silent "unsigned".
        if windows.isEmpty { return .inconclusive }

        let anyCurrentlyValid = windows.contains {
            rrsigWindowCurrentlyValid(inception: $0.inception, expiration: $0.expiration, now: now)
        }
        return anyCurrentlyValid ? .recordsPresentUnverified : .signatureExpiredOrNotYetValid
    }

    /// Parses the raw 32-bit inception/expiration fields out of an RRSIG record's wire-format rdata,
    /// which `DNSResolver` surfaces as a hex string for non-A/AAAA types. Pure and testable. The
    /// values are returned as raw `UInt32` seconds (not `Date`), because RFC 4034 §3.1.5 compares
    /// them with the current time using RFC 1982 serial-number arithmetic, not absolute epochs.
    ///
    /// RRSIG rdata layout: typeCovered(2) algorithm(1) labels(1) originalTTL(4)
    /// signatureExpiration(4) signatureInception(4) keyTag(2) signerName(var) signature(var).
    /// Returns nil if the typeCovered doesn't match `expectedTypeCovered` (KDNS-05).
    static func rrsigValidityWindow(rdataHex: String, expectedTypeCovered: UInt16) -> (inception: UInt32, expiration: UInt32)? {
        guard let bytes = hexToBytes(rdataHex), bytes.count >= 18 else { return nil }
        let typeCovered = (UInt16(bytes[0]) << 8) | UInt16(bytes[1])
        guard typeCovered == expectedTypeCovered else { return nil }
        let expiration = beUInt32(bytes, 8)
        let inception = beUInt32(bytes, 12)
        return (inception, expiration)
    }

    /// Maximum plausible RRSIG validity span, in seconds (5 years). Real signers use windows of days
    /// to a few months. A span approaching the 2^31 serial half-space is implausible and is exactly
    /// what a spoofed local clock would need to wrap a long-expired signature back into "valid" under
    /// serial arithmetic, so we refuse to treat such a window as currently valid (B047).
    /// B291: increased from 2 years (63_072_000) to 5 years — legitimate zones with infrequent
    /// re-signing (e.g. offline KSK ceremonies) can produce 2.5+ year validity windows.
    static let maxPlausibleValiditySpanSeconds: UInt32 = 157_680_000

    /// RFC 1982 (SERIAL_BITS = 32) "a is at or before b": the forward distance from a to b, computed
    /// mod 2^32, is under 2^31.
    static func serialLessThanOrEqual(_ a: UInt32, _ b: UInt32) -> Bool {
        (b &- a) < 0x8000_0000
    }

    /// A042/B047: is `now` within [inception, expiration] using RFC 1982 serial-number arithmetic
    /// (mod 2^32), not absolute-epoch comparison — so a window that straddles the 2106 epoch wrap is
    /// evaluated correctly. Guards against local-clock spoofing by rejecting an implausibly wide
    /// window and a window whose expiration precedes its inception. This checks the validity *window*
    /// only; it does not cryptographically verify the signature.
    static func rrsigWindowCurrentlyValid(inception: UInt32, expiration: UInt32, now: Date) -> Bool {
        let nowEpoch = UInt32(truncatingIfNeeded: Int64(now.timeIntervalSince1970))
        // Expiration must be at or after inception under serial arithmetic, else malformed.
        guard serialLessThanOrEqual(inception, expiration) else { return false }
        // B047: reject an implausibly wide window (clock-spoof / forged validity).
        guard (expiration &- inception) <= maxPlausibleValiditySpanSeconds else { return false }
        return serialLessThanOrEqual(inception, nowEpoch) && serialLessThanOrEqual(nowEpoch, expiration)
    }

    private static func beUInt32(_ bytes: [UInt8], _ offset: Int) -> UInt32 {
        (UInt32(bytes[offset]) << 24) | (UInt32(bytes[offset + 1]) << 16)
            | (UInt32(bytes[offset + 2]) << 8) | UInt32(bytes[offset + 3])
    }

    private static func hexToBytes(_ hex: String) -> [UInt8]? {
        let chars = Array(hex)
        guard chars.count % 2 == 0 else { return nil }
        var out: [UInt8] = []
        out.reserveCapacity(chars.count / 2)
        var i = 0
        while i < chars.count {
            guard let hi = chars[i].hexDigitValue, let lo = chars[i + 1].hexDigitValue else { return nil }
            out.append(UInt8(hi << 4 | lo))
            i += 2
        }
        return out
    }

    // MARK: - C059: Owner/signer binding helpers

    /// Parses the signer name from RRSIG wire-format rdata (hex-encoded).
    /// RRSIG rdata layout: typeCovered(2) algorithm(1) labels(1) originalTTL(4)
    /// signatureExpiration(4) signatureInception(4) keyTag(2) signerName(var) signature(var).
    /// The signer name is encoded as a sequence of `<length-byte><label-bytes>` runs starting
    /// at byte offset 18. A zero-length label terminates the name (root).
    static func parseSignerName(rdataHex: String) -> String? {
        guard let bytes = hexToBytes(rdataHex), bytes.count >= 18 else { return nil }
        var i = 18
        var labels: [String] = []
        while i < bytes.count {
            let len = Int(bytes[i])
            i += 1
            if len == 0 { break } // root label
            guard i + len <= bytes.count else { return nil }
            if let label = String(bytes: Array(bytes[i..<(i + len)]), encoding: .utf8) {
                labels.append(label)
            }
            i += len
        }
        return labels.isEmpty ? "." : labels.joined(separator: ".")
    }

    /// Validates that `signer` is the queried zone itself or an ancestor zone.
    /// e.g., queriedName = "www.example.com", signer = "example.com" → valid (parent zone signed).
    /// queriedName = "example.com", signer = "evil.com" → invalid (unrelated zone).
    static func isSignerValid(for queriedName: String, signer: String) -> Bool {
        if signer == queriedName { return true }
        // The signer must be a suffix (ancestor) of the queried name.
        let q = queriedName.hasSuffix(".") ? String(queriedName.dropLast()) : queriedName
        let s = signer.hasSuffix(".") ? String(signer.dropLast()) : signer
        guard !s.isEmpty, s != "." else { return false }
        return q.hasSuffix("." + s)
    }

    /// Normalizes a domain name for comparison: lowercase, strip trailing dot.
    static func normalizeDomainName(_ name: String) -> String {
        var n = name.lowercased().trimmingCharacters(in: .whitespaces)
        if n.hasSuffix(".") { n = String(n.dropLast()) }
        return n
    }
}
