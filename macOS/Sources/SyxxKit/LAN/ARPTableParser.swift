// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Network).
// LAN cartography: discover on-link devices to surface a rogue DNS/DHCP server.
import Foundation

public enum ARPEntryState: String, Codable, Sendable {
    case reachable
    case incomplete
    case permanent
    case unknown
}

public struct ARPEntry: Identifiable, Hashable, Codable, Sendable {
    public var id: String { "\(interface)|\(ipAddress)|\(macAddress)" }
    public let host: String?
    public let ipAddress: String
    public let macAddress: String
    public let interface: String
    public let state: ARPEntryState
    public let rawLine: String

    public init(
        host: String?,
        ipAddress: String,
        macAddress: String,
        interface: String,
        state: ARPEntryState,
        rawLine: String
    ) {
        self.host = host
        self.ipAddress = ipAddress
        self.macAddress = macAddress
        self.interface = interface
        self.state = state
        self.rawLine = rawLine
    }
}

public struct ARPParseResult: Hashable, Codable, Sendable {
    public let entries: [ARPEntry]
    public let candidateCount: Int
    public let rejectedCandidateCount: Int

    public init(entries: [ARPEntry], candidateCount: Int, rejectedCandidateCount: Int) {
        self.entries = entries
        self.candidateCount = candidateCount
        self.rejectedCandidateCount = rejectedCandidateCount
    }
}

public enum ARPTableParser {
    public static func parse(_ output: String) -> [ARPEntry] {
        parseWithDiagnostics(output).entries
    }

    public static func parseWithDiagnostics(_ output: String) -> ARPParseResult {
        var entries: [ARPEntry] = []
        var candidateCount = 0
        var rejectedCandidateCount = 0

        for rawLine in output.split(whereSeparator: \.isNewline) {
            let line = String(rawLine)
            let isCandidate = looksLikeCandidate(line)
            if let entry = parseLine(line) {
                candidateCount += 1
                entries.append(entry)
            } else if isCandidate {
                candidateCount += 1
                rejectedCandidateCount += 1
            }
        }

        return ARPParseResult(
            entries: entries,
            candidateCount: candidateCount,
            rejectedCandidateCount: rejectedCandidateCount
        )
    }

    public static func parseLine(_ line: String) -> ARPEntry? {
        let tokens = line.split(separator: " ").map(String.init)
        guard tokens.count >= 4 else { return nil }
        guard let rawIP = firstParenthesizedValue(in: line) else { return nil }
        // C114: validate that the parenthesized value is actually an IP address. Without this,
        // any diagnostic line containing parentheses is parsed as a device row with a bogus IP.
        guard IPClassifier.classify(rawIP) != .invalid else { return nil }
        let ipAddress = rawIP
        guard let atIndex = tokens.firstIndex(of: "at"), tokens.indices.contains(atIndex + 1) else { return nil }
        guard let onIndex = tokens.firstIndex(of: "on"), tokens.indices.contains(onIndex + 1) else { return nil }

        let rawMAC = tokens[atIndex + 1].trimmingCharacters(in: .whitespacesAndNewlines)
        let isIncomplete = rawMAC.lowercased() == "(incomplete)" ||
            tokens.contains { token in
                let normalized = token.lowercased()
                return normalized == "incomplete" || normalized == "invalid"
            }
        let macAddress: String
        if isIncomplete {
            macAddress = rawMAC.lowercased()
        } else {
            guard let canonical = canonicalMACAddress(rawMAC) else { return nil }
            macAddress = canonical
        }
        let interface = tokens[onIndex + 1].trimmingCharacters(in: .whitespacesAndNewlines)
        let hostToken = tokens.first
        let host = hostToken == "?" ? nil : hostToken

        return ARPEntry(
            host: host,
            ipAddress: ipAddress,
            macAddress: macAddress,
            interface: interface,
            state: state(for: macAddress, tokens: tokens),
            rawLine: line
        )
    }

    private static func looksLikeCandidate(_ line: String) -> Bool {
        let tokens = line.split(separator: " ").map(String.init)
        return firstParenthesizedValue(in: line) != nil &&
            (tokens.contains("at") || tokens.contains("on"))
    }

    private static func firstParenthesizedValue(in line: String) -> String? {
        guard let open = line.firstIndex(of: "(") else { return nil }
        guard let close = line[open...].firstIndex(of: ")") else { return nil }
        let start = line.index(after: open)
        guard start < close else { return nil }
        return String(line[start..<close])
    }

