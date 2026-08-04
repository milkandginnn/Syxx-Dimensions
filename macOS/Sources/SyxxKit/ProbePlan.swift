import Foundation

// MARK: - Shared probe orchestration plan

/// Standardized per-section probe orchestration that replaces copy-pasted section runners across
/// all four frontends (macOS CLI, macOS App, Windows CLI, Windows GUI).
///
/// Each static method runs the standard probes for one section, respecting the per-probe budget
/// from `ProbeBudget`, and returns a `SectionInputs` with only the probes in that section filled.
/// The caller merges these fields into their own `SectionInputs` and handles presentation.
///
/// - Note: Only section-level field assignment lives here. Presentation (`print` / `Console.WriteLine`)
///   stays in the frontend. This struct does NOT emit user-visible strings.
public struct ProbePlan {

    // MARK: - Prophet

    /// Runs all Prophet probes (resolution + dnssec + canaries + WS-H probes) against `resolver`.
    /// - Parameters:
    ///   - domain: The domain to resolve.
    ///   - resolver: The resolver under test (typically the system DNS server).
    ///   - includeAAAA: If true, also queries AAAA records alongside A.
    /// - Returns: A `SectionInputs` with Prophet fields filled (or nil where probes timed out).
    public static func runProphet(domain: String, resolver: String, includeAAAA: Bool = true) async -> SectionInputs {
        var budget = ProbeBudget.defaultProphet
        var inputs = SectionInputs()

        // Core A-record resolution
        inputs.resolution = await ProbeRunner.run(label: "query", budget: &budget) {
            await DNSResolver.queryUDPDetailed(DNSQuery(name: domain, type: 1, server: resolver))
        }
        // Optional AAAA resolution (not merged into sectionInputs — informational only)
        if includeAAAA {
            _ = await ProbeRunner.run(label: "queryAAAA", budget: &budget) {
                await DNSResolver.queryUDPDetailed(DNSQuery(name: domain, type: 28, server: resolver))
            }
        }
        // DNSSEC
        inputs.dnssec = await ProbeRunner.run(label: "dnssec", budget: &budget) {
            await DNSSECVerifier.checkStatus(domain: domain, resolver: resolver)
        }
        // Canaries
        inputs.nxCanary = await ProbeRunner.run(label: "nxCanary", budget: &budget) {
            await DNSCanaries.nxdomainHijack(resolver: resolver)
        }
        inputs.dnssecCanary = await ProbeRunner.run(label: "dnssecCanary", budget: &budget) {
            await DNSCanaries.dnssecControl(resolver: resolver)
        }
        // Workstream H probes
        inputs.latency = await ProbeRunner.run(label: "latency", budget: &budget) {
            await ResolverLatencyProbe.probe(domain: domain, qtype: 1, server: resolver)
        }
        inputs.stability = await ProbeRunner.run(label: "stability", budget: &budget) {
            await AnswerStabilityProbe.probe(domain: domain, qtype: 1, server: resolver)
        }
        inputs.wildcardSynthesis = await ProbeRunner.run(label: "wildcard", budget: &budget) {
            await WildcardSynthesisProbe.probe(domain: domain, resolverUnderTest: resolver)
        }
        inputs.denial = await ProbeRunner.run(label: "denial", budget: &budget) {
            await DenialOfExistenceProbe.probe(name: domain, qtype: 1, server: resolver)
        }
        inputs.dualStack = await ProbeRunner.run(label: "dualStack", budget: &budget) {
            await DualStackProbe.probe(domain: domain, server: resolver)
        }
        inputs.edns0 = await ProbeRunner.run(label: "edns0", budget: &budget) {
            await EDNS0Probe.probe(domain: domain, server: resolver)
        }
        inputs.versionBind = await ProbeRunner.run(label: "versionBind", budget: &budget) {
            await VersionBindProbe.probe(server: resolver)
        }

        return inputs
    }

    // MARK: - Mirror

