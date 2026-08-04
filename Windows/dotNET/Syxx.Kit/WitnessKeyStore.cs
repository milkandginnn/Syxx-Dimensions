using System.Runtime.InteropServices;
using System.Text;

namespace Syxx.Kit
{
    /// <summary>
    /// Manages the Ed25519 witness signing key, the .NET twin of macOS
    /// <c>WitnessKeyManager</c>.
    ///
    /// SECRET STORAGE (via <see cref="ISecretStore"/>):
    ///   On Windows: uses <see cref="DpapiSecretStore"/> (Win32 DPAPI via crypt32.dll).
    ///   On Linux:   uses <see cref="LinuxSecretStore"/> (libsecret P/Invoke + AES-256-GCM
    ///               encrypted file fallback at ~/.syxx/keystore/).
    ///   Other:      uses <see cref="LinuxSecretStore"/> file-only fallback.
    ///
    /// The store is injected via <see cref="SetSecretStore"/> or lazily initialised from
    /// <see cref="PlatformSecretStore.Create"/>. The <c>witness.key</c> file contains a
    /// reference token (<c>__syxx_ls:&lt;keyId&gt;</c>) on all platforms. Existing Windows
    /// installations with legacy DPAPI-binary files are read transparently via backward compat.
    ///
    /// The plaintext key is never stored in a field, property, or view-model — only a bool
    /// <c>HasKey</c> is exposed externally. A transient decrypted copy exists only during
    /// signing/verification calls (WitnessSigner.Sign/WitnessSigner.Verify).
    /// </summary>
    public static class WitnessKeyStore
    {
        // Reference token prefix used on ALL platforms via ISecretStore abstraction.
        private const string RefTokenPrefix = "__syxx_ls:";

        /// <summary>The injected secret store. If null, lazily created via
        /// <see cref="PlatformSecretStore.Create"/> on first use.</summary>
        private static ISecretStore? _store;

        /// <summary>
        /// Injects the secret store implementation (for DI or testing).
        /// Call once at application startup, before any key operations.
        /// </summary>
        public static void SetSecretStore(ISecretStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        private static ISecretStore SecretStore
        {
            get
            {
                if (_store is null)
                {
                    _store = PlatformSecretStore.Create();
                }
                return _store;
            }
        }

        private static string KeyPath
        {
            get
            {
                // D109: use LocalApplicationData instead of Roaming ApplicationData.
                // The DPAPI-encrypted signing key should not be copied to a profile server
                // at logoff on domain-joined machines with roaming profiles.
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "Syxx", "witness.key");
            }
        }

