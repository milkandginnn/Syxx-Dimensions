import Foundation
import SyxxKit
import Sentry
import SwiftUI
import UserNotifications

/// The tools exposed in the GUI sidebar. Each maps to the CLI/library feature of the same name.
enum Tool: String, CaseIterable, Identifiable {
    case beta         = "What's New"
    case dashboard    = "Dashboard"
    case prophet      = "Prophet"
    case mirror       = "Mirror"
    case portal       = "Portal"
    case glutton      = "Glutton"
    case cage         = "Cage"
    case witness      = "Witness"
    case conclusions  = "Conclusions"
    case settings     = "Settings"
    case license      = "License"

    var id: String { rawValue }

    var systemImage: String {
        switch self {
        case .beta:         return "sparkles"
        case .dashboard:    return "chart.bar.fill"
        case .prophet:      return "magnifyingglass"
        case .mirror:       return "arrow.triangle.branch"
        case .portal:       return "network"
        case .glutton:      return "drop.triangle"
        case .cage:         return "point.3.connected.trianglepath.dotted"
        case .witness:      return "seal"
        case .conclusions:  return "doc.text.magnifyingglass"
        case .settings:     return "gearshape.fill"
        case .license:      return "key.fill"
        }
    }
}

/// Structured Prophet result shown in the ProphetView result card.
struct ProphetResult: Sendable {
    struct ResolverResponse: Identifiable, Sendable {
        let id = UUID()
        let resolver: String
        let ip: String
    }
    struct Consensus: Sendable {
        let resolversQueried: Int
        // B231: nil when the latency probe was skipped or timed out — rendering this as "0ms"
        // fabricated a perfect measurement. Consumers render "—" instead.
        let medianLatency: Int?
    }
    let resolverResponses: [ResolverResponse]
    let consensus: Consensus
}

/// The 11 public reference oracles Prophet queries alongside the local resolver (12 total).
/// Hoisted out of `runLookup` so the Prophet view can populate the roster before any scan
/// runs (owner directive 2026-08-02: "Prophet resolvers populate"). These are the REAL
/// resolvers the scan queries, not illustrative text.
enum ProphetOracleRoster {
    static let publicOracles: [(name: String, ip: String)] = [
        ("Cloudflare (1.1.1.1)", "1.1.1.1"),
        ("Google (8.8.8.8)", "8.8.8.8"),
        ("Quad9 (9.9.9.9)", "9.9.9.9"),
        ("OpenDNS (208.67.222.222)", "208.67.222.222"),
        ("AdGuard (94.140.14.14)", "94.140.14.14"),
        ("NextDNS (45.90.28.0)", "45.90.28.0"),
        ("Control D (76.76.2.0)", "76.76.2.0"),
        ("Comodo (8.26.56.26)", "8.26.56.26"),
        ("Level3 (4.2.2.1)", "4.2.2.1"),
        ("Verisign (64.6.64.6)", "64.6.64.6"),
        ("DNS.WATCH (84.200.69.80)", "84.200.69.80")
    ]

    /// The full 12-oracle roster for a given local resolver address (Local first).
    static func roster(localServer: String) -> [(name: String, ip: String)] {
        [("Local (\(localServer))", localServer)] + publicOracles
    }
}

struct CageRow: Identifiable {
    let id = UUID()
    let time: String
    let domain: String
    let note: String
    let process: String
    let resolver: String
    let severity: Severity
}

/// Honest state for the Portal traceroute surface. A trace that cannot run is `.failed` with a
/// plain reason; an empty or unsafe target is `.invalidTarget`. Never renders a clean path from
/// no data — hops shown are only ever the system tool's real output lines.
enum TraceState: Equatable {
    case idle
    case running
    case completed
    case failed(String)
    case invalidTarget
}

/// B255 test seam for Witness vantage comparison. Production always uses `.production`
/// (live `VantageComparer`). Tests inject a fixed result, a typed failure, or a probe-nil
/// outcome so the signing/error-classification path is deterministic without host network.
enum WitnessVantageSeam: Sendable {
    case production
    /// Successful comparison value, as if the network leg completed.
    case fixed(SyxxKit.ComparisonResult)
    /// Comparison throws; surfaces through the same failure path as a real thrown error.
    case failed(String)
    /// ProbeRunner returned no value (deadline / cancelled). Exact product copy at the nil branch.
    case noResult
}

