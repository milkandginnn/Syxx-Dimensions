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

## Build/CI advisory — measured, not assumed

**Meta-finding that outranks the rest:** three good local gates already exist (`scripts/cross-platform-parity-gate.py`, `honesty-coverage-gate.py`, `package-pin-parity.py`), all no-SDK and deterministic. They are invoked from `.github/workflows/ci.yml` **and nowhere else**, and the `Makefile` has zero targets for Swift, C#, or Server. With CI off, the problem is not a missing gate — it is that there is **no local gate runner at all**.

### N10 — the ProbeBudget shared-cause hypothesis is refuted; Swift's product drifted

Two independent drifts, not one shared fixture cause:

| Section | Swift impl | Swift test expects | C# impl | C# test expects |
|---|---|---|---|---|
| Prophet | 45 / 5.0 / 15 | 30 / 2.5 / 15 | 30 / 2.5 / 15 | 30 / **5** / 15 |
| Mirror | 50 / 6.0 / 10 | 30 / 2.5 / 10 | 30 / 2.5 / 10 | 30 / **5** / 10 |
| Portal | 30 / 5.0 / 5 | 15 / 2.5 / 5 | 15 / 2.5 / 5 | 15 / **5** / 5 |
| Glutton | 25 / 5.0 / 5 | 20 / 2.5 / 5 | 20 / 2.5 / 5 | 20 / **5** / 5 |

C# is pure test hygiene — one stale field. **Swift is a product defect: the implementation drifted and the test is correct.** macOS Prophet allows 45 s total / 5 s per probe where Windows allows 30 s / 2.5 s, so identical slow networks produce different undetermined rates per platform. Owner picks which set is canon. Divergence count: ten.

### Corpus census — two fixtures have zero readers anywhere

`matrix/default-spec.frozen.json` is read only by Windows (Swift twin quarantined). **`stage0/seeds.json` has no live reader on any platform.** **`probe-parity/probe-parity.json` has none either** — its generator's own header claims two readers, `ProbeParityReplayTests.cs` and `.swift`, and **neither file exists**.

### Quarantine — deception surface is smaller than reported

43 files / 9,839 lines is right, but only **19 files / 3,523 lines are named plainly `*.swift`**. The other 24 already carry a visibly-dead suffix. Renaming those 19 finishes a convention the repo already invented and makes the invariant mechanically checkable.

### Keychain — both signing routes are blocked here, measured

- Data-protection keychain: `SecItemAdd` → **-34018**. Legacy keychain, same query minus the flag: **0**. So the generic framing "unsigned bundles can't use the keychain" is false and would send the fix the wrong way.
- `security find-identity -v -p codesigning` → **0 valid identities**.
- Ad-hoc signing with a `keychain-access-groups` entitlement → **SIGKILL at exec**. It is a restricted entitlement.

Both routes need an Apple Developer identity first. Not a same-week option.

**The seam does not buy keychain coverage.** With a seam, `keychainQuery`/`writeKeychainData`/the data-protection flag execute in no test on any host — exactly the class `honesty-coverage-gate.py` exists to refuse. What the seam *does* buy is deleting the backup/restore dance entirely, so the defect class that produced the helper bug stops being representable. That is the honest argument for it; test coverage is not.

### Recommended local gates, ranked by signal ÷ effort

| # | Action | Effort |
|---|---|---|
| 1 | Fix `WitnessKeychainTestSupport.baseQuery` — **in progress** | 15 min |
| 2 | `scripts/local-gate.sh` called from the existing pre-commit hook | 45 min |
| 3 | `swift build --package-path macOS --build-tests` inside it | +0 |
| 4 | Rename the 19 plain `.swift` quarantine files | 10 min |
| 5 | Keychain probe + `XCTSkip`, incl. probe-guarded tearDown — **in progress** | 20 min |
| 6 | `scripts/dead-test-gate.py`, manifest-derived | 45 min |
| 7 | `KNOWN-FAILURES.txt` two-way ratchet | 1 h |
| 8 | `corpus/probe-budget/defaults.json` + rewire both suites | 1.5 h |
| 9 | `scripts/corpus-binding-gate.py` | 2 h |
| 10 | Witness protocol seam — owner-gated | 3-4 h |
| 11 | Signing identity — external, $99/yr | — |

