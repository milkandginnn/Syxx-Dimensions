using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NSec.Cryptography;

namespace Syxx.Kit;

/// <summary>
/// Custom auto-update service (G-1). Sparkle-inspired but fully original.
///
/// Flow:
/// 1. Fetch appcast from /api/appcast?platform=win (or deb on Linux)
/// 2. Verify the Ed25519 signature against the PINNED update public key
/// 3. Decode the manifest FROM the signed bytes and compare version to current
/// 4. If update available, download the package from a pinned host
/// 5. Verify the signed SHA-256 hash of the artifact
/// 6. Install the update (extract and replace)
///
/// Every one of those steps is a refusal point. An update that cannot be proven
/// authentic is not an update: see <see cref="AppcastVerifier"/> for why TLS is not a
/// substitute for the signature here. Twin of macOS/Sources/SyxxKit/UpdateService.swift.
/// </summary>
public sealed class UpdateService
{
    public static UpdateService Shared { get; } = new();

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _platform;

    public event Action<UpdateState>? StateChanged;
    public event Action<double>? ProgressChanged;

    public UpdateState CurrentState { get; private set; } = new UpdateState.Idle();

    /// <summary>
    /// The manifest whose signature verified during <see cref="CheckForUpdatesAsync"/>.
    /// <see cref="DownloadAndInstallAsync"/> acts on THIS and never on a second, unverified
    /// fetch. The previous implementation re-fetched the appcast at download time and verified
    /// nothing, so the manifest that gated the offer and the manifest that supplied the download
    /// URL were different documents. That is a TOCTOU an attacker who can serve the appcast
    /// wins for free. Same fix as the macOS twin (UpdateService.swift:43).
    /// </summary>
    private AppcastManifest? _verifiedManifest;

