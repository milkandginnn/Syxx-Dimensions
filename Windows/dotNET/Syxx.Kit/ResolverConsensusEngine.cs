using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

// Stream A (Warp/Mirror reimagining §3): the SINGLE cross-resolver consensus engine.
//
// Before this type, Prophet's peer-consensus card and Mirror's local-vs-consensus comparison were
// two independent fan-outs over two independently-maintained resolver rosters (ProphetOracleRoster
// vs VantageComparer.PublicResolvers) with their own tri-state derivations. This engine owns the
// ONE canonical roster + the fan-out machinery, and derives BOTH views from it:
//   (a) SYMMETRIC peer-consensus  — agree / disagree / insufficient / inconclusive across the
//       distinct IPs the reachable resolvers returned (what Prophet renders).
//   (b) ASYMMETRIC local-vs-consensus delta — consistent / redirection / degraded / inconclusive
//       of the LOCAL answer against the public baseline (what Mirror renders).
// Every run records its result + timestamp in the shared ResolverConsensusStore keyed by domain.
//
// Behavioural twin of macOS SyxxKit/ResolverConsensusEngine.swift — keep the roster, the fan-out,
// and the classification wiring in lockstep.
//
// HONESTY: the decision policy itself is NOT re-implemented here. Symmetric classification reuses
// ProphetPresentation.EvaluateConsensus and asymmetric classification reuses VantageComparer.Evaluate
// (which routes through the single CrossVantageConsensus policy). The engine only unifies the roster,
// the fan-out, and the store. The tri-state is preserved end to end: an unreachable leg is never a
// clean IP, and a not-measured / single-vantage run is never read back as agreement.
public static class ResolverConsensusEngine
{
    /// Symmetric peer-consensus over the distinct returned IPs (what Prophet needs).
    public enum PeerConsensusState
    {
        /// 0 reachable resolvers: no data to judge.
        Inconclusive,
        /// 1 reachable resolver: need >= 2 for consensus.
        Insufficient,
        /// All reachable resolvers returned the same IP.
        Agree,
        /// Reachable resolvers returned different IPs.
        Disagree,
    }

    /// Asymmetric local-vs-consensus delta (what Mirror needs). 1:1 with
    /// <see cref="VantageComparisonResult"/>; a distinct engine-level vocabulary so the shared store
    /// does not depend on the Mirror comparer's own enum.
    public enum LocalDeltaState
    {
        Consistent,
        RedirectionDetected,
        Degraded,
        Inconclusive,
    }

    // -- The ONE canonical roster ------------------------------------------------------------------

    /// The 11 public reference resolvers, plain-named. THE single source of truth for the resolver
    /// list: ProphetOracleRoster (Prophet's 12-oracle roster) and VantageComparer.PublicResolvers
    /// (Mirror's primary slice) are both projections of this list — neither hand-maintains IPs.
    public static readonly IReadOnlyList<(string Name, string Ip)> PublicResolvers =
        new List<(string, string)>
        {
            ("Cloudflare", "1.1.1.1"),
            ("Google", "8.8.8.8"),
            ("Quad9", "9.9.9.9"),
            ("OpenDNS", "208.67.222.222"),
            ("AdGuard", "94.140.14.14"),
            ("NextDNS", "45.90.28.0"),
            ("Control D", "76.76.2.0"),
            ("Comodo", "8.26.56.26"),
            ("Level3", "4.2.2.1"),
            ("Verisign", "64.6.64.6"),
            ("DNS.WATCH", "84.200.69.80"),
        };

    /// DoH endpoints for the resolvers that publish one. Canonical superset shared with macOS; each
    /// façade projects the subset it queries (Mirror uses the primary three below).
    public static readonly IReadOnlyDictionary<string, string> DoHEndpoints = new Dictionary<string, string>
    {
        { "Cloudflare", "https://cloudflare-dns.com/dns-query" },
        { "Google", "https://dns.google/dns-query" },
        { "Quad9", "https://dns.quad9.net/dns-query" },
        { "OpenDNS", "https://doh.opendns.com/dns-query" },
        { "AdGuard", "https://dns.adguard.com/dns-query" },
        { "NextDNS", "https://dns.nextdns.io/dns-query" },
        { "Control D", "https://freedns.controld.com/p0" },
    };

    /// The resolvers Mirror's local-vs-consensus comparison fans out to (UDP + DoH). A NAMED slice of
    /// the canonical roster, not a separate list — the witness wire is pinned to exactly these keys.
    public static readonly IReadOnlyList<string> MirrorPrimaryResolvers =
        new[] { "Cloudflare", "Google", "Quad9" };

