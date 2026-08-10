using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Syxx.Kit;

/// <summary>
/// Windows DPAPI-based <see cref="ISecretStore"/> implementation. Encrypts secrets with
/// Win32 CryptProtectData (user-scoped DPAPI) and stores cipher blobs under
/// <c>~/.syxx/keystore/</c>.
///
/// THIS IS THE ONLY FILE that imports crypt32.dll / kernel32.dll. It is decorated with
/// [SupportedOSPlatform("windows")] and must NEVER be instantiated on non-Windows platforms.
/// Linux/macOS callers use <see cref="LinuxSecretStore"/> instead.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class DpapiSecretStore : ISecretStore
{
    // ── DPAPI P/Invoke (Win32 crypt32.dll) ────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    // ── Store directory ───────────────────────────────────────────────────────────

    private static readonly string StoreDir;

    static DpapiSecretStore()
    {
        // E187: use LocalApplicationData (not Roaming) — DPAPI blobs are machine-specific
        // and should not roam across devices via folder redirection or profile sync.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                      ?? Environment.GetEnvironmentVariable("LOCALAPPDATA")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        StoreDir = Path.Combine(appData, "Syxx", "keystore");
    }

    // ── ISecretStore implementation ───────────────────────────────────────────────

    public void Store(string keyType, string keyId, string secret)
    {
        var path = GetStorePath(keyType, keyId);
        var dir = Path.GetDirectoryName(path);

        // WG-042: always apply EnsurePrivateDir — idempotent on existing directories.
        // On Windows this creates the directory with inherited ACLs; on Linux it sets 0700.
        // Must run even if the directory already exists, to repair permissions if they drifted.
        if (dir != null)
            SyxxPaths.EnsurePrivateDir(dir);

        // E206: on Windows, additionally restrict the keystore directory ACL to the current user.
        // This prevents other local users from enumerating or reading DPAPI blobs.
        if (dir != null && OperatingSystem.IsWindows())
        {
            try
            {
                var dirInfo = new DirectoryInfo(dir);
                var acl = dirInfo.GetAccessControl();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    Environment.UserName,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                    System.Security.AccessControl.PropagationFlags.None,
                    System.Security.AccessControl.AccessControlType.Allow));
                dirInfo.SetAccessControl(acl);
            }
            catch (PlatformNotSupportedException) { /* non-Windows or container — skip */ }
            catch (System.UnauthorizedAccessException) { /* insufficient privilege — skip */ }
        }

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var entropy = GetEntropy(keyType);
        var ciphertext = Protect(plaintext, entropy);
        File.WriteAllBytes(path, ciphertext);
    }

    public string? Retrieve(string keyType, string keyId)
    {
        try
        {
            var path = GetStorePath(keyType, keyId);
            if (!File.Exists(path)) return null;
            var ciphertext = File.ReadAllBytes(path);
            if (ciphertext.Length == 0) return null;

            // E043 migration: try machine-specific entropy first, then fall back to the legacy
            // static entropy for blobs written before the E043 change.
            byte[]? plaintext = null;
            try
            {
                plaintext = Unprotect(ciphertext, GetEntropy(keyType));
            }
            catch
            {
                // New entropy failed — try legacy static entropy for pre-E043 blobs.
                try
                {
                    plaintext = Unprotect(ciphertext, GetLegacyStaticEntropy(keyType));
                }
                catch
                {
                    return null;
                }
            }

            try
            {
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                // E166: zero decrypted plaintext from memory to prevent secret leakage
                // via GC heap dumps, crash dumps, or memory forensics.
                Array.Clear(plaintext);
                Array.Clear(ciphertext);
            }
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string keyType, string keyId)
    {
        try
        {
            var path = GetStorePath(keyType, keyId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// G-02: definitive for this store — every secret is one file named
    /// <c>{keyType}_{keyId}</c> in a single directory, so the keyType prefix can be enumerated
    /// without knowing any keyId. An undecryptable blob still counts as Present: the question is
    /// whether a secret EXISTS, and a blob we cannot read is emphatically not an absent one.
    /// An IO/permission failure yields <see cref="SecretExistence.Unknown"/> — never Absent, which
    /// would license the caller to mint over a key that is sitting right there.
    /// </summary>
    public SecretExistence Exists(string keyType)
    {
        try
        {
            if (!Directory.Exists(StoreDir))
            {
                // No store directory: this user has never had a secret written here.
                return SecretExistence.Absent;
            }
            return Directory.EnumerateFiles(StoreDir, Sanitize(keyType) + "_*").Any()
                ? SecretExistence.Present
                : SecretExistence.Absent;
        }
        catch
        {
            return SecretExistence.Unknown;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// WG-042: builds the store path and validates it cannot escape <see cref="StoreDir"/>.
    /// Even though <see cref="Sanitize"/> rejects ".." and directory separators, a defense-in-depth
    /// check resolves the full path and confirms it is rooted under the expected directory. This
    /// catches any encoding or platform-specific bypass that character-level sanitization might miss
    /// (e.g. Windows short-name aliases, Unicode normalization, or future Sanitize regressions).
    private static string GetStorePath(string keyType, string keyId)
    {
        var candidate = Path.Combine(StoreDir, $"{Sanitize(keyType)}_{Sanitize(keyId)}");
        var fullPath = Path.GetFullPath(candidate);
        var storeDirFull = Path.GetFullPath(StoreDir);

        // Ensure the resolved path is rooted under the keystore directory.
        // Path.EndsInDirectorySeparator handles trailing separator differences.
        if (!fullPath.StartsWith(storeDirFull, StringComparison.OrdinalIgnoreCase)
            || (fullPath.Length > storeDirFull.Length
                && fullPath[storeDirFull.Length] != Path.DirectorySeparatorChar
                && fullPath[storeDirFull.Length] != Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException(
                $"WG-042: resolved keystore path '{fullPath}' escapes the expected directory '{storeDirFull}'");
        }

        return fullPath;
    }

    private static string Sanitize(string value)
    {
        // WG-042: reject path traversal attempts before character-level sanitization.
        // ".." and directory separators could escape the keystore directory even after
        // invalid-filename-character replacement (none of these are in GetInvalidFileNameChars
        // on all platforms — '.' and '/' are valid on some filesystems).
        if (value.Contains("..", StringComparison.Ordinal) ||
            value.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            value.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid key ID: path traversal characters are not permitted", nameof(value));
        }

        var invalid = Path.GetInvalidFileNameChars();
        return string.Create(value.Length, (value, invalid), static (span, state) =>
        {
            int i = 0;
            foreach (var ch in state.value)
                span[i++] = state.invalid.AsSpan().IndexOf(ch) >= 0 ? '_' : ch;
        });
    }

    /// <summary>Per-keyType entropy so blobs for different purposes are not interchangeable
    /// under the same Windows user account.</summary>
    /// <remarks>E043: entropy now incorporates the machine-specific MachineGuid from the Windows
    /// registry instead of a static constant. MIGRATION: existing blobs encrypted with the legacy
    /// static entropy remain readable — <see cref="Retrieve"/> tries the new entropy first and
    /// falls back to the legacy static entropy on failure. New writes always use machine-specific
    /// entropy, so the legacy path is self-retiring as secrets are rewritten.</remarks>
    private static byte[] GetEntropy(string keyType)
    {
        // E043: derive entropy from machine-specific secret (MachineGuid) instead of static constant.
        var machineId = GetMachineGuid();
        var prefix = $"com.milkandgin.syxx:{machineId}";
        var prefixBytes = Encoding.UTF8.GetBytes(prefix);
        var result = new byte[prefixBytes.Length + 1 + keyType.Length];
        prefixBytes.CopyTo(result, 0);
        result[prefixBytes.Length] = (byte)':';
        Encoding.UTF8.GetBytes(keyType, result.AsSpan(prefixBytes.Length + 1));
        return result;
    }

    /// <summary>Legacy static entropy — used ONLY as a decryption fallback for blobs written
    /// before E043. Never used for new writes.</summary>
    private static byte[] GetLegacyStaticEntropy(string keyType)
    {
        var appEntropy = "com.milkandgin.syxx"u8;
        var result = new byte[appEntropy.Length + 1 + keyType.Length];
        appEntropy.CopyTo(result);
        result[appEntropy.Length] = (byte)':';
        Encoding.UTF8.GetBytes(keyType, result.AsSpan(appEntropy.Length + 1));
        return result;
    }

    /// <summary>Reads the Windows MachineGuid from HKLM\SOFTWARE\Microsoft\Cryptography.
    /// Falls back to Environment.MachineName if the registry is inaccessible (restricted
    /// environments, containers). Cached for the process lifetime.</summary>
    private static string? _machineGuidCache;
    private static string GetMachineGuid()
    {
        if (_machineGuidCache is not null) return _machineGuidCache;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(guid))
                return _machineGuidCache = guid;
        }
        catch
        {
            // Registry inaccessible (restricted environment, container, or security policy).
            // Fall through to hostname fallback.
        }
        // Fallback: Environment.MachineName — weaker than MachineGuid but always available.
        // This means entropy differs across installations on the same hardware only if the
        // hostname changes, which is rare on Windows desktops.
        return _machineGuidCache = Environment.MachineName;
    }

    // ── DPAPI encrypt / decrypt ───────────────────────────────────────────────────

    private static DATA_BLOB ToBlob(byte[] bytes)
    {
        var blob = new DATA_BLOB { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
        return blob;
    }

    private static byte[] FromBlob(DATA_BLOB blob)
    {
        var bytes = new byte[blob.cbData];
        if (blob.cbData > 0)
            Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
        LocalFree(blob.pbData);
        return bytes;
    }

    internal static byte[] Protect(byte[] plaintext, byte[] entropy)
    {
        var inBlob = ToBlob(plaintext);
        var entropyBlob = ToBlob(entropy);
        try
        {
            if (!CryptProtectData(ref inBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
            {
                throw new InvalidOperationException(
                    $"CryptProtectData failed: {Marshal.GetLastWin32Error()}");
            }
            return FromBlob(outBlob);
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
            Marshal.FreeHGlobal(entropyBlob.pbData);
        }
    }

    internal static byte[] Unprotect(byte[] ciphertext, byte[] entropy)
    {
        var inBlob = ToBlob(ciphertext);
        var entropyBlob = ToBlob(entropy);
        try
        {
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero,
                    IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
            {
                throw new InvalidOperationException(
                    $"CryptUnprotectData failed: {Marshal.GetLastWin32Error()}");
            }
            return FromBlob(outBlob);
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pbData);
            Marshal.FreeHGlobal(entropyBlob.pbData);
        }
    }

    // ── Legacy backward-compat decryption ─────────────────────────────────────────
    //
    // Before ISecretStore existed, LicenseManager and WitnessKeyStore embedded DPAPI-
    // encrypted blobs directly in config/witness.key files using fixed app-level entropy.
    // These ReadLegacy/DecryptLegacy methods are called during upgrade to read old-format
    // data on Windows. They are called ONLY when OperatingSystem.IsWindows() is true,
    // so the DllImport is resolved only on Windows.

    /// <summary>Legacy entropy used by LicenseManager for old-format license blobs.</summary>
    internal static readonly byte[] LegacyLicenseEntropy = "com.milkandgin.syxx"u8.ToArray();

    /// <summary>Legacy entropy used by WitnessKeyStore for old-format witness.blobs.</summary>
    internal static readonly byte[] LegacyWitnessEntropy = "com.milkandgin.syxx/witness-key"u8.ToArray();

    /// <summary>Decrypt a legacy-format DPAPI blob with the given entropy.
    /// Returns null on any failure.</summary>
    internal static string? DecryptLegacy(byte[] ciphertext, byte[] entropy)
    {
        try
        {
            var plaintext = Unprotect(ciphertext, entropy);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return null;
        }
    }
}