`swift build --build-tests` is the load-bearing choice for the gate: it type-checks the test target without running a test, so it needs no keychain and no network, and it can be **green today** while everything else is still red. A gate whose success condition is unreachable gets bypassed within a day.

Each gate must be proven by falsification — and #9 must be **RED on first run**, because two fixtures already have zero readers. A green first run means it measures nothing.

## Code review of the four committed waves

**No blockers on three. `9a7efa68` would NOT have been approved as it stands** — one unstated fail-open filter change outside its declared scope, and one inline correctness claim the code contradicts.

### N11 [high] — forged packets can vote in UDP unanimity (regression I committed)

`macOS/Sources/SyxxKit/VantageComparer.swift` `detectUnanimousTampering`. The N2 repair silently dropped `!$0.didFail` alongside the intended RCODE-set removal. The comparer's `didFail` (`:24-28`) covers `.timeout, .transportError, .malformedResponse, .responseIdMismatch, .truncated` — a *different* set from `DNSResolver.swift:265-269`. So that filter was never what excluded SERVFAIL/REFUSED; dropping it only admitted outcomes that FAILED yet still carry records.

**`.responseIdMismatch` is the signature of an off-path spoofer.** It can now define or break UDP unanimity — either masking a real tamper conclusion or supplying the unanimous answer itself. Fix in progress: `filter { !$0.didFail && $0.hasRecords }`, which keeps N2 fixed because SERVFAIL/REFUSED carry no records regardless.

### N12 [high] — hard-exclusion scan is fail-open, and arms itself when N1 is resolved

`Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:1002-1010` sets `HardExclusion` with no usability guard — the same omission W20 fixed 60 lines above, but this path is fail-**open**. A hard-excluded diagnosis is admitted to `Suppressed` as `HardExcluded`, and `:1136` filters `HardExcluded` **out** of the overhang, so a threat excluded by an indicator from an `Invalidated`/`Untested` section vanishes and permits `Clean`.

**Latent but coupled to N1:** shipping threat diagnoses exclude on `TrustedWitnessOK`, one of the four unwired clean-support indicators. **The change that makes `Clean` reachable is the same change that arms this hole.**

This also corrects triage N3, which recorded the `Reason != HardExcluded` pattern as no longer reaching the report card. The instance at `:1136` is live and decides `Clean` vs `Inconclusive`.

### N13 [medium] — "strictly fail-closed" is overbroad, in both runtimes now

The W20 comment's invariant holds for non-contradictory observations only. For contradictory ones the guard removes a term from both numerator and denominator of `Contradiction`, generally *lowering* measured contradiction and making tiering easier. Defensible and matches Swift, but the comment states an invariant the code does not hold, and `9a7efa68` copied that comment into Swift. Fixture D covers only the non-contradictory ghost.

### Lower-severity review findings

`corpus/dnssec/validity-span.json` claims Swift conformance nothing enforces (Swift can be retyped with nothing going red). `[CallerFilePath]` bakes an absolute build-machine path into the C# corpus test. `SingleDoHDisagreementIsNotTamper` is now fully subsumed by the inverted test. `SuppressedDiagnosis.RawSupport` carries `ObservedSupport`, not `RawScore.RawSupport` — a name collision sitting exactly where confusing the two reopens the false-clean hole. The W04 guard is opt-in on `dohProvided`. `@unchecked Sendable`'s stated justification is not the real one: the forcing property is `[String: Any]`, whose `JSONSerialization` contents are Foundation reference types.

### Confirmed on the record

The W20 divergence is sound — `ObservedSupport` is bit-identical before and after, so no new suppression path was introduced. The W04 test inversion was legitimate: the old fixture produced a fully clean result on a run whose only encrypted witness contradicted every UDP resolver. The W16 binding is real, not vacuous — both suites hardcode the value *in addition to* reading the fixture, closing the usual drift loophole. `CleanDiagnosisReachabilityTests` pins a documented contract and fails in the right direction.

**One consequence worth stating to the owner:** on a network where 2 of 3 DoH endpoints are blocked — common, and precisely this product's target environment — every Mirror run is now undetermined. Intended posture, correctly argued, but it changes the modal user-visible outcome on hostile networks.

## Matrix cluster (11 names) — reported

Eight root causes. **Only one is a live signal against Swift product code, and four are a live signal against C#.**

