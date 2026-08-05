# Syxx GUI Implementation Plan

## macOS (Swift 6 / SwiftUI) + Windows 11 (WinUI 3 / C#)

**Source of truth:** `Mockups-Web-Promos/SyxxApp.dc.html` (canonical component),
`Mockups-Web-Promos/Syxx Views.dc.html` (style guide + all views side-by-side),
`Mockups-Web-Promos/_ds/…/tokens/` (color, type, spacing tokens).
**Data contracts:** `macOS/Sources/SyxxKit/`, `AppModel.swift`, `Conclusion.swift`.
**Collision rules:** Bob owns `macOS/Sources/SyxxApp/**` only. Do not touch SyxxKit.
**Honesty rule (cardinal):** inconclusive/untested NEVER renders green. See HANDOFF-BOB.md.

---

## Part 0 — Design Token Translation

### 0.1 Color Palette (Syxx-flavoured dark theme)

The mockup overrides the Nnyx/Io base with a **violet/gold** palette in `SyxxApp.dc.html`
`renderVals()` darkT object. These are the authoritative values; they differ from Nnyx's
cyan-electric palette. The existing [`IoDesignSystem.swift`](macOS/Sources/SyxxApp/Views/Components/IoDesignSystem.swift)
already uses these exact hex values — no changes needed.

| Token | Hex (dark) | Swift name | Use |
|---|---|---|---|
| `--io-bg` | `#130d26` | `.ioBackgroundDark` | Canvas / deepest bg |
| `--io-surface` | `#1d1640` | `.ioSurfaceDark` | Card / panel |
| `--io-surface-elevated` | `#2a2154` | `.ioSurfaceElevated` | Chip / hover / secondary btn |
| `--io-border` | `#382c66` | `.ioBorderSubtle` | Hairline borders |
| `--io-blue` | `#8b5cf6` | `.ioBlue` | Primary action, links, selected item |
| `--io-electric` | `#b98cff` | `.ioElectric` | Secondary accent, Pro badge gradient end |
| `--io-accent-start` | `#f0ac38` | `.ioAccentStart` | Gold (sidebar label, dividers) |
| `--io-accent-end` | `#7c3aed` | `.ioAccentEnd` | Violet gradient end |
| `--io-critical` | `#f5405e` | `.ioCriticalRed` | Critical severity |
| `--io-high` | `#fa7317` | `.ioHighOrange` | High severity |
| `--io-warning` | `#f59e0a` | `.ioWarningAmber` | Medium / warning |
| `--io-success` | `#0d9487` | `.ioSuccessGreen` | Clean / healthy |
| `--io-text` | `#ffffff` | `.ioText` | Primary text |
| `--io-text-secondary` | `#a99fc8` | `.ioTextSecondary` | Muted / captions |
| `--syxx-label` | `#f2ba4a` | `.ioAccentStart` (close enough) | Section eyebrow labels, dividers |
| `--syxx-chrome` | `rgba(20,13,40,.74)` | `.ioSurfaceDark.opacity(0.74)` | Title bar bg |
| `--syxx-sidebar` | `rgba(20,13,40,.50)` | `.ioSurfaceDark.opacity(0.50)` | Sidebar bg |

**Light theme** tokens are defined in `IoDesignSystem.swift` `ioDynamic(light:dark:)` pairs.
The toggle is the `localDarkMode` binding in `ContentView.swift`.

### 0.2 Gradients

| Name | CSS | SwiftUI equivalent |
|---|---|---|
| Wordmark | `linear-gradient(100deg, #f0ac38, #e6c6dd 52%, #a96ad6)` | `LinearGradient(colors: [.ioAccentStart, Color(hex:"#e6c6dd"), Color(hex:"#a96ad6")], startPoint: .leading, endPoint: .trailing)` |
| Accent tile / Pro | `linear-gradient(135deg, #f0ac38, #7c3aed)` | `LinearGradient(colors: [.ioAccentStart, .ioAccentEnd], startPoint: .topLeading, endPoint: .bottomTrailing)` |
| App bg radial | `radial-gradient(120% 90% at 50% 0%, #241a48, #0c0820 72%)` | `RadialGradient(colors:[Color(hex:"#241a48"), Color(hex:"#0c0820")], center:.init(x:0.3,y:0), startRadius:0, endRadius:900)` |
| Permanent card | `linear-gradient(150deg, accent-start 12% surface, accent-end 16% surface)` | Use `color-mix` approximation; in Swift: two-stop gradient from `.ioSurfaceDark` mixed with `.ioAccentStart.opacity(0.12)` → `.ioAccentEnd.opacity(0.16)` |

### 0.3 Typography (Space Grotesk + Space Mono)

Fonts are already bundled in `macOS/Sources/SyxxApp/Resources/` and referenced in existing views.

| Scale | px | Weight | Swift helper |
|---|---|---|---|
| View title / hero | 28px | 600 | `.ioTitle1()` |
| Card title | 20px | 700 | `.ioTitle2()` |
| Section header | 18px | 600 | `.ioTitle3()` |
| Body | 14px | 400 | `.ioBody()` |
| Caption | 12px | 400 | `.ioCaption()` |
| Small | 11px | 400 | `.ioSmall()` |
| Eyebrow (ALL CAPS) | 10px + 0.06em tracking | 700 | `.ioCaption().weight(.bold)` + `.textCase(.uppercase).tracking(...)` |
| Mono body | 13–14px | 400/700 | `.ioMono(13)` / `.ioMonoBold(14)` |

