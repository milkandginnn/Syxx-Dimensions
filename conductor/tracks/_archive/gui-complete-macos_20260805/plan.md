# Implementation Plan: Complete macOS GUI — Mockup-Faithful SwiftUI

**Track ID:** gui-complete-macos_20260805
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-05
**Updated:** 2026-08-05 (v2 — incorporates mockup references)
**Status:** [ ] Not Started

## Overview

Six phases ordered by dependency. Foundation first (highlight tone-down + typography audit), then first-run screens (EULA + Guided Setup), then the non-Warden screens (Dashboard + Conclusions + Settings + Sidebar), then the six Warden view parity pass, then output catalog + polish. Prophet is the reference implementation throughout.

---

## Phase 1: Foundation — Highlight Tone-Down + Typography Audit

Reduce the highlight color intensity on all Warden result display containers. Audit every font call against the Type Specimen minimum sizes.

### Tasks

- [x] Task 1.1: **Tone down Prophet result containers** — in `ProphetView.swift`, reduce `pres.color.opacity(0.14)→opacity(0.03)` gradient to `opacity(0.08)→opacity(0.02)`, reduce border from `opacity(0.28)` to `opacity(0.18)`. Apply to both `prophetResultCard` and `prophetDnssecCard`.
- [x] Task 1.2: **Propagate tone-down to all Wardens** — apply the same opacity reduction to result containers in MirrorView, PortalView, GluttonView, CageView, WitnessView. Each uses its own module highlight color at the same opacity ramp.
- [x] Task 1.3: **Typography audit** — grep all `.font(.custom(` calls in `macOS/Sources/SyxxApp/`. Cross-reference against Type Specimen roles and minimum sizes. Flag any Cinzel below 18pt, any Cormorant Garamond body text not using the `.body()` helper (which applies `cormorantOpticalScale`), any face not in the specimen.
- [x] Task 1.4: **Fix flagged typography issues** — replace raw `.font(.custom("CormorantGaramond-Regular", size: X))` with `.font(.body(X))` where appropriate. Replace any non-specimen face with the correct role. Bump any face below its minimum size.

### Verification

- [ ] All six Warden result containers use `opacity(0.08)→opacity(0.02)` fill + `opacity(0.18)` border
- [ ] Zero `.font(.custom(` calls use a face below its Type Specimen minimum size
- [ ] `swift build` zero errors

---

## Phase 2: First-Run Screens — EULA Gate + Guided Setup

Match the EULA and Guided Setup modals to the mockup pixel-for-pixel.

### Tasks

- [x] Task 2.1: **EULA glass card** — apply `.background(.ultraThinMaterial)` or `NSVisualEffectView` wrapper to `EULAPanel.swift`. Ensure: 600px width, `rgba(28,17,52,.85)` tint, violet border `rgba(160,140,255,.24)`, radius 18, shadow `0 40px 100px rgba(0,0,0,.6)`. (Already partially done in current edit — verify and refine.)
- [x] Task 2.2: **EULA typography** — agreement body text in EB Garamond Regular 14.5px (not Cormorant Garamond). Section numbers bold (`**1. License Grant.**` rendered as bold inline). "MilkandGin" owner name bold.
- [x] Task 2.3: **EULA footer** — "Decline" as plain text link (not a button), "Agree & Continue →" as `ioPrimaryButton` gradient. Remove the checkbox toggle from `AgreementGate` — mockup has no checkbox, just the two footer actions.
- [x] Task 2.4: **Guided Setup width** — reduce `SetupChoices` from 960px to 640px per mockup. Adjust internal padding to fit.
- [x] Task 2.5: **Guided Setup toggles** — remove 3 extra toggles (update check, crash reports, telemetry) that aren't in the mockup. Keep only: Resolver picker, Transport picker, "Encrypt all DNS queries" toggle, "Notify me on trust changes" toggle.
- [x] Task 2.6: **Experience cards styling** — Story card selected state uses violet border (2px) + violet check icon. General card selected state uses teal border + teal check. Unselected: subtle border, no icon.
- [x] Task 2.7: **Guided Setup backdrop** — verify radial violet glow matches mockup: `radial-gradient(80% 60% at 50% 0%, rgba(124,92,255,.18), rgba(11,5,24,.9))` over near-opaque canvas.