| Test | Class |
|---|---|
| `MatrixAllNegativeParity…_isClean` | **LIVE-DEFECT** — `.clean` unreachable |
| `testSingleDecisiveThreat_…` | STALE (B156 raised coverage 0.561 → 1.000) |
| `testC063_…differsInStrengthAndTier` | STALE at `:41` + **POLICY COLLISION** at `:45` |
| `testHardExclusionDominates_…` | STALE — assertion unsatisfiable by construction |
| `testContradictionBounded_…` | STALE — F045 vs the C062 overhang |
| `ProbeRunner.test_default{Prophet,Mirror,Portal,Glutton}` ×4 | STALE on Swift; **LIVE-DEFECT on the C# twins** |
| `test_run_timeout_accountsCorrectly` | STALE — C058 added a 1.5 s grace drain |
| `MonitorScheduler.testStartRunsImmediatelyThenStopHalts` | **FLAKY by construction** — product correct |

### N14 — `.clean` is unreachable in the shipped Swift spec

**B156 defeating its own purpose.** It exists so unmappable rules cannot hard-cap coverage; when *all* of a diagnosis's support rules are unmappable, it makes the diagnosis unreachable outright. None of `clean`'s four support rules is in `indicatorMapping`, so `totalSupportWeight` is 0 for every possible bundle → coverage 0 → `DeterminativeState.clean` cannot be produced by the shipped engine **under any input**, and the clean branch at `:986` is dead code.

Distinct from N1: N1 is a deliberate decision leaving the *pipeline* silent but the engine capable. B156 additionally removes engine capability — a side effect, not the recorded decision. Recommended fix: build the denominator from `mappable ∪ {indicators actually present in the bundle}`.

### N15 — the clean-blocking overhang is INVERTED between runtimes

| | surfaced `suppressed` | clean-blocking overhang |
|---|---|---|
| Swift | **excludes** hardExcluded | **includes** it (B163) |
| C# | **includes** it | **excludes** it |

Both decisions are inverted. Swift's pair is the honest one. C#'s means a single degraded or unusable-section exclusion both suppresses the threat **and** removes it from the clean gate — a false-`Clean` path over a threat that fired with real observed support. Compounds N12.

### N16 — ProbeBudget C058 diagonal half-port

Not a shared fixture and not environmental — both cross-cutting leads were checked against this cluster and neither applies. `maxProbes` is identical in all four artifacts; only the two timeout fields moved, and the runtimes fail on *different* fields. **Three different budget tables are in play.** Swift tests are stale; the C# tests are a live signal against C# product, which never received C058.

### N17 — three further C#/Swift divergences in the same function

`ExclusionQualityFloor` + usability guard present in Swift, absent in C# (fail-open: a quality-0.01 observation silently zeroes a threat on Windows). B166's `break`-outside-the-flag exits the required-section loop early when hard-gating is off. B168 guards `clean` on all three overhang admissions in Swift, only one in C#.

### C063 vs B157 — genuine policy collision, owner call

Not stale, not live. Two policies conflict and the code cannot satisfy both. Two objections to B157 as written regardless of which wins: it fires for any `quality > 0`, so 1% completeness reads as 62%; and the floor sits *exactly on* `minimumFitForProbable`, making the outcome rounding-dependent and the quality axis inert whenever alignment is 1.0. Same bundle yields `ThreatDetected` on Swift and undetermined on C#.

## Running divergence count: seventeen

N2, N6, N7, N8, N9 (×4), N10, N11, N12, N13, N14, N15, N16, N17 (×3), B157. Every one is the same shape.

## Parity research — two premises corrected

**1. The directional pattern does not exist, and acting on it would produce the wrong fix.** All four comparator findings verified, but **C015 runs the other way**: Swift *retains* `.diverges` on a shared provider ("a /13 CDN block spans millions of IPs — an attacker in the same provider's space is not evidence of benign geo-routing") while C# downgrades to undetermined. Here macOS is the confident side.

The invariant that actually holds across 13 of 14 cases: **a repair landed on the Swift tree and its C# twin was never updated.** The finding IDs are one sequential review series — B076, B079, B156, B157, B162, B163, B167, B168, C015, C060, C061, C067, C117 — executed against Swift; the C# twins were point-in-time ports that stopped tracking. Most of that series is honesty work, which is why the aggregate *looks* like a platform temperament. **Port the series; do not apply a confidence-bias correction.**