    private UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _baseUrl = SyxxConfig.DimensionBaseURL.TrimEnd('/');
        // Detect platform: Linux uses "deb", Windows uses "win"
        _platform = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "deb" : "win";
    }

    /// <summary>Check for updates by fetching the appcast.</summary>
    public async Task CheckForUpdatesAsync()
    {
        SetState(new UpdateState.Checking());
        _verifiedManifest = null;
        try
        {
            var url = $"{_baseUrl}/api/appcast?platform={_platform}";
            var response = await _http.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                SetState(new UpdateState.UpToDate());
                return;
            }

            response.EnsureSuccessStatusCode();

            // Verify FIRST, then decode from the bytes the signature actually covered.
            // Decoding first and verifying afterwards leaves a parser-differential seam:
            // the fields the app acts on would come from one parse and the signed bytes
            // from another, and a manifest crafted to be read differently by the two
            // slips through.
            var raw = await response.Content.ReadAsByteArrayAsync();
            var outcome = AppcastVerifier.VerifiedManifest(raw);
            if (outcome.Verification != AppcastVerification.Verified || outcome.Manifest is null)
            {
                SetState(new UpdateState.Failed("Invalid update signature"));
                return;
            }

            _verifiedManifest = outcome.Manifest;

            // Compare versions
            var currentVersion = SyxxConfig.Version;
            if (IsVersionNewer(outcome.Manifest.Version, currentVersion))
            {
                SetState(new UpdateState.AvailableState(outcome.Manifest.Version, outcome.Manifest.ReleaseNotes));
            }
            else
            {
                SetState(new UpdateState.UpToDate());
            }
        }
        catch (Exception ex)
        {
            _verifiedManifest = null;
            SetState(new UpdateState.Failed(ex.Message));
        }
    }

    /// <summary>Download and install the update.</summary>
    public async Task DownloadAndInstallAsync()
    {
        if (CurrentState is not UpdateState.AvailableState avail)
            return;

        SetState(new UpdateState.Downloading(0));
        try
        {
            // Act on the manifest whose signature verified, not a fresh unverified fetch.
            var manifest = _verifiedManifest;
            if (manifest is null || manifest.Version != avail.Version)
            {
                SetState(new UpdateState.Failed("Invalid update signature"));
                return;
            }

            if (manifest.Packages == null || manifest.Packages.Length == 0)
            {
                SetState(new UpdateState.Failed("No package available"));
                return;
            }

            var package = manifest.Packages[0];
            var downloadUri = ValidatedDownloadUrl(package);

            // Download with progress — extension comes from authenticated package type,
            // never forced to .zip (Linux .deb must stay .deb).
            var tempPath = await DownloadWithProgressAsync(downloadUri, package.Type);

            // Verify SHA-256 against the SIGNED hash before any install step.
            var hash = ComputeSHA256(tempPath);
            if (!string.Equals(hash, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                SetState(new UpdateState.Failed("Download corrupted (hash mismatch)"));
                return;
            }
            var byteCount = new FileInfo(tempPath).Length;
            if (byteCount != package.Size || byteCount <= 0)
            {
                File.Delete(tempPath);
                SetState(new UpdateState.Failed("Download corrupted (hash mismatch)"));
                return;
            }

            // Install using the authenticated package type.
            SetState(new UpdateState.Installing());
            await InstallPackageAsync(tempPath, package.Type);

            // Clean up
            try { File.Delete(tempPath); } catch { }

        }
        catch (Exception ex)
        {
            SetState(new UpdateState.Failed(ex.Message));
        }
    }

    /// <summary>
    /// Installer types the authenticated manifest may name. Keep in lockstep with
    /// Server/syxx_server/appcast.py _PLATFORM_PACKAGE_TYPE and the macOS twin.
    /// </summary>
    internal static readonly HashSet<string> AllowedPackageTypes =
        new(StringComparer.Ordinal) { "dmg", "zip", "deb" };

    /// <summary>
    /// The gate between a signed manifest and an actual download.
    ///
    /// A signed manifest that points at an unverified binary on an arbitrary host is the
    /// same hole one step later, so every constraint is enforced here and every failure is
    /// a refusal, never a warning:
    ///  - the artifact must carry a full 64-hex-character SHA-256 in the SIGNED manifest,
    ///    otherwise there is nothing to check the download against;
    ///  - size must be a positive byte count from the signed metadata;
    ///  - package type must be an allowed installer type and must match the URL extension;
    ///  - the download URL must pass <see cref="RemoteUrlPolicy"/>: https, no credentials,
    ///    default port, and a host under one of the product's own registrable domains.
    ///    Without this the manifest can redirect the client anywhere.
    ///
    /// Twin of macOS UpdateService.validatedDownloadURL. The thrown messages reuse the
    /// EXISTING user-visible strings this service already shows; the generic catch in
    /// <see cref="DownloadAndInstallAsync"/> surfaces them via UpdateState.Failed.
    /// </summary>
    internal static Uri ValidatedDownloadUrl(AppcastPackage package)
    {
        var hash = (package.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
        var isFullSha256 = hash.Length == 64 && hash.All(Uri.IsHexDigit);
        if (!isFullSha256)
        {
            // PENDING COPY APPROVAL (AGENTS.md 4.7): reusing the existing
            // "Download corrupted (hash mismatch)" string, matching the macOS twin. Refusing
            // with an imprecise message beats installing an artifact nothing can verify.
            throw new UpdateServiceException("Download corrupted (hash mismatch)");
        }
        if (package.Size <= 0)
        {
            throw new UpdateServiceException("Download corrupted (hash mismatch)");
        }

        var pkgType = (package.Type ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedPackageTypes.Contains(pkgType))
        {
            throw new UpdateServiceException("Unsupported package format");
        }

        if (!RemoteUrlPolicy.Evaluate(package.Url).IsAllowed ||
            !Uri.TryCreate(package.Url, UriKind.Absolute, out var uri))
        {
            throw new UpdateServiceException("Invalid download URL");
        }
        // Reject signed type/extension mismatch before any download bytes are fetched.
        var pathExt = Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        if (!string.Equals(pathExt, pkgType, StringComparison.Ordinal))
        {
            throw new UpdateServiceException("Unsupported package format");
        }
        return uri;
    }

    private void SetState(UpdateState state)
    {
        CurrentState = state;
        StateChanged?.Invoke(state);
    }

    /// <summary>
    /// Downloads to a temp path whose extension is taken from the authenticated package type
    /// (zip vs deb). Forcing every package into a .zip path made Linux .deb installs impossible.
    /// </summary>
    private async Task<string> DownloadWithProgressAsync(Uri uri, string packageType)
    {
        var pkgType = (packageType ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedPackageTypes.Contains(pkgType))
        {
            throw new UpdateServiceException("Unsupported package format");
        }
        var tempPath = Path.Combine(Path.GetTempPath(), $"Syxx-update-{Guid.NewGuid()}.{pkgType}");

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long bytesRead = 0;
        int read;

        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read);
            bytesRead += read;

            if (totalBytes > 0)
            {
                ProgressChanged?.Invoke((double)bytesRead / totalBytes);
                SetState(new UpdateState.Downloading((double)bytesRead / totalBytes));
            }
        }

        return tempPath;
    }

    private string ComputeSHA256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    private async Task InstallPackageAsync(string path, string packageType)
    {
        var type = (packageType ?? string.Empty).Trim().ToLowerInvariant();
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        // Authenticated type and on-disk extension must agree before the installer runs.
        if (!string.Equals(type, ext, StringComparison.Ordinal) || !AllowedPackageTypes.Contains(type))
        {
            throw new UpdateServiceException("Unsupported package format");
        }

        if (type == "zip")
        {
            await InstallZipAsync(path);
        }
        else if (type == "deb")
        {
            await InstallDebAsync(path);
        }
        else
        {
            // .dmg is not a Windows/Linux installer path.
            throw new UpdateServiceException("Unsupported package format");
        }
    }

    private async Task InstallZipAsync(string path)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"SyxxUpdate-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract
            System.IO.Compression.ZipFile.ExtractToDirectory(path, tempDir);

            // Find the executable
            var exeFiles = Directory.GetFiles(tempDir, "*.exe", SearchOption.AllDirectories);
            if (exeFiles.Length == 0)
            {
                throw new InvalidOperationException("No executable found in update package");
            }

            var currentExe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(currentExe))
            {
                throw new InvalidOperationException("Cannot determine current executable location");
            }

            var currentDir = Path.GetDirectoryName(currentExe)!;
            var backupDir = currentDir + ".backup";

            // Remove any existing backup
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }

            // Move current to backup
            Directory.Move(currentDir, backupDir);

            try
            {
                // Copy new files
                CopyDirectory(tempDir, currentDir);
            }
            catch
            {
                // Restore backup on failure
                if (Directory.Exists(currentDir))
                {
                    Directory.Delete(currentDir, true);
                }
                Directory.Move(backupDir, currentDir);
                throw;
            }

            // Clean up backup
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }
        }
        finally
        {
            // Clean up temp
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }

        await Task.CompletedTask;
    }

    private void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }

    private async Task InstallDebAsync(string path)
    {
        // On Linux, use dpkg to install the .deb package
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new InvalidOperationException(".deb packages can only be installed on Linux");
        }

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/pkexec",
                Arguments = $"dpkg -i \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await Task.Run(() => process.WaitForExit());

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dpkg install failed with exit code {process.ExitCode}");
        }
    }

    private bool IsVersionNewer(string v1, string v2)
    {
        var parts1 = v1.Split('.');
        var parts2 = v2.Split('.');

        for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
        {
            var p1 = i < parts1.Length && int.TryParse(parts1[i], out var n1) ? n1 : 0;
            var p2 = i < parts2.Length && int.TryParse(parts2[i], out var n2) ? n2 : 0;

            if (p1 > p2) return true;
            if (p1 < p2) return false;
        }
        return false;
    }
}

