# Linux parity — remaining work: implementation plan

- **Date:** 2026-08-05
- **Supersedes:** nothing; complements `PLAN-linux-parity-team-2026-08-04 - saved.md`
- **Produced by:** 4-seat review team (accuracy, security, testing, completeness)
- **Goal:** land the uncommitted build pass, fix two medium-severity security findings,
  close the remaining open items within scope, and create a backlog for reserved items.

---

## 0. Review verdict on the existing build pass

### Accuracy: NO FALSE CLAIMS

Every plan claim about landed code is substantiated by the working tree. G-01 through G-05,
the decoy staging, and the Swift twin edit are all present and correct. Line-number
discrepancies are all attributable to the plan referencing pre-fix line numbers.

### Security: 2 medium findings, 10 low/informational

| # | Severity | Finding | File |
|---|----------|---------|------|
| S-1 | **Medium** | `PassphraseAvailable()` calls `TryGetPassphrase()` twice per write — TOCTOU gap | `LinuxSecretStore.cs:131-138,107-118` |
| S-2 | **Medium** | Cached derived key returned by reference across lock boundary — concurrent `Clear()` can zero it mid-encrypt | `LinuxSecretStore.cs:118` |
| S-3 | **Medium** | `SYXX_WITNESS_PASSPHRASE` env var readable via `/proc/<pid>/environ` (same threat as argv) | interface doc |
| S-4 | Low | StringBuilder `material` holding machine IDs never zeroed | `LinuxSecretStore.cs:443-523` |
| S-5 | Low | Idle timeout checked only on access, no background eviction | `LinuxSecretStore.cs:85-89` |
| S-6 | Low | V1 legacy read path re-derives weak key on every read | `LinuxSecretStore.cs:741-780` |

Positive findings (no change needed): G-16 fix structurally correct; `secret_password_free`
binding correct; G-01 verify-after-store correct; `WriteFilePrivately` atomic 0600 writes
correct; `EnsureStoreDirectory` 0700 correct; HKDF null salt acceptable.

### Testing: 22+13+7 new tests; 7 gaps remain; 2 untracked files at risk

**Tests created:** 22 in `WitnessKeyStoreLoadResultTests.cs` (all three presence states,
all guard sites, orphan cleanup); 13 in `WitnessKeypairGuardTests.cs` (C-10/C-11); 7 in
`AppModelWitnessKeyPresenceTests.swift` (M-01); 3 in `FirstRunScanTests.cs` (G-03);
7+ in `AppViewModelWitnessTests.cs` (external verify + save gate); 3 in
`WitnessViewModelTests.cs` (ImportKey + SignAsync).

**New gaps discovered:**

- T-1: Two new test files are **untracked** in git — 20 tests at risk of loss
- T-2: `AppViewModelWitnessTests.cs` carries a notice it was never compiled or run
- T-3: Linux `ImportKey` tests still touch the real key path (live hazard partially present)
- T-4: No automated mutation-testing gate — guard coverage is a point-in-time claim

**Known unfixed gaps (from original plan):**

- T-5: `SelfVerify` has zero tests on Linux
- T-6: External-then-self contamination (G-15) untested on all platforms
- T-7: No severity-tier test on Windows or macOS

---

## 1. Phase 0 — Immediate (do before anything else)

**These are all zero-risk housekeeping that protect the existing work.**

### 1.1 Commit the build pass

All G-01 through G-05 work is uncommitted in the working tree. This is the highest-priority
action — a `git checkout .`, stash conflict, or another stream's force-push could destroy it.

```
# Stage only the parity-relevant files (not the mixed-stream noise)
git add Windows/dotNET/Syxx.Kit/WitnessKeyStore.cs
git add Windows/dotNET/Syxx.Kit/LinuxSecretStore.cs
git add Windows/dotNET/Syxx.Kit/PlatformSecretStore.cs
git add Windows/dotNET/Syxx.Kit/Synthesis/Synthesizer.cs
git add Windows/dotNET/Syxx.Kit/Synthesis/FirstRunScan.cs
git add macOS/Sources/SyxxKit/Synthesis/FirstRunScan.swift
git add scripts/package-pin-parity.py
git add Windows/Syxx.sln
git add .github/workflows/ci.yml
git add Windows/dotNET/Syxx.Tests/WitnessKeyStoreLoadResultTests.cs
git add Windows/dotNET/Syxx.Tests/WitnessKeypairGuardTests.cs
git add Windows/dotNET/Syxx.Tests/FirstRunScanTests.cs
git add Windows/dotNET/SyxxUI.Tests/AppViewModelWitnessTests.cs
git add Linux/Ubuntu/SyxxUI.Avalonia.Tests/WitnessViewModelTests.cs
git add macOS/Tests/SyxxAppTests/AppModelWitnessTests.swift
git add macOS/Tests/SyxxAppTests/AppModelWitnessKeyPresenceTests.swift
git add Linux/dotNET/Syxx.Linux/    # the staged deletion
```

