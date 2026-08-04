import Foundation

/// Assembles a `Conclusion` from what each check actually produced.
///
/// History: this once fronted a Dempster-Shafer belief-combination engine (weight-of-evidence mass
/// functions, a hypothesis frame, pignistic betting probabilities, referral thresholds) that — across
/// several rewrites — kept producing a confidence/probability distribution that did not move in
/// response to real evidence. On 2026-07-21 the owner directed that engine removed in full. A later,
/// superseding owner directive (same day) directed a *new* determinative layer be integrated for
/// real: the matrix-based `MatrixDeterminativeEngine`.
///
/// So this file does two things now:
///  1. It still reports, un-massaged, exactly what ran / what each check observed / raw exposure
///     findings — every raw field traces directly to something a check produced.
///  2. It ALSO runs the matrix determinative engine over that same evidence and attaches the
///     resulting `DeterminativeResult` to the `Conclusion`. The engine is honest by construction:
///     unmapped / not-observed indicators are absent (never fabricated), missing/invalidated required
///     sections hard-gate their diagnoses (fail-closed), and abstention is a first-class outcome
///     (`.inconclusive`) rather than an unearned `.clean`. If the engine cannot even run (spec/bundle
///     validation failure), `determination` is left `nil` — no result is invented.
public enum SynthesisEngine {
    /// `now`/`config` kept for call-site compatibility during the migration off the old signature;
    /// `config` is currently unused (no thresholds left to tune here — the matrix engine owns its
    /// own `ThresholdProfile`) and is not removed yet so callers don't all need to change at once.
    public struct Config: Sendable {
        public init() {}
    }

    public static func synthesize(evidence: [EvidenceItem],
                                  sectionStatuses: [SectionStatusEntry],
                                  advisories: [Advisory] = [],
                                  config: Config = Config(),
                                  now: Date = Date()) -> Conclusion {
        return SyxxTracer.spanSync("syxx.synthesis.synthesize", attributes: [
            "synthesis.evidence_count": .int(evidence.count),
            "synthesis.section_count": .int(sectionStatuses.count)
        ]) { span in
            let determination = runDeterminativeEngine(evidence: evidence, sectionStatuses: sectionStatuses)
            // The four-state result must survive into telemetry, and a NIL determination — the
            // engine failing to run — must never be indistinguishable from `.clean`. Previously
            // this span discarded its handle entirely (`{ _ in }`), so the one signal that says
            // "no result was produced" was invisible to the collector.
            if let d = determination {
                span.setAttribute(key: "syxx.result", value: d.state.rawValue)
                span.setAttribute(key: "syxx.result.diagnosis_count", value: d.diagnoses.count)
                span.setAttribute(key: "syxx.result.suppressed_count", value: d.suppressed.count)
                switch d.state {
                case .clean:          SyxxTracer.setOutcome(span, .success, evidence: true)
                case .threatDetected: SyxxTracer.setOutcome(span, .failure, reason: "threat_detected", evidence: true)
                case .inconclusive:   SyxxTracer.setOutcome(span, .inconclusive, reason: "unresolved", evidence: true)
                case .untested:       SyxxTracer.setOutcome(span, .untested, reason: "nothing_evaluated", evidence: false)
                }
            } else {
                span.setAttribute(key: "syxx.result", value: "none")
                SyxxTracer.setOutcome(span, .inconclusive, reason: "engine_did_not_run", evidence: false)
            }
            let conclusion = Conclusion(generatedAt: now,
                              sectionStatuses: sectionStatuses,
                              evidence: evidence,
                              advisories: advisories,
                              determination: determination)
            SyxxDiagnostics.breadcrumb(
                category: "engine.result",
                message: "SynthesisEngine concluded",
                level: .warning,
                data: [
                    "conclusion": determination?.state.rawValue ?? "none",
                    "confidence": "",
                    "section_count": String(sectionStatuses.count),
                    "engine_version": "matrix.1"
                ])
            return conclusion
        }
    }

    // MARK: - Determinative engine bridge

