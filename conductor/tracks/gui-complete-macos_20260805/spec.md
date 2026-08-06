# Specification: Complete macOS GUI — Mockup-Faithful SwiftUI Implementation

**Track ID:** gui-complete-macos_20260805
**Type:** Feature
**Created:** 2026-08-05
**Updated:** 2026-08-05
**Status:** Draft (v2 — incorporates mockup references)

## Summary

Bring the macOS SwiftUI GUI into pixel-faithful alignment with the HTML mockups across all screens: EULA/License gate, Guided Setup, Dashboard, the six Warden tool views, Conclusions, Settings, and the sidebar. Prophet is the gold standard — other modules match its quality. Highlight color application on result display containers must be toned down. Typography must follow the Type Specimen; fonts that are difficult to read or absent from the guide must be corrected.

## Reference Documents

| Document | Location | Purpose |
|---|---|---|
| IMPLEMENTATION_GUIDE.md | `Syxx GUI Mockups/IMPLEMENTATION_GUIDE.md` | Authoritative design tokens, layout sizes, component recipes |
| macOS GUI Mxups.html | `Syxx GUI Mockups/Syxx macOS GUI Mxups.html` | Pixel-perfect macOS mockups (Story + General) |
| Windows GUI Mxups.html | `Syxx GUI Mockups/Syxx Windows GUI Mxups.html` | WinUI 3 reference for parity |
| Linux GUI Mxups.html | `Syxx GUI Mockups/Syxx Linux GUI Mxups.html` | Avalonia reference for parity |
| WardenSidebar.dc.html | `Syxx GUI Mockups/WardenSidebar.dc.html` | Sidebar component spec (nav order, glyphs, active state, dividers) |
| Syxx Type Specimen.html | `~/Downloads/Syxx Type Specimen.html` | Typography reference — all roles, sizes, weights |
| IoDesignSystem.swift | `macOS/Sources/SyxxApp/Views/Components/IoDesignSystem.swift` | Current SwiftUI design tokens |

## Context

Syxx ships three desktop GUIs from two shared-logic twins. Windows (14 views) and Linux (13 views) both have view sets that closely follow the mockups. macOS has the design system infrastructure (`IoDesignSystem.swift`, `PurpleScrollView`, font registration) and 15+ view files, but several screens diverge from the mockups in layout, glass morphism, typography, and component styling.

**Gold standard:** Prophet — its rune header, resolver consensus card, DNSSEC card, typography, and module highlight color (#00b4ff) are the reference implementation for all other modules. **Exception:** the highlight color on Prophet's result display containers (gradient fill + border) is too strong and needs to be toned down.

## Acceptance Criteria

- [ ] **EULA/License gate** matches mockup: 600px glass card, `rgba(28,17,52,.85)` + backdrop blur, violet border `rgba(160,140,255,.24)`, EB Garamond 14.5px body, Cinzel title, "Decline" text + "Agree & Continue →" gradient button
- [ ] **Guided Setup** matches mockup: 640px modal (not 960px), 2 experience cards (Story violet / General teal), 4 toggles only (Resolver, Transport, Encrypt DNS, Notify), "Skip — use defaults" + "Begin →" footer
- [ ] **Dashboard** matches mockup: trust ring SVG (teal→violet gradient), status banner, action pair (Conduct All Scans / Flush DNS Cache), 6 warden tiles (2-col grid), Council's Verdict card, Pro Covenant banner
- [ ] **Six Warden views** (Prophet/Mirror/Portal/Glutton/Cage/Witness) each have: correct module highlight color, rune header matching Prophet's pattern, result display containers with **toned-down** highlight (lower opacity gradients, subtler borders)
- [ ] **Conclusions** matches mockup: verdict banner + confidence ring, hard-gate warning, execution-status chips, hypotheses distribution bar, recommended steps
- [ ] **Settings** matches mockup: grouped cards (Encrypted DNS, Scans, Privacy) with custom toggles, Cinzel section headings, "THE SANCTUM" eyebrow in Story mode
- [ ] **Sidebar** matches `WardenSidebar.dc.html`: 236px, logo + wordmark gradient, nav order (What's New → Dashboard → / → Prophet…Witness → / → Conclusions → / → Dimensions), gold dividers, active violet gradient, PRO lock badges, "Start your story anew" pill
- [ ] **Typography** matches Type Specimen: all roles resolve to correct PostScript names and sizes; no face used outside the specimen; Cormorant Garamond body at optical-matched sizes
- [ ] **Output catalogs** updated: every new or changed user-visible string added to `outputs-catalog-story-mode.csv` and `outputs-catalog-general-mode.csv`
- [ ] **Font readability** audit: any face that is difficult to read at its assigned size is bumped or swapped per the specimen's minimum-size rules (Cinzel ≥ 18pt, etc.)
- [ ] `swift build` zero errors under Swift 6 strict concurrency
- [ ] `bash bundle-app.sh` produces a working `Syxx.app`

## Dependencies

- `SyxxKit` (Swift shared-logic twin) — data models, investigation engine
- `IoDesignSystem.swift` — color tokens, font registration, typography helpers
- `PurpleScrollView.swift` — custom scroll container with geometry modes
- Mockup HTML files (read-only reference)

## Out of Scope

- Server-side changes (Python `syxx_server/`)
- Windows or Linux view modifications (this track is macOS only)
- New investigation features or engine logic
- App Store submission or notarization
- Qt target
- Cage feed UI (rejected by owner — "Listening for DNS on port 53…" placeholder not needed)
- Dimensions view (external link, not a local view)

## Technical Notes

- **Prophet highlight tone-down:** Current result containers use `pres.color.opacity(0.14)→opacity(0.03)` gradient fill + `pres.color.opacity(0.28)` border. Reduce to `opacity(0.08)→opacity(0.02)` fill + `opacity(0.18)` border. Apply same reduction to all six Warden result containers for consistency.
- **Glass morphism:** macOS doesn't have CSS `backdrop-filter:blur()`. Use `.background(.ultraThinMaterial)` or `NSVisualEffectView` via `NSViewRepresentable` for the EULA/Setup modals.
- **Font readability:** The Type Specimen defines minimum sizes per role. Audit every `.font(.custom(...))` call against these minimums. Cormorant Garamond at body sizes uses the `cormorantOpticalScale` factor (1.107×) — verify all body text uses `.body()` helper, not raw `.custom()`.
- **Output catalogs:** CSV format is `file:line,trigger,module,task,output,change`. Every new `Text("...")` or `.foregroundStyle` that changes user-visible copy must get a row in both story and general catalogs.

---

_Generated by Conductor. Review and edit as needed._
