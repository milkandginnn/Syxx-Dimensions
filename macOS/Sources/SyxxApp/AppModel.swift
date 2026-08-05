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

@MainActor
final class AppModel: ObservableObject {
    /// The MOTD is the main-window entry point for both returning users and the first ready
    /// frame after guided setup. Subsequent navigation remains user-controlled.
    @Published var selection: Tool = .beta

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

    // Inputs
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

    // License state (E-5: in-app activation, E-6: license state display)
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

    // Output / Running state
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
    
    // Synthesis & Diagnostics
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

    // Portal traceroute state — a GUI wrapper around the system `traceroute`. Tri-state honest:
    // a trace that cannot run is `.failed`, an empty/unsafe target is `.invalidTarget`; hops are
    // only ever the tool's real output lines, never fabricated.
    @Published var traceState: TraceState = .idle
    @Published var traceHops: [String] = []

    // Leak probe state (populated by runLeak)
    @Published var leakTestSubdomain: String = ""
    @Published var leakProbeResults: [String: Bool] = [:]
    @Published var collectorURL: String = "https://syxx.app"
    @Published var collectorResult: LeakTestResult?
    @Published var collectorRunning: Bool = false
    
    // Cached system DNS servers — populated once on appear to avoid calling
    // NetworkKeyState.getSystemDNSServers() synchronously in view bodies (F011/AttributeGraph cycles).
    @Published var cachedSystemDNSServers: [String] = []

    // Cached encrypted-DNS status — populated once on appear to avoid AttributeGraph cycles
    // and to provide the Prophet view with a tri-state result (C011 fix: .unknown ≠ .no).
    @Published var cachedEncryptedDNSStatus: EncryptedDNSStatus?

    // D278: honest state of the startup resolver probe. `.checking` is the initial unknown —
    // no view renders it as a measured resolver. Written only by applySystemResolverProbe.
    @Published var resolverState: ResolverState = .checking

    // MARK: - Local Unbound resolver (Dashboard "Enable Unbound DNS")
    //
    // Clean integration point for the Dashboard button (IMPLEMENTATION_GUIDE §2.2a). Core logic lives in
    // SyxxKit (`UnboundManager` / `UnboundResolver`); this exposes the capability to the GUI without any
    // GUI layout work. A view binds a toggle to `setUnbound(_:)` and renders `unboundState`.
    //
    // NOTE (copy-gate): `unboundStatusMessage` values are PROPOSED wording pending owner approval.
    // TODO(dev-ID): the always-on auto-toggle + in-flow failsafe (DESIGN.md layer 4) is gated on the
    // NetworkExtension entitlement and intentionally not wired here — this is the manual analog.
    private let unboundManager = UnboundManager()
    @Published var unboundEnabled: Bool = false
    @Published var unboundBusy: Bool = false
    @Published var unboundState: UnboundResolver.UnboundResult? = nil
    @Published var unboundStatusMessage: String = "" {
        didSet { appendDashboardFeedback(unboundStatusMessage) }
    }

    /// Enable/disable the local validating resolver as the system DNS. Honest tri-state; never flips
    /// `unboundEnabled` true unless the manager actually reports `.enabled`.
    @MainActor func setUnbound(_ on: Bool) {
        guard !unboundBusy else { return }
        unboundBusy = true
        Task {
            let result: UnboundResolver.UnboundResult
            if on {
                result = await unboundManager.enable()
            } else {
                result = unboundManager.disable()
            }
            await MainActor.run {
                self.unboundState = result
                switch result {
                case .enabled:
                    self.unboundEnabled = true
                    self.unboundStatusMessage = "Local Unbound resolver active."
                case .disabled:
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Local Unbound resolver disabled; original DNS restored."
                case .notInstalled(let hint):
                    self.unboundEnabled = false
                    self.unboundStatusMessage = hint
                case .elevationRequired(let step):
                    // D088: the GUI cannot grant elevation in-process (the XPC helper is not
                    // yet wired into UnboundManager). State the truth: point the user at the
                    // CLI workaround rather than presenting a toggle that can only ever fail.
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Administrator rights required: \(step). To enable from Terminal, run: sudo syxx unbound enable"
                case .failed(let reason):
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Could not enable: \(reason)"
                case .inconclusive(let reason):
                    self.unboundEnabled = false
                    self.unboundStatusMessage = "Unconfirmed: \(reason)"
                }
                self.unboundBusy = false
            }
        }
    }

    // In-flight run task — cancelled on resetAll() so an orphaned completion handler
    // can't overwrite the reset state (F010 fix).
    private var runTask: Task<Void, Never>?

    // In-flight Witness testify task — tracked so a rapid re-testify or witnessFree() can cancel
    // the prior one before it finishes and clobbers state (the completion handler guards on
    // Task.isCancelled before writing final state back). Same discipline as runTask/F010.
    private var witnessTask: Task<Void, Never>?

    // Cage states
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
    private var captureTask: Task<Void, Never>?
    // B244: hideCage countdown task — tracked so a second call or resetAll() can cancel it.
    // Without tracking, two overlapping loops decrement cageRemain at 2× speed.
    private var hideTask: Task<Void, Never>?
    // B245: startTraceroute task — tracked so a new trace or resetAll() cancels the prior one.
    // Without tracking, a stale trace's completion can overwrite reset state or a newer target.
    private var traceTask: Task<Void, Never>?
    // B237: CaptureProcessBox removed — store() was never called (the XPC flow never hands back a
    // Process), so terminate() was always a no-op. Stop now cancels only the tail loop and states
    // honestly that the privileged capture runs to its configured duration.

    // Witness states
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
    /// here, or that invites the user to enter one, points them straight at an irreversible
    /// overwrite of the identity every previously sealed testimony verifies against.
    enum WitnessKeyAbsence: Equatable {
        case confirmedAbsent
        case undetermined(OSStatus)
    }

    /// M-01: the gate every signing-key WRITE passes through. Reads the Keychain tri-state
    /// (`checkKeyPresence`) rather than the collapsed `hasKey`, because a write is irreversible:
    /// overwriting the key silently invalidates every testimony ever sealed with the old one.
    ///
    /// Linux (`WitnessViewModel.LoadOrCreatePersistentSigningKey`, RG-2) refuses outright on
    /// `Undetermined` — "the identity exists and is simply out of reach." macOS adopts that, and
    /// additionally requires explicit confirmation for the `.present` case, which had no check at
    /// all: `saveWitnessKey` called `saveKey` straight through.
    enum WitnessKeyWriteGate: Equatable {
        /// No key on this machine (`errSecItemNotFound`) — a first write destroys nothing.
        case proceed
        /// A key is present. The write would replace it; the user must say so explicitly.
        case needsOverwriteConfirmation
        /// The Keychain could not be read. Refuse: a key may exist and would be destroyed.
        case refusedStoreUnreachable(OSStatus)
    }

    /// M-01 copy. Never says "no key" — the whole point is that we do not know.
    static let witnessKeyStoreUnreachableMessage =
        "Your signing key could not be checked because the Keychain could not be read (it may be "
        + "locked, or the request was cancelled). This does not mean you have no key. Nothing was "
        + "saved and your existing identity is untouched — unlock the Keychain and try again."

    /// M-01: consult the Keychain tri-state before any write to the signing key.
    func witnessKeyWriteGate() -> WitnessKeyWriteGate {
        switch WitnessKeyManager.checkKeyPresence() {
        case .absent: return .proceed
        case .present: return .needsOverwriteConfirmation
        case .undetermined(let status): return .refusedStoreUnreachable(status)
        }
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

    // Dimension (Workstream D) — startup / broadcast / update / whats-new
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
    }

    /// Runs all six wardens in sequence. Each tool is run with its normal timeout.
    /// Results populate the per-warden published properties so the Dashboard can reflect state.
    /// B236: (1) Track which disciplines actually ran and emit an honest completion message.
    /// (2) Route each scan through withTimeout to prevent a hung probe from leaving runningTool
    /// set forever (same guard run() uses, per D068). (3) Clear runningTool in defer so all
    /// exit paths — cancel, timeout, error — leave the UI recoverable.
    func runAllWardens() {
        guard runningTool == nil else { return }
        let corrId = UUID().uuidString
        scanCorrelationId = corrId
        runningTool = .dashboard
        output = "Conducting all scans…"
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state"); b.message = "All wardens scan started"; return b }())
        runTask = Task {
            var ran: [String] = []
            var skipped: [String] = []
            defer {
                // B236: emit an honest completion message listing what actually ran.
                let proSkipped = skipped.isEmpty ? "" : " Skipped: \(skipped.joined(separator: ", "))."
                self.selection = .dashboard
                self.output = "Completed: \(ran.joined(separator: ", ")).\(proSkipped)"
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state"); b.message = "Wardens scan complete: \(ran.joined(separator: ","))"; return b }())
                self.runningTool = nil
                self.scanCorrelationId = nil
            }
            // Prophet
            selection = .prophet
            output = "Running Prophet…"
            let prophetOutcome = await Self.withTimeout(seconds: Self.timeout(for: .prophet)) { [weak self] in
                await self?.runLookup() ?? ""
            }
            guard !Task.isCancelled else { return }
            ran.append("Prophet")
            if prophetOutcome == nil { self.output = "Prophet timed out." }
            // Mirror (Pro only)
            if isPro {
                selection = .mirror
                output = "Running Mirror…"
                let mirrorOutcome = await Self.withTimeout(seconds: Self.timeout(for: .mirror)) { [weak self] in
                    await self?.runCompare() ?? ""
                }
                guard !Task.isCancelled else { return }
                ran.append("Mirror")
                if mirrorOutcome == nil { self.output = "Mirror timed out." }
            } else {
                skipped.append("Mirror")
            }
            // Portal
            selection = .portal
            output = "Running Portal…"
            let portalOutcome = await Self.withTimeout(seconds: Self.timeout(for: .portal)) { [weak self] in
                await self?.runNetworkState() ?? ""
            }
            guard !Task.isCancelled else { return }
            ran.append("Portal")
            if portalOutcome == nil { self.output = "Portal timed out." }
            // Glutton (Pro only)
            if isPro {
                selection = .glutton
                output = "Running Glutton…"
                let gluttonOutcome = await Self.withTimeout(seconds: Self.timeout(for: .glutton)) { [weak self] in
                    await self?.runLeak() ?? ""
                }
                guard !Task.isCancelled else { return }
                ran.append("Glutton")
                if gluttonOutcome == nil { self.output = "Glutton timed out." }
            } else {
                skipped.append("Glutton")
            }
            // Witness (Pro only)
            if isPro {
                selection = .witness
                output = "Running Witness…"
                let witnessOutcome = await Self.withTimeout(seconds: Self.timeout(for: .witness)) { [weak self] in
                    await self?.runWitness() ?? ""
                }
                guard !Task.isCancelled else { return }
                ran.append("Witness")
                if witnessOutcome == nil { self.output = "Witness timed out." }
            } else {
                skipped.append("Witness")
            }
            // Cage is intentionally not included — it requires a separate privileged capture flow.
            skipped.append("Cage")
        }
    }

