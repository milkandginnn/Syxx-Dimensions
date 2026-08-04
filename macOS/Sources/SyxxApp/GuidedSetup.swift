import AppKit
import SwiftUI
import SyxxKit

struct GuidedSetup: View {
    @ObservedObject var coordinator: FirstRunCoordinator

    var body: some View {
        ZStack {
            // IMPLEMENTATION_GUIDE §2.1a backdrop: near-opaque canvas + radial violet glow.
            Color(red: 11/255, green: 5/255, blue: 24/255).opacity(0.94)
                .ignoresSafeArea()
            RadialGradient(
                colors: [
                    Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.18),
                    Color(red: 11/255, green: 5/255, blue: 24/255).opacity(0)
                ],
                center: UnitPoint(x: 0.50, y: 0.0),
                startRadius: 0,
                endRadius: 720
            )
            .ignoresSafeArea()

            switch coordinator.state {
            case .loading:
                ProgressView()
                    .controlSize(.large)
                    .accessibilityLabel("Loading setup")
            case .agreementRequired:
                AgreementGate(coordinator: coordinator)
            case .setupRequired:
                if coordinator.isConfiguring {
                    ProgressView()
                        .controlSize(.large)
                        .accessibilityLabel("Loading setup")
                } else if let message = coordinator.configurationFailureMessage {
                    LifecycleFailureView(
                        message: message,
                        retry: coordinator.clearConfigurationFailure
                    )
                } else {
                    SetupChoices(coordinator: coordinator)
                }
            case .persistenceFailure:
                LifecycleFailureView(
                    message: coordinator.failureContext == .agreement
                        ? "Syxx could not save your agreement acceptance. Nothing has been accepted."
                        : "Syxx could not save your setup. Your choices have not been applied.",
                    retry: coordinator.retry
                )
            case .stateUnknown:
                LifecycleFailureView(
                    message: "Syxx could not verify the saved setup. Syxx will remain closed until the settings can be read.",
                    retry: coordinator.retry
                )
            case .ready:
                EmptyView()
            }
        }
        .frame(minWidth: 1050, minHeight: 613)
    }
}

private struct AgreementGate: View {
    @ObservedObject var coordinator: FirstRunCoordinator
    @State private var accepted = false
    @State private var showAcceptanceAlert = false

    var body: some View {
        VStack(spacing: 0) {
            VStack(spacing: 5) {
                Text("⚖")
                    .font(.system(size: 30))
                Text("End-User License Agreement")
                    .font(.ioTitle2())
                    .foregroundStyle(Color.ioText)
                Text("Please read these terms before continuing.")
                    .font(.ioLore(14))
                    .foregroundStyle(Color.ioTextSecondary)
            }
            .padding(.vertical, 14)
            .frame(maxWidth: .infinity)

            Divider().overlay(Color.ioBorderSubtle)

            PurpleScrollView(geometry: .compact) {
                Text(coordinator.agreementText)
                    .font(.ioBody())
                    .foregroundStyle(Color.ioText)
                    .lineSpacing(3)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 30)
                    .padding(.vertical, 20)
            }
            .frame(height: 300)

            Toggle("I have read and agree to the terms above.", isOn: $accepted)
                .toggleStyle(.checkbox)
                .font(.ioBody())
                .foregroundStyle(Color.ioText)
                .padding(.horizontal, 30)
                .padding(.bottom, 12)
                .accessibilityHint("Required before Syxx can continue")

            Divider().overlay(Color.ioBorderSubtle)

            HStack {
                Button("Decline") { NSApplication.shared.terminate(nil) }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.ioTextSecondary)
                    .keyboardShortcut(.cancelAction)
                    .accessibilityHint("Quits Syxx without saving acceptance")

                Spacer()

                Button("Agree & Continue →") {
                    guard accepted else {
                        showAcceptanceAlert = true
                        return
                    }
                    _ = coordinator.acceptAgreement()
                }
                .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                .keyboardShortcut(.defaultAction)
                .accessibilityHint("Saves acceptance of this agreement and continues to guided setup")
            }
            .padding(.horizontal, 30)
            .padding(.vertical, 16)
        }
        .frame(width: 600)
        .background(Color.ioSurfaceDark.opacity(0.96))
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.ioBorderSubtle))
        .shadow(color: .black.opacity(0.6), radius: 50, y: 24)
        .alert(
            "End-User License Agreement",
            isPresented: $showAcceptanceAlert
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text("You must accept the EULA in order to use Syxx. You have not yet accepted the EULA.")
        }
    }
}

