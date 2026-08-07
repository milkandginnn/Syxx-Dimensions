# fix-build-errors — Implementation Plan

## Phase 1: Diagnosis

- [x] Task: Run all platform builds to capture current error state
    - [x] Run `swift build --package-path macOS` and capture full error output
    - [x] Run `dotnet build` on macOS-safe .NET projects and capture errors
    - [x] Run `ruff check Server/` and `ty` and capture errors
    - [x] Catalog errors by platform, file, and error type

## Phase 2: Fix macOS (Swift) build

- [ ] Task: Resolve Swift compilation errors
    - [ ] Fix each compilation error identified in Phase 1
    - [ ] Run `swift build --package-path macOS` until zero errors
- [ ] Task: Eliminate Swift warnings
    - [ ] Identify and fix all warnings from the build output
    - [ ] Run `swift build --package-path macOS` until zero warnings
- [ ] Task: Run Swift tests
    - [ ] Execute `swift test --package-path macOS`
    - [ ] Verify all tests pass

## Phase 3: Fix Windows/Linux (.NET) build

- [ ] Task: Resolve C# compilation errors in shared projects
    - [ ] Fix each compilation error in `Syxx.Kit` and non-UI projects
    - [ ] Run `dotnet build` on the macOS-buildable subset until zero errors
- [ ] Task: Fix Linux/Avalonia-specific errors
    - [ ] Address any Avalonia UI compilation errors
    - [ ] Run `dotnet build` for Linux/Avalonia project until zero errors
- [ ] Task: Eliminate .NET warnings
    - [ ] Identify and fix all warnings from the build output
    - [ ] Run `dotnet build` until zero warnings
- [ ] Task: Run .NET tests
    - [ ] Execute `dotnet test Windows/dotNET/Syxx.Tests`
    - [ ] Verify all tests pass

## Phase 4: Fix Server (Python) build

- [ ] Task: Resolve ruff lint errors
    - [ ] Run `ruff check Server/` and fix all violations
    - [ ] Verify `ruff check Server/` exits 0
- [ ] Task: Resolve type-checking errors
    - [ ] Run `ty` and fix all type errors
    - [ ] Verify `ty` exits 0
- [ ] Task: Run Python tests
    - [ ] Execute server test suite
    - [ ] Verify all tests pass

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