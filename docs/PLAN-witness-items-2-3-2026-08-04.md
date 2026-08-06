# PLAN — Witness key-custody, remaining items (2026-08-04)

Plan only — **no building**. Items 1, 4, 5 are already implemented and reviewed
(APPROVE ×2); this captures the two remaining items (2, 3) plus the two review nits,
as a reference for a rewrite. New user-visible strings are catalogued into
`outputs-catalog-story-mode.csv` / `outputs-catalog-general-mode.csv` (both modes), so
they don't gate on per-string approval.

## Status recap

| Item | State |
|---|---|
| 1 / 4-load — RG-2 Linux silent key rotation | DONE, reviewed. Kit tri-state `LoadKeyResult()` (`WitnessKeyStore.cs:146`); Linux throws on `Undetermined`, never generates/overwrites. |
| 4-Win — sign-with-old-key after save error | DONE. `SaveKeyBoxToViewModel()`→bool + gate (`WitnessView.xaml.cs:46,71,117`). |
| 5(a) — B081 trust-source | DONE. `AppModel.swift:2019` → `WitnessPinning.configuredTrustedWitnessKeys()`. |
| 5(b) — external-paste verify (all 3) / 5(c) — Linux import | DONE. |
| **2 — macOS Keychain −34018** | **PLAN below** (gated on a signed-app runtime test). |
| **3 — opt-in passphrase→Ed25519 recovery KDF** | **PLAN below** (owner chose opt-in KDF; salt = app-constant). |

---

## ITEM 2 — macOS Keychain −34018 (entitlement gate)

**Symptom / cause.** The shipping `macOS/Sources/SyxxApp/Syxx.entitlements` is
non-sandboxed with only network entitlements — no `keychain-access-groups`, no
`application-identifier`. Every witness Keychain op uses `kSecUseDataProtectionKeychain:
true` (`WitnessKeyManager.swift:56,89,152,183`; `LicenseManager.swift:89` shares it).
`bundle-app.sh:228-232` signs with Developer ID and no provisioning profile → per Apple
TN3137 the data-protection keychain can return `errSecMissingEntitlement (−34018)`.
Corroborated 3×: redx-1 static analysis, impl-mac's test host, and the pre-existing
`swift test` −34018 skips.

**GATE — one runtime observation (owner):** build+sign via `bundle-app.sh`, launch the
**bundled** app (not `swift run`), click **Generate Keypair** in Witness, and confirm the
license survives a relaunch (shared substrate). Record whether −34018 appears.

**Decision tree.**

- **−34018 NOT observed →** ship config is fine; **make no entitlement change** (the edit
  is riskier than the bug). Close item as "verified benign for Developer-ID signing."
- **−34018 observed → two options:**
  - **2B (recommended, lowest friction):** remove `kSecUseDataProtectionKeychain: true`
    from all 5 sites → legacy file keychain; **no entitlement, no CLI/DeepSyxx signing
    change.** Trade-off: loses the strict `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`
    enforcement B188 added.
  - **2A:** add `keychain-access-groups = [994XAF8MYH.com.milkandgin.syxx]` (LITERAL — the
    raw plist is signed with no Xcode var substitution) + possibly
    `com.apple.application-identifier`. **Blast radius:** the CLI (`bundle-app.sh:220-223`,
    signed with no `--entitlements`) and DeepSyxx share `WitnessKeyManager`/`LicenseManager`
    and would each need a matching group entitlement — a cross-process keychain-sharing
    change. **Re-run the gate test after any 2A change** (a wrong value can −34018 silently
    at runtime despite codesign succeeding).

**Lane:** macOS build (`Syxx.entitlements`, `bundle-app.sh`) + engine `SyxxKit`.

---

## ITEM 3 — opt-in passphrase→Ed25519 recovery KDF

Owner decision: **opt-in KDF**, never default, behind a stark low-entropy warning.
Salt decision: **app-constant (passphrase-only)** — reproduce from the passphrase alone;
weaker (shared salt → amortized precomputation), acceptable because the passphrase is the
only secret in any salt scheme and the warning states it plainly.

**Shape.** The recovery path just fills the SAME 32-byte Ed25519 seed the base64 path
already produces, from `Argon2id(passphrase, appSalt)`; then → base64 → the existing
`saveKey`/`SaveKey` validate-and-persist gate. No change to signing, verifying, or wire
format.

