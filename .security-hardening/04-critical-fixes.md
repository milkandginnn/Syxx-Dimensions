# Security Hardening — Step 4: Critical Vulnerability Remediation Plan

**Target:** Syxx — cross-platform DNS tampering detection toolkit
**Date:** 2026-08-05
**Status:** PLAN — awaiting owner review before implementation

---

## Remediation Summary

| ID | Severity | Fix | Effort | Risk |
|---|---|---|---|---|
| SYXX-VULN-001 | Medium | CSP nonce for Stripe checkout | Low | Low — Stripe hosted checkout, not server-rendered |
| SYXX-VULN-006 | Medium | Migrate collector key to Keychain | Medium | Low — backward-compatible migration |
| SYXX-VULN-007 | Low | Migrate witness keys to Keychain | Low | Low — same pattern as SYXX-VULN-006 |

---

## 1. SYXX-VULN-001: CSP `unsafe-inline` for Scripts

### Analysis

The checkout route at `app.py:1207-1244` does **not** serve any HTML — it 303-redirects the browser to Stripe's hosted Checkout page. The CSP `unsafe-inline` in `script-src` is emitted by `_security_headers()` on **every** API response, but the server only serves JSON (API responses) and one locked-down HTML page (`/api/whats-new`).

**Key insight:** The `unsafe-inline` in the global CSP is a defense-in-depth gap, but it is not exploitable in the current deployment because:
- The server never renders user-controlled HTML
- All API responses are JSON (`Content-Type: application/json`)
- The `/api/whats-new` endpoint already uses its own strict CSP override
- The Stripe checkout page is hosted by Stripe, not by this server

### Remediation Plan

**Option A (Recommended): Remove `unsafe-inline` from the global CSP, add it only where needed**

The global CSP at `app.py:612-620` currently serves all responses. Since the server never serves inline scripts, `unsafe-inline` can be removed from the global `script-src` and the CSP tightened to:

```
script-src 'self' https://js.stripe.com;
```

This removes the blanket `unsafe-inline` from 99% of responses. The Stripe.js `frame-src` and `connect-src` directives remain unchanged — Stripe.js runs inside an iframe, not as an inline script on this server.

**File:** `Server/syxx_server/app.py:614`

Change:
```python
"script-src 'self' 'unsafe-inline' https://js.stripe.com; "
```
To:
```python
"script-src 'self' https://js.stripe.com; "
```

**Option B (Future): Add CSP nonce for any server-rendered HTML**

If the server ever renders HTML pages (e.g., a marketing page or a payment confirmation page), implement a per-request nonce:

```python
import secrets
nonce = secrets.token_urlsafe(16)
# In the CSP header:
f"script-src 'self' 'nonce-{nonce}' https://js.stripe.com; "
# In the HTML template:
f'<script nonce="{nonce}" src="https://js.stripe.com/v3/"></script>'
```

This is not needed today because the server does not render HTML with inline scripts.

### Regression Tests

1. Verify all API responses include CSP without `unsafe-inline` in `script-src`
2. Verify `/api/checkout` still 303-redirects to Stripe successfully
3. Verify `/api/whats-new` still uses its own strict CSP override
4. Verify the Stripe.js iframe loads correctly in the browser (Stripe.js is loaded by Stripe's hosted page, not this server's CSP)

---

## 2. SYXX-VULN-006: Collector Public Key in UserDefaults

### Current State

Three UserDefaults keys store the collector's trust anchor:
- `collector_trusted_key` — Ed25519 public key (base64) — `LeakDetector.swift:906`
- `collector_trusted_key_id` — SHA-256 prefix of the key — `LeakDetector.swift:907`
- `collector_trusted_key_timestamp` — rotation timestamp — `LeakDetector.swift:908`

Read path: `persistedTrustedSigningKey()` at `LeakDetector.swift:924` reads from UserDefaults.

### Remediation Plan

