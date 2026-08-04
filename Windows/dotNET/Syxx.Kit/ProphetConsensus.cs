using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

// Parity twin of the macOS AppModel.runLookup 12-oracle consensus path
// (AppModel.swift:1011-1047). On macOS the roster + result struct + parallel
// query all live in the app target; on Windows + Linux (which share Syxx.Kit)
// they live here so both VMs reach the same 12-oracle consensus card without
// duplicating the roster or the task fan-out. macOS keeps its app-local copy
// as the gold standard and is not touched by this file.
//
// HONESTY: the strongest DNSSEC posture emitted is RecordsPresentUnverified;
// this helper does NOT perform full cryptographic chain validation (CF-08/A2).
// An unreachable oracle renders as "[unreachable]" — never as a clean IP.

/// The 11 public reference oracles Prophet queries alongside the local resolver
/// (12 total). Stream A: this is now a display-named PROJECTION of the single canonical
/// roster on <see cref="ResolverConsensusEngine.PublicResolvers"/> — the IPs are no longer
/// hand-maintained here. Identical shape to the macOS ProphetOracleRoster.publicOracles list;
/// the roster itself lives in ONE place (the engine) so Prophet and Mirror can never drift.
public static class ProphetOracleRoster
{
    public static readonly IReadOnlyList<(string Name, string Ip)> PublicOracles =
        ResolverConsensusEngine.PublicResolvers
            .Select(r => (Name: r.Name + " (" + r.Ip + ")", r.Ip))
            .ToList();

    /// The full 12-oracle roster for a given local resolver address (Local first).
    public static IReadOnlyList<(string Name, string Ip)> Roster(string localServer) =>
        ResolverConsensusEngine.Roster(localServer);
}

/// Structured Prophet consensus result shown in the ProphetView result card.
/// Parity twin of macOS AppModel.ProphetResult (AppModel.swift:41-55).
public sealed record ProphetResult
{
    public sealed record ResolverResponse(string Resolver, string Ip);

    public sealed record ConsensusData(int ResolversQueried, int? MedianLatencyMs);

    public IReadOnlyList<ResolverResponse> ResolverResponses { get; }
    public ConsensusData Consensus { get; }

    public ProphetResult(IReadOnlyList<ResolverResponse> resolverResponses, ConsensusData consensus)
    {
        ResolverResponses = resolverResponses;
        Consensus = consensus;
    }
}

/// Runs the 12-oracle consensus fan-out for a Prophet lookup. Mirrors the macOS
/// runLookup task-group path that builds resolverResponses, sorts Local first,
/// and surfaces an honest active-oracle count. The DNSSEC posture is fetched
/// separately by the caller via <see cref="DNSSECVerifier.CheckStatusAsync"/> so
/// it can be cleared at scan start and set only when the fresh query lands.
public static class ProphetConsensus
{
    /// Query the 12-oracle roster in parallel for <paramref name="domain"/>/<paramref name="qtype"/>
    /// and return the populated <see cref="ProphetResult"/>. <paramref name="medianLatencyMs"/>
    /// is the already-measured median latency (from ProbePlan.Latency) carried through unchanged;
    /// pass null when the latency probe was skipped or timed out (B231: renders as "—").
    public static async Task<ProphetResult> RunAsync(
        string domain,
        ushort qtype,
        string localServer,
        int? medianLatencyMs)
    {
        // Stream A: the roster + 12-oracle fan-out + Local-first sort now live in the shared
        // ResolverConsensusEngine. ProphetConsensus keeps its public signature and ProphetResult
        // shape, but no longer owns a private roster or fan-out.
        var responses = await ResolverConsensusEngine.RunPeerFanOutAsync(domain, qtype, localServer);

        var activeCount = ResolverConsensusEngine.ReachableCount(responses);

        // Record the symmetric peer-consensus result in the shared last-run store so Mirror can read
        // Prophet's last result (and how long ago it ran) instead of re-running consensus itself (§3).
        ResolverConsensusStore.Record(new ResolverConsensusSnapshot(
            domain,
            DateTimeOffset.UtcNow,
            ResolverConsensusEngine.ClassifyPeer(responses),
            activeCount,
            LocalDelta: null));

        return new ProphetResult(responses.ToList(), new ProphetResult.ConsensusData(activeCount, medianLatencyMs));
    }
}