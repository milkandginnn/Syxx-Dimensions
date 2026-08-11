# spec.md — macOS Window Chrome: Traffic Lights & Drag Area

## Overview

The macOS app opens without visible traffic lights (close/minimize/maximize buttons) and without a draggable area to move the window. This is a bug in the window chrome configuration: the app correctly transitions from borderless (first-run) to titled chrome when entering the `.ready` state, but the traffic lights are not visible and the window cannot be moved by dragging.

## Root Cause

The window style mask is set correctly in `FirstRunWindowConfigurator` (SyxxApp.swift:223):
```
window.styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
window.titlebarAppearsTransparent = true
window.titleVisibility = .hidden
```

However:
1. **`isMovableByWindowBackground = false`** is explicitly set at line 226, disabling the standard drag-by-content behavior that transparent-titlebar apps rely on.
2. The traffic lights may be present but invisible/unclickable due to the interaction between `titleVisibility = .hidden`, `titlebarAppearsTransparent = true`, `fullSizeContentView`, and the content painting over the titlebar area with `.ignoresSafeArea()`. The canvas background fills the full window including the titlebar region, which can occlude the traffic lights.
3. `PurpleScrollView`'s automatic content inset adjustment (line 139) references the titlebar band, but when the titlebar is transparent and the content runs full-bleed, the inset calculation may be incorrect, causing content to drift upward — the previously reported "drifting" issue.

## Functional Requirements

1. **Traffic lights must be visible and functional** when the app is in the `.ready` state (post-onboarding). The close (red), minimize (yellow), and zoom/fullscreen (green) buttons must appear at the top-left of the window and respond to clicks.
2. **A drag area must exist** that allows the user to move the window by clicking and dragging. This may be the entire window background (via `isMovableByWindowBackground = true`) or a dedicated drag region at the top of the window.
3. **The current content layout must be preserved.** The 35pt top origin for pane content, the full-bleed canvas background, the sidebar gradient, and the MOTD hero image must all continue to render correctly. The fix must not cause content to shift, drift, or become occluded by the titlebar.
4. **The first-run (borderless) experience must remain unchanged.** The onboarding cards should continue to display without traffic lights or a title bar.
5. **The transparent title bar aesthetic must be maintained.** The app's design relies on content painting edge-to-edge with no visible titlebar band. The fix must not introduce an opaque system-material band.

## Non-Functional Requirements

1. **No PurpleScrollView drift.** The fix must not cause content to shift upward into the traffic light area or drift out of the window bounds — the issue previously observed when the titlebar configuration was changed.
2. **No layout regression.** The 46pt top padding for the sidebar and the 35pt pane origin convention must remain intact.
3. **Cross-platform parity.** This is a macOS-only change. The Windows and Linux clients already have their own window chrome and are unaffected.

## Acceptance Criteria

1. When the app is in `.ready` state, the traffic lights (close/minimize/zoom) are visible at the top-left of the window.
2. Clicking each traffic light performs its expected action (close, minimize, zoom).
3. The user can move the window by clicking and dragging in the titlebar area or the window background.
4. Pane content does not drift upward or shift when the window is resized.
5. The 35pt top origin for pane content and the 46pt sidebar top padding are preserved.
6. The first-run onboarding experience (borderless window) is unchanged.
7. No opaque titlebar band is visible in the `.ready` state.
8. The `SYXX_GEOM_DEBUG=1` debug probe reports correct geometry values after the fix.

## Out of Scope

- Changes to the Windows or Linux window chrome.
- Changes to the first-run onboarding window style.
- Redesign of the app's visual layout or content structure.
- Adding a custom title bar view with custom traffic light buttons (standard macOS buttons are sufficient).
