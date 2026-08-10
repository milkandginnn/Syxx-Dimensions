using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Syxx.Kit;

/// <summary>Supplies the user passphrase that wraps the file-store key (§8). Implemented by the CLI
/// (prompt, <c>SYXX_WITNESS_PASSPHRASE</c>, <c>--passphrase-file</c>) and by the GUI (a dialog).
///
/// <para>C-11: an implementation must NEVER read the passphrase from a positional or
/// <c>--passphrase</c> argument. <c>/proc/&lt;pid&gt;/cmdline</c> is world-readable on stock Ubuntu,
/// so an argv passphrase is legible to every other user on the box for the life of the process.</para>
///
/// <para>SECURITY NOTE: The environment variable route (<c>SYXX_WITNESS_PASSPHRASE</c>) carries
/// the same exposure as argv on stock Ubuntu kernels — any same-UID process can read
/// <c>/proc/&lt;pid&gt;/environ</c>. Implementations should clear the variable after reading:
/// <c>Environment.SetEnvironmentVariable("SYXX_WITNESS_PASSPHRASE", null)</c>.
/// Prefer <c>--passphrase-file</c> (0600) or the interactive prompt.</para></summary>
public interface IKeystorePassphraseProvider
{
    /// <summary>The passphrase for the local keystore, or <c>null</c> when the person has not
    /// supplied one. Returning <c>null</c> is not an error: it selects machine-only wrapping, which
    /// is weaker and is breadcrumbed as such.</summary>
    string? TryGetPassphrase();
}

/// <summary>
/// Holds the passphrase-derived key for one session and nothing longer (§8 point 6).
///
/// <para>What is retained: the DERIVED key, for <see cref="IdleTimeout"/> after its last use.
/// What is never retained: the passphrase itself — it is used to derive and then dropped.
/// On timeout the derived key is overwritten with
/// <see cref="CryptographicOperations.ZeroMemory"/> before the reference is released, so it does not
/// sit in a collectable buffer waiting for a GC that may never run.</para>
/// </summary>
public static class KeystorePassphraseSession
{
    private static readonly object Gate = new();
    private static IKeystorePassphraseProvider? _provider;

    private static byte[]? _cachedKey;
    private static string? _cachedSaltHex;
    private static int _cachedMode;
    private static DateTime _lastUsedUtc;

    /// <summary>How long a derived key survives without use. Argon2id at the parameters below costs
    /// a visible fraction of a second, so re-deriving on every read would be felt; holding it
    /// forever would defeat the point of asking.</summary>
    public static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Installs the passphrase source. Called once at startup by the CLI or the GUI.
    /// With no provider installed the store falls back to machine-only wrapping and says so.</summary>
    public static void SetProvider(IKeystorePassphraseProvider? provider)
    {
        lock (Gate)
        {
            _provider = provider;
            ClearLocked();
        }
    }

    /// <summary>Drops and zeroes the derived key. Call on lock, on sign-out, on idle.</summary>
    public static void Clear()
    {
        lock (Gate) ClearLocked();
    }

