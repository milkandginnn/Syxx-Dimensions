# DIAG — Witness & Cage failures (2026-07-29)

Read-only diagnosis. No source edited. Classification legend:
**CODE DEFECT** / **HONESTY GUARD FIRING CORRECTLY** / **ENVIRONMENTAL**.

---

## FAILURE A — Witness: "WitnessSignerError error 1" on "Witness Testimony"

### Decode of the error (done first)
Enum: `macOS/Sources/SyxxKit/WitnessSigner.swift:50-57`, declaration order:

| index | case | line |
|-------|------|------|
| 0 | `emptyKey` | 51 |
| **1** | **`invalidPrivateKey`** | **52** |
| 2 | `encodingFailed` | 53 |
| 3 | `missingPublicKey` | 54 |
| 4 | `unsupportedVersion` | 55 |
| 5 | `expired` | 56 |

`WitnessSignerError` conforms only to `Error` (line 50), NOT `LocalizedError`, so its
`localizedDescription` is the generic Foundation bridge string
"The operation couldn't be completed. (SyxxKit.WitnessSignerError error N.)" where
N is the case ordinal. **error 1 = `.invalidPrivateKey`**, thrown at
`WitnessSigner.swift:104`:

```swift
guard let keyData = Data(base64Encoded: privateKeyBase64), keyData.count == 32 else {
    throw WitnessSignerError.invalidPrivateKey            // line 104
}
```

It is index 1, not 0 (`emptyKey`), which proves the stored key string was **non-empty
but did not base64-decode to exactly 32 bytes**.

### Reproduction path (traced from the production entry point)
- Button "Witness Testimony": `WitnessView.swift:111-121` → `saveWitnessKey(witnessKeyInput)`
  then `model.witnessTestify()`.
- `witnessTestify()` `AppModel.swift:1515` → loads key from Keychain (`:1537`) →
  `WitnessTestifyPlanner.seal(...)` (`:1549`).
- `WitnessTestifyPlanner.seal` `SyxxKit/Trust/WitnessTestifyPlanner.swift:44-52` →
  `WitnessSigner.createSignedSnapshot(...)` (`:51`).
- Throws `.invalidPrivateKey` at `WitnessSigner.swift:104`; caught at
  `AppModel.swift:1556-1560` → message
  `"Could not create witness snapshot: \(error.localizedDescription)"` →
  **"Could not create witness snapshot: The operation couldn't be completed.
  (SyxxKit.WitnessSignerError error 1.)"** — exact match to the screenshot.
  (The `runWitness()` path `AppModel.swift:997/1007` funnels to the same throw.)

Keychain is confirmed reachable in this failure: `hasKey` (`:1533`) and `loadKey()`
(`:1537`) both succeeded — otherwise the earlier guards would have produced
"A signing key is required" or "Could not load signing key from Keychain". So this is
**not** a keychain-access failure; the stored string simply is not a valid key.

### The originating defect (why the key is invalid)
The value stored is whatever the owner typed into the SecureField. The UI is
internally **contradictory and misleading**:

- `WitnessView.swift:99`: caption tells the user
  **"Tell the witness a secret that you can remember (essentially, a password):"**
- `WitnessView.swift:102`: "Entrust the Witness with the private key..."
- `WitnessView.swift:106`: SecureField placeholder **"Ed25519 private key (base64)…"**

The caption instructs a **memorable passphrase**; the code requires a **base64-encoded
32-byte Ed25519 private key**. There is **NO passphrase→key derivation** anywhere
(grep for PBKDF/scrypt/Argon2/deriveKey across `macOS/Sources` returns nothing; the
only key origin is `WitnessSigner.generateKeyPair()`), so a memorable secret such as
"thisvid-secret" is stored verbatim and can never decode to 32 bytes.

Compounding gaps:
- `AppModel.saveWitnessKey` `AppModel.swift:1497-1513` and
  `WitnessKeyManager.saveKey` `WitnessKeyManager.swift:73-79` accept **any UTF-8
  string** with zero validation that it base64-decodes to 32 bytes. The invalid key
  is persisted silently; the failure only surfaces later, at sign time, as a cryptic
  enum error.