/// <summary>
/// Raised by update refusal points whose message is surfaced verbatim through the existing
/// UpdateState.Failed path. Messages are the strings this service already showed the user;
/// no new user-facing wording is introduced here.
/// </summary>
public sealed class UpdateServiceException : Exception
{
    public UpdateServiceException(string message) : base(message) { }
}

// MARK: - Appcast signature verification

/// <summary>
/// Tri-state outcome of appcast verification. Never a bare bool: "refused because this build
/// carries no trust anchor" and "refused because the signature does not cover this content"
/// are different facts and a caller that cannot tell them apart cannot report honestly.
///
/// Only <see cref="Verified"/> may lead to an install. Every other case is a refusal.
/// Twin of macOS AppcastVerification (UpdateService.swift:347).
/// </summary>
public enum AppcastVerification
{
    /// <summary>The signature is a valid Ed25519 signature, by a pinned key, over exactly these bytes.</summary>
    Verified,
    /// <summary>This build has no usable pinned update key, so nothing can be proven. Refuse.</summary>
    RefusedNoPinnedKey,
    /// <summary>The body is not a JSON object, or contains a value with no cross-platform-stable
    /// canonical form, or does not decode to a manifest.</summary>
    RefusedMalformedManifest,
    /// <summary>No `signature` field, empty, or not a JSON string.</summary>
    RefusedSignatureMissing,
    /// <summary>The `signature` field is present but is not 64 bytes of base64.</summary>
    RefusedSignatureMalformed,
    /// <summary>A well-formed signature that does not verify over these bytes with a pinned key.
    /// Includes replayed signatures from other manifests and signatures by unpinned keys.</summary>
    RefusedSignatureMismatch,
}