    private static void ClearLocked()
    {
        if (_cachedKey is not null) CryptographicOperations.ZeroMemory(_cachedKey);
        _cachedKey = null;
        _cachedSaltHex = null;
        _cachedMode = 0;
        // Nothing left to evict; stop the timer rather than let it wake a sleeping process.
        _evictionTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>S-5: drives idle eviction from a timer instead of only from the next access.
    ///
    /// <para>Before this, <see cref="ExpireIfIdleLocked"/> ran only when someone called in, so a
    /// process that derived a key and then went idle — the overwhelmingly common case for a desktop
    /// app left open — held the key in memory until exit. <see cref="IdleTimeout"/> described an
    /// intention that nothing enforced. The timer makes the documented window the real one.</para>
    ///
    /// <para>It is armed only while a key is actually cached and is stopped by
    /// <see cref="ClearLocked"/>, so an idle process is not woken for nothing.</para></summary>
    private static Timer? _evictionTimer;

    private static void ScheduleEvictionLocked(TimeSpan due)
    {
        if (due < TimeSpan.Zero) due = TimeSpan.Zero;
        _evictionTimer ??= new Timer(
            static _ => EvictIfIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        try
        {
            _evictionTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Racing process shutdown. The key is zeroed on exit anyway; never take the app down
            // over a timer that is already gone.
        }
    }

    private static void EvictIfIdle()
    {
        lock (Gate)
        {
            ExpireIfIdleLocked();

            // Still here means it was used after the timer was armed. Re-arm for the time it has
            // actually got left, so a key used at the last moment is not granted a whole extra
            // window.
            if (_cachedKey is not null)
                ScheduleEvictionLocked(IdleTimeout - (DateTime.UtcNow - _lastUsedUtc));
        }
    }

    /// <summary>True when a derived key is currently held. For honest UI: report the observed state,
    /// never the toggle.</summary>
    public static bool HasCachedKey
    {
        get { lock (Gate) { ExpireIfIdleLocked(); return _cachedKey is not null; } }
    }

    private static void ExpireIfIdleLocked()
    {
        if (_cachedKey is not null && DateTime.UtcNow - _lastUsedUtc > IdleTimeout)
        {
            ClearLocked();
        }
    }

    /// <summary>Returns a defensive copy of the derived 32-byte key for the requested parameters,
    /// deriving it if the cache is empty, expired, or holds a key for different parameters.
    ///
    /// <para>S-1: when <paramref name="preferPassphrase"/> is <c>true</c>, the passphrase is
    /// obtained exactly once inside this method. If the provider returns <c>null</c> the mode
    /// silently falls back to machine-only and the actual mode used is reported in the returned
    /// tuple. The caller must use <c>actualMode</c> (not the preferred mode) for the breadcrumb
    /// and the AEAD associated data, so the ciphertext header always matches the key that sealed
    /// it — closing the TOCTOU gap that existed when <c>PassphraseAvailable()</c> and this method
    /// each called <c>TryGetPassphrase()</c> independently.</para>
    ///
    /// <para>S-2: the returned array is a fresh copy so that a concurrent <see cref="Clear"/>
    /// cannot zero the key while the caller is encrypting or decrypting with it. The caller owns
    /// the copy and MUST zero it via <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/>
    /// when finished.</para></summary>
    internal static (byte[] Key, int ActualMode) GetOrDeriveKey(
        byte[] salt, bool preferPassphrase, Func<string?, int, byte[]> derive)
    {
        var saltHex = Convert.ToHexStringLower(salt);
        lock (Gate)
        {
            ExpireIfIdleLocked();

            // Determine the actual mode by attempting the passphrase when preferred.
            // The passphrase exists only inside this call — it is handed to the derivation and
            // then goes out of scope; it is never stored in a field and never breadcrumbed.
            string? passphrase = null;
            int actualMode;
            if (preferPassphrase)
            {
                passphrase = _provider?.TryGetPassphrase();
                actualMode = !string.IsNullOrEmpty(passphrase)
                    ? LinuxSecretStore.KdfModePassphrase
                    : LinuxSecretStore.KdfModeMachineOnly;
            }
            else
            {
                actualMode = LinuxSecretStore.KdfModeMachineOnly;
            }

            if (_cachedKey is not null && _cachedMode == actualMode
                && string.Equals(_cachedSaltHex, saltHex, StringComparison.Ordinal))
            {
                _lastUsedUtc = DateTime.UtcNow;
                ScheduleEvictionLocked(IdleTimeout);   // S-5
                return (_cachedKey.AsSpan().ToArray(), actualMode);
            }

            var key = derive(passphrase, actualMode);

            ClearLocked();
            _cachedKey = key;
            _cachedSaltHex = saltHex;
            _cachedMode = actualMode;
            _lastUsedUtc = DateTime.UtcNow;
            ScheduleEvictionLocked(IdleTimeout);   // S-5
            return (_cachedKey.AsSpan().ToArray(), actualMode);
        }
    }
}

/// <summary>
/// Linux <see cref="ISecretStore"/> implementation. Ties libsecret (GNOME Keyring / KDE Wallet)
/// as the primary credential backend via P/Invoke, falling back to an AES-256-GCM encrypted
/// file store at <c>~/.syxx/keystore/</c> when libsecret is unavailable.
///
/// Backends (in order of preference):
///   1. libsecret via P/Invoke  — GNOME Keyring / KDE Wallet integration (no process spawn)
///   2. secret-tool CLI         — fallback when the native library is absent but the CLI is
///   3. AES-256-GCM file store  — always-available fallback, Argon2id-derived key
///
/// All backends are guarded by runtime platform checks in the factory, so this type compiles
/// safely on any platform but is only the default on Linux (and macOS fallback).
///
/// <para>BLAST RADIUS. <see cref="PlatformSecretStore.Initialise"/> wires ONE instance of this type
/// into BOTH <see cref="LicenseManager"/> and <see cref="WitnessKeyStore"/>. Everything said here
/// about the witness signing key is therefore equally true of the licence key.</para>
///
/// <para>WHAT THE FILE STORE IS AND IS NOT WORTH (P-04, stated precisely because both the
/// overstatement and the understatement have been made). Steady state, a 0600 file needs same-user
/// access, root, or a file leak — a backup, a synced home directory, a stolen disk. It is NOT true
/// that any local process can read it at any time. But before this change there was a window at
/// EVERY write during which the ciphertext was world-readable, and a key derived from machine
/// material alone needs NO secret to reproduce once the bytes are captured. That is
/// weak-KDF-plus-file-access-with-a-race. The race is closed below (create with the mode, never
/// chmod after writing); the weak KDF is closed only when a passphrase is supplied, and its absence
/// is breadcrumbed rather than passed over.</para>
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

    internal static void SetFallbackDirectoryForTesting(string? path)
    {
        _storeDirOverride = path;
        KeystorePassphraseSession.Clear();   // the store salt is per-directory; drop the derived key
    }

    /// <summary>Per-instance directory, which takes precedence over the process-wide seam above.
    ///
    /// <para>It exists because the static seam is a single global that two test classes running in
    /// parallel will clobber for each other — the kind of shared mutable state that makes a suite
    /// intermittently red for reasons unrelated to the code under test. An instance override lets a
    /// test point ONE store at ONE directory and race nothing. <c>internal</c> for the same reason
    /// the static seam is: no product code may relocate a user's secrets.</para></summary>
    private readonly string? _instanceDir;

    public LinuxSecretStore() { }

    internal LinuxSecretStore(string directory) => _instanceDir = directory;

    private string StoreDirForInstance => _instanceDir ?? StoreDir;

    private static string StoreDir => _storeDirOverride ?? DefaultStoreDir;

    /// <summary>The directory used by the file-fallback store, for a store with no instance
    /// override.</summary>
    public static string FallbackDirectory => StoreDir;

    // ── ISecretStore ──────────────────────────────────────────────────────────────

    public void Store(string keyType, string keyId, string secret)
    {
        // 1. Try libsecret P/Invoke — and VERIFY, for the same reason as the secret-tool branch
        // below. `LibSecretNative.Store` returning true means the library call reported success,
        // not that a Secret Service persisted anything. On a headless Linux box the shared object
        // is present (it ships as a GUI dependency) while no daemon backs it: the write reports
        // success, the read returns nothing, and returning here made the file fallback — the one
        // backend that always works — unreachable precisely when it was the only one left.
        if (LibSecretNative.IsAvailable)
        {
            if (LibSecretNative.Store(keyType, keyId, secret)
                && LibSecretNative.Retrieve(keyType, keyId) == secret)
                return;
        }

        // 2. Try secret-tool CLI — and VERIFY the round-trip rather than assume it. On a headless
        // system the binary exists while no Secret Service answers (CI's ubuntu runners proved
        // this): the store call no-ops, the secret vanishes, and Retrieve returns null. Returning
        // here on mere tool-availability made the "always-available" file fallback unreachable
        // exactly when it was needed. A backend counts only when it demonstrably holds the secret.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && SecretTool.IsAvailable())
        {
            SecretTool.Store(keyType, keyId, secret);
            if (SecretTool.Retrieve(keyType, keyId) == secret)
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
    ///     prefix enumerates. It needs no service to be running, which is why it is asked first.
    ///   • libsecret P/Invoke (DONT_MATCH_NAME search) — answers for ANY secret carrying the
    ///     <c>syxx-key-type</c> attribute, regardless of schema name. Uses
    ///     <see cref="LibSecretNative.SearchForKeyType"/> which walks the returned GList of
    ///     <c>SecretRetrievable</c> items and filters by the attribute value, so witness and licence
    ///     secrets are distinguished. The search is issued with <c>SECRET_SEARCH_NONE</c>: it does
    ///     NOT request unlock and does NOT load secret values, so a LOCKED but populated collection
    ///     still answers Present. Before the answer is believed,
    ///     <see cref="LibSecretNative.SearchCapabilityUsable"/> runs a control search for a keyType
    ///     that cannot exist; if that control returns a hit or errors, the binding is not behaving
    ///     as documented and the whole capability is discarded rather than trusted. See that method
    ///     for why an unverified P/Invoke must not be allowed to answer Present — a false Present
    ///     would make first-run minting impossible.
    ///   • libsecret P/Invoke (schema-scoped search) — fallback for what the P/Invoke backend wrote,
    ///     using the <c>com.milkandgin.syxx</c> schema. Kept for resilience if the DONT_MATCH_NAME
    ///     schema is somehow not loadable.
    ///   • secret-tool — definitive for what secret-tool itself wrote, since it files secrets under
    ///     the <c>syxx-key-type</c> attribute, which <c>search</c> can query alone. But only while
    ///     secret-tool is still installed AND the Secret Service answers.
    ///
    /// <para>G-02b RESIDUAL, and what changed. The boundary is temporal, not per-backend: the gap is
    /// where a secret IS in the keyring but the keyring is unanswerable AT PROBE TIME. Repairing
    /// G-16 opened the P/Invoke write route; binding <c>secret_password_search_sync</c> with
    /// <c>SECRET_SCHEMA_DONT_MATCH_NAME</c> (see <see cref="LibSecretNative.SearchForKeyType"/>)
    /// closed the enumeration gap for BOTH backends — P/Invoke-stored and secret-tool-stored secrets
    /// are now visible to the probe. The locked-keyring case is fully closed: search matches on
    /// attributes (no unlock required for existence), so a locked but populated keyring returns
    /// Present. The "<c>libsecret-tools</c> removed after minting" half is also closed, because the
    /// shared library that performed the write is by construction still present to perform the probe.
    /// What remains open is a Secret Service that is absent or refuses at probe time — there, every
    /// backend returns Unknown and the caller's fail-open arm mints. That arm is deliberate: failing
    /// closed on Unknown would leave every GNOME/KDE desktop with no route to a first identity. It
    /// emits a Warning breadcrumb rather than being silent.</para>
    /// </summary>
    public SecretExistence Exists(string keyType)
    {
        var answer = ExistsInFileStore(keyType);
        if (answer == SecretExistence.Present) return SecretExistence.Present;

        if (LibSecretNative.IsAvailable)
        {
            // G-16 + G-02b: DONT_MATCH_NAME search — finds secrets regardless of which schema name
            // they were stored under (P/Invoke's com.milkandgin.syxx vs secret-tool's
            // org.freedesktop.Secret.Generic). The GList walk filters by syxx-key-type to separate
            // witness from licence secrets. This closes the locked-keyring case: search matches on
            // attributes (no unlock required for existence), so a locked but populated keyring
            // returns Present instead of Unknown.
            var searchAnswer = LibSecretNative.SearchForKeyType(keyType);
            if (searchAnswer == SecretExistence.Present) return SecretExistence.Present;
            if (searchAnswer == SecretExistence.Unknown) answer = SecretExistence.Unknown;

            // Schema-scoped search — finds only secrets stored through this P/Invoke backend.
            // Redundant when the DONT_MATCH_NAME search above succeeded, but kept as a second probe
            // for resilience: if the DONT_MATCH_NAME schema is somehow malformed but the scoped
            // schema works, this still answers.
            var nativeAnswer = LibSecretNative.Exists(keyType);
            if (nativeAnswer == SecretExistence.Present) return SecretExistence.Present;
            if (nativeAnswer == SecretExistence.Unknown) answer = SecretExistence.Unknown;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && SecretTool.IsAvailable())
        {
            var toolAnswer = SecretTool.Exists(keyType);
            if (toolAnswer == SecretExistence.Present) return SecretExistence.Present;
            if (toolAnswer == SecretExistence.Unknown) answer = SecretExistence.Unknown;
        }

        return answer;
    }

    /// <summary>Enumerates the AES-GCM file store for any <c>{keyType}_*</c> entry. Read-only: unlike
    /// <see cref="EnsureStoreDirectory"/> it must not create the directory, since a presence probe
    /// that materialises state is not a probe.</summary>
    private SecretExistence ExistsInFileStore(string keyType)
    {
        try
        {
            if (!Directory.Exists(StoreDirForInstance)) return SecretExistence.Absent;
            return Directory.EnumerateFiles(StoreDirForInstance, Sanitize(keyType) + "_*").Any()
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

    /// <summary>File format v2: <c>["SYXXKS02":8][kdfMode:1][salt:16][nonce:12][tag:16][ciphertext:N]</c>.
    /// v1 (<c>[nonce:12][tag:16][ciphertext:N]</c>, machine-derived key over a constant salt) is still
    /// READ, so no existing secret is stranded, and is rewritten as v2 on the next store.</summary>
    internal static ReadOnlySpan<byte> MagicV2 => "SYXXKS02"u8;

    internal static readonly int NonceSize = 12;
    internal static readonly int TagSize = 16;

    /// <summary>Argon2id salt length. libsodium's <c>crypto_pwhash</c> fixes this at 16 bytes, so it
    /// is not a tunable.</summary>
    internal const int SaltSize = 16;

    /// <summary>The key is wrapped with machine-derived material only — no user secret took part.
    /// Weaker, and breadcrumbed at every derivation.</summary>
    internal const int KdfModeMachineOnly = 1;

    /// <summary>The key is wrapped with the user passphrase (§8) together with machine material.</summary>
    internal const int KdfModePassphrase = 2;

    private const int MagicSize = 8;
    private static readonly int HeaderSize = MagicSize + 1 + SaltSize + NonceSize + TagSize;   // 53

    /// <summary>Argon2id cost. One lane is not a choice — libsodium's <c>crypto_pwhash</c> exposes no
    /// parallelism, and NSec rejects any other value. 64 MiB over 3 passes is roughly a fifth of a
    /// second on a desktop and is memory-hard enough that a GPU attacker loses most of its advantage.
    /// It buys two to four orders of magnitude, which is worth having and is NOT enough on its own to
    /// rescue a low-entropy passphrase — see <see cref="DicePassphrase.EntropyBits"/>, which is why
    /// that number is displayed rather than implied.</summary>
    private const int Argon2MemoryBytes = 64 * 1024 * 1024;
    private const int Argon2Passes = 3;

    /// <summary>Name of the per-store salt. Leading dot so it cannot collide with a
    /// <c>{keyType}_{keyId}</c> entry and cannot be mistaken for one by
    /// <see cref="ExistsInFileStore"/>.</summary>
    private const string SaltFileName = ".kdf-salt";

    /// <summary>
    /// Reads (or creates) the per-store Argon2id salt.
    ///
    /// <para>P-04 defect 2: the previous key derivation used <c>SHA256("Syxx-AES-256-GCM-Key-v1")</c>
    /// — the same salt on every install on earth. Against a reconstructible input that made the
    /// 350,000 PBKDF2 iterations worthless: one precomputation served every machine. A per-store
    /// random salt makes each install its own problem.</para>
    ///
    /// <para>The salt is also copied into every ciphertext header, so losing this file cannot strand
    /// an existing secret; the file exists so that all entries in one store share a salt and
    /// therefore one cached derivation.</para>
    /// </summary>
    private byte[] LoadOrCreateStoreSalt()
    {
        var path = Path.Combine(StoreDirForInstance, SaltFileName);
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == SaltSize) return existing;
                // A wrong-length salt file is corruption. Every v2 file carries its own copy of the
                // salt it was written with, so replacing this one cannot orphan anything.
                Breadcrumb("keystore salt file was the wrong length and was regenerated");
            }
        }
        catch (Exception ex)
        {
            Breadcrumb("keystore salt file could not be read", ex);
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        try
        {
            EnsureStoreDirectory();
            WriteFilePrivately(path, salt);
        }
        catch (Exception ex)
        {
            // A salt that cannot be persisted still protects THIS write, because it is stored in the
            // header. The cost is a fresh Argon2id derivation for each entry.
            Breadcrumb("keystore salt file could not be written; per-entry salts will be used", ex);
        }
        return salt;
    }

    /// <summary>
    /// Derives the 256-bit master key for the file store with Argon2id.
    ///
    /// <para>P-04 defect 1: before this, no user secret took part at all — the key was a function of
    /// <c>/etc/machine-id</c>, <c>product_uuid</c> and the hostname, every one of which an attacker
    /// holding the file usually also holds. When <paramref name="passphrase"/> is non-empty it is the
    /// first and dominant input; the machine material is kept alongside it as a second factor, so a
    /// stolen file still needs the machine as well as the words.</para>
    ///
    /// <para>P-04 defect 4: each machine-material read that fails now emits a breadcrumb.
    /// <c>/sys/class/dmi/id/product_uuid</c> is mode 0400 and so ALWAYS fails unprivileged, and is
    /// absent outright on most arm64 — before this, that plus a missing <c>/etc/machine-id</c> left
    /// the key derived from a bare hostname with nothing anywhere reporting it. Silent degradation of
    /// a security primitive is a fabricated assurance (spine #4). No breadcrumb carries any of the
    /// material, and none carries the passphrase.</para>
    /// </summary>
    private static byte[] DeriveMasterKey(string? passphrase, byte[] salt, int mode)
    {
        int machineSources = 0;

        // /etc/machine-id — stable per installation
        string machineId = string.Empty;
        try
        {
            machineId = File.ReadAllText("/etc/machine-id").Trim();
            machineSources++;
        }
        catch (Exception ex)
        {
            Breadcrumb("keystore key material: /etc/machine-id unreadable", ex);
        }

        // /sys/class/dmi/id/product_uuid — hardware-bound, mode 0400 so root-only, and absent on
        // most arm64. Expected to fail for an ordinary user; recorded so the loss is visible.
        string productUuid = string.Empty;
        try
        {
            productUuid = File.ReadAllText("/sys/class/dmi/id/product_uuid").Trim();
            machineSources++;
        }
        catch (Exception ex)
        {
            Breadcrumb("keystore key material: product_uuid unreadable (expected unprivileged)", ex);
        }

        // Hostname — volatile but provides uniqueness in containers. Never counted as a real source:
        // it is guessable and often published.
        string hostName = Environment.MachineName;

        bool havePassphrase = !string.IsNullOrEmpty(passphrase);
        if (!havePassphrase)
        {
            // E016: breadcrumb wrapped in try/catch — must not replace the original exception path.
            try
            {
                SyxxDiagnostics.Breadcrumb(
                    category: "keystore.kdf",
                    message: "Keystore key derived WITHOUT a user passphrase — machine material only. "
                           + "Anyone who obtains the keystore file and can read this machine's identifiers "
                           + "can reproduce the key.",
                    level: DiagnosticSeverity.Warning,
                    data: new Dictionary<string, string>
                    {
                        ["machineSources"] = machineSources.ToString(CultureInfo.InvariantCulture),
                        ["kdf"] = "argon2id",
                    });
            }
            catch { /* E016: breadcrumb infrastructure must not mask the KDF path */ }
        }
        else if (machineSources == 0)
        {
            Breadcrumb("keystore key material: no machine identifier was readable; the passphrase is "
                     + "the only input to the key");
        }

        if (!havePassphrase && machineSources == 0)
        {
            // E016: breadcrumb wrapped in try/catch — must not replace the original exception path.
            try
            {
                SyxxDiagnostics.Breadcrumb(
                    category: "keystore.kdf",
                    message: "Keystore key derived from the hostname alone — no passphrase and no machine "
                           + "identifier was readable. This is the weakest state this store has.",
                    level: DiagnosticSeverity.Error,
                    data: new Dictionary<string, string> { ["kdf"] = "argon2id" });
            }
            catch { /* E016: breadcrumb infrastructure must not mask the KDF path */ }
        }

        // The passphrase leads, so it dominates the first block of the derivation; the machine
        // material follows. A NUL separator keeps the two from being confusable by concatenation.
        //
        // S-4, ZEROING CONTRACT — read this before changing the assembly below, and do not
        // restate it more strongly than it is.
        //
        // WHAT IS ZEROED: the assembled password bytes, in a pooled buffer wiped in the `finally`
        // before it is returned to the pool. That buffer is the only place the passphrase and the
        // machine material appear together.
        //
        // WHY IT IS ASSEMBLED THIS WAY: the previous version built the same bytes with
        // `passphrase + "\0" + material.ToString()`. String concatenation materialises an immutable
        // heap `string` CONTAINING THE PASSPHRASE IN PLAINTEXT, which cannot be zeroed and survives
        // until an indeterminate GC. It then zeroed only the `byte[]` and called `material.Clear()`
        // under a comment claiming the material had been zeroed. `StringBuilder.Clear()` sets
        // `Length = 0`; it does not wipe the backing `char[]`. Both claims were false, which is the
        // fabricated-assurance class this file exists to refuse (spine #4). Encoding straight into
        // the pooled buffer means the passphrase is never copied into a string here at all.
        //
        // WHAT IS *NOT* ZEROED, STATED PLAINLY: `machineId`, `productUuid` and `hostName` are
        // `string`s returned by the runtime, and .NET cannot reliably wipe a `string` — the GC may
        // already have copied it. Those three are machine IDENTIFIERS, not user secrets: an attacker
        // who can read this process's heap can equally read `/etc/machine-id`. The passphrase is the
        // only value here whose disclosure is fatal, and it is the one this method keeps out of the
        // heap. The caller's `passphrase` string is owned by the provider and is likewise not
        // zeroable; closing that would take a `SecureString`/`char[]` provider contract, which is
        // recorded as open work rather than implied to be done.
        int capacity = 1 + Encoding.UTF8.GetMaxByteCount(machineId.Length)
                         + Encoding.UTF8.GetMaxByteCount(productUuid.Length)
                         + Encoding.UTF8.GetMaxByteCount(hostName.Length)
                         + (havePassphrase ? Encoding.UTF8.GetMaxByteCount(passphrase!.Length) : 0);

        var password = ArrayPool<byte>.Shared.Rent(capacity);
        int written = 0;
        try
        {
            if (havePassphrase)
                written += Encoding.UTF8.GetBytes(passphrase.AsSpan(), password.AsSpan(written));
            password[written++] = 0;   // the NUL separator, byte-identical to the "\0" above
            written += Encoding.UTF8.GetBytes(machineId.AsSpan(), password.AsSpan(written));
            written += Encoding.UTF8.GetBytes(productUuid.AsSpan(), password.AsSpan(written));
            written += Encoding.UTF8.GetBytes(hostName.AsSpan(), password.AsSpan(written));

            var argon2id = new Argon2id(new Argon2Parameters
            {
                DegreeOfParallelism = 1,
                MemorySize = Argon2MemoryBytes,
                NumberOfPasses = Argon2Passes,
            });
            return argon2id.DeriveBytes(password.AsSpan(0, written), salt, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password.AsSpan(0, written));
            ArrayPool<byte>.Shared.Return(password);
            _ = mode;   // bound into the AEAD's associated data by the caller, not into the key
        }
    }

    /// <summary>Derives the per-entry key from the master key, so two entries in one store never
    /// share an AES-GCM key. HKDF is free next to Argon2id, and it means the expensive derivation is
    /// done once per session rather than once per secret.</summary>
    private static byte[] DeriveEntryKey(byte[] masterKey, string keyType, string keyId)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: masterKey,
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes("syxx-keystore-v2|" + keyType + "|" + keyId));
    }

    private static byte[] AssociatedData(string keyType, int mode) =>
        // The KDF mode is inside the AEAD's associated data, so an attacker cannot flip the header
        // byte from "passphrase" to "machine only" and have us derive a key they can reproduce: the
        // tag check fails first.
        Encoding.UTF8.GetBytes("syxx-keystore-v2:" + keyType + ":"
                               + mode.ToString(CultureInfo.InvariantCulture));

    private string GetFilePath(string keyType, string keyId) =>
        Path.Combine(StoreDirForInstance, $"{Sanitize(keyType)}_{Sanitize(keyId)}");

    /// <summary>
    /// P-04 defect 3, half one. The directory used to be created by <c>Directory.CreateDirectory</c>
    /// with no mode, leaving <c>~/.syxx/keystore</c> at the umask default — world-listable, and
    /// PERMANENTLY, because nothing ever narrowed it afterwards. <c>Directory.CreateDirectory(path,
    /// mode)</c> (.NET 7+) passes the mode to <c>mkdir(2)</c>, so the directory is never briefly
    /// permissive; there is no window to lose.
    /// </summary>
    private void EnsureStoreDirectory()
    {
        if (Directory.Exists(StoreDirForInstance)) return;

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(StoreDirForInstance);
            return;
        }

        Directory.CreateDirectory(StoreDirForInstance,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// P-04 defect 3, half two, and P-04b.
    ///
    /// <para>The old path was <c>File.Create</c> → write ciphertext → spawn <c>chmod 0600</c>. Two
    /// faults. The ciphertext was on disk world-readable for the whole span between the write and the
    /// chmod, and once captured it needed no secret to decrypt. And <c>chmod</c> was resolved through
    /// <c>$PATH</c> — hazard D102, already fixed elsewhere in this Kit via a <c>SystemToolPath()</c>
    /// helper (see <c>UnboundManager</c>, <c>EncryptedDNSConfigurator</c>). That helper is superseded
    /// here rather than reused: <see cref="File.SetUnixFileMode"/> has existed since .NET 7 and this
    /// project is <c>net10.0</c>, so the subprocess buys nothing and only carries risk.</para>
    ///
    /// <para>The replacement creates the temporary file with <see cref="FileStreamOptions.UnixCreateMode"/>
    /// — the mode is passed to <c>open(2)</c>, so the file is 0600 from the instant it exists and has
    /// never held a byte at any other mode — writes, then <c>rename(2)</c>s over the target.
    /// <c>File.Move(..., overwrite: true)</c> is a rename within one directory and so is atomic: a
    /// crash mid-write cannot leave a truncated keystore entry either.</para>
    /// </summary>
    private static void WriteFilePrivately(string path, ReadOnlySpan<byte> content)
    {
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(tmp, options))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            throw;
        }
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
        byte[]? plaintext = null;
        byte[]? masterKey = null;
        try
        {
            EnsureStoreDirectory();
            var path = GetFilePath(keyType, keyId);

            var salt = LoadOrCreateStoreSalt();

            // S-1: preferPassphrase=true; GetOrDeriveKey calls TryGetPassphrase() once and
            // falls back to machine-only if the provider returns null. No TOCTOU gap.
            // S-2: masterKey is a defensive copy — caller owns and must zero it.
            (masterKey, int mode) = KeystorePassphraseSession.GetOrDeriveKey(
                salt, preferPassphrase: true,
                (pass, actualMode) => DeriveMasterKey(pass, salt, actualMode));
            var entryKey = DeriveEntryKey(masterKey, keyType, keyId);

            plaintext = Encoding.UTF8.GetBytes(secret);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            try
            {
                using var aes = new AesGcm(entryKey, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(keyType, mode));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entryKey);
            }

            var blob = new byte[HeaderSize + ciphertext.Length];
            var span = blob.AsSpan();
            MagicV2.CopyTo(span);
            span[MagicSize] = (byte)mode;
            salt.CopyTo(span[(MagicSize + 1)..]);
            nonce.CopyTo(span[(MagicSize + 1 + SaltSize)..]);
            tag.CopyTo(span[(MagicSize + 1 + SaltSize + NonceSize)..]);
            ciphertext.CopyTo(span[HeaderSize..]);

            WriteFilePrivately(path, blob);
        }
        catch (Exception ex)
        {
            // ISecretStore.Store is documented best-effort and WitnessKeyStore.SaveKey verifies with
            // a read-back (G-01). Swallowing silently is what made that verification necessary, so
            // the failure is at least recorded here.
            Breadcrumb("keystore write failed", ex);
            // …and named on stderr. A breadcrumb goes to the telemetry sink, which is absent in CI
            // and in any offline run, so the only symptom of a failed write was a later read
            // returning null with nothing to say why. This is the store the product falls back to
            // when every other backend is gone; when it cannot write, that must be legible where
            // the failure is actually being read.
            Console.Error.WriteLine(
                $"[syxx keystore] file-fallback write failed for {keyType}/{keyId} in "
                + $"{StoreDirForInstance}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            if (masterKey is not null) CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private string? RetrieveFromFile(string keyType, string keyId)
    {
        try
        {
            var path = GetFilePath(keyType, keyId);
            if (!File.Exists(path)) return null;

            var fileBytes = File.ReadAllBytes(path);

            if (fileBytes.Length >= HeaderSize
                && fileBytes.AsSpan(0, MagicSize).SequenceEqual(MagicV2))
            {
                return DecryptV2(fileBytes, keyType, keyId);
            }

            return DecryptLegacyV1(fileBytes, keyType);
        }
        catch
        {
            return null;
        }
    }

    private static string? DecryptV2(byte[] fileBytes, string keyType, string keyId)
    {
        int mode = fileBytes[MagicSize];
        var salt = fileBytes.AsSpan(MagicSize + 1, SaltSize).ToArray();
        var nonce = fileBytes.AsSpan(MagicSize + 1 + SaltSize, NonceSize);
        var tag = fileBytes.AsSpan(MagicSize + 1 + SaltSize + NonceSize, TagSize);
        var ciphertext = fileBytes.AsSpan(HeaderSize);

        // S-1: For reads, preferPassphrase matches the file's recorded mode — if the file was
        // sealed with a passphrase, attempt the passphrase; if machine-only, skip it.
        // S-2: masterKey is a defensive copy — zero it in finally.
        var (masterKey, actualMode) = KeystorePassphraseSession.GetOrDeriveKey(
            salt, preferPassphrase: mode == KdfModePassphrase,
            (pass, m) => DeriveMasterKey(pass, salt, m));
        var entryKey = DeriveEntryKey(masterKey, keyType, keyId);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(entryKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(keyType, actualMode));
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            // Either the file was tampered with or the passphrase was wrong — indistinguishable, by
            // design, because AES-GCM will not tell them apart. Evict the cached derivation so a
            // mistyped passphrase costs one attempt rather than locking the store for the whole idle
            // window: without this the wrong key stays cached and every later read fails too, which
            // reads to the user as "my key is gone".
            if (actualMode == KdfModePassphrase) KeystorePassphraseSession.Clear();
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entryKey);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    /// <summary>
    /// LEGACY READ ONLY, never written again. v1 entries were sealed with PBKDF2 over machine
    /// material and the constant salt <c>SHA256("Syxx-AES-256-GCM-Key-v1")</c> — that is precisely
    /// the weakness P-04 names. It is kept because a user's licence key and signing key are already
    /// sitting in this format on disk, and stranding them would destroy the identity this whole
    /// subsystem exists to protect. The next <see cref="Store"/> for the same entry rewrites it as
    /// v2; nothing rewrites it in place, because a presence probe or a read must not mutate state.
    /// </summary>
    private static string? DecryptLegacyV1(byte[] fileBytes, string keyType)
    {
        if (fileBytes.Length < NonceSize + TagSize) return null;

        var material = new StringBuilder();
        try { material.Append(File.ReadAllText("/etc/machine-id").Trim()); } catch { }
        try { material.Append(File.ReadAllText("/sys/class/dmi/id/product_uuid").Trim()); } catch { }
        material.Append(Environment.MachineName);

        var legacySalt = SHA256.HashData("Syxx-AES-256-GCM-Key-v1"u8.ToArray());
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(material.ToString()),
            legacySalt,
            iterations: 350000,
            HashAlgorithmName.SHA256,
            outputLength: 32);
        // S-4: no passphrase takes part in the v1 derivation, so nothing secret passes through this
        // StringBuilder — only machine identifiers, which are readable from the filesystem anyway.
        // No Clear() call here: it would set Length = 0 without wiping the backing char[], and a
        // wipe that does not wipe is worse than an honest absence.

        var plaintext = new byte[fileBytes.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                fileBytes.AsSpan(0, NonceSize),
                fileBytes.AsSpan(NonceSize + TagSize),
                fileBytes.AsSpan(NonceSize, TagSize),
                plaintext,
                Encoding.UTF8.GetBytes("syxx-keystore-v1:" + keyType));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void DeleteFromFile(string keyType, string keyId)
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

    /// <summary>Records a degradation without ever carrying the material that degraded. Exception
    /// TYPE only — an exception message on this path can hold a path under the user's home
    /// directory, which SyxxDiagnostics forbids.</summary>
    /// <remarks>E016: wrapped in try/catch — a breadcrumb failure must never replace the original
    /// exception that triggered the breadcrumb call.</remarks>
    private static void Breadcrumb(string message, Exception? ex = null)
    {
        try
        {
            var data = new Dictionary<string, string>();
            if (ex is not null) data["error"] = ex.GetType().Name;
            SyxxDiagnostics.Breadcrumb(
                category: "keystore",
                message: message,
                level: DiagnosticSeverity.Warning,
                data: data);
        }
        catch
        {
            // E016: breadcrumb infrastructure must not mask the original error path.
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
        private static IntPtr _attrKeyTypeName;
        private static IntPtr _attrKeyIdName;

        /// <summary>libsecret <c>SecretSchema</c> attribute names. These are the strings
        /// <c>secret_attributes_buildv</c> matches the varargs NAME slots against; anything else
        /// makes it return NULL. Declared once and reused at every call site, which is the whole of
        /// the G-16 fix.</summary>
        private const string AttrKeyType = "syxx-key-type";
        private const string AttrKeyId = "syxx-key-id";

        /// <summary><c>SECRET_SEARCH_NONE</c>. Deliberately NOT <c>SECRET_SEARCH_UNLOCK</c>
        /// (1&lt;&lt;2) or <c>SECRET_SEARCH_LOAD_SECRETS</c> (1&lt;&lt;3): an existence probe must
        /// neither pop an unlock prompt nor pull secret values into this process, and a search
        /// without them still returns LOCKED items — which is exactly the case a probe must not
        /// mistake for "empty".</summary>
        private const int SecretSearchNone = 0;

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

        /// <summary><c>GList *secret_password_search_sync (const SecretSchema *, SecretSearchFlags,
        /// GCancellable *, GError **, ...)</c>. Three varargs slots is enough for one
        /// name/value pair plus the NULL terminator, which is all an existence probe needs — the
        /// keyId lives only in the token file that has, by hypothesis, been lost.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretPasswordSearchSync(
            IntPtr schema, int flags, IntPtr cancellable, out IntPtr error,
            IntPtr attrK1, IntPtr attrV1, IntPtr terminator);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GErrorFree(IntPtr error);

        /// <summary><c>secret_password_free</c>: frees AND zeroes the returned password. NOT
        /// <c>g_error_free</c>, which the previous code called on it — passing a plain string to a
        /// GError destructor reads a type tag and a domain out of the middle of the password bytes
        /// and frees whatever pointer it finds there. That is heap corruption, and it became
        /// reachable the moment G-16 was fixed and this backend started returning values.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SecretPasswordFree(IntPtr password);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GListFreeFull(IntPtr list, IntPtr freeFunc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GListFree(IntPtr list);

        /// <summary><c>GList *secret_password_search_sync (const SecretSchema *, SecretSearchFlags,
        /// GCancellable *, GError **, ...)</c> called with NO attribute filter — just the NULL
        /// terminator. Used by the DONT_MATCH_NAME search that must find secrets regardless of which
        /// schema name they were stored under (our P/Invoke schema vs secret-tool's
        /// <c>org.freedesktop.Secret.Generic</c>).</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretPasswordSearchSyncUnscoped(
            IntPtr schema, int flags, IntPtr cancellable, out IntPtr error,
            IntPtr terminator);

        /// <summary><c>GHashTable *secret_retrievable_get_attributes (SecretRetrievable *)</c>.
        /// Returns a <c>(transfer full)</c> hash table the caller must unref. The table's keys and
        /// values are owned by the retrievable and must NOT be freed separately.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SecretRetrievableGetAttributes(IntPtr retrievable);

        /// <summary><c>gpointer g_hash_table_lookup (GHashTable *, gconstpointer)</c>. Returns the
        /// value stored under the key, or NULL. The returned pointer is owned by the hash table —
        /// do NOT free it.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GHashTableLookup(IntPtr hashTable, IntPtr key);

        /// <summary><c>void g_hash_table_unref (GHashTable *)</c>. Decrements the reference count
        /// of a hash table created with <c>(transfer full)</c>.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GHashTableUnref(IntPtr hashTable);

        /// <summary><c>guint g_list_length (GList *)</c>. Walks the list and returns the element
        /// count. O(n), but the list is tiny (one or two Syxx secrets at most).</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint GListLength(IntPtr list);

        /// <summary><c>gpointer g_list_nth_data (GList *, guint)</c>. Returns the data pointer at
        /// position <c>n</c> (0-based), or NULL if out of range.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GListNthData(IntPtr list, uint n);

        /// <summary><c>void g_object_unref (gpointer)</c>. Decrements the reference count of a
        /// GObject. When it reaches zero the object is finalized. Each <c>SecretRetrievable</c>
        /// returned by <c>secret_password_search_sync</c> is a GObject that must be unref'd.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void GObjectUnref(IntPtr obj);

        private static SecretPasswordStoreSync? _storeFunc;
        private static SecretPasswordLookupSync? _lookupFunc;
        private static SecretPasswordClearSync? _clearFunc;
        private static SecretPasswordSearchSync? _searchFunc;
        private static SecretPasswordSearchSyncUnscoped? _searchUnscopedFunc;
        private static SecretPasswordFree? _passwordFree;
        private static GErrorFree? _gErrorFree;
        private static GListFreeFull? _gListFreeFull;
        private static GListFree? _gListFree;
        private static IntPtr _gObjectUnrefPtr;
        private static GObjectUnref? _gObjectUnref;
        private static SecretRetrievableGetAttributes? _retrievableGetAttrs;
        private static GHashTableLookup? _gHashTableLookup;
        private static GHashTableUnref? _gHashTableUnref;
        private static GListLength? _gListLength;
        private static GListNthData? _gListNthData;

        /// <summary>Second schema with <c>SECRET_SCHEMA_DONT_MATCH_NAME</c> (flag 1&lt;&lt;1 = 2).
        /// Used by <see cref="SearchForKeyType"/> to find secrets regardless of which schema name
        /// they were stored under — our P/Invoke writes use <c>com.milkandgin.syxx</c>, but
        /// <c>secret-tool</c> writes use <c>org.freedesktop.Secret.Generic</c>. A schema-scoped
        /// search would miss one or the other.</summary>
        private static IntPtr _searchSchemaPtr;

        internal static bool IsAvailable
        {
            get
            {
                if (!_checked) TryLoad();
                return _available;
            }
        }

        private static IntPtr TryGetExport(IntPtr handle, string name) =>
            NativeLibrary.TryGetExport(handle, name, out var addr) ? addr : IntPtr.Zero;

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

                var freePtr = TryGetExport(_handle, "secret_password_free");
                if (freePtr != IntPtr.Zero)
                    _passwordFree = Marshal.GetDelegateForFunctionPointer<SecretPasswordFree>(freePtr);

                // secret_password_search_sync arrived in libsecret 0.19. Its absence is not an error
                // — the store simply keeps its pre-existing inability to answer Exists for this
                // backend, and Exists reports Unknown rather than claiming Absent.
                var searchPtr = TryGetExport(_handle, "secret_password_search_sync");
                if (searchPtr != IntPtr.Zero)
                {
                    _searchFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordSearchSync>(searchPtr);
                    // Same function, different varargs shape: the unscoped delegate passes only the
                    // NULL terminator (no attribute name/value pairs), for the DONT_MATCH_NAME search
                    // that must find secrets regardless of schema name.
                    _searchUnscopedFunc = Marshal.GetDelegateForFunctionPointer<SecretPasswordSearchSyncUnscoped>(searchPtr);
                }

                // secret_retrievable_get_attributes: needed to inspect each search result's attributes
                // and determine whether a SecretRetrievable is a witness or licence secret.
                var retrievableAttrsPtr = TryGetExport(_handle, "secret_retrievable_get_attributes");
                if (retrievableAttrsPtr != IntPtr.Zero)
                    _retrievableGetAttrs = Marshal.GetDelegateForFunctionPointer<SecretRetrievableGetAttributes>(retrievableAttrsPtr);

                // glib/gobject: resolvable through the libsecret handle because dlsym searches a
                // library's dependency chain; libglib-2.0.so.0 is the explicit fallback. The previous
                // code asked for "glib-2.0.so.0", which NativeLibrary.Load does not decorate with a
                // "lib" prefix, so that fallback could never have loaded.
                var errFreePtr = TryGetExport(_handle, "g_error_free");
                var listFreeFullPtr = TryGetExport(_handle, "g_list_free_full");
                var listFreePtr = TryGetExport(_handle, "g_list_free");
                var hashLookupPtr = TryGetExport(_handle, "g_hash_table_lookup");
                var hashUnrefPtr = TryGetExport(_handle, "g_hash_table_unref");
                var listLengthPtr = TryGetExport(_handle, "g_list_length");
                var listNthDataPtr = TryGetExport(_handle, "g_list_nth_data");
                _gObjectUnrefPtr = TryGetExport(_handle, "g_object_unref");

                if (errFreePtr == IntPtr.Zero || listFreeFullPtr == IntPtr.Zero
                    || hashLookupPtr == IntPtr.Zero || listLengthPtr == IntPtr.Zero)
                {
                    try
                    {
                        var glib = NativeLibrary.Load("libglib-2.0.so.0");
                        if (errFreePtr == IntPtr.Zero) errFreePtr = TryGetExport(glib, "g_error_free");
                        if (listFreeFullPtr == IntPtr.Zero) listFreeFullPtr = TryGetExport(glib, "g_list_free_full");
                        if (listFreePtr == IntPtr.Zero) listFreePtr = TryGetExport(glib, "g_list_free");
                        if (hashLookupPtr == IntPtr.Zero) hashLookupPtr = TryGetExport(glib, "g_hash_table_lookup");
                        if (hashUnrefPtr == IntPtr.Zero) hashUnrefPtr = TryGetExport(glib, "g_hash_table_unref");
                        if (listLengthPtr == IntPtr.Zero) listLengthPtr = TryGetExport(glib, "g_list_length");
                        if (listNthDataPtr == IntPtr.Zero) listNthDataPtr = TryGetExport(glib, "g_list_nth_data");
                    }
                    catch { /* glib not separately loadable; the resolved subset still works */ }
                }
                if (_gObjectUnrefPtr == IntPtr.Zero)
                {
                    try { _gObjectUnrefPtr = TryGetExport(NativeLibrary.Load("libgobject-2.0.so.0"), "g_object_unref"); }
                    catch { /* list entries will be freed without unref-ing the items */ }
                }

                if (errFreePtr != IntPtr.Zero)
                    _gErrorFree = Marshal.GetDelegateForFunctionPointer<GErrorFree>(errFreePtr);
                if (listFreeFullPtr != IntPtr.Zero)
                    _gListFreeFull = Marshal.GetDelegateForFunctionPointer<GListFreeFull>(listFreeFullPtr);
                if (listFreePtr != IntPtr.Zero)
                    _gListFree = Marshal.GetDelegateForFunctionPointer<GListFree>(listFreePtr);
                if (hashLookupPtr != IntPtr.Zero)
                    _gHashTableLookup = Marshal.GetDelegateForFunctionPointer<GHashTableLookup>(hashLookupPtr);
                if (hashUnrefPtr != IntPtr.Zero)
                    _gHashTableUnref = Marshal.GetDelegateForFunctionPointer<GHashTableUnref>(hashUnrefPtr);
                if (listLengthPtr != IntPtr.Zero)
                    _gListLength = Marshal.GetDelegateForFunctionPointer<GListLength>(listLengthPtr);
                if (listNthDataPtr != IntPtr.Zero)
                    _gListNthData = Marshal.GetDelegateForFunctionPointer<GListNthData>(listNthDataPtr);
                if (_gObjectUnrefPtr != IntPtr.Zero)
                    _gObjectUnref = Marshal.GetDelegateForFunctionPointer<GObjectUnref>(_gObjectUnrefPtr);

                // Build the SecretSchema as a pinned memory block.
                // Schema layout (see libsecret/secret-schema.h):
                //   [name_ptr:8][flags:4][padding:4]
                //   [attr0_name:8][attr0_type:4][padding:4]
                //   [attr1_name:8][attr1_type:4][padding:4]
                //   [attr2_name:8][attr2_type:4][padding:4]
                //   [attr3_name:8][attr3_type:4][padding:4]  -- sentinel (name=null)
                var schemaName = Marshal.StringToCoTaskMemUTF8("com.milkandgin.syxx");
                _attrKeyTypeName = Marshal.StringToCoTaskMemUTF8(AttrKeyType);
                _attrKeyIdName = Marshal.StringToCoTaskMemUTF8(AttrKeyId);
                const int SecretSchemaAttributeString = 0;

                var schemaBytes = new byte[64]; // 5 entries * 12 bytes + alignment padding
                var schemaHandle = GCHandle.Alloc(schemaBytes, GCHandleType.Pinned);
                _schemaPtr = schemaHandle.AddrOfPinnedObject();

                // Write schema_name pointer
                Marshal.WriteIntPtr(_schemaPtr, 0, schemaName);
                // flags = 0 (SECRET_SCHEMA_NONE)
                Marshal.WriteInt32(_schemaPtr, 8, 0);
                // attr0: name, type
                Marshal.WriteIntPtr(_schemaPtr, 16, _attrKeyTypeName);
                Marshal.WriteInt32(_schemaPtr, 24, SecretSchemaAttributeString);
                // attr1: name, type
                Marshal.WriteIntPtr(_schemaPtr, 32, _attrKeyIdName);
                Marshal.WriteInt32(_schemaPtr, 40, SecretSchemaAttributeString);
                // attr2-4: sentinel (null name, zero type) -- already zeroed

                // Build a SECOND schema for the DONT_MATCH_NAME search. Same attribute definitions,
                // but the flag tells libsecret to ignore the schema name when matching. This is
                // required because secret-tool stores items with xdg:schema =
                // org.freedesktop.Secret.Generic, while our P/Invoke writes use
                // com.milkandgin.syxx. A schema-scoped search (flag 0) would only find one set.
                const int SecretSchemaDontMatchName = 1 << 1; // = 2
                var searchSchemaBytes = new byte[64];
                var searchSchemaHandle = GCHandle.Alloc(searchSchemaBytes, GCHandleType.Pinned);
                _searchSchemaPtr = searchSchemaHandle.AddrOfPinnedObject();

                Marshal.WriteIntPtr(_searchSchemaPtr, 0, schemaName);   // name is ignored by the flag
                Marshal.WriteInt32(_searchSchemaPtr, 8, SecretSchemaDontMatchName);
                Marshal.WriteIntPtr(_searchSchemaPtr, 16, _attrKeyTypeName);
                Marshal.WriteInt32(_searchSchemaPtr, 24, SecretSchemaAttributeString);
                Marshal.WriteIntPtr(_searchSchemaPtr, 32, _attrKeyIdName);
                Marshal.WriteInt32(_searchSchemaPtr, 40, SecretSchemaAttributeString);
                // sentinel: already zeroed

                _available = true;
            }
            catch
            {
                // libsecret not available; caller falls back to secret-tool or file store
                _available = false;
            }
        }

