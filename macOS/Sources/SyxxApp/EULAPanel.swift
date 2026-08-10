import SwiftUI

/// Read-only access to the same bundled agreement bytes used by first-run acceptance.
struct EULAView: View {
    var menuPresented: Bool = true
    @Environment(\.dismiss) private var dismiss

    private var agreementText: String {
        switch BundledAgreement.load() {
        case .success(let agreement):
            return agreement.text
        case .failure:
            return "The End-User License Agreement is unavailable in this build."
        }
    }

    var body: some View {
        // Mockup: 600px card, rgba(28,17,52,.85) + backdrop-filter:blur(24px),
        // border rgba(160,140,255,.24), radius 18, shadow 0 40px 100px rgba(0,0,0,.6).
        VStack(spacing: 0) {
            // Header: icon + Cinzel title + EB Garamond italic subtitle
            VStack(spacing: 5) {
                Text("⚖").font(.system(size: 30))
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

            Divider().overlay(Color.ioVioletDivider.opacity(0.14))

            // Agreement body: EB Garamond 14.5px per mockup, scrollable
            PurpleScrollView(geometry: .compact) {
                Text(agreementText)
                    .font(.custom("EBGaramond-Regular", size: 14.5))
                    .foregroundStyle(Color.ioTextSecondary)
                    .lineSpacing(3)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 30)
                    .padding(.vertical, 20)
            }
            .frame(maxHeight: 320)

            Divider().overlay(Color.ioVioletDivider.opacity(0.14))

            // Footer: Decline text link + Agree & Continue gradient button
            HStack {
                Button("Decline") { dismiss() }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.ioTextMuted)

                Spacer()

                Button("Agree & Continue →") { dismiss() }
                    .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                    .keyboardShortcut(.defaultAction)
            }
            .padding(.horizontal, 30)
            .padding(.vertical, 16)
            .background(Color(red: 10/255, green: 6/255, blue: 20/255).opacity(0.35))
        }
        .frame(width: 600)
        .background(.ultraThinMaterial)
        .background(Color(red: 28/255, green: 17/255, blue: 52/255).opacity(0.85))
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color(red: 160/255, green: 140/255, blue: 255/255).opacity(0.24), lineWidth: 1))
        .shadow(color: .black.opacity(0.6), radius: 50, y: 24)
    }
}

#if DEBUG
#Preview {
    EULAView(menuPresented: true)
        .syxxPreviewPanel()
}
#endif