    /// Flushes the system DNS cache (macOS: `dscacheutil -flushcache`).
    /// B238: (a) Read terminationStatus — report failure on non-zero. (b) Message reflects what
    /// was actually done: dscacheutil flushes the directory-service cache but not the
    /// mDNSResponder cache (which additionally requires `sudo killall -HUP mDNSResponder`).
    /// (c) Run off the main actor via Task.detached so the UI thread is not blocked.
    func flushDNSCache() {
        output = "Flushing DNS cache…"
        Task.detached { [weak self] in
            let proc = Process()
            proc.executableURL = URL(fileURLWithPath: "/usr/bin/dscacheutil")
            proc.arguments = ["-flushcache"]
            let pipe = Pipe()
            proc.standardOutput = pipe
            proc.standardError = pipe
            let message: String
            do {
                try proc.run()
                proc.waitUntilExit()
                if proc.terminationStatus == 0 {
                    message = "Directory-service DNS cache flushed. The system resolver cache (mDNSResponder) may still hold entries."
                } else {
                    let stderr = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?
                        .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                    message = "Flush command failed (exit \(proc.terminationStatus)).\(stderr.isEmpty ? "" : " \(stderr)")"
                }
            } catch {
                let detail = ErrorPresenter.userDetail(for: error, component: "dns.flush")
                message = detail.map { "Could not flush DNS cache: \($0)" } ?? "Could not flush DNS cache."
            }
            await MainActor.run { self?.output = message }
        }
    }

