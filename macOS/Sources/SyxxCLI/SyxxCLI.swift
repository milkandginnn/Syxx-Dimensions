import Foundation
import SyxxKit
import SyxxTelemetry

/// B253 — machine-readable process results for every `syxx` subcommand.
///
/// Shared contract with `WitnessExitStatus` (`SyxxWitness.swift`) and the Windows
/// `WitnessCommand.ExitStatus` / leak/mirror exit paths:
/// - `0` completed non-threat (clean measurement, help, successful non-adjudicating work)
/// - `1` detected threat / tampering / confirmed leak / filter failure where applicable
/// - `2` inconclusive or invalid evidence (could not adjudicate)
/// - `3` operational failure (misuse, I/O error, missing dependency)
///
/// HELP COPY: top-level `syxx --help` does not yet document these codes. Pending owner-approved
/// help block (template is the Witness usage text). Exit codes are wired first; help text is not
/// invented here.
enum CLIExitStatus: Int32 {
    case completed = 0
    case threat = 1
    case inconclusive = 2
    case failure = 3
}

@main
struct SyxxCLI {
    // B253: standardized exit codes — 0=completed non-threat, 1=threat, 2=inconclusive, 3=failure
    nonisolated(unsafe) static var exitCode: Int32 = CLIExitStatus.inconclusive.rawValue