        /// <summary>
        /// G-16. The varargs of <c>secret_password_store_sync</c> are ATTRIBUTE NAME / ATTRIBUTE
        /// VALUE pairs terminated by NULL. The previous call passed the keyType VALUE ("witness")
        /// into the first NAME slot and the keyId VALUE into the first VALUE slot, so libsecret was
        /// asked for an attribute literally named "witness". No such attribute is declared in the
        /// schema, <c>secret_attributes_buildv</c> answered with <c>g_critical</c> + NULL, and
        /// <c>secret_password_store_sync</c>'s "<c>if (!attributes) return FALSE;</c>" returned FALSE
        /// WITHOUT setting the GError. <see cref="Store"/> read that as "backend declined" and fell
        /// through, so nothing was ever persisted by this route.
        ///
        /// <para>The fix is the slot alignment below: name, value, name, value, NULL.</para>
        ///
        /// <para>EVIDENCE BOUNDARY. Read from libsecret master's <c>secret-password.c</c> and
        /// <c>secret-attributes.c</c>. NOT verified against 0.18 / 0.20 or distro-patched builds, and
        /// not executed at all — there is no libsecret on the authoring host. The claim is "this call
        /// could not have stored on the sources read", not "never, on every machine".</para>
        /// </summary>
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
                        IntPtr.Zero,        // collection: NULL = default
                        labelPtr,
                        passwordPtr,
                        IntPtr.Zero,        // cancellable: NULL (synchronous)
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        _attrKeyIdName, keyIdPtr,       // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

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
                        IntPtr.Zero,        // cancellable
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        _attrKeyIdName, keyIdPtr,       // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

