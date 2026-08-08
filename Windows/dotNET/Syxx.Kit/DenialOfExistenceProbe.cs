using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

public enum DenialStatus { NotADenial, NsecProven, SoaBacked, BareDenial, Contradictory, Inconclusive, Untested }

/// <summary>
/// C127: which kind of negative answer this is — NXDOMAIN (the name itself does not exist) or
/// NODATA (the name exists but carries no record of the requested type). RFC 2308 §2.1/§2.2 make
/// these different facts, and the absence of an SOA means different things for each: on an NXDOMAIN
/// it is a synthesis signature, on a NODATA it is routine, because §5 makes the SOA a SHOULD for
/// authoritative answers only and many resolvers omit it on a cached NODATA.
/// </summary>
/// <remarks>
/// Twin of the nested macOS type <c>DenialOfExistence.NegativeKind</c> (Swift rawValues
/// <c>nameDoesNotExist</c> / <c>typeHasNoData</c>). Named with the <c>Denial</c> prefix here only
/// because C# has no nested-type namespace to hide behind; the PROPERTY is <c>NegativeKind</c>, which
/// is the name that has to match. Engine-internal, like <see cref="DenialStatus"/> — neither has a
/// C# wire representation today, so no JSON converter is introduced.
/// </remarks>
public enum DenialNegativeKind { NameDoesNotExist, TypeHasNoData }

public sealed record DenialOfExistence(
    DenialStatus Status,
    string QueriedName = "",
    string? SoaOwner = null,
    bool HasNSEC = false,
    DenialNegativeKind? NegativeKind = null,
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

        // C127: read the kind of negative off the response status before grading it.
        DenialNegativeKind? kind = status switch
        {
            DNSResponseStatus.NXDomain => DenialNegativeKind.NameDoesNotExist,
            DNSResponseStatus.NoData or DNSResponseStatus.Success => DenialNegativeKind.TypeHasNoData,
            _ => null
        };

        DenialOfExistence Make(DenialStatus s, string d) =>
            new(s, QueriedName: name, SoaOwner: soa?.Name, HasNSEC: hasNSEC, NegativeKind: kind, Detail: d);

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

        // C126: evaluate SOA ancestry FIRST. An SOA outside the queried name's hierarchy must stay
        // Contradictory regardless of hasNSEC. NSEC presence may then upgrade a *consistent* denial;
        // it must never rescue an inconsistent one. Previously hasNSEC short-circuited above this
        // check, so attaching any NSEC/NSEC3 record — which an on-path injector can do freely, since
        // nothing here validates the NSEC interval or its RRSIG — bypassed the only real test and
        // reported an injected denial as an authenticated one.
        // B312: normalize SOA name for consistent display — IsAncestor normalizes internally
        // but the detail string must use the same normalized form to avoid casing mismatch.
        var soaNameNormalized = soa?.Name.ToLowerInvariant() ?? "";
        if (soa is not null && !IsAncestor(soa.Name, name))
            return Make(DenialStatus.Contradictory, $"SOA owner {soaNameNormalized} is not an ancestor of {name} — denial from the wrong zone, likely injected");

        if (hasNSEC)
            return Make(DenialStatus.NsecProven, "negative accompanied by NSEC/NSEC3 — authenticated denial of existence");

        if (soa is not null)
            return Make(DenialStatus.SoaBacked, $"negative backed by SOA for {soaNameNormalized} — proper authoritative negative");

        // B071: a declared-but-unparseable authority section means the SOA may exist but be
        // unreadable — that is a corrupt response, not evidence of a synthesized bare denial.
        // Malformed must never grade as the BareDenial positive.
        if (authorityMalformed)
            return Make(DenialStatus.Inconclusive, $"negative for {name} has an authority section that could not be parsed — cannot distinguish a bare denial from a corrupted response");

        // C127: the "likely injected" bare-denial framing applies to NXDOMAIN only. A genuine
        // authoritative NXDOMAIN carries an SOA, so its absence is a synthesis signature. A NODATA
        // with no SOA is not: RFC 2308 §5 makes the SOA a SHOULD for authoritative answers only, and
        // resolvers routinely omit it on a cached NODATA. Grading those as suspicious was a false
        // positive on ordinary traffic.
        if (kind == DenialNegativeKind.NameDoesNotExist)
            return Make(DenialStatus.BareDenial, $"NXDOMAIN for {name} carries no SOA and no NSEC — bare denial (a genuine authoritative NXDOMAIN includes an SOA); corroborate");

        return Make(DenialStatus.Inconclusive, $"NODATA for {name} carries no SOA and no NSEC — no records of the requested type exist; a missing SOA on a type-negative is not evidence of synthesis");
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
