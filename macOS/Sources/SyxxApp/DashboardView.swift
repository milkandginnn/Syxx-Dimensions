import SwiftUI
import SyxxKit

struct DashboardView: View {
    @EnvironmentObject var model: AppModel

    private let wardens: [(name: String, rune: String, role: String, color: Color, tint: Color, glow: Color)] = [
        ("Prophet", "\u{16A8}", "Resolver accuracy",
         Color(red: 45/255, green: 212/255, blue: 191/255),
         Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.10),
         Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.35)),
        ("Mirror", "\u{16DE}", "Remote vs local",
         Color(red: 167/255, green: 139/255, blue: 255/255),
         Color(red: 167/255, green: 139/255, blue: 255/255).opacity(0.12),
         Color(red: 167/255, green: 139/255, blue: 255/255).opacity(0.35)),
        ("Portal", "\u{16DC}", "LAN & route trace",
         Color(red: 45/255, green: 212/255, blue: 191/255),
         Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.10),
         Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.35)),
        ("Glutton", "\u{16A6}", "DNS leak test",
         Color(red: 255/255, green: 77/255, blue: 109/255),
         Color(red: 255/255, green: 77/255, blue: 109/255).opacity(0.10),
         Color(red: 255/255, green: 77/255, blue: 109/255).opacity(0.35)),
        ("Cage", "\u{16BE}", "Live query capture",
          Color(red: 167/255, green: 139/255, blue: 255/255),
          Color(red: 148/255, green: 120/255, blue: 246/255).opacity(0.12),
          Color(red: 167/255, green: 139/255, blue: 255/255).opacity(0.30)),
        ("Witness", "\u{16C9}", "Environment trust",
         Color(red: 167/255, green: 139/255, blue: 255/255),
         Color(red: 167/255, green: 139/255, blue: 255/255).opacity(0.12),
         Color(red: 167/255, green: 139/255, blue: 255/255).opacity(0.35))
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Eyebrow + Title
            VStack(alignment: .leading, spacing: 5) {
                Text("THE WATCHTOWER · REALM OVERVIEW")
                    .font(.ioCaption(.bold))
                    .foregroundColor(Color(red: 138/255, green: 110/255, blue: 240/255)) // #8a6ef0
                    .tracking(0.26)
                Text(isCleanResult ? "The Vigil Holds" : "Watchtower Idle")
                    .font(.custom("Cinzel-SemiBold", size: 27))
                    .foregroundColor(.ioText)
            }

            Spacer(minLength: 8)

            // Banner: trust ring + health meter + status
            HStack(spacing: 16) {
                IoProgressRing(
                    progress: trustProgress,
                    lineWidth: 10,
                    size: 70,
                    label: trustLabel.uppercased(),
                    sublabel: "\(completedWardens) of 6 run"
                )

                VStack(alignment: .leading, spacing: 8) {
                    HStack(spacing: 10) {
                        Text(isCleanResult ? "The Ways are Warded" : "Awaiting Initial Scan")
                            .font(.custom("Cinzel-SemiBold", size: 19))
                            .foregroundColor(.ioText)

                        StatusPill(label: transportLabel, color: transportColor)

                        Text(resolverIP)
                            .font(.ioMono(11))
                            .foregroundColor(Color(red: 185/255, green: 169/255, blue: 255/255))
                            .padding(.horizontal, 10)
                            .padding(.vertical, 5)
                            .background(Color(white: 1, opacity: 0.06))
                            .cornerRadius(999)
                    }

                    // Health meter bar
                    GeometryReader { geo in
                        ZStack(alignment: .leading) {
                            RoundedRectangle(cornerRadius: 999)
                                .fill(Color(white: 1, opacity: 0.08))
                                .frame(height: 9)
                            RoundedRectangle(cornerRadius: 999)
                                .fill(
                                    LinearGradient(
                                        colors: healthBarColors,
                                        startPoint: .leading, endPoint: .trailing
                                    )
                                )
                                .frame(width: geo.size.width * trustProgress, height: 9)
                                .shadow(color: healthBarColors.last?.opacity(0.5) ?? .clear, radius: 6)
                        }
                    }
                    .frame(height: 9)

                    Text("\(completedWardens) of 6 checks have run." + encryptionStatusClause)
                        .font(.ioLore(13))
                        .foregroundColor(.ioTextMuted)
                }
            }
            .padding(14)
            .background(Color(white: 1, opacity: 0.03))
            .cornerRadius(14)
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color(white: 1, opacity: 0.09), lineWidth: 1))

            Spacer(minLength: 8)

            // Actions
            VStack(spacing: 8) {
                // Primary: Conduct All Scans
                Button(action: { model.runAllWardens() }) {
                    HStack(spacing: 12) {
                        Text("⚔")
                            .font(.system(size: 24))
                        VStack(alignment: .leading, spacing: 2) {
                            Text("Conduct All Scans")
                                .font(.ioDisplay(size: 16, weight: .semibold))
                                .foregroundColor(.white)
                            Text("Summon all six wardens to the roads")
                                .font(.body(12))
                                .foregroundColor(Color.white.opacity(0.72))
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(
                        LinearGradient(
                            colors: [Color(red: 124/255, green: 92/255, blue: 255/255),
                                     Color(red: 91/255, green: 63/255, blue: 214/255)],
                            startPoint: .leading, endPoint: .trailing
                        )
                    )
                    .cornerRadius(12)
                    .shadow(color: Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.4), radius: 12, y: 8)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("Run all scans")
                .accessibilityHint("Tests your DNS for tampering, leaks, hijacking, and captures live queries")

                // Secondary row
                HStack(spacing: 10) {
                    // Flush DNS Cache
                    Button(action: { model.flushDNSCache() }) {
                        HStack(spacing: 10) {
                            Text("≈")
                                .font(.system(size: 15))
                                .frame(width: 28, height: 28)
                                .background(Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.12))
                                .cornerRadius(7)
                                .foregroundColor(Color(red: 45/255, green: 212/255, blue: 191/255))
                            VStack(alignment: .leading, spacing: 1) {
                                Text("Flush DNS Cache")
                                    .font(.uiLabel(13))
                                    .foregroundColor(.ioText)
                                Text("Clear the resolver cache")
                                    .font(.body(11))
                                    .foregroundColor(.ioTextMuted)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(10)
                        .background(Color(white: 1, opacity: 0.03))
                        .cornerRadius(11)
                        .overlay(RoundedRectangle(cornerRadius: 11).stroke(Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.30), lineWidth: 1))
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Flush DNS cache")
                    .accessibilityHint("Clears the system resolver cache so the next lookup fetches fresh results")

                    // Enable Unbound DNS
                    Button(action: { model.setUnbound(true) }) {
                        HStack(spacing: 10) {
                            Text("◈")
                                .font(.system(size: 14))
                                .frame(width: 28, height: 28)
                                .background(Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.12))
                                .cornerRadius(7)
                                .foregroundColor(Color(red: 167/255, green: 139/255, blue: 255/255))
                            VStack(alignment: .leading, spacing: 1) {
                                Text("Enable Unbound DNS")
                                    .font(.uiLabel(13))
                                    .foregroundColor(.ioText)
                                Text("Run a local validating resolver")
                                    .font(.body(11))
                                    .foregroundColor(.ioTextMuted)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(10)
                        .background(Color(white: 1, opacity: 0.03))
                        .cornerRadius(11)
                        .overlay(RoundedRectangle(cornerRadius: 11).stroke(Color(white: 1, opacity: 0.08).opacity(0.5), lineWidth: 1))
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("Enable Unbound DNS")
                    .accessibilityHint("Runs a local validating DNS resolver on your machine for DNSSEC verification")

                    // Enable Encrypted DNS — state-driven caption, never fabricated
                    HStack(spacing: 10) {
                        Text("⊝")
                            .font(.system(size: 15))
                            .frame(width: 28, height: 28)
                            .background(Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.10))
                            .cornerRadius(7)
                            .foregroundColor(Color(red: 45/255, green: 212/255, blue: 191/255))
                        VStack(alignment: .leading, spacing: 1) {
                            Text("Enable Encrypted DNS")
                                .font(.uiLabel(13))
                                .foregroundColor(Color(red: 201/255, green: 193/255, blue: 230/255))
                            Text(encryptedDNSCaption)
                                .font(.body(11))
                                .foregroundColor(.ioTextMuted)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10)
                    .background(Color(white: 1, opacity: 0.02))
                    .cornerRadius(11)
                    .overlay(RoundedRectangle(cornerRadius: 11).stroke(Color(white: 1, opacity: 0.08), lineWidth: 1))
                    .opacity(model.isEncrypted ? 0.5 : 1.0)
                    .accessibilityElement(children: .combine)
                    .accessibilityLabel("Encrypted DNS")
                    .accessibilityHint(encryptedDNSAccessibilityHint)
                }
            }

            Spacer(minLength: 8)

            // 2-column layout: warden grid + activity terminal.
            // "THE COUNCIL'S RESULT" card and "THE CHRONICLE" were removed by owner
            // directive (2026-07-23). The result surfaces of record are
            // OverallResultView / ConclusionsView (still reachable via the sidebar,
            // ContentView.swift). The Dashboard no longer claims any result-like state
            // beyond the honest trust ring (`trustLabel`, "—" when undetermined).
            HStack(alignment: .top, spacing: 10) {
                // Left: Warden grid (2-col)
                VStack(alignment: .leading, spacing: 8) {
                    Text("THE SIX WARDENS")
                        .font(.eyebrow(10))
                        .foregroundColor(Color(red: 143/255, green: 131/255, blue: 184/255))
                        .tracking(0.20)

                    LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
                        ForEach(wardens, id: \.name) { w in
                            wardenTile(w)
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)

                // Right: Activity terminal — timestamped scrollback of real button
                // feedback (model.output + unboundStatusMessage via model.dashboardLog).
                // Renders ONLY strings production code actually emitted; it never prints
                // a synthetic "ready"/"ok" line of its own.
                VStack(alignment: .leading, spacing: 8) {
                    // WORDING PROPOSAL (pending owner approval): "ACTIVITY LOG" eyebrow.
                    Text("ACTIVITY LOG")
                        .font(.eyebrow(10))
                        .foregroundColor(Color(red: 143/255, green: 131/255, blue: 184/255))
                        .tracking(0.20)

                    terminalPane
                }
                .frame(maxWidth: .infinity, alignment: .topLeading)
            }

            Spacer(minLength: 8)

            // Pro Covenant banner (mockup §3.6). Gradient fill per IMPLEMENTATION_GUIDE
            // §1.2: linear-gradient(120deg, rgba(124,92,255,.16), rgba(45,212,191,.06)).
            HStack(spacing: 12) {
                Text("✧")
                    .font(.system(size: 18))
                    .foregroundColor(Color(red: 124/255, green: 92/255, blue: 255/255))
                VStack(alignment: .leading, spacing: 2) {
                    Text("The Pro Covenant")
                        .font(.bodyAlternate(14))
                        .foregroundColor(.ioText)
                    Text("Server-backed checks, signed testimony, and the full Mirror reflection await those who hold the Covenant.")
                        .font(.ioLore(11))
                        .foregroundColor(.ioTextMuted)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer()
                Text("UNLOCK")
                    .font(.uiLabel(11))
                    .tracking(1.5)
                    .foregroundColor(Color(red: 124/255, green: 92/255, blue: 255/255))
                    .padding(.horizontal, 14)
                    .padding(.vertical, 6)
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.4), lineWidth: 1))
            }
            .padding(14)
            .background(
                LinearGradient(
                    colors: [Color(red: 124/255, green: 92/255, blue: 255/255).opacity(0.16),
                             Color(red: 45/255, green: 212/255, blue: 191/255).opacity(0.06)],
                    startPoint: UnitPoint(x: 0.25, y: 0.933),
                    endPoint: UnitPoint(x: 0.75, y: 0.067)
                )
            )
            .cornerRadius(14)
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color(red: 148/255, green: 120/255, blue: 246/255).opacity(0.14), lineWidth: 1))
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    // MARK: - Warden tile

    @ViewBuilder
    private func wardenTile(_ w: (name: String, rune: String, role: String, color: Color, tint: Color, glow: Color)) -> some View {
        let tool = wardenTool(for: w.name)
        Button(action: {
            if let t = tool { model.selection = t }
        }) {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 10) {
                    Text(w.rune)
                        .font(.ioDisplay(size: 20, weight: .semibold))
                        .frame(width: 32, height: 32)
                        .background(w.tint)
                        .cornerRadius(8)
                        .foregroundColor(w.color)
                        .shadow(color: w.glow, radius: 12)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(w.name)
                            .font(.bodyAlternate(14))
                            .foregroundColor(.ioText)
                        Text(w.role)
                            .font(.ioLore(10))
                            .foregroundColor(.ioTextMuted)
                    }
                }

                Text(wardenState(for: w.name))
                    .font(.ioMicro(.bold))
                    .tracking(0.05)
                    .textCase(.uppercase)
                    .foregroundColor(w.color)

                // Live capture ticker (owner-approved copy "Captures: {n}", 2026-07-23):
                // {n} is the REAL published cageEventsCount — the same property CageView
                // renders, updated live during capture — shown modulo 1000 per the owner
                // ("goes up to 999 and starts over"). Neutral secondary tone: zero is a
                // real measurement, neither success-green nor alarming.
                if w.name == "Cage" {
                    Text("Captures: \(model.cageEventsCount % 1000)")
                        .font(.ioMono(10))
                        .foregroundColor(.ioTextSecondary)
                }
            }
            // IMPLEMENTATION_GUIDE §2.4: Dashboard warden tile radius 11, padding ~7–10px, 32px rune.
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(white: 1, opacity: 0.025))
            .cornerRadius(11)
            .overlay(RoundedRectangle(cornerRadius: 11).stroke(Color(white: 1, opacity: 0.08), lineWidth: 1))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("\(w.name) — \(w.role)")
        .accessibilityHint("Opens the \(w.name) tool for detailed diagnostics")
    }

    // MARK: - Helpers

    private func wardenTool(for name: String) -> Tool? {
        switch name {
        case "Prophet":  return .prophet
        case "Mirror":   return .mirror
        case "Portal":   return .portal
        case "Glutton":  return .glutton
        case "Cage":     return .cage
        case "Witness":  return .witness
        default:         return nil
        }
    }

    private func wardenState(for name: String) -> String {
        switch name {
        case "Prophet":
            guard let r = model.prophetResult else { return "NOT RUN" }
            let active = r.resolverResponses.filter { !$0.ip.contains("[unreachable]") }
            guard !active.isEmpty else { return "NO DATA" }
            // Check actual answer consensus — need >= 2 reachable resolvers AND
            // all must return the same IP(s) before a "PASSED" is honest.
            guard active.count >= 2 else { return "INCONCLUSIVE" }
            let uniqueIPs = Set(active.map { $0.ip })
            guard uniqueIPs.count == 1 else { return "INCONCLUSIVE" }
            return "PASSED"
        case "Mirror":
            return DashboardWardenState.mirror(isPro: model.isPro, result: model.mirrorResult)
        case "Portal":
            guard let r = model.portalReport else { return "NOT RUN" }
            // A valid report must have at least detected the active interfaces
            // or DNS servers — otherwise it's inconclusive.
            if r.dnsServers.isEmpty && r.activeInterfaces.isEmpty { return "NO DATA" }
            return "PASSED"
        case "Glutton":
            guard model.isPro else { return "LOCKED · PRO" }
            guard let r = model.leakResult else { return "NOT RUN" }
            switch r.result {
            case .confirmed: return "LEAK FOUND"
            case .notDetected: return "CLEAR"
            case .inconclusive: return "INCONCLUSIVE"
            case .invalid: return "INVALID"
            case .error: return "ERROR"
            }
        case "Cage":
            return model.cageResultMessage.isEmpty ? "NOT RUN" : "CAPTURED"
        case "Witness":
            guard model.isPro else { return "LOCKED · PRO" }
            guard model.witnessSnapshot != nil else { return "NOT RUN" }
            // A snapshot may exist but be invalid (expired, forged, etc.).
            // Check the verification result to determine the real state.
            switch model.witnessResult {
            case .valid: return "SIGNED"
            case .expired: return "EXPIRED"
            case .stale: return "STALE"
            case .invalidSignature: return "INVALID"
            case .notVerified, .noKey, .futureDated, .untrustedSigner,
                    .displayMismatch, .malformedInput, .unsupportedFormat:
                return "INCONCLUSIVE"
            }
        default:
            return "NOT RUN"
        }
    }

    private var trustProgress: Double {
        Double(completedWardens) / 6.0
    }

    private var completedWardens: Int {
        var count = 0
        if model.prophetResult != nil { count += 1 }
        if model.portalReport != nil { count += 1 }
        if !model.cageResultMessage.isEmpty { count += 1 }
        if model.isPro {
            if model.mirrorResult != nil { count += 1 }
            if model.leakResult != nil { count += 1 }
            if model.witnessSnapshot != nil { count += 1 }
        }
        return count
    }

    /// True only when the matrix engine has produced an affirmative clean determination.
    private var isCleanResult: Bool {
        model.conclusion?.determination?.state == .clean
    }

    private var trustLabel: String {
        // The matrix engine emits a per-diagnosis `strength` (0…1), not a fabricated 0-100
        // confidence. Inconclusive / no-determination shows "—" (never a manufactured number).
        // Threat states also show "—" — a diagnostic strength number in the trust ring is
        // misleading when the result is "threat detected".
        guard let det = model.conclusion?.determination else { return "—" }
        switch det.state {
        case .untested:       return "—"
        case .inconclusive:   return "—"
        case .threatDetected: return "—"
        case .clean:
            guard let s = det.diagnoses.first?.strength else { return "—" }
            return "\(Int((s * 100).rounded()))"
        }
    }

    /// Tri-state transport label: confirmed encrypted, confirmed cleartext, or undetermined.
    private var transportLabel: String {
        switch model.cachedEncryptedDNSStatus?.state {
        case .yes:     return "DoT · TLS"
        case .no:      return "Do53 · clear"
        case .unknown: return "Transport unknown"
        case nil:      return "Transport unknown"
        }
    }

    /// Transport colour derived from the same source as the label.
    private var transportColor: Color {
        switch model.cachedEncryptedDNSStatus?.state {
        case .yes:     return .ioSuccessGreen
        case .no:      return .ioWarningAmber
        case .unknown: return .ioInfoPurple
        case nil:      return .ioInfoPurple
        }
    }

    /// Resolver IP from actual measurements, or honest "not determined" when unavailable.
    /// D278: `first(where:)` so an empty-string entry never reads as a measured resolver.
    private var resolverIP: String {
        model.portalReport?.dnsServers.first(where: { !$0.isEmpty })
            ?? model.cachedSystemDNSServers.first(where: { !$0.isEmpty })
            ?? "not determined"
    }

    /// Health meter colours: green gradient only for confirmed clean; red for threat;
    /// neutral for undetermined.
    private var healthBarColors: [Color] {
        guard let state = model.conclusion?.determination?.state else {
            return [Color(white: 0.4), Color(white: 0.5)]
        }
        switch state {
        case .clean:
            return [Color(red: 45/255, green: 212/255, blue: 191/255),
                    Color(red: 124/255, green: 92/255, blue: 255/255)]
        case .threatDetected:
            return [.ioCriticalRed, .ioHighOrange]
        case .inconclusive, .untested:
            return [Color(white: 0.4), Color(white: 0.5)]
        }
    }

    /// Encrypted DNS tile caption — driven by real state, never fabricated.
    private var encryptedDNSCaption: String {
        if model.isEncrypted { return "Encrypted DNS active" }
        switch model.cachedEncryptedDNSStatus?.state {
        case .no:      return "Not encrypted"
        case .unknown: return "Status unknown"
        case .yes:     return "Encrypted DNS active"
        case nil:      return "Status not determined"
        }
    }

    /// Accessibility hint for the encrypted DNS tile — factual, never fabricated.
    private var encryptedDNSAccessibilityHint: String {
        if model.isEncrypted { return "Encrypted DNS is currently active" }
        switch model.cachedEncryptedDNSStatus?.state {
        case .no:      return "DNS traffic is not encrypted"
        case .unknown: return "DNS encryption status could not be determined"
        case .yes:     return "Encrypted DNS is currently active"
        case nil:      return "DNS encryption status has not been checked"
        }
    }

    /// Encryption clause for the banner — state-dependent, never unconditional.
    private var encryptionStatusClause: String {
        switch model.cachedEncryptedDNSStatus?.state {
        case .yes:     return " Transport is encrypted."
        case .no:      return " Transport is not encrypted."
        case .unknown: return " Transport status unknown."
        case nil:      return " Transport not yet checked."
        }
    }

    // MARK: - Activity terminal

    /// 12-hour wall-clock timestamps for the activity terminal (owner directive 2026-07-23:
    /// 12-hour, no epoch). POSIX locale pins "h:mm:ss a" regardless of the system's
    /// 24-hour-time preference.
    private static let logTimeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "h:mm:ss a"
        return f
    }()

    /// Terminal-styled scrollback matching CageView's live-feed conventions (CageView.swift
    /// :124-162 — .ioMono(12), .ioTextSecondary, #100826 dark inset, .ioBorderSubtle hairline).
    /// Newest entries at the bottom, auto-scrolling. Displays ONLY entries in
    /// `model.dashboardLog`, each of which is a string a production code path really assigned
    /// to `output` / `unboundStatusMessage`; the empty state is an honest quiet placeholder,
    /// never a synthetic "ready" line.
    private var terminalPane: some View {
        Group {
            if model.dashboardLog.isEmpty {
                // WORDING PROPOSAL (pending owner approval): "No activity yet."
                Text("No activity yet.")
                    .font(.ioMono(12))
                    .foregroundColor(.ioTextSecondary)
                    .opacity(0.5)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            } else {
                PurpleScrollView(geometry: .compact, scrollToBottom: model.dashboardLog.count) {
                    VStack(alignment: .leading, spacing: 3) {
                        ForEach(model.dashboardLog) { entry in
                            Text("\(Self.logTimeFormatter.string(from: entry.time))  \(entry.text)")
                                .font(.ioMono(12))
                                .foregroundColor(.ioTextSecondary)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
        .frame(height: 220)
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(Color(red: 16/255, green: 8/255, blue: 38/255))
        .cornerRadius(12)
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))
    }
}

#if DEBUG
#Preview {
    DashboardView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .dashboard, pro: true))
        .syxxPreviewCanvas()
}
#endif
