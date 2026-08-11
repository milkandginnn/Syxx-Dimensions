import XCTest
@testable import SyxxKit

/// Covers the wiring of the Workstream H datapoints (A1/A2/A3/A8/A9) into the synthesis engine:
/// each section adapter must expand the datapoint's typed result into the right `EvidenceItem` /
/// `SectionStatus`. WeightRubric-based assertions removed in matrix engine migration (2026-07-21).
final class SynthesizerDatapointWiringTests: XCTestCase {

    // MARK: A2 — authoritative divergence (Mirror)

    func testAuthoritativeDivergesEmitsEvidenceAndPositiveSection() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            authoritative: AuthoritativeComparison(status: .diverges,
                authoritativeAnswers: ["1.1.1.1"], resolverAnswers: ["9.9.9.9"],
                nameservers: ["ns1.example.com", "ns2.example.com"]))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .authoritativeDiverges })
    }

    func testAuthoritativeMatchesIsCleanNoEvidence() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            authoritative: AuthoritativeComparison(status: .matches))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testAuthoritativeInconclusiveDoesNotEmitEvidence() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            authoritative: AuthoritativeComparison(status: .inconclusive))
        XCTAssertEqual(se.status, .inconclusive)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testAuthoritativeDivergesReachesEngineAndMarksMirrorPositive() {
        var inputs = SectionInputs()
        inputs.resolution = DNSResponse(status: .success,
            records: [DNSRecord(name: "x.example.com", type: 1, ttl: 60, rdata: "9.9.9.9")])
        inputs.authoritative = AuthoritativeComparison(status: .diverges,
            authoritativeAnswers: ["1.1.1.1"], resolverAnswers: ["9.9.9.9"], nameservers: ["ns1.example.com"])
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .authoritativeDiverges })
        XCTAssertEqual(c.sectionStatuses.first { $0.section == .mirror }?.status, .positive)
        // REMOVED: c.probabilities — hypothesis probability distributions removed in matrix engine migration (2026-07-21).
        // The matrix engine maps authoritativeDiverges to the authoritativeMismatch indicator (strength 0.70),
        // which contributes to diagnoses via DiagnosisScore.strength rather than hypothesis probabilities.
    }

    // MARK: A3 — SVCB/HTTPS stripping (Mirror)

    func testSvcbDivergentEmitsStrippedEvidence() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            svcb: SVCBFinding(status: .divergent, presentAt: ["1.1.1.1"], absentAt: ["8.8.8.8"]))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .svcbStripped })
    }

    func testSvcbConsistentIsClean() {
        for status in [SVCBFinding.Status.present, .absent] {
            let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
                svcb: SVCBFinding(status: status, presentAt: ["1.1.1.1"]))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
            XCTAssertTrue(se.evidence.isEmpty)
        }
    }

    // MARK: B050 — expired/not-yet-valid target RRSIG is a DEGRADED caveat, not a threat finding

    /// Mutation-proven: an expired / not-yet-valid TARGET RRSIG must map the Prophet section to
    /// `.degraded`, NEVER `.positive`. If someone reverts the status back to `.positive`, this fails.
    /// The observation is still surfaced (the user should see the stale-signature caveat).
    func testExpiredTargetRRSIGIsDegradedNotPositive() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: .signatureExpiredOrNotYetValid,
                                          nxCanary: nil, dnssecCanary: nil)
        XCTAssertEqual(se.status, .degraded, "expired target RRSIG is ambiguous — a quality caveat, not a threat")
        XCTAssertNotEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .dnssecExpired })
    }

    /// B047 preservation: an expired TARGET RRSIG is degraded, but a known-signed CONTROL canary that
    /// comes back stripped is on-path DNSSEC stripping and stays `.positive`. When both are present the
    /// section combine keeps `.positive` (a real positive dominates a degraded caveat).
    func testControlCanaryStrippedStaysPositiveEvenWithExpiredTarget() {
        let strippedControl = DNSCanaries.CanaryResult(
            section: .prophet, status: .positive,
            evidence: EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                                   observation: "control zone lost its signatures on this path", rationale: "x"),
            detail: "")
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: .signatureExpiredOrNotYetValid,
                                          nxCanary: nil, dnssecCanary: strippedControl)
        XCTAssertEqual(se.status, .positive, "B047 control-canary escalation must survive the B050 change")
    }

    // MARK: A9 — answer stability (Prophet)

    func testAnswerUnstableEmitsEvidenceAndPositiveSection() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            stability: AnswerStability(status: .unstable, sampleCount: 3, usableCount: 3, distinctAnswerSets: 3))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .answerUnstable })
    }

    func testAnswerRotatingAndStableAreClean() {
        for status in [AnswerStability.Status.stable, .rotating] {
            let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
                stability: AnswerStability(status: status, usableCount: 3))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
            XCTAssertTrue(se.evidence.isEmpty)
        }
    }

    func testTtlAnomalyDegradesWithoutEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            stability: AnswerStability(status: .ttlAnomaly, usableCount: 3, ttlHonored: false))
        XCTAssertEqual(se.status, .degraded)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    // MARK: F5 — cross-vantage NXDOMAIN hijack (Prophet)

    func testWildcardNxHijackEmitsEvidenceAndPositiveSection() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            wildcardSynthesis: WildcardSynthesis(status: .nxHijack, syntheticAddresses: ["10.0.0.1"], usableCount: 2))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .nxSubdomainHijack })
    }

    func testWildcardBenignStatesAreClean() {
        for status in [WildcardSynthesis.Status.wildcardZone, .noSynthesis] {
            let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
                wildcardSynthesis: WildcardSynthesis(status: status, usableCount: 2))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
            XCTAssertTrue(se.evidence.isEmpty)
        }
    }

    // MARK: F4 — cross-vantage TTL anomaly (Mirror)

    func testTtlLockstepEmitsEvidence() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            ttlAnomaly: TTLAnomaly(status: .lockstepTTL, ttlsByResolver: ["1.1.1.1": 300, "8.8.8.8": 300, "9.9.9.9": 300]))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .ttlLockstep })
    }

    func testTtlExtremeEmitsEvidence() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            ttlAnomaly: TTLAnomaly(status: .extremeTTL, ttlsByResolver: ["1.1.1.1": 700000]))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .ttlExtreme })
    }

    func testTtlIndependentCachingIsClean() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            ttlAnomaly: TTLAnomaly(status: .independentCaching))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    // MARK: F7 — CNAME chain (Mirror)

    func testCnameChainDivergentEmitsEvidence() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
            cnameChain: CNAMEChainAudit(status: .divergent, testChain: ["w", "evil"]))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .cnameChainDivergent })
    }

    func testCnameChainBenignStatesAreClean() {
        for status in [CNAMEChainAudit.Status.consistent, .noCNAME] {
            let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
                cnameChain: CNAMEChainAudit(status: status))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
            XCTAssertTrue(se.evidence.isEmpty)
        }
    }

    // MARK: A6 — authenticated denial of existence (Prophet)

    func testDenialInjectedEmitsStrongEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            denial: DenialOfExistence(status: .contradictory, queriedName: "nope.example.com", detail: "wrong zone"))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .denialInjected })
    }

    func testDenialBareEmitsWeakEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            denial: DenialOfExistence(status: .bareDenial, queriedName: "nope.example.com"))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .denialBare })
    }

    func testDenialAuthenticatedIsClean() {
        for status in [DenialOfExistence.Status.nsecProven, .soaBacked, .notADenial] {
            let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
                denial: DenialOfExistence(status: status, queriedName: "x"))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
            XCTAssertTrue(se.evidence.isEmpty)
        }
    }

    // MARK: F8 — dual-stack / DNS64 (Prophet)

    func testDualStackIpv4SuppressedEmitsWeakEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            dualStack: DualStackAudit(status: .ipv4Suppressed, aaaaAddresses: ["2606:2800::1"]))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .ipv4Suppressed })
    }

    func testDualStackBenignStatesAreClean() {
        for status in [DualStackAudit.Status.dualStack, .ipv4Only, .dns64Synthesis] {
            let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
                dualStack: DualStackAudit(status: status))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
            XCTAssertTrue(se.evidence.isEmpty)
        }
    }

    // MARK: F1 — EDNS0 capability (Prophet)

    func testEdnsBrokenEmitsEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            edns0: EDNS0Capability(status: .broken))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .ednsBlocked })
    }

    func testEdnsNotSupportedIsDegradedNotClean() {
        // A legacy resolver must be surfaced (degraded), never a silent clean, and carry no threat evidence.
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            edns0: EDNS0Capability(status: .notSupported))
        XCTAssertEqual(se.status, .degraded)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testEdnsSupportedIsClean() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            edns0: EDNS0Capability(status: .supported, udpPayloadSize: 1232))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    // MARK: F10 — ECS leak (Glutton)

    func testEcsForwardedEmitsPrivacyEvidence() {
        let se = EvidenceAdapters.glutton(leak: nil, ecs: ECSLeak(status: .forwarded, scopePrefixLength: 24))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .ecsSubnetLeak })
    }

    func testEcsNotForwardedIsClean() {
        let se = EvidenceAdapters.glutton(leak: nil, ecs: ECSLeak(status: .notForwarded))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testGluttonUntestedWhenNeitherRan() {
        let se = EvidenceAdapters.glutton(leak: nil, ecs: nil)
        XCTAssertEqual(se.status, .untested)
    }

    // REMOVED: testEcsLeakRubricWeight — WeightRubric removed in matrix engine migration (2026-07-21).
    // The matrix engine maps ecsSubnetLeak and dnsLeakConfirmed to the dnsLeakDetected indicator
    // with different strengths (ECS not currently mapped; confirmed leak at 0.95), preserving
    // the ordering invariant at the indicator level rather than via explicit rubric weights.

    // MARK: F2 — CHAOS version.bind (Prophet, contextual / tilt-only)

    func testVersionBindForwarderTiltsButDoesNotFlipSection() {
        // A clean resolution keeps Prophet negative; F2 adds a contextual evidence item WITHOUT flipping
        // the section to a finding (tilt-only — it never sets .positive).
        let clean = DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.2.3.4")])
        let se = EvidenceAdapters.prophet(resolution: clean, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            versionBind: VersionBindResult(status: .disclosed, banner: "dnsmasq-2.80", server: "8.8.8.8", forwarderOnPublicPath: true))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .resolverBannerAnomaly })
    }

    func testVersionBindDisclosedNonForwarderEmitsNoEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            versionBind: VersionBindResult(status: .disclosed, banner: "unbound 1.13.1", server: "8.8.8.8", forwarderOnPublicPath: false))
        XCTAssertFalse(se.evidence.contains { $0.evidenceId == .resolverBannerAnomaly })
    }

    func testVersionBindWithheldAndUntestedEmitNoEvidence() {
        for status in [VersionBindResult.Status.withheld, .untested] {
            let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
                versionBind: VersionBindResult(status: status, server: "1.1.1.1"))
            XCTAssertTrue(se.evidence.isEmpty, "\(status) must emit no evidence")
        }
    }

    // REMOVED: testVersionBindRubricWeight — WeightRubric removed in matrix engine migration (2026-07-21).
    // resolverBannerAnomaly is intentionally NOT mapped to any matrix indicator (contextual/tilt-only),
    // while dnssecCanaryStripped maps to signedAnswerMismatch at 0.80 — the ordering is preserved
    // structurally by the mapping table, not by rubric weights.

    func testVersionBindAloneIsNeverDecisive() {
        // F2 on its own must NOT produce a .threatDetected result — it is contextual/tilt-only and
        // resolverBannerAnomaly is not mapped to any matrix indicator.
        var inputs = SectionInputs()
        inputs.versionBind = VersionBindResult(status: .disclosed, banner: "dnsmasq-2.80",
                                               server: "8.8.8.8", forwarderOnPublicPath: true)
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertNotEqual(c.determination?.state, .threatDetected)
        // REMOVED: c.referralTriggered — referral concept removed in matrix engine migration (2026-07-21)
    }

    // MARK: F3 — stub/OS entropy (Prophet, capture-sourced posture)

    func testWeakStubEntropyIsDegradedWithTilt() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            stubEntropy: StubEntropyAudit(status: .weakRandomization, sampleCount: 12,
                                          distinctSourcePorts: 1, distinctTransactionIDs: 12, detail: "fixed source port"))
        XCTAssertEqual(se.status, .degraded)   // posture caveat, never a threat finding
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .weakStubEntropy })
    }

    func testWellRandomizedStubIsClean() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            stubEntropy: StubEntropyAudit(status: .wellRandomized, sampleCount: 12))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testStubEntropyInsufficientDataIsInconclusive() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            stubEntropy: StubEntropyAudit(status: .insufficientData, sampleCount: 3))
        XCTAssertEqual(se.status, .inconclusive)
    }

    // REMOVED: testStubEntropyRubricWeight — WeightRubric removed in matrix engine migration (2026-07-21).
    // weakStubEntropy is intentionally NOT mapped to any matrix indicator (posture caveat only),
    // while redirection maps to lanDnsRedirect at 0.90 — the ordering is preserved structurally.

    // MARK: F9 — response size (Cage, capture-sourced telemetry)

    func testOversizedResponsesEmitWeakEvidenceAndDegrade() {
        let se = EvidenceAdapters.cage(exfil: nil, responseSize: ResponseSizeAudit(status: .oversized,
            sampleCount: 10, maxBytes: 4096, oversizedCount: 4, detail: "sustained large responses"))
        XCTAssertEqual(se.status, .degraded)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .responseSizeAnomaly })
    }

    func testNormalResponseSizeIsClean() {
        let se = EvidenceAdapters.cage(exfil: nil, responseSize: ResponseSizeAudit(status: .normal, sampleCount: 10))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testCageUntestedWhenNeitherExfilNorSizeRan() {
        XCTAssertEqual(EvidenceAdapters.cage(exfil: nil, responseSize: nil).status, .untested)
    }

    // REMOVED: testResponseSizeRubricWeight — WeightRubric removed in matrix engine migration (2026-07-21).
    // responseSizeAnomaly is intentionally NOT mapped to any matrix indicator (quality caveat only),
    // while exfilLikely maps to localEnvironmentCompromised at 0.60 — the ordering is preserved structurally.

    func testResponseSizeAloneIsNeverDecisive() {
        var inputs = SectionInputs()
        inputs.responseSize = ResponseSizeAudit(status: .oversized, sampleCount: 10, maxBytes: 4096,
                                                oversizedCount: 4, detail: "sustained large responses")
        let c = Synthesizer.conclude(from: inputs)
        // Response size alone is a quality caveat (degraded), not a threat indicator — it must not
        // reach .threatDetected.
        XCTAssertNotEqual(c.determination?.state, .threatDetected)
        // REMOVED: c.referralTriggered — referral concept removed in matrix engine migration (2026-07-21)
    }

    // MARK: Portal — Open Resolver & Amplification (advisory, outside the tamper softmax)

    func testOpenResolverHighAmpEmitsAdvisoryNotEvidence() {
        let se = EvidenceAdapters.portal(report: nil, openResolver:
            OpenResolverFinding(status: .recursiveHighAmp, target: "192.168.1.1", recursive: true,
                                amplificationFactor: 30, detail: "exposure"))
        XCTAssertTrue(se.advisories.contains { $0.advisoryId == .openResolverExposure })
        XCTAssertTrue(se.evidence.isEmpty)   // advisory only — never result evidence
    }

    func testOpenResolverNotRecursiveEmitsNothing() {
        let se = EvidenceAdapters.portal(report: nil, openResolver:
            OpenResolverFinding(status: .notRecursive, target: "192.168.1.1"))
        XCTAssertTrue(se.advisories.isEmpty)
        XCTAssertEqual(se.status, .untested)
    }

    /// B009 (false-secure guard): an ACTIVE captive portal is intercepting DNS, so the Portal section
    /// must read `.degraded`, NEVER `.negative` (clean). Every other portal signal here is clean, so
    /// the portal is the only thing that can move the section off clean — reporting clean would be the
    // MARK: - C1 gateway/DHCP audit

    /// Regression: real `route -n get default` output INDENTS its fields. The pre-fix parser
    /// did an untrimmed hasPrefix("gateway:") and silently found no gateway on every macOS box.
    /// Fail state (mutation-proven): drop the per-line trim and this REDs on the real output.
    func testRouteOutputParserHandlesIndentedGatewayLine() {
        let real = """
           route to: default
        destination: default
               mask: default
            gateway: 10.0.0.1
          interface: en0
        """
        XCTAssertEqual(NetworkKeyState.parseGatewayFromRouteOutput(real), "10.0.0.1")
        // A zero/absent gateway is nil, never a fabricated address.
        XCTAssertNil(NetworkKeyState.parseGatewayFromRouteOutput("    gateway: 0.0.0.0"))
        XCTAssertNil(NetworkKeyState.parseGatewayFromRouteOutput("route: no default"))
    }

    private func gatewayReport(_ gw: GatewayInfo?) -> NetworkStateReport {
        NetworkStateReport(
            dnsServers: ["192.168.1.1"], activeInterfaces: ["en0"], vpnActive: false, captivePortalDetected: false,
            vpn: VPNStatus(state: .no, tunnelInterfaces: [], evidence: ""),
            captivePortal: CaptivePortalStatus(state: .no, evidence: ""),
            encryptedDNS: EncryptedDNSStatus(state: .yes, evidence: ""),
            scopedResolvers: [], gateway: gw)
    }

    /// Happy path: DHCP pushing a private, off-subnet, non-gateway resolver produces the C1
    /// evidence item and a positive sub-status.
    /// Failure case: remove the off-link filter (flag nothing) → this test REDs.
    func testGatewayDhcpOffLinkPrivateDNSIsFlagged() {
        let gw = GatewayInfo(ip: "192.168.1.1", isDNS: true, adminReachable: .yes,
                             dhcpPushedDNS: ["192.168.77.13"], detail: "")
        let se = EvidenceAdapters.portal(report: gatewayReport(gw))
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .gatewayDhcpOffLinkDNS },
                      "an off-subnet private DHCP-pushed resolver must produce the C1 evidence item")
        XCTAssertEqual(se.status, .positive)
    }

    /// ISP-pushed PUBLIC resolvers are normal. Failure case: flag public addresses too
    /// (drop the privateUse guard) → this test REDs on the false positive.
    func testGatewayDhcpPublicDNSIsNotFlagged() {
        let gw = GatewayInfo(ip: "192.168.1.1", isDNS: false, adminReachable: .yes,
                             dhcpPushedDNS: ["75.75.75.75", "75.75.76.76"], detail: "")
        let se = EvidenceAdapters.portal(report: gatewayReport(gw))
        XCTAssertFalse(se.evidence.contains { $0.evidenceId == .gatewayDhcpOffLinkDNS },
                       "ISP-pushed public resolvers must never read as a rogue gateway")
    }

    /// The gateway pushing ITSELF (or a same-/24 sibling) is the standard home shape — clean.
    func testGatewayDhcpSameSubnetDNSIsNotFlagged() {
        let gw = GatewayInfo(ip: "192.168.1.1", isDNS: true, adminReachable: .yes,
                             dhcpPushedDNS: ["192.168.1.1", "192.168.1.53"], detail: "")
        let se = EvidenceAdapters.portal(report: gatewayReport(gw))
        XCTAssertFalse(se.evidence.contains { $0.evidenceId == .gatewayDhcpOffLinkDNS })
    }

    /// A failed gateway detection is inconclusive, never silently clean.
    /// Failure case: return .negative for an empty gateway IP → this test REDs.
    func testGatewayDetectionFailureIsInconclusiveNotClean() {
        let gw = GatewayInfo(ip: "", isDNS: false, adminReachable: .unknown,
                             dhcpPushedDNS: [], detail: "Could not determine default gateway")
        let se = EvidenceAdapters.portal(report: gatewayReport(gw))
        XCTAssertNotEqual(se.status, .negative,
                          "a gateway probe that could not run must not contribute clean support")
    }

    /// cardinal false "secure" result.
    func testCaptivePortalSectionIsDegradedNotClean() {
        let report = NetworkStateReport(
            dnsServers: ["192.168.1.1"], activeInterfaces: ["en0"], vpnActive: false, captivePortalDetected: true,
            vpn: VPNStatus(state: .no, tunnelInterfaces: [], evidence: ""),
            captivePortal: CaptivePortalStatus(state: .yes, evidence: "portal detected"),
            encryptedDNS: EncryptedDNSStatus(state: .yes, evidence: ""),
            scopedResolvers: [], gateway: nil)
        let se = EvidenceAdapters.portal(report: report)
        XCTAssertNotEqual(se.status, .negative, "an active captive portal must NOT read as clean (false-secure)")
        XCTAssertEqual(se.status, .degraded, "an active captive portal must degrade the Portal section")
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .captivePortalActive })
    }

    func testOpenResolverNeverAffectsResult() {
        var inputs = SectionInputs()
        inputs.openResolver = OpenResolverFinding(status: .recursiveHighAmp, target: "192.168.1.1",
                                                  recursive: true, amplificationFactor: 30, detail: "exposure")
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertTrue(c.advisories.contains { $0.advisoryId == .openResolverExposure })  // rides its own channel
        // REMOVED: c.diagnosis (.localCompromise, .interception_onPath, .interception_transport, .spoofing) —
        // old hypothesis enum removed in matrix engine migration (2026-07-21). An advisory-only run produces
        // no evidence, so the engine cannot reach .threatDetected (no indicators fire).
        XCTAssertNotEqual(c.determination?.state, .threatDetected)
    }

    // MARK: A2 — server-confirmed authoritative divergence (Mirror, additive Pro corroboration)

    private func diverges(resolver: [String], authoritative: [String] = ["93.184.216.34"]) -> AuthoritativeComparison {
        AuthoritativeComparison(status: .diverges, authoritativeAnswers: authoritative, resolverAnswers: resolver)
    }
    private func groundTruth(_ answers: [String], result: String = "authoritative") -> GroundTruth {
        GroundTruth(domain: "x.com", type: "A", result: result,
                    authoritative: .init(answers: answers, rcode: 0, signed: true))
    }

    func testAuthoritativeDivergesServerConfirmedIsDecisive() {
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil, authoritative: diverges(resolver: ["10.0.0.9"]),
                                         serverGroundTruth: groundTruth(["93.184.216.34"]))
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .authoritativeDivergesConfirmed })
        XCTAssertFalse(se.evidence.contains { $0.evidenceId == .authoritativeDiverges })
    }

    func testAuthoritativeDivergesServerOverlapStaysRegular() {
        // Resolver answer overlaps server ground truth (CDN-ish) — not disjoint → no server confirmation.
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil,
                                         authoritative: diverges(resolver: ["93.184.216.34", "10.0.0.9"]),
                                         serverGroundTruth: groundTruth(["93.184.216.34"]))
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .authoritativeDiverges })
        XCTAssertFalse(se.evidence.contains { $0.evidenceId == .authoritativeDivergesConfirmed })
    }

    func testAuthoritativeDivergesNoServerIsUnchanged() {
        // Offline / LITE path: no server ground truth → exactly today's local finding.
        let se = EvidenceAdapters.mirror(comparison: nil, warp: nil, authoritative: diverges(resolver: ["10.0.0.9"]),
                                         serverGroundTruth: nil)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .authoritativeDiverges })
        XCTAssertFalse(se.evidence.contains { $0.evidenceId == .authoritativeDivergesConfirmed })
    }

    // REMOVED: testAuthoritativeConfirmedRubricWeight — WeightRubric removed in matrix engine migration
    // (2026-07-21). The ordering invariants are preserved in the indicator mapping table:
    //   authoritativeDivergesConfirmed → authoritativeMismatch at 0.80
    //   authoritativeDiverges          → authoritativeMismatch at 0.70
    //   nxSubdomainHijack              → suspiciousNxDomainPattern at 0.80
    //   denialInjected                 → liveQueryTamperPattern at 0.75
    //   denialBare                     → (not mapped — below reporting tier)

    // MARK: A1 — resolver latency (Prophet, quality overlay only)

    func testLatencySlowDegradesNoThreatEvidence() {
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            latency: ResolverLatency(status: .slow, sampleCount: 3, successCount: 3, medianMs: 210))
        XCTAssertEqual(se.status, .degraded)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testLatencyFastIsCleanAndNeverInvalidates() {
        for status in [ResolverLatency.Status.fast, .moderate] {
            let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
                latency: ResolverLatency(status: status, sampleCount: 3, successCount: 3, medianMs: 20))
            XCTAssertEqual(se.status, .negative, "\(status) should be clean")
        }
    }

    func testLatencyUnreachableDegradesButDoesNotInvalidateSection() {
        // A secondary timing probe failing must NOT hard-gate the whole section (untested/invalidated).
        let se = EvidenceAdapters.prophet(resolution: nil, dnssec: nil, nxCanary: nil, dnssecCanary: nil,
            latency: ResolverLatency(status: .unreachable, sampleCount: 4, successCount: 0))
        XCTAssertEqual(se.status, .degraded)
        XCTAssertFalse(se.status.blocksClean)
    }

    // MARK: A8 — encrypted transport (Portal)

    func testEncryptedTransportBlockedEmitsEvidence() {
        let se = EvidenceAdapters.portal(report: nil,
            encryptedTransport: EncryptedTransport(status: .blocked, endpoint: "cloudflare-dns.com"))
        XCTAssertEqual(se.status, .positive)
        XCTAssertTrue(se.evidence.contains { $0.evidenceId == .encryptedDnsBlocked })
    }

    func testEncryptedTransportWorkingIsClean() {
        let se = EvidenceAdapters.portal(report: nil,
            encryptedTransport: EncryptedTransport(status: .working, endpoint: "cloudflare-dns.com"))
        XCTAssertEqual(se.status, .negative)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    func testPortalUntestedWhenNothingRan() {
        let se = EvidenceAdapters.portal(report: nil, encryptedTransport: nil)
        XCTAssertEqual(se.status, .untested)
        XCTAssertTrue(se.evidence.isEmpty)
    }

    // REMOVED: testWiredRubricWeights — WeightRubric and hypothesis-based weight table removed in
    // matrix engine migration (2026-07-21). The equivalent "sanity" check now lives in the
    // indicator mapping table (SynthesisEngine.indicatorMapping) which maps EvidenceId → indicator
    // strength, not per-hypothesis weight. Many evidence IDs from the old rubric (answerUnstable,
    // ipv4Suppressed, ttlLockstep, cnameChainDivergent, ednsBlocked, denialBare) are intentionally
    // unmapped — they remain as raw evidence observations but do not drive any indicator.
}