/// <summary>
/// Verifies the Ed25519 signature on an appcast manifest against a key pinned in this binary.
///
/// WHY THIS IS NOT OPTIONAL. Until 2026-07-29 both clients decoded the signature, rebuilt the
/// canonical payload, threw both away and returned true, with the comment "trust HTTPS transport
/// for now". That is the exact reasoning this product exists to refute. Syxx detects DNS
/// tampering, hijacking and spoofing; it cannot assume the network or the origin is honest for
/// its own update channel. TLS authenticates the transport; the signature is what defends
/// against a compromised or impersonated origin, which is precisely the threat model. Anyone
/// able to serve the appcast could push an arbitrary update to both platforms.
///
/// FAIL CLOSED, without exception. There is deliberately no "if no key is configured, allow"
/// path, no debug bypass, and no runtime key fetch. Fetching the verification key from the same
/// origin that serves the manifest is not verification.
///
/// Twin of macOS AppcastVerifier (UpdateService.swift:377).
/// </summary>
public static class AppcastVerifier
{
    /// <summary>
    /// Ed25519 public keys, base64 raw 32 bytes, that may sign an update manifest.
    ///
    /// Pinned in this binary — never fetched from the manifest origin. The value below is the
    /// DEV-ONLY update public key under Server/dev-keys/update-signing.pub.b64 (see that
    /// directory's README). It is deliberately NOT the collector / leak identity.
    ///
    /// Production key rotation is owner-gated: replace this pin on macOS, Windows, and Linux
    /// together only after the owner provisions a production update-signing key offline.
    /// Do NOT populate or rotate this by fetching from the server, and do NOT add a bypass.
    /// </summary>
    public static readonly string[] PinnedPublicKeysBase64 =
    {
        "6OcHid3EjTbWJvwIkR10VjQnP+0W9fHTv+wt1NuBqFU=",
    };

    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    /// <summary>
    /// The read path for the SIGNED bytes. Case-insensitive because the server emits camelCase
    /// keys ("version") and the manifest records use .NET-cased properties (Version).
    /// </summary>
    private static readonly JsonSerializerOptions SignedBytesReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Manifest is non-null only when Verification == Verified, and is decoded from
    /// the SIGNED bytes.</summary>
    internal readonly record struct ManifestOutcome(
        AppcastVerification Verification,
        AppcastManifest? Manifest);

