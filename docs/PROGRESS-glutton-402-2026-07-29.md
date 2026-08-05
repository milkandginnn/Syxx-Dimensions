# PROGRESS — leak-test collector status collapse fix (2026-07-29)

Anchor: DIAG-glutton-2026-07-29.md. Scope: macOS/Sources/SyxxKit/LeakDetector.swift +
Windows/dotNET/Syxx.Kit/LeakDetector.cs (+ tests). NOT SyxxApp/**, NOT SyxxUI/**.

## Findings before edits (verified, not re-derived)

- macOS LeakDetector.swift:381-384 `CollectorError` is bare `Error`; `badStatus(Int)` value
  never read. Confirmed by grep: zero hits for 402/payment/requiresPro in macOS/Sources.
- macOS :191-193 and :273-275 collapse every non-200 into one `.error` + bridge-dump string.
- Windows LeakDetector.cs:547 / :565 throw `HttpRequestException($"HTTP {code}")` — the status
  is in the MESSAGE only, nothing classifies it. Same collapse. Windows does NOT distinguish.
- PRE-EXISTING PARITY DIVERGENCE (independent of this defect): macOS returns `.error` for a
  token/verify failure (:191, :273); Windows returns `Inconclusive` (:206, :270).
- Windows has NO LeakDetector tests at all (Syxx.Tests has no leak file).
- macOS ErrorPresenter.isBoilerplate would suppress the whole reason string today, so the GUI
  shows NO note rather than a wrong one once the string is boilerplate. After LocalizedError
  conformance the reason is genuine copy and passes through.
- Windows GluttonView.xaml.cs:127-138 gates on `attested` BEFORE the Result switch, so a 402
  (no signature) renders "[UNVERIFIED] Result cannot be trusted" and the entitlement reason is
  never shown. UI patch goes in the report, not applied (SyxxUI is another agent's lane).

## Plan

1. Swift: add `CollectorFailure` (entitlementRequired/authenticationFailed/rateLimited/
   serverError/unexpectedStatus/malformedResponse/collectorIdentityUnverified/transportFailure),
   carry it on `LeakTestResult.collectorFailure`, classify status + error, conform
   `CollectorError` to `LocalizedError`. Same for requestToken AND verifyToken.
2. 402 -> `.inconclusive` (nothing broke, the feature is gated). Everything else -> `.error`.
   Never `.notDetected`.
3. Windows twin: identical classification; align verdicts to macOS.
4. Tests + mutation runs on every new branch.

## CONCURRENT-EDIT WARNING (affects test counts, not correctness)

Another agent is editing this working tree DURING this session. Observed at 11:00 CDT:
`macOS/Tests/SyxxKitTests/WitnessKeyValidationTests.swift` mtime 11:00:05, three seconds before
my check; `macOS/Sources/SyxxKit/WitnessKeyManager.swift` 10:53; SyxxAppTests/
WitnessKeychainTestSupport.swift 10:54. Two builds aborted with
"input file ... was modified during the build" and a transient "cannot find 'WitnessKeyManager'
in scope". The macOS total moved 543 -> 565 executed and 6 -> 4 skipped for reasons that are NOT
mine. Report the failure count (0), not an attributable delta.

## Status log

- [x] read diag, both twins, consumers
- [x] baseline macOS: 543 executed / 6 skipped / 0 failures (10:32, before the concurrent edits)
- [x] baseline Windows: 175 passed / 0 failed
- [x] macOS implementation
- [x] macOS tests (8 new) + 9 mutations, all RED then restored
- [x] macOS after: 565 executed / 4 skipped / 0 failures
- [x] Windows implementation (Syxx.Kit builds)
- [x] Windows tests (11 new) + 9 mutations; W8 was GREEN first pass (real gap), added
      Classify_PrefersTransportIdentityOverEverythingElse, re-ran W8 -> RED
- [x] Windows after: 195 passed / 0 failed
- [x] REAL RUNS: production collector returns 402 (verified by curl, 1.4s, body
      {"error": "payment required: activate a Syxx Pro license", ...}); real CLI binary driven
      through 402/429/503/418/403 and all five now read differently and honestly.

## BLOCKED / ENVIRONMENT (needs the owner)

The `syxx` CLI binary cannot reach https://syxx.app from this machine. Its requests die with
NSURLErrorNetworkConnectionLost (-1005, errno 57) on interface utun7, while a standalone binary
issuing the byte-identical request over the same URLSession config succeeds (HTTP 402 in 1.33s)
in the same second. So it is a per-binary local block (VPN split tunnel or an app firewall),
not a server or code fault. The production 402 was therefore confirmed with curl and replayed
verbatim through the real CLI over loopback.

DONE. Do not commit (per instructions).
