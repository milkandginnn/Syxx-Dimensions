# DIAG — macOS GUI "major regression": Glutton / Witness / Cage

Read-only diagnosis. No source files edited. AppModel.swift / CageView.swift were
left untouched (another agent is editing them). Only read-only git commands used
(`git diff HEAD -- <path>`, `git diff --name-only`, `git show` — no stash/checkout/
restore/reset).

**One-line answer to the only question asked — DID RECENT UNCOMMITTED EDITS CAUSE
THESE?**  **No.** None of the three failures is a functional regression caused by the
mechanical rename or the other uncommitted edits. Two are environmental (garbage
stored key; unsigned privileged helper) and one is server/environmental (collector
returned a non-200). The uncommitted Witness edit changed the *text* of an
already-failing path but did not break a working one.

---

## Failure 1 — Glutton (leak test): "Token request failed … CollectorError error 0"

**(a) What exactly throws / which enum case**

- Enum: `CollectorError` at `macOS/Sources/SyxxKit/LeakDetector.swift:381`
  - case index 0: `badStatus(Int)`  ← **this is the one thrown** ("error 0")
  - case index 1: `badJSON(Error)`
- Throw site for the token request: `LeakDetector.swift:412`, inside
  `requestToken(...)`:

  ```swift
  guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
      let code = (response as? HTTPURLResponse)?.statusCode ?? -1
      throw CollectorError.badStatus(code)   // line 412
  }
  ```

  The user-facing prefix "Token request failed" confirms it is the **token** call
  (`POST api/leak/token`), not the later `verify` call.
- Meaning: the collector server **did answer over HTTP but with a non-200 status**
  (a dead network would throw a `URLError` out of `session.data(for:)`, not
  `badStatus`). Most likely 401/403 (no session token / no license presented) or a
  5xx. The exact code is swallowed into the associated value and not surfaced in the
  bridged NSError string.

**(b) Is the throw site new/modified in the uncommitted diff?**

- **UNCHANGED from HEAD.** `git diff HEAD -- LeakDetector.swift` has **zero** hunks
  touching `requestToken` or the `badStatus` throw. The diff in this file touches
  only: `VerifyResponse` (added `inconclusive`, `leaked` made optional),
  `extractSignedLeaked` (new, B091), and surrounding leak-verdict plumbing — none of
  it on the token-request path.

**(c) Verdict: ENVIRONMENTAL / server-side — NOT a regression.**

- The token endpoint returned non-200. This needs a reachable collector server plus
  valid credentials (session token or license). The rename did not touch this path.
- **Cannot determine from static analysis alone** whether it is 401 (auth) vs 5xx
  (server). What would settle it: run the leak test with a network capture, or add a
  temporary log of `code` at line 411, or `curl -i` the collector's
  `POST /api/leak/token?zone=<domain>` with the same credentials and read the status.

---

## Failure 2 — Witness: "Could not create witness snapshot … WitnessSignerError error 1"

**(a) What exactly throws / which enum case**

- Enum: `WitnessSignerError` at `macOS/Sources/SyxxKit/WitnessSigner.swift:50`
  - index 0: `emptyKey`
  - index 1: `invalidPrivateKey`  ← **this is the one thrown** ("error 1")
  - index 2: `encodingFailed`
  - index 3: `missingPublicKey`
  - index 4: `unsupportedVersion`
  - index 5: `expired`
- Throw site: `WitnessSigner.swift:103-105`, inside `createSignedSnapshot(...)`:

  ```swift
  guard let keyData = Data(base64Encoded: privateKeyBase64), keyData.count == 32 else {
      throw WitnessSignerError.invalidPrivateKey   // line 104
  }
  ```

- Surfaced by `AppModel.swift:1007` ("Could not create witness snapshot: \(err.message)")
  where `err.message` is `error.localizedDescription` of the thrown
  `WitnessSignerError` (no `LocalizedError` conformance → the default
  "…WitnessSignerError error 1." NSError bridge).
- **ROOT CAUSE — the stored signing key is garbage.** I dumped the keychain
  (read-only):

  ```
  security find-generic-password -s com.milkandgin.syxx -a witness-key -w  →  lklklklk
  ```

  `lklklklk` is valid base64 but decodes to **6 bytes** (`0x964964964964`), not the
  32 bytes an Ed25519 private key requires. So the `keyData.count == 32` clause fails
  and `invalidPrivateKey` is thrown. `emptyKey` (index 0) was *not* thrown because the
  key string is non-empty — consistent with the observed "error 1".
  - This junk key was stored earlier via `WitnessKeyManager.saveKey`, which performs
    **no key-validity check** (only UTF-8 encodability) — `WitnessKeyManager.swift:73-79`.
    Someone pasted "lklklklk" into the key field (a placeholder / smoke-test value).