private struct SetupChoices: View {
    @ObservedObject var coordinator: FirstRunCoordinator
    /// Visible screen is initialized from the single source of truth.
    @State private var selection = GuidedSetupSelection.safeDefaults
    @State private var isSubmitting = false

    private let resolvers = EncryptedDNSConfig.catalog

    var body: some View {
        VStack(spacing: 0) {
            VStack(spacing: 5) {
                Image("Syxx-logo")
                    .resizable()
                    .scaledToFit()
                    .frame(width: 42, height: 42)
                    .accessibilityHidden(true)
                Text("Welcome to Syxx")
                    .font(.ioTitle1())
                    .foregroundStyle(Color.ioText)
                Text("A first-run setup takes about a minute. You can change anything later.")
                    .font(.ioLore(15))
                    .foregroundStyle(Color.ioTextSecondary)
            }
            .padding(.vertical, 13)
            .frame(maxWidth: .infinity)

            Divider().overlay(Color.ioBorderSubtle)

            PurpleScrollView(geometry: .fullPanel) {
                VStack(alignment: .leading, spacing: 14) {
                    sectionLabel("Step 1 · Choose your experience")

                    HStack(spacing: 12) {
                        experienceCard(
                            mode: "story",
                            title: "Story Mode",
                            description: "Six wardens, lore, and quest framing guide every check."
                        )
                        experienceCard(
                            mode: "general",
                            title: "General Mode",
                            description: "Standard technical English, with plain labels and no storybook framing."
                        )
                    }

                    sectionLabel("Step 2 · Confirm your defaults")

                    HStack(alignment: .top, spacing: 30) {
                        VStack(spacing: 0) {
                            Picker("Resolver", selection: $selection.resolverID) {
                                ForEach(resolvers) { resolver in
                                    Text(resolver.displayName).tag(resolver.id)
                                }
                            }
                            .accessibilityHint("Where lookups are sent")
                            settingDivider

                            Picker("Encrypted transport", selection: $selection.transport) {
                                Text("DoH (HTTPS)").tag(EncryptedDNSTransport.doh)
                                Text("DoT (TLS)").tag(EncryptedDNSTransport.dot)
                            }
                            settingDivider

                            setupToggle(
                                "Encrypt all DNS queries",
                                detail: "Recommended when DNS is cleartext",
                                value: $selection.encryptedDNSEnabled
                            )
                            setupToggle(
                                "Notify me on trust changes",
                                detail: "Alerts when the network's trust changes",
                                value: $selection.notificationsEnabled
                            )
                        }
                        .frame(maxWidth: .infinity)

                        VStack(spacing: 0) {
                            setupToggle(
                                "Check for updates automatically",
                                detail: "Contacts syxx.app after setup to check for updates and news",
                                value: $selection.updateCheckEnabled
                            )
                            setupToggle(
                                "Send crash reports",
                                detail: "Shares diagnostics after setup when enabled",
                                value: $selection.crashReportingEnabled
                            )
                            setupToggle(
                                "Send anonymous usage statistics",
                                detail: "Shares app operation telemetry after setup when enabled",
                                value: $selection.telemetryEnabled
                            )
                        }
                        .frame(maxWidth: .infinity)
                    }
                }
                .padding(.horizontal, 30)
                .padding(.vertical, 14)
            }

            Divider().overlay(Color.ioBorderSubtle)

            HStack {
                Button("Skip: use defaults") {
                    guard !isSubmitting else { return }
                    isSubmitting = true
                    // Skip persists the same safeDefaults the screen started with.
                    // No encryption apply, no diagnostic scan.
                    _ = coordinator.skipWithDefaults()
                    isSubmitting = false
                }
                .buttonStyle(.plain)
                .foregroundStyle(Color.ioTextSecondary)
                .disabled(isSubmitting || coordinator.isConfiguring)

                Spacer()

                Button("Begin →") {
                    guard !isSubmitting else { return }
                    isSubmitting = true
                    #if DEBUG
                    if ProcessInfo.processInfo.isSyxxXcodePreview {
                        isSubmitting = false
                        return
                    }
                    #endif
                    Task { @MainActor in
                        // Configuration (when requested) completes before ready.
                        // completeSetup / ready only after config + exact readback succeed.
                        _ = await coordinator.beginSetup(selection)
                        isSubmitting = false
                    }
                }
                .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                .keyboardShortcut(.defaultAction)
                .disabled(isSubmitting || coordinator.isConfiguring)
                .accessibilityHint("Saves these choices and opens What's New")
            }
            .padding(.horizontal, 30)
            .padding(.vertical, 12)
        }
        .frame(width: 960, height: 585)
        .background(Color.ioSurfaceDark.opacity(0.96))
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.ioBorderSubtle))
        .shadow(color: .black.opacity(0.6), radius: 50, y: 24)
        .onAppear {
            // Re-assert single source of truth if the view is recreated.
            selection = GuidedSetupSelection.safeDefaults
        }
    }

    private func sectionLabel(_ text: String) -> some View {
        Text(text.uppercased())
            .font(.ioMicro(.bold))
            .tracking(2.2)
            .foregroundStyle(Color.ioBlue)
    }

    private func experienceCard(mode: String, title: String, description: String) -> some View {
        Button {
            selection.experienceMode = mode
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(title)
                        .font(.ioTitle3())
                        .foregroundStyle(Color.ioText)
                    Spacer()
                    if selection.experienceMode == mode {
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundStyle(Color.ioBlue)
                    }
                }
                Text(description)
                    .font(.ioSmall())
                    .foregroundStyle(Color.ioTextSecondary)
                    .multilineTextAlignment(.leading)
            }
            .padding(14)
            .frame(maxWidth: .infinity, minHeight: 82, alignment: .leading)
            .background(Color.ioBackgroundDark.opacity(0.7))
            .overlay(
                RoundedRectangle(cornerRadius: 12)
                    .stroke(
                        selection.experienceMode == mode ? Color.ioBlue : Color.ioBorderSubtle,
                        lineWidth: selection.experienceMode == mode ? 2 : 1
                    )
            )
            .clipShape(RoundedRectangle(cornerRadius: 12))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(selection.experienceMode == mode ? .isSelected : [])
    }

    private func setupToggle(
        _ title: String,
        detail: String,
        value: Binding<Bool>
    ) -> some View {
        VStack(spacing: 0) {
            Toggle(isOn: value) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.ioBody())
                    Text(detail).font(.caption2).foregroundStyle(Color.ioTextSecondary)
                }
            }
            .toggleStyle(.switch)
            .padding(.vertical, 10)
            settingDivider
        }
    }

    private var settingDivider: some View {
        Divider().overlay(Color.ioBorderSubtle)
    }
}

#if DEBUG
#Preview {
    GuidedSetup(coordinator: SyxxPreviewSupport.firstRunCoordinator())
        .syxxPreviewCanvas()
}
#endif

private struct LifecycleFailureView: View {
    let message: String
    let retry: () -> Void

    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 32))
                .foregroundStyle(Color.ioWarningAmber)
            Text(message)
                .font(.ioBody())
                .foregroundStyle(Color.ioText)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 460)
            HStack(spacing: 12) {
                Button("Quit Syxx") { NSApplication.shared.terminate(nil) }
                    .buttonStyle(IoOutlineButtonStyle())
                Button("Try Again", action: retry)
                    .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(30)
        .background(Color.ioSurfaceDark)
        .clipShape(RoundedRectangle(cornerRadius: 16))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.ioBorderSubtle))
    }
}
