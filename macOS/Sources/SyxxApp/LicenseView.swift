import SwiftUI
import SyxxKit

struct LicenseView: View {
    @EnvironmentObject var model: AppModel
    @State private var billingPeriod: BillingPeriod = .yearly
    
    enum BillingPeriod: String, CaseIterable {
        case monthly = "Monthly"
        case yearly = "Yearly"
        
        var sku: String {
            switch self {
            case .monthly: return "syxx_mac_monthly_sub"
            case .yearly: return "syxx_mac_yearly_sub"
            }
        }
        
        var price: String {
            switch self {
            case .monthly: return "$3.95"
            case .yearly: return "$19.95"
            }
        }
        
        var period: String {
            switch self {
            case .monthly: return "/ month"
            case .yearly: return "/ year"
            }
        }
    }
    
    var body: some View {
        VStack(alignment: .leading, spacing: 24) {
                // Header
                VStack(alignment: .leading, spacing: 4) {
                    Text("ACCOUNT • LICENSING")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioBlue)
                    Text("License & Registration")
                        .font(.ioTitle1())
                        .foregroundColor(.ioText)
                    Text("All local diagnostics are free forever. Pro unlocks the server-backed checks.")
                        .font(.ioBody())
                        .foregroundColor(.ioTextSecondary)
                }
                
                // Tier cards — side by side
                HStack(alignment: .top, spacing: 16) {
                    // Lite tier
                    VStack(alignment: .leading, spacing: 14) {
                        HStack {
                            Text("Lite")
                                .font(.ioTitle2())
                                .foregroundColor(.ioText)
                            Spacer()
                            switch model.licenseState {
                            case .lite, .unknown:
                                StatusPill(label: "CURRENT", color: .ioWarningAmber)
                            default:
                                EmptyView()
                            }
                        }
                        Text("Free")
                            .font(.custom("Cinzel-Bold", size: 28))
                            .foregroundColor(.ioText)

                        Divider().background(Color.ioBorderSubtle)

                        VStack(alignment: .leading, spacing: 10) {
                            featureRow(label: "Prophet • resolver accuracy", ok: true)
                            featureRow(label: "Portal • LAN & route trace", ok: true)
                            featureRow(label: "Cage • live query capture", ok: true)
                            featureRow(label: "Glutton • DNS leak test", ok: false, color: .ioTextMuted)
                            featureRow(label: "Witness • environment trust", ok: false, color: .ioTextMuted)
                        }
                    }
                    .padding(22)
                    .frame(maxWidth: .infinity)
                    .background(Color.ioSurfaceDark)
                    .cornerRadius(12)
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))

