import Foundation
import SyxxKit
struct SyxxRouteCLI {
    /// B253: 0=hops produced, 3=usage/unsafe target/empty trace.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx route",
            usage: "Usage: syxx route <target>   (traceroute path analysis)") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        var target = "8.8.8.8"
        if args.count >= 2 {
            target = args[1]
        }
        
        print("Analyzing route path to: \(target)")
        print("Traversing route path hops...")
        guard isSafeTraceTarget(target) else {
            print("Error: target must be a plain hostname or IP address.")
            return CLIExitStatus.failure.rawValue
        }
        
        // Execute traceroute to target and parse hops (shared TracerouteRunner).
        let lines = await TracerouteRunner.trace(target: target, maxHops: 15, timeout: 20)
        guard !lines.isEmpty else {
            print("Traceroute failed, timed out, or produced no output.")
            return CLIExitStatus.failure.rawValue
        }
        print("\nTrace Route Hops:")
        for line in lines {
            print("  \(line)")
        }
        return CLIExitStatus.completed.rawValue
    }
    static func isSafeTraceTarget(_ target: String) -> Bool {
        if target.isEmpty || target.hasPrefix("-") || target.rangeOfCharacter(from: .whitespacesAndNewlines) != nil {
            return false
        }
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-:")
        return target.rangeOfCharacter(from: allowed.inverted) == nil
    }
}