**KDF params (pinned, versioned):** Argon2id, `t=3`, `m=262144 KiB (256 MiB)`, `p=1`,
`out=32`, `salt=16` (a fixed domain-separated app constant, e.g.
`SHA-256("syxx-witness-kdf-v1")[0..16]`).

**Cross-platform (byte-identical via one libsodium `crypto_pwhash`):**

- **.NET:** no new package — `NSec.Cryptography.PasswordBasedKeyDerivationAlgorithm.Argon2id`
  (already referenced). `DegreeOfParallelism` MUST be 1; `MemorySize` is **KiB**; salt is
  16 bytes; `algo.DeriveBytes(pw, salt16, 32)`. One shared `WitnessRecoveryKdf.cs` in
  `Syxx.Kit` serves both Windows + Avalonia (Kit is ProjectReferenced by both).
- **macOS:** add `jedisct1/swift-sodium` to `macOS/Package.swift` (CryptoKit/swift-crypto
  have no Argon2; libsodium not yet vendored). `Sodium().pwHash.hash(outputLength:32,
  passwd:, salt: salt16, opsLimit:3, memLimit:268435456, alg:.Argon2ID13)`. NEW
  `macOS/Sources/SyxxKit/WitnessRecoveryKDF.swift`.
- **Three must-match footguns:** (1) NSec `MemorySize` is **KiB (262144)**, swift-sodium
  `memLimit` is **bytes (268435456)** — same cost, different unit; (2) pin the raw numeric
  t/m/p (not named MODERATE constants) and embed in the version tag; (3) **passphrase =
  Unicode NFC-normalize then UTF-8** on both sides (`Normalize(FormC)` /
  `precomposedStringWithCanonicalMapping`) or "café" derives different keys per OS.
- **Seed→Ed25519:** the 32-byte output IS the seed, no clamp — `Curve25519.Signing.
  PrivateKey(rawRepresentation:)` / `Key.Import(Ed25519, seed, RawPrivateKey)`.

**Opt-in UX (all 3 GUIs), never default, paste/Generate stay primary:** a disclosure
toggle OFF by default → passphrase + **confirm-passphrase** (a silent typo derives a
different key) + a stark warning. Derived seed → the existing save-gate.

- macOS `WitnessView.swift` (block after the key `SecureField` ~:111) + `AppModel`
  `deriveWitnessKeyFromPassphrase`.
- Windows `SyxxUI/Views/WitnessView.xaml(.cs)` + VM.
- Linux `SyxxUI.Avalonia/Views/WitnessView.axaml(.cs)` + VM `[RelayCommand]
  DeriveFromPassphrase`.

**Golden vectors + tests:** new `corpus/witness-kdf/*.json` (`{passphrase, salt-hex, t,m,p,
expectedSeedHex, expectedPublicKeyBase64, expectedKeyId}`) loaded by BOTH the Swift and C#
`WitnessCrossPlatformGoldenVectorTests` twins; include a plain-ASCII vector, an
**NFC-normalization** vector (decomposed vs precomposed → identical seed), and a
derive→sign→verify `.valid` vector. Keep the "empty fixture fails" guard.

**Security-review checklist:** opt-in enforced (default OFF, no implicit selection); stark
warning rendered adjacent (not a tooltip), honesty-spine wording (no "secure", no implied
secrecy); params ≥ (t3/256 MiB/p1/32/16), numeric-pinned + versioned; passphrase & seed
never logged, best-effort zeroize, confirm-passphrase; app-constant-salt consequence
documented; derived key passes `validatePrivateKey` before persist (same gate as pasted).

**Lane:** crypto = `Syxx.Kit` + `SyxxKit`; UX = SyxxApp/SyxxUI/Avalonia; `Package.swift`.

---

## Review nits (from the items 1/4/5 security review — LOW, non-blocking)

- **LOW-1:** broaden the Windows/Linux external-verify `catch` to map ANY exception →
  `MalformedInput` (defense-in-depth: a future converter with an unguarded
  `GetString`/`GetDouble` — e.g. `TriStateJsonConverter.cs:34-41` — throws a
  non-`JsonException` that the current JsonException-only catch would let escape). Not
  reachable today (`TriState` not in the `WitnessSnapshot` graph). macOS already immune.
- **LOW-2:** `WitnessKeyStore.SaveKey` (`:265-271`) writes the secret to the store before
  the reference-token file; a partial failure orphans a secret in the keyring (prior
  identity intact). Store-orphan cleanup nit, not security.
