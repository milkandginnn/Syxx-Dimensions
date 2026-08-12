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
| N1 | `SyxxMatrixIntegration.cs:217-220` + `SynthesisEngine.cs:281-306` | **`clean` may be unreachable on Windows/Linux.** All 14 `IndicatorMapping` entries are threat-side; none of `clean`'s four supporting indicators (`ResolverAgreement`, `RouteClean`, `TrustedWitnessOK`, `LocalEvidenceSealValid`) has an `EvidenceId` producer. If it holds at runtime, coverage/fit/strength are 0, `clean` never reaches a tier, and every threat-free run falls to `Inconclusive`. Read-level inference, orchestrator-verified as far as reading allows; needs a run to confirm. **Do not fix by restoring the W01/W03 mappings** — that reinstates the false-clean defects. **RESOLVED 2026-08-12: not a defect.** Confirmed unreachable end-to-end by test, but `SynthesisEngine.cs:200-204` documents it as deliberate — clean-support indicators are intentionally unwired, "inconclusive over unearned secure". Also established: porting Swift's `mappableIndicators` would make it strictly worse, since that set derives from the threat-side mapping, so all four clean rules become unmappable and `totalSupportWeight` collapses to 0. |
| N2 | `VantageComparer.swift:333` | Swift twin of W06, still live: `if udpStatuses.count > 1 { return .tampered }` over outcomes filtered only by `!didFail`. Benign RCODE variation graded as tampering. Sits three lines above M03's repair; no review run caught it. |
| N3 | `MatrixDeterminativeEngine.cs:1106` | Surviving instance of the `Reason != HardExcluded` filter that made W02's old guard fail. No longer reaches the report card, but the pattern is intact. |
| N4 | `SignedEvidencePayload.swift` | M12's repair writes the real TTL for v2/v3/v4 alike while verification *is* version-keyed. The unversioned byte-shift already happened; snapshots sealed by the `ttl: 0` build now fail verification. **Wave 5 inverts** — no longer "prevent a silent shift" but "a shift landed; bump to v5 and add `legacyZeroTTL` retroactively". |
| N6 | `MatrixDeterminativeEngine.cs:984-991` | Exclude-polarity loop has no usability guard; the Swift twin does at `:823`. An indicator from a non-usable section can still add contradiction mass. Fail-closed direction (suppresses rather than promotes), so lower urgency — but a genuine parity gap. |
| N7 | `MatrixDeterminativeEngine.swift:785` | Inferred live Swift defect: the guard `continue`s before `observedSupport` accumulates, which would delete the observation from the overhang-admission measure. `MatrixUntestedStateTests.swift:69-88` asserts the opposite, so that test may be red at HEAD. Under verification by the Swift implementer. |
| N8 | `VantageComparer.swift:292-293` | W04's Swift twin kept the narrow `allDohFailed` form after the C# fix landed, so the two runtimes diverge on the same corroboration decision and W04 ships half-fixed. Routed to the Swift implementer in Wave 2. |
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

---

# Swift suite investigation — 39 failing names (2026-08-12)

The macOS suite had not compiled at HEAD; `ad7ed1d8` unblocked it and its first real run showed 39 distinct failing names. Three debuggers were dispatched. Findings below are per-cluster.

## App-layer cluster (13 names) — reported

**10 of 13 are ONE environmental cause, not ten defects.** `-34018` is `errSecMissingEntitlement`. `WitnessKeyManager` targets the data-protection keychain (`kSecUseDataProtectionKeychain: true`), which requires a code-signed caller with a keychain-access-group entitlement. A `swift test` xctest bundle is unsigned, so every keychain WRITE fails at `WitnessKeyManager.swift:204`.

Nine tests fail on their first statement — `try WitnessKeyManager.generateAndStoreKey()` — before touching any product logic. The tenth (`testRunWitnessThreadsRealErrorNotGenericString`) plants a raw key instead but still fails, because `hasKey` reads the same keychain.

**The control group proves it:** the three tests in these same classes that need no successful keychain write are exactly the three that pass. The partition is exact.

**Consequence: the Witness signing path has no test signal on this host.** Nine tests never execute a line of witness logic. Every Witness regression claim from this suite is unproven rather than passing.

**Independent LIVE-DEFECT found in the test scaffolding.** `WitnessKeychainTestSupport.baseQuery` (`macOS/Tests/SyxxAppTests/WitnessKeychainTestSupport.swift:33-40`) omits `kSecUseDataProtectionKeychain`, so it addresses a *different* keychain from `WitnessKeyManager`. On a properly-signed host, `tearDown`'s `restore()` writes the backed-up key to the wrong keychain and `deleteKey()` deletes from the wrong one — a developer's real signing key would be silently replaced and never restored. That is precisely the harm the file's own header claims to prevent. Fix is one line; do it regardless of the entitlement work.

**Remaining 3 are STALE tests, not defects.** Two `TypographyResourcesTests` and one `DimensionsShellLayoutTests` pin contracts that dated in-source owner directives (2026-08-02) deliberately superseded — `cardTitle` role added, body 15.5 → 17.5, sidebar wordmark restyled. The implementation is the approved state; the tests drifted. **This keeps them out of Tier 3 UI governance** — that would apply only if someone proposed reverting the shell to satisfy the test.

