# Test Adequacy Report — Syxx

**Track:** check-test-adequacy_20260808
**Date:** 2026-08-08
**Status:** Detection-only — no code modified

## Executive Summary

Audited 2,532 tests across 238 files on 4 platforms, 16 corpus-driven harnesses, and 6 gates.
Found **2 🔴 Critical**, **12 🟡 Should Fix**, and **4 💡 Consider** findings.

| Severity | Count | Description |
|----------|:-----:|-------------|
| 🔴 Critical | 2 | Tests committed against uncommitted code; latent CI breakage |
| 🟡 Should Fix | 12 | Mock relaxations, anti-vacuity gaps, OS-gated returns |
| 💡 Consider | 4 | Setup hardening, type-checker scope, minor coverage |

**No harness was found silently skipping cases today.** The two 🔴 findings are about test/source
consistency — tests that will fail on a clean checkout. The 🟡 findings are about hardening against
future drift.

---

## Suite Inventory

| Platform | Files | Tests | Corpus-Driven | Eval Assert | Untracked |
|----------|------:|------:|:---:|:---:|:---:|
| Swift (macOS) | 123* | 1,080 | 6 | 4 of 6 | 6 |
| C# (.NET) | 84 | 1,032 | 12 | 9 of 12 | 9 |
| Python | 31 | 420 | 2 | 2 of 2 | 0 |
| **Total** | **238** | **2,532** | **20** | **15 of 20** | **15** |

*Includes 19 quarantined files (219 tests)

## Gate Inventory

| Gate | Anti-Vacuity | Proven-Red | CI Wired | Live Status |
|------|:---:|:---:|:---:|:---:|
| cross-platform-parity-gate.py | ✅ | ✅ | ✅ | 🟢 PASS |
| cli-surface-gate.py | ✅ | ✅ | ✅ | 🔴 RED (2 unledgered) |
| parity-local-gate.sh | ✅ | ✅ | ❌ (local) | N/A |
| package-pin-parity.py | ✅ | ✅ | ✅ | 🟢 PASS |
| behavioral-vector-runner.py | ⚠️ | ✅ | 🔴 UNTRACKED | N/A |
| check-eula-parity.py | ⚠️ | ❓ | ❌ | N/A |

---

## Findings

### 🔴 Critical

**S-01: TriState test softened to match uncommitted code**
- File: `Server/tests/test_types.py:41` (commit `840d35d`)
- `test_three_distinct_members` asserts `len(TriState) == 4`
- Committed `types.py` has 3 members; `UNKNOWN` was added to the working tree but never committed
- Clean checkout will FAIL — committed enum has 3 members, test expects 4
- Fix: either commit the `UNKNOWN` addition to `types.py` or revert the test to `len(TriState) == 3`

**S-02: Font count test softened to match uncommitted code**
- File: `macOS/Tests/SyxxAppTests/TypographyResourcesTests.swift:15` (commit `36e2fee`)
- `bundledFonts.count == 24` (was 23)
- Committed `IoDesignSystem.swift` has 23 fonts; `EBGaramond-Regular` was added to working tree but never committed
- Clean checkout will FAIL — committed source has 23 fonts, test expects 24
- Fix: commit the `EBGaramond-Regular` addition to `bundledFonts` in `IoDesignSystem.swift`

**G-01: behavioral-vector-runner.py and corpus/behavioral-vectors/ are UNTRACKED**
- Both the runner script and the entire behavioral vectors corpus are not in git
- The `parity-behavioral` CI job in `.github/workflows/ci.yml` references this script
- CI will fail on any clean checkout with "No such file or directory"
- Fix: `git add scripts/behavioral-vector-runner.py corpus/behavioral-vectors/` and commit

### 🟡 Should Fix

**S-03: Mock signatures relaxed with `**kwargs` (5 occurrences)**
- `Server/tests/test_payments.py:706,734` — `fake_urlopen(req, timeout=None, **kwargs)` (×2)
- `Server/tests/test_resolver_fetcher_ssrf.py:186,211,237` — `mock(name, qtype, server, timeout=3, do=False, **kwargs)` (×3)
- Mocks now silently accept arbitrary keyword arguments
- Could hide real API changes where production code passes unexpected args
- Fix: remove `**kwargs` and explicitly declare the new parameters, or add assertion that only expected kwargs are passed

