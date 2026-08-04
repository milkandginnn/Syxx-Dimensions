import SwiftUI

struct BetaView: View {
    var body: some View {
        // Remote MOTD first. On timeout/failure, RemoteContentView (motd mode) loads
        // Resources/fallback-motd.html in the same WebView (no other local HTML substitute).
        RemoteContentView(
            url: DimensionService.shared.fetchWhatsNewURL()
                ?? URL(string: "https://macmotd.syxx.app/")!,
            mode: .motd
        )
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

#if DEBUG
#Preview {
    BetaView()
        .syxxPreviewCanvas()
}
#endif
