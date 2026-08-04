import Foundation
import CryptoKit

/// Custom auto-update service (G-1). Sparkle-inspired but fully original.
///
/// Flow:
/// 1. Fetch appcast from /api/appcast?platform=mac
/// 2. Verify the Ed25519 signature against the PINNED update public key
/// 3. Decode the manifest FROM the signed bytes and compare version to current
/// 4. If update available, download the package from a pinned host
/// 5. Verify the signed SHA-256 hash of the artifact
/// 6. Install the update (replace app bundle)
///
/// Every one of those steps is a refusal point. An update that cannot be proven
/// authentic is not an update: see `AppcastVerifier` for why TLS is not a substitute
/// for the signature here.
@MainActor
public final class UpdateService: ObservableObject {
    public static let shared = UpdateService()

    @Published public var state: UpdateState = .idle
    @Published public var progress: Double = 0.0
    /// E111: track when the last successful check occurred. Lives on the service (not the view)
    /// so it persists across panel open/close cycles and reflects actual check history.
    @Published public var lastChecked: Date? = nil

    public enum UpdateState: Equatable {
        case idle
        case checking
        case upToDate
        case available(version: String, releaseNotes: String)
        case downloading(progress: Double)
        case installing
        case failed(String)
    }

    private let baseURL: URL
    private let session: URLSession
    private var downloadTask: URLSessionDownloadTask?

    /// The manifest whose signature verified during `checkForUpdates`. `downloadAndInstall`
    /// acts on THIS and never on a second, unverified fetch — the previous implementation
    /// re-fetched the appcast at download time and verified nothing, so the manifest that
    /// gated the offer and the manifest that supplied the download URL were different
    /// documents. That is a TOCTOU an attacker who can serve the appcast wins for free.
    private var verifiedManifest: AppcastManifest?

    private init() {
        baseURL = SyxxConfig.dimensionBaseURL
        let config = URLSessionConfiguration.default
        config.timeoutIntervalForRequest = 30
        config.timeoutIntervalForResource = 300
        self.session = URLSession(configuration: config)
    }

    /// Check for updates by fetching the appcast.
    public func checkForUpdates() async {
        state = .checking
        verifiedManifest = nil
        do {
            // F021: forward the early-access opt-in to the appcast endpoint so the server
            // can serve beta-channel manifests when available. Without this the toggle
            // persisted in UserDefaults but never reached the update pipeline.
            let earlyAccess = UserDefaults.standard.bool(forKey: "syxx.earlyAccessOptIn")
            let channel = earlyAccess ? "beta" : "stable"
            let url = URL(string: "\(baseURL)/api/appcast?platform=mac&channel=\(channel)")!
            let (data, response) = try await session.data(from: url)

            guard let httpResp = response as? HTTPURLResponse else {
                throw UpdateError.invalidResponse
            }

            if httpResp.statusCode == 404 {
                state = .upToDate
                return
            }

            guard httpResp.statusCode == 200 else {
                throw UpdateError.serverError(httpResp.statusCode)
            }

            // Verify FIRST, then decode from the bytes the signature actually covered.
            // Decoding first and verifying afterwards leaves a parser-differential seam:
            // the fields the app acts on would come from one parse and the signed bytes
            // from another, and a manifest crafted to be read differently by the two
            // slips through.
            let outcome = AppcastVerifier.verifiedManifest(rawManifest: data)
            guard outcome.verification == .verified, let manifest = outcome.manifest else {
                throw UpdateError.invalidSignature
            }

            verifiedManifest = manifest

            // Compare versions
            let currentVersion = SyxxConfig.version
            if isVersion(manifest.version, newerThan: currentVersion) {
                state = .available(version: manifest.version, releaseNotes: manifest.releaseNotes)
            } else {
                state = .upToDate
            }
        } catch {
            verifiedManifest = nil
            state = .failed(error.localizedDescription)
        }
    }

