# PLAN — Witness model cross-client debug findings & fixes (2026-08-04)

Planning run only — **no code edited** (confirmed unmodified by `git diff` during the run).
Four parallel investigators (macOS, Windows, Linux, cross-platform parity) produced the
evidence below, assessed as CONFIRMED / FALSIFIED / INCONCLUSIVE with confidence and
`file:line` citations. This doc extends `docs/PLAN-witness-items-2-3-2026-08-04.md`.

## Ranked issues (confidence × impact)

| # | Issue | Platform | Assessment | Confidence |
|---|-------|----------|---------|-----------|
| 1 | Build-break: `DNSPathSnapshot : Equatable` synthesis fails | macOS (whole pkg) | CONFIRMED | High |
| 2 | Keychain −34018 collapse → fabricated "no key" | macOS GUI | CONFIRMED (mech) / INCONCLUSIVE (runtime) | High-ish |
| 3 | Import overwrites identity + false "saved" on silent store failure | Linux | CONFIRMED | High |
| 4 | Stale on-screen identity after import | Linux | CONFIRMED | High (low sev) |
| 5 | Orphaned/accumulating DPAPI key blobs on overwrite | Windows | CONFIRMED | High |
| 6 | Misleading "signing key required" when keyring unreachable (undetermined not consumed) | Windows GUI | CONFIRMED (parity gap) | Medium |
| 7 | Empty external-paste clobbers prior valid result | Windows GUI | CONFIRMED (parity gap) | Low-Med |
| 8 | SaveKey secret→token ordering (LOW-2) | Win/Linux | CONFIRMED | Low |

---

## 1. BUILD-BLOCKER — macOS won't compile (fix BEFORE anything else)

**Assessment:** CONFIRMED, High. Whole package fails `emit-module`; no Witness test can run,
no binary can ship. All other macOS evidence is static-only until this is fixed.

**Cause:** `DNSPathSnapshot` declares `Equatable`
(`macOS/Sources/SyxxKit/DNSPathConfirmation.swift:90`) but its stored members
`VPNStatus` and `EncryptedDNSStatus` (`NetworkKeyState.swift:17,28`) conform only to
`Codable, Sendable` — not `Equatable`. The synthesized `==` fails.

**Fix:** add `Equatable` to `VPNStatus` and `EncryptedDNSStatus` (or derive it from their
raw associated values / back them with `==`-adherent types).

**Verify:** `swift build` / `swift test --filter Witness` in `macOS/`.

**Blast radius:** none beyond the package; unblocks the −34018 gate test (item 2) below.

---

## 2. macOS Keychain −34018 collapse (`hasKey` undetermined → "no key")

**Assessment:** mechanism CONFIRMED (High); runtime outcome INCONCLUSIVE — cannot be
empirically observed while the build is broken (item 1).

**Cause chain:**

- `hasKey` collapses `.undetermined → false` (`WitnessKeyManager.swift:38-44`).
- All three GUI Witness flows gate on that: `runWitness` (`AppModel.swift:1304-1307`),
  `witnessTestify` (`AppModel.swift:1948-1950`), `witnessVerify` (`AppModel.swift:2007-2012`;
  `WitnessView.swift:46-48`).
- `WitnessKeyManager`'s own doc tells exactly these callers to use `checkKeyPresence()`
  when the distinction matters (`WitnessKeyManager.swift:35-37`).
- Every Keychain op sets `kSecUseDataProtectionKeychain: true`
  (`WitnessKeyManager.swift:56,89,152,183`) + Developer-ID signing with no provisioning
  profile (`bundle-app.sh:228-232`) + `Syxx.entitlements` has no `application-identifier`
  or `keychain-access-groups` → −34018 risk per TN3137 (plan item 2).
- `loadKey()` never caches an error (`WitnessKeyManager.swift:95-103`), so a locked store
  re-fails every access → a GUI can be permanently "no key".