**(b) Is the throw site new/modified in the uncommitted diff?**

- **MODIFIED — but only the guard predicate, not the outcome.** `git diff HEAD --
  WitnessSigner.swift`:

  ```diff
  - guard let keyData = Data(base64Encoded: privateKeyBase64) else {
  + guard let keyData = Data(base64Encoded: privateKeyBase64), keyData.count == 32 else {
        throw WitnessSignerError.invalidPrivateKey
  ```

- What this changes for the "lklklklk" key:
  - **NEW code:** count check fails → throws `WitnessSignerError.invalidPrivateKey`
    (index 1) — the exact error reported.
  - **HEAD code:** base64 decode *succeeds* (6 bytes), falls through to
    `Curve25519.Signing.PrivateKey(rawRepresentation: keyData)` at
    `WitnessSigner.swift:106`, which throws a **`CryptoKitError.incorrectKeySize`** —
    a *different* error string, but **still a thrown failure**. The snapshot could
    never be created with this key under HEAD either.
- So the uncommitted edit changed the **error identity/text** the user sees, from a
  CryptoKit key-size error to a clean `WitnessSignerError.invalidPrivateKey`. It did
  **not** convert a working snapshot into a broken one. A genuinely generated 32-byte
  key (`WitnessSigner.generateKeyPair` / `WitnessKeyManager.generateAndStoreKey`)
  passes **both** the old and the new guard.
- Storage integrity is intact: the `WitnessKeyManager` diff (D052) only changes
  `writeKeychainData` from delete-then-add to atomic update-when-exists; it does not
  alter the bytes stored, and `generateAndStoreKey`/`saveKey` are otherwise unchanged.

**(c) Verdict: PRE-EXISTING (bad stored key) — NOT a rename regression.**

- The failure is caused by a malformed key sitting in the keychain
  ("lklklklk"), not by the uncommitted edits. The `count == 32` edit is a *hardening*
  that makes the rejection cleaner; it rejects only keys a valid Ed25519 signer would
  also reject.
- Honest caveat: the specific *string* in the bug report ("WitnessSignerError error 1")
  is indeed produced by the uncommitted edit — under HEAD you'd have seen a CryptoKit
  error string instead. If someone equated "new error text" with "new bug," that is
  the source of the confusion. The underlying inability to sign is old.
- **Fix (data, not code):** delete the junk key and generate a real one —
  `security delete-generic-password -s com.milkandgin.syxx -a witness-key`, then let
  the app/CLI generate a keypair (`syxx witness --gen-keypair` /
  `WitnessKeyManager.generateAndStoreKey()`). Then Witness will sign.
- **Separate honesty gap worth a delta (not the reported symptom):**
  `WitnessKeyManager.saveKey` (`WitnessKeyManager.swift:73`) accepts any UTF-8 string
  as a signing key with no validation, so garbage can be persisted and only surfaces
  as a failure at signing time. Validating base64-decodability + 32-byte length at
  save time would reject "lklklklk" at the point of entry with an actionable message.

---

## Failure 3 — Cage: "Could not start capture: Helper installation failed: CFErrorDomainLaunchd (8)"

**(a) What exactly throws**

- Not an enum case — this is an `NSError` built from a failed **`SMJobBless`** call.
  Path: `PrivilegeManager.swift:134` calls `SMJobBless(kSMDomainSystemLaunchd,
  "com.milkandgin.deepsyxx", auth, &error)`. On failure (`PrivilegeManager.swift:143-154`)
  it reads the `CFError`, formats `"\(domain) (\(code)): \(reason)"`, and wraps it as
  `"Helper installation failed: \(detail)"`.
- `CFErrorDomainLaunchd (8)` is launchd reporting it would not load the privileged
  job. Reached because the helper is absent
  (`/Library/PrivilegedHelperTools/com.milkandgin.deepsyxx`,
  `PrivilegeManager.swift:43-46`) so an install is attempted, and the install is
  rejected by launchd — the classic `SMJobBless` outcome when the app and/or the
  embedded helper are **not properly code-signed** and their mutual code-signing
  requirements (`SMPrivilegedExecutables` / `SMAuthorizedClients`) cannot be
  validated.
