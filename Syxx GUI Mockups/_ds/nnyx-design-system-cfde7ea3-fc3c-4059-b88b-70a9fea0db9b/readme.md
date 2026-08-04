# Nnyx Design System ("Io")

A premium, dark, slate-based design system for **Nnyx** — a rootkit, malware,
persistence, and threat-intelligence scanner for **macOS Tahoe (26.x) on Apple
Silicon**, by **MilkandGin LLC**. Nnyx ships a unified CLI (`nnyx`) and a SwiftUI
GUI that share a multi-engine scanning framework. This design system reproduces
the GUI's visual language ("Io") on the web so you can build branded mocks,
slides, prototypes, and production-adjacent UI.

Nnyx positions itself less like a consumer AV and more like a **security control
plane**: its differentiator is a *governed trust subsystem* — auto-derived
known-good binaries stay separate from human trust overrides, all writes are
admin-authenticated and helper-mediated, audited, and reversible.

## Sources used to build this system
- **Codebase (ground truth):** the attached `Nnyx/` repo — SwiftUI app under
  `Sources/NnyxApp/`. The visual system is defined in
  `Views/Components/IoDesignSystem.swift` (colors, type, `IoCard`, `IoButton`,
  `IoSeverityBadge`, `IoStatCard`, `IoProgressRing`, `ioGlow`, `SigningSource`),
  `Views/Components/GlassPanel.swift`, and `Views/LogoView.swift`. Screens were
  recreated from `IoDashboardView.swift`, `ScanView.swift`, `DetectionsView.swift`,
  and `TrustDatabankView.swift`.
- **GitHub:** `https://github.com/milkandginnn/Nnyx` (private) — same codebase as
  the local mount. Explore it for the full Swift source if you have access; it is
  the authoritative reference for behavior and exact values.
- **Design notes:** `DESIGN.md`, `nnyx-gui-plan.md`, and
  `Macware Scanner Design Elements.txt` in the repo root.

---

## CONTENT FUNDAMENTALS — how Nnyx writes

**Voice: calm, precise, technical, and reassuring.** Nnyx is a security tool, so
copy is direct and evidence-led — it states what was observed, what the app
already did, and what you should do, in that order. It never hypes or alarms
without cause.

- **Person:** Addresses the user as **"you"**; refers to the product as
  **"Nnyx"** in the third person ("Nnyx will stop flagging…", "What Nnyx did").