    /// Download and install the update.
    public func downloadAndInstall() async {
        guard case .available(let version, _) = state else { return }

        state = .downloading(progress: 0.0)
        do {
            // Act on the manifest whose signature verified, not a fresh unverified fetch.
            guard let manifest = verifiedManifest, manifest.version == version else {
                throw UpdateError.invalidSignature
            }

            guard let package = manifest.packages.first else {
                throw UpdateError.noPackage
            }

            let downloadURL = try Self.validatedDownloadURL(for: package)

            // Download with progress tracking
            let tempURL = try await downloadWithProgress(downloadURL)

            // Verify SHA-256 against the SIGNED hash before any install step.
            let hash = try computeSHA256(tempURL)
            guard hash.caseInsensitiveCompare(package.sha256) == .orderedSame else {
                try? FileManager.default.removeItem(at: tempURL)
                throw UpdateError.hashMismatch
            }
            // Size from signed metadata must match the bytes on disk.
            let attrs = try FileManager.default.attributesOfItem(atPath: tempURL.path)
            let byteCount = (attrs[.size] as? NSNumber)?.intValue ?? -1
            guard byteCount == package.size, byteCount > 0 else {
                try? FileManager.default.removeItem(at: tempURL)
                throw UpdateError.hashMismatch
            }

            // Install using the authenticated package type, not a guessed extension alone.
            state = .installing
            try installPackage(tempURL, packageType: package.type)

            // Clean up
            try? FileManager.default.removeItem(at: tempURL)

        } catch {
            state = .failed(error.localizedDescription)
        }
    }

    /// The gate between a signed manifest and an actual download.
    ///
    /// A signed manifest that points at an unverified binary on an arbitrary host is the
    /// same hole one step later, so every constraint is enforced here and every failure is
    /// a refusal, never a warning:
    ///  • the artifact must carry a full 64-hex-character SHA-256 in the SIGNED manifest,
    ///    otherwise there is nothing to check the download against;
    ///  • size must be a positive byte count from the signed metadata;
    ///  • package type must be an allowed installer type and must match the URL extension;
    ///  • the download URL must pass `RemoteUrlPolicy` — https, no credentials, port 443,
    ///    and a host under one of the product's own registrable domains. Without this the
    ///    manifest can redirect the client anywhere.
    nonisolated static func validatedDownloadURL(for package: AppcastPackage) throws -> URL {
        let hash = package.sha256.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let isFullSHA256 = hash.count == 64 && hash.allSatisfy { $0.isHexDigit }
        guard isFullSHA256 else {
            // PENDING COPY APPROVAL (AGENTS.md §4.7): reusing the existing
            // "Download corrupted (hash mismatch)" string. Proposed replacement wording is
            // listed in DELTAS.md; refusing with an imprecise message beats installing an
            // artifact nothing can verify.
            throw UpdateError.hashMismatch
        }
        guard package.size > 0 else {
            throw UpdateError.hashMismatch
        }

        let pkgType = package.type.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard Self.allowedPackageTypes.contains(pkgType) else {
            throw UpdateError.unsupportedFormat
        }

        guard RemoteUrlPolicy.evaluate(package.url).isAllowed,
              let url = URL(string: package.url) else {
            throw UpdateError.invalidURL
        }
        // Reject signed type/extension mismatch before any download bytes are fetched.
        guard url.pathExtension.lowercased() == pkgType else {
            throw UpdateError.unsupportedFormat
        }
        return url
    }

    /// Installer types the authenticated manifest may name. Keep in lockstep with
    /// Server/syxx_server/appcast.py `_PLATFORM_PACKAGE_TYPE` and the C# twin.
    nonisolated static let allowedPackageTypes: Set<String> = ["dmg", "zip", "deb"]

    /// Cancel any in-progress download.
    public func cancel() {
        downloadTask?.cancel()
        downloadTask = nil
        if case .downloading = state {
            state = .idle
        }
    }

    // MARK: - Private helpers

