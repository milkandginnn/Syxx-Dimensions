import Foundation
import SyxxKit

// MARK: - Response Models

public struct StartupResponse: Codable, Sendable {
    public let broadcast: Broadcast?
    public let update: UpdateInfo?
    public let whatsNewUrl: String?
}

public struct Broadcast: Codable, Sendable, Equatable {
    public let id: String
    public let title: String
    public let body: String
    public let severity: String
    public let url: String?
    public let minVersion: String?
    public let maxVersion: String?

    public static func == (lhs: Broadcast, rhs: Broadcast) -> Bool { lhs.id == rhs.id }
}

public struct UpdateInfo: Codable, Sendable {
    public let available: Bool
    public let latest: String
    public let url: String?
}

/// Consolidated client for the Dimension startup/whats-new/broadcast endpoints.
/// Designed to be called once per launch and cache results in UserDefaults.
@MainActor
public final class DimensionService {
    public static let shared = DimensionService()

    public var baseURL: String {
        let stored = UserDefaults.standard.string(forKey: "dimension_base_url") ?? "https://syxx.app"
        // D038: enforce HTTPS and restrict to approved hosts in production builds.
        // A local attacker who writes UserDefaults could otherwise redirect all control content
        // to http://attacker. The override is only permitted in development mode.
        return DimensionService.sanitizeBaseURL(stored)
    }

    /// D038: validate and sanitize the base URL. Only HTTPS on approved hosts is allowed in
    /// production; test overrides require DEBUG build configuration.
    static func sanitizeBaseURL(_ raw: String) -> String {
        #if DEBUG
        // Development mode: allow any URL for testing.
        return raw
        #else
        guard let url = URL(string: raw),
              url.scheme?.lowercased() == "https",
              let host = url.host?.lowercased() else {
            return "https://syxx.app"
        }
        // Approved hosts for production builds.
        let approvedHosts = ["syxx.app", "leak.syxx.app", "macmotd.syxx.app", "winmotd.syxx.app"]
        if approvedHosts.contains(host) || host.hasSuffix(".syxx.app") {
            return raw
        }
        return "https://syxx.app"
        #endif
    }

    private let cacheKey = "dimension_startup_cache"
    private let cacheDateKey = "dimension_startup_cache_date"
    private let cacheHMACKey = "dimension_startup_cache_hmac"  // D037: HMAC for cache integrity
    private let cacheInterval: TimeInterval = 86_400 // 24h

    private init() {}

    // MARK: - Startup

    public func fetchStartup(platform: String = "mac",
                              version: String = SyxxConfig.version,
                              force: Bool = false) async -> StartupResponse? {
        if UserDefaults.standard.bool(forKey: "dimension_opt_out") {
            return nil
        }
        if !force, let cached = loadCachedResponse(), !isCacheStale() {
            return cached
        }
        guard let url = URL(string: "\(baseURL)/api/startup?platform=\(platform)&version=\(version)") else { return nil }
        do {
            let (data, _) = try await URLSession.shared.data(from: url)
            let decoded = try JSONDecoder().decode(StartupResponse.self, from: data)
            cacheResponse(decoded)
            return decoded
        } catch {
            // B089: a forced check that fails must not return a stale cached response as if
            // it were fresh. When force is true, the caller explicitly asked for a live check;
            // falling back to cache silently rebrands stale data as a current result.
            if force { return nil }
            return loadCachedResponse()
        }
    }

    // MARK: - What's New

    public func fetchWhatsNewURL() -> URL? {
        if UserDefaults.standard.bool(forKey: "dimension_opt_out") {
            return nil
        }
        // Use the dedicated per-platform MOTD origin (macmotd.syxx.app). Only an ABSOLUTE server-supplied
        // pointer may override it; a relative pointer is deliberately ignored, because gluing it onto
        // `baseURL` (the leak/dimension backend) misrouted What's-New to leak.syxx.app instead of the MOTD
        // host. The MOTD must resolve to macmotd/winmotd, never leak.
        if let cached = loadCachedResponse(), let ptr = cached.whatsNewUrl,
           ptr.hasPrefix("http"), let u = URL(string: ptr) {
            return u
        }
        return URL(string: "https://macmotd.syxx.app/")
    }

