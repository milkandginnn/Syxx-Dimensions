# Privilege Escalation Fix — DeepSyxx XPC Helper

**Date:** July 29, 2026
**Branch:** `matrix-cutover-phase3`
**Component:** macOS GUI / Cage Capture

---

## Summary

The macOS app was prompting for the user's password **four times** when using Cage capture. The fix replaces the old `osascript ... with administrator privileges` approach with a proper XPC helper (DeepSyxx) that caches credentials and prompts only once.

## Root Cause

The old approach used `osascript do shell script ... with administrator privileges` which spawns a new privileged process for each operation. Each invocation triggers a separate password prompt. Four operations = four prompts.

## Fix

### Architecture

Replaced `osascript` with a **privileged XPC helper** installed via `SMJobBless`:

1. **Install once** — The helper is installed via `SMJobBless` which prompts for password **once**
2. **Reuse silently** — After installation, all privileged operations go through the helper via XPC with no further prompts

### Components

| Component | Location | Purpose |
|-----------|----------|---------|
| DeepSyxx helper | `macOS/Sources/DeepSyxx/main.swift` | Root-level daemon that performs privileged operations |
| XPC protocol | `macOS/Sources/SyxxKit/PrivilegedHelperProtocol.swift` | Shared protocol defining the helper's interface |
| PrivilegeManager | `macOS/Sources/SyxxApp/PrivilegeManager.swift` | App-side client that manages helper installation and XPC communication |
| Launchd plist | `macOS/Sources/DeepSyxx/com.milkandgin.deepsyxx.plist` | Launchd configuration for the helper |
| Helper Info.plist | `macOS/Sources/DeepSyxx/DeepSyxx-Info.plist` | Embedded Info.plist with `SMAuthorizedClients` |
| App entitlements | `macOS/Sources/SyxxApp/Syxx.entitlements` | App entitlements (sandbox disabled, network access) |
| Helper entitlements | `macOS/Sources/DeepSyxx/DeepSyxx.entitlements` | Helper entitlements (sandbox disabled) |

### Security

- `SMJobBless` validates the helper's code signature at installation time using `SMAuthorizedClients`
- The app's `SMPrivilegedExecutables` in `Info.plist` validates the app's identity
- Bidirectional trust chain prevents unauthorized apps from communicating with the helper
- Each operation is a specific named method (`startCageCapture`, `flushDNSCache`, etc.) — no generic command execution

### Code Signing

- `bundle-app.sh` auto-detects Developer ID or Apple Development certificates from the keychain
- Both app and helper are signed with the same team ID (`994XAF8MYH`)
- Info.plist is generated **before** signing so `SMPrivilegedExecutables` is bound to the signature

### Authorization

The authorization uses `AuthorizationCreate` with flags `[.extendRights, .interactionAllowed, .preAuthorize]`:

- `.extendRights` — requests the right to modify system daemons
- `.interactionAllowed` — allows the system to show a password/Touch ID dialog
- `.preAuthorize` — pre-authorizes the right so SMJobBless doesn't re-prompt

## Files Changed

| File | Change |
|------|--------|
| `macOS/bundle-app.sh` | Added code signing for app and helper; Info.plist generated before signing; helper bundled into LaunchServices |
| `macOS/Package.swift` | Added `DeepSyxx` executable target with linker settings for embedded Info.plist; added `Security` and `ServiceManagement` frameworks to SyxxApp |
| `Package.swift` | Synced with macOS/Package.swift |
| `macOS/Sources/SyxxApp/AppModel.swift` | Cage capture now uses `PrivilegeManager.shared.startCageCapture()` instead of osascript |
| `macOS/Sources/SyxxApp/PrivilegeManager.swift` | New file — manages helper installation, XPC connection, and privileged operations |
| `macOS/Sources/SyxxKit/PrivilegedHelperProtocol.swift` | New file — shared XPC protocol |
| `macOS/Sources/DeepSyxx/main.swift` | New file — helper daemon implementation |
| `macOS/Sources/DeepSyxx/com.milkandgin.deepsyxx.plist` | New file — launchd configuration |
| `macOS/Sources/DeepSyxx/DeepSyxx-Info.plist` | New file — embedded Info.plist with SMAuthorizedClients |
| `macOS/Sources/SyxxApp/Syxx.entitlements` | New file — app entitlements |
| `macOS/Sources/DeepSyxx/DeepSyxx.entitlements` | New file — helper entitlements |

## Verification

| Check | Result |
|-------|--------|
| Swift build | ✅ Passes |
| Code signing (app) | ✅ Identifier=`com.milkandgin.syxx`, Team=`994XAF8MYH` |
| Code signing (helper) | ✅ Identifier=`com.milkandgin.deepsyxx`, Team=`994XAF8MYH` |
| Info.plist bound to signature | ✅ `Info.plist entries=12` |
| Helper bundled in app | ✅ `Contents/Library/LaunchServices/com.milkandgin.deepsyxx` |
| Launchd plist embedded | ⚠️ See D079. This row previously read `✅ Contents/Library/LaunchServices/com.milkandgin.deepsyxx.plist`, which was never true: that directory holds only the helper binary. SMJobBless does not read a sidecar plist. It reads the `__TEXT,__launchd_plist` section INSIDE the helper binary, which was absent, so launchd refused the job with `CFErrorDomainLaunchd (8)` = `kSMErrorJobPlistNotFound` and Cage could never start a capture. The section is now embedded via `-sectcreate` in `Package.swift`, verified with `otool -l`, and `bundle-app.sh` fails the build if it is missing. An actual `SMJobBless` install has still not been observed, so D079 stays OPEN. |

## Remaining

1. **Developer ID certificate** — Apple is investigating an issue with the user's Developer ID certificate. Apple Development is being used as a fallback. Once the Developer ID is available, rebuild with `bundle-app.sh` to sign with the correct certificate.

2. **Server deployment** — The MOTD branding fix (`whats-new-mac.html` masthead) needs to be deployed to the production server via `scripts/deploy.sh`.