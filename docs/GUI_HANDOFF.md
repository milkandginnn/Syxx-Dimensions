# Syxx GUI — Build Handoff (for Antigravity)

**Owner of this doc:** Claude (building the Verdict engine in `SyxxKit`/`Syxx.Kit`).
**Owner of the work described:** Antigravity (building the macOS SwiftUI GUI).
**Date:** 2026-07-05.

This lets you build the Syxx GUI in parallel with the Verdict engine. Everything below the
"Screens → data" map is unblocked **today** — those views call `SyxxKit` functions that already
exist. Only the single **Overall Verdict** card depends on me; build it against the documented
contract (§5) with the provided mock until I land the real type.

---

## 1. Scope & sources

- **Target:** the macOS SwiftUI app only — SwiftPM product **`syxx-gui`**, target **`SyxxApp`**
  (`macOS/Sources/SyxxApp/`). Windows GUI is out of scope here.
- **Design canvases (ground truth for screens/copy):** `~/Downloads/Syxx GUI redesign-2/`
  - `SyxxApp.dc.html` — the desktop app (primary).
  - `Syxx GUI.dc.html`, `Syxx Web.dc.html` — secondary; check for shared components / web parity.
  - Rendering: a template DSL — `<x-import>` (component include), `<sc-if>` (state), `<sc-for>`
    (list), bindings like `{{ r.ttl }}`. Read them as declarative source; you don't need to run them.
- **The "Io" design system → real SwiftUI:** the canvas reuses Nnyx's "Io" system. Its authoritative
  source is Swift, in the **Nnyx repo**: `~/Software/Nnyx/Sources/NnyxApp/Views/Components/`
  — `IoDesignSystem.swift` (colors, type, `IoCard`, `IoButton`, `IoSeverityBadge`, `IoStatCard`,
  `IoProgressRing`, `ioGlow`, `SigningSource`), `GlassPanel.swift`, and `Views/LogoView.swift`.
  **Port these into `SyxxApp/Views/Components/`** (Syxx and Nnyx are separate SwiftPM packages, so
  copy+adapt rather than import; same author, MilkandGin, so no licensing issue).
