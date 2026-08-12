using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// <summary>
    /// C051: mutually-exclusive tri-state result replacing the three independent booleans.
    /// Consistent = local answers match the public union; RedirectionDetected = confirmed
    /// redirection; Degraded = divergence that may be CDN/geo; Inconclusive = not enough
    /// evidence to judge. Only one state is valid at a time.
    /// </summary>
    public enum VantageComparisonResult
    {
        Consistent,
        RedirectionDetected,
        Degraded,
        Inconclusive
    }

    public class ComparisonResult
    {
        public string LocalServer { get; set; } = string.Empty;
        public List<DNSRecord> LocalRecords { get; set; } = new List<DNSRecord>();
        public Dictionary<string, List<DNSRecord>> PublicResolverResults { get; set; } = new Dictionary<string, List<DNSRecord>>();

        // C051: mutually-exclusive tri-state result replacing the three independent booleans.
        // The macOS wire (gold standard) carries storable `isInconclusive`/`isDegraded`/
        // `isRedirectionDetected` booleans (VantageComparer.swift:42-44); the legacy C# spelling
        // `result` (enum name/number) is also accepted on read for files written before this
        // alignment. The booleans below are the single source of truth on the wire and SET the
        // enum when true, so a macOS-produced snapshot's inconclusive/degraded state survives the
        // round-trip and the display-integrity rebuild derives the same canonical result the
        // macOS signer derived. Only one state is valid at a time; setting false is a no-op.
        public VantageComparisonResult Result { get; set; } = VantageComparisonResult.Consistent;

        [JsonPropertyName("isRedirectionDetected")]
        public bool IsRedirectionDetected
        {
            get => Result == VantageComparisonResult.RedirectionDetected;
            set { if (value) Result = VantageComparisonResult.RedirectionDetected; }
        }
        [JsonPropertyName("isInconclusive")]
        public bool IsInconclusive
        {
            get => Result == VantageComparisonResult.Inconclusive;
            set { if (value) Result = VantageComparisonResult.Inconclusive; }
        }
        [JsonPropertyName("isDegraded")]
        public bool IsDegraded
        {
            get => Result == VantageComparisonResult.Degraded;
            set { if (value) Result = VantageComparisonResult.Degraded; }
        }

        public ResolverQueryOutcome LocalOutcome { get; set; } = ResolverQueryOutcome.Empty();
        public Dictionary<string, ResolverQueryOutcome> PublicResolverOutcomes { get; set; } = new Dictionary<string, ResolverQueryOutcome>();
        public Dictionary<string, ResolverQueryOutcome>? DohOutcomes { get; set; }
    }

    public class ResolverQueryOutcome
    {
        public List<DNSRecord> Records { get; set; } = new List<DNSRecord>();
        public DNSResponseStatus Status { get; set; }

        /// <summary>
        /// The resolver's failure text. [B251] The WIRE name is <c>errorDescription</c>, matching
        /// macOS <c>ResolverQueryOutcome.errorDescription</c> (VantageComparer.swift:5-13), which is
        /// the gold standard. This is display-only: the SIGNED canonical field is the unified
        /// <c>errorMessage</c> on <see cref="ResolverQueryOutcomeCanonical"/> and is untouched.
        ///
        /// Getting this wrong is not cosmetic. The D083/B037 display-integrity guard rebuilds the
        /// canonical payload from these display outcomes and compares it field-by-field with the
        /// signed evidence, so a reader that drops the error text produces a rebuilt payload with a
        /// null <c>errorMessage</c> and rejects an honest macOS snapshot as DISPLAY MISMATCH.
        /// </summary>
        [JsonPropertyName("errorDescription")]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// [B251] Read-only compatibility alias for snapshots written by Windows builds before the
        /// wire name was aligned to macOS. Set-only, so it is never serialized (System.Text.Json
        /// ignores properties without a getter when writing) and can never emit a second spelling.
        /// </summary>
        [JsonPropertyName("errorMessage")]
        public string? LegacyErrorMessage
        {
            set { if (ErrorMessage is null) ErrorMessage = value; }
        }

        [JsonIgnore]
        public bool DidFail => Status == DNSResponseStatus.Timeout ||
                               Status == DNSResponseStatus.TransportError ||
                               Status == DNSResponseStatus.MalformedResponse ||
                               Status == DNSResponseStatus.ResponseIdMismatch ||
                               Status == DNSResponseStatus.Truncated;

        public static ResolverQueryOutcome Empty()
        {
            return new ResolverQueryOutcome { Status = DNSResponseStatus.NoData };
        }

        public static ResolverQueryOutcome FromResponse(DNSResponse response)
        {
            return new ResolverQueryOutcome
            {
                Records = response.Records,
                Status = response.Status,
                ErrorMessage = response.ErrorMessage
            };
        }
    }

    /// <summary>
    /// E162: which transport an observation was made over. A resolver name alone does not identify
    /// an observation — the same resolver is deliberately probed over Do53 AND DoH — so vantage
    /// results are tagged with their transport and partitioned on that tag. Purely internal to the
    /// comparer; it never reaches the witness JSON, so it needs no wire converter.
    /// </summary>
    public enum DnsVantageTransport
    {
        Do53,
        DoH,
    }

    public static class VantageComparer
    {
        /// <summary>
        /// Mirror's public-resolver baseline. Stream A: a PROJECTION of the single canonical roster
        /// on <see cref="ResolverConsensusEngine.PublicResolvers"/>, sliced to the primary resolvers
        /// Mirror fans out to. The IPs are no longer hand-maintained here — the roster lives in ONE
        /// place so Prophet and Mirror can never drift. The witness wire is pinned to exactly these
        /// keys, so the slice is fixed at Cloudflare/Google/Quad9.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> PublicResolvers =
            ResolverConsensusEngine.MirrorPrimaryResolvers.ToDictionary(
                name => name,
                name => ResolverConsensusEngine.PublicResolvers.First(r => r.Name == name).Ip,
                StringComparer.Ordinal);

        /// <summary>
        /// DoH endpoints for the same public resolvers.  A DoH query uses HTTPS and is routed
        /// via the system's HTTP stack, so on its own it is *not* a tamper-proof channel.
        /// However, when the three UDP public-resolver answers unanimously agree with each
        /// other but the DoH answer disagrees, that pattern is strong evidence that UDP traffic
        /// is being intercepted on-path (B010).
        ///
        /// Stream A: a PROJECTION of <see cref="ResolverConsensusEngine.DoHEndpoints"/> sliced to
        /// the Mirror primary resolvers.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DoHEndpoints =
            ResolverConsensusEngine.MirrorPrimaryResolvers.ToDictionary(
                name => name,
                name => ResolverConsensusEngine.DoHEndpoints[name],
                StringComparer.Ordinal);

        public static async Task<ComparisonResult> CompareVantagePointsAsync(string domain, ushort type = 1, string localServer = "127.0.0.1")
        {
            // Stream A: the local + Do53 + DoH fan-out (and its transport partitioning) now lives in
            // the shared ResolverConsensusEngine, sourced from the single canonical roster. The
            // comparer keeps ownership of the asymmetric decision (Evaluate) and the ComparisonResult
            // assembly — including the exact wire keys the signed evidence depends on — unchanged.
            var (localOutcome, publicOutcomes, dohOutcomes) =
                await ResolverConsensusEngine.RunLocalDeltaFanOutAsync(domain, type, localServer);
            var localRecords = localOutcome.Records;

            var publicResults = publicOutcomes.ToDictionary(r => r.Key, r => r.Value.Records);

            var result = Evaluate(localOutcome, publicOutcomes, dohOutcomes);

            // Mirror does NOT write the shared store: per §3 it READS Prophet's last peer-consensus
            // result and never re-runs (or overwrites) consensus itself. The asymmetric delta below
            // is Mirror's own section result. Prophet (ProphetConsensus.RunAsync) and the unified run
            // (ResolverConsensusEngine.RunUnifiedAsync) are the store's only writers.
            return new ComparisonResult
            {
                LocalServer = localServer,
                LocalRecords = localRecords,
                PublicResolverResults = publicResults,
                // C051: map Result booleans to the mutually-exclusive enum.
                Result = result.Redirection ? VantageComparisonResult.RedirectionDetected
                    : result.Inconclusive ? VantageComparisonResult.Inconclusive
                    : result.Degraded ? VantageComparisonResult.Degraded
                    : VantageComparisonResult.Consistent,
                LocalOutcome = localOutcome,
                PublicResolverOutcomes = publicOutcomes,
                DohOutcomes = dohOutcomes
            };
        }

        /// <summary>
        /// [E162] Splits the flat set of vantage observations into the Do53 map and the DoH map by
        /// TRANSPORT. Factored out of <see cref="CompareVantagePointsAsync"/> so the property that
        /// matters can be tested without a network: the same resolver observed over both transports
        /// yields TWO retained observations, one in each map, and neither silently overwrites the
        /// other.
        /// </summary>
        /// <remarks>
        /// A duplicate WITHIN one transport is a different matter and still throws: two Do53
        /// observations of "Cloudflare" in one run would mean the probe layer queried the same
        /// vantage twice, and quietly keeping one of them would drop evidence. Better to fail loudly
        /// than to decide, silently, which observation of a resolver the user gets to see.
        /// </remarks>
        internal static (Dictionary<string, ResolverQueryOutcome> Do53, Dictionary<string, ResolverQueryOutcome> DoH)
            PartitionByTransport(IEnumerable<(DnsVantageTransport Transport, string Key, ResolverQueryOutcome outcome)> results)
        {
            var do53 = new Dictionary<string, ResolverQueryOutcome>();
            var doh = new Dictionary<string, ResolverQueryOutcome>();
            foreach (var r in results)
            {
                var target = r.Transport == DnsVantageTransport.Do53 ? do53 : doh;
                if (target.ContainsKey(r.Key))
                {
                    throw new InvalidOperationException(
                        $"Two {r.Transport} observations of resolver '{r.Key}' in one comparison run; " +
                        "refusing to discard one of them.");
                }
                target[r.Key] = r.outcome;
            }
            return (do53, doh);
        }

        /// <summary>
        /// Pure decision logic, factored out so it can be unit-tested without a network.
        /// Mirrors <c>VantageComparer.evaluate</c> on macOS.
        /// </summary>
        public static Result Evaluate(ResolverQueryOutcome localOutcome,
                                       Dictionary<string, ResolverQueryOutcome> publicOutcomes,
                                       Dictionary<string, ResolverQueryOutcome>? dohOutcomes = null)
        {
            var baselines = publicOutcomes.Values.Where(o => o.Records.Count > 0).ToList();
            bool anyPublicBaseline = baselines.Count > 0;
            bool sufficientBaseline = baselines.Count >= 2;

            if (localOutcome.DidFail || !anyPublicBaseline)
                return new Result { Redirection = false, Inconclusive = true, Degraded = false };

            // Single baseline — insufficient evidence for a redirection result.
            if (!sufficientBaseline)
                return new Result { Redirection = false, Inconclusive = false, Degraded = true };

            // A successful-but-empty local answer (NODATA/NXDOMAIN — NOT a transport failure, already
            // handled above via DidFail) is not tampering: split-horizon DNS, geo-specific zones, or a
            // name that genuinely has no record of this type all produce it. With no local IP to
            // corroborate a block, calling this "redirection" is a fabricated positive from a non-answer
            // (B017), so it is inconclusive — a real redirection needs a blocking IP or a disjoint
            // non-empty answer.
            if (localOutcome.Records.Count == 0)
                return new Result { Redirection = false, Inconclusive = true, Degraded = false };

            // WS2-B.2 (B058/B059): poisoned baseline must not suppress redirection.
            // Use union-based overlap for redirection (CDN-compatible), but flag when a single
            // overlapping resolver has a unique uncorroborated answer as degraded (potential poison).
            var publicUnion = new HashSet<string>(baselines.SelectMany(o => o.Records.Select(r => r.Rdata)));
            var localRdatas = new HashSet<string>(localOutcome.Records.Select(r => r.Rdata));
            int resolverOverlapCount = baselines.Count(bl =>
            {
                var blRdatas = new HashSet<string>(bl.Records.Select(r => r.Rdata));
                return blRdatas.Overlaps(localRdatas);
            });

            bool localLooksBlocked = localOutcome.Records.Any(r => IPClassifier.IsBlockingAnswer(r.Rdata));

            bool redirection;
            bool inconclusiveFromDivergence = false;  // B281: disjoint answer not confirmable as a redirect
            if (localLooksBlocked)
            {
                redirection = true;
            }
            else if (!localRdatas.Overlaps(publicUnion))
            {
                // B281: a fully-disjoint local answer is no longer an automatic redirection. It is
                // classified by CrossVantageConsensus — the SAME shared decision Prophet uses —
                // against the ATTACKER-INDEPENDENT DoH consensus ONLY (DohCorroboratedPool), never
                // the Do53/UDP publicUnion, which a port-53 on-path attacker authors (finding 2). A
                // different responsible network from the DoH consensus → redirection stands; the same
                // responsible network, a mixed family, or no DoH consensus → inconclusive (never a
                // silent clean, never a bare accusation). No VPN branch (finding 3). Twin of macOS
                // VantageComparer.swift.
                var dohConsensus = CrossVantageConsensus.DohCorroboratedPool(dohOutcomes);
                // Type-filter the candidate to A/AAAA (C117) so the decision matches macOS byte-for-byte;
                // the surrounding `localRdatas` retains its existing (unfiltered) shape for the legacy paths.
                var localAddrs = CrossVantageConsensus.AddressSet(localOutcome.Records) ?? new HashSet<string>();
                switch (CrossVantageConsensus.ClassifyDivergence(localAddrs, dohConsensus))
                {
                    case CrossVantageOutcome.Mismatch:
                        redirection = true;
                        break;
                    default:   // Inconclusive — weak / same-network / mixed-family / no reference
                        redirection = false;
                        inconclusiveFromDivergence = true;
                        break;
                }
            }
            else
            {
                // B010: check for unanimous UDP tampering via DoH cross-validation.
                redirection = DetectUnanimousTampering(publicOutcomes, dohOutcomes, localRdatas);
            }

            // Degraded amplification: when redirection NOT detected but only 1 resolver
            // overlaps with unique uncorroborated answer → possible poisoned baseline (B059).
            bool onlyOverlapIsUnique = false;
            if (!redirection && resolverOverlapCount == 1)
            {
                var overlapping = baselines.FirstOrDefault(bl =>
                {
                    var blRdatas = new HashSet<string>(bl.Records.Select(r => r.Rdata));
                    return blRdatas.Overlaps(localRdatas);
                });
                if (overlapping is not null)
                {
                    var onlyAns = new HashSet<string>(overlapping.Records.Select(r => r.Rdata));
                    int corroborators = baselines.Count(bl =>
                        new HashSet<string>(bl.Records.Select(r => r.Rdata)).SetEquals(onlyAns));
                    onlyOverlapIsUnique = corroborators < 2;
                }
            }

            // WS2-B.2 (B058/B059): upgrade LIKELY→CONFIRMED. When the *only* thing keeping the local
            // answer inside the public union is a single, uncorroborated (i.e. suspected-poisoned)
            // baseline that echoes the local IP, but ≥2 *other* baselines unanimously agree on a
            // different destination disjoint from local, the lone echo is poison — not consensus —
            // and the local answer is a confirmed redirection. Requiring the alternative to be
            // corroborated by ≥2 agreeing resolvers keeps honest CDN/geo divergence (where the other
            // resolvers disagree with each other) at DEGRADED instead of a fabricated positive.
            if (!redirection && resolverOverlapCount == 1 && onlyOverlapIsUnique)
            {
                var nonOverlapping = baselines
                    .Where(bl => !new HashSet<string>(bl.Records.Select(r => r.Rdata)).Overlaps(localRdatas))
                    .ToList();
                bool corroboratedAlternative = nonOverlapping.Any(candidate =>
                {
                    var cand = new HashSet<string>(candidate.Records.Select(r => r.Rdata));
                    return nonOverlapping.Count(bl =>
                        new HashSet<string>(bl.Records.Select(r => r.Rdata)).SetEquals(cand)) >= 2;
                });
                if (corroboratedAlternative)
                    redirection = true;
            }

            bool degraded = publicOutcomes.Values.Any(o => o.DidFail || o.Records.Count == 0)
                || (!redirection && resolverOverlapCount == 1 && onlyOverlapIsUnique);

            // B010 residual: UDP unanimous + DoH cross-check below quorum → cannot distinguish
            // "tampered + attacker also suppresses DoH" from "clean + DoH provider outage". Must be
            // inconclusive, never a false-secure clean result. Only applies when redirection was NOT
            // already detected (no double-counting) and when DoH outcomes were provided.
            //
            // W04: the quorum floor is the SAME one DetectUnanimousTampering enforces — at least two
            // DoH endpoints must actually return records. Testing "all DoH failed" was too narrow: a
            // single answering endpoint made that false while DetectUnanimousTampering still bailed
            // out at its own `< 2` gate, so a run with no completed cross-transport corroboration was
            // reported fully clean. One witness is below the floor, so it counts as none.
            bool dohProvided = dohOutcomes is not null && dohOutcomes.Count > 0;
            var udpSetsForCheck = publicOutcomes.Values.Where(o => o.Records.Count > 0)
                .Select(o => new HashSet<string>(o.Records.Select(r => r.Rdata))).ToList();
            bool udpUnanimous = udpSetsForCheck.Count >= 2
                && udpSetsForCheck.All(s => s.SetEquals(udpSetsForCheck[0]));
            int dohAnsweringCount = dohProvided
                ? dohOutcomes!.Values.Count(o => o.Records.Count > 0)
                : 0;
            bool dohBelowCorroborationFloor = dohProvided && dohAnsweringCount < 2;
            bool dohBlockedWhileUdpUnanimous = !redirection && udpUnanimous && dohBelowCorroborationFloor;

            // B281: fold in the unconfirmable-disjoint-divergence inconclusive. `degraded` was
            // computed above (before any early exit), so a timed-out UDP baseline cannot be erased
            // into a clean (finding 4). Twin of macOS VantageComparer.swift.
            bool isInconclusive = dohBlockedWhileUdpUnanimous || inconclusiveFromDivergence;

            return new Result
            {
                Redirection = redirection,
                Inconclusive = isInconclusive,
                Degraded = degraded && !isInconclusive
            };
        }

        /// <summary>
        /// Detects the unanimous-tampering pattern (B010): all UDP public resolvers agree on
        /// a single answer set, but ≥2 DoH endpoints transparently return a *different* set.
        /// The DoH answers are also checked for mutual agreement so we don't flag a DoH
        /// transport hiccup as tampering.
        /// </summary>
        public static bool DetectUnanimousTampering(
            Dictionary<string, ResolverQueryOutcome> publicOutcomes,
            Dictionary<string, ResolverQueryOutcome>? dohOutcomes,
            HashSet<string> localRdatas)
        {
            if (dohOutcomes == null || dohOutcomes.Count == 0) return false;

            // Collect UDP rdata sets from resolvers that returned records.
            var udpSets = publicOutcomes.Values
                .Where(o => o.Records.Count > 0)
                .Select(o => new HashSet<string>(o.Records.Select(r => r.Rdata)))
                .ToList();
            if (udpSets.Count < 2) return false;

            // Check unanimity: all UDP answer sets point to the same destination.
            var firstUdp = udpSets[0];
            if (!udpSets.All(s => s.SetEquals(firstUdp))) return false;

            // Collect DoH rdata sets from endpoints that returned records.
            var dohSets = dohOutcomes.Values
                .Where(o => o.Records.Count > 0)
                .Select(o => new HashSet<string>(o.Records.Select(r => r.Rdata)))
                .ToList();
            if (dohSets.Count < 2) return false;

            // DoH should also show internal agreement (mutual consistency).
            var firstDoh = dohSets[0];
            if (!dohSets.All(s => s.SetEquals(firstDoh))) return false;

            // The UDP unanimity answer and the DoH consensus must differ.
            if (firstUdp.SetEquals(firstDoh)) return false;

            // Finally, the local answer must match the UDP tampered answer (the local resolver
            // is also being intercepted) — otherwise this is just strange network behavior.
            if (!localRdatas.SetEquals(firstUdp)) return false;

            return true;
        }
    }

    /// C051: internal result type — the three booleans are kept for Evaluate's internal logic
    /// but consumers should use ComparisonResult.Result (the enum) for the public API.
    public class Result
    {
        public bool Redirection { get; set; }
        public bool Inconclusive { get; set; }
        public bool Degraded { get; set; }
    }
}
