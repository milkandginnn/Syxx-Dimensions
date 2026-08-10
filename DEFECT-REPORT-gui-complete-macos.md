# Defect Report: macOS GUI vs Mockups

**Track:** `gui-complete-macos_20260805`  
**Date:** 2026-08-05  
**Method:** Fresh line-by-line code audit against HTML mockups + Type Specimen + IMPLEMENTATION_GUIDE.md  
**Scope:** Typography correctness, pixel-perfect mockup fidelity, design-token compliance

---

## CRITICAL (will render incorrectly or not at all)

### D-01 · EBGaramond-Regular not registered — EULA body falls back to system font

**Mockup:** `font-family:'EB Garamond',serif; font-size:14.5px` (Regular weight)  
**Code:** `EULAPanel.swift:42` and `GuidedSetup.swift:92` use `.font(.custom("EBGaramond-Regular", size: 14.5))`  
**Defect:** `bundledFonts` list (IoDesignSystem.swift:284) includes `EBGaramond-Italic` but **not** `EBGaramond-Regular`. The TTF file exists in `Resources/EB_Garamond/` and `Resources/EB Garamond/` but is never registered. SwiftUI silently falls back to the system font.  
**Fix:** Add `.init(resourceName: "EBGaramond-Regular", postScriptName: "EBGaramond-Regular")` to `bundledFonts`.

### D-02 · Sidebar wordmark gradient is 2-color, mockup is 3-color

**Mockup:** `linear-gradient(90deg,#ff9a3d,#ff2d78,#9a5cff)` — gold → hot pink → violet  
**Code:** `LinearGradient.ioWordmark` (IoDesignSystem.swift:186) is `#ff2d78 → #ff3dd5` — hot pink → pink-violet, **missing the gold start**  
**Fix:** Add `Color(red: 255/255, green: 154/255, blue: 61/255)` as the first color stop.

### D-03 · Sidebar eyebrow is "DIMENSIONS" (Cinzel 13pt) — mockup says "The Six Wardens" (system-ui 9.5px)

**Mockup:** `font-size:9.5px; letter-spacing:.22em; color:#8f83b8; font-weight:600; font-family:system-ui` — text is `{{ subtitle }}` where `subtitle = prefix === 'g-' ? 'DNS Investigation' : 'The Six Wardens'`  
**Code:** `ContentView.swift:258` — `Text("DIMENSIONS").font(.custom("Cinzel-SemiBold", size: 13)).foregroundColor(.ioVioletPale)`  
**Defect:** Wrong text ("DIMENSIONS" vs "The Six Wardens"/"DNS Investigation"), wrong font (Cinzel-SemiBold vs system-ui), wrong size (13pt vs 9.5px), wrong weight (SemiBold vs 600), wrong tracking.  
**Fix:** Replace with `Text(model.experienceMode == "general" ? "DNS INVESTIGATION" : "THE SIX WARDENS").font(.system(size: 9.5, weight: .semibold)).tracking(9.5 * 0.22).foregroundColor(Color(red: 143/255, green: 131/255, blue: 184/255))` (or equivalent using the `.ioEyebrow()` modifier).

---

## HIGH (visible design deviation from mockup)

### D-04 · EULA and GuidedSetup lack glass-morphism backdrop blur

**Mockup:** `backdrop-filter:blur(24px)` on the EULA card  
**Code:** `EULAPanel.swift` — `.background(Color(red:28/255,green:17/255,blue:52/255).opacity(0.85))` — no blur at all  
**Spec (IMPLEMENTATION_GUIDE):** "macOS uses .ultraThinMaterial or NSVisualEffectView"  
**Fix:** Add `.background(.ultraThinMaterial)` or wrap in `NSVisualEffectView` behind the colored overlay.

### D-05 · Settings uses system .toggleStyle(.switch) — mockup has custom toggles

**Mockup:** Custom track 44×26, knob 20px, ON = violet + glow  
**Code:** `SettingsView.swift` — all toggles use the default `.toggleStyle(.switch)` (system capsule)  
**Fix:** Implement `IoToggle` (the `IoToggle` struct was removed per E123 comment "defined but never instantiated") and apply `.toggleStyle(IoToggleStyle())` to all Settings toggles.

