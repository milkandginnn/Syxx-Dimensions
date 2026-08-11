# WS9-FIX-PLAN.md — UI/UX Parity Fix Implementation Plan

*Generated 2026-07-24. Detailed solution designs for WS9 Phase 1 findings.*

---

## Phase 2: Solution Design — macOS GUI Critical Fixes

### 1. Dashboard Trust Ring Missing Gradient Overlay

**Finding:** Dashboard trust ring missing gradient overlay compared to mockup.

**Root Cause:** SwiftUI gradient modifier not applied or incorrect gradient parameters.

**Solution Design:**

1. Add `.background(LinearGradient(...))` modifier to trust ring view
2. Match gradient colors from mockup (teal to violet transition)
3. Ensure gradient angles match mockup (likely 45 degrees)
4. Reference mockup at `Syxx macOS GUI Mxups (standalone).html`

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/DashboardView.swift` — add gradient to trust ring

**Validation:**

- Build and compare against mockup → gradient should match
- Test in light/dark mode → gradient should be visible in both

---

### 2. Prophet View Card Padding 8px Off

**Finding:** Prophet view card padding is 8px off from mockup specification.

**Root Cause:** Incorrect `.padding()` value in Prophet card view.

**Solution Design:**

1. Measure mockup padding value
2. Adjust `.padding()` modifier to match mockup exactly
3. Ensure consistent padding across all card views

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/ProphetView.swift` — fix card padding

**Validation:**

- Compare visual output against mockup → padding should match
- Test with different content lengths → padding should remain consistent

---

### 3. Mirror View Comparison Table Cell Alignment Wrong

**Finding:** Mirror view comparison table cell alignment does not match mockup.

**Root Cause:** Incorrect table layout or alignment modifiers.

**Solution Design:**

1. Use `Table` or `Grid` with proper column alignment
2. Set alignment modifiers (`.leading()`, `.center()`, `.trailing()`) per mockup
3. Ensure column widths match mockup proportions

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/MirrorView.swift` — fix table alignment

**Validation:**

- Compare table layout against mockup → alignment should match
- Test with various data → alignment should remain consistent

---

### 4. Portal Traceroute Output Uses Wrong Monospace

**Finding:** Portal traceroute output uses incorrect monospace font.

**Root Cause:** Wrong font family or size for monospace text.

**Solution Design:**

1. Use `.font(.system(.body, design: .monospaced))` or custom monospace font
2. Match font size from mockup (likely 12px or 13px)
3. Ensure line height matches mockup

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/PortalView.swift` — fix monospace font

**Validation:**

- Compare font rendering against mockup → should match
- Test with long traceroute output → should wrap correctly

---

### 5. Glutton Collector Card Missing Status Badge

**Finding:** Glutton collector card missing status badge shown in mockup.

**Root Cause:** Status badge view not added or hidden.

**Solution Design:**

1. Add status badge view to Glutton card
2. Implement badge states (active, inactive, error)
3. Match badge colors from Warden theme tokens

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/GluttonView.swift` — add status badge

**Validation:**

- Verify badge displays in all states
- Compare badge appearance against mockup

---

### 6. Cage Terminal View Missing Timestamp Column

**Finding:** Cage terminal view missing timestamp column from mockup.

**Root Cause:** Table grid missing timestamp column or column hidden.

**Solution Design:**

1. Add timestamp column to terminal table
2. Format timestamps to match mockup (likely ISO 8601 or relative time)
3. Ensure column width matches mockup

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/CageView.swift` — add timestamp column

**Validation:**

- Verify timestamp column displays correctly
- Test with various timestamps → formatting should be consistent

---

### 7. Witness Key Entry Field Wrong Width

**Finding:** Witness key entry field has incorrect width compared to mockup.

**Root Cause:** Incorrect `.frame()` width or missing constraints.

**Solution Design:**

1. Measure mockup field width
2. Set `.frame(width: ...)` to match mockup
3. Ensure field is responsive to window resizing if needed

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/WitnessView.swift` — fix field width

**Validation:**

- Compare field width against mockup → should match
- Test with window resize → field should behave appropriately

---

### 8. Conclusions Verdict Banner Uses Wrong Severity Colors

**Finding:** Conclusions verdict banner uses incorrect severity colors (not matching Warden theme).

**Root Cause:** Wrong color tokens or hardcoded colors instead of theme tokens.

**Solution Design:**

1. Use Warden theme color tokens for severity levels
2. Map verdict levels to correct theme colors (critical red, warning orange, success green)
3. Remove any hardcoded color values

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/ConclusionsView.swift` — use theme color tokens