- The GUI offers **no "generate keypair" affordance**. `generateAndStoreKey()` exists
  (`WitnessKeyManager.swift:96`) but is never called from `SyxxApp` (grep confirms).
  The only guidance the guards give — "use `syxx witness --gen-keypair`"
  (`AppModel.swift:963`) — is a CLI command a GUI-only owner cannot reach.

### Honesty check
The crypto guard at `WitnessSigner.swift:104` is **HONESTY GUARD FIRING CORRECTLY** —
it refuses to sign with bogus key material rather than fabricating a seal (the C070
class: never sign/verify against invalid/empty key bytes). No false "sealed" is
produced. That guard is the **symptom**, not the bug.

### Verdict — FAILURE A
**CODE DEFECT (upstream), guard firing correctly (downstream).**
- The `.invalidPrivateKey` throw is correct and must stay.
- The real defect is the UI/logic contradiction: the interface tells the user to
  enter a memorable password, no KDF turns a password into an Ed25519 key, save-time
  validation is absent, and there is no in-GUI key generation.
- **Fix requires a design + user-facing-wording decision** (either implement a real
  passphrase→Ed25519 KDF, or replace the "password" caption with a
  Generate-Keypair button + save-time validation). Both touch `SyxxApp/**` (off-limits
  here) and change user-visible copy (proposal-gated). **Hand off to
  syxx-neural-planner/engineer + syxx-neural-gui-architect; do not fix in this
  read-only pass.**
- Cannot determine from static evidence exactly what string the owner typed; not
  needed — any non-32-byte-base64 value produces this identical failure.

---

## FAILURE B — Cage: "Helper installation failed: CFErrorDomainLaunchd (8)"

### Reproduction / evidence (runtime, this machine)
Error surfaced from `PrivilegeManager.installHelper()` `PrivilegeManager.swift:134-158`:
`SMJobBless(...)` returned false with a CFError of domain `CFErrorDomainLaunchd`,
code 8, wrapped into "Helper installation failed: ..." at `:153`.

`CFErrorDomainLaunchd` code **8 = `kSMErrorJobPlistNotFound`** in the ServiceManagement
error enumeration.

**Decisive test (run just now):**
```
$ otool -l Syxx.app/Contents/Library/LaunchServices/com.milkandgin.deepsyxx | grep -c __launchd_plist
0                       # REQUIRED section is ABSENT
$ otool -l ... | grep -c __info_plist
1                       # only __info_plist is embedded
# __TEXT sectnames present: __info_plist, __unwind_info, __objc_imageinfo
```
`SMJobBless` requires the helper executable to **embed** its launchd plist in
`__TEXT,__launchd_plist`. It is absent → launchd cannot find the job plist →
`kSMErrorJobPlistNotFound` (error 8). `Package.swift:81-84` embeds only `__info_plist`;
`bundle-app.sh:27` copies `com.milkandgin.deepsyxx.plist` as a **sidecar file** next to
the binary, which SMJobBless does not read.

`/Library/PrivilegedHelperTools/com.milkandgin.deepsyxx` is confirmed **absent** — the
helper has never successfully installed.

### The "unsigned build" prior is WRONG — stated plainly
The task's strong prior (unsigned / ad-hoc build, no Developer ID → registration
refusal) does **not** hold on this machine. Runtime evidence:

- Running app is the **signed bundle**: pid 11129 =
  `/Users/masterrabbit/Software/Syxx/macOS/Syxx.app/Contents/MacOS/Syxx` (not a raw
  `.build` binary), so a caller-signature mismatch is ruled out.
- Main app `codesign`: `Authority=Apple Development: joelwarne@proton.me (6L9S66PLGX)`,
  chains to Apple Root CA, **TeamIdentifier=994XAF8MYH**.
- Helper `codesign`: same authority, **TeamIdentifier=994XAF8MYH**.
- The requirement strings — `SMPrivilegedExecutables` in the generated Info.plist
  (`bundle-app.sh:60-64`) and `SMAuthorizedClients` in `DeepSyxx-Info.plist:17-20` —
  demand `anchor apple generic and certificate leaf[subject.OU] = "994XAF8MYH"`.
  **Both binaries satisfy this**: Apple Development chains to `anchor apple generic`,
  and the OU/team matches exactly.