    static func main() async {
        // DEFER ORDERING IS LOAD-BEARING. Defers run LIFO, so this exit defer — registered FIRST —
        // runs LAST. The flush registered below therefore runs BEFORE the process exits. Reverse
        // the two and every CLI span is lost: BatchSpanProcessor's default schedule delay is longer
        // than most CLI invocations, so nothing has been exported yet when Darwin.exit fires.
        defer { Darwin.exit(exitCode) }

        // The macOS CLI had NEITHER Sentry nor OTel — an inverted parity gap, since the Windows CLI
        // has reported crashes all along. A CLI crash on a user's machine produced no signal at all.
        // Bootstrap must be the first statement: SyxxTracer memoises its tracer on first use, so a
        // span created before registration would pin the process to a no-op tracer permanently.
        SentryBootstrap.setup()
        SentryDiagnosticSink.install()
        OTelBootstrap.setup(serviceName: "syxx-cli-macos")
        defer { OTelBootstrap.flush() }

        // Unified binary: parse subcommand or fall through to interactive mode
        let args = CommandLine.arguments
        if args.count > 1 {
            let subcommand = args[1].lowercased()
            switch subcommand {
            // B253: every adjudicating subcommand returns a typed process result. Unconditional
            // `exitCode = 0` after a void run is forbidden wherever a typed result exists — that
            // path made confirmed leaks, tampering, and incomplete measurements read as clean.
            case "nslook":
                exitCode = await NslookCLI.run(args: Array(args.dropFirst(2)))
            case "mirror":
                exitCode = await MirrorCLI.run(args: Array(args.dropFirst(2)))
            case "leak":
                exitCode = await LeakCLI.run(args: Array(args.dropFirst(2)))
            case "witness":
                exitCode = await WitnessCLI.run(args: Array(args.dropFirst(2)))
            case "key":
                exitCode = await KeyCLI.run(args: Array(args.dropFirst(2)))
            case "route":
                exitCode = await RouteCLI.run(args: Array(args.dropFirst(2)))
            case "lan":
                exitCode = await LanCLI.run(args: Array(args.dropFirst(2)))
            case "forensics":
                exitCode = await ForensicsCLI.run(args: Array(args.dropFirst(2)))
            case "sentinel":
                exitCode = await SentinelCLI.run(args: Array(args.dropFirst(2)))
            case "cage":
                exitCode = await CageCLI.run(args: Array(args.dropFirst(2)))
            case "warp":
                exitCode = await WarpCLI.run(args: Array(args.dropFirst(2)))
            case "speed":
                exitCode = await SpeedCLI.run(args: Array(args.dropFirst(2)))
            case "unbound":
                exitCode = await UnboundCLI.run(args: Array(args.dropFirst(2)))
            case "filter":
                exitCode = await FilterCLI.run(args: Array(args.dropFirst(2)))
            case "firstrun":
                exitCode = await FirstRunCommand.run(args: Array(args.dropFirst(2)))
            // Phase 4.2: CI-only behavioral transcript emitter (production Synthesizer path).
            case "parity-transcript":
                exitCode = ParityTranscriptCommand.run(args: Array(args.dropFirst(2)))
            // B253: every adjudicating subcommand returns a typed process result. Unconditional
            // exiting 2 made `syxx --help` indistinguishable from a scan that measured nothing.
            case "--help", "-h":
                printHelp()
                exitCode = CLIExitStatus.completed.rawValue
            // B240: reserve -V for version (not -v) to avoid collision with -v = verbose in
            // SyxxConfig.isVerbose. --version is the canonical long form.
            case "--version", "-V":
                printVersion()
                exitCode = CLIExitStatus.completed.rawValue
            default:
                print("Unknown subcommand '\(args[1])'. Use 'syxx --help' for usage.")
                exitCode = CLIExitStatus.failure.rawValue
            }
            return
        }

        // Interactive guided mode (no args)
        // B240: handleGlobalFlags and isVerbose/isJsonOutput were computed here but only reachable
        // when args.count <= 1 (binary name only) — guaranteed false for both flags. The global-flag
        // accessors are available to subcommands that parse their own argv; the dead locals are removed.
        // --json output for conclusion-bearing subcommands remains unimplemented (B137 Fix 3).
        if SyxxConfig.handleGlobalFlags(CommandLine.arguments, tool: "syxx",
            usage: "Interactive launcher. Six checks: Prophet, Mirror, Portal, Glutton, Cage, Witness (+ License).") { return }
        print("""

                                         kmOQO
                                    LLQCUXh#
                                 X]]>ii?<!Ykkk#
                             dv][!i\\     c]+1bbbh
                           j]]!,           r?]dddwmk
                       $/][l'              O]>pqqd/]OZp%
                    q)]]l`                W?]]wwwd   z]J0w%
                  0]-l'                  \\?][mmmm       L[zQd
                  C[,                 q[??]COOOa          uQw
                  J]:               (????JQQZ             vLq
                  C]:            v_--?fCCw                vLq
                  C]:          [+__cU0tuZZO0wh            cLw
                  J[:        {~\\wbdfYZZZOOO0QLLo          cCw
                  J]:      $udbdZtmm]>;/}]]]-~~JCd        vCZ
                  J[:     &ddddtZh         u?????(#       vJm
                  C];    &qppp/Z             {??---       uJm
                  J];    *qqqLt               -----(      vUm
                  C];    dwwwJL               x---_]      cUm
                  U];    dwwmZU               j-___?      vUZ
                  C];    omZZZC               _____<      cYm
                  L]?(   #ZOOO00             r_____      0vYm
                    01]?) B000QLLm         Or___+_    JzYCo
                       $(]]}OQLLCCCJJCQJUYY_____-  YXYLC
                          ]|??LCCCJJUUUYY-____-8cYYQ]
                             X/--jzcr}______(nYY0
                                 f--ij   hnYYm
                                    r-_1UUq
                                       X$

          Syxx
          A DNS investigative and protection suite.
          © J.J. Warne d/b/a MilkandGin
          All rights reserved.
          feedback@milkandgin.com

        """)

        let accountID = SyxxConfig.accountID
        // B233: distinguish four states — valid / invalid / timeout / networkError — so a
        // blocked network does not demote a paying Pro to "LITE" and so activation failure
        // names the observed cause instead of inventing a server-side rejection. The collapse
        // to Bool made the exact networks this tool investigates indistinguishable from a bad
        // key, bypassing the E-7 offline grace that isFeatureUnlocked() honours.
        var isPro = false
        var licenseStatusLine = "🔴 LITE VERSION"

        if let storedKey = LicenseManager.getStoredLicenseKey() {
            print("🔑 Local license key found. Verifying with Keygen...")
            switch await LicenseManager.validateLicenseDetailed(key: storedKey, accountID: accountID) {
            case .valid:
                isPro = true
                licenseStatusLine = "🟢 PRO ACTIVATED"
            case .invalid:
                licenseStatusLine = "🔴 LITE VERSION (key rejected by licensing service)"
            case .timeout:
                // Unverified — could not reach Keygen. The 30-day offline grace inside
                // isFeatureUnlocked() may still grant Pro; consult it rather than demoting.
                let fallback = await LicenseManager.isFeatureUnlocked()
                isPro = fallback.unlocked
                licenseStatusLine = fallback.unlocked
                    ? "🟡 \(fallback.message) (Pro features unchanged — network could not verify)"
                    : "🟡 UNVERIFIED — could not reach the licensing service (network timeout); Pro features unchanged pending verification"
            case .networkError(let detail):
                let fallback = await LicenseManager.isFeatureUnlocked()
                isPro = fallback.unlocked
                licenseStatusLine = fallback.unlocked
                    ? "🟡 \(fallback.message) (Pro features unchanged — network could not verify)"
                    : "🟡 UNVERIFIED — \(detail); Pro features unchanged pending verification"
            }
        }

        var inputs = SectionInputs()
        var running = true
        // ANSI color codes matching app warden colors
        let teal = "\u{001B}[38;2;45;212;191m"      // Prophet, Portal (#2dd4bf)
        let violet = "\u{001B}[38;2;167;139;255m"    // Mirror, Witness (#a78bff)
        let red = "\u{001B}[38;2;255;77;109m"        // Glutton (#ff4d6d)
        let blue = "\u{001B}[38;2;124;92;255m"       // Cage (#7c5cff)
        let reset = "\u{001B}[0m"
        while running {
            print("==================================================================================")
            // B233: render the four-state line computed above — never collapse a timeout or
            // network error to "LITE VERSION", which asserts a server-side determination the
            // code never observed and demotes a paying subscriber on a blocked network.
            print("License status: \(licenseStatusLine)")
            print("----------------------------------------------------------------------------------")
            print("Choose a check:")
            print("  \(teal)\u{16A8}\(reset) [1] Prophet      Tests your DNS resolver for accuracy.")
            print("  \(violet)\u{16DE}\(reset) [2] Mirror       Tests downloaded web content against a remote copy to ensure it matches.")
            print("  \(teal)\u{16DC}\(reset) [3] Portal       Tests your local LAN state and the route taken to a chosen domain.")
            print("  \(red)\u{16A6}\(reset) [4] Glutton      A DNS leak test — diagnoses vulnerable VPN connections.")
            print("  \(blue)\u{16BE}\(reset) [5] Cage         DNS capture & evaluation — processes behind queries, beaconing & attacks.")
            print("  \(violet)\u{16C9}\(reset) [6] Witness      Trust analysis for your home or office computer environment.")
            print("  [C] Conclusions  Synthesize a unified conclusion from all completed checks.")
            print("  [L] License      Register / manage your license key.")
            print("  [Q] Quit")
            print("==================================================================================")
            print("Your choice (1-6 / C / L / Q): ", terminator: "")

            guard let raw = readLine()?.trimmingCharacters(in: .whitespacesAndNewlines).uppercased() else { break }

            switch raw {
            case "1", "PROPHET":
                await runProphet(domain: promptDomain(why: "What is domain of your choosing? (Ex. github.com, reddit.com, or syxx.app) The Prophet asks your resolver for its answer and weighs whether that answer is true. (Choose a domain. The Prophet holds your resolver's answer up against what the domain's own authorities attest, and against names that ought not exist, to judge whether it may be believed.)"), inputs: &inputs)
            case "2", "MIRROR":
                await runMirror(target: promptDomain(why: "In the mirror you shall see the domain or site of web for which you are endeared: The light of the mirror breaks the bonds of deception, revealing whether the site you receive is the same one shown to all. (The Mirror compares cryptographic hashes of the page as your network delivers it against a copy fetched through a clean, independent path, confirming that what you receive is the content served to everyone else.)", prompt: "Enter the website/URL to mirror-check (e.g. example.com): "), isPro: isPro, inputs: &inputs)
            case "3", "PORTAL":
                await runPortal(domain: promptDomain(why: "Of all domains, to which shall the Portal deliver you today? The Portal traces the course your traffic must follow to reach that far host. Bearing you all the way, it delivers you along the traced route in less than a blink. (A traceroute follows the packets sent from your device to the domain or IP you chose, mapping the nodes, or hops, they pass through. If traffic is being redirected, it can help reveal where. If the last hop falls short of your domain, it may have been blocked by a firewall or swept into a web proxy.)"), inputs: &inputs)
            case "4", "GLUTTON":
                await runGlutton(isPro: isPro, inputs: &inputs)
            case "5", "CAGE":
                await runCage(inputs: &inputs)
            case "6", "WITNESS":
                runWitness(isPro: isPro, inputs: &inputs)
            case "C", "CONCLUSIONS", "CONCLUSION":
                showConclusions(inputs: inputs)
            case "L":
                print("\nEnter, ye, your key of license, and allow the good fortunes of the realm to draw you in: (Your license key unlocks the app's Pro checks and the features anchored to the Dimension. It is validated with the licensing service and then kept on this machine, and it never travels alongside your DNS data.) By this separation, your network findings remain unbound from your identity.")
                print("Enter your Keygen.sh License Key: ", terminator: "")
                guard let key = readLine()?.trimmingCharacters(in: .whitespacesAndNewlines), !key.isEmpty else {
                    print("Error: Key cannot be empty."); continue
                }
                print("Verifying license with Keygen...")
                // B233: branch on the full four-state result, so the failure message names
                // the observed cause rather than asserting "invalid, expired, or locked" —
                // three causes the code cannot distinguish — after a DNS/TLS timeout.
                let activationResult = await LicenseManager.validateLicenseDetailed(key: key, accountID: accountID)
                switch activationResult {
                case .valid:
                    // D042: do not print SUCCESS unless the key is actually persisted. A silent
                    // Keychain write failure (disk full, Keychain locked, sandbox restriction)
                    // would leave the user believing the license is activated when it is not.
                    do {
                        try LicenseManager.saveLicenseKey(key)
                        isPro = true
                        licenseStatusLine = "🟢 PRO ACTIVATED"
                        print("SUCCESS! License key activated. Pro features unlocked.")
                    } catch {
                        print("WARNING: License verified with Keygen but could not be saved to Keychain — you may need to re-enter it next launch. Error: \(error.localizedDescription)")
                        // Still grant Pro for this session, but warn the user.
                        isPro = true
                        licenseStatusLine = "🟢 PRO ACTIVATED (Keychain save failed — re-enter next launch)"
                    }
                case .invalid:
                    print("FAILED! The licensing service rejected this key (it is not recognised, revoked, or otherwise not valid for this account).")
                case .timeout:
                    print("FAILED! Could not reach the licensing service (request timed out). Your key was NOT rejected — retry when the network is available.")
                case .networkError(let detail):
                    print("FAILED! Could not reach the licensing service — \(detail). Your key was NOT rejected — retry when the network is available.")
                }
            case "Q", "QUIT", "EXIT":
                print("\nThank you for using Syxx. Goodbye!")
                // B232: an explicit quit is a successful interactive session — not an
                // inconclusive measurement. Without this, `syxx` with no args followed by
                // [Q] would exit 2.
                exitCode = 0
                running = false
            default:
                print("Invalid choice. Enter 1-6, C, L, or Q.")
            }
        }
    }

