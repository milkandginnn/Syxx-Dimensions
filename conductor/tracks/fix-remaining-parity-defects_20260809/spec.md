# spec.md — Fix Remaining Parity Review Defects

**Track ID:** fix-remaining-parity-defects_20260809
**Type:** bug
**Status:** new

## Overview

Fix the 7 remaining defects from the 6-dimension parity review: 1 Critical, 2 High, 4 Medium.

## Defects

### D-1: Trust anchor bootstrap unpinned on C# (Critical)

`LeakDetector.cs:951` — `SyncTrustedSigningKeyAsync` fetches `/api/pubkey` with `new HashSet<string>()` (empty pin set). MITM with any CA-issued certificate can inject forged Ed25519 key. Swift uses `CollectorPinning.configuredPins()`.

**Fix:** Replace `new HashSet<string>()` with `CollectorPinning.ConfiguredPins()`.

### D-2: Config source asymmetry — `CollectorPinning` and `WarpManifestPinning` (High)

`LeakDetector.cs:909` and `WarpManifestPinning.cs:43` — C# reads only from `SYXX_COLLECTOR_PINS` / env var, skipping the `UserDefaults` equivalent. Swift checks `UserDefaults` first, then env var. Windows GUI can't set SPKI pins or manifest trust keys.

**Fix:** Add a persisted-file config read before the env var fallback in both `ConfiguredPins()` and `ConfiguredTrustedManifestKeys()`. Use the existing `%LocalAppData%/Syxx/` file pattern.

### D-3: Exponent casing `E` vs `e` (High)

`ParityTranscriptCommand.cs` `WriteValue` — `"R"`/`"G17"` produces uppercase `E` for exponents. Swift produces lowercase `e`. Latent for any vector with scientific notation.

**Fix:** Post-process the formatted string to lowercase `E` → `e`.

### D-4: `DomainCanonical.areEquivalent` comparison semantics (Medium)

`DomainCanonical.swift:99` uses Swift `==` (canonical equivalence). `DomainCanonical.cs:101` uses `StringComparison.Ordinal`. Both canonicalizers produce NFC, but the comparison semantics differ in principle.

**Fix:** Switch Swift to `ca.utf8.elementsEqual(cb.utf8)` for byte-stable ordinal comparison.

### D-5: `LookalikeDetector` Turkish İ case-folding (Medium)

`LookalikeDetector.swift:68,84,128` uses `lowercased()` (Unicode-aware). `LookalikeDetector.cs:52,56,96` uses `ToLowerInvariant()`. For Turkish İ (U+0130), Swift produces `i` + combining dot; C# produces `i` alone.

**Fix:** Replace Swift `lowercased()` with a locale-invariant fold.

### D-6: `ToAsciiName` IDNA version divergence (Medium)

`DNSResolver.swift:690-730` uses hand-rolled RFC 3492 punycode. `DNSResolver.cs:1164` uses `IdnMapping.GetAscii()` (IDNA 2008). Different rules for label separators, STD3 rules, and unassigned code points.

**Fix:** Normalize full IDNA separator set to ASCII dot before calling `IdnMapping.GetAscii()` on C#.

### D-7: Swift bare `.sorted()` vs C# `StringComparer.Ordinal` (Medium)

6+ call-sites in Swift use bare `.sorted()` on `[String]` (NFD-aware canonical equivalence). C# uses `StringComparer.Ordinal`. Dormant because all sorted strings are ASCII.

**Fix:** Replace bare `.sorted()` on `[String]` with `sorted { $0.utf8.lexicographicallyPrecedes($1.utf8) }` at all call-sites.

## Acceptance Criteria

- [ ] `LeakDetector.cs:951` uses `CollectorPinning.ConfiguredPins()` instead of empty HashSet
- [ ] `CollectorPinning.ConfiguredPins()` and `WarpManifestPinning.ConfiguredTrustedManifestKeys()` read from persisted file before env var
- [ ] `WriteValue` lowercases `E` → `e` for exponent notation
- [ ] `DomainCanonical.areEquivalent` uses byte-stable ordinal comparison on both platforms
- [ ] `LookalikeDetector` case-folding is locale-invariant on both platforms
- [ ] `ToAsciiName` normalizes full IDNA separator set on C#
- [ ] Swift bare `.sorted()` on `[String]` replaced with explicit UTF-8 ordinal comparator
- [ ] `dotnet build` — 0 errors
- [ ] `swift build --package-path macOS` — 0 errors
- [ ] `Server/venv/bin/python -m pytest Server/tests/ -q` — 439 passed

## Out of Scope

- Adding non-ASCII/IDN test cases to the corpus
- Fixing decompression on non-DimensionClient HTTP clients
- Adding `LookalikeDetector.Decoded` C# test