import SwiftUI
import SyxxKit

struct ProphetView: View {
    @EnvironmentObject var model: AppModel
    // E108: removed @State localDomain that shadowed model.domain. Bind directly to
    // $model.domain like every other pane, so typing here doesn't clobber other panes.

    var body: some View {
        // Owner directive 2026-08-02: the Prophet pane is NON-scrollable. The outer
        // PurpleScrollView(.fullPanel) wrapper was removed so the pane fits the 1050x613
        // window without a pane-level scrollbar. Fixed children (rune header, body, domain
        // card, resolver consensus card with its 4-line inner scroller) take their natural
        // height; the DNSSEC card flexes to fill the remainder down to 20px from the bottom.
        VStack(alignment: .leading, spacing: 14) {
            // Warden header. Owner directive 2026-08-02 (superseding the stacked variant):
            // the rune tile sits to the LEFT of the eyebrow + module name, matching the
            // `HStack(alignment: .top, spacing: 15)` header every other pane uses
            // (ConclusionsView.swift:54, MirrorView.swift:14, PortalView.swift:24,
            // GluttonView.swift:13, CageView.swift:12, WitnessView.swift:59). The two prose
            // paragraphs stay OUTSIDE that HStack so they keep the single 35px left origin
            // shared with the cards below, which the earlier directive established.
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .top, spacing: 15) {
                    // Module highlight (owner directive 2026-08-02): Prophet's highlight is the
                    // pastel near-neon blue #00b4ff, not the violet ioBlue. The rune tile carries
                    // it at low alpha for the fill and full strength for the glyph — the same
                    // variable-alpha treatment as the result containers and the primary button.
                    Text("\u{16A8}") // ᚨ
                        .font(.custom("Cinzel-SemiBold", size: 28))
                        .frame(width: 54, height: 54)
                        .background(Color.ioProphetBlue.opacity(0.10))
                        .cornerRadius(14)
                        .foregroundColor(.ioProphetBlue)
                        .shadow(color: Color.ioProphetBlue.opacity(0.35), radius: 22)
                        .accessibilityHidden(true)

                    VStack(alignment: .leading, spacing: 4) {
                        Text("THE SEER · RESOLVER ACCURACY")
                            .font(.ioCaption(.bold))
                            .foregroundColor(.ioProphetBlue)
                            .tracking(0.26)

                        Text("Prophet")
                            .font(.custom("Cinzel-SemiBold", size: 26))
                            .foregroundColor(.ioText)
                    }
                }

                // Owner copy (MEMORY.md:1819). Cormorant SC Regular 17pt (owner directive
                // 2026-08-02). Full pane width (the prior maxWidth: 560 clipped it).
                // No wrapping quotation marks (MEMORY.md:1795).
                Text("Thus the voyage began: The Prophet names each domain aright, so no false guise may be borne into view.")
                    .font(.bodyAlternate(17))
                    .foregroundColor(.ioTextMuted)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)

