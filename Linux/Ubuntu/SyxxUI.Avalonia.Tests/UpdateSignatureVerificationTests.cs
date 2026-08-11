using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// The update channel is a supply-chain path: whoever can serve the appcast can serve code.
/// Until 2026-07-29 UpdateService.VerifySignature decoded the signature, rebuilt the canonical
/// payload, discarded both and returned true with the comment "trust HTTPS transport for now".
/// Any well-formed manifest carrying ANY signature value, including 64 zero bytes, installed.
///
/// These tests exist to make that specific failure impossible to reintroduce quietly. The two
/// that matter most are ReplayedSignatureFromAnotherManifestIsRefused and
/// ManifestAlteredAfterSigningIsRefused: an implementation that checks "is there a signature"
/// rather than "does this signature cover this content" passes everything else and fails those.
///
/// The vectors are produced by the SERVER'S canonicalizer
/// (Server/syxx_server/appcast.py:_canon, via corpus/appcast-crossplatform/generate.py), not by
/// this platform's, so a macOS/Windows/server three-way divergence has somewhere to surface.
/// macOS asserts against the same file in
/// macOS/Tests/SyxxKitTests/UpdateSignatureVerificationTests.swift.
///
/// A missing or unreadable fixture THROWS. A vacuously-green parity test is the green-that-lies.
/// </summary>
public class UpdateSignatureVerificationTests
{
    // MARK: - Fixture loading

    private sealed record CanonicalVector(
        string Name, string Why, string Raw, string CanonicalUtf8Base64, string CanonicalSha256);

    private sealed record SignatureVector(string Name, string Why, string Raw, string Expect);

    private sealed record Vectors(
        string TestPublicKeyBase64,
        string DecoyPublicKeyBase64,
        CanonicalVector[] CanonicalVectors,
        SignatureVector[] SignatureVectors);