    /// Structural / flag tokens that are not neighbour-state labels on Darwin `arp -an`
    /// or Linux `ip neigh` lines. Anything residual after stripping these is treated as a
    /// state token and must be recognized explicitly (C141).
    private static let structuralTokens: Set<String> = [
        "at", "on", "ifscope", "published", "pub", "only", "temp", "proxy", "rev",
        "?", "dev", "lladdr", "ether", "link/ether"
    ]

    /// Explicit incomplete / unresolved neighbour states (C141).
    private static let incompleteStateTokens: Set<String> = [
        "incomplete", "invalid", "probe", "failed"
    ]

    /// Explicit permanent / static neighbour states (C141).
    private static let permanentStateTokens: Set<String> = [
        "permanent", "static", "noarp"
    ]

    /// Explicit reachable / confirmed neighbour states (C141).
    /// Darwin `arp -an` has no "reachable" keyword; a valid MAC with no residual state
    /// token is the measured BSD default and maps to reachable below.
    private static let reachableStateTokens: Set<String> = [
        "reachable", "dynamic", "stale", "delay"
    ]

    /// C141: return reachable only for explicitly recognized reachable states (or the
    /// BSD default of a valid MAC with no residual state token). An unrecognized state
    /// token with otherwise valid fields must remain `.unknown`, never collapse to
    /// reachable via "MAC looks fine."
    private static func state(for macAddress: String, tokens: [String]) -> ARPEntryState {
        let normalized = tokens.map { $0.lowercased() }

        if macAddress == "(incomplete)" {
            return .incomplete
        }

        // First pass: honor known state tokens wherever they appear.
        if normalized.contains(where: { incompleteStateTokens.contains($0) }) {
            return .incomplete
        }
        if normalized.contains(where: { permanentStateTokens.contains($0) }) {
            return .permanent
        }
        if normalized.contains(where: { reachableStateTokens.contains($0) }) {
            return .reachable
        }

        // Residual tokens after stripping structural markers must be recognized states.
        // An unknown residual (e.g. localized type, future kernel token, "expired") is
        // not evidence of reachability.
        let residual = residualStateTokens(normalized)
        if !residual.isEmpty {
            return .unknown
        }

        // BSD `arp -an` default: valid MAC, no residual state keyword → measured reachable.
        if canonicalMACAddress(macAddress) != nil {
            return .reachable
        }
        return .unknown
    }

    /// Tokens that remain after removing host, MAC, interface, parenthesized IP, bracketed
    /// link-type tags, and known structural flags. Any leftover is treated as a state label.
    private static func residualStateTokens(_ normalized: [String]) -> [String] {
        var residual: [String] = []
        var index = 0
        while index < normalized.count {
            let token = normalized[index]
            // Skip "at <mac>" and "on <iface>" / "dev <iface>" / "lladdr <mac>" pairs.
            if token == "at" || token == "on" || token == "dev" || token == "lladdr" {
                index += 2
                continue
            }
            // Parenthesized IP or incomplete marker, bracketed link type.
            if (token.hasPrefix("(") && token.hasSuffix(")")) ||
                (token.hasPrefix("[") && token.hasSuffix("]")) {
                index += 1
                continue
            }
            // Host placeholder / structural flags.
            if structuralTokens.contains(token) {
                index += 1
                continue
            }
            // MAC address forms are not state labels.
            if canonicalMACAddress(token) != nil {
                index += 1
                continue
            }
            // First token is the hostname (or "?") — structural, not a state.
            if index == 0 {
                index += 1
                continue
            }
            // Known state families already handled above; if they reappear they are not residual.
            if incompleteStateTokens.contains(token)
                || permanentStateTokens.contains(token)
                || reachableStateTokens.contains(token) {
                index += 1
                continue
            }
            residual.append(token)
            index += 1
        }
        return residual
    }

    static func canonicalMACAddress(_ value: String) -> String? {
        let groups = value.split(separator: ":", omittingEmptySubsequences: false)
        guard groups.count == 6 else { return nil }
        let hexDigits = CharacterSet(charactersIn: "0123456789abcdefABCDEF")
        var canonical: [String] = []
        for group in groups {
            guard (1...2).contains(group.count),
                  group.unicodeScalars.allSatisfy(hexDigits.contains) else {
                return nil
            }
            canonical.append(String(group).lowercased().leftPadded(to: 2, with: "0"))
        }
        return canonical.joined(separator: ":")
    }
}

private extension String {
    func leftPadded(to width: Int, with character: Character) -> String {
        guard count < width else { return self }
        return String(repeating: String(character), count: width - count) + self
    }
}
