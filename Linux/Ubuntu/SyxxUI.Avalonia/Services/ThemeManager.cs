using Avalonia;
using Avalonia.Styling;
using Syxx.Kit;

namespace SyxxUI.Avalonia.Services;

/// <summary>
/// Manages dark/light/system theme switching for the Avalonia client.
/// Applies the requested <see cref="ThemeVariant"/> to the running application and persists
/// the choice via <see cref="SyxxSettings.ThemeMode"/>. "system" delegates to the OS
/// colour-scheme preference (FluentAvalonia's PreferSystemTheme).
/// </summary>
public static class ThemeManager
{
    /// The three valid theme mode values. Case-insensitive on read; always stored lowercase. */
    public const string ModeSystem = "system";
    public const string ModeDark = "dark";
    public const string ModeLight = "light";

    /// <summary>
    /// Apply the given theme mode to the running application.
    /// <list type="bullet">
    ///   <item>"system" → <see cref="ThemeVariant.Default"/> (FluentAvalonia follows the OS).</item>
    ///   <item>"dark" → <see cref="ThemeVariant.Dark"/>.</item>
    ///   <item>"light" → <see cref="ThemeVariant.Light"/>.</item>
    /// </list>
    /// Unknown values fall back to "system" rather than crashing — a corrupt settings file must
    /// not brick the UI.
    /// </summary>
    public static void ApplyTheme(string themeMode)
    {
        var app = Application.Current;
        if (app is null) return;

        var normalised = Normalise(themeMode);
        app.RequestedThemeVariant = normalised switch
        {
            ModeDark => ThemeVariant.Dark,
            ModeLight => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    /// <summary>
    /// Persist the chosen theme mode and apply it immediately.
    /// Returns true when the settings file was written successfully.
    /// </summary>
    public static bool ApplyAndPersist(string themeMode, SyxxSettings settings)
    {
        settings.ThemeMode = Normalise(themeMode);
        ApplyTheme(settings.ThemeMode);
        return SyxxSettingsStore.Save(settings);
    }

    /// <summary>
    /// Read the current theme mode from settings and apply it.
    /// Called once during startup (from <see cref="App.OnFrameworkInitializationCompleted"/>).
    /// </summary>
    public static void ApplyFromSettings(SyxxSettings settings) =>
        ApplyTheme(settings.ThemeMode);

    /// <summary>
    /// Normalise an arbitrary string to one of the three valid mode constants.
    /// Null, empty, and unrecognised values all map to <see cref="ModeSystem"/>.
    /// </summary>
    public static string Normalise(string? themeMode) =>
        string.Equals(themeMode, ModeDark, StringComparison.OrdinalIgnoreCase) ? ModeDark :
        string.Equals(themeMode, ModeLight, StringComparison.OrdinalIgnoreCase) ? ModeLight :
        ModeSystem;
}