## M05 — worse than the triage entry states

`witnessTestify()` (`AppModel.swift:1981`) does not ignore a self-verification result — **there isn't one.** `WitnessTestifyPlanner.seal` signs and computes drift but never self-verifies. The fix on that path is larger than "consult the existing result": the verification must be added first.

No test would have caught M05. The nine-case distinction does survive on `witnessResult` via `witnessVerify()`, so M05 is a collapse of the testimony badge, not of the app's whole vocabulary.

Two tests will pin the defect once trust keys are configured: `testRunWitnessSuccessPathSetsRealSignedState` and the guard in `testFreeAfterSuccessfulVerifyClearsSnapshot…` hard-require `.signed`, which is correct today only because an empty configured trust set skips the trust check. Both need revisiting in the M05 wave.

Widening `WitnessTestimonyState` is a **three-platform change** — macOS, `Windows/Syxx.Tests`, `Ubuntu/SyxxUI.Avalonia.Tests` — plus the shared `corpus/witness-testify-parity/witness-testify-parity.json`, because the parity fixture encodes the same collapsed state space.

## Unverified prediction to check cheaply

`LicenseManager.swift` is the only other file using `kSecUseDataProtectionKeychain`. If any of the remaining failing names exercise licence keychain writes, they share this root cause and must not be triaged as separate defects.

## Comparator cluster (15 names) — reported

**All 15 are STALE-TEST across 7 root causes. No live defects, none undecided.** Analysis was post-Wave-2, so N2 and N8/W04 are already accounted for.

Eleven of the fifteen have been red since the initial commit, hidden by the non-compiling suite. They are not this cycle's regressions.

| Cause | Tests | What moved |
|---|---|---|
| 1 — M03 tri-state + W04 floor | 4 | `.undetermined` now maps to inconclusive, never redirection |
| 2 — RFC 5737 glue vs D018 filter | 5 | **One fixture line.** Mock glue resolves to `203.0.113.1/.2`, which `IPClassifier` classifies `.documentation`; D018 keeps only `.publicRoutable`, so `nsIPs` is always empty and every `compare()` short-circuits |
| 3 — C015 retained divergence | 1 | Same-provider no longer downgrades `.diverges` → inconclusive |
| 4 — B079 disjoint rotation (M14) | 1 | CDN edge rotation is inconclusive, not unstable |
| 5 — C067 ECS silence (M13) | 1 | No echoed ECS option is inconclusive; only scope-0 is `.notForwarded` |
| 6 — B076/B077 TTL guards | 2 | Uniform TTL populations are inconclusive, not `.independentCaching` |
| 7 — D100 permutation harness | 1 | Harness measures the wrong property; B098 binds each resolver's own TTL by design |

**Three tests contradict their own name or doc comment** — a test named `...IsNotTamper` asserts tamper; another's doc says "MUST be inconclusive" while its assertion says otherwise. That is the decisive evidence they are stale.

### Do not "fix" these in the product

- **Cause 1** is the never-false-clean family. Flipping `redirection` to false without also pinning `inconclusive == true` converts each into a false-clean assertion — exactly what M03/W04 closed.
- **Cause 7 is the most dangerous of the 15.** Satisfying its byte-identity assertion in the product — dropping TTLs from `perResolverAnswers` or sorting records across resolvers — would delete answer-ownership binding and let an attacker swap answers between resolvers under a valid signature. Anyone reading this failure as "the payload is nondeterministic" will reach for precisely that.
- **Causes 3, 4, 5** each revert to an unearned clean or a CDN false positive if repaired product-side.

### Coverage that must be added alongside

Cause 2's fixture change removes the only (accidental) coverage of the D018 glue filter — add a case asserting documentation/private glue yields inconclusive, or deleting D018 goes unnoticed. Cause 4 leaves `AnswerStability.Status.unstable` unreachable from `classify`; give it a reachable definition or retire it.

### N9 — four more Swift-only honesty repairs C# never received

Found while verifying. In each, Windows returns the **more confident** conclusion on evidence macOS now declines to decide:

| Repair | C# site | C# behavior |
|---|---|---|
| C067 | `ECSProbe.cs:42` | returns `NotForwarded` for both scope-0 AND no-option |
| B076/B077 | `TTLAnomalyProbe.cs:45-61` | neither guard; uniform populations fall through to `IndependentCaching` |
| B079 | `AnswerStabilityProbe.cs:83` | still returns `Unstable` for disjoint rotation |
| C015 | `AuthoritativeVantage.cs:130-132` | still downgrades `Diverges` → inconclusive on shared provider |

Landing the 15 test corrections makes the Swift suite green and leaves this gap guarded by nothing.

**Unverified C# lead:** `Windows/dotNET/Syxx.Tests/SynthesisEngineTests.cs:35` uses the same `203.0.113.1/.2` glue against the identical filter at `IPClassifier.cs:67` — likely the same latent fixture fault as cause 2. Cheap to check.

## Running divergence count

Nine cross-runtime divergences are now known: N2, N6, N7, N8, B157, and N9's four. Every one is the same shape — one side repaired, the other not. This is the failure mode that produced the original 36, and it is still producing new ones.