    /// Runs all Mirror probes (vantage comparison, authoritative cross-check, SVCB/TTL/CNAME, content warp).
    /// - Parameters:
    ///   - domain: The domain to compare (hostname only, without scheme).
    ///   - localResolver: The local resolver under test.
    ///   - isPro: If true, attempts Dimension server corroboration when authoritative diverges.
    ///   - targetURL: Optional full URL for content warp. Defaults to `https://<domain>`.
    /// - Returns: A `SectionInputs` with Mirror fields filled (or nil where probes timed out / were skipped).
    public static func runMirror(domain: String, localResolver: String, isPro: Bool = false, targetURL: String? = nil) async -> SectionInputs {
        var budget = ProbeBudget.defaultMirror
        var inputs = SectionInputs()

        // Vantage comparison (throws on transport failure — caught inside closure)
        // C066 Fix 3: capture VantageComparer's error description (not the Error itself,
        // which is not Sendable) so the transport failure reason is not silently lost.
        // The runner's span already records "operation_returned_nil"; this adds the cause.
        let vantageErrorRef = VantageErrorCapture()
        inputs.comparison = (await ProbeRunner.run(label: "vantage", budget: &budget) {
            do {
                return try await VantageComparer.compareVantagePoints(domain: domain, localServer: localResolver)
            } catch {
                vantageErrorRef.message = error.localizedDescription
                return nil as SyxxKit.ComparisonResult?
            }
        }).flatMap { $0 }
        if inputs.comparison == nil, let msg = vantageErrorRef.message {
            debugPrint("[ProbePlan] vantage transport error: \(msg)")
        }

        guard let comparison = inputs.comparison else {
            // If vantage failed, skip dependent probes but still attempt content warp
            inputs.warpManifest = await runContentWarp(domain: domain, targetURL: targetURL, budget: &budget)
            return inputs
        }

        // A2: authoritative delegation cross-check — use all trusted bootstraps except the one under test (B031)
        let bootstrapResolvers = AuthoritativeVantage.defaultBootstrapResolvers.filter { $0 != localResolver }
        inputs.authoritative = await ProbeRunner.run(label: "authoritative", budget: &budget) {
            await AuthoritativeVantage.compare(
                domain: domain,
                // C060: pass only address (A/AAAA) rdata to the authoritative cross-check.
                // localRecords includes CNAME rdata (domain names) which can never match the
                // address-only auth set, making `.matches` unreachable for CNAME'd domains.
                resolverAnswers: comparison.localRecords.filter { $0.type == 1 || $0.type == 28 }.map { $0.rdata },
                bootstrapResolvers: bootstrapResolvers,
                resolverDidFail: comparison.localOutcome.didFail
            )
        }

        // A2 Pro: Dimension server corroboration
        // D029: re-check entitlement locally (not just trust the caller isPro bool) before firing
        // the Pro dimension path, matching Windows ProbePlan.cs parity.
        if isPro, inputs.authoritative?.status == .diverges {
            let lic = await LicenseManager.isFeatureUnlocked()
            if lic.unlocked {
                let client = DimensionClient(baseURL: SyxxConfig.dimensionBaseURL, licenseKey: LicenseManager.getStoredLicenseKey())
                // D081: DimensionOutcome distinguishes "fetched" from "transport failure" / "decode failure"
                let outcome = await ProbeRunner.run(label: "groundTruth", budget: &budget) {
                    await client.authoritative(domain: domain, type: "A")
                }
                if case .fetched(let gt) = outcome {
                    inputs.serverGroundTruth = gt
                }
            }
        }

        // A3: SVCB, TTL, CNAME
        let resolverList = [localResolver, "1.1.1.1", "8.8.8.8", "9.9.9.9"].uniqued()
        inputs.svcb = await ProbeRunner.run(label: "svcb", budget: &budget) {
            await SVCBProbe.probe(domain: domain, resolvers: resolverList)
        }
        inputs.ttlAnomaly = await ProbeRunner.run(label: "ttl", budget: &budget) {
            await TTLAnomalyProbe.probe(domain: domain, resolvers: resolverList)
        }
        inputs.cnameChain = await ProbeRunner.run(label: "cname", budget: &budget) {
            await CNAMEChainProbe.probe(domain: domain, resolverUnderTest: localResolver)
        }

        // Content warp (WARP_MANIFEST_V1 signed-manifest content integrity)
        inputs.warpManifest = await runContentWarp(domain: domain, targetURL: targetURL, budget: &budget)

        return inputs
    }

