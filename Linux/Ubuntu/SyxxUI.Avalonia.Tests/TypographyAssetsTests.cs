using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// Phase 0 parity: Syxx Dimensions typography assets and AXAML declarations against the
/// macOS gold-standard manifest (24 flat .ttf + 8 OFL in macOS/Sources/SyxxApp/Resources,
/// role map at macOS IoDesignSystem.swift:172-195). Twin of
/// Windows/dotNET/Syxx.Tests/TypographyAssetsTests.cs.
/// HONESTY SCOPE: this is a file/declaration-level suite. It proves the assets are present,
/// byte-identical, flat, and that App.axaml points each role at the right file. Avalonia
/// render-level font resolution (avares URI loading, the "#Family Name" form, and any visual
/// parity with macOS) stays INCONCLUSIVE until a real GUI run on a Linux display host.
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
                    Directory.Exists(Path.Combine(dir.FullName, "Linux")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the Syxx repo root (a directory containing both 'macOS' and 'Linux' " +
                $"subdirectories) walking up from {AppContext.BaseDirectory}.");
        }
    }

    private static string FontsDir => Path.Combine(RepoRoot, "Linux", "Ubuntu", "SyxxUI.Avalonia", "Assets", "Fonts");
    private static string MacResourcesDir => Path.Combine(RepoRoot, "macOS", "Sources", "SyxxApp", "Resources");
    private static string AppAxamlPath => Path.Combine(RepoRoot, "Linux", "Ubuntu", "SyxxUI.Avalonia", "App.axaml");

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
        // Failure case: rename CormorantGaramond-Regular.ttf out of Assets/Fonts (or truncate it) -> red.
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
        var lin = Path.Combine(FontsDir, fileName);
        var mac = Path.Combine(MacResourcesDir, fileName);
        Assert.True(File.Exists(lin), $"Missing Linux face {lin}");
        Assert.True(File.Exists(mac), $"Missing macOS gold-standard face {mac}");
        var hLin = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(lin)));
        var hMac = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mac)));
        Assert.Equal(hMac, hLin);
        // Failure case: flip one byte in any copied face (e.g. Spectral-Light.ttf) -> hash mismatch -> red.
    }

    [Fact]
    public void AppAxamlDeclarationsMatchOwnerApprovedRoleMap()
    {
        var axaml = File.ReadAllText(AppAxamlPath);

        Assert.Equal("avares://Syxx/Assets/Fonts/CormorantGaramond-Regular.ttf#Cormorant Garamond",
            FontFamilyValue(axaml, "SyxTypeBody"));
        Assert.Equal("avares://Syxx/Assets/Fonts/CormorantGaramond-Light.ttf#Cormorant Garamond Light",
            FontFamilyValue(axaml, "SyxTypeBodyLight"));
        Assert.Equal("avares://Syxx/Assets/Fonts/Spectral-Light.ttf#Spectral Light",
            FontFamilyValue(axaml, "SyxTypeNavLabel"));
        Assert.DoesNotContain("Jost", FontFamilyValue(axaml, "SyxTypeBody"), StringComparison.Ordinal);
        // Failure case: revert SyxTypeBody to Jost-Regular.ttf#Jost (the retired Phase 3.7 mapping) -> red.
        // Key-scoped on purpose: SyxUiFont and uiLabel/eyebrow legitimately stay on Jost (owner decision).
    }

    private static string FontFamilyValue(string axaml, string key)
    {
        var m = Regex.Match(axaml, $@"x:Key=""{key}"">([^<]+)</FontFamily>");
        Assert.True(m.Success, $"Missing FontFamily declaration for key {key} in {AppAxamlPath}");
        return m.Groups[1].Value;
    }
}