        /// <summary>Whether a witness signing key is present on disk.</summary>
        public static bool HasKey
        {
            get
            {
                try
                {
                    if (!File.Exists(KeyPath)) return false;
                    var fi = new FileInfo(KeyPath);
                    return fi.Length > 0;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Loads and decrypts the witness signing key.
        /// Returns the base64-encoded Ed25519 private key, or <c>null</c> if none is stored or
        /// the blob cannot be decrypted / retrieved (wrong user, corruption, missing store).
        ///
        /// Handles two on-disk formats transparently:
        ///   1. Reference token (<c>__syxx_ls:&lt;keyId&gt;</c>)  — written by the current code.
        ///   2. Raw DPAPI-encrypted binary blob                     — legacy Windows format.
        /// </summary>
        public static string? LoadKey()
        {
            try
            {
                if (!File.Exists(KeyPath)) return null;
                byte[] fileBytes = File.ReadAllBytes(KeyPath);
                if (fileBytes.Length == 0) return null;

                // Try reference-token format (new code, all platforms).
                // The token is ASCII/UTF8 text starting with underscore, which is never
                // the first byte of a DPAPI blob (DPAPI output is opaque binary).
                if (fileBytes[0] == (byte)'_')
                {
                    var stored = Encoding.UTF8.GetString(fileBytes);
                    if (stored.StartsWith(RefTokenPrefix, StringComparison.Ordinal))
                    {
                        var keyId = stored.Substring(RefTokenPrefix.Length);
                        return SecretStore.Retrieve("witness", keyId);
                    }
                    // Not a valid reference token — treat as absent.
                    return null;
                }

                // Legacy Windows DPAPI binary blob (backward compat).
                if (OperatingSystem.IsWindows())
                {
#pragma warning disable CA1416 // DpapiSecretStore.DecryptLegacy is [SupportedOSPlatform("windows")]; guarded by OperatingSystem.IsWindows()
                    return DpapiSecretStore.DecryptLegacy(fileBytes, DpapiSecretStore.LegacyWitnessEntropy);
#pragma warning restore CA1416
                }

                return null;
            }
            catch
            {
                // Undecryptable / unreadable => treat as no usable key (fail closed).
                return null;
            }
        }

        /// <summary>
        /// Encrypts and persists the base64-encoded Ed25519 private key via the
        /// platform secret store. The <c>witness.key</c> file contains a reference token
        /// pointing to the actual secret in the store.
        ///
        /// The key is validated BEFORE anything is written. Storing an unvalidated string defers the
        /// failure all the way to signing time, where it surfaces as an opaque error long after the
        /// user could connect it to what they typed. The check is
        /// <see cref="WitnessSigner.ValidatePrivateKey"/>, the same one the signer applies, so
        /// save-time and sign-time acceptance cannot drift apart. Parity with the macOS twin
        /// <c>WitnessKeyManager.saveKey</c>.
        /// </summary>
        /// <exception cref="WitnessSignerException">
        /// <paramref name="base64Key"/> is not usable Ed25519 signing key material. Nothing is
        /// written to the secret store or to disk in that case.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown if the store write fails.</exception>
        public static void SaveKey(string base64Key)
        {
            WitnessSigner.ValidatePrivateKey(base64Key);

            string dir = Path.GetDirectoryName(KeyPath)!;
            Directory.CreateDirectory(dir);

            // Store the actual secret in the platform secret store (DPAPI on Windows,
            // libsecret/AES-GCM on Linux), write a reference token to the file.
            var keyId = Guid.NewGuid().ToString("N");
            SecretStore.Store("witness", keyId, base64Key);

            // D109: atomic write — write to a .tmp file then move, so a crash mid-write
            // cannot truncate the existing good key file.
            string tmpPath = KeyPath + ".tmp";
            File.WriteAllText(tmpPath, RefTokenPrefix + keyId);
            File.Move(tmpPath, KeyPath, overwrite: true);

            // D109: restrict the key file ACL to the current user (matches CLI hardening
            // in WitnessCommand.RestrictKeyFileToCurrentUser).
            RestrictKeyFileToCurrentUser(KeyPath);
        }

        /// D109: restricts the file's ACL to the current user only (Windows), or mode 0600 (Linux).
        /// Moved from WitnessCommand so both the CLI and GUI key stores are equally hardened.
        private static void RestrictKeyFileToCurrentUser(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var security = new System.Security.AccessControl.FileSecurity();
                    security.SetAccessRuleProtection(true, false);
                    var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                    if (currentUser != null)
                    {
                        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                            currentUser,
                            System.Security.AccessControl.FileSystemRights.FullControl,
                            System.Security.AccessControl.InheritanceFlags.None,
                            System.Security.AccessControl.PropagationFlags.None,
                            System.Security.AccessControl.AccessControlType.Allow));
                    }
                    new FileInfo(path).SetAccessControl(security);
                }
                else if (OperatingSystem.IsLinux())
                {
                    // chmod 0600
                    File.SetUnixFileMode(path,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            catch
            {
                // Best-effort: the key file still has its DPAPI/secret-store protection;
                // the ACL is defense-in-depth, not the sole barrier.
            }
        }

        /// <summary>
        /// Deletes the stored witness signing key, if present.
        /// Also removes the secret from the platform store (DPAPI keystore file,
        /// libsecret keyring, or AES-GCM file store).
        /// </summary>
        public static void DeleteKey()
        {
            try
            {
                if (!File.Exists(KeyPath)) return;

                // Read the reference token and clear the secret from the store.
                try
                {
                    var fileContent = File.ReadAllText(KeyPath);
                    if (fileContent.StartsWith(RefTokenPrefix, StringComparison.Ordinal))
                    {
                        var keyId = fileContent.Substring(RefTokenPrefix.Length);
                        SecretStore.Delete("witness", keyId);
                    }
                }
                catch { /* best-effort */ }

                File.Delete(KeyPath);
            }
            catch { /* best-effort */ }
        }
    }
}
