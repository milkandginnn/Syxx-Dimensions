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
        VStack(spacing: 0) {
            VStack(spacing: 5) {
                Text("⚖").font(.system(size: 30))
                Text("End-User License Agreement")
                    .font(.custom("Cinzel-SemiBold", size: 22))
                    .foregroundStyle(Color.ioText)
                Text("Please read these terms before continuing.")
                    .font(.custom("EBGaramond-Italic", size: 14))
                    .foregroundStyle(Color.ioTextSecondary)
            }
            .padding(.vertical, 14)
            .frame(maxWidth: .infinity)

            Divider().overlay(Color.ioBorderSubtle)

            PurpleScrollView(geometry: .compact) {
                Text(agreementText)
                    .font(.body(14.5))
                    .foregroundStyle(Color.ioText)
                    .lineSpacing(3)
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(24)
            }

            Divider().overlay(Color.ioBorderSubtle)

            HStack {
                Spacer()
                Button("Close") { dismiss() }
                    .buttonStyle(IoFilledButtonStyle(color: .ioBlue))
                    .keyboardShortcut(.defaultAction)
            }
            .padding(16)
        }
        .frame(width: 600, height: 520)
        .background(Color.ioSurfaceDark)
    }
}

#if DEBUG
#Preview {
    EULAView(menuPresented: true)
        .syxxPreviewPanel()
}
#endif
