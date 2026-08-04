import SwiftUI
import SyxxKit

// Opened from Syxx menu → "About Syxx". Lives in its own NSPanel.
struct AboutView: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 0) {
            // ── Content ──────────────────────────────────────────────────────
            VStack(spacing: 16) {
                LogoView(size: 96)
                    .padding(.top, 32)

                VStack(spacing: 4) {
                    Text("Syxx")
                        .font(.custom("Cinzel-Bold", size: 32))
                        .foregroundStyle(
                            LinearGradient(colors: [.ioAccentStart, .ioAccentEnd],
                                           startPoint: .leading, endPoint: .trailing)
                        )
                    Text("\"Lyin' Eyes\"")
                        .font(.custom("EBGaramond-Italic", size: 15))
                        .foregroundColor(.ioText)
                }

                VStack(spacing: 2) {
                    Text("Version \(SyxxConfig.version)  ·  Build \(buildString)")
                        .font(.ioMono(13))
                        .foregroundColor(.ioTextSecondary)
                    Text("DNS Investigation Suite")
                        .font(.ioBody())
                    
                        .foregroundColor(.ioTextSecondary)
                }
                
                Divider()
                    .background(Color.ioBorderSubtle)
                    .padding(.horizontal, 40)
                    .padding(.vertical, 4)

                VStack(spacing: 4) {
                    Text("© Joel \"J.J.\" Warne 2026. All Rights Reserved.")
                        .font(.ioSmall())
                        .foregroundColor(.ioTextSecondary)
                    Text("MilkandGin")
                        .font(.ioSmall())
                        .foregroundColor(.ioTextSecondary)
                    HStack(spacing: 16) {
                        linkButton("syxx.app", url: "https://syxx.app")
                        linkButton("milkandgin.com", url: "https://milkandgin.com")
                    }
                    .padding(.top, 2)
                }
            }
            .frame(maxWidth: .infinity)
            .multilineTextAlignment(.center)
            .padding(.horizontal, 32)

            Spacer(minLength: 24)

            // ── Button bar ───────────────────────────────────────────────────
            Divider().background(Color.ioBorderSubtle)
            HStack(spacing: 12) {
                Button("Acknowledgements") {
                    NSWorkspace.shared.open(URL(string: "https://syxx.app/acknowledgements")!)
                }
                .buttonStyle(IoOutlineButtonStyle())

                Button("OK") { dismiss() }
                    .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 16)
        }
        .frame(width: 480)
        .background(Color.ioSurfaceDark)
    }

    private func linkButton(_ label: String, url: String) -> some View {
        Button(label) { NSWorkspace.shared.open(URL(string: url)!) }
            .buttonStyle(.plain)
            .font(.ioSmall())
            .foregroundColor(.ioBlue)
    }

    // E033: never fabricate a build number from today's date — a changing identifier is not a
    // stable build identifier and impairs support/evidence reproducibility. Display "unknown"
    // when the compiled bundle carries no CFBundleVersion, matching Windows behavior.
    private var buildString: String {
        if let b = Bundle.main.infoDictionary?["CFBundleVersion"] as? String { return b }
        return "unknown"
    }
}

#if DEBUG
#Preview {
    AboutView()
        .syxxPreviewPanel()
}
#endif
