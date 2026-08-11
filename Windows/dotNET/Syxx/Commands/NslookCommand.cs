using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx nslook` — DNS lookup. Body moved verbatim from the former SyxxNSLook tool;
    /// argv is the arguments AFTER the subcommand token, so args[0] is the first real argument.</summary>
    internal static class NslookCommand
    {
        public static async Task RunAsync(string[] args)
        {
            // Windows consoles default to the legacy OEM code page, which renders the
            // report glyphs as "??". Force UTF-8 so output matches the macOS CLI.
            // E012: emit a diagnostic when UTF-8 encoding fails on legacy console hosts so
            // mojibake in evidence output (IDN domains, dashes, arrows) is explainable.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warning] Console UTF-8 encoding unavailable: {ex.Message}");
            }

            if (SyxxConfig.HandleGlobalFlags(args, "syxx nslook",
                "Usage: syxx nslook <domain> [record_type] [@server] [--dnssec] [--doh [url]]")) return;

            if (args.Length < 1)
            {
                PrintUsage();
                return;
            }

            string target = args[0];
            string typeStr = "A";
            string server = "8.8.8.8";
            bool useDoH = false;
            string dohUrl = "https://cloudflare-dns.com/dns-query";
            bool validateDNSSEC = false;

            int idx = 1;
            while (idx < args.Length)
            {
                string arg = args[idx];
                if (arg == "--dnssec")
                {
                    validateDNSSEC = true;
                }
                else if (arg == "--doh")
                {
                    useDoH = true;
                    if (idx + 1 < args.Length)
                    {
                        dohUrl = args[idx + 1];
                        idx++;
                    }
                }
                else if (arg.StartsWith("@"))
                {
                    server = arg.Substring(1);
                }
                else if (!arg.StartsWith("-"))
                {
                    typeStr = arg;
                }
                idx++;
            }

            var typeMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = 1,
                ["NS"] = 2,
                ["CNAME"] = 5,
                ["SOA"] = 6,
                ["PTR"] = 12,
                ["MX"] = 15,
                ["TXT"] = 16,
                ["AAAA"] = 28,
                ["SRV"] = 33,
                ["NAPTR"] = 35,
                ["DS"] = 43,
                ["RRSIG"] = 46,
                ["DNSKEY"] = 48,
                ["NSEC"] = 47,
                ["TLSA"] = 52,
                ["SVCB"] = 64,
                ["HTTPS"] = 65,
                ["CAA"] = 257
            };

            ushort queryType;
            if (typeMap.TryGetValue(typeStr, out var mapped))
            {
                queryType = mapped;
            }
            else if (ushort.TryParse(typeStr, out var numeric))
            {
                // Accept a raw numeric type code for anything not in the table.
                queryType = numeric;
            }
            else
            {
                // Never silently fall back to A — that would query a different record type than the
                // user asked for and mask tampering in the requested type (WIN-111 / MAC-111).
                var supported = new List<string>(typeMap.Keys);
                supported.Sort(StringComparer.Ordinal);
                Console.WriteLine($"Error: Unsupported record type '{typeStr}'. Supported: {string.Join(", ", supported)}, or a numeric type code.");
                return;
            }

            Console.WriteLine($"Querying {typeStr} records for {target}...");

            try
            {
                List<DNSRecord> records;
                if (useDoH)
                {
                    Console.WriteLine($"Using DoH server: {dohUrl}");
                    records = await DNSResolver.QueryDoHAsync(target, queryType, dohUrl);
                }
                else
                {
                    Console.WriteLine($"Using UDP resolver: {server}");
                    var query = new DNSQuery(target, queryType, server);
                    records = await DNSResolver.QueryUDPAsync(query);
                }

                if (records.Count == 0)
                {
                    Console.WriteLine("No records found.");
                }
                else
                {
                    foreach (var rec in records)
                    {
                        Console.WriteLine($"{rec.Name}\tTTL: {rec.Ttl}\tType: {rec.Type}\tData: {rec.Rdata}");
                    }
                }

                if (validateDNSSEC)
                {
                    Console.WriteLine("\nChecking DNSSEC record status...");
                    var status = await DNSSECVerifier.CheckStatusAsync(target, server);
                    Console.WriteLine($"DNSSEC Status: {DNSSECVerifier.Describe(status)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DNS Query failed: {ex.Message}");
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine(@"Usage: syxx nslook <domain> [record_type] [@server] [options]

Record Types:
  A, AAAA, CNAME, MX, TXT, NS, DNSKEY, RRSIG, DS

Options:
  @server          Specify target resolver IP (e.g. @1.1.1.1)
  --dnssec         Query and display DNSSEC status
  --doh [url]      Use DNS-over-HTTPS (DoH) resolver

Examples:
  syxx nslook google.com
  syxx nslook cloudflare.com AAAA @1.1.1.1 --dnssec
  syxx nslook apple.com A --doh https://dns.google/dns-query");
        }
    }
}
