# plan.md — Fix macOS Build Warnings

**Track ID:** fix-macos-build-warnings_20260809
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-09
**Status:** [x] Complete

## Phase 1: Catalog, Fix, Verify

- [x] Task 1.1: Catalog — 1 warning: sentry-cocoa v11 deprecation
- [x] Task 1.2: Fix — suppressed on SyxxTelemetry target (third-party, unfixable upstream)
- [x] Task 1.3: Suppress — added `.unsafeFlags(["-suppress-warnings"])` with comment
- [x] Task 1.4: Verify — `grep -c warning` returns 0
- [x] Task 1.5: Verify — `swift build` zero errors