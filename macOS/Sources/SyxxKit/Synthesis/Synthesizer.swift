import Foundation

/// A section adapter's output: the section's terminal status plus the evidence it produced.
public struct SectionEvidence: Sendable {
    public let status: SectionStatus
    public let evidence: [EvidenceItem]
    /// Exposure findings this section surfaces OUTSIDE the tamper result (see `Conclusion.advisories`).
    public let advisories: [Advisory]
    /// Ph3.2: fraction of expected probes that ran successfully (0..1). Passed through to the
    /// SectionStatusEntry so clean-support reliability scales with completeness.
    public let completeness: Double
    public init(status: SectionStatus, evidence: [EvidenceItem], advisories: [Advisory] = [], completeness: Double) {
        self.status = status
        self.evidence = evidence
        self.advisories = advisories
        self.completeness = max(0.0, min(1.0, completeness))
    }
}

/// Optional per-section results, gathered by whichever sections the CLI/GUI actually ran. A `nil`
/// field means that section was not run → `.untested`. `Synthesizer.conclude(from:)` turns this
/// into a signed-able `Conclusion`.
public struct SectionInputs: Sendable {
    public var resolution: DNSResponse?                 // Prophet
    public var dnssec: DNSSECStatus?
    public var nxCanary: DNSCanaries.CanaryResult?
    public var dnssecCanary: DNSCanaries.CanaryResult?
    public var latency: ResolverLatency?                // Prophet · datapoint A1
    public var stability: AnswerStability?              // Prophet · datapoint A9
    public var wildcardSynthesis: WildcardSynthesis?    // Prophet · datapoint F5
    public var denial: DenialOfExistence?               // Prophet · datapoint A6
    public var dualStack: DualStackAudit?               // Prophet · datapoint F8
    public var edns0: EDNS0Capability?                  // Prophet · datapoint F1
    public var versionBind: VersionBindResult?          // Prophet · datapoint F2 (contextual)
    public var stubEntropy: StubEntropyAudit?           // Prophet · datapoint F3 (capture-sourced, posture)
    public var comparison: ComparisonResult?            // Mirror
    /// The authoritative query context for the Stage-5 answer matrix — the domain/type Mirror
    /// actually queried; when nil, `conclude()` falls back to deriving from the first observed record.
    public var targetDomain: String?                    // Mirror · Stage-5 query context
    public var targetQueryType: UInt16?                 // Mirror · Stage-5 query context
    /// LEGACY warp result (WARP_ASSEMBLY / proxy path, retired) — kept as a compile shim for the
    /// Mirror View surface (owned by the UI stream). No longer populated; read `warpManifest`.
    public var warp: WarpResult?
    /// WARP_MANIFEST_V1 signed-manifest content-integrity result (the live warp result).
    public var warpManifest: WarpManifestResult?
    public var authoritative: AuthoritativeComparison?  // Mirror · datapoint A2
    public var svcb: SVCBFinding?                        // Mirror · datapoint A3
    public var ttlAnomaly: TTLAnomaly?                   // Mirror · datapoint F4
    public var cnameChain: CNAMEChainAudit?              // Mirror · datapoint F7
    public var serverGroundTruth: GroundTruth?          // Mirror · A2 server corroboration (Pro, additive)
    public var network: NetworkStateReport?             // Portal
    public var encryptedTransport: EncryptedTransport?  // Portal · datapoint A8
    /// The OBSERVED DNS-transport state as it was during THIS section's own query window — the
    /// third transport question (`DNSTransportState.swift`), distinct from the configured question
    /// (`network.encryptedDNS`) and the reachability question (`encryptedTransport`). The three can
    /// legitimately disagree and a disagreement is itself a finding.
    ///
    /// Shared INPUT, not a shared decision. Prophet and Mirror both read this because a cleartext
    /// path is what makes on-path answer rewriting possible, but they ask different questions —
    /// Prophet symmetric peer consensus across its oracles, Mirror asymmetric local-versus-baseline
    /// — so each draws its own conclusion from these facts. Nothing routes them through one
    /// decision function.
    ///
    /// Correlated to the section's scan interval, never read live at redraw: a pane showing the
    /// current transport beside results gathered under a different one is a fabricated correlation.
    /// `nil` means the state was not observed for this run, which is undetermined, not "cleartext".
    public var transportObservation: DNSTransportWindow?  // Prophet + Mirror · shared observation
    public var openResolver: OpenResolverFinding?       // Portal · open-resolver / amplification (advisory)
    public var leak: LeakTestResult?                    // Glutton
    public var ecs: ECSLeak?                            // Glutton · datapoint F10
    public var exfil: [ExfilAssessment]?                // Cage
    public var responseSize: ResponseSizeAudit?         // Cage · datapoint F9 (capture-sourced, telemetry)
    /// C058 — fraction (0...1) of Cage capture records that carried a real process identity, or `nil`
    /// when this was not a capture-sourced run. Threaded to `EvidenceAdapters.cage(attributionRate:)`
    /// by `conclude` so a mostly-unattributed capture is DEGRADED, never a silent clean.
    public var cageAttributionRate: Double?             // Cage · C058 attribution completeness
    public var drifts: [AnswerDrift]?                   // Witness
    public var configChanges: [ConfigChange]?
    /// B174: true when the system resolver was undetectable and a public substitute (e.g. 8.8.8.8)
    /// was used in its place. Degrades the run — a comparison against a public vantage when "local"
    /// IS a public vantage measures nothing the Mirror section can distinguish from clean.
    public var resolverSubstituted: Bool = false
    public init() {}
}

/// Maps each section's typed result into `(SectionStatus, [EvidenceItem])` — the DoW #1 "what does
/// each datapoint evidence" table, as code. Weights are first-pass ⟨corroborate⟩ values.
public enum EvidenceAdapters {

    /// Roll sub-signal statuses into one section status. An invalidated premise takes precedence
    /// (B062: evidence that cannot be trusted must suppress any positive finding from the same
    /// section). Otherwise a positive sub-finding makes the section positive; a section with
    /// nothing that ran is untested; otherwise take the strongest present.
    static func combine(_ subs: [SectionStatus]) -> SectionStatus {
        let ran = subs.filter { $0 != .untested }
        if ran.isEmpty { return .untested }
        if ran.contains(.invalidated) { return .invalidated }
        if subs.contains(.positive) { return .positive }
        if ran.contains(.inconclusive) { return .inconclusive }
        if ran.contains(.degraded) { return .degraded }
        return .negative
    }