// Ph3.2 — SectionEvidence.Completeness must be a real N/M over the section's datapoint
// catalog, never the inert 1.0 default; the count-based clean-support scaling in the
// engine depends on it. Ported from Windows Syxx.Tests / SyxxKitTests.cs:3084-3143,
// AND these tests MUST fail against the unfixed code (mutation proof per Protocol §4).
final class SectionCompletenessTests: XCTestCase {

    private func ok() -> DNSResponse {
        DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.1.1.1")])
    }

    func testProphetCompletenessGrowsWithConclusiveDatapoints() {
        let two = EvidenceAdapters.prophet(resolution: ok(), dnssec: .recordsPresentUnverified, nxCanary: nil, dnssecCanary: nil)
        XCTAssertEqual(two.completeness, 2.0 / Double(EvidenceAdapters.ProphetDatapointCatalog), accuracy: 1e-6)

        let three = EvidenceAdapters.prophet(resolution: ok(), dnssec: .recordsPresentUnverified, nxCanary: nil, dnssecCanary: nil,
            latency: ResolverLatency(status: .fast, sampleCount: 3, successCount: 3, medianMs: 20))
        XCTAssertEqual(three.completeness, 3.0 / Double(EvidenceAdapters.ProphetDatapointCatalog), accuracy: 1e-6)
        XCTAssertGreaterThan(three.completeness, two.completeness,
            "an additional conclusive datapoint must raise completeness")
    }

    func testUntestedSectionsReadZeroCompleteness() {
        XCTAssertEqual(EvidenceAdapters.portal(report: nil).completeness, 0.0)
        XCTAssertEqual(EvidenceAdapters.glutton(leak: nil, ecs: nil).completeness, 0.0)
        XCTAssertEqual(EvidenceAdapters.witness(drifts: nil, configChanges: nil).completeness, 0.0)
    }

    func testCompletenessSurvivesIntoSectionStatusEntries() {
        var inputs = SectionInputs()
        inputs.resolution = ok()
        let c = Synthesizer.conclude(from: inputs)
        let prophetEntry = c.sectionStatuses.first { $0.section == .prophet }!
        XCTAssertEqual(prophetEntry.completeness, 1.0 / Double(EvidenceAdapters.ProphetDatapointCatalog), accuracy: 1e-6)
    }
}