    // MARK: - Conclusions (C option)

    /// Synthesizes a result from all completed checks and displays it — only when the
    /// user explicitly asks. No auto-result between runs.
    private static func showConclusions(inputs: SectionInputs) {
        print("")
        print("==================================================================================")
        print("CONCLUSIONS — SYNTHESIS CONCLUSION")
        print("----------------------------------------------------------------------------------")
        let conclusion = Synthesizer.conclude(from: inputs)
        // A3 — the glanceable report card. Ungraded ("—") when the run could not be measured.
        let card = ReportCards.For(conclusion)
        print("🎓 GRADE: \(card.gradeLabel): \(card.headline)")
        for reason in card.topReasons {
            print("     • \(reason)")
        }
        print("")
        // Result now comes from the matrix determinative engine (`conclusion.determination`).
        let va = ResultActions.For(conclusion)
        let resultStr: String
        if let det = conclusion.determination {
            switch det.state {
            case .untested:       resultStr = "UNTESTED"; exitCode = 2
            case .inconclusive:   resultStr = "INCONCLUSIVE"; exitCode = 2
            case .clean:          resultStr = "CLEAN"; exitCode = 0
            case .threatDetected: resultStr = (det.diagnoses.first?.displayName ?? "TAMPERING").uppercased(); exitCode = 1
            }
        } else {
            resultStr = "UNFINISHED"; exitCode = 3
        }
        print("📊 CONCLUSION: \(resultStr)")
        print("   Severity: \(va.severity.rawValue.uppercased())")
        if let s = conclusion.determination?.diagnoses.first?.strength {
            print("   Strength: \(Int((s * 100).rounded()))%")
        }
        // A1 — the "so what": what it means + the one action that matters.
        print("   What this means: \(va.meaning)")
        print("   Do this: \(va.primaryAction)")
        // A2 — plain-language "why we think so" over the matrix determinative result.
        let expl = PlainExplanations.For(conclusion)
        print("   Why we think so: \(expl.narrative)")
        print("   Key Observations:")
        if conclusion.evidence.isEmpty {
            print("     (No evidence collected yet — run checks first to gather network intelligence)")
        } else {
            for ev in conclusion.evidence {
                print("     - [\(ev.section.rawValue.uppercased())] \(ev.observation)")
            }
        }
        if let det = conclusion.determination, !det.suppressed.isEmpty {
            print("   ⚠️ UNVERIFIED SIGNALS (seen but not confirmable):")
            for s in det.suppressed {
                print("     - \(s.displayName): \(s.detail)")
            }
        }
        print("----------------------------------------------------------------------------------")
        print("Press Enter to return to the menu.", terminator: "")
        _ = readLine()
    }

    // MARK: - Prompt helpers

    static func promptDomain(why: String, prompt: String = "Enter the domain to check (e.g. google.com): ") -> String {
        print("\n\(why)")
        print(prompt, terminator: "")
        guard let raw = readLine()?.trimmingCharacters(in: .whitespacesAndNewlines), !raw.isEmpty else { return "" }
        // F007: canonicalize at the CLI boundary so DNS queries always receive a valid A-label.
        guard let canonical = DomainCanonical.canonicalizeOrNil(raw) else {
            print("Error: Invalid domain name '\(raw)'.")
            return ""
        }
        return canonical
    }

    // MARK: - 1. Prophet — DNS Resolution & Protocol Integrity

