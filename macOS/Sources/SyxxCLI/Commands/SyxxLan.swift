import Foundation
import SyxxKit
struct SyxxLanCLI {
    /// B253: 0=complete map, 2=partial/unreadable (incomplete evidence), 3=throw/operational.
    @discardableResult
    static func run(_ argv: [String]) -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx lan",
            usage: "Usage: syxx lan   (map on-link devices from the ARP table and flag your DNS resolver)") {
            return CLIExitStatus.completed.rawValue
        }
        print("Mapping on-link devices (ARP cache)...")
        let snapshot: LANSnapshot
        do {
            snapshot = try LANCartographer().snapshot()
        } catch {
            print("Could not read the ARP table: \(error.localizedDescription)")
            return CLIExitStatus.failure.rawValue
        }
        // Cross-reference against the system resolvers so a resolver that is itself an on-link
        // device is called out — the first step toward locating a rogue DNS box (datapoint X1).
        let resolverIPs = Set(NetworkKeyState.getSystemDNSServers())

        // C101: branch on the snapshot's status before rendering. An unreadable or partial
        // snapshot must not be rendered as "nothing unusual, your cache is just cold" — that
        // message implies a complete, clean measurement when the ARP table was actually
        // truncated, timed out, or contained lines the parser could not understand.
        switch snapshot.status {
        case .unreadable:
            print("The LAN map could not be built: \(snapshot.reason ?? "unknown reason").")
            print("This is not a measurement of your network — it is an incomplete result.")
            return CLIExitStatus.inconclusive.rawValue
        case .partial:
            print("⚠️ The LAN map is incomplete — this listing is not a complete picture of your network.")
            if let reason = snapshot.reason { print("   Reason: \(reason)") }
            if snapshot.devices.isEmpty {
                // Partial with zero devices is effectively unreadable.
                return CLIExitStatus.inconclusive.rawValue
            }
            // Fall through to render the devices we do have, with the warning already printed.
            print("")
        case .complete:
            if snapshot.devices.isEmpty {
                print("No devices found in the ARP cache. (Try generating traffic first, e.g. ping your gateway.)")
                return CLIExitStatus.completed.rawValue
            }
            print("")
        }

        print("Found \(snapshot.summary.deviceCount) device(s) across interface(s): \(snapshot.summary.interfaces.joined(separator: ", "))")
        print("  reachable/permanent: \(snapshot.summary.reachableCount)   incomplete: \(snapshot.summary.incompleteCount)")
        print("----------------------------------------------------------------------------------")
        for d in snapshot.devices {
            let isResolver = resolverIPs.contains(d.ipAddress)
            let flag = isResolver ? "  ⟵ THIS DEVICE IS ONE OF YOUR DNS RESOLVERS" : ""
            let ip = d.ipAddress.padding(toLength: 16, withPad: " ", startingAt: 0)
            let mac = d.macAddress.padding(toLength: 18, withPad: " ", startingAt: 0)
            let kind = d.addressKind.rawValue.padding(toLength: 15, withPad: " ", startingAt: 0)
            print("  \(ip) \(mac) \(kind) [\(d.state.rawValue)] on \(d.interface)\(flag)")
            if !d.notes.isEmpty {
                print("      notes: \(d.notes.joined(separator: "; "))")
            }
        }
        print("----------------------------------------------------------------------------------")
        // C106: ARP bindings are unauthenticated — any host on the segment can claim any IP.
        print("⚠️  ARP bindings are unauthenticated — a device on this network can claim any address.")
        print("   Treat this map as a lead, not as proof of device identity.")
        let resolversOnLan = snapshot.devices.filter { resolverIPs.contains($0.ipAddress) }
        if !resolversOnLan.isEmpty {
            print("\n🔎 Derived: your DNS resolver is a device on this LAN:")
            for d in resolversOnLan {
                print("   \(d.ipAddress) is at MAC \(d.macAddress) (\(d.addressKind.rawValue)). Confirm this is your intended router/resolver.")
            }
        } else if !resolverIPs.isEmpty {
            // C105: the ARP cache is not a probe — resolvers absent from it may simply not have
            // been contacted recently. Wording must be conditional, not categorical.
            print("\n🔎 Derived: none of your configured resolvers are currently in this host's ARP cache.")
            print("   That usually means they are off-LAN (reached through the gateway), but a resolver")
            print("   this Mac has not contacted recently will also be absent — this is not conclusive.")
        }

        // C100: persist the snapshot so future runs can diff against it.
        do {
            try LANCartographer.saveSnapshot(snapshot)
        } catch {
            // Persistence failure is non-fatal for the one-shot listing — the diff capability
            // just will not work across runs until the write succeeds.
        }
        // Partial evidence is not a completed clean measurement.
        if snapshot.status == .partial {
            return CLIExitStatus.inconclusive.rawValue
        }
        return CLIExitStatus.completed.rawValue
    }
}
