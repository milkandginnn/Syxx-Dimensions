#if DEBUG
import Foundation
import SwiftUI
import SyxxKit

@MainActor
enum SyxxPreviewSupport {
    static func appModel(selection: Tool = .dashboard, pro: Bool = false) -> AppModel {
        let model = AppModel()
        model.selection = selection
        model.domain = "example.com"
        model.server = "1.1.1.1"
        model.recordType = "A"
        model.url = "https://example.com"
        model.isPro = pro
        model.licenseState = pro ? .pro : .lite
        model.licenseMessage = pro ? "Pro subscription active" : "Lite version"
        // D278: previews must present a measured resolver, never a checking/unknown state, and
        // keep the single-writer discipline: resolver state and cache always agree.
        model.resolverState = .measured(["192.168.1.1", "1.1.1.1"])
        model.cachedSystemDNSServers = ["192.168.1.1", "1.1.1.1"]
        model.collectorURL = "https://syxx.app"
        model.leakTestSubdomain = "id-preview.leak.syxx.app"
        model.leakProbeResults = ["1.1.1.1": true, "8.8.8.8": false]
        model.collectorResult = LeakTestResult(
            result: .inconclusive,
            observedResolvers: [],
            errorReason: "Preview data only. Run the collector for measured evidence."
        )
        model.traceState = .completed
        model.traceHops = [
            "1  192.168.1.1  2.1 ms",
            "2  10.20.0.1  8.7 ms",
            "3  203.0.113.10  18.4 ms"
        ]
        model.cageRows = [
            CageRow(time: "10:31:02", domain: "example.com", note: "A query", process: "mDNSResponder", resolver: "192.168.1.1", severity: .unknown),
            CageRow(time: "10:31:07", domain: "cdn.example.com", note: "HTTPS query", process: "Safari", resolver: "1.1.1.1", severity: .info)
        ]
        model.cageEventsCount = model.cageRows.count
        model.appendDashboardFeedback("Preview model initialized.")
        return model
    }

    static func firstRunCoordinator() -> FirstRunCoordinator {
        FirstRunCoordinator(
            document: AgreementDocument(version: "preview", sha256: String(repeating: "0", count: 64)),
            agreementText: sampleAgreement,
            settingsURL: FileManager.default.temporaryDirectory.appendingPathComponent("syxx-preview-settings.json")
        )
    }

    static var sampleBroadcast: Broadcast {
        Broadcast(
            id: "preview-broadcast",
            title: "Dimension Broadcast",
            body: "This is preview-only Dimension content for checking panel layout.",
            severity: "info",
            url: "https://syxx.app",
            minVersion: nil,
            maxVersion: nil
        )
    }

    static var sampleConclusion: Conclusion {
        Conclusion(
            generatedAt: Date(timeIntervalSinceReferenceDate: 805_000_000),
            sectionStatuses: [
                .init(section: .prophet, status: .negative),
                .init(section: .mirror, status: .untested, completeness: 0.0),
                .init(section: .portal, status: .degraded, completeness: 0.6),
                .init(section: .glutton, status: .inconclusive, completeness: 0.5),
                .init(section: .cage, status: .negative),
                .init(section: .witness, status: .untested, completeness: 0.0)
            ],
            evidence: [
                EvidenceItem(
                    section: .prophet,
                    evidenceId: .resolverStatus,
                    observation: "Two preview resolvers returned the same address.",
                    rationale: "This preview exercises the evidence layout only."
                ),
                EvidenceItem(
                    section: .portal,
                    evidenceId: .configChange,
                    observation: "The route sample is partial.",
                    rationale: "Incomplete preview data remains visibly incomplete."
                )
            ],
            advisories: [
                Advisory(
                    section: .portal,
                    advisoryId: .openResolverExposure,
                    title: "Preview advisory",
                    detail: "A measured run is required before this can become real evidence.",
                    severity: .warning
                )
            ],
            determination: DeterminativeResult(
                state: .inconclusive,
                diagnoses: [],
                suppressed: [
                    SuppressedDiagnosis(
                        id: DiagnosisID("preview_unverified_signal"),
                        displayName: "Unverified resolver shift",
                        reason: .belowTier,
                        rawStrength: 0.35,
                        rawSupport: 0.35,
                        detail: "The preview contains a signal, but not enough corroboration to name a finding."
                    )
                ],
                explanation: "Preview evidence is incomplete, so the result remains inconclusive."
            )
        )
    }

    private static let sampleAgreement = """
    Syxx Preview License Agreement

    This preview text exists only to render the first-run canvas. The bundled agreement remains the production source of truth.
    """
}

extension View {
    func syxxPreviewCanvas() -> some View {
        self
            .frame(width: 1050, height: 613)
            .background(Color.ioBackgroundDark)
            .preferredColorScheme(.dark)
    }

    func syxxPreviewPanel() -> some View {
        self
            .background(Color.ioSurfaceDark)
            .preferredColorScheme(.dark)
    }
}

extension ProcessInfo {
    var isSyxxXcodePreview: Bool {
        environment["XCODE_RUNNING_FOR_PREVIEWS"] == "1"
    }
}
#endif
