using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Syxx.Kit;

/// <summary>
/// G-4 settings: one TYPED store (twin of macOS SyxxKit/SyxxSettings.swift), not scattered registry
/// reads. Persisted as JSON under LocalApplicationData (%LOCALAPPDATA%\Syxx on Windows,
/// ~/.local/share/Syxx on Linux). Keep the field set identical to the Swift twin.
///
/// HONESTY: a preference is an intent record, never a claim of effect. <c>EncryptedDnsEnabled</c>
/// records that the user asked for encryption; whether the system actually encrypted is decided by
/// <see cref="EncryptedDNSConfig.ClassifyApply"/>. Surfaces read the real state, not this stored wish.
/// </summary>
public sealed class SyxxSettings
{
    public const int CurrentSchemaVersion = 2;

    public bool EncryptedDnsEnabled { get; set; }
    public string EncryptedDnsResolverId { get; set; } = EncryptedDNSConfig.DefaultResolverId;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EncryptedDNSTransport EncryptedDnsTransport { get; set; } = EncryptedDNSTransport.Doh;

    /// Which resolver the free scans query (null = system resolver).
    public string? ScanResolverId { get; set; }

    /// Stored preference for OS notification delivery when monitoring detects a drift.
    public bool NotificationsEnabled { get; set; } = true;

    /// Telemetry (G12): once-per-launch platform+version, no identifier.
    /// B221: default must be false — a missing or absent key must resolve to "denied", not "granted".
    /// The first-run coordinator explicitly sets true when the user consents.
    public bool TelemetryEnabled { get; set; } = false;

    /// Update checks: contacts syxx.app once per launch to check for updates and news.
    /// When disabled, no startup fetch is performed. Default on.
    public bool UpdateCheckEnabled { get; set; } = true;

    /// Crash reporting: sends diagnostic reports via Sentry when the app crashes.
    /// When disabled, Sentry is not initialized. Default on.
    public bool CrashReportingEnabled { get; set; } = true;

    /// EULA acceptance: tracks whether the user has accepted the license agreement.
    /// Legacy migration input only. It does not prove acceptance of the current document.
    public bool EulaAccepted { get; set; } = false;

    /// Storybook/Wardens copy or general technical English.
    public string ExperienceMode { get; set; } = "story";

    public int SettingsSchemaVersion { get; set; } = CurrentSchemaVersion;
    public string? AgreementVersion { get; set; }
    public string? AgreementSha256 { get; set; }
    public string? AgreementAcceptedAtUtc { get; set; }
    public int? CompletedSetupVersion { get; set; }
    public string? SetupCompletedAtUtc { get; set; }
}

public static class SyxxSettingsStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Syxx", "settings.json");

    /// Load persisted settings, or defaults if absent/unreadable/corrupt. Never throws: a broken
    /// settings file falls back to defaults rather than bricking the app.
    public static SyxxSettings Load(string? path = null)
    {
        return LoadWithStatus(path) switch
        {
            SettingsLoadStatus.LoadedStatus loaded => loaded.Settings,
            SettingsLoadStatus.AbsentStatus => new SyxxSettings(),
            _ => FailClosedDefaults(),
        };
    }

    public static SettingsLoadStatus LoadWithStatus(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) { return SettingsLoadStatus.Absent(); }
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SettingsLoadStatus.Unavailable();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<SyxxSettings>(json);
            return loaded is null
                ? SettingsLoadStatus.Corrupt()
                : SettingsLoadStatus.Loaded(loaded);
        }
        // B221: JsonSerializer.Deserialize can throw NotSupportedException (unsupported type
        // in the graph) and ArgumentException in addition to JsonException. The "never throws"
        // contract requires all of them to resolve to a load status, not escape to the caller.
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return SettingsLoadStatus.Corrupt();
        }
    }

    /// Persist settings. Returns true only on a real write (a UI must not claim "saved" otherwise).
    /// B223: atomic write (temp + move) prevents truncation on crash; permission hardening via
    /// SyxxPaths matches macOS twin's 0600/0700.
    public static bool Save(SyxxSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var dir = Path.GetDirectoryName(path)!;
            SyxxPaths.EnsurePrivateDir(dir);
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmpPath, path, overwrite: true);
            SyxxPaths.RestrictToOwner(path);
            return true;
        }
        // F034: log the exception detail before collapsing to false so callers (and diagnostics)
        // can distinguish disk-full, permission-denied, and serialization failures.
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SyxxSettingsStore.Save failed ({path}): {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static PersistenceResult SaveAndVerify(
        SyxxSettings settings,
        string? path = null,
        SettingsPersistence? persistence = null)
    {
        path ??= DefaultPath;
        persistence ??= SettingsPersistence.Live;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var data = JsonSerializer.SerializeToUtf8Bytes(
                settings,
                new JsonSerializerOptions { WriteIndented = true });
            persistence.Write(data, path);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return PersistenceResult.Failure("write");
        }

        try
        {
            var decoded = JsonSerializer.Deserialize<SyxxSettings>(persistence.Read(path));
            if (decoded is null) { return PersistenceResult.Unknown("unreadable-read-back"); }
            var expected = JsonSerializer.Serialize(settings);
            var actual = JsonSerializer.Serialize(decoded);
            return StringComparer.Ordinal.Equals(expected, actual)
                ? PersistenceResult.Success()
                : PersistenceResult.Unknown("mismatched-read-back");
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return PersistenceResult.Unknown("unreadable-read-back");
        }
    }

    private static SyxxSettings FailClosedDefaults() => new()
    {
        NotificationsEnabled = false,
        TelemetryEnabled = false,
        UpdateCheckEnabled = false,
        CrashReportingEnabled = false,
    };
}