**2. Three runtimes, not four.** `Linux/dotNET/Syxx.Linux/Syxx.Linux.csproj` references `Syxx.Kit.csproj` directly — Linux ships the *same assembly* as Windows. Engine divergence is impossible there; only `Linux/Ubuntu/SyxxUI.Avalonia/` and `Program.cs` can differ. This halves the reader matrix to Swift / C# / Python.

**3. My own Wave 1 comment is now stale.** `MatrixDeterminativeEngine.cs:941-954` still says Swift `continue`s before `observedSupport` accumulates. `9a7efa68` closed that; Swift accumulates at `:804`. The comment describes a divergence that no longer exists and would mislead the next reviewer into restoring it. Fix in progress.

### The mechanism recommendation, with its empirical basis

**`corpus/b281-consensus/decision-table.json` is the only decision surface read by both a Swift and a C# test, and the only decision surface with no divergence.** Every corpus with one reader or none sits on top of a divergent pair. The mechanism is already proven in this repo; what failed is coverage and liveness, not design.

Four committed oracles enforce nothing: `probe-parity.json` (schema string "VantageComparer.evaluate tri-state replay" — exactly the N2/N8 surface) is **written by a generator and read by nobody**; `corpus/matrix/` is C#-only; `validity-span.json` is Python+C# only; `stage0/seeds.json` is read only from `_quarantine/` and `archive/`.

Recommended: give the four orphaned corpora their missing readers (~3 test files), add corpus-liveness as a third assertion in the **existing** `scripts/cross-platform-parity-gate.py` (~80 lines), then author decision-table corpora for the divergent surfaces.

**Critical constraint on that last step:** author corpora from *intended semantics*, never captured from either implementation's current output. A corpus generated by running today's Swift and today's C# would freeze P1–P14 into the oracle and certify the divergence at expense.

Why the existing gate missed all of this: it checks OTel span names, wire enum vocabularies, and JSON wire keys — and **not one of the fourteen findings changed a wire string**. It is also invoked from exactly one place, `ci.yml:427`, with no Makefile target and no hook, so with CI off it has not run at all.

### New divergences beyond the seventeen already recorded

| ID | Consequence |
|---|---|
| P1 | **CNAME-delegated zones grade as tampering on Windows.** Swift returns undetermined ("address ground truth not obtainable"); C# maps an empty authoritative set to `Diverges` — "likely injected answer". Reachability proven: two CNAME-only responders each append an empty set, clearing the quorum gate. Any CNAME-fronted name. |
| P6 | **C117 address-record filtering applied on one side only.** Swift filters to A/AAAA at eight sites; C# at one. With CNAME or RRSIG rdata alongside addresses — universal under DNSSEC — C# per-resolver sets differ even when the A records agree, `udpUnanimous` goes false, and B010 unanimous-tamper detection silently disables. Fail-open. N2's exact repair pattern, applied to one runtime. |
| P7 | **C# lost the tri-state entirely.** Swift returns `.undetermined` when `dohOutcomes` is nil; C# returns plain `false` and `isInconclusive` omits the term. W05's disposition holds only when DoH was *provided*. UDP unanimous + local matching + no DoH: macOS undetermined, Windows clean. |
| P8 | Authoritative-responder split: Swift returns undetermined ("the split is itself a tampering signal"); C# takes `MaxBy(count) ?? authSets[0]` and silently picks a side. |
| P9 | `fec0::/10` → `Reserved` on C#, `.publicRoutable` on Swift. |

**Checked and clean:** all threshold values, 35 rule weights per side, `ExclusionFiringThreshold`, `OverhangFloor`, `Tier()` predicates, `round6` banker's rounding, `majorityConsensus`. **The numbers are in parity; the control flow is not.** Every divergence is a branch one side gained and the other never did.

## Swift language advisory

**`@unchecked Sendable` is the wrong idiom and the justifying comment is factually wrong on both claims.** `let` does not confer `Sendable`, and `JSONSerialization` returns bridged Objective-C *reference* types. Decisive: **neither `@Sendable` closure touches `expected`** — the capture is wrong, not the type. Fix in progress.

That fix also closes a silent-skip: `if let m = c.expected["manifestState"] as? String` **vanishes the assertion** if the corpus key is renamed or retyped — the same green-that-lies family as the known brotli skip.

