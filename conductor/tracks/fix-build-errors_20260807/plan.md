# fix-build-errors — Implementation Plan

## Phase 1: Diagnosis

- [x] Task: Run all platform builds to capture current error state
    - [x] Run `swift build --package-path macOS` and capture full error output
    - [x] Run `dotnet build` on macOS-safe .NET projects and capture errors
    - [x] Run `ruff check Server/` and `ty` and capture errors
    - [x] Catalog errors by platform, file, and error type

## Phase 2: Fix macOS (Swift) build

- [x] Task: Resolve Swift compilation errors
    - [x] Fix each compilation error identified in Phase 1
    - [x] Run `swift build --package-path macOS` until zero errors
- [x] Task: Eliminate Swift warnings
    - [x] Identify and fix all warnings from the build output
    - [x] Run `swift build --package-path macOS` until zero warnings
- [x] Task: Run Swift tests
    - [x] Execute `swift test --package-path macOS`
    - [x] Verify all tests pass

## Phase 3: Fix Windows/Linux (.NET) build

- [x] Task: Resolve C# compilation errors in shared projects
    - [x] Fix each compilation error in `Syxx.Kit` and non-UI projects
    - [x] Run `dotnet build` on the macOS-buildable subset until zero errors
- [x] Task: Fix Linux/Avalonia-specific errors
    - [x] Address any Avalonia UI compilation errors
    - [x] Run `dotnet build` for Linux/Avalonia project until zero errors
- [x] Task: Eliminate .NET warnings
    - [x] Identify and fix all warnings from the build output
    - [x] Run `dotnet build` until zero warnings
- [x] Task: Run .NET tests
    - [x] Execute `dotnet test Windows/dotNET/Syxx.Tests` for all runnable test classes
    - [x] Verify all tests pass (84 tests across 10 classes, 0 failures)

## Phase 4: Fix Server (Python) build

- [x] Task: Resolve ruff lint errors
    - [x] Run `ruff check Server/` and fix all violations
    - [x] Verify `ruff check Server/` exits 0
- [x] Task: Resolve type-checking errors
    - [x] Run `ty` and fix all type errors
    - [x] Verify `ty` exits 0
- [x] Task: Run Python tests
    - [x] Execute server test suite
    - [x] Verify all tests pass (420 passed, 0 failed)

## Phase 5: Cross-platform verification

- [ ] Task: Run parity-transcript comparison
    - [ ] Execute `dotnet run --project Windows/dotNET/Syxx/Syxx.csproj -c Release -- parity-transcript --out=/tmp/t.net.json`
    - [ ] Execute `swift run --package-path macOS -c release syxx parity-transcript --out=/tmp/t.swift.json`
    - [ ] Run `diff <(jq --sort-keys /tmp/t.net.json) <(jq --sort-keys /tmp/t.swift.json)`
    - [ ] Verify zero differences

## Phase 6: Final verification

- [ ] Task: Full build gate
    - [ ] Confirm all builds exit 0 with zero errors and zero warnings
    - [ ] Confirm all test suites pass
    - [ ] Record each claim with its verification state per the five-state system