### Verification

- [ ] EULA modal: 600px, glass background, EB Garamond body, Cinzel title, "Decline" + "Agree & Continue →" footer
- [ ] Guided Setup: 640px, 2 experience cards with correct selection colors, exactly 4 toggles
- [ ] Both modals render correctly in dark and light mode
- [ ] `swift build` zero errors

---

## Phase 3: Non-Warden Screens — Dashboard, Conclusions, Settings, Sidebar

Match the structural screens to the mockup layout and component recipes.

### Tasks

- [x] Task 3.1: **Dashboard — trust ring** — implement SVG-style trust ring per mockup §2.4: `viewBox 0 0 118 118`, rendered ~70px, stroke 10-13px, track `rgba(148,120,246,.15)`, fill = teal→violet gradient, `stroke-linecap:round`, rotated -90°, glow drop-shadow. Use `Canvas` or `Circle().trim()` with `AngularGradient`.
- [x] Task 3.2: **Dashboard — status banner** — trust ring + real resolver address + honest health line in a single card. Match mockup layout: ring left, text right, `SyxSurfaceFillBrush` background.
- [x] Task 3.3: **Dashboard — action pair** — "Conduct All Scans" (primary gradient) + "Flush DNS Cache" (ghost/outline). Below: "Enable Unbound DNS" (active) + "Enable Encrypted DNS" (disabled when already encrypted per §2.2a).
- [x] Task 3.4: **Dashboard — warden tiles** — 6 tiles in 2-column grid, each with rune glyph, module name, status pill. Radius 11, padding 7-10px.
- [x] Task 3.5: **Dashboard — Council's Verdict + Chronicle + Pro Covenant** — Council card with verdict badge, Chronicle with 3 recent rows, Pro Covenant banner with gradient fill.
- [x] Task 3.6: **Conclusions — verdict banner + confidence ring** — match mockup: `viewBox 0 0 94 94`, rendered ~76px, stroke 8px. Verdict badge below.
- [x] Task 3.7: **Conclusions — hard-gate warning + execution chips + hypotheses bar** — match mockup layout for the evidence sections.
- [x] Task 3.8: **Settings — grouped cards** — match mockup §3.6: cards for Encrypted DNS (resolver/transport selects + toggle), Scans, Privacy. Custom toggles (track 44×26, knob 20px, ON = violet + glow).
- [x] Task 3.9: **Settings — header** — "THE SANCTUM" eyebrow (10.5px, .26em, uppercase, violet) + "Settings" Cinzel title in Story mode. "PREFERENCES" + "Settings" in General mode.
- [x] Task 3.10: **Sidebar — nav order + styling** — match `WardenSidebar.dc.html`: 236px, logo + "Syxx" wordmark gradient + "THE SIX WARDENS" eyebrow, gold hairline dividers before Prophet/Conclusions/Dimensions, nav order What's New → Dashboard → / → Prophet…Witness → / → Conclusions → / → Dimensions. Active item: violet gradient fill, white text, shadow. Inactive: `#b3a7d6`. PRO lock badges on Mirror/Glutton/Witness. "Start your story anew" gold-bordered pill at bottom.
- [x] Task 3.11: **Sidebar — nav icons** — What's New = sparkles SVG, Dashboard = bar chart SVG, wardens = Elder Futhark runes (text), Conclusions = report SVG, Dimensions = globe SVG + trailing ↗. All 16px, `stroke:currentColor`.

### Verification

