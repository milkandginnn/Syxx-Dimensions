import Foundation
import Security
import ServiceManagement
import SyxxKit

@_silgen_name("SMJobCopyDictionary")
private func legacySMJobCopyDictionary(_ domain: CFString, _ jobLabel: CFString) -> Unmanaged<CFDictionary>?

@_silgen_name("SMJobRemove")
private func legacySMJobRemove(
    _ domain: CFString,
    _ jobLabel: CFString,
    _ auth: AuthorizationRef,
    _ wait: Bool,
    _ outError: UnsafeMutablePointer<Unmanaged<CFError>?>?
) -> Bool

@_silgen_name("SMJobBless")
private func legacySMJobBless(
    _ domain: CFString,
    _ executableLabel: CFString,
    _ auth: AuthorizationRef,
    _ outError: UnsafeMutablePointer<Unmanaged<CFError>?>?
) -> Bool

/// Process-wide holder for the ONE `AuthorizationRef` the app uses for every privileged operation.
///
/// WHY THIS EXISTS. `uninstallHelper()` and `installHelper()` each used to declare their OWN local
/// `var authRef: AuthorizationRef?`, call `AuthorizationCreate`, and free it on return via
/// `defer { AuthorizationFree(auth, []) }`. Nothing survived the call, so macOS had no live ref to
/// extend rights on and re-prompted for the login password on every privileged operation. This is
/// one of the three independent prompt sources behind the owner's "four, sometimes twelve"
/// password demands per session.
///
/// `.preAuthorize` was already present and was never the fix: it only moves the prompt from
/// `SMJobBless` time to `AuthorizationCreate` time, WITHIN one ref's lifetime.
///
/// LIFETIME RULES.
/// - `AuthorizationFree(auth, [])` (EMPTY flags) releases the handle but LEAVES the acquired
///   rights in place. That is what makes reuse possible, and it is why nothing here ever frees
///   with `.destroyRights` except `teardown()`.
/// - `.destroyRights` tears the acquired rights down and forces the next operation to re-prompt,
///   so it belongs ONLY in a deliberate teardown / sign-out path. Never per call.
/// - A grant is cached. A REFUSAL IS NEVER CACHED: `ref` stays nil so the next privileged
///   operation asks again, and the caller gets a typed refusal instead of a completed operation.
///
/// Guarded by an `NSLock`, matching `LicenseManager`'s per-launch keychain cache
/// (`SyxxKit/LicenseManager.swift:91-113`), which solves the same "one prompt per process, shared
/// across concurrent callers" problem for the Keychain.
///
/// HONESTY NOTE. This reduces the prompt COUNT; it does not eliminate prompts. The owner's build
/// is ad-hoc signed, and an ad-hoc signature changes on every rebuild, so macOS cannot grant a
/// stable "always allow" ACL (documented for the Keychain half at `LicenseManager.swift:84-88`).
/// A Developer-ID-signed build is the actual fix.
final class AuthorizationSession: @unchecked Sendable {

    /// Tri-state outcome of acquiring authorization. A refusal must never be representable as a
    /// completed operation, so there is no `Bool` here (AGENTS.md "TRI-STATE, NEVER BOOLEAN").
    enum Acquisition {
        /// Rights are held. The ref is owned by the session and must NOT be freed by the caller.
        case granted(AuthorizationRef)
        /// The user actively refused: cancelled the prompt, or was denied the right.
        case denied(OSStatus)
        /// We could not even ask (no interactive session, internal Security failure, bad rights).
        /// Distinct from `.denied` because "the user said no" and "we never got to ask" are
        /// different facts and only one of them is the user's decision.
        case unavailable(OSStatus)
    }

    /// The app-wide session. Created lazily on first privileged operation.
    static let shared = AuthorizationSession(create: AuthorizationSession.createSystemAuthorization)

    private let lock = NSLock()
    private var ref: AuthorizationRef?
    private var creations = 0
    private let create: (UnsafeMutablePointer<AuthorizationRef?>) -> OSStatus
    private let free: (AuthorizationRef, AuthorizationFlags) -> OSStatus