    // MARK: - Broadcast

    public func fetchBroadcast() async -> Broadcast? {
        if UserDefaults.standard.bool(forKey: "dimension_opt_out") {
            return nil
        }
        guard let url = URL(string: "\(baseURL)/api/broadcast") else { return nil }
        do {
            let (data, _) = try await URLSession.shared.data(from: url)
            let wrapper = try JSONDecoder().decode([String: Broadcast?].self, from: data)
            return wrapper["broadcast"] ?? nil
        } catch {
            return loadCachedResponse()?.broadcast
        }
    }

    // MARK: - Dismissal tracking

    public func hasDismissed(_ broadcastID: String) -> Bool {
        let dismissed: [String] = UserDefaults.standard.stringArray(forKey: "dimension_dismissed_broadcasts") ?? []
        return dismissed.contains(broadcastID)
    }

    public func markDismissed(_ broadcastID: String) {
        var dismissed = UserDefaults.standard.stringArray(forKey: "dimension_dismissed_broadcasts") ?? []
        if !dismissed.contains(broadcastID) {
            dismissed.append(broadcastID)
            UserDefaults.standard.set(dismissed, forKey: "dimension_dismissed_broadcasts")
        }
    }

    // MARK: - Version range check

    public func isVersionInRange(_ version: String, minVersion: String?, maxVersion: String?) -> Bool {
        let current = parseVersion(version)
        if let min = minVersion, compareVersions(parseVersion(min), current) == .orderedDescending { return false }
        if let max = maxVersion, compareVersions(parseVersion(max), current) == .orderedAscending { return false }
        return true
    }

    private func compareVersions(_ a: [Int], _ b: [Int]) -> Foundation.ComparisonResult {
        let maxLen = max(a.count, b.count)
        for i in 0..<maxLen {
            let av = i < a.count ? a[i] : 0
            let bv = i < b.count ? b[i] : 0
            if av < bv { return .orderedAscending }
            if av > bv { return .orderedDescending }
        }
        return .orderedSame
    }

    // MARK: - Cache

    private func cacheResponse(_ response: StartupResponse) {
        if let data = try? JSONEncoder().encode(response) {
            UserDefaults.standard.set(data, forKey: cacheKey)
            UserDefaults.standard.set(Date(), forKey: cacheDateKey)
            // D037: sign the cached response with a Keychain-stored HMAC key so a local attacker
            // who writes UserDefaults cannot persist arbitrary broadcast links and update URIs
            // that the GUI presents as vendor messages.
            if let hmac = CacheIntegrityTag.sign(data) {
                UserDefaults.standard.set(hmac, forKey: cacheHMACKey)
            }
        }
    }

    private func loadCachedResponse() -> StartupResponse? {
        guard let data = UserDefaults.standard.data(forKey: cacheKey) else { return nil }
        // D037: reject unsigned or tampered cache — a missing/mismatched HMAC means the cache
        // was written by something other than Syxx.
        guard let hmac = UserDefaults.standard.data(forKey: cacheHMACKey),
              CacheIntegrityTag.verify(data, hmac: hmac) == .valid else { return nil }
        return try? JSONDecoder().decode(StartupResponse.self, from: data)
    }

    private func isCacheStale() -> Bool {
        guard let date = UserDefaults.standard.object(forKey: cacheDateKey) as? Date else { return true }
        return Date().timeIntervalSince(date) > cacheInterval
    }

    // E031: parity with Windows ParseVersion — keep every segment, defaulting non-numeric
    // or empty segments to 0 rather than dropping them. "1..2" must yield [1, 0, 2] on both
    // platforms, not [1, 2] on macOS.
    private func parseVersion(_ v: String) -> [Int] {
        v.split(separator: ".", omittingEmptySubsequences: false).map { Int($0.filter(\.isWholeNumber)) ?? 0 }
    }
}