    private static readonly JsonSerializerOptions FixtureReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static string FixturePath([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/Windows/dotNET/Syxx.Tests/UpdateSignatureVerificationTests.cs
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("appcast golden vectors: cannot resolve source directory.");
        return Path.Combine(Path.GetFullPath(Path.Combine(dir, "..", "..", "..")),
                            "corpus", "appcast-crossplatform", "appcast-vectors.json");
    }

    private static Vectors LoadVectors()
    {
        var path = FixturePath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"appcast golden vectors missing at {path}", path);
        }
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("appcast golden vectors are empty; refusing to pass vacuously.");
        }
        var vectors = JsonSerializer.Deserialize<Vectors>(json, FixtureReaderOptions)
                      ?? throw new InvalidOperationException("appcast golden vectors did not decode.");
        // A fixture that quietly shrank to nothing would turn every test below green.
        Assert.True(vectors.CanonicalVectors.Length >= 5, "canonical vectors were removed");
        Assert.True(vectors.SignatureVectors.Length >= 15, "signature vectors were removed");
        return vectors;
    }

    private static AppcastVerification Expected(string token) => token switch
    {
        "verified" => AppcastVerification.Verified,
        "signatureMismatch" => AppcastVerification.RefusedSignatureMismatch,
        "signatureMalformed" => AppcastVerification.RefusedSignatureMalformed,
        "signatureMissing" => AppcastVerification.RefusedSignatureMissing,
        "malformedManifest" => AppcastVerification.RefusedMalformedManifest,
        _ => throw new InvalidOperationException($"unknown expectation token '{token}' in the fixture"),
    };

    private static byte[] Utf8(string raw) => Encoding.UTF8.GetBytes(raw);

    /// <summary>Parses the raw fixture text and strips the signature field, exactly as the
    /// verifier does before canonicalizing.</summary>
    private static Dictionary<string, JsonElement> UnsignedRoot(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        var root = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name != "signature")
            {
                root[prop.Name] = prop.Value.Clone();
            }
        }
        return root;
    }

    private static SignatureVector VectorNamed(Vectors vectors, string name)
    {
        var vector = vectors.SignatureVectors.FirstOrDefault(v => v.Name == name);
        Assert.True(vector is not null, $"{name} vector missing from the fixture");
        return vector!;
    }

    // MARK: - Canonical bytes must match the server, byte for byte

    /// <summary>
    /// The signature covers the SERVER'S canonical bytes. If this platform computes different
    /// bytes it either rejects every honest update or, if the divergence is on the other side,
    /// verifies bytes it does not act on. Neither is survivable, and only a server-produced
    /// fixture can catch it; a test against our own canonicalizer would be green either way.
    /// </summary>
    [Fact]
    public void CanonicalBytesMatchTheServerProducer()
    {
        var vectors = LoadVectors();
        foreach (var vector in vectors.CanonicalVectors)
        {
            var produced = AppcastCanonicalJson.CanonicalBytes(UnsignedRoot(vector.Raw));
            var expected = Convert.FromBase64String(vector.CanonicalUtf8Base64);
            Assert.True(expected.SequenceEqual(produced),
                $"{vector.Name} ({vector.Why}):\n" +
                $"  server: {Encoding.UTF8.GetString(expected)}\n" +
                $"  dotnet: {Encoding.UTF8.GetString(produced)}");
        }
    }

    /// <summary>
    /// Non-ASCII specifically: Python's json.dumps defaults to ensure_ascii=True, so the server
    /// signs \uXXXX escapes. .NET serializers emit raw UTF-8 or uppercase hex and would silently
    /// diverge here, which is why the canonicalizer is hand-rolled.
    /// </summary>
    [Fact]
    public void NonAsciiReleaseNotesAreEscapedLikePython()
    {
        var vectors = LoadVectors();
        var vector = vectors.CanonicalVectors.FirstOrDefault(v => v.Name == "non-ascii-release-notes");
        Assert.True(vector is not null, "non-ascii-release-notes vector missing from the fixture");
        var produced = Encoding.UTF8.GetString(AppcastCanonicalJson.CanonicalBytes(UnsignedRoot(vector!.Raw)));
        Assert.Contains("\\u00e9", produced, StringComparison.Ordinal);
        Assert.Contains("\\ud83d\\udd12", produced, StringComparison.Ordinal);
        Assert.DoesNotContain("é", produced, StringComparison.Ordinal);
    }

    /// <summary>Values with no repr the server, Swift and .NET are guaranteed to agree on get
    /// refused rather than guessed at.</summary>
    [Fact]
    public void NonIntegerNumberHasNoCanonicalFormAndIsRefused()
    {
        Assert.Throws<AppcastCanonicalJson.CanonicalizationException>(
            () => AppcastCanonicalJson.CanonicalBytes(UnsignedRoot("{\"publishedAt\": 1.5}")));
    }

    // MARK: - The six required cases

    /// <summary>Case 1: a valid signature over the exact canonical bytes is accepted.</summary>
    [Fact]
    public void ValidSignatureIsAccepted()
    {
        var vectors = LoadVectors();
        foreach (var vector in vectors.SignatureVectors.Where(v => v.Expect == "verified"))
        {
            var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(AppcastVerification.Verified == result, $"{vector.Name}: {vector.Why}");
        }
    }

    /// <summary>
    /// Case 2: a REAL signature, lifted from a different manifest, must be refused. This is the
    /// case a "is a signature present?" implementation passes and a real verifier fails.
    /// </summary>
    [Fact]
    public void ReplayedSignatureFromAnotherManifestIsRefused()
    {
        var vectors = LoadVectors();
        var vector = VectorNamed(vectors, "replayed-signature-from-other-manifest");
        var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
        Assert.True(AppcastVerification.RefusedSignatureMismatch == result, vector.Why);
    }

    /// <summary>
    /// Case 6: one field mutated after signing. Same class of defect as case 2, from the other
    /// direction: the signature is authentic, the content is not what was signed.
    /// </summary>
    [Fact]
    public void ManifestAlteredAfterSigningIsRefused()
    {
        var vectors = LoadVectors();
        foreach (var name in new[] { "version-altered-after-signing", "package-url-altered", "package-hash-altered" })
        {
            var vector = VectorNamed(vectors, name);
            var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(AppcastVerification.RefusedSignatureMismatch == result, $"{name}: {vector.Why}");
        }
    }

    /// <summary>Case 3: well-formed base64 garbage is refused.</summary>
    [Fact]
    public void GarbageSignatureIsRefused()
    {
        var vectors = LoadVectors();
        foreach (var name in new[] { "garbage-signature-correct-length", "garbage-signature-wrong-length", "signature-not-base64" })
        {
            var vector = VectorNamed(vectors, name);
            var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(Expected(vector.Expect) == result, $"{name}: {vector.Why}");
            Assert.True(AppcastVerification.Verified != result, $"{name} must never verify");
        }
    }

    /// <summary>Case 4: a stripped, empty, or non-string signature is refused.</summary>
    [Fact]
    public void StrippedOrEmptySignatureIsRefused()
    {
        var vectors = LoadVectors();
        foreach (var name in new[] { "signature-empty", "signature-field-absent", "signature-not-a-string" })
        {
            var vector = VectorNamed(vectors, name);
            var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(AppcastVerification.RefusedSignatureMissing == result, $"{name}: {vector.Why}");
        }
    }

    /// <summary>
    /// Case 5: no pinned key means nothing can be proven, so the update is REFUSED. Not allowed
    /// through, not warned about. A perfectly valid manifest is the right input here: the point
    /// is that validity is irrelevant when there is no trust anchor.
    /// </summary>
    [Fact]
    public void NoPinnedKeyRefusesEvenAValidManifest()
    {
        var vectors = LoadVectors();
        var valid = VectorNamed(vectors, "valid");
        var raw = Utf8(valid.Raw);

        // Sanity: this same manifest verifies when the key IS pinned, so a refusal below is
        // attributable to the missing pin and not to a broken vector.
        Assert.Equal(AppcastVerification.Verified,
            AppcastVerifier.Verify(raw, new[] { vectors.TestPublicKeyBase64 }));

        Assert.Equal(AppcastVerification.RefusedNoPinnedKey,
            AppcastVerifier.Verify(raw, Array.Empty<string>()));
        Assert.Equal(AppcastVerification.RefusedNoPinnedKey,
            AppcastVerifier.Verify(raw, new[] { "" }));
        Assert.Equal(AppcastVerification.RefusedNoPinnedKey,
            AppcastVerifier.Verify(raw, new[] { "not base64!!" }));
        // Right encoding, wrong length: 31 bytes.
        Assert.Equal(AppcastVerification.RefusedNoPinnedKey,
            AppcastVerifier.Verify(raw, new[] { Convert.ToBase64String(Enumerable.Repeat((byte)0x41, 31).ToArray()) }));
        // One good pin and one corrupt pin: refuse the whole set rather than verifying against
        // the survivor, because the build's trust anchor is not what its author thought.
        Assert.Equal(AppcastVerification.RefusedNoPinnedKey,
            AppcastVerifier.Verify(raw, new[] { vectors.TestPublicKeyBase64, "garbage" }));
    }

    /// <summary>
    /// The shipping build pins the dedicated update public key (DEV key until production
    /// rotation is owner-gated). Corpus vectors are signed by the fixture TEST key, which is
    /// deliberately different — so the shipping pin must REFUSE them (wrong key), never accept
    /// an unpinned signer. Missing/empty pin still refuses.
    /// </summary>
    [Fact]
    public void ShippingBuildPinsUpdateKeyAndRefusesUnpinnedSignatures()
    {
        var vectors = LoadVectors();
        var valid = VectorNamed(vectors, "valid");
        Assert.True(AppcastVerifier.PinnedPublicKeysBase64.Length > 0,
            "Phase 3.2 requires a pinned update public key");
        // Corpus "valid" is signed by the fixture test key, not the shipping pin.
        Assert.Equal(AppcastVerification.RefusedSignatureMismatch,
            AppcastVerifier.Verify(Utf8(valid.Raw)));
        Assert.Equal(AppcastVerification.RefusedNoPinnedKey,
            AppcastVerifier.Verify(Utf8(valid.Raw), Array.Empty<string>()));
    }

    /// <summary>
    /// A manifest signed by the DEV update private key must verify against the shipping pin.
    /// Failure case: alter metadata after signing → refuse.
    /// </summary>
    [Fact]
    public void DevUpdateKeyManifestVerifiesAgainstShippingPin()
    {
        Assert.NotEmpty(AppcastVerifier.PinnedPublicKeysBase64);
        Assert.Equal("6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=",
            AppcastVerifier.PinnedPublicKeysBase64[0]);

        // DEV seed = SHA256("syxx-dev-only-update-signing-key-v1-NOT-PRODUCTION")
        var seed = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes("syxx-dev-only-update-signing-key-v1-NOT-PRODUCTION"));
        using var key = NSec.Cryptography.Key.Import(
            NSec.Cryptography.SignatureAlgorithm.Ed25519,
            seed,
            NSec.Cryptography.KeyBlobFormat.RawPrivateKey);

        // Build via mutable JSON DOM so we can attach signature then re-parse as bytes.
        using var doc = JsonDocument.Parse(
            "{\"build\":\"dev\",\"critical\":false,\"minVersion\":\"1.0.0\"," +
            "\"packages\":[{\"sha256\":\"" + new string('a', 64) +
            "\",\"size\":100,\"type\":\"zip\",\"url\":\"https://syxx.app/dist/Syxx-9.9.9.zip\"}]," +
            "\"platform\":\"win\",\"publishedAt\":1753747200," +
            "\"releaseNotes\":\"dev pin check\",\"version\":\"9.9.9\"}");
        var root = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
            root[prop.Name] = prop.Value.Clone();
        var payload = AppcastCanonicalJson.CanonicalBytes(root);
        var sigB64 = Convert.ToBase64String(
            NSec.Cryptography.SignatureAlgorithm.Ed25519.Sign(key, payload));

        // Served text: original unsigned object + signature field (Python-server style).
        var servedObj = new Dictionary<string, object?>
        {
            ["build"] = "dev",
            ["critical"] = false,
            ["minVersion"] = "1.0.0",
            ["packages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["sha256"] = new string('a', 64),
                    ["size"] = 100,
                    ["type"] = "zip",
                    ["url"] = "https://syxx.app/dist/Syxx-9.9.9.zip",
                },
            },
            ["platform"] = "win",
            ["publishedAt"] = 1753747200L,
            ["releaseNotes"] = "dev pin check",
            ["version"] = "9.9.9",
            ["signature"] = sigB64,
        };
        var served = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(servedObj));
        Assert.Equal(AppcastVerification.Verified, AppcastVerifier.Verify(served));

        // Mutation after signing.
        servedObj["version"] = "0.0.1";
        var tampered = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(servedObj));
        Assert.Equal(AppcastVerification.RefusedSignatureMismatch, AppcastVerifier.Verify(tampered));
    }

    // MARK: - Other refusals

    /// <summary>A valid Ed25519 signature from a key that simply is not ours. Pinning is the
    /// whole point.</summary>
    [Fact]
    public void SignatureFromUnpinnedKeyIsRefused()
    {
        var vectors = LoadVectors();
        var vector = VectorNamed(vectors, "signed-by-unpinned-key");
        var raw = Utf8(vector.Raw);
        Assert.True(AppcastVerification.RefusedSignatureMismatch ==
            AppcastVerifier.Verify(raw, new[] { vectors.TestPublicKeyBase64 }), vector.Why);
        // ...and verifies once that key IS the pinned one, proving the vector is well-formed
        // and the refusal above is about pinning rather than a malformed signature.
        Assert.Equal(AppcastVerification.Verified,
            AppcastVerifier.Verify(raw, new[] { vectors.DecoyPublicKeyBase64 }));
    }

    [Fact]
    public void MalformedManifestBodiesAreRefused()
    {
        var vectors = LoadVectors();
        foreach (var name in new[] { "not-json", "json-array-not-object", "float-valued-field" })
        {
            var vector = VectorNamed(vectors, name);
            var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(AppcastVerification.RefusedMalformedManifest == result, $"{name}: {vector.Why}");
        }
    }

    /// <summary>Every vector in the fixture, so a newly added vector is exercised without
    /// editing this file, and so no vector can be added that nothing asserts on.</summary>
    [Fact]
    public void EverySignatureVectorMatchesItsExpectation()
    {
        var vectors = LoadVectors();
        foreach (var vector in vectors.SignatureVectors)
        {
            var result = AppcastVerifier.Verify(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(Expected(vector.Expect) == result, $"{vector.Name}: {vector.Why} (got {result})");
        }
    }

    /// <summary>The manifest handed to the rest of the app must come from the signed bytes.</summary>
    [Fact]
    public void VerifiedManifestIsDecodedFromTheSignedBytes()
    {
        var vectors = LoadVectors();
        var valid = VectorNamed(vectors, "valid");
        var outcome = AppcastVerifier.VerifiedManifest(Utf8(valid.Raw), new[] { vectors.TestPublicKeyBase64 });
        Assert.Equal(AppcastVerification.Verified, outcome.Verification);
        Assert.NotNull(outcome.Manifest);
        Assert.Equal("2.1.0", outcome.Manifest!.Version);
        Assert.Equal("https://syxx.app/dist/Syxx-2.1.0.dmg", outcome.Manifest.Packages.First().Url);

        // Refusals never hand back a manifest, so a caller cannot use one by mistake.
        foreach (var vector in vectors.SignatureVectors.Where(v => v.Expect != "verified"))
        {
            var refused = AppcastVerifier.VerifiedManifest(Utf8(vector.Raw), new[] { vectors.TestPublicKeyBase64 });
            Assert.True(refused.Manifest is null, $"{vector.Name} was refused but still returned a manifest");
        }
    }

    // MARK: - The artifact, not just the manifest

    /// <summary>A signed manifest pointing at an unverifiable binary is the same hole one step
    /// later.</summary>
    [Fact]
    public void PackageWithoutAFullSha256IsRefused()
    {
        const string good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f";
        const string url = "https://syxx.app/dist/Syxx-2.1.0.zip";

        _ = UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, good, 1, "zip"));

        // Empty hash must refuse (historical server defect A264).
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, "", 1, "zip")));
        // Truncated, and non-hex of the right length.
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, good.Substring(0, 63), 1, "zip")));
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, new string('z', 64), 1, "zip")));
    }

    /// <summary>Zero-byte packages are not installable — refuse before download.</summary>
    [Fact]
    public void ZeroSizePackageIsRefused()
    {
        const string good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f";
        const string url = "https://syxx.app/dist/Syxx-2.1.0.zip";
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, good, 0, "zip")));
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, good, -1, "zip")));
    }

    /// <summary>
    /// Authenticated package type must match the URL extension; mismatch refuses install.
    /// Also proves .deb is accepted as a type (Linux path) and is not forced to .zip.
    /// </summary>
    [Fact]
    public void PackageTypeExtensionMismatchIsRefused()
    {
        const string good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f";
        // type=zip but URL ends in .deb
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(
                new AppcastPackage("https://syxx.app/dist/Syxx.deb", good, 1, "zip")));
        // unknown type
        Assert.Throws<UpdateServiceException>(() =>
            UpdateService.ValidatedDownloadUrl(
                new AppcastPackage("https://syxx.app/dist/Syxx.exe", good, 1, "exe")));
        // happy: matching zip
        _ = UpdateService.ValidatedDownloadUrl(
            new AppcastPackage("https://syxx.app/dist/Syxx.zip", good, 1, "zip"));
        // happy: matching deb (Linux authenticated type)
        _ = UpdateService.ValidatedDownloadUrl(
            new AppcastPackage("https://syxx.app/dist/Syxx.deb", good, 1, "deb"));
    }

    /// <summary>The manifest must not be able to redirect the download anywhere it likes.</summary>
    [Fact]
    public void DownloadUrlIsConstrainedToProductHosts()
    {
        const string good = "9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f9f";
        static Uri Attempt(string url) =>
            UpdateService.ValidatedDownloadUrl(new AppcastPackage(url, good, 1, "zip"));

        _ = Attempt("https://syxx.app/dist/Syxx.zip");
        _ = Attempt("https://dist.syxx.app/Syxx.zip");

        Assert.Throws<UpdateServiceException>(() => Attempt("https://evil.example/Syxx.zip"));
        Assert.Throws<UpdateServiceException>(() => Attempt("http://syxx.app/Syxx.zip"));
        Assert.Throws<UpdateServiceException>(() => Attempt("file:///tmp/Syxx.zip"));
        Assert.Throws<UpdateServiceException>(() => Attempt("https://syxx.app.evil.example/Syxx.zip"));
        Assert.Throws<UpdateServiceException>(() => Attempt("https://user:pass@syxx.app/Syxx.zip"));
        Assert.Throws<UpdateServiceException>(() => Attempt("https://syxx.app:8443/Syxx.zip"));
        Assert.Throws<UpdateServiceException>(() => Attempt(""));
    }
}