    /// Builds a `SyxxRunInput` from the raw evidence + section statuses and evaluates it with the
    /// matrix engine. Returns `nil` (no manufactured result) if the engine throws — which, for a
    /// well-formed default spec, only happens on a malformed evidence bundle. Any such failure is a
    /// bug to fix, never a silent "clean."
    static func runDeterminativeEngine(evidence: [EvidenceItem],
                                       sectionStatuses: [SectionStatusEntry]) -> DeterminativeResult? {
        let sections = sectionStatuses.map { entry in
            // B176(d): a section that does not map to the matrix enum is a programming error —
            // the two enums are defined to be identical. Never silently collapse to .prophet.
            guard let mapped = SyxxSection(rawValue: entry.section.rawValue) else {
                preconditionFailure("SynthesisEngine: section '\(entry.section.rawValue)' has no SyxxSection case — add it to both enums")
            }
            return SyxxSectionInput(section: mapped,
                             status: EvidenceUsability(sectionStatus: entry.status),
                             trusted: true,
                             notes: nil)
        }
        // C063: real per-section datapoint completeness (`Synthesizer.DatapointCompleteness`,
        // carried on `SectionStatusEntry.completeness`) is the quality signal for every indicator
        // observed in that section. A duplicate section entry cannot survive to evaluation
        // (`EvidenceBundle.validate()` rejects it → nil determination), but this dict must not trap
        // first: keep the MINIMUM on collision (attenuates, never inflates).
        let completenessBySection = Dictionary(
            sectionStatuses.map { ($0.section, $0.completeness) },
            uniquingKeysWith: min)
        let indicators = mapEvidenceToIndicators(evidence, sectionCompleteness: completenessBySection)
        let input = SyxxRunInput(sections: sections, indicators: indicators)
        do {
            return try SyxxMatrixIntegration.evaluate(input)
        } catch {
            // Honest failure: the engine could not evaluate this bundle. Do NOT fabricate a result.
            // Honest to the USER — but this was also silent to US. A nil determination is how a
            // result goes missing: the user gets "the engine could not evaluate this run" and no
            // one ever learns why. Report it; still return nil.
            SyxxDiagnostics.capture(DiagnosticEvent(
                errorClass: "engine.no_determination",
                component: "SynthesisEngine",
                impact: .noDetermination,
                severity: .error,
                underlying: String(describing: error),
                // Group on the error VARIANT. `MatrixEngineError.description` interpolates
                // diagnosis and indicator ids, so grouping on the message would shard one bug
                // into hundreds of issues.
                fingerprint: ["engine.no_determination", matrixErrorKind(error)],
                tags: [
                    "syxx.engine_error_kind": matrixErrorKind(error),
                    "syxx.section_count": String(sections.count),
                    "syxx.indicator_count": String(indicators.count)
                ],
                extra: [
                    // Shape only — no hostnames, no addresses, no user data.
                    "section_statuses": sections
                        .map { "\($0.section.rawValue)=\($0.status.rawValue)" }
                        .sorted().joined(separator: ","),
                    "indicator_ids": indicators
                        .map { $0.indicator.rawValue }
                        .sorted().joined(separator: ",")
                ]))
            return nil
        }
    }

    /// Stable grouping key: the *variant* of `MatrixEngineError`, never its interpolated
    /// description. Thirteen distinct root causes reach the one catch above; grouping them as one
    /// issue would hide twelve bugs behind the first.
    static func matrixErrorKind(_ error: Error) -> String {
        guard let e = error as? MatrixEngineError else { return "unknown" }
        switch e {
        case .emptySpec:                   return "emptySpec"
        case .duplicateDiagnosis:          return "duplicateDiagnosis"
        case .diagnosisHasNoRules:         return "diagnosisHasNoRules"
        case .unknownRequiredSection:      return "unknownRequiredSection"
        case .contradictoryIndicatorRole:  return "contradictoryIndicatorRole"
        case .duplicateRule:               return "duplicateRule"
        case .strengthOutOfRange:          return "strengthOutOfRange"
        case .invalidRuleParameter:        return "invalidRuleParameter"
        case .danglingReference:           return "danglingReference"
        case .duplicateSectionInBundle:    return "duplicateSectionInBundle"
        case .duplicateIndicatorInBundle:  return "duplicateIndicatorInBundle"
        case .indicatorStrengthOutOfRange: return "indicatorStrengthOutOfRange"
        case .indicatorQualityOutOfRange:  return "indicatorQualityOutOfRange"
        }
    }

