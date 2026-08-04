// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Network).
// LAN cartography: discover on-link devices to surface a rogue DNS/DHCP server.
import Foundation

public struct LANDevice: Identifiable, Hashable, Codable, Sendable {
    public var id: String { "\(interface)|\(macAddress)|\(ipAddress)" }
    public let ipAddress: String
    public let macAddress: String
    public let interface: String
    public let host: String?
    public let state: ARPEntryState
    public let addressKind: LANAddressKind
    public let notes: [String]

    public init(entry: ARPEntry) {
        self.ipAddress = entry.ipAddress
        self.macAddress = entry.macAddress
        self.interface = entry.interface
        self.host = entry.host
        self.state = entry.state
        self.addressKind = NetworkAddressClassifier.kind(forIPv4: entry.ipAddress)
        self.notes = LANDevice.notes(for: entry)
    }

    private static func notes(for entry: ARPEntry) -> [String] {
        var output: [String] = []
        if entry.state == .incomplete {
            output.append("Incomplete ARP entry")
        }
        if entry.macAddress.lowercased() == "ff:ff:ff:ff:ff:ff" {
            output.append("Broadcast MAC")
        }
        let kind = NetworkAddressClassifier.kind(forIPv4: entry.ipAddress)
        if !kind.isProbeable {
            output.append(kind.rawValue)
        }
        if entry.host == nil {
            output.append("No hostname")
        }
        return output
    }
}

public struct LANSnapshotSummary: Hashable, Codable, Sendable {
    public let deviceCount: Int
    public let reachableCount: Int
    public let incompleteCount: Int
    public let interfaces: [String]
}

public enum LANSnapshotStatus: String, Hashable, Codable, Sendable {
    case complete
    case partial
    case unreadable
}

public enum LANSnapshotValidationError: Error, Hashable, Sendable {
    case negativeRejectedCandidateCount
    case completeRequiresEvidence
    case completeCannotCarryReason
    case completeCannotCarryRejectedCandidates
    case partialRequiresEvidence
    case partialRequiresReason
    case unreadableCannotCarryDevices
    case unreadableRequiresReason
}

public struct LANSnapshot: Hashable, Codable, Sendable {
    public let capturedAt: Date
    public let devices: [LANDevice]
    public let summary: LANSnapshotSummary
    public let status: LANSnapshotStatus
    public let reason: String?
    public let rejectedCandidateCount: Int

    private init(
        capturedAt: Date,
        devices: [LANDevice],
        status: LANSnapshotStatus,
        reason: String?,
        rejectedCandidateCount: Int
    ) throws {
        guard rejectedCandidateCount >= 0 else {
            throw LANSnapshotValidationError.negativeRejectedCandidateCount
        }
        let normalizedReason = reason?.trimmingCharacters(in: .whitespacesAndNewlines)
        let hasReason = !(normalizedReason?.isEmpty ?? true)
        switch status {
        case .complete:
            guard !devices.isEmpty else {
                throw LANSnapshotValidationError.completeRequiresEvidence
            }
            guard reason == nil else {
                throw LANSnapshotValidationError.completeCannotCarryReason
            }
            guard rejectedCandidateCount == 0 else {
                throw LANSnapshotValidationError.completeCannotCarryRejectedCandidates
            }
        case .partial:
            guard !devices.isEmpty else {
                throw LANSnapshotValidationError.partialRequiresEvidence
            }
            guard hasReason else {
                throw LANSnapshotValidationError.partialRequiresReason
            }
        case .unreadable:
            guard devices.isEmpty else {
                throw LANSnapshotValidationError.unreadableCannotCarryDevices
            }
            guard hasReason else {
                throw LANSnapshotValidationError.unreadableRequiresReason
            }
        }

        self.capturedAt = capturedAt
        self.devices = devices
        self.status = status
        self.reason = hasReason ? normalizedReason : nil
        self.rejectedCandidateCount = rejectedCandidateCount
        self.summary = LANSnapshotSummary(
            deviceCount: devices.count,
            reachableCount: devices.filter { $0.state == .reachable || $0.state == .permanent }.count,
            incompleteCount: devices.filter { $0.state == .incomplete }.count,
            interfaces: Array(Set(devices.map(\.interface))).sorted()
        )
    }

