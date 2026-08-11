# plan.md — Fix Remaining Parity Review Defects

**Track ID:** fix-remaining-parity-defects_20260809
**Spec:** [spec.md](./spec.md)
**Created:** 2026-08-09
**Status:** [ ] Pending

## Phase 1: Fix trust anchor bootstrap (D-1)

- [ ] Task 1.1: Replace `new HashSet<string>()` with `CollectorPinning.ConfiguredPins()` at `LeakDetector.cs:951`

## Phase 2: Fix config source asymmetry (D-2)

- [ ] Task 2.1: Add persisted-file read to `CollectorPinning.ConfiguredPins()` before env var fallback
- [ ] Task 2.2: Add persisted-file read to `WarpManifestPinning.ConfiguredTrustedManifestKeys()` before env var fallback

## Phase 3: Fix exponent casing (D-3)

- [ ] Task 3.1: Lowercase `E` → `e` in `WriteValue` exponent output

## Phase 4: Fix `DomainCanonical.areEquivalent` (D-4)

- [ ] Task 4.1: Switch Swift `ca == cb` to `ca.utf8.elementsEqual(cb.utf8)`

## Phase 5: Fix `LookalikeDetector` case-folding (D-5)

- [ ] Task 5.1: Replace Swift `lowercased()` with locale-invariant fold in `LookalikeDetector`

## Phase 6: Fix `ToAsciiName` IDNA divergence (D-6)

- [ ] Task 6.1: Normalize full IDNA separator set to ASCII dot before `IdnMapping.GetAscii()` on C#

## Phase 7: Fix Swift bare `.sorted()` (D-7)

- [ ] Task 7.1: Replace bare `.sorted()` on `[String]` with explicit UTF-8 ordinal comparator at all 6+ call-sites

## Phase 8: Verification

- [ ] Task 8.1: `dotnet build` — 0 errors
- [ ] Task 8.2: `swift build --package-path macOS` — 0 errors
- [ ] Task 8.3: `Server/venv/bin/python -m pytest Server/tests/ -q` — 439 passed
- [ ] Task 8.4: Verify all 10 acceptance criteria from spec.md