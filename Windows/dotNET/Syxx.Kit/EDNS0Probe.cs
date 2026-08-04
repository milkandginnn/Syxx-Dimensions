using System;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum EDNS0CapabilityStatus { Supported, NotSupported, Broken, Inconclusive, Untested }

public sealed record EDNS0Capability(
    EDNS0CapabilityStatus Status,
    ushort? UdpPayloadSize = null,
    byte? Version = null,
    string Detail = "")
{
    public static readonly EDNS0Capability Untested = new(EDNS0CapabilityStatus.Untested, Detail: "not run");
}

public static class EDNS0Probe
{
    public const ushort TypeA = 1;
    public const ushort ProbeUDPSize = 4096;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<EDNS0Capability> Probe(string domain = "cloudflare.com",
        string server = "",
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        var ednsResp = await query(new DNSQuery(name: domain, type: TypeA, server: server, edns0UDPSize: ProbeUDPSize));
        if (!DidFail(ednsResp))
        {
            if (ednsResp.Edns is EDNSResponseInfo e)
            {
                return new EDNS0Capability(EDNS0CapabilityStatus.Supported, UdpPayloadSize: e.UdpPayloadSize, Version: e.Version,
                    Detail: $"resolver echoed an EDNS0 OPT record (UDP {e.UdpPayloadSize}, version {e.Version}) — EDNS0 supported");
            }
            // B073: the additional section declared records but the OPT could not be parsed. That
            // is a malformed response, NOT a legacy resolver — EDNS support is UNKNOWN and must
            // never read as a confident "not supported".
            if (ednsResp.EdnsMalformed)
            {
                return new EDNS0Capability(EDNS0CapabilityStatus.Inconclusive,
                    Detail: "resolver answered but its additional/OPT section was malformed — EDNS0 support unknown, not merely absent");
            }
            return new EDNS0Capability(EDNS0CapabilityStatus.NotSupported,
                Detail: "resolver answered but returned no OPT record — no EDNS0 (legacy / pre-2020 flag-day resolver)");
        }
        var plainResp = await query(new DNSQuery(name: domain, type: TypeA, server: server));
        if (!DidFail(plainResp))
        {
            return new EDNS0Capability(EDNS0CapabilityStatus.Broken,
                Detail: "the EDNS0 query got no response but a plain query succeeded — OPT is being dropped on-path (middlebox); EDNS0 is broken, not merely unsupported");
        }
        return new EDNS0Capability(EDNS0CapabilityStatus.Inconclusive,
            Detail: "neither the EDNS0 nor the plain query got a usable response — resolver/path unreachable");
    }

    private static bool DidFail(DNSResponse r) =>
        r.Status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError
                 or DNSResponseStatus.MalformedResponse or DNSResponseStatus.ResponseIdMismatch;
}