    public static func complete(
        capturedAt: Date = Date(),
        devices: [LANDevice]
    ) throws -> LANSnapshot {
        try create(
            capturedAt: capturedAt,
            devices: devices,
            status: .complete,
            reason: nil
        )
    }

    public static func partial(
        capturedAt: Date = Date(),
        devices: [LANDevice],
        reason: String,
        rejectedCandidateCount: Int = 0
    ) throws -> LANSnapshot {
        try create(
            capturedAt: capturedAt,
            devices: devices,
            status: .partial,
            reason: reason,
            rejectedCandidateCount: rejectedCandidateCount
        )
    }

    public static func unreadable(
        capturedAt: Date = Date(),
        reason: String,
        rejectedCandidateCount: Int = 0
    ) throws -> LANSnapshot {
        try create(
            capturedAt: capturedAt,
            devices: [],
            status: .unreadable,
            reason: reason,
            rejectedCandidateCount: rejectedCandidateCount
        )
    }

    public static func create(
        capturedAt: Date = Date(),
        devices: [LANDevice],
        status: LANSnapshotStatus,
        reason: String?,
        rejectedCandidateCount: Int = 0
    ) throws -> LANSnapshot {
        try LANSnapshot(
            capturedAt: capturedAt,
            devices: devices,
            status: status,
            reason: reason,
            rejectedCandidateCount: rejectedCandidateCount
        )
    }

    private enum CodingKeys: String, CodingKey {
        case capturedAt
        case devices
        case summary
        case status
        case reason
        case rejectedCandidateCount
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self = try LANSnapshot.create(
            capturedAt: container.decode(Date.self, forKey: .capturedAt),
            devices: container.decode([LANDevice].self, forKey: .devices),
            status: container.decode(LANSnapshotStatus.self, forKey: .status),
            reason: container.decodeIfPresent(String.self, forKey: .reason),
            rejectedCandidateCount: container.decodeIfPresent(
                Int.self,
                forKey: .rejectedCandidateCount
            ) ?? 0
        )
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(capturedAt, forKey: .capturedAt)
        try container.encode(devices, forKey: .devices)
        try container.encode(summary, forKey: .summary)
        try container.encode(status, forKey: .status)
        try container.encodeIfPresent(reason, forKey: .reason)
        try container.encode(rejectedCandidateCount, forKey: .rejectedCandidateCount)
    }
}

public struct DuplicateMacBinding: Hashable, Codable, Sendable {
    public let macAddress: String
    public let ipAddresses: [String]
    public let interfaces: [String]
}

public struct LANDiff: Hashable, Codable, Sendable {
    public let newDevices: [LANDevice]
    public let missingDevices: [LANDevice]
    public let changedAddresses: [String]
    public let changedInterfaces: [String]
    public let duplicateMacBindings: [DuplicateMacBinding]

    public var hasChanges: Bool {
        !newDevices.isEmpty ||
            !missingDevices.isEmpty ||
            !changedAddresses.isEmpty ||
            !changedInterfaces.isEmpty ||
            !duplicateMacBindings.isEmpty
    }
}

public struct LANCartographer {
    private let commandRunner: CommandRunning

    public init(commandRunner: CommandRunning = SystemCommandRunner()) {
        self.commandRunner = commandRunner
    }