    /// The current, deliberately-conservative mapping from raw `EvidenceId` observations onto the
    /// matrix engine's normalised 0…1 indicator strengths.
    ///
    /// Rules of this mapping (honesty-critical):
    ///  * Only observations whose meaning maps *unambiguously* to an indicator are emitted. Where a
    ///    single `EvidenceId` does not cleanly imply one indicator (e.g. `dnsUnencrypted`, which may
    ///    be a user's own choice, or `answerDrift`, which is temporal not cross-vantage), it is left
    ///    UNMAPPED — the indicator stays absent (`nil` strength), which the engine treats as
    ///    "not observed," never as zero-support-that-counts.
    ///  * Several `EvidenceId`s can map to the same indicator (e.g. confirmed + unconfirmed
    ///    authoritative divergence). Because `EvidenceBundle.validate()` rejects a duplicate
    ///    indicator id, we collapse to ONE observation per indicator, taking the MAX strength seen —
    ///    a stronger corroboration never gets diluted by a weaker one.
    ///  * Presence of an `EvidenceItem` means the observation actually fired, so `contradictory` is
    ///    false. `quality` is NOT hardcoded (C063): it is the datapoint completeness of the SAME
    ///    section that produced the winning observation, clamped to [0,1]. The engine attenuates
    ///    multiplicatively — `pow(alignment, decay) * clamp01(quality)`
    ///    (`MatrixDeterminativeEngine.swift`, `rawScore`) — so an indicator observed on a
    ///    20%-complete section weighs measurably less than one on a full section. A section MISSING
    ///    from `sectionCompleteness` reads 0.0, never 1.0 (the B151 lesson: an inert 1.0 default is
    ///    fabricated certainty); quality 0 zeroing that indicator's contribution is acceptable ONLY
    ///    because, by construction of `Synthesizer.DatapointCompleteness`, a section that produced
    ///    an `EvidenceItem` has at least one conclusive datapoint and therefore completeness > 0 —
    ///    a missing/zero entry is itself a defect (asserted/logged in debug) and must attenuate,
    ///    not inflate.
    ///
    /// Clean-support indicators (`resolverAgreement`, `routeClean`, `trustedWitnessOK`,
    /// `localEvidenceSealValid`) are intentionally NOT wired here: the current evidence model emits
    /// items for *findings*, and the trusted-witness / evidence-seal signals that would affirm a
    /// clean pattern are not yet plumbed as `EvidenceId`s. Consequently a benign run reaches
    /// `.inconclusive` rather than `.clean` — the honest posture (inconclusive over unearned secure)
    /// until those affirmative-clean signals are wired.
    static func mapEvidenceToIndicators(_ evidence: [EvidenceItem],
                                        sectionCompleteness: [Section: Double]) -> [SyxxIndicatorInput] {
        // indicator -> (max strength, originating section)
        var best: [SyxxIndicator: (strength: Double, section: Section)] = [:]

        func note(_ indicator: SyxxIndicator, _ strength: Double, from section: Section) {
            if let existing = best[indicator] {
                if strength > existing.strength { best[indicator] = (strength, section) }
            } else {
                best[indicator] = (strength, section)
            }
        }

        for item in evidence {
            guard let (indicator, strength) = Self.indicatorMapping[item.evidenceId] else { continue }
            note(indicator, strength, from: item.section)
        }

        return best.map { (indicator, value) in
            // C063 — quality is the completeness of the SAME section as the winning observation
            // (the max-strength collapse carries the section with it), clamped [0,1]. Missing
            // entry ⇒ 0.0, never 1.0 (B151); present-but-zero alongside evidence is impossible by
            // DatapointCompleteness construction, hence the debug assertion.
            let rawCompleteness = sectionCompleteness[value.section]
            #if DEBUG
            if rawCompleteness == nil {
                FileHandle.standardError.write(Data(
                    "SyxxKit C063: evidence from section '\(value.section.rawValue)' has no completeness entry — quality forced to 0.0 (attenuates, never inflates). This is a defect; report it.\n".utf8))
            }
            #endif
            assert(rawCompleteness == nil || rawCompleteness! > 0,
                   "C063: section '\(value.section.rawValue)' produced evidence but reports completeness 0 — impossible by DatapointCompleteness construction")
            let quality = min(1.0, max(0.0, rawCompleteness ?? 0.0))
            // B176(d): same mapping invariant as above — an unmappable section is a programming
            // error, never silently redirected to .prophet.
            guard let mappedSection = SyxxSection(rawValue: value.section.rawValue) else {
                preconditionFailure("SynthesisEngine: indicator section '\(value.section.rawValue)' has no SyxxSection case")
            }
            return SyxxIndicatorInput(
                indicator: indicator,
                section: mappedSection,
                observedStrength: value.strength,
                quality: quality,
                contradictory: false,
                notes: nil
            )
        }
        // Stable order for deterministic bundles / parity.
        .sorted { $0.indicator.rawValue < $1.indicator.rawValue }
    }