/// D278: the outcome of the system-resolver startup probe. `.measured` carries the real
/// values; `.unavailable` means the probe could not measure a resolver (subprocess failed,
/// timed out, or reported none). Views never construct this — tests inject it through
/// `AppModel.systemResolverProbe`.
enum SystemResolverProbe: Sendable {
    case measured(dnsServers: [String], encryptedStatus: EncryptedDNSStatus)
    case unavailable
}

/// D278: honest startup state of the system-resolver measurement. `.checking` is the initial
/// unknown; no surface may render it as a measured resolver. `.measured` carries the exact
/// server list the probe reported; `.unavailable` means measurement failed or was empty.
enum ResolverState: Equatable {
    case checking
    case measured([String])
    case unavailable
}

// MARK: - AppModel

@MainActor
final class AppModel: ObservableObject {

    // MARK: - Navigation

    /// The MOTD is the main-window entry point for both returning users and the first ready
    /// frame after guided setup. Subsequent navigation remains user-controlled.
    @Published var selection: Tool = .beta

    // MARK: - Test seams

    /// B255: injectable vantage comparison. Defaults to live network. Tests only.
    var witnessVantageSeam: WitnessVantageSeam = .production

    /// D278: injectable system-resolver probe. Production runs the real Kit calls (which exec
    /// subprocesses) off the main actor via checkStartup; tests inject a fixed result. The real
    /// closure maps an empty server list to `.unavailable` — a failed probe is never a
    /// measurement. Never overridden in production.
    var systemResolverProbe: @Sendable () async -> SystemResolverProbe = {
        let servers = NetworkKeyState.getSystemDNSServers()
        let status = NetworkKeyState.encryptedDNSStatus()
        guard !servers.isEmpty else { return .unavailable }
        return .measured(dnsServers: servers, encryptedStatus: status)
    }

    // MARK: - Inputs

    @Published var domain: String = "github.com"
    /// D278: never an invented resolver IP at launch. `server` is only ever set from a
    /// measured value (applySystemResolverProbe / resetAll), and the runLookup gate stops
    /// Prophet from auditing a resolver that was never measured.
    @Published var server: String = ""
    @Published var recordType: String = "A"   // B096: pending record-type picker binding
    @Published var url: String = "https://github.com"
    // B250: `target` removed — it was dead @Published state (never bound, never read).
    // Traceroute uses `domain` directly via a local variable.
    @Published var hasWitnessKey: Bool = false   // backed by Keychain — use WitnessKeyManager to manage
    @Published var licenseKey: String = ""
    @Published var isPro: Bool = false

    // MARK: - License state (E-5: in-app activation, E-6: license state display)

    @Published var licenseState: LicenseState = .unknown
    @Published var licenseMessage: String = ""
    @Published var isValidatingLicense: Bool = false

    enum LicenseState: Equatable, Sendable {
        case unknown
        case lite
        case trial(hoursRemaining: Int)
        case pro
        case activationFailed(String)
    }

    // Serialized launch-time license validation. `validationTask` is the single in-flight task;
    // concurrent callers (checkStartup, ProbePlan scans, activateLicense recovery paths) await
    // the same task instead of spawning parallel Keychain prompts. `hasAttemptedValidation`
    // prevents re-entry after the first validation completes within the same process lifetime —
    // subsequent callers see the already-populated license state and return immediately.
    var validationTask: Task<Void, Never>?
    var hasAttemptedValidation = false

    // MARK: - Output / Running state

    @Published var output: String = "" {
        didSet { appendDashboardFeedback(output) }
    }

    /// Cage's OWN honest result line, kept separate from the shared `output` feed. `output` is a
    /// global activity channel that every module writes to (Prophet/Mirror/Portal/Glutton/Witness),
    /// so a module finishing while the Cage panel is on screen used to paint its result into Cage's
    /// panel. Cage now writes here instead. `didSet` still feeds the Dashboard activity log, so the
    /// capture's messages keep appearing in the scrollback exactly as before.
    @Published var cageOutput: String = "" {
        didSet { appendDashboardFeedback(cageOutput) }
    }