    private func downloadWithProgress(_ url: URL) async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            let task = self.session.downloadTask(with: url) { tempURL, _, error in
                if let error = error {
                    continuation.resume(throwing: error)
                    return
                }
                guard let tempURL = tempURL else {
                    continuation.resume(throwing: UpdateError.downloadFailed("no file returned"))
                    return
                }
                let destURL = FileManager.default.temporaryDirectory
                    .appendingPathComponent(UUID().uuidString)
                    .appendingPathExtension(url.pathExtension)
                do {
                    try FileManager.default.moveItem(at: tempURL, to: destURL)
                    continuation.resume(returning: destURL)
                } catch {
                    continuation.resume(throwing: error)
                }
            }
            task.resume()
            self.downloadTask = task
        }
    }

    private func computeSHA256(_ url: URL) throws -> String {
        let data = try Data(contentsOf: url)
        let hash = SHA256.hash(data: data)
        return hash.compactMap { String(format: "%02x", $0) }.joined()
    }

    private func installPackage(_ url: URL, packageType: String) throws {
        let type = packageType.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let ext = url.pathExtension.lowercased()
        // Authenticated type and on-disk extension must agree before the installer runs.
        guard type == ext, Self.allowedPackageTypes.contains(type) else {
            throw UpdateError.unsupportedFormat
        }

        if type == "dmg" {
            try installDMG(url)
        } else if type == "zip" {
            try installZIP(url)
        } else {
            // .deb is not a macOS installer path.
            throw UpdateError.unsupportedFormat
        }
    }

    private func installDMG(_ url: URL) throws {
        // Mount the DMG
        let mountPoint = "/tmp/SyxxUpdate-\(UUID().uuidString)"
        try FileManager.default.createDirectory(atPath: mountPoint, withIntermediateDirectories: true)

        let mountProcess = Process()
        mountProcess.executableURL = URL(fileURLWithPath: "/usr/bin/hdiutil")
        mountProcess.arguments = ["attach", "-nobrowse", "-mountpoint", mountPoint, url.path]
        let pipe = Pipe()
        mountProcess.standardOutput = pipe
        mountProcess.standardError = pipe
        try mountProcess.run()
        mountProcess.waitUntilExit()

        guard mountProcess.terminationStatus == 0 else {
            let output = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
            throw UpdateError.installFailed("DMG mount failed: \(output)")
        }

        // Find the .app in the mounted volume
        let contents = try FileManager.default.contentsOfDirectory(atPath: mountPoint)
        guard let appName = contents.first(where: { $0.hasSuffix(".app") }) else {
            throw UpdateError.installFailed("No .app found in DMG")
        }

        let sourceApp = URL(fileURLWithPath: "\(mountPoint)/\(appName)")
        let destApp = URL(fileURLWithPath: Bundle.main.bundlePath)

        // Replace the app bundle
        let fm = FileManager.default
        let backupURL = destApp.appendingPathExtension("backup")

        // Remove any existing backup
        try? fm.removeItem(at: backupURL)

        // Move current app to backup
        try fm.moveItem(at: destApp, to: backupURL)

        // Copy new app
        do {
            try fm.copyItem(at: sourceApp, to: destApp)
        } catch {
            // Restore backup on failure
            try? fm.moveItem(at: backupURL, to: destApp)
            throw UpdateError.installFailed("Copy failed: \(error.localizedDescription)")
        }

        // Unmount
        let unmountProcess = Process()
        unmountProcess.executableURL = URL(fileURLWithPath: "/usr/bin/hdiutil")
        unmountProcess.arguments = ["detach", mountPoint]
        try unmountProcess.run()
        unmountProcess.waitUntilExit()

        // Clean up backup and mount point
        try? fm.removeItem(at: backupURL)
        try? fm.removeItem(at: URL(fileURLWithPath: mountPoint))
    }

    private func installZIP(_ url: URL) throws {
        // Unzip to temp directory, then copy .app
        let tempDir = FileManager.default.temporaryDirectory
            .appendingPathComponent("SyxxUpdate-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        process.arguments = ["-q", url.path, "-d", tempDir.path]
        try process.run()
        process.waitUntilExit()

        guard process.terminationStatus == 0 else {
            throw UpdateError.installFailed("Unzip failed")
        }

        // Find the .app
        let contents = try FileManager.default.contentsOfDirectory(atPath: tempDir.path)
        guard let appName = contents.first(where: { $0.hasSuffix(".app") }) else {
            throw UpdateError.installFailed("No .app found in ZIP")
        }

        let sourceApp = tempDir.appendingPathComponent(appName)
        let destApp = URL(fileURLWithPath: Bundle.main.bundlePath)

        // Replace
        let fm = FileManager.default
        let backupURL = destApp.appendingPathExtension("backup")
        try? fm.removeItem(at: backupURL)
        try fm.moveItem(at: destApp, to: backupURL)

        do {
            try fm.copyItem(at: sourceApp, to: destApp)
        } catch {
            try? fm.moveItem(at: backupURL, to: destApp)
            throw UpdateError.installFailed("Copy failed: \(error.localizedDescription)")
        }

        // Clean up
        try? fm.removeItem(at: backupURL)
        try? fm.removeItem(at: tempDir)
    }

    private func isVersion(_ v1: String, newerThan v2: String) -> Bool {
        let parts1 = v1.split(separator: ".").compactMap { Int($0) }
        let parts2 = v2.split(separator: ".").compactMap { Int($0) }

        for i in 0..<max(parts1.count, parts2.count) {
            let p1 = i < parts1.count ? parts1[i] : 0
            let p2 = i < parts2.count ? parts2[i] : 0
            if p1 > p2 { return true }
            if p1 < p2 { return false }
        }
        return false
    }
}