    /// C064 — the reverse of `indicatorMapping`: the set of raw `EvidenceId`s that feed any of the
    /// given indicators. Surfaces use it so a named diagnosis is justified ONLY by observations
    /// that actually support it (`DiagnosisScore.supportingIndicators`), never by a raw-ledger
    /// prefix (the "spoofing headline because a privacy-leak line" disease). Pure and
    /// deterministic; indicators with no mapped `EvidenceId` simply contribute nothing.
    static func evidenceIds(supporting indicators: [IndicatorID]) -> Set<EvidenceId> {
        let wanted = Set(indicators)
        var out = Set<EvidenceId>()
        for (evidenceId, entry) in indicatorMapping where wanted.contains(entry.0.id) {
            out.insert(evidenceId)
        }
        return out
    }

    /// The unambiguous `EvidenceId` → (`SyxxIndicator`, strength) table. Anything not listed is
    /// deliberately unmapped (see `mapEvidenceToIndicators` doc). Strengths are conservative: a
    /// "confirmed" observation reads near-certain, an unconfirmed one moderate.
    static let indicatorMapping: [EvidenceId: (SyxxIndicator, Double)] = [
        // Prophet — answer authenticity
        // B092: the Dimension-confirmed variant arrives over ordinary TLS with no response
        // signature or pinned key. 0.95 overclaimed what the transport proved. 0.80 reflects
        // a corroborating second source (above the local-only 0.70, below cryptographic proof).
        .authoritativeDivergesConfirmed: (.authoritativeMismatch, 0.80),
        .authoritativeDiverges:          (.authoritativeMismatch, 0.70),
        .dnssecCanaryStripped:           (.signedAnswerMismatch, 0.80),

        // Portal — path / redirection
        .redirection:                    (.lanDnsRedirect, 0.90),
        .gatewayDhcpOffLinkDNS:          (.resolverPathAnomaly, 0.70),
        .captivePortalActive:            (.captivePortalSignal, 0.90),

        // Glutton — transport / privacy
        .dnsLeakConfirmed:               (.dnsLeakDetected, 0.95),
        .encryptedDnsBlocked:            (.forcedPlainDnsFallback, 0.85),
        .svcbStripped:                   (.protectedTransportBypass, 0.70),

        // Cage — live tampering patterns
        .nxSubdomainHijack:              (.suspiciousNxDomainPattern, 0.80),
        .nxCanaryHijack:                 (.suspiciousNxDomainPattern, 0.80),
        .denialInjected:                 (.liveQueryTamperPattern, 0.75),
        .scriptMismatch:                 (.liveQueryContentRewrite, 0.75),

        // Witness / host — tentative: DNS exfiltration implies a local process tunnelling out.
        // Kept at moderate strength and reviewed as tentative (see report).
        .exfilLikely:                    (.localEnvironmentCompromised, 0.60),
    ]
}
