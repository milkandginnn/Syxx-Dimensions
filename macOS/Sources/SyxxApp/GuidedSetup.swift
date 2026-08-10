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
        // Note: .frame(minWidth: 1050, minHeight: 613) removed — FirstRunWindowConfigurator
        // owns all window sizing per state, locking min==max to prevent SwiftUI from
        // growing the window behind its back.
    }
}

private struct AgreementGate: View {
    @ObservedObject var coordinator: FirstRunCoordinator

    var body: some View {
        // Mockup: 600px card, rgba(28,17,52,.85) + glass, border rgba(160,140,255,.24),
        // radius 18, shadow 0 40px 100px rgba(0,0,0,.6). No checkbox — "Decline" text
        // link + "Agree & Continue →" gradient button.
        VStack(spacing: 0) {
            VStack(spacing: 5) {
                Text("⚖")
                    .font(.system(size: 30))
                    .padding(.bottom, 6)
                Text("End-User License Agreement")
                    .font(.custom("Cinzel-SemiBold", size: 22))
                    .foregroundStyle(Color.ioText)
                Text("Please read these terms before continuing.")
                    .font(.custom("EBGaramond-Italic", size: 14))
                    .foregroundStyle(Color.ioTextMuted)
                    .padding(.top, 4)
            }
            .padding(.vertical, 14)
            .padding(.horizontal, 30)
            .frame(maxWidth: .infinity)

            Divider().overlay(Color(white: 1, opacity: 0.06))

            PurpleScrollView(geometry: .compact) {
                Text(coordinator.agreementText)
                    .font(.custom("EBGaramond-Regular", size: 14.5))
                    .foregroundStyle(Color.ioTextSecondary)
                    .lineSpacing(3)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 30)
                    .padding(.vertical, 20)
            }
            .frame(maxHeight: 320)

            Divider().overlay(Color(white: 1, opacity: 0.06))

            HStack {
                Button("Decline") { NSApplication.shared.terminate(nil) }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.ioTextMuted)
                    .keyboardShortcut(.cancelAction)
                    .accessibilityHint("Quits Syxx without saving acceptance")

                Spacer()

                Button("Agree & Continue →") {
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
        .background(Color(red: 28/255, green: 17/255, blue: 52/255).opacity(0.85))
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color(red: 160/255, green: 140/255, blue: 255/255).opacity(0.24), lineWidth: 1))
        .shadow(color: .black.opacity(0.6), radius: 50, y: 24)
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

                    sectionLabel("Appearance")

                    VStack(alignment: .leading, spacing: 6) {
                        Text("Choose how Syxx looks. System follows your macOS appearance setting.")
                            .font(.ioSmall())
                            .foregroundStyle(Color.ioTextSecondary)

                        Picker("Theme", selection: Binding(
                            get: { SyxxThemeMode(rawValueOrSystem: selection.themeMode) },
                            set: { selection.themeMode = $0.rawValue }
                        )) {
                            ForEach(SyxxThemeMode.allCases, id: \.self) { mode in
                                Text(mode.displayName).tag(mode)
                            }
                        }
                        .pickerStyle(.segmented)
                        .accessibilityLabel("Appearance theme")
                        .accessibilityHint("Choose between System, Dark, or Light appearance")
                    }

                    sectionLabel("Step 2 · Confirm your defaults")

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
        .frame(width: 640, height: 585)
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
        // Mockup: Story selected = violet (#7c5cff), General selected = teal (#2dd4bf).
        let isSelected = selection.experienceMode == mode
        let accent: Color = mode == "story" ? .ioBlue : .ioSuccessGreen
        return Button {
            selection.experienceMode = mode
        } label: {
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(title)
                        .font(.ioTitle3())
                        .foregroundStyle(Color.ioText)
                    Spacer()
                    if isSelected {
                        Image(systemName: "checkmark.circle.fill")
                            .foregroundStyle(accent)
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
                        isSelected ? accent : Color.ioBorderSubtle,
                        lineWidth: isSelected ? 2 : 1
                    )
            )
            .clipShape(RoundedRectangle(cornerRadius: 12))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
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