**Gate:** `dotnet build Windows/Syxx.Linux.slnf -c Release` green;
`dotnet test Windows/dotNET/Syxx.Tests/Syxx.Tests.csproj -c Release` green.

### 1.2 Create `Linux/PARITY-LEDGER.md`

The plan's §4.5 specifies this as the durable artifact answering "is Linux at parity."
First entries: the §0 findings, G-01 through G-05 results from §7.0, and this review's
security findings S-1 through S-3.

### 1.3 Create `Linux/dotNET/README.md`

The plan's Phase 1 specifies this: state that the Linux CLI is
`Windows/dotNET/Syxx/Syxx.csproj`, built via `Windows/Syxx.Linux.slnf`, and why the decoy
was removed.

---

## 2. Phase 1 — Security fixes (S-1, S-2, S-3)

**These are the two medium findings from the security review. Both are in
`LinuxSecretStore.cs` and can be addressed in one change.**

### 2.1 S-1: Collapse `PassphraseAvailable()` + `GetOrDeriveKey` into a single call

**Problem:** `StoreWithFile` calls `PassphraseAvailable()` (which invokes
`TryGetPassphrase()` once) to decide the mode, then `GetOrDeriveKey` invokes
`TryGetPassphrase()` again. Between the two calls the passphrase could change (clipboard
cleared, one-shot provider exhausted).

**Fix:** Remove `PassphraseAvailable()` as a public decision point. Instead, have
`GetOrDeriveKey` accept the mode preference as a hint and attempt passphrase derivation; if
the provider returns null, fall back to machine-only mode within the same call and report
the actual mode used as an output. The caller gets `(key, actualMode)` and breadcrumbed
accordingly.

**Files:** `LinuxSecretStore.cs` (lines 107-138, 637-640).

### 2.2 S-2: Return a defensive copy from `GetOrDeriveKey`

**Problem:** The cached derived key is returned by reference across the lock boundary. A
concurrent `Clear()` can zero the key while AES-GCM is encrypting with it.

**Fix:** `GetOrDeriveKey` returns `byte[]` — change to return a copy
(`_cachedKey.AsSpan().ToArray()`). The caller owns and zeroes their copy. The cached
instance stays inside the lock for the session's lifetime. Add a
`CryptographicOperations.ZeroMemory` on the caller's copy in a `finally` block in
`StoreWithFile` and `RetrieveWithFile`.

**Files:** `LinuxSecretStore.cs` (line 118 return, 640-661 callers).

### 2.3 S-3: Document `SYXX_WITNESS_PASSPHRASE` exposure

**Problem:** The interface doc lists `SYXX_WITNESS_PASSPHRASE` as an accepted input method
without noting that `/proc/<pid>/environ` is readable by the same user on stock Ubuntu.

**Fix:** Add to the `IKeystorePassphraseProvider` doc comment: "The environment variable
route carries the same exposure as argv on stock Ubuntu kernels — any same-UID process can
read `/proc/<pid>/environ`. Prefer `--passphrase-file` (0600) or the interactive prompt."
In the CLI implementation, clear the env var after reading:
`Environment.SetEnvironmentVariable("SYXX_WITNESS_PASSPHRASE", null)`.

**Files:** `LinuxSecretStore.cs` (interface doc), CLI passphrase provider (wherever implemented).

### 2.4 S-4: Zero the `material` StringBuilder

**Fix:** Add `material.Clear()` in the `finally` block of `DeriveMasterKey` after the
password bytes are constructed.

**Files:** `LinuxSecretStore.cs` (line ~518 finally block).

**Gate:** All four fixes in one commit. Run `dotnet test` to verify no regressions. The
passphrase session behavior change (S-1) needs a new test: "provider returns different
values on consecutive calls → store uses the second value's mode."

---

## 3. Phase 2 — Open items within scope

### 3.1 G-16 + G-02b co-landing (large, must land together)

**The plan's §7.3b is explicit: repairing G-16 makes the G-02b fail-open arm load-bearing,
so the two must land together.**

