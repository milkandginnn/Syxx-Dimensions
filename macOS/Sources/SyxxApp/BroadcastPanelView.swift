import SwiftUI
import SyxxKit

/// Shown once per broadcast id in an NSPanel — the Dimension service may publish a MOTD with
/// severity, title, body, and optional link. User dismisses it and it never re-appears.
struct BroadcastPanelView: View {
    @Environment(\.dismiss) private var dismissPanel
    let broadcast: Broadcast
    let onDismiss: () -> Void

    @State private var alertMessage: String?
    @State private var isShowingAlert = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 16) {
                // Severity indicator + title
                HStack(spacing: 10) {
                    Circle()
                        .fill(severityColor)
                        .frame(width: 10, height: 10)
                    Text(broadcast.title)
                        .font(.uiLabel(17))
                        .foregroundColor(.ioText)
                }

                // Body
                Text(broadcast.body)
                    .font(.ioBody())
                    .foregroundColor(.ioTextSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 24)
            .padding(.top, 24)

            Spacer(minLength: 16)

            // Button bar
            Divider().background(Color.ioBorderSubtle)
            HStack(spacing: 10) {
                if let urlStr = broadcast.url, let url = URL(string: urlStr) {
                    Button(action: {
                        let eval = RemoteUrlPolicy.evaluate(urlStr)
                        if eval.isAllowed {
                            NSWorkspace.shared.open(url)
                        } else {
                            alertMessage = eval.reason
                            isShowingAlert = true
                        }
                    }) {
                        Text("Learn More")
                    }
                    .buttonStyle(IoOutlineButtonStyle())
                }

                Spacer()

                Button("Dismiss") {
                    onDismiss()
                    dismissPanel()
                }
                .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                .keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 14)
        }
        .frame(width: 420)
        .background(Color.ioSurfaceDark)
        .alert("Link Blocked", isPresented: $isShowingAlert) {
            Button("OK", role: .cancel) { }
        } message: {
            Text(alertMessage ?? "This link is not authorized to be opened.")
        }
    }

    private var severityColor: Color {
        switch broadcast.severity.lowercased() {
        case "critical": return .ioCriticalRed
        case "warning":  return .ioWarningAmber
        case "info":     return .ioInfoPurple
        default:         return .ioTextSecondary
        }
    }
}

#if DEBUG
#Preview {
    BroadcastPanelView(broadcast: SyxxPreviewSupport.sampleBroadcast, onDismiss: {})
        .syxxPreviewPanel()
}
#endif
