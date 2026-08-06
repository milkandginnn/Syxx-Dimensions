# WORKLOG — Witness error legibility + save-time key validation (2026-07-29)

Scope: `macOS/Sources/SyxxKit/**`, `Windows/dotNET/Syxx.Kit/**` (+ their tests).
Off-limits and NOT touched: `macOS/Sources/SyxxApp/**`, `Windows/dotNET/SyxxUI/**`,
`macOS/Sources/SyxxKit/LeakDetector.swift`.

## Test counts

|   | before | after |
|---|---|---|
| macOS `swift test` | 543 executed / 5 skipped / **0 failures** | 565 executed / 4 skipped / **0 failures** |
| Windows `dotnet test dotNET/Syxx.Tests` | 175 passed / 0 failed | 195 passed / 0 failed |

Caveat on the deltas: another agent was landing `CollectorError` work in the same tree during
this session, so the totals include their additions as well as mine. Mine are +12 macOS
(`WitnessKeyValidationTests`) and +9 Windows (`WitnessKeyValidationTests.cs`). Skipped went 5 -> 4
because `testSaveWitnessKeyReachableFailurePathAndDocumentedGap` no longer needs its `XCTSkip`.

`dotnet build Windows/Syxx.sln` cannot run on this host (NETSDK1100 — `SyxxUI.Tests` targets
Windows). Used `Windows/Syxx.Linux.slnf`: 0 Error(s), 68 Warning(s).

## Wire-value grep counts (identical command before and after)

`grep -rn --include="*.swift" --include="*.cs" --include="*.py" --include="*.json" -F -- '<s>' .`
excluding `archive/ _quarantine/ .build/ obj/ bin/`

| literal | before | after |
|---|---|---|
| `"verdict"` | 59 | **59** |
| `"clean"` | 49 | **49** |
| `"threatDetected"` | 8 | **8** |
| `"inconclusive"` | 141 | **141** |
| `"untested"` | 23 | **23** |

Unchanged. (Absolute numbers differ from the task's quoted 13/19/4/33/16 because the task counted
over a narrower file set; what matters is before == after under one identical command.)

## Files changed

Production:

- `macOS/Sources/SyxxKit/WitnessSigner.swift` — `LocalizedError` on `WitnessSignerError`;
  new shared `validatedPrivateKey(fromBase64:)` / `validatePrivateKey(base64:)`.
- `macOS/Sources/SyxxKit/WitnessKeyManager.swift` — `saveKey` validates before writing;
  added `invalidateCacheForTesting()`.
- `macOS/Sources/SyxxKit/DNSResolver.swift` — `LocalizedError` on `DNSResolverError`.
- `Windows/dotNET/Syxx.Kit/WitnessSigner.cs` — `ImportValidatedPrivateKey` /
  `ValidatePrivateKey`; all key rejections are now `WitnessSignerException` (was raw
  `FormatException` / NSec exception).
- `Windows/dotNET/Syxx.Kit/WitnessKeyStore.cs` — `SaveKey` validates before writing.

Tests:

- NEW `macOS/Tests/SyxxKitTests/WitnessKeyValidationTests.swift` (12)
- NEW `Windows/dotNET/Syxx.Tests/WitnessKeyValidationTests.cs` (9)
- NEW `macOS/Tests/SyxxAppTests/WitnessKeychainTestSupport.swift`
- `macOS/Tests/SyxxAppTests/AppModelWitnessTests.swift` — tearDown restores verbatim; one test
  inverted (it previously asserted the defect), one plants a legacy bad key raw.
- `macOS/Tests/SyxxAppTests/WitnessTestifyParityTests.swift` — tearDown restores verbatim.

## Mutations run

1. macOS, remove `LocalizedError` from `WitnessSignerError` -> RED, 20 assertions.
   Reproduced the owner's exact screen string:
   `The operation couldn't be completed. (SyxxKit.WitnessSignerError error 1.)`
2. macOS, remove `try WitnessSigner.validatePrivateKey(...)` from `saveKey` -> RED, 4 assertions
   ("did not throw an error").
3. Windows, remove `WitnessSigner.ValidatePrivateKey(...)` from `SaveKey` -> RED, 1 test.
4. Windows, revert `ImportValidatedPrivateKey` to raw `Convert.FromBase64String` + `Key.Import`
   -> RED, 3 tests.

All four restored; suites re-run green afterwards.

## Real runs (the actual bar)

- `syxx witness create example.com --key lklklklk` over real DNS (system resolver 10.2.0.1):
  `Failed to create witness snapshot: The signing key is not valid Ed25519 key material. An
  Ed25519 private key is exactly 32 bytes, written as base64.`
- Generate keypair to scratch, sign `example.com` over real DNS, verify: `VALID` /
  `Tamper-evident — Ed25519 signed content matches.`
- `syxx nslook` 70-byte label: `DNS Query failed: The DNS response could not be parsed: DNS label
  length is invalid (70 bytes)`
- `syxx nslook example.com @192.0.2.1` (blackhole): `DNS Query failed: The DNS query timed out
  with no answer.`
- Cross-platform signature parity: `E011ByteParityTests.testHardcodedCSharpSignatureVerifies`
  green on macOS after the change.

## Status

- [x] Job 1 — `WitnessSignerError: LocalizedError` (+ audit; `DNSResolverError` also fixed)
- [x] Job 2 — save-time Ed25519 validation, macOS + Windows twin
- [x] Job 3 — investigated; NOT renamed, reported instead (see report)
