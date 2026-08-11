using System;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Syxx.Kit;

namespace SyxxUI.Services;

/// <summary>
/// Manages dark/light theme switching for the Syxx Windows app.
/// Reads the user's theme preference from SyxxSettings and applies the matching
/// resource dictionary. Supports "system" (follow OS), "dark", and "light" modes.
/// Swaps MergedDictionaries at runtime so DynamicResource references update immediately.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string LightDictionaryPath = "Styles/IoDesignSystem.Light.xaml";

    private readonly Application _app;
    private string _currentTheme = "dark";
    private bool _disposed;
    private UserPreferenceChangedEventHandler? _systemThemeHandler;

    public ThemeManager(Application app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    /// <summary>The currently active theme: "dark" or "light".</summary>
    public string CurrentTheme => _currentTheme;

    /// <summary>
    /// Applies the specified theme mode. Accepts "system", "dark", or "light".
    /// When "system", detects the OS theme and applies accordingly.
    /// Persists the choice to settings.
    /// </summary>
    public void ApplyTheme(string themeMode)
    {
        var resolved = ResolveTheme(themeMode);
        if (resolved == _currentTheme && IsLightDictionaryLoaded() == (resolved == "light"))
        {
            return;
        }

        _currentTheme = resolved;

        if (resolved == "light")
        {
            LoadLightOverrides();
        }
        else
        {
            RemoveLightOverrides();
        }
    }

    /// <summary>
    /// Subscribes to OS theme changes so "system" mode auto-updates.
    /// Call once after construction.
    /// </summary>
    public void SubscribeToSystemThemeChanges(SyxxSettings settings)
    {
        try
        {
            _systemThemeHandler = (_, _) =>
            {
                if (string.Equals(settings.ThemeMode, "system", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyTheme("system");
                }
            };
            SystemEvents.UserPreferenceChanged += _systemThemeHandler;
        }
        catch
        {
            // SystemEvents may not be available in all hosting contexts; best-effort.
        }
    }

    /// <summary>
    /// Resolves "system" to "dark" or "light" by querying the Windows registry.
    /// Falls back to "dark" if detection fails.
    /// </summary>
    internal static string ResolveTheme(string themeMode)
    {
        if (string.Equals(themeMode, "dark", StringComparison.OrdinalIgnoreCase))
        {
            return "dark";
        }
        if (string.Equals(themeMode, "light", StringComparison.OrdinalIgnoreCase))
        {
            return "light";
        }
        // "system" or unrecognized → detect OS theme
        return DetectOsTheme();
    }

    /// <summary>
    /// Detects the current OS theme by reading the Windows registry AppsUseLightTheme value.
    /// Returns "light" if the OS is in light mode, "dark" otherwise (default).
    /// </summary>
    internal static string DetectOsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int intVal)
            {
                return intVal == 0 ? "dark" : "light";
            }
        }
        catch
        {
            // Registry access may fail in sandboxed contexts; fall through to default.
        }
        return "dark";
    }

    private bool IsLightDictionaryLoaded()
    {
        var merged = _app.Resources.MergedDictionaries;
        foreach (var dict in merged)
        {
            if (dict.Source?.OriginalString?.Contains("Light", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }
        return false;
    }

    private void LoadLightOverrides()
    {
        if (IsLightDictionaryLoaded())
        {
            return;
        }

        var lightDict = new ResourceDictionary
        {
            Source = new Uri($"ms-appx:///{LightDictionaryPath}", UriKind.Absolute)
        };
        _app.Resources.MergedDictionaries.Add(lightDict);
    }

    private void RemoveLightOverrides()
    {
        var merged = _app.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source?.OriginalString?.Contains("Light", StringComparison.OrdinalIgnoreCase) == true)
            {
                merged.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_systemThemeHandler is not null)
            {
                SystemEvents.UserPreferenceChanged -= _systemThemeHandler;
                _systemThemeHandler = null;
            }
        }
        catch { /* best-effort cleanup */ }
    }
}