    static func runProphet(domain: String, inputs: inout SectionInputs) async {
        guard !domain.isEmpty else { print("Error: Domain name cannot be empty."); return }
        print("\n----------------------------------------------------------------------------------")
        print("🔮 PROPHET — is your resolver telling the truth for: \(domain)")
        print("----------------------------------------------------------------------------------")

        let servers = NetworkKeyState.getSystemDNSServers()
        let localResolver: String
        if let first = servers.first {
            localResolver = first
        } else {
            print("⚠️ System resolver undetectable — falling back to 8.8.8.8")
            localResolver = "8.8.8.8"
        }
        let encryption = NetworkKeyState.encryptedDNSStatus()
        print("• Current local DNS server: \(localResolver)")
        switch encryption.state {
        case .yes: print("• System DNS encryption: 🔒 Encrypted")
        case .no:  print("• System DNS encryption: 🔓 Unencrypted (standard DNS)")
        case .unknown: print("• System DNS encryption: ❓ Unknown (\(encryption.evidence))")
        }

        var budget = ProbeBudget.defaultProphet

        let resp = await ProbeRunner.run(label: "resolution.a", budget: &budget) {
            await DNSResolver.queryUDPDetailed(DNSQuery(name: domain, type: 1, server: localResolver))
        } ?? DNSResponse(status: .timeout, errorMessage: "Resolution A timed out")
        if resp.status == .success {
            print("\nResolved A records:")
            for rec in resp.records { print("  • \(rec.rdata) (TTL \(rec.ttl)s)") }
        } else {
            print("\n❌ No A records returned (\(resp.status.rawValue)). Not evidence of safety — compare the typed status.")
        }

        let aaaaResp = await ProbeRunner.run(label: "resolution.aaaa", budget: &budget) {
            await DNSResolver.queryUDPDetailed(DNSQuery(name: domain, type: 28, server: localResolver))
        } ?? DNSResponse(status: .timeout, errorMessage: "Resolution AAAA timed out")
        if aaaaResp.status == .success {
            print("Resolved AAAA records:")
            for rec in aaaaResp.records { print("  • \(rec.rdata) (TTL \(rec.ttl)s)") }
        }

        print("\n• DNSSEC protocol integrity:")
        // `nil` here means the probe never completed — budget exhausted, runner timeout, or a
        // thrown error. Defaulting to `.unsigned` turned that into a positive factual claim
        // ("NO DNSSEC RECORDS OBSERVED (zone appears unsigned)") derived from zero successful
        // reads. `.inconclusive` is the honest state and already exists for exactly this.
        let status = await ProbeRunner.run(label: "dnssec", budget: &budget) {
            await DNSSECVerifier.checkStatus(domain: domain, resolver: localResolver)
        } ?? .inconclusive
        print("  \(status.description)")

        inputs.resolution = resp
        inputs.dnssec = status
        inputs.nxCanary = await ProbeRunner.run(label: "nxCanary", budget: &budget) {
            await DNSCanaries.nxdomainHijack(resolver: localResolver)
        }
        inputs.dnssecCanary = await ProbeRunner.run(label: "dnssecCanary", budget: &budget) {
            await DNSCanaries.dnssecControl(resolver: localResolver)
        }
        if let nx = inputs.nxCanary { print("• NXDOMAIN canary   : \(nx.status) — \(nx.detail)") }
        if let ds = inputs.dnssecCanary { print("• DNSSEC canary     : \(ds.status) — \(ds.detail)") }
        inputs.latency = await ProbeRunner.run(label: "latency", budget: &budget) {
            await ResolverLatencyProbe.probe(domain: domain, qtype: 1, server: localResolver)
        }
        print("• Latency check    : \(inputs.latency.map { "\($0.medianMs)ms" } ?? "failed")")
        inputs.stability = await ProbeRunner.run(label: "stability", budget: &budget) {
            await AnswerStabilityProbe.probe(domain: domain, qtype: 1, server: localResolver)
        }
        print("• Answer stability  : \(inputs.stability?.status.rawValue ?? "failed")")
        inputs.wildcardSynthesis = await ProbeRunner.run(label: "wildcard", budget: &budget) {
            await WildcardSynthesisProbe.probe(domain: domain, resolverUnderTest: localResolver)
        }
        print("• Wildcard check    : \(inputs.wildcardSynthesis?.status.rawValue ?? "failed")")
        inputs.denial = await ProbeRunner.run(label: "denial", budget: &budget) {
            await DenialOfExistenceProbe.probe(name: domain, qtype: 1, server: localResolver)
        }
        print("• Denial detection  : \(inputs.denial?.status.rawValue ?? "failed")")
        inputs.dualStack = await ProbeRunner.run(label: "dualStack", budget: &budget) {
            await DualStackProbe.probe(domain: domain, server: localResolver)
        }
        print("• Dual-stack check  : \(inputs.dualStack?.status.rawValue ?? "failed")")
        inputs.edns0 = await ProbeRunner.run(label: "edns0", budget: &budget) {
            await EDNS0Probe.probe(domain: domain, server: localResolver)
        }
        print("• EDNS0 capability  : \(inputs.edns0?.status.rawValue ?? "failed")")
        inputs.versionBind = await ProbeRunner.run(label: "versionBind", budget: &budget) {
            await VersionBindProbe.probe(server: localResolver)
        }
        if let vb = inputs.versionBind {
            switch vb.status {
            case .disclosed:
                print("\n• Resolver fingerprint (version.bind): \(vb.banner ?? "—")"
                    + (vb.forwarderOnPublicPath
                        ? "  ⚠️ a resolver expected to be public is self-identifying as local-forwarder software — something local is on the path"
                        : ""))
            case .withheld:
                print("\n• Resolver fingerprint (version.bind): withheld (hardened / public resolver) — no signal, not a clean result on its own")
            case .untested:
                print("\n• Resolver fingerprint (version.bind): could not be obtained — unmeasured, not a clean result")
            }
        }

        print("----------------------------------------------------------------------------------\n")
    }

    // MARK: - 2. Mirror — Tampering & Censorship Detection