    /// - Parameters:
    ///   - create: the authorization factory. Injected so tests can drive the caching and refusal
    ///     paths deterministically; production always uses `createSystemAuthorization`.
    ///   - free: the release call. Injected for the same reason, and so a test can assert that
    ///     teardown (and only teardown) passes `.destroyRights`.
    init(
        create: @escaping (UnsafeMutablePointer<AuthorizationRef?>) -> OSStatus,
        free: @escaping (AuthorizationRef, AuthorizationFlags) -> OSStatus = { AuthorizationFree($0, $1) }
    ) {
        self.create = create
        self.free = free
    }

    /// How many times the factory has actually been invoked. This is the observable that proves
    /// caching: N privileged operations in one process must produce exactly ONE invocation, which
    /// is exactly one password prompt.
    var creationAttempts: Int {
        lock.lock()
        defer { lock.unlock() }
        return creations
    }

    /// Whether rights are currently held. False after `teardown()` and after any refusal.
    var isHoldingRights: Bool {
        lock.lock()
        defer { lock.unlock() }
        return ref != nil
    }

    /// Return the shared ref, creating it on first use. Safe to call from any thread.
    func acquire() -> Acquisition {
        lock.lock()
        defer { lock.unlock() }

        if let existing = ref {
            return .granted(existing)   // reuse — no second prompt
        }

        var fresh: AuthorizationRef?
        creations += 1
        let status = create(&fresh)

        guard status == errAuthorizationSuccess, let auth = fresh else {
            // Deliberately do NOT store anything: a failure cached would either be mistaken for a
            // grant or would suppress every later attempt. Both are worse than asking again.
            switch status {
            case errAuthorizationCanceled, errAuthorizationDenied:
                return .denied(status)
            default:
                return .unavailable(status)
            }
        }

        ref = auth
        return .granted(auth)
    }

    /// Deliberate teardown / sign-out. Destroys the acquired rights, so the NEXT privileged
    /// operation legitimately prompts again. Called from `uninstallHelper()` only.
    func teardown() {
        lock.lock()
        defer { lock.unlock() }
        guard let auth = ref else { return }
        _ = free(auth, [.destroyRights])
        ref = nil
    }

    /// The production factory: the system daemon-modification right, pre-authorized so the prompt
    /// happens here (once) rather than inside `SMJobBless`.
    ///
    /// `withCString` matters. The old `uninstallHelper()` passed `kSMRightModifySystemDaemons`
    /// straight into `AuthorizationItem(name:)`; Swift's implicit `String` to `UnsafePointer<CChar>`
    /// bridge is only valid for the duration of that call, so the stored `name` dangled by the time
    /// `AuthorizationCreate` read it. `installHelper()` already did it correctly; both now share
    /// this one implementation.
    private static func createSystemAuthorization(
        _ out: UnsafeMutablePointer<AuthorizationRef?>
    ) -> OSStatus {
        let rightName = String(kSMRightModifySystemDaemons)
        return rightName.withCString { cString in
            var item = AuthorizationItem(name: cString, valueLength: 0, value: nil, flags: 0)
            return withUnsafeMutablePointer(to: &item) { itemPtr in
                var rights = AuthorizationRights(count: 1, items: itemPtr)
                return AuthorizationCreate(
                    &rights,
                    nil,
                    [.extendRights, .interactionAllowed, .preAuthorize],
                    out
                )
            }
        }
    }
}

/// Manages privileged operations using an XPC helper tool.
/// The helper is installed once via SMJobBless (prompts for password),
/// then all subsequent privileged operations use the helper without additional prompts.
@MainActor
final class PrivilegeManager {
    static let shared = PrivilegeManager()

    private var connection: NSXPCConnection?
    private var isInstalling = false
    private var installationContinuation: CheckedContinuation<Void, Error>?
    /// Session-scoped flag: once an install has been attempted (successfully or not), do not
    /// re-prompt for the password within the same process lifetime. A failed install that leaves
    /// the helper unregistered would otherwise re-prompt on every subsequent privileged operation
    /// (Cage capture, DNS flush), producing a loop of password dialogs the user cannot escape
    /// without quitting the app.
    private var hasAttemptedInstall = false

