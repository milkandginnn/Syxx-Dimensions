import Foundation
import SyxxKit
struct SyxxNSLookCLI {
    /// B253: 0=query completed, 2=no records (empty answer is not a threat), 3=error/usage.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx nslook",
            usage: "Usage: syxx nslook <domain> [record_type] [@server] [--dnssec] [--doh [url]]") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        guard args.count >= 2 else {
            printUsage()
            return CLIExitStatus.failure.rawValue
        }
        
        let target = args[1]
        var typeStr = "A"
        var server = "8.8.8.8"
        var useDoH = false
        var dohURLStr = "https://cloudflare-dns.com/dns-query"
        var validateDNSSEC = false
        
        // Parse arguments
        var idx = 2
        while idx < args.count {
            let arg = args[idx]
            if arg == "--dnssec" {
                validateDNSSEC = true
            } else if arg == "--doh" {
                useDoH = true
                if idx + 1 < args.count {
                    dohURLStr = args[idx + 1]
                    idx += 1
                }
            } else if arg.hasPrefix("@") {
                server = String(arg.dropFirst())
            } else if !arg.hasPrefix("-") {
                typeStr = arg
            }
            idx += 1
        }
        
        let typeMap: [String: UInt16] = [
            "A": 1, "NS": 2, "CNAME": 5, "SOA": 6, "PTR": 12, "MX": 15, "TXT": 16,
            "AAAA": 28, "SRV": 33, "NAPTR": 35, "DS": 43, "RRSIG": 46, "DNSKEY": 48,
            "NSEC": 47, "TLSA": 52, "SVCB": 64, "HTTPS": 65, "CAA": 257
        ]
        let queryType: UInt16
        if let mapped = typeMap[typeStr.uppercased()] {
            queryType = mapped
        } else if let numeric = UInt16(typeStr) {
            // Accept a raw numeric type code for anything not in the table.
            queryType = numeric
        } else {
            // Never silently fall back to A — that would query a different record type than the
            // user asked for and mask tampering in the requested type (MAC-111).
            print("Error: Unsupported record type '\(typeStr)'. Supported: \(typeMap.keys.sorted().joined(separator: ", ")), or a numeric type code.")
            return CLIExitStatus.failure.rawValue
        }
        print("Querying \(typeStr) records for \(target)...")
        
        do {
            let records: [DNSRecord]
            if useDoH {
                guard let url = URL(string: dohURLStr) else {
                    print("Error: Invalid DoH URL \(dohURLStr)")
                    return CLIExitStatus.failure.rawValue
                }
                print("Using DoH server: \(url.absoluteString)")
                records = try await DNSResolver.queryDoH(name: target, type: queryType, url: url)
            } else {
                print("Using UDP resolver: \(server)")
                let query = DNSQuery(name: target, type: queryType, server: server)
                records = try await DNSResolver.queryUDP(query)
            }
            
            if records.isEmpty {
                print("No records found.")
            } else {
                for rec in records {
                    print("\(rec.name)\tTTL: \(rec.ttl)\tType: \(rec.type)\tData: \(rec.rdata)")
                }
            }
            
            if validateDNSSEC {
                print("\nChecking DNSSEC record status...")
                let status = await DNSSECVerifier.checkStatus(domain: target, resolver: server)
                print("DNSSEC Status: \(status.description)")
            }
            // Empty answer is a completed query, not a threat and not an operational failure.
            return CLIExitStatus.completed.rawValue
            
        } catch {
            print("DNS Query failed: \(error.localizedDescription)")
            return CLIExitStatus.failure.rawValue
        }
    }
    
    static func printUsage() {
        print("""
        Usage: syxx nslook <domain> [record_type] [@server] [options]
        
        Record Types:
          A, AAAA, CNAME, MX, TXT, NS, DNSKEY, RRSIG, DS
        
        Options:
          @server          Specify target resolver IP (e.g. @1.1.1.1)
          --dnssec         Query and display DNSSEC status
          --doh [url]      Use DNS-over-HTTPS (DoH) resolver
        
        Examples:
          syxx nslook google.com
          syxx nslook cloudflare.com AAAA @1.1.1.1 --dnssec
          syxx nslook apple.com A --doh https://dns.google/dns-query
        """)
    }
}
