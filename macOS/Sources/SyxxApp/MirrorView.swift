import SwiftUI
import SyxxKit

struct MirrorView: View {
    @EnvironmentObject var model: AppModel

    private var comp: SyxxKit.ComparisonResult? { model.sectionInputs.comparison }
    private var warp: WarpResult? { model.sectionInputs.warp }

    var body: some View {
        PurpleScrollView(geometry: .fullPanel) {
        VStack(alignment: .leading, spacing: 20) {
            // Warden header: rune tile + eyebrow + name + lore
            HStack(alignment: .top, spacing: 15) {
                Text("\u{16DE}")
                    .font(.custom("Cinzel-SemiBold", size: 28))
                    .frame(width: 54, height: 54)
                    .background(Color.ioElectric.opacity(0.10))
                    .cornerRadius(14)
                    .foregroundColor(Color.ioElectric)
                    .shadow(color: Color.ioElectric.opacity(0.35), radius: 22)
                    .accessibilityHidden(true)

                VStack(alignment: .leading, spacing: 4) {
                    Text("THE REFLECTION \u{00B7} REMOTE VS LOCAL")
                        .font(.ioCaption(.bold))
                        .foregroundColor(Color.ioElectric)
                        .tracking(0.26)

                    Text("Mirror")
                        .font(.custom("Cinzel-SemiBold", size: 26))
                        .foregroundColor(.ioText)

                    // Owner copy (Untitled 11.rtf:48, 2026-08-02). Cormorant SC Regular 17pt
                    // per the owner directive. Full pane width: the prior maxWidth: 560 clamp
                    // clipped this line, same defect Prophet's subcaption had.
                    Text("Grasped fast in the Prophet's hand, the Mirror did answer, and shewed the records born locally in accord with those discerned afar, proving that the true names shifted not their visages at each summoning.")
                        .font(.bodyAlternate(17))
                        .foregroundColor(.ioTextMuted)
                        .fixedSize(horizontal: false, vertical: true)

                    // Body text beneath the subcaption (owner copy, Untitled 11.rtf:50,
                    // 2026-08-02). Cormorant Garamond Regular 13pt, full pane width.
                    // Describes the Content Warp hash comparison only; the DNS resolver
                    // comparison and its Pro gating stay described below (E110).
                    Text("Mirror pulls the static web contents as seen from your device, as well as the static web contents as seen from the remote server, and then compares their hashes. A match of the static web content hashes pulled from your device and the remote server generally means that the content you're being served is what the world is being served.")
                        .font(.body(13))
                        .foregroundColor(.ioTextSecondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }

            // Domain picker container. Owner directive 2026-08-02: the Mirror module highlight
            // colour is YELLOW, applied to this container (chrome + card title) via
            // Color.ioMirrorYellow. The E110 free-tier/Pro disclosure that used to live in this
            // slot now sits entirely in the paragraph below this container, so nothing was lost.
            VStack(alignment: .leading, spacing: 10) {
                // Owner copy (Untitled 11.rtf:52, 2026-08-02). Marcellus SC 14pt.
                //
                // EM-DASH EXCEPTION: the project rule (AGENTS.md:68) forbids em-dashes in
                // user-visible text. The owner granted a SPECIFIC, LINE-SCOPED exception for
                // this string and required the code to say so. It is reproduced verbatim,
                // including the em-dash after "a place", the ellipsis after "a domain", and
                // the literal double hyphen that follows. Do not "clean up" this punctuation
                // and do not treat this line as precedent for any other string.
                Text("Yourself looking into the mirror, you see a place\u{2014}a domain\u{2026}--very far away but seeming so close. What domain is it that you see?")
                    .font(.cardTitle(14))
                    .foregroundColor(.ioMirrorYellow)
                    .fixedSize(horizontal: false, vertical: true)

                HStack(spacing: 12) {
                    TextField("Domain (e.g., github.com)", text: $model.domain)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 220)

                    Button(action: {
                        model.selection = .mirror
                        model.run()
                    }) {
                        if model.runningTool == .mirror {
                            ProgressView().controlSize(.small)
                        } else {
                            Text("Check")
                        }
                    }
                    .buttonStyle(IoFilledButtonStyle(color: .ioElectric))
                    .disabled(model.isRunning)
                    .accessibilityLabel("Run Mirror comparison")
                }
            }
            .padding(14)
            // Owner directive 2026-08-02: yellow module highlight on the domain picker
            // container. The card keeps the standard opaque surface with a low-opacity yellow
            // wash layered ON TOP of it, so the tint reads as a highlight rather than a filled
            // panel. Stacking order is explicit here on purpose: chaining two .background()
            // modifiers would put the opaque surface in FRONT of the wash and hide it.
            .background(
                RoundedRectangle(cornerRadius: 10)
                    .fill(Color.ioSurfaceDark)
                    .overlay(
                        RoundedRectangle(cornerRadius: 10)
                            .fill(Color.ioMirrorYellow.opacity(0.07))
                    )
            )
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.ioMirrorYellow.opacity(0.55), lineWidth: 1))

            // E110: DNS resolver comparison is always available; hash comparison requires Pro.
            // `.lineLimit(nil)` alone did NOT wrap this: it removes the line cap but does not
            // stop SwiftUI from proposing a single-line height, so the paragraph rendered as
            // one truncated line ending in an ellipsis and the free-tier/Pro disclosure was
            // unreadable on screen. `.fixedSize(horizontal: false, vertical: true)` is what
            // forces the multi-line measurement, matching every other wrapped body in this pane.
            Text("Mirror tests your DNS resolver for signs of tampering or censorship by resolving a given domain both locally and remotely. With Pro, it also compares cryptographic hashes of static web components like external JavaScript files.")
                .font(.ioBody())
                .foregroundColor(.ioTextSecondary)
                .lineLimit(nil)
                .fixedSize(horizontal: false, vertical: true)

            // Side-by-side comparison tables
            HStack(alignment: .top, spacing: 20) {
                // Left Column: Resolver comparison
                VStack(alignment: .leading, spacing: 12) {
                    Text("DNS Resolver Matching:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioAccentStart)
                        .textCase(.uppercase)

                    if let c = comp {
                        VStack(spacing: 10) {
                            // Remote (public) resolver results
                            ForEach(Array(c.publicResolverOutcomes.keys.sorted()), id: \.self) { name in
                                let outcome = c.publicResolverOutcomes[name]!
                                VStack(alignment: .leading, spacing: 6) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "globe")
                                            .foregroundColor(.ioElectric)
                                        Text(name)
                                            .font(.ioCaption(.bold))
                                            .foregroundColor(.ioTextSecondary)
                                    }
                                    Text(displayIPs(outcome))
                                        .font(.ioMonoBold(14))
                                        .foregroundColor(.ioText)
                                    Text(outcome.didFail ? "Failed: \(outcome.errorDescription ?? outcome.status.rawValue)" : "Public resolver")
                                        .font(.ioSmall())
                                        .foregroundColor(outcome.didFail ? .ioCriticalRed : .ioTextSecondary)
                                }
                                .padding(12)
                                .frame(maxWidth: .infinity, alignment: .leading)
                                .background(Color.ioBackgroundDark)
                                .cornerRadius(8)
                                .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                            }

                            // Local resolver
                            VStack(alignment: .leading, spacing: 6) {
                                HStack(spacing: 8) {
                                    Image(systemName: "desktopcomputer")
                                        .foregroundColor(.ioElectric)
                                    Text("Local resolver")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                }
                                Text(displayIPs(c.localOutcome))
                                    .font(.ioMonoBold(14))
                                    .foregroundColor(.ioText)
                                Text(c.localOutcome.didFail
                                     ? "Failed: \(c.localOutcome.errorDescription ?? c.localOutcome.status.rawValue)"
                                     : "Server: \(c.localServer)")
                                    .font(.ioSmall())
                                    .foregroundColor(c.localOutcome.didFail ? .ioCriticalRed : .ioTextSecondary)
                            }
                            .padding(12)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                        }

                        // Result — label on top, evidence detail wrapping below
                        VStack(alignment: .leading, spacing: 4) {
                            Text(resultLabel(c))
                                .font(.ioCaption(.bold))
                                .foregroundColor(resultColor(c))
                            Text(resultDetail(c))
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                                .lineLimit(nil)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        .padding(10)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background(resultColor(c).opacity(0.1))
                        .cornerRadius(8)
                        .overlay(RoundedRectangle(cornerRadius: 8).stroke(resultColor(c).opacity(0.35), lineWidth: 1))
                    } else {
                        Text("Run a check to see resolver comparison results.")
                            .font(.ioBody())
                            .foregroundColor(.ioTextSecondary)
                    }
                }
                .frame(maxWidth: .infinity)
                .padding(16)
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))