**Validation:**

- Compare banner colors against mockup → should match Warden theme
- Test all severity levels → colors should be correct

---

### 9. Sidebar Active Indicator Not Matching WardenSidebar.dc.html

**Finding:** Sidebar active indicator uses 10px pipe vs 4px in mockup.

**Root Cause:** Incorrect width for active indicator view.

**Solution Design:**

1. Change active indicator width from 10px to 4px
2. Match indicator color from Warden theme tokens
3. Ensure indicator positioning matches mockup

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/SidebarView.swift` — fix indicator width

**Validation:**

- Compare indicator against mockup → should be 4px
- Test active state transitions → should animate correctly

---

### 10. Dashboard Warden Grid 3-Column vs 2-Column in Mockup

**Finding:** Dashboard warden grid uses 3 columns vs 2 columns in mockup.

**Root Cause:** Incorrect grid column count or adaptive layout logic.

**Solution Design:**

1. Change grid from 3 columns to 2 columns
2. Adjust grid spacing to match mockup
3. Ensure responsive behavior if needed

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/DashboardView.swift` — fix grid columns

**Validation:**

- Compare grid layout against mockup → should be 2 columns
- Test with various content → grid should adapt correctly

---

### 11. License Card Pro Badge Uses Wrong Gold

**Finding:** License card Pro badge uses incorrect gold color.

**Root Cause:** Wrong color token or hardcoded gold value.

**Solution Design:**

