using System.Runtime.InteropServices;
using System.Text;

namespace Syxx.Kit
{
    /// <summary>
    /// RG-2 tri-state: what a load attempt actually established about the on-disk signing key.
    /// Distinguishes the three cases the old boolean <c>LoadKey() == null</c> collapsed into one:
    ///   <see cref="Present"/>    — a key exists and was decrypted/retrieved successfully.
    ///   <see cref="Absent"/>     — no key was ever established here. Requires proof from BOTH oracles:
    ///                               no (or an empty) token file, AND a secret store that positively
    ///                               reports it holds no witness secret (G-02).
    ///   <see cref="Undetermined"/> — a key DOES exist (a token or legacy blob proves it) but the
    ///                               secure store could not be reached to hand it back. This is NOT
    ///                               "no key"; a caller must NEVER mint-and-overwrite on it, or a
    ///                               transient keyring outage silently destroys the identity.
    /// </summary>
    public enum KeyPresence
    {
        Present,
        Absent,
        Undetermined
    }

    /// <summary>Result of <see cref="WitnessKeyStore.LoadKeyResult"/>: the tri-state presence plus
    /// the decrypted key (non-null only when <see cref="Presence"/> is <see cref="KeyPresence.Present"/>).</summary>
    public readonly record struct KeyLoadResult(KeyPresence Presence, string? Key);

    /// <summary>RG-2: thrown by callers when the witness signing key is <see cref="KeyPresence.Undetermined"/>
    /// — a key exists but the secure store is unreachable. Signals "do not overwrite the identity",
    /// as distinct from a generic key-access failure.</summary>
    public sealed class WitnessKeyStoreUnavailableException : Exception
    {
        public WitnessKeyStoreUnavailableException(string message) : base(message) { }
        public WitnessKeyStoreUnavailableException(string message, Exception inner) : base(message, inner) { }
    }

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

        /// <summary>The <see cref="ISecretStore"/> keyType every witness secret is filed under.
        /// The keyId beside it is a per-save GUID that lives only in the token file, which is why
        /// <see cref="ISecretStore.Exists"/> is asked about the keyType alone (G-02).</summary>
        private const string WitnessKeyType = "witness";

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

        /// <summary>G-05 test-only seam. When non-null, the token file is read and written here
        /// instead of under <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
        ///
        /// It exists because the presence switch this type feeds (Absent mints, Present reuses,
        /// Undetermined refuses) had zero coverage: no test could drive the three states without
        /// writing the developer's real <c>~/.local/share/Syxx/witness.key</c>, so the tests that
        /// claimed to cover it injected a stub past the code under test instead. <c>internal</c> plus
        /// <c>InternalsVisibleTo("Syxx.Tests")</c> keeps it out of every product assembly.</summary>
        private static string? _keyPathOverride;

        /// <summary>Redirects <see cref="KeyPath"/> for the duration of a test. Pass <c>null</c> to
        /// restore the real per-user location. Tests must restore in a finally/Dispose — the field is
        /// static, so a leak contaminates every later test in the assembly.</summary>
        internal static void SetKeyPathForTesting(string? path) => _keyPathOverride = path;

        private static string KeyPath => _keyPathOverride ?? DefaultKeyPath;

        private static string DefaultKeyPath
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

        /// <summary>Whether a witness signing key may be present on this machine.
        ///
        /// G-02: <c>false</c> is returned only on POSITIVE proof of absence — no token file, or an
        /// empty one. A token file we cannot stat or open (permission denied, failing mount, a path
        /// that is not a file) returns <c>true</c>, because a read error is not evidence the key is
        /// gone; the previous <c>catch { return false; }</c> converted every such error into "no key"
        /// (spine #2, missing data is not clean data). A caller that must tell "exists" apart from
        /// "reachable" uses <see cref="LoadKeyResult"/>, which is tri-state.</summary>
        public static bool HasKey
        {
            get
            {
                var probe = ProbeTokenFile(readContent: false);
                return probe.State switch
                {
                    TokenFileState.Readable => true,
                    // Unknowable is not "no key" — refuse to claim absence we have not established.
                    TokenFileState.Unreadable => true,
                    _ => false,   // Missing / Empty: positively no established key here
                };
            }
        }

        /// <summary>What a guarded open of the token file established. <see cref="Missing"/> and
        /// <see cref="Empty"/> are positive proof no key was established at this path;
        /// <see cref="Unreadable"/> is the state <c>File.Exists</c> cannot express, because it
        /// returns false on permission denial and IO failure exactly as it does for a missing
        /// file (G-02a).</summary>
        private enum TokenFileState { Missing, Empty, Readable, Unreadable }