                    if (resultPtr != IntPtr.Zero)
                    {
                        var result = Marshal.PtrToStringUTF8(resultPtr);
                        // secret_password_free zeroes the buffer before releasing it. If it could not
                        // be resolved we leak rather than guess at a destructor — a wrong free on a
                        // secret is worse than a leaked page.
                        _passwordFree?.Invoke(resultPtr);
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
                        IntPtr.Zero,        // cancellable
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        _attrKeyIdName, keyIdPtr,       // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

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

        private static bool? _searchUsable;

        /// <summary>
        /// Whether the search binding may be believed.
        ///
        /// <para>WHY THIS EXISTS. Nothing in this P/Invoke layer has ever been executed against a
        /// real libsecret — there is none on the authoring host — and the answer this binding
        /// produces is consumed by the mint guard. A false <b>Unknown</b> is harmless: the caller
        /// already fails open on Unknown. A false <b>Present</b> is not: it would tell every
        /// first-run GNOME/KDE machine that an identity already exists, leaving no route to a first
        /// key at all. Believing an unverified binding in that direction is exactly the "the artifact
        /// exists, so it must be in force" reasoning this codebase keeps getting wrong.</para>
        ///
        /// <para>So the binding validates itself at the point of use: it searches for a keyType that
        /// cannot exist. A correct binding returns an empty list and no error. Anything else — a hit,
        /// an error, a throw — means the call is not behaving as documented, the capability is
        /// discarded, and this backend goes back to answering Unknown.</para>
        /// </summary>
        private static bool SearchCapabilityUsable()
        {
            if (_searchUsable.HasValue) return _searchUsable.Value;

            var control = "syxx-control-" + Guid.NewGuid().ToString("N");
            var outcome = RawSearch(control);
            _searchUsable = outcome == RawSearchOutcome.Empty;
            if (_searchUsable == false)
            {
                // E016: breadcrumb wrapped in try/catch — must not mask the search-capability result.
                try
                {
                    SyxxDiagnostics.Breadcrumb(
                        category: "keystore.libsecret",
                        message: "libsecret search binding failed its control probe and will not be used "
                               + "to answer key presence; this backend reports Unknown instead.",
                        level: DiagnosticSeverity.Warning,
                        data: new Dictionary<string, string> { ["outcome"] = outcome.ToString() });
                }
                catch { /* E016: breadcrumb infrastructure must not replace the capability probe result */ }
            }
            return _searchUsable.Value;
        }

        private enum RawSearchOutcome { Empty, Found, Failed }

        private static RawSearchOutcome RawSearch(string keyType)
        {
            if (_searchFunc is null) return RawSearchOutcome.Failed;

            try
            {
                var keyTypePtr = Marshal.StringToCoTaskMemUTF8(keyType);
                try
                {
                    IntPtr list = _searchFunc(
                        _schemaPtr,
                        SecretSearchNone,
                        IntPtr.Zero,                    // cancellable
                        out var error,
                        _attrKeyTypeName, keyTypePtr,   // attribute NAME, attribute VALUE
                        IntPtr.Zero);                   // varargs terminator

                    if (error != IntPtr.Zero)
                    {
                        _gErrorFree?.Invoke(error);
                        if (list != IntPtr.Zero) FreeList(list);
                        return RawSearchOutcome.Failed;
                    }

                    if (list == IntPtr.Zero) return RawSearchOutcome.Empty;

                    FreeList(list);
                    return RawSearchOutcome.Found;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypePtr);
                }
            }
            catch
            {
                return RawSearchOutcome.Failed;
            }
        }

