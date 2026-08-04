using System.Runtime.InteropServices;
using System.Text;

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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                      ?? Environment.GetEnvironmentVariable("APPDATA")
                      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming");
        StoreDir = Path.Combine(appData, "Syxx", "keystore");
    }

    // ── ISecretStore implementation ───────────────────────────────────────────────

    public void Store(string keyType, string keyId, string secret)
    {
        var path = GetStorePath(keyType, keyId);
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

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

            var entropy = GetEntropy(keyType);
            var plaintext = Unprotect(ciphertext, entropy);
            return Encoding.UTF8.GetString(plaintext);
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

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static string GetStorePath(string keyType, string keyId)
        => Path.Combine(StoreDir, $"{Sanitize(keyType)}_{Sanitize(keyId)}");

    private static string Sanitize(string value)
    {
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
    private static byte[] GetEntropy(string keyType)
    {
        // Combine app-level entropy with key-type discriminator
        var appEntropy = "com.milkandgin.syxx"u8;
        var result = new byte[appEntropy.Length + 1 + keyType.Length];
        appEntropy.CopyTo(result);
        result[appEntropy.Length] = (byte)':';
        Encoding.UTF8.GetBytes(keyType, result.AsSpan(appEntropy.Length + 1));
        return result;
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
