import Foundation
import SyxxKit
import Sentry

// MARK: - License validation & activation (E-5/E-6)

extension AppModel {

    // MARK: - State transitions

    func updateLicenseState(_ state: LicenseState, message: String) {
        let oldState = licenseState
        licenseState = state
        licenseMessage = message
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state")
            b.message = "License state changed: \(oldState) → \(state)"
            b.data = ["old_state": "\(oldState)", "new_state": "\(state)", "message": message]
            return b }())
        switch state {
        case .pro, .trial:
            isPro = true
        case .unknown, .lite, .activationFailed:
            isPro = false
        }
    }

    /// Validates the stored license key / trial state on launch. Serialized: concurrent callers
    /// (checkStartup, ProbePlan, activateLicense recovery) share a single in-flight Task instead
    /// of each spawning independent Keychain prompts. After the first validation completes within
    /// the same process lifetime, subsequent calls return immediately — the license state is
    /// already populated and the Keychain cache serves the answer without a new prompt.
    func validateLicenseOnLaunch() async {
        // If validation already completed this process, skip — the cached state is authoritative.
        if hasAttemptedValidation { return }

        // If a validation Task is already in flight, await it rather than starting another.
        if let existing = validationTask {
            await existing.value
            return
        }

        let task = Task { [weak self] in
            guard let self = self else { return }
            let result = await LicenseManager.isFeatureUnlocked()
            await MainActor.run {
                self.hasAttemptedValidation = true
                self.applyLicenseResult(result)
            }
        }
        validationTask = task
        await task.value
    }

    /// Apply a license-check result to the UI state. Extracted so the activateLicense recovery
    /// paths can re-check state after a failed activation (timeout / network error) without going
    /// through the `hasAttemptedValidation` guard — those paths need to restore prior entitlement,
    /// which is a distinct operation from the launch-time validation the guard protects.
    func applyLicenseResult(_ result: (unlocked: Bool, message: String)) {
        if result.unlocked {
            if result.message.contains("Pro Subscription") {
                updateLicenseState(.pro, message: result.message)
            } else if result.message.contains("Free Trial"),
                      let hours = extractTrialHours(from: result.message) {
                updateLicenseState(.trial(hoursRemaining: hours), message: result.message)
            } else {
                updateLicenseState(.pro, message: result.message)
            }
        } else {
            updateLicenseState(.lite, message: result.message)
        }
    }

    private func extractTrialHours(from message: String) -> Int? {
        let pattern = /(\d+)\s*hours?\s*remaining/
        guard let match = message.firstMatch(of: pattern) else { return nil }
        return Int(match.1)
    }

    /// Called when the user clicks "Activate" in LicenseView. Saves the key to the Keychain,
    /// validates it against Keygen, and updates the license state.
    func activateLicense() async {
        let key = licenseKey.trimmingCharacters(in: .whitespaces)
        guard !key.isEmpty else {
            updateLicenseState(.activationFailed("Please enter a license key."), message: "")
            return
        }

        isValidatingLicense = true
        updateLicenseState(.unknown, message: "Validating license key with Keygen…")

        // E048: validate BEFORE persisting. Previously the key was saved to the Keychain
        // before calling Keygen, so an invalid/timeout/network-error result would overwrite a
        // known-good stored credential with a bad key, disabling collector-backed evidence.
        // Now the Keychain is touched only when the server confirms the key is valid.
        let result = await LicenseManager.validateLicenseDetailed(key: key,
                                                                   accountID: SyxxConfig.accountID)

        await MainActor.run {
            self.isValidatingLicense = false
            switch result {
            case .valid:
                // Only persist after confirmed valid — the previous key (if any) is replaced
                // atomically by a verified successor.
                do {
                    try LicenseManager.saveLicenseKey(key)
                } catch {
                    self.updateLicenseState(.activationFailed("License is valid but could not be saved to the system keychain."),
                                           message: "")
                    return
                }
                self.updateLicenseState(.pro, message: "Pro subscription active.")
                self.licenseKey = ""
            case .invalid:
                // Do NOT overwrite the stored key — the entered key is invalid.
                self.updateLicenseState(.activationFailed("The license key is not valid."),
                                        message: "")
            case .timeout:
                // Do NOT overwrite the stored key — network may be blocked.
                // Re-check full state (trial, stored key, etc.) to restore any prior entitlement.
                // Bypasses the hasAttemptedValidation guard — this is recovery, not a launch-time check.
                self.updateLicenseState(.activationFailed("License check timed out. Check your internet connection."),
                                        message: "")
                Task {
                    let result = await LicenseManager.isFeatureUnlocked()
                    await MainActor.run { self.applyLicenseResult(result) }
                }
            case .networkError(let msg):
                // Do NOT overwrite the stored key — server unreachable.
                // Bypasses the hasAttemptedValidation guard — this is recovery, not a launch-time check.
                self.updateLicenseState(.activationFailed("Could not reach the license server: \(msg)"),
                                        message: "")
                Task {
                    let result = await LicenseManager.isFeatureUnlocked()
                    await MainActor.run { self.applyLicenseResult(result) }
                }
            }
        }
    }
}
