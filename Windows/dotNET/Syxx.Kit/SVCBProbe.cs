using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum SVCBFindingStatus { Present, Absent, Divergent, Inconclusive, Untested }

public sealed record SVCBFinding(
    SVCBFindingStatus Status,
    IReadOnlyList<string>? PresentAt = null,
    IReadOnlyList<string>? AbsentAt = null,
    IReadOnlyList<string>? FailedAt = null,
    string Detail = "")
{
    public static readonly SVCBFinding Untested = new(SVCBFindingStatus.Untested, Detail: "not run");
}

public static class SVCBProbe
{
    public const ushort TypeHTTPS = 65;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<SVCBFinding> Probe(string domain,
        IReadOnlyList<string>? resolvers = null,
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        resolvers ??= ["1.1.1.1", "8.8.8.8", "9.9.9.9"];
        var present = new List<string>();
        var absent = new List<string>();
        var failed = new List<string>();
        var recordSets = new Dictionary<string, List<string>>();
        foreach (var server in resolvers)
        {
            var resp = await query(new DNSQuery(name: domain, type: TypeHTTPS, server: server, port: 53));
            if (resp.Status == DNSResponseStatus.Success && resp.Records.Any(r => r.Type == TypeHTTPS))
            {
                present.Add(server);
                // C018: canonical per-record descriptors so parameter stripping is comparable.
                recordSets[server] = resp.Records.Where(r => r.Type == TypeHTTPS)
                    .Select(r => CanonicalizeSvcbRdata(r.Rdata) ?? $"unparsed:{r.Rdata}")
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
            }
            else if (resp.Status is DNSResponseStatus.Success or DNSResponseStatus.NoData or DNSResponseStatus.NXDomain)
                absent.Add(server);
            else
                failed.Add(server);
        }
        return Classify(present, absent, failed, recordSets);
    }

    public static SVCBFinding Classify(List<string> present, List<string> absent, List<string> failed,
        Dictionary<string, List<string>>? recordSetsByServer = null)
    {
        SVCBFinding Make(SVCBFindingStatus s, string d) =>
            new(s, PresentAt: [.. present.OrderBy(x => x, StringComparer.Ordinal)], AbsentAt: [.. absent.OrderBy(x => x, StringComparer.Ordinal)],
                FailedAt: [.. failed.OrderBy(x => x, StringComparer.Ordinal)], Detail: d);

        if (present.Count + absent.Count < 1)
            return Make(SVCBFindingStatus.Inconclusive, "no resolver returned a usable HTTPS-record answer");

        if (present.Count > 0 && absent.Count > 0)
            return Make(SVCBFindingStatus.Divergent, $"{present.Count} resolver(s) serve the HTTPS record, {absent.Count} return none — possible SVCB/HTTPS stripping (corroborate)");

        if (present.Count > 0)
        {
            // C018: presence alone is not integrity. A vantage can keep the type-65 RR while
            // stripping ECH/ALPN/port parameters — compare the canonical record SETS too.
            if (recordSetsByServer is not null && present.Count > 1)
            {
                var canons = present
                    .Where(recordSetsByServer.ContainsKey)
                    .Select(s => string.Join("\n", recordSetsByServer[s]))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (canons.Count > 1)
                    return Make(SVCBFindingStatus.Divergent,
                        "resolvers all serve an HTTPS record but with DIFFERENT priority/target/parameter sets — possible SVCB parameter stripping (e.g. ECH/ALPN removed); corroborate");
            }
            return Make(SVCBFindingStatus.Present, "all responding resolvers serve the HTTPS record — consistent");
        }

        return Make(SVCBFindingStatus.Absent, "all responding resolvers report no HTTPS record — domain likely has none");
    }

    /// <summary>
    /// C018: canonicalize an HTTPS/SVCB record's RDATA hex into a comparable descriptor:
    /// "prio=&lt;n&gt;;target=&lt;name&gt;;&lt;key&gt;=&lt;value-hex&gt;;…" with parameter keys in ascending order
    /// (RFC 9460 wire order is already ascending; sorting makes the comparison robust anyway).
    /// The target name inside SVCB RDATA is REQUIRED to be uncompressed (RFC 9460 §2.2), so the
    /// record-local hex is parseable standalone; a compression pointer or truncated field is
    /// malformed and returns null (caller surfaces it as unparsed, never silently equal).
    /// </summary>
    internal static string? CanonicalizeSvcbRdata(string rdataHex)
    {
        if (string.IsNullOrEmpty(rdataHex) || rdataHex.Length % 2 != 0) return null;
        byte[] bytes;
        try { bytes = Convert.FromHexString(rdataHex); }
        catch (FormatException) { return null; }
        if (bytes.Length < 3) return null;

        int priority = (bytes[0] << 8) | bytes[1];
        int off = 2;

        // Uncompressed wire-format target name.
        var labels = new List<string>();
        while (true)
        {
            if (off >= bytes.Length) return null;
            byte len = bytes[off];
            if (len == 0) { off++; break; }
            if ((len & 0xC0) != 0) return null;   // compression forbidden in SVCB RDATA
            if (off + 1 + len > bytes.Length) return null;
            labels.Add(System.Text.Encoding.ASCII.GetString(bytes, off + 1, len).ToLowerInvariant());
            off += 1 + len;
        }
        string target = labels.Count == 0 ? "." : string.Join(".", labels);

        // SvcParams: key(2) length(2) value(length), keys strictly ascending per RFC 9460.
        var svcParams = new SortedDictionary<int, string>();
        while (off < bytes.Length)
        {
            if (off + 4 > bytes.Length) return null;
            int key = (bytes[off] << 8) | bytes[off + 1];
            int len = (bytes[off + 2] << 8) | bytes[off + 3];
            off += 4;
            if (off + len > bytes.Length) return null;
            svcParams[key] = Convert.ToHexString(bytes, off, len).ToLowerInvariant();
            off += len;
        }

        var parts = new List<string> { $"prio={priority}", $"target={target}" };
        parts.AddRange(svcParams.Select(kv => $"{kv.Key}={kv.Value}"));
        return string.Join(";", parts);
    }
}
