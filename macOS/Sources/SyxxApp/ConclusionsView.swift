import SwiftUI
import SyxxKit
import Sentry

struct ConclusionsView: View {
    @EnvironmentObject var model: AppModel
    
    private var completedModules: (prophet: Bool, mirror: Bool, portal: Bool, glutton: Bool, cage: Bool, witness: Bool) {
        (
            prophet: model.sectionInputs.resolution != nil,
            mirror: model.sectionInputs.comparison != nil,
            portal: model.sectionInputs.network != nil,
            glutton: model.sectionInputs.leak != nil,
            cage: model.sectionInputs.exfil != nil,
            witness: model.sectionInputs.drifts != nil
        )
    }

    private var missingModuleNames: [String] {
        var missing: [String] = []
        let m = completedModules
        if !m.prophet { missing.append("Prophet") }
        if !m.mirror { missing.append("Mirror") }
        if !m.portal { missing.append("Portal") }
        if !m.glutton { missing.append("Glutton") }
        if !m.cage { missing.append("Cage") }
        if !m.witness { missing.append("Witness") }
        return missing
    }

    private var isAllModulesComplete: Bool {
        missingModuleNames.isEmpty
    }

    private func leakResultLabel(_ result: LeakResult) -> String {
        switch result {
        case .notDetected:
            return "No leak detected"
        case .confirmed:
            return "Leak detected"
        case .inconclusive:
            return "Inconclusive"
        case .invalid:
            return "INVALID"
        case .error:
            return "Error"
        }
    }