// MARK: - Appcast signature verification

/// Tri-state outcome of appcast verification. Never a bare Bool: "refused because this build
/// carries no trust anchor" and "refused because the signature does not cover this content"
/// are different facts and a caller that cannot tell them apart cannot report honestly.
///
/// Only `.verified` may lead to an install. Every other case is a refusal.
public enum AppcastVerification: String, Equatable, Sendable {
    /// The signature is a valid Ed25519 signature, by a pinned key, over exactly these bytes.
    case verified
    /// This build has no usable pinned update key, so nothing can be proven. Refuse.
    case refusedNoPinnedKey
    /// The body is not a JSON object, or contains a value with no cross-platform-stable
    /// canonical form, or does not decode to a manifest.
    case refusedMalformedManifest
    /// No `signature` field, empty, or not a JSON string.
    case refusedSignatureMissing
    /// The `signature` field is present but is not 64 bytes of base64.
    case refusedSignatureMalformed
    /// A well-formed signature that does not verify over these bytes with a pinned key.
    /// Includes replayed signatures from other manifests and signatures by unpinned keys.
    case refusedSignatureMismatch
}

/// Verifies the Ed25519 signature on an appcast manifest against a key pinned in this binary.
///
/// WHY THIS IS NOT OPTIONAL. Until 2026-07-29 both clients decoded the signature, rebuilt the
/// canonical payload, threw both away and returned true, with the comment "trust HTTPS transport
/// for now". That is the exact reasoning this product exists to refute. Syxx detects DNS
/// tampering, hijacking and spoofing; it cannot assume the network or the origin is honest for
/// its own update channel. TLS authenticates the *transport*; the signature is what defends
/// against a compromised or impersonated *origin*, which is precisely the threat model. Anyone
/// able to serve the appcast could push an arbitrary update to both platforms.
///
/// FAIL CLOSED, without exception. There is deliberately no "if no key is configured, allow"
/// path, no debug bypass, and no runtime key fetch. Fetching the verification key from the same
/// origin that serves the manifest is not verification.
public enum AppcastVerifier {

    /// Ed25519 public keys, base64 raw 32 bytes, that may sign an update manifest.
    ///
    /// Pinned in this binary — never fetched from the manifest origin. The value below is the
    /// DEV-ONLY update public key under `Server/dev-keys/update-signing.pub.b64` (see that
    /// directory's README). It is deliberately NOT the collector / leak identity.
    ///
    /// Production key rotation is owner-gated: replace this pin on macOS, Windows, and Linux
    /// together only after the owner provisions a production update-signing key offline.
    /// Do NOT populate or rotate this by fetching from the server, and do NOT add a bypass.
    public static let pinnedPublicKeysBase64: [String] = [
        "6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=",
    ]

    struct ManifestOutcome {
        let verification: AppcastVerification
        /// Non-nil only when `verification == .verified`. Decoded from the SIGNED bytes.
        let manifest: AppcastManifest?
    }