    /// The capture's own result line for the Cage panel. Cage-scoped by construction (`cageOutput`),
    /// never the shared `output` feed — so a Prophet/Mirror/Portal/Glutton/Witness result finishing
    /// while Cage is on screen can never reach the Cage surface. The Cage view binds to THIS, so the
    /// binding is testable at the model level.
    var cageResultMessage: String { cageOutput }

    /// What the Cage terminal feed should show, as one honest, testable decision.
    /// `.listening` appears ONLY while a capture is actually running — never before a capture has
    /// started, and never after one that finished or failed to start. A capture that never ran must
    /// never render as a capture that is running and simply has not seen traffic yet.
    enum CageTerminalFeed: Equatable { case rows, listening, none }
    var cageTerminalFeed: CageTerminalFeed {
        if !cageRows.isEmpty { return .rows }        // real captured queries, live or final
        if cageCapturing { return .listening }        // genuinely listening, only here
        return .none                                  // idle / finished-empty / failed-to-start
    }

    /// One line of the Dashboard activity terminal. Display-only: `text` is always a string a
    /// production code path actually assigned to `output` or `unboundStatusMessage` — the
    /// terminal never synthesizes its own lines (no fabricated "ready"/"ok").
    struct DashboardLogEntry: Identifiable, Equatable {
        let id = UUID()
        let time: Date
        let text: String
    }

    /// Timestamped scrollback of every real feedback string emitted on the shared `output`
    /// channel and the Unbound status channel, fed by `didSet` on those properties so messages
    /// emitted while the Dashboard is off-screen (e.g. mid `runAllWardens`) are still recorded.
    /// Empty assignments (resets, e.g. `resetAll()`) are not activity and are skipped. Capped
    /// to the newest 200 entries — a display bound, not a data source.
    @Published private(set) var dashboardLog: [DashboardLogEntry] = []