    var body: some View {
        PurpleScrollView(geometry: .fullPanel) {
            VStack(alignment: .leading, spacing: 20) {
                // Warden header: rune tile + eyebrow + name + lore
                HStack(alignment: .top, spacing: 15) {
                    Text("\u{2712}")
                        .font(.custom("Cinzel-SemiBold", size: 28))
                        .frame(width: 54, height: 54)
                        .background(Color.ioWarningAmber.opacity(0.10))
                        .cornerRadius(14)
                        .foregroundColor(Color.ioWarningAmber)
                        .shadow(color: Color.ioWarningAmber.opacity(0.35), radius: 22)
                        .accessibilityHidden(true)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("THE COUNCIL \u{00B7} SYNTHESIS")
                            .font(.ioCaption(.bold))
                            .foregroundColor(Color.ioWarningAmber)
                            .tracking(0.26)

                        Text("Conclusion")
                            .font(.custom("Cinzel-SemiBold", size: 26))
                            .foregroundColor(.ioText)

                        Text("Unified synthesis of network evidence across all six wardens.")
                            .font(.ioLore(16))
                            .foregroundColor(.ioTextMuted)
                            .fixedSize(horizontal: false, vertical: true)
                            .frame(maxWidth: 560)
                    }
                }
                if let conclusion = model.conclusion {
                    // STAGE 2: Verified Conclusion View
                    VStack(alignment: .leading, spacing: 18) {
                        OverallResultView(conclusion: conclusion)
                            .accessibilityElement(children: .combine)
                            .accessibilityLabel(resultAccessibilityLabel(conclusion))
                            .accessibilityAddTraits(.isHeader)

                        explanationCard(PlainExplanations.For(conclusion))
                            .accessibilityLabel("Explanation of conclusion")
                        actionCard(ResultActions.For(conclusion))
                            .accessibilityLabel("Recommended action: \(ResultActions.For(conclusion).primaryAction)")

                        // How Decision Was Made
                        VStack(alignment: .leading, spacing: 10) {
                            Text("How Decision Was Made")
                                .font(.ioTitle2())
                                .foregroundColor(.ioText)

                            Text("All decisions involve resolving conflicting information. When you appropriately weight each piece of evidence based on its relative value, the scale tips in support of the primary conclusion.")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                                .padding(14)
                                .background(Color.ioSurfaceDark)
                                .cornerRadius(10)
                                .overlay(
                                    RoundedRectangle(cornerRadius: 10)
                                        .stroke(Color.ioBorderSubtle, lineWidth: 1)
                                )
                        }

                        HStack {
                            Button(action: {
                                // E121: clear collected evidence too, so the user truly starts clean.
                                model.conclusion = nil
                                model.sectionInputs = SectionInputs()
                            }) {
                                Text("Reset Analysis")
                            }
                            .buttonStyle(IoOutlineButtonStyle())
                            
                            Spacer()
                        }
                    }
                    .padding(.bottom, 24)
                } else {
                    // STAGE 1: Pre-Verification Gate & Module Status
                    VStack(alignment: .leading, spacing: 18) {
                        Text("Module Status")
                            .font(.ioTitle2())
                            .foregroundColor(.ioText)

                        LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 12) {
                            moduleCard(name: "Prophet", role: "Resolver accuracy", completed: completedModules.prophet)
                            moduleCard(name: "Mirror", role: "Remote vs local", completed: completedModules.mirror)
                            moduleCard(name: "Portal", role: "LAN & route trace", completed: completedModules.portal)
                            moduleCard(name: "Glutton", role: "Privacy leak test", completed: completedModules.glutton)
                            moduleCard(name: "Cage", role: "Live query capture", completed: completedModules.cage)
                            moduleCard(name: "Witness", role: "Environment trust", completed: completedModules.witness)
                        }

                        if !isAllModulesComplete {
                            VStack(alignment: .leading, spacing: 8) {
                                HStack(spacing: 10) {
                                    Image(systemName: "exclamationmark.triangle.fill")
                                        .foregroundColor(.ioWarningAmber)
                                    Text("Modules Incomplete")
                                        .font(.ioBody(.bold))
                                        .foregroundColor(.ioWarningAmber)
                                }
                                Text("A reliable decision requires the completion of all modules. Please complete modules \(missingModuleNames.joined(separator: ", ")) and return when done.")
                                    .font(.ioBody())
                                    .foregroundColor(.ioTextSecondary)
                            }
                            .padding(14)
                            .background(Color.ioWarningAmber.opacity(0.08))
                            .cornerRadius(10)
                            .overlay(
                                RoundedRectangle(cornerRadius: 10)
                                    .stroke(Color.ioWarningAmber.opacity(0.3), lineWidth: 1)
                            )
                        } else {
                            // E105: "All modules have run" is not a success state — it just means
                            // evidence is ready for synthesis. Use neutral color, not green.
                            VStack(alignment: .leading, spacing: 8) {
                                HStack(spacing: 10) {
                                    Image(systemName: "checkmark.seal.fill")
                                        .foregroundColor(.ioTextSecondary)
                                    Text("All Modules Complete")
                                        .font(.ioBody(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                }
                                Text("Evidence gathered across all six wardens is ready for final synthesis.")
                                    .font(.ioBody())
                                    .foregroundColor(.ioTextSecondary)
                            }
                            .padding(14)
                            .background(Color.ioTextSecondary.opacity(0.08))
                            .cornerRadius(10)
                            .overlay(
                                RoundedRectangle(cornerRadius: 10)
                                    .stroke(Color.ioTextSecondary.opacity(0.3), lineWidth: 1)
                            )
                        }

                        // Synthesize directly from the accumulated section inputs. Routing through
                        // AppModel.run()/execute() is wrong here: execute() has no .conclusions case,
                        // so the old path fell to `default: return ""` and synthesis never ran.
                        Button(action: runSynthesis) {
                            HStack {
                                Image(systemName: "sparkles")
                                Text("Generate Conclusion")
                            }
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 10)
                        }
                        .buttonStyle(IoFilledButtonStyle())
                        .disabled(!isAllModulesComplete)
                        .opacity(isAllModulesComplete ? 1.0 : 0.45)

                        // Collected Objective Evidence
                        Text("Collected Objective Evidence")
                            .font(.ioTitle2())
                            .foregroundColor(.ioText)
                            .padding(.top, 10)

                        VStack(alignment: .leading, spacing: 12) {
                            if let resolution = model.sectionInputs.resolution {
                                evidenceRow(title: "Prophet", fact: "DNS query for \(model.domain) returned \(resolution.records.count) record(s) over \(resolution.status.rawValue).")
                            }
                            if let comparison = model.sectionInputs.comparison {
                                let localIPs = comparison.localOutcome.records.map(\.rdata).joined(separator: ", ")
                                // E106: count only resolvers that actually answered, not those that failed.
                                // A failed resolver is not evidence and must not inflate the "checked" count.
                                let answered = comparison.publicResolverOutcomes.values.filter { !$0.didFail }.count
                                let failed = comparison.publicResolverOutcomes.values.filter { $0.didFail }.count
                                let resolverDetail = failed > 0
                                    ? "Remote reference resolvers answered: \(answered) (\(failed) failed)."
                                    : "Remote reference resolvers answered: \(answered)."
                                evidenceRow(title: "Mirror", fact: "Local resolver answered \(localIPs.isEmpty ? "no records" : localIPs). \(resolverDetail)")
                            }
                            if let network = model.sectionInputs.network {
                                evidenceRow(title: "Portal", fact: "Active resolvers: \(network.dnsServers.joined(separator: ", ")). VPN state: \(network.vpn.state == .yes ? "Active" : "Inactive").")
                            }
                            if let leak = model.sectionInputs.leak {
                                // E107: lead with the result, then the count as supporting detail.
                                // The count alone is meaningless without the result that qualifies it.
                                let resultLabel = leakResultLabel(leak.result)
                                let transportNote = leak.transportPinning == .failed
                                    ? " Transport verification failed — collector identity could not be confirmed."
                                    : " Transport: \(leak.transportPinning.rawValue)."
                                evidenceRow(title: "Glutton", fact: "\(resultLabel). Collector observed \(leak.observedResolvers.count) resolver IP(s) during probe.\(transportNote)")
                            }
                            if let exfil = model.sectionInputs.exfil {
                                evidenceRow(title: "Cage", fact: "Captured \(exfil.count) network flow domain(s) during live monitoring.")
                            }
                            if let drifts = model.sectionInputs.drifts {
                                evidenceRow(title: "Witness", fact: "Tracked \(drifts.count) domain resolution drift(s) against baseline environment signatures.")
                            }
                            if !completedModules.prophet && !completedModules.mirror && !completedModules.portal && !completedModules.glutton && !completedModules.cage && !completedModules.witness {
                                Text("No module evidence collected yet. Run checks in the sidebar to collect evidence.")
                                    .font(.ioCaption())
                                    .foregroundColor(.ioTextMuted)
                            }
                        }
                    }
                    .padding(.bottom, 24)
                }
            }
        }
    }

    // E105: "completed" means the module has run, not that it returned clean. Use neutral
    // colors to avoid spending green on inconclusive/error results. Green is reserved for
    // the synthesis result card when the conclusion is actually clean.
    private func moduleCard(name: String, role: String, completed: Bool) -> some View {
        HStack(spacing: 12) {
            Image(systemName: completed ? "checkmark.circle.fill" : "circle")
                .foregroundColor(completed ? .ioTextSecondary : .ioTextMuted)
                .font(.system(size: 18))

            VStack(alignment: .leading, spacing: 2) {
                Text(name)
                    .font(.ioBody(.bold))
                    .foregroundColor(completed ? .ioText : .ioTextMuted)
                Text(role)
                    .font(.ioCaption())
                    .foregroundColor(.ioTextSecondary)
            }
            Spacer()
        }
        .padding(12)
        .background(Color.ioSurfaceDark)
        .cornerRadius(8)
        .overlay(
            RoundedRectangle(cornerRadius: 8)
                .stroke(completed ? Color.ioTextSecondary.opacity(0.3) : Color.ioBorderSubtle, lineWidth: 1)
        )
    }

    private func evidenceRow(title: String, fact: String) -> some View {
        HStack(alignment: .top, spacing: 10) {
            Text("•")
                .foregroundColor(Color.ioWarningAmber)
                .font(.ioBody(.bold))
            VStack(alignment: .leading, spacing: 2) {
                Text(title)
                    .font(.ioCaption(.bold))
                    .foregroundColor(Color.ioWarningAmber)
                Text(fact)
                    .font(.ioBody())
                    .foregroundColor(.ioText)
            }
        }
        .padding(10)
        .background(Color.ioSurfaceDark)
        .cornerRadius(6)
    }

    @ViewBuilder
    private func actionCard(_ action: ResultAction) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Text("WHAT THIS MEANS")
                    .font(.ioSmall())
                    .foregroundColor(.ioTextSecondary)
                // `qualified` is the Kit saying the result rests on incomplete evidence. Surfacing
                // it is the difference between "you are clean" and "what we could see looked clean".
                if action.qualified {
                    Text("QUALIFIED")
                        .font(.ioCaption())
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(Color.ioWarningAmber.opacity(0.18))
                        .foregroundColor(.ioWarningAmber)
                        .clipShape(Capsule())
                }
                Spacer(minLength: 0)
            }

            Text(action.meaning)
                .font(.ioBody())
                .foregroundColor(.ioText)
                .fixedSize(horizontal: false, vertical: true)

            HStack(alignment: .top, spacing: 8) {
                Text("DO THIS")
                    .font(.ioSmall())
                    .foregroundColor(.ioSeverity(action.severity))
                Text(action.primaryAction)
                    .font(.ioBody())
                    .foregroundColor(.ioText)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.top, 2)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ioSurfaceElevated)
        .overlay(
            RoundedRectangle(cornerRadius: 10).stroke(Color.ioSeverity(action.severity).opacity(0.45),
                                                      lineWidth: 1)
        )
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }

    // MARK: - A2 — plain language

    @ViewBuilder
    private func explanationCard(_ explanation: PlainExplanation) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("WHY WE BELIEVE THIS")
                .font(.ioSmall())
                .foregroundColor(.ioTextSecondary)
            Text(explanation.narrative)
                .font(.ioBody())
                .foregroundColor(.ioText)
                .fixedSize(horizontal: false, vertical: true)
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.ioSurfaceElevated)
        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.ioBorderSubtle, lineWidth: 1))
        .clipShape(RoundedRectangle(cornerRadius: 10))
    }

    // MARK: - Synthesis Logic
    
    private func runSynthesis() {
        // Synthesize from whatever sections actually ran (accumulated in model.sectionInputs). No mock
        // fallback: if nothing has run, this yields an honest all-untested "inconclusive", not a fake demo.
        model.conclusion = Synthesizer.conclude(from: model.sectionInputs)
        let d = model.conclusion?.determination
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: SentryLevel.info, category: "engine.result")
            b.message = "Synthesis concluded"
            b.data = [
                "conclusion": d?.state.rawValue ?? "none",
                "engine_version": "matrix.1"
            ]
            return b }())
    }

    /// Build a VoiceOver-friendly label for the result.
    private func resultAccessibilityLabel(_ conclusion: Conclusion) -> String {
        guard let det = conclusion.determination else {
            return "Conclusion unavailable. No synthesis has been performed."
        }
        let stateLabel: String
        switch det.state {
        case .clean:          stateLabel = "Clean"
        case .threatDetected: stateLabel = "Threat detected"
        case .inconclusive:   stateLabel = "Inconclusive"
        case .untested:       stateLabel = "Untested"
        }
        if let diag = det.diagnoses.first {
            return "\(stateLabel): \(diag.displayName). \(diag.explanation)"
        }
        return stateLabel
    }
}

#if DEBUG
#Preview {
    ConclusionsView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .conclusions, pro: true))
        .syxxPreviewCanvas()
}
#endif
