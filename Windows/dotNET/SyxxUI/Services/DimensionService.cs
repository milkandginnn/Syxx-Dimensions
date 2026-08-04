using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Syxx.Kit;

namespace SyxxUI.Services;

// MARK: - Response Models

public sealed record StartupResponse(
    Broadcast? Broadcast,
    UpdateInfo? Update,
    string? WhatsNewUrl
);

public sealed record Broadcast(
    string Id,
    string Title,
    string Body,
    string Severity,
    string? Url,
    string? MinVersion,
    string? MaxVersion
);

public sealed record UpdateInfo(
    bool Available,
    string Latest,
    string? Url
);

/// Consolidated client for the Dimension startup/whats-new/broadcast endpoints.
/// Mirrors macOS DimensionService.swift.
public sealed class DimensionService
{
    public static readonly DimensionService Shared = new();

    private const string BaseUrlKey = "dimension_base_url";
    private const string CacheKey = "dimension_startup_cache";
    private const string CacheDateKey = "dimension_startup_cache_date";
    private const string CacheHmacKey = "dimension_startup_cache_hmac";  // D037: HMAC for cache integrity
    private const string DismissedKey = "dimension_dismissed_broadcasts";
    // E038: parity with macOS UserDefaults "dimension_opt_out" — when true, all Dimension
    // fetches are skipped. The toggle must be reachable from the Windows UI as well.
    private const string OptOutKey = "dimension_opt_out";
    private static readonly TimeSpan CacheInterval = TimeSpan.FromHours(24);

    // E039: bound the background fetch to 15 s (macOS URLSession default is 60 s; 15 s is
    // ample for a lightweight JSON endpoint and prevents a 100 s default hang on hostile/slow
    // networks). Fire-and-forget dispatch keeps the UI responsive regardless.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private DimensionService() { }

    // Unpackaged app (WindowsPackageType=None): ApplicationData.Current requires package
    // identity and throws 0x80073D54 APPMODEL_ERROR_NO_PACKAGE, which crashed every launch
    // inside the MainWindow constructor (stowed exception 0xc000027b, no window ever shown).
    // Settings therefore live in a JSON file under %LOCALAPPDATA%\Syxx, the same directory
    // convention as DNSCapture reports and WitnessKeyStore.
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Syxx", "dimension-settings.json");

    private readonly object _settingsLock = new();
    private readonly Dictionary<string, string> _settings = LoadSettings();

    private static Dictionary<string, string> LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(SettingsPath)) ?? [];
        }
        catch
        {
            // Corrupt or unreadable settings must never block startup; start empty.
        }
        return [];
    }

    private string? GetSetting(string key)
    {
        lock (_settingsLock)
            return _settings.TryGetValue(key, out var value) ? value : null;
    }

    private void SetSetting(string key, string value)
    {
        lock (_settingsLock)
        {
            _settings[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings));
            }
            catch
            {
                // Best-effort persistence: losing the startup cache is acceptable,
                // crashing the GUI is not.
            }
        }
    }

    public string BaseURL
    {
        // D038: enforce HTTPS and restrict to approved hosts in production builds.
        // A local attacker who writes LocalSettings could otherwise redirect all control content
        // to http://attacker. The override is only permitted in DEBUG builds.
        get => SanitizeBaseURL(GetSetting(BaseUrlKey) ?? "https://syxx.app");
        set => SetSetting(BaseUrlKey, value);
    }

    /// D038: validate and sanitize the base URL. Only HTTPS on approved hosts is allowed in
    /// production; test overrides require DEBUG build configuration.
    private static string SanitizeBaseURL(string raw)
    {
#if DEBUG
        return raw;
#else
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return "https://syxx.app";
        }
        var host = uri.Host.ToLowerInvariant();
        // Approved hosts for production builds.
        string[] approvedHosts = ["syxx.app", "leak.syxx.app", "macmotd.syxx.app", "winmotd.syxx.app"];
        if (Array.Exists(approvedHosts, h => h == host) || host.EndsWith(".syxx.app", StringComparison.Ordinal))
            return raw;
        return "https://syxx.app";
