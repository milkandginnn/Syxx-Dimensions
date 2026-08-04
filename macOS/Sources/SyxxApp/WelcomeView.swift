import SwiftUI

/// First-run onboarding wizard guiding the user through EULA acceptance, toolset introduction,
/// domain selection, and their first network scan — one step at a time.
struct WelcomeView: View {
    @ObservedObject var model: AppModel
    @State private var step: WizardStep = .welcome
    @State private var eulaAccepted = UserDefaults.standard.bool(forKey: "syxx.eulaAccepted")
    @State private var showEULA = false
    @State private var scanDomain = "syxx.app"
    @State private var scanCompleted = false

    enum WizardStep: CaseIterable {
        case welcome
        case eula
        case tour
        case domain
        case scan
        case done

        var title: String {
            switch self {
            case .welcome: "Welcome"
            case .eula:    "License"
            case .tour:    "The Six Toolsets"
            case .domain:  "Choose a Domain"
            case .scan:    "First Scan"
            case .done:    "All Clear"
            }
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            // Progress bar
            HStack(spacing: 4) {
                ForEach(WizardStep.allCases.indices, id: \.self) { i in
                    let currentIdx = WizardStep.allCases.firstIndex(of: step) ?? 0
                    Capsule()
                        .fill(i <= currentIdx ? Color.accentColor : Color.secondary.opacity(0.3))
                        .frame(height: 3)
                }
            }
            .padding(.horizontal, 40)
            .padding(.top, 16)

            Spacer()

            // Step content
            Group {
                switch step {
                case .welcome: welcomeStep
                case .eula:    eulaStep
                case .tour:    tourStep
                case .domain:  domainStep
                case .scan:    scanStep
                case .done:    doneStep
                }
            }

            Spacer()

            // Navigation
            HStack {
                if step != .welcome {
                    Button("Back") { withAnimation { previousStep() } }
                        .buttonStyle(.bordered)
                }
                Spacer()
                if step != .done && step != .scan {
                    Button(step == .domain ? "Start Scan" : "Next") { withAnimation { nextStep() } }
                        .buttonStyle(.borderedProminent)
                        .disabled(!canAdvance)
                }
            }
            .padding(.horizontal, 40)
            .padding(.bottom, 24)
        }
        .frame(width: 600, height: 480)
        .sheet(isPresented: $showEULA) {
            EULAView(menuPresented: false)
        }
    }

    // MARK: - Step views

