using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

/// <summary>
/// Standardized per-section probe orchestration — the single source of truth for "what probes
/// run in each section" across all four frontends (macOS CLI, macOS App, Windows CLI, Windows GUI).
///
/// Each static method runs the standard probes for one section, respecting the per-probe budget
/// from <see cref="ProbeBudget"/>, and returns a <see cref="SectionInputs"/> with only the probes
/// in that section filled. The caller merges these fields into their own SectionInputs and handles
/// presentation.
///
/// Note: Only section-level field assignment lives here. Presentation (Console.WriteLine) stays in
/// the frontend. This class does NOT emit user-visible strings.
/// </summary>
public static class ProbePlan
{
    // MARK: - Prophet

    /// <summary>
    /// Runs all Prophet probes (resolution + dnssec + canaries + WS-H probes) against <paramref name="resolver"/>.
    /// </summary>
    /// <param name="domain">The domain to resolve.</param>
    /// <param name="resolver">The resolver under test (typically the system DNS server).</param>
    /// <param name="includeAAAA">If true, also queries AAAA records alongside A.</param>
    /// <returns>A SectionInputs with Prophet fields filled (or null where probes timed out).</returns>
    // B096: qtype parameter threads the user-selected record type through to the core query
    // and dependent probes (latency, stability, denial). Defaults to 1 (A) for backward
    // compatibility with callers that don't specify a type.
    public static async Task<SectionInputs> RunProphetAsync(string domain, string resolver, bool includeAAAA = true, ushort qtype = 1)
    {
        var budget = ProbeBudget.DefaultProphet;
        var inputs = new SectionInputs();

        // Core record resolution — uses the caller's qtype instead of hardcoding A (B096).
        inputs.Resolution = await ProbeRunner.RunAsync<DNSResponse>("query", budget, () =>
            DNSResolver.QueryUDPDetailedAsync(new DNSQuery(name: domain, type: qtype, server: resolver)));

        // Optional AAAA resolution (informational only — not stored in inputs)
        if (includeAAAA && qtype != 28)
        {
            _ = await ProbeRunner.RunAsync<DNSResponse>("queryAAAA", budget, () =>
                DNSResolver.QueryUDPDetailedAsync(new DNSQuery(name: domain, type: 28, server: resolver)));
        }

        // DNSSEC — enum (value type), left unwrapped from ProbeRunner
        inputs.Dnssec = await DNSSECVerifier.CheckStatusAsync(domain: domain, resolver: resolver);

        // Canaries
        inputs.NxCanary = await ProbeRunner.RunAsync<DNSCanaries.CanaryResult>("nxCanary", budget, () =>
            DNSCanaries.NxdomainHijackAsync(resolver: resolver));
        inputs.DnssecCanary = await ProbeRunner.RunAsync<DNSCanaries.CanaryResult>("dnssecCanary", budget, () =>
            DNSCanaries.DnssecControlAsync(resolver: resolver));

        // Workstream H probes — B096: use the selected qtype instead of hardcoded A.
        inputs.Latency = await ProbeRunner.RunAsync<ResolverLatency>("latency", budget, () =>
            ResolverLatencyProbe.Probe(domain: domain, qtype: qtype, server: resolver));
        inputs.Stability = await ProbeRunner.RunAsync<AnswerStability>("stability", budget, () =>
            AnswerStabilityProbe.Probe(domain: domain, qtype: qtype, server: resolver));
        inputs.WildcardSynthesis = await ProbeRunner.RunAsync<WildcardSynthesis>("wildcard", budget, () =>
            WildcardSynthesisProbe.Probe(domain: domain, resolverUnderTest: resolver));
        inputs.Denial = await ProbeRunner.RunAsync<DenialOfExistence>("denial", budget, () =>
            DenialOfExistenceProbe.Probe(name: domain, qtype: qtype, server: resolver));
        inputs.DualStack = await ProbeRunner.RunAsync<DualStackAudit>("dualStack", budget, () =>
            DualStackProbe.Probe(domain: domain, server: resolver));
        inputs.Edns0 = await ProbeRunner.RunAsync<EDNS0Capability>("edns0", budget, () =>
            EDNS0Probe.Probe(domain: domain, server: resolver));
        inputs.VersionBind = await ProbeRunner.RunAsync<VersionBindResult>("versionBind", budget, () =>
            VersionBindProbe.Probe(server: resolver));

        return inputs;
    }

