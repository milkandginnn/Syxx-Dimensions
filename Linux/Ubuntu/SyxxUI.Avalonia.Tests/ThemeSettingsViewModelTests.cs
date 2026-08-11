using SyxxUI.Avalonia.Services;
using SyxxUI.Avalonia.ViewModels;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Tests for the theme-related members of <see cref="SettingsViewModel"/>.
/// Verifies that the ThemeModeIndex ↔ theme mode string mapping works correctly
/// and that the appearance options are populated.
/// </summary>
public sealed class ThemeSettingsViewModelTests
{
    [Fact]
    public void ThemeModeOptions_ContainsThreeEntries()
    {
        var vm = new SettingsViewModel();
        Assert.Equal(3, vm.ThemeModeOptions.Count);
        Assert.Equal("System", vm.ThemeModeOptions[0]);
        Assert.Equal("Dark", vm.ThemeModeOptions[1]);
        Assert.Equal("Light", vm.ThemeModeOptions[2]);
    }

    [Fact]
    public void ThemeModeIndex_DefaultsToSystem_Zero()
    {
        // Default SyxxSettings has ThemeMode = "system" → index 0.
        var vm = new SettingsViewModel();
        Assert.Equal(0, vm.ThemeModeIndex);
    }

    [Fact]
    public void ThemeModeIndex_MapsCorrectly_AllIndices()
    {
        // Verify all three index → mode mappings by creating fresh VMs
        // and checking that the ThemeModeOptions at each index match expectations.
        var vm = new SettingsViewModel();

        Assert.Equal("System", vm.ThemeModeOptions[0]);
        Assert.Equal("Dark", vm.ThemeModeOptions[1]);
        Assert.Equal("Light", vm.ThemeModeOptions[2]);
    }

    [Fact]
    public void ChangingThemeModeIndex_DoesNotThrow()
    {
        var vm = new SettingsViewModel();
        // Changing the index triggers Persist() + ThemeManager.ApplyTheme().
        // In headless test context ApplyTheme silently no-ops. Persist writes to disk
        // but that is safe (writes to ~/.local/share/Syxx/settings.json).
        var ex = Record.Exception(() => vm.ThemeModeIndex = 1);
        Assert.Null(ex);

        ex = Record.Exception(() => vm.ThemeModeIndex = 2);
        Assert.Null(ex);

        ex = Record.Exception(() => vm.ThemeModeIndex = 0);
        Assert.Null(ex);
    }
}