    /// Capture a snapshot of on-link devices from the ARP table.
    ///
    /// - Parameters:
    ///   - maxDevices: maximum number of devices to include (0 = unlimited). When exceeded,
    ///                 the snapshot is flagged as `partial` (E001).
    ///   - timeout: wall-clock deadline in seconds. A timeout is `partial` only when rows
    ///              were captured; otherwise the snapshot is `unreadable`.
    public func snapshot(maxDevices: Int = 0, timeout: TimeInterval = 10.0) throws -> LANSnapshot {
        guard timeout > 0 else {
            return try LANSnapshot.unreadable(reason: "timeout must be positive")
        }

        let command: CommandResult
        do {
            command = try commandRunner.run(
                "/usr/sbin/arp",
                arguments: ["-an"],
                timeout: timeout
            )
        } catch {
            let reason = error.localizedDescription.trimmingCharacters(in: .whitespacesAndNewlines)
            return try LANSnapshot.unreadable(
                reason: reason.isEmpty ? "arp command could not be executed" : reason
            )
        }

        let parseResult = ARPTableParser.parseWithDiagnostics(command.stdout)
        let devices = parseResult.entries
            .map(LANDevice.init(entry:))
            .sorted { lhs, rhs in
                if lhs.interface == rhs.interface {
                    return lhs.ipAddress.localizedStandardCompare(rhs.ipAddress) == .orderedAscending
                }
                return lhs.interface < rhs.interface
            }

        if command.timedOut {
            return devices.isEmpty
                ? try LANSnapshot.unreadable(
                    reason: "arp command timed out before completion",
                    rejectedCandidateCount: parseResult.rejectedCandidateCount
                )
                : try LANSnapshot.partial(
                    devices: devices,
                    reason: "arp command timed out before completion",
                    rejectedCandidateCount: parseResult.rejectedCandidateCount
                )
        }
        if command.exitCode != 0 {
            return devices.isEmpty
                ? try LANSnapshot.unreadable(
                    reason: commandFailureReason(command),
                    rejectedCandidateCount: parseResult.rejectedCandidateCount
                )
                : try LANSnapshot.partial(
                    devices: devices,
                    reason: commandFailureReason(command),
                    rejectedCandidateCount: parseResult.rejectedCandidateCount
                )
        }
        if devices.isEmpty {
            return try LANSnapshot.unreadable(
                reason: "arp output contained no parseable neighbour rows",
                rejectedCandidateCount: parseResult.rejectedCandidateCount
            )
        }
        if maxDevices > 0, devices.count > maxDevices {
            return try LANSnapshot.partial(
                devices: Array(devices.prefix(maxDevices)),
                reason: "snapshot limited to \(maxDevices) devices",
                rejectedCandidateCount: parseResult.rejectedCandidateCount
            )
        }
        if parseResult.rejectedCandidateCount > 0 {
            return try LANSnapshot.partial(
                devices: devices,
                reason: parserRejectionReason(parseResult.rejectedCandidateCount),
                rejectedCandidateCount: parseResult.rejectedCandidateCount
            )
        }
        return try LANSnapshot.complete(devices: devices)
    }

    private func commandFailureReason(_ command: CommandResult) -> String {
        let diagnostic = command.stderr.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? command.stdout.trimmingCharacters(in: .whitespacesAndNewlines)
            : command.stderr.trimmingCharacters(in: .whitespacesAndNewlines)
        let reason = command.exitCode.map { "arp command exited with code \($0)" }
            ?? "arp command did not report an exit code"
        return diagnostic.isEmpty ? reason : "\(reason): \(diagnostic)"
    }

    private func parserRejectionReason(_ rejectedCandidateCount: Int) -> String {
        let noun = rejectedCandidateCount == 1 ? "row" : "rows"
        return "arp parser rejected \(rejectedCandidateCount) candidate \(noun)"
    }

