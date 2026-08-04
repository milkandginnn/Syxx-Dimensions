import Foundation
import SyxxKit
struct SyxxLeakCLI {
    /// B253: exit codes follow the CLI-wide contract (parity with Windows LeakCommand):
    /// 0=no leak, 1=leak confirmed, 2=inconclusive, 3=operational error.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx leak",
            usage: "Usage: syxx leak [--domain <base>] [--servers <ip...>] [--collector <url>] [--token <bearer>] [--key <license>]") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        var baseDomain = "leak.syxx.app"
        var servers = NetworkKeyState.getSystemDNSServers()
        var collectorURL: URL? = nil
        var authToken: String? = nil
        var licenseKey: String? = nil
        var idx = 1
        while idx < args.count {
            let arg = args[idx]
            if arg == "--domain" || arg == "-d" {
                if idx + 1 < args.count {
                    baseDomain = args[idx + 1]
                    idx += 1
                }
            } else if arg == "--servers" || arg == "-s" {
                servers.removeAll()
                while idx + 1 < args.count && !args[idx + 1].hasPrefix("-") {
                    servers.append(args[idx + 1])
                    idx += 1
                }
            } else if arg == "--collector" || arg == "-c" {
                if idx + 1 < args.count {
                    collectorURL = URL(string: args[idx + 1])
                    idx += 1
                }
            } else if arg == "--token" || arg == "-t" {
                if idx + 1 < args.count {
                    authToken = args[idx + 1]
                    idx += 1
                }
            } else if arg == "--key" || arg == "-k" {
                if idx + 1 < args.count {
                    licenseKey = args[idx + 1]
                    idx += 1
                }
            }
            idx += 1
        }
        // Accept secrets from the environment so they need not appear in argv (which is visible
        // via `ps` to any local process). Explicit flags still win; env is the recommended path.
        let env = ProcessInfo.processInfo.environment
        if authToken == nil { authToken = env["SYXX_BEARER_TOKEN"] }
        if licenseKey == nil { licenseKey = env["SYXX_LICENSE_KEY"] }
        if servers.isEmpty {
            print("INCONCLUSIVE: No system DNS resolvers were detected. Refusing to fall back to public resolvers for a leak test.")
            return CLIExitStatus.inconclusive.rawValue
        }
        // Collector-confirmed path.
        if let url = collectorURL {
            // Paid route: a bearer session token or a license key is required. Fall back
            // to the license key already stored by activation if neither flag was given.
            if authToken == nil && licenseKey == nil {
                licenseKey = LicenseManager.getStoredLicenseKey()
                if licenseKey != nil {
                    print("Using stored Syxx Pro license key for collector authentication.")
                }
            }
            if authToken == nil && licenseKey == nil {
                print("ERROR: The collector API is a paid route. Pass --token <bearer-token> or --key <license-key> (or activate a license first).")
                return CLIExitStatus.failure.rawValue
            }
            print("Running collector-confirmed leak test via \(url.absoluteString) ...")
            print("Base domain : \(baseDomain)")
            print("Resolvers   : \(servers.joined(separator: ", "))")
            let result = await LeakDetector.runCollectorTest(
                baseDomain: baseDomain,
                servers: servers,
                collectorBaseURL: url,
                authToken: authToken,
                licenseKey: licenseKey
            )
            // B242: (1) .confirmed prints LEAK wording — the headline signal was previously invisible.
            // (2) .notDetected prints the real count (was hardcoded "1") and handles 0 explicitly.
            let exitCode: Int32
            switch result.result {
            case .confirmed:
                print("\n🚨 DNS LEAK CONFIRMED — \(result.observedResolvers.count) resolver(s) observed outside your expected path")
                if let reason = result.errorReason { print("  Detail: \(reason)") }
                exitCode = CLIExitStatus.threat.rawValue
            case .notDetected:
                if result.observedResolvers.count == 0 {
                    print("\nNo resolver was observed by the collector — verify your network path")
                } else {
                    print("\n\(result.observedResolvers.count) resolver(s) observed — verify they match your provider")
                }
                exitCode = CLIExitStatus.completed.rawValue
            case .inconclusive:
                print("\nINCONCLUSIVE")
                exitCode = CLIExitStatus.inconclusive.rawValue
            case .invalid:
                print("\nINVALID CONCLUSION")
                exitCode = CLIExitStatus.inconclusive.rawValue
            case .error:
                print("\nEXECUTION ERROR")
                exitCode = CLIExitStatus.failure.rawValue
            }
            if !result.observedResolvers.isEmpty {
                print("Observed resolvers (collector-attested):")
                for r in result.observedResolvers {
                    print("  \(r)")
                }
            }
            if let sig = result.signature {
                print("Collector receipt (Ed25519-signed attestation): \(sig)")
                // B186: four typed states — never collapse nil onto "INVALID", never green without key trust.
                if let valid = result.signatureValid {
                    if valid {
                        if let trusted = result.signatureKeyTrusted {
                            if trusted {
                                print("  [VALID] Ed25519 signature valid · signer trusted")
                            } else {
                                print("  [WARNING] Ed25519 signature valid · signer NOT trusted")
                            }
                        } else {
                            print("  Ed25519 signature valid · signer trust not configured")
                        }
                    } else {
                        print("  [BLOCKED] Ed25519 signature INVALID — collector attestation cannot be verified")
                    }
                } else {
                    print("  Signature not verified — no key supplied")
                }
                // B186: surface transport pinning so unpinned is distinguishable from pinned.
                print("  Transport: \(result.transportPinning.rawValue)")
            } else {
                print("WARNING: No collector receipt returned — this result is not attributable evidence.")
            }
            if let reason = result.errorReason {
                print("Reason: \(reason)")
            }
            return exitCode
        }
        // Local-only path (no collector).
        let target = LeakDetector.generateTestSubdomain(baseDomain: baseDomain)
        print("Generated leak test subdomain: \(target)")
        print("Querying upstream DNS servers: \(servers.joined(separator: ", "))")
        let results = await LeakDetector.triggerQueries(subdomain: target, servers: servers)
        print("\nQuery Trigger Results:")
        for (server, success) in results {
            print("  \(server): \(success ? "PROBE SENT" : "FAILED / UNREACHABLE")")
        }
        // Only conclude from what actually happened: if no probe reached a resolver, the test did not
        // run — never imply a dispatched state the premises contradict.
        if results.contains(where: { $0.1 }) {
            print("\nINCONCLUSIVE (no collector): DNS resolver probes were dispatched but collector-side confirmation was not requested. Pass --collector <URL> with --token <bearer-token> or --key <license-key> for a verified result.")
            return CLIExitStatus.inconclusive.rawValue
        } else {
            print("\nINCONCLUSIVE — all probe dispatches FAILED, so the leak test did not run and nothing was measured. This is NOT a clean result. Cause: your system resolvers are unreachable (no connectivity, a VPN/firewall blocking DNS, or a captive portal). Restore DNS connectivity and re-run; add --collector <URL> --key <license> for a verified result.")
            // Windows twin: all probes failed is operational failure (3), not threat (1).
            return CLIExitStatus.failure.rawValue
        }
    }
}