                    // Pro tier
                    VStack(alignment: .leading, spacing: 14) {
                        HStack {
                            Text("Pro")
                                .font(.ioTitle2())
                                .foregroundColor(.ioText)
                            Spacer()
                            switch model.licenseState {
                            case .pro:
                                StatusPill(label: "CURRENT", color: .ioSuccessGreen)
                            case .trial:
                                StatusPill(label: "TRIAL", color: .ioBlue)
                            default:
                                StatusPill(label: "3-DAY FREE TRIAL", color: .ioBlue)
                            }
                        }
                        
                        // Billing period picker
                        Picker("Billing Period", selection: $billingPeriod) {
                            ForEach(BillingPeriod.allCases, id: \.self) { period in
                                Text(period.rawValue).tag(period)
                            }
                        }
                        .pickerStyle(.segmented)
                        
                        HStack(alignment: .firstTextBaseline, spacing: 4) {
                            Text(billingPeriod.price)
                                .font(.custom("Cinzel-Bold", size: 28))
                                .foregroundColor(.ioText)
                            Text(billingPeriod.period)
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                        }

                        Divider().background(Color.ioBorderSubtle)

                        VStack(alignment: .leading, spacing: 10) {
                            featureRow(label: "Everything in Lite, plus:", ok: true, color: .ioBlue)
                            featureRow(label: "Mirror • remote-vs-local asset comparison", ok: true, color: .ioBlue)
                            featureRow(label: "Remote static-file hash verification", ok: true, color: .ioBlue)
                            featureRow(label: "Syxx Warp • web structure matching", ok: true, color: .ioBlue)
                            featureRow(label: "Syxx Leak • DNS privacy logging", ok: true, color: .ioBlue)
                        }

                        Button(action: {
                            let base = SyxxConfig.dimensionBaseURL.absoluteString
                            let checkout = "\(base)/api/checkout?sku=\(billingPeriod.sku)"
                            if let url = URL(string: checkout) {
                                NSWorkspace.shared.open(url)
                            }
                        }) {
                            Text("Start Pro Trial")
                                .frame(maxWidth: .infinity)
                        }
                        .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                        .accessibilityLabel("Start Pro trial")
                        .accessibilityHint("Opens the checkout page to begin a three-day free trial of Pro features with \(billingPeriod.rawValue.lowercased()) billing")
                    }
                    .padding(22)
                    .frame(maxWidth: .infinity)
                    .background(
                        LinearGradient(
                            colors: [Color.ioSurfaceDark, Color.ioBlue.opacity(0.12)],
                            startPoint: .topLeading, endPoint: .bottomTrailing
                        )
                    )
                    .cornerRadius(12)
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBlue.opacity(0.4), lineWidth: 1))
                }
                
                // Activate License Key Box
                VStack(alignment: .leading, spacing: 14) {
                    Text("Activate a License Key")
                        .font(.ioTitle3())
                        .foregroundColor(.ioText)
                    
                    HStack(spacing: 8) {
                        switch model.licenseState {
                        case .unknown where model.isValidatingLicense:
                            Text("🔑 Checking license status…")
                        case .unknown:
                            Text("No license key stored")
                        case .lite:
                            Text("Lite version — all local diagnostics are free")
                        case .trial(let hours):
                            Text("⏳ Pro Trial — \(hours) hour\(hours == 1 ? "" : "s") remaining")
                        case .pro:
                            Text("🟢 Pro subscription active")
                        case .activationFailed(let msg):
                            Text("⚠️ \(msg)")
                        }
                        Spacer()
                        licenseStatusPill
                    }
                    .font(.ioMono(14))
                    .foregroundColor(.ioTextSecondary)
                    
                    Text("Enter, ye, your key of license, and allow the good fortunes of the realm to draw you in: (Your license key unlocks the app's Pro checks and the features anchored to the Dimension. It is validated with the licensing service and then kept on this machine, and it never travels alongside your DNS data.) By this separation, your network findings remain unbound from your identity.")
                        .font(.ioCaption())
                        .foregroundColor(.ioTextSecondary)
                        .fixedSize(horizontal: false, vertical: true)

                    HStack(spacing: 12) {
                        TextField("SYXX-XXXX-XXXX-XXXX-XXXX", text: $model.licenseKey)
                            .textFieldStyle(.roundedBorder)
                            .font(.ioMono(14))
                            .disabled(model.isValidatingLicense)
                            .accessibilityLabel("License key")
                            .accessibilityHint("Enter your Syxx license key in the format SYXX-XXXX-XXXX-XXXX-XXXX to activate Pro features")

                        Button(action: {
                            Task { await model.activateLicense() }
                        }) {
                            if model.isValidatingLicense {
                                HStack(spacing: 6) {
                                    ProgressView().controlSize(.small)
                                    Text("Validating…")
                                }
                            } else {
                                Text("Activate")
                            }
                        }
                        .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                        .disabled(model.licenseKey.trimmingCharacters(in: .whitespaces).isEmpty || model.isValidatingLicense)
                        .accessibilityLabel("Activate license key")
                        .accessibilityHint("Validates your license key with the Syxx licensing service and unlocks Pro features")
                    }
                    
                    // Machine ID and activation count removed — were hardcoded placeholders,
                    // not measured values. Will be restored when licensing service returns real data.
                }
                .padding(22)
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))

                // Privacy / Startup checks settings
                VStack(alignment: .leading, spacing: 14) {
                    Text("Privacy & Network Check Settings")
                        .font(.ioTitle3())
                        .foregroundColor(.ioText)

                    Toggle(isOn: $model.dimensionOptIn) {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Enable consolidated startup update and broadcast checks")
                                .font(.ioBody())
                                .foregroundColor(.ioText)
                            Text("Checks for software updates and critical security broadcasts once per launch. When enabled, this performs a single network request to syxx.app. No tracking parameters are transmitted.")
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                        }
                    }
                    .toggleStyle(.checkbox)
                    .accessibilityLabel("Enable startup update checks")
                    .accessibilityHint("Performs a single network request to syxx.app at launch to check for updates and security broadcasts, with no tracking parameters")
                }
                .padding(22)
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))
        }
        // ANTI-WOBBLE: fill the content area and top-anchor, identical to every other pane.
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    @ViewBuilder private var licenseStatusPill: some View {
        switch model.licenseState {
        case .unknown where model.isValidatingLicense:
            StatusPill(label: "CHECKING", color: .ioTextSecondary)
        case .unknown:
            StatusPill(label: "NOT CHECKED", color: .ioTextSecondary)
        case .lite, .activationFailed:
            StatusPill(label: "LITE VERSION", color: .ioWarningAmber)
        case .trial:
            StatusPill(label: "TRIAL", color: .ioBlue)
        case .pro:
            StatusPill(label: "PRO", color: .ioSuccessGreen)
        }
    }

    private func featureRow(label: String, ok: Bool, color: Color = .ioSuccessGreen) -> some View {
        HStack(spacing: 10) {
            Text(ok ? "✓" : "✗")
                .foregroundColor(color)
                .font(.ioBody(.bold))
            Text(label)
                .font(.ioBody())
                .foregroundColor(.ioText)
        }
    }
}

#if DEBUG
#Preview {
    LicenseView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .license))
        .syxxPreviewCanvas()
}
#endif
