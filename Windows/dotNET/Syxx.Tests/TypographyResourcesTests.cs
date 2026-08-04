using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Syxx Dimensions typography contract for Windows packaging and design-system keys
/// (Phase A owner-approved role map; supersedes the retired Phase 3.7 Jost-body contract —
/// same stale-contract class as macOS F071, updated to the approved mapping, not softened).
/// Each test documents a mutation that must fail.
/// </summary>
public sealed class TypographyResourcesTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "REIMPLEMENTATION-PLAN-2026-08-01.md")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            // Fallback: walk up from test project typical layout
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        }
    }

    private static string FontsDir => Path.Combine(RepoRoot, "Windows", "dotNET", "SyxxUI", "Assets", "Fonts");
    private static string DesignSystemPath => Path.Combine(RepoRoot, "Windows", "dotNET", "SyxxUI", "Styles", "IoDesignSystem.xaml");

    public static IEnumerable<object[]> RequiredFaces() => new[]
    {
        new object[] { "Jost-Light.ttf" },
        new object[] { "Jost-Regular.ttf" },
        new object[] { "Jost-Medium.ttf" },
        new object[] { "Cinzel-Regular.ttf" },
        new object[] { "Cinzel-SemiBold.ttf" },
        new object[] { "Cinzel-Bold.ttf" },
        new object[] { "CormorantGaramond-Light.ttf" },
        new object[] { "CormorantGaramond-Regular.ttf" },
        new object[] { "CormorantGaramond-Medium.ttf" },
        new object[] { "CormorantGaramond-Bold.ttf" },
        new object[] { "CormorantSC-Regular.ttf" },
        new object[] { "CormorantSC-SemiBold.ttf" },
        new object[] { "CormorantSC-Bold.ttf" },
        new object[] { "EBGaramond-Italic.ttf" },
        new object[] { "EBGaramond-Regular.ttf" },
        new object[] { "JetBrainsMono-Regular.ttf" },
        new object[] { "JetBrainsMono-Medium.ttf" },
        new object[] { "JetBrainsMono-SemiBold.ttf" },
        new object[] { "Spectral-Light.ttf" },
        new object[] { "Spectral-Regular.ttf" },
        new object[] { "Spectral-Medium.ttf" },
        new object[] { "Spectral-SemiBold.ttf" },
        new object[] { "Spectral-Italic.ttf" },
        new object[] { "MarcellusSC-Regular.ttf" },
    };

    public static IEnumerable<object[]> RequiredLicenses() => new[]
    {
        new object[] { "OFL-Jost.txt" },
        new object[] { "OFL-Cinzel.txt" },
        new object[] { "OFL-Cormorant.txt" },
        new object[] { "OFL-CormorantGaramond.txt" },
        new object[] { "OFL-EBGaramond.txt" },
        new object[] { "OFL-JetBrainsMono.txt" },
        new object[] { "OFL-Spectral.txt" },
        new object[] { "OFL-MarcellusSC.txt" },
    };

    [Theory]
    [MemberData(nameof(RequiredFaces))]
    public void RequiredFaceIsPackaged(string fileName)
    {
        var path = Path.Combine(FontsDir, fileName);
        Assert.True(File.Exists(path), $"Missing font asset {path}");
        // Failure case: delete the file from Assets/Fonts → fails.
    }

    [Theory]
    [MemberData(nameof(RequiredLicenses))]
    public void RequiredLicenseIsPackaged(string fileName)
    {
        var path = Path.Combine(FontsDir, fileName);
        Assert.True(File.Exists(path), $"Missing license {path}");
        // Failure case: remove OFL-Jost.txt → fails.
    }

    [Fact]
    public void RetiredArchivoFacesAreNotInActiveFontRoot()
    {
        foreach (var name in new[]
                 {
                     "Archivo-Regular.ttf", "Archivo-Medium.ttf", "Archivo-SemiBold.ttf", "Archivo-Bold.ttf",
                     "SpaceGrotesk-Variable.ttf", "SpaceMono-Regular.ttf"
                 })
        {
            Assert.False(File.Exists(Path.Combine(FontsDir, name)),
                $"Retired face must not remain in active font root: {name}");
        }
        // Failure case: restore Archivo-Regular.ttf into Assets/Fonts → fails.
    }

    [Fact]
    public void DesignSystemExposesSemanticTypeKeys()
    {
        var xaml = File.ReadAllText(DesignSystemPath);
        foreach (var key in new[]
                 {
                     "IoTypeDisplay", "IoTypeBodyLight", "IoTypeBody", "IoTypeBodyAlternate",
                     "IoTypeDeck", "IoTypeEmphasis", "IoTypeNavLabel", "IoTypeUiLabel", "IoTypeEyebrow", "IoTypeTechnical"
                 })
        {
            Assert.Contains(key, xaml, StringComparison.Ordinal);
        }
        Assert.Contains("Assets/Fonts/CormorantGaramond-Regular.ttf#Cormorant Garamond", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/Fonts/CormorantGaramond-Light.ttf#Cormorant Garamond Light", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/Fonts/Spectral-Light.ttf#Spectral Light", xaml, StringComparison.Ordinal);
        Assert.Contains("Assets/Fonts/Jost-Medium.ttf#Jost Medium", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Archivo", xaml, StringComparison.Ordinal);
        // Failure case: revert IoTypeBody to the retired Jost mapping → the Cormorant Garamond
        // assertions fail; drop IoTypeNavLabel → the key assertion fails.
    }

    [Fact]
    public void JostFacesMatchMacOSByteForByte()
    {
        var macRoot = Path.Combine(RepoRoot, "macOS", "Sources", "SyxxApp", "Resources");
        foreach (var weight in new[] { "Light", "Regular", "Medium" })
        {
            var win = Path.Combine(FontsDir, $"Jost-{weight}.ttf");
            var mac = Path.Combine(macRoot, $"Jost-{weight}.ttf");
            Assert.True(File.Exists(win) && File.Exists(mac), $"missing Jost-{weight}");
            var hWin = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(win)));
            var hMac = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mac)));
            Assert.Equal(hMac, hWin);
        }
        // Failure case: replace Windows Jost-Regular with a different cut → hash fails.
    }

    [Fact]
    public void CompatibilityUiKeysResolveToJostNotSystemUi()
    {
        var xaml = File.ReadAllText(DesignSystemPath);
        var ui = Regex.Match(xaml, @"x:Key=""IoFontUI"">([^<]+)</FontFamily>");
        var grotesk = Regex.Match(xaml, @"x:Key=""IoFontGrotesk"">([^<]+)</FontFamily>");
        Assert.True(ui.Success && grotesk.Success);
        Assert.Contains("Jost", ui.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("Jost", grotesk.Groups[1].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("system-ui", ui.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
        // Failure case: IoFontUI → Segoe UI / system-ui → fails.
    }
}