### 0.4 Spacing & Corner Radii

| Concept | Value | Used for |
|---|---|---|
| View inset | 32pt | All main pane padding |
| Section gap | 24pt | VStack spacing between major sections |
| Card padding | 16–18pt | IoCard internal padding |
| Grid gap | 12–16pt | LazyVGrid / HStack grid gaps |
| Sidebar width | 258pt | Fixed — already correct in `CustomSidebar` |
| Card corner | 12pt | Standard IoCard cornerRadius |
| Panel corner | 14–16pt | Large panels / frames |
| Button/input corner | 8pt | Already in `IoFilledButtonStyle` |
| Chip corner | 6pt | StatusPill |
| Badge corner | 4pt | Severity badges |
| Pill corner | 999pt | Pill-shaped tags |

---

## Part 1 — Shell (ContentView + CustomSidebar)

### Current state

[`ContentView.swift`](macOS/Sources/SyxxApp/ContentView.swift) and the embedded `CustomSidebar`
are substantially correct. The following gaps need closing:

### 1.1 Title Bar

**Mockup:** macOS title bar is 44pt tall, `--syxx-chrome` backdrop, centered "Syxx — Lyin' Eyes"
text, real traffic-light dots at left. Windows title bar is 40pt, logo+title at left, standard
Win11 caption buttons (minimize/maximize/close) at right.

**macOS gap:** `WindowChromeConfigurator` hides the system title bar. The traffic lights are native
(left-aligned by AppKit). The centered label is correct. ✅ No changes needed.

**Windows gap:** Out of scope for macOS build; addressed in Part 11 (Windows plan).

### 1.2 Sidebar — What's Different from Mockup

The mockup sidebar uses **custom SVG icons** for each nav item (not SF Symbols). The current
`SidebarItem` uses `Image(systemName:)`. The visual difference is acceptable for the Store
launch; SF Symbols read clearly. This is a **cosmetic delta, not a blocker**.

**Gaps to close:**

- The `--syxx-label` gold divider pills between sidebar sections should be `height: 2pt`,
  `cornerRadius: 2pt`, `opacity: 0.85`. Currently correct in `ContentView.swift` line 143–147. ✅
- Sidebar footer: `StatusPill` + theme toggle + help + logs buttons. Currently correct. ✅
- **New requirement from HANDOFF-BOB.md §Scrollable Panels:** The sidebar content is likely
  short enough to fit without scroll on most macOS windows (800pt height). However, it currently
  uses `ScrollView`. Per the spec, if content fits, remove scroll. **Decision:** Keep `ScrollView`
  — the sidebar must accommodate any window height and the content can exceed the available height
  on small screens. `ScrollView` is justified here.

### 1.3 Background Radial Gradient

The main pane background in the mockup is:
`radial-gradient(130% 70% at 50% -12%, rgba(140,80,240,.20), transparent 62%)`
overlaid on `--io-bg`. The existing `IoCanvasBackground` (`IoDesignSystem.swift` line 56–73)
implements this correctly for dark mode. ✅

---

## Part 2 — What's New / Beta Screen (macOS)

### 2.1 Design spec (from `SyxxApp.dc.html` lines 972–1048)

**Layout:** Full-height column, dark `#0a1120` bg, NO scroll (fills the 800pt window).

- Header strip: 22pt top/32pt h-pad, gradient-to-dark bg, border-bottom.
  - Eyebrow: "FROM MILKANDGIN · PRIVATE BETA" in `.ioElectric`
  - Title 24px/700 "Meet the rest of the family" in `#eaf0ff` (use `.ioText` in light mode)
  - Subtitle 13px/`#93a2c4`
- Content area: 16pt top/32pt h-pad, `flex:1` = fills remaining height, column gap 14pt.
  - **Nnyx card** (flex:1): 14px corner, border `rgba(255,255,255,.08)`, bg `#0d1626`.
    Two-column grid: left = text (logo + bullets), right = two screenshots side-by-side.
  - **Zzyn card** (flex:1): Same structure, screenshots on LEFT, text on RIGHT.
  - **Sign-up strip** (fixed height ~70pt): Gradient border pill, horizontal layout with
    email input + "Reserve my seat" button.

**Scroll policy:** Static — no ScrollView. The three cards plus strip fill the 800pt window.
With a 44pt title bar + 44pt header strip, content area = ~712pt. Three flex cards + strip
distribute to fill exactly. If the window is resized smaller, the cards compress (min-height
not set = graceful). Keep this as non-scrollable; if the window height drops below 680pt,
content may clip — acceptable, Syxx has a minimum window size.

### 2.2 Required changes to `BetaView.swift`

**Current `BetaView.swift`** uses `ScrollView` wrapping a `VStack`. The mockup is **not scrollable**.

**Plan:**

1. Replace the outer `ScrollView` with a plain `VStack(spacing: 0)`.
2. Header strip: `VStack(alignment: .leading, spacing: 4)` with bg
   `LinearGradient(colors: [Color(hex: "#0c1526"), Color(hex: "#0a1120")], startPoint: .top, endPoint: .bottom)`
   - border-bottom. Pad: 22pt top, 16pt bottom, 32pt horizontal.
