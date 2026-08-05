import SwiftUI
import SyxxKit

struct PortalView: View {
    @EnvironmentObject var model: AppModel

    /// D278: copy for the Resolvers pane when there are no servers to list. While the startup
    /// probe is still in flight, an empty pane must read as "checking", never as a measured
    /// "No DNS servers discovered" or a silent blank. Every other empty state (unavailable, or
    /// a measured list that came back empty) reads as "No DNS servers discovered".
    static func resolversPaneEmptyCopy(state: ResolverState) -> String {
        switch state {
        case .checking:
            return "Checking system DNS servers…"
        case .measured, .unavailable:
            return "No DNS servers discovered"
        }
    }

    var body: some View {
        PurpleScrollView(geometry: .fullPanel) {
        VStack(alignment: .leading, spacing: 20) {
            // Warden header: rune tile + eyebrow + name + lore
            HStack(alignment: .top, spacing: 15) {
                Text("\u{16DC}")
                    .font(.custom("Cinzel-SemiBold", size: 28))
                    .frame(width: 54, height: 54)
                    .background(Color.ioSuccessGreen.opacity(0.10))
                    .cornerRadius(14)
                    .foregroundColor(Color.ioSuccessGreen)
                    .shadow(color: Color.ioSuccessGreen.opacity(0.35), radius: 22)
                    .accessibilityHidden(true)

                VStack(alignment: .leading, spacing: 4) {
                    Text("THE GATEKEEPER \u{00B7} LAN & ROUTE TRACE")
                        .font(.ioCaption(.bold))
                        .foregroundColor(Color.ioSuccessGreen)
                        .tracking(0.26)

                    Text("Portal")
                        .font(.custom("Cinzel-SemiBold", size: 26))
                        .foregroundColor(.ioText)

                    Text("A Portal carried the traveler to a distant land, tracing the path of a hidden interceptor that had reached into the stream.")
                        .font(.ioLore(16))
                        .foregroundColor(.ioTextMuted)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: 560)
                }
            }
            
            // Form input
            VStack(alignment: .leading, spacing: 10) {
                Text("Of all domains, to which shall the Portal deliver you today? The Portal traces the course your traffic must follow to reach that far host. Bearing you all the way, it delivers you along the traced route in less than a blink. (A traceroute follows the packets sent from your device to the domain or IP you chose, mapping the nodes, or hops, they pass through. If traffic is being redirected, it can help reveal where. If the last hop falls short of your domain, it may have been blocked by a firewall or swept into a web proxy.)")
                    .font(.ioCaption())
                    .foregroundColor(.ioTextSecondary)
                    .fixedSize(horizontal: false, vertical: true)

                HStack(spacing: 12) {
                    TextField("Domain (e.g., github.com)", text: $model.domain)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 220)

                    Button(action: {
                        model.selection = .portal
                        model.run()
                        model.startTraceroute()
                    }) {
                        if model.runningTool == .portal {
                            ProgressView().controlSize(.small)
                        } else {
                            Text("Check")
                        }
                    }
                    .buttonStyle(IoFilledButtonStyle())
                    .disabled(model.isRunning)
                    .accessibilityLabel("Run Portal network scan")
                }
            }
            .padding(14)
            .background(Color.ioSuccessGreen.opacity(0.05))
            .background(Color.ioSurfaceDark)
            .cornerRadius(10)
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.ioSuccessGreen.opacity(0.15), lineWidth: 1))
            
            // Resolvers & Interfaces Row
            HStack(spacing: 16) {
                // System Resolvers
                VStack(alignment: .leading, spacing: 8) {
                    Text("Resolvers:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioTextSecondary)
                        .textCase(.uppercase)
                    
                    // E119: prefer portalReport resolvers (measured at scan time) over the stale
                    // cache; fall back to cache only when no report is available. This matches
                    // DashboardView's precedence and prevents two panes from disagreeing.
                    let servers = model.portalReport?.dnsServers ?? model.cachedSystemDNSServers
                    if servers.isEmpty {
                        // D278: .checking must never render as a measured "no servers" message.
                        Text(PortalView.resolversPaneEmptyCopy(state: model.resolverState))
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                    } else {
                        PurpleScrollView(geometry: .compact) {
                            VStack(spacing: 6) {
                                ForEach(servers, id: \.self) { server in
                                    HStack {
                                        Text("DNS Server")
                                            .font(.ioCaption())
                                            .foregroundColor(.ioTextSecondary)
                                        Spacer()
                                        Text(server)
                                            .font(.ioMono(14))
                                            .foregroundColor(.ioText)
                                    }
                                }
                            }
                            .padding(.bottom, 16)
                        }
                    }
                }
                .padding(16)
                .frame(maxWidth: .infinity, maxHeight: 140, alignment: .leading)
                .background(Color.ioSuccessGreen.opacity(0.05))
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioSuccessGreen.opacity(0.15), lineWidth: 1))
                
                // Active Interfaces — real data from portalReport
                VStack(alignment: .leading, spacing: 8) {
                    Text("Active interfaces:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioTextSecondary)
                        .textCase(.uppercase)

                    let report = model.portalReport
                    if let r = report {
                        PurpleScrollView(geometry: .compact) {
                            VStack(spacing: 6) {
                                ForEach(r.activeInterfaces, id: \.self) { iface in
                                    HStack {
                                        Text(iface)
                                            .font(.ioCaption())
                                            .foregroundColor(.ioTextSecondary)
                                        Spacer()
                                    }
                                }

                                HStack {
                                    Text("VPN status:")
                                        .font(.ioCaption())
                                        .foregroundColor(.ioTextSecondary)
                                    Spacer()
                                    let vpnActive = r.vpn.state == .yes
                                    HStack(spacing: 6) {
                                        Circle()
                                            .fill(vpnActive ? Color.ioSuccessGreen : (r.vpn.state == .unknown ? Color.ioWarningAmber : Color.ioTextSecondary))
                                            .frame(width: 6, height: 6)
                                        Text(vpnActive ? "Yes • active" : (r.vpn.state == .unknown ? "Unknown" : "No tunnel"))
                                            .font(.ioSmall(.bold))
                                            .foregroundColor(vpnActive ? .ioSuccessGreen : (r.vpn.state == .unknown ? .ioWarningAmber : .ioTextSecondary))
                                    }
                                }
                            }
                            .padding(.bottom, 16)
                        }
                    } else {
                        Text("Run Portal to see active interfaces and VPN status.")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                    }
                }
                .padding(16)
                .frame(maxWidth: .infinity, maxHeight: 140, alignment: .leading)
                .background(Color.ioSuccessGreen.opacity(0.05))
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioSuccessGreen.opacity(0.15), lineWidth: 1))
            }
            
            // Side-by-side section: LAN Devices & Traceroute Hop Path
            HStack(alignment: .top, spacing: 20) {
                // Left side: LAN Discovery — not yet available in GUI
                VStack(alignment: .leading, spacing: 10) {
                    Text("Devices on network segment:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioTextSecondary)
                        .textCase(.uppercase)

                    Text("LAN device discovery is not yet available in the GUI. Run `syxx lan` from Terminal to enumerate devices on your network segment.")
                        .font(.ioBody())
                        .foregroundColor(.ioTextSecondary)
                        .lineLimit(nil)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(16)
                .background(Color.ioSuccessGreen.opacity(0.05))
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioSuccessGreen.opacity(0.15), lineWidth: 1))

                // Right side: Traceroute — real system traceroute, honest tri-state rendering.
                VStack(alignment: .leading, spacing: 10) {
                    Text("Route to chosen domain:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioTextSecondary)
                        .textCase(.uppercase)

                    switch model.traceState {
                    case .idle:
                        Text("Run Portal to trace the route to your chosen domain.")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                            .lineLimit(nil)
                    case .invalidTarget:
                        Text("Enter a valid domain or IP address above, then run Portal, to trace its route.")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                            .lineLimit(nil)
                    case .running:
                        HStack(spacing: 8) {
                            ProgressView().controlSize(.small)
                            Text("Tracing the route to \(model.domain)…")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                        }
                    case .failed(let reason):
                        Text("The traceroute could not run (\(reason)). This is not evidence of a clean path.")
                            .font(.ioBody())
                            .foregroundColor(.ioWarningAmber)
                            .fixedSize(horizontal: false, vertical: true)
                    case .completed:
                        VStack(alignment: .leading, spacing: 8) {
                            PurpleScrollView(geometry: .compact) {
                                traceHopRows
                                    .padding(.bottom, 16)
                            }
                            .frame(height: 140)

                            Text("If the trace stops early, the last responding hop is where interception or blocking begins.")
                                .font(.ioSmall())
                                .foregroundColor(.ioTextSecondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(16)
                .background(Color.ioSuccessGreen.opacity(0.05))
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioSuccessGreen.opacity(0.15), lineWidth: 1))
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private var traceHopRows: some View {
        VStack(alignment: .leading, spacing: 3) {
            ForEach(Array(model.traceHops.enumerated()), id: \.offset) { _, hop in
                Text(hop)
                    .font(.ioMono(12))
                    .foregroundColor(.ioText)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
    }
}

#if DEBUG
#Preview {
    PortalView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .portal))
        .syxxPreviewCanvas()
}
#endif