    static func runMirror(target: String, isPro: Bool, inputs: inout SectionInputs) async {
        guard !target.isEmpty else { print("Error: Target cannot be empty."); return }
        let rawDomain = hostOnly(target)
        // F007: canonicalize at the CLI boundary.
        guard let domain = DomainCanonical.canonicalizeOrNil(rawDomain) else {
            print("Error: Invalid domain name '\(rawDomain)'.")
            return
        }
        print("\n----------------------------------------------------------------------------------")
        print("🪞 MIRROR — does what you receive match independent copies: \(domain)")
        print("----------------------------------------------------------------------------------")

        let servers = NetworkKeyState.getSystemDNSServers()
        let localResolver: String
        if let first = servers.first {
            localResolver = first
        } else {
            print("⚠️ System resolver undetectable — falling back to 8.8.8.8")
            localResolver = "8.8.8.8"
        }
        var budget = ProbeBudget.defaultMirror

        // Vantage comparison (throws on transport failure)
        let vantageResult: SyxxKit.ComparisonResult? = (await ProbeRunner.run(label: "vantage", budget: &budget) {
            do {
                return try await VantageComparer.compareVantagePoints(domain: domain, localServer: localResolver)
            } catch {
                print("❌ Vantage comparison failed: \(error.localizedDescription)")
                return nil as SyxxKit.ComparisonResult?
            }
        }).flatMap { $0 }
        inputs.comparison = vantageResult
        inputs.targetDomain = domain   // Stage-5 answer-matrix query context (type defaults to A)

        if let result = vantageResult {
            // A2: discover the delegation via TRUSTED resolvers independent of the one under test (B031)
            let bootstrapResolvers = AuthoritativeVantage.defaultBootstrapResolvers.filter { $0 != localResolver }
            inputs.authoritative = await ProbeRunner.run(label: "authoritative", budget: &budget) {
                await AuthoritativeVantage.compare(domain: domain, resolverAnswers: result.localRecords.map { $0.rdata }, bootstrapResolvers: bootstrapResolvers, resolverDidFail: result.localOutcome.didFail)
            }
            // A2 Pro corroboration (additive)
            if isPro, inputs.authoritative?.status == .diverges {
                let client = DimensionClient(baseURL: SyxxConfig.dimensionBaseURL, licenseKey: LicenseManager.getStoredLicenseKey())
                // D081: DimensionOutcome distinguishes "fetched" from "transport failure" / "decode failure"
                let outcome = await ProbeRunner.run(label: "groundTruth", budget: &budget) {
                    await client.authoritative(domain: domain, type: "A")
                }
                if case .fetched(let gt) = outcome {
                    inputs.serverGroundTruth = gt
                    print("• Dimension ground truth : server-confirmed reference obtained ✅")
                }
            }
            // A3: SVCB, TTL, CNAME
            inputs.svcb = await ProbeRunner.run(label: "svcb", budget: &budget) {
                // B056: run SVCB divergence over the vetted trio only — mixing the arbitrary
                // local resolver into the "known-modern" comparison set made a legacy router
                // that doesn't forward type 65 read as a transport downgrade (svcbStripped).
                await SVCBProbe.probe(domain: domain, resolvers: ["1.1.1.1", "8.8.8.8", "9.9.9.9"])
            }
            inputs.ttlAnomaly = await ProbeRunner.run(label: "ttl", budget: &budget) {
                await TTLAnomalyProbe.probe(domain: domain, resolvers: [localResolver] + ["1.1.1.1", "8.8.8.8", "9.9.9.9"].filter { $0 != localResolver })
            }
            inputs.cnameChain = await ProbeRunner.run(label: "cname", budget: &budget) {
                await CNAMEChainProbe.probe(domain: domain, resolverUnderTest: localResolver)
            }
            // Display results
            func describe(_ o: ResolverQueryOutcome) -> String {
                if let e = o.errorDescription { return "[failed: \(e)]" }
                return o.records.isEmpty ? "[no A records]" : o.records.map { $0.rdata }.joined(separator: ", ")
            }
            print("Comparing local answers against public vantage points:")
            print("  - Local resolver: \(describe(result.localOutcome))")
            for name in VantageComparer.publicResolvers.keys.sorted() {
                if let o = result.publicResolverOutcomes[name] { print("  - \(name): \(describe(o))") }
            }
            print("\n🔮 TAMPERING ADVISORY:")
            if result.isInconclusive {
                print("⚠️  INCONCLUSIVE — no public vantage point returned a usable baseline. Not evidence of safety.")
            } else if result.isRedirectionDetected {
                let blocking = result.localRecords.contains { IPClassifier.isBlockingAnswer($0.rdata) }
                if blocking || result.localRecords.isEmpty {
                    print("⚠️  CENSORED/BLOCKED — local network returned an internal/invalid IP while public servers resolved it.")
                } else {
                    print("🚨 MALICIOUSLY REDIRECTED — local resolver returned a real but wrong IP. DO NOT enter credentials.")
                }
            } else {
                if result.isDegraded {
                    print("⚠️  No tampering detected, but the baseline was DEGRADED.")
                } else {
                    print("✅ SECURE — no answer tampering, censorship, or redirection detected for \(domain) (A-record comparison only).")
                }
            }
        } else {
            print("⚠️  Vantage comparison could not complete — all probes timed out or failed.")
        }

        // Content-level mirror (WARP_MANIFEST_V1 signed-manifest content integrity)
        print("\n• Web content integrity (signed manifest verified against your vantage):")
        // B234: entitlement checks are NOT DNS probes — do not wrap them in ProbeRunner.run
        // with its 2.5 s per-probe timeout. A Keygen HTTPS round-trip on a degraded network
        // (the exact network this tool investigates) routinely exceeds that budget, coalescing
        // nil to "License check failed" and upselling a paying subscriber. Call directly, as
        // SyxxWarp.swift does.
        let check = await LicenseManager.isFeatureUnlocked()
        if !check.unlocked {
            print("  🔒 \(check.message) — content mirroring is a server-anchored Pro feature (Option L to unlock).")
        } else {
            let origin = target.contains("://") ? target : "https://\(domain)"
            let client = DimensionClient(baseURL: SyxxConfig.dimensionBaseURL, licenseKey: LicenseManager.getStoredLicenseKey())
            let warp = await ProbeRunner.run(label: "warp", budget: &budget) {
                await WarpEngine.runVantageCheck(origin: origin, client: client)
            }
            inputs.warpManifest = warp
            if let w = warp {
                switch w.conclusion {
                case .verifiedMatch:
                    print("  ✅ VERIFIED_MATCH — a pinned, fresh signed manifest matches the bytes your vantage fetched.")
                case .tamperingDetected:
                    print("  🚨 TAMPERING_DETECTED — a verified, fresh manifest disagrees byte-for-byte with your vantage (or the signature is invalid).")
                case .inconclusive:
                    print("  ⚠️  INCONCLUSIVE — \(w.inconclusiveReason ?? "untrusted/unpinned signer, stale freshness, or unfetchable content").")
                }
            } else {
                print("  ⚠️  INCONCLUSIVE — content comparison could not complete (timeout or failure).")
            }
        }
        print("----------------------------------------------------------------------------------\n")
    }

    // MARK: - 3. Portal — Network & Path Diagnostics