- CLI is immune (env / `--key-file` / `--gen-keypair`, `SyxxWitness.swift:147-171`).

**Gate (owner):** fix item 1, build+sign via `bundle-app.sh`, launch the **bundled** app,
Generate Keypair, confirm license survives relaunch; record whether −34018 appears. Then
apply the decision tree from `docs/PLAN-witness-items-2-3-2026-08-04.md`.

**Recommended companion fix (independent of the gate):** stop gating on `hasKey` for the
three flows; use `checkKeyPresence()` so an undetermined/unreachable Keychain renders an
honest "could not determine" / store-unavailable state instead of "no signing key."

---

## 3. Linux import overwrites identity + false success on silent store failure

**Assessment:** CONFIRMED, High.

**Cause chain:**

- `ImportKey` calls `WitnessKeyStore.SaveKey(trimmed)` unconditionally
  (`WitnessViewModel.cs:369`); `SaveKey` writes a NEW keyId + token
  (`WitnessKeyStore.cs:264-271`), overwriting the existing identity — no confirmation, no
  Undetermined guard.
- `LinuxSecretStore.Store` silently falls back to AES-GCM file store
  (`LinuxSecretStore.cs:42-60`), so a pasted key replaces a merely-unreachable keyring
  identity — exactly the state RG-2's sign path refuses to touch.
- `LinuxSecretStore.StoreWithFile` swallows every exception (`LinuxSecretStore.cs:172-175`,
  comment: "caller should verify via Retrieve") while `SaveKey` still writes the token file
  (`:269-271`) → a failed file-fallback write still yields "Signing key saved"; the next
  load reads token-present + store-missing → **permanently Undetermined until overwritten.**

**Fix (3 parts):**

1. **Confirm-replace:** before overwriting an existing identity, surface an explicit
   replace confirmation in the Linux VM (and apply the same guard on Windows — it shares
   `SaveKey`).
2. **Undetermined guard:** if the current store state is Undetermined, refuse import
   (mirror RG-2) rather than silently replacing via file fallback.
3. **Honest save result:** `SaveKey` must verify via `Retrieve` (or surface the swallowed
   store exception) before reporting success, so a failed durably-write reports failure and
   does not write the token file pointing at an unpersisted secret.

---

## 4. Stale on-screen identity after Linux import

**Assessment:** CONFIRMED, High (low severity).

**Cause:** a successful `ImportKey` clears only `ImportKeyText`
(`WitnessViewModel.cs:387-389`); it does not reset `IdentityLine` / `SignatureLine` /
`VerifyLine` / `_lastSnapshot` (set at `:292`). After importing key B, the screen shows
identity A until the next seal; "Re-verify last seal" also re-certifies the old seal
(crypto passes because it uses the embedded public key) — displayed identity ≠ key that
will sign next.

**Fix:** on successful import, clear `IdentityLine`/`SignatureLine`/`VerifyLine` and
`_lastSnapshot`, resetting the result panel, so the displayed identity is refreshed to the
newly imported key.

---

## 5. Windows: orphaned/accumulating DPAPI key blobs on overwrite

**Assessment:** CONFIRMED, High (low severity).

