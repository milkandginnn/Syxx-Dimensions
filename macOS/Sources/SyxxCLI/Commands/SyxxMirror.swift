import Foundation
import SyxxKit

/// B253: exit codes follow the CLI-wide contract: 0=clean, 1=threat, 2=inconclusive, 3=error.
/// Mirror detects DNS tampering, so a detected divergence is a threat (1), not just "not clean".
struct SyxxMirrorCLI {
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx mirror",
            usage: "Usage: syxx mirror <domain> [--local <resolver>]") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        guard args.count >= 2 else {
            print("Usage: syxx mirror <domain> [--local local_resolver]")
            return CLIExitStatus.failure.rawValue
        }
        
        let rawDomain = args[1]
        guard let targetDomain = try? DomainCanonical.canonicalize(rawDomain) else {
            print("Error: Invalid domain '\(rawDomain)' — could not canonicalize")
            return CLIExitStatus.failure.rawValue
        }
        // C001: never silently fall back to a guessed resolver (192.168.1.1). When no system
        // resolver is detectable, the run is inconclusive — a fabricated resolver address is not
        // a measured datum and must not be presented as one.
        let systemServers = NetworkKeyState.getSystemDNSServers()
        guard let firstSystem = systemServers.first else {
            print("⚠️ No system DNS resolvers detected. Cannot determine your local resolver.")
            print("   Use --local <resolver> to specify one, or check your network configuration.")
            return CLIExitStatus.inconclusive.rawValue
        }
        var localResolver = firstSystem
        
        var idx = 2
        while idx < args.count {
            let arg = args[idx]
            if arg == "--local" || arg == "-l" {
                if idx + 1 < args.count {
                    localResolver = args[idx + 1]
                    idx += 1
                }
            }
            idx += 1
        }
        
        print("Comparing resolution for: \(targetDomain)")
        print("Local resolver: \(localResolver)")
        
        do {
            let result = try await VantageComparer.compareVantagePoints(domain: targetDomain, localServer: localResolver)
            
            func describe(_ outcome: ResolverQueryOutcome) -> String {
                if let error = outcome.errorDescription {
                    return "Query failed: \(error)"
                }
                let rdataStrs = outcome.records.map { $0.rdata }.joined(separator: ", ")
                return rdataStrs.isEmpty ? "No A records" : rdataStrs
            }
            
            print("\nLocal records:")
            if result.localOutcome.records.isEmpty || result.localOutcome.didFail {
                print("  \(describe(result.localOutcome))")
            } else {
                for rec in result.localOutcome.records {
                    print("  \(rec.rdata) (TTL: \(rec.ttl))")
                }
            }
            
            print("\nPublic Vantage Points:")
            for name in VantageComparer.publicResolvers.keys.sorted() {
                guard let outcome = result.publicResolverOutcomes[name] else { continue }
                print("  \(name) (Resolver: \(VantageComparer.publicResolvers[name] ?? "")): [\(describe(outcome))]")
            }
            
            print("\n-------------------------------------------")
            // B253: return meaningful exit codes based on the comparison result.
            let exitCode: Int32
            if result.isInconclusive {
                print("⚠️  INCONCLUSIVE: Public vantage points did not return a usable baseline. This is not evidence that the traffic is secure.")
                exitCode = CLIExitStatus.inconclusive.rawValue
            } else if result.isRedirectionDetected {
                print("⚠️  WARNING: DNS Tampering/Redirection detected! Local results differ from independent vantage points.")
                exitCode = CLIExitStatus.threat.rawValue
            } else {
                if result.isDegraded {
                    print("⚠️  DEGRADED: No tampering detected, but one or more vantage points failed or returned no usable records.")
                    exitCode = CLIExitStatus.completed.rawValue
                } else {
                    print("✅  SUCCESS: No DNS tampering detected. Local results match public vantage points.")
                    exitCode = CLIExitStatus.completed.rawValue
                }
            }
            print("-------------------------------------------")
            return exitCode

        } catch {
            print("Error comparing vantage points: \(error.localizedDescription)")
            return CLIExitStatus.failure.rawValue
        }
    }
}
