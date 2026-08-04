using System.Collections.Generic;
using Syxx.Kit;

namespace Syxx.Kit;

/// <summary>
/// Ph6.1: The versioned, WitnessSigner-signed catalog of acts that qualify as decisive for the
/// M4 safety gate. Per AMEND-O2, membership+version is signed for forensic reproducibility.
/// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/DecisiveActCatalog.swift.
/// </summary>
public static class DecisiveActCatalog
{
    /// <summary>Current catalog version. Bump when adding or removing decisive acts.</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// The set of EvidenceIds treated as decisive in the current catalog version.
    /// These are high-certainty, low-ambiguity signals that independently justify a tampered verdict.
    /// </summary>
    public static readonly HashSet<EvidenceId> Acts = new()
    {
        // RRSIG-validated DNSSEC canary stripping — unambiguous on-path interception evidence.
        EvidenceId.DnssecCanaryStripped,

        // Collector-confirmed DNS leak — dimension server observed query from unexpected resolver.
        EvidenceId.DnsLeakConfirmed,

        // Multi-source authoritative divergence — ≥2 independent public resolvers contradict local.
        EvidenceId.AuthoritativeDivergesConfirmed,

        // Confirmed NXDOMAIN hijack — known-nonexistent name resolves to a real IP.
        EvidenceId.NxCanaryHijack,
    };

    /// <summary>Builds the signed ValidatedDecisiveActs value for the current catalog version.</summary>
    public static ValidatedDecisiveActs CurrentValidatedActs() =>
        new(CurrentVersion, Acts);
}