    private var welcomeStep: some View {
        VStack(spacing: 20) {
            Image(systemName: "shield.checkered")
                .font(.system(size: 56))
                .foregroundColor(.accentColor)

            Text("Welcome to Syxx")
                .font(.largeTitle)
                .fontWeight(.bold)

            Text("Syxx investigates your DNS for tampering, hijacking, spoofing, and censorship. It asks one question per toolset — and gives you an answer you can trust.")
                .font(.body)
                .multilineTextAlignment(.center)
                .foregroundColor(.secondary)
                .padding(.horizontal, 48)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var eulaStep: some View {
        VStack(spacing: 20) {
            Image(systemName: "doc.text")
                .font(.system(size: 44))
                .foregroundColor(.accentColor)

            Text("License Agreement")
                .font(.title2)
                .fontWeight(.bold)

            if !eulaAccepted {
                Text("Before Syxx can scan your network, you need to review and accept the End User License Agreement.")
                    .font(.body)
                    .multilineTextAlignment(.center)
                    .foregroundColor(.secondary)
                    .padding(.horizontal, 48)
                    .fixedSize(horizontal: false, vertical: true)

                Button("Review License Agreement") { showEULA = true }
                    .buttonStyle(.bordered)

                Button("I Accept") {
                    eulaAccepted = true
                    UserDefaults.standard.set(true, forKey: "syxx.eulaAccepted")
                }
                .buttonStyle(.borderedProminent)
            } else {
                Image(systemName: "checkmark.seal.fill")
                    .font(.system(size: 36))
                    .foregroundColor(.green)
                Text("License accepted. Syxx is ready to scan your network.")
                    .font(.body)
                    .multilineTextAlignment(.center)
                    .foregroundColor(.secondary)
                    .padding(.horizontal, 48)
            }
        }
    }

    private var tourStep: some View {
        VStack(spacing: 16) {
            Text("The Six Toolsets")
                .font(.title2)
                .fontWeight(.bold)

            Text("Each toolset answers one question about your DNS — together they form a complete picture.")
                .font(.body)
                .multilineTextAlignment(.center)
                .foregroundColor(.secondary)
                .padding(.horizontal, 48)
                .fixedSize(horizontal: false, vertical: true)

            VStack(alignment: .leading, spacing: 10) {
                toolsetRow(icon: "magnifyingglass", name: "Prophet", desc: "Is a single DNS query trustworthy?")
                toolsetRow(icon: "arrow.triangle.branch", name: "Mirror", desc: "Is someone interfering with your queries?")
                toolsetRow(icon: "point.3.connected.trianglepath.dotted", name: "Portal", desc: "What does your network path look like?")
                toolsetRow(icon: "drop.triangle", name: "Glutton", desc: "Is your VPN actually keeping DNS private?")
                toolsetRow(icon: "rectangle.stack", name: "Cage", desc: "What DNS traffic is flowing, and from where?")
                toolsetRow(icon: "checkmark.shield", name: "Witness", desc: "Can the evidence be trusted?")
            }
            .padding(.horizontal, 48)
        }
    }

    private func toolsetRow(icon: String, name: String, desc: String) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon)
                .frame(width: 24)
                .foregroundColor(.accentColor)
            Text(name).fontWeight(.semibold).frame(width: 70, alignment: .leading)
            Text(desc).font(.callout).foregroundColor(.secondary)
        }
    }

    private var domainStep: some View {
        VStack(spacing: 20) {
            Image(systemName: "globe")
                .font(.system(size: 44))
                .foregroundColor(.accentColor)

            Text("Choose a Domain")
                .font(.title2)
                .fontWeight(.bold)

            Text("Syxx will scan your DNS resolution for this domain. Start with the default, or enter any domain you want to investigate.")
                .font(.body)
                .multilineTextAlignment(.center)
                .foregroundColor(.secondary)
                .padding(.horizontal, 48)
                .fixedSize(horizontal: false, vertical: true)

            TextField("Domain to scan", text: $scanDomain)
                .textFieldStyle(.roundedBorder)
                .frame(width: 300)
                .multilineTextAlignment(.center)
                .font(.body.monospaced())
        }
    }

    private var scanStep: some View {
        VStack(spacing: 20) {
            if model.runningTool != nil {
                ProgressView()
                    .scaleEffect(1.5)
                Text("Scanning \(scanDomain)...")
                    .font(.headline)
                Text(model.output)
                    .font(.caption)
                    .foregroundColor(.secondary)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 48)
            } else if scanCompleted {
                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 48))
                    .foregroundColor(.green)
                Text("Scan Complete")
                    .font(.title2)
                    .fontWeight(.bold)
                Text("Your network has been analyzed across all six toolsets. Review the results on the dashboard.")
                    .font(.body)
                    .multilineTextAlignment(.center)
                    .foregroundColor(.secondary)
                    .padding(.horizontal, 48)
                    .fixedSize(horizontal: false, vertical: true)
            } else {
                Text("Preparing scan…")
                    .font(.headline)
                    .foregroundColor(.secondary)
            }
        }
        .onAppear {
            #if DEBUG
            if ProcessInfo.processInfo.isSyxxXcodePreview {
                scanCompleted = true
                return
            }
            #endif
            if !scanCompleted {
                model.domain = scanDomain
                model.runAllWardens()
                // Poll for completion
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
                    checkScanComplete()
                }
            }
        }
    }

    private func checkScanComplete() {
        if model.runningTool == nil {
            scanCompleted = true
        } else {
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.0) {
                checkScanComplete()
            }
        }
    }

    private var doneStep: some View {
        VStack(spacing: 20) {
            Image(systemName: "party.popper.fill")
                .font(.system(size: 48))
                .foregroundColor(.accentColor)

            Text("You're All Set")
                .font(.title2)
                .fontWeight(.bold)

            Text("Syxx is now monitoring your DNS. Explore the Dashboard for your full scan report, or dive into individual toolsets from the sidebar.")
                .font(.body)
                .multilineTextAlignment(.center)
                .foregroundColor(.secondary)
                .padding(.horizontal, 48)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // MARK: - Navigation

    private var canAdvance: Bool {
        switch step {
        case .welcome: true
        case .eula:    eulaAccepted
        case .tour:    true
        case .domain:  !scanDomain.trimmingCharacters(in: .whitespaces).isEmpty
        case .scan:    scanCompleted
        case .done:    false
        }
    }

    private func nextStep() {
        switch step {
        case .welcome: step = .eula
        case .eula:    step = .tour
        case .tour:    step = .domain
        case .domain:
            step = .scan
        case .scan:
            step = .done
            UserDefaults.standard.set(true, forKey: "hasCompletedOnboarding")
        case .done: break
        }
    }

    private func previousStep() {
        let all = WizardStep.allCases
        if let idx = all.firstIndex(of: step), idx > 0 {
            step = all[idx - 1]
        }
    }
}

#if DEBUG
#Preview {
    WelcomeView(model: SyxxPreviewSupport.appModel())
        .syxxPreviewPanel()
}
#endif
