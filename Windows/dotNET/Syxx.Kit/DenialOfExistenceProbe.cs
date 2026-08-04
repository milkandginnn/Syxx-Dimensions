using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum DenialStatus { NotADenial, NsecProven, SoaBacked, BareDenial, Contradictory, Inconclusive, Untested }

public sealed record DenialOfExistence(
    DenialStatus Status,
    string QueriedName = "",
    string? SoaOwner = null,
    bool HasNSEC = false,
    string Detail = "")
{
    public static readonly DenialOfExistence Untested = new(DenialStatus.Untested, Detail: "not run");
}

public static class DenialOfExistenceProbe
{
    public const ushort TypeSOA = 6;
    public const ushort TypeNSEC = 47;
    public const ushort TypeNSEC3 = 50;

    public static Func<DNSQuery, Task<DNSResponse>> DefaultQuery =>
        async q => await DNSResolver.QueryUDPDetailedAsync(q);

    public static async Task<DenialOfExistence> Probe(string name,
        ushort qtype = 1,
        string server = "1.1.1.1",
        Func<DNSQuery, Task<DNSResponse>>? query = null)
    {
        query ??= DefaultQuery;
        var resp = await query(new DNSQuery(name: name, type: qtype, server: server, port: 53));
        return Classify(name, resp.Status,
            resp.Records.Where(r => r.Type == qtype).ToList(),
            resp.AuthorityRecords,
            resp.AuthorityMalformed);
    }

    public static DenialOfExistence Classify(string name, DNSResponseStatus status,
        List<DNSRecord> answers, List<DNSRecord> authority, bool authorityMalformed = false)
    {
        var soa = authority.FirstOrDefault(r => r.Type == TypeSOA);
        bool hasNSEC = authority.Any(r => r.Type == TypeNSEC || r.Type == TypeNSEC3);

        DenialOfExistence Make(DenialStatus s, string d) =>
            new(s, QueriedName: name, SoaOwner: soa?.Name, HasNSEC: hasNSEC, Detail: d);

        switch (status)
        {
            case DNSResponseStatus.Success when answers.Count > 0:
                return Make(DenialStatus.NotADenial, $"{name} resolved — denial-of-existence check does not apply");
            case DNSResponseStatus.NXDomain:
            case DNSResponseStatus.NoData:
            case DNSResponseStatus.Success:
                break;
            default:
                return Make(DenialStatus.Inconclusive, $"no usable answer for {name} ({status}) — cannot grade the denial");
        }

        if (answers.Count > 0)
            return Make(DenialStatus.Contradictory, $"negative status yet {answers.Count} answer record(s) present — inconsistent, likely injected");

        if (hasNSEC)
            return Make(DenialStatus.NsecProven, "negative accompanied by NSEC/NSEC3 — authenticated denial of existence");

        if (soa is not null)
        {
            if (IsAncestor(soa.Name, name))
                return Make(DenialStatus.SoaBacked, $"negative backed by SOA for {soa.Name} — proper authoritative negative");
            return Make(DenialStatus.Contradictory, $"SOA owner {soa.Name} is not an ancestor of {name} — denial from the wrong zone, likely injected");
        }

        // B071: a declared-but-unparseable authority section means the SOA may exist but be
        // unreadable — that is a corrupt response, not evidence of a synthesized bare denial.
        // Malformed must never grade as the BareDenial positive.
        if (authorityMalformed)
            return Make(DenialStatus.Inconclusive, $"negative for {name} has an authority section that could not be parsed — cannot distinguish a bare denial from a corrupted response");

        return Make(DenialStatus.BareDenial, $"negative for {name} carries no SOA and no NSEC — bare denial (a genuine authoritative negative includes an SOA); corroborate");
    }

    public static bool IsAncestor(string zone, string name)
    {
        var z = Normalize(zone);
        var n = Normalize(name);
        if (z.Length == 0) return true;
        return n == z || n.EndsWith("." + z, StringComparison.Ordinal);
    }

    private static string Normalize(string s)
    {
        var v = s.Trim().ToLowerInvariant();
        if (v.EndsWith(".", StringComparison.Ordinal)) v = v[..^1];
        return v;
    }
}