    static func runPortal(domain: String, inputs: inout SectionInputs) async {
        print("\n----------------------------------------------------------------------------------")
        print("🌀 PORTAL — your local network, and the path to \(domain.isEmpty ? "(no domain)" : domain)")
        print("----------------------------------------------------------------------------------")

        let resolvers = NetworkKeyState.getSystemDNSServers()
        let interfaces = NetworkKeyState.getActiveInterfaces()
        let vpn = NetworkKeyState.vpnStatus()
        print("• Resolvers        : \(resolvers.isEmpty ? "(none)" : resolvers.joined(separator: ", "))")
        print("• Active interfaces: \(interfaces.joined(separator: ", "))")
        print("• VPN              : \(triLabel(vpn.state)) (\(vpn.evidence))")

        var budget = ProbeBudget.defaultPortal

        inputs.network = await ProbeRunner.run(label: "networkReport", budget: &budget) {
            await NetworkKeyState.generateReport()
        }
        inputs.encryptedTransport = await ProbeRunner.run(label: "encryptedTransport", budget: &budget) {
            await EncryptedTransportProbe.probe()
        }

        // Open Resolver & Amplification exposure (advisory) — scoped to YOUR OWN network devices only.
        if let ownResolver = resolvers.first(where: { isOwnNetworkAddress($0) }) {
            inputs.openResolver = await ProbeRunner.run(label: "openResolver", budget: &budget) {
                await OpenResolverProbe.probe(target: ownResolver)
            }
            switch inputs.openResolver?.status {
            case .recursiveHighAmp: print("• Open resolver    : 🚨 \(inputs.openResolver!.detail)")
            case .recursiveLowAmp:  print("• Open resolver    : ⚠️ \(inputs.openResolver!.detail)")
            case .notRecursive:     print("• Open resolver    : ✓ your LAN resolver (\(ownResolver)) does not openly recurse for us")
            case .untested:         print("• Open resolver    : (unmeasured — \(ownResolver) did not respond)")
            case .none:             break
            }
        } else {
            print("• Open resolver    : (skipped — your configured resolver is public; this check applies to your own LAN devices)")
        }

        // LAN cartography + rogue-resolver cross-reference (synchronous, not ProbeRunner-wrapped).
        do {
            let snapshot = try LANCartographer().snapshot()
            let resolverSet = Set(resolvers)
            print("• On-link devices  : \(snapshot.summary.deviceCount)")
            for d in snapshot.devices {
                let flag = resolverSet.contains(d.ipAddress) ? "  ⟵ one of your DNS resolvers" : ""
                print("    \(d.ipAddress)\t\(d.macAddress)\t[\(d.addressKind.rawValue)]\(flag)")
            }
        } catch {
            print("• LAN scan unavailable: \(error.localizedDescription)")
        }

        guard !domain.isEmpty, isSafeTraceTarget(domain) else {
            print("\n(No valid domain given — skipping route trace.)")
            print("----------------------------------------------------------------------------------\n")
            return
        }
        print("\n• Route to \(domain):")
        let hops = await TracerouteRunner.trace(target: domain, maxHops: 15)
        if hops.isEmpty {
            print("    (No hops returned — the trace could not run or timed out.)")
        } else {
            for hop in hops { print("    \(hop)") }
        }
        print("💡 If the trace stops early, the last responding hop is where interception/blocking begins.")
        print("----------------------------------------------------------------------------------\n")
    }

    // MARK: - 4. Glutton — Privacy Leak Detection

    static func runGlutton(isPro: Bool, inputs: inout SectionInputs) async {
        print("\n----------------------------------------------------------------------------------")
        print("🍽️  GLUTTON — is your VPN/tunnel actually keeping DNS private?")
        print("----------------------------------------------------------------------------------")

        var budget = ProbeBudget.defaultGlutton

        // B234: entitlement check is NOT a DNS probe — do not wrap in ProbeRunner.run. The
        // per-probe timeout (2.5 s) is tighter than a Keygen round-trip on the degraded
        // networks Glutton targets, so a paying subscriber is told to buy the product they
        // own and Glutton returns without ever running the leak test.
        let check = await LicenseManager.isFeatureUnlocked()
        print("• Feature status: \(check.message)")
        if !check.unlocked {
            print("💡 Server-anchored feature. Register a license via [L] to unlock. Subscriptions are $3.95/month.")
            print("----------------------------------------------------------------------------------\n")
            return
        }

        let vpn = NetworkKeyState.vpnStatus()
        print("• VPN status: \(triLabel(vpn.state)) — \(vpn.evidence)")

        let servers = NetworkKeyState.getSystemDNSServers()
        if servers.isEmpty {
            print("⚠️  INCONCLUSIVE: No system DNS resolvers detected; refusing to fall back to public resolvers.")
            print("----------------------------------------------------------------------------------\n")
            return
        }
        let subdomain = LeakDetector.generateTestSubdomain(baseDomain: "leak.syxx.app")
        print("• Test label: \(subdomain)")
        inputs.ecs = await ProbeRunner.run(label: "ecs", budget: &budget) {
            await ECSProbe.probe(server: servers.first ?? "1.1.1.1")
        }
        // Premises first: show each probe's real outcome BEFORE drawing any conclusion.
        let results = await ProbeRunner.run(label: "triggerQueries", budget: &budget) {
            await LeakDetector.triggerQueries(subdomain: subdomain, servers: servers)
        } ?? [:]
        for (server, ok) in results { print("  - Probe via \(server): \(ok ? "sent" : "failed")") }

        // Only conclude from what actually happened.
        if results.contains(where: { $0.1 }) {
            inputs.leak = LeakTestResult(result: .inconclusive, errorReason: "Local check only; use collector for confirmed result.")
            print("\n💡 This local client dispatched signed probes. Confirmation of which resolver forwarded them")
            print("   requires the collector: syxx-leak --collector <url> --key <license>.")
        } else {
            inputs.leak = LeakTestResult(result: .inconclusive,
                errorReason: "All probe dispatches failed — system resolvers unreachable; the leak test did not run.")
            print("\n🚨 All probe dispatches FAILED — the leak test could not run, so nothing was measured.")
            print("   💡 This is NOT a clean result. Cause: your system resolvers are unreachable — no connectivity,")
            print("      a VPN/firewall blocking DNS, or a captive portal intercepting it.")
            print("   💡 To perfect the test: restore DNS connectivity (or disable the blocking VPN/firewall) and re-run;")
            print("      add --collector <url> --key <license> for a verified result.")
        }
        print("----------------------------------------------------------------------------------\n")
    }

    // MARK: - 5. Cage — Capture, Forensics & Attribution

    static func runCage(inputs: inout SectionInputs) async {
        print("\n----------------------------------------------------------------------------------")
        print("🪤 CAGE — what DNS traffic is flowing, and is any of it hostile?")
        print("----------------------------------------------------------------------------------")

        // The chief purpose of Cage is a live DNS-query capture. Ask how long to capture FIRST, then run
        // the real port-53 capture via the privileged syxx-cage tool and feed its NDJSON into the engine,
        // so the guided result actually reflects the capture — exactly what the standalone does.
        let duration = promptCaptureDuration()

        // Run the capture (elevating with sudo when we are not already root). The child prints its own
        // live readout + forensic report. On any failure it returns nil and has already explained why —
        // an unavailable capture leaves Cage UNTESTED, never falsely clean.
        guard let capturePath = await runCageCapture(durationSeconds: duration) else {
            print("----------------------------------------------------------------------------------\n")
            return
        }

        // Feed the capture-sourced datapoints (F3/F9/exfil) into the determinative engine.
        analyzeMonitorReport(path: capturePath, inputs: &inputs)
        print("----------------------------------------------------------------------------------\n")
    }