    private init() {}

    /// Ensure the privileged helper is installed and connected.
    /// Prompts for password only on first installation.
    func ensureHelperInstalled() async throws {
        if connection != nil {
            return // Already connected
        }

        if isInstalling {
            // Wait for existing installation to complete using continuation
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
                installationContinuation = continuation
            }
            if connection != nil {
                return
            }
        }

        isInstalling = true
        defer {
            isInstalling = false
            installationContinuation = nil
        }

        // D094(a): replace the file-existence check with a launchd registration + version
        // comparison. A stale helper left by an older Syxx (or a file planted by anything
        // that can write the directory) was adopted forever — SMJobBless is never called
        // for an upgrade, and no signature or version was ever checked.
        let helperLabel = "com.milkandgin.deepsyxx" as CFString
        var needsInstall = true
        let bundledVersion = Bundle.main.infoDictionary?["CFBundleVersion"] as? String
        if let jobDict = legacySMJobCopyDictionary(kSMDomainSystemLaunchd, helperLabel)?.takeRetainedValue() as? [String: Any],
           let installedVersion = jobDict["CFBundleVersion"] as? String {
            if let bundled = bundledVersion {
                // Bundled build: compare versions — mismatch triggers re-bless.
                if installedVersion == bundled {
                    needsInstall = false
                } else {
                    SyxxDiagnostics.breadcrumb(
                        category: "gui.privilege",
                        message: "helper version mismatch: installed=\(installedVersion), bundled=\(bundled) — re-blessing",
                        level: .warning)
                }
            } else {
                // Unbundled dev build (no CFBundleVersion in Bundle.main): the helper is registered
                // in launchd and we cannot compare versions, so assume it is current. Re-blessing
                // an unbundled build would re-prompt on every call because the version comparison
                // can never succeed (bundledVersion is nil, so installedVersion == nil is false).
                needsInstall = false
                SyxxDiagnostics.breadcrumb(
                    category: "gui.privilege",
                    message: "unbundled build: helper already registered (v\(installedVersion)), skipping install",
                    level: .warning)
            }
        }
        // If hasAttemptedInstall is set, skip the install even if needsInstall is true — a prior
        // attempt within this process already prompted the user, and re-prompting creates a loop.
        if needsInstall && hasAttemptedInstall {
            SyxxDiagnostics.breadcrumb(
                category: "gui.privilege",
                message: "skipping helper install: already attempted this session",
                level: .warning)
            needsInstall = false
        }
        if needsInstall {
            hasAttemptedInstall = true
            do {
                try await installHelper()
            } catch {
                // C011: a failed install must not permanently suppress retry. Clear the flag so the
                // next ensureHelperInstalled() re-prompts instead of being short-circuited by the
                // "already attempted this session" check above. UI follow-up: no retry affordance
                // exists for the install state in this file or its direct view; the state reset is
                // the core fix, surface a "Retry installation" action at the call site as needed.
                hasAttemptedInstall = false
                throw error
            }
        }

        // Connect to the helper via XPC
        try await connectToHelper()
        