### D-06 · Settings missing "Conceal queried domains" and "Conceal client identifiers" toggles

**Mockup (§3.9):** "Privacy & Word from Afar" section has these two toggles  
**Code:** `SettingsView.swift` — Privacy section has "Check for updates", "Send crash reports", "Send anonymous usage statistics" instead. The mockup's conceal toggles are entirely absent.  
**Fix:** Add the two conceal toggles. Reorganize the Privacy section to match the mockup's "Privacy & Word from Afar" title.

### D-07 · Conclusions missing confidence ring, execution chips, hypotheses bar

**Mockup (§3.10):** Confidence ring (viewBox 0 0 94 94, ~76px, stroke 8px), execution-status chips per module, hypotheses distribution bar  
**Code:** `ConclusionsView.swift` — has module cards with system checkmark icons, but no confidence ring, no execution chips, no hypotheses bar  
**Fix:** Implement the three missing components from the mockup.

### D-08 · Sidebar hairline under logo is wrong color

**Mockup:** `rgba(148,120,246,.16)` — violet tint  
**Code:** `Color(white: 1, opacity: 0.10).opacity(0.6)` — white tint  
**Fix:** Replace with `Color(red: 148/255, green: 120/255, blue: 246/255).opacity(0.16)`.

### D-09 · EULA footer missing rgba(10,6,20,.35) background

**Mockup:** `background:rgba(10,6,20,.35)` on the footer bar  
**Code:** `EULAPanel.swift` — footer is a plain `HStack` with no background  
**Fix:** Add `.background(Color(red: 10/255, green: 6/255, blue: 20/255).opacity(0.35))` to the footer.

---

## MEDIUM (noticeable deviation, lesser impact)

### D-10 · Settings card radius 12 vs mockup 14

**Mockup:** `border-radius:14px`  
**Code:** `cornerRadius(12)` in `SettingsView.swift:card()`  
**Fix:** Change to `cornerRadius(14)`.

### D-11 · GuidedSetup "Skip: use defaults" uses colon instead of em-dash

**Mockup:** "Skip — use defaults" (em-dash)  
**Code:** `GuidedSetup.swift` — "Skip: use defaults" (colon)  
**Fix:** Replace colon with em-dash (`\u{2014}`).

### D-12 · GuidedSetup uses system .toggleStyle(.switch)

**Code:** `GuidedSetup.swift` — same issue as D-05 but on the Guided Setup screen  
**Fix:** Same IoToggleStyle as D-05.

### D-13 · Portal result container tone-down differs from spec

**Spec (IMPLEMENTATION_GUIDE):** "result containers use opacity(0.08)→opacity(0.02) fill + opacity(0.18) border"  
**Code:** `PortalView.swift` — `Color.ioSuccessGreen.opacity(0.05)` fill and `opacity(0.15)` border  
**Fix:** Use `opacity(0.08)`→`opacity(0.02)` fill ramp and `opacity(0.18)` border per spec.

### D-14 · Dashboard "Flush DNS Cache" is teal-bordered card — mockup has ghost/outline button

**Mockup:** Ghost/outline button style (transparent bg, colored border, no card elevation)  
**Code:** `DashboardView.swift` — teal-bordered card with `Color.ioSurfaceDark` background  
**Fix:** Restyle as a ghost/outline button matching the mockup's button pattern.

### D-15 · Dashboard warden tile colors don't use design-system tokens

