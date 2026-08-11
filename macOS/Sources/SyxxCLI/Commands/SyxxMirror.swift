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
        // D325: parse all flags first, then use remaining positional args as domain.
        // Previously `--local 8.8.8.8 example.com` parsed "--local" as the domain,
        // which failed canonicalization. Now flags are consumed first.
        guard args.count >= 2 else {
            print("Usage: syxx mirror <domain> [--local local_resolver]")
            return CLIExitStatus.failure.rawValue
        }

        // First pass: extract flags
        var localResolverOverride: String? = nil
        var positionalArgs: [String] = []
        var idx = 0
        while idx < args.count {
            let arg = args[idx]
            if (arg == "--local" || arg == "-l") && idx + 1 < args.count {
                localResolverOverride = args[idx + 1]
                idx += 2
            } else if arg.hasPrefix("-") {
                // Unknown flag — skip it
                idx += 1
            } else {
                positionalArgs.append(arg)
                idx += 1
            }
        }

        guard let rawDomain = positionalArgs.first, !rawDomain.isEmpty else {
            print("Usage: syxx mirror <domain> [--local local_resolver]")
            return CLIExitStatus.failure.rawValue
        }
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
        // D325: use the flag-parsed override instead of the old positional scan
        var localResolver = localResolverOverride ?? firstSystem
        
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