**`Sources/` is clean — a positive finding.** Every target is Swift 6 language mode with no per-target downgrade, and SwiftPM builds `SyxxKit` before its tests, so the compile failure proves `SyxxKit` compiled. The exact diagnostic class was hunted across the corpus and no second instance exists. A clean search reported as clean.

### N18 — mixed test harness plus unsynchronized static mutable state

`TriStateTests.swift` is **swift-testing** (`import Testing`); the other 58 files are XCTest, all in one target. **swift-testing runs `@Test` functions in parallel by default**, and `.serialized` appears nowhere. Meanwhile four `nonisolated(unsafe) private static var` hold mutable collections with no serializer:

`ResolverConsensusStore.swift:44` · `DNSCapture.swift:720` · `LicenseManager.swift:103` · `SyxxTracer.swift:22`

`nonisolated(unsafe)` is an author's assertion of external serialization, not a fix. Unlike the `@unchecked Sendable` classes, which own a `DispatchQueue`, these own nothing. **One test away from real concurrent access — and unsynchronized `Dictionary`/`Set` mutation corrupts the CoW buffer and crashes the process**, it does not merely return a wrong value.

Framing for the record: **the compiler became more correct, not the code regressed.** The `[String: Any]` was always non-Sendable and always captured; earlier frontends missed it. There is no toolchain pin — no `.swift-version` anywhere — so any `swift` that moves on PATH can reopen this class mid-track.

### N19 — two cross-runtime numeric traps that make verbatim porting unsafe

**NaN asymmetry.** `Swift.min(1.0, .nan)` → **1.0**; `Math.Min(1.0, double.NaN)` → **NaN**, which propagates through `Math.Pow` and poisons `Fit`/`Contradiction`. Currently unreachable — both engines validate inputs finite in [0,1] — but guarded solely by that validation running on every path.

**C#'s `Clamp01` was deliberately written as an explicit ternary rather than `Math.Clamp`, specifically to match Swift's NaN behavior.** Do not let anyone "simplify" it — that silently reintroduces a divergence.

**`pow` is the irreducible one.** Swift's is platform libm; C#'s is the CoreCLR intrinsic. Neither C99 nor IEEE-754 requires correctly-rounded transcendentals, so they may disagree in the last ulp — and the result feeds an **exact `>=` against 0.62**. One ulp at a tier boundary flips an emitted diagnosis between twins. Any parity corpus needs an epsilon or must avoid threshold-straddling fixtures. `RoundingParityTests.swift` sits in `_quarantine` and is very likely the test written to pin exactly this.

**Quarantine restore is cheaper than feared.** For all four restore-class candidates the `.phase32-active-dup` copy is the *newer, already-adapted* one. Every symbol resolves against current `Sources/`; two types grew but their new fields carry defaults. API-drift burden: effectively zero. Strict-concurrency burden: zero. ~387 lines, a rename plus deleting the stale twin.

## Divergence count: twenty-four

Seventeen previously recorded, plus P1, P6, P7, P8, P9, N18, N19.

## N20 — two sibling instances of the forged-outcome defect remain

Same missing `!$0.didFail` filter, found while fixing N11. Neither fixed.

**`VantageComparer.swift:149` `udpBaselines` — LIVE FALSE-NEGATIVE, worse than the one repaired.** A forged record-carrying outcome becomes a *baseline* and enters `publicUnion`, `resolverOverlapCount`, and the B058/B059 upgrade. An attacker who redirects local **and** spoofs one public resolver to echo the same address puts that address into `publicUnion`, so `localRdatas` is no longer disjoint and the B281 redirection branch is skipped entirely. The upgrade recovers it only when ≥2 honest resolvers agree on a corroborated alternative; in the CDN-divergent case the run lands on degraded instead. **One forged packet downgrades a real redirection.** C# has the same shape.

**`VantageComparer.swift:320` `udpSetsForCheck` — latent.** A forged outcome can break the unanimity the W04 residual guard keys on. No post-fix input was found where this alone yields a false clean, so it is called latent rather than proven. C# twin at `VantageComparer.cs:358-361`.

**Correction to the record:** 9a7efa68 claimed "parity with `VantageComparer.cs:386-394`". That was parity with a C# defect — C# filters on `Records.Count > 0` only and its `DidFail` covers the same five statuses, so it admits the same forged outcomes. The claim copied a bug into Swift rather than fixing anything. Swift is now correct; C# needs the same repair on all three sites.
