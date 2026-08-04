import SwiftUI
import SyxxKit

// Opened from Syxx menu → "Check for Updates". Lives in its own NSPanel.
struct UpdateView: View {
    @Environment(\.dismiss) private var dismiss
    @StateObject private var updateService = UpdateService.shared
    @State private var earlyAccess = false
    // E111: removed view-local @State lastChecked — now on UpdateService so it persists
    // across panel open/close cycles.

    var body: some View {
        VStack(spacing: 0) {
            // ── Content ──────────────────────────────────────────────────────
            VStack(alignment: .leading, spacing: 20) {
                // Logo + app name row
                HStack(spacing: 14) {
                    LogoView(size: 56)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Syxx")
                            .font(.custom("Cinzel-Bold", size: 20))
                            .foregroundColor(.ioText)
                        Text("DNS Investigation Suite")
                            .font(.ioSmall())
                            .foregroundColor(.ioTextSecondary)
                    }
                }

                // Version / last-checked info grid
                infoRow("Version",
                        value: "\(SyxxConfig.version)  ·  Build \(buildString)",
                        mono: true)
                // E111: use service's lastChecked which persists across panel cycles.
                // Render "unknown" instead of "Never" when no check has occurred.
                if let lc = updateService.lastChecked {
                    infoRow("Last checked",
                            value: lc.formatted(date: .long, time: .shortened),
                            mono: false)
                } else {
                    infoRow("Last checked", value: "unknown", mono: false)
                }

                // Status pill
                statusBanner

                // Early access toggle
                // E113: wire to settings store so the preference persists, but state clearly
                // that beta delivery is not yet implemented.
                HStack(spacing: 12) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("Early access")
                            .font(.ioBody(.semibold))
                            .foregroundColor(.ioText)
                        Text("Receive beta builds before public release. Betas may be unstable.")
                            .font(.ioSmall())
                            .foregroundColor(.ioTextSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                        Text("Preference saved. Beta delivery arrives with the next update cycle; this switch has no effect until then.")
                            .font(.ioSmall())
                            .foregroundColor(.ioWarningAmber)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    Spacer()
                    Toggle("", isOn: $earlyAccess)
                        .labelsHidden()
                        .tint(.ioBlue)
                        .onChange(of: earlyAccess) { _, newValue in
                            // Persist the preference so it survives panel close/open.
                            UserDefaults.standard.set(newValue, forKey: "syxx.earlyAccessOptIn")
                        }
                        .onAppear {
                            earlyAccess = UserDefaults.standard.bool(forKey: "syxx.earlyAccessOptIn")
                        }
                }
            }
            .padding(.horizontal, 28)
            .padding(.top, 28)

            Spacer(minLength: 20)

            // ── Button bar ───────────────────────────────────────────────────
            Divider().background(Color.ioBorderSubtle)
            HStack(spacing: 12) {
                Button("Done") { dismiss() }
                    .buttonStyle(IoOutlineButtonStyle())
                    .keyboardShortcut(.cancelAction)

                // E112: button action and label switch based on update state.
                // Check → Download & Install → (installation handled by service).
                Button(action: primaryButtonAction) {
                    if case .checking = updateService.state {
                        HStack(spacing: 6) {
                            ProgressView().controlSize(.small)
                            Text("Checking…")
                        }
                    } else if case .downloading = updateService.state {
                        HStack(spacing: 6) {
                            ProgressView().controlSize(.small)
                            Text("Downloading…")
                        }
                    } else if case .installing = updateService.state {
                        HStack(spacing: 6) {
                            ProgressView().controlSize(.small)
                            Text("Installing…")
                        }
                    } else if case .available = updateService.state {
                        Text("Download & Install")
                    } else {
                        Text("Check for Updates")
                    }
                }
                .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                .disabled(isCheckDisabled)
                .keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 16)
        }
        .frame(width: 480)
        .background(Color.ioSurfaceDark)
    }

    // MARK: - Sub-views

    @ViewBuilder private var statusBanner: some View {
        HStack(spacing: 10) {
            Circle()
                .fill(statusColor)
                .frame(width: 8, height: 8)
            Text(statusText)
                .font(.ioBody(.medium))
                .foregroundColor(statusColor)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(statusColor.opacity(0.08))
        .cornerRadius(8)
        .overlay(RoundedRectangle(cornerRadius: 8).stroke(statusColor.opacity(0.25), lineWidth: 1))
    }

    private func infoRow(_ label: String, value: String, mono: Bool) -> some View {
        HStack {
            Text(label)
                .font(.ioBody())
                .foregroundColor(.ioTextSecondary)
            Spacer()
            Text(value)
                .font(mono ? .ioMonoBold(13) : .ioBody(.semibold))
                .foregroundColor(.ioText)
        }
    }

    // MARK: - State helpers

    private var statusColor: Color {
        switch updateService.state {
        case .idle:              return .ioTextSecondary
        case .checking:          return .ioTextSecondary
        case .upToDate:          return .ioSuccessGreen
        case .available:         return .ioWarningAmber
        case .downloading:       return .ioBlue
        case .installing:        return .ioBlue
        case .failed:            return .ioCriticalRed
        }
    }

    private var statusText: String {
        switch updateService.state {
        case .idle:              return "Not yet checked."
        case .checking:          return "Checking for updates…"
        case .upToDate:          return "Syxx is up to date."
        case .available(let v, _): return "Update available: version \(v)"
        case .downloading(let p): return "Downloading update… \(Int(p * 100))%"
        case .installing:        return "Installing update…"
        case .failed(let msg):   return "Update failed: \(msg)"
        }
    }

    // E033: never fabricate a build number from today's date — display "unknown" when no
    // compiled build ID exists, matching Windows behavior.
    private var buildString: String {
        if let b = Bundle.main.infoDictionary?["CFBundleVersion"] as? String { return b }
        return "unknown"
    }

    private var isCheckDisabled: Bool {
        switch updateService.state {
        case .checking, .downloading, .installing:
            return true
        default:
            return false
        }
    }

    // MARK: - Update check

    /// E112: primary button action switches based on update state.
    private func primaryButtonAction() {
        switch updateService.state {
        case .available:
            downloadAndInstall()
        default:
            checkForUpdates()
        }
    }

    private func checkForUpdates() {
        Task {
            await updateService.checkForUpdates()
            await MainActor.run {
                // E111: stamp lastChecked on the service (not view-local state) so it persists.
                // B089: only stamp when the check actually succeeded.
                if case .failed = updateService.state {
                    // leave lastChecked unchanged — the check did not produce a result
                } else {
                    updateService.lastChecked = Date()
                }
            }
        }
    }

    private func downloadAndInstall() {
        Task {
            await updateService.downloadAndInstall()
        }
    }
}

#if DEBUG
#Preview {
    UpdateView()
        .syxxPreviewPanel()
}
#endif