- Surfaced to the GUI via the `cageLaunchErrorMarker` path
  (`AppModel.swift:1388`) → "Could not start capture: …".

**(b) Is this throw site new/modified in the uncommitted diff?**

- **UNCHANGED from HEAD.** `git diff HEAD --name-only -- PrivilegeManager.swift`
  returns nothing — the file is not modified. The `SMJobBless` call and its error
  wrapping are exactly as committed.

**(c) Verdict: ENVIRONMENTAL (unsigned privileged helper / no Apple Developer ID) —
NOT a regression.** The stated hypothesis is confirmed.

- Syxx has no Apple Developer ID yet (the reason macOS ships last). `SMJobBless`
  requires the main app and the helper to be signed and to reference each other's
  code-signing requirements; an unsigned / ad-hoc-signed or improperly-embedded
  helper fails registration in exactly the `CFErrorDomainLaunchd` family.
- **What is actually required to fix (no code workaround):**
  1. An Apple Developer ID Application certificate.
  2. Sign the app *and* the `com.milkandgin.deepsyxx` helper with it.
  3. Embed the helper in `Contents/Library/LaunchServices/` and ship its launchd
     plist, with matching `SMPrivilegedExecutables` (in the app's Info.plist) and
     `SMAuthorizedClients` (in the helper's Info.plist) designated-requirement
     strings referencing the Developer ID.
  4. Notarize.
  Consider migrating from `SMJobBless` to `SMAppService` (13+), but that too requires
  a signed, embedded daemon — it does not remove the signing requirement.
- **Do NOT** bypass privileged-helper signing in code. A privileged root helper that
  installs without a verifiable code-signing requirement is a security hole, not a
  fix.

---

## Combined conclusion — single root cause, or three?

**Three independent causes. None of them is the uncommitted rename.** There is no
common regression; the "major regression" framing does not hold up under
reproduction of the actual throw sites.

| Module  | Enum case thrown                     | file:line                  | Throw site vs HEAD | Verdict |
|---------|--------------------------------------|----------------------------|--------------------|---------|
| Glutton | `CollectorError.badStatus` (idx 0)   | LeakDetector.swift:412     | **unchanged**      | ENVIRONMENTAL (collector returned non-200; needs server + creds) |
| Witness | `WitnessSignerError.invalidPrivateKey` (idx 1) | WitnessSigner.swift:104 | guard predicate **modified** (text only) | PRE-EXISTING (garbage keychain key "lklklklk" = 6 bytes, not 32) |
| Cage    | `SMJobBless` → `CFErrorDomainLaunchd (8)` | PrivilegeManager.swift:134 | **unchanged**      | ENVIRONMENTAL (unsigned privileged helper; no Developer ID) |

Why the rename looked guilty and isn't: the prior session's kill happened inside
`LeakDetector.swift` and the Witness signing path — exactly two of the three failing
areas — which is a strong prior. But tested against the real throw sites:

- The **Glutton** and **Cage** throw sites are byte-for-byte unchanged from HEAD.
- The **Witness** edit that *is* present (`keyData.count == 32`) only changes the
  error *text* for a key that was already invalid; it rejects nothing a correct
  Ed25519 signer would accept. The functional failure (can't sign with "lklklklk")
  exists identically at HEAD, just with a CryptoKit error string instead.

So the mechanical rename (`LeakVerdict`→`LeakResult`, `witnessVerdict`→
`witnessResult`, `DeterminativeVerdict`→`DeterminativeState`, section
`*Result`) did **not** cause any of the three. The code the rename produced compiles
into the same failing-but-honest paths.

**Honesty check (does any of this show a false secure/success/clean?):** No — the
opposite. All three surfaces correctly report failure rather than a fabricated
success. That is the desired behavior. The one adjacent honesty gap is
`WitnessKeyManager.saveKey` accepting an unvalidated key (deferring the failure to
signing time) — worth a delta, but it still fails honestly.

**Not verified by running the app** (I did not build/run the GUI): the collector's
exact HTTP status for Glutton, and that a freshly generated 32-byte key makes Witness
succeed on this machine. Both are static-analysis + keychain-dump conclusions. To
close them: (1) log/`curl` the token endpoint status; (2) delete + regenerate the
witness key and re-run.