    /// Prompt for a packet-capture duration in seconds. Enter alone defaults to 60; an optional trailing
    /// "s" is accepted; values below 1 or above 9000 re-prompt.
    static func promptCaptureDuration() -> Int {
        print("\nAnd you are nonetheless asked before closing the cage for how long you wish it sealed. (Enter the duration of the packet capture in seconds, e.g. 15s, up to 9000s (2.5 hours). While closed, surf the web and use your apps as normal. You must produce DNS queries in order to capture and analyze them.)")
        while true {
            print("> ", terminator: "")
            guard let raw = readLine()?.trimmingCharacters(in: .whitespacesAndNewlines) else { return 60 }
            if raw.isEmpty { return 60 }
            var s = raw.lowercased()
            if s.hasSuffix("s") { s = String(s.dropLast()) }
            s = s.trimmingCharacters(in: .whitespaces)
            if let n = Int(s), n >= 1, n <= 9000 { return n }
            print("Please enter a whole number of seconds from 1 to 9000 (e.g. 60).")
        }
    }

    /// Locate and run the privileged `syxx-cage` capture for `durationSeconds`, elevating with sudo when
    /// we are not already root. Returns the path to the raw NDJSON capture it produced (retained via
    /// `--keep`), or nil when the capture could not run — in which case it has already printed an honest
    /// UNTESTED explanation. We never fabricate a result: an unavailable capture must leave Cage untested.
    static func runCageCapture(durationSeconds: Int) async -> String? {
        // Unified binary: re-exec THIS executable with the hidden `cage` subcommand rather than a
        // separate `syxx-cage` tool (which no longer exists). Prefer the resolved absolute path so
        // sudo does not have to PATH-resolve the name.
        let selfPath = Bundle.main.executablePath ?? CommandLine.arguments.first ?? "syxx"
        let capturesDir = CageHelpers.cageCapturesDir()
        let startTime = Date()

        let process = Process()
        if geteuid() == 0 {
            process.executableURL = URL(fileURLWithPath: selfPath)
            process.arguments = ["cage", "--duration", String(durationSeconds), "--keep"]
        } else {
            print("• Live packet capture needs administrator rights — you may be prompted for your password.")
            process.executableURL = URL(fileURLWithPath: "/usr/bin/sudo")
            process.arguments = [selfPath, "cage", "--duration", String(durationSeconds), "--keep"]
        }
        // Inherit stdio so the live capture readout shows and any sudo password prompt works.
        // Process() defaults to creating pipes for stdin/stdout/stderr, which breaks sudo's
        // password prompt (it needs the terminal's stdin to read the password and suppress echo).
        process.standardInput = FileHandle.standardInput
        process.standardOutput = FileHandle.standardOutput
        process.standardError = FileHandle.standardError
        do {
            try process.run()
            process.waitUntilExit()
        } catch {
            print("⚠️  Live capture could not start: \(error.localizedDescription). No packets were captured — Cage stays UNTESTED (this is NOT a clean result).")
            return nil
        }

        guard let path = CageHelpers.newestCaptureFile(in: capturesDir, after: startTime) else {
            print("⚠️  The capture produced no readable file, so nothing was measured — Cage stays UNTESTED (this is NOT a clean result).")
            return nil
        }
        return path
    }

    /// Feed a Syxx Cage Capture NDJSON report into the determinative engine. Only capture-sourced datapoints
    /// are derived (F3 stub entropy, F9 response size, exfil); a passive capture supplies none of the
    /// active cross-vantage premises, so the engine can only refer/inconclusive here — never a confident
    /// clean bill of health.
    static func analyzeMonitorReport(path: String, inputs: inout SectionInputs) {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else {
            print("❌ Could not read a report at \(path). Nothing was analyzed — this is NOT a clean result.")
            return
        }
        let records = MonitorReportAnalyzer.parseNDJSON(text)
        guard !records.isEmpty else {
            print("⚠️  No capture records parsed from that file. Nothing was measured — NOT a clean network, just no data.")
            return
        }
        let capInputs = MonitorReportAnalyzer.sectionInputs(from: records)
        inputs.stubEntropy = capInputs.stubEntropy
        inputs.responseSize = capInputs.responseSize
        inputs.exfil = capInputs.exfil
        let c = Synthesizer.conclude(from: capInputs)
        let outbound = records.filter { $0.network.direction.lowercased().contains("outbound") }.count

        print("\n📥 Ingested \(records.count) records (\(outbound) queries, \(records.count - outbound) responses)")
        if let se = capInputs.stubEntropy { print("• F3 stub/OS randomization : \(se.status.rawValue) — \(se.detail)") }
        if let rs = capInputs.responseSize { print("• F9 response size         : \(rs.status.rawValue) — \(rs.detail)") }

        let capResult: String
        if let det = c.determination {
            switch det.state {
            case .untested:       capResult = "UNTESTED"
            case .inconclusive:   capResult = "INCONCLUSIVE"
            case .clean:          capResult = "CLEAN"
            case .threatDetected: capResult = (det.diagnoses.first?.displayName ?? "TAMPERING").uppercased()
            }
        } else {
            capResult = "UNFINISHED"
        }
        print("\n📊 ENGINE CONCLUSION (capture-only): \(capResult)")
        if c.evidence.isEmpty {
            print("   (no engine evidence surfaced from this capture)")
        } else {
            for ev in c.evidence { print("   - [\(ev.section.rawValue.uppercased())] \(ev.observation)") }
        }
        if let det = c.determination {
            for s in det.suppressed { print("   ⚠️ \(s.displayName): \(s.detail)") }
        }

        print("\n💡 A passive capture carries no active cross-vantage premises (authoritative divergence,")
        print("   authenticated denial, redirection, collector-confirmed leak), so those sections stay")
        print("   untested — the engine cannot issue a clean bill of health from a capture alone.")
    }

    // MARK: - 6. Witness — Trust Infrastructure

    static func runWitness(isPro: Bool, inputs: inout SectionInputs) {
        print("\n----------------------------------------------------------------------------------")
        print("👁️  WITNESS — can this environment, and this app, be trusted?")
        print("----------------------------------------------------------------------------------")

        print("• App license : \(isPro ? "🟢 PRO (verified with Keygen)" : "🔴 LITE")")

        let current = NetworkConfigSnapshot.capture()
        print("• Environment snapshot:")
        print("    resolvers          : \(current.resolvers.isEmpty ? "(none)" : current.resolvers.joined(separator: ", "))")
        print("    active interfaces  : \(current.activeInterfaces.joined(separator: ", "))")
        print("    VPN active         : \(current.vpnActive.rawValue)")
        // C090: tri-state display — .unknown is a distinct outcome, not "no".
        print("    DNS encrypted      : \(current.dnsEncrypted.rawValue)")

        if let previous = Sentinel.loadSnapshot() {
            let changes = Sentinel.diff(previous: previous, current: current)
            // B087: preserve ConfigChange via the dedicated configChanges input (→ .inconclusive
            // per Synthesizer.witness), not fabricated AnswerDrift with empty domain/answers
            // (→ false .positive per drift).
            inputs.drifts = []
            inputs.configChanges = changes
            if changes.isEmpty {
                print("• Change since last Witness run: ✅ none — environment is stable.")
            } else {
                print("• 🚨 Change since last Witness run — re-run Prophet/Mirror/Glutton:")
                for c in changes { print("    • \(c.kind.rawValue): \(c.detail)") }
            }
        } else {
            // B087: no prior baseline means the witness adapter must return .untested, not
            // .negative. Set both to nil (not empty arrays) so the adapter's untested path
            // is reachable.
            inputs.drifts = nil
            inputs.configChanges = nil
            print("• No prior baseline — this snapshot is now the trusted baseline.")
        }
        try? Sentinel.saveSnapshot(current)
        print("\n💡 To sign a tamper-evident evidence snapshot of a comparison, use: syxx-witness")
        print("----------------------------------------------------------------------------------\n")
    }

