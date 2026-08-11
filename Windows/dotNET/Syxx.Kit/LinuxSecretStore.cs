using System.Runtime.InteropServices;
using System.Security.Cryptography;

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
public sealed partial class LinuxSecretStore : ISecretStore
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

}