    /// The full 12-entry roster for a given local resolver address (Local first), display-named as
    /// "Name (ip)" — the format Prophet's rows use.
    public static IReadOnlyList<(string Name, string Ip)> Roster(string localServer) =>
        new[] { ("Local (" + localServer + ")", localServer) }
            .Concat(PublicResolvers.Select(r => (Name: r.Name + " (" + r.Ip + ")", r.Ip)))
            .ToList();

    // -- Prophet fan-out (symmetric) ---------------------------------------------------------------

    /// Query the full 12-oracle roster in parallel (UDP) for <paramref name="domain"/>/<paramref name="qtype"/>
    /// and return the display-named responses, Local first. Reproduces the fan-out ProphetConsensus
    /// used to own inline; ProphetConsensus.RunAsync now delegates here.
    public static async Task<IReadOnlyList<ProphetResult.ResolverResponse>> RunPeerFanOutAsync(
        string domain, ushort qtype, string localServer)
    {
        var oracles = Roster(localServer);
        var tasks = oracles.Select(async oracle =>
        {
            var res = await DNSResolver.QueryUDPDetailedAsync(
                new DNSQuery(name: domain, type: qtype, server: oracle.Ip));
            return new ProphetResult.ResolverResponse(oracle.Name, IpOf(res));
        });
        var responses = (await Task.WhenAll(tasks)).ToList();
        SortLocalFirst(responses);
        return responses;
    }

    /// Reduce a resolver response to the string Prophet renders: the first record's rdata, else
    /// "no records" (successful but empty) or "[unreachable]" (transport failure). Never a clean IP
    /// for a leg that did not answer.
    private static string IpOf(DNSResponse res) =>
        res.Records.Count > 0
            ? res.Records[0].Rdata
            : (res.Status == DNSResponseStatus.Success ? "no records" : "[unreachable]");

    private static void SortLocalFirst(List<ProphetResult.ResolverResponse> responses) =>
        responses.Sort((r1, r2) =>
        {
            if (r1.Resolver.StartsWith("Local", StringComparison.Ordinal)) return -1;
            if (r2.Resolver.StartsWith("Local", StringComparison.Ordinal)) return 1;
            return string.Compare(r1.Resolver, r2.Resolver, StringComparison.Ordinal);
        });

    /// Count of resolvers that returned a real (reachable) answer — never counts "[unreachable]".
    public static int ReachableCount(IReadOnlyList<ProphetResult.ResolverResponse> responses) =>
        responses.Count(r => !r.Ip.Contains("[unreachable]", StringComparison.Ordinal));

    /// Symmetric classification: agree / disagree / insufficient / inconclusive from the distinct
    /// returned IPs. Reuses ProphetPresentation.EvaluateConsensus (the single symmetric-logic owner).
    public static PeerConsensusState ClassifyPeer(IReadOnlyList<ProphetResult.ResolverResponse> responses)
    {
        var state = ProphetPresentation.EvaluateConsensus(responses).State;
        return state switch
        {
            ProphetPresentation.ResolverConsensus.Agree => PeerConsensusState.Agree,
            ProphetPresentation.ResolverConsensus.Disagree => PeerConsensusState.Disagree,
            ProphetPresentation.ResolverConsensus.Insufficient => PeerConsensusState.Insufficient,
            _ => PeerConsensusState.Inconclusive,
        };
    }

    // -- Mirror fan-out (asymmetric) ---------------------------------------------------------------

    /// Query the local resolver plus the Mirror primary slice over BOTH UDP (Do53) and DoH, in
    /// parallel. Returns the raw outcomes, partitioned by transport. Reproduces the fan-out
    /// VantageComparer.CompareVantagePointsAsync used to own inline; the comparer now delegates here
    /// and keeps its own Evaluate + ComparisonResult assembly.
    public static async Task<(ResolverQueryOutcome Local,
                              Dictionary<string, ResolverQueryOutcome> Public,
                              Dictionary<string, ResolverQueryOutcome> Doh)>
        RunLocalDeltaFanOutAsync(string domain, ushort type, string localServer)
    {
        var localOutcome = await QueryOutcomeAsync(new DNSQuery(domain, type, localServer));

        var udpTasks = MirrorPrimaryResolvers.Select(async name =>
        {
            var ip = PublicResolvers.First(r => r.Name == name).Ip;
            return (Transport: DnsVantageTransport.Do53, Key: name,
                    outcome: await QueryOutcomeAsync(new DNSQuery(domain, type, ip)));
        });
        var dohTasks = MirrorPrimaryResolvers.Select(async name =>
        {
            var resp = await DNSResolver.QueryDoHDetailedAsync(domain, type, DoHEndpoints[name]);
            return (Transport: DnsVantageTransport.DoH, Key: name,
                    outcome: ResolverQueryOutcome.FromResponse(resp));
        });

        var allResults = await Task.WhenAll(udpTasks.Concat(dohTasks));
        var (publicOutcomes, dohOutcomes) = VantageComparer.PartitionByTransport(allResults);
        return (localOutcome, publicOutcomes, dohOutcomes);
    }