- **Current SyxxApp (expand, don't discard):** `SyxxApp.swift` (@main), `ContentView.swift`,
  `AppModel.swift`. **Preserve** the `SyxxAppDelegate` Dock-icon wiring and the
  `resources: [.copy("Resources/Syxx.icns")]` in `Package.swift` that I already added, plus
  `macOS/bundle-app.sh`.
- **Logic layer you call:** `~/Software/Syxx/macOS/Sources/SyxxKit/` (read-only for you — see §6).

## 2. Ownership seam (so we don't collide)

- **You (Antigravity):** everything under `macOS/Sources/SyxxApp/**` — views, the Io component port,
  view-models. **Do not edit `SyxxKit`.**
- **Me (Claude):** `SyxxKit` + `Syxx.Kit` — the Verdict engine (`Conclusion`/`Evidence`/`Hypothesis`
  types, the two DNS canaries, the accumulator). **I will not edit `SyxxApp` views.**
- **Contract:** the `Conclusion` types land in `SyxxKit` (I own them); you `import SyxxKit` and bind
  the verdict view. Until I ship them, use the mock in §5 — the field names there are the ones I'll
  implement, so a mock-built view drops onto the real type.

## 3. Design tokens (from the canvas `_ds` tokens — build to these)

```
bg #0f1629   surface #1a2138   surface-elevated #26304a   border #334561
text #ffffff   text-secondary #94a3bf
accent gradient: #05b5d4 (io-blue) → #8c5cf5 (io-electric)
severity: critical #f5405e · high #fa7317 · warning #f59e0a · success #0d9487 · info #8c5cf5
fonts: Space Grotesk (display/sans) · Space Mono (mono)   (Io tokens: font-rounded/sans/mono)
```

> **Palette-divergence flag (needs a USER decision, not a blocker):** the GUI "Io" accent is
> cyan→electric-violet, but the finalized **Syxx app icon** is violet→gold→magenta (the ribbon
> logo), and the Nnyx/Zzyn icons are on that same violet-gold-magenta family. Build to the Io
> tokens as given; flag to USER whether the Syxx GUI accent should shift toward the icon palette so
> the mark and the shell read as one family.

## 4. Screens → `SyxxKit` data map (all unblocked today except the Verdict card)

Nav (from canvas): **DNS Investigation / Diagnostics / Dimensions / Account**; Overview + Dashboard.
The six checks are branded **Prophet · Mirror · Portal · Glutton · Cage · Witness**. Tagline:
"Syxx — Lyin' Eyes".

| Screen | Feeds from `SyxxKit` (exact entry points) |
|---|---|
| **Dashboard / Overview** ("Every resolver tells a story") | `NetworkKeyState.getSystemDNSServers()`, `NetworkKeyState.encryptedDNSStatus() → EncryptedDNSStatus`, recent activity from `AnswerLedger`; **+ the Overall Verdict card** (§5). |
| **Prophet · Resolver Accuracy** | `DNSResolver.queryUDPDetailed(_)` / `queryDoHDetailed(...)` → `DNSResponse` (typed RCODE/flags/status); `DNSSECVerifier.checkStatus(domain:resolver:) → DNSSECStatus`; `NetworkKeyState.encryptedDNSStatus()`, `getSystemDNSServers()`, `getScopedResolvers()`. Honest copy already in canvas ("Records returned, but unverified…"). |
| **Mirror · Tampering** *(Pro)* | `VantageComparer.compareVantagePoints(domain:localServer:) → ComparisonResult` (`isRedirectionDetected`, `isInconclusive`); `ContentWarp.comparePageContent(targetURLStr:proxyBaseURLStr:) → WarpResult`. |
| **Portal · Network & Path** | `NetworkKeyState.generateReport() → NetworkStateReport` (vpn/captivePortal/encryptedDNS/scopedResolvers/dnsServers/activeInterfaces); `IPClassifier`; `LANCartographer` (+ `.diff`); traceroute (see `SyxxRoute`). |
| **Glutton · Leak** *(Pro)* | `LeakDetector.runCollectorTest(...) → LeakTestResult` (`verdict: LeakVerdict`, `observedResolvers`). Collector base = `https://syxx.app`, needs license/token. |
| **Cage · Capture & Forensics** | Capture via `syxx-cages` (`DNSCapture` + `DNSCaptureReportGenerator.generateReport(...)`); forensics: `ExfilAnalyzer.assess(...) → ExfilAssessment`, `BeaconAnalyzer.score(_) → BeaconScore`, `Blocklist.classify(...)`, `SieveBlocklistLoader.bundledLists()`. Canvas has a "Cage is hidden / capturing in background" state — model as a timed background mode. |
| **Witness · Trust** | `WitnessSigner.createSignedSnapshot(domain:comparison:secretKey:) → WitnessSnapshot`, `verifySnapshot(...)`; `AnswerLedger` drift; `Sentinel.capture()`/`diff(previous:current:)`. |
| **Account** | `LicenseManager` (Pro state, trial, activate). Pricing from canvas: **Pro $2/mo (3-day trial), Permanent $10 once**; "Local tools free forever", server-backed = Mirror/Warp/Leak. |

## 5. The one dependency — the Verdict contract (build against this)

The **Overall Verdict** card (Dashboard, and the end-of-run summary) binds to types I'm implementing
in `SyxxKit`. Target signatures (implement your view against these exact names):

```swift
public struct Conclusion: Codable, Sendable {
    public let verdict: Hypothesis                    // top hypothesis
    public let confidence: Int                        // 0…100, computed AFTER status gating
    public let probabilities: [HypothesisProbability] // sorted desc, sums ~1.0
    public let evidence: [EvidenceItem]               // ranked, strongest first
    public let sectionStatuses: [SectionStatusEntry]  // per-section run status — the DoW #3 gate
    public let nextSteps: [String]
    public let referralTriggered: Bool                // true when confidence < 75 OR margin < 30
    public let referral: Referral?                    // present when referralTriggered
    public let generatedAt: Date
    public var hasHardGatedSection: Bool { get }      // true if any section is untested/invalidated
}
// NB: `sectionStatuses` is the LAST, DEFAULTED init arg (`= []`), so the old mock below still builds.

public enum Hypothesis: String, Codable, Sendable, CaseIterable {
    case clean, spoofing, censorship, interception, contentTampering, privacyLeak, localCompromise, inconclusive
    public var title: String { get }                  // "Clean", "DNS spoofing / poisoning", …
    public var severity: Severity { get }             // → Io severity color
}

public struct HypothesisProbability: Codable, Sendable {
    public let hypothesis: Hypothesis
    public let probability: Double                     // 0…1  (render as bars)
}

public struct EvidenceItem: Codable, Sendable {
    public let section: Section                        // which of the six produced it
    public let observation: String                     // "Local answer diverges from public consensus"
    public let supports: [Hypothesis]
    public let rationale: String
}

public enum Section: String, Codable, Sendable { case prophet, mirror, portal, glutton, cage, witness }

// Per-section terminal status — the DoW #3 gate. `untested`/`invalidated` are HARD gates.
public enum SectionStatus: String, Codable, Sendable {
    case untested, invalidated, inconclusive, degraded, positive, negative
    public var blocksClean: Bool { get }   // true for untested/invalidated
}
public struct SectionStatusEntry: Codable, Sendable { public let section: Section; public let status: SectionStatus }

public struct Referral: Codable, Sendable {
    public let reason: String
    public let subreddits: [String]                    // ["r/AskNetSec", "r/NetSecStudents"]
    public let prefilledPost: String                   // copy-to-clipboard body
}

public enum Severity: String, Codable, Sendable { case clean, info, warning, high, critical, unknown }
```

**Render rules (cardinal — must match the engine's intent):**

- **`inconclusive` and any low-confidence result must NEVER render green.** Reserve the affirmative
  (green) treatment for `clean` with high confidence. Map `Hypothesis.severity` → Io color.
- Keep **three axes visually separate**: operation status (did the run finish) · finding (the
  verdict) · evidence integrity (witness signature). Do not let a green "signed OK" read as "safe."
  (This is the exact `syxx-witness` output bug we're fixing — don't reproduce it.)
- When `referralTriggered`, surface the "second opinion" block prominently with the copy-to-clipboard
  Reddit post.
- **`sectionStatuses` is a first-class axis — show per-section run status, and never dilute a hard state into confidence:**
  - `untested` = "not run" (hard) · `invalidated` = "ran, uninterpretable" (hard) · `inconclusive` = ran-but-undecided · `degraded` = ran-but-partial · `positive`/`negative` = the finding.
  - **"Clean" is impossible while any section is `untested`/`invalidated`** (`hasHardGatedSection == true`). Surface those sections explicitly (e.g. a "not run" chip), don't fold them into a lower confidence number.
  - Confidence is computed *only* from the sections that actually ran — treat it as reliability of the evidence present, not a proxy for coverage.

**Mock for building now** (drop into a preview/dev file, delete when the real type lands):

```swift
extension Conclusion {
    static let mockInconclusive = Conclusion(
        verdict: .inconclusive, confidence: 61,
        probabilities: [.init(hypothesis: .inconclusive, probability: 0.42),
                        .init(hypothesis: .interception, probability: 0.31),
                        .init(hypothesis: .clean, probability: 0.27)],
        evidence: [.init(section: .portal, observation: "Resolver is an unexpected on-link device",
                         supports: [.interception], rationale: "Not the default gateway; encrypted DNS off")],
        nextSteps: ["Enable DoH/DoT and re-run", "Identify the resolver device in Portal"],
        referralTriggered: true,
        referral: .init(reason: "Confidence 61% (< 75%)", subreddits: ["r/AskNetSec", "r/NetSecStudents"],
                        prefilledPost: "…"),
        generatedAt: .now)
}
```

## 6. Conventions / gotchas

- SwiftPM app, **no Xcode project** — build `swift build --product syxx-gui`; ship via `bundle-app.sh`.
- App is **windowed** (nav sidebar), not menu-bar (that's Nnyx/Zzyn). Keep `WindowGroup`.
- **Pro gating:** Mirror/Warp/Leak are server-backed — gate their run buttons on `LicenseManager`
  Pro/trial state; free tools (Prophet/Portal/Cage/Witness local paths) always run.
- **Honesty > polish:** every result view is tri-state (secure / tampered / inconclusive). If a
  `SyxxKit` call returns a `.inconclusive`/`.unknown`/degraded state, the view must show it as such,
  never as success. This is the project's cardinal rule (see `AGENTS.md`).
- Keep the `SyxxAppDelegate` (runtime Dock icon) and `Package.swift` `resources` I added.

## 7. Integration checklist

- [ ] Io components ported to `SyxxApp/Views/Components/`.
- [ ] Nav shell + the 6 check screens wired to the §4 entry points.
- [ ] Account screen wired to `LicenseManager` with the canvas pricing.
- [ ] Overall Verdict card built against §5 (mock now → real `Conclusion` on my drop).
- [ ] `swift build --product syxx-gui` green; `bundle-app.sh` produces `Syxx.app`.
- [ ] Ping me when the verdict view is ready so we integration-test the real `Conclusion`.