    // MARK: - Mirror

    /// <summary>
    /// Runs all Mirror probes (vantage comparison, authoritative cross-check, SVCB/TTL/CNAME, content warp).
    /// </summary>
    /// <param name="domain">The domain to compare (hostname only, without scheme).</param>
    /// <param name="localResolver">The local resolver under test.</param>
    /// <param name="isPro">If true, attempts Dimension server corroboration when authoritative diverges.</param>
    /// <param name="targetURL">Optional full URL for content warp. Defaults to "https://&lt;domain&gt;".</param>
    /// <returns>A SectionInputs with Mirror fields filled (or null where probes timed out / were skipped).</returns>
    public static async Task<SectionInputs> RunMirrorAsync(string domain, string localResolver, bool isPro = false, string? targetURL = null)
    {
        var budget = ProbeBudget.DefaultMirror;
        var inputs = new SectionInputs();

        // Vantage comparison (throws on transport failure — caught inside closure)
        inputs.Comparison = await ProbeRunner.RunAsync<ComparisonResult>("vantage", budget, async () =>
        {
            try { return await VantageComparer.CompareVantagePointsAsync(domain: domain, localServer: localResolver); }
            catch { return null; }
        });

        if (inputs.Comparison == null)
        {
            // If vantage failed, skip dependent probes but still attempt content warp
            // B141: only attempt content warp when isPro is true — the license check
            // invokes DPAPI on Windows which crashes on Linux.
            if (isPro)
                inputs.WarpManifest = await RunContentWarpAsync(domain, targetURL, budget);
            return inputs;
        }

        // A2: authoritative delegation cross-check — use all trusted bootstraps except the one under test (B031)
        var bootstrapResolvers = AuthoritativeVantage.DefaultBootstrapResolvers.Where(r => r != localResolver).ToList();
        inputs.Authoritative = await ProbeRunner.RunAsync<AuthoritativeComparison>("authoritative", budget, () =>
            AuthoritativeVantage.Compare(
                domain: domain,
                resolverAnswers: inputs.Comparison.LocalRecords.Select(r => r.Rdata).ToList(),
                bootstrapResolvers: bootstrapResolvers,
                resolverDidFail: inputs.Comparison.LocalOutcome.DidFail));

        // A2 Pro: Dimension server corroboration
        if (isPro && inputs.Authoritative?.Status == AuthComparisonStatus.Diverges)
        {
            var lic = await LicenseManager.IsFeatureUnlockedAsync(); // tuple — not ProbeRunner-wrappable
            if (lic.Unlocked)
            {
                string? storedKey = LicenseManager.GetStoredLicenseKey();
                inputs.ServerGroundTruth = await ProbeRunner.RunAsync<GroundTruth>("groundTruth", budget, () =>
                    DimensionClient.AuthoritativeAsync(SyxxConfig.DimensionBaseURL, domain, "A", storedKey));
            }
        }

        // A3: SVCB, TTL, CNAME
        var resolverList = new[] { localResolver, "1.1.1.1", "8.8.8.8", "9.9.9.9" }
            .Where(r => r != localResolver).Prepend(localResolver).ToList();
        inputs.Svcb = await ProbeRunner.RunAsync<SVCBFinding>("svcb", budget, () =>
            SVCBProbe.Probe(domain: domain, resolvers: resolverList));
        inputs.TtlAnomaly = await ProbeRunner.RunAsync<TTLAnomaly>("ttl", budget, () =>
            TTLAnomalyProbe.Probe(domain: domain, resolvers: resolverList));
        inputs.CnameChain = await ProbeRunner.RunAsync<CNAMEChainAudit>("cname", budget, () =>
            CNAMEChainProbe.Probe(domain: domain, resolverUnderTest: localResolver));

        // Content warp — B141: only when isPro is true, since the license check
        // invokes Windows DPAPI which crashes on Linux.
        if (isPro)
            inputs.WarpManifest = await RunContentWarpAsync(domain, targetURL, budget);

        return inputs;
    }