                // Body text beneath subcaption (owner copy, 2026-08-02). Cormorant Garamond
                // Regular 13pt, full pane width.
                Text("Prophet compares twelve resolver answers to queries sent for the domain you identify. A match only indicates consistency and does not mean the answers are valid. Resolver answers can be spoofed. Prophet then checks for DNSSEC on the domain, which does communicate trustworthiness.")
                    .font(.body(13))
                    .foregroundColor(.ioTextSecondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Input card
            VStack(alignment: .leading, spacing: 10) {
                // Owner copy (MEMORY.md:1823). Marcellus SC 14pt (owner directive 2026-08-02).
                // No wrapping quotation marks (MEMORY.md:1795).
                Text("In what domain do voices agree, their truth brought forth in dawn, and by the sacred seal, their truth preserved.")
                    .font(.cardTitle(14))
                    .foregroundColor(.ioTextMuted)
                    .fixedSize(horizontal: false, vertical: true)

                HStack(spacing: 12) {
                    TextField("a domain to divine…", text: $model.domain)
                        .textFieldStyle(.plain)
                        .padding(12)
                        .background(Color(red: 10/255, green: 6/255, blue: 20/255, opacity: 0.6))
                        .cornerRadius(10)
                        .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(white: 1, opacity: 0.12), lineWidth: 1))
                        // Owner directive 2026-08-02: shorten the textbox by ~half so the
                        // button sits closer to center. The prior stretch filled the card.
                        .frame(maxWidth: 220)
                        .font(.ioMono(14))
                        .foregroundColor(.ioText)
                        .accessibilityLabel("Domain to test")
                        .accessibilityHint("Enter a domain name to query multiple DNS resolvers and check for consensus")

                    Button(action: {
                        // E108: model.domain is already bound, no need to assign.
                        model.run()
                    }) {
                        // Owner directive 2026-08-02: the primary button carries the module
                        // colour, the way every other pane tints its own action control
                        // (Mirror passes .ioElectric to IoFilledButtonStyle). #00b4ff is a
                        // bright fill, so the label is deep ink rather than white: white on
                        // #00b4ff is about 2.3:1, well under the 4.5:1 the HIG expects at
                        // this size, while this ink reads about 7:1.
                        Text("Divine the Domain")
                            .font(.uiLabel(14))
                            .foregroundColor(Color(red: 4/255, green: 32/255, blue: 48/255))
                            .padding(.horizontal, 22)
                            .padding(.vertical, 12)
                            .background(LinearGradient.ioProphetButton)
                            .cornerRadius(10)
                    }
                    .buttonStyle(.plain)
                    // E108: disable button on empty/whitespace domain to prevent clobbering.
                    .disabled(model.domain.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    .accessibilityLabel("Divine the Domain")
                    .accessibilityHint("Queries multiple DNS resolvers for the domain and checks whether they agree on the result")
                }
            }
            .padding(16)
            .background(Color(white: 1, opacity: 0.03))
            .cornerRadius(14)
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(Color(white: 1, opacity: 0.08), lineWidth: 1))

            // Results
            if let result = model.prophetResult {
                prophetResultCard(result)
                // DNSSEC duplicate result container (owner directive, MEMORY.md:1822):
                // flexes to fill the remaining pane height down to 20px from the bottom.
                // Tri-state via DNSSECStatus, never a Bool, never a mocked "secure"; the
                // strongest positive state emitted is recordsPresentUnverified (CF-08 / A2
                // full chain validation is not done here).
                if let dnssec = model.dnssecStatus {
                    prophetDnssecCard(dnssec)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    // Reserve the flex slot so the layout does not jump when the DNSSEC
                    // query lands (owner directive 2026-08-02).
                    Spacer().frame(maxHeight: .infinity)
                }
            } else {
                // Empty (pre-scan) state. Owner copy MEMORY.md:1825, Garamond EB Italic 15pt.
                // No wrapping quotation marks (the curly quotes in MEMORY.md are delimiters).
                Text("In silence doth the Prophet await a vision wherein the truth of twelvefold kind is aligned, and all things abide in surety.")
                    .font(.ioLore(15))
                    .foregroundColor(Color(red: 125/255, green: 115/255, blue: 153/255))
                    .frame(maxWidth: .infinity)
                    .padding(34)
                    .overlay(
                        RoundedRectangle(cornerRadius: 14)
                            .stroke(Color(white: 1, opacity: 0.12), style: StrokeStyle(lineWidth: 1, dash: [6, 4]))
                    )
                // Reserve the DNSSEC flex slot pre-scan so the layout does not jump.
                Spacer().frame(maxHeight: .infinity)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .padding(.bottom, 20)
    }

    // MARK: - Consensus evaluation (tri-state: agree / disagree / insufficient / inconclusive)

    private enum ResolverConsensus {
        case inconclusive
        case insufficient(active: Int)
        case agree(active: Int)
        case disagree(active: Int, uniqueIPs: [String])
    }

    /// Evaluate resolver consensus by comparing actual returned IPs, not just resolver count.
    /// - `.inconclusive`: 0 reachable resolvers — no data to judge.
    /// - `.insufficient`: 1 reachable resolver — need >= 2 for consensus.
    /// - `.agree`: all reachable resolvers returned the same IP(s).
    /// - `.disagree`: reachable resolvers returned different IPs.
    private func evaluateConsensus(responses: [ProphetResult.ResolverResponse]) -> ResolverConsensus {
        let active = responses.filter { !$0.ip.contains("[unreachable]") }
        guard !active.isEmpty else {
            return .inconclusive
        }
        guard active.count >= 2 else {
            return .insufficient(active: active.count)
        }
        let uniqueIPs = Set(active.map { $0.ip })
        if uniqueIPs.count == 1 {
            return .agree(active: active.count)
        }
        return .disagree(active: active.count, uniqueIPs: Array(uniqueIPs))
    }

    // MARK: - Consensus presentation (label + color, computed once per result card)

    private struct ConsensusPresentation {
        /// Headline run 1. For agree/disagree this is the Spectral-Light 20pt lore line rendered
        /// in the module highlight colour (MEMORY.md:1827/1829). For inconclusive/insufficient it
        /// is the single honest status title rendered in the semantic colour.
        let headlinePrimary: String
        /// Headline run 2 (Spectral-Light 17pt), present ONLY for the lore agree/disagree states.
        /// nil for inconclusive/insufficient — those never borrow the success or failure voice.
        let headlineSecondary: String?
        let detail: String
        /// Semantic colour for the status dot, border, and fraction (secure / tampered /
        /// inconclusive). Kept distinct per state so the module-highlight headline colour does
        /// not erase the secure-vs-tampered distinction.
        let color: Color
        let agreementFraction: String
    }

    /// Build the user-visible headline, explanatory detail, and semantic colour for a consensus
    /// state. agree/disagree carry the two-run lore headline (MEMORY.md:1827-1833); inconclusive
    /// and insufficient keep their own honest wording and never fall through to either voice.
    private func prophetConsensusLabel(
        for consensus: ResolverConsensus,
        domain: String,
        medianLatency: Int?
    ) -> ConsensusPresentation {
        // B231: nil latency was rendered as an em-dash; em-dashes are banned in user-visible
        // strings, so an unmeasured latency now reads "not measured" (never a fabricated "0ms").
        let latencyFragment = medianLatency.map { "\($0)ms median" } ?? "not measured"
        switch consensus {
        case .inconclusive:
            // Honest inconclusive — distinct from both success and failure (coordinator directive).
            return ConsensusPresentation(
                headlinePrimary: "Inconclusive: no reachable resolver",
                headlineSecondary: nil,
                detail: "No resolver could be reached for \(domain). Check your network connection.",
                color: .ioTextSecondary,
                agreementFraction: "0 / 0 reachable · \(latencyFragment)"
            )
        case .insufficient(let active):
            // Honest under-measured state — one vantage cannot establish consensus either way.
            return ConsensusPresentation(
                headlinePrimary: "Insufficient: only \(active) resolver reachable",
                headlineSecondary: nil,
                detail: "Only one resolver responded for \(domain). Add public oracles to test consensus.",
                color: .ioTextSecondary,
                agreementFraction: "\(active) / \(active) (single vantage) · \(latencyFragment)"
            )
        case .agree(let active):
            // Success lore (MEMORY.md:1827 headline, :1831 detail). Owner directive 2026-08-02:
            // the AGREE container's chrome + conclusion lines use ioProphetBlue (#00b4ff)
            // instead of the prior green. The dot, background, border, headline runs, and
            // consensus footer all derive from this colour.
            return ConsensusPresentation(
                headlinePrimary: "No tampering doth abide;",
                headlineSecondary: "the 12 resolvers are of one accord.",
                detail: "Then came the vision of twelve oracles, each bearing witness, and all in steadfast accord, as though one truth moved through many mouths.",
                color: .ioProphetBlue,
                agreementFraction: "\(active) / \(active) agree · \(latencyFragment)"
            )
        case .disagree(let active, _):
            // Failure lore (MEMORY.md:1829 headline, :1833 detail).
            return ConsensusPresentation(
                headlinePrimary: "Tampering doth yet abide;",
                headlineSecondary: "the twelve resolvers are not of one accord.",
                detail: "Then came the vision of twelve oracles, each bearing witness, yet their voices fell apart, and no single truth held them in accord.",
                color: .ioWarningAmber,
                agreementFraction: "0 / \(active) agree · \(latencyFragment)"
            )
        }
    }

    // MARK: - Result card

    @ViewBuilder
    private func prophetResultCard(_ result: ProphetResult) -> some View {
        let consensus = evaluateConsensus(responses: result.resolverResponses)
        let pres = prophetConsensusLabel(
            for: consensus,
            domain: model.domain,
            medianLatency: result.consensus.medianLatency
        )

        VStack(alignment: .leading, spacing: 10) {
            if let secondary = pres.headlineSecondary {
                // Lore headline (agree/disagree): Spectral-Light, two adjacent runs. Owner
                // directive 2026-08-02: BOTH runs use pres.color (ioProphetBlue for agree,
                // ioWarningAmber for disagree) so the AGREE conclusion reads in the blue
                // highlight. The semantic dot keeps secure and tampered distinct.
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Circle()
                        .fill(pres.color)
                        .frame(width: 9, height: 9)
                        .shadow(color: pres.color, radius: 8)
                    Text(pres.headlinePrimary)
                        .font(.custom("Spectral-Light", size: 20))
                        .foregroundColor(pres.color)
                    Text(secondary)
                        .font(.custom("Spectral-Light", size: 17))
                        .foregroundColor(pres.color)
                        .fixedSize(horizontal: false, vertical: true)
                    Spacer(minLength: 0)
                }
            } else {
                // Honest non-lore status (inconclusive/insufficient): single title, semantic colour.
                HStack(spacing: 10) {
                    Circle()
                        .fill(pres.color)
                        .frame(width: 9, height: 9)
                        .shadow(color: pres.color, radius: 8)
                    Text(pres.headlinePrimary)
                        .font(.ioDisplay(size: 16, weight: .semibold))
                        .foregroundColor(pres.color)
                }
            }

            Text(pres.detail)
                .font(.ioLore(15))
                .foregroundColor(.ioTextMuted)
                .fixedSize(horizontal: false, vertical: true)

            // Consensus footer. Owner directive 2026-08-02: the "Consensus" label AND the
            // fraction use pres.color (ioProphetBlue for agree).
            HStack {
                Text("Consensus")
                    .font(.body(15))
                    .foregroundColor(pres.color)
                Spacer()
                Text(pres.agreementFraction)
                    .font(.ioMono(13))
                    .foregroundColor(pres.color)
            }

            // Resolver rows (owner directive 2026-08-02): the 12 oracles now live INSIDE the
            // consensus card as an inner scrollable list, INVISIBLE until a scan has run (this
            // card is only rendered when model.prophetResult != nil). Fixed height of ~4 lines;
            // rows beyond the window are reached by scrolling within the card. Resolver row text
            // keeps its existing colours (owner: "leave the text inside as it is").
            prophetResolverRosterRows()
        }
        .padding(16)
        // Owner directive 2026-08-02: the container fill is the state colour on a VARIABLE
        // alpha ramp (for agree: rgba(0, 180, 255, 0.14) down to rgba(0, 180, 255, 0.03)),
        // and the border a brighter step of the same colour. A non-matching or otherwise
        // non-agreeing state keeps its own colour here — only the alpha channel is shared.
        .background(
            LinearGradient(colors: [pres.color.opacity(0.14), pres.color.opacity(0.03)],
                           startPoint: .top, endPoint: .bottom)
        )
        .cornerRadius(14)
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(pres.color.opacity(0.28), lineWidth: 1))
    }

    // MARK: - Resolver roster (inner scrollable; the 12 oracles + honest per-leg answer state)

    /// The honest display state of one resolver leg's answer. Extracted from the view so it is
    /// unit-testable without SwiftUI (same pattern as PortalView.resolversPaneEmptyCopy).
    enum ResolverAnswerState: Equatable {
        /// A usable measured address returned by that resolver.
        case measured(String)
        /// Measured, but the leg was unreachable or returned no address — unknown, never clean.
        case unknown(String)
        /// No measurement exists yet for this leg (before a scan, or a leg not in the last run).
        case notQueried
    }

    /// Pure mapping from a raw answer string (or nil) to its honest display state. A missing
    /// value is `.notQueried` (never rendered blank-as-clean); an unreachable/empty leg is
    /// `.unknown`; only a real address is `.measured`. The honesty gate lives here.
    static func resolverAnswerState(for ip: String?) -> ResolverAnswerState {
        guard let ip else { return .notQueried }
        if ip.contains("[unreachable]") || ip == "no records" { return .unknown(ip) }
        return .measured(ip)
    }

    /// One row of the roster: a resolver name and the honest state of its answer.
    struct RosterEntry: Identifiable {
        var id: String { name }
        let name: String
        let answer: ResolverAnswerState
    }

    /// Pure builder for the roster rows. Extracted so the population logic — which resolvers show
    /// and each leg's honest answer state — is unit-testable without SwiftUI. The Local resolver
    /// only appears once it has been measured (an unmeasured Local resolver is never shown as
    /// known — D278 discipline); every leg's answer runs through `resolverAnswerState`.
    static func prophetRosterEntries(
        localServer: String,
        responses: [ProphetResult.ResolverResponse]?
    ) -> [RosterEntry] {
        let oracles: [(name: String, ip: String)] =
            localServer.trimmingCharacters(in: .whitespaces).isEmpty
                ? ProphetOracleRoster.publicOracles
                : ProphetOracleRoster.roster(localServer: localServer)
        let measured: [String: String] = (responses ?? [])
            .reduce(into: [:]) { $0[$1.resolver] = $1.ip }
        return oracles.map {
            RosterEntry(name: $0.name, answer: resolverAnswerState(for: measured[$0.name]))
        }
    }

    /// Renders the 12 oracle rows inside the consensus card as an inner scrollable list.
    /// INVISIBLE until a scan has run (the enclosing result card is only rendered when
    /// model.prophetResult != nil). Fixed height of ~4 lines of text; rows beyond the window
    /// are reached by scrolling within the card. Answers come from the last scan's real
    /// measured responses; a leg with no measurement shows a neutral pending marker, and a
    /// failed leg shows its honest unreachable/unknown value. Never green. Resolver row text
    /// keeps its existing colours (owner: "leave the text inside as it is").
    @ViewBuilder
    private func prophetResolverRosterRows() -> some View {
        let entries = Self.prophetRosterEntries(
            localServer: model.server,
            responses: model.prophetResult?.resolverResponses
        )
        // Inner scroll sized to exactly four rows: Cormorant Garamond 15pt lays out on a 19pt
        // line box (measured from the shipped face, not estimated), plus 7pt padding above and
        // below = a 33pt row, so 4 * 33 = 132. The compact PurpleScrollView keeps the app's
        // purple knob; the remaining oracles are reached by scrolling inside the card.
        PurpleScrollView(geometry: .compact) {
            VStack(spacing: 0) {
                ForEach(entries) { entry in
                    HStack {
                        Text(entry.name)
                            .font(.body(15))
                            .foregroundColor(Color(red: 143/255, green: 131/255, blue: 184/255))
                        Spacer()
                        prophetResolverAnswer(entry.answer)
                    }
                    .padding(.vertical, 7)
                    .overlay(
                        Rectangle().fill(Color(white: 1, opacity: 0.06)).frame(height: 1),
                        alignment: .bottom
                    )
                }
            }
        }
        .frame(height: 132)
    }

    /// One resolver's answer cell, rendered per its honest state. A measured address is the
    /// production evidence style; an unknown/unreachable leg is muted (never green); a
    /// not-yet-measured leg is a faint, wordless pending glyph — never blank, never a fabricated
    /// value, never green.
    @ViewBuilder
    private func prophetResolverAnswer(_ state: ResolverAnswerState) -> some View {
        switch state {
        case .measured(let ip):
            Text(ip)
                .font(.ioMono(13))
                .foregroundColor(.ioText)
        case .unknown(let ip):
            Text(ip)
                .font(.ioMono(13))
                .foregroundColor(.ioTextMuted)
        case .notQueried:
            // Wordless neutral pending marker (U+2026 ellipsis; not an em-dash). Not green, not
            // blank-as-clean. A spoken/visible pending LABEL is deliberately not invented here —
            // it needs owner-approved wording (flagged in the handoff), so only this typographic
            // placeholder ships for now.
            Text("\u{2026}")
                .font(.ioMono(13))
                .foregroundColor(.ioTextFaint)
        }
    }

    // MARK: - DNSSEC result card (duplicate of the resolver result container, hidden pre-scan)

    @ViewBuilder
    private func prophetDnssecCard(_ status: DNSSECStatus) -> some View {
        let pres = dnssecPresentation(for: status, domain: model.domain)
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .top, spacing: 10) {
                Circle()
                    .fill(pres.color)
                    .frame(width: 9, height: 9)
                    .shadow(color: pres.color, radius: 8)
                    .padding(.top, 7)
                // DNSSEC headline in the Spectral-Light lore voice (MEMORY.md:1835). Rendered in
                // the semantic colour (1835 specifies the face and size, not the module highlight),
                // so configured / broken / unsigned / inconclusive stay visually distinct.
                Text(pres.title)
                    .font(.custom("Spectral-Light", size: 20))
                    .foregroundColor(pres.color)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Text(pres.detail)
                .font(.ioLore(15))
                .foregroundColor(.ioTextMuted)
                .fixedSize(horizontal: false, vertical: true)

            // DNSSEC posture body: the typed status description, never a fabricated "secure".
            // Owner directive 2026-08-02: this card flexes to fill the remaining pane height, so
            // the body scrolls internally (PurpleScrollView .compact) and never overflows the
            // flex card or the non-scrollable pane.
            PurpleScrollView(geometry: .compact) {
                Text(status.description)
                    .font(.ioMono(13))
                    .foregroundColor(.ioTextSecondary)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(12)
                    .background(Color(white: 1, opacity: 0.02))
                    .cornerRadius(10)
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color(white: 1, opacity: 0.06), lineWidth: 1))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)

            HStack {
                Text("DNSSEC")
                    .font(.body(15))
                    .foregroundColor(pres.color)
                Spacer()
                Text(pres.statusLabel)
                    .font(.ioMono(13))
                    .foregroundColor(pres.color)
            }
        }
        .padding(16)
        // Same variable-alpha ramp as the consensus container. `pres.color` is #00b4ff only for
        // the configured-but-unverified state; unsigned / broken / inconclusive keep their own.
        .background(
            LinearGradient(colors: [pres.color.opacity(0.14), pres.color.opacity(0.03)],
                           startPoint: .top, endPoint: .bottom)
        )
        .cornerRadius(14)
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(pres.color.opacity(0.28), lineWidth: 1))
    }

    // MARK: - DNSSEC presentation (tri-state: unsigned / configured / broken / inconclusive)

    private struct DnssecPresentation {
        let title: String
        let detail: String
        let color: Color
        let statusLabel: String
    }

    /// Build the user-visible label, explanatory detail, semantic colour, and short status
    /// tag for a DNSSECStatus. The strongest positive state emitted is `recordsPresentUnverified`
    /// (DNSSEC configured, signatures within validity window) and never "validated" or "secure",
    /// because this client does not perform full cryptographic chain validation (CF-08 / A2).
    private func dnssecPresentation(for status: DNSSECStatus, domain: String) -> DnssecPresentation {
        let display = domain.isEmpty ? "this domain" : domain
        switch status {
        case .unsigned:
            // UNSIGNED is the common, benign case (most domains do not sign). It is deliberately
            // NOT rendered with the owner's failure wording ("witness cannot be held in trust"),
            // which would falsely alarm every ordinary unsigned domain. Kept as an honest, distinct
            // state; owner lore for it is not specified in MEMORY.md and is raised as a question.
            return DnssecPresentation(
                title: "No DNSSEC: zone unsigned",
                detail: "\(display) returned no DNSKEY or RRSIG records. The zone appears unsigned, so resolver answers cannot be cryptographically verified by DNSSEC.",
                color: .ioWarningAmber,
                statusLabel: "unsigned"
            )
        case .recordsPresentUnverified:
            // Success (MEMORY.md:1835). The strongest positive state this client emits: signatures
            // observed within their validity window, but no full cryptographic chain validation.
            // Owner directive 2026-08-02: the "rightly set" container uses ioProphetBlue (parity
            // with the no-tampering consensus container) instead of the prior green.
            return DnssecPresentation(
                title: "DNSSEC is rightly set for this domain.",
                detail: "\(display) returned DNSKEY and RRSIG records with at least one signature inside its validity window. Full cryptographic validation is not performed by this client, so this is the strongest positive state emitted.",
                color: .ioProphetBlue,
                statusLabel: "configured · unverified"
            )
        case .signatureExpiredOrNotYetValid:
            // Failure (MEMORY.md:1837). A genuinely broken signer: signatures present but expired
            // or not yet valid. The alarming wording is warranted here (unlike the unsigned case).
            return DnssecPresentation(
                title: "DNSSEC is not rightly set for this domain; resolvers may not speak true, and their witness cannot be held in trust.",
                detail: "\(display) returned RRSIG records, but every one observed is expired or not yet valid. This is a stale-signature or broken-signer signal, not a trustworthy configuration.",
                color: .ioCriticalRed,
                statusLabel: "broken"
            )
        case .inconclusive:
            // Honest inconclusive — must not borrow the success or failure wording (coordinator
            // directive). Owner lore for this state is not specified; raised as a question.
            return DnssecPresentation(
                title: "DNSSEC status inconclusive",
                detail: "DNSSEC records for \(display) could not be retrieved (timeout, transport error, or blocked). No determination can be made.",
                color: .ioTextSecondary,
                statusLabel: "inconclusive"
            )
        }
    }
}

#if DEBUG
#Preview {
    ProphetView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .prophet))
        .syxxPreviewCanvas()
}
#endif
