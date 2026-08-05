import SwiftUI
import SyxxKit

struct CageView: View {
    @EnvironmentObject var model: AppModel
    
    var body: some View {
        ZStack {
            PurpleScrollView(geometry: .fullPanel) {
                VStack(alignment: .leading, spacing: 20) {
                    // Warden header: rune tile + eyebrow + name + lore
                    HStack(alignment: .top, spacing: 15) {
                        Text("\u{16BE}")
                            .font(.custom("Cinzel-SemiBold", size: 28))
                            .frame(width: 54, height: 54)
                            .background(Color.ioCageCyan.opacity(0.10))
                            .cornerRadius(14)
                            .foregroundColor(Color.ioCageCyan)
                            .shadow(color: Color.ioCageCyan.opacity(0.35), radius: 22)
                            .accessibilityHidden(true)

                        VStack(alignment: .leading, spacing: 4) {
                            Text("THE WARDEN \u{00B7} LIVE QUERY CAPTURE")
                                .font(.ioCaption(.bold))
                                .foregroundColor(Color.ioCageCyan)
                                .tracking(0.26)

                            Text("Cage")
                                .font(.custom("Cinzel-SemiBold", size: 26))
                                .foregroundColor(.ioText)

                            Text("The Cage trapped the DNS queries so they could be inspected without escape, their headers and answers laid bare for judgment.")
                                .font(.ioLore(16))
                                .foregroundColor(.ioTextMuted)
                                .fixedSize(horizontal: false, vertical: true)
                                .frame(maxWidth: 560)
                        }

                        Spacer()

                        HStack(spacing: 8) {
                            TextField("Seconds", value: $model.cageDuration, formatter: NumberFormatter())
                                .textFieldStyle(.roundedBorder)
                                .frame(width: 80)

                            Button(action: {
                                model.toggleCapture()
                            }) {
                                Text(model.cageCapturing ? "Stop Capture" : "Close the Cages")
                            }
                            .buttonStyle(IoFilledButtonStyle(color: model.cageCapturing ? .ioCriticalRed : .ioBlue))
                            .accessibilityLabel(model.cageCapturing ? "Stop DNS capture" : "Start DNS capture")

                            Button(action: {
                                model.hideCage()
                            }) {
                                Text("Hide the Cages")
                            }
                            .buttonStyle(IoOutlineButtonStyle())
                            .accessibilityLabel("Hide the capture view")
                        }
                    }
                    
                    Text("And you are nonetheless asked before closing the cage for how long you wish it sealed. (Enter the duration of the packet capture in seconds, e.g. 15s, up to 9000s (2.5 hours). While closed, surf the web and use your apps as normal. You must produce DNS queries in order to capture and analyze them.)")
                        .font(.ioCaption())
                        .foregroundColor(.ioTextSecondary)
                        .fixedSize(horizontal: false, vertical: true)

                    Text("Cage (syxx cage / DNSCapture) performs live packet capture, extracting DNS header flags, transaction IDs, query entropy, and query rates to detect exfiltration, beaconing, or DNS tunneling.")
                        .font(.ioBody())
                        .foregroundColor(.ioTextSecondary)
                        .lineLimit(nil)
                    
                    // Live Status line
                    HStack(spacing: 16) {
                        if model.cageCapturing {
                            HStack(spacing: 6) {
                                Circle()
                                    .fill(Color.ioCriticalRed)
                                    .frame(width: 8, height: 8)
                                    .ioPulse(color: .ioCriticalRed)
                                Text("CAPTURING")
                                    .font(.ioSmall(.bold))
                                    .foregroundColor(.ioCriticalRed)
                            }
                            .padding(.horizontal, 10)
                            .padding(.vertical, 4)
                            .background(Color.ioCriticalRed.opacity(0.12))
                            .cornerRadius(999)
                        } else {
                            HStack(spacing: 6) {
                                Circle()
                                    .fill(Color.ioTextSecondary)
                                    .frame(width: 8, height: 8)
                                Text("IDLE")
                                    .font(.ioSmall(.bold))
                                    .foregroundColor(.ioTextSecondary)
                            }
                            .padding(.horizontal, 10)
                            .padding(.vertical, 4)
                            .background(Color.ioSurfaceElevated)
                            .cornerRadius(999)
                        }
                        
                        Text("Events captured: ")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                        + Text("\(model.cageEventsCount)")
                            .font(.ioMonoBold(14))
                            .foregroundColor(.ioText)
                    }
                    
                    // Honest capture result. The engine computes a message ("No packets were
                    // detected…" / "Captured N DNS packet(s)." / "Could not start capture…") but the
                    // view never showed it — so a finished capture silently reopened to an empty grid
                    // with no explanation. Shown once a capture ends; amber when nothing was captured
                    // so it reads as a real state. Bound to `cageResultMessage` (Cage's OWN channel),
                    // never the shared `output` feed — so a Prophet/Mirror/Portal/Glutton/Witness
                    // result finishing while Cage is on screen can never paint into this panel.
                    if !model.cageCapturing && !model.cageResultMessage.isEmpty {
                        Text(model.cageResultMessage)
                            .font(.ioBody())
                            .foregroundColor(model.cageRows.isEmpty ? .ioWarningAmber : .ioTextSecondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }

                    // Live terminal feed — streams the ACTUAL captured queries as they arrive (from the
                    // real syxx cage capture file). Honesty spine: the box appears only when there is
                    // something real to show, and the "listening" line renders ONLY while a capture is
                    // genuinely running (`model.cageTerminalFeed`). A capture that never ran, or that
                    // failed to start, must never render as one that is listening and simply has not
                    // seen traffic yet. There is no fabricated shell prompt and no asserted port: the
                    // panel shows only what was actually measured. Per-query conclusions live in the
                    // grid below, so this feed never implies a result.
                    if model.cageTerminalFeed != .none {
                        VStack(alignment: .leading, spacing: 4) {
                            switch model.cageTerminalFeed {
                            case .listening:
                                Text("Listening for DNS traffic…")
                                    .font(.ioMono(12))
                                    .foregroundColor(.ioTextSecondary)
                            case .rows:
                                let feed = Array(model.cageRows.prefix(15).reversed())   // oldest of the 15 first, newest last
                                PurpleScrollView(geometry: .compact, scrollToBottom: model.cageEventsCount) {
                                    VStack(alignment: .leading, spacing: 2) {
                                        ForEach(feed, id: \.id) { row in
                                            Text(terminalLine(row))
                                                .font(.ioMono(12))
                                                .foregroundColor(.ioTextSecondary)
                                                .lineLimit(1)
                                                .truncationMode(.middle)
                                                .frame(maxWidth: .infinity, alignment: .leading)
                                        }
                                    }
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                }
                                .frame(height: 180)
                            case .none:
                                EmptyView()
                            }
                        }
                        .padding(14)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(Color(red: 16/255, green: 8/255, blue: 38/255))
                        .cornerRadius(12)
                        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))
                    }
                    
                    // Grid Columns Header
                    VStack(spacing: 8) {
                        HStack {
                            Text("Time")
                                .font(.ioCaption(.bold))
                                .frame(width: 80, alignment: .leading)
                            Text("Domain")
                                .font(.ioCaption(.bold))
                                .frame(maxWidth: .infinity, alignment: .leading)
                            Text("Process · PID")
                                .font(.ioCaption(.bold))
                                .frame(width: 160, alignment: .leading)
                            Text("Resolver")
                                .font(.ioCaption(.bold))
                                .frame(width: 100, alignment: .leading)
                            Text("Conclusion")
                                .font(.ioCaption(.bold))
                                .frame(width: 80, alignment: .trailing)
                        }
                        .foregroundColor(.ioTextSecondary)
                        .padding(.horizontal, 14)
                        
                        // Rows
                        ForEach(model.cageRows, id: \.id) { row in
                            HStack(spacing: 0) {
                                // Left accent border — color reflects severity
                                Rectangle()
                                    .fill(severityAccentColor(row.severity))
                                    .frame(width: 3)
                                    .cornerRadius(2)

                                HStack {
                                    Text(row.time)
                                        .font(.ioMono(13))
                                        .foregroundColor(.ioTextSecondary)
                                        .frame(width: 80, alignment: .leading)

                                    VStack(alignment: .leading, spacing: 2) {
                                        Text(row.domain)
                                            .font(.ioMono(14))
                                            .foregroundColor(.ioText)
                                        if !row.note.isEmpty {
                                            Text(row.note)
                                                .font(.ioSmall())
                                                .foregroundColor(.ioTextSecondary)
                                        }
                                    }
                                    .frame(maxWidth: .infinity, alignment: .leading)

                                    Text(row.process)
                                        .font(.ioMono(13))
                                        .foregroundColor(.ioTextSecondary)
                                        .frame(width: 160, alignment: .leading)

                                    Text(row.resolver)
                                        .font(.ioMono(13))
                                        .foregroundColor(.ioTextSecondary)
                                        .frame(width: 100, alignment: .leading)

                                    IoSeverityBadge(severity: row.severity)
                                        .frame(width: 80, alignment: .trailing)
                                }
                                .padding(12)
                            }
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(
                                RoundedRectangle(cornerRadius: 8)
                                    .stroke(Color.ioBorderSubtle, lineWidth: 1)
                            )
                            .clipped()
                        }
                    }
                }
                .padding(.bottom, 32)
                // Horizontal margin is owned by ContentView's 35px padding; no double-margins here.
                // ANTI-WOBBLE: fill the scroll viewport width and top-anchor, matching ConclusionsView.
                .frame(maxWidth: .infinity, alignment: .topLeading)
            }

            // Background Hidden Timer screen overlay
            if model.cageHidden {
                ZStack {
                    Color.ioBackgroundDark.ignoresSafeArea()
                    
                    VStack(spacing: 24) {
                        LogoView(size: 64)
                            .opacity(0.9)
                        
                        Text("The Cage is hidden")
                            .font(.ioTitle1())
                            .foregroundColor(.ioText)
                        
                        Text("Syxx-Cage is capturing quietly in the background. This window returns when the timer runs out.")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                            .multilineTextAlignment(.center)
                            .frame(maxWidth: 360)
                        
                        Text("\(model.cageRemain)s")
                            .font(.ioMonoBold(48))
                            .foregroundColor(.ioCageCyan)
                        
                        Button(action: {
                            model.cageHidden = false
                        }) {
                            Text("Return to Application")
                        }
                        .buttonStyle(IoOutlineButtonStyle())
                    }
                    .padding(40)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    // MARK: - Helpers

    /// One captured-query line for the live terminal feed: `• HH:mm:ss  <domain>  via <process>  (<resolver>)`.
    private func terminalLine(_ row: CageRow) -> String {
        var line = "• \(row.time)  \(row.domain)  via \(row.process)"
        if !row.resolver.isEmpty { line += "  (\(row.resolver))" }
        return line
    }

    /// Folded onto the shared source (Color.ioSeverity) — see IoDesignSystem. This used to be its
    /// own copy of the switch; a second copy is how surfaces drift apart.
    private func severityAccentColor(_ s: Severity) -> Color { .ioSeverity(s) }
}

#if DEBUG
#Preview {
    CageView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .cage, pro: true))
        .syxxPreviewCanvas()
}
#endif
