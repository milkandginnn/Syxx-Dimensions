// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Network).
// LAN cartography: discover on-link devices to surface a rogue DNS/DHCP server.
import Foundation

public enum LANAddressKind: String, Codable, Sendable {
    case privateUnicast = "Private unicast"
    case linkLocal = "Link-local"
    case multicast = "Multicast"
    case loopback = "Loopback"
    case broadcast = "Broadcast"
    case other = "Other"
    // D025: distinct kind for public-routable addresses so they are not silently lumped with
    // private 'other' and TCP-connect-scanned unannounced. Not probeable by default — public IPs
    // should be announced to the user before any TCP-connect scan.
    case publicRoutable = "Public routable"

    public var isProbeable: Bool {
        switch self {
        case .privateUnicast, .linkLocal, .other:
            return true
        case .multicast, .loopback, .broadcast, .publicRoutable:
            return false
        }
    }
}

public enum NetworkAddressClassifier {
    public static func kind(forIPv4 address: String) -> LANAddressKind {
        let octets = address
            .split(separator: ".")
            .compactMap { UInt8($0) }
        guard octets.count == 4 else { return .other }

        if octets == [255, 255, 255, 255] {
            return .broadcast
        }
        // C114: detect directed broadcast addresses (last octet 255 or 0 on private/link-local
        // ranges). Without the subnet mask we cannot determine the exact broadcast for arbitrary
        // prefix lengths, but the common /24 and /16 cases produce last-octet 255 or 0, and the
        // ARP table entry for these always carries the broadcast MAC ff:ff:ff:ff:ff:ff — so this
        // catches the common case and prevents counting it as a probeable device.
        if octets[3] == 255 || octets[3] == 0 {
            let isPrivateOrLinkLocal =
                octets[0] == 10 ||
                (octets[0] == 172 && (16...31).contains(Int(octets[1]))) ||
                (octets[0] == 192 && octets[1] == 168) ||
                (octets[0] == 169 && octets[1] == 254)
            if isPrivateOrLinkLocal { return .broadcast }
        }
        if octets[0] == 127 {
            return .loopback
        }
        if octets[0] >= 224 && octets[0] <= 239 {
            return .multicast
        }
        if octets[0] == 169 && octets[1] == 254 {
            return .linkLocal
        }
        if octets[0] == 10 {
            return .privateUnicast
        }
        if octets[0] == 172 && (16...31).contains(Int(octets[1])) {
            return .privateUnicast
        }
        if octets[0] == 192 && octets[1] == 168 {
            return .privateUnicast
        }

        // D025: distinguish public-routable addresses from other private/unclassified addresses.
        // This prevents unannounced TCP-connect scans of public IPs.
        if IPClassifier.classify(address) == .publicRoutable {
            return .publicRoutable
        }

        return .other
    }
}