3. Content area: `VStack(spacing: 14)` `.frame(maxHeight: .infinity)` `.padding(16).padding(.horizontal, 16)`.
4. Nnyx card: `HStack(spacing: 0)` where left column = `VStack` (logo + bullets) at `.frame(maxWidth: .infinity)` and right column = `HStack` of two `Image` views with `object-fit: contain` equivalent (`.scaledToFit()`).
5. Zzyn card: Same but images LEFT, text RIGHT — use `HStack` with the image HStack appearing first.
6. Sign-up strip: Replace the current toggle+email grid with a single horizontal `HStack` matching the mockup (title/subtitle on left, email+button on right, gradient border). Remove the checkbox-based interest toggles — the mockup uses a single "Reserve my seat" CTA. **Keep the email validation guard.**
7. **WKWebView future hook:** Per HANDOFF-BOB.md, this panel should eventually be a `RemoteContentView` (WKWebView, JS off, origin-allowlisted). For the Store launch: ship the current SwiftUI layout. Leave a `// TODO: replace with RemoteContentView(url: …)` comment at the top of the content area for the follow-up. Do not implement WKWebView now; that is a separate task.

### 2.3 Data bindings

| Element | Binding |
|---|---|
| Email field | `@State private var email: String` (local, no AppModel) |
| Reserve button action | POST to `https://syxx.app/api/beta-signup` or local state `signedUp = true` |
| Nnyx/Zzyn images | `Image("nnyx-1")` etc. from `Bundle.module` (need to verify asset names) |

**Open question OQ-1:** Are `nnyx-1.png`, `nnyx-2.png`, `zzyn-1.png`, `zzyn-2.png` in the
app bundle? They're in `Mockups-Web-Promos/assets/` — they need to be copied to
`macOS/Sources/SyxxApp/Resources/` and declared in `Package.swift` `.copy("Resources")`.
The beta signup POST endpoint and response format need to be confirmed with Claude.

---

## Part 3 — Dashboard Screen

### 3.1 Design spec (from `SyxxApp.dc.html` lines 133–253)

**Layout:** `VStack(spacing: 16)` with 26pt top / 32pt horizontal padding.
**Scroll policy:** The mockup content (header + stat row + two info cards + support blurb + 2 upgrade cards) fits in 800pt minus the 44pt title bar = 756pt usable. Currently `DashboardView.swift` has no `ScrollView` — correct. ✅

**Differences from current `DashboardView.swift`:**

- The mockup shows a **5-column stat grid** (Trust score, Alerts today, Resolvers, Leak found, Queries seen) — NOT a ring + 4 stats. The current code has a `IoProgressRing` + 4 `IoStatCard`. **The mockup dropped the ring from the dashboard** in the final design; the ring lives in the Conclusion screen only.
- The stat row has a **"System status"** eyebrow + resolver label (`Resolver: 1.1.1.1` in success green) at the top of the card.

**Plan for Dashboard:**

1. Remove the `ZStack { IoProgressRing … VStack … }` ring cluster.
2. Expand stat grid to 5 columns: `LazyVGrid(columns: Array(repeating: .init(.flexible()), count: 5), spacing: 12)`.
3. Add "System status" eyebrow + resolver label row above the grid.
4. Trust score stat card: value = `trust.map(String.init) ?? "—"` (from existing computed var in `DashboardView.swift`).
5. All other bindings already correct.
6. macOS vs Windows: Windows Dashboard shows a DNS-fact carousel + network overview (firewall/router IP/ext IP/ISP/DNS/connection cards). macOS shows "why pay" cards + support blurb + upgrade cards. This conditional already exists via `isMac`/`isWin` in the mockup; on macOS this is fine as-is.

---

## Part 4 — Prophet Screen

### 4.1 Design spec (from `SyxxApp.dc.html` lines 255–322)

**Layout:** `VStack(spacing: 24)` with 32pt padding. No scroll.
**Four output cards** in a 2×2 LazyVGrid (gap 16pt):

- Local DNS server (mono IP + interface label)
- System DNS encryption status (colored dot + label + explanation)
- Returned domain records (scrollable list of `type / rdata / TTL` rows with left-border color)
- Returned DNSSEC records (shield icon + status)

**Scroll policy:** Header + form strip + warning banner + 2×2 grid all fit in 756pt. The
records card has an **internal** `ScrollView` (max-height 240pt in current code — correct for
the record list). The outer VStack is NOT scrollable. The current `ProphetView.swift` wraps in a
plain `VStack` — correct. ✅

**Gaps to close in `ProphetView.swift`:**