#endif
    }

    // E038: opt-out gate mirroring macOS UserDefaults "dimension_opt_out". When true, all
    // Dimension server contact is suppressed — the user has chosen not to receive startup
    // broadcasts, update checks, or What's New content.
    public bool OptedOut
    {
        get => string.Equals(GetSetting(OptOutKey), "true", StringComparison.OrdinalIgnoreCase);
        set => SetSetting(OptOutKey, value ? "true" : "false");
    }

    // MARK: - Startup

    /// E129: whether the LAST FetchStartupAsync response was served from cache rather than live.
    /// Reset on each call; read immediately after the call returns.
    public bool LastResponseWasCached { get; private set; }

    public async Task<StartupResponse?> FetchStartupAsync(string platform = "win", bool force = false)
    {
        LastResponseWasCached = false;
        // E038: honor the opt-out before any network or cache access, matching macOS
        // DimensionService.swift:51 which early-returns nil when dimension_opt_out is set.
        if (OptedOut) return null;
        if (!force)
        {
            var cached = LoadCachedResponse();
            if (cached is not null && !IsCacheStale())
            {
                LastResponseWasCached = true;
                return cached;
            }
        }
        try
        {
            var url = $"{BaseURL}/api/startup?platform={platform}&version={SyxxConfig.Version}";
            var response = await _http.GetFromJsonAsync<StartupResponse>(url, _json);
            if (response is not null)
                CacheResponse(response);
            return response;
        }
        catch
        {
            var fallback = LoadCachedResponse();
            LastResponseWasCached = fallback is not null;
            return fallback;
        }
    }

    // MARK: - What's New

    public string? FetchWhatsNewUrl()
    {
        // E038: honor the opt-out before any content is surfaced, matching macOS
        // DimensionService.swift:71 which returns nil when dimension_opt_out is set.
        if (OptedOut) return null;
        // Use the dedicated per-platform MOTD origin (winmotd.syxx.app). Only an ABSOLUTE server-supplied
        // pointer may override it; a relative pointer is deliberately ignored, because gluing it onto
        // BaseURL misrouted What's-New to the leak/dimension backend instead of the MOTD host. The MOTD
        // must resolve to macmotd/winmotd, never leak.
        var cached = LoadCachedResponse();
        if (cached?.WhatsNewUrl is { } ptr)
        {
            // D103 / Phase 2.2: route through RemoteUrlPolicy.AuthorizeForNavigation so a refused
            // pointer never becomes a navigable SourceUrl. Bare StartsWith("http") admitted
            // plaintext http://, any host, and unparseable strings like "httpfoo".
            if (RemoteUrlPolicy.AuthorizeForNavigation(ptr) is not null)
                return ptr;
        }
        return "https://winmotd.syxx.app/";
    }

    // MARK: - Dismissal tracking

    public bool HasDismissed(string broadcastId)
    {
        var raw = GetSetting(DismissedKey) ?? "";
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(broadcastId);
    }

    public void MarkDismissed(string broadcastId)
    {
        var raw = GetSetting(DismissedKey) ?? "";
        var list = raw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!list.Contains(broadcastId))
        {
            list.Add(broadcastId);
            SetSetting(DismissedKey, string.Join(",", list));
        }
    }

    // MARK: - Version range check

    public bool IsVersionInRange(string version, string? minVersion, string? maxVersion)
    {
        var current = ParseVersion(version);
        if (minVersion is not null && CompareVersions(ParseVersion(minVersion), current) > 0)
            return false;
        if (maxVersion is not null && CompareVersions(ParseVersion(maxVersion), current) < 0)
            return false;
        return true;
    }

    // MARK: - Cache

    private void CacheResponse(StartupResponse response)
    {
        var json = JsonSerializer.Serialize(response, _json);
        SetSetting(CacheKey, json);
        SetSetting(CacheDateKey, DateTime.UtcNow.ToString("O"));
        // D037: sign the cached response with a DPAPI-protected HMAC key so a local attacker
        // who writes LocalSettings cannot persist arbitrary broadcast links and update URIs
        // that the GUI presents as vendor messages.
        var hmac = IntegrityTag.Sign(Encoding.UTF8.GetBytes(json));
        if (hmac != null)
            SetSetting(CacheHmacKey, Convert.ToBase64String(hmac));
    }

    private StartupResponse? LoadCachedResponse()
    {
        var json = GetSetting(CacheKey);
        if (string.IsNullOrEmpty(json)) return null;
        // D037: reject unsigned or tampered cache — a missing/mismatched HMAC means the cache
        // was written by something other than Syxx.
        var hmacBase64 = GetSetting(CacheHmacKey);
        if (string.IsNullOrEmpty(hmacBase64)) return null;
        byte[] expectedHmac;
        try { expectedHmac = Convert.FromBase64String(hmacBase64); }
        catch { return null; }
        if (!IntegrityTag.Verify(Encoding.UTF8.GetBytes(json), expectedHmac))
            return null;
        try { return JsonSerializer.Deserialize<StartupResponse>(json, _json); }
        catch { return null; }
    }

    private bool IsCacheStale()
    {
        var dateStr = GetSetting(CacheDateKey);
        if (!DateTime.TryParse(dateStr, out var date)) return true;
        return DateTime.UtcNow - date > CacheInterval;
    }

    private static int[] ParseVersion(string v)
    {
        return v.Split('.')
            .Select(part => int.TryParse(new string(part.Where(char.IsDigit).ToArray()), out var n) ? n : 0)
            .ToArray();
    }

    private static int CompareVersions(int[] a, int[] b)
    {
        var maxLen = Math.Max(a.Length, b.Length);
        for (int i = 0; i < maxLen; i++)
        {
            var av = i < a.Length ? a[i] : 0;
            var bv = i < b.Length ? b[i] : 0;
            if (av < bv) return -1;
            if (av > bv) return 1;
        }
        return 0;
    }
}
