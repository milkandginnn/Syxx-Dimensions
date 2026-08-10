import Foundation

// ROADMAP Workstream H · datapoint F2 — CHAOS version.bind resolver fingerprint.
//
// Sends a CHAOS-class (QCLASS 3) TXT query for `version.bind` to the resolver under test. This is a
// *contextual* signal — it improves resolver attribution/triage, it is NEVER proof of tampering — so
// it is weighted lightly and only escalates to a weak interception hint in one specific case:
//
//   • resolver returns a version banner                        → .disclosed (recorded for attribution).
//   • …and the target is a resolver the caller expects to be a
//     PUBLIC recursive, yet the banner names local-forwarder
//     software (dnsmasq / pi-hole / …)                          → forwarderOnPublicPath: a weak
//                                                                 interception hint — something local is
//                                                                 answering on the path.
//   • resolver refuses / returns no banner                     → .withheld (normal for a hardened public
//                                                                 resolver; NOT a signal, NOT "clean").
//   • the CHAOS query could not complete                       → .untested (never rendered as clean).
//
// Layer note: this only fingerprints whichever resolver actually answers on :53 — it says nothing about
// the upstream recursive's behaviour. Attribution/triage, not a result driver.

public struct VersionBindResult: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case disclosed     // a version/hostname banner came back (contextual attribution)
        case withheld      // refused / empty — hardened resolver, no signal (not an error, not clean)
        case untested      // the CHAOS query could not be completed (never rendered clean)
    }

    public let status: Status
    public let banner: String?
    public let server: String
    /// Set only when a caller-designated PUBLIC recursive returned a *local-forwarder* banner — a weak,
    /// contextual interception hint. Never set for a router / self-hosted resolver or an unknown target.
    public let forwarderOnPublicPath: Bool
    public let detail: String

    public init(status: Status, banner: String? = nil, server: String = "",
                forwarderOnPublicPath: Bool = false, detail: String = "") {
        self.status = status
        self.banner = banner
        self.server = server
        self.forwarderOnPublicPath = forwarderOnPublicPath
        self.detail = detail
    }

    public static let untested = VersionBindResult(status: .untested, detail: "not run")
}

public enum VersionBindProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeTXT: UInt16 = 16
    public static let classCHAOS: UInt16 = 3

    /// Software signatures that identify a *local forwarder* rather than a full upstream recursive.
    /// Kept deliberately narrow and unambiguous — these are effectively only ever seen on home routers /
    /// LAN DNS boxes, so on a public-resolver IP they mean something local is intercepting. Public
    /// recursives (unbound / bind / knot / powerdns — and AdGuard, which is *also* a public service) are
    /// intentionally excluded to avoid false interception hints.
    static let forwarderSignatures = ["dnsmasq", "pi-hole", "pihole", "openwrt", "ubios", "unifi"]

    /// Known public recursive resolver IPs. Used so the interception escalation only fires when a
    /// resolver that *should* be a big upstream recursive instead self-identifies as a local forwarder.
    static let knownPublicResolvers: Set<String> = [
        "1.1.1.1", "1.0.0.1", "8.8.8.8", "8.8.4.4", "9.9.9.9", "149.112.112.112",
        "208.67.222.222", "208.67.220.220", "94.140.14.14", "94.140.15.15", "76.76.2.0", "76.76.10.0"
    ]

    public static func isKnownPublicResolver(_ ip: String) -> Bool { knownPublicResolvers.contains(ip) }

    /// Probe `server` for its `version.bind` banner. `expectedPublicResolver` should be true when the
    /// caller believes `server` is a big upstream recursive (defaults to whether it's a known public IP)
    /// — only then does a forwarder banner become an interception hint.
    public static func probe(server: String,
                             expectedPublicResolver: Bool? = nil,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> VersionBindResult {
        let resp = await query(DNSQuery(name: "version.bind", type: typeTXT, server: server, qclass: classCHAOS))
        let isPublic = expectedPublicResolver ?? isKnownPublicResolver(server)
        return classify(response: resp, server: server, expectedPublicResolver: isPublic)
    }

    /// Pure classification over an already-fetched response (offline-testable).
    public static func classify(response: DNSResponse, server: String, expectedPublicResolver: Bool) -> VersionBindResult {
        if response.didNotSucceed {
            return VersionBindResult(status: .untested, server: server,
                detail: "the CHAOS version.bind query got no usable response (\(response.status.rawValue)) — resolver/path unreachable; no fingerprint obtained")
        }
        // Find a TXT (type 16) answer and decode its character-strings into a banner.
        let txt = response.records.first { $0.type == typeTXT }
        guard let hex = txt?.rdata, let banner = decodeTXTBanner(hexRData: hex), !banner.isEmpty else {
            return VersionBindResult(status: .withheld, server: server,
                detail: "resolver did not disclose a version.bind banner (refused or empty) — normal for a hardened / public resolver; no signal")
        }
        let forwarder = expectedPublicResolver && matchesForwarderSignature(banner)
        return VersionBindResult(status: .disclosed, banner: banner, server: server,
            forwarderOnPublicPath: forwarder,
            detail: forwarder
                ? "a resolver expected to be a public recursive answered with a local-forwarder banner (\(banner)) — something local is answering on the path"
                : "resolver version banner: \(banner)")
    }

    static func matchesForwarderSignature(_ banner: String) -> Bool {
        let low = banner.lowercased()
        return forwarderSignatures.contains { low.contains($0) }
    }

    /// Decode TXT RDATA (rendered as a hex string by `DNSResolver.formatRData`) into its concatenated
    /// character-strings. TXT RDATA is one or more `<length-byte><bytes>` runs.
    static func decodeTXTBanner(hexRData: String) -> String? {
        guard let bytes = hexToBytes(hexRData), !bytes.isEmpty else { return nil }
        var i = 0
        var parts: [String] = []
        while i < bytes.count {
            let len = Int(bytes[i]); i += 1
            guard len > 0 else { continue }
            guard i + len <= bytes.count else { break }
            let slice = Array(bytes[i..<(i + len)])
            if let s = String(bytes: slice, encoding: .utf8) { parts.append(s) }
            i += len
        }
        let joined = parts.joined(separator: " ").trimmingCharacters(in: .whitespacesAndNewlines)
        return joined.isEmpty ? nil : joined
    }

    static func hexToBytes(_ hex: String) -> [UInt8]? {
        let chars = Array(hex)
        guard chars.count % 2 == 0 else { return nil }
        var out: [UInt8] = []
        out.reserveCapacity(chars.count / 2)
        var i = 0
        while i < chars.count {
            guard let hi = chars[i].hexDigitValue, let lo = chars[i + 1].hexDigitValue else { return nil }
            out.append(UInt8((hi << 4) | lo))
            i += 2
        }
        return out
    }
}
