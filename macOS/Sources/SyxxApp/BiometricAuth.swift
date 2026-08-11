import Foundation
import LocalAuthentication
import OSLog

/// Biometric authentication (Touch ID) for the Syxx macOS app.
///
/// Evaluates the biometric policy **once** at startup and returns a tri-state result carrying the
/// authenticated `LAContext`. That context is then passed to Keychain queries via
/// `kSecUseAuthenticationContext`, turning N per-item Keychain prompts into ONE Touch ID prompt
/// per session.
///
/// Tri-state design matches the project convention (see `WitnessKeyManager.KeyPresence`,
/// `DimensionOutcome`, `ProbeOutcome`): biometry is never reduced to a boolean because
/// "unavailable" (no Touch ID hardware, no enrolled fingers) must be distinguishable from
/// "denied" (user cancelled, authentication failed) — the first is a silent fallback, the
/// second may warrant user feedback.
///
/// ## Graceful degradation
/// When biometry is unavailable the app MUST still work with password-only Keychain access.
/// Callers handle `.unavailable` by proceeding without a context — the Keychain query falls
/// back to its normal accessibility class.
public enum BiometricAuth {

    private static let log = Logger(subsystem: "com.milkandgin.syxx", category: "biometric")

    // MARK: - Tri-state result

    /// Outcome of a biometric authentication attempt.
    ///
    /// - `authenticated`: Biometry succeeded; the associated `LAContext` can be reused for
    ///   Keychain queries via `kSecUseAuthenticationContext`.
    /// - `unavailable`: Biometry is not available (no Touch ID hardware, no enrolled fingers,
    ///   passcode not set). The associated error is diagnostic only — callers should fall back
    ///   silently to password-only access.
    /// - `denied`: Biometry is available but the user denied or failed authentication (cancelled,
    ///   too many failures, lockout). Callers may surface feedback or fall back to password-only.
    public enum BiometricResult: @unchecked Sendable {
        case authenticated(LAContext)
        case unavailable(Error)
        case denied(Error)
    }

    // MARK: - Public API

    /// Evaluates biometric authentication and returns a tri-state result.
    ///
    /// This is the primary entry point — call it from a `Task` or `async` context before the
    /// first Keychain access to obtain an `LAContext` reusable for all subsequent Keychain
    /// operations in this session.
    ///
    /// - Parameter reason: Localized string shown in the Touch ID dialog. Defaults to a
    ///   generic message suitable for license/Keychain access.
    /// - Returns: A `BiometricResult` carrying either the authenticated context or a diagnostic error.
    public static func evaluate(
        reason: String = "Authenticate to access Syxx license and Keychain data."
    ) async -> BiometricResult {
        let context = LAContext()

        // canEvaluatePolicy is a prerequisite — it checks hardware capability, enrollment state,
        // and passcode configuration without presenting a prompt.
        var canEvaluateError: NSError?
        guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics,
                                        error: &canEvaluateError) else {
            // Biometry unavailable: no hardware, no enrolled fingers, or passcode not set.
            // This is NOT an error condition — the app falls back to password-only access.
            let error = canEvaluateError ?? NSError(
                domain: LAErrorDomain,
                code: LAError.Code.biometryNotAvailable.rawValue,
                userInfo: nil
            )
            log.debug("BiometricAuth: biometry unavailable — \(error.localizedDescription, privacy: .public)")
            return .unavailable(error)
        }

        // Evaluate the biometric policy. This presents the Touch ID dialog and suspends until
        // the user authenticates, cancels, or fails.
        do {
            try await context.evaluatePolicy(
                .deviceOwnerAuthenticationWithBiometrics,
                localizedReason: reason
            )
            log.debug("BiometricAuth: authentication succeeded — context reusable for Keychain")
            return .authenticated(context)
        } catch let laError as LAError {
            // Distinguish capability failures from user actions.
            switch laError.code {
            case .biometryNotAvailable, .biometryNotEnrolled, .passcodeNotSet:
                // These should have been caught by canEvaluatePolicy above, but handle defensively.
                log.debug("BiometricAuth: became unavailable during evaluation — \(laError.localizedDescription, privacy: .public)")
                return .unavailable(laError)
            case .userCancel, .appCancel, .systemCancel:
                log.debug("BiometricAuth: authentication cancelled — \(laError.code.rawValue)")
                return .denied(laError)
            case .authenticationFailed:
                log.debug("BiometricAuth: authentication failed (wrong biometric or too many attempts)")
                return .denied(laError)
            default:
                log.warning("BiometricAuth: unexpected LAError code \(laError.code.rawValue)")
                return .denied(laError)
            }
        } catch {
            // Non-LAError — unexpected, treat as denial.
            log.warning("BiometricAuth: unexpected error — \(error.localizedDescription, privacy: .public)")
            return .denied(error)
        }
    }
}