- **Casing:** Title Case for view titles and section headers ("System Protection
  Status", "Cryptographic Trust Databank"). **UPPERCASE** micro-labels for
  eyebrows, stats, and badges ("NAVIGATION", "THREAT", "EVENTS COUNT", "CRITICAL",
  "RUNNING"). Sentence case for body and helper text.
- **Numbers & units:** Always concrete — "2,481 files scanned in 3.4s",
  "Score: 12 (Low Risk)", "24,711 events", "12s ago". Monospace for paths,
  hashes, versions, team IDs, and terminal commands.
- **Status language:** short, declarative state words — *Optimal, Active, On,
  Healthy, Running, Stopped, Not Installed, Protected, Investigate Findings,
  Immediate Action Required.*
- **Findings triad:** every detection is framed as **Description → Evidence →
  What Nnyx did → What you should do.** Remediation copy is imperative
  ("Quarantine the binary and remove the LaunchDaemon").
- **Reassurance on empty states:** "Your system appears clean and secure",
  "No threats detected". Clean = calm green, never empty-feeling.
- **Emoji:** used **sparingly and functionally**, not decoratively. The only
  sanctioned uses mirror the app: signing-source badges (🔐 Apple System,
  🛒 Mac App Store, 📜 Developer ID, 🛠️ Development, ⚠️ Unsigned) and the two
  footer affordances (💡 Help, 📁 Logs). Do not add emoji elsewhere.
- **Honesty about limits:** the app openly states what it cannot do ("not mocked
  in this kit", "requires Full Disk Access", "requires administrator approval").
  Keep that candor.

Example (verbatim tone): *"Nnyx cannot reach the internet — host did not respond.
This is almost always caused by a firewall rule silently blocking Nnyx's outbound
connections. Network features will be skipped if you continue."*

---

## VISUAL FOUNDATIONS

**Overall vibe:** a premium, dark "security console" — deep navy-slate canvas,
electric-cyan brand action color, violet secondary, and crisp semantic colors
mapped to threat severity. Glassy surfaces, soft accent glows, restrained
spring motion. It feels like a native macOS pro app, not a web dashboard.

- **Color:** slate-222 neutral ladder (`#0F1629` bg → `#1A2138` surface →
  `#26304A` elevated → `#334561` border). Brand = **electric cyan `#05B5D4`**
  (primary action, selection, links, focus); **violet `#8C5CF5`** secondary +
  gradient end. Severity is the semantic spine: Critical `#F5405E`, High
  `#FA7317`, Medium/Warning `#F59E0A`, Low/Success `#0D9487` (teal), Info violet.
  A separate 5-color **signing-source** palette encodes provenance.
- **Type:** the **Apple system font** (SF Pro) — reproduced on web with the
  native system stack. SF Pro **Rounded** for big numbers, scores, and the
  wordmark; **SF Mono** for paths, hashes, versions, and terminal output.
  Scale: Hero 34 / Title 28·22·18 (semibold) / Body 14 / Caption 12 / Small 11 /
  eyebrow 10 (uppercase, tracked) / badge 9.
- **Spacing:** exact shipped values, **not** a strict 4/8 grid (e.g. sidebar
  items are 10px vertical). View inset 32, card padding 16–24, section rhythm 24.
- **Backgrounds:** flat deep-slate fills — **no photographic imagery, no
  repeating patterns/textures**. The only gradients are (a) the brand
  cyan→violet accent (logo tile, header avatar) and (b) the severity-graded
  threat-ring stroke. A subtle dark-teal radial glow may sit behind window
  chrome.
- **Corner radii:** badge 4 · chip 6 · button/sidebar-item/toggle 8 · card/stat
  12 · glass panel 16 · pills fully round.
- **Cards:** slate `#1A2138` fill, 12px radius, soft drop shadow
  (`0 4px 8px rgba(0,0,0,.2)`). Active stat tiles get a faint **8% color wash +
  30% color border**. Glass panels add a 1px **white-10% hairline** edge and a
  16px radius.
- **Shadows & the signature glow:** the brand effect is **`ioGlow`** — a soft
  double drop-shadow in an accent/semantic color (`0 0 8px @30%`, `0 0 12px @20%`)
  on the logo, threat ring, status pills, and key state icons. Real depth
  shadows are minimal and black-based.
- **Borders:** 1px `#334561` for cards/dividers; 1px white-10% hairline for
  glass; selected sidebar item is a solid cyan fill (no border).
- **Animation:** SwiftUI **spring** feel (`cubic-bezier(.34,1.56,.64,1)`) for
  hover lifts and selection; ease-in-out for progress. Durations ~0.15s (row
  tint), ~0.2s (scale springs), ~0.8s (ring fill). Pulse rings on live status.
  No infinite decorative motion on content.
- **Hover states:** **scale up** (1.02 rows/cards, 1.05 quick-scan buttons) +
  **brightness 1.1** on filled buttons; faint slate fill on hover rows.
- **Press/selected:** selected sidebar item = solid cyan + white semibold label;
  toggles fill with an 8% cyan wash + 40% cyan border + check icon.
- **Transparency & blur:** sparingly. The app deliberately **replaced** the
  metallic `NSVisualEffectView` material with a flat slate surface for palette
  consistency — so prefer solid surfaces; use `color-mix` washes for tints, not
  heavy blur.
- **Imagery vibe:** there is essentially no photography; the aesthetic is
  vector + type + glow on dark slate, cool-toned throughout.

---

## ICONOGRAPHY

Nnyx is a native macOS app and uses **SF Symbols** throughout (e.g.
`lock.shield.fill`, `checkmark.seal.fill`, `shield.lefthalf.filled`,
`waveform.path.ecg`, `cpu.fill`, `magnifyingglass.circle.fill`,
`exclamationmark.triangle.fill`, `key.fill`, `archivebox.fill`). SF Symbols are
not redistributable and are unavailable on the web.

**Substitution (flagged):** this system uses **[Lucide](https://lucide.dev)** via
CDN as the closest match — same clean 2px-stroke outline style. The UI kit loads
`lucide-react` and maps each SF Symbol to its nearest Lucide equivalent (e.g.
`shield.lefthalf.filled` → `ShieldHalf`, `magnifyingglass.circle.fill` →
`ScanSearch`, `cpu.fill` → `Cpu`, `key.fill` → `KeyRound`, `archivebox.fill` →
`Archive`). When building for production on Apple platforms, prefer the real SF
Symbols; Lucide is the web stand-in.

**Emoji as icons:** used only for the signing-source badges and the Help/Logs
footer affordances (see Content Fundamentals). No other emoji.

**Logo:** the app logo is defined as SwiftUI vector paths in `LogoView.swift`
(a gradient rounded tile holding a white shield + magnifier lens with a red
center dot). It is faithfully recreated as the `Logo` React component — no raster
asset existed in the source to copy, so the vector was reproduced from the exact
path data.

---

## INDEX — what's in this system

**Root**
- `styles.css` — the single entry point consumers link (an `@import` manifest).
- `readme.md` — this guide. `SKILL.md` — Agent-Skills-compatible wrapper.

**Tokens** (`tokens/`, all `@import`ed by `styles.css`)
- `colors.css` — brand, severity, slate neutrals, signing-source, logo, aliases.
- `typography.css` — families (sans / rounded / mono), scale, weights, tracking.
- `spacing.css` — spacing scale + component paddings + layout sizes.
- `effects.css` — radii, borders, shadows, the glow recipe, motion easings.

**Components** (React, `window.NnyxDesignSystem_cfde7e.<Name>`)
- `components/brand/` — **Logo** (tile + optional wordmark lockup).
- `components/core/` — **Button**, **Card**, **GlassPanel** + **SectionHeader**,
  **StatCard**, **ProgressRing**.
- `components/security/` — **SeverityBadge**, **SigningBadge**, **StatusPill** +
  **WorkerBadge**, **SidebarItem** + **SidebarSectionLabel**, **DetectionRow**.

**UI kit** (`ui_kits/nnyx-app/`)
- `index.html` — interactive full app: sidebar nav across **Dashboard**, **Scan**
  (live progress sim), **Detections** (master/detail), **Trust Bank**, plus
  placeholder panes. Built from `Dashboard.jsx`, `ScanScreen.jsx`,
  `DetectionsScreen.jsx`, `TrustScreen.jsx`, `AppShell.jsx`, `data.jsx`.

**Foundation cards** (`guidelines/`) — specimen cards for the Design System tab
(Colors, Type, Spacing, Brand groups).

---

## Caveats / substitutions
- **Fonts:** Nnyx's font *is* the macOS system font (SF Pro / SF Pro Rounded /
  SF Mono). No webfont files exist to bundle, so the tokens resolve to the native
  system stack. On non-Apple devices the fallback stack renders a close match.
- **Icons:** SF Symbols → **Lucide** (CDN) substitution, as described above.
- These are flagged for your awareness — supply official SF assets for production
  Apple-platform work.