Migrate all three keys to the Keychain using the same pattern as `WitnessKeyManager.keychainQuery()` and `WitnessKeyManager.writeKeychainData()`.

**File:** `macOS/Sources/SyxxKit/LeakDetector.swift`

#### Step 1: Add Keychain storage methods to `CollectorPinning`

Add a new `CollectorTrustStore` enum (or extend `CollectorPinning`) with Keychain read/write methods:

```swift
/// Manages the collector's trusted signing key in the Keychain.
/// Mirrors the pattern in WitnessKeyManager.swift.
public enum CollectorTrustStore {
    private static let bundleIdentifier = "com.milkandgin.syxx"
    private static let keyAccount = "collector-trusted-key"
    private static let keyIdAccount = "collector-trusted-key-id"
    private static let keyTimestampAccount = "collector-trusted-key-timestamp"

    private static func keychainQuery(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
            kSecUseDataProtectionKeychain as String: true
        ]
    }

    /// Read a string from Keychain. Returns nil if not found.
    public static func read(account: String) -> String? {
        var query = keychainQuery(account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    /// Write a string to Keychain (upsert).
    public static func write(_ value: String, account: String) throws {
        guard let data = value.data(using: .utf8) else { return }
        let query = keychainQuery(account: account)
        if SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess {
            let update: [String: Any] = [kSecValueData as String: data]
            let status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
            guard status == errSecSuccess else { return }
        } else {
            var addQuery = query
            addQuery[kSecValueData as String] = data
            SecItemAdd(addQuery as CFDictionary, nil)
        }
    }
}
```

#### Step 2: Replace UserDefaults writes in `syncTrustedSigningKey`

At `LeakDetector.swift:906-908`, replace:

```swift
UserDefaults.standard.set(publicKey, forKey: "collector_trusted_key")
UserDefaults.standard.set(keyId, forKey: "collector_trusted_key_id")
UserDefaults.standard.set(rotationTimestamp, forKey: "collector_trusted_key_timestamp")
```

With:

```swift
try? CollectorTrustStore.write(publicKey, account: CollectorTrustStore.keyAccount)
try? CollectorTrustStore.write(keyId, account: CollectorTrustStore.keyIdAccount)
try? CollectorTrustStore.write(rotationTimestamp, account: CollectorTrustStore.keyTimestampAccount)
```

#### Step 3: Replace UserDefaults read in `persistedTrustedSigningKey`

At `LeakDetector.swift:924`, replace:

```swift
UserDefaults.standard.string(forKey: "collector_trusted_key")
```

With:

```swift
CollectorTrustStore.read(account: CollectorTrustStore.keyAccount)
    ?? UserDefaults.standard.string(forKey: "collector_trusted_key")  // migration fallback
```

#### Step 4: Add one-time migration on app launch

In `SyxxApp.swift` or `LeakDetector.swift` initialization, add:

```swift
/// One-time migration: move collector trust anchor from UserDefaults to Keychain.
static func migrateTrustAnchorToKeychain() {
    let ud = UserDefaults.standard
    // Only migrate if Keychain doesn't already have the key
    if CollectorTrustStore.read(account: CollectorTrustStore.keyAccount) == nil,
       let key = ud.string(forKey: "collector_trusted_key") {
        try? CollectorTrustStore.write(key, account: CollectorTrustStore.keyAccount)
        if let keyId = ud.string(forKey: "collector_trusted_key_id") {
            try? CollectorTrustStore.write(keyId, account: CollectorTrustStore.keyIdAccount)
        }
        if let ts = ud.string(forKey: "collector_trusted_key_timestamp") {
            try? CollectorTrustStore.write(ts, account: CollectorTrustStore.keyTimestampAccount)
        }
        // Remove from UserDefaults after successful migration
        ud.removeObject(forKey: "collector_trusted_key")
        ud.removeObject(forKey: "collector_trusted_key_id")
        ud.removeObject(forKey: "collector_trusted_key_timestamp")
    }
}
```