**Mockup:** Each module has its own accent (Prophet=#00b4ff, Mirror=#ffd400, Portal=#2dd4bf, Glutton=#ff6eb4, Cage=#00e5ff, Witness=#ff3355)  
**Code:** `DashboardView.swift` — some tiles use inline `Color(red:green:blue:)` instead of `Color.ioProphetBlue`, etc.  
**Fix:** Replace inline colors with the design-system tokens from IoDesignSystem.

### D-16 · Sidebar eyebrow font size and tracking mismatch

**Mockup:** `9.5px; letter-spacing:.22em` = 2.09px tracking  
**Code:** `Cinzel-SemiBold` at 13pt with no explicit tracking  
**Note:** Even if D-03 is fixed (text content), the font metrics must also match.  
**Fix:** Covered by D-03 fix.

### D-17 · EULA body dividers use wrong color

**Mockup:** `border-bottom:1px solid rgba(148,120,246,.14)` — violet tint  
**Code:** `Divider().overlay(Color(white: 1, opacity: 0.06))` — white tint  
**Fix:** Use `Color(red: 148/255, green: 120/255, blue: 246/255).opacity(0.14)`.

### D-18 · Settings section title typography wrong

**Mockup:** `font:700 10px system-ui; letter-spacing:.2em; color:#8f83b8; text-transform:uppercase`  
**Code:** `.font(.caption).bold()` — system caption, no tracking, no violet tint  
**Fix:** Use `.font(.system(size: 10, weight: .bold)).tracking(2).foregroundColor(Color(red: 143/255, green: 131/255, blue: 184/255)).textCase(.uppercase)`.

---

## LOW (minor deviation, cosmetic)

### D-19 · Sidebar "Start your story anew" pill uses `Color.ioSurfaceDark` background

**Mockup:** No explicit background fill on the pill (it's transparent with just the border)  
**Code:** `.background(Color.ioSurfaceDark)`  
**Fix:** Remove the background fill or use `Color.clear`.

### D-20 · Sidebar nav item inactive weight is 500 (Jost Medium), mockup is 500

**Mockup:** `font-weight:500` for inactive items  
**Code:** `SidebarItem` uses `.font(.navLabel(15))` which maps to Spectral-Light (300 weight)  
**Fix:** The navLabel role should use Jost-Medium (500) for inactive items per the mockup. The mockup's `font-weight:500` + `font-size:14px` is closer to Jost 500 than Spectral 300.

### D-21 · Sidebar "What's New" uses ✦ glyph instead of spark SVG

**Mockup:** SVG spark icon (12-point star + small circle)  
**Code:** `SidebarItem(label: "What's New", rune: "✦", ...)` — plain Unicode glyph  
**Fix:** Replace with the SVG spark icon from the mockup, or accept the glyph as a simplification.

### D-22 · Sidebar "Dashboard" uses ◧ glyph instead of dash SVG

**Mockup:** SVG bar-chart icon (3 vertical bars)  
**Code:** `SidebarItem(label: "Dashboard", rune: "◧", ...)` — plain Unicode glyph  
**Fix:** Replace with the SVG bar-chart icon, or accept the glyph as a simplification.

### D-23 · Sidebar "Conclusion" uses ✒ glyph instead of document-check SVG

**Mockup:** SVG document-check icon  
**Code:** `SidebarItem(label: "Conclusion", rune: "✒", ...)` — plain Unicode glyph  
**Fix:** Replace with the SVG document-check icon, or accept the glyph as a simplification.

### D-24 · Sidebar "Dimensions" uses ⊕ glyph instead of globe SVG

**Mockup:** SVG globe icon  
**Code:** `SidebarItem(label: "Dimensions", rune: "⊕", ...)` — plain Unicode glyph  
**Fix:** Replace with the SVG globe icon, or accept the glyph as a simplification.

---

## Summary

| Severity | Count | IDs |
|----------|-------|-----|
| CRITICAL | 3 | D-01, D-02, D-03 |
| HIGH | 6 | D-04, D-05, D-06, D-07, D-08, D-09 |
| MEDIUM | 9 | D-10–D-18 |
| LOW | 6 | D-19–D-24 |
| **Total** | **24** | |

**Top 3 by impact:**
1. **D-01** — EBGaramond-Regular not registered; EULA body text is not EB Garamond at all
2. **D-02** — Wordmark gradient missing gold; the brand's signature gradient is wrong
3. **D-03** — Sidebar eyebrow wrong text, wrong font, wrong size; the nav's identity label is wrong
