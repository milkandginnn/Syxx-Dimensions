using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Syxx.Kit
{
    /// <summary>
    /// B281 — the SINGLE cross-vantage agreement policy, shared by Prophet's multi-resolver consensus
    /// and Mirror's local-vs-baseline comparison (<see cref="VantageComparer.Evaluate"/>). Byte-for-
    /// byte decision twin of macOS <c>SyxxKit/Synthesis/CrossVantageConsensus.swift</c>. Keeping the
    /// policy in ONE place is the whole point: two hand-rolled copies are exactly how B271/B272
    /// (macOS fixed, Windows twin left behind) happened.
    ///
    /// The owner's reported false positive (2026-08-02: 140.82.113.3 vs .4, both GitHub) came from
    /// Prophet reducing each resolver to its first record and calling any set with more than one
    /// distinct string "possible tampering". This type compares FULL A/AAAA RRsets and applies an
    /// honest, adversary-aware policy (see B281 for the audit trail):
    ///  * Identical normalized A/AAAA RRsets → Match.
    ///  * A blocking/non-routable answer beside routable ones → Mismatch (redirect-to-block).
    ///  * Any other difference is judged ONLY against an attacker-INDEPENDENT reference (a DoH
    ///    consensus, <see cref="DohCorroboratedPool"/>): different responsible network → Mismatch;
    ///    same responsible network / mixed family / no reference → Inconclusive.
    /// There is NO VPN branch (a tunnel proves nothing about egress and its operator is an adversary),
    /// and /16-/32 adjacency never GRANTS a match — it only separates Mismatch from Inconclusive.
    /// </summary>
    public enum CrossVantageOutcome
    {
        Match,          // identical normalized A/AAAA RRsets across the reachable vantages
        Mismatch,       // confirmed redirect: blocking answer, or different responsible network
        Inconclusive,   // differs, but no attacker-independent evidence confirms a redirect
        Insufficient,   // fewer than two vantages returned a usable answer
        NoData          // no vantage returned a usable answer
    }

    public static class CrossVantageConsensus
    {
        /// <summary>
        /// C117: reduce a resolver's raw records to its A(1)/AAAA(28) rdata set. CNAME(5)/RRSIG(46)
        /// rdata are names / hex, not addresses, and must never enter the comparison. Returns null
        /// when the resolver produced no usable address answer (drop that vantage).
        /// </summary>
        public static HashSet<string>? AddressSet(IEnumerable<DNSRecord> records)
        {
            var set = new HashSet<string>(records.Where(r => r.Type == 1 || r.Type == 28).Select(r => r.Rdata));
            return set.Count == 0 ? null : set;
        }

        /// <summary>
        /// B281 verified equivalence reference: addresses each returned by ≥2 independent DoH endpoints
        /// (address records only). DoH is HTTPS-routed and attacker-independent (C025); a port-53
        /// hijacker who rewrites every Do53 answer cannot forge it. Do53/UDP corroboration is NOT
        /// counted (the attacker authors it — adversarial-review finding 2). Twin of Swift
        /// <c>dohCorroboratedPool</c>.
        ///
        /// LIMITATION (documented): the DoH legs use a stock, unpinned HTTPS client bootstrapped by
        /// hostname through the resolver under test, so this is attacker-independent against a port-53
        /// hijacker, NOT against a TLS-MITM holding a client-trusted root.
        /// </summary>
        public static HashSet<string> DohCorroboratedPool(Dictionary<string, ResolverQueryOutcome>? dohOutcomes)
        {
            if (dohOutcomes == null) return new HashSet<string>();
            var counts = new Dictionary<string, int>();
            foreach (var outcome in dohOutcomes.Values)
            {
                if (outcome.DidFail) continue;
                var addrs = new HashSet<string>(outcome.Records.Where(r => r.Type == 1 || r.Type == 28).Select(r => r.Rdata));
                foreach (var a in addrs) counts[a] = counts.TryGetValue(a, out var n) ? n + 1 : 1;
            }
            return new HashSet<string>(counts.Where(kv => kv.Value >= 2).Select(kv => kv.Key));
        }

        /// <summary>
        /// Prophet's entry point: classify agreement across N peer resolver answer sets (each an
        /// A/AAAA rdata set via <see cref="AddressSet"/>). <paramref name="trustedReference"/> is an
        /// attacker-independent consensus when the caller has one (empty otherwise). Twin of Swift
        /// <c>classifyPeerConsensus</c>.
        /// </summary>
        public static CrossVantageOutcome ClassifyPeerConsensus(List<HashSet<string>> vantageAnswers,
                                                                HashSet<string>? trustedReference = null)
        {
            var usable = vantageAnswers.Where(s => s.Count > 0).ToList();
            if (usable.Count == 0) return CrossVantageOutcome.NoData;
            if (usable.Count < 2) return CrossVantageOutcome.Insufficient;

            // Identical normalized RRsets (order-independent set equality).
            if (usable.Skip(1).All(s => s.SetEquals(usable[0]))) return CrossVantageOutcome.Match;

            // Differ. A blocking answer beside a routable one is a redirect-to-block signature.
            var union = new HashSet<string>();
            foreach (var s in usable) union.UnionWith(s);
            bool hasBlocking = union.Any(IPClassifier.IsBlockingAnswer);
            bool hasRoutable = union.Any(a => !IPClassifier.IsBlockingAnswer(a));
            if (hasBlocking && hasRoutable) return CrossVantageOutcome.Mismatch;

            return ClassifyDivergence(union, trustedReference ?? new HashSet<string>());
        }

        /// <summary>
        /// The load-bearing shared decision: given a candidate answer that DIFFERS from the rest,
        /// decide Mismatch vs Inconclusive using ONLY an attacker-independent reference. Twin of Swift
        /// <c>classifyDivergence</c>.
        /// </summary>
        public static CrossVantageOutcome ClassifyDivergence(HashSet<string> candidate, HashSet<string> trustedReference)
        {
            if (candidate.Count == 0) return CrossVantageOutcome.Inconclusive;
            // Finding 2: with no attacker-independent reference, strength is not computable.
            if (trustedReference.Count == 0) return CrossVantageOutcome.Inconclusive;
            return IsStrongDisagreement(candidate, trustedReference)
                ? CrossVantageOutcome.Mismatch
                : CrossVantageOutcome.Inconclusive;
        }

        /// <summary>
        /// B281 "strong disagreement": a candidate answer sits in a different responsible network from
        /// the trusted reference. Family-aware — a NAT64/DNS64 AAAA vs A references is "not comparable"
        /// (skipped), not a forced false Mismatch. Twin of Swift <c>isStrongDisagreement</c>.
        /// </summary>
        public static bool IsStrongDisagreement(HashSet<string> candidate, HashSet<string> reference)
        {
            foreach (var c in candidate)
            {
                var cFamily = AddressFamilyOf(c);
                if (cFamily == null) continue;                                   // unparseable → skip
                var sameFamilyRefs = reference.Where(r => AddressFamilyOf(r) == cFamily).ToList();
                if (sameFamilyRefs.Count == 0) continue;                         // cross-family (NAT64) → skip
                if (!sameFamilyRefs.Any(r => SharesResponsibleNetwork(c, r)))
                    return true;                                                 // comparable + out-of-network
            }
            return false;
        }

        /// <summary>
        /// B281: same responsible network? IPv4 /16, IPv6 /32 — coarse proxies chosen to err toward
        /// WITHHOLDING an accusation, never toward a false clean. Only ever a Mismatch-vs-Inconclusive
        /// discriminator against a trusted reference; never grants a match. Twin of Swift
        /// <c>sharesResponsibleNetwork</c>.
        /// </summary>
        public static bool SharesResponsibleNetwork(string a, string b)
        {
            if (TryBytes(a, AddressFamily.InterNetwork, out var a4) && TryBytes(b, AddressFamily.InterNetwork, out var b4))
                return a4[0] == b4[0] && a4[1] == b4[1];                          // same /16
            if (TryBytes(a, AddressFamily.InterNetworkV6, out var a6) && TryBytes(b, AddressFamily.InterNetworkV6, out var b6))
                return a6[0] == b6[0] && a6[1] == b6[1] && a6[2] == b6[2] && a6[3] == b6[3];  // same /32
            return false;
        }

        private static AddressFamily? AddressFamilyOf(string value)
        {
            if (IPAddress.TryParse(value, out var ip)) return ip.AddressFamily;
            return null;
        }

        private static bool TryBytes(string value, AddressFamily family, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (IPAddress.TryParse(value, out var ip) && ip.AddressFamily == family)
            {
                bytes = ip.GetAddressBytes();
                return true;
            }
            return false;
        }
    }
}
