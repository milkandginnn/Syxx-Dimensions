using SyxxUI.Services;
using Xunit;

namespace SyxxUI.Tests.Services;

/// <summary>
/// Tests for ThemeManager theme resolution logic.
/// These tests verify the static ResolveTheme method which doesn't require WinUI runtime.
/// </summary>
public sealed class ThemeManagerTests
{
    [Theory]
    [InlineData("dark", "dark")]
    [InlineData("Dark", "dark")]
    [InlineData("DARK", "dark")]
    [InlineData("light", "light")]
    [InlineData("Light", "light")]
    [InlineData("LIGHT", "light")]
    public void ResolveTheme_ExplicitMode_ReturnsExpected(string input, string expected)
    {
        var result = ThemeManager.ResolveTheme(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("system")]
    [InlineData("System")]
    [InlineData("SYSTEM")]
    public void ResolveTheme_SystemMode_ReturnsDarkOrLight(string input)
    {
        var result = ThemeManager.ResolveTheme(input);
        Assert.True(result == "dark" || result == "light",
            $"Expected 'dark' or 'light' but got '{result}'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("auto")]
    [InlineData("blue")]
    public void ResolveTheme_UnrecognizedInput_FallsBackToOsDetection(string input)
    {
        var result = ThemeManager.ResolveTheme(input);
        // Unrecognized values fall through to OS detection, which returns dark or light
        Assert.True(result == "dark" || result == "light",
            $"Expected 'dark' or 'light' but got '{result}'");
    }

    [Fact]
    public void DetectOsTheme_ReturnsDarkOrLight()
    {
        var result = ThemeManager.DetectOsTheme();
        Assert.True(result == "dark" || result == "light",
            $"Expected 'dark' or 'light' but got '{result}'");
    }

    [Fact]
    public void ResolveTheme_DarkIsExplicit_NotAffectedByOsTheme()
    {
        // Even if OS is light, explicit "dark" should always return dark
        var result = ThemeManager.ResolveTheme("dark");
        Assert.Equal("dark", result);
    }

    [Fact]
    public void ResolveTheme_LightIsExplicit_NotAffectedByOsTheme()
    {
        // Even if OS is dark, explicit "light" should always return light
        var result = ThemeManager.ResolveTheme("light");
        Assert.Equal("light", result);
    }
}