### Regression Tests

1. `syncTrustedSigningKey` writes to Keychain, not UserDefaults
2. `persistedTrustedSigningKey()` reads from Keychain first, falls back to UserDefaults
3. Migration moves existing UserDefaults keys to Keychain and removes them from UserDefaults
4. `configuredTrustedSigningKeys()` still returns the correct set after migration
5. After migration, `UserDefaults.standard.string(forKey: "collector_trusted_key")` returns nil
6. Keychain items have `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`

---

## 3. SYXX-VULN-007: Witness Trusted Keys in UserDefaults

### Current State

`WitnessPinning` at `WitnessSigner.swift:378-393` reads `witness_trusted_keys` from UserDefaults.

### Remediation Plan

Apply the same Keychain pattern as SYXX-VULN-006.

**File:** `macOS/Sources/SyxxKit/WitnessSigner.swift`

#### Step 1: Replace UserDefaults read in `configuredTrustedWitnessKeys`

At `WitnessSigner.swift:389`, replace:

```swift
var raw = UserDefaults.standard.string(forKey: userDefaultsKey) ?? ""
```

With:

```swift
var raw = CollectorTrustStore.read(account: "witness-trusted-keys")
    ?? UserDefaults.standard.string(forKey: userDefaultsKey) ?? ""
```

#### Step 2: Add migration call

Add a one-time migration in `WitnessPinning`:

```swift
static func migrateTrustedKeysToKeychain() {
    let ud = UserDefaults.standard
    if CollectorTrustStore.read(account: "witness-trusted-keys") == nil,
       let keys = ud.string(forKey: userDefaultsKey) {
        try? CollectorTrustStore.write(keys, account: "witness-trusted-keys")
        ud.removeObject(forKey: userDefaultsKey)
    }
}
```

Call `migrateTrustedKeysToKeychain()` alongside `migrateTrustAnchorToKeychain()` on app launch.

### Regression Tests

1. `configuredTrustedWitnessKeys()` reads from Keychain first, falls back to UserDefaults
2. Migration moves existing UserDefaults key to Keychain and removes it
3. `SYXX_WITNESS_TRUSTED_KEYS` environment variable still works as fallback
4. Keychain item has `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`

---

## 4. Implementation Order

1. **SYXX-VULN-001** — single-line CSP change in `app.py`. No risk, immediate improvement.
2. **SYXX-VULN-006** — add `CollectorTrustStore`, update `syncTrustedSigningKey` and `persistedTrustedSigningKey`, add migration. Medium effort.
3. **SYXX-VULN-007** — reuse `CollectorTrustStore.write`/`read`, update `WitnessPinning`, add migration. Low effort (reuses Step 2 infrastructure).

---

## 5. Files to Modify

| File | Change |
|---|---|
| `Server/syxx_server/app.py:614` | Remove `'unsafe-inline'` from `script-src` |
| `macOS/Sources/SyxxKit/LeakDetector.swift` | Add `CollectorTrustStore` enum, update `syncTrustedSigningKey` (lines 906-908), update `persistedTrustedSigningKey` (line 924), add migration |
| `macOS/Sources/SyxxKit/WitnessSigner.swift` | Update `configuredTrustedWitnessKeys` (line 389), add migration |
| `macOS/Sources/SyxxApp/SyxxApp.swift` | Call migration functions on app launch |

---

## 6. Out of Scope

- CSP nonce implementation (not needed — server renders no HTML with inline scripts)
- Ed25519 key rotation API (separate track, low priority)
- SQLite PII encryption at rest (file permissions are current protection, separate consideration)
- Automated dependency scanning in CI (separate DevOps task)

---

*Report generated by Security Hardening Orchestrator — Step 4 of 13*
*Next: Phase 2 Checkpoint — user approval required before implementation*
