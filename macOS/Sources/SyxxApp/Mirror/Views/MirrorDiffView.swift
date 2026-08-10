import SwiftUI
import SyxxKit

/// Unified diff display component for Mirror comparisons.
///
/// Shows line-by-line differences with:
/// - Green (#2dd4bf) for added lines
/// - Red (#ff4d6d) for removed lines
/// - Context lines in muted purple (#8f83b8)
/// - Line numbers
/// - Expandable sections: HTML | CSS | JavaScript
struct MirrorDiffView: View {
    /// The diff result to display.
    let diffResult: DiffResult

    /// Which sections are currently expanded.
    @State private var expandedSections: Set<DiffSection> = [.html]

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Section tabs
            sectionTabs

            // Diff content for expanded sections
            ScrollView([.vertical, .horizontal]) {
                VStack(alignment: .leading, spacing: 0) {
                    ForEach(DiffSection.allCases, id: \.self) { section in
                        if expandedSections.contains(section) {
                            sectionContent(section)
                        }
                    }

                    if !diffResult.hasChanges {
                        noChangesView
                    }
                }
                .padding(12)
            }
        }
        .background(Color.ioBackgroundDark)
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(Color.ioBorderSubtle, lineWidth: 1)
        )
    }

    // MARK: - Section Tabs

    private var sectionTabs: some View {
        HStack(spacing: 0) {
            ForEach(DiffSection.allCases, id: \.self) { section in
                let lines = diffResult.lines(for: section)
                let changes = lines.filter { $0.kind != .context }.count

                Button {
                    withAnimation(.easeInOut(duration: 0.15)) {
                        if expandedSections.contains(section) {
                            expandedSections.remove(section)
                        } else {
                            expandedSections.insert(section)
                        }
                    }
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: expandedSections.contains(section) ? "chevron.down" : "chevron.right")
                            .font(.system(size: 9))
                        Text(section.rawValue)
                            .font(.system(size: 11, weight: .medium, design: .monospaced))
                        if changes > 0 {
                            Text("(\(changes))")
                                .font(.system(size: 10, design: .monospaced))
                                .foregroundColor(changes > 0 ? .ioCriticalRed : .ioEyebrowMuted)
                        }
                    }
                    .foregroundColor(expandedSections.contains(section) ? .ioText : .ioTextMuted)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(
                        expandedSections.contains(section)
                            ? Color.ioBackgroundDark
                            : Color.ioSurfaceDark
                    )
                }
                .buttonStyle(.plain)

                if section != DiffSection.allCases.last {
                    Rectangle()
                        .fill(Color.ioBorderSubtle)
                        .frame(width: 1)
                }
            }

            Spacer()

            // Summary
            HStack(spacing: 12) {
                Label("\(diffResult.totalAdditions)", systemImage: "plus")
                    .foregroundColor(.ioSuccessGreen)
                Label("\(diffResult.totalRemovals)", systemImage: "minus")
                    .foregroundColor(.ioCriticalRed)
            }
            .font(.system(size: 11, design: .monospaced))
            .padding(.trailing, 14)
        }
        .background(Color.ioSurfaceDark)
        .overlay(
            Rectangle()
                .fill(Color.ioBorderSubtle)
                .frame(height: 1),
            alignment: .bottom
        )
    }

    // MARK: - Section Content

    @ViewBuilder
    private func sectionContent(_ section: DiffSection) -> some View {
        let lines = diffResult.lines(for: section)
        if lines.isEmpty {
            Text("No differences in \(section.rawValue)")
                .font(.system(size: 11, design: .monospaced))
                .foregroundColor(.ioEyebrowMuted)
                .padding(.vertical, 8)
        } else {
            ForEach(lines) { line in
                diffLineRow(line)
            }
        }
    }

    private func diffLineRow(_ line: DiffLine) -> some View {
        HStack(alignment: .top, spacing: 0) {
            // Old line number
            Text(line.oldLineNumber.map { "\($0)" } ?? "")
                .font(.system(size: 10, design: .monospaced))
                .foregroundColor(.ioTextFaint)
                .frame(width: 36, alignment: .trailing)
                .padding(.trailing, 4)

            // New line number
            Text(line.newLineNumber.map { "\($0)" } ?? "")
                .font(.system(size: 10, design: .monospaced))
                .foregroundColor(.ioTextFaint)
                .frame(width: 36, alignment: .trailing)
                .padding(.trailing, 8)

            // Kind indicator
            Text(prefix(for: line.kind))
                .font(.system(size: 12, weight: .bold, design: .monospaced))
                .foregroundColor(color(for: line.kind))
                .frame(width: 14)

            // Content
            Text(line.text.isEmpty ? " " : line.text)
                .font(.system(size: 12, design: .monospaced))
                .foregroundColor(color(for: line.kind))
                .textSelection(.enabled)

            Spacer(minLength: 0)
        }
        .padding(.vertical, 1)
        .background(background(for: line.kind))
    }

    private var noChangesView: some View {
        HStack(spacing: 8) {
            Image(systemName: "checkmark.circle.fill")
                .foregroundColor(.ioSuccessGreen)
            Text("No differences detected — remote and local content are identical.")
                .font(.system(size: 12, design: .monospaced))
                .foregroundColor(.ioSuccessGreen)
        }
        .padding(.vertical, 16)
    }

    // MARK: - Styling Helpers

    private func prefix(for kind: DiffLineKind) -> String {
        switch kind {
        case .context: return " "
        case .added: return "+"
        case .removed: return "−"
        }
    }

    private func color(for kind: DiffLineKind) -> Color {
        switch kind {
        case .context: return .ioEyebrowMuted
        case .added: return .ioSuccessGreen
        case .removed: return .ioCriticalRed
        }
    }

    private func background(for kind: DiffLineKind) -> Color {
        switch kind {
        case .context: return .clear
        case .added: return Color.ioSuccessGreen.opacity(0.06)
        case .removed: return Color.ioCriticalRed.opacity(0.06)
        }
    }
}

#if DEBUG
#Preview("MirrorDiffView") {
    let htmlDiff = [
        DiffLine(id: 0, kind: .context, text: "<!DOCTYPE html>", oldLineNumber: 1, newLineNumber: 1),
        DiffLine(id: 1, kind: .context, text: "<html>", oldLineNumber: 2, newLineNumber: 2),
        DiffLine(id: 2, kind: .removed, text: "  <script src=\"https://cdn.example.com/analytics.js\">", oldLineNumber: 3, newLineNumber: nil),
        DiffLine(id: 3, kind: .added, text: "  <script src=\"https://evil.example.com/track.js\">", oldLineNumber: nil, newLineNumber: 3),
        DiffLine(id: 4, kind: .context, text: "</html>", oldLineNumber: 4, newLineNumber: 4),
    ]
    let cssDiff: [DiffLine] = []
    let jsDiff = [
        DiffLine(id: 10, kind: .removed, text: "console.log('original');", oldLineNumber: 1, newLineNumber: nil),
        DiffLine(id: 11, kind: .added, text: "console.log('injected');", oldLineNumber: nil, newLineNumber: 1),
    ]

    MirrorDiffView(diffResult: DiffResult(htmlDiff: htmlDiff, cssDiff: cssDiff, jsDiff: jsDiff))
        .frame(width: 700, height: 350)
        .background(Color.ioSurfaceDark)
}
#endif
