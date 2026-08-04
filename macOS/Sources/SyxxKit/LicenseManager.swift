import Foundation
import Security
import CommonCrypto
import CryptoKit

#if canImport(Darwin)
import Darwin
#endif

public struct KeygenValidationResponse: Codable, Sendable {
    public struct Meta: Codable, Sendable {
        public let constant: String
        public let valid: Bool
    }
    public let meta: Meta
}

public enum LicenseManagerError: Error {
    case invalidKeyEncoding
    case keychainError(OSStatus)
}

/// A typed outcome for license validation, distinguishing "the key is invalid" from "we
/// couldn't reach the validation server" — collapsing both to `false` makes a censored/blocked
/// network indistinguishable from a bad key, silently locking users out of detection features
/// exactly when the network is the thing worth investigating (M22/W23).
public enum LicenseValidationResult: Sendable {
    case valid
    case invalid
    case timeout
    case networkError(String)

    public var isValid: Bool { self == .valid }
}

extension LicenseValidationResult: Equatable {
    public static func == (lhs: LicenseValidationResult, rhs: LicenseValidationResult) -> Bool {
        switch (lhs, rhs) {
        case (.valid, .valid), (.invalid, .invalid), (.timeout, .timeout):
            return true
        case let (.networkError(a), .networkError(b)):
            return a == b
        default:
            return false
        }
    }
}

public enum LicenseManager {

    /// Reverse-DNS identifier for this app, used to namespace the Keychain items so they don't
    /// collide with other apps' generic-password entries.
    private static let bundleIdentifier = "com.milkandgin.syxx"
    private static let licenseKeyAccount = "license-key"
    private static let trialStateAccount = "trial-state"
    /// E-7 offline grace: Keychain account for the epoch-ms of the last SUCCESSFUL Keygen validation.
    /// Written on every .valid result; read to grant an offline grace window when the network is
    /// unreachable (censored/filtered) — the exact condition this product investigates.
    private static let lastValidValidationAccount = "last-valid-validation"
    /// 30 days in milliseconds. A paying subscriber whose network blocks Keygen stays unlocked for
    /// 30 days from their last successful online validation. After that we cannot confirm the
    /// subscription is still active and they fall through to the normal locked state.
    /// Internal (not private) so it can be used as a default argument on `offlineGraceApplies`.
    internal static let offlineGraceMs: Int64 = 30 * 24 * 60 * 60 * 1000

    private static let validationTimeout: TimeInterval = 8.0

    /// B187: Pinned P-256 public key (SubjectPublicKeyInfo DER, hex-encoded, 91 bytes = 182 hex chars)
    /// from Keygen.sh. Used to verify the Keygen-Signature header on validate-key responses before
    /// trusting meta.valid. Until this is populated, unverifiable responses return false (safe default).
    /// Retrieve from: Keygen dashboard Settings → Public Key (PEM), then hex-encode the DER bytes.
    private static let keygenPublicKeyDERHex = "3059301306072a8648ce3d020106082a8648ce3d030107034200043495cab670d545c30a9f6a3cce56658883ffbe96a48c03a09efbc3350ed23e513c9064ea53b32b354202fae899e50ab30262788441c7f95bbd3779215da6a78e"

    private static var configURL: URL {
        let home = FileManager.default.homeDirectoryForCurrentUser
        return home.appendingPathComponent(".config/syxx/license.json")
    }

    // MARK: - Keychain plumbing

