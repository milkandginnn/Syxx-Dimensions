import Foundation
import SyxxKit

// MARK: - Dimension Startup (Workstream D)

extension AppModel {

    /// One phone-home per launch: fetch broadcast + update + whats-new pointer.
    /// Caches the response for 24h; broadcasts auto-show once per id.
    func checkStartup() {
        let settings = SyxxSettingsStore.load()
        // D278: the system-resolver probes exec subprocesses and must never run synchronously
        // on the main actor (first paint froze waiting on scutil). Copy the @Sendable seam out
        // first, then run it on a detached task and publish through the single writer. A bare
        // Task would inherit the main actor and still block.
        let probe = systemResolverProbe
        Task.detached { [weak self] in
            let result = await probe()
            await MainActor.run {
                self?.applySystemResolverProbe(result)
            }
        }
        Task {
            // Evaluate biometric authentication ONCE before any Keychain access. This converts
            // N per-item Keychain prompts into ONE Touch ID prompt per session. The authenticated
            // LAContext is stored in LicenseManager and reused for all subsequent Keychain operations.
            // Graceful degradation: if biometry is unavailable (no hardware, no enrolled fingers),
            // the app falls back to password-only access without error.
            let biometricResult = await BiometricAuth.evaluate()
            switch biometricResult {
            case .authenticated(let context):
                LicenseManager.setBiometricContext(context)
            case .unavailable, .denied:
                // No biometric context — Keychain operations fall back to password-only access.
                break
            }

            // Validate license state from Keychain / trial
            await self.validateLicenseOnLaunch()

            // B185: sync the collector's trusted signing key on launch so the trust set is
            // populated from the pinned /api/pubkey endpoint. A failed sync is non-fatal —
            // the persisted key (or manual config) is used as fallback.
            if let collectorBase = URL(string: self.collectorURL.trimmingCharacters(in: .whitespaces)),
               collectorBase.scheme != nil {
                _ = await CollectorPinning.syncTrustedSigningKey(collectorBaseURL: collectorBase)
            }

            guard settings.updateCheckEnabled else { return }
            let resp = await DimensionService.shared.fetchStartup()
            self.startupResponse = resp
            if let u = resp?.update {
                self.updateAvailable = u.available
                self.updateLatest = u.latest
                self.updateURL = u.url ?? "https://syxx.app/download"
            }
            if let b = resp?.broadcast,
               !DimensionService.shared.hasDismissed(b.id),
               DimensionService.shared.isVersionInRange(SyxxConfig.version,
                                                         minVersion: b.minVersion,
                                                         maxVersion: b.maxVersion) {
                self.pendingBroadcast = b
            }
        }
    }

    /// D278: the ONLY writer of `cachedSystemDNSServers`, `cachedEncryptedDNSStatus`,
    /// `resolverState`, and the B229 server default. A probe that measured nothing is honest
    /// `.unavailable` — empty/nil is never treated as a measured resolver, and `server` is
    /// only ever set from a measured value.
    func applySystemResolverProbe(_ probe: SystemResolverProbe) {
        switch probe {
        case .measured(let dnsServers, let encryptedStatus) where !dnsServers.isEmpty:
            resolverState = .measured(dnsServers)
            cachedSystemDNSServers = dnsServers
            cachedEncryptedDNSStatus = encryptedStatus
            // B229: Prophet's default resolver-under-test must be the user's real system
            // resolver, not a hard-coded public IP (moved here from checkStartup, D278).
            server = dnsServers.first(where: { !$0.isEmpty }) ?? server
        case .measured, .unavailable:
            resolverState = .unavailable
            cachedSystemDNSServers = []
            cachedEncryptedDNSStatus = nil
            // `server` untouched: an empty or failed measurement is never a resolver.
        }
    }

    /// Call after user dismisses a broadcast panel — never re-show.
    func dismissBroadcast(_ id: String) {
        DimensionService.shared.markDismissed(id)
        pendingBroadcast = nil
    }
}
