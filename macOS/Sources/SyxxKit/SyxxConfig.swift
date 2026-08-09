import Foundation

public enum SyxxConfig {
    /// Pinned telemetry cache directory. Sentry's default lives under ~/Library/Caches and varies
    /// with bundling, which makes it unsafe to delete deterministically — and deterministic
    /// deletion is exactly what consent withdrawal requires: sentry-cocoa persists unsent
    /// envelopes and would transmit them on the next start, AFTER the user opted out.
    public static var telemetryCacheDirectory: URL? {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".syxx/telemetry-cache", isDirectory: true)
    }

    /// Keygen account this build validates licenses against.
    public static let accountID = "4b50cc83-1664-4626-99b1-fbd4e76e35c3"

    /// Product version, reported by every CLI via `--version`. Keep in step with the server.
    public static let version = "2.0.0"

    /// Build environment for telemetry tagging ("production", "staging", "development").
    public static let buildEnvironment: String = {
        #if DEBUG
        return "development"
        #else
        return ProcessInfo.processInfo.environment["SYXX_ENVIRONMENT"] ?? "production"
        #endif
    }()

    /// Sentry DSN for crash/error reporting. Read from environment variable `SENTRY_DSN`
    /// or UserDefaults key `sentry_dsn`. Returns nil if not configured (Sentry disabled).
    public static var sentryDSN: String? {
        if let envDSN = ProcessInfo.processInfo.environment["SENTRY_DSN"], !envDSN.isEmpty {
            return envDSN
        }
        return UserDefaults.standard.string(forKey: "sentry_dsn")
    }

    /// Dimension server base URL (truth/diagnostics API). Overridable via the same UserDefaults key the
    /// GUI uses (`dimension_base_url`) so both clients share one host.
    public static var dimensionBaseURL: URL {
        let s = UserDefaults.standard.string(forKey: "dimension_base_url") ?? "https://syxx.app"
        return URL(string: s) ?? URL(string: "https://syxx.app")!
    }

    /// Handles the universal `--version`/`-v` and `--help`/`-h` flags for a CLI. Returns `true`
    /// if a flag was handled and the caller should exit immediately. `usage` is the tool-specific
    /// usage text shown for `--help` (pass `nil` to show just the version/name line).
    @discardableResult
    public static func handleGlobalFlags(_ arguments: [String], tool: String, usage: String? = nil) -> Bool {
        let flags = Set(arguments.dropFirst())
        if flags.contains("--version") || flags.contains("-v") {
            print("\(tool) \(version)")
            return true
        }
        if flags.contains("--help") || flags.contains("-h") {
            print("\(tool) \(version)")
            if let usage = usage {
                print("")
                print(usage)
            }
            return true
        }
        return false
    }

    /// Whether the `--verbose` flag is present in the given argument list.
    public static func isVerbose(_ arguments: [String]) -> Bool {
        arguments.contains("--verbose") || arguments.contains("-V")
    }

    /// Whether the `--json` output format flag is present in the given argument list.
    public static func isJsonOutput(_ arguments: [String]) -> Bool {
        arguments.contains("--json")
    }
}