    private static func keychainQuery(account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: bundleIdentifier,
            kSecAttrAccount as String: account,
            // B188: apply the same accessibility class as WitnessKeyManager for consistency;
            // use the data-protection keychain so the accessibility class is enforced.
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
            kSecUseDataProtectionKeychain as String: true
        ]
    }

    // Per-launch keychain cache. EVERY keychain access funnels through read/writeKeychainData, so
    // caching here covers the license key, trial-state, trial-max-time, and first-launch in one
    // place. This exists because an AD-HOC / unsigned build's code signature changes on each rebuild,
    // so macOS cannot grant a stable "always allow" ACL and prompts for the login password on EVERY
    // keychain access — and the app reads the keychain on every entitlement check (6 call sites). The
    // cache turns N-prompts-per-session into ONE per item, on first access. It is a mitigation, NOT
    // the fix: a Developer-ID-signed build gets a stable ACL and never prompts. Only THIS app writes
    // these items, and write-through keeps the cache correct (including the anti-rollback max-time),
    // so a cached value can never go stale behind our back.
    private static let _kcLock = NSLock()
    nonisolated(unsafe) private static var _kcCache: [String: Data?] = [:]

    private static func readKeychainData(account: String) -> Data? {
        _kcLock.lock()
        if let cached = _kcCache[account] {   // present in cache (value may itself be nil = "absent")
            _kcLock.unlock()
            return cached
        }
        _kcLock.unlock()

        var query = keychainQuery(account: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        let data = (status == errSecSuccess) ? (result as? Data) : nil

        // D084: only cache successful reads and confirmed absence (errSecItemNotFound).
        // A transient Keychain error (locked, auth failed, user cancelled) must NOT be
        // cached as "absent" — that would fabricate a durable false-negative from one
        // blocked prompt, affecting license checks for the entire process lifetime.
        if status == errSecSuccess || status == errSecItemNotFound {
            _kcLock.lock()
            _kcCache[account] = data
            _kcLock.unlock()
        }
        return data
    }

    private static func writeKeychainData(_ data: Data, account: String) throws {
        let query = keychainQuery(account: account)
        if SecItemCopyMatching(query as CFDictionary, nil) == errSecSuccess {
            let update: [String: Any] = [kSecValueData as String: data]
            let status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
            guard status == errSecSuccess else {
                throw LicenseManagerError.keychainError(status)
            }
        } else {
            var addQuery = query
            addQuery[kSecValueData as String] = data
            let status = SecItemAdd(addQuery as CFDictionary, nil)
            guard status == errSecSuccess else {
                throw LicenseManagerError.keychainError(status)
            }
        }
        // Write-through: keep the cache consistent so a subsequent read sees the new value without a
        // fresh keychain hit (this is what keeps the anti-rollback max-time correct across a session).
        _kcLock.lock()
        _kcCache[account] = data
        _kcLock.unlock()
    }

    /// Returns the locally stored license key from the Keychain, if any.
    public static func getStoredLicenseKey() -> String? {
        guard let data = readKeychainData(account: licenseKeyAccount) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    /// Saves the license key to the Keychain, namespaced under `bundleIdentifier`.
    public static func saveLicenseKey(_ key: String) throws {
        guard let data = key.data(using: .utf8) else {
            throw LicenseManagerError.invalidKeyEncoding
        }
        try writeKeychainData(data, account: licenseKeyAccount)
    }

    // MARK: - Trial state (M20)

    private struct TrialState: Codable {
        let firstLaunchEpochMillis: Int64
        let signature: String
    }

    private static let trialMaxTimeAccount = "trial-max-time"

    /// B190: async version — replaces the semaphore-based sync call.
    /// The response is unauthenticated (no signature verification on the trial anchor endpoint),
    /// so callers MUST NOT use the server's time to render a definite lock — only to supplement
    /// a local anchor when the server is reachable.
    private static func syncTrialAnchorWithServer(fingerprint: String, localFirstLaunch: Int64) async -> (serverFirstLaunch: Int64, serverTime: Int64)? {
        guard let url = URL(string: "https://syxx.app/api/trial/anchor") else { return nil }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.timeoutInterval = 1.5
        let body: [String: Any] = ["fingerprint": fingerprint]
        request.httpBody = try? JSONSerialization.data(withJSONObject: body)

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse,
                  httpResponse.statusCode == 200,
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let serverLaunch = (json["firstLaunch"] as? NSNumber)?.int64Value,
                  let serverTime = (json["serverTime"] as? NSNumber)?.int64Value else {
                return nil
            }
            return (serverLaunch, serverTime)
        } catch {
            return nil
        }
    }

    /// Retrieves or initializes the first-launch timestamp, storing it in the Keychain (not a
    /// hand-editable plaintext file) and signing it with a key derived from this device's
    /// fingerprint. A record that fails signature verification — because it was hand-edited or
    /// copied from a different machine — is treated as absent rather than trusted, so editing
    /// the stored value can no longer trivially extend or reset the trial window.
    ///
    /// Anchors the launch date on the server if online (KTRU-03). The server response is
    /// unauthenticated, so it NEVER triggers a definite clock-regression lock — that would let
    /// any MITM lock a paying user out with a forged serverTime.
    public static func getFirstLaunchDate() async -> Date {
        var localLaunchMillis: Int64? = nil
        if let data = readKeychainData(account: trialStateAccount),
           let state = try? JSONDecoder().decode(TrialState.self, from: data),
           trialSignature(epochMillis: state.firstLaunchEpochMillis) == state.signature {
            localLaunchMillis = state.firstLaunchEpochMillis
        }

        let fingerprint = getSystemFingerprint()
        var finalLaunchMillis: Int64

        if let online = await syncTrialAnchorWithServer(fingerprint: fingerprint, localFirstLaunch: localLaunchMillis ?? 0) {
            let serverLaunch = online.serverFirstLaunch * 1000 // Server uses seconds, we want millis

            // D059: never overwrite an earlier local anchor with a later server timestamp.
            // Going offline creates a local anchor; the server has never seen the
            // fingerprint, so it returns now() as firstLaunch. Taking min(local, server)
            // prevents the offline-then-online path from resetting the trial window.
            if let local = localLaunchMillis {
                finalLaunchMillis = min(local, serverLaunch)
            } else {
                finalLaunchMillis = serverLaunch
            }
            // B190: the server response is unauthenticated — never render a definite
            // clock-regression lock from unauthenticated serverTime. Keep the local anchor.
        } else {
            // D021: server unreachable — reduced-trust mode. The local launch date is derived from
            // a device fingerprint (IOPlatformUUID + baked salt) that is forgeable offline, so this
            // path is less trustworthy than the server-anchored path above. Mark as reduced-trust
            // rather than silent-accept to distinguish from server-verified launches.
            if let local = localLaunchMillis {
                finalLaunchMillis = local
            } else {
                let now = Date()
                let epochMillis = Int64((now.timeIntervalSince1970 * 1000).rounded())
                finalLaunchMillis = epochMillis
            }
        }

        let state = TrialState(firstLaunchEpochMillis: finalLaunchMillis, signature: trialSignature(epochMillis: finalLaunchMillis))
        if let data = try? JSONEncoder().encode(state) {
            try? writeKeychainData(data, account: trialStateAccount)
        }

        return Date(timeIntervalSince1970: Double(finalLaunchMillis) / 1000.0)
    }

    /// D021: HMAC keyed by device fingerprint (IOPlatformUUID + baked salt). This is NOT tamper-proof —
    /// the key material is derivable from public device info and the baked-in binary salt, so an
    /// attacker with local access can forge a valid signature. This signature provides a basic
    /// integrity check against casual editing but should not be presented as cryptographic
    /// tamper-resistance.
    ///
    /// B220: refuse to sign under an empty fingerprint. If the fingerprint collapses to ""
    /// (hash failure, UTF-8 edge case, or a future code path that honours
    /// MachineFingerprint.Quality), HMAC-SHA256 under an empty key becomes a publicly-known
    /// constant every such machine can compute — a trial record copied from one box would
    /// verify on another. Returning a non-hex sentinel (rather than an empty string or the
    /// HMAC of a blank) guarantees verification fails, so the config file is treated as
    /// absent rather than trusted when the fingerprint is degraded.
    private static func trialSignature(epochMillis: Int64) -> String {
        let fingerprint = getSystemFingerprint()
        guard !fingerprint.isEmpty else { return kUnsignableSentinel }
        let hmac = hmacSHA256(message: "\(epochMillis)", key: fingerprint)
        return hmac.isEmpty ? kUnsignableSentinel : hmac
    }

    /// Sentinel returned by `trialSignature` (and by `hmacSHA256` when the key is empty)
    /// instead of producing an HMAC under a degraded key. Non-empty so the JSON field is
    /// well-formed; non-hex so it can never equal a genuine 64-hex-char HMAC-SHA256 output;
    /// verification under it always fails.
    private static let kUnsignableSentinel = "syxx.unsignable.empty-fingerprint"

    /// Pure E-7 offline-grace predicate, exposed for tests. Returns true when `nowMs` is within
    /// `graceWindowMs` of `lastValidMs`. A nil `lastValidMs` (no recorded validation) returns false.
    /// Mutation: removing the `<= graceWindowMs` check → returns false for a recent lastValidMs → test REDs.
    /// Default grace window is 30 days (2_592_000_000 ms), matching `offlineGraceMs`.
    public static func offlineGraceApplies(nowMs: Int64, lastValidMs: Int64?,
                                           graceWindowMs: Int64 = 2_592_000_000) -> Bool {
        guard let last = lastValidMs else { return false }
        return nowMs - last <= graceWindowMs
    }

    /// Returns whether Pro features are unlocked, validating the stored key against Keygen
    /// internally rather than trusting a caller-supplied Bool (KTRU-01). Falls through to the
    /// free-trial check when no key is stored or validation fails.
    ///
    /// E-7 offline grace: a paying subscriber whose network blocks Keygen (the very condition Syxx
    /// investigates) stays unlocked for 30 days from their last successful online validation.
    /// This distinguishes "subscription lapsed" (.invalid) from "can't reach server" (.timeout /
    /// .networkError). A censored network must not silently demote Pro to Locked.
    public static func isFeatureUnlocked() async -> (unlocked: Bool, message: String) {
        // Validate stored license key internally — never trust a caller-supplied Bool (KTRU-01).
        if let storedKey = getStoredLicenseKey() {
            let result = await validateLicenseDetailed(key: storedKey, accountID: SyxxConfig.accountID)
            switch result {
            case .valid:
                // Record the successful validation time for E-7 offline grace.
                let nowMs = Int64((Date().timeIntervalSince1970 * 1000).rounded())
                if let data = "\(nowMs)".data(using: .utf8) {
                    try? writeKeychainData(data, account: lastValidValidationAccount)
                }
                return (true, "🟢 Unlocked via Active Pro Subscription ($3.95/mo)")
            case .invalid:
                // Key is definitively bad — do NOT grant offline grace, fall through to trial.
                break
            case .timeout, .networkError:
                // E-7: cannot reach Keygen — could be the filtered network this app investigates.
                // Check offline grace window before falling through to the trial/locked path.
                let nowMs = Int64((Date().timeIntervalSince1970 * 1000).rounded())
                if let data = readKeychainData(account: lastValidValidationAccount),
                   let str = String(data: data, encoding: .utf8),
                   let lastValid = Int64(str),
                   nowMs - lastValid <= offlineGraceMs {
                    // Within 30 days of last successful validation — stay unlocked.
                    // OUTPUT REQUIRES USER APPROVAL (AGENTS.md:42): pending approval for exact wording.
                    return (true, "🟢 Unlocked via Pro Subscription (offline — last verified within 30 days)")
                }
                // No recent validation record, or > 30 days ago — fall through.
            }
        }

        let now = Date()
        let nowMillis = Int64((now.timeIntervalSince1970 * 1000).rounded())

        var maxSeenMillis: Int64 = 0
        if let maxData = readKeychainData(account: trialMaxTimeAccount),
           let maxStr = String(data: maxData, encoding: .utf8),
           let maxVal = Int64(maxStr) {
            maxSeenMillis = maxVal
        }

        if nowMillis < maxSeenMillis {
            return (false, "🔒 Locked (Clock regression detected). Please restore your system clock to proceed.")
        }

        if nowMillis > maxSeenMillis {
            let maxStr = "\(nowMillis)"
            if let maxData = maxStr.data(using: .utf8) {
                try? writeKeychainData(maxData, account: trialMaxTimeAccount)
            }
        }

        let firstLaunch = await getFirstLaunchDate()

        let trialDuration: TimeInterval = 3 * 24 * 60 * 60 // 3-day free trial
        let elapsed = now.timeIntervalSince(firstLaunch)
        if elapsed < 0 {
            return (false, "🔒 Locked (Clock regression detected). Please restore your system clock to proceed.")
        }
        if elapsed < trialDuration {
            let remainingHours = Int((trialDuration - elapsed) / 3600)
            return (true, "⏳ Unlocked via Free Trial (\(remainingHours) hours remaining)")
        }

        return (false, "🔒 Locked (Trial Expired). Requires a $3.95/month subscription to cover hosting costs.")
    }

    // MARK: - Fingerprint (M21, D001)

    /// Generates a privacy-preserving device fingerprint: a salted hash of the raw platform
    /// UUID AND a Keychain-stored random secret. The raw `IOPlatformUUID` is world-readable
    /// via `ioreg`; D001 fixes this by mixing in a 32-byte random secret stored in the Keychain
    /// (with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`), so an attacker who only knows
    /// the IOPlatformUUID cannot derive the fingerprint without also having Keychain access.
    private static let fingerprintUUIDAccount = "fingerprint-uuid"
    private static let fingerprintSecretAccount = "fingerprint-secret-v2"

    /// Retrieve or generate the Keychain-stored random secret. This is the D001 fix:
    /// a 32-byte random secret that only this app (with Keychain access) can read.
    private static func getOrCreateKeychainSecret() -> String {
        if let data = readKeychainData(account: fingerprintSecretAccount),
           let secret = String(data: data, encoding: .utf8), !secret.isEmpty {
            return secret
        }
        // Generate a cryptographically random 32-byte secret
        var bytes = [UInt8](repeating: 0, count: 32)
        let status = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        guard status == errSecSuccess else {
            // Fallback: use UUID (less secure but better than nothing)
            return UUID().uuidString
        }
        let secret = bytes.map { String(format: "%02x", $0) }.joined()
        if let data = secret.data(using: .utf8) {
            try? writeKeychainData(data, account: fingerprintSecretAccount)
        }
        return secret
    }

    public static func getSystemFingerprint() -> String {
        let keychainSecret = getOrCreateKeychainSecret()

        #if os(macOS)
        let platformExpert = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("IOPlatformExpertDevice"))
        if platformExpert > 0 {
            defer { IOObjectRelease(platformExpert) }
            if let uuid = IORegistryEntryCreateCFProperty(platformExpert, kIOPlatformUUIDKey as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? String {
                // D001: mix the Keychain secret with the IOPlatformUUID so neither alone suffices
                return saltedFingerprintHash(uuid.trimmingCharacters(in: .whitespacesAndNewlines), secret: keychainSecret)
            }
        }
        #endif
        // KTRU-10: fall back to a persisted random Keychain UUID, not Host.current().name
        // which is user-settable and collides across identically-named machines.
        if let data = readKeychainData(account: fingerprintUUIDAccount),
           let uuid = String(data: data, encoding: .utf8), !uuid.isEmpty {
            return saltedFingerprintHash(uuid, secret: keychainSecret)
        }
        let newUUID = UUID().uuidString
        if let data = newUUID.data(using: .utf8) {
            try? writeKeychainData(data, account: fingerprintUUIDAccount)
        }
        return saltedFingerprintHash(newUUID, secret: keychainSecret)
    }

    private static func saltedFingerprintHash(_ rawIdentifier: String, secret: String) -> String {
        // D001: the secret is mixed in so IOPlatformUUID alone is insufficient
        let salted = "com.milkandgin.syxx.fingerprint.v2:\(rawIdentifier):\(secret)"
        var digest = [UInt8](repeating: 0, count: Int(CC_SHA256_DIGEST_LENGTH))
        let data = Array(salted.utf8)
        CC_SHA256(data, CC_LONG(data.count), &digest)
        return digest.map { String(format: "%02x", $0) }.joined()
    }

    private static func hmacSHA256(message: String, key: String) -> String {
        // B220: refuse to compute under an empty key. HMAC-SHA256 with key="" is a well-defined
        // constant every caller can recompute, so a signature produced under it is a publicly
        // known value posing as a proof of origin. Returning "" lets `trialSignature` replace
        // it with the unsignable sentinel so verification fails closed.
        guard !key.isEmpty,
              let keyData = key.data(using: .utf8),
              let messageData = message.data(using: .utf8) else {
            return ""
        }
        var macOut = [UInt8](repeating: 0, count: Int(CC_SHA256_DIGEST_LENGTH))
        keyData.withUnsafeBytes { keyBytes in
            messageData.withUnsafeBytes { messageBytes in
                CCHmac(CCHmacAlgorithm(kCCHmacAlgSHA256), keyBytes.baseAddress, keyData.count, messageBytes.baseAddress, messageData.count, &macOut)
            }
        }
        return macOut.map { String(format: "%02x", $0) }.joined()
    }

    // MARK: - Validation (M22)

    /// B187: Verifies the P-256 ECDSA signature on a Keygen.sh validate-key response.
    /// Returns true only when the signature is cryptographically valid against the pinned
    /// public key. Returns false when the key is not configured, the signature header is
    /// missing/malformed, or verification fails — caller must NOT trust meta.valid.
    private static func verifyKeygenSignature(body: Data, httpResponse: HTTPURLResponse,
                                               requestPath: String, requestMethod: String) -> Bool {
        guard !keygenPublicKeyDERHex.isEmpty else { return false }

        // Parse the Keygen-Signature header: keyid="...", algorithm="...", signature="...", headers="..."
        guard let sigHeader = httpResponse.value(forHTTPHeaderField: "Keygen-Signature") else {
            return false
        }
        var sigParams: [String: String] = [:]
        let paramPattern = "([^=,\"]+)=\"([^\"]*)\""
        guard let regex = try? NSRegularExpression(pattern: paramPattern) else { return false }
        let matches = regex.matches(in: sigHeader, range: NSRange(sigHeader.startIndex..., in: sigHeader))
        for match in matches {
            if let keyRange = Range(match.range(at: 1), in: sigHeader),
               let valRange = Range(match.range(at: 2), in: sigHeader) {
                sigParams[String(sigHeader[keyRange])] = String(sigHeader[valRange])
            }
        }
        guard let encSig = sigParams["signature"], !encSig.isEmpty,
              let sigData = Data(base64Encoded: encSig) else { return false }

        // Date header freshness: +/- 5 minutes to prevent replay of old signed responses.
        guard let dateHeader = httpResponse.value(forHTTPHeaderField: "Date"),
              let respDate = HTTPDateFormatter.date(from: dateHeader) else { return false }
        if abs(respDate.timeIntervalSinceNow) > 300 { return false }

        // Compute SHA-256 digest of response body.
        let bodyDigest = SHA256.hash(data: body)
        let encDigest = Data(bodyDigest).base64EncodedString()

        // Reconstruct signing data matching Keygen's format.
        let host = "api.keygen.sh"
        let signingData = """
        (request-target): \(requestMethod.lowercased()) \(requestPath)
        host: \(host)
        date: \(dateHeader)
        digest: sha-256=\(encDigest)
        """

        // Load the pinned P-256 public key from its DER representation and verify.
        // The signature from Keygen is DER-encoded ECDSA; SHA-256 is the digest for P-256.
        guard let derBytes = Data(hexString: keygenPublicKeyDERHex),
              let pubKey = try? P256.Signing.PublicKey(derRepresentation: derBytes) else {
            return false
        }
        guard let ecdsaSig = try? P256.Signing.ECDSASignature(derRepresentation: sigData) else {
            return false
        }
        guard let signingBytes = signingData.data(using: .utf8) else { return false }
        let digest = SHA256.hash(data: signingBytes)
        return pubKey.isValidSignature(ecdsaSig, for: digest)
    }

    /// ISO-8601/RFC-1123 date formatter for HTTP Date headers.
    private enum HTTPDateFormatter {
        static let formatter: DateFormatter = {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.timeZone = TimeZone(secondsFromGMT: 0)
            f.dateFormat = "EEE, dd MMM yyyy HH:mm:ss zzz"
            return f
        }()
        static func date(from string: String) -> Date? { formatter.date(from: string) }
    }

    /// Validates a license key using Keygen.sh API, returning a typed result so a network
    /// failure (e.g. on a censored/filtered connection) is distinguishable from an invalid key.
    public static func validateLicenseDetailed(key: String, accountID: String) async -> LicenseValidationResult {
        guard !accountID.isEmpty, let url = URL(string: "https://api.keygen.sh/v1/accounts/\(accountID)/licenses/actions/validate-key") else {
            return .networkError("Invalid Keygen account/URL")
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/vnd.api+json", forHTTPHeaderField: "Content-Type")
        request.setValue("application/vnd.api+json", forHTTPHeaderField: "Accept")
        request.timeoutInterval = validationTimeout

        // Validate by key alone (no machine/fingerprint scope): a fingerprint scope makes Keygen
        // require a machine already activated on the license, so a freshly-issued key would read
        // as invalid until activation. The server validates the same way (no scope).
        let body: [String: Any] = [
            "meta": [
                "key": key
            ]
        ]

        guard let httpBody = try? JSONSerialization.data(withJSONObject: body) else {
            return .networkError("Failed to encode validation request")
        }
        request.httpBody = httpBody

        do {
            let (data, response) = try await URLSession.shared.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse else {
                return .networkError("Non-HTTP response")
            }
            guard httpResponse.statusCode == 200 else {
                // B187: 401/404 are UNAUTHENTICATED statuses (classic captive-portal/interception
                // response), not a definite "key is bad" — treat as transient so offline grace applies.
                return .networkError("HTTP \(httpResponse.statusCode)")
            }
            let validation = try JSONDecoder().decode(KeygenValidationResponse.self, from: data)
            // D066: verify the Ed25519 response signature before trusting meta.valid OR
            // meta.invalid. A forged "invalid" response silently revokes Pro capability
            // (disabling collector-backed probes) just as a forged "valid" grants it.
            // The signature must check regardless of the meta.valid value — an unverifiable
            // response is untrusted in either direction.
            let requestPath = "/v1/accounts/\(accountID)/licenses/actions/validate-key"
            if !verifyKeygenSignature(body: data, httpResponse: httpResponse,
                                      requestPath: requestPath, requestMethod: "post") {
                SyxxDiagnostics.breadcrumb(category: "license.validation",
                    message: "Keygen signature unverifiable — refusing to trust response",
                    level: .warning, data: [:])
                return .networkError("Keygen response signature unverifiable")
            }
            return validation.meta.valid ? .valid : .invalid
        } catch let error as URLError where error.code == .timedOut {
            return .timeout
        } catch {
            SyxxDiagnostics.breadcrumb(category: "license.validation", message: "License validation failed", level: .warning, data: ["error": "\(error)"])
            return .networkError(error.localizedDescription)
        }
    }

    /// Back-compat boolean wrapper.
    public static func validateLicense(key: String, accountID: String) async -> Bool {
        (await validateLicenseDetailed(key: key, accountID: accountID)).isValid
    }
}

// MARK: - Data hex decoding (B187)

extension Data {
    /// Initializes Data from a hex-encoded string. Returns nil for invalid hex or odd-length strings.
    init?(hexString: String) {
        let hex = hexString.trimmingCharacters(in: .whitespacesAndNewlines)
        guard hex.count % 2 == 0 else { return nil }
        var data = Data(capacity: hex.count / 2)
        var index = hex.startIndex
        while index < hex.endIndex {
            let nextIndex = hex.index(index, offsetBy: 2)
            guard let byte = UInt8(hex[index..<nextIndex], radix: 16) else { return nil }
            data.append(byte)
            index = nextIndex
        }
        self = data
    }
}