    /// <summary>
    /// Verifies <paramref name="rawManifest"/> and, only on success, decodes the manifest from
    /// the signed bytes. Passing null pins means "use the production pin set"; passing an empty
    /// array is an explicit no-trust-anchor refusal, never a fallback.
    /// </summary>
    internal static ManifestOutcome VerifiedManifest(
        byte[] rawManifest,
        IReadOnlyList<string>? pinnedPublicKeysBase64 = null)
    {
        var pins = ParsedPins(pinnedPublicKeysBase64 ?? PinnedPublicKeysBase64);
        if (pins is null)
        {
            return new ManifestOutcome(AppcastVerification.RefusedNoPinnedKey, null);
        }

        Dictionary<string, JsonElement> root;
        try
        {
            using var doc = JsonDocument.Parse(rawManifest);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ManifestOutcome(AppcastVerification.RefusedMalformedManifest, null);
            }
            // Clone: elements must outlive the disposed document. Last key wins on duplicates,
            // matching macOS JSONSerialization dictionary behavior.
            root = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                root[prop.Name] = prop.Value.Clone();
            }
        }
        catch (JsonException)
        {
            return new ManifestOutcome(AppcastVerification.RefusedMalformedManifest, null);
        }

        if (!root.TryGetValue("signature", out var signatureElement) ||
            signatureElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrEmpty(signatureElement.GetString()))
        {
            return new ManifestOutcome(AppcastVerification.RefusedSignatureMissing, null);
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureElement.GetString()!);
        }
        catch (FormatException)
        {
            return new ManifestOutcome(AppcastVerification.RefusedSignatureMalformed, null);
        }
        if (signature.Length != 64)
        {
            return new ManifestOutcome(AppcastVerification.RefusedSignatureMalformed, null);
        }

        root.Remove("signature");
        byte[] payload;
        try
        {
            payload = AppcastCanonicalJson.CanonicalBytes(root);
        }
        catch (AppcastCanonicalJson.CanonicalizationException)
        {
            return new ManifestOutcome(AppcastVerification.RefusedMalformedManifest, null);
        }

        var anyPinVerifies = pins.Any(pin => Algorithm.Verify(pin, payload, signature));
        if (!anyPinVerifies)
        {
            return new ManifestOutcome(AppcastVerification.RefusedSignatureMismatch, null);
        }

        // Decode from the bytes that were signed, so the fields the app acts on are the
        // fields the signature covers.
        AppcastManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AppcastManifest>(payload, SignedBytesReaderOptions);
        }
        catch (JsonException)
        {
            manifest = null;
        }
        if (manifest is null)
        {
            return new ManifestOutcome(AppcastVerification.RefusedMalformedManifest, null);
        }
        return new ManifestOutcome(AppcastVerification.Verified, manifest);
    }

    /// <summary>Verification only, for callers that do not need the decoded manifest.</summary>
    internal static AppcastVerification Verify(
        byte[] rawManifest,
        IReadOnlyList<string>? pinnedPublicKeysBase64 = null)
        => VerifiedManifest(rawManifest, pinnedPublicKeysBase64).Verification;

    /// <summary>
    /// All-or-nothing: if ANY configured pin is unusable the build's trust anchor is not what
    /// its author thought it was, so the whole set is refused rather than silently verifying
    /// against the survivors.
    /// </summary>
    private static List<PublicKey>? ParsedPins(IReadOnlyList<string> base64Keys)
    {
        if (base64Keys.Count == 0)
        {
            return null;
        }
        var keys = new List<PublicKey>(base64Keys.Count);
        foreach (var encoded in base64Keys)
        {
            byte[] raw;
            try
            {
                raw = Convert.FromBase64String(encoded);
            }
            catch (FormatException)
            {
                return null;
            }
            if (raw.Length != 32)
            {
                return null;
            }
            try
            {
                keys.Add(PublicKey.Import(Algorithm, raw, KeyBlobFormat.RawPublicKey));
            }
            catch
            {
                return null;
            }
        }
        return keys;
    }
}

/// <summary>
/// Reproduces the server's signing canonicalization byte for byte.
///
/// The producer is Server/syxx_server/appcast.py:56-58:
/// `json.dumps(obj, separators=(",", ":"), sort_keys=True).encode("utf-8")`, with Python's
/// default `ensure_ascii=True`. That last default is the trap: Python emits \uXXXX for every
/// character outside 0x20-0x7E, so a client that re-serializes with raw UTF-8 (which .NET's
/// UnsafeRelaxedJsonEscaping does) computes different bytes the moment a release note contains
/// an accent, and then rejects every honest update. Hand-rolled rather than delegated to
/// JsonSerializer for exactly that reason. Twin of macOS AppcastCanonicalJSON
/// (UpdateService.swift:474). Pinned against server-produced vectors in
/// corpus/appcast-crossplatform/appcast-vectors.json.
/// </summary>
internal static class AppcastCanonicalJson
{
    /// <summary>
    /// A value with no canonical form both platforms and the server agree on, e.g. a
    /// non-integer number. Refused rather than guessed at.
    /// </summary>
    internal sealed class CanonicalizationException : Exception
    {
        public CanonicalizationException(string message) : base(message) { }
    }