**G-16 fix (already coded in the build pass — verify, don't re-implement):**
The accuracy reviewer confirmed the P/Invoke call now passes `_attrKeyTypeName, keyTypePtr,
_attrKeyIdName, keyIdPtr, IntPtr.Zero` — correct varargs alignment. The
`SearchCapabilityUsable` control probe is in place.

**G-02b remaining work:**

1. Bind `secret_password_search_sync` (libsecret ≥ 0.19).
2. GList walk over `SecretRetrievable*`.
3. `secret_retrievable_get_attributes` + GHashTable lookup to scope by keyType (separate
   `witness` from `license` — an unscoped search conflates them).
4. Second schema carrying `SECRET_SCHEMA_DONT_MATCH_NAME` (secret-tool items carry
   `xdg:schema = org.freedesktop.Secret.Generic`).
5. `g_object_unref` / `g_list_free` on every exit path.
6. Update `AbsentUnlessStoreHoldsWitnessSecret` to use the search result — a found-but-locked
   item returns `Present` (search matches on attributes; only value reads require unlock).

**Blocker:** Cannot verify from macOS. Needs a CI leg or the owner's Linux box.

**CI route:** Add a `ci.yml` job with `libsecret-1-0`, `libsecret-tools`, `gnome-keyring`,
run under `dbus-run-session` with `gnome-keyring-daemon --unlock`, asserting
Present/Absent/Unknown against a real keyring both unlocked and locked.

**Gate:** `dotnet test` green + real-keyring verification pack executed by the owner.

### 3.2 G-16b: `DNSPathConfirmation.swift` blocks all Swift compilation

**Status:** `macOS/Sources/SyxxKit/DNSPathConfirmation.swift:90` — type `DNSPathSnapshot`
does not conform to protocol `Equatable`. The file is untracked.

**Options:**

1. Fix the conformance (add `Equatable` to `DNSPathSnapshot`).
2. Claim ownership with whoever's stream created it and ask them to fix or remove it.
3. If the file is abandoned, `git rm` it (it's untracked, so just delete).

**Gate:** `swift build --package-path macOS --target SyxxKit` succeeds.

### 3.3 G-17: macOS `hasKey` collapses `.undetermined` → `false`

**File:** `macOS/Sources/SyxxKit/WitnessKeyManager.swift:42-47`

**Fix:** Propagate `.undetermined` through the return type. The method's doc already states
"false now genuinely means confirmed absent" — make the code match the doc. An unreachable
Keychain should produce "store unreachable" rather than "no key exists."

**Depends on:** G-16b (Swift must compile to test this).

**Gate:** New test: `testHasKeyReturnsUndeterminedWhenKeychainIsUnreachable` — mock a
Keychain error, assert `.undetermined`, not `false`.

### 3.4 G-06 caveat items (partially liftable)

Three collapses inside non-green tiers:

1. **macOS `runWitness()` divergent mapping** (`AppModel.swift:1356-1380`) — RESERVED. Route
   to Mirror stream.
2. **Windows default arm reuses MalformedInput wording** (`WitnessView.xaml.cs:263-264`) —
   RESERVED but potentially liftable. One wording change in a non-reserved method.
3. **Empty paste not distinguished from malformed bytes** — Windows and macOS render red
   "malformed" for empty input. RESERVED — requires amending tests that pin the collapse.

### 3.5 §7.4: `cross-platform-parity-gate.py` is RED

**File:** `macOS/Sources/SyxxKit/Synthesis/CrossVantageConsensus.swift:38` — `Outcome`
enum is {insufficient, match, mismatch, noData} vs canon {failure, inconclusive, success,
unclassified, untested}.

**Fix:** Align the enum or add a mapping layer. Route to whoever owns `SyxxTracer.Outcome`.
This is independent of Linux parity work and currently masks regressions.

---

## 4. Phase 3 — Test gap closure

### 4.1 T-5: SelfVerify tests on Linux

**File:** `Linux/Ubuntu/SyxxUI.Avalonia.Tests/WitnessViewModelTests.cs`

Add:

- `SelfVerify_AfterSuccessfulSeal_ReturnsVerified` — seal → self-verify → green.
- `SelfVerify_NoSnapshot_PromptsUser` — press re-verify without sealing first.
- `SelfVerify_StaleSnapshot_DoesNotShowGreen` — seal A → change domain → seal fails →
  re-verify must NOT show green about A (G-12 Route B).

### 4.2 T-6: External-then-self contamination (single-instance)

**Files:** All three platform test suites.

The defect requires ONE VM doing seal → paste external → self-verify. Current tests use
two separate VMs. Add:

- `SingleInstance_SealThenExternalVerifyThenSelfVerify_DoesNotPresentForeignAsOwn` —
  seal → verify external JSON → self-verify → assert the self-verify result references
  the local seal, not the foreign snapshot.

### 4.3 T-7: Severity-tier tests on Windows and macOS

**Files:** `Windows/dotNET/SyxxUI.Tests/` and `macOS/Tests/SyxxAppTests/`.

Add `[Theory]`-style tests pinning the exact severity tier (color/emoji/icon) for each
`SnapshotVerification` enum member on both platforms. Linux already has this at
`WitnessViewModelTests.cs:335-345`.

### 4.4 T-4: Automated mutation-testing gate

The mutation testing was a one-time manual exercise. Add a CI step (or a test-time
self-check) that verifies at least one test fails when each of the 5 G-01/G-02 guards is
neutered. This could be a test that reflectively inspects the guard sites and asserts test
count ≥ guard count.

---

## 5. Phase 4 — Items liftable from reservation (owner decision required)

### 5.1 G-11: PasswordChar on the Linux import box (cheapest high-severity fix)

**File:** `Linux/Ubuntu/SyxxUI.Avalonia/Views/WitnessView.axaml:55`
**Fix:** Add `PasswordChar="•"` to the `TextBox`. Correct `WitnessViewModel.cs:38-39` doc
comment.
**Effort:** one attribute + one doc comment.
**Reserved only because** §1 puts all `.axaml` out of scope, not because any stream owns it.

### 5.2 G-14: External verify output TextBlock (cheapest "no output at all" fix)

**File:** `Linux/Ubuntu/SyxxUI.Avalonia/Views/WitnessView.axaml:67-80`
**Fix:** Add a `TextBlock` gated on its own text being non-empty (pattern exists at
`axaml:61` with `ImportStatusLine`). Give it its own bound property separate from
`VerifyLine`.
**Effort:** one TextBlock + one new ViewModel property.
**Severity:** arguably as serious as G-09 — a user who opens the app to check someone
else's testimony gets no output at all.

### 5.3 G-09 partial: `ConclusionsView.xaml.cs` (not on any reserved list)

**File:** `Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml.cs:128-137`
**Fix:** Read from the correct model property (the one that carries the manifest) instead
of the dead `SectionInputs.Warp`. The Mirror stream's imminent rewrite makes the full
MirrorView fix redundant, but ConclusionsView is independent.
**Effort:** medium — needs the correct property name from AppViewModel.

---

## 6. Phase 5 — Reserved backlog (routed to other streams)

These items are real and evidenced but touch files owned by the Mirror stream or the
hypothetical second Avalonia team. They are listed here for tracking, not for this plan's
execution.

| Item | Severity | Owner | Summary |
|------|----------|-------|---------|
| G-09 (full) | Highest | Mirror stream | Windows discards tampering conclusion |
| G-10 | High | 2nd Avalonia team | Linux seals with wrong identity (auto-mint) |
| G-12 | Medium-high | 2nd Avalonia team | Green "Verified" beside seal failure |
| G-13 | Low | Stage-5 wording | Copy and catalog drift |
| G-15 | High | Reserved all platforms | Foreign testimony presented as own |

---

## 7. Verification packs (not yet authored)

The plan's §8.1 specifies verification packs as a required deliverable. None exist yet.
Each pack must contain:

1. A copy-pasteable command block the owner runs on their Linux machine.
2. Pre-declared expectation table: pass, fail, and inconclusive output.
3. False-clean description for each check.
4. Self-contained — no multi-step interactive setup.

**Packs needed:**

- **Keyring pack:** libsecret store/retrieve/search with unlocked and locked keyring.
- **Network pack:** `resolvectl status`, `ip neigh show`, VPN tunnel detection.
- **Package pack:** `.deb` install, snap install, both programs present and runnable.
- **CI pack:** the four §4.6 mechanical gates, recorded with exit codes.

---

## 8. Execution order

```
Phase 0: Commit → Ledger → README          (housekeeping, protects existing work)
Phase 1: S-1 + S-2 + S-3 + S-4             (security fixes, one commit)
Phase 2: G-16b → G-17 → G-16+G-02b → §7.4  (open items, dependency-ordered)
Phase 3: T-5 → T-6 → T-7 → T-4             (test gaps)
Phase 4: G-11 → G-14 → G-09 partial         (liftable items, owner decides)
Phase 5: reserved backlog                    (other streams)
```

**Critical path:** Phase 0 → Phase 1 → Phase 2 (G-16b unblocks G-17, G-16+G-02b need
real Linux).

**Parallelizable:** Phase 3 and Phase 4 can run concurrently with Phase 2 once Phase 1
lands.