                // Right Column: Static content hashes comparison
                VStack(alignment: .leading, spacing: 12) {
                    Text("Static Content Integrity:")
                        .font(.ioCaption(.bold))
                        .foregroundColor(.ioAccentStart)
                        .textCase(.uppercase)

                    if let w = warp {
                        VStack(spacing: 10) {
                            VStack(alignment: .leading, spacing: 6) {
                                HStack(spacing: 8) {
                                    Image(systemName: "checkmark.shield.fill")
                                        .foregroundColor(.ioElectric)
                                    Text("Remote structure")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                }
                                Text("\(w.remoteScriptURLs.count) scripts, \(w.remoteStylesheetURLs.count) stylesheets, \(w.remoteIframeURLs.count) iframes")
                                    .font(.ioMono(13))
                                    .foregroundColor(.ioText)
                                Text("Retrieved via off-network vantage proxy")
                                    .font(.ioSmall())
                                    .foregroundColor(.ioTextSecondary)
                            }
                            .padding(12)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))

                            VStack(alignment: .leading, spacing: 6) {
                                HStack(spacing: 8) {
                                    Image(systemName: "doc.text.fill")
                                        .foregroundColor(.ioElectric)
                                    Text("Local structure")
                                        .font(.ioCaption(.bold))
                                        .foregroundColor(.ioTextSecondary)
                                }
                                Text("\(w.localScriptURLs.count) scripts, \(w.localStylesheetURLs.count) stylesheets, \(w.localIframeURLs.count) iframes")
                                    .font(.ioMono(13))
                                    .foregroundColor(.ioText)
                                Text("Retrieved locally from destination server")
                                    .font(.ioSmall())
                                    .foregroundColor(.ioTextSecondary)
                            }
                            .padding(12)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.ioBackgroundDark)
                            .cornerRadius(8)
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.ioBorderSubtle, lineWidth: 1))
                        }

                        HStack(spacing: 8) {
                            Text(w.isInconclusive ? "INCONCLUSIVE"
                                 : w.isSecure ? "CONTENT INTACT"
                                 : "TAMPERING DETECTED")
                                .font(.ioCaption(.bold))
                                .foregroundColor(w.isInconclusive ? .ioWarningAmber
                                                : w.isSecure ? .ioSuccessGreen
                                                : .ioCriticalRed)
                            // E110: name the actual failing predicate instead of always "structures".
                            Text(w.isInconclusive
                                 ? (w.inconclusiveReason ?? "content check could not be completed")
                                 : w.isSecure
                                 ? "Local and remote structures match."
                                 : tamperingDetail(w))
                                .font(.ioCaption())
                                .foregroundColor(.ioTextSecondary)
                        }
                        .padding(10)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .background((w.isInconclusive ? Color.ioWarningAmber
                                      : w.isSecure ? Color.ioSuccessGreen
                                      : Color.ioCriticalRed).opacity(0.1))
                        .cornerRadius(8)
                        .overlay(RoundedRectangle(cornerRadius: 8).stroke(
                            (w.isInconclusive ? Color.ioWarningAmber
                             : w.isSecure ? Color.ioSuccessGreen
                             : Color.ioCriticalRed).opacity(0.35), lineWidth: 1))

                        // E110: render mismatchedScriptHashes when non-empty — this is the evidence
                        // behind the tampering conclusion.
                        if !w.mismatchedScriptHashes.isEmpty {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("Mismatched script hashes:")
                                    .font(.ioCaption(.bold))
                                    .foregroundColor(.ioCriticalRed)
                                ForEach(w.mismatchedScriptHashes, id: \.self) { url in
                                    Text(url)
                                        .font(.ioMono(11))
                                        .foregroundColor(.ioText)
                                        .lineLimit(2)
                                }
                            }
                            .padding(10)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .background(Color.ioCriticalRed.opacity(0.05))
                            .cornerRadius(6)
                        }
                    } else {
                        if comp != nil {
                            // Mirror check was run but warp is nil — license gate
                            VStack(alignment: .leading, spacing: 4) {
                                Text("CONTENT WARP REQUIRES PRO")
                                    .font(.ioCaption(.bold))
                                    .foregroundColor(.ioAccentStart)
                                Text("Content Warp requires a Pro license. Upgrade at syxx.app")
                                    .font(.ioCaption())
                                    .foregroundColor(.ioTextSecondary)
                            }
                        } else {
                            Text("Run a check to see integrity results.")
                                .font(.ioBody())
                                .foregroundColor(.ioTextSecondary)
                        }
                    }
                }
                .frame(maxWidth: .infinity)
                .padding(16)
                .background(Color.ioSurfaceDark)
                .cornerRadius(12)
                .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.ioBorderSubtle, lineWidth: 1))
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func displayIPs(_ o: ResolverQueryOutcome) -> String {
        if o.didFail { return "[failed]" }
        if o.records.isEmpty { return "[no records — \(o.status.rawValue)]" }
        return o.records.map(\.rdata).joined(separator: ", ")
    }

    private func resultLabel(_ c: SyxxKit.ComparisonResult) -> String {
        if c.isInconclusive { return "INCONCLUSIVE" }
        if c.isRedirectionDetected { return "TAMPERING DETECTED" }
        if c.isDegraded { return "RESOLVERS AGREE (DEGRADED)" }
        return "RESOLVERS AGREE"
    }

    private func resultColor(_ c: SyxxKit.ComparisonResult) -> Color {
        if c.isInconclusive { return .ioWarningAmber }
        if c.isRedirectionDetected { return .ioCriticalRed }
        if c.isDegraded { return .ioWarningAmber }
        return .ioSuccessGreen
    }

    private func resultDetail(_ c: SyxxKit.ComparisonResult) -> String {
        if c.isInconclusive { return "No trustworthy public baseline available." }
        if c.isDegraded { return "No tampering detected — some public resolvers were unreachable." }
        if c.isRedirectionDetected {
            let localIPs = c.localOutcome.records.map(\.rdata).sorted()
            var disagreements: [String] = []
            for (name, outcome) in c.publicResolverOutcomes.sorted(by: { $0.key < $1.key }) {
                guard !outcome.didFail else { continue }
                let publicIPs = outcome.records.map(\.rdata).sorted()
                if publicIPs != localIPs && !publicIPs.isEmpty {
                    disagreements.append("\(name): \(publicIPs.joined(separator: ", "))")
                }
            }
            if disagreements.isEmpty {
                return "Local and public resolver answers disagree — details unavailable."
            }
            return "Local (\(localIPs.joined(separator: ", "))) differs from: \(disagreements.joined(separator: "; "))"
        }
        return "No DNS-level redirection detected."
    }

    /// E110: name the actual failing predicate(s) instead of always "structures differ".
    private func tamperingDetail(_ w: WarpResult) -> String {
        var reasons: [String] = []
        if !w.isScriptStructureIdentical { reasons.append("script structure") }
        if !w.mismatchedScriptHashes.isEmpty { reasons.append("external script hash") }
        if !w.isInlineScriptIdentical { reasons.append("inline script") }
        if !w.isStylesheetStructureIdentical { reasons.append("stylesheet structure") }
        if !w.isIframeStructureIdentical { reasons.append("iframe structure") }
        if reasons.isEmpty { return "Local and remote content differs — possible content injection." }
        return "Content differs in: \(reasons.joined(separator: ", ")) — possible injection."
    }
}

#if DEBUG
#Preview {
    MirrorView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .mirror, pro: true))
        .syxxPreviewCanvas()
}
#endif
