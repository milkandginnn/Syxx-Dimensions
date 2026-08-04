import Foundation

public enum IPAddressClass: String, Codable, Hashable, Sendable {
    case publicRoutable
    case privateUse
    case loopback
    case linkLocal
    case carrierGradeNAT
    case documentation
    case multicast
    case unspecified
    case reserved
    case invalid
}

public enum IPClassifier {
    public static func classify(_ value: String) -> IPAddressClass {
        if let ipv4 = parseIPv4(value) {
            return classifyIPv4(ipv4)
        }
        if let ipv6 = parseIPv6(value) {
            return classifyIPv6(ipv6)
        }
        return .invalid
    }

    public static func isBlockingAnswer(_ value: String) -> Bool {
        switch classify(value) {
        case .privateUse, .loopback, .linkLocal, .carrierGradeNAT, .documentation, .multicast, .unspecified, .reserved:
            return true
        case .invalid:
            return true
        case .publicRoutable:
            return false
        }
    }

    private static func parseIPv4(_ value: String) -> [UInt8]? {
        var addr = in_addr()
        guard value.withCString({ inet_pton(AF_INET, $0, &addr) }) == 1 else {
            return nil
        }
        let raw = UInt32(bigEndian: addr.s_addr)
        return [
            UInt8((raw >> 24) & 0xff),
            UInt8((raw >> 16) & 0xff),
            UInt8((raw >> 8) & 0xff),
            UInt8(raw & 0xff)
        ]
    }

    private static func classifyIPv4(_ ip: [UInt8]) -> IPAddressClass {
        if ip[0] == 0 { return .unspecified }
        if ip[0] == 10 { return .privateUse }
        if ip[0] == 127 { return .loopback }
        if ip[0] == 169 && ip[1] == 254 { return .linkLocal }
        if ip[0] == 172 && (16...31).contains(Int(ip[1])) { return .privateUse }
        if ip[0] == 192 && ip[1] == 168 { return .privateUse }
        if ip[0] == 100 && (64...127).contains(Int(ip[1])) { return .carrierGradeNAT }
        // KTRU-11: add missing RFC 5735/6890 special-use ranges
        if ip[0] == 192 && ip[1] == 0 && ip[2] == 0 { return .reserved }           // 192.0.0.0/24 (IETF protocol assignments, not private)
        if ip[0] == 192 && ip[1] == 0 && ip[2] == 2 { return .documentation }
        if ip[0] == 192 && ip[1] == 88 && ip[2] == 99 { return .documentation }       // 192.88.99.0/24 (6to4 relay anycast)
        if ip[0] == 198 && (ip[1] == 18 || ip[1] == 19) { return .privateUse }         // 198.18.0.0/15 (benchmarking)
        if ip[0] == 198 && ip[1] == 51 && ip[2] == 100 { return .documentation }
        if ip[0] == 203 && ip[1] == 0 && ip[2] == 113 { return .documentation }
        if (224...239).contains(Int(ip[0])) { return .multicast }
        if ip[0] >= 240 { return .reserved }
        return .publicRoutable
    }

    private static func parseIPv6(_ value: String) -> [UInt8]? {
        var addr = in6_addr()
        guard value.withCString({ inet_pton(AF_INET6, $0, &addr) }) == 1 else {
            return nil
        }
        return withUnsafeBytes(of: addr.__u6_addr.__u6_addr8) { Array($0) }
    }

    private static func classifyIPv6(_ ip: [UInt8]) -> IPAddressClass {
        if ip.allSatisfy({ $0 == 0 }) { return .unspecified }
        if ip[0..<15].allSatisfy({ $0 == 0 }) && ip[15] == 1 { return .loopback }
        // KTRU-09: detect IPv4-mapped ::ffff:0:0/96 and classify by the embedded IPv4
        if ip[0..<10].allSatisfy({ $0 == 0 }) && ip[10] == 0xff && ip[11] == 0xff {
            return classifyIPv4([ip[12], ip[13], ip[14], ip[15]])
        }
        if ip[0] == 0xfe && (ip[1] & 0xc0) == 0x80 { return .linkLocal }
        if (ip[0] & 0xfe) == 0xfc { return .privateUse }
        if ip[0] == 0xff { return .multicast }
        if ip[0] == 0x20 && ip[1] == 0x01 && ip[2] == 0x0d && ip[3] == 0xb8 { return .documentation }
        return .publicRoutable
    }
}
