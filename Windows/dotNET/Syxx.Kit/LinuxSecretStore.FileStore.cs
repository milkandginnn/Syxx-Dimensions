using System.Security.Cryptography;
using System.Text;

namespace Syxx.Kit;

public sealed partial class LinuxSecretStore
{
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
}