    /// Verifies `rawManifest` and, only on success, decodes the manifest from the signed bytes.
    static func verifiedManifest(
        rawManifest: Data,
        pinnedPublicKeysBase64: [String] = AppcastVerifier.pinnedPublicKeysBase64
    ) -> ManifestOutcome {
        let pins: [Curve25519.Signing.PublicKey]
        switch parsedPins(pinnedPublicKeysBase64) {
        case .none:
            return ManifestOutcome(verification: .refusedNoPinnedKey, manifest: nil)
        case .some(let keys):
            pins = keys
        }

        guard let root = (try? JSONSerialization.jsonObject(with: rawManifest)) as? [String: Any] else {
            return ManifestOutcome(verification: .refusedMalformedManifest, manifest: nil)
        }

        guard let rawSignature = root["signature"] as? String, !rawSignature.isEmpty else {
            return ManifestOutcome(verification: .refusedSignatureMissing, manifest: nil)
        }
        guard let sigData = Data(base64Encoded: rawSignature), sigData.count == 64 else {
            return ManifestOutcome(verification: .refusedSignatureMalformed, manifest: nil)
        }

        var unsigned = root
        unsigned.removeValue(forKey: "signature")
        guard let payload = try? AppcastCanonicalJSON.canonicalBytes(of: unsigned) else {
            return ManifestOutcome(verification: .refusedMalformedManifest, manifest: nil)
        }

        guard pins.contains(where: { $0.isValidSignature(sigData, for: payload) }) else {
            return ManifestOutcome(verification: .refusedSignatureMismatch, manifest: nil)
        }

        // Decode from the bytes that were signed, so the fields the app acts on are the
        // fields the signature covers.
        guard let manifest = try? JSONDecoder().decode(AppcastManifest.self, from: payload) else {
            return ManifestOutcome(verification: .refusedMalformedManifest, manifest: nil)
        }
        return ManifestOutcome(verification: .verified, manifest: manifest)
    }

    /// Verification only, for callers that do not need the decoded manifest.
    static func verify(
        rawManifest: Data,
        pinnedPublicKeysBase64: [String] = AppcastVerifier.pinnedPublicKeysBase64
    ) -> AppcastVerification {
        verifiedManifest(rawManifest: rawManifest,
                         pinnedPublicKeysBase64: pinnedPublicKeysBase64).verification
    }

    /// All-or-nothing: if ANY configured pin is unusable the build's trust anchor is not what
    /// its author thought it was, so the whole set is refused rather than silently verifying
    /// against the survivors.
    private static func parsedPins(_ base64Keys: [String]) -> [Curve25519.Signing.PublicKey]? {
        guard !base64Keys.isEmpty else { return nil }
        var keys: [Curve25519.Signing.PublicKey] = []
        for encoded in base64Keys {
            guard let raw = Data(base64Encoded: encoded), raw.count == 32,
                  let key = try? Curve25519.Signing.PublicKey(rawRepresentation: raw) else {
                return nil
            }
            keys.append(key)
        }
        return keys
    }
}

/// Reproduces the server's signing canonicalization byte for byte.
///
/// The producer is `Server/syxx_server/appcast.py:56-58`:
/// `json.dumps(obj, separators=(",", ":"), sort_keys=True).encode("utf-8")`, with Python's
/// default `ensure_ascii=True`. That last default is the trap: Python emits `\uXXXX` for every
/// character outside 0x20-0x7E, so a client that re-serializes with raw UTF-8 (which both
/// `JSONSerialization.sortedKeys` and .NET's `UnsafeRelaxedJsonEscaping` do) computes different
/// bytes the moment a release note contains an accent, and then rejects every honest update.
/// Hand-rolled rather than delegated to `JSONSerialization` for exactly that reason.
/// Pinned against server-produced vectors in `corpus/appcast-crossplatform/appcast-vectors.json`.
enum AppcastCanonicalJSON {
    enum CanonicalizationError: Error {
        /// A value with no canonical form both platforms and the server agree on, e.g. a
        /// non-integer number. Refused rather than guessed at.
        case unsupportedValue
    }

    static func canonicalBytes(of object: [String: Any]) throws -> Data {
        var out = String()
        try encodeObject(object, into: &out)
        return Data(out.utf8)
    }

    private static func encode(_ value: Any, into out: inout String) throws {
        switch value {
        case let dict as [String: Any]:
            try encodeObject(dict, into: &out)
        case let array as [Any]:
            out += "["
            for (index, element) in array.enumerated() {
                if index > 0 { out += "," }
                try encode(element, into: &out)
            }
            out += "]"
        case let string as String:
            encodeString(string, into: &out)
        case let number as NSNumber:
            try encodeNumber(number, into: &out)
        case is NSNull:
            out += "null"
        default:
            throw CanonicalizationError.unsupportedValue
        }
    }

