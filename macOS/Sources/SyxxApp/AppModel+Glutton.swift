import Foundation
import SyxxKit

// MARK: - Glutton (DNS leak detection) tool implementation

extension AppModel {

    func runLeak() async -> String {
        let servers = NetworkKeyState.getSystemDNSServers()
        guard !servers.isEmpty else {
            self.leakTestSubdomain = ""
            self.leakProbeResults = [:]
            self.sectionInputs.leak = LeakTestResult(result: .error, errorReason: "No system DNS resolvers detected.")
            self.leakResult = self.sectionInputs.leak
            return "🚨 ERROR: no system DNS resolvers detected — Syxx will not fall back to public resolvers for a leak check."
        }
        var budget = ProbeBudget.defaultGlutton

        if let url = URL(string: collectorURL.trimmingCharacters(in: .whitespaces)), url.scheme != nil {
            let licenseKey = LicenseManager.getStoredLicenseKey()
            let collectorRes = await LeakDetector.runCollectorTest(
                servers: servers,
                collectorBaseURL: url,
                licenseKey: licenseKey
            )
            self.collectorResult = collectorRes
            self.sectionInputs.leak = collectorRes
            self.leakResult = collectorRes
            // B246: never write a resolver IP into the subdomain slot — observed resolvers
            // are rendered from collectorResult.observedResolvers in GluttonView.
            self.leakTestSubdomain = ""
        } else {
            let subdomain = LeakDetector.generateTestSubdomain(baseDomain: "leak.syxx.app")
            let results = await ProbeRunner.run(label: "triggerQueries", budget: &budget) {
                await LeakDetector.triggerQueries(subdomain: subdomain, servers: servers)
            } ?? [:]
            self.leakTestSubdomain = subdomain
            self.leakProbeResults = results
            self.sectionInputs.leak = LeakTestResult(result: .inconclusive, errorReason: "Local check only; use collector for confirmed result.")
            self.leakResult = self.sectionInputs.leak
        }

        self.sectionInputs.ecs = await ProbeRunner.run(label: "ecs", budget: &budget) {
            await ECSProbe.probe(server: servers.first ?? "1.1.1.1")
        }
        let res = self.leakResult
        var lines = ["Conclusion: \(res?.result.rawValue.uppercased() ?? "INCONCLUSIVE")", "Observed Resolvers: \(res?.observedResolvers.isEmpty ?? true ? "none" : (res?.observedResolvers.joined(separator: ", ") ?? ""))"]
        // The core may build `errorReason` from a Swift error's raw description; show it only when it
        // is genuine human-readable text, never a crash-reporter fragment ("… CollectorError error 0.").
        if let r = res?.errorReason, let clean = ErrorPresenter.displayReason(r) { lines.append("Note: \(clean)") }
        return lines.joined(separator: "\n")
    }

    func runCollectorVerification() async {
        guard !collectorRunning else { return }
        collectorRunning = true
        collectorResult = nil
        let servers = NetworkKeyState.getSystemDNSServers()
        guard !servers.isEmpty else {
            collectorResult = LeakTestResult(result: .error, errorReason: "No system DNS resolvers detected.")
            collectorRunning = false
            return
        }
        guard let url = URL(string: collectorURL.trimmingCharacters(in: .whitespaces)),
              url.scheme != nil else {
            collectorResult = LeakTestResult(result: .error, errorReason: "Invalid collector URL.")
            collectorRunning = false
            return
        }
        let licenseKey = LicenseManager.getStoredLicenseKey()
        let result = await LeakDetector.runCollectorTest(
            servers: servers,
            collectorBaseURL: url,
            licenseKey: licenseKey
        )
        self.collectorResult = result
        self.sectionInputs.leak = result
        self.collectorRunning = false
    }
}