    // MARK: - Shared helpers

    static func triLabel(_ s: ConditionState) -> String {
        switch s {
        case .yes: return "🔴 active/yes"
        case .no: return "🟢 inactive/no"
        case .unknown: return "❓ unknown"
        }
    }

    static func hostOnly(_ target: String) -> String {
        var s = target
        if let r = s.range(of: "://") { s = String(s[r.upperBound...]) }
        if let slash = s.firstIndex(of: "/") { s = String(s[..<slash]) }
        return s
    }

    static func isSafeTraceTarget(_ target: String) -> Bool {
        if target.isEmpty || target.hasPrefix("-") || target.rangeOfCharacter(from: .whitespacesAndNewlines) != nil {
            return false
        }
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-:")
        return target.rangeOfCharacter(from: allowed.inverted) == nil
    }

    /// True when `ip` is one of the user's OWN network addresses (private / LAN / loopback) — the only
    /// resolvers the open-resolver probe is allowed to target. Public resolvers are out of scope.
    static func isOwnNetworkAddress(_ ip: String) -> Bool {
        switch IPClassifier.classify(ip) {
        case .privateUse, .loopback, .linkLocal: return true
        default: return false
        }
    }
}


// MARK: - Subcommand dispatch structs

// Each command was extracted from a standalone tool whose argv was `[toolName, arg1, ...]`. Rebuild
// that shape from the dispatcher's stripped `args` so a command's own positional parsing (which
// starts at index 1) is not thrown off by the `syxx <sub>` prefix.
// B253: every wrapper returns the command's typed process result so the dispatcher never invents 0.
private enum NslookCLI {
    static func run(args: [String]) async -> Int32 { await SyxxNSLookCLI.run(["syxx-nslook"] + args) }
}
private enum MirrorCLI {
    static func run(args: [String]) async -> Int32 { await SyxxMirrorCLI.run(["syxx-mirror"] + args) }
}
private enum LeakCLI {
    static func run(args: [String]) async -> Int32 { await SyxxLeakCLI.run(["syxx-leak"] + args) }
}
private enum WitnessCLI {
    /// B253: propagates `syxx witness`'s real outcome to the process exit status instead of letting
    /// it inherit the default 2. Returns the code rather than assigning `SyxxCLI.exitCode` here, so
    /// the dispatch table is the single place exit codes are set.
    static func run(args: [String]) async -> Int32 {
        await SyxxWitnessCLI.run(["syxx-witness"] + args).rawValue
    }
}
private enum KeyCLI {
    static func run(args: [String]) async -> Int32 { await SyxxKeyCLI.run(["syxx-key"] + args) }
}
private enum RouteCLI {
    static func run(args: [String]) async -> Int32 { await SyxxRouteCLI.run(["syxx-route"] + args) }
}
private enum LanCLI {
    static func run(args: [String]) async -> Int32 { SyxxLanCLI.run(["syxx-lan"] + args) }
}
private enum ForensicsCLI {
    static func run(args: [String]) async -> Int32 { SyxxForensicsCLI.run(["syxx-forensics"] + args) }
}
private enum FilterCLI {
    static func run(args: [String]) async -> Int32 { await SyxxFilterCLI.run(["syxx-filter"] + args) }
}
private enum SentinelCLI {
    static func run(args: [String]) async -> Int32 { SyxxSentinelCLI.run(["syxx-sentinel"] + args) }
}
private enum CageCLI {
    static func run(args: [String]) async -> Int32 { await SyxxCageCLI.run(["syxx-cage"] + args) }
}
private enum WarpCLI {
    static func run(args: [String]) async -> Int32 { await SyxxWarpCLI.run(["syxx-warp"] + args) }
}
private enum SpeedCLI {
    static func run(args: [String]) async -> Int32 { await SyxxSpeedCLI.run(["syxx-speed"] + args) }
}
private enum UnboundCLI {
    static func run(args: [String]) async -> Int32 { await SyxxUnboundCLI.run(["syxx-unbound"] + args) }
}

// MARK: - Help and version

private func printHelp() {
    print("Usage: syxx [subcommand] [options]")
    print("")
    print("Subcommands:")
    print("  nslook     DNS lookup (type, resolver)")
    print("  mirror     Vantage comparison (local vs public)")
    print("  leak       VPN DNS leak test")
    print("  witness    Signed evidence (keygen, sign, verify)")
    print("  key        Network state (resolver, interface, VPN)")
    print("  route      Traceroute to a target")
    print("  lan        ARP/LAN map")
    print("  forensics  DNS forensics analysis")
    print("  sentinel   Config drift detection")
    print("  cage       Packet capture (requires sudo)")
    print("  warp       Content integrity check")
    print("  speed      Resolver speed benchmark (ranks all resolvers by latency)")
    print("  unbound    Local DNSSEC-validating resolver (verify | status | enable | disable)")
        print("  filter     DNS blocklist / Pi-hole enforcement + bypass check")
    print("  firstrun   Five-minute first-run scan — runs all disciplines, grades the network")
    print("")


    print("Without a subcommand, enters interactive guided mode.")
    print("Examples:")
    print("  syxx nslook google.com MX @8.8.8.8")
    print("  syxx mirror google.com")
    print("  syxx route 8.8.8.8")
    print("  sudo syxx cage --duration 10")
}

private func printVersion() {
    // Single source of truth. This was hardcoded "1.0.0" while SyxxConfig.version said "2.0.0",
    // so `syxx --version` and `syxx nslook --version` (which routes through
    // SyxxConfig.handleGlobalFlags) reported DIFFERENT versions from the same binary. A tool that
    // misreports its own version makes every field bug report ambiguous.
    print("Syxx \(SyxxConfig.version)")
    print("© J.J. Warne d/b/a MilkandGin. All rights reserved.")
}