    private static func encodeObject(_ dict: [String: Any], into out: inout String) throws {
        // Python's sort_keys orders by Unicode code point. UTF-8 byte order is code-point
        // order, so sorting on UTF-8 bytes matches Python and, unlike UTF-16 ordering,
        // stays correct for astral-plane keys. Windows sorts the same way.
        let sorted = dict.keys.sorted { lexicographicallyPrecedes(Array($0.utf8), Array($1.utf8)) }
        out += "{"
        for (index, key) in sorted.enumerated() {
            if index > 0 { out += "," }
            encodeString(key, into: &out)
            out += ":"
            try encode(dict[key]!, into: &out)
        }
        out += "}"
    }

    private static func lexicographicallyPrecedes(_ lhs: [UInt8], _ rhs: [UInt8]) -> Bool {
        for (a, b) in zip(lhs, rhs) where a != b { return a < b }
        return lhs.count < rhs.count
    }

    private static func encodeNumber(_ number: NSNumber, into out: inout String) throws {
        if CFGetTypeID(number) == CFBooleanGetTypeID() {
            out += number.boolValue ? "true" : "false"
            return
        }
        // Non-integer numbers have no repr the server (Python `float.__repr__`), Swift and
        // .NET are guaranteed to agree on, so there is no honest canonical form. Refuse.
        guard !CFNumberIsFloatType(number as CFNumber) else {
            throw CanonicalizationError.unsupportedValue
        }
        out += String(number.int64Value)
    }

    /// Python's `ESCAPE_ASCII` rules: escape `"` and `\`, use the short forms for
    /// \b \f \n \r \t, and emit `\uXXXX` (lowercase hex, surrogate pairs above the BMP) for
    /// everything outside printable ASCII 0x20-0x7E. Forward slash is NOT escaped.
    private static func encodeString(_ string: String, into out: inout String) {
        out += "\""
        for scalar in string.unicodeScalars {
            switch scalar {
            case "\"": out += "\\\""
            case "\\": out += "\\\\"
            case "\u{08}": out += "\\b"
            case "\u{0C}": out += "\\f"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            default:
                let value = scalar.value
                if value >= 0x20 && value <= 0x7E {
                    out.unicodeScalars.append(scalar)
                } else if value <= 0xFFFF {
                    out += String(format: "\\u%04x", value)
                } else {
                    let adjusted = value - 0x10000
                    let high = 0xD800 + (adjusted >> 10)
                    let low = 0xDC00 + (adjusted & 0x3FF)
                    out += String(format: "\\u%04x\\u%04x", high, low)
                }
            }
        }
        out += "\""
    }
}

// MARK: - Models

/// Decoded from the CANONICAL (signed) bytes, which by construction no longer carry the
/// `signature` field — hence no `signature` property here. Nothing downstream should read a
/// field that was not covered by the signature.
struct AppcastManifest: Codable {
    let platform: String
    let version: String
    let build: String
    let minVersion: String
    let critical: Bool
    let releaseNotes: String
    let packages: [AppcastPackage]
    let publishedAt: Int
}

struct AppcastPackage: Codable {
    let url: String
    let sha256: String
    let size: Int
    let type: String
}

enum UpdateError: LocalizedError {
    case invalidResponse
    case serverError(Int)
    case invalidSignature
    case noPackage
    case invalidURL
    case downloadFailed(String)
    case hashMismatch
    case unsupportedFormat
    case installFailed(String)

    var errorDescription: String? {
        switch self {
        case .invalidResponse: return "Invalid response from server"
        case .serverError(let code): return "Server error: \(code)"
        case .invalidSignature: return "Invalid update signature"
        case .noPackage: return "No package available"
        case .invalidURL: return "Invalid download URL"
        case .downloadFailed(let msg): return "Download failed: \(msg)"
        case .hashMismatch: return "Download corrupted (hash mismatch)"
        case .unsupportedFormat: return "Unsupported package format"
        case .installFailed(let msg): return "Installation failed: \(msg)"
        }
    }
}

// MARK: - Download Delegate (removed — deprecated downloadTask(with:delegate:) API)
