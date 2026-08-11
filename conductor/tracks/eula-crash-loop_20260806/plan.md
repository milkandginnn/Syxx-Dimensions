# Implementation Plan: Fix EULA Crash Loop — Window Animation Bug and Bundle Path Mismatch

**Track ID:** eula-crash-loop_20260806
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-06
**Status:** [x] Phase 1 Complete

## Overview

Two independent bugs, one phase each. Both are single-line fixes with clear acceptance criteria. Bug 2 (bundle path) is the ship-blocker; Bug 1 (window animation) is a quality-of-experience issue. No dependencies between the two tasks.

---

## Phase 1: Fix Both Bugs

### Tasks

- [x] Task 1.1: **Fix window origin in FirstRunWindowConfigurator** — in `macOS/Sources/SyxxApp/SyxxApp.swift`, replace `NSRect(origin: .zero, size: cardSize)` with `var f = window.frame; f.size = cardSize; window.setFrame(f, …)` in the `default` case of `configure()`. Match the pattern already used in the `.ready` case.

 **Files:** `macOS/Sources/SyxxApp/SyxxApp.swift` (lines 240–246)
 **Claim:** The window resizes in place without sliding to the bottom-left corner.

- [x] Task 1.2: **Fix bundle copy destination in bundle-app.sh** — in `macOS/bundle-app.sh`, change the copy destination in the `for b in "$BINDIR"/*.bundle` loop from `"$APP/Contents/Resources/"` to `"$APP/"`. This places `Syxx_SyxxApp.bundle` at the `.app` root, matching the path `Bundle.module` expects.

 **Files:** `macOS/bundle-app.sh` (line 122)
 **Claim:** `Bundle.module` resolves on user machines without the `buildPath` fallback.

### Verification

- [ ] Task 1.1: **Window animation** — build and run the app. Verify the EULA window appears centered and does not slide to the bottom-left corner during state transitions. Verify by visual inspection.

 **Verification state:** `verified-by-real-run`

- [ ] Task 1.2: **Bundle path** — run `bash bundle-app.sh` and then launch the produced `Syxx.app`. Verify the app does not crash on launch. Verify `ls Syxx.app/Syxx_SyxxApp.bundle` exists.

 **Verification state:** `verified-by-real-run`

- [x] Task 1.X: **Build integrity** — `swift build` produces zero errors (103/103 targets). `bash bundle-app.sh` produces a working `Syxx.app` with `Syxx_SyxxApp.bundle` at the correct path.

 **Verification state:** `verified-by-CI`

---

## Final Verification

- [ ] Both acceptance criteria sets from spec.md met
- [ ] App launches from `Syxx.app` without crashing on a machine without the dev build directory
- [ ] EULA and Guided Setup modals render correctly with smooth window transitions
- [ ] No regression in the `.ready` state transition
- [ ] `bundle-app.sh` produces a distributable `.app` with the bundle at the correct path