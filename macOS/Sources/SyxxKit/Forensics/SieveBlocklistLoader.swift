// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Core).
// Provides the built-in ad/tracker domain lists used by BlocklistAnalyzer.
import Foundation

public enum SieveBlocklistLoader {
    /// D081: Distinguish "file loaded" from "file could not be loaded" — a bare nil return
    /// would let a missing/corrupt blocklist silently reduce analysis to "not on any list."
    public enum LoadResult: Sendable {
        case loaded(NamedDomainList)
        case failed(String)  // human-readable reason (file not found, unreadable, parse error)
    }

    public static func bundledLists() -> [NamedDomainList] {
        // C082: renamed from "NextDNS Default"/"NextDNS Strict"/"DNS-layer" — those names
        // implied provenance from the real NextDNS blocklists (millions of entries) when these
        // are actually ~50-domain hardcoded tracker samples bundled with Syxx.
        let defaultDomains = bundledDomains("nextdns-default")
        let strictDomains = bundledDomains("nextdns-strict")
        let dnsDomains = bundledDomains("dns-layer")
        return [
            NamedDomainList(name: "Syxx built-in tracker sample (n=\(defaultDomains.count))", domains: defaultDomains),
            NamedDomainList(name: "Syxx built-in strict sample (n=\(strictDomains.count))", domains: strictDomains),
            NamedDomainList(name: "Syxx built-in DNS-layer sample (n=\(dnsDomains.count))", domains: dnsDomains),
        ]
    }

    public static func loadFromFile(url: URL) -> LoadResult {
        do {
            let raw = try String(contentsOf: url, encoding: .utf8)
            let name = url.deletingPathExtension().lastPathComponent
            return .loaded(NamedDomainList(name: name, rawText: raw))
        } catch {
            return .failed("Failed to load blocklist from \(url.path): \(error.localizedDescription)")
        }
    }

    public static func loadFromPath(_ path: String) -> LoadResult {
        let url: URL
        if path.contains("://"), let parsed = URL(string: path) {
            // D026: require https-only for remote blocklists to prevent LFI (file:///) and SSRF (http://).
            // file:// could read arbitrary local files; http:// could be used for SSRF.
            guard let scheme = parsed.scheme?.lowercased(), scheme == "https" else {
                return .failed("Remote blocklist URLs must use https:// scheme (got \(parsed.scheme ?? "nil")) — file:// and http:// are rejected for security")
            }
            url = parsed
        } else {
            url = URL(fileURLWithPath: path)
        }
        return loadFromFile(url: url)
    }

    public static func builtinList(name: String, domains: [String]) -> NamedDomainList {
        NamedDomainList(name: name, domains: Set(domains))
    }

    private static func bundledDomains(_ key: String) -> Set<String> {
        switch key {
        case "nextdns-default":
            return Set([
                "doubleclick.net", "googlesyndication.com", "googleadservices.com",
                "google-analytics.com", "googletagmanager.com", "facebook.com",
                "fbcdn.net", "instagram.com", "snapchat.com", "tiktok.com",
                "adsrvr.org", "adnxs.com", "rubiconproject.com", "criteo.com",
                "criteo.net", "casalemedia.com", "moatads.com", "indexww.com",
                "pubmatic.com", "openx.net", "appnexus.com", "contextweb.com",
                "agkn.com", "sharethis.com", "taboola.com", "outbrain.com",
                "exdynsrv.com", "addthis.com", "scorecardresearch.com",
                "quantserve.com", "comscore.com", "chartbeat.com",
                "newrelic.com", "hotjar.com", "fullstory.com",
                "amplitude.com", "mixpanel.com", "segment.io",
                "optimizely.com", "crazyegg.com", "clicktale.net",
                "mouseflow.com", "luckyorange.com", "sessioncam.com",
                "demdex.net", "krxd.net", "bluekai.com", "exelator.com",
                "tapad.com", "media.net", "adroll.com", "tidaltv.com",
            ])
        case "nextdns-strict":
            return Set([
                "doubleclick.net", "googlesyndication.com", "googleadservices.com",
                "google-analytics.com", "googletagmanager.com", "facebook.com",
                "fbcdn.net", "instagram.com", "snapchat.com", "tiktok.com",
                "adsrvr.org", "adnxs.com", "rubiconproject.com", "criteo.com",
                "criteo.net", "casalemedia.com", "moatads.com", "indexww.com",
                "pubmatic.com", "openx.net", "appnexus.com", "contextweb.com",
                "agkn.com", "sharethis.com", "taboola.com", "outbrain.com",
                "exdynsrv.com", "addthis.com", "scorecardresearch.com",
                "quantserve.com", "comscore.com", "chartbeat.com",
                "newrelic.com", "hotjar.com", "fullstory.com",
                "amplitude.com", "mixpanel.com", "segment.io",
                "optimizely.com", "crazyegg.com", "clicktale.net",
                "mouseflow.com", "luckyorange.com", "sessioncam.com",
                "demdex.net", "krxd.net", "bluekai.com", "exelator.com",
                "tapad.com", "media.net", "adroll.com", "tidaltv.com",
                "pinterest.com", "linkedin.com", "twitter.com", "x.com",
                "reddit.com", "youtube.com", "ytimg.com", "googlevideo.com",
                "netflix.com", "nflxvideo.net", "hbomax.com", "disneyplus.com",
                "hulu.com", "amazonaws.com", "cloudfront.net", "akamaihd.net",
            ])
        case "dns-layer":
            return Set([
                "doubleclick.net", "googlesyndication.com", "googleadservices.com",
                "google-analytics.com", "googletagmanager.com", "facebook.com",
                "fbcdn.net", "adsrvr.org", "adnxs.com", "rubiconproject.com",
                "criteo.com", "moatads.com", "indexww.com", "pubmatic.com",
                "openx.net", "appnexus.com", "contextweb.com", "agkn.com",
                "scorecardresearch.com", "quantserve.com", "comscore.com",
                "newrelic.com", "hotjar.com", "amplitude.com", "mixpanel.com",
                "demdex.net", "krxd.net", "bluekai.com", "exelator.com",
            ])
        default:
            return Set([])
        }
    }
}
