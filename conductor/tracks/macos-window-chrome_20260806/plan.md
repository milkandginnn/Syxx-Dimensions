# plan.md — macOS Window Chrome: Traffic Lights & Drag Area

## Phase 1: Diagnose and Fix Window Chrome Configuration

- [x] Task: Verify current traffic light visibility in `.ready` state
    - [x] Build and launch the macOS app bundle
    - [x] Complete onboarding to reach `.ready` state
    - [x] Confirm whether traffic lights are present but hidden, or absent
    - [x] Run with `SYXX_GEOM_DEBUG=1` and capture baseline geometry values
    - [x] Record findings as evidence (screenshot + geom.log output)

- [x] Task: Fix `isMovableByWindowBackground` in `FirstRunWindowConfigurator`
    - [x] Change `window.isMovableByWindowBackground = false` (SyxxApp.swift:226) to `true` in the `.ready` case
    - [x] This enables the standard macOS drag-by-content behavior for transparent-titlebar windows
    - [x] Verify the first-run `.default` case still uses `isMovableByWindowBackground = true` (no change needed)

- [x] Task: Fix traffic light visibility
    - [x] In `WindowChromeConfigurator` (ContentView.swift), add explicit configuration to ensure traffic lights are visible:
        - Keep `titlebarAppearsTransparent = true` and `titleVisibility = .hidden`
        - Ensure `window.toolbar = nil` is still set (removes the opaque toolbar band)
        - Add `window.standardWindowButton(.closeButton)?.isHidden = false` and same for `.miniaturizeButton` and `.zoomButton` to force visibility
        - Alternatively, if the traffic lights are present but occluded by the full-bleed content, adjust the content's safe area inset so the traffic light region (top ~28pt, left ~78pt) is not painted over
    - [x] Test both approaches and select the one that preserves the transparent titlebar aesthetic

- [x] Task: Prevent PurpleScrollView content drift
    - [x] Read PurpleScrollView.swift:139 (the automatic content inset adjustment for the titlebar band)
    - [x] Verify that the `automaticallyAdjustsScrollInset` setting accounts for the transparent titlebar
    - [x] If the inset calculation is wrong when traffic lights are visible, adjust the `contentInset` calculation to account for the traffic light region
    - [x] Verify the 35pt top origin and 46pt sidebar padding are preserved

- [ ] Task: Conductor - User Manual Verification 'Diagnose and Fix Window Chrome Configuration' (Protocol in workflow.md)

## Phase 2: Verify and Test

- [ ] Task: Visual verification of the fix
    - [ ] Build the app bundle and launch
    - [ ] Complete onboarding → verify `.ready` state shows traffic lights
    - [ ] Click each traffic light (close, minimize, zoom) and confirm correct behavior
    - [ ] Drag the window by the background/titlebar area and confirm movement
    - [ ] Verify no opaque titlebar band is visible
    - [ ] Verify no content drift on window resize
    - [ ] Run with `SYXX_GEOM_DEBUG=1` and compare geometry values to baseline
    - [ ] Record verification state as `verified-by-real-run` in the track ledger

- [ ] Task: Verify first-run onboarding is unchanged
    - [ ] Reset the app state to trigger onboarding
    - [ ] Verify the borderless window appears without traffic lights
    - [ ] Verify the onboarding cards are correctly sized and centered
    - [ ] Verify the onboarding window is movable (via `isMovableByWindowBackground`)

- [ ] Task: Verify no PurpleScrollView drift
    - [ ] Navigate to each module pane (Prophet, Mirror, Portal, Cage, Witness, Conclusions)
    - [ ] Resize the window to various sizes (minimum 1040×613, larger sizes)
    - [ ] Confirm no content shifts upward into the traffic light area
    - [ ] Confirm the 35pt top origin is preserved in all panes
    - [ ] Confirm the sidebar 46pt top padding is preserved

- [ ] Task: Conductor - User Manual Verification 'Verify and Test' (Protocol in workflow.md)

## Phase 3: Edge Cases and Polish

- [ ] Task: Edge case verification
    - [ ] Test window behavior after sleep/wake (traffic lights still visible)
    - [ ] Test window behavior after display change (external monitor connect/disconnect)
    - [ ] Test window behavior at minimum size (1040×613)
    - [ ] Test window behavior when maximized/fullscreen
    - [ ] Verify the `PaneGeometryProbe` debug output is consistent with the fix

- [ ] Task: Code cleanup
    - [ ] Remove any temporary debug code added during diagnosis
    - [ ] Ensure comments in `FirstRunWindowConfigurator` and `WindowChromeConfigurator` accurately describe the traffic light and drag behavior
    - [ ] Verify `PurpleScrollView` tests (`PurpleScrollViewTests.swift`) still pass
    - [ ] Run `swift build --package-path macOS` and `swift test --package-path macOS`

- [ ] Task: Conductor - User Manual Verification 'Edge Cases and Polish' (Protocol in workflow.md)
