# Phase 0 Triage — 36 defects against HEAD `a2adae23`

**Date:** 2026-08-12 · **Baseline:** `a2adae23`, branch `main` · **Method:** read-only specialist classification, orchestrator spot-verified

## Headline

**25 of 36 are already repaired. 6 need code. 5 describe code that no longer exists.**

The findings were authored against a pre-revert tree, which Phase 0 existed to detect. Repairing all 36 would have churned correct code in 25 places.

| State | Count | Defects |
|---|---|---|
| repaired (sound) | 25 | W01–W03, W06, W08–W11, W14–W17, W21, M02–M03, M06–M15 |
| **live — needs work** | **5** | **W04, W12, W20, M01, M05** |
| **moved + still live** | **1** | **W13** → `LinuxSecretStore.cs:562-565` |
| absent-code | 5 | W05, W07, W18, W19, M04 |

## Live defects (the actual Wave 1–6 workload)

| ID | Location | State |
|---|---|---|
| W04 | `VantageComparer.cs:401` | Partially repaired. The all-DoH-failed path now raises `Inconclusive` via `dohBlockedWhileUdpUnanimous` (`:351-357`), but the `<2 endpoints answered` path still returns a clean result. Fix is narrow: extend the `:356` condition to mirror the `:401` gate. |
| W12 | `WarpEngine.cs:383` | **Regressed, worse than reported.** All three call sites — `ProbePlan.cs:163`, `Program.cs:655`, `WarpCommand.cs:48` — now pass no nonce. The finding recorded ProbePlan as the one site that *did*. Replay protection is off on every Windows Warp path. Orchestrator-verified. |
| W20 | `MatrixDeterminativeEngine.cs:924-927` | **Broader than reported.** No usability guard at all in the support loop. A non-usable section's indicator now also reaches `alignedSupport`, `SupportingIndicators`, `HasDecisive`, `MajorCount` — not just coverage. `IsUsable` and `sectionById` are already in scope; a two-line guard. |
| W13 | `LinuxSecretStore.cs:562-565` | Moved from the cited file, live on both halves. `ReadToEnd()` precedes `WaitForExit(5000)`, and stderr is redirected but never drained — same hang from the opposite pipe. Retarget from `Exists` to `Retrieve`. |
| M01 | `DeepSyxx/main.swift:189-264` | **Widened, not fixed.** The entire restricted-DNS gate is gone — `isValidRestrictedDNSAddress` and `inet_pton` appear zero times in a 354-line file, and `setDNSServers` passes the caller's array straight to `networksetup`. `127.0.0.1`, previously rejected, now passes too. Residual protection is the XPC code-signature check only. Task is to *write* the gate. Orchestrator-verified. |
| M05 | `AppModel.swift:1382` and `:1981` | Two sites, not one. The `default:` branch is gone because the assignment was hoisted out of the switch entirely, so `.displayMismatch` and `.untrustedSigner` still render `.signed` — and now so do `.invalidSignature`, `.malformedInput`, `.unsupportedFormat`, which the finding recorded as correctly caught. `WitnessTestimonyState` has no state between `signed` and `failed`. |

## Absent-code — dispositions required

| ID | What is gone | Question for the owner |
|---|---|---|
| W05 | `UnanimousTamperingResult` enum and the tri-state entirely | Do **not** reintroduce. Its purpose is served by the reachable `dohBlockedWhileUdpUnanimous` path; fix W04's residual gate instead. |
| W07 | `CollectorPinning.cs` and the persisted-pin-file source | Feature absent, not bug fixed. Restore the file-based pin source, or accept env-var-only pinning? |
| W18 | `GetInstallationSalt()` and the whole install-salt mechanism | Closed as a side effect of W17's reversion. No action. |
| W19 | The C009 `fallbackToDefaultDomain` guard | **Removed, not extended.** Silent default-domain substitution is now live on *both* surfaces (`FirstRunScan.cs:57`). Worse: Avalonia (`ConclusionsViewModel.cs:66`) passes no domain at all, so Linux always scans `github.com` regardless of user input. Orchestrator-verified. Re-scope as "restore C009 and wire both surfaces". |
| M04 | Mirror degradation badge, diff panel, `buildTerminalData` | Closed by deletion. The nil path now renders an honest licence-gate message, no fabricated clean. Was the feature a requirement? |

## New defects found during triage — not in the original 36

