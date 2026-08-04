import SwiftUI
import SyxxKit

// Renamed in spirit even though the file/type name is unchanged (deeper rename is a separate,
// cross-platform pass — see the delta filed for this). This view no longer renders a diagnosis,
// a result badge, a confidence score, or a hypothesis-probability distribution. Those numbers
// come from a synthesis engine the owner has directed be removed; showing them here — even
// correctly — would still be presenting a determinative claim this product no longer makes.
// This view now shows only what actually happened: which checks ran, what they observed, and
// any raw exposure findings. No diagnosis. No result. The reader draws their own conclusion.
struct OverallResultView: View {
    let conclusion: Conclusion

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            // Header — factual only: when the report was generated, and a real file save.
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 8) {
                    Text("Report")
                        .font(.ioTitle2())
                        .foregroundColor(.ioText)
                    Text("Generated at \(conclusion.generatedAt, style: .time) on \(conclusion.generatedAt, style: .date)")
                        .font(.ioCaption())
                        .foregroundColor(.ioTextSecondary)
                }
                Spacer()
                Button(action: saveReportToFile) {
                    Label("Save Report…", systemImage: "square.and.arrow.down")
                }
                .buttonStyle(IoOutlineButtonStyle())
            }

            Divider().background(Color.ioBorderSubtle)

            // Determination — the matrix determinative engine's tri-state result PLUS its
            // evidentiary basis. Honesty rule: the result is always shown together with the raw
            // "Checks Run" / "Evidence" sections below, never on its own. Copy shown here is the
            // engine's OWN output (state + per-diagnosis displayName/explanation); final label/wording
            // polish is a gui-architect/approval item, not finalised here.
            if let det = conclusion.determination {
                determinationSection(det)
                Divider().background(Color.ioBorderSubtle)
            }

            // Section execution status — did each check run, not what it means.
            VStack(alignment: .leading, spacing: 8) {
                Text("Checks Run")
                    .font(.ioCaption(.bold))
                    .foregroundColor(.ioTextSecondary)
                    .textCase(.uppercase)

                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 10) {
                    ForEach(conclusion.sectionStatuses, id: \.section.rawValue) { entry in
                        HStack {
                            Image(systemName: sectionIcon(entry.section))
                                .foregroundColor(.ioTextSecondary)
                                .frame(width: 16)
                            Text(entry.section.rawValue.capitalized)
                                .font(.ioBody())
                                .foregroundColor(.ioText)
                            Spacer()
                            statusBadge(entry.status)
                        }
                        .padding(8)
                        .background(Color.ioSurfaceDark)
                        .cornerRadius(6)
                        .overlay(
                            RoundedRectangle(cornerRadius: 6)
                                .stroke(Color.ioBorderSubtle, lineWidth: 1)
                        )
                    }
                }
            }

            // Raw evidence — what each check actually observed, not what it "means."
            if !conclusion.evidence.isEmpty {
                Divider().background(Color.ioBorderSubtle)
                VStack(alignment: .leading, spacing: 10) {
                    Text("Evidence")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioTextSecondary)
                        .textCase(.uppercase)

                    ForEach(0..<conclusion.evidence.count, id: \.self) { idx in
                        let item = conclusion.evidence[idx]
                        VStack(alignment: .leading, spacing: 6) {
                            HStack {
                                Image(systemName: sectionIcon(item.section))
                                    .foregroundColor(.ioTextSecondary)
                                Text("\(item.section.rawValue.capitalized) check")
                                    .font(.ioCaption(.semibold))
                                    .foregroundColor(.ioTextSecondary)
                                Spacer()
                            }
                            Text(item.observation)
                                .font(.ioBody(.bold))
                                .foregroundColor(.ioText)
                            Text(item.rationale)
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                                .lineLimit(nil)
                        }
                        .padding(12)
                        .background(Color.ioSurfaceDark)
                        .cornerRadius(8)
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(Color.ioBorderSubtle, lineWidth: 1)
                        )
                    }
                }
            }

            // Exposure/hygiene findings — already explicitly non-diagnostic in the model
            // ("surfaced prominently but NEVER folded into the tamper result" — Conclusion.swift).
            if !conclusion.advisories.isEmpty {
                Divider().background(Color.ioBorderSubtle)
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 8) {
                        Image(systemName: "exclamationmark.shield.fill")
                            .foregroundColor(.ioWarningAmber)
                        Text("Exposure Findings")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioTextSecondary)
                            .textCase(.uppercase)
                    }
                    ForEach(conclusion.advisories, id: \.advisoryId.rawValue) { advisory in
                        HStack(alignment: .top, spacing: 10) {
                            Circle()
                                .fill(advisoryColor(advisory.severity))
                                .frame(width: 8, height: 8)
                                .padding(.top, 6)
                            VStack(alignment: .leading, spacing: 4) {
                                Text(advisory.title)
                                    .font(.ioBody(.bold))
                                    .foregroundColor(.ioText)
                                Text(advisory.detail)
                                    .font(.ioCaption())
                                    .foregroundColor(.ioTextSecondary)
                                Text(advisory.section.rawValue.capitalized + " • " + advisory.advisoryId.rawValue)
                                    .font(.ioMono(10))
                                    .foregroundColor(.ioTextSecondary)
                            }
                        }
                        .padding(12)
                        .background(Color.ioSurfaceDark)
                        .cornerRadius(8)
                        .overlay(
                            RoundedRectangle(cornerRadius: 8)
                                .stroke(advisoryColor(advisory.severity).opacity(0.3), lineWidth: 1)
                        )
                    }
                }
            }

            // Incomplete-report note — factual (which checks didn't run), not a result on what
            // that means. Replaces the old "hard gate" warning and "Result is Ambiguous" banner.
            if conclusion.hasHardGatedSection {
                Divider().background(Color.ioBorderSubtle)
                HStack(spacing: 10) {
                    Image(systemName: "info.circle.fill")
                        .foregroundColor(.ioTextSecondary)
                    Text("Some checks did not run. This report reflects only what was actually measured.")
                        .font(.ioBody())
                        .foregroundColor(.ioTextSecondary)
                }
                .padding(12)
                .background(Color.ioSurfaceDark)
                .cornerRadius(8)
            }
        }
        .padding(18)
        .background(Color.ioSurfaceDark)
        .cornerRadius(12)
        .shadow(color: .black.opacity(0.2), radius: 8, y: 4)
        .overlay(
            RoundedRectangle(cornerRadius: 12)
                .stroke(Color.ioBorderSubtle, lineWidth: 1)
        )
    }

    // MARK: - Determination rendering

    @ViewBuilder
    private func determinationSection(_ det: DeterminativeResult) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Determination")
                .font(.ioCaption(.bold))
                .foregroundColor(.ioTextSecondary)
                .textCase(.uppercase)

            // State banner — never green unless affirmatively clean.
            HStack(alignment: .top, spacing: 10) {
                Image(systemName: stateIcon(det.state))
                    .foregroundColor(stateColor(det.state))
                    .font(.system(size: 18, weight: .semibold))
                VStack(alignment: .leading, spacing: 4) {
                    Text(stateHeadline(det.state))
                        .font(.ioBody(.bold))
                        .foregroundColor(stateColor(det.state))
                    Text(det.explanation)
                        .font(.ioCaption())
                        .foregroundColor(.ioTextSecondary)
                        .lineLimit(nil)
                }
                Spacer()
            }
            .padding(12)
            .background(Color.ioSurfaceDark)
            .cornerRadius(8)
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(stateColor(det.state).opacity(0.4), lineWidth: 1)
            )

            // Emitted diagnoses — only diagnoses that affirmatively reached probable/conclusive.
            ForEach(0..<det.diagnoses.count, id: \.self) { idx in
                let dx = det.diagnoses[idx]
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text(dx.displayName)
                            .font(.ioBody(.bold))
                            .foregroundColor(.ioText)
                        Spacer()
                        StatusPill(label: dx.confidence.rawValue.uppercased(),
                                   color: diagnosisColor(state: det.state, confidence: dx.confidence))
                    }
                    Text(dx.explanation)
                        .font(.ioCaption())
                        .foregroundColor(.ioTextSecondary)
                        .lineLimit(nil)
                }
                .padding(12)
                .background(Color.ioSurfaceDark)
                .cornerRadius(8)
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(Color.ioBorderSubtle, lineWidth: 1)
                )
            }

            // Honesty overhang — threat signals seen but not verifiable. Must never be hidden.
            if !det.suppressed.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    HStack(spacing: 8) {
                        Image(systemName: "questionmark.diamond.fill")
                            .foregroundColor(.ioWarningAmber)
                        Text("Unverified Signals")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioTextSecondary)
                            .textCase(.uppercase)
                    }
                    ForEach(0..<det.suppressed.count, id: \.self) { idx in
                        let s = det.suppressed[idx]
                        VStack(alignment: .leading, spacing: 2) {
                            Text(s.displayName)
                                .font(.ioBody(.semibold))
                                .foregroundColor(.ioText)
                            Text(s.detail)
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                                .lineLimit(nil)
                        }
                    }
                }
                .padding(12)
                .background(Color.ioSurfaceDark)
                .cornerRadius(8)
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .stroke(Color.ioWarningAmber.opacity(0.3), lineWidth: 1)
                )
            }
        }
    }

    /// Diagnosis pill colour: green tiers for clean results, red/orange for threats only.
    private func diagnosisColor(state: DeterminativeState, confidence: DiagnosisConfidence) -> Color {
        switch state {
        case .clean:
            return confidence == .conclusive ? .ioSuccessGreen : Color(red: 45/255, green: 212/255, blue: 191/255)
        case .threatDetected:
            return confidence == .conclusive ? .ioCriticalRed : .ioHighOrange
        case .inconclusive, .untested:
            return .ioInfoPurple
        }
    }

    private func stateColor(_ state: DeterminativeState) -> Color {
        switch state {
        case .clean:          return .ioSuccessGreen
        case .threatDetected: return .ioCriticalRed
        case .inconclusive:   return .ioInfoPurple   // never green — honest abstention
        case .untested:       return .ioInfoPurple   // not yet evaluated
        }
    }

    private func stateIcon(_ state: DeterminativeState) -> String {
        switch state {
        case .clean:          return "checkmark.shield.fill"
        case .threatDetected: return "exclamationmark.shield.fill"
        case .inconclusive:   return "questionmark.circle.fill"
        case .untested:       return "circle.dashed"
        }
    }

    private func stateHeadline(_ state: DeterminativeState) -> String {
        switch state {
        case .clean:          return "No tampering detected"
        case .threatDetected: return "Tampering detected"
        case .inconclusive:   return "Inconclusive"
        case .untested:       return "Not yet tested"
        }
    }

    // MARK: - Report export

    // Writes an actual file to disk (NSSavePanel, defaulting to Desktop) — not a clipboard copy
    // the user then has to paste somewhere themselves to see or keep. Text mirrors exactly what
    // this view renders: no diagnosis, no fabricated distribution, just what ran and what it saw.
    private func saveReportToFile() {
        var lines = ["Syxx DNS report — raw evidence, no interpretation.", "",
                     "Generated: \(conclusion.generatedAt)", ""]
        lines.append("Checks run:")
        for entry in conclusion.sectionStatuses {
            // E120: same plain-English mapping as the on-screen badges.
            lines.append("- \(entry.section.rawValue.capitalized): \(statusLabel(entry.status))")
        }
        if !conclusion.evidence.isEmpty {
            lines.append("")
            lines.append("Evidence:")
            for item in conclusion.evidence {
                lines.append("- [\(item.section.rawValue)] \(item.observation) — \(item.rationale)")
            }
        }
        if !conclusion.advisories.isEmpty {
            lines.append("")
            lines.append("Exposure findings:")
            for advisory in conclusion.advisories {
                lines.append("- \(advisory.title): \(advisory.detail) [\(advisory.advisoryId.rawValue)]")
            }
        }
        let text = lines.joined(separator: "\n")

        let panel = NSSavePanel()
        panel.title = "Save Syxx Report"
        panel.nameFieldStringValue = "Syxx Report \(ISO8601DateFormatter().string(from: conclusion.generatedAt).prefix(19)).txt"
        panel.allowedContentTypes = [.plainText]
        panel.directoryURL = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            try? text.write(to: url, atomically: true, encoding: .utf8)
        }
    }

    // MARK: - Color & Icon Helpers

    private func sectionIcon(_ section: SyxxKit.Section) -> String {
        switch section {
        case .prophet: return "magnifyingglass"
        case .mirror: return "arrow.triangle.branch"
        case .portal: return "network"
        case .glutton: return "drop.triangle"
        case .cage: return "point.3.connected.trianglepath.dotted"
        case .witness: return "seal"
        }
    }

    private func advisoryColor(_ severity: Severity) -> Color {
        switch severity {
        case .clean: return .ioSuccessGreen
        case .info: return .ioInfoPurple
        case .warning: return .ioWarningAmber
        case .high: return .ioHighOrange
        case .critical: return .ioCriticalRed
        case .unknown: return .ioTextSecondary
        }
    }

    /// E120: shared plain-English label for a section status, used by both on-screen badges
    /// and the exported report text.
    private func statusLabel(_ status: SectionStatus) -> String {
        switch status {
        case .positive:     return "Finding"
        case .negative:     return "No finding"
        case .degraded:     return "Partial"
        case .inconclusive: return "Inconclusive"
        case .untested:     return "Not run"
        case .invalidated:  return "Not usable"
        }
    }

    /// E120: plain-English labels instead of raw enum jargon. "POSITIVE" in a results grid
    /// reads as a pass to a non-clinical reader; "Finding" is unambiguous.
    @ViewBuilder
    private func statusBadge(_ status: SectionStatus) -> some View {
        let label = statusLabel(status).uppercased()
        switch status {
        case .positive:     StatusPill(label: label, color: .ioCriticalRed)
        case .negative:     StatusPill(label: label, color: .ioSuccessGreen)
        case .degraded:     StatusPill(label: label, color: .ioWarningAmber)
        case .inconclusive: StatusPill(label: label, color: .ioInfoPurple)
        case .untested:     StatusPill(label: label, color: .ioTextSecondary)
        case .invalidated:  StatusPill(label: label, color: .ioCriticalRed)
        }
    }
}

#if DEBUG
#Preview {
    OverallResultView(conclusion: SyxxPreviewSupport.sampleConclusion)
        .padding(24)
        .syxxPreviewCanvas()
}
#endif
