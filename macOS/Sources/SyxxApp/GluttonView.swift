import SwiftUI
import SyxxKit

struct GluttonView: View {
    @EnvironmentObject var model: AppModel

    private var report: NetworkStateReport? { model.portalReport }

    var body: some View {
        PurpleScrollView(geometry: .fullPanel) {
        VStack(alignment: .leading, spacing: 24) {
                HStack(alignment: .top) {
                    ModuleHeader(rune: "\u{16A6}", eyebrow: "THE DEVOURER \u{00B7} DNS LEAK TEST", name: "Glutton", accent: .ioGluttonPink) {
                        Text("Alas, the Glutton hungered for encrypted packets and demanded that any leak in the sealed DNS traffic be found and named, for a single crack was enough to render him famished.")
                            .font(.ioLore(16))
                            .foregroundColor(.ioTextMuted)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: 560)
                    }

                    Spacer()

                    Button(action: {
                        model.selection = .glutton
                        model.run()
                    }) {
                        if model.runningTool == .glutton {
                            ProgressView().controlSize(.small)
                        } else {
                            Text("Check for Leaks")
                        }
                    }
                    .buttonStyle(IoFilledButtonStyle())
                    .disabled(model.isRunning)
                    .accessibilityLabel("Check for DNS leaks")
                    .accessibilityHint("Probes whether unencrypted DNS queries leak outside your VPN tunnel")
                }

                Text("Glutton probes whether unencrypted DNS queries leak outside encrypted VPN tunnels (syxx leak), catching when queries bypass configured tunnels and making them visible. This test is only meaningful if you're connected to the internet via a VPN or using DNS encryption profile. It will be skipped if no VPN or DNS encryption is detected.")
                    .font(.ioBody())
                    .foregroundColor(.ioTextSecondary)
                    .lineLimit(nil)

                // Status Cards Grid
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 16) {
                    // Feature Status — from model.licenseState (not a stale @State copy)
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Feature Status:")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioTextSecondary)
                            .textCase(.uppercase)

                        // E117: three states (unknown / locked / unlocked) rendered distinctly —
                        // unknown shows a neutral spinner, never a red ✗ for a check that hasn't completed.
                        let isUnknown = model.licenseState == .unknown
                        let isUnlocked = model.isPro
                        let message: String = {
                            if isUnknown { return "Loading…" }
                            if !model.licenseMessage.isEmpty { return model.licenseMessage }
                            return isUnlocked ? "Pro features unlocked" : "Not unlocked"
                        }()
                        HStack(spacing: 8) {
                            if isUnknown {
                                ProgressView().controlSize(.small)
                            } else {
                                // F013: reserved red for actual faults; locked-feature indicator
                                // uses amber so it reads as "action needed" not "something broke".
                                Image(systemName: isUnlocked ? "checkmark" : "lock.fill")
                                    .font(.ioBody(.bold))
                                    .foregroundColor(isUnlocked ? .ioSuccessGreen : .ioWarningAmber)
                            }
                            Text(message)
                                .font(.ioBody(.bold))
                                .foregroundColor(isUnknown ? .ioTextSecondary : (isUnlocked ? .ioSuccessGreen : .ioWarningAmber))
                        }
                    }
                    .padding(18)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.ioGluttonPink.opacity(0.05))
                    .background(Color.ioSurfaceDark)
                    .cornerRadius(12)
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioGluttonPink.opacity(0.15), lineWidth: 1))

                    // VPN / DNS Encryption status — real portalReport
                    VStack(alignment: .leading, spacing: 8) {
                        Text("VPN / DNS Encryption status:")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioTextSecondary)
                            .textCase(.uppercase)

                        if let r = report {
                            HStack(spacing: 8) {
                                Circle()
                                    .fill(r.vpn.state == .yes ? Color.ioSuccessGreen : (r.vpn.state == .unknown ? Color.ioWarningAmber : Color.ioTextSecondary))
                                    .frame(width: 8, height: 8)
                                Text(vpnLabel(r.vpn))
                                    .font(.ioBody(.bold))
                                    .foregroundColor(r.vpn.state == .yes ? .ioSuccessGreen : (r.vpn.state == .unknown ? .ioWarningAmber : .ioTextSecondary))
                            }

                            Text(vpnDetail(r))
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                        } else {
                            Text("Run Portal first or check the CLI for VPN status.")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                        }
                    }
                    .padding(18)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.ioGluttonPink.opacity(0.05))
                    .background(Color.ioSurfaceDark)
                    .cornerRadius(12)
                    .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioGluttonPink.opacity(0.15), lineWidth: 1))
                }

                // Local Probes Table — real probe results
                VStack(alignment: .leading, spacing: 10) {
                    Text("Test Label:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioTextSecondary)
                        .textCase(.uppercase)
                    if model.leakTestSubdomain.isEmpty {
                        // B246: distinguish "no check run" from "collector ran (no local subdomain)"
                        if model.collectorResult != nil {
                            Text("Collector check ran — probe subdomain only used for local checks.")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                        } else {
                            Text("Run a leak check to generate a probe subdomain.")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                        }
                    } else {
                        Text(model.leakTestSubdomain)
                            .font(.ioMono(14))
                            .foregroundColor(.ioTextSecondary)

                        if model.leakProbeResults.isEmpty {
                            Text("No probes dispatched.")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                        } else {
                            ForEach(model.leakProbeResults.keys.sorted(), id: \.self) { server in
                                let ok = model.leakProbeResults[server] ?? false
                                HStack {
                                    Text("Probe via").font(.ioCaption()).foregroundColor(.ioTextSecondary)
                                    Spacer()
                                    Text(server).font(.ioMono(13))
                                    Spacer()
                                    // E118: "sent" is a dispatch fact, not a result — use neutral
                                    // tone so green is reserved for the collector result below.
                                    Text(ok ? "sent" : "failed")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(ok ? .ioTextSecondary : .ioCriticalRed)
                                }
                                .padding(12)
                                .background(Color.ioBackgroundDark)
                                .cornerRadius(8)
                                .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                            }
                        }
                    }
                }

                // Collector verification
                VStack(alignment: .leading, spacing: 0) {
                    VStack(alignment: .leading, spacing: 14) {
                        Text("Collector verification")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioTextSecondary)
                            .textCase(.uppercase)

                        Text("A leak collector confirms which upstream resolver actually forwarded your probes. The collector produces a signed attestation usable as evidence.")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                            .lineLimit(nil)

                        HStack(spacing: 12) {
                            TextField("Collector URL", text: $model.collectorURL)
                                .font(.ioMono(13))
                                .textFieldStyle(.roundedBorder)
                                .accessibilityLabel("Collector server URL")
                                .accessibilityHint("The URL of the leak collector server that confirms which upstream resolver forwarded your probes")

                            Button(action: {
                                Task { await model.runCollectorVerification() }
                            }) {
                                if model.collectorRunning {
                                    ProgressView().controlSize(.small)
                                } else {
                                    Text("Run Collector Verification")
                                }
                            }
                            .buttonStyle(IoFilledButtonStyle())
                            .disabled(model.collectorRunning)
                            .accessibilityLabel("Run collector verification")
                            .accessibilityHint("Contacts the collector server to verify which resolver actually forwarded your DNS probes")
                        }

                        if let cr = model.collectorResult {
                            VStack(alignment: .leading, spacing: 6) {
                                HStack {
                                    Text("Conclusion:")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                    Text(cr.result.rawValue)
                                        .font(.ioBody(.bold))
                                        .foregroundColor(gluttonResultColor(cr))
                                }
                                if !cr.observedResolvers.isEmpty {
                                    Text("Observed resolvers: \(cr.observedResolvers.joined(separator: ", "))")
                                        .font(.ioMono(12))
                                        .foregroundColor(.ioTextSecondary)
                                }
                                if let sig = cr.signature {
                                    VStack(alignment: .leading, spacing: 4) {
                                        Text("Signed attestation: \(sig.prefix(32))…")
                                            .font(.ioMono(11))
                                            .foregroundColor(.ioTextSecondary)
                                        // Signature trust: four typed states — never green without key trust.
                                        if let valid = cr.signatureValid {
                                            if valid {
                                                if let trusted = cr.signatureKeyTrusted {
                                                    Text(trusted ? "✅ Ed25519 signature valid · signer trusted" : "⚠️ Ed25519 signature valid · signer NOT trusted")
                                                        .font(.ioCaption(.bold))
                                                        .foregroundColor(trusted ? .ioSuccessGreen : .ioCriticalRed)
                                                } else {
                                                    Text("Ed25519 signature valid · signer trust not configured")
                                                        .font(.ioCaption(.bold))
                                                        .foregroundColor(.ioWarningAmber)
                                                }
                                            } else {
                                                Text("⛔ Ed25519 signature invalid")
                                                    .font(.ioCaption(.bold))
                                                    .foregroundColor(.ioCriticalRed)
                                            }
                                        } else {
                                            Text("Signature not verified — no key supplied")
                                                .font(.ioCaption(.bold))
                                                .foregroundColor(.ioTextMuted)
                                        }
                                        // Transport pinning — surface collector identity verification.
                                        Text("Transport: \(cr.transportPinning.rawValue)")
                                            .font(.ioCaption())
                                            .foregroundColor(cr.transportPinning == .failed ? .ioCriticalRed : .ioTextSecondary)
                                    }
                                }
                                // Show the reason only when it is genuine human-readable text. The core
                                // may build it from a Swift error's raw description, which surfaces as
                                // crash-reporter material ("… CollectorError error 0.") — never product
                                // copy. The amber ERROR conclusion above already states that it failed.
                                if let err = cr.errorReason, let clean = ErrorPresenter.displayReason(err) {
                                    Text(clean)
                                        .font(.ioBody())
                                        .foregroundColor(.ioWarningAmber)
                                }
                            }
                            .padding(12)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                        }
                    }
                    .padding(18)
                    .background(Color.ioSurfaceDark)
                    .cornerRadius(12)
                }
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))
            }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .task {
            if model.licenseState == .unknown {
                let (unlocked, _) = await LicenseManager.isFeatureUnlocked()
                model.licenseState = unlocked ? .pro : .lite
            }
        }
    }

    /// Collector result colour — green only for `.notDetected` when transport identity is
    /// verified AND the signing key (if any) is trusted; red for `.confirmed` (leak found)
    /// and `.invalid`; amber for inconclusive/error or unverified notDetected.
    /// B066: a crypto-valid signature by an untrusted key must not render green.
    /// B067: a notDetected result with unverified transport identity must not render green.
    private func gluttonResultColor(_ cr: LeakTestResult) -> Color {
        switch cr.result {
        case .confirmed:    return .ioCriticalRed
        case .notDetected:
            // B067: gate green on verified transport identity.
            guard cr.transportPinning == .enforced else { return .ioWarningAmber }
            // B066: gate green on trusted signing key when a signature is present and valid.
            if cr.signatureValid == true && cr.signatureKeyTrusted != true {
                return .ioWarningAmber
            }
            return .ioSuccessGreen
        case .invalid:      return .ioCriticalRed
        case .inconclusive: return .ioWarningAmber
        case .error:        return .ioWarningAmber
        }
    }

    private func vpnLabel(_ v: VPNStatus) -> String {
        switch v.state {
        case .yes: return "VPN active — tunnel(s): \(v.tunnelInterfaces.joined(separator: ", "))"
        case .no: return "No VPN tunnel detected"
        case .unknown: return "VPN status unknown"
        }
    }

    private func vpnDetail(_ r: NetworkStateReport) -> String {
        let enc: String
        switch r.encryptedDNS.state {
        case .yes: enc = "Encryption detected (unverified)"
        case .no: enc = "Encryption not detected"
        case .unknown: enc = "DNS encryption status unknown"
        }
        return enc + " — " + r.vpn.evidence
    }
}

#if DEBUG
#Preview {
    GluttonView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .glutton, pro: true))
        .syxxPreviewCanvas()
}
#endif
