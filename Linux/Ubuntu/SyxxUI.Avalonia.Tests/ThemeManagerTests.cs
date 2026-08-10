using Avalonia.Styling;
using Syxx.Kit;
using SyxxUI.Avalonia.Services;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="ThemeManager"/>.
/// These run headless (no Avalonia Application.Current) — they verify the normalisation logic
/// and the guard against a missing application instance.
/// </summary>
public sealed class ThemeManagerTests
{
    // ── Normalise ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("dark", ThemeManager.ModeDark)]
    [InlineData("Dark", ThemeManager.ModeDark)]
    [InlineData("DARK", ThemeManager.ModeDark)]
    [InlineData("light", ThemeManager.ModeLight)]
    [InlineData("Light", ThemeManager.ModeLight)]
    [InlineData("LIGHT", ThemeManager.ModeLight)]
    [InlineData("system", ThemeManager.ModeSystem)]
    [InlineData("System", ThemeManager.ModeSystem)]
    public void Normalise_ValidValues_ReturnsCanonicalForm(string input, string expected)
    {
        Assert.Equal(expected, ThemeManager.Normalise(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("purple")]
    [InlineData("auto")]
    [InlineData("high-contrast")]
    public void Normalise_InvalidValues_FallsBackToSystem(string? input)
    {
        Assert.Equal(ThemeManager.ModeSystem, ThemeManager.Normalise(input));
    }

    // ── ApplyTheme with no Application.Current ──────────────────────────

    [Fact]
    public void ApplyTheme_WithNoApplication_DoesNotThrow()
    {
        // In headless test context Application.Current is null.
        // ApplyTheme must silently return rather than NRE.
        var ex = Record.Exception(() => ThemeManager.ApplyTheme("dark"));
        Assert.Null(ex);
    }

    [Fact]
    public void ApplyTheme_WithNoApplication_LightDoesNotThrow()
    {
        var ex = Record.Exception(() => ThemeManager.ApplyTheme("light"));
        Assert.Null(ex);
    }

    [Fact]
    public void ApplyTheme_WithNoApplication_SystemDoesNotThrow()
    {
        var ex = Record.Exception(() => ThemeManager.ApplyTheme("system"));
        Assert.Null(ex);
    }

    // ── Mode constants ──────────────────────────────────────────────────

    [Fact]
    public void ModeConstants_AreExpectedValues()
    {
        Assert.Equal("system", ThemeManager.ModeSystem);
        Assert.Equal("dark", ThemeManager.ModeDark);
        Assert.Equal("light", ThemeManager.ModeLight);
    }

    // ── SyxxSettings.ThemeMode ──────────────────────────────────────────

    [Fact]
    public void SyxxSettings_DefaultThemeMode_IsSystem()
    {
        var settings = new SyxxSettings();
        Assert.Equal("system", settings.ThemeMode);
    }

    [Fact]
    public void SyxxSettings_ThemeMode_PersistsThroughSerialisation()
    {
        var settings = new SyxxSettings { ThemeMode = "dark" };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<SyxxSettings>(json);
        Assert.NotNull(loaded);
        Assert.Equal("dark", loaded!.ThemeMode);
    }

    [Fact]
    public void SyxxSettings_ThemeMode_LightPersists()
    {
        var settings = new SyxxSettings { ThemeMode = "light" };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<SyxxSettings>(json);
        Assert.NotNull(loaded);
        Assert.Equal("light", loaded!.ThemeMode);
    }

    [Fact]
    public void SyxxSettings_MissingThemeMode_DeserialisesToDefault()
    {
        // Simulate a settings file written before ThemeMode existed.
        var json = """{"EncryptedDnsEnabled":false,"NotificationsEnabled":true}""";
        var loaded = System.Text.Json.JsonSerializer.Deserialize<SyxxSettings>(json);
        Assert.NotNull(loaded);
        Assert.Equal("system", loaded!.ThemeMode);
    }

    // ── Normalise integration with settings ─────────────────────────────

    [Theory]
    [InlineData("dark", "dark")]
    [InlineData("light", "light")]
    [InlineData("system", "system")]
    [InlineData("garbage", "system")]
    public void Normalise_SettingsIntegration(string stored, string expected)
    {
        var settings = new SyxxSettings { ThemeMode = stored };
        Assert.Equal(expected, ThemeManager.Normalise(settings.ThemeMode));
    }
}