**Cause:** every `SaveKey` mints a fresh `Guid` (`WitnessKeyStore.cs:264`) and `DeleteKey`
deletes only the current token's keyId (`:319-340`). Superseded DPAPI blobs accumulate at
`%APPDATA%\Syxx\keystore\` forever.

**Fix:** prune superseded keyIds during save/re-key — after a successful overwrite, delete
the prior token's blob; or garbage-collect stale blobs tagged older than the reference
token. (Complements LOW-2.)

---

## 6. Windows GUI: "signing key required" when keyring merely unreachable

**Assessment:** CONFIRMED (cross-platform parity gap), Medium.

**Cause:** `WitnessTestifyAsync` uses `LoadKey()` (`AppViewModel.cs:771`), which collapses
`Undetermined → null` (`WitnessKeyStore.cs:128`), rendering the misleading "A signing key
is required" (`:776`). The Linux twin raises `WitnessKeyStoreUnavailableException`
(`WitnessViewModel.cs:261-265`). RG-2's tri-state is never consumed by the Windows GUI.

**Fix:** surface `Undetermined` distinctly on Windows (consume `LoadKeyResult()` tri-state)
to match Linux, showing a store-unavailable message rather than "no key."

---

## 7. Windows: empty external-paste clobbers a prior valid result

**Assessment:** CONFIRMED (cross-platform parity gap), Low-Med.

**Cause:** an empty paste box yields `WitnessSnapshot=null; WitnessVerification=MalformedInput`
(`AppViewModel.cs:906-916`), overwriting any previously validated result. Linux twin shows a
caution prompt instead (`WitnessViewModel.cs:320-325`).

**Fix:** on empty external-paste, no-op (keep existing result) or show a caution prompt,
matching the Linux behavior.

---

## 8. SaveKey secret-then-token ordering (LOW-2)

**Assessment:** CONFIRMED (already in items 2/3 doc), Low.

Writes secret before reference-token file (`WitnessKeyStore.cs:265` then `:269-271`); a
crash between them orphans a secret. Fold into fix 5 (write-order + cleanup).

---

## Correctness confirmed good (FALSIFIED as bugs — do not "fix")

- **Seed→Ed25519 byte-identity cross-platform:** same 32-byte seed → identical public key
  and keyId on CryptoKit / NSec/libsodium / OpenSSL. macOS signing is **non-deterministic**
  (randomized nonce) → signature **bytes** must not be pinned in golden vectors; validity-only
  assertions are correct. Cross-verify works both directions.
- **Exit codes / status:** 0/1/2/3 mapped identically across the three clients; the 9-case
  tri-state is covered everywhere (`WitnessView.xaml.cs:245-265`, `AppViewModel.cs` 9-state
  renderer, Linux `ApplyVerification` `WitnessViewModel.cs:416-437`).
- **LOW-1 catch breadth:** **empirically falsified as a live bug** — every bad-shape parse
  throws `JsonException` today (the `!double.IsFinite` guard at `WitnessWireJson.cs:202-203`
  catches `∞`; every snapshot-graph converter throws `JsonException`; TriState is not in the
  graph). Remains a fragile defense-in-depth nit only — do not treat as current defect.
- **RG-2 Linux sign path:** correctly throws on Undetermined, never silently rotates
  (`WitnessViewModel.cs:157-173`, `WitnessKeyStore.cs:146-221`).

## Open items (resolved during this run)

- `corpus/witness-crossplatform/` contains exactly the 5 vectors both suites load.
- `corpus/witness-testify-parity/witness-testify-parity.json` is the shared single source
  of truth for both Swift and C# parity suites.
- `corpus/witness-kdf/` does **not** exist → plan item 3 (passphrase→Ed25519 KDF) is
  genuinely unimplemented; its golden vectors are still to be created.

## Suggested sequencing

1. Fix build-break (item 1) — unblocks everything.
2. Run the −34018 gate test (item 2) on a signed bundled app; apply the decision tree.
3. Linux import triple-fix (confirm-replace + Undetermined guard + honest save result) — items 3 & 4.
4. Windows parity fixes — items 5, 6, 7 (+ LOW-2 fold into 5).
5. Item 3 KDF implementation + `corpus/witness-kdf/` vectors (per existing plan).

## Verification after fixes

- `swift test --filter Witness` (macOS) — exercises real Keychain + 9-case tri-state.
- `dotnet test` on Windows `Syxx.Tests` + `SyxxUI.Tests`.
- Linux `SyxxUI.Avalonia.Tests` VM state-machine tests.
- Both `WitnessCrossPlatformGoldenVectorTests` twins (validity-only, not sig bytes).
- `make validate [` / per-repo test gate before commit.
