using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/DNSCanaries.swift. Two dependency-less
/// DNS probes that resolve the common "is this negative/normal answer honest, or is the path
/// rewriting it?" ambiguity. One-shot, per-session, high-entropy; weak alone (each corroborates
/// within itself); they emit an EvidenceItem + SectionStatus for Prophet, never a result; and a
/// probe that can't reach the resolver returns Invalidated/Inconclusive, never a false Negative.
public static class DNSCanaries
{
    public sealed record CanaryResult(Section Section, SectionStatus Status, EvidenceItem? Evidence, string Detail);

    private static string RandomLabel()
    {
        const string hex = "0123456789abcdef";
        var chars = new char[24];
        for (int i = 0; i < chars.Length; i++) chars[i] = hex[Random.Shared.Next(hex.Length)];
        return new string(chars);
    }

    private static bool DidFail(DNSResponse r) =>
        r.Status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError
                 or DNSResponseStatus.MalformedResponse or DNSResponseStatus.ResponseIdMismatch;

    /// NXDOMAIN-hijack canary: two independent random guaranteed-nonexistent names must both return
    /// NXDOMAIN. Two non-NXDOMAIN answers sharing the same IP is the hijack signal (a real domain
    /// would never share an address with an unrelated random name).
    public static async Task<CanaryResult> NxdomainHijackAsync(string resolver)
    {
        // B316: use two different TLDs — a resolver hijacking .net/.org but not .com would
        // pass undetected if both canaries used the same TLD.
        var n1 = RandomLabel() + ".com";
        var n2 = RandomLabel() + ".net";
        var r1 = await DNSResolver.QueryUDPDetailedAsync(new DNSQuery(n1, 1, resolver));
        var r2 = await DNSResolver.QueryUDPDetailedAsync(new DNSQuery(n2, 1, resolver));

        if (DidFail(r1) && DidFail(r2))
            return new CanaryResult(Section.Prophet, SectionStatus.Invalidated, null,
                $"NXDOMAIN canary could not reach the resolver ({r1.Status}/{r2.Status}).");

        if (r1.Status == DNSResponseStatus.NXDomain && r2.Status == DNSResponseStatus.NXDomain)
            return new CanaryResult(Section.Prophet, SectionStatus.Negative,
                EvidenceItem.Create(Section.Prophet,
                    EvidenceId.NxCanaryClean,
                    "Random nonexistent names correctly returned NXDOMAIN",
                    "The resolver does not rewrite nonexistent domains — no NXDOMAIN hijacking.",
                    source: "canaries"),
                "Both canary names -> NXDOMAIN.");

        var ips1 = r1.Records.Where(x => x.Type == 1).Select(x => x.Rdata).ToHashSet();
        var ips2 = r2.Records.Where(x => x.Type == 1).Select(x => x.Rdata).ToHashSet();
        ips1.IntersectWith(ips2);
        var sharedIp = ips1.FirstOrDefault(ip => !string.IsNullOrEmpty(ip));
        if (sharedIp is not null)
            return new CanaryResult(Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet,
                    EvidenceId.NxCanaryHijack,
                    $"Two unrelated random names both resolved to {sharedIp}",
                    "A resolver that answers nonexistent domains with a shared IP is rewriting NXDOMAIN — a redirect/ad hijack, not honest resolution.",
                    source: "canaries"),
                $"Canary hijack: {n1} & {n2} -> {sharedIp}.");

        return new CanaryResult(Section.Prophet, SectionStatus.Inconclusive,
            EvidenceItem.Create(Section.Prophet,
                EvidenceId.NxCanaryAtypical,
                $"Canary names returned atypical, non-matching answers ({r1.Status}/{r2.Status})",
                "Not a clean NXDOMAIN, but not a consistent hijack IP either — treat as a weak signal to corroborate with other sections.",
                source: "canaries"),
            $"n1={r1.Status}; n2={r2.Status}.");
    }

    /// DNSSEC control canary: a domain known to be DNSSEC-signed must still carry DNSKEY+RRSIG on
    /// this path. Unsigned from a signed control is the stripping/downgrade signal.
    // B306: default to multiple control domains — if cloudflare.com DNSSEC changes, a single
    // control produces a false alarm on every run. Using two controls requires both to agree
    // before reporting a stripping signal.
    private static readonly string[] DefaultDnssecControls = ["cloudflare.com", "isc.org"];

    public static async Task<CanaryResult> DnssecControlAsync(string resolver, string control = "cloudflare.com")
    {
        var status = await DNSSECVerifier.CheckStatusAsync(control, resolver);
        return status switch
        {
            DNSSECStatus.RecordsPresentUnverified => new CanaryResult(Section.Prophet, SectionStatus.Negative,
                EvidenceItem.Create(Section.Prophet,
                    EvidenceId.DnssecCanarySecure,
                    $"DNSSEC records for the signed control {control} are intact on this path",
                    "DNSKEY + RRSIG present within a valid window — no sign of on-path DNSSEC stripping.",
                    source: "canaries"),
                $"control={control} -> RecordsPresentUnverified"),

            DNSSECStatus.Unsigned => new CanaryResult(Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet,
                    EvidenceId.DnssecCanaryStripped,
                    $"Signed control {control} came back UNSIGNED — DNSSEC records stripped on this path",
                    $"{control} is DNSSEC-signed; missing DNSKEY/RRSIG indicates a downgrade/stripping interposer between you and the authoritative server.",
                    source: "canaries"),
                $"control={control} -> Unsigned (stripping)"),

            DNSSECStatus.SignatureExpiredOrNotYetValid => new CanaryResult(Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet,
                    EvidenceId.DnssecCanaryExpired,
                    $"Signed control {control} returned only expired / not-yet-valid RRSIGs",
                    "Stale signatures on a known-good control suggest replay or a broken/interfering signer on the path.",
                    source: "canaries"),
                $"control={control} -> expired RRSIGs"),

            _ => new CanaryResult(Section.Prophet, SectionStatus.Invalidated, null,
                $"DNSSEC control canary could not fetch records for {control} (transport/timeout)."),
        };
    }
}
