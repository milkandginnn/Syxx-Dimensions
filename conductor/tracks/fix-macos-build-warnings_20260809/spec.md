# spec.md — Fix macOS Build Warnings

**Track ID:** fix-macos-build-warnings_20260809
**Type:** chore
**Status:** new

## Overview

Eliminate all warnings from the macOS Swift build. The build currently produces warnings from both Syxx-owned source files and third-party dependencies. A zero-warning build is a quality gate — warnings hide real issues and erode the signal-to-noise ratio.

## Scope

- All warnings in `swift build --package-path macOS` output
- Syxx-owned sources (PurpleScrollView, etc.)
- Third-party warnings (Sentry, SwiftProtobuf) — suppress if unfixable upstream

## Acceptance Criteria

- [ ] `swift build --package-path macOS 2>&1 | grep -c warning` returns 0
- [ ] No regression in existing functionality
- [ ] `swift test --package-path macOS` passes (keychain-safe tests only)
- [ ] All suppressed warnings have a comment explaining why

## Out of Scope

- C# build warnings
- Python lint warnings
- Runtime warnings (only compile-time)