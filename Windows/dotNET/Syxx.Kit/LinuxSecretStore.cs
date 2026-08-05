using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Syxx.Kit;

/// <summary>
/// Linux <see cref="ISecretStore"/> implementation. Ties libsecret (GNOME Keyring / KDE Wallet)
/// as the primary credential backend via P/Invoke, falling back to an AES-256-GCM encrypted
/// file store at <c>~/.syxx/keystore/</c> when libsecret is unavailable.
///
/// Backends (in order of preference):
///   1. libsecret via P/Invoke  — GNOME Keyring / KDE Wallet integration (no process spawn)
///   2. secret-tool CLI         — fallback when the native library is absent but the CLI is
///   3. AES-256-GCM file store  — always-available fallback with PBKDF2-derived key
///
/// All backends are guarded by runtime platform checks in the factory, so this type compiles
/// safely on any platform but is only the default on Linux (and macOS fallback).
///
/// Thread-safety: all backends synchronise internally. This class does not add locking.
/// </summary>
public sealed class LinuxSecretStore : ISecretStore
{
    // ── Data directory ────────────────────────────────────────────────────────────

    private static readonly string DefaultStoreDir;

    static LinuxSecretStore()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                   ?? Environment.GetEnvironmentVariable("HOME")
                   ?? "/tmp";
        DefaultStoreDir = Path.Combine(home, ".syxx", "keystore");
    }

    /// <summary>Test-only seam for the file-fallback directory, same shape and same reason as
    /// <c>WitnessKeyStore.SetKeyPathForTesting</c>: without it, the only way to prove the existence
    /// probe genuinely enumerates the file store is to write a secret into the developer's real
    /// <c>~/.syxx/keystore</c>, and a killed test process then leaves an orphan in a real user's
    /// store. Pass <c>null</c> to restore. <c>internal</c>, so no product code can relocate a user's
    /// secrets.</summary>
    private static string? _storeDirOverride;

    internal static void SetFallbackDirectoryForTesting(string? path) => _storeDirOverride = path;

    private static string StoreDir => _storeDirOverride ?? DefaultStoreDir;

    /// <summary>The directory used by the file-fallback store.</summary>
    public static string FallbackDirectory => StoreDir;

    // ── ISecretStore ──────────────────────────────────────────────────────────────

    public void Store(string keyType, string keyId, string secret)
    {
        // 1. Try libsecret P/Invoke
        if (LibSecretNative.IsAvailable)
        {
            if (LibSecretNative.Store(keyType, keyId, secret))
                return;
        }

        // 2. Try secret-tool CLI
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && SecretTool.IsAvailable())
        {
            SecretTool.Store(keyType, keyId, secret);
            return;
        }

        // 3. Fallback: AES-256-GCM encrypted file
        StoreWithFile(keyType, keyId, secret);
    }

    public string? Retrieve(string keyType, string keyId)
    {
        // 1. Try libsecret P/Invoke
        if (LibSecretNative.IsAvailable)
        {
            var result = LibSecretNative.Retrieve(keyType, keyId);
            if (result is not null)
                return result;
        }

        // 2. Try secret-tool CLI
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && SecretTool.IsAvailable())
        {
            var result = SecretTool.Retrieve(keyType, keyId);
            if (result is not null)
                return result;
        }

        // 3. Try file fallback
        return RetrieveFromFile(keyType, keyId);
    }

    public void Delete(string keyType, string keyId)
    {
        if (LibSecretNative.IsAvailable)
            LibSecretNative.Delete(keyType, keyId);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SecretTool.Delete(keyType, keyId);

        DeleteFromFile(keyType, keyId);
    }

    /// <summary>
    /// G-02: is any secret of <paramref name="keyType"/> held, without knowing a keyId? Each backend
    /// answers for itself and the answers are combined conservatively — Present beats Unknown beats
    /// Absent — so <see cref="SecretExistence.Absent"/> is returned only when every backend that could
    /// be holding a secret positively said it holds none.
    ///
    /// Per backend:
    ///   • File store — definitive. One file per (keyType, keyId) in one directory, so the keyType
    ///     prefix enumerates. This is where a secret lands whenever the keyring is locked or missing,
    ///     which is the common Linux case and the one G-02 is about.
    ///   • secret-tool — definitive for what secret-tool itself wrote, since it files secrets under the
    ///     <c>syxx-key-type</c> attribute, which <c>search</c> can query alone. But only while
    ///     secret-tool is still installed AND the Secret Service answers: a removed
    ///     <c>libsecret-tools</c> package, or a locked/absent keyring, leaves the question unanswerable
    ///     rather than answered "no". That is <see cref="SecretExistence.Unknown"/>, and it is the one
    ///     reachable case where a live keyring secret can go unseen. See the caller's residual note.
    ///   • libsecret P/Invoke — CANNOT answer, and separately cannot STORE. The bound entry point is
    ///     <c>secret_password_lookup_sync</c>, which needs an exact attribute value pair, and the only
    ///     value identifying a single secret is the keyId the lost token file was holding; no
    ///     search/enumerate entry point is bound. Beyond that: <see cref="LibSecretNative.Store"/>
    ///     passes the keyType VALUE ("witness") in the attribute-NAME varargs slot, while the schema
    ///     at <c>TryLoad</c> declares only <c>syxx-key-type</c>/<c>syxx-key-id</c>. libsecret's
    ///     <c>secret_attributes_buildv</c> answers an undeclared attribute name with
    ///     <c>g_critical</c> + NULL, and <c>secret_password_store_sync</c> then does
    ///     "<c>if (!attributes) return FALSE;</c>" WITHOUT setting a GError — so every write through
    ///     this backend fails silently and <see cref="Store"/> falls through to secret-tool or the file
    ///     store. Nothing has ever been persisted here. The Unknown downgrade below is therefore a
    ///     guard against a REPAIRED backend, not against a live hazard; if that repair happens, the
    ///     caller's fail-open arm becomes load-bearing and must be revisited in the same change.
    /// </summary>
    public SecretExistence Exists(string keyType)
    {
        var answer = ExistsInFileStore(keyType);
        if (answer == SecretExistence.Present) return SecretExistence.Present;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && SecretTool.IsAvailable())
        {
            var toolAnswer = SecretTool.Exists(keyType);
            if (toolAnswer == SecretExistence.Present) return SecretExistence.Present;
            if (toolAnswer == SecretExistence.Unknown) answer = SecretExistence.Unknown;
        }

        // Deliberately last and deliberately not gated on the two above: a secret written through the
        // P/Invoke backend is invisible to both of them, so its mere availability downgrades a
        // file-store "Absent" to "Unknown". Overstating this as Absent is the whole G-02 defect.
        if (LibSecretNative.IsAvailable && answer == SecretExistence.Absent)
        {
            return SecretExistence.Unknown;
        }

        return answer;
    }

    /// <summary>Enumerates the AES-GCM file store for any <c>{keyType}_*</c> entry. Read-only: unlike
    /// <see cref="GetFilePath"/> it must not create the directory, since a presence probe that
    /// materialises state is not a probe.</summary>
    private static SecretExistence ExistsInFileStore(string keyType)
    {
        try
        {
            if (!Directory.Exists(StoreDir)) return SecretExistence.Absent;
            return Directory.EnumerateFiles(StoreDir, Sanitize(keyType) + "_*").Any()
                ? SecretExistence.Present
                : SecretExistence.Absent;
        }
        catch
        {
            // Permission denied on $HOME, a failing mount: we could not look, so we do not claim.
            return SecretExistence.Unknown;
        }
    }

    // ── AES-256-GCM encrypted file store ──────────────────────────────────────────

    /// <summary>Derives a 256-bit AES-GCM key from machine-specific data.
    /// Stable across reboots; changes only on OS reinstall or hostname change.</summary>
    private static byte[] GetFileStoreKey()
    {
        var material = new StringBuilder();

        // /etc/machine-id — stable per installation
        try { material.Append(File.ReadAllText("/etc/machine-id").Trim()); } catch { }

        // /sys/class/dmi/id/product_uuid — hardware-bound, root-only in many distros
        try { material.Append(File.ReadAllText("/sys/class/dmi/id/product_uuid").Trim()); } catch { }

        // Hostname — volatile but provides uniqueness in containers
        material.Append(Environment.MachineName);

        using var sha256 = SHA256.Create();
        var salt = sha256.ComputeHash("Syxx-AES-256-GCM-Key-v1"u8.ToArray());

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(material.ToString()),
            salt,
            iterations: 350000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    /// <summary>File format: [nonce:12][tag:16][ciphertext:N]</summary>
    internal static readonly int NonceSize = 12;
    internal static readonly int TagSize = 16;

    private static string GetFilePath(string keyType, string keyId)
    {
        if (!Directory.Exists(StoreDir))
            Directory.CreateDirectory(StoreDir);

        return Path.Combine(StoreDir, $"{Sanitize(keyType)}_{Sanitize(keyId)}");
    }

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

    private void StoreWithFile(string keyType, string keyId, string secret)
    {
        try
        {
            var path = GetFilePath(keyType, keyId);
            var key = GetFileStoreKey();
            var plaintext = Encoding.UTF8.GetBytes(secret);

            using var aes = new AesGcm(key, TagSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            var associatedData = Encoding.UTF8.GetBytes("syxx-keystore-v1:" + keyType);

            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

            // Write: [nonce][tag][ciphertext]
            using var fileStream = File.Create(path);
            fileStream.Write(nonce, 0, nonce.Length);
            fileStream.Write(tag, 0, tag.Length);
            fileStream.Write(ciphertext, 0, ciphertext.Length);

            // chmod 0600 on Unix (owner-read/write only)
            if (OperatingSystem.IsLinux())
                SetUnixPermissions(path);
        }
        catch
        {
            // best-effort; caller should verify via Retrieve
        }
    }

    private string? RetrieveFromFile(string keyType, string keyId)
    {
        try
        {
            var path = GetFilePath(keyType, keyId);
            if (!File.Exists(path)) return null;

            var fileBytes = File.ReadAllBytes(path);
            if (fileBytes.Length < NonceSize + TagSize) return null;

            var key = GetFileStoreKey();
            using var aes = new AesGcm(key, TagSize);

            var nonce = fileBytes.AsSpan(0, NonceSize);
            var tag = fileBytes.AsSpan(NonceSize, TagSize);
            var ciphertext = fileBytes.AsSpan(NonceSize + TagSize);
            var plaintext = new byte[ciphertext.Length];
            var associatedData = Encoding.UTF8.GetBytes("syxx-keystore-v1:" + keyType);

            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteFromFile(string keyType, string keyId)
    {
        try
        {
            var path = GetFilePath(keyType, keyId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }

    private static void SetUnixPermissions(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "0600", path },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(1000);
        }
        catch
        {
            // best-effort
        }
    }

    // ── libsecret P/Invoke backend (primary) ──────────────────────────────────────

    /// <summary>Thin P/Invoke wrapper around libsecret functions loaded dynamically
    /// via NativeLibrary to avoid DllNotFoundException on systems without libsecret.</summary>
    internal static class LibSecretNative
    {
        private static IntPtr _handle;
        private static bool _available;
        private static bool _checked;

        private static IntPtr _schemaPtr;

        // Delegate types for libsecret functions
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool SecretPasswordStoreSync(
            IntPtr schema, IntPtr collection, IntPtr label, IntPtr password,
            IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr attrK2, IntPtr attrV2, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretPasswordLookupSync(
            IntPtr schema, IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr attrK2, IntPtr attrV2, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool SecretPasswordClearSync(
            IntPtr schema, IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr attrK2, IntPtr attrV2, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GErrorFree(IntPtr error);

        private static SecretPasswordStoreSync? _storeFunc;
        private static SecretPasswordLookupSync? _lookupFunc;
        private static SecretPasswordClearSync? _clearFunc;
        private static GErrorFree? _gErrorFree;

        internal static bool IsAvailable
        {
            get
            {
                if (!_checked) TryLoad();
                return _available;
            }
        }

        private static void TryLoad()
        {
            _checked = true;

            try
            {
                _handle = NativeLibrary.Load("libsecret-1.so.0");

                // Resolve function pointers
                _storeFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordStoreSync>(
                    NativeLibrary.GetExport(_handle, "secret_password_store_sync"));
                _lookupFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordLookupSync>(
                    NativeLibrary.GetExport(_handle, "secret_password_lookup_sync"));
                _clearFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordClearSync>(
                    NativeLibrary.GetExport(_handle, "secret_password_clear_sync"));

                // g_error_free from glib (libsecret depends on glib)
                try
                {
                    _gErrorFree = Marshal.GetDelegateForFunctionPointer<GErrorFree>(
                        NativeLibrary.GetExport(_handle, "g_error_free"));
                }
                catch
                {
                    // glib may be in a separate library; try libglib-2.0.so.0
                    var glibHandle = NativeLibrary.Load("glib-2.0.so.0");
                    _gErrorFree = Marshal.GetDelegateForFunctionPointer<GErrorFree>(
                        NativeLibrary.GetExport(glibHandle, "g_error_free"));
                }

                // Build the SecretSchema as a pinned memory block.
                // Schema layout (see libsecret/secret-schema.h):
                //   [name_ptr:8][flags:4][padding:4]
                //   [attr0_name:8][attr0_type:4][padding:4]
                //   [attr1_name:8][attr1_type:4][padding:4]
                //   [attr2_name:8][attr2_type:4][padding:4]
                //   [attr3_name:8][attr3_type:4][padding:4]  -- sentinel (name=null)
                var schemaName = Marshal.StringToCoTaskMemUTF8("com.milkandgin.syxx");
                var attr0Name = Marshal.StringToCoTaskMemUTF8("syxx-key-type");
                var attr1Name = Marshal.StringToCoTaskMemUTF8("syxx-key-id");
                const int SecretSchemaAttributeString = 0;

                var schemaBytes = new byte[64]; // 5 entries * 12 bytes + alignment padding
                var schemaHandle = GCHandle.Alloc(schemaBytes, GCHandleType.Pinned);
                _schemaPtr = schemaHandle.AddrOfPinnedObject();

                // Write schema_name pointer
                Marshal.WriteIntPtr(_schemaPtr, 0, schemaName);
                // flags = 0 (SECRET_SCHEMA_NONE)
                Marshal.WriteInt32(_schemaPtr, 8, 0);
                // attr0: name, type
                Marshal.WriteIntPtr(_schemaPtr, 16, attr0Name);
                Marshal.WriteInt32(_schemaPtr, 24, SecretSchemaAttributeString);
                // attr1: name, type
                Marshal.WriteIntPtr(_schemaPtr, 32, attr1Name);
                Marshal.WriteInt32(_schemaPtr, 40, SecretSchemaAttributeString);
                // attr2-4: sentinel (null name, zero type) -- already zeroed

                _available = true;
            }
            catch
            {
                // libsecret not available; caller falls back to secret-tool or file store
                _available = false;
            }
        }

        internal static bool Store(string keyType, string keyId, string secret)
        {
            if (!IsAvailable || _storeFunc is null) return false;

            try
            {
                var labelPtr = Marshal.StringToCoTaskMemUTF8($"Syxx {keyType}");
                var passwordPtr = Marshal.StringToCoTaskMemUTF8(secret);
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                var keyIdPtr = Marshal.StringToCoTaskMemUTF8(keyId);

                try
                {
                    bool result = _storeFunc(
                        _schemaPtr,
                        IntPtr.Zero,   // collection: NULL = default
                        labelPtr,
                        passwordPtr,
                        IntPtr.Zero,   // cancellable: NULL (synchronous)
                        out var error,
                        keyTypePtr, keyIdPtr,
                        // We only have 2 attributes; remaining are terminator
                        IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero);  // varargs terminator

                    if (!result && error != IntPtr.Zero)
                        _gErrorFree?.Invoke(error);

                    return result;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(labelPtr);
                    Marshal.FreeCoTaskMem(passwordPtr);
                    Marshal.FreeCoTaskMem(keyTypePtr);
                    Marshal.FreeCoTaskMem(keyIdPtr);
                }
            }
            catch
            {
                return false;
            }
        }

        internal static string? Retrieve(string keyType, string keyId)
        {
            if (!IsAvailable || _lookupFunc is null) return null;

            try
            {
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                var keyIdPtr = Marshal.StringToCoTaskMemUTF8(keyId);

                try
                {
                    IntPtr resultPtr = _lookupFunc(
                        _schemaPtr,
                        IntPtr.Zero,   // cancellable
                        out var error,
                        keyTypePtr, keyIdPtr,
                        IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero);  // terminator

                    if (resultPtr != IntPtr.Zero)
                    {
                        var result = Marshal.PtrToStringUTF8(resultPtr);
                        // libsecret expects caller to free the returned string with g_free
                        try { _gErrorFree?.Invoke(resultPtr); } catch { }
                        return result;
                    }

                    if (error != IntPtr.Zero)
                        _gErrorFree?.Invoke(error);

                    return null;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypePtr);
                    Marshal.FreeCoTaskMem(keyIdPtr);
                }
            }
            catch
            {
                return null;
            }
        }

        internal static void Delete(string keyType, string keyId)
        {
            if (!IsAvailable || _clearFunc is null) return;

            try
            {
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                var keyIdPtr = Marshal.StringToCoTaskMemUTF8(keyId);

                try
                {
                    _clearFunc(
                        _schemaPtr,
                        IntPtr.Zero,   // cancellable
                        out var error,
                        keyTypePtr, keyIdPtr,
                        IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero);  // terminator

                    if (error != IntPtr.Zero)
                        _gErrorFree?.Invoke(error);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypePtr);
                    Marshal.FreeCoTaskMem(keyIdPtr);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    // ── secret-tool CLI fallback ──────────────────────────────────────────────────

    /// <summary>Shells out to <c>secret-tool</c> (from <c>libsecret-tools</c> package)
    /// when the native library P/Invoke fails or is unavailable.</summary>
    internal static class SecretTool
    {
        private static bool? _available;

        /// <summary>Returns true when secret-tool is on $PATH and responds to --help.</summary>
        public static bool IsAvailable()
        {
            if (_available.HasValue) return _available.Value;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    Arguments = "--help",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) { _available = false; return false; }
                proc.WaitForExit(2000);
                _available = proc.ExitCode == 0;
                return _available.Value;
            }
            catch
            {
                _available = false;
                return false;
            }
        }

        public static void Store(string keyType, string keyId, string secret)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList =
                    {
                        "store",
                        $"--label=Syxx {keyType}",
                        "syxx-key-type", keyType,
                        "syxx-key-id", keyId
                    },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return;
                proc.StandardInput.Write(secret);
                proc.StandardInput.Close();
                proc.WaitForExit(5000);
            }
            catch
            {
                // fall through to file store
            }
        }

        public static string? Retrieve(string keyType, string keyId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "lookup", "syxx-key-type", keyType, "syxx-key-id", keyId },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                    ? output.TrimEnd('\n', '\r')
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>G-02: asks whether the keyring holds any secret filed under
        /// <c>syxx-key-type = keyType</c>, which <c>secret-tool search</c> can answer without a keyId.
        /// Only an unambiguous "found nothing" is reported as <see cref="SecretExistence.Absent"/>;
        /// a non-zero exit we cannot attribute to an empty result, or a timeout, is
        /// <see cref="SecretExistence.Unknown"/>, because the D-Bus service being unreachable is not
        /// the same as the keyring being empty.</summary>
        public static SecretExistence Exists(string keyType)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "search", "--all", "syxx-key-type", keyType },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return SecretExistence.Unknown;

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(5000)) return SecretExistence.Unknown;

                if (proc.ExitCode == 0)
                {
                    return string.IsNullOrWhiteSpace(stdout)
                        ? SecretExistence.Absent
                        : SecretExistence.Present;
                }

                // secret-tool exits non-zero both for "no such secret" and for real failures
                // (no session bus, service refused). Only the former is proof of absence.
                return stderr.Contains("No such secret", StringComparison.OrdinalIgnoreCase)
                    ? SecretExistence.Absent
                    : SecretExistence.Unknown;
            }
            catch
            {
                return SecretExistence.Unknown;
            }
        }

        public static void Delete(string keyType, string keyId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "clear", "syxx-key-type", keyType, "syxx-key-id", keyId },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