    public static func prophet(resolution: DNSResponse?, dnssec: DNSSECStatus?,
                               nxCanary: DNSCanaries.CanaryResult?, dnssecCanary: DNSCanaries.CanaryResult?,
                               latency: ResolverLatency? = nil, stability: AnswerStability? = nil,
                               wildcardSynthesis: WildcardSynthesis? = nil,
                               denial: DenialOfExistence? = nil,
                               dualStack: DualStackAudit? = nil,
                               edns0: EDNS0Capability? = nil,
                               versionBind: VersionBindResult? = nil,
                               stubEntropy: StubEntropyAudit? = nil) -> SectionEvidence {
        var ev: [EvidenceItem] = []; var subs: [SectionStatus] = []
        // B161: split "no answer at all" (timeout/transport/malformed ⇒ invalidated) from "an answer
        // with a refusal RCODE" (servFail/refused ⇒ inconclusive + emit the resolverStatus evidence
        // item). Previously, didNotSucceed (then named didFail) caught .servFail/.refused first, so
        // the switch arm below was unreachable for those RCODEs — the resolverStatus signal was
        // discarded before it reached the ledger or the engine, and a valid parsed refusal was filed
        // as invalidated ("ran but its premise failed") instead of inconclusive ("ran, valid data,
        // unresolved").
        if let r = resolution {
            switch r.status {
            case .refused, .servFail:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .resolverStatus,
                    observation: "Resolver returned \(r.status.rawValue) for the target domain",
                    rationale: "A refusal / failure / NXDOMAIN can be a block — ambiguous alone; corroborate with the NXDOMAIN canary."))
                subs.append(.inconclusive)
            case .nxDomain:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .resolverStatus,
                    observation: "Resolver returned \(r.status.rawValue) for the target domain",
                    rationale: "A refusal / failure / NXDOMAIN can be a block — ambiguous alone; corroborate with the NXDOMAIN canary."))
                subs.append(.inconclusive)
            case .timeout, .transportError, .malformedResponse, .responseIdMismatch, .truncated:
                subs.append(.invalidated)
            default: subs.append(.negative)
            }
        }
        if let d = dnssec {
            switch d {
            case .signatureExpiredOrNotYetValid:
                // B050: an expired / not-yet-valid RRSIG on the TARGET is ambiguous — replay OR a benign
                // broken/misconfigured signer — so it is a run-quality caveat (DEGRADED), never a
                // standalone tampering finding (.positive). It lowers confidence and blocks an unearned
                // clean, but cannot NAME a threat alone. The B047 control-canary escalation is untouched:
                // a known-signed CONTROL returning unsigned (dnssecCanary below) is on-path DNSSEC
                // stripping and stays .positive.
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .dnssecExpired,
                    observation: "DNSSEC signatures for the target are expired / not-yet-valid",
                    rationale: "Stale RRSIGs can indicate replay or a broken / interfering signer."))
                subs.append(.degraded)
            case .recordsPresentUnverified: subs.append(.negative)
            case .unsigned, .inconclusive: subs.append(.inconclusive)
            }
        }
        for c in [nxCanary, dnssecCanary].compactMap({ $0 }) {
            if let e = c.evidence { ev.append(e) }
            subs.append(c.status)
        }
        // A1 — resolver latency is a run-quality overlay only: it never argues a threat, it just
        // reduces confidence when the resolver is slow/flaky/unreachable (no EvidenceItem emitted).
        if let lat = latency {
            switch lat.status {
            case .slow, .degraded, .unreachable: subs.append(.degraded)
            case .fast, .moderate:               subs.append(.negative)
            case .untested:                      break
            }
        }
        // A9 — answer stability. Only a fully-flapping set (no shared member) is a corroborate-grade
        // finding; rotation/stability are clean, a TTL anomaly is a quality knock, few samples inconclusive.
        if let st = stability {
            switch st.status {
            case .unstable:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .answerUnstable,
                    observation: "Resolver's answer set changed with no shared address across \(st.usableCount) samples",
                    rationale: "Answers that flap with nothing in common can be intermittent poisoning or on-path interference — corroborate against the authoritative and consensus checks."))
                subs.append(.positive)
            case .ttlAnomaly:
                // E017: emit an EvidenceItem for TTL anomaly so the degraded knock is explained
                // (parity with Windows which emits AnswerUnstable for unstable).
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .answerUnstable,
                    observation: "Resolver's TTL values are anomalous across \(st.usableCount) samples",
                    rationale: "Unexpected TTL patterns can indicate caching interference or on-path manipulation — corroborate against the authoritative and consensus checks."))
                subs.append(.degraded)
            case .stable, .rotating: subs.append(.negative)
            case .inconclusive:      subs.append(.inconclusive)
            case .untested:          break
            }
        }
        // F5 — cross-vantage NXDOMAIN hijack for the target's own namespace. Only a confirmed
        // synthesis (a trusted resolver denies, this resolver invents) is a finding; a wildcard
        // zone or an honest denial is clean.
        if let ws = wildcardSynthesis {
            switch ws.status {
            case .nxHijack:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .nxSubdomainHijack,
                    observation: "Resolver invented address(es) for nonexistent subdomains of the target that a trusted resolver denies"
                        + (ws.syntheticAddresses.isEmpty ? "" : " (→ \(ws.syntheticAddresses.prefix(2).joined(separator: ", ")))"),
                    rationale: "A resolver fabricating answers for names that provably do not exist is hijacking NXDOMAIN to a block/ad page, aimed at this domain's namespace."))
                subs.append(.positive)
            case .wildcardZone, .noSynthesis: subs.append(.negative)
            case .inconclusive:               subs.append(.inconclusive)
            case .untested:                   break
            }
        }
        // A6 — authenticated denial of existence. A contradictory/wrong-zone denial is a strong
        // injected-negative signal; a bare denial (no SOA/NSEC) is weak corroboration; a properly
        // SOA/NSEC-backed negative is clean.
        if let dx = denial {
            switch dx.status {
            case .contradictory:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .denialInjected,
                    observation: "Negative answer for \(dx.queriedName) is inconsistent — \(dx.detail)",
                    rationale: "A negative that carries answers, or is signed by an SOA outside the name's zone, is a fabricated denial — a censor/middlebox pretending the name does not exist."))
                subs.append(.positive)
            case .bareDenial:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .denialBare,
                    observation: "Negative answer for \(dx.queriedName) carries no SOA or NSEC — bare denial",
                    rationale: "A genuine authoritative negative includes an SOA (and NSEC in a signed zone); a bare denial can be a synthesised block — corroborate."))
                // B058: single-vantage SOA-absence cannot distinguish "resolver strips SOA" from
                // "injected bare denial" — degraded, not positive. Requires corroboration.
                subs.append(.degraded)
            case .nsecProven, .soaBacked: subs.append(.negative)
            // B061: .notADenial means the check does not apply (the name resolved) — absence of
            // applicability is not cleanliness. Emit no sub-status; skip this datapoint entirely.
            case .notADenial:           break
            case .inconclusive:         subs.append(.inconclusive)
            case .untested:                            break
            }
        }
        // F8 — dual-stack audit. Only a suppressed A record (AAAA works, A doesn't) is a weak signal;
        // dual-stack, IPv4-only, and NAT64/DNS64 synthesis are all benign (DNS64 explicitly so).
        if let ds = dualStack {
            switch ds.status {
            case .ipv4Suppressed:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .ipv4Suppressed,
                    observation: "AAAA resolves but the A record does not for the target",
                    rationale: "A missing IPv4 record while IPv6 resolves can be selective IPv4 blocking — weak; a domain may also legitimately publish only AAAA, so corroborate."))
                subs.append(.positive)
            case .dualStack, .ipv4Only, .dns64Synthesis: subs.append(.negative)
            case .inconclusive:                          subs.append(.inconclusive)
            case .untested:                              break
            }
        }
        // F1 — EDNS0 capability. A middlebox dropping OPT (broken) is a weak interception signal; a
        // legacy resolver (notSupported) is surfaced as a DEGRADED caveat — never a silent clean pass;
        // a working EDNS0 path is benign.
        if let ed = edns0 {
            switch ed.status {
            case .broken:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .ednsBlocked,
                    observation: "EDNS0 (OPT) queries are dropped on this path while plain queries work",
                    rationale: "A network device stripping EDNS0 is interfering with DNS at the protocol level (it also breaks DNSSEC and large answers) — corroborate."))
                subs.append(.positive)
            case .notSupported: subs.append(.degraded)   // legacy resolver — surfaced, not rendered clean
            case .supported:    subs.append(.negative)
            case .inconclusive: subs.append(.inconclusive)
            case .untested:     break
            }
        }
        // F2 — CHAOS version.bind. Contextual only: it improves attribution, it never decides. So it is
        // TILT-ONLY — it emits a weak interception evidence item (weight 0.4) when a would-be public
        // recursive answers with a local-forwarder banner, but it does NOT change the section status
        // (no `.positive`, no gate). A withheld/undisclosed banner or a failed probe contributes nothing.
        if let vb = versionBind, vb.forwarderOnPublicPath, let banner = vb.banner {
            ev.append(EvidenceItem(section: .prophet,
                evidenceId: .resolverBannerAnomaly,
                observation: "\(vb.server) is expected to be a public recursive but answered version.bind with a local-forwarder banner (\(banner))",
                rationale: "A big upstream resolver does not identify as dnsmasq/pi-hole/router software — a local device is answering on the path. Contextual: it aids attribution/triage and corroborates other interception signals, but never decides on its own."))
        }
        // F3 — TXID/source-port entropy (capture-sourced). Weak randomization is a *spoofability posture*,
        // surfaced as a DEGRADED caveat (lowers confidence, never a silent clean) plus a weak spoofing
        // tilt — never a threat finding on its own. insufficientData is inconclusive (never clean).
        if let se = stubEntropy {
            switch se.status {
            case .weakRandomization:
                ev.append(EvidenceItem(section: .prophet,
                    evidenceId: .weakStubEntropy,
                    observation: "Local stub/OS DNS randomization is weak — \(se.detail)",
                    rationale: "Low source-port / transaction-ID entropy on the LOCAL resolver path makes this host easier to spoof — a spoofability posture, not proof of upstream cache poisoning. Weak and contextual: corroborate with the spoofing/interception checks."))
                subs.append(.degraded)
            case .wellRandomized:   subs.append(.negative)
            case .insufficientData: subs.append(.inconclusive)
            case .untested:         break
            }
        }
        return SectionEvidence(status: combine(subs), evidence: ev, completeness: DatapointCompleteness(subs, totalDatapoints: ProphetDatapointCatalog))
    }

    public static func mirror(comparison: ComparisonResult?, warp warpManifest: WarpManifestResult?,
                              authoritative: AuthoritativeComparison? = nil,
                              svcb: SVCBFinding? = nil,
                              ttlAnomaly: TTLAnomaly? = nil,
                              cnameChain: CNAMEChainAudit? = nil,
                              serverGroundTruth: GroundTruth? = nil) -> SectionEvidence {
        var ev: [EvidenceItem] = []; var subs: [SectionStatus] = []
        if let c = comparison {
            if c.isInconclusive { subs.append(.inconclusive) }
            else if c.isRedirectionDetected {
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .redirection,
                    observation: "Local resolver's answer diverges from the public-resolver consensus",
                    rationale: "You received a different answer than the rest of the internet — redirection / poisoning, unless a captive portal explains it."))
                subs.append(.positive)
            } else if c.isDegraded { subs.append(.degraded) }
            else if c.localRecords.isEmpty && c.publicResolverResults.isEmpty {
                // A comparison with NO local records and NO public results compared NOTHING. The
                // flags are all false because nothing was observed — not because the answers agreed.
                // Falling through to `.negative` here made zero cross-vantage data contribute CLEAN
                // SUPPORT: the engine's own "Mirror ran and found nothing wrong" for a Mirror that
                // never ran. That is a false clean produced from an empty set, and SynthesizerTests
                // pinned it as the expected contract (audit 2026-07-16, ROADMAP TOP-TEN #1).
                //
                // Compare EvidenceAdapters.cage / MonitorReportAnalyzer, which already return nil
                // (=> untested) for an empty capture rather than a clean one. Mirror now matches.
                subs.append(.inconclusive)
            }
            else { subs.append(.negative) }
        }
        // WARP_MANIFEST_V1: drive the sub-status from the signed-manifest CONCLUSION lattice (§5).
        // TAMPERING_DETECTED is positive interference evidence; INCONCLUSIVE never renders clean;
        // VERIFIED_MATCH is clean support. Missing/unverifiable data is never a silent pass.
        if let w = warpManifest {
            switch w.conclusion {
            case .tamperingDetected:
                let mismatched = w.assets.first(where: { $0.state == .mismatch })?.url
                let detail: String
                if w.htmlState == .mismatch {
                    detail = "Page HTML bytes differ from the signed clean manifest"
                } else if w.signatureState == .invalidSignature {
                    detail = "The manifest signature is present but does not verify"
                } else if let mismatched {
                    detail = "Asset bytes differ from the signed manifest: \(mismatched)"
                } else {
                    detail = "Content bytes differ from the signed manifest"
                }
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .scriptMismatch,
                    observation: detail,
                    rationale: "A verified, fresh signed manifest disagrees byte-for-byte with what your vantage fetched (or the manifest signature failed) — evidence of on-path content interference. Benign per-request/dynamic content is possible; see the diff before concluding."))
                subs.append(.positive)
            case .inconclusive:
                // C199: distinct EvidenceId from confirmed tampering — inconclusive warp must not
                // carry the same weight as a verified content mismatch in the indicator mapping.
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .scriptMismatchInconclusive,
                    observation: "Content warp check was inconclusive" + (w.inconclusiveReason.map { ": \($0)" } ?? ""),
                    rationale: "An inconclusive warp (untrusted/unpinned signer, stale/forged freshness, or unfetchable content) could not determine whether content was modified — not a clean result; requires corroboration."))
                subs.append(.inconclusive)
            case .verifiedMatch:
                subs.append(.negative)
            }
        }
        // A2 — authoritative ground truth. A full divergence is real evidence of an upstream rewrite;
        // a match is clean; anything ambiguous is inconclusive. CDN/geo routing is NOT dampened
        // upstream: `AuthoritativeVantage.classify` RETAINS `.diverges` when both answer sets sit in
        // one provider's ranges (C015/K-16) — it annotates the provider rather than erasing the
        // finding, because an attacker may sit inside that provider's own ASN. The rationale emitted
        // below states that limit rather than claiming CDN/geo has been ruled out.
        if let a = authoritative {
            switch a.status {
            case .diverges:
                // Additive Pro corroboration: when the Dimension server's untampered ground truth ALSO
                // shows the resolver's answers are not served by the authority, upgrade to a DECISIVE
                // server-confirmed finding. Absent/unreachable/non-confirming server → the local finding
                // stands exactly as before (offline/LITE path unchanged).
                let serverConfirmed: Bool = serverGroundTruth.map { gt in
                    let truth = Set((gt.authoritative?.answers ?? []).map { $0.lowercased() })
                    let seen = Set(a.resolverAnswers.map { $0.lowercased() })
                    return gt.result == "authoritative" && !truth.isEmpty && !seen.isEmpty && seen.isDisjoint(with: truth)
                } ?? false
                if serverConfirmed {
                    // B092: the Dimension response arrives over ordinary TLS with no response
                    // signature or pinned key. "Near-proof" / "independent, untampered" overclaims
                    // what the transport actually proved — a compromised CA, TLS interceptor, or
                    // Dimension service could forge this. Report the corroboration honestly as a
                    // second source, not as cryptographic proof.
                    ev.append(EvidenceItem(section: .mirror,
                        evidenceId: .authoritativeDivergesConfirmed,
                        observation: "Recursive answer diverges from the domain's authoritative nameservers — corroborated by Dimension server"
                            + (a.nameservers.isEmpty ? "" : " (\(a.nameservers.prefix(2).joined(separator: ", ")))"),
                        rationale: "The Dimension server also reports the authority does not serve the address your resolver returned — corroborates an upstream rewrite over TLS, but the response is unsigned and should be independently verified."))
                } else {
                    // K-16 COUPLING — the rationale below is load-bearing on the classifier.
                    // It used to end "(benign CDN/geo routing is already excluded for a full
                    // divergence)". That was never true on this platform: `classify` RETAINS
                    // `.diverges` when both answer sets lie in one provider's ranges (C015),
                    // annotating the provider instead of softening, because an attacker may sit
                    // inside the provider's own ASN. A `.diverges` can therefore legitimately carry
                    // a shared provider, so the old claim told the user CDN/geo had been ruled out
                    // when it had not. The wording states what is actually established, and matches
                    // the C# twin at Syxx.Kit/Synthesis/Synthesizer.cs:488 verbatim.
                    ev.append(EvidenceItem(section: .mirror,
                        evidenceId: .authoritativeDiverges,
                        observation: "Recursive answer diverges from the domain's authoritative nameservers"
                            + (a.nameservers.isEmpty ? "" : " (\(a.nameservers.prefix(2).joined(separator: ", ")))"),
                        rationale: "The domain's own authoritative servers do not serve the address your resolver returned — evidence of an upstream rewrite. CDN/geo routing can also produce this shape, including when both answers sit in one provider's ranges, so corroborate before concluding."))
                }
                subs.append(.positive)
            case .matches:      subs.append(.negative)
            case .inconclusive: subs.append(.inconclusive)
            case .untested:     break
            }
        }
        // A3 — SVCB/HTTPS stripping. Only DIVERGENCE across modern resolvers is a signal; present/absent
        // consistently is clean; no usable answer is inconclusive.
        if let s = svcb {
            switch s.status {
            case .divergent:
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .svcbStripped,
                    observation: "HTTPS/SVCB record served by \(s.presentAt.count) resolver(s) but returned by none at \(s.absentAt.count)",
                    rationale: "Inconsistent HTTPS (type 65) records across modern resolvers can indicate SVCB/ECH stripping that silently downgrades transport — corroborate."))
                subs.append(.positive)
            case .present, .absent: subs.append(.negative)
            case .inconclusive:     subs.append(.inconclusive)
            case .untested:         break
            }
        }
        // F4 — cross-vantage TTL anomalies. Lockstep TTLs across independent resolvers or an
        // implausibly-pinned TTL are weak corroboration; ordinary variance is clean.
        if let t = ttlAnomaly {
            switch t.status {
            case .lockstepTTL:
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .ttlLockstep,
                    observation: "Independent resolvers report the identical TTL for the target — not independent caching",
                    rationale: "Independent recursive resolvers normally hold different remaining TTLs; identical TTLs suggest a shared upstream or an on-path device serving one answer to all — corroborate."))
                subs.append(.positive)
            case .extremeTTL:
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .ttlExtreme,
                    observation: "A resolver reports an implausibly large TTL (> 7 days) for the target",
                    rationale: "A TTL far beyond normal can be a pinned/stale answer held past its validity — a possible replay; corroborate."))
                subs.append(.positive)
            case .independentCaching: subs.append(.negative)
            case .inconclusive:       subs.append(.inconclusive)
            case .untested:           break
            }
        }
        // F7 — CNAME-chain divergence from a unanimous reference consensus (the resolver is the outlier).
        if let cc = cnameChain {
            switch cc.status {
            case .divergent:
                ev.append(EvidenceItem(section: .mirror,
                    evidenceId: .cnameChainDivergent,
                    observation: "Resolver's CNAME chain differs from a unanimous trusted-reference chain",
                    rationale: "When trusted resolvers agree on the CNAME path but yours takes a different one, a mid-chain rewrite is possible — corroborate (a split-horizon/geo edge case can also cause it)."))
                subs.append(.positive)
            case .consistent, .noCNAME: subs.append(.negative)
            case .inconclusive:         subs.append(.inconclusive)
            case .untested:             break
            }
        }
        return SectionEvidence(status: combine(subs), evidence: ev, completeness: DatapointCompleteness(subs, totalDatapoints: MirrorDatapointCatalog))
    }

    public static func portal(report: NetworkStateReport?, encryptedTransport: EncryptedTransport? = nil,
                              openResolver: OpenResolverFinding? = nil) -> SectionEvidence {
        var ev: [EvidenceItem] = []; var subs: [SectionStatus] = []; var advisories: [Advisory] = []
        if let r = report {
        switch r.captivePortal.state {
        case .yes:
            ev.append(EvidenceItem(section: .portal,
                evidenceId: .captivePortalActive,
                observation: "A captive portal is active on this network",
                rationale: "Captive portals legitimately rewrite DNS before login — a benign explanation for apparent redirection."))
            subs.append(.degraded)
        case .unknown: subs.append(.inconclusive)
        case .no: subs.append(.negative)
        }
        switch r.encryptedDNS.state {
        case .no:
            ev.append(EvidenceItem(section: .portal,
                evidenceId: .dnsUnencrypted,
                observation: "System DNS is unencrypted (Do53)",
                rationale: "Cleartext DNS on port 53 can be observed and altered by your network or ISP."))
            subs.append(.negative)
        case .yes:
            ev.append(EvidenceItem(section: .portal,
                evidenceId: .dnsEncrypted,
                observation: "System DNS is encrypted (DoH/DoT)",
                rationale: "Encrypted DNS (DoH/DoT) is active, which prevents cleartext snooping or transparent redirection on port 53."))
            subs.append(.negative)
        case .unknown: subs.append(.inconclusive)
        }
        }
        // C1 — router/gateway DNS audit. The decisive rogue shape is DHCP pushing a PRIVATE IPv4
        // resolver that is neither the gateway nor on the gateway's own /24 — the classic
        // rogue-DHCP / hijacked-router redirect. ISP-pushed PUBLIC resolvers are normal and never
        // flagged, and IPv6 pushes are skipped (a dual-stack router pushing its own ULA is benign
        // and off-subnet judgment needs prefix knowledge we do not have). A failed detection is
        // inconclusive, never silently clean; an audited-clean gateway is a real negative datapoint.
        if let r = report, let gw = r.gateway {
            if gw.ip.isEmpty {
                subs.append(.inconclusive)
            } else {
                let offLink = gw.dhcpPushedDNS.filter { pushed in
                    pushed != gw.ip
                        && pushed.contains(".")
                        && IPClassifier.classify(pushed) == .privateUse
                        && !ipv4SameSlash24(pushed, gw.ip)
                }
                if !offLink.isEmpty {
                    ev.append(EvidenceItem(section: .portal,
                        evidenceId: .gatewayDhcpOffLinkDNS,
                        observation: "DHCP pushed a private DNS server off your gateway's subnet: \(offLink.joined(separator: ", "))",
                        rationale: "Your router (or something impersonating it) directed DNS to a private address that is not the gateway and not on its subnet — the shape of a rogue-DHCP or hijacked-router redirect."))
                    subs.append(.positive)
                } else {
                    subs.append(.negative)
                }
            }
        }
        // A8 — active encrypted-transport health. A blocked DoH path (network forcing cleartext) is a
        // weak interception signal; a working path is clean; a reached-but-odd answer is inconclusive.
        if let et = encryptedTransport {
            switch et.status {
            case .blocked:
                ev.append(EvidenceItem(section: .portal,
                    evidenceId: .encryptedDnsBlocked,
                    observation: "Active DoH probe to \(et.endpoint) was blocked — the encrypted-DNS path does not complete",
                    rationale: "A network that blocks encrypted DNS forces resolution to cleartext, where it can be observed or altered — corroborate with the encryption-state check."))
                subs.append(.positive)
            case .working:      subs.append(.negative)
            case .inconclusive: subs.append(.inconclusive)
            case .untested:     break
            }
        }
        // Open Resolver & Amplification — an EXPOSURE advisory, deliberately kept OUT of the tamper result
        // (it says "your infra is exposable", not "you were tampered with"). Emits an Advisory, never
        // evidence and never a section-status change. notRecursive/untested contribute nothing.
        if let orf = openResolver {
            switch orf.status {
            case .recursiveHighAmp:
                advisories.append(Advisory(section: .portal, advisoryId: .openResolverExposure,
                    title: "Possible open-resolver / amplification exposure — \(orf.target)",
                    detail: orf.detail, severity: .high))
            case .recursiveLowAmp:
                advisories.append(Advisory(section: .portal, advisoryId: .openResolverExposure,
                    title: "Recursive resolver on your network — \(orf.target)",
                    detail: orf.detail, severity: .info))
            case .notRecursive, .untested: break
            }
        }
        // B176(c): `ev` is provably empty when `subs` is empty — every evidence-emitting branch
        // above also appends to `subs`. Assert the invariant so a future code change that breaks
        // this coupling is caught in debug builds rather than silently discarding evidence.
        if subs.isEmpty && advisories.isEmpty {
            assert(ev.isEmpty, "B176(c): ev non-empty with empty subs — evidence would be silently discarded")
            return SectionEvidence(status: .untested, evidence: ev, completeness: 0.0)
        }
        if subs.isEmpty { return SectionEvidence(status: .untested, evidence: ev, advisories: advisories, completeness: 0.0) }
        return SectionEvidence(status: combine(subs), evidence: ev, advisories: advisories, completeness: DatapointCompleteness(subs, totalDatapoints: PortalDatapointCatalog))
    }

    public static func glutton(leak: LeakTestResult?, ecs: ECSLeak? = nil) -> SectionEvidence {
        var ev: [EvidenceItem] = []; var subs: [SectionStatus] = []
        if let l = leak {
            switch l.result {
            case .confirmed:
                ev.append(EvidenceItem(section: .glutton,
                    evidenceId: .dnsLeakConfirmed,
                    observation: "Collector confirmed DNS reached an unexpected resolver" + (l.observedResolvers.isEmpty ? "" : " (\(l.observedResolvers.joined(separator: ", ")))"),
                    rationale: "Your DNS is leaking outside the tunnel, or to a resolver you didn't choose."))
                subs.append(.positive)
            case .notDetected:   subs.append(.negative)
            // B164: .inconclusive without a signature means the leak test never had a collector
            // to talk to (ProbePlan.runGlutton synthesises this for every collector-less run).
            // That is "untested" — the probe never executed — not "invalidated" (ran but corrupt).
            case .inconclusive:  subs.append(l.signature == nil ? .untested : .inconclusive)
            case .invalid:       subs.append(.invalidated)
            case .error:         subs.append(.invalidated)
            }
        }
        // F10 — ECS forwarding is a privacy exposure (your subnet reaches authoritatives); stripping it is clean.
        if let e = ecs {
            switch e.status {
            // B138: the ECS probe sends a synthetic test subnet (203.0.113.0), so a non-zero
            // echoed scope proves ECS CAPABILITY, not that the user's real subnet was forwarded.
            // A confirmed privacy leak requires paired plain/ECS comparison or collector-grounded
            // evidence. Report capability honestly; do not promote to a privacy-leak claim.
            case .forwarded:
                ev.append(EvidenceItem(section: .glutton,
                    evidenceId: .ecsSubnetLeak,
                    observation: "Resolver honours EDNS Client Subnet and echoed a scope" + (e.scopePrefixLength.map { " (/\($0))" } ?? "") + " for the synthetic test subnet",
                    rationale: "The resolver processes ECS options — a capability that could expose your subnet to authoritative servers if your real prefix is forwarded. Confirming an actual leak requires a paired plain/ECS query or a collector observation."))
                subs.append(.positive)
            case .notForwarded: subs.append(.negative)
            case .inconclusive: subs.append(.inconclusive)
            case .untested:     break
            }
        }
        if subs.isEmpty { return SectionEvidence(status: .untested, evidence: [], completeness: 0.0) }
        return SectionEvidence(status: combine(subs), evidence: ev, completeness: DatapointCompleteness(subs, totalDatapoints: GluttonDatapointCatalog))
    }

    // C058 — expert-selected attribution-completeness floor. Below this fraction of capture records
    // carrying a real process identity, the process-initiator signal (a GroundTruth Medium tampering
    // signal) is too sparse for the section to read a silent clean: it is forced to DEGRADED with a
    // disclosing evidence item. 0.5 = "at least half of the events could be attributed"; chosen
    // conservatively and applied identically on both platforms (Synthesizer.cs mirror).
    static let CageAttributionThreshold: Double = 0.5

    /// - Parameter attributionRate: fraction (0...1) of capture records that carried a real process
    ///   identity, or `nil` when not applicable (not a capture-sourced run). Optional/defaulted so
    ///   existing call sites compile unchanged until the app surfaces are wired (Phase 2).
    public static func cage(exfil: [ExfilAssessment]?, responseSize: ResponseSizeAudit? = nil,
                            attributionRate: Double? = nil) -> SectionEvidence {
        if exfil == nil && responseSize == nil { return SectionEvidence(status: .untested, evidence: [], completeness: 0.0) }
        var ev: [EvidenceItem] = []; var subs: [SectionStatus] = []
        if let a = exfil {
            if a.isEmpty {
                subs.append(.negative)   // capture ran, nothing suspicious
            } else {
                for x in a {
                    switch x.suspicion {
                    case .likelyExfil, .suspicious:
                        ev.append(EvidenceItem(section: .cage,
                            evidenceId: x.suspicion == .likelyExfil ? .exfilLikely : .exfilSuspicious,
                            observation: "\(x.domain): \(x.suspicion.rawValue) (entropy \(String(format: "%.1f", x.entropyBitsPerChar)) b/char)",
                            rationale: x.rationale.isEmpty ? "High-entropy / beaconing DNS traffic." : x.rationale.joined(separator: "; ")))
                        subs.append(.positive)
                    case .benign: subs.append(.negative)
                    case .inconclusive: subs.append(.inconclusive)
                    }
                }
            }
        }
        // F9 — response-size / amplification (capture-sourced). A sustained large-response pattern is
        // surfaced as a DEGRADED caveat (lowers confidence, never a silent clean) plus a weak
        // localCompromise tilt; big DNSSEC/TXT answers are also legitimately large, so it corroborates
        // the exfil check and never decides. insufficientData is inconclusive; normal is clean.
        if let rs = responseSize {
            switch rs.status {
            case .oversized:
                ev.append(EvidenceItem(section: .cage,
                    evidenceId: .responseSizeAnomaly,
                    observation: "Sustained large DNS responses observed — \(rs.detail)",
                    rationale: "A run of unusually large responses can be amplification or covert DNS tunnelling (large TXT/NULL answers). Weak and contextual — big DNSSEC/TXT answers are also legitimately large — so it corroborates the exfil check, never decides alone."))
                subs.append(.degraded)
            case .normal:           subs.append(.negative)
            case .insufficientData: subs.append(.inconclusive)
            case .untested:         break
            }
        }
        // C058 — process-attribution completeness. When the capture could attribute a process identity
        // to too few records, the process-initiator signal (a Medium GroundTruth tampering signal) was
        // effectively not evaluated across the capture; a section that combined to a silent clean here
        // would be claiming tampering was ruled out on a dimension it could not see. A below-threshold
        // rate forces DEGRADED (never a bare clean) with a disclosing evidence item; an adequate rate is
        // recorded as an evaluated negative datapoint. `nil` = not a capture-sourced run → no datapoint.
        if let rate = attributionRate {
            if rate < CageAttributionThreshold {
                ev.append(EvidenceItem(section: .cage,
                    evidenceId: .attributionIncomplete,
                    observation: "Process attribution incomplete — \(Int((rate * 100).rounded()))% of capture records carried a process identity",
                    rationale: "Without a process initiator for most events, on-host tampering whose only distinguishing signal is its originating process cannot be ruled out — the Cage section is degraded, not clean."))
                subs.append(.degraded)
            } else {
                subs.append(.negative)
            }
        }
        if subs.isEmpty { return SectionEvidence(status: .untested, evidence: ev, completeness: 0.0) }
        // Cage's datapoint set is data-driven (one per assessed domain + the size audit + the
        // attribution-completeness slot when a capture supplied one).
        // When exfil is non-nil (even if empty, meaning the assessment ran), it is at least 1 datapoint.
        let cageDatapoints = (exfil != nil ? max(exfil!.count, 1) : 0)
            + (responseSize != nil ? 1 : 0)
            + (attributionRate != nil ? 1 : 0)
        return SectionEvidence(status: combine(subs), evidence: ev, completeness: DatapointCompleteness(subs, totalDatapoints: cageDatapoints))
    }

    // B160: nil inputs mean "no prior baseline / unreadable baseline" (untested); non-nil but
    // empty means "baseline loaded and diffed, no drift" (negative with completeness 1.0).
    // The caller (SyxxCLI.swift:783-787) MUST pass nil when there is no prior baseline so this
    // adapter returns .untested rather than fabricating a clean check from an empty comparison.
    public static func witness(drifts: [AnswerDrift]?, configChanges: [ConfigChange]?) -> SectionEvidence {
        if drifts == nil && configChanges == nil { return SectionEvidence(status: .untested, evidence: [], completeness: 0.0) }
        var ev: [EvidenceItem] = []; var subs: [SectionStatus] = []
        for d in drifts ?? [] {
            ev.append(EvidenceItem(section: .witness,
                evidenceId: .answerDrift,
                observation: "\(d.domain) answer changed: \(d.previousAnswer) → \(d.newAnswer)",
                rationale: "An answer that drifted from your trusted baseline may be new tampering (or a legitimate change — corroborate)."))
            subs.append(.positive)
        }
        for c in configChanges ?? [] {
            ev.append(EvidenceItem(section: .witness,
                evidenceId: .configChange,
                observation: "Network config changed: \(c.kind.rawValue) — \(c.detail)",
                rationale: "An unexplained change to your resolver / VPN / encryption state warrants a re-audit."))
            subs.append(.inconclusive)
        }
        if subs.isEmpty { return SectionEvidence(status: .negative, evidence: [], completeness: 1.0) }
        return SectionEvidence(status: combine(subs), evidence: ev, completeness: DatapointCompleteness(subs, totalDatapoints: subs.count))
    }

    /// C1 — true only when both strings are IPv4 dotted-quads sharing their first three octets.
    /// Non-IPv4 input is never "same subnet" (the caller skips IPv6 separately).
    static func ipv4SameSlash24(_ a: String, _ b: String) -> Bool {
        let pa = a.split(separator: "."), pb = b.split(separator: ".")
        guard pa.count == 4, pb.count == 4 else { return false }
        return pa[0] == pb[0] && pa[1] == pb[1] && pa[2] == pb[2]
    }

    // Ph3.2: real per-section datapoint completeness N/M — mirrored from Synthesizer.cs:66-71.
    // M is the section's full datapoint catalog; N is slots that produced a CONCLUSIVE reading
    // (Negative / Positive / Degraded). A probe that never ran or returned Inconclusive/Invalidated
    // counts toward M only — so a 4-clean + 1-timeout run reads 0.8, never the inert default 1.0.
    static func DatapointCompleteness(_ subs: [SectionStatus], totalDatapoints: Int) -> Double {
        guard totalDatapoints > 0 else { return 0.0 }
        let usable = subs.filter { $0 == .negative || $0 == .positive || $0 == .degraded }.count
        return min(1.0, Double(usable) / Double(totalDatapoints))
    }

    // Datapoint-catalog sizes (M) per section — MUST track the corresponding builder's probe
    // slots below. Portal's openResolver is advisory-only (never a result datapoint) and Mirror's
    // serverGroundTruth corroborates `authoritative` rather than being its own slot, so neither counts.
    // B173: exclude versionBind (tilt-only — emits EvidenceItem but never appends a subs entry)
    // and stubEntropy (capture-sourced — absent from every non-capture run). Counting them in M
    // structurally capped Prophet completeness at 10/12 = 0.833 for the CLI/GUI path,
    // systematically under-stating every Prophet-derived indicator's weight.
    static let ProphetDatapointCatalog = 10   // resolution, dnssec, nxCanary, dnssecCanary, latency, stability, wildcard, denial, dualStack, edns0
    static let MirrorDatapointCatalog = 6     // comparison, warp, authoritative, svcb, ttlAnomaly, cnameChain
    static let PortalDatapointCatalog = 4     // captivePortal, encryptedDNS, encryptedTransport, gateway (C1)
    static let GluttonDatapointCatalog = 2    // leak, ecs
}