**A-01: CrossVantageConsensusGoldenVectorTests — missing evaluated-count assertion**
- File: `macOS/Tests/SyxxKitTests/CrossVantageConsensusGoldenVectorTests.swift:64`
- No counter, no nonzero guard, no `evaluated == cases.count` assertion
- Currently evaluates all cases (no skip mechanism), but no mechanical proof
- Fix: add WarpEngineTests house pattern (counter + `XCTAssertGreaterThan(evaluated, 0)` + `XCTAssertEqual(evaluated, cases.count)`)

**A-02: WitnessCrossPlatformGoldenVectorTests — hardcoded vector list + missing counter**
- File: `macOS/Tests/SyxxKitTests/WitnessCrossPlatformGoldenVectorTests.swift:21`
- `vectorNames` is a static array of 3 strings — any vector added to `corpus/witness-crossplatform/` is silently uncovered
- No evaluated-count assertion across all 5 tests that iterate over `vectorNames`
- Already flagged by cross-platform-parity track in `not-covered.md`
- Fix: derive vector list from directory at runtime, add evaluated-count house pattern

**D-01: C# WitnessKeyValidationTests — catch-all exception helper**
- File: `Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs:128`
- `static bool Try(Action a) { try { a(); return true; } catch { return false; } }` catches all exceptions indiscriminately
- An unexpected `NullReferenceException` would be silently converted to `false`
- Fix: narrow `catch` to `catch (WitnessSignerException)` or `catch (Exception)`

**D-02: C# WitnessKeyValidationTests — zero-assertion characterization test**
- File: `Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs:138-144`
- `ValidatePrivateKey_EmbeddedWhitespaceIsSkippedByTheRuntime` has no `Assert.*` calls
- Relies on methods not throwing to indicate pass — invisible in test output
- Fix: wrap in `var ex = Record.Exception(() => ...); Assert.Null(ex)`

**D-03 to D-09: C# OS-gated early returns (7 tests)**
- Files: `DNSCaptureAttributionTests.cs:96`, `LinuxSecretStoreKdfTests.cs:71,86,99`, `WitnessKeypairGuardTests.cs:230`, `ConfigAndPolicyTests.cs:930`, `LeakCollectorStatusTests.cs:443`
- Tests use `if (OperatingSystem.IsWindows()) return;` which silently passes on non-target platforms
- A `SkippableFactAttributeSubstitute` already exists at `DNSCaptureAttributionTests.cs:108` but only used by one test
- Fix: replace `return` with `Assert.Skip("POSIX-only")` or use `SkippableFactAttributeSubstitute`

**G-02: cli-surface-gate.py is currently RED**
- 2 unledgered `speed` command asymmetries: macOS accepts `--help`/`-h` on `speed`, Windows does not
- These are not in the 18 ledgered exceptions
- Fix: either ledger the exceptions or fix the asymmetry

**G-03: parity-behavioral CI job has no nonzero guard**
- If all offline vectors were deleted and only fixture vectors existed (all SKIPped), the job would exit 0
- Fix: add a minimum-executed-count assertion to the CI step

### 💡 Consider

**A-03: TypographyResourcesTests.cs — hardcoded RequiredFaces() list**
- File: `Windows/dotNET/Syxx.Tests/TypographyResourcesTests.cs`
- `RequiredFaces()` returns 24 items; a font added to disk but not the list would be silently uncovered
- Covered by sibling `TypographyAssetsTests.cs` which asserts `FlatFaceCountMatchesManifestExactly`
- Fix: add `Assert.Equal(24, RequiredFaces().Count())` to pin the list length

**A-04: LinuxDistroTests.cs — limited distro coverage**
- File: `Windows/dotNET/Syxx.Tests/LinuxDistroTests.cs`
- Tests cover ~11 distros; unrecognized distros resolve to `LinuxFamily.Unknown` (safe default)
- Parser design is inherently safe — unknown distros cannot produce false results
- Fix: add corpus of real os-release files from additional distros (Alpine, Gentoo, NixOS, openSUSE)

**D-10, D-11: Swift setup `try?` that could redirect test paths**
- Files: `SyxxSettingsTests.swift:45,47`, `ThemeModeTests.swift:111,131`
- `try?` on `createDirectory`/`write` in test setup could silently fall through to different code path
- If setup fails, the test exercises the "missing file" path instead of the intended "corrupt/legacy" path
- Fix: use `try` / `XCTAssertNoThrow` for setup operations that define the test's code path