    /// Asymmetric classification, mapped to the engine vocabulary. Reuses VantageComparer.Evaluate
    /// (the single asymmetric-logic owner, routed through CrossVantageConsensus).
    public static LocalDeltaState ClassifyLocalDelta(
        ResolverQueryOutcome localOutcome,
        Dictionary<string, ResolverQueryOutcome> publicOutcomes,
        Dictionary<string, ResolverQueryOutcome>? dohOutcomes = null)
    {
        var r = VantageComparer.Evaluate(localOutcome, publicOutcomes, dohOutcomes);
        return r.Redirection ? LocalDeltaState.RedirectionDetected
            : r.Inconclusive ? LocalDeltaState.Inconclusive
            : r.Degraded ? LocalDeltaState.Degraded
            : LocalDeltaState.Consistent;
    }

    private static async Task<ResolverQueryOutcome> QueryOutcomeAsync(DNSQuery query)
    {
        var response = await DNSResolver.QueryUDPDetailedAsync(query);
        return ResolverQueryOutcome.FromResponse(response);
    }

    // -- Unified single run (exposes BOTH, persists to the shared store) ----------------------------

    /// The result of a single unified consensus run: the symmetric peer view AND the asymmetric
    /// local-vs-consensus view, plus the responses/outcomes both were derived from.
    public sealed record ResolverConsensusRun(
        string Domain,
        DateTimeOffset Timestamp,
        IReadOnlyList<ProphetResult.ResolverResponse> Responses,
        PeerConsensusState PeerConsensus,
        int ReachableCount,
        LocalDeltaState LocalDelta);

    /// ONE fan-out (full 12 roster over UDP + the primary DoH legs) that derives BOTH the symmetric
    /// peer-consensus result and the asymmetric local-vs-consensus delta, records the snapshot in the
    /// shared store, and returns both. This is the entry point the Mirror/Prophet UI will migrate to
    /// so a single scan feeds both cards; the existing per-façade paths remain wire-identical.
    public static async Task<ResolverConsensusRun> RunUnifiedAsync(
        string domain, ushort qtype, string localServer)
    {
        // Full-roster UDP fan-out keyed by plain name, plus the local leg and the primary DoH legs.
        var localTask = QueryOutcomeAsync(new DNSQuery(domain, qtype, localServer));
        var udpTasks = PublicResolvers.Select(async r =>
            (r.Name, Outcome: await QueryOutcomeAsync(new DNSQuery(domain, qtype, r.Ip)))).ToList();
        var dohTasks = MirrorPrimaryResolvers.Select(async name =>
            (Name: name, Outcome: ResolverQueryOutcome.FromResponse(
                await DNSResolver.QueryDoHDetailedAsync(domain, qtype, DoHEndpoints[name])))).ToList();

        var localOutcome = await localTask;
        var udp = (await Task.WhenAll(udpTasks)).ToDictionary(t => t.Name, t => t.Outcome, StringComparer.Ordinal);
        var doh = (await Task.WhenAll(dohTasks)).ToDictionary(t => t.Name, t => t.Outcome, StringComparer.Ordinal);

        // Symmetric view: display-named responses (Local first) over every UDP leg.
        var responses = new List<ProphetResult.ResolverResponse>
        {
            new("Local (" + localServer + ")", IpOfOutcome(localOutcome)),
        };
        responses.AddRange(PublicResolvers.Select(r =>
            new ProphetResult.ResolverResponse(r.Name + " (" + r.Ip + ")", IpOfOutcome(udp[r.Name]))));
        SortLocalFirst(responses);

        var peer = ClassifyPeer(responses);
        var reachable = ReachableCount(responses);

        // Asymmetric view: local vs the primary UDP slice, cross-checked against the DoH legs.
        var primaryUdp = MirrorPrimaryResolvers.ToDictionary(n => n, n => udp[n], StringComparer.Ordinal);
        var delta = ClassifyLocalDelta(localOutcome, primaryUdp, doh);

        var run = new ResolverConsensusRun(
            domain, DateTimeOffset.UtcNow, responses, peer, reachable, delta);
        ResolverConsensusStore.Record(
            new ResolverConsensusSnapshot(domain, run.Timestamp, peer, reachable, delta));
        return run;
    }

    /// Outcome → the string Prophet renders (mirrors <see cref="IpOf"/> for an already-materialised
    /// outcome): first record's rdata, else "no records" / "[unreachable]".
    private static string IpOfOutcome(ResolverQueryOutcome o) =>
        o.Records.Count > 0
            ? o.Records[0].Rdata
            : (o.Status == DNSResponseStatus.Success ? "no records" : "[unreachable]");
}