So signing is currently adequate for the requirement match; the failure is a
**build/packaging defect**, not a signing/environment gap. (A Developer ID +
notarization is still needed for *distribution* to other machines, but it is not the
cause of error 8 here.)

### Already filed
This is **delta D079** `DELTAS.md:29269-29325` (CRITICAL, still OPEN — present in
DELTAS.md, not in DELTAS-fixed.md), part (a) exactly:
missing `__TEXT,__launchd_plist` → `kSMErrorJobPlistNotFound`. D079 also records a
second latent breakage (b): even if blessed, `DeepSyxx/main.swift:206-212` uses
`NSXPCListener.service()` (XPC-service singleton) instead of
`NSXPCListener(machServiceName:)`, so the declared Mach service is never claimed and
XPC connect would fail next. The current symptom is (a); (b) would block the very next
step.

### Fix direction (no bypass)
Per D079 fixes: add the `__launchd_plist` linker section to the DeepSyxx target,
switch the listener API, and add a `bundle-app.sh` post-build assertion that fails if
`otool -l "$HELPER" | grep -q __launchd_plist` is false. Migration to
`SMAppService.daemon` is the modern path (SMJobBless deprecated since macOS 13; host is
macOS 26.5.2). **No SIP/authorization/sudo bypass — none is needed; the mechanism is
sound once the plist is embedded and the listener is fixed.**

### Honesty check — does Cage report the failure honestly? YES.
Trace of the failure path:
- `startCageCapture` throws; caught at `AppModel.swift:1132` →
  `resultBox.finish(cageLaunchErrorMarker + error.localizedDescription)`.
- `finalMessage` `AppModel.swift:1386-1388` detects the marker **first** and returns
  **"Could not start capture: Helper installation failed: CFErrorDomainLaunchd (8)."**
  (before any "No packets" / "Captured N" wording).
- `cageEventsCount = 0`, `cageRows = []` (`:1184-1187`); records empty →
  `cageSectionInputs(fromRows: [])` returns `(nil, nil)` (`:1373`), so
  `sectionInputs.exfil = nil` → Cage stays **UNTESTED**, never a silent clean.

A capture that never ran therefore reads as an explicit error + UNTESTED, **not** as a
completed clean/empty capture. This path is as honest as the missing-binary path at
`AppModel.swift:1106`. Residual (minor, not an honesty defect): the surfaced text
"CFErrorDomainLaunchd (8)" is cryptic to an end user — a wording nicety for the
surfacer, not a false-clean.

### Verdict — FAILURE B
**CODE DEFECT (build/packaging), already filed as D079 (OPEN).** Root cause verified
by runtime `otool`: helper lacks the `__TEXT,__launchd_plist` section, so
`SMJobBless` fails with `kSMErrorJobPlistNotFound` = `CFErrorDomainLaunchd` error 8.
The "unsigned/ad-hoc build" hypothesis is **refuted** — app and helper are signed with
Apple Development, team 994XAF8MYH, matching the SMJobBless requirement. Honesty of
the failure surface is **GOOD** (explicit error, Cage stays UNTESTED). Hand off the
fix to the macOS build/helper owner (syxx-neural-executor); it is confined to
`Package.swift`, `DeepSyxx/main.swift`, and `bundle-app.sh`.

### Blast radius
- Witness (A): `SyxxKit` core + `SyxxApp` GUI. The `syxx witness` CLI path is not hit
  by the passphrase contradiction if the user supplies a real base64 key; the GUI is
  the affected surface. Parity: Windows `Syxx.Kit.WitnessSigner` has the same
  `invalidPrivateKey` guard — check whether the WinUI Witness surface has the same
  "enter a memorable password" wording (likely same UX class). Server not implicated.
- Cage (B): macOS-only mechanism (SMJobBless/XPC helper). Windows/Ubuntu use different
  privilege models — not this defect. Server not implicated.