| # | Location | Issue |
|---|---|---|
| N1 | `SyxxMatrixIntegration.cs:217-220` + `SynthesisEngine.cs:281-306` | **`clean` may be unreachable on Windows/Linux.** All 14 `IndicatorMapping` entries are threat-side; none of `clean`'s four supporting indicators (`ResolverAgreement`, `RouteClean`, `TrustedWitnessOK`, `LocalEvidenceSealValid`) has an `EvidenceId` producer. If it holds at runtime, coverage/fit/strength are 0, `clean` never reaches a tier, and every threat-free run falls to `Inconclusive`. Read-level inference, orchestrator-verified as far as reading allows; needs a run to confirm. **Do not fix by restoring the W01/W03 mappings** — that reinstates the false-clean defects. |
| N2 | `VantageComparer.swift:333` | Swift twin of W06, still live: `if udpStatuses.count > 1 { return .tampered }` over outcomes filtered only by `!didFail`. Benign RCODE variation graded as tampering. Sits three lines above M03's repair; no review run caught it. |
| N3 | `MatrixDeterminativeEngine.cs:1106` | Surviving instance of the `Reason != HardExcluded` filter that made W02's old guard fail. No longer reaches the report card, but the pattern is intact. |
| N4 | `SignedEvidencePayload.swift` | M12's repair writes the real TTL for v2/v3/v4 alike while verification *is* version-keyed. The unversioned byte-shift already happened; snapshots sealed by the `ttl: 0` build now fail verification. **Wave 5 inverts** — no longer "prevent a silent shift" but "a shift landed; bump to v5 and add `legacyZeroTTL` retroactively". |
| N5 | `DNSResolver.swift:26` | `EncryptedTransportProbe`'s cleartext control query defaults to `8.8.8.8` — a privacy tool sending the fallback control to Google rather than the user's own resolver. |

## Features removed alongside their defects — owner decisions, not triage calls

`--verbose` (W21), `SecretExistence` tri-state and `KeyPresence` (W09), E029 at-rest key wrapping (M08 — the Ed25519 key is now raw base64 under Keychain ACL only), C009 default-domain guard (W19), Mirror degradation UI (M04), persisted collector pins (W07).

## Quarantine audit (Task 0.6)

**43 files, 9,839 lines, none of it compiled** — `Tests/_quarantine` sits outside every declared SwiftPM target.

Two corrections to the brief: the directory holds 43 files, not 44; and `.phase32-active-dup` marks a second copy *inside* quarantine, not a live duplicate — 14 base names exist twice, 9 pairs byte-identical.

| Remediation class | Files | Lines |
|---|---|---|
| restore (likely compiles today) | 4 | 387 |
| migrate | 11 | 3,749 |
| duplicate-into-live | 5 | 1,201 |
| delete | 23 | 4,502 |

**The dark-coverage hypothesis is partially supported — for one defect class, not the others.**

- **Confirmed (Q1):** `SoWhatLayerTests` is the only artifact asserting that an `Inconclusive` determination is never graded as a letter, plus tray↔card agreement. `ReportCards.For` gates on three branches; the live suite covers only `.untested`. Two of three branches have no test behind them.
- **Not supported (Q2, Q3):** the contradicting-indicator and quality-floor mechanics postdate every quarantined file and are covered live.
- **New gaps, not regressions (Q4, Q5):** unmappable-rule denominator composition and non-usable-section coverage arithmetic are asserted **nowhere** — neither quarantined nor live. Write these fresh.

Two committed corpora are unread on macOS: `corpus/matrix/default-spec.frozen.json` (the only mechanical check of ~100 hand-transcribed weights against the shared cross-platform oracle — its test fails on one type error) and `corpus/stage0/seeds.json` (carries the never-false-secure invariant plus `XCTExpectFailure` ratchets on known false-clean seeds).

## Consequences for the plan

1. Wave 1 loses W01, W02, W03, M02, M09, M10 — all repaired. It gains W20 and N1. **W20 is now the wave.**
2. Wave 2 loses W06, W11, M03, M13, M14 — all repaired. It gains W04's residual gate and N2.
3. Wave 3 loses W09, W17, W18, M06, M08 — repaired or absent. Retains W13 at its new location.
4. Wave 4 loses W08, W10, W14, W15, M07, M11 — repaired. Retains W12 (regressed) and M01 (widened). Both are security gates and both are worse than reported.
5. Wave 5 inverts per N4.
6. Wave 6 loses W21, M15 — repaired. Retains M05 (two sites) and W19 re-scoped as "restore C009 and wire both surfaces".

## Method note

Two specialists classified in parallel under read-only scope with no network and no builds; 33 of 50 and 33 of 50 tool calls used; the audit used 40 of 45. The orchestrator independently verified the highest-consequence claims in code: M01's missing gate, M05's two sites, M04's absence, M12's TTL, W12's three nonce-less call sites, W19's Avalonia caller, and N1's mapping table. No agent wrote, moved, or deleted any file. `rules/` untouched throughout.

Unproven and flagged as such: N1's end-to-end runtime effect, and W20's real-world reachability (the missing guard is certain; how often a non-usable section emits surviving evidence is not).
