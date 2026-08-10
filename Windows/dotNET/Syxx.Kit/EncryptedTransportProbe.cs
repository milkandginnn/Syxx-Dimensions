using System;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum EncryptedTransportStatus { Working, Blocked, Inconclusive, Untested }

public sealed record EncryptedTransport(
    EncryptedTransportStatus Status,
    string Endpoint = "",
    string Detail = "")
{
    public static readonly EncryptedTransport Untested = new(EncryptedTransportStatus.Untested, Detail: "not run");
}

public static class EncryptedTransportProbe
{
    public const ushort TypeA = 1;
    public static readonly string DefaultEndpoint = "https://cloudflare-dns.com/dns-query";

    /// <summary>
    /// M3: DoH fallback endpoints — when the primary DoH endpoint fails, iterate through
    /// these before falling through to the cleartext control. Mirrors the macOS twin's
    /// three-endpoint array at EncryptedTransportProbe.swift:53-91.
    /// </summary>
    private static readonly string[] FallbackEndpoints = new[]
    {
        "https://cloudflare-dns.com/dns-query",
        "https://dns.google/dns-query",
        "https://dns.quad9.net/dns-query"
    };

    public static async Task<EncryptedTransport> Probe(string testName = "cloudflare.com",
        ushort qtype = TypeA,
        string? endpoint = null,
        Func<string, ushort, string, Task<DNSResponse>>? query = null,
        Func<string, ushort, Task<DNSResponse>>? control = null)
    {
        query ??= async (name, type, url) => await DNSResolver.QueryDoHDetailedAsync(name, type, url);
        // Cleartext Do53 control: proves the network path is up so a failed DoH path can be honestly
        // attributed to encrypted-DNS filtering rather than an offline host. Runs only when needed.
        control ??= async (name, type) => await DNSResolver.QueryUDPDetailedAsync(new DNSQuery(name: name, type: type));
        endpoint ??= DefaultEndpoint;

        // Try primary endpoint first.
        var resp = await query(testName, qtype, endpoint);
        if (resp.Status == DNSResponseStatus.Success && resp.Records.Count > 0)
            return Classify(resp.Status, resp.Records.Count, false, endpoint);

        // M3: Iterate through fallback endpoints before falling through to cleartext control.
        foreach (var fallback in FallbackEndpoints)
        {
            if (string.Equals(fallback, endpoint, StringComparison.OrdinalIgnoreCase))
                continue; // already tried as primary
            var fbResp = await query(testName, qtype, fallback);
            if (fbResp.Status == DNSResponseStatus.Success && fbResp.Records.Count > 0)
                return Classify(fbResp.Status, fbResp.Records.Count, false, fallback);
        }

        // All DoH endpoints failed — run cleartext control to distinguish blocking from offline.
        bool controlSucceeded = false;
        {
            var c = await control(testName, qtype);
            controlSucceeded = c.Status == DNSResponseStatus.Success && c.Records.Count > 0;
        }
        return Classify(resp.Status, resp.Records.Count, controlSucceeded, endpoint);
    }

    public static EncryptedTransport Classify(DNSResponseStatus status, int recordCount,
        bool controlSucceeded, string endpoint)
    {
        EncryptedTransport Make(EncryptedTransportStatus s, string d) =>
            new(s, Endpoint: endpoint, Detail: d);

        if (status == DNSResponseStatus.Success && recordCount > 0)
            return Make(EncryptedTransportStatus.Working, $"DoH resolution completed over TLS via {endpoint} — encrypted path works");

        if (status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError
            or DNSResponseStatus.Refused)
            // B051: a failed DoH path is Blocked ONLY when a cleartext Do53 control succeeds, proving the
            // network is up so the encrypted path is being filtered. Without that proof (the control also
            // failed → offline host or provider down) the failure is unattributable, so it stays
            // inconclusive — a missing/timed-out path must never manufacture a "blocked" finding.
            // B313: Refused is included here — the endpoint actively rejected the query, which is a
            // policy-level block, not a network error. The control-succeeded gate still applies.
            return controlSucceeded
                ? Make(EncryptedTransportStatus.Blocked, $"DoH endpoint {endpoint} unreachable — encrypted DNS is blocked/filtered on this network (DNS may fall open to plaintext)")
                : Make(EncryptedTransportStatus.Inconclusive, $"DoH endpoint {endpoint} did not complete and the cleartext control also failed, so blocking cannot be distinguished from an offline network");

        return Make(EncryptedTransportStatus.Inconclusive, $"DoH endpoint {endpoint} reached but returned {status} — encrypted path status unclear");
    }
}
