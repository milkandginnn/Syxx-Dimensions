import Foundation
import NetworkExtension
import SyxxKit

/// macOS glue for the encrypt-all-DNS feature. Installs system-wide encrypted DNS through
/// `NEDNSSettingsManager` — the OS's own programmatic API. There is NO downloaded `.mobileconfig`
/// profile and NO local proxy: the app requests the setting, the user approves it via a system
/// prompt, and every DNS query the machine makes then rides DoH/DoT.
///
/// HARD REQUIREMENT (owner-track, same class as notarization): `NEDNSSettingsManager` only functions
/// when the app is signed with the `com.apple.developer.networking.networkextension` entitlement
/// carrying the `dns-settings` value, granted by Apple. The current unsigned SwiftPM build therefore
/// CANNOT complete a save — `saveToPreferences` returns a permission error, which this maps to
/// `.entitlementRequired` so the UI states exactly what is needed rather than showing a fake success.
///
/// HONESTY: every path routes through `EncryptedDNSConfig.classifyApply`. "Enabled" is reported only
/// when the save succeeded, the manager reports active, AND a real encrypted query completed. Merely
/// saving is never called success.
public enum EncryptedDNSConfigurator {

    /// Turn encryption on for the whole system using the given catalog resolver + transport.
    public static func enable(resolverID: String,
                              transport: EncryptedDNSTransport) async -> EncryptedDNSApplyResult {
        guard let resolver = EncryptedDNSConfig.resolver(id: resolverID) else {
            return .failed(reason: "unknown resolver \(resolverID)")
        }

        let manager = NEDNSSettingsManager.shared()

        // Build the DNS settings for the chosen transport. serverAddresses are the fallback IPs the
        // OS uses to reach the encrypted endpoint.
        let settings: NEDNSSettings
        switch transport {
        case .doh:
            guard let urlStr = resolver.dohURL, let url = URL(string: urlStr) else {
                return .failed(reason: "\(resolver.displayName) does not specify a valid DoH URL")
            }
            let s = NEDNSOverHTTPSSettings(servers: resolver.addresses)
            s.serverURL = url
            settings = s
        case .dot:
            guard let host = resolver.dotHost, !host.isEmpty else {
                return .failed(reason: "\(resolver.displayName) does not specify a valid DoT host")
            }
            let s = NEDNSOverTLSSettings(servers: resolver.addresses)
            s.serverName = host
            settings = s
        case .dnscrypt:
            return .failed(reason: "DNSCrypt is enabled via the local resolver (Unbound + dnscrypt-proxy), not the system encrypt-all toggle")
        }
        manager.dnsSettings = settings

        // Save. A missing-entitlement rejection is its own honest state.
        do {
            try await manager.saveToPreferences()
        } catch {
            if isEntitlementError(error) {
                return .entitlementRequired
            }
            return .failed(reason: error.localizedDescription)
        }

        // Installed ≠ in effect. Reload and confirm the manager is actually enabled...
        var active = false
        do {
            try await manager.loadFromPreferences()
            active = manager.isEnabled
        } catch {
            return .inconclusive(reason: "saved but could not reload to confirm: \(error.localizedDescription)")
        }

        let endpointStr: String? = transport == .dot
            ? resolver.dotHost.map { "https://\($0)/dns-query" }
            : resolver.dohURL
        let endpoint = endpointStr.flatMap { URL(string: $0) } ?? EncryptedTransportProbe.defaultEndpoint
        let probe = await EncryptedTransportProbe.probe(endpoint: endpoint)
        let probeCompleted = probe.status == .working

        return EncryptedDNSConfig.classifyApply(
            resolverID: resolverID, transport: transport,
            saveSucceeded: true, entitlementRejected: false,
            systemReportsActive: active, encryptedProbeCompleted: probeCompleted,
            failureReason: nil)
    }

    /// Turn it back off — remove Syxx's DNS settings so the system reverts to its prior resolver.
    /// Returns a tri-state: removed / entitlement-required / failed. Never claims removal it didn't do.
    public static func disable() async -> EncryptedDNSApplyResult {
        let manager = NEDNSSettingsManager.shared()
        do {
            try await manager.loadFromPreferences()
            try await manager.removeFromPreferences()
            return .disabled
        } catch {
            if isEntitlementError(error) { return .entitlementRequired }
            return .failed(reason: error.localizedDescription)
        }
    }

    /// Read whether Syxx's encrypted DNS is currently installed and active. Honest: an error or an
    /// un-loadable manager is `.inconclusive`, never "off" asserted as fact.
    public static func currentState() async -> EncryptedDNSApplyResult {
        let manager = NEDNSSettingsManager.shared()
        do {
            try await manager.loadFromPreferences()
            return manager.isEnabled
                ? .inconclusive(reason: "installed and active; run a probe to confirm the encrypted path")
                : .failed(reason: "not installed")
        } catch {
            return .inconclusive(reason: "could not read current DNS settings: \(error.localizedDescription)")
        }
    }

    /// The NEDNSSettingsManager permission/entitlement error. Distinguishing it lets the UI say
    /// "this build needs the DNS-Settings entitlement" instead of a generic failure.
    ///
    /// D092: match on exact domain strings, not substring contains. The old `.contains("NEConfiguration")`
    /// matched any domain merely containing that substring, and the blanket NEVPNErrorDomain match
    /// laundered every NE configuration failure (stale, disabled, read-write-failed, user-declined)
    /// into `.entitlementRequired` — a confident, specific, and often wrong diagnosis.
    /// Now only permission-related error codes within these domains are classified as entitlement errors.
    private static func isEntitlementError(_ error: Error) -> Bool {
        let ns = error as NSError
        // Match exact error domains — not substring contains — and only treat specific
        // permission-related codes as entitlement issues.
        if ns.domain == "NEConfigurationErrorDomain" || ns.domain == "NEDNSSettingsErrorDomain" {
            // Configuration invalid or permission denied within the NE configuration subsystem.
            // NEConfigurationError code 1 = "configuration is invalid" (often entitlement-related).
            return ns.code == 1 || ns.code == 7
        }
        if ns.domain == NEVPNErrorDomain {
            // Only configurationReadWriteFailed (permission) is entitlement-related;
            // configurationInvalid, configurationStale, configurationDisabled, etc. are not.
            return ns.code == NEVPNError.configurationReadWriteFailed.rawValue
        }
        // Check the underlying error for a permission-denied OSStatus.
        if let underlying = ns.userInfo[NSUnderlyingErrorKey] as? NSError {
            if underlying.domain == NSOSStatusErrorDomain {
                let code = underlying.code
                return code == errSecAuthFailed || code == errSecInteractionNotAllowed
            }
        }
        return false
    }
}
