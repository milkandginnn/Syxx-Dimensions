# fix-build-errors — Specification

## Overview

Restore successful, clean builds across all platforms after a cross-platform refactoring
introduced compilation errors. The build is currently blocked on macOS (Swift), Windows (C#),
Linux (C#/Avalonia), and Server (Python).

## Functional Requirements

1. **macOS build** — `swift build --package-path macOS` must exit 0 with zero errors and zero
   warnings.
2. **Windows build** — `dotnet build Windows/Syxx.sln` must exit 0 with zero errors and zero
   warnings (note: SyxxUI cannot build on macOS; verification on actual Windows required).
3. **Linux build** — `dotnet build` for the Linux/Avalonia project must exit 0 with zero errors
   and zero warnings.
4. **Server build** — Python server must pass `ruff check` and `ty` type-checking with zero
   errors.
5. **Cross-platform parity check** — `parity-transcript` must produce identical sorted-JSON
   output from both Swift and .NET runtimes.

## Non-Functional Requirements

- No suppression of warnings via compiler flags or pragmas unless the warning is a
  known platform-specific false positive with a documented reason.
- Refactoring-introduced errors must be fixed at the source, not worked around.

## Acceptance Criteria

1. `swift build --package-path macOS` exits 0, no errors, no warnings.
2. `dotnet build Windows/Syxx.sln` exits 0, no errors, no warnings (verified on Windows).
3. `dotnet build` for Linux/Avalonia exits 0, no errors, no warnings.
4. `ruff check Server/` and `ty` pass with zero errors.
5. `parity-transcript` diff shows zero differences between Swift and .NET.
6. All existing tests pass on each platform.

## Out of Scope

- Functional changes to Syxx behavior beyond fixing compilation.
- New features or enhancements.
- Addressing pre-existing warnings that predate the refactoring.
- Windows GUI verification from macOS (not possible per tech-stack constraints).