# Implementation Plan: Complete macOS GUI — SwiftUI View Parity

**Track ID:** gui-complete-macos_20260805
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-05
**Status:** [ ] Not Started

## Overview

Build 10 missing SwiftUI tool views in four phases, ordered by dependency: foundation first (ViewModels + navigation wiring), then the primary views users see daily, then the secondary views, then polish and accessibility. Each view is ported from the WinUI 3 XAML equivalent, translated to SwiftUI idioms using the `IoDesignSystem` tokens.

---

## Phase 1: ViewModels + Navigation Wiring

Create the ViewModel layer that all views will bind to, and wire the `NavigationSplitView` sidebar to route between them.

### Tasks

- [ ] Task 1.1: Create `ViewModels/` directory and `DashboardViewModel.swift` — binds to `SyxxKit` investigation/trust data, exposes `trustScore: String`, `trustColor: Color`, `resolverAddress: String`, `healthLine: String`
- [ ] Task 1.2: Create `ProphetViewModel.swift` — binds to resolver comparison data, exposes section inputs and evaluation results
- [ ] Task 1.3: Create `GluttonViewModel.swift` — binds to content diff data, exposes script/style hash comparisons
- [ ] Task 1.4: Create `MirrorViewModel.swift` — binds to evidence matrix, exposes section results
- [ ] Task 1.5: Create `PortalViewModel.swift` — binds to portal reachability data
- [ ] Task 1.6: Create `WitnessViewModel.swift` — binds to witness/signed-receipt data
- [ ] Task 1.7: Create `CageViewModel.swift` — binds to Cage capture session, exposes status (idle/listening/captured)
- [ ] Task 1.8: Create `ConclusionsViewModel.swift` — binds to synthesis engine, exposes overall result and evidence list
- [ ] Task 1.9: Create `FirstRunViewModel.swift` — manages onboarding state and license entry
- [ ] Task 1.10: Create `LicenseViewModel.swift` — binds to license store, exposes activation status
- [ ] Task 1.11: Wire `ContentView` sidebar — add `NavigationLink` for each tool view in the `NavigationSplitView` sidebar, using the same order and labels as WinUI `NavigationView`

### Verification

- [ ] `swift build` succeeds with zero errors
- [ ] `NavigationSplitView` sidebar shows all 10 items
- [ ] Clicking each sidebar item navigates to a placeholder view (empty `Text("DashboardView")` etc.)

---

## Phase 2: Primary Views (Dashboard, Prophet, Glutton, Conclusions)

Build the four views users see most — these present investigation results.

### Tasks

- [ ] Task 2.1: Implement `DashboardView.swift` — trust ring (dash at rest), real resolver address, honest health line, four action buttons. Port from WinUI `DashboardView.xaml`. Use `GeometryReader` + `ScrollView` for scroll anchoring per macOS convention
- [ ] Task 2.2: Implement `ProphetView.swift` — resolver comparison sections (A, B, C, D, E), per-section result cards with turquoise/purple/gold accents. Port from WinUI `ProphetView.xaml`
- [ ] Task 2.3: Implement `GluttonView.swift` — content diff sections (scripts, stylesheets, inline), hash comparison display, tamper indicators. Port from WinUI `GluttonView.xaml`
- [ ] Task 2.4: Implement `ConclusionsView.swift` — overall result badge, evidence list with expand/collapse, synthesis explanation. Port from WinUI `ConclusionsView.xaml`. Use `OverallResultView` component

### Verification

- [ ] Each view renders with the correct layout hierarchy when navigated from sidebar
- [ ] Design tokens (colors, fonts, spacing) match `IoDesignSystem`
- [ ] Views display "—" or "Not run" when no investigation has been performed (honesty)
- [ ] `swift build && bash bundle-app.sh && open Syxx.app` — all four views navigable

---

## Phase 3: Secondary Views (Mirror, Portal, Witness, Cage, FirstRun, License)

Build the remaining six views.

### Tasks

- [ ] Task 3.1: Implement `MirrorView.swift` — evidence matrix grid, per-section drill-down. Port from WinUI `MirrorView.xaml`
- [ ] Task 3.2: Implement `PortalView.swift` — reachability test results, domain list, per-domain status. Port from WinUI `PortalView.xaml`
- [ ] Task 3.3: Implement `WitnessView.swift` — signed receipt display, verification status, chain integrity. Port from WinUI `WitnessView.xaml`
- [ ] Task 3.4: Implement `CageView.swift` — capture session status (idle/listening/captured), start/stop controls. **No feed UI** (Cage feed rows rejected). Port status display from WinUI `CageView.xaml` only
- [ ] Task 3.5: Implement `FirstRunView.swift` — onboarding flow, license key entry, server URL configuration. Port from WinUI `FirstRunView.xaml`
- [ ] Task 3.6: Implement `LicenseView.swift` — activation status, subscription info, manage/deactivate actions. Port from WinUI `LicenseView.xaml`

### Verification

- [ ] All 10 views navigable from sidebar
- [ ] `swift build` succeeds with zero errors under Swift 6 strict concurrency
- [ ] CageView shows status only (no feed rows)
- [ ] FirstRunView appears on first launch (no license stored)

---

## Phase 4: Polish — Accessibility, Theming, Parity Audit

Final quality pass across all views.

### Tasks

- [ ] Task 4.1: Add `.accessibilityLabel()` and `.accessibilityHint()` to every interactive control in all 10 views
- [ ] Task 4.2: Verify Dynamic Type support — all text uses `.font()` with semantic sizes or scales with `@ScaledMetric`
- [ ] Task 4.3: Verify dark/light mode — toggle `colorScheme`, confirm all views render correctly with both themes
- [ ] Task 4.4: Keyboard navigation audit — tab through each view, ensure focus order is logical, all actions reachable without mouse
- [ ] Task 4.5: Cross-platform parity audit — open WinUI and macOS side by side, screenshot each view, document any remaining visual differences
- [ ] Task 4.6: Add OTel spans to view lifecycle — `syxx.macos.gui.{viewName}.appear` and `.disappear` spans on each view's `.onAppear`/`.onDisappear`
- [ ] Task 4.7: Add Sentry breadcrumbs to error paths — capture breadcrumbs on ViewModel errors and navigation failures

### Verification

- [ ] Accessibility: VoiceOver reads all controls correctly
- [ ] Dark/light mode: no broken colors in either theme
- [ ] Keyboard: all views navigable without mouse
- [ ] `swift build` succeeds, `bundle-app.sh` produces working app
- [ ] Parity audit documented in findings

---

## Final Verification

- [ ] All 10 acceptance criteria from spec.md met
- [ ] `swift build` zero errors, zero warnings under Swift 6
- [ ] `bash bundle-app.sh && open Syxx.app` — all views navigable, themed, accessible
- [ ] No "verdict" language anywhere in code or UI
- [ ] Observability: OTel spans on all view lifecycles, Sentry on all error paths
- [ ] Ready for review

---

_Generated by Conductor. Tasks will be marked [~] in progress and [x] complete._