/// Runs every section adapter over the gathered inputs and hands the aggregate to `SynthesisEngine`.
/// The single entry point the CLI/GUI call after running whatever sections they could.
public enum Synthesizer {
    public static func conclude(from inputs: SectionInputs,
                                config: SynthesisEngine.Config = .init(),
                                now: Date = Date()) -> Conclusion {
        let sections: [(Section, SectionEvidence)] = [
            (.prophet, EvidenceAdapters.prophet(resolution: inputs.resolution, dnssec: inputs.dnssec,
                                                nxCanary: inputs.nxCanary, dnssecCanary: inputs.dnssecCanary,
                                                latency: inputs.latency, stability: inputs.stability,
                                                wildcardSynthesis: inputs.wildcardSynthesis,
                                                denial: inputs.denial, dualStack: inputs.dualStack,
                                                edns0: inputs.edns0, versionBind: inputs.versionBind,
                                                stubEntropy: inputs.stubEntropy)),
            (.mirror,  EvidenceAdapters.mirror(comparison: inputs.comparison, warp: inputs.warpManifest,
                                               authoritative: inputs.authoritative, svcb: inputs.svcb,
                                               ttlAnomaly: inputs.ttlAnomaly, cnameChain: inputs.cnameChain,
                                               serverGroundTruth: inputs.serverGroundTruth)),
            (.portal,  EvidenceAdapters.portal(report: inputs.network, encryptedTransport: inputs.encryptedTransport,
                                               openResolver: inputs.openResolver)),
            (.glutton, EvidenceAdapters.glutton(leak: inputs.leak, ecs: inputs.ecs)),
            (.cage,    EvidenceAdapters.cage(exfil: inputs.exfil, responseSize: inputs.responseSize,
                                             attributionRate: inputs.cageAttributionRate)),
            (.witness, EvidenceAdapters.witness(drifts: inputs.drifts, configChanges: inputs.configChanges)),
        ]
        var statuses: [SectionStatusEntry] = []
        var evidence: [EvidenceItem] = []
        var advisories: [Advisory] = []
        for (section, se) in sections {
            statuses.append(SectionStatusEntry(section: section, status: se.status, completeness: se.completeness))
            evidence.append(contentsOf: se.evidence)
            advisories.append(contentsOf: se.advisories)
        }
        // B174: when the system resolver was undetectable and a public substitute was used,
        // the Mirror section's local-vs-public comparison degenerates (local IS public).
        // Cap the Mirror section at .degraded so the result is never clean on substituted evidence.
        if inputs.resolverSubstituted {
            if let idx = statuses.firstIndex(where: { $0.section == .mirror }),
               statuses[idx].status == .negative {
                statuses[idx] = SectionStatusEntry(section: .mirror, status: .degraded,
                                                   completeness: statuses[idx].completeness)
            }
            SyxxDiagnostics.breadcrumb(
                category: "synthesis.resolver",
                message: "Resolver was substituted — Mirror section capped at degraded",
                level: .warning)
        }
        // The matrix determinative engine consumes evidence + section statuses directly. The old
        // DST-era extra inputs to `synthesize` (the answer-matrix, the decisive-act catalog, and the
        // resolver-provenance confidence scalar) were removed together with that engine
        // (owner directive 2026-07-21); the raw structured comparison still reaches the engine via
        // the per-section evidence built above.
        let conclusion = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: statuses,
                                                    advisories: advisories, config: config, now: now)
        reportResultProvenance(conclusion)
        return conclusion
    }

    /// Emit the run's result AND the attribution for every degraded section, once, from the
    /// finished `Conclusion` — never before it exists (no result before evidence, applied to
    /// telemetry).
    ///
    /// WHY: today a fleet-wide rise in inconclusive results is uninvestigable. You cannot tell
    /// whether one resolver started dropping DNSSEC queries, an OS release broke a platform API,
    /// or the matrix spec regressed. This links the two halves: the result breadcrumb carries the
    /// five-state outcome and the per-section statuses, and every diagnostic captured during the
    /// run already shares the same `run_id`.
    ///
    /// It also enforces the standing invariant: a section that is untested/invalidated/inconclusive
    /// with NO diagnostic recorded against it means some path degraded a measurement silently.
    /// That is the defect class this project exists to prevent, and it is detectable mechanically —
    /// so it is reported as its own issue rather than waiting for someone to notice.
    static func reportResultProvenance(_ conclusion: Conclusion) {
        // FIVE states, never four: `nil` determination is "the engine could not run", which is
        // categorically different from `.inconclusive` ("ran, valid data, did not resolve").
        let state = conclusion.determination?.state.rawValue ?? "no_determination"

        var data: [String: String] = ["syxx.result.state": state]
        if let det = conclusion.determination {
            data["syxx.result.diagnoses"] = String(det.diagnoses.count)
            data["syxx.result.suppressed"] = String(det.suppressed.count)
        }

        var blocking: [String] = []
        var unattributed: [String] = []
        for entry in conclusion.sectionStatuses {
            data["syxx.section.\(entry.section.rawValue).status"] = entry.status.rawValue
            guard entry.status.blocksClean || entry.status == .inconclusive else { continue }
            blocking.append(entry.section.rawValue)
            if SyxxDiagnostics.run?.errorClasses(for: entry.section).isEmpty ?? true {
                unattributed.append(entry.section.rawValue)
            }
        }
        data["syxx.blocking_sections"] = blocking.sorted().joined(separator: ",")
        data["syxx.attribution_complete"] = String(unattributed.isEmpty)

        // An abstention or a non-result is the interesting case; a clean run is routine.
        let level: DiagnosticSeverity =
            (state == "clean" || state == "threatDetected") ? .warning : .error
        SyxxDiagnostics.breadcrumb(category: "syxx.result",
                                   message: "result=\(state)",
                                   level: level,
                                   data: data)

        // A degraded section nobody explained is itself a bug — in OUR code, not the network.
        for section in unattributed {
            SyxxDiagnostics.capture(DiagnosticEvent(
                errorClass: "result.unattributed_degradation",
                component: "Synthesizer",
                impact: .none,
                severity: .warning,
                fingerprint: ["result.unattributed_degradation", section],
                tags: ["syxx.section": section, "syxx.result.state": state]))
        }
    }
}