        private static void FreeList(IntPtr list)
        {
            try
            {
                if (_gListFreeFull is not null && _gObjectUnrefPtr != IntPtr.Zero)
                    _gListFreeFull(list, _gObjectUnrefPtr);
                else
                    _gListFree?.Invoke(list);
            }
            catch { /* a leaked list must not take the probe down */ }
        }

        /// <summary>G-02b: does the keyring hold any secret filed under this keyType, asked without a
        /// keyId and without the Secret Service being required to unlock anything? Unknown whenever
        /// the search is unavailable, unvalidated, or reported an error — never Absent on a backend
        /// we could not actually interrogate.
        ///
        /// <para>This is the SCHEMA-SCOPED search: it uses the <c>com.milkandgin.syxx</c> schema and
        /// therefore finds only secrets stored through our P/Invoke backend. Secrets stored by
        /// <c>secret-tool</c> (which uses <c>org.freedesktop.Secret.Generic</c>) are invisible to
        /// this search. For a search that finds both, use <see cref="SearchForKeyType"/>.</para>
        /// </summary>
        internal static SecretExistence Exists(string keyType)
        {
            if (!IsAvailable || _searchFunc is null) return SecretExistence.Unknown;
            if (!SearchCapabilityUsable()) return SecretExistence.Unknown;

            return RawSearch(keyType) switch
            {
                RawSearchOutcome.Found => SecretExistence.Present,
                RawSearchOutcome.Empty => SecretExistence.Absent,
                _ => SecretExistence.Unknown,
            };
        }

