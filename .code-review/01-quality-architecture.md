# Phase 2B — Code Quality & Architecture

## Code Quality (`comprehensive-review__code-reviewer`)
**1 Critical, 3 High, 7 Medium, 2 Low**

### Critical
- `Server/syxx_server/app.py:996` — `do_DELETE` has no CSRF protection while `do_POST` does. Attacker can use `fetch()` with DELETE method to bypass CSRF and delete user's monitors.

### High
- `Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs:407` — `CaptureEventGate` not thread-safe. `_lastEmitted` Dictionary accessed without synchronization.
- `Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs` — `Breadcrumb` called from `catch` blocks can throw, replacing original exception.
- `Server/syxx_server/app.py` — `_client_ip` returns loopback on malformed XFF, enabling rate-limit evasion.

### Medium
- `Windows/dotNET/Syxx.Kit/WarpEngine.cs` — `ResolveOverrideForTests` mutable internal static (not thread-safe for parallel tests)
- `Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs` — 1,683 lines single file
- `Server/syxx_server/app.py` — CSP nonce caching correct, header duplication pre-existing
- `Windows/dotNET/Syxx.Kit/Capture/CaptureEvents.cs` — rcode string matching fragile to producer changes
- `macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift` — No C# twin exists
- `Server/syxx_server/resolver.py` — `_format_rdata` returns `None` for MX but `rdata.hex()` for SOA (inconsistent)
- `Server/syxx_server/dnssec_validator.py` — `_sq` silently swallows `DNSError` for all callers

## Architecture (`comprehensive-review__architect-review`)
**1 Critical, 5 High, 5 Medium, 2 Low**

### Critical
- `macOS/Sources/SyxxKit/DicePassphraseWordlist.swift` — Missing Swift generator; C# has full generator, Swift only has wordlist data. Doc comments reference types that don't exist.

### High
- `macOS/Sources/SyxxKit/CageRedundancyCorrelator.swift` — macOS-only pure logic, no C# twin
- `macOS/Sources/SyxxKit/DNSPathConfirmation.swift` — macOS-only daemon communication, no C# equivalent
- `DELTAS.md:245,249` — Text corruption in A031 entry
- `.github/workflows/ci.yml` — Linux packaging jobs never executed, all `fixed-unverified`
- `Linux/dotNET/Syxx.Linux/` — Project deletion needs build verification

### Medium
- `Linux/Ubuntu/SyxxUI.Avalonia/Mirror/` — Linux-only, no macOS/Swift equivalent (3 separate implementations)
- `Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs` — 2.4× larger than Swift twin
- `DELTAS.md` — Non-sequential numbering (A032 → A262 jump)
- `docs/plan-lanes/` — 4,900 lines of planning docs, risk of staleness
- `Server/syxx_server/` — 6,723 LOC refactoring across 36 files, broad scope