    func run() {
        guard runningTool == nil else { return }
        let tool = selection
        let corrId = UUID().uuidString
        scanCorrelationId = corrId
        runningTool = tool
        output = "Running \(tool.rawValue)…"
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state"); b.message = "\(tool.rawValue) scan started"; return b }())
        // D068 fix: previously nothing was ever assigned to `runTask`, so resetAll()'s
        // `runTask?.cancel()` was silently a no-op. And with no timeout, a check whose
        // underlying network call hangs (no throw, just never returns) left `runningTool`
        // set forever — every panel showing that tool's spinner stayed stuck with no recovery
        // short of quitting the app. Race `execute(tool)` against a per-tool timeout instead.
        runTask = Task {
            // B235: reject non-runnable tools BEFORE the timeout race — a sidebar selection
            // for Cage/Conclusions/Dashboard/Settings/License/Beta must never fabricate a
            // timeout message with a fabricated duration.
            guard Self.isRunnable(tool) else {
                self.output = "\(tool.rawValue) is not run from this button."
                self.runningTool = nil
                self.scanCorrelationId = nil
                return
            }
            let outcome = await Self.withTimeout(seconds: Self.timeout(for: tool)) { [weak self] in
                await self?.execute(tool) ?? .notRunnable
            }
            guard !Task.isCancelled else { return }
            switch outcome {
            case .text(let t) where !t.isEmpty:
                self.output = t
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.scan"); b.message = "\(tool.rawValue) scan complete"; b.data = ["tool": tool.rawValue]; return b }())
            case .text:
                // Real run produced no output — honest empty result, not a timeout.
                self.output = "\(tool.rawValue) finished with no output."
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.scan"); b.message = "\(tool.rawValue) scan empty"; b.data = ["tool": tool.rawValue]; return b }())
            case .notRunnable:
                // Defensive: `isRunnable` above should have prevented this, but if `execute`
                // is extended and forgets to add a case, the honest line is "not runnable",
                // not a fabricated timeout with a fabricated duration.
                self.output = "\(tool.rawValue) is not run from this button."
            case nil:
                // Genuine timeout — the race was lost.
                self.output = "\(tool.rawValue) timed out — no response after \(Int(Self.timeout(for: tool)))s. Try again."
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .warning, category: "gui.scan"); b.message = "\(tool.rawValue) scan timed out after \(Int(Self.timeout(for: tool)))s"; return b }())
            }
            self.runningTool = nil
            self.scanCorrelationId = nil
        }
    }

    /// Per-tool ceiling matched to what each check actually does — Portal traces a route and
    /// Glutton runs a full leak collector round-trip, both slower than a single Prophet lookup.
    private static func timeout(for tool: Tool) -> Double {
        switch tool {
        case .portal:  return 90
        case .glutton: return 120
        default:       return 60
        }
    }

    /// Races `operation` against a timeout; returns nil on timeout rather than leaving the
    /// caller awaiting forever. The loser of the race is cancelled, not just abandoned.
    private static func withTimeout<T: Sendable>(seconds: Double, operation: @escaping @Sendable () async -> T) async -> T? {
        await withTaskGroup(of: T?.self) { group in
            group.addTask { await operation() }
            group.addTask {
                try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                return nil
            }
            let result = await group.next() ?? nil
            group.cancelAll()
            return result
        }
    }

    // B235: typed outcome for `execute()`. The old `String` return conflated three distinct
    // states — real text, "tool has no runnable implementation", and "execution produced no
    // output" — and the empty-string branch in `run()` fabricated a timeout + a duration.
    // `.notRunnable` is now distinct from `.text("")`, so the "timed out" message only fires
    // on a real timeout.
    private enum ExecOutcome: Sendable {
        case text(String)
        case notRunnable
    }

    /// Which tools can `run()` dispatch? Kept as a single source of truth so the sidebar can
    /// never steer a non-runnable tool into the timeout branch.
    private static func isRunnable(_ tool: Tool) -> Bool {
        switch tool {
        case .prophet, .mirror, .portal, .glutton, .witness:
            return true
        default:
            return false
        }
    }

    private func execute(_ tool: Tool) async -> ExecOutcome {
        switch tool {
        case .prophet:      return .text(await runLookup())
        case .mirror:       return .text(await runCompare())
        case .portal:       return .text(await runNetworkState())
        case .glutton:      return .text(await runLeak())
        case .witness:      return .text(await runWitness())
        // B235: every unmapped case is .notRunnable — never "" (which the old run() branch
        // read as "timed out — no response after 60s"). Cage, Conclusions, Dashboard,
        // Settings, License, Beta all have their own dedicated UI surfaces and are not
        // dispatched by the single-shot run() button.
        default:
            return .notRunnable
        }
    }

    // MARK: - License state (E-5/E-6)

    private func updateLicenseState(_ state: LicenseState, message: String) {
        let oldState = licenseState
        licenseState = state
        licenseMessage = message
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state")
            b.message = "License state changed: \(oldState) → \(state)"
            b.data = ["old_state": "\(oldState)", "new_state": "\(state)", "message": message]
            return b }())
        switch state {
        case .pro, .trial:
            isPro = true
        case .unknown, .lite, .activationFailed:
            isPro = false
        }
    }

    /// Validates the stored license key / trial state on launch. Called from checkStartup().
    func validateLicenseOnLaunch() async {
        let result = await LicenseManager.isFeatureUnlocked()
        await MainActor.run {
            if result.unlocked {
                if result.message.contains("Pro Subscription") {
                    updateLicenseState(.pro, message: result.message)
                } else if result.message.contains("Free Trial"),
                          let hours = extractTrialHours(from: result.message) {
                    updateLicenseState(.trial(hoursRemaining: hours), message: result.message)
                } else {
                    updateLicenseState(.pro, message: result.message)
                }
            } else {
                updateLicenseState(.lite, message: result.message)
            }
        }
    }

    private func extractTrialHours(from message: String) -> Int? {
        let pattern = /(\d+)\s*hours?\s*remaining/
        guard let match = message.firstMatch(of: pattern) else { return nil }
        return Int(match.1)
    }

    /// Called when the user clicks "Activate" in LicenseView. Saves the key to the Keychain,
    /// validates it against Keygen, and updates the license state.
    func activateLicense() async {
        let key = licenseKey.trimmingCharacters(in: .whitespaces)
        guard !key.isEmpty else {
            updateLicenseState(.activationFailed("Please enter a license key."), message: "")
            return
        }

        isValidatingLicense = true
        updateLicenseState(.unknown, message: "Validating license key with Keygen…")

        // E048: validate BEFORE persisting. Previously the key was saved to the Keychain
        // before calling Keygen, so an invalid/timeout/network-error result would overwrite a
        // known-good stored credential with a bad key, disabling collector-backed evidence.
        // Now the Keychain is touched only when the server confirms the key is valid.
        let result = await LicenseManager.validateLicenseDetailed(key: key,
                                                                   accountID: SyxxConfig.accountID)

        await MainActor.run {
            self.isValidatingLicense = false
            switch result {
            case .valid:
                // Only persist after confirmed valid — the previous key (if any) is replaced
                // atomically by a verified successor.
                do {
                    try LicenseManager.saveLicenseKey(key)
                } catch {
                    self.updateLicenseState(.activationFailed("License is valid but could not be saved to the system keychain."),
                                           message: "")
                    return
                }
                self.updateLicenseState(.pro, message: "Pro subscription active.")
                self.licenseKey = ""
            case .invalid:
                // Do NOT overwrite the stored key — the entered key is invalid.
                self.updateLicenseState(.activationFailed("The license key is not valid."),
                                        message: "")
            case .timeout:
                // Do NOT overwrite the stored key — network may be blocked.
                // Re-check full state (trial, stored key, etc.) to restore any prior entitlement.
                self.updateLicenseState(.activationFailed("License check timed out. Check your internet connection."),
                                        message: "")
                Task { await self.validateLicenseOnLaunch() }
            case .networkError(let msg):
                // Do NOT overwrite the stored key — server unreachable.
                self.updateLicenseState(.activationFailed("Could not reach the license server: \(msg)"),
                                        message: "")
                Task { await self.validateLicenseOnLaunch() }
            }
        }
    }

    // MARK: - Dimension Startup (Workstream D)

    /// One phone-home per launch: fetch broadcast + update + whats-new pointer.
    /// Caches the response for 24h; broadcasts auto-show once per id.
    func checkStartup() {
        let settings = SyxxSettingsStore.load()
        // D278: the system-resolver probes exec subprocesses and must never run synchronously
        // on the main actor (first paint froze waiting on scutil). Copy the @Sendable seam out
        // first, then run it on a detached task and publish through the single writer. A bare
        // Task would inherit the main actor and still block.
        let probe = systemResolverProbe
        Task.detached { [weak self] in
            let result = await probe()
            await MainActor.run {
                self?.applySystemResolverProbe(result)
            }
        }
        Task {
            // Validate license state from Keychain / trial
            await self.validateLicenseOnLaunch()

            // B185: sync the collector's trusted signing key on launch so the trust set is
            // populated from the pinned /api/pubkey endpoint. A failed sync is non-fatal —
            // the persisted key (or manual config) is used as fallback.
            if let collectorBase = URL(string: self.collectorURL.trimmingCharacters(in: .whitespaces)),
               collectorBase.scheme != nil {
                _ = await CollectorPinning.syncTrustedSigningKey(collectorBaseURL: collectorBase)
            }

            guard settings.updateCheckEnabled else { return }
            let resp = await DimensionService.shared.fetchStartup()
            self.startupResponse = resp
            if let u = resp?.update {
                self.updateAvailable = u.available
                self.updateLatest = u.latest
                self.updateURL = u.url ?? "https://syxx.app/download"
            }
            if let b = resp?.broadcast,
               !DimensionService.shared.hasDismissed(b.id),
               DimensionService.shared.isVersionInRange(SyxxConfig.version,
                                                         minVersion: b.minVersion,
                                                         maxVersion: b.maxVersion) {
                self.pendingBroadcast = b
            }
        }
    }

    /// D278: the ONLY writer of `cachedSystemDNSServers`, `cachedEncryptedDNSStatus`,
    /// `resolverState`, and the B229 server default. A probe that measured nothing is honest
    /// `.unavailable` — empty/nil is never treated as a measured resolver, and `server` is
    /// only ever set from a measured value.
    private func applySystemResolverProbe(_ probe: SystemResolverProbe) {
        switch probe {
        case .measured(let dnsServers, let encryptedStatus) where !dnsServers.isEmpty:
            resolverState = .measured(dnsServers)
            cachedSystemDNSServers = dnsServers
            cachedEncryptedDNSStatus = encryptedStatus
            // B229: Prophet's default resolver-under-test must be the user's real system
            // resolver, not a hard-coded public IP (moved here from checkStartup, D278).
            server = dnsServers.first(where: { !$0.isEmpty }) ?? server
        case .measured, .unavailable:
            resolverState = .unavailable
            cachedSystemDNSServers = []
            cachedEncryptedDNSStatus = nil
            // `server` untouched: an empty or failed measurement is never a resolver.
        }
    }

    /// Call after user dismisses a broadcast panel — never re-show.
    func dismissBroadcast(_ id: String) {
        DimensionService.shared.markDismissed(id)
        pendingBroadcast = nil
    }

    // MARK: - Tool implementations (adapted for structured UI)

    /// Run a Prophet lookup against `server`. Internal (not private) so the D278 gate is
    /// driveable hermetically by tests: an unmeasured resolver must never be audited.
    func runLookup() async -> String {
        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return "Enter a domain to look up." }
        // A new run means the previous DNSSEC posture is stale: hide the card until the
        // fresh query at the end of this run lands. Never show last scan's measurement as this scan's.
        dnssecStatus = nil
        // D278: never audit a resolver that was never measured. While the startup probe is
        // still in flight, or after it failed, Prophet must not present an unknown resolver
        // as audited (an empty `server` would otherwise reach the `via` output line).
        if resolverState == .checking {
            return "System resolver not yet measured. Prophet cannot audit an unknown resolver. Retry in a moment."
        }
        if resolverState == .unavailable {
            return "System resolver could not be determined. Check your network connection, then retry."
        }
        let typeMap: [String: UInt16] = ["A": 1, "NS": 2, "CNAME": 5, "SOA": 6, "PTR": 12, "MX": 15,
                                         "TXT": 16, "AAAA": 28, "SRV": 33, "CAA": 257, "HTTPS": 65, "DNSKEY": 48]
        let type = typeMap[recordType] ?? 1
        let srv = server  // local capture for @Sendable ProbeRunner closures

        // Display query against the user-selected server
        var displayBudget = ProbeBudget.defaultProphet
        let resp = await ProbeRunner.run(label: "query", budget: &displayBudget) {
            await DNSResolver.queryUDPDetailed(DNSQuery(name: name, type: type, server: srv))
        } ?? DNSResponse(status: .timeout, errorMessage: "Probe timed out or failed")

        // C049: use the user-selected resolver as the single resolver-under-test.
        // The old code defaulted to the system resolver, discarding the user's choice.
        let auditServer = server
        let planResult = await ProbePlan.runProphet(domain: name, resolver: auditServer)
        sectionInputs.resolution = planResult.resolution
        sectionInputs.dnssec = planResult.dnssec
        sectionInputs.nxCanary = planResult.nxCanary
        sectionInputs.dnssecCanary = planResult.dnssecCanary
        sectionInputs.latency = planResult.latency
        sectionInputs.stability = planResult.stability
        sectionInputs.wildcardSynthesis = planResult.wildcardSynthesis
        sectionInputs.denial = planResult.denial
        sectionInputs.dualStack = planResult.dualStack
        sectionInputs.edns0 = planResult.edns0
        sectionInputs.versionBind = planResult.versionBind

        var lines = ["\(recordType) records for \(name) via \(server)", "Status: \(resp.status.rawValue)"]
        if resp.records.isEmpty {
            lines.append("(no records)")
        } else {
            for r in resp.records { lines.append("  \(r.rdata)   TTL \(r.ttl)   type \(r.type)") }
        }
        // Query local resolver + reference public resolvers for Prophet multi-oracle consensus
        let oracles = ProphetOracleRoster.roster(localServer: srv)

        var resolverResponses: [ProphetResult.ResolverResponse] = []
        // B231: ResolverLatency.medianMs is already in milliseconds — do NOT multiply by 1000.
        // Preserve nil when the probe was skipped; a fabricated 0ms reads as a perfect score.
        let latencyMs = sectionInputs.latency.map { Int($0.medianMs.rounded()) }

        await withTaskGroup(of: (String, String).self) { group in
            for oracle in oracles {
                group.addTask {
                    let res = await DNSResolver.queryUDPDetailed(DNSQuery(name: name, type: type, server: oracle.ip))
                    let ip = res.records.first?.rdata ?? (res.status == .success ? "no records" : "[unreachable]")
                    return (oracle.name, ip)
                }
            }
            for await (oracleName, ip) in group {
                resolverResponses.append(ProphetResult.ResolverResponse(resolver: oracleName, ip: ip))
            }
        }

        // Sort so Local resolver is first
        resolverResponses.sort { r1, r2 in
            if r1.resolver.hasPrefix("Local") { return true }
            if r2.resolver.hasPrefix("Local") { return false }
            return r1.resolver < r2.resolver
        }

        // B230: carry the honest count — 0 reachable oracles renders as "no oracle answered"
        // in the view; fabricating a floor of 1 made the unreachable state look measured.
        let activeCount = resolverResponses.filter { !$0.ip.contains("[unreachable]") }.count
        self.prophetResult = ProphetResult(
            resolverResponses: resolverResponses,
            consensus: ProphetResult.Consensus(resolversQueried: activeCount, medianLatency: latencyMs)
        )
        // DNSSEC posture for the duplicate result card: a REAL DO-enabled DNSKEY query against
        // the same audited resolver. Hidden pre-scan (nil), tri-state after — never asserted.
        self.dnssecStatus = await DNSSECVerifier.checkStatus(domain: name, resolver: srv)
        return lines.joined(separator: "\n")
    }

    private func runCompare() async -> String {
        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return "Enter a domain to compare." }
        let local = NetworkKeyState.getSystemDNSServers().first ?? server

        // B228: pass targetURL: nil so ProbePlan derives the warp target from the domain the
        // user actually typed, rather than always fetching https://github.com (the stale
        // default of `url`). The Mirror panel's "content matches" claim would otherwise be
        // evidence about a different site than the one on screen.
        let result = await ProbePlan.runMirror(domain: name, localResolver: local, isPro: isPro, targetURL: nil)
        sectionInputs.comparison = result.comparison
        sectionInputs.targetDomain = name   // Stage-5 answer-matrix query context (type defaults to A)
        sectionInputs.authoritative = result.authoritative
        sectionInputs.serverGroundTruth = result.serverGroundTruth
        sectionInputs.svcb = result.svcb
        sectionInputs.ttlAnomaly = result.ttlAnomaly
        sectionInputs.cnameChain = result.cnameChain
        // WARP_MANIFEST_V1 signed-manifest result (the live warp result). The legacy `warp`
        // (WarpResult) field is retired; the Mirror View surface (UI stream) migrates to read
        // `sectionInputs.warpManifest`.
        sectionInputs.warpManifest = result.warpManifest
        self.mirrorResult = result.comparison

        guard let comparison = result.comparison else {
            return "Comparison failed: vantage probe did not return a result."
        }

        let summary: String
        if comparison.isInconclusive { summary = "⚠️ INCONCLUSIVE — no trustworthy public baseline" }
        else if comparison.isRedirectionDetected { summary = "🚨 TAMPERING / REDIRECTION DETECTED" }
        else if comparison.isDegraded { summary = "⚠️ Inconclusive-leaning — baseline degraded (some resolvers failed); not a clean bill of health." }
        else { summary = "✅ No tampering detected" }
        var lines = ["Conclusion: \(summary)", "", "Local (\(comparison.localServer)): \(describe(comparison.localOutcome))"]
        for name in VantageComparer.publicResolvers.keys.sorted() {
            if let o = comparison.publicResolverOutcomes[name] { lines.append("\(name): \(describe(o))") }
        }
        return lines.joined(separator: "\n")
    }

    private func describe(_ o: ResolverQueryOutcome) -> String {
        if let e = o.errorDescription { return "[failed: \(e)]" }
        if o.records.isEmpty { return "[no records — \(o.status.rawValue)]" }
        return o.records.map { $0.rdata }.joined(separator: ", ")
    }

    private func runLeak() async -> String {
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

    private func runNetworkState() async -> String {
        let result = await ProbePlan.runPortal()
        self.portalReport = result.network
        sectionInputs.network = result.network
        sectionInputs.encryptedTransport = result.encryptedTransport
        sectionInputs.openResolver = result.openResolver

        guard let report = result.network else {
            return "Network state report could not be generated."
        }

        func tri(_ s: ConditionState) -> String { s == .yes ? "yes" : (s == .no ? "no" : "unknown") }
        var lines = ["System DNS resolvers:"]
        lines.append(report.dnsServers.isEmpty ? "  (none detected)" : report.dnsServers.map { "  \($0)" }.joined(separator: "\n"))
        lines.append("")
        lines.append("Active interfaces: \(report.activeInterfaces.joined(separator: ", "))")
        lines.append("")
        lines.append("VPN: \(tri(report.vpn.state)) — \(report.vpn.evidence)")
        lines.append("Captive portal: \(tri(report.captivePortal.state)) — \(report.captivePortal.evidence)")
        lines.append("Encrypted DNS: \(tri(report.encryptedDNS.state)) — \(report.encryptedDNS.evidence)")
        lines.append("")

        // Open Resolver & Amplification exposure (advisory) — scoped to the user's own LAN devices only.
        if let openRes = result.openResolver {
            switch openRes.status {
            case .recursiveHighAmp: lines.append("Open resolver: 🚨 \(openRes.detail)")
            case .recursiveLowAmp:  lines.append("Open resolver: ⚠️ \(openRes.detail)")
            case .notRecursive:     lines.append("Open resolver: ✓ your LAN resolver does not openly recurse for us")
            case .untested:         lines.append("Open resolver: (unmeasured — probe did not complete)")
            }
        } else {
            lines.append("Open resolver: (skipped — configured resolver is public)")
        }

        return lines.joined(separator: "\n")
    }

    // MARK: - Portal traceroute (GUI wrapper around the system `traceroute`)

    /// Start a system traceroute to the current `domain`, off the main thread, collecting the
    /// tool's real hop lines into `traceHops`. Gated by `isSafeTraceTarget` so we never hand
    /// hostile input to the subprocess. Honest states only: `.invalidTarget` (empty/unsafe),
    /// `.running`, `.completed` (real hops), `.failed` (could not run / no output). Never fabricates.
    func startTraceroute() {
        let target = domain.trimmingCharacters(in: .whitespaces)
        traceHops = []
        guard !target.isEmpty, AppModel.isSafeTraceTarget(target) else {
            traceState = .invalidTarget
            return
        }
        // B245: cancel any prior trace so a stale completion can't clobber the new target's result
        // or resurrect state after resetAll().
        traceTask?.cancel()
        traceState = .running
        traceTask = Task.detached(priority: .userInitiated) {
            let outcome = await AppModel.performTraceroute(target: target)
            await MainActor.run {
                // B245: guard the write-back — if cancelled (reset or newer trace), discard.
                guard !Task.isCancelled else { return }
                self.traceHops = outcome.hops
                self.traceState = outcome.state
            }
        }
    }

    /// Run the shared `TracerouteRunner` for `target` and map its real output lines to a tri-state
    /// outcome. Matches the guided-CLI Portal trace's tool invocation (`-q 1 -w 1 -m 30`, 45s guard).
    /// An empty result is an honest failure, never a clean path.
    nonisolated static func performTraceroute(target: String) async -> (hops: [String], state: TraceState) {
        let hops = await TracerouteRunner.trace(target: target, maxHops: 30)
        if hops.isEmpty {
            return ([], .failed("the traceroute produced no output"))
        }
        return (hops, .completed)
    }

    /// Reject empty, flag-like, whitespace-bearing, or non-hostname/IP input before it reaches the
    /// traceroute subprocess. Mirrors the guided CLI's `isSafeTraceTarget`.
    nonisolated static func isSafeTraceTarget(_ target: String) -> Bool {
        if target.isEmpty || target.hasPrefix("-") || target.rangeOfCharacter(from: .whitespacesAndNewlines) != nil {
            return false
        }
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-:")
        return target.rangeOfCharacter(from: allowed.inverted) == nil
    }

    /// Sendable error carrier so a caught error's message can be threaded OUT of a ProbeRunner
    /// closure (whose Output must be Sendable — `any Error` is not). Fixes the three catch blocks in
    /// `runWitness()` that used to discard the real error and report a fixed generic string.
    private struct WitnessProbeError: Error, Sendable { let message: String }

    /// Resolve the Witness vantage comparison through the B255 seam. Production uses live
    /// `VantageComparer` under ProbeRunner; injected seams skip the network and return the
    /// exact classification the caller asserts. Budget is owned locally so it is not a
    /// MainActor-isolated `inout` crossing into nonisolated `ProbeRunner.run`.
    private func resolveWitnessComparison(domain name: String, local: String) async -> Result<SyxxKit.ComparisonResult, WitnessProbeError>? {
        switch witnessVantageSeam {
        case .production:
            var probeBudget = ProbeBudget.defaultMirror
            return await ProbeRunner.run(label: "vantage", budget: &probeBudget) {
                do {
                    return .success(try await VantageComparer.compareVantagePoints(domain: name, localServer: local))
                } catch {
                    return .failure(WitnessProbeError(message: ErrorPresenter.userDetail(for: error, component: "witness.vantage") ?? ""))
                }
            }
        case .fixed(let comparison):
            return .success(comparison)
        case .failed(let detail):
            return .failure(WitnessProbeError(message: detail))
        case .noResult:
            return nil
        }
    }

    private func runWitness() async -> String {
        // B195 parity: route through the SAME published state the interactive Witness tab uses, so
        // "Run All Wardens" and the generic runner reflect what actually happened instead of leaving
        // the tab stale. Mirrors witnessTestify()'s state discipline (idle/running/signed/failed).
        showValidation = false
        showTestimony = true
        witnessSignature = ""
        witnessSnapshot = nil
        witnessValid = false
        witnessResult = .notVerified
        witnessTestimonyState = .running

        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else {
            let msg = "Enter a domain."
            witnessTestimonyState = .failed(msg); return msg
        }
        // G-17: tri-state, not `hasKey` — see witnessTestify().
        switch WitnessKeyManager.checkKeyPresence() {
        case .present:
            break
        case .absent:
            let msg = "A signing key is required — use syxx witness --gen-keypair or WitnessSigner.generateKeyPair() to create one."
            witnessTestimonyState = .failed(msg); return msg
        case .undetermined:
            let msg = Self.witnessKeyStoreUnreachableMessage
            witnessTestimonyState = .failed(msg); return msg
        }
        let local = NetworkKeyState.getSystemDNSServers().first ?? server

        let comparisonResult = await resolveWitnessComparison(domain: name, local: local)
        let comparison: SyxxKit.ComparisonResult
        switch comparisonResult {
        case .success(let value):
            comparison = value
        case .failure(let err):
            let msg = err.message.isEmpty ? "Could not create witness snapshot." : "Could not create witness snapshot: \(err.message)"
            witnessTestimonyState = .failed(msg); return msg
        case .none:
            let msg = "Witness failed: vantage comparison did not return a result."
            witnessTestimonyState = .failed(msg); return msg
        }

        // Load key from Keychain momentarily for signing, then release.
        guard let key = WitnessKeyManager.loadKey() else {
            let msg = "Witness failed: signing key could not be loaded from Keychain."
            witnessTestimonyState = .failed(msg); return msg
        }
        // createSignedSnapshot and verifySnapshot are synchronous local operations;
        // ProbeRunner wrapping provides budget accounting even though timeout is N/A for sync work.
        var signBudget = ProbeBudget.defaultMirror
        let snapResult: Result<WitnessSnapshot, WitnessProbeError>? = await ProbeRunner.run(label: "signSnapshot", budget: &signBudget) {
            do {
                return .success(try WitnessSigner.createSignedSnapshot(domain: name, comparison: comparison, privateKeyBase64: key))
            } catch {
                return .failure(WitnessProbeError(message: ErrorPresenter.userDetail(for: error, component: "witness.sign") ?? ""))
            }
        }
        let snap: WitnessSnapshot
        switch snapResult {
        case .success(let value):
            snap = value
        case .failure(let err):
            let msg = err.message.isEmpty ? "Could not create witness snapshot." : "Could not create witness snapshot: \(err.message)"
            witnessTestimonyState = .failed(msg); return msg
        case .none:
            let msg = "Witness failed: could not create signed snapshot."
            witnessTestimonyState = .failed(msg); return msg
        }

        // Self-verify. D050: verifySnapshot returns a tri-state SnapshotVerification (never Bool),
        // so each forensic reason is rendered distinctly — an expired honest snapshot is never "❌ INVALID".
        var verifyBudget = ProbeBudget.defaultMirror
        let verifyResult: WitnessSigner.SnapshotVerification? = await ProbeRunner.run(label: "verifySnapshot", budget: &verifyBudget) {
            WitnessSigner.verifySnapshot(snap)
        }
        let selfVerifyLine: String
        if let verification = verifyResult {
            switch verification {
            case .valid:
                selfVerifyLine = "✅ VALID"
            case .expired:
                selfVerifyLine = "⚠️ EXPIRED — snapshot has passed its expiry date"
            case .stale:
                selfVerifyLine = "⚠️ STALE — snapshot exceeds max age"
            case .futureDated:
                selfVerifyLine = "⚠️ FUTURE DATED — snapshot timestamp ahead of local clock"
            case .untrustedSigner:
                selfVerifyLine = "⚠️ UNTRUSTED SIGNER — key not in trust set"
            case .displayMismatch:
                selfVerifyLine = "⚠️ DISPLAY MISMATCH — snapshot display fields differ from signed evidence"
            case .invalidSignature:
                selfVerifyLine = "❌ INVALID SIGNATURE"
            case .malformedInput:
                selfVerifyLine = "⚠️ MALFORMED INPUT — could not decode key or signature"
            case .unsupportedFormat:
                selfVerifyLine = "⚠️ UNSUPPORTED FORMAT — snapshot version not recognized"
            }
        } else {
            selfVerifyLine = "could not verify — self-verification did not return a result."
        }

        // The snapshot is signed regardless of the self-verify outcome; surface it to the tab.
        witnessSnapshot = snap
        witnessSignature = snap.signature
        witnessTestimonyState = .signed

        // Same drift-population step as witnessTestify(), single-sourced in the Kit planner (D093):
        // signing here stays ProbeRunner-wrapped above for budget accounting, so only steps 6-7 are
        // shared, but the drift can no longer diverge between the interactive and Run-All paths.
        self.sectionInputs.drifts = WitnessTestifyPlanner
            .driftIfRedirected(domain: name, local: local, comparison: comparison)
            .map { [$0] } ?? []
        return """
        Signed witness snapshot for \(name)
        Redirection detected: \(snap.comparison.isRedirectionDetected)
        Inconclusive: \(snap.comparison.isInconclusive)
        Signature: \(snap.signature)
        Self-verify: \(selfVerifyLine)
        """
    }
    
    // MARK: - Cage (Sniffer) logic
    
    func toggleCapture() {
        if cageCapturing {
            // B237: the privileged capture runs via XPC and cannot be terminated from here —
            // CaptureProcessBox.store was never called, so terminate() was always a no-op.
            // Cancel the live tail loop and be honest that the helper runs to its duration.
            captureTask?.cancel()
            // B244: cancel the hideCage countdown so it doesn't keep decrementing after stop.
            hideTask?.cancel()
            hideTask = nil
            cageCapturing = false
            cageHidden = false
            cageOutput = "Live tail stopped. The privileged capture continues to its configured duration."
            return
        }
        startRealCapture()
    }

    func hideCage() {
        if !cageCapturing { startRealCapture() }
        // B244: cancel any prior countdown so two calls don't race at 2× speed.
        hideTask?.cancel()
        cageHidden = true
        cageRemain = cageDuration
        hideTask = Task {
            while self.cageRemain > 0 && self.cageHidden && self.cageCapturing {
                try? await Task.sleep(for: .seconds(1))
                guard !Task.isCancelled else { return }
                self.cageRemain -= 1
            }
            guard !Task.isCancelled else { return }
            self.cageHidden = false   // reappear when the timer ends or capture stops
        }
    }

    /// Runs the REAL `syxx cage` capture (raw-socket, so it needs administrator authorization) and
    /// streams its actual captured queries live — never fabricated rows. On a missing binary, a
    /// declined prompt, or a quiet network, it says so honestly and shows nothing.
    ///
    /// STALE-COMMENT CORRECTION (2026-07-29). This used to say the privileged CLI runs under
    /// `osascript ... with administrator privileges`. It does not, and has not since the capture
    /// moved to the SMJobBless helper: the real path below is
    /// `PrivilegeManager.shared.startCageCapture(...)` over XPC. The osascript claim is why the
    /// prompt-storm diagnosis listed a third prompt source that no longer exists in this file.
    ///
    /// The tailing design still stands, for a different reason: the helper replies with the
    /// capture's output only ONCE, after the child process exits, so there is no live stdout to
    /// watch. The CLI instead streams one JSON record per captured packet to the file we name with
    /// `--output`, and we TAIL that file every ~0.4s while the capture runs. The XPC call and the
    /// tail loop run concurrently, so the live feed is never blocked on the buffered reply.
    private func startRealCapture() {
        guard let bin = Self.locateCageBinary() else {
            cageCapturing = false
            cageRows = []
            cageEventsCount = 0
            cageOutput = "Live capture is unavailable: the syxx capture binary isn't bundled with the app yet. For now, run `sudo syxx cage` from Terminal."
            return
        }
        cageCapturing = true
        cageEventsCount = 0
        cageRows = []
        cageOutput = ""
        let duration = cageDuration
        let outputURL = Self.liveCaptureURL()

        // B237: CaptureProcessBox removed — store() was never called, so terminate() was a no-op.
        // The privileged capture runs via XPC and cannot be terminated from here.
        let resultBox = CaptureResultBox()

        captureTask = Task {
            let runner = Task.detached(priority: .userInitiated) {
                do {
                    // D116: pass the resolved CLI path to the helper over XPC. The helper
                    // validates it (path prefix + code signature) before exec — never derive
                    // the path from Bundle.main inside a bare SMJobBless daemon.
                    let result = try await PrivilegeManager.shared.startCageCapture(
                        cliPath: bin.path,
                        duration: duration,
                        outputPath: outputURL.path,
                        ownerUID: getuid(),
                        ownerGID: getgid()
                    )

                    await resultBox.finish(result.output)
                } catch {
                    // Plain, actionable text to the screen; full technical detail to the diagnostic
                    // sink. A bare `error.localizedDescription` here is what rendered crash-reporter
                    // strings ("… CFErrorDomainLaunchd (8)", "… error 0.") as product copy.
                    let detail = ErrorPresenter.userDetail(for: error, component: "cage.capture") ?? ""
                    await resultBox.finish(cageLaunchErrorMarker + detail)
                }
            }
            defer { runner.cancel() }

            // Live tail: each poll reads only the bytes the file grew by, decodes the new JSON records
            // into rows, and updates the counter + feed. Monotonic, so a partial read never shrinks
            // the display.
            var offset: UInt64 = 0
            var pendingBytes = Data()   // L5: undecodable trailing bytes (split multi-byte char) carried between reads
            var pending = ""
            var accumulated: [CageRow] = []
            var records: [DNSCaptureRecord] = []          // raw records → attribution rate (C058) + per-domain cadence (B190)
            var byDomain: [String: [Date]] = [:]          // B190: per-domain OUTBOUND query timestamps, accumulated live
            var rejectedRows = 0                          // C034: JSON-decode failures (lost/corrupt records)
            var shown = 0
            while !Task.isCancelled {
                let done = await resultBox.finished
                Self.readNewRows(path: outputURL.path, offset: &offset, pendingBytes: &pendingBytes,
                                 pending: &pending, into: &accumulated, records: &records, byDomain: &byDomain,
                                 rejectedRows: &rejectedRows)
                if accumulated.count != shown {
                    shown = accumulated.count
                    // C033: bounded GUI ring buffer — only the newest N rows are rendered.
                    // The full capture stays on disk (NDJSON); the counter stays accurate.
                    let newest = accumulated.suffix(Self.cageMaxGUIRows)
                    self.cageRows = Array(newest.reversed())   // newest-first for the grid
                    self.cageEventsCount = accumulated.count
                }
                if done { break }
                try? await Task.sleep(for: .milliseconds(400))
            }

            // Cancelled (Stop / reset): leave state to the caller, just clean up.
            // B237: do NOT delete the live file here — the privileged helper may still be
            // appending to it. The file will be cleaned up on the next capture start.
            if Task.isCancelled {
                return
            }

            // One last incremental read to catch any bytes written between the final poll and exit.
            Self.readNewRows(path: outputURL.path, offset: &offset, pendingBytes: &pendingBytes,
                             pending: &pending, into: &accumulated, records: &records, byDomain: &byDomain,
                             rejectedRows: &rejectedRows)
            let rawText = await resultBox.result ?? ""
            // Live rows are authoritative when present. If the live file could not be read (e.g. the
            // admin prompt did not hand us ownership), fall back to parsing the buffered stdout so a
            // real capture is never mis-reported as empty.
            // C033: bound the final display rows too — the NDJSON on disk has everything,
            // but the GUI array stays bounded to prevent OOM on long captures.
            let liveRows = Array(accumulated.suffix(Self.cageMaxGUIRows).reversed()) // newest-first
            let finalRows: [CageRow] = liveRows.isEmpty ? Self.parseCageOutput(rawText) : liveRows

            guard !Task.isCancelled else {
                // B237: do NOT delete the live file — the privileged helper may still be writing.
                return
            }

            self.cageRows = finalRows
            self.cageEventsCount = finalRows.count
            // C034: thread rejected-row count so partial/lossy captures are disclosed, not clean.
            self.cageOutput = Self.finalMessage(rawText: rawText, rowCount: finalRows.count,
                                                rejectedRows: rejectedRows)
            self.cageCapturing = false
            self.cageHidden = false

            // C058 + B190: build the Cage section input from the decoded capture RECORDS, not just the
            // set of domain strings, so two signals the row grid used to discard reach synthesis:
            //   • per-domain OUTBOUND query cadence (B190) — the beacon leg is now actually evaluated
            //     instead of the dead `timestamps: []` path that capped `.likelyExfil` unreachable;
            //   • process-attribution completeness (C058) — a mostly-unattributed capture is DEGRADED,
            //     never a silent clean.
            // A zero-record / no-outbound capture still yields nil exfil so Cage stays UNTESTED rather
            // than falsely clean, matching MonitorReportAnalyzer.exfilAssessments' contract. When the
            // live file could not be read we fall back to the parsed stdout rows — a degraded but honest
            // source (clock-only times, parsed process name), never fabricated series.
            if records.isEmpty {
                let fallback = Self.cageSectionInputs(fromRows: finalRows)
                self.sectionInputs.exfil = fallback.exfil
                self.sectionInputs.cageAttributionRate = fallback.attributionRate
            } else {
                self.sectionInputs.exfil = MonitorReportAnalyzer.exfilAssessments(from: records)
                self.sectionInputs.cageAttributionRate = Self.attributionRate(from: records)
            }

            Self.cleanupLiveFile(outputURL)
        }
    }

    /// A unique per-run path under the user's own captures dir for the GUI to tail. Created eagerly so
    /// the privileged CLI (running as root) can write into a user-owned 0700 directory. Also sweeps
    /// any live files a crashed prior run left behind, so the captures dir never accumulates strays.
    private nonisolated static func liveCaptureURL() -> URL {
        let dir = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".syxx/captures", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true,
                                                 attributes: [.posixPermissions: 0o700])
        if let leftovers = try? FileManager.default.contentsOfDirectory(atPath: dir.path) {
            for f in leftovers where f.hasPrefix("gui-live-") && f.hasSuffix(".txt") {
                try? FileManager.default.removeItem(atPath: dir.appendingPathComponent(f).path)
            }
        }
        return dir.appendingPathComponent("gui-live-\(UUID().uuidString).txt")
    }

    private nonisolated static func cleanupLiveFile(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
    }

    /// Reads the bytes appended to `path` since `offset`, decodes each COMPLETE JSON line into a
    /// `DNSCaptureRecord`, maps it to a `CageRow`, and appends to `accumulated`. A trailing partial
    /// line (root still writing it) is buffered in `pending` and completed on the next read. Cheap:
    /// only new bytes are ever read and decoded. An unreadable file is a no-op (rows stay as they were).
    private nonisolated static func readNewRows(path: String, offset: inout UInt64,
                                                pendingBytes: inout Data,
                                                pending: inout String, into accumulated: inout [CageRow],
                                                records: inout [DNSCaptureRecord],
                                                byDomain: inout [String: [Date]],
                                                rejectedRows: inout Int) {
        guard let size = fileSize(path), size > offset else { return }
        guard let handle = FileHandle(forReadingAtPath: path) else { return }
        defer { try? handle.close() }
        handle.seek(toFileOffset: offset)
        let payload = handle.readDataToEndOfFile()
        // Advance by the bytes actually read, not the stale stat size: the writer may have appended
        // between the stat and this read, and using `size` would re-read (and duplicate) that tail.
        offset += UInt64(payload.count)
        // L5 — a read can end in the middle of a multi-byte UTF-8 character (the writer is mid-write).
        // Decoding the raw payload would fail and silently drop the WHOLE chunk even though `offset`
        // already advanced past it, losing records from both the rows and the attribution/cadence series.
        // Prepend any bytes we could not decode last time, decode the longest valid UTF-8 prefix, and
        // carry the undecodable trailing bytes forward to complete on the next read.
        var buffer = pendingBytes
        buffer.append(payload)
        let (text, remainder) = decodeUTF8Prefix(buffer)
        pendingBytes = remainder
        guard !text.isEmpty else { return }
        pending += text
        // Split into complete lines; keep any unterminated remainder as the next pending buffer.
        var lines = pending.components(separatedBy: "\n")
        pending = lines.removeLast()   // text after the final "\n" (empty when it ended cleanly)
        let dec = JSONDecoder()
        for line in lines {
            let s = line.trimmingCharacters(in: .whitespaces)
            guard !s.isEmpty else { continue }
            // C034: count JSON-decode failures so lost/corrupt records are disclosed rather than
            // silently vanishing from the capture. A non-zero count marks evidence partial.
            guard let d = s.data(using: .utf8),
                  let rec = try? dec.decode(DNSCaptureRecord.self, from: d) else {
                rejectedRows += 1
                continue
            }
            records.append(rec)
            // B190: accumulate this record's per-domain OUTBOUND query time so the row (and the final
            // section input) can evaluate beacon cadence instead of the dead `timestamps: []` path.
            let dom = rec.packet.questions.first?.questionName ?? "unknown"
            if rec.network.direction.lowercased().contains("outbound"), !dom.isEmpty, dom != "unknown",
               let t = MonitorReportAnalyzer.parseTimestamp(rec.timestamp) {
                byDomain[dom, default: []].append(t)
            }
            accumulated.append(cageRow(from: rec, timestamps: byDomain[dom] ?? []))
        }
    }

    /// L5 — decode the longest valid UTF-8 prefix of `data`, returning that string and any undecodable
    /// trailing bytes (a split multi-byte character, at most 3 continuation bytes) to carry forward. A
    /// buffer that is genuinely corrupt (no valid prefix within the last 3 bytes) is decoded lossily so
    /// the tail loop always makes forward progress rather than re-buffering the same bytes forever.
    private nonisolated static func decodeUTF8Prefix(_ data: Data) -> (String, Data) {
        if let s = String(data: data, encoding: .utf8) { return (s, Data()) }
        let bytes = [UInt8](data)
        var cut = bytes.count
        let minKeep = max(0, bytes.count - 3)
        while cut > minKeep {
            cut -= 1
            if let s = String(bytes: bytes[0..<cut], encoding: .utf8) {
                return (s, Data(bytes[cut...]))
            }
        }
        return (String(decoding: bytes, as: UTF8.self), Data())
    }

    private nonisolated static func fileSize(_ path: String) -> UInt64? {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: path),
              let size = attrs[.size] as? UInt64 else { return nil }
        return size
    }

    /// Maps a decoded capture record to the same `CageRow` shape the stdout parser produces: severity
    /// comes from the REAL `ExfilAnalyzer` assessment (entropy / blocklist), never an invented one.
    private nonisolated static func cageRow(from rec: DNSCaptureRecord, timestamps: [Date]) -> CageRow {
        let domain = rec.packet.questions.first?.questionName ?? "unknown"
        let proc = rec.process.name ?? "unknown"
        let resolver = rec.network.sourcePort == 53 ? rec.network.sourceIP : rec.network.destIP
        let direction = rec.network.direction
        let rcode = rec.packet.rcode
        // B190: assess with the REAL per-domain cadence so the beacon leg participates (not `timestamps: []`).
        let assessment = ExfilAnalyzer.assess(domain: domain, timestamps: timestamps)
        // C059: honest tri-state badge. An unjudgeable domain (`.inconclusive`) must NOT render as the
        // same reassuring INFO as an affirmatively-benign row — it maps to the neutral "not judged"
        // `.unknown` treatment (muted grey), never green, never info-purple.
        let severity: Severity
        switch assessment.suspicion {
        case .likelyExfil:  severity = .critical
        case .suspicious:   severity = .warning
        case .benign:       severity = .info
        case .inconclusive: severity = .unknown
        }
        var note = rcode.isEmpty ? direction : "\(direction) · \(rcode)"
        if assessment.suspicion == .likelyExfil || assessment.suspicion == .suspicious {
            note += " — \(assessment.rationale.first ?? assessment.suspicion.rawValue)"
        }
        return CageRow(time: shortTime(rec.timestamp), domain: domain, note: note,
                       process: proc, resolver: resolver, severity: severity)
    }

    /// "yyyy-MM-dd HH:mm:ss.SSS" -> "HH:mm:ss" for the fixed-width Time column.
    private nonisolated static func shortTime(_ ts: String) -> String {
        let parts = ts.split(separator: " ")
        guard parts.count >= 2 else { return ts }
        return String(parts[1].split(separator: ".").first ?? parts[1])
    }

    // L6 — the former private `parseTimestamp` / `exfilAssessments` copies were removed; the GUI live-tail
    // path now calls `MonitorReportAnalyzer.parseTimestamp` / `.exfilAssessments` directly so the GUI and
    // log-analysis paths share one grouping+parsing implementation and cannot drift.

    /// "HH:mm:ss" clock time (all the stdout-parse fallback rows carry) -> a nominal same-reference-day
    /// `Date`. Only inter-arrival intervals matter to the beacon cadence, so the reference day is
    /// immaterial; used solely by the degraded stdout fallback, never the live capture path.
    private nonisolated static func parseClockTime(_ time: String) -> Date? {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm:ss"
        return f.date(from: time)
    }

    /// C058 — fraction (0...1) of capture records that carried a real process identity (`isAttributed`),
    /// or nil when there were no records. Threaded into `sectionInputs.cageAttributionRate` so a mostly-
    /// unattributed capture is DEGRADED at synthesis, never a silent clean. Computed over EVERY record
    /// (both directions) to match the `Synthesizer.cage` contract wording ("fraction of capture records");
    /// inbound responses are frequently unattributable, which biases the rate toward degraded — the
    /// honest-safe direction.
    private nonisolated static func attributionRate(from records: [DNSCaptureRecord]) -> Double? {
        guard !records.isEmpty else { return nil }
        let attributed = records.filter { $0.process.isAttributed }.count
        return Double(attributed) / Double(records.count)
    }

    /// Degraded fallback Cage section input, used ONLY when the live capture file could not be read and we
    /// are left with the parsed stdout rows. Row clock-times ("HH:mm:ss") are reconstructed to nominal
    /// same-day Dates so the beacon cadence can still be evaluated where feasible; attribution comes from
    /// the parsed process name ("unknown"/empty = unattributed). Honest but lossy (no date, no millis, no
    /// direction filter) — never a fabricated series. Returns (nil, nil) for no rows so Cage stays UNTESTED.
    private nonisolated static func cageSectionInputs(fromRows rows: [CageRow]) -> (exfil: [ExfilAssessment]?, attributionRate: Double?) {
        guard !rows.isEmpty else { return (nil, nil) }
        var byDomain: [String: [Date]] = [:]
        var order: [String] = []
        for r in rows {
            if byDomain[r.domain] == nil { byDomain[r.domain] = []; order.append(r.domain) }
            if let t = parseClockTime(r.time) { byDomain[r.domain]!.append(t) }
        }
        let exfil = order.map { ExfilAnalyzer.assess(domain: $0, timestamps: byDomain[$0] ?? []) }
        let attributed = rows.filter { $0.process != "unknown" && !$0.process.isEmpty }.count
        return (exfil.isEmpty ? nil : exfil, Double(attributed) / Double(rows.count))
    }

    /// The honest end-of-run status line, reusing the same wording the capture already used.
    private nonisolated static func finalMessage(rawText: String, rowCount: Int,
                                                  rejectedRows: Int = 0) -> String {
        if rawText.hasPrefix(cageLaunchErrorMarker) {
            let detail = String(rawText.dropFirst(cageLaunchErrorMarker.count))
                .trimmingCharacters(in: .whitespacesAndNewlines)
            // State the failure without a cause when the error carried no readable one, rather than
            // trailing a bare "Could not start capture: " or a crash-reporter fragment.
            return detail.isEmpty ? "Could not start capture." : "Could not start capture: \(detail)"
        }
        // C044: detect capture abort (pcap error mid-run) — any surviving rows are partial
        // evidence from an aborted session, never a normal clean report.
        let aborted = rawText.contains("CAGE_CAPTURE_ABORTED")
        if rowCount > 0 {
            // C034: any rejected records mark the capture as PARTIAL — a clean count would mask
            // evidence loss. The rejected count is surfaced so the user knows the capture is
            // incomplete; downstream synthesis treats this as degraded/inconclusive evidence.
            if aborted {
                return "⚠️ INCONCLUSIVE — Capture aborted after \(rowCount) packet(s) (pcap error). Evidence is partial and must not be treated as conclusive."
            }
            if rejectedRows > 0 {
                return "⚠️ PARTIAL — Captured \(rowCount) DNS packet(s), \(rejectedRows) record(s) rejected (decode failure). Evidence is incomplete."
            }
            return "Captured \(rowCount) DNS packet(s)."
        }
        let cancelled = rawText.contains("-128") || rawText.lowercased().contains("cancel")
        return cancelled
            ? "Capture cancelled — administrator authorization is required for live packet capture."
            : "No packets were detected (a quiet network, or authorization was not granted)."
    }

    private nonisolated static func locateCageBinary() -> URL? {
        let fm = FileManager.default
        var candidates: [URL] = []
        // Bundled app: `syxx` in Resources/ or next to the executable.
        if let res = Bundle.main.resourceURL { candidates.append(res.appendingPathComponent("syxx")) }
        if let exe = Bundle.main.executableURL { candidates.append(exe.deletingLastPathComponent().appendingPathComponent("syxx")) }
        // SwiftPM dev build: walk up from the exe dir into .build/debug or .build/release.
        if let exe = Bundle.main.executableURL {
            var dir = exe.deletingLastPathComponent()
            for _ in 0..<5 {
                let debug = dir.appendingPathComponent(".build/debug/syxx")
                let release = dir.appendingPathComponent(".build/release/syxx")
                if fm.fileExists(atPath: debug.path) { candidates.append(debug) }
                if fm.fileExists(atPath: release.path) { candidates.append(release) }
                dir = dir.deletingLastPathComponent()
            }
        }
        // PATH fallback: `which syxx`
        if let path = Self.findInPATH("syxx") { candidates.append(path) }
        return candidates.first { fm.isExecutableFile(atPath: $0.path) }
    }

    /// Resolve a command name via `which` so we can find `syxx` on PATH even when
    /// running outside the app bundle (e.g. `swift run syxx-gui`).
    private nonisolated static func findInPATH(_ name: String) -> URL? {
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/which")
        proc.arguments = [name]
        let pipe = Pipe()
        proc.standardOutput = pipe
        proc.standardError = pipe
        guard (try? proc.run()) != nil else { return nil }
        proc.waitUntilExit()
        guard proc.terminationStatus == 0 else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        let path = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !path.isEmpty else { return nil }
        return URL(fileURLWithPath: path)
    }

    /// Parses `syxx cage` real per-packet stdout lines — `• [HH:mm:ss] Direction: domain (via proc
    /// | Rcode: code)` — into rows, coloring each by the REAL `ExfilAnalyzer` assessment (entropy /
    /// blocklist), not an invented severity.
    private nonisolated static func parseCageOutput(_ text: String) -> [CageRow] {
        var rows: [CageRow] = []
        var byDomain: [String: [Date]] = [:]   // B190: per-domain clock times, so the beacon leg is evaluated here too
        for raw in text.split(whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard line.hasPrefix("• ["), let tEnd = line.firstIndex(of: "]") else { continue }
            let time = String(line[line.index(line.startIndex, offsetBy: 3)..<tEnd])
            let rest = String(line[line.index(after: tEnd)...]).trimmingCharacters(in: .whitespaces)
            guard let colon = rest.firstIndex(of: ":") else { continue }
            let direction = String(rest[..<colon]).trimmingCharacters(in: .whitespaces)
            var tail = String(rest[rest.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
            var proc = "unknown", rcode = "", resolver = ""
            if let paren = tail.firstIndex(of: "(") {
                let meta = String(tail[tail.index(after: paren)...]).replacingOccurrences(of: ")", with: "")
                tail = String(tail[..<paren]).trimmingCharacters(in: .whitespaces)
                for p in meta.split(separator: "|").map({ $0.trimmingCharacters(in: .whitespaces) }) {
                    if p.hasPrefix("via ") { proc = String(p.dropFirst(4)) }
                    if p.hasPrefix("Rcode:") { rcode = p.replacingOccurrences(of: "Rcode:", with: "").trimmingCharacters(in: .whitespaces) }
                    if p.hasPrefix("Resolver:") { resolver = p.replacingOccurrences(of: "Resolver:", with: "").trimmingCharacters(in: .whitespaces) }
                }
            }
            // B190: reconstruct this domain's clock-time series (feasible from the parsed time) so the
            // beacon leg is evaluated in the fallback path rather than silently scoring "no beacon."
            if let t = parseClockTime(time) { byDomain[tail, default: []].append(t) }
            let assessment = ExfilAnalyzer.assess(domain: tail, timestamps: byDomain[tail] ?? [])
            // C059: honest tri-state badge — `.inconclusive` maps to the neutral `.unknown` treatment,
            // never the reassuring INFO an affirmatively-benign row gets.
            let severity: Severity
            switch assessment.suspicion {
            case .likelyExfil:  severity = .critical
            case .suspicious:   severity = .warning
            case .benign:       severity = .info
            case .inconclusive: severity = .unknown
            }
            var note = rcode.isEmpty ? direction : "\(direction) · \(rcode)"
            if assessment.suspicion == .likelyExfil || assessment.suspicion == .suspicious {
                note += " — \(assessment.rationale.first ?? assessment.suspicion.rawValue)"
            }
            rows.append(CageRow(time: time, domain: tail, note: note, process: proc, resolver: resolver, severity: severity))
        }
        return rows.reversed()   // most-recent first
    }
    
    // MARK: - Witness Testimony Flow
    
    /// Persists a user-supplied signing key into the Keychain via WitnessKeyManager.
    /// The plaintext key never lives on the model; only `hasWitnessKey` is exposed.
    ///
    /// D052: the Keychain write no longer fails silently. On a real save error the failure is
    /// surfaced in the Testimony card (amber operational failure) and `false` is returned so the
    /// caller does NOT proceed to testify/verify (which would otherwise mislead with a generic
    /// "a signing key is required" message) and does NOT clear the input field.
    /// - Returns: `true` when the key was saved or there was nothing to save (empty input);
    ///   `false` when the Keychain write threw, was refused, or is awaiting confirmation.
    ///
    /// M-01 (Linux RG-2 adoption): the write is now gated on the Keychain PRESENCE tri-state. It
    /// previously called `saveKey` unconditionally, so a user following the old "no signing key —
    /// enter your key above" message overwrote the key their existing testimonies verify against,
    /// with no prompt and no way back. `confirmOverwrite` is the user's explicit "yes, replace it";
    /// an unreadable Keychain is refused outright, never guessed at.
    /// - Parameter confirmOverwrite: pass `true` only after the user has confirmed replacement of
    ///   an existing key. Never default this to `true`.
    @discardableResult
    func saveWitnessKey(_ base64Key: String, confirmOverwrite: Bool = false) -> Bool {
        let trimmed = base64Key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return true }
        switch witnessKeyWriteGate() {
        case .proceed:
            break
        case .needsOverwriteConfirmation:
            guard confirmOverwrite else {
                witnessKeyOverwritePending = true
                return false
            }
        case .refusedStoreUnreachable:
            let msg = Self.witnessKeyStoreUnreachableMessage
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        do {
            try WitnessKeyManager.saveKey(trimmed)
        } catch {
            // G-17: do NOT re-derive `hasWitnessKey` from `hasKey` here. On an unreadable Keychain
            // that reports "no key on this machine" from a state where we could not ask — the same
            // collapse M-01 closes. Only a determinate answer may move the flag.
            refreshHasWitnessKeyIfDeterminate()
            let detail = ErrorPresenter.userDetail(for: error, component: "witness.saveKey")
            let msg = detail.map { "Could not save the signing key to the Keychain: \($0)" }
                ?? "Could not save the signing key to the Keychain."
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        // The write succeeded, so a key is present by construction — no need to re-ask a Keychain
        // that might now refuse and make the UI claim there is no key one line after storing one.
        hasWitnessKey = true
        return true
    }

    /// G-17: move `hasWitnessKey` only on a determinate Keychain answer. `.undetermined` leaves the
    /// flag as it was — an unreadable Keychain is not evidence that the key is gone.
    private func refreshHasWitnessKeyIfDeterminate() {
        switch WitnessKeyManager.checkKeyPresence() {
        case .present: hasWitnessKey = true
        case .absent: hasWitnessKey = false
        case .undetermined: break
        }
    }

    // E045: generate a new Ed25519 keypair and store the private key in the Keychain.
    // Surfaces the public key so the user can share it for trust-set configuration.
    // Previously, WitnessKeyManager.generateAndStoreKey() existed but was never exposed
    // through the GUI, forcing users to CLI tools or external generators.
    ///
    /// M-01: gated identically to `saveWitnessKey`. `generateAndStoreKey()` calls the same
    /// unconditional `saveKey`, so this button was the shorter road to the same irreversible loss —
    /// one click, no typing, every previously sealed testimony stops verifying.
    /// - Returns: `true` when a keypair was generated; `false` when the write was refused or is
    ///   awaiting the user's explicit confirmation.
    @discardableResult
    func generateWitnessKey(confirmOverwrite: Bool = false) -> Bool {
        switch witnessKeyWriteGate() {
        case .proceed:
            break
        case .needsOverwriteConfirmation:
            guard confirmOverwrite else {
                witnessKeyOverwritePending = true
                return false
            }
        case .refusedStoreUnreachable:
            let msg = Self.witnessKeyStoreUnreachableMessage
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        do {
            let publicKey = try WitnessKeyManager.generateAndStoreKey()
            hasWitnessKey = true
            output = "New Ed25519 keypair generated. Public key: \(publicKey)"
            showValidation = false
            showTestimony = false
        } catch {
            let detail = ErrorPresenter.userDetail(for: error, component: "witness.generateKey")
            let msg = detail.map { "Could not generate signing key: \($0)" }
                ?? "Could not generate signing key."
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        return true
    }

    func witnessTestify() {
        // Cancel any prior in-flight testify so two rapid clicks can't race — the last finisher must
        // not win regardless of click order (e.g. domain B's UI ending up with domain A's signature),
        // and a completion must never fire after witnessFree() and resurrect cleared state. The task
        // body guards on Task.isCancelled before writing final state back.
        witnessTask?.cancel()
        showValidation = false
        showTestimony = true
        witnessSignature = ""
        witnessSnapshot = nil
        witnessValid = false
        witnessResult = .notVerified
        witnessTestimonyState = .running
        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else {
            let msg = "Enter a domain to attest."
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        }
        // G-17: same tri-state as witnessVerify. "I could not read the Keychain" must not be
        // reported as "you have no signing key" — that reading is what sends the user to the
        // overwrite.
        switch WitnessKeyManager.checkKeyPresence() {
        case .present:
            break
        case .absent:
            let msg = "A signing key is required — a snapshot signed with a default/empty key would be forgeable."
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        case .undetermined:
            let msg = Self.witnessKeyStoreUnreachableMessage
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        }
        guard let key = WitnessKeyManager.loadKey() else {
            let msg = "Could not load signing key from Keychain."
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        }
        let srv = server
        witnessTask = Task {
            do {
                let local = NetworkKeyState.getSystemDNSServers().first ?? srv
                let comparison: SyxxKit.ComparisonResult
                switch self.witnessVantageSeam {
                case .production:
                    comparison = try await VantageComparer.compareVantagePoints(domain: name, localServer: local)
                case .fixed(let value):
                    comparison = value
                case .failed(let detail):
                    throw NSError(domain: "Syxx.WitnessVantageSeam", code: 1,
                                  userInfo: [NSLocalizedDescriptionKey: detail])
                case .noResult:
                    let msg = "Witness failed: vantage comparison did not return a result."
                    guard !Task.isCancelled else { return }
                    self.output = msg
                    self.witnessTestimonyState = .failed(msg)
                    return
                }
                // Steps 5-7 (sign → interpret redirection → build drift) are the pure determinative
                // core, single-sourced in SyxxKit.WitnessTestifyPlanner and shared with runWitness()
                // so the drift-population step can never again be dropped on one path (D093).
                let outcome = try WitnessTestifyPlanner.seal(domain: name, local: local, comparison: comparison, privateKeyBase64: key)
                // Cancelled (superseded by a newer testify, or freed) → do not write stale state back.
                guard !Task.isCancelled else { return }
                self.witnessSnapshot = outcome.snapshot
                self.witnessSignature = outcome.snapshot.signature   // Ed25519 signature
                self.witnessTestimonyState = .signed
                self.sectionInputs.drifts = outcome.drift.map { [$0] } ?? []
            } catch {
                guard !Task.isCancelled else { return }
                let detail = ErrorPresenter.userDetail(for: error, component: "witness.testify")
                let msg = detail.map { "Could not create witness snapshot: \($0)" }
                    ?? "Could not create witness snapshot."
                self.output = msg
                self.witnessTestimonyState = .failed(msg)
            }
        }
    }

    func witnessVerify() {
        // D069/B049: distinguish "nothing to verify" from a real signature failure, and never set a
        // valid/green state unless the Ed25519 check actually passed. A missing snapshot is NOT a
        // signature mismatch; a missing key is NOT tampering. Each branch sets an honest result.
        guard let snap = witnessSnapshot else {
            witnessValid = false
            witnessResult = .notVerified
            showValidation = true
            return
        }
        // M-01: read the D084 tri-state, not `hasKey`. `hasKey` maps `.undetermined -> false`
        // (WitnessKeyManager.swift:38-44) and its own doc comment tells callers that need to tell
        // "no key" from "could not ask" to use `checkKeyPresence()` — this call site ignored that,
        // so a merely locked Keychain rendered as a definite "no signing key on this machine.
        // Enter your key above" and walked the user into overwriting the key they still had.
        switch WitnessKeyManager.checkKeyPresence() {
        case .present:
            break
        case .absent:
            witnessValid = false
            witnessResult = .noKey(.confirmedAbsent)
            showValidation = true
            return
        case .undetermined(let status):
            witnessValid = false
            witnessResult = .noKey(.undetermined(status))
            showValidation = true
            return
        }
        // D011/B081: pass configured trusted WITNESS keys so verification checks the witness
        // signer against the correct trust set, not just the snapshot's own embedded key.
        // Previously this read the COLLECTOR trust set (CollectorPinning), so an honest witness
        // seal rendered .untrustedSigner whenever a collector key was configured. Windows/Linux
        // pass no trust arg and auto-load the witness set (WitnessSigner.swift:254-257); passing
        // the witness keys here (empty -> nil) reproduces that same auto-load behavior.
        let trustedKeys = Array(WitnessPinning.configuredTrustedWitnessKeys())
        let result = WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
        applyWitnessVerification(result)
    }

    /// 5(b): verify a testimony pasted from another machine. Unlike `witnessVerify` (which checks the
    /// SELF snapshot), this decodes an external `WitnessSnapshot` from JSON and routes it through the
    /// SAME `verifySnapshotDetailed` + result switch. No local signing key is required — verification
    /// uses the snapshot's embedded public key against the configured witness trust set, exactly as
    /// the CLI (`SyxxWitness.swift`) does. Malformed JSON yields an honest `.malformedInput` result,
    /// never a crash.
    func witnessVerifyExternal() {
        let trimmed = externalSnapshotJson.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let data = trimmed.data(using: .utf8) else {
            witnessSnapshot = nil
            witnessValid = false
            witnessResult = .malformedInput
            showValidation = true
            return
        }
        guard let snap = try? JSONDecoder().decode(WitnessSnapshot.self, from: data) else {
            // Decode failure is an honest malformed/inconclusive result — not a tamper accusation
            // and never a crash. Clear any stale self-snapshot so the report renders the paste's
            // outcome alone.
            witnessSnapshot = nil
            witnessValid = false
            witnessResult = .malformedInput
            showValidation = true
            return
        }
        // Surface the pasted snapshot's own metadata/signature in the SAME validation report the
        // self-verify path renders.
        witnessSnapshot = snap
        witnessSignature = snap.signature
        let trustedKeys = Array(WitnessPinning.configuredTrustedWitnessKeys())
        let result = WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
        applyWitnessVerification(result)
    }

    /// Shared mapping from the forensic tri-state to the honest, non-contradictory `witnessResult`
    /// the view renders. Both self-verify and external-paste verify funnel through here so the two
    /// paths can never drift into rendering the same outcome differently (E021 / D069/B049).
    private func applyWitnessVerification(_ result: WitnessSigner.SnapshotVerification) {
        switch result {
        case .valid:
            witnessValid = true
            witnessResult = .valid
        case .expired:
            witnessValid = false
            witnessResult = .expired
        case .stale:
            witnessValid = false
            witnessResult = .stale
        case .futureDated:
            witnessValid = false
            witnessResult = .futureDated
        case .untrustedSigner:
            witnessValid = false
            witnessResult = .untrustedSigner
        case .displayMismatch:
            witnessValid = false
            witnessResult = .displayMismatch
        case .invalidSignature:
            witnessValid = false
            witnessResult = .invalidSignature
        case .malformedInput:
            witnessValid = false
            witnessResult = .malformedInput
        case .unsupportedFormat:
            witnessValid = false
            witnessResult = .unsupportedFormat
        }
        showValidation = true
    }

    func witnessFree() {
        // Cancel any in-flight testify first so its completion handler can't fire afterward and
        // resurrect the state we're about to clear (the task body guards on Task.isCancelled).
        witnessTask?.cancel()
        witnessTask = nil
        showTestimony = false
        showValidation = false
        witnessTestimonyState = .idle
        // Actually free the evidence: without this a later Verify (no re-Testify) still showed the
        // old snapshot/signature/result. All four are now reset so "Free" truly clears.
        witnessSnapshot = nil
        witnessSignature = ""
        witnessValid = false
        witnessResult = .notVerified
    }
}

/// Sentinel prefix marking a process-launch failure in the buffered stdout so the final-status
/// helper can distinguish "could not start" from a real (possibly empty) capture.
private let cageLaunchErrorMarker = "__syxx_launch_error__"

// B237: CaptureProcessBox removed. store() had no callers (the XPC flow never returned a Process),
// so terminate() was always a no-op. The privileged capture cannot be stopped from the GUI; only
// the live tail loop is cancellable.

/// Holds the privileged capture's final stdout once the process exits, so the tail loop can detect
/// completion without blocking on the buffered process.
private actor CaptureResultBox {
    private(set) var result: String?
    var finished: Bool { result != nil }
    func finish(_ msg: String) { result = msg }
}
