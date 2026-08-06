import SwiftUI
import SyxxKit
import SyxxTelemetry

/// G-4 settings pane (macOS). Backed by the typed `SyxxSettings` store — no scattered UserDefaults.
///
/// HONESTY throughout: a toggle records intent; it never asserts a capability is in effect. The
/// Encrypted-DNS switch actually calls `EncryptedDNSConfigurator` and shows the REAL result
/// (including the "needs a signed+entitled build" state on the current unsigned build), never a fake
/// "on". Notifications are labelled as a stored preference because delivery is not built yet. Data
/// removal really erases `~/.syxx`.
struct SettingsView: View {
    @State private var settings = SyxxSettingsStore.load()
    @State private var saveNote = ""
    // E116: initialize to "Checking…" so the pane never asserts an unmeasured state as fact.
    @State private var encryptState = "Checking…"
    @State private var encryptBusy = false
    @State private var dataNote = ""

    private let resolvers = EncryptedDNSConfig.catalog

    var body: some View {
        PurpleScrollView(geometry: .fullPanel) {
            VStack(alignment: .leading, spacing: 24) {
                header

                // MARK: Experience Mode (Guided Setup)
                card(title: "EXPERIENCE MODE · GUIDED SETUP", subtitle: "Choose between Storybook narrative or General technical interface.") {
                    Picker("Interface Mode", selection: $settings.experienceMode) {
                        Text("Story Mode (Wardens & Lore)").tag("story")
                        Text("General Mode (Technical English)").tag("general")
                    }
                    .pickerStyle(.segmented)
                    .onChange(of: settings.experienceMode) { _, _ in persist() }
                    .accessibilityLabel("Interface experience mode")
                    .accessibilityHint("Choose between Storybook narrative with wardens and lore, or General technical English")

                    Text(settings.experienceMode == "story" ? "Story Mode renders the app using Warden lore, Futhark runes, and narrative titles." : "General Mode renders the app using plain, direct technical English.")
                        .font(.caption2)
                        .foregroundStyle(Color.ioTextSecondary)
                }

                // MARK: Encrypted DNS
                card(title: "ENCRYPTED DNS", subtitle: "Send every DNS query over an encrypted transport (DoH/DoT). Configures the system directly — no profile to install.") {
                    Toggle("Encrypt all DNS queries", isOn: $settings.encryptedDNSEnabled)
                        .onChange(of: settings.encryptedDNSEnabled) { oldValue, on in
                            // D097: do NOT persist before the operation — persist only after
                            // confirmation. The prior value is passed so applyEncryption can
                            // revert on failure.
                            Task { await applyEncryption(on, priorValue: oldValue) }
                        }
                        .accessibilityLabel("Encrypt all DNS queries")
                        .accessibilityHint("Configures your system to send all DNS queries over encrypted DoH or DoT transport")
                    Picker("Resolver", selection: $settings.encryptedDNSResolverID) {
                        ForEach(resolvers) { r in Text(r.displayName).tag(r.id) }
                    }
                    .onChange(of: settings.encryptedDNSResolverID) { _, _ in persist() }
                    .accessibilityLabel("Encrypted DNS resolver")
                    .accessibilityHint("Select which encrypted DNS provider to use when encryption is enabled")
                    Picker("Transport", selection: $settings.encryptedDNSTransport) {
                        Text("DoH (HTTPS)").tag(EncryptedDNSTransport.doh)
                        Text("DoT (TLS)").tag(EncryptedDNSTransport.dot)
                    }
                    .onChange(of: settings.encryptedDNSTransport) { _, _ in persist() }
                    .accessibilityLabel("Encrypted DNS transport")
                    .accessibilityHint("Choose DNS-over-HTTPS or DNS-over-TLS as the encrypted transport protocol")
                    // E116: render encryptBusy as a spinner so the user sees the check is in-flight.
                    HStack(spacing: 6) {
                        if encryptBusy { ProgressView().controlSize(.small) }
                        Text(encryptState)
                            .font(.caption)
                            .foregroundStyle(Color.ioTextSecondary)
                    }
                    Text("Syxx keeps its own audit lookups in cleartext so it can still detect tampering; only your system's ordinary DNS is encrypted here.")
                        .font(.caption2)
                        .foregroundStyle(Color.ioTextSecondary)
                }

                // MARK: Scans
                card(title: "SCANS", subtitle: "Which resolver the free checks query.") {
                    Picker("Scan resolver", selection: Binding(
                        get: { settings.scanResolverID ?? "" },
                        set: { settings.scanResolverID = $0.isEmpty ? nil : $0; persist() })) {
                        Text("System resolver").tag("")
                        ForEach(resolvers) { r in Text(r.displayName).tag(r.id) }
                    }
                    .accessibilityLabel("Scan resolver")
                    .accessibilityHint("Select which resolver the free diagnostic checks should query, or use the system default")
                }

                // MARK: Privacy
                card(title: "PRIVACY", subtitle: "What leaves your machine.") {
                    Toggle("Check for updates automatically", isOn: $settings.updateCheckEnabled)
                        .onChange(of: settings.updateCheckEnabled) { _, _ in persist() }
                        .accessibilityLabel("Check for updates automatically")
                        .accessibilityHint("Syxx contacts syxx.app once per launch to check for updates and news")

                    Toggle("Send crash reports", isOn: $settings.crashReportingEnabled)
                        .onChange(of: settings.crashReportingEnabled) { _, isOn in
                            persist()
                            if isOn {
                                SentryBootstrap.setup(ifAllowed: true)
                            } else {
                                SyxxDiagnostics.uninstall()
                            }
                        }
                        .accessibilityLabel("Send crash reports")
                        .accessibilityHint("If Syxx crashes, a diagnostic report will be sent to help fix bugs — never your identity or DNS data")

                    Toggle("Send anonymous usage statistics", isOn: $settings.telemetryEnabled)
                        .onChange(of: settings.telemetryEnabled) { _, isOn in
                            persist()
                            if isOn {
                                OTelBootstrap.setup(serviceName: "syxx-gui-macos", ifAllowed: true)
                            } else {
                                OTelBootstrap.discardOnConsentWithdrawal()
                            }
                        }
                        .accessibilityLabel("Send anonymous usage statistics")
                        .accessibilityHint("Sends only your OS and app version to syxx.app, never your identity or DNS data")

                    Link("Privacy Policy", destination: URL(string: "https://syxx.app/privacy")!)
                        .font(.caption)
                        .foregroundColor(.ioBlue)
                        .accessibilityLabel("Open Syxx Privacy Policy")
                        .accessibilityHint("Opens the Syxx privacy policy at syxx.app in your default browser")

                    Text("Syxx may contact syxx.app once per launch to check for news and updates. The ping carries only your OS and app version — never your identity, resolvers, or the names you look up.")
                        .font(.caption2)
                        .foregroundStyle(Color.ioTextSecondary)
                }

                // MARK: Notifications
                card(title: "NOTIFICATIONS", subtitle: "Alerts for network changes.") {
                    Toggle("Notify me when the network's trust changes", isOn: $settings.notificationsEnabled)
                        .onChange(of: settings.notificationsEnabled) { _, _ in persist() }
                        .accessibilityLabel("Notify on network trust changes")
                        .accessibilityHint("Alerts you when the trustworthiness of your network changes, once continuous monitoring is available")
                    Text("Preference saved. Delivery arrives with continuous monitoring; this switch has no effect until then.")
                        .font(.caption2)
                        .foregroundStyle(Color.ioWarningAmber)
                }

                // MARK: Data
                card(title: "DATA", subtitle: "Everything Syxx stores lives in ~/.syxx and never leaves your machine.") {
                    Button(role: .destructive) {
                        Sentinel.forget(); HomeBaseline.forget(); AnswerLedger().forget()
                        dataNote = "Erased: Sentinel baseline, home baseline, and the passive-DNS ledger (~/.syxx)."
                    } label: { Text("Remove stored data") }
                    .accessibilityLabel("Remove all stored data")
                    .accessibilityHint("Erases the Sentinel baseline, home baseline, and passive DNS ledger from your machine")
                    if !dataNote.isEmpty {
                        Text(dataNote).font(.caption).foregroundStyle(Color.ioTextSecondary)
                    }
                }

                if !saveNote.isEmpty {
                    Text(saveNote).font(.caption).foregroundStyle(Color.ioTextSecondary)
                }
            }
        }
        .task {
            #if DEBUG
            guard !ProcessInfo.processInfo.isSyxxXcodePreview else { return }
            #endif
            await refreshEncryptionState()
        }
    }