    internal static byte[] CanonicalBytes(IReadOnlyDictionary<string, JsonElement> obj)
    {
        var sb = new StringBuilder();
        EncodeObject(obj, sb);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void Encode(JsonElement value, StringBuilder sb)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                // Last key wins on duplicates, matching the verifier's root parse.
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var prop in value.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value;
                }
                EncodeObject(dict, sb);
                break;
            case JsonValueKind.Array:
                sb.Append('[');
                var first = true;
                foreach (var element in value.EnumerateArray())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Encode(element, sb);
                }
                sb.Append(']');
                break;
            case JsonValueKind.String:
                EncodeString(value.GetString()!, sb);
                break;
            case JsonValueKind.Number:
                EncodeNumber(value, sb);
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            case JsonValueKind.Null:
                sb.Append("null");
                break;
            default:
                throw new CanonicalizationException($"unsupported JSON value kind {value.ValueKind}");
        }
    }

    private static void EncodeObject(IReadOnlyDictionary<string, JsonElement> dict, StringBuilder sb)
    {
        // Python's sort_keys orders by Unicode code point. UTF-8 byte order is code-point
        // order, so sorting on UTF-8 bytes matches Python and, unlike UTF-16 ordinal
        // ordering (string.CompareOrdinal), stays correct for astral-plane keys. macOS
        // sorts the same way (UpdateService.swift:513).
        var sorted = dict.Keys.ToList();
        sorted.Sort(CompareUtf8Bytes);
        sb.Append('{');
        for (var i = 0; i < sorted.Count; i++)
        {
            if (i > 0) sb.Append(',');
            EncodeString(sorted[i], sb);
            sb.Append(':');
            Encode(dict[sorted[i]], sb);
        }
        sb.Append('}');
    }

    private static int CompareUtf8Bytes(string a, string b)
    {
        var lhs = Encoding.UTF8.GetBytes(a);
        var rhs = Encoding.UTF8.GetBytes(b);
        var n = Math.Min(lhs.Length, rhs.Length);
        for (var i = 0; i < n; i++)
        {
            if (lhs[i] != rhs[i]) return lhs[i] < rhs[i] ? -1 : 1;
        }
        return lhs.Length.CompareTo(rhs.Length);
    }

    private static void EncodeNumber(JsonElement number, StringBuilder sb)
    {
        // Non-integer numbers have no repr the server (Python float.__repr__), Swift and
        // .NET are guaranteed to agree on, so there is no honest canonical form. Refuse.
        // The raw-text check also refuses exponent forms like 1e3, which macOS refuses too
        // (JSONSerialization parses them as floating point).
        var raw = number.GetRawText();
        if (raw.IndexOfAny(NonIntegerMarkers) >= 0 || !number.TryGetInt64(out var value))
        {
            throw new CanonicalizationException("non-integer numbers have no canonical form");
        }
        sb.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static readonly char[] NonIntegerMarkers = { '.', 'e', 'E' };

    /// <summary>
    /// Python's ESCAPE_ASCII rules: escape `"` and `\`, use the short forms for
    /// \b \f \n \r \t, and emit \uXXXX (lowercase hex, surrogate pairs above the BMP) for
    /// everything outside printable ASCII 0x20-0x7E. Forward slash is NOT escaped.
    /// Iterating UTF-16 chars emits astral characters as surrogate pairs naturally,
    /// which is exactly Python's output.
    /// </summary>
    private static void EncodeString(string value, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c >= 0x20 && c <= 0x7E)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    break;
            }
        }
        sb.Append('"');
    }
}

// MARK: - Models

/// <summary>
/// Decoded from the CANONICAL (signed) bytes, which by construction no longer carry the
/// `signature` field, hence no Signature property here. Nothing downstream should read a
/// field that was not covered by the signature.
/// </summary>
public record AppcastManifest(
    string Platform,
    string Version,
    string Build,
    string MinVersion,
    bool Critical,
    string ReleaseNotes,
    AppcastPackage[] Packages,
    long PublishedAt
);

public record AppcastPackage(
    string Url,
    string Sha256,
    long Size,
    string Type
);

// MARK: - Update State

public abstract record UpdateState
{
    public record Idle : UpdateState;
    public record Checking : UpdateState;
    public record UpToDate : UpdateState;
    public record AvailableState(string Version, string ReleaseNotes) : UpdateState;
    public record Downloading(double Progress) : UpdateState;
    public record Installing : UpdateState;
    public record Failed(string Reason) : UpdateState;

    // Convenience constructors
    public static UpdateState Idle_ => new Idle();
    public static UpdateState Checking_ => new Checking();
    public static UpdateState UpToDate_ => new UpToDate();
    public static UpdateState Available(string version, string notes) => new AvailableState(version, notes);
    public static UpdateState Downloading_(double progress) => new Downloading(progress);
    public static UpdateState Installing_ => new Installing();
    public static UpdateState Failed_(string reason) => new Failed(reason);
}
