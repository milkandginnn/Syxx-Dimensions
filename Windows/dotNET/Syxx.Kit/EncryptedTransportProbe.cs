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

    public static async Task<EncryptedTransport> Probe(string testName = "cloudflare.com",
        ushort qtype = TypeA,
        string endpoint = null,
        Func<string, ushort, string, Task<DNSResponse>>? query = null,
        Func<string, ushort, Task<DNSResponse>>? control = null)
    {
        query ??= async (name, type, url) => await DNSResolver.QueryDoHDetailedAsync(name, type, url);
        // Cleartext Do53 control: proves the network path is up so a failed DoH path can be honestly
        // attributed to encrypted-DNS filtering rather than an offline host. Runs only when needed.
        control ??= async (name, type) => await DNSResolver.QueryUDPDetailedAsync(new DNSQuery(name: name, type: type));
        endpoint ??= DefaultEndpoint;
        var resp = await query(testName, qtype, endpoint);
        bool controlSucceeded = false;
        if (!(resp.Status == DNSResponseStatus.Success && resp.Records.Count > 0))
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

        if (status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError)
            // B051: a failed DoH path is Blocked ONLY when a cleartext Do53 control succeeds, proving the
            // network is up so the encrypted path is being filtered. Without that proof (the control also
            // failed → offline host or provider down) the failure is unattributable, so it stays
            // inconclusive — a missing/timed-out path must never manufacture a "blocked" finding.
            return controlSucceeded
                ? Make(EncryptedTransportStatus.Blocked, $"DoH endpoint {endpoint} unreachable — encrypted DNS is blocked/filtered on this network (DNS may fall open to plaintext)")
                : Make(EncryptedTransportStatus.Inconclusive, $"DoH endpoint {endpoint} did not complete and the cleartext control also failed, so blocking cannot be distinguished from an offline network");

        return Make(EncryptedTransportStatus.Inconclusive, $"DoH endpoint {endpoint} reached but returned {status} — encrypted path status unclear");
    }
}
