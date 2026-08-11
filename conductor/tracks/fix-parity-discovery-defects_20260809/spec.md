# spec.md — Fix Parity-Discovery Defects

**Track ID:** fix-parity-discovery-defects_20260809
**Type:** bug
**Status:** new

## Overview

Fix three defects identified by the 6-dimension parity review and confirmed by 3 research agents.

## Defects

### D-1: JSON encoder diverges from Swift (High)

`ParityTranscriptCommand.cs:267` uses `JavaScriptEncoder.Default` which escapes 6 HTML characters (`'`, `&`, `<`, `>`, `` ` ``, `+`) that Swift's `JSONSerialization` does not. The prior `UnsafeRelaxedJsonEscaping` under-escaped non-ASCII. Neither built-in encoder matches Swift.

**Fix:** Use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` + post-process non-ASCII bytes to `\uXXXX` (mirroring existing `EscapeForwardSlashes` pattern).

### D-2: DimensionClient lacks AutomaticDecompression (High)

`DimensionClient.cs:64` (`_http`) and `:157` (`_hardenedHttp`) lack `AutomaticDecompression`. Swift's `URLSession` transparently decompresses gzip/deflate/brotli. Compressed API responses fail on C#.

**Fix:** Add `SocketsHttpHandler` with `AutomaticDecompression = GZip | Deflate | Brotli` to both clients. Pattern already exists at `WarpEngine.cs:591`.

### D-3: LookalikeDetector.Decoded missing NFC normalization (High)

`LookalikeDetector.cs:110` — `IdnMapping.GetUnicode()` does not guarantee NFC output. Swift applies `precomposedStringWithCanonicalMapping` after punycode decode. NFD-encoded A-labels produce different homoglyph/typosquat classifications.

**Fix:** Add `.Normalize(NormalizationForm.FormC)` after `idn.GetUnicode(label)`.

## Acceptance Criteria

- [ ] `JavaScriptEncoder.Default` replaced with `UnsafeRelaxedJsonEscaping` + non-ASCII → `\uXXXX` post-process
- [ ] `DimensionClient._http` and `_hardenedHttp` have `AutomaticDecompression`
- [ ] `LookalikeDetector.Decoded` applies `.Normalize(NormalizationForm.FormC)`
- [ ] `dotnet build Windows/dotNET/Syxx/Syxx.csproj -c Release` — 0 errors
- [ ] `bash scripts/verify-parity-fingerprint.sh` exits 0
- [ ] `python3 scripts/cross-platform-parity-gate.py` exits 0
- [ ] `Server/venv/bin/python -m pytest Server/tests/ -q` — 420 passed

## Out of Scope

- Adding non-ASCII test cases to the corpus
- Fixing decompression on other HTTP clients (LicenseManager, UpdateService, LeakDetector, DNSResolver, NetworkKeyState)
- Adding C# test for `LookalikeDetector.Decoded`