**T-01: ty file-level overrides are broader than necessary**
- `Server/pyproject.toml` has 10 `[tool.ty]` override blocks at file level
- `ty` v0.0.69 lacks inline suppression support, so file-level is the only option
- Fix: use more specific `include` globs when `ty` adds support for narrower overrides

---

## Not Covered

This audit could not verify:

- **CI-only gates** (`parity-transcript`, `parity-behavioral`): blocked on GitHub Actions billing issue C-06
- **Windows-only tests**: `SyxxUI.Tests` and `SyxxUI.Logic.Tests` cannot build on macOS (WinUI XamlCompiler crashes)
- **Runtime behavior**: gates were verified for anti-vacuity and proven-red capability, but not run against actual divergent output
- **Quarantine tests**: 19 files under `macOS/Tests/_quarantine/` (219 tests) were inventoried but not audited
- **Archived tests**: 16 files under `archive/dst-tests-windows/` were noted but not audited
- **Test performance**: no benchmarking or flakiness analysis was performed
- **Coverage gaps**: this audit checked that existing tests are honest, not that all code paths are covered

---

## Ledger

| ID | Severity | Platform | File | Finding | Phase |
|----|----------|----------|------|---------|-------|
| S-01 | 🔴 | Python | `Server/tests/test_types.py` | TriState test softened to match uncommitted code | 3 |
| S-02 | 🔴 | Swift | `macOS/Tests/SyxxAppTests/TypographyResourcesTests.swift` | Font count test softened to match uncommitted code | 3 |
| G-01 | 🔴 | Cross | `scripts/behavioral-vector-runner.py` | Runner and corpus UNTRACKED — CI breakage | 5 |
| S-03 | 🟡 | Python | `Server/tests/test_payments.py`, `test_resolver_fetcher_ssrf.py` | 5 mock signatures relaxed with `**kwargs` | 3 |
| A-01 | 🟡 | Swift | `macOS/Tests/SyxxKitTests/CrossVantageConsensusGoldenVectorTests.swift` | Missing evaluated-count assertion | 2 |
| A-02 | 🟡 | Swift | `macOS/Tests/SyxxKitTests/WitnessCrossPlatformGoldenVectorTests.swift` | Hardcoded 3-vector list + missing counter | 2 |
| D-01 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs:128` | Catch-all exception helper | 4 |
| D-02 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs:138` | Zero-assertion characterization test | 4 |
| D-03 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/DNSCaptureAttributionTests.cs:96` | OS-gated early return | 4 |
| D-04 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/LinuxSecretStoreKdfTests.cs:71` | OS-gated early return | 4 |
| D-05 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/LinuxSecretStoreKdfTests.cs:86` | OS-gated early return | 4 |
| D-06 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/LinuxSecretStoreKdfTests.cs:99` | OS-gated early return | 4 |
| D-07 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/WitnessKeypairGuardTests.cs:230` | OS-gated early return | 4 |
| D-08 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/ConfigAndPolicyTests.cs:930` | Partial skip for scope-id data row | 4 |
| D-09 | 🟡 | C# | `Windows/dotNET/Syxx.Tests/LeakCollectorStatusTests.cs:443` | Network-dependent early return | 4 |
| G-02 | 🟡 | Cross | `scripts/cli-surface-gate.py` | Currently RED — 2 unledgered speed asymmetries | 5 |
| G-03 | 🟡 | Cross | `.github/workflows/ci.yml` (parity-behavioral) | No nonzero guard on final summary | 2 |
| A-03 | 💡 | C# | `Windows/dotNET/Syxx.Tests/TypographyResourcesTests.cs` | Hardcoded RequiredFaces() list | 2 |
| A-04 | 💡 | C# | `Windows/dotNET/Syxx.Tests/LinuxDistroTests.cs` | Limited distro coverage (safe default) | 2 |
| D-10 | 💡 | Swift | `macOS/Tests/SyxxKitTests/SyxxSettingsTests.swift:45` | Setup `try?` could redirect test path | 4 |
| D-11 | 💡 | Swift | `macOS/Tests/SyxxKitTests/ThemeModeTests.swift:111` | Setup `try?` could redirect test path | 4 |
| T-01 | 💡 | Python | `Server/pyproject.toml` | ty file-level overrides broader than needed | 3 |

---

_Generated by check-test-adequacy_20260808. No test code was modified._