        // Resume any waiters
        installationContinuation?.resume()
    }
    
    /// Remove the privileged helper (for uninstallation).
    func uninstallHelper() async throws {
        let auth = try Self.authorize(diagnosticContext: "uninstallHelper")

        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            // NOTE: no `AuthorizationFree` here. The ref belongs to `AuthorizationSession.shared`
            // and is reused by every later privileged operation; freeing it per call is exactly
            // what made macOS re-prompt for the password on each one.
            var error: Unmanaged<CFError>?
            let success = legacySMJobRemove(
                kSMDomainSystemLaunchd,
                "com.milkandgin.deepsyxx" as CFString,
                auth,
                true, // wait for removal
                &error
            )

            if success {
                // Close the connection
                connection?.invalidate()
                connection = nil
                continuation.resume()
            } else {
                let err = error?.takeRetainedValue() as Error?
                continuation.resume(throwing: err ?? PrivilegeError.installationFailed)
            }
        }

        // The helper is gone, so this IS the deliberate teardown path: destroy the acquired rights
        // rather than leaving them held for a helper that no longer exists. A later install will
        // legitimately prompt again.
        AuthorizationSession.shared.teardown()
    }

    /// Acquire the process-wide authorization, translating a refusal into a thrown error.
    ///
    /// The tri-state from `AuthorizationSession` is preserved in the diagnostic breadcrumb (a
    /// cancel and a Security-framework failure are different facts). The THROWN error keeps the
    /// existing wording unchanged, because distinguishing the two on screen would be new
    /// user-visible copy and that needs owner approval (AGENTS.md "OUTPUT REQUIRES USER APPROVAL").
    private static func authorize(diagnosticContext: String) throws -> AuthorizationRef {
        switch AuthorizationSession.shared.acquire() {
        case .granted(let auth):
            return auth
        case .denied(let status):
            SyxxDiagnostics.breadcrumb(
                category: "gui.privilege",
                message: "\(diagnosticContext): authorization DENIED by the user (status \(status))",
                level: .warning)
            throw PrivilegeError.authorizationFailed
        case .unavailable(let status):
            SyxxDiagnostics.breadcrumb(
                category: "gui.privilege",
                message: "\(diagnosticContext): authorization UNAVAILABLE, could not prompt (status \(status))",
                level: .error)
            throw PrivilegeError.authorizationFailed
        }
    }
    
    private func installHelper() async throws {
        // ONE ref for the whole process (see AuthorizationSession). The refusal wording below is
        // unchanged from before this refactor — no new user-visible strings.
        let auth: AuthorizationRef
        switch AuthorizationSession.shared.acquire() {
        case .granted(let a):
            auth = a
        case .denied(let status), .unavailable(let status):
            SyxxDiagnostics.breadcrumb(
                category: "gui.privilege",
                message: "installHelper: authorization refused (status \(status))",
                level: .error)
            throw NSError(
                domain: "com.milkandgin.syxx.PrivilegeManager",
                code: Int(status),
                userInfo: [NSLocalizedDescriptionKey: "Authorization failed with status: \(status)"]
            )
        }

        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            // NOTE: no `AuthorizationFree` here — the ref is process-wide and reused.
            var error: Unmanaged<CFError>?
            let success = legacySMJobBless(
                kSMDomainSystemLaunchd,
                "com.milkandgin.deepsyxx" as CFString,
                auth,
                &error
            )

            if success {
                continuation.resume()
            } else {
                if let cfError = error?.takeRetainedValue() {
                    let bridgedError = cfError as Error
                    let nsError = bridgedError as NSError
                    let domain = nsError.domain
                    let code = nsError.code
                    let reason = nsError.userInfo[NSLocalizedDescriptionKey] as? String ?? nsError.localizedDescription
                    // Full technical detail (domain + code + reason) to the diagnostic sink only. A
                    // string like "CFErrorDomainLaunchd (8)" is crash-reporter material, not something
                    // a user can act on, so it must never reach the screen.
                    SyxxDiagnostics.breadcrumb(
                        category: "gui.privilege",
                        message: "SMJobBless failed: \(domain) (\(code)): \(reason)",
                        level: .error)
                    // Append the reason to the user message only when it is genuine human-readable
                    // text, never a domain/code token or a type dump.
                    let userReason = ErrorPresenter.displayReason(reason).map { " \($0)" } ?? ""
                    continuation.resume(throwing: NSError(
                        domain: "com.milkandgin.syxx.PrivilegeManager",
                        code: code,
                        userInfo: [NSLocalizedDescriptionKey: "Helper installation failed.\(userReason)"]
                    ))
                } else {
                    continuation.resume(throwing: PrivilegeError.installationFailed)
                }
            }
        }
    }
    
    private func connectToHelper() async throws {
        let conn = NSXPCConnection(machServiceName: "com.milkandgin.deepsyxx", options: .privileged)
        conn.remoteObjectInterface = NSXPCInterface(with: SyxxPrivilegedHelperProtocol.self)
        
        conn.interruptionHandler = { [weak self] in
            Task { @MainActor in
                self?.connection = nil
            }
        }
        
        conn.invalidationHandler = { [weak self] in
            Task { @MainActor in
                self?.connection = nil
            }
        }
        
        conn.resume()

        // D094(b): NSXPCConnection.resume() succeeds lazily even when the Mach service
        // does not exist — the failure only appears at the first message. Perform a cheap
        // round-trip via the `ping` method and cache `connection` only if it answers.
        // Without this, `ensureHelperInstalled` permanently believes it is connected
        // because `self.connection` was set unconditionally and every subsequent call
        // short-circuits on `connection != nil`.
        let alive = await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
            let proxy = conn.remoteObjectProxyWithErrorHandler { _ in
                continuation.resume(returning: false)
            } as? SyxxPrivilegedHelperProtocol
            guard let proxy = proxy else {
                continuation.resume(returning: false)
                return
            }
            proxy.ping { replied in
                continuation.resume(returning: replied)
            }
        }
        guard alive else {
            conn.invalidate()
            throw PrivilegeError.connectionFailed
        }
        self.connection = conn
    }

    /// Start cage capture via the helper.
    /// D091/D116: `cliPath` is the absolute path of the bundled CLI, supplied by the caller
    /// (which computes it from the app bundle layout via AppModel.locateCageBinary()).
    func startCageCapture(cliPath: String, duration: Int, outputPath: String, ownerUID: UInt32, ownerGID: UInt32) async throws -> (output: String, exitCode: Int32) {
        try await ensureHelperInstalled()

        guard let conn = connection else {
            throw PrivilegeError.connectionFailed
        }

        return try await withCheckedThrowingContinuation { continuation in
            let proxy = conn.remoteObjectProxyWithErrorHandler { error in
                continuation.resume(throwing: error)
            } as? SyxxPrivilegedHelperProtocol

            // D094(c): a failed cast must resume the continuation — optional chaining on a nil
            // proxy silently drops the call and leaks the continuation forever.
            guard let proxy = proxy else {
                continuation.resume(throwing: PrivilegeError.connectionFailed)
                return
            }

            proxy.startCageCapture(
                cliPath: cliPath,
                duration: duration,
                outputPath: outputPath,
                ownerUID: ownerUID,
                ownerGID: ownerGID
            ) { output, exitCode, error in
                if let error = error {
                    continuation.resume(throwing: NSError(domain: "PrivilegeManager", code: Int(exitCode), userInfo: [NSLocalizedDescriptionKey: error]))
                } else {
                    continuation.resume(returning: (output, exitCode))
                }
            }
        }
    }
    
    /// Flush the DNS cache via the helper.
    func flushDNSCache() async throws {
        try await ensureHelperInstalled()
        
        guard let conn = connection else {
            throw PrivilegeError.connectionFailed
        }
        
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            let proxy = conn.remoteObjectProxyWithErrorHandler { error in
                continuation.resume(throwing: error)
            } as? SyxxPrivilegedHelperProtocol

            // D094(c): a failed cast must resume the continuation — optional chaining on a nil
            // proxy silently drops the call and leaks the continuation forever.
            guard let proxy = proxy else {
                continuation.resume(throwing: PrivilegeError.connectionFailed)
                return
            }

            proxy.flushDNSCache { success, error in
                if success {
                    continuation.resume()
                } else {
                    continuation.resume(throwing: NSError(domain: "PrivilegeManager", code: -1, userInfo: [NSLocalizedDescriptionKey: error ?? "Failed to flush DNS cache"]))
                }
            }
        }
    }
}

enum PrivilegeError: Error, LocalizedError {
    case authorizationFailed
    case installationFailed
    case connectionFailed
    
    var errorDescription: String? {
        switch self {
        case .authorizationFailed:
            return "Failed to obtain authorization for privileged helper installation"
        case .installationFailed:
            return "Failed to install privileged helper"
        case .connectionFailed:
            return "Failed to connect to privileged helper"
        }
    }
}