    /// <summary>
    /// Runs content-warp only (used standalone or as part of Mirror). Produces the WARP_MANIFEST_V1
    /// signed-manifest result: pins the trust set out of band, fetches the signed manifest from the
    /// Dimension server, and verifies it against the client vantage. Without a pinned key the signer
    /// is UntrustedSigner → INCONCLUSIVE (never a match); an unreachable server is INCONCLUSIVE.
    /// </summary>
    private static async Task<WarpManifestResult?> RunContentWarpAsync(string domain, string? targetURL, ProbeBudget budget)
    {
        var lic = await LicenseManager.IsFeatureUnlockedAsync(); // tuple — not ProbeRunner-wrappable
        if (!lic.Unlocked) return null;

        string origin = targetURL ?? $"https://{domain}";
        string? licenseKey = LicenseManager.GetStoredLicenseKey();
        // Fresh per-run replay nonce (§4.5): the server must echo it inside the signed manifest,
        // so a captured manifest cannot be replayed into a later run within the freshness window.
        string nonce = WarpEngine.MakeNonce();
        return await ProbeRunner.RunAsync<WarpManifestResult>("warp", budget, () =>
            WarpEngine.RunVantageCheckAsync(origin, SyxxConfig.DimensionBaseURL, licenseKey, nonce));
    }

    // MARK: - Portal

    /// <summary>
    /// Runs all Portal probes (network state, encrypted transport, open resolver).
    /// </summary>
    /// <returns>A SectionInputs with Portal fields filled (or null where probes timed out / were skipped).</returns>
    public static async Task<SectionInputs> RunPortalAsync()
    {
        var budget = ProbeBudget.DefaultPortal;
        var inputs = new SectionInputs();

        // Network state report — local calls with internal timeouts; not ProbeRunner-wrapped
        inputs.Network = await NetworkKeyState.GenerateReportAsync();

        inputs.EncryptedTransport = await ProbeRunner.RunAsync<EncryptedTransport>("encryptedTransport", budget, () =>
            EncryptedTransportProbe.Probe());

        // Open resolver check (only for LAN/private resolvers)
        if (inputs.Network?.DnsServers.FirstOrDefault(IsOwnNetworkAddress) is string ownResolver)
        {
            inputs.OpenResolver = await ProbeRunner.RunAsync<OpenResolverFinding>("openResolver", budget, () =>
                OpenResolverProbe.Probe(target: ownResolver));
        }

        return inputs;
    }

    /// <summary>True when <paramref name="ip"/> is a private/LAN address.</summary>
    private static bool IsOwnNetworkAddress(string ip)
    {
        return IPClassifier.Classify(ip) is IPAddressClass.PrivateUse
            or IPAddressClass.Loopback
            or IPAddressClass.LinkLocal;
    }

    // MARK: - Glutton

    /// <summary>
    /// Runs all Glutton probes (ECS probe + leak trigger queries). The per-probe trigger results
    /// are not returned — only the synthetic LeakTestResult (always inconclusive without a collector).
    /// </summary>
    /// <param name="servers">The system DNS servers to test against.</param>
    /// <returns>A SectionInputs with Glutton fields filled.</returns>
    public static async Task<SectionInputs> RunGluttonAsync(IReadOnlyList<string> servers)
    {
        var budget = ProbeBudget.DefaultGlutton;
        var inputs = new SectionInputs();

        inputs.Ecs = await ProbeRunner.RunAsync<ECSLeak>("ecs", budget, () =>
            ECSProbe.Probe(server: servers.FirstOrDefault() ?? "1.1.1.1"));

        string subdomain = LeakDetector.GenerateTestSubdomain();
        _ = await ProbeRunner.RunAsync<Dictionary<string, bool>>("triggerQueries", budget, () =>
            LeakDetector.TriggerQueriesAsync(subdomain, servers.ToList()));

        // Without a collector URL, the leak result is always inconclusive.
        inputs.Leak = new LeakTestResult { Result = LeakResult.Inconclusive,
            ErrorReason = "Local check only; use collector for confirmed result." };

        return inputs;
    }
}