    private var header: some View {
        // Mockup §3.9: "THE SANCTUM" eyebrow (10.5px, .26em, uppercase, violet)
        // + "Settings" Cinzel title in Story mode.
        // "PREFERENCES" + "Settings" in General mode.
        VStack(alignment: .leading, spacing: 6) {
            Text("THE SANCTUM")
                .font(.system(size: 10.5, weight: .bold))
                .tracking(2.8)
                .foregroundStyle(Color(red: 138/255, green: 110/255, blue: 240/255)) // #8a6ef0
            Text("Settings")
                .font(.custom("Cinzel-Bold", size: 28))
                .foregroundStyle(Color.ioText)
        }
    }

    @ViewBuilder
    private func card<Content: View>(title: String, subtitle: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title).font(.caption).bold().foregroundStyle(Color.ioTextSecondary)
            Text(subtitle).font(.footnote).foregroundStyle(Color.ioTextSecondary)
            content()
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.ioSurfaceDark))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))
    }

    private func persist() {
        saveNote = SyxxSettingsStore.save(settings) ? "Saved." : "Could not save settings."
    }

    /// D097: apply the encryption change, reverting the persisted toggle on failure.
    /// The toggle is persisted ONLY after a confirmed .enabled/.disabled result — never before.
    private func applyEncryption(_ on: Bool, priorValue: Bool) async {
        encryptBusy = true
        let result: EncryptedDNSApplyResult = on
            ? await EncryptedDNSConfigurator.enable(resolverID: settings.encryptedDNSResolverID,
                                                    transport: settings.encryptedDNSTransport)
            : await EncryptedDNSConfigurator.disable()
        encryptState = describe(result, turningOn: on)
        // D097: persist only on confirmed state changes; revert on failure.
        switch result {
        case .enabled, .disabled:
            persist()
        case .entitlementRequired, .failed, .inconclusive:
            // Revert the toggle to the prior value and re-persist so the stored setting
            // matches the actual system state — never a fabricated "on".
            settings.encryptedDNSEnabled = priorValue
            persist()
        }
        encryptBusy = false
    }

    private func refreshEncryptionState() async {
        encryptState = describe(await EncryptedDNSConfigurator.currentState(), turningOn: settings.encryptedDNSEnabled)
    }

    /// Honest rendering of the tri-state — the whole point of the feature.
    /// D083: split the disable path so `.failed` while turning off never asserts "Encryption is off."
    /// when the removal itself failed. Also correct `.entitlementRequired` — nothing was saved.
    private func describe(_ r: EncryptedDNSApplyResult, turningOn: Bool) -> String {
        switch r {
        case .enabled:
            return "Encrypted: every system DNS query is going over \(settings.encryptedDNSTransport == .doh ? "DoH" : "DoT")."
        case .disabled:
            return "Encryption is off."
        case .entitlementRequired:
            return "This build cannot change system DNS: it needs to be signed with Apple's DNS-Settings entitlement. The configuration was rejected and is not stored."
        case .failed(let reason):
            return turningOn
                ? "Could not enable: \(reason). Your DNS was left unchanged."
                : "Could not turn encryption off: \(reason). Your system DNS settings may still be Syxx's — verify in System Settings ▸ Network ▸ DNS."
        case .inconclusive(let reason):
            return "Could not confirm encryption state: \(reason). Treated as NOT protected until confirmed."
        }
    }
}

#if DEBUG
#Preview {
    SettingsView()
        .syxxPreviewCanvas()
}
#endif