- [ ] Dashboard matches mockup: trust ring, status banner, action pair, 6 tiles, Council card, Pro Covenant
- [ ] Conclusions matches mockup: verdict banner, confidence ring, evidence sections
- [ ] Settings matches mockup: grouped cards with custom toggles
- [ ] Sidebar matches `WardenSidebar.dc.html`: correct nav order, glyphs, active state, PRO badges, gold dividers
- [ ] `swift build` zero errors

---

## Phase 4: Warden View Parity Pass

Systematic pass over all six Warden views to ensure each matches the Prophet gold standard.

### Tasks

- [x] Task 4.1: **Mirror** — rune header matches Prophet pattern (54px tile, `ioMirrorYellow` highlight, eyebrow + Cinzel name + lore). Result containers use toned-down opacity from Phase 1. Domain picker container uses yellow module highlight per existing owner directive.
- [x] Task 4.2: **Portal** — rune header matches Prophet pattern (`ioSuccessGreen` highlight). Apply highlight color more broadly to section headers and status indicators (per owner note: "needs to be more broadly applied").
- [x] Task 4.3: **Glutton** — rune header matches Prophet pattern (`ioGluttonPink` highlight). Result containers toned down.
- [x] Task 4.4: **Cage** — rune header matches Prophet pattern (`ioCageCyan` highlight). No feed UI (rejected). Status-only display.
- [x] Task 4.5: **Witness** — rune header matches Prophet pattern (`ioWitnessRed` highlight). Signing key input + verification report match mockup layout.
- [x] Task 4.6: **Prophet refinement** — address remaining spacing/font issues noted by owner. Verify DNSSEC card fits in pane after Phase 1 tone-down.

### Verification

- [ ] All six Warden views have consistent rune header pattern (54px tile, module highlight, eyebrow + Cinzel + lore)
- [ ] All result display containers use toned-down opacity
- [ ] Portal highlight applied broadly (not just header)
- [ ] `swift build` zero errors

---

## Phase 5: Output Catalogs + Polish

Update the output-catalog CSVs for every new or changed user-visible string. Final polish pass.

### Tasks

- [x] Task 5.1: **Audit all new `Text("...")` strings** — grep all views modified in this track for user-visible strings not yet in the output catalogs.
- [x] Task 5.2: **Update `outputs-catalog-story-mode.csv`** — add rows for every new Story-mode string (lore text, warden titles, button labels, status pills). Format: `file:line,trigger,module,task,output,change`.
- [x] Task 5.3: **Update `outputs-catalog-general-mode.csv`** — add rows for every General-mode equivalent (technical labels, plain English descriptions).
- [x] Task 5.4: **Dark/light mode audit** — toggle `colorScheme`, verify every view renders correctly in both themes. Fix any hardcoded colors that don't resolve per-appearance.
- [x] Task 5.5: **Accessibility pass** — `.accessibilityLabel()` on every interactive control, `.accessibilityHint()` on every button, keyboard navigation through every view, Dynamic Type support via `@ScaledMetric` where needed.
- [x] Task 5.6: **Otel spans on view lifecycle** — `syxx.macos.gui.{viewName}.appear` / `.disappear` spans on `.onAppear` / `.onDisappear` for every view modified in this track.

### Verification

- [ ] Both output-catalog CSVs updated with all new strings
- [ ] Dark/light mode: no broken colors in either theme
- [ ] Keyboard: all views navigable without mouse
- [ ] `swift build` zero errors
- [ ] `bash bundle-app.sh` produces working `Syxx.app`

---

## Final Verification

- [ ] All acceptance criteria from spec.md met
- [ ] Side-by-side comparison: macOS app vs mockup HTML — no significant visual divergence
- [ ] Prophet gold standard preserved (highlight tone-down applied, DNSSEC fits)
- [ ] Output catalogs complete for both Story and General modes
- [ ] No "verdict" language in code or UI
- [ ] Ready for review

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._