1. Use Warden theme gold token for Pro badge
2. Remove hardcoded gold color
3. Ensure badge matches mockup appearance

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/LicenseView.swift` — use theme gold token

**Validation:**

- Compare badge color against mockup → should match
- Test in light/dark mode → color should be appropriate

---

### 12. About Panel Layout Broken

**Finding:** About panel layout is broken (likely due to same DashboardView refactor issue).

**Root Cause:** Layout constraints or view hierarchy broken during refactor.

**Solution Design:**

1. Restore About panel layout from pre-refactor state or rebuild from mockup
2. Ensure all About panel components are present and correctly positioned
3. Reference mockup for correct layout

**Implementation Files:**

- `macOS/Sources/SyxxApp/Views/AboutView.swift` — restore layout

**Validation:**

- Build and compare against mockup → layout should match
- Test all About panel interactions → should work correctly

---

## Phase 2: Solution Design — Windows GUI Fixes

### 13-21. Windows Layout Deviations (9 findings)

**Summary:** Sidebar width, Dashboard collapse, Prophet spacing, Mirror alignment, Portal font, Glutton card missing, Cage table columns, Witness padding, Conclusions scroll clipping.

**Solution Design:**

1. Fix MainWindow sidebar width (220px → 236px)
2. Add min-width guard to Dashboard (900px minimum)
3. Adjust Prophet card input spacing
4. Fix Mirror resolver table alignment
5. Use correct font for Portal route display
6. Add missing Glutton collector card
7. Fix Cage capture table column widths
8. Adjust Witness report card padding
9. Fix Conclusions evidence list scroll clipping

**Implementation Files:**

- `Windows/SyxxUI/Views/MainWindow.xaml` — sidebar width
- `Windows/SyxxUI/Views/DashboardView.xaml` — min-width guard
- `Windows/SyxxUI/Views/ProphetView.xaml` — spacing
- `Windows/SyxxUI/Views/MirrorView.xaml` — alignment
- `Windows/SyxxUI/Views/PortalView.xaml` — font
- `Windows/SyxxUI/Views/GluttonView.xaml` — add card
- `Windows/SyxxUI/Views/CageView.xaml` — table columns
- `Windows/SyxxUI/Views/WitnessView.xaml` — padding
- `Windows/SyxxUI/Views/ConclusionsView.xaml` — scroll clipping

**Validation:**

- Compare each view against `Syxx Windows GUI Mxups.html`
- Test responsive behavior
- Verify all interactions work correctly

---

### 22-27. Windows Typography Mismatches (6 findings)

**Summary:** Segoe UI Variable used where Cinzel should be, EB Garamond italic missing.

**Solution Design:**

1. Replace Segoe UI Variable with Cinzel for headers
2. Add EB Garamond italic for lore blurbs
3. Ensure font weights match mockup specifications

**Implementation Files:**

- Windows XAML resource dictionaries — update font families
- Apply correct fonts to header and lore text elements

**Validation:**

- Compare typography against mockup → should match
- Test font rendering at various sizes

---

### 28-31. Windows Color Token Off (4 findings)

**Solution Design:**

1. Update color tokens to match Warden theme specification
2. Fix any hardcoded color values
3. Ensure color consistency across all views

**Implementation Files:**

- Windows resource dictionaries — update color tokens

**Validation:**

- Compare colors against mockup → should match
- Test in light/dark themes

---

### 32-34. Windows Component Behavior Mismatches (3 findings)

**Summary:** Toggle persistence, scroll inertia issues.

**Solution Design:**

1. Fix toggle state persistence
2. Adjust scroll inertia to match mockup behavior
3. Fix card hover states

**Implementation Files:**

- Windows view code-behind files — fix behavior

**Validation:**

- Test toggle persistence across app restarts
- Test scroll behavior → should match mockup
- Test hover states → should animate correctly

---

## Phase 2: Solution Design — Ubuntu GUI Fixes

### 35-41. Ubuntu Layout Deviations (7 findings)

**Summary:** Avalonia Grid alignment vs mockup.

**Solution Design:**

1. Fix Grid column/row definitions to match mockup
2. Adjust alignment properties for all Grid children
3. Ensure spacing matches mockup

**Implementation Files:**

- `Ubuntu/SyxxUI.Avalonia/Views/*.axaml` — fix Grid layouts

**Validation:**

- Compare against `Syxx Linux GUI Mxups.html`
- Test responsive behavior

---

### 42-46. Ubuntu Typography Mismatches (5 findings)

**Summary:** Cascadia Mono weight inconsistencies.

**Solution Design:**

1. Standardize Cascadia Mono weights across all views
2. Match font weights to mockup specifications

**Implementation Files:**

- Ubuntu AXAML styles — update font weights

**Validation:**

- Compare typography against mockup
- Test font rendering

---

### 47-49. Ubuntu Color Token Mismatches (3 findings)

**Summary:** Surface/neutral, rune tint differences.

**Solution Design:**

1. Update color tokens to match Warden theme
2. Fix surface/neutral colors
3. Fix rune tint colors

**Implementation Files:**

- Ubuntu resource dictionaries — update colors

**Validation:**

- Compare colors against mockup

---

### 50-51. Ubuntu Component Behavior Mismatches (2 findings)

**Summary:** ScrollViewer inertia, card hover states.

**Solution Design:**

1. Adjust ScrollViewer inertia to match mockup
2. Fix card hover state animations

**Implementation Files:**

- Ubuntu view code-behind — fix behavior

**Validation:**

- Test scroll behavior
- Test hover animations

---

## Phase 2: Solution Design — Theme Token Fixes

### 52-62. Warden Theme Token Mismatches (11 findings)

**Summary:** 5 color tokens, 3 corner radii, 3 type scale inconsistencies.

**Solution Design:**

1. Fix color tokens: teal #2dd4bf tint variance, critical red #ff4d6d shade differences, violet #7c5cff opacity
2. Fix corner radii: badge 6px vs 8px, pill 999px vs 997px
3. Fix type scale: 10px vs 10.5px, 12px vs 13px, 14px vs 14.5px

**Implementation Files:**

- All platforms: theme token definition files
- Reference `IMPLEMENTATION_GUIDE.md` for correct values

**Validation:**

- Automated token validation in build process
- Visual comparison against mockups
- Cross-platform consistency check

---

## Phase 3: Implementation Priority Order

### Critical (Immediate - macOS GUI Functionality)

1. Fix macOS DashboardView layout (broken GUI)
2. Restore all macOS GUI components to match mockup

### High Priority (Within 1 week - All Platforms)

1. Windows GUI layout fixes (9 findings)
2. Windows typography fixes (6 findings)
3. Ubuntu GUI layout fixes (7 findings)
4. Theme token fixes (11 findings)

### Medium Priority (Within 2 weeks)

1. Windows color and behavior fixes (7 findings)
2. Ubuntu typography, color, and behavior fixes (10 findings)
3. Cross-platform visual parity verification

---

## Next Steps

1. Begin macOS GUI repair (Critical - broken functionality)
2. Implement Windows GUI fixes
3. Implement Ubuntu GUI fixes
4. Apply theme token fixes across all platforms
5. Comprehensive visual parity verification against HTML mockups

*Plan updated 2026-07-24. Ready for Phase 3 implementation.*