- The form input strip currently shows three fields (Domain, RecordType picker, Resolver IP).
  The mockup shows Domain input + Check button in a clean `HStack`, with the record type picker
  **removed** from the top bar (it's implicit). **Decision:** Keep the record type picker — it's
  a useful feature not visible in the static mockup but present in the mockup's dc.html logic.
  This is an additive enhancement; do not remove.
- The "Records returned, but unverified" warning banner: the mockup shows it ALWAYS visible
  as a contextual note. The current code shows it always too. ✅
- The 2×2 grid: currently `LazyVGrid` with two `.flexible()` columns. ✅ but the left column
  has two short cards (DNS server + encryption) and the right column has two tall cards (records +
  DNSSEC). The mockup uses `align-items: start` so the shorter cards don't stretch. In SwiftUI:
  `LazyVGrid` with `alignment: .top` on the grid rows, or use explicit `HStack(alignment: .top)`.
- **Record row left-border color:** The mockup uses `border-left: 3px solid var(--io-success)`.
  In SwiftUI: add a `.overlay(alignment: .leading) { Rectangle().fill(.ioSuccessGreen).frame(width: 3) }`.

---

## Part 5 — Mirror Screen

### 5.1 Design spec (from `SyxxApp.dc.html` lines 324–391)

**Layout:** `VStack(spacing: 24)` with 32pt padding. No scroll.
**Two sections:**

1. **Resolver comparison** (2-col grid): Remote resolver card + Local resolver card + result banner.
2. **Static content matching** (2-col grid): Remote hash card + Local hash card + hash-match row + result banner.

**Scroll policy:** All content fits in 756pt. No outer ScrollView. Current `MirrorView.swift` is
in a plain `VStack` — correct. ✅

**Gaps to close in `MirrorView.swift`:**

- The mockup renders the resolver section with individual resolver cards (Remote vantage + Local),
  not the full `publicResolverOutcomes` table. The current code iterates all public resolvers
  (Cloudflare, Google, Quad9) as separate rows. **Decision:** Keep the full table — it provides
  more diagnostic signal. The mockup shows a simplified 2-card view; the real app is richer.
- The section label eyebrow "Comparing locally pulled DNS resolvers..." uses `--syxx-label` gold.
  In current code: `.foregroundColor(.ioTextSecondary)`. **Fix:** use `.ioAccentStart`.
- Both sections need the result banner with colored left-border. Current code uses `.background(verdictColor.opacity(0.1))` + `.overlay(stroke)`. Correct. ✅
- **Static content hash section** is currently missing from `MirrorView.swift` — it only shows
  the DNS resolver comparison, not the Warp content hash comparison. The `warp: WarpResult?`
  binding exists but only shows script/stylesheet/iframe counts. **Fix:** Add the SHA-256 hash
  comparison rows. The `WarpResult` struct has `localScriptURLs`/`remoteScriptURLs`; for the
  hash display, use the warp `isSecure`/`isInconclusive` flags and show a simplified hash match
  widget (the actual SHA-256 is not surfaced by `WarpResult` — show the match verdict).

---

## Part 6 — Portal Screen

### 6.1 Design spec (from `SyxxApp.dc.html` lines 393–462)

**Layout:** `VStack(spacing: 12)` with 18pt top / 30pt horizontal padding. No scroll.
**Four sections stacked:**

1. Header + domain input + Check button
2. 2-col grid: Resolvers card + Interfaces+VPN card (compact, ~100pt each)
3. LAN devices grid (3-col, 6 rows)
4. Route hop list + warning banner

**Scroll policy:** All content at target data sizes fits in 756pt. The mockup is NOT scrollable.
The current `PortalView.swift` is a plain `VStack` without `ScrollView` — correct. ✅

**Gaps to close in `PortalView.swift`:**

- Interfaces card: shows `en0 · Wi-Fi / 10.0.1.162`, `utun4 · VPN / 10.2.0.2`, `lo0 · loopback`
  as rows. Current code only shows interface names without IPs. **Fix:** The `NetworkStateReport`
  needs to expose IP addresses per interface. This requires a SyxxKit change — **leave a note for
  Claude** (`// TODO(Claude): expose per-interface IP in NetworkStateReport`).
- LAN devices grid: Currently shows "not yet available" placeholder. The mockup shows a 3-column
  grid with `ip / mac / note` rows. LAN cartography IS implemented in SyxxKit
  (`LANCartographer`). **Fix:** Wire `AppModel.runNetworkState()` to also call
  `LANCartographer` and store results in `model.sectionInputs`. This is an `AppModel.swift` change
  — coordinate with Claude per HANDOFF-BOB.md before touching `AppModel.swift`.
- Route hop list: Currently shows "not yet available" placeholder. The mockup shows a 3-row
  traceroute table with hop number / host / label / latency / severity badge. `syxx-route` exists
  as a CLI. **Fix:** Wire a traceroute call (Process launch to `/usr/sbin/traceroute`) into
  `runNetworkState()`. Out of scope for this planning pass — add as a follow-up note.
- Portal section padding: mockup is `18px 30px` (vs current `32pt`). **Fix:** change
  `.padding(32)` to `.padding(.horizontal, 30).padding(.top, 18)`.

---

## Part 7 — Glutton Screen

### 7.1 Design spec (from `SyxxApp.dc.html` lines 464–523)

**Layout:** `VStack(spacing: 24)` with 32pt padding. No scroll.

**Sections:**

1. Header + "Check for Leaks" button (right-aligned)
2. Italic quote + description body
3. 2-col grid: Feature Status card + VPN/Encryption card
4. Test Label + probe results list
5. Collector result panel (terminal-style top bar + content area)

**Scroll policy:** All content fits in ~756pt at typical data sizes (2 probe rows). No scroll.
The current `GluttonView.swift` uses a `ScrollView`. Per HANDOFF-BOB.md, remove it if content fits.
**Fix:** Remove the outer `ScrollView`; wrap in a plain `VStack` with `.frame(maxHeight: .infinity, alignment: .top)`.

**Gaps to close in `GluttonView.swift`:**

- The description text should be an **italic quote** in `--font-rounded` (Space Grotesk) followed
  by a secondary body copy. Current code has a single non-italic paragraph. **Fix:** Add `.italic()` and `font-style` to the quote line.
- Feature Status card: shows "⏳ Trial · unlocked" with time remaining. Current code calls
  `LicenseManager.isFeatureUnlocked(isPro: model.isPro)` and shows the result. The mockup shows
  `trial hours remaining`. **Fix:** The `isFeatureUnlocked` return value includes `.message` which
  should carry the hours-remaining string when on trial. Verify this with Claude.
- Collector result panel: the mockup shows a terminal-header (`$ syxx-leak --collector …`) + a
  result section below with base domain, resolvers count, and a "No Leak" / "Leak" verdict row.
  The current code shows a static info block. **Fix:** When `model.sectionInputs.leak` is set,
  render the collector result panel using the `LeakTestResult` fields: `verdict`, `observedResolvers`.
  Map `.confirmed` → "⚠️ LEAK DETECTED" in critical red; `.notDetected` → "✅ No Leak" in success
  green; `.inconclusive` → "Inconclusive — collector not reached" in warning amber.
- **Probe failure state:** Per ROADMAP.md USER Note — when probes fail, explain WHY and what it
  means for privacy, BEFORE showing any conclusion. The probe rows already show "failed" in red.
  **Add:** If all probes failed, show an explanatory note: "All probe dispatches failed. This
  usually means the DNS resolvers are unreachable or the VPN is blocking outbound DNS. Without a
  successful probe, Syxx cannot determine whether your DNS leaked. Use `syxx-leak --collector`
  from Terminal with a working network connection."

---

## Part 8 — Cage Screen

### 8.1 Design spec (from `SyxxApp.dc.html` lines 525–577)

**Layout:** `VStack(spacing: 20)` with 32pt padding. No scroll.
**Section order:**

1. Header + duration input + "Close the Cages" primary btn + "Hide the Cages" secondary btn
2. Italic quote + description
3. Status pill (CAPTURING/IDLE) + events count
4. Terminal pre block (fixed height ~150pt, internal scroll)
5. Column header row + capture rows list

**Scroll policy:** The terminal block has a fixed max-height with internal scroll. The capture
rows list grows. At > ~6 rows the outer panel would overflow. **Decision:** The outer VStack
should be in a `ScrollView` only when the capture rows exceed a threshold. Simple approach:
always wrap the entire content in a `ScrollView` since the terminal + rows can grow. This is
correct behavior and justified. The current `CageView.swift` uses `ScrollView` correctly. ✅

**Gaps to close in `CageView.swift`:**

- The title should be "Live Query Capture" (28px/600) not "CAGE • DNS CAPTURE" as the main
  heading. The eyebrow is `CAGE · DNS CAPTURE`. **Fix:** Add a `Text("Live Query Capture").font(.ioTitle1())` below the eyebrow.
- The italic quote is missing from the current view. **Fix:** Add the italicized quote text.
- The column header grid uses `grid-template-columns: 80px 1.5fr 1.3fr 90px 96px` in the mockup.
  The current code uses fixed widths: `80 / maxWidth / 160 / 100 / 80`. **Fix:** The current
  proportional approach is fine; adjust to match the mockup column widths approximately.
- The capture rows left-border color: current code uses background color variations but no
  left-border. **Fix:** Add `.overlay(alignment: .leading) { Rectangle().fill(row.severity.color).frame(width: 3) }`.
- **Caged/Hidden overlay:** Current code implements this correctly with the `if model.cageHidden` ZStack. ✅

---

## Part 9 — Witness Screen

### 9.1 Design spec (from `SyxxApp.dc.html` lines 579–651)

**Layout:** `VStack(spacing: 22)` with 32pt padding. No scroll for the form+testimony.

**Scroll policy:** The form section + testimony output + validation report + footer button can
exceed 756pt when both testimony and validation are shown simultaneously. **Use `ScrollView`** here —
the current `WitnessView.swift` already does. ✅

**Gaps to close in `WitnessView.swift`:**

- Testimony output card: shows "Creating signed Witness Testimony for {domain} via {resolver}."
  - snapshot signature row + "Saved to ~/Documents/Testimony.txt" note.
  Current code shows this correctly IF `witnessSignature` is set. However, the "Saved to
  ~/Documents/Testimony.txt" claim (line 99–103) is **fabricated** — no file is actually saved.
  Per AGENTS.md/ROADMAP.md honesty rules: **Remove** the "Saved to" line. Replace with:
  "Snapshot created in memory. Use `syxx-witness` from Terminal to persist to disk."
- Validation report: current code shows a hardcoded timestamp "2026-07-05 23:00:46 +0000".
  **Fix:** Use `model.witnessSnapshot?.createdAt` formatted via `Date.FormatStyle`.
  The `WitnessSnapshot` struct needs a `createdAt: Date` field — leave a note for Claude if missing.
- "Validation was conclusive" / "inconclusive" text: current code says "conclusive" always.
  **Fix:** If `model.witnessSnapshot?.comparison.isInconclusive == true`, say "inconclusive";
  if `isRedirectionDetected`, say "concluded: redirection detected"; otherwise "conclusive: no redirection".
- The validation result row background should match the verdict color:
  `.borderLeft(color: redirectionDetected ? .ioCriticalRed : .ioSuccessGreen)`.
- "Free the Witnesses" button footer: `margin-top: auto` in mockup = push to bottom of available
  space. In SwiftUI: `Spacer()` before the HStack. Already present. ✅

---

## Part 10 — Conclusion / Verdict Screen

### 10.1 Design spec (from `SyxxApp.dc.html` lines 653–866)

This is the most complex screen. The canonical layout uses a `ScrollView` since the conclusion
output is long (evidence groups + next steps + referral block can be 1560pt tall — the mockup
explicitly shows the full-height version at `vh=1560`).

**Scroll policy:** Requires `ScrollView`. The current `ConclusionsView.swift` does NOT use
`ScrollView` — it clips the content. **Fix:** Wrap the main VStack in `ScrollView`.

**Layout (ordered, within 32pt h-pad / 24pt top-pad):**

1. **Header row:** `Verdict · {domain}` eyebrow, "Conclusion" title (28px), "About ▾" button,
   tab switcher ("Suspicious" / "Clean"), right-aligned: generated-at timestamp + "Bundle signed" badge.
   **Current state:** `ConclusionsView.swift` shows a simple header with "Generate Synthesis Diagnosis"
   button. The mockup header is richer. The About collapsible, tab switcher, and signed-bundle badge
   are not currently implemented. **Add:** tab switcher that toggles between suspicious/clean view
   in the mockup data — in the real app this doesn't apply (the engine produces one verdict);
   remove the tabs. The About collapsible is worth keeping; add as a disclosure group.

2. **Verdict + confidence banner:** colored bg card with verdict icon / verdict name / flavor
   chip / severity badge / confidence bar. Plus "Incomplete run" indicator when `hasHardGatedSection`.
   **Current state:** `OverallVerdictView.swift` already implements most of this correctly.
   **Gaps:** The confidence bar (horizontal progress bar) is replaced with `IoProgressRing` in
   the current code. The mockup uses a flat horizontal bar. **Fix:** Add a horizontal bar as the
   primary indicator; keep the `IoProgressRing` as supplementary (it's in the top-right corner
   in the current code — fine).

3. **2-col grid: Hypothesis distribution + Discipline status:**
   Left: `ForEach(conclusion.probabilities)` with label + horizontal bar.
   Right: `LazyVGrid(columns: [.flexible(), .flexible()], spacing: 8)` of section status chips.
   Plus a legend row below the right column.
   **Current state:** `OverallVerdictView.swift` shows probabilities as bars (correct) and a
   3-column grid for section statuses. **Fix:** Change section status grid to **2 columns**
   matching the mockup. Add the legend row below.

4. **Evidence section:** "Strongest signal" lead-finding card (click to open discipline).
   Then evidence groups per section, each with a disclosure-style header (`{SectionName} open ↗`)
   and strong/weak evidence rows. Weak signals behind a "+N weak signals ▾" toggle.
   **Current state:** `OverallVerdictView.swift` shows a flat evidence list with no grouping
   by section and no weak-signal toggle. **Fix:** Add grouping headers and the weak-signal
   collapsible. Use `@State private var showWeakSignals = false`.

5. **Next steps:** Simple list of bullet points prefixed with `→`. Currently correct. ✅

6. **Referral block:** "Get a second opinion" section with violet border, subreddit pills,
   pre-filled post textarea + Copy button, links to subreddits. Currently correct in
   `OverallVerdictView.swift`. ✅

7. **Pre-run state** (when `model.conclusion == nil`): Show the discipline checklist with
   "Generate Synthesis Diagnosis" button. Currently in `ConclusionsView.swift`. ✅

### 10.2 Key binding deltas

| Element | Current binding | Required |
|---|---|---|
| Verdict title | `conclusion.verdict.title.uppercased()` | Keep + add flavor chip from `conclusion.verdict` description |
| Confidence indicator | `IoProgressRing` | Add horizontal bar + keep ring |
| Evidence grouping | Flat list | Group by `item.section`, add section header button |
| Weak signals | Always shown | Collapsible behind `@State showWeakSignals` |
| Section status grid | 3 columns | 2 columns |
| Domain in header | Not shown | Read from `model.domain` |

---

## Part 11 — License Screen

### 11.1 Design spec (from `SyxxApp.dc.html` lines 868–922)

**Layout:** `VStack(spacing: 24)` with 32pt padding. Uses `ScrollView` currently.
**Scroll policy:** Header + 2-col tier grid + activate box fits in ~756pt. **Remove** the
`ScrollView` wrapper. The current `LicenseView.swift` wraps in `ScrollView` — fix.

**Gaps:**

- **Payment buttons:** Per HANDOFF-BOB.md §License Page: "send people to the website,
  `https://syxx.app`." Replace the `model.isPro = true` action on "Start Pro Trial" and
  "Buy Permanent" with `NSWorkspace.shared.open(URL(string: "https://syxx.app")!)`.
- The tier grid uses a `Grid(GridRow {…})` layout. The mockup uses a 2-col CSS grid with
  equal columns. The current code is close but uses `Grid` which may not handle the stretched
  equal-height cards. **Fix:** Replace `Grid`/`GridRow` with `HStack(alignment: .top, spacing: 16)` +
  two `VStack` children each `.frame(maxWidth: .infinity)`.
- The Permanent License card background is the accent gradient, not a flat surface. Currently
  implemented with a `LinearGradient`. ✅
- The "Activate a License Key" box: the "Activate" button currently sets `model.isPro = true`
  without actually calling `LicenseManager`. Per AGENTS.md security rules: gate on real
  LicenseManager validation. **Fix:** Call `LicenseManager.activate(key:)` and set
  `model.isPro` only on a successful response. This requires `AppModel.swift` coordination
  (leave note for Claude).
- Machine ID line: currently shows hardcoded "7F3A-C1D9-4E20". **Fix:** Call
  `LicenseManager.machineFingerprint()` and display the first 8 chars. Leave note for Claude
  to expose this as a public API if not already.

---

## Part 12 — Windows Welcome Screen

### 12.1 Design spec (from `SyxxApp.dc.html` lines 925–968)

**Target:** Windows 11, WinUI 3, C# 13 / .NET 10. This is a new file:
`Windows/SyxxUI/Views/WelcomeView.xaml` (when the WinUI project is scaffolded).

**Layout (XAML):**

```
ScrollViewer (VerticalScrollBarVisibility=Hidden if content fits, else Auto)
  StackPanel Padding="32" Spacing="22"
    -- Logo + Title row: Image(56×56) + StackPanel(eyebrow + "Lyin' Eyes for Windows" title)
    -- Description TextBlock MaxWidth=720
    -- UniformGrid Columns=2 spacing=14: "Local tools" card + "Backed by syxx.app" card
    -- MilkandGin support blurb card (Grid 1-col, icon+text row)
    -- UniformGrid Columns=2 spacing=14: Pro Monthly card + Permanent License card
```

**ViewModel bindings:**

| Control | Binding | Source |
|---|---|---|
| "Start Pro Trial" button | `Command=GoToWebsiteCommand` | Opens `https://syxx.app` via `Launcher.LaunchUriAsync` |
| "Buy Permanent" button | Same | Same |

**Scroll policy:** Content fits in 760px (800px window - 40px title bar). Disable scroll.
`ScrollViewer.VerticalScrollBarVisibility="Disabled"`.

---

## Part 13 — Windows GUI Project Scaffold

The Windows project currently has only CLI executables. A GUI project needs to be created.

**Plan:**

1. New project: `Windows/SyxxUI/SyxxUI.csproj`
   - TargetFramework: `net10.0-windows10.0.19041.0`
   - UseWinUI: true
   - OutputType: WinExe
   - Reference: `Syxx.Kit.csproj`
2. `App.xaml` / `App.xaml.cs` — standard WinUI bootstrap.
3. `MainWindow.xaml` — shell with NavigationView (sidebar) + Frame (content).
   - NavigationView style: matches the CSS sidebar layout (258px wide, violet/gold palette).
   - NavigationView.Header: `Border` with logo + "Syxx" title gradient text.
   - NavigationView items: same 10 items as macOS (Welcome, Dashboard, Prophet, Mirror,
     Portal, Glutton, Cage, Witness, Conclusion, License).
4. Fonts: Embed Space Grotesk + Space Mono as resources and register via `FontFamily` URI.
5. Colors: Define as `ResourceDictionary` mapping Io tokens to `Color` resources.
6. `ViewModels/AppViewModel.cs` — mirrors `AppModel.swift`, implements `INotifyPropertyChanged`
   (or use CommunityToolkit.MVVM `ObservableObject`).
7. Per-screen UserControls (10 total, matching macOS).

**This scaffold is a significant effort — scope it as a separate subtask.**
`Package.swift` equivalent: add to `Windows/Syxx.sln` when scaffolded; announce before editing.

---

## Part 14 — RemoteContentView (WKWebView / WebView2)

Per HANDOFF-BOB.md §What's New and §Welcome panels:

**macOS — `RemoteContentView.swift`:**

```swift
// macOS/Sources/SyxxApp/Views/Components/RemoteContentView.swift
// WKWebView wrapper: JS off, origin allow-list, offline cache, ephemeral store.
// Payment links → NSWorkspace.shared.open(); never navigate in-WebView.
```

Implementation steps:

1. `struct RemoteContentView: NSViewRepresentable` wrapping `WKWebView`.
2. `WKWebViewConfiguration.defaultWebpagePreferences.allowsContentJavaScript = false`.
3. `WKWebView.navigationDelegate` blocks non-Dimension navigation and opens externally.
4. Cache: on successful load, write HTML to `URL.applicationSupportDirectory/syxx-whats-new.html`.
   On failure, load cached file.
5. Ephemeral store: `WKWebViewConfiguration.websiteDataStore = .nonPersistent()`.
6. CSP header (from server): `default-src 'self'; script-src 'none'; img-src data: 'self'`.

**Plan for When's New:** Replace the `BetaView.swift` content with `RemoteContentView` pointing
to `https://syxx.app/app/whats-new/macos`. The current SwiftUI layout ships first (Store
launch); `RemoteContentView` is a follow-up. **Do not implement now.**

**Windows — `RemoteContentPanel.xaml`:**

- `WebView2` control, `CoreWebView2Settings.IsScriptEnabled = false`.
- Navigation handler: block non-Dimension hosts, open externally via `Launcher.LaunchUriAsync`.
- Same offline-cache pattern.

---

## Part 15 — Scroll Policy Summary

| Screen | Scroll | Reason |
|---|---|---|
| What's New | ❌ No | 3 flex cards fill the window height exactly |
| Dashboard | ❌ No | All content fits; already non-scrollable |
| Prophet | ❌ No outer / ✅ inner records list | Records list overflows internally |
| Mirror | ❌ No | All resolver+hash content fits |
| Portal | ❌ No | With current data volumes; fix if LAN grows |
| Glutton | ❌ No | Remove existing ScrollView |
| Cage | ✅ Yes | Terminal block + growing row list can overflow |
| Witness | ✅ Yes | Testimony + validation together exceed 756pt |
| Conclusion | ✅ Yes | 1560pt full height documented in mockup |
| License | ❌ No | Remove existing ScrollView; 3 sections fit |

---

## Part 16 — Implementation Order

Ordered by dependency and risk. Each phase must leave the build green before proceeding.

### Phase 1 — Token + design system alignment (LOW risk, no AppModel touch)

- Verify `IoDesignSystem.swift` tokens match the Syxx-specific `darkT` values above. ✅ (already correct)
- Fix `--syxx-label` gold color usages: any `.ioTextSecondary` used as a section eyebrow should be `.ioAccentStart`.
- Files: `IoDesignSystem.swift` (read-only verify), then each view file's eyebrow labels.

### Phase 2 — BetaView redesign (no AppModel touch)

- New layout per §2.2. Remove ScrollView, add header strip, Nnyx/Zzyn cards with screenshots, sign-up strip.
- Add asset images to Resources/ and Package.swift.
- Files: `BetaView.swift`, `macOS/Sources/SyxxApp/Resources/` (new images), `macOS/Package.swift`.

### Phase 3 — Dashboard stat grid (no AppModel touch)

- Remove ring, add 5-column stat grid, add "System status" header.
- File: `DashboardView.swift`.

### Phase 4 — Scroll policy fixes (no AppModel touch)

- `GluttonView.swift`: remove outer ScrollView.
- `LicenseView.swift`: remove ScrollView; redirect payment buttons to syxx.app.
- `ConclusionsView.swift`: add ScrollView.

### Phase 5 — Conclusion screen enrichment (no AppModel touch)

- `OverallVerdictView.swift`: add horizontal confidence bar, section status 2-col grid + legend,
  evidence grouped by section with weak-signal toggle, About collapsible.
- `ConclusionsView.swift`: scroll wrapper.
- Files: `ConclusionsView.swift`, `OverallVerdictView.swift`.

### Phase 6 — Per-screen polish (no AppModel touch unless noted)

- `ProphetView.swift`: left-border on record rows, alignment fix.
- `MirrorView.swift`: eyebrow gold color, hash comparison widget.
- `CageView.swift`: title fix, italic quote, left-border on capture rows.
- `WitnessView.swift`: remove fabricated "Saved to" line; honest validation wording.
- `PortalView.swift`: padding fix (30pt h-pad / 18pt top).

### Phase 7 — AppModel-touching fixes (coordinate with Claude)

- `GluttonView.swift`: wire `LeakTestResult` collector result panel + probe-failure explanation.
- `LicenseView.swift`: wire real LicenseManager activation.
- `PortalView.swift`: LAN device data (after Claude wires `LANCartographer` into `sectionInputs`).
- Announced edits to `AppModel.swift` per HANDOFF-BOB.md.

### Phase 8 — Windows GUI scaffold (new project, new files)

- Create `Windows/SyxxUI/` project per §13.
- Shell + NavigationView.
- All 10 UserControls.

---

## Open Questions

| ID | Question | Blocking |
|---|---|---|
| OQ-1 | Are `nnyx-1.png` / `nnyx-2.png` / `zzyn-1.png` / `zzyn-2.png` being bundled into the app? | Phase 2 |
| OQ-2 | What does the beta signup POST endpoint expect (fields, response)? | Phase 2 |
| OQ-3 | Does `LicenseManager` expose `machineFingerprint()` as a public method? | Phase 6/7 |
| OQ-4 | Does `WitnessSnapshot` have a `createdAt: Date` field? | Phase 6 |
| OQ-5 | Does `LicenseManager.isFeatureUnlocked` carry trial hours remaining in `.message`? | Phase 7 |
| OQ-6 | What is the Dimension server path for macOS "What's New" HTML feed? | RemoteContentView |
| OQ-7 | Will Windows Store submission require a UWP-style `Package.appxmanifest`? | Phase 8 |
| OQ-8 | Should the Windows GUI share any `.cs` ViewModel code with `Syxx.Kit`? | Phase 8 |