    func appendDashboardFeedback(_ text: String) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        dashboardLog.append(DashboardLogEntry(time: Date(), text: trimmed))
        if dashboardLog.count > 200 {
            dashboardLog.removeFirst(dashboardLog.count - 200)
        }
    }

    // D068 fix: per-tool state, not a single global flag. A global `isRunning` meant starting
    // ANY check made every panel's spinner condition (`isRunning && selection == .x`) evaluate
    // against the same flag, so switching panels mid-run showed a second, phantom spinner; and a
    // hung check with no timeout left every panel permanently disabled with no way to recover
    // short of quitting. `runningTool` names exactly which tool is running, or none.
    @Published var runningTool: Tool? = nil
    var isRunning: Bool { runningTool != nil }

    // Correlation ID: a fresh UUID v4 generated at the start of every user-initiated scan.
    // Flows through all spans and breadcrumbs so a single user action can be traced
    // end-to-end in Sentry and OTel. Nil when no scan is in progress.
    @Published var scanCorrelationId: String? = nil

    // MARK: - Synthesis & Diagnostics

    @Published var conclusion: Conclusion? = nil
    @Published var portalReport: NetworkStateReport? = nil
    @Published var sectionInputs = SectionInputs()   // real per-section results accumulate here as checks run

    // Per-warden result snapshots — populated after each tool run, checked by DashboardView
    @Published var prophetResult: ProphetResult? = nil
    /// Real DNSSEC posture for the Prophet duplicate result card (owner directive 2026-08-02).
    /// Nil = never measured this session (the card stays hidden); set only by an actual
    /// DO-enabled DNSKEY query in runLookup. Tri-state via DNSSECStatus, never a Bool.
    @Published var dnssecStatus: DNSSECStatus? = nil
    @Published var mirrorResult: SyxxKit.ComparisonResult? = nil
    @Published var leakResult: LeakTestResult? = nil

    /// Computed from the cached encrypted-DNS status populated once at startup.
    var isEncrypted: Bool { cachedEncryptedDNSStatus?.state == .yes }

    // MARK: - Monitoring (A4/A5)

    // A4 — five-minute first-run scan state.

    // A5 — continuous monitoring. The scheduler drives ContinuousMonitor.tickOnce on an interval;
    // these mirror its published state for the UI. Off by default: monitoring is a subscription
    // capability the user opts into, and a background loop should never start unasked.
    @Published var monitoringOn = false
    // B247: notification authorization state — monitoring can be "on" but unable to alert.
    // The UI must reflect this so the user knows alerting is impossible when denied.
    @Published var notificationAuthorized: Bool? = nil   // nil = undetermined, true = authorized, false = denied
    @Published var lastMonitorTick: MonitorTick?
    @Published var lastMonitorChange: MonitorTick?   // latched until acknowledged
    lazy var monitor: MonitorScheduler = MonitorScheduler(
        interval: 900,
        onTick: { [weak self] tick in
            self?.lastMonitorTick = tick
            if tick.state == .changed { self?.lastMonitorChange = tick }

            // A5/D-4: OS notification delivery when a drift is detected.
            // Respects the user's notificationsEnabled preference. Notification failure
            // must never crash the monitor loop — UNUserNotificationCenter.add is
            // non-throwing but we guard defensively.
            // C089: deliver a notification for every alert in the tick, not just the first —
            // collapsing N alerts into alerts.first meant a config-change alert could hide
            // a simultaneous answer-drift alert (or vice versa). Gate on !alerts.isEmpty so
            // a .changed tick with empty alerts (should not happen, but defensive) is a no-op.
            if tick.state == .changed, SyxxSettingsStore.load().notificationsEnabled,
               !tick.alerts.isEmpty {
                for alert in tick.alerts {
                    let content = UNMutableNotificationContent()
                    content.title = "Syxx \u{2014} Network Change Detected"
                    content.body = alert.headline
                    content.sound = .default
                    let request = UNNotificationRequest(
                        identifier: "syxx-monitor-\(UUID().uuidString)",
                        content: content, trigger: nil)
                    // B247: capture delivery errors — a failed add() means alerting is broken
                    // while monitoring appears armed. Surface in the dashboard log.
                    UNUserNotificationCenter.current().add(request) { [weak self] error in
                        if let error = error {
                            Task { @MainActor in
                                self?.appendDashboardFeedback("[monitor] notification delivery failed: \(error.localizedDescription)")
                            }
                        }
                    }
                }
            }
        }
    )

    @MainActor func setMonitoring(_ on: Bool) {
        monitoringOn = on
        if on { monitor.start() } else { monitor.stop() }
    }

    @MainActor func acknowledgeMonitorChange() {
        monitor.acknowledgeChange()
        lastMonitorChange = nil
    }

    // MARK: - Portal traceroute state

    // Portal traceroute state — a GUI wrapper around the system `traceroute`. Tri-state honest:
    // a trace that cannot run is `.failed`, an empty/unsafe target is `.invalidTarget`; hops are
    // only ever the tool's real output lines, never fabricated.
    @Published var traceState: TraceState = .idle
    @Published var traceHops: [String] = []

    // MARK: - Leak probe state (Glutton)

    @Published var leakTestSubdomain: String = ""
    @Published var leakProbeResults: [String: Bool] = [:]
    @Published var collectorURL: String = "https://syxx.app"
    @Published var collectorResult: LeakTestResult?
    @Published var collectorRunning: Bool = false

    // MARK: - Cached network state

    // Cached system DNS servers — populated once on appear to avoid calling
    // NetworkKeyState.getSystemDNSServers() synchronously in view bodies (F011/AttributeGraph cycles).
    @Published var cachedSystemDNSServers: [String] = []

    // Cached encrypted-DNS status — populated once on appear to avoid AttributeGraph cycles
    // and to provide the Prophet view with a tri-state result (C011 fix: .unknown ≠ .no).
    @Published var cachedEncryptedDNSStatus: EncryptedDNSStatus?

    // D278: honest state of the startup resolver probe. `.checking` is the initial unknown —
    // no view renders it as a measured resolver. Written only by applySystemResolverProbe.
    @Published var resolverState: ResolverState = .checking

    // MARK: - Local Unbound resolver

    // Clean integration point for the Dashboard button (IMPLEMENTATION_GUIDE §2.2a). Core logic lives in
    // SyxxKit (`UnboundManager` / `UnboundResolver`); this exposes the capability to the GUI without any
    // GUI layout work. A view binds a toggle to `setUnbound(_:)` and renders `unboundState`.
    //
    // NOTE (copy-gate): `unboundStatusMessage` values are PROPOSED wording pending owner approval.
    // TODO(dev-ID): the always-on auto-toggle + in-flow failsafe (DESIGN.md layer 4) is gated on the
    // NetworkExtension entitlement and intentionally not wired here — this is the manual analog.
    let unboundManager = UnboundManager()
    @Published var unboundEnabled: Bool = false
    @Published var unboundBusy: Bool = false
    @Published var unboundState: UnboundResolver.UnboundResult? = nil
    @Published var unboundStatusMessage: String = "" {
        didSet { appendDashboardFeedback(unboundStatusMessage) }
    }

    // MARK: - In-flight tasks

    // In-flight run task — cancelled on resetAll() so an orphaned completion handler
    // can't overwrite the reset state (F010 fix).
    var runTask: Task<Void, Never>?

    // In-flight Witness testify task — tracked so a rapid re-testify or witnessFree() can cancel
    // the prior one before it finishes and clobbers state (the completion handler guards on
    // Task.isCancelled before writing final state back). Same discipline as runTask/F010.
    var witnessTask: Task<Void, Never>?

    // MARK: - Cage state

    @Published var cageDuration: Int = 60
    @Published var cageCapturing: Bool = false
    @Published var cageEventsCount: Int = 0
    @Published var cageHidden: Bool = false
    @Published var cageRemain: Int = 0
    @Published var cageRows: [CageRow] = []
    /// C033: maximum rows retained in the GUI ring buffer. Older rows are evicted so a busy
    /// long capture cannot grow memory unboundedly or freeze SwiftUI rendering. The aggregate
    /// `cageEventsCount` tracks the true total (never bounded).
    static let cageMaxGUIRows = 500
    var captureTask: Task<Void, Never>?
    // B244: hideCage countdown task — tracked so a second call or resetAll() can cancel it.
    // Without tracking, two overlapping loops decrement cageRemain at 2× speed.
    var hideTask: Task<Void, Never>?
    // B245: startTraceroute task — tracked so a new trace or resetAll() cancels the prior one.
    // Without tracking, a stale trace's completion can overwrite reset state or a newer target.
    var traceTask: Task<Void, Never>?
    // B237: CaptureProcessBox removed — store() was never called (the XPC flow never hands back a
    // Process), so terminate() was always a no-op. Stop now cancels only the tail loop and states
    // honestly that the privileged capture runs to its configured duration.

    // MARK: - Witness state

    @Published var witnessSignature: String = ""
    /// 5(b): plaintext of a testimony pasted from another machine, bound to the external-verify
    /// TextField. Verified via `witnessVerifyExternal`; never signed or persisted.
    @Published var externalSnapshotJson: String = ""
    @Published var showTestimony: Bool = false
    @Published var showValidation: Bool = false
    @Published var witnessSnapshot: WitnessSnapshot? = nil
    @Published var witnessValid: Bool = false
    /// E021 / D069/B049: honest, non-contradictory, forensic-grade outcome of the last Verify
    /// Testimony action. The view renders exactly ONE result from this. Every failure reason is
    /// distinct so expired honest snapshots are never confused with forged ones.
    @Published var witnessResult: WitnessResult = .notVerified
    enum WitnessResult: Equatable {
        case notVerified
        /// M-01 (adopted from Linux RG-2 / the .NET G-02a fix): the Keychain could not supply a
        /// usable signing key. The payload says WHICH, because "there is no key" and "I could not
        /// ask" are different facts and only ONE of them justifies telling the user to type a new
        /// key in. Collapsing them is how macOS came to instruct a user with a locked Keychain into
        /// overwriting the very key every sealed testimony depends on.
        case noKey(WitnessKeyAbsence)
        case valid
        case expired
        case stale
        case futureDated
        case untrustedSigner
        case displayMismatch
        case invalidSignature
        case malformedInput
        case unsupportedFormat
    }

    /// M-01: why the Keychain could not supply a signing key. Mirrors
    /// `WitnessKeyManager.KeyPresence` (D084) into the view layer WITHOUT flattening it, which is
    /// exactly what `WitnessKeyManager.hasKey` does and what its own doc comment warns callers off.
    ///
    /// `.confirmedAbsent` is an answer: `errSecItemNotFound`. There is genuinely no key, so asking
    /// the user to supply one is correct and destroys nothing.
    /// `.undetermined` is MISSING DATA (spine #2): the Keychain was not readable at all — locked,
    /// auth failed, the prompt was cancelled. A key may well exist. Any copy that reads as "no key"
    /// here, or that invites the user to enter one, overwrites the identity they still have.
    enum WitnessKeyAbsence: Equatable {
        case confirmedAbsent
        case undetermined(OSStatus)
    }

    /// M-01: set when a signing-key write was refused because a key already exists here. The view
    /// raises the confirmation from this; **nothing has been written while it is true.**
    @Published var witnessKeyOverwritePending: Bool = false

    /// B195: honest lifecycle state of the last Witness Testimony (sealing) action, distinct from
    /// `witnessResult` (which is the VERIFY outcome). The testimony card renders from this so a
    /// probe that failed or aborted is never shown as the pending "Not yet signed" placeholder —
    /// a dead probe and a running probe must be visually distinguishable (Honesty Protocol §4).
    /// `.failed` carries the already-authored reason string that was previously dropped into
    /// `output` and never rendered by WitnessView.
    @Published var witnessTestimonyState: WitnessTestimonyState = .idle
    enum WitnessTestimonyState: Equatable {
        case idle
        case running
        case signed
        case failed(String)
    }

    // MARK: - Dimension (Workstream D) — startup / broadcast / update / whats-new

    @Published var startupResponse: StartupResponse?
    @Published var pendingBroadcast: Broadcast?
    @Published var updateAvailable: Bool = false
    @Published var updateLatest: String = ""
    @Published var updateURL: String = ""
    @Published var dimensionOptIn: Bool = !UserDefaults.standard.bool(forKey: "dimension_opt_out") {
        didSet {
            UserDefaults.standard.set(!dimensionOptIn, forKey: "dimension_opt_out")
            if !dimensionOptIn {
                startupResponse = nil
                pendingBroadcast = nil
                updateAvailable = false
                updateLatest = ""
                updateURL = ""
            } else {
                checkStartup()
            }
        }
    }

    let recordTypes = ["A", "AAAA", "CNAME", "MX", "TXT", "NS", "SOA", "PTR", "SRV", "CAA", "HTTPS", "DNSKEY"]

    // MARK: - Reset

    /// Resets all inputs and outputs to their initial state — the app looks as it did when first opened.
    func resetAll() {
        // Cancel any in-flight run task so its completion handler can't overwrite the reset (F010).
        runTask?.cancel()
        runTask = nil
        // Same for the Witness testify task — an orphaned completion must not resurrect witness state.
        witnessTask?.cancel()
        witnessTask = nil
        domain = "github.com"
        // B229/D278: re-sync with the measured system resolver on reset. Never invent an IP:
        // when nothing has been measured, keep the current value — a fabricated fallback like
        // 8.8.8.8 would be presented as a measured resolver.
        server = cachedSystemDNSServers.first(where: { !$0.isEmpty }) ?? server
        recordType = "A"
        url = "https://github.com"
        // Note: witness key is Keychain-backed and deliberately NOT cleared here.
        licenseKey = ""
        output = ""
        runningTool = nil
        conclusion = nil
        portalReport = nil
        traceState = .idle
        traceHops = []
        sectionInputs = SectionInputs()
        leakTestSubdomain = ""
        leakProbeResults = [:]
        collectorResult = nil
        cageOutput = ""
        cageDuration = 60
        cageCapturing = false
        cageEventsCount = 0
        cageHidden = false
        cageRemain = 0
        cageRows = []
        // B237: CaptureProcessBox removed — terminate() was always a no-op.
        captureTask?.cancel()
        captureTask = nil
        // B244: cancel the hideCage countdown so it doesn't survive a reset.
        hideTask?.cancel()
        hideTask = nil
        // B245: cancel any in-flight traceroute so its completion can't overwrite reset state.
        traceTask?.cancel()
        traceTask = nil
        witnessSignature = ""
        showTestimony = false
        showValidation = false
        witnessSnapshot = nil
        witnessValid = false
        witnessResult = .notVerified
        witnessTestimonyState = .idle
        prophetResult = nil
        mirrorResult = nil
        leakResult = nil
        // C218: clear the activity terminal so stale entries from a prior session do not
        // persist into a fresh one. A reset IS a new session boundary.
        dashboardLog.removeAll()
    }
}
