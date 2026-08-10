import SwiftUI
import SyxxKit

/// Badge displaying the degradation score with color-coded classification.
///
/// Color coding:
/// - Identical: green (ioSuccessGreen)
/// - Minor: green-yellow (ioSuccessGreen with slight amber)
/// - Moderate: amber (ioWarningAmber)
/// - Significant: orange-red (ioHighOrange)
/// - Critical: red (ioCriticalRed)
struct MirrorDegradationBadge: View {
    let score: DegradationScore

    var body: some View {
        HStack(spacing: 10) {
            // Score circle
            ZStack {
                Circle()
                    .stroke(background, lineWidth: 3)
                    .frame(width: 40, height: 40)
                Circle()
                    .trim(from: 0, to: score.score)
                    .stroke(foreground, style: StrokeStyle(lineWidth: 3, lineCap: .round))
                    .frame(width: 40, height: 40)
                    .rotationEffect(.degrees(-90))
                Text("\(score.percentage)%")
                    .font(.system(size: 10, weight: .bold, design: .monospaced))
                    .foregroundColor(foreground)
            }

            // Label
            VStack(alignment: .leading, spacing: 2) {
                Text("DEGRADATION")
                    .font(.system(size: 9, weight: .semibold, design: .monospaced))
                    .foregroundColor(.ioEyebrowMuted)
                    .tracking(1.5)
                Text(score.classification.label)
                    .font(.system(size: 13, weight: .semibold, design: .monospaced))
                    .foregroundColor(foreground)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(background.opacity(0.1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(foreground.opacity(0.3), lineWidth: 1)
        )
    }

    private var foreground: Color {
        switch score.classification {
        case .identical: return .ioSuccessGreen
        case .minorDifferences: return .ioSuccessGreen
        case .moderateDifferences: return .ioWarningAmber
        case .significantDifferences: return .ioHighOrange
        case .criticalDifferences: return .ioCriticalRed
        }
    }

    private var background: Color {
        foreground.opacity(0.35)
    }
}

#if DEBUG
#Preview("Degradation Badges") {
    VStack(spacing: 12) {
        MirrorDegradationBadge(score: DegradationScore(score: 0.0))
        MirrorDegradationBadge(score: DegradationScore(score: 0.05))
        MirrorDegradationBadge(score: DegradationScore(score: 0.23))
        MirrorDegradationBadge(score: DegradationScore(score: 0.45))
        MirrorDegradationBadge(score: DegradationScore(score: 0.82))
    }
    .padding(20)
    .background(Color.ioSurfaceDark)
}
#endif
