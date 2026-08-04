namespace Syxx.Kit;

/// <summary>
/// Ph7.2: the static, expert-elicited FMEA Occurrence defaults for the Stage-5 priority model,
/// and the v2 severity×occurrence×detectability (SOD) scoring rule.
///
/// Occurrence is "how often does this mechanism class appear in the wild against ordinary
/// users", on the FMEA 1–10 scale (1 = essentially never observed, 10 = ubiquitous). These are
/// EXPERT-ELICITED priors, not corpus-measured rates — the corpus-dependent measurement (P7.1)
/// is deferred until the labeled corpus (O1) exists, at which point these constants are replaced
/// by measured values and the elicitation note below is updated. Documented rationale per value:
///
///  - Rewrite   7: resolver-level answer rewriting (ISP redirection, NXDOMAIN monetization,
///                 captive infrastructure) is the single most commonly reported interference.
///  - Block     6: DNS-level blocking/censorship is routine in many jurisdictions and on
///                 corporate/school networks, slightly less universal than rewriting.
///  - Transport 5: encrypted-transport interference (DoH/DoT blocking, downgrade) is common on
///                 managed networks but not the default posture of consumer ISPs.
///  - Leak      6: VPN/tunnel DNS leaks are frequent misconfigurations rather than attacks,
///                 but they occur constantly in practice.
///  - Content   3: on-path content tampering (script injection) is now rare on HTTPS-dominant
///                 paths; it concentrates on legacy HTTP and hostile middleboxes.
///  - Host      2: local compromise (exfiltrating malware using DNS) is the rarest class an
///                 ordinary run encounters.
///  - Unresolved 5: by definition unknown — the scale midpoint, never a confident extreme.
///
/// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/RPNOccurrence.swift (owed).
/// </summary>
public static class RPNOccurrence
{
    /// <summary>The Stage-5 priority model that includes Occurrence (v1 "syxx.stage5.sd" is S×D only).</summary>
    public const string ModelId = "syxx.stage5.sod";
    public const int ModelVersion = 2;

    public static int DefaultOccurrence(MechanismClass mechanismClass) => mechanismClass switch
    {
        MechanismClass.Rewrite    => 7,
        MechanismClass.Block      => 6,
        MechanismClass.Transport  => 5,
        MechanismClass.Leak       => 6,
        MechanismClass.Content    => 3,
        MechanismClass.Host       => 2,
        _                         => 5,
    };

    /// <summary>
    /// The v2 SOD score. Occurrence is REQUIRED for this model — a v2 finding without it is a
    /// contract violation (validated in Stage5Presentation.ValidationErrors), never a silent
    /// fallback to the S×D product.
    /// </summary>
    public static int Score(PriorityFactors factors) =>
        factors.Occurrence is { } occurrence
            ? factors.Severity * factors.Detectability * occurrence
            : throw new System.ArgumentException("SOD v2 factors require a non-null Occurrence.", nameof(factors));

    /// <summary>Builds v2 factors for a mechanism class using the expert-elicited default.</summary>
    public static PriorityFactors FactorsFor(MechanismClass mechanismClass, int severity, int detectability) =>
        new(severity, detectability, DefaultOccurrence(mechanismClass));
}