        /// <summary>
        /// G-16 + G-02b: searches the keyring for any secret carrying
        /// <c>syxx-key-type = <paramref name="keyType"/></c>, regardless of which schema name it was
        /// stored under. Uses <c>SECRET_SCHEMA_DONT_MATCH_NAME</c> so that both P/Invoke-stored
        /// secrets (<c>com.milkandgin.syxx</c>) and secret-tool-stored secrets
        /// (<c>org.freedesktop.Secret.Generic</c>) are visible.
        ///
        /// <para>WHY DONT_MATCH_NAME IS NEEDED. <c>secret-tool store</c> files items under the
        /// freedesktop standard schema <c>org.freedesktop.Secret.Generic</c>. Our P/Invoke backend
        /// files them under <c>com.milkandgin.syxx</c>. A schema-scoped search (the existing
        /// <see cref="Exists"/>) matches only one. On a machine where the witness secret was stored
        /// by secret-tool and the token file was subsequently lost, the scoped search returns empty
        /// and the mint guard fires — overwriting a live identity. The DONT_MATCH_NAME flag tells
        /// libsecret to ignore the schema name and match on attributes alone, closing that gap.</para>
        ///
        /// <para>WHY THE GLIST WALK. An unscoped search (no attribute filter in varargs) would
        /// return ALL secrets in the collection — witness, licence, and anything else any application
        /// ever stored. The <c>syxx-key-type</c> attribute filter in the varargs narrows it to Syxx
        /// secrets, but the client-side walk verifies each result's <c>syxx-key-type</c> attribute
        /// matches the requested keyType. Without this check, a machine holding only a licence
        /// secret would incorrectly report <see cref="SecretExistence.Present"/> for a witness
        /// probe, blocking first-run minting.</para>
        ///
        /// <para>MEMORY CONTRACT. <c>secret_password_search_sync</c> returns a <c>GList</c> of
        /// <c>SecretRetrievable*</c> (transfer full, elements transfer full). Each element is a
        /// GObject that must be <c>g_object_unref</c>'d. The list itself is freed with
        /// <c>g_list_free</c>. <c>secret_retrievable_get_attributes</c> returns a
        /// <c>(transfer full)</c> <c>GHashTable</c> that must be <c>g_hash_table_unref</c>'d.
        /// The hash table's keys and values are owned by the retrievable and must NOT be freed.
        /// Every exit path — normal, early return, exception — cleans up in a <c>finally</c>.</para>
        ///
        /// <para>SEARCH FLAGS. <c>SECRET_SEARCH_NONE</c> (0): does NOT request unlock and does NOT
        /// load secret values. A locked but populated collection still answers
        /// <see cref="SecretExistence.Present"/> — which is exactly the case this probe must not
        /// mistake for empty.</para>
        /// </summary>
        internal static SecretExistence SearchForKeyType(string keyType)
        {
            // Guard: all four bindings needed for the walk must be resolvable.
            if (!IsAvailable || _searchUnscopedFunc is null) return SecretExistence.Unknown;
            if (!SearchCapabilityUsable()) return SecretExistence.Unknown;
            if (_retrievableGetAttrs is null || _gHashTableLookup is null
                || _gListLength is null || _gListNthData is null)
                return SecretExistence.Unknown;

            IntPtr list = IntPtr.Zero;
            try
            {
                list = _searchUnscopedFunc(
                    _searchSchemaPtr,       // DONT_MATCH_NAME schema
                    SecretSearchNone,       // no unlock, no value load
                    IntPtr.Zero,            // cancellable: NULL (synchronous)
                    out var error,
                    IntPtr.Zero);           // varargs terminator — unscoped search

                if (error != IntPtr.Zero)
                {
                    _gErrorFree?.Invoke(error);
                    return SecretExistence.Unknown;
                }

                if (list == IntPtr.Zero)
                    return SecretExistence.Absent;

                // Walk the GList. For each SecretRetrievable, inspect its attributes to determine
                // whether it carries the requested keyType. The search returned ALL items matching
                // the DONT_MATCH_NAME schema (i.e., all items with our declared attributes), so the
                // walk MUST filter by the syxx-key-type value to avoid conflating witness with
                // licence secrets.
                uint count = _gListLength(list);
                if (count == 0)
                    return SecretExistence.Absent;

                var keyTypeNamePtr = Marshal.StringToCoTaskMemUTF8(AttrKeyType);
                try
                {
                    bool found = false;
                    for (uint i = 0; i < count; i++)
                    {
                        IntPtr retrievable = _gListNthData(list, i);
                        if (retrievable == IntPtr.Zero) continue;

                        IntPtr attrs = IntPtr.Zero;
                        try
                        {
                            attrs = _retrievableGetAttrs(retrievable);
                            if (attrs == IntPtr.Zero) continue;

                            IntPtr valuePtr = _gHashTableLookup(attrs, keyTypeNamePtr);
                            if (valuePtr == IntPtr.Zero) continue;

                            var value = Marshal.PtrToStringUTF8(valuePtr);
                            if (string.Equals(value, keyType, StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }
                        finally
                        {
                            // secret_retrievable_get_attributes returns (transfer full) — must unref.
                            if (attrs != IntPtr.Zero)
                                _gHashTableUnref?.Invoke(attrs);
                        }
                    }

                    return found ? SecretExistence.Present : SecretExistence.Absent;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(keyTypeNamePtr);
                }
            }
            catch
            {
                return SecretExistence.Unknown;
            }
            finally
            {
                // Free the GList and all its SecretRetrievable elements. FreeList uses
                // g_list_free_full(list, g_object_unref) when both are available, which unrefs every
                // element and frees the list structure in one call. Falls back to g_list_free (list
                // structure only) if g_object_unref was not resolvable — elements leak, but the
                // probe does not crash.
                if (list != IntPtr.Zero)
                    FreeList(list);
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
                    // ASSUMPTION, and the ONE place this probe could manufacture a false Absent — the
                    // worst outcome in this subsystem, since Absent is what licenses minting over an
                    // identity. It reads "the process ran, the service answered, it printed no matches"
                    // as proof of absence. That holds if a locked-but-populated keyring can never
                    // produce the same signature (exit 0, empty stdout) — libsecret's search returns
                    // locked items too, and secret-tool's search asks to unlock and load values, so a
                    // locked collection should fail loudly rather than answer emptily. UNVERIFIED
                    // against a live keyring: there is none on the authoring host. It costs less than
                    // it did, because LibSecretNative.Exists now answers for the same keyring without
                    // asking it to unlock, and Present from any backend wins. It still matters, and it
                    // is the FIRST item for the real-keyring CI leg: populate the keyring, lock it,
                    // and assert this returns Present or Unknown — never Absent.
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
