import SwiftUI
import SyxxKit

/// A macOS Terminal.app-style pane for displaying Mirror bundle content.
///
/// Renders:
/// - Title bar with red/yellow/green traffic light dots
/// - Dark background (#0b0518)
/// - Monospace font (Menlo 12pt)
/// - Prompt line: `$ curl <domain>` in teal (#2dd4bf)
/// - Scrollable content area showing concatenated HTML+CSS+JS
struct MirrorTerminalView: View {
    /// Title shown in the terminal title bar (e.g. "Remote (Server)" or "Local (Your Mac)").
    let title: String
    /// The domain that was fetched (shown in the prompt line).
    let domain: String
    /// The bundle to display. If nil, shows a placeholder.
    let bundle: MirrorBundle?
    /// Optional warning to display (G-02 fallback).
    let warning: String?

    init(title: String, domain: String, bundle: MirrorBundle?, warning: String? = nil) {
        self.title = title
        self.domain = domain
        self.bundle = bundle
        self.warning = warning
    }

    var body: some View {
        VStack(spacing: 0) {
            // Title bar
            terminalTitleBar

            // Content area
            ScrollView([.vertical, .horizontal]) {
                VStack(alignment: .leading, spacing: 0) {
                    // Warning banner (G-02 fallback)
                    if let warning {
                        warningBanner(warning)
                    }

                    // Prompt line
                    promptLine

                    if let bundle {
                        // Summary line
                        Text(bundle.summaryLine)
                            .font(.system(.caption, design: .monospaced))
                            .foregroundColor(.ioEyebrowMuted)
                            .padding(.bottom, 6)

                        // Content
                        contentBlock
                    } else {
                        placeholderContent
                    }
                }
                .padding(16)
            }
        }
        .background(Color.ioBackgroundDark)
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(Color.ioBorderSubtle, lineWidth: 1)
        )
    }

    // MARK: - Title Bar

    private var terminalTitleBar: some View {
        HStack(spacing: 8) {
            // Traffic light dots
            HStack(spacing: 6) {
                Circle().fill(Color(red: 1.0, green: 0.36, blue: 0.36)).frame(width: 12, height: 12)
                Circle().fill(Color(red: 1.0, green: 0.78, blue: 0.25)).frame(width: 12, height: 12)
                Circle().fill(Color(red: 0.29, green: 0.84, blue: 0.39)).frame(width: 12, height: 12)
            }

            Spacer()

            Text(title)
                .font(.system(size: 11, weight: .medium, design: .monospaced))
                .foregroundColor(.ioTextMuted)

            Spacer()

            // Balance spacer for centering
            HStack(spacing: 6) {
                Circle().fill(Color.clear).frame(width: 12, height: 12)
                Circle().fill(Color.clear).frame(width: 12, height: 12)
                Circle().fill(Color.clear).frame(width: 12, height: 12)
            }
            .opacity(0)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Color.ioSurfaceDark)
        .overlay(
            Rectangle()
                .fill(Color.ioBorderSubtle)
                .frame(height: 1),
            alignment: .bottom
        )
    }

    // MARK: - Prompt Line

    private var promptLine: some View {
        HStack(spacing: 4) {
            Text("$")
                .foregroundColor(Color.ioSuccessGreen)
            Text("curl \(domain.isEmpty ? "<domain>" : domain)")
                .foregroundColor(Color.ioSuccessGreen)
        }
        .font(.system(size: 12, design: .monospaced))
        .padding(.bottom, 8)
    }

    // MARK: - Content

    @ViewBuilder
    private var contentBlock: some View {
        let b = bundle!
        // HTML section
        if !b.htmlContent.isEmpty {
            sectionHeader("HTML")
            codeBlock(b.htmlContent)
        }
        // CSS section
        if !b.cssContent.isEmpty {
            sectionHeader("CSS")
            codeBlock(b.cssContent)
        }
        // JS section
        if !b.jsContent.isEmpty {
            sectionHeader("JavaScript")
            codeBlock(b.jsContent)
        }
        // SHA-256 hash
        if !b.metadata.serverHash.isEmpty {
            HStack(spacing: 6) {
                Text("SHA-256:")
                    .foregroundColor(.ioEyebrowMuted)
                Text(String(b.metadata.serverHash.prefix(16)) + "…")
                    .foregroundColor(.ioTextSecondary)
                    .textSelection(.enabled)
            }
            .font(.system(size: 10, design: .monospaced))
            .padding(.top, 8)
        }
    }

    private var placeholderContent: some View {
        Text("No content captured yet.\nRun a Mirror check to populate this pane.")
            .font(.system(size: 12, design: .monospaced))
            .foregroundColor(.ioTextFaint)
            .padding(.top, 8)
    }

    // MARK: - Helpers

    private func sectionHeader(_ name: String) -> some View {
        Text("── \(name) " + String(repeating: "─", count: max(1, 40 - name.count)))
            .font(.system(size: 10, weight: .medium, design: .monospaced))
            .foregroundColor(.ioEyebrowMuted)
            .padding(.top, 10)
            .padding(.bottom, 4)
    }

    private func codeBlock(_ text: String) -> some View {
        let lines = text.components(separatedBy: "\n")
        return VStack(alignment: .leading, spacing: 1) {
            ForEach(Array(lines.prefix(500).enumerated()), id: \.offset) { idx, line in
                HStack(alignment: .top, spacing: 8) {
                    Text("\(idx + 1)")
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundColor(.ioTextFaint)
                        .frame(width: 36, alignment: .trailing)
                    Text(line.isEmpty ? " " : line)
                        .font(.system(size: 12, design: .monospaced))
                        .foregroundColor(.ioText)
                        .textSelection(.enabled)
                }
            }
            if lines.count > 500 {
                Text("… (\(lines.count - 500) more lines truncated)")
                    .font(.system(size: 10, design: .monospaced))
                    .foregroundColor(.ioTextFaint)
            }
        }
    }

    private func warningBanner(_ text: String) -> some View {
        HStack(spacing: 6) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundColor(.ioWarningAmber)
            Text(text)
                .font(.system(size: 11, design: .monospaced))
                .foregroundColor(.ioWarningAmber)
        }
        .padding(8)
        .background(Color.ioWarningAmber.opacity(0.1))
        .cornerRadius(4)
        .padding(.bottom, 8)
    }
}

#if DEBUG
#Preview("MirrorTerminalView - Remote") {
    MirrorTerminalView(
        title: "Remote (Server)",
        domain: "example.com",
        bundle: MirrorBundle(
            source: .remote,
            domain: "example.com",
            timestamp: Date(),
            htmlContent: "<!DOCTYPE html>\n<html>\n<head><title>Example</title></head>\n<body><h1>Hello</h1></body>\n</html>",
            cssContent: "body { font-family: sans-serif; }\nh1 { color: blue; }",
            jsContent: "console.log('hello');\ndocument.title = 'Loaded';",
            metadata: BundleMetadata(
                fetchDuration: 0.42,
                byteSize: 256,
                resourceCount: 2,
                signature: "abc123def456",
                serverHash: "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456"
            )
        ),
        warning: nil
    )
    .frame(width: 450, height: 400)
    .background(Color.ioSurfaceDark)
}

#Preview("MirrorTerminalView - Empty") {
    MirrorTerminalView(
        title: "Local (Your Mac)",
        domain: "example.com",
        bundle: nil,
        warning: nil
    )
    .frame(width: 450, height: 400)
    .background(Color.ioSurfaceDark)
}
#endif