        private readonly record struct TokenFileProbe(
            TokenFileState State, byte[]? Bytes, Exception? Error);

        /// <summary>Opens the token file under a guard that distinguishes "not there" from "could not
        /// be looked at". <paramref name="readContent"/> false stats only, for <see cref="HasKey"/>.
        /// Never throws.</summary>
        private static TokenFileProbe ProbeTokenFile(bool readContent)
        {
            try
            {
                using var stream = new FileStream(
                    KeyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = stream.Length;
                if (length == 0)
                {
                    return new TokenFileProbe(TokenFileState.Empty, null, null);
                }
                if (!readContent)
                {
                    return new TokenFileProbe(TokenFileState.Readable, null, null);
                }
                var bytes = new byte[length];
                stream.ReadExactly(bytes);
                return new TokenFileProbe(TokenFileState.Readable, bytes, null);
            }
            catch (FileNotFoundException)
            {
                return new TokenFileProbe(TokenFileState.Missing, null, null);
            }
            catch (DirectoryNotFoundException)
            {
                return new TokenFileProbe(TokenFileState.Missing, null, null);
            }
            catch (Exception ex)
            {
                // UnauthorizedAccessException (no traversal/read permission, or the path is a
                // directory), IOException (failing mount, locked file), PathTooLong, etc. The key may
                // well be sitting right there — we simply could not look.
                return new TokenFileProbe(TokenFileState.Unreadable, null, ex);
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
            // Preserves TODAY's behavior byte-for-byte: Present hands back the key, every other
            // tri-state (Absent / Undetermined) maps to null, exactly as the old single-return code
            // did. Windows/CLI/tests that call LoadKey() are unaffected — only the richer
            // LoadKeyResult() lets a caller tell "no key" apart from "store unreachable" (RG-2).
            var result = LoadKeyResult();
            return result.Presence == KeyPresence.Present ? result.Key : null;
        }

        /// <summary>
        /// RG-2 tri-state load. Returns whether a signing key is <see cref="KeyPresence.Present"/>
        /// (with the decrypted key), <see cref="KeyPresence.Absent"/> (none was ever established), or
        /// <see cref="KeyPresence.Undetermined"/> (a key exists but the secure store could not be
        /// reached to retrieve/decrypt it — the keyring is locked, or unavailable over SSH/headless).
        ///
        /// The distinction is load-bearing: a token file or a legacy DPAPI blob is itself PROOF that a
        /// key was established. When that proof is present but the store hands back nothing (null) or
        /// throws, the honest answer is Undetermined, NOT Absent — never overwrite an identity we
        /// merely failed to reach.
        ///
        /// Absent needs both oracles to agree, and both are guarded: the token file is opened under a
        /// guard that tells "not there" apart from "could not be looked at" (G-02a — <c>File.Exists</c>
        /// cannot, it returns false either way), and a missing token file is then put to the secret
        /// store, because the token and the secret resolve from independent roots and losing one does
        /// not mean losing the other (G-02, see
        /// <see cref="AbsentUnlessStoreHoldsWitnessSecret"/>).
        ///
        /// Handles two on-disk formats transparently:
        ///   1. Reference token (<c>__syxx_ls:&lt;keyId&gt;</c>)  — written by the current code.
        ///   2. Raw DPAPI-encrypted binary blob                     — legacy Windows format.
        /// </summary>
        public static KeyLoadResult LoadKeyResult()
        {
            var probe = ProbeTokenFile(readContent: true);
            switch (probe.State)
            {
                case TokenFileState.Missing:
                    // No token file. That is proof about the FILE, not about the identity — the token
                    // and the secret sit under independent roots (G-02). Ask the store before
                    // concluding nothing was ever established here.
                    return AbsentUnlessStoreHoldsWitnessSecret("no witness.key token file");

                case TokenFileState.Empty:
                    // A zero-length file is an interrupted/blank write. Same reasoning as Missing: the
                    // secret may still be in the store, so it is not on its own proof of Absent.
                    return AbsentUnlessStoreHoldsWitnessSecret("witness.key is empty");

                case TokenFileState.Unreadable:
                    // G-02a: the previous code reached here only from File.ReadAllBytes, because
                    // File.Exists returns false rather than throwing on permission denial or a failing
                    // mount — so those errors were read as "no key" and the caller minted over the
                    // identity. A key path we cannot look at is Undetermined, as the doc always claimed.
                    return Undetermined("witness.key could not be read", probe.Error);
            }

            var fileBytes = probe.Bytes!;

            // Reference-token format (current code, all platforms). The token is ASCII/UTF8 text
            // starting with underscore, which is never the first byte of an opaque DPAPI blob.
            if (fileBytes[0] == (byte)'_')
            {
                var stored = Encoding.UTF8.GetString(fileBytes);
                if (stored.StartsWith(RefTokenPrefix, StringComparison.Ordinal))
                {
                    var keyId = stored.Substring(RefTokenPrefix.Length);
                    string? secret;
                    try
                    {
                        secret = SecretStore.Retrieve(WitnessKeyType, keyId);
                    }
                    catch (Exception ex)
                    {
                        // The token proves a key was established; the store threw reaching for it →
                        // unreachable store, not absent key. Undetermined.
                        return Undetermined("secret store threw retrieving the witness key", ex);
                    }

                    // RG-2 core: a valid token but a null store result means the key EXISTS (the token
                    // proves it) and the store is merely unreachable right now. This is Undetermined,
                    // never Absent — the old code returned null here and the caller minted over the
                    // identity. Never again.
                    return secret is null
                        ? Undetermined("secret store returned no value for an existing witness key token")
                        : new KeyLoadResult(KeyPresence.Present, secret);
                }

                // Non-empty file that starts with '_' but is not a valid reference token: content we
                // cannot parse. Never overwrite what we cannot understand → Undetermined.
                return Undetermined("witness.key holds an unrecognized non-empty token");
            }

            // Legacy Windows DPAPI binary blob (backward compat).
            if (OperatingSystem.IsWindows())
            {
#pragma warning disable CA1416 // DpapiSecretStore.DecryptLegacy is [SupportedOSPlatform("windows")]; guarded by OperatingSystem.IsWindows()
                var decrypted = DpapiSecretStore.DecryptLegacy(fileBytes, DpapiSecretStore.LegacyWitnessEntropy);
#pragma warning restore CA1416
                // A non-empty legacy blob that will not decrypt (returns null) is an existing-but-
                // unreadable key (wrong user, corruption, store unreachable) — Undetermined, not Absent.
                return decrypted is null
                    ? Undetermined("legacy DPAPI witness blob failed to decrypt")
                    : new KeyLoadResult(KeyPresence.Present, decrypted);
            }

            // Non-empty, non-token file on a non-Windows platform: unparseable existing content.
            // Never overwrite it — Undetermined.
            return Undetermined("witness.key holds unrecognized non-empty content");
        }

        /// <summary>
        /// C-10: the tri-state a destructive command asks BEFORE it acts — is it safe to mint a new
        /// signing identity here? <see cref="KeyPresence.Absent"/> is the only answer that says yes.
        ///
        /// <para>WHY THIS IS SEPARATE FROM <see cref="LoadKeyResult"/>. LoadKeyResult decrypts, and
        /// decrypting now costs a user passphrase (§8): the file store derives its key with Argon2id
        /// over the passphrase, so on a passphrase-protected install LoadKeyResult can only answer
        /// after a prompt. A guard that has to prompt is a guard people route around, and worse, a
        /// guard that returns "could not decrypt" would be indistinguishable from "no key" to a
        /// caller that only looked at the key being null. This probe asks the same two oracles — the
        /// token file and <see cref="ISecretStore.Exists"/> — and never asks for the secret itself,
        /// so it is answerable with the keyring locked and with no passphrase in hand.</para>
        ///
        /// <para>WHAT Present MEANS HERE. "A key was established at this path" — proved by a
        /// well-formed token file or by the store holding a witness secret. It does NOT claim the key
        /// is currently decryptable; a probe that had to decrypt to say so would be the thing this
        /// method exists to avoid. For the C-10 decision the distinction does not matter: both are
        /// "do not overwrite".</para>
        ///
        /// <para>The fail-open arm on an uninterrogable store is inherited from
        /// <see cref="AbsentUnlessStoreHoldsWitnessSecret"/> deliberately and unchanged — see that
        /// method's RESIDUAL note for why refusing to mint there would be the worse failure, and for
        /// the Warning breadcrumb that keeps it from being silent.</para>
        /// </summary>
        public static KeyPresence ProbeKeyPresence()
        {
            var probe = ProbeTokenFile(readContent: true);
            switch (probe.State)
            {
                case TokenFileState.Missing:
                    return AbsentUnlessStoreHoldsWitnessSecret("no witness.key token file").Presence;

                case TokenFileState.Empty:
                    return AbsentUnlessStoreHoldsWitnessSecret("witness.key is empty").Presence;

                case TokenFileState.Unreadable:
                    return Undetermined("witness.key could not be read", probe.Error).Presence;
            }

            var fileBytes = probe.Bytes!;

            // A well-formed reference token is itself proof that a key was established here. The
            // store is NOT consulted: whether it can hand the secret back right now is a question
            // about reachability, and answering it would require the passphrase this probe refuses
            // to need.
            if (fileBytes[0] == (byte)'_')
            {
                var stored = Encoding.UTF8.GetString(fileBytes);
                return stored.StartsWith(RefTokenPrefix, StringComparison.Ordinal)
                    ? KeyPresence.Present
                    : Undetermined("witness.key holds an unrecognized non-empty token").Presence;
            }

            // A legacy Windows DPAPI blob is equally proof of an established key. Its decryptability
            // is not asked, for the same reason.
            return KeyPresence.Present;
        }

        /// <summary>
        /// G-02(b): decides Absent vs Undetermined when the token file is gone or blank.
        ///
        /// A missing token file is NOT proof that no identity exists. The token and the secret resolve
        /// from independent roots — the token from <c>LocalApplicationData</c>
        /// (<c>$XDG_DATA_HOME</c>/<c>~/.local/share</c>), the secret from the OS keyring or
        /// <c>$HOME/.syxx/keystore</c>. Lose the token while the secret survives (XDG divergence
        /// between a desktop, snap/flatpak and plain-shell launch; an unreadable home in a headless
        /// session) and the old code reported Absent, the caller minted a second identity under a fresh
        /// GUID, and every previously sealed testimony began verifying against a different public key —
        /// with nothing on screen but a successful seal.
        ///
        /// So the store is asked directly, about the keyType alone, because the keyId lived only in the
        /// file we just failed to read. Only a store that POSITIVELY establishes it holds no witness
        /// secret yields Absent. A store that says it holds one yields Undetermined — an identity
        /// exists and its token is lost, which is a case for the owner to recover, never for us to
        /// paper over by minting a rival key.
        ///
        /// <para>RESIDUAL, stated rather than hidden. <see cref="SecretExistence.Unknown"/> and
        /// <see cref="SecretExistence.Unsupported"/> carry no evidence either way, so the token file
        /// stays the only oracle and the answer is Absent. The alternative — refusing to mint on
        /// Unknown — would leave any machine whose store cannot be interrogated unable to establish a
        /// first identity at all, with no in-app route out. So this arm fails OPEN, deliberately, and
        /// emits a Warning breadcrumb rather than taking it silently.
        ///
        /// The cost is precise and worth naming: on Linux, the libsecret search binding
        /// (<c>LinuxSecretStore.SearchForKeyType</c>) now closes the locked-keyring case — search
        /// matches on attributes without unlock, so a locked but populated keyring returns Present
        /// (→ Undetermined, not Absent). The "<c>libsecret-tools</c> removed after minting" case is
        /// also closed for P/Invoke-stored secrets, because the shared library that performed the
        /// write is by construction still present. What remains open is a Secret Service that is
        /// entirely absent at probe time (headless SSH with no session bus, no D-Bus daemon) —
        /// there, every backend returns Unknown and this arm mints. It does NOT apply to the
        /// AES-GCM file store, which needs no service to enumerate.</para>
        /// </summary>
        private static KeyLoadResult AbsentUnlessStoreHoldsWitnessSecret(string tokenFileReason)
        {
            SecretExistence existence;
            try
            {
                existence = SecretStore.Exists(WitnessKeyType);
            }
            catch (Exception ex)
            {
                // The probe itself failed. We know nothing about the store's contents and we have no
                // token file either — the one thing we must not do is call that "no key".
                return Undetermined("secret store threw probing for an existing witness secret", ex);
            }

            switch (existence)
            {
                case SecretExistence.Present:
                    return Undetermined(
                        "a witness secret exists in the secure store but its token file is gone ("
                        + tokenFileReason + ") — refusing to mint a second identity over it");

                case SecretExistence.Absent:
                    // Both oracles agree: nothing was ever established here. Safe to mint.
                    return new KeyLoadResult(KeyPresence.Absent, null);

                default:
                    SyxxDiagnostics.Breadcrumb(
                        category: "witness.key",
                        message: "Witness key treated as absent without positive proof — the secret "
                               + "store cannot be enumerated, so a lost token file is indistinguishable "
                               + "from a first run",
                        level: DiagnosticSeverity.Warning,
                        data: new Dictionary<string, string>
                        {
                            ["reason"] = tokenFileReason,
                            ["storeExistence"] = existence.ToString(),
                            ["store"] = SecretStore.GetType().Name,
                        });
                    return new KeyLoadResult(KeyPresence.Absent, null);
            }
        }

        /// <summary>Records the reason a load could not be resolved and returns an Undetermined
        /// result. Emitting a breadcrumb keeps the "we could not reach the key, we did not decide it
        /// was gone" decision observable rather than silent.</summary>
        private static KeyLoadResult Undetermined(string reason, Exception? ex = null)
        {
            var data = new Dictionary<string, string> { ["reason"] = reason };
            if (ex is not null) data["error"] = ex.GetType().Name;
            SyxxDiagnostics.Breadcrumb(
                category: "witness.key",
                message: "Witness key load undetermined (store unreachable) — identity preserved",
                level: DiagnosticSeverity.Warning,
                data: data);
            return new KeyLoadResult(KeyPresence.Undetermined, null);
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
        /// <exception cref="InvalidOperationException">
        /// The secret store did not hand the key back when read straight after the write (G-01).
        /// Neither the token file nor the previously stored identity is touched in that case.
        /// </exception>
        public static void SaveKey(string base64Key)
        {
            WitnessSigner.ValidatePrivateKey(base64Key);

            string dir = Path.GetDirectoryName(KeyPath)!;
            Directory.CreateDirectory(dir);

            // Store the actual secret in the platform secret store (DPAPI on Windows,
            // libsecret/AES-GCM on Linux), write a reference token to the file.
            var keyId = Guid.NewGuid().ToString("N");
            SecretStore.Store(WitnessKeyType, keyId, base64Key);

            // G-01: read the secret back BEFORE the token file is written. ISecretStore.Store is
            // documented best-effort and every Linux backend swallows its failures — LinuxSecretStore
            // catches everything at :172-175 with the comment "caller should verify via Retrieve", and
            // SecretTool.Store returns void, catching all at :543-546. Unverified, a locked keyring
            // produced a token file pointing at a secret that was never stored: the seal in progress
            // succeeded with a key nothing could ever read again, and every launch after it was
            // permanently Undetermined with no in-app recovery. Failing here instead leaves the
            // previous identity — or the clean absence of one — exactly as it was.
            // Honesty spine #2: a silent persist failure must not read as success.
            string? readBack;
            try
            {
                readBack = SecretStore.Retrieve(WitnessKeyType, keyId);
            }
            catch (Exception ex)
            {
                DiscardUnreferencedSecret(keyId);
                throw new InvalidOperationException(StoreWriteUnverifiedMessage, ex);
            }

            if (readBack is null || !string.Equals(readBack, base64Key, StringComparison.Ordinal))
            {
                DiscardUnreferencedSecret(keyId);
                throw new InvalidOperationException(StoreWriteUnverifiedMessage);
            }

            // D109: atomic write — write to a .tmp file then move, so a crash mid-write
            // cannot truncate the existing good key file.
            string tmpPath = KeyPath + ".tmp";
            File.WriteAllText(tmpPath, RefTokenPrefix + keyId);
            File.Move(tmpPath, KeyPath, overwrite: true);

            // D109: restrict the key file ACL to the current user (matches CLI hardening
            // in WitnessCommand.RestrictKeyFileToCurrentUser).
            RestrictKeyFileToCurrentUser(KeyPath);
        }

        /// <summary>G-01 user copy. Reached through <c>ErrorPresenter.Compose</c> at the GUI boundary,
        /// so it must read as a clause a person can act on, not as a store diagnostic.</summary>
        internal const string StoreWriteUnverifiedMessage =
            "the secure store did not hand back the key just written, so nothing was saved (the "
            + "keyring may be locked, or unavailable over SSH/headless). No stored identity was "
            + "changed — unlock the keyring and try again.";

        /// <summary>Best-effort removal of a secret that was written but never referenced. Safe by
        /// construction: <paramref name="keyId"/> is the GUID minted moments ago and no token file
        /// points at it, so nothing reachable can be lost. Leaving it behind would accumulate
        /// orphaned secrets in the keyring on every failed save.</summary>
        private static void DiscardUnreferencedSecret(string keyId)
        {
            try { SecretStore.Delete(WitnessKeyType, keyId); }
            catch { /* the save is already failing; a stuck orphan must not mask that */ }
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
                        SecretStore.Delete(WitnessKeyType, keyId);
                    }
                }
                catch { /* best-effort */ }

                File.Delete(KeyPath);
            }
            catch { /* best-effort */ }
        }
    }
}