    /// Runs content-warp only (used standalone or as part of Mirror). Produces the WARP_MANIFEST_V1
    /// signed-manifest result: pins the trust set out of band, fetches the signed manifest from the
    /// Dimension server, and verifies it against the client vantage. Without a pinned key the signer
    /// is `untrustedSigner` → INCONCLUSIVE (never a match); an unreachable server is INCONCLUSIVE.
    private static func runContentWarp(domain: String, targetURL: String?, budget: inout ProbeBudget) async -> WarpManifestResult? {
        let licenseCheck: (Bool, String)? = await ProbeRunner.run(label: "licenseCheck", budget: &budget) {
            await LicenseManager.isFeatureUnlocked()
        }
        guard let licenseCheck, licenseCheck.0 else { return nil }
        let origin = targetURL ?? "https://\(domain)"
        let client = DimensionClient(baseURL: SyxxConfig.dimensionBaseURL, licenseKey: LicenseManager.getStoredLicenseKey())
        return await ProbeRunner.run(label: "warp", budget: &budget) {
            await WarpEngine.runVantageCheck(origin: origin, client: client)
        }
    }

    // MARK: - Portal

    /// Runs all Portal probes (network state, encrypted transport, open resolver).
    /// - Returns: A `SectionInputs` with Portal fields filled (or nil where probes timed out / were skipped).
    public static func runPortal() async -> SectionInputs {
        var budget = ProbeBudget.defaultPortal
        var inputs = SectionInputs()

        // Network state report — local calls with internal timeouts; not ProbeRunner-wrapped
        inputs.network = await NetworkKeyState.generateReport()

        inputs.encryptedTransport = await ProbeRunner.run(label: "encryptedTransport", budget: &budget) {
            await EncryptedTransportProbe.probe()
        }

        // Open resolver check (only for LAN/private resolvers)
        if let ownResolver = (inputs.network?.dnsServers ?? []).first(where: { isOwnNetworkAddress($0) }) {
            inputs.openResolver = await ProbeRunner.run(label: "openResolver", budget: &budget) {
                await OpenResolverProbe.probe(target: ownResolver)
            }
        }

        return inputs
    }

    /// True when `ip` is a private/LAN address — the only resolvers the open-resolver probe may target.
    private static func isOwnNetworkAddress(_ ip: String) -> Bool {
        switch IPClassifier.classify(ip) {
        case .privateUse, .loopback, .linkLocal: return true
        default: return false
        }
    }

    // MARK: - Glutton

    /// Runs all Glutton probes (ECS probe + leak trigger queries).
    /// - Parameters:
    ///   - servers: The system DNS servers to test against.
    ///   - licenseKey: Optional license key for collector-backed leak test.
    /// - Returns: A `SectionInputs` with Glutton fields filled (or nil where probes timed out).
    public static func runGlutton(servers: [String], licenseKey: String? = nil) async -> SectionInputs {
        var budget = ProbeBudget.defaultGlutton
        var inputs = SectionInputs()

        let subdomain = LeakDetector.generateTestSubdomain(baseDomain: "leak.syxx.app")

        inputs.ecs = await ProbeRunner.run(label: "ecs", budget: &budget) {
            await ECSProbe.probe(server: servers.first ?? "1.1.1.1")
        }

        let _ = await ProbeRunner.run(label: "triggerQueries", budget: &budget) {
            await LeakDetector.triggerQueries(subdomain: subdomain, servers: servers)
        }

        // Without a collector URL, the leak result is always inconclusive.
        // Assigned generically here; the caller may replace with a collector-backed result.
        inputs.leak = LeakTestResult(result: .inconclusive, errorReason: "Local check only; use collector for confirmed result.")

        return inputs
    }
}

// MARK: - C066 Helpers

/// Thread-safe reference wrapper for capturing an error message from inside a @Sendable closure.
/// Used by ProbePlan's vantage comparison to propagate the transport error description without
/// violating Sendable constraints (Error itself is not Sendable, but a String is).
private final class VantageErrorCapture: @unchecked Sendable {
    private let lock = NSLock()
    private var _message: String?
    var message: String? {
        get { lock.lock(); defer { lock.unlock() }; return _message }
        set { lock.lock(); defer { lock.unlock() }; _message = newValue }
    }
}

// MARK: - Helpers

extension Sequence where Element: Hashable {
    /// Returns unique elements while preserving order.
    fileprivate func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}
