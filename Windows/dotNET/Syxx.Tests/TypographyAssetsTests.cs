using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Phase 0 parity: Syxx Dimensions typography assets and XAML declarations against the
/// macOS gold-standard manifest (24 flat .ttf + 8 OFL in macOS/Sources/SyxxApp/Resources,
/// role map at macOS IoDesignSystem.swift:172-195).
/// HONESTY SCOPE: this is a file/declaration-level suite. It proves the assets are present,
/// byte-identical, flat, and that IoDesignSystem.xaml points each role at the right file.
/// WinUI render-level font resolution (ms-appx URI loading, the "#Family Name" form, and
/// any visual parity with macOS) stays INCONCLUSIVE until a Windows build runs; SyxxUI
/// cannot compile on the macOS host where these tests execute.
/// Each test documents a mutation that must turn it red.
/// </summary>
public sealed class TypographyAssetsTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "macOS")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Windows")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the Syxx repo root (a directory containing both 'macOS' and 'Windows' " +
                $"subdirectories) walking up from {AppContext.BaseDirectory}.");
        }
    }

    private static string FontsDir => Path.Combine(RepoRoot, "Windows", "dotNET", "SyxxUI", "Assets", "Fonts");
    private static string MacResourcesDir => Path.Combine(RepoRoot, "macOS", "Sources", "SyxxApp", "Resources");
    private static string DesignSystemPath => Path.Combine(RepoRoot, "Windows", "dotNET", "SyxxUI", "Styles", "IoDesignSystem.xaml");

    // The 24-face macOS gold-standard manifest (authoritative: macOS/Sources/SyxxApp/Resources).
    public static IEnumerable<object[]> ManifestFaces() => new[]
    {
        new object[] { "Cinzel-Bold.ttf" },
        new object[] { "Cinzel-Regular.ttf" },
        new object[] { "Cinzel-SemiBold.ttf" },
        new object[] { "CormorantGaramond-Bold.ttf" },
        new object[] { "CormorantGaramond-Light.ttf" },
        new object[] { "CormorantGaramond-Medium.ttf" },
        new object[] { "CormorantGaramond-Regular.ttf" },
        new object[] { "CormorantSC-Bold.ttf" },
        new object[] { "CormorantSC-Regular.ttf" },
        new object[] { "CormorantSC-SemiBold.ttf" },
        new object[] { "EBGaramond-Italic.ttf" },
        new object[] { "EBGaramond-Regular.ttf" },
        new object[] { "JetBrainsMono-Medium.ttf" },
        new object[] { "JetBrainsMono-Regular.ttf" },
        new object[] { "JetBrainsMono-SemiBold.ttf" },
        new object[] { "Jost-Light.ttf" },
        new object[] { "Jost-Medium.ttf" },
        new object[] { "Jost-Regular.ttf" },
        new object[] { "MarcellusSC-Regular.ttf" },
        new object[] { "Spectral-Italic.ttf" },
        new object[] { "Spectral-Light.ttf" },
        new object[] { "Spectral-Medium.ttf" },
        new object[] { "Spectral-Regular.ttf" },
        new object[] { "Spectral-SemiBold.ttf" },
    };

    // The 8 OFL license files of the macOS gold-standard manifest.
    public static IEnumerable<object[]> ManifestLicenses() => new[]
    {
        new object[] { "OFL-Cinzel.txt" },
        new object[] { "OFL-Cormorant.txt" },
        new object[] { "OFL-CormorantGaramond.txt" },
        new object[] { "OFL-EBGaramond.txt" },
        new object[] { "OFL-JetBrainsMono.txt" },
        new object[] { "OFL-Jost.txt" },
        new object[] { "OFL-MarcellusSC.txt" },
        new object[] { "OFL-Spectral.txt" },
    };

    [Theory]
    [MemberData(nameof(ManifestFaces))]
    public void EveryManifestFaceExistsFlatAndNonEmpty(string fileName)
    {
        var path = Path.Combine(FontsDir, fileName);
        Assert.True(File.Exists(path), $"Missing manifest font asset {path}");
        Assert.True(new FileInfo(path).Length > 0, $"Font asset is empty {path}");
        // Failure case: rename Spectral-Light.ttf out of Assets/Fonts (or truncate it to 0 bytes) -> red.
    }

    [Theory]
    [MemberData(nameof(ManifestLicenses))]
    public void EveryManifestLicenseExistsAndCarriesSilHeader(string fileName)
    {
        var path = Path.Combine(FontsDir, fileName);
        Assert.True(File.Exists(path), $"Missing manifest license {path}");
        var text = File.ReadAllText(path);
        Assert.Contains("SIL OPEN FONT LICENSE", text, StringComparison.Ordinal);
        // Failure case: empty one OFL file (or strip the license header) -> red.
    }

    [Fact]
    public void FlatFaceCountMatchesManifestExactly()
    {
        var flatFaces = Directory.GetFiles(FontsDir, "*.ttf", SearchOption.TopDirectoryOnly);
        Assert.Equal(24, flatFaces.Length);
        // Failure case: drop any extra .ttf into the flat root (or delete one) -> red.
    }

    [Fact]
    public void NoFontFaceLivesInASubdirectory()
    {
        Assert.True(Directory.Exists(FontsDir), $"Missing fonts directory {FontsDir}");
        var nested = Directory.GetFiles(FontsDir, "*.ttf", SearchOption.AllDirectories)
            .Where(p => Path.GetDirectoryName(p) != FontsDir)
            .ToArray();
        Assert.True(nested.Length == 0,
            "Nested .ttf staging residue must not exist under Assets/Fonts: " + string.Join(", ", nested));
        // Failure case: recreate a nested folder with a .ttf (e.g. Assets/Fonts/Spectral/Spectral-Light.ttf) -> red.
    }

    [Theory]
    [MemberData(nameof(ManifestFaces))]
    public void EveryFaceIsByteIdenticalToItsMacOSTwin(string fileName)
    {
        var win = Path.Combine(FontsDir, fileName);
        var mac = Path.Combine(MacResourcesDir, fileName);
        Assert.True(File.Exists(win), $"Missing Windows face {win}");
        Assert.True(File.Exists(mac), $"Missing macOS gold-standard face {mac}");
        var hWin = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(win)));
        var hMac = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mac)));
        Assert.Equal(hMac, hWin);
        // Failure case: flip one byte in any copied face (e.g. Spectral-Light.ttf) -> hash mismatch -> red.
    }

    [Fact]
    public void DesignSystemDeclarationsMatchOwnerApprovedRoleMap()
    {
        var xaml = File.ReadAllText(DesignSystemPath);

        Assert.Equal("ms-appx:///Assets/Fonts/CormorantGaramond-Regular.ttf#Cormorant Garamond",
            FontFamilyValue(xaml, "IoTypeBody"));
        Assert.Equal("ms-appx:///Assets/Fonts/CormorantGaramond-Light.ttf#Cormorant Garamond Light",
            FontFamilyValue(xaml, "IoTypeBodyLight"));
        Assert.Equal("ms-appx:///Assets/Fonts/Spectral-Light.ttf#Spectral Light",
            FontFamilyValue(xaml, "IoTypeNavLabel"));
        Assert.DoesNotContain("Jost", FontFamilyValue(xaml, "IoTypeBody"), StringComparison.Ordinal);
        // Failure case: revert IoTypeBody to Jost-Regular.ttf#Jost (the retired Phase 3.7 mapping) -> red.
        // Key-scoped on purpose: IoFontUI/IoFontGrotesk aliases legitimately stay on Jost (Phase C sweep).
    }

    private static string FontFamilyValue(string xaml, string key)
    {
        var m = Regex.Match(xaml, $@"x:Key=""{key}"">([^<]+)</FontFamily>");
        Assert.True(m.Success, $"Missing FontFamily declaration for key {key} in {DesignSystemPath}");
        return m.Groups[1].Value;
    }
}