    public static func diff(previous: LANSnapshot, current: LANSnapshot) -> LANDiff {
        let previousIDs = Set(previous.devices.map { $0.id.lowercased() })
        let currentIDs = Set(current.devices.map { $0.id.lowercased() })
        let canInferAbsence = previous.status == .complete && current.status == .complete

        let newDevices = previous.status == .complete
            ? current.devices.filter { !previousIDs.contains($0.id.lowercased()) }
            : []
        let missingDevices = canInferAbsence
            ? previous.devices.filter { !currentIDs.contains($0.id.lowercased()) }
            : []

        var changedAddresses: [String] = []
        var changedInterfaces: [String] = []
        if canInferAbsence {
            let previousByMAC = Dictionary(
                grouping: previous.devices.compactMap { device -> (LANDevice, String)? in
                    guard device.state != .incomplete,
                          let macAddress = ARPTableParser.canonicalMACAddress(device.macAddress) else {
                        return nil
                    }
                    return (device, macAddress)
                },
                by: { $0.1 }
            )
            let currentByMAC = Dictionary(
                grouping: current.devices.compactMap { device -> (LANDevice, String)? in
                    guard device.state != .incomplete,
                          let macAddress = ARPTableParser.canonicalMACAddress(device.macAddress) else {
                        return nil
                    }
                    return (device, macAddress)
                },
                by: { $0.1 }
            )
            for macAddress in currentByMAC.keys.sorted() {
                guard let oldBindings = previousByMAC[macAddress],
                      oldBindings.count == 1,
                      let newBindings = currentByMAC[macAddress],
                      newBindings.count == 1,
                      let old = oldBindings.first?.0,
                      let device = newBindings.first?.0 else {
                    continue
                }
                if old.ipAddress.caseInsensitiveCompare(device.ipAddress) != .orderedSame {
                    changedAddresses.append(
                        "\(device.macAddress) moved \(old.ipAddress) -> \(device.ipAddress)"
                    )
                }
                if old.interface != device.interface {
                    changedInterfaces.append(
                        "\(device.macAddress) moved \(old.interface) -> \(device.interface)"
                    )
                }
            }
        }

        return LANDiff(
            newDevices: newDevices,
            missingDevices: missingDevices,
            changedAddresses: changedAddresses,
            changedInterfaces: changedInterfaces,
            duplicateMacBindings: duplicateBindings(in: current.devices)
        )
    }

    private static func duplicateBindings(in devices: [LANDevice]) -> [DuplicateMacBinding] {
        Dictionary(
            grouping: devices.compactMap { device -> (LANDevice, String)? in
                guard device.state != .incomplete,
                      let macAddress = ARPTableParser.canonicalMACAddress(device.macAddress) else {
                    return nil
                }
                return (device, macAddress)
            },
            by: { $0.1 }
        )
        .compactMap { macAddress, bindings -> DuplicateMacBinding? in
            let ipAddresses = Array(Set(bindings.map(\.0.ipAddress))).sorted {
                $0.localizedStandardCompare($1) == .orderedAscending
            }
            guard ipAddresses.count > 1 else { return nil }
            return DuplicateMacBinding(
                macAddress: macAddress,
                ipAddresses: ipAddresses,
                interfaces: Array(Set(bindings.map(\.0.interface))).sorted()
            )
        }
        .sorted { $0.macAddress < $1.macAddress }
    }
}

// MARK: - C100: LAN snapshot persistence

/// C100: persist the LAN snapshot alongside the sentinel snapshot so `diff` can detect
/// changes between runs — the old code had no save/load, so each `syxx lan` invocation started
/// from nothing and "a new device appeared on your network and it is now your DNS resolver"
/// was not detectable across runs.
extension LANCartographer {

    /// Location of the persisted LAN snapshot.
    public static var snapshotURL: URL {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx", isDirectory: true)
        return base.appendingPathComponent("lan-snapshot.json")
    }

    /// Save a LAN snapshot to disk. Owner-only (0600) — the file lists on-link device IPs and
    /// MACs, which is LAN-adjacent information.
    public static func saveSnapshot(_ snapshot: LANSnapshot, to url: URL = snapshotURL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        let data = try JSONEncoder().encode(snapshot)
        try data.write(to: url, options: .atomic)
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }

    /// Load the previously persisted LAN snapshot. Returns nil when the file is absent or
    /// unreadable; callers should treat that as "no prior LAN state to compare against".
    public static func loadSnapshot(from url: URL = snapshotURL) -> LANSnapshot? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(LANSnapshot.self, from: data)
    }
}
