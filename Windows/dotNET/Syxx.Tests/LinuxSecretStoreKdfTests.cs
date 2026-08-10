using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// P-04 — the four defects in the AES-GCM file store's key derivation and file handling, and §8's
/// replacement of the missing user secret.
///
/// <para>WHY THIS FILE IS IN Syxx.Tests AND NOT THE AVALONIA SUITE. It needs the <c>internal</c>
/// directory seam on <see cref="LinuxSecretStore"/>, and only <c>Syxx.Tests</c> holds
/// <c>InternalsVisibleTo</c> on <c>Syxx.Kit</c>. The file store IS the Linux path — Linux takes
/// <c>Syxx.Kit</c> by ProjectReference — so a test here is a test of Linux behaviour.</para>
///
/// <para>FILESYSTEM SAFETY. Every store here is constructed with a per-test temp directory through
/// the instance seam, so none of them can reach a real user's <c>~/.syxx/keystore</c> and none of
/// them can clobber another test class that uses the process-wide seam.</para>
/// </summary>
[CollectionDefinition("KeystoreKdf", DisableParallelization = true)]
public sealed class KeystoreKdfCollection { }

[Collection("KeystoreKdf")]
public sealed class LinuxSecretStoreKdfTests : IDisposable
{
    private const string KeyType = "kdf-test";
    private readonly string _dir;

    public LinuxSecretStoreKdfTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "syxx-kdf-tests-" + Guid.NewGuid().ToString("N"));
        KeystorePassphraseSession.SetProvider(null);
    }

    public void Dispose()
    {
        KeystorePassphraseSession.SetProvider(null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private LinuxSecretStore NewStore(string? dir = null) =>
        (LinuxSecretStore)Activator.CreateInstance(
            typeof(LinuxSecretStore),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: new object[] { dir ?? _dir },
            culture: null)!;

    private string EntryPath(string keyId) => Path.Combine(_dir, $"{KeyType}_{keyId}");

    private sealed class FixedPassphrase : IKeystorePassphraseProvider
    {
        private readonly string? _value;
        public FixedPassphrase(string? value) => _value = value;
        public string? TryGetPassphrase() => _value;
    }

    // ── P-04 defect 3: the write race and the world-listable directory ────────────

    /// <summary>Fail state, and it was permanent: the directory was created by
    /// <c>Directory.CreateDirectory</c> with no mode, so <c>~/.syxx/keystore</c> sat at the umask
    /// default (usually 0755) forever — every entry in it listable by every user on the box.</summary>
    [Fact]
    public void StoreDirectory_IsCreatedOwnerOnly()
    {
        if (OperatingSystem.IsWindows()) return;   // Unix modes only

        NewStore().Store(KeyType, "dirmode", "x");

        var mode = File.GetUnixFileMode(_dir);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, mode);
    }

    /// <summary>Fail state: <c>File.Create</c> wrote the ciphertext and only THEN chmod'd, so the
    /// blob existed world-readable for the span of the write — and once captured it needed no secret
    /// to decrypt. This asserts the end state; the window itself is closed structurally, by passing
    /// the mode to <c>open(2)</c> and renaming into place rather than chmod-ing after the fact.</summary>
    [Fact]
    public void StoredEntry_IsOwnerReadWriteOnly()
    {
        if (OperatingSystem.IsWindows()) return;   // Unix modes only

        NewStore().Store(KeyType, "filemode", "x");

        var mode = File.GetUnixFileMode(EntryPath("filemode"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    /// <summary>The salt file holds no secret, but it is in the same directory and must not be the
    /// one loose thread. Fail state: a 0644 sidecar next to 0600 entries.</summary>
    [Fact]
    public void SaltFile_IsOwnerReadWriteOnly()
    {
        if (OperatingSystem.IsWindows()) return;   // Unix modes only

        NewStore().Store(KeyType, "saltmode", "x");

        var salt = Path.Combine(_dir, ".kdf-salt");
        Assert.True(File.Exists(salt));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(salt));
    }

    /// <summary>A half-written temp file must never be left behind to be mistaken for an entry.</summary>
    [Fact]
    public void NoTemporaryFilesSurviveAWrite()
    {
        NewStore().Store(KeyType, "tmpcheck", "x");
        Assert.Empty(Directory.EnumerateFiles(_dir, "*.tmp-*"));
    }

    // ── P-04 defect 2: the constant salt ──────────────────────────────────────────

    /// <summary>Fail state, quoted from the code it replaces: the salt was
    /// <c>SHA256("Syxx-AES-256-GCM-Key-v1")</c> — identical on every install on earth. Against a
    /// reconstructible input, 350,000 PBKDF2 iterations bought nothing: one precomputation served
    /// every machine.</summary>
    [Fact]
    public void Salt_IsRandomPerStore_NotTheConstantOne()
    {
        var otherDir = _dir + "-other";
        try
        {
            NewStore().Store(KeyType, "a", "x");
            NewStore(otherDir).Store(KeyType, "a", "x");

            var s1 = File.ReadAllBytes(Path.Combine(_dir, ".kdf-salt"));
            var s2 = File.ReadAllBytes(Path.Combine(otherDir, ".kdf-salt"));

            Assert.Equal(16, s1.Length);
            Assert.NotEqual(s1, s2);

            var legacyConstant = SHA256.HashData(Encoding.UTF8.GetBytes("Syxx-AES-256-GCM-Key-v1"));
            Assert.NotEqual(legacyConstant.Take(16).ToArray(), s1);
        }
        finally
        {
            try { Directory.Delete(otherDir, recursive: true); } catch { }
        }
    }

    /// <summary>The observable consequence of a per-store salt: the same secret under the same
    /// passphrase in two stores must not produce the same key material. Fail state: one
    /// precomputation still covers both.</summary>
    [Fact]
    public void SameSecretInTwoStores_DoesNotShareTheSameSalt()
    {
        var otherDir = _dir + "-other2";
        try
        {
            NewStore().Store(KeyType, "same", "identical-secret");
            NewStore(otherDir).Store(KeyType, "same", "identical-secret");

            var a = File.ReadAllBytes(EntryPath("same"));
            var b = File.ReadAllBytes(Path.Combine(otherDir, $"{KeyType}_same"));

            // Header salt at offset 9 (magic:8 + kdfMode:1).
            Assert.NotEqual(a.Skip(9).Take(16).ToArray(), b.Skip(9).Take(16).ToArray());
        }
        finally
        {
            try { Directory.Delete(otherDir, recursive: true); } catch { }
        }
    }

    // ── P-04 defect 1 / §8: the user secret ───────────────────────────────────────

    /// <summary>With no provider installed the store still works — headless, CI, first run — but the
    /// file records that no user secret took part, and the derivation says so out loud. Fail state:
    /// the weakest mode is indistinguishable from the strongest on disk.</summary>
    [Fact]
    public void WithoutAPassphrase_TheEntryRecordsMachineOnlyWrapping()
    {
        NewStore().Store(KeyType, "nopass", "secret-value");

        var bytes = File.ReadAllBytes(EntryPath("nopass"));
        Assert.Equal("SYXXKS02"u8.ToArray(), bytes.Take(8).ToArray());
        Assert.Equal(1, bytes[8]);   // KdfModeMachineOnly
        Assert.Equal("secret-value", NewStore().Retrieve(KeyType, "nopass"));
    }

    /// <summary>§8: the passphrase is the user secret the derivation entirely lacked. Fail state:
    /// the passphrase is collected and then not actually used to wrap anything.</summary>
    [Fact]
    public void WithAPassphrase_TheEntryRecordsPassphraseWrapping_AndRoundTrips()
    {
        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));

        NewStore().Store(KeyType, "withpass", "secret-value");

        var bytes = File.ReadAllBytes(EntryPath("withpass"));
        Assert.Equal(2, bytes[8]);   // KdfModePassphrase
        Assert.Equal("secret-value", NewStore().Retrieve(KeyType, "withpass"));
    }

    /// <summary>The load-bearing assertion for the whole feature. Fail state: the passphrase is
    /// decoration and the file opens without it.</summary>
    [Fact]
    public void AWrongPassphrase_CannotDecrypt()
    {
        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));
        NewStore().Store(KeyType, "wrongpass", "secret-value");

        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cedar"));
        Assert.Null(NewStore().Retrieve(KeyType, "wrongpass"));

        // And the right one still works afterwards — a failed attempt must not poison the session.
        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));
        Assert.Equal("secret-value", NewStore().Retrieve(KeyType, "wrongpass"));
    }

    /// <summary>Removing the passphrase must not silently open the file. Fail state: an attacker
    /// (or a misconfigured launcher) drops the provider and the machine-only path decrypts a
    /// passphrase-wrapped entry.</summary>
    [Fact]
    public void RemovingThePassphrase_CannotDecryptAPassphraseWrappedEntry()
    {
        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));
        NewStore().Store(KeyType, "dropped", "secret-value");

        KeystorePassphraseSession.SetProvider(null);
        Assert.Null(NewStore().Retrieve(KeyType, "dropped"));
    }

    /// <summary>The KDF mode is inside the AEAD's associated data. Fail state: flipping the header
    /// byte from "passphrase" to "machine only" makes us derive a key the attacker can reproduce
    /// from machine identifiers they already hold — a downgrade splice that costs one byte.</summary>
    [Fact]
    public void DowngradingTheKdfModeByte_DoesNotYieldPlaintext()
    {
        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));
        NewStore().Store(KeyType, "splice", "secret-value");

        var path = EntryPath("splice");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(2, bytes[8]);
        bytes[8] = 1;                       // claim machine-only wrapping
        File.WriteAllBytes(path, bytes);

        KeystorePassphraseSession.SetProvider(null);
        Assert.Null(NewStore().Retrieve(KeyType, "splice"));
    }

    /// <summary>AES-GCM integrity, at the new header offsets. Fail state: a flipped ciphertext byte
    /// yields a silently corrupted secret instead of nothing.</summary>
    [Fact]
    public void TamperedCiphertext_FailsAuthentication()
    {
        NewStore().Store(KeyType, "tamper", "integrity-protected");

        var path = EntryPath("tamper");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Null(NewStore().Retrieve(KeyType, "tamper"));
    }

    // ── Migration: no existing secret is stranded ─────────────────────────────────

    /// <summary>Every Linux secret in the field today is in v1 format. Fail state, and it is the
    /// worst one available in this subsystem: the KDF change strands them, the witness key becomes
    /// unreadable, and every previously sealed testimony stops verifying — C-10's outcome reached by
    /// a different route.</summary>
    [Fact]
    public void LegacyV1Entry_StillDecrypts()
    {
        Directory.CreateDirectory(_dir);
        var path = EntryPath("legacy");
        File.WriteAllBytes(path, WriteLegacyV1Blob("legacy-secret"));

        Assert.Equal("legacy-secret", NewStore().Retrieve(KeyType, "legacy"));
    }

    /// <summary>A v1 entry is upgraded on its next write, so the weak format drains rather than
    /// persisting forever. Fail state: v1 entries stay v1 and the constant salt lives on.</summary>
    [Fact]
    public void LegacyV1Entry_IsRewrittenAsV2OnTheNextStore()
    {
        Directory.CreateDirectory(_dir);
        var path = EntryPath("upgrade");
        File.WriteAllBytes(path, WriteLegacyV1Blob("old-value"));

        NewStore().Store(KeyType, "upgrade", "new-value");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal("SYXXKS02"u8.ToArray(), bytes.Take(8).ToArray());
        Assert.Equal("new-value", NewStore().Retrieve(KeyType, "upgrade"));
    }

    /// <summary>Reproduces the pre-change on-disk format exactly: <c>[nonce:12][tag:16][ct]</c>,
    /// AES-256-GCM under PBKDF2-350k over machine material with the constant salt. Kept in the test
    /// rather than the product so the legacy derivation cannot be reached by anything but the legacy
    /// READ path.</summary>
    private static byte[] WriteLegacyV1Blob(string secret)
    {
        var material = new StringBuilder();
        try { material.Append(File.ReadAllText("/etc/machine-id").Trim()); } catch { }
        try { material.Append(File.ReadAllText("/sys/class/dmi/id/product_uuid").Trim()); } catch { }
        material.Append(Environment.MachineName);

        var salt = SHA256.HashData(Encoding.UTF8.GetBytes("Syxx-AES-256-GCM-Key-v1"));
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(material.ToString()), salt, 350000, HashAlgorithmName.SHA256, 32);

        var plaintext = Encoding.UTF8.GetBytes(secret);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag,
            Encoding.UTF8.GetBytes("syxx-keystore-v1:" + KeyType));

        var blob = new byte[12 + 16 + ciphertext.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, 12);
        ciphertext.CopyTo(blob, 28);
        return blob;
    }

    // ── P-04 defect 4: silent degradation ─────────────────────────────────────────

    /// <summary>Fail state, quoted from the code it replaces: three <c>try{}catch{}</c> blocks with
    /// no breadcrumb. <c>product_uuid</c> is mode 0400, so it ALWAYS fails unprivileged and is absent
    /// outright on most arm64; if <c>/etc/machine-id</c> also fails the key is derived from a bare
    /// hostname with nothing anywhere reporting it. Silent degradation of a security primitive is a
    /// fabricated assurance.</summary>
    [Fact]
    public void EveryDegradationOfTheKeyMaterialIsBreadcrumbed()
    {
        var sink = new RecordingSink();
        SyxxDiagnostics.Install(sink);
        try
        {
            NewStore().Store(KeyType, "degrade", "x");

            var messages = sink.Messages();

            // No passphrase was installed, so that must be said.
            Assert.Contains(messages, m =>
                m.Contains("WITHOUT a user passphrase", StringComparison.OrdinalIgnoreCase));

            // product_uuid is unreadable on every host this suite runs on (mode 0400 on Linux,
            // nonexistent on macOS), so its loss must be reported rather than swallowed.
            Assert.Contains(messages, m =>
                m.Contains("product_uuid", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            SyxxDiagnostics.Uninstall();
        }
    }

    /// <summary>C-11, at the store boundary. Fail state: the passphrase leaves the machine through
    /// the telemetry pipe the user consented to for scan failures.</summary>
    [Fact]
    public void ThePassphraseNeverReachesSyxxDiagnostics()
    {
        const string phrase = "yodel wispy zebra";
        var sink = new RecordingSink();
        SyxxDiagnostics.Install(sink);
        try
        {
            KeystorePassphraseSession.SetProvider(new FixedPassphrase(phrase));
            var store = NewStore();
            store.Store(KeyType, "leakcheck", "secret-value");
            store.Retrieve(KeyType, "leakcheck");
            store.Exists(KeyType);

            // A wrong-passphrase failure is the most likely place for a "helpful" diagnostic to
            // start echoing what was typed, so drive that too.
            KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid acid acid"));
            NewStore().Retrieve(KeyType, "leakcheck");

            var everything = sink.Everything();
            Assert.DoesNotContain(phrase, everything, StringComparison.OrdinalIgnoreCase);
            foreach (var word in phrase.Split(' '))
                Assert.DoesNotContain(word, everything, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SyxxDiagnostics.Uninstall();
        }
    }

    // ── §8 point 6: session cadence ───────────────────────────────────────────────

    /// <summary>The derived key is held for a session and zeroed on idle; the passphrase itself is
    /// never retained. Fail state: the derived key lives for the life of the process, so locking the
    /// screen protects nothing.</summary>
    [Fact]
    public void TheDerivedKeyIsDroppedOnIdleTimeout()
    {
        var original = KeystorePassphraseSession.IdleTimeout;
        try
        {
            KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));
            NewStore().Store(KeyType, "idle", "secret-value");
            Assert.True(KeystorePassphraseSession.HasCachedKey);

            KeystorePassphraseSession.IdleTimeout = TimeSpan.Zero;
            Assert.False(KeystorePassphraseSession.HasCachedKey);

            // And it re-derives rather than failing.
            KeystorePassphraseSession.IdleTimeout = original;
            Assert.Equal("secret-value", NewStore().Retrieve(KeyType, "idle"));
        }
        finally
        {
            KeystorePassphraseSession.IdleTimeout = original;
        }
    }

    [Fact]
    public void ClearingTheSessionDropsTheDerivedKey()
    {
        KeystorePassphraseSession.SetProvider(new FixedPassphrase("acid zoom cider"));
        NewStore().Store(KeyType, "clearme", "secret-value");
        Assert.True(KeystorePassphraseSession.HasCachedKey);

        KeystorePassphraseSession.Clear();
        Assert.False(KeystorePassphraseSession.HasCachedKey);
    }

    // ── Existence probe still behaves ─────────────────────────────────────────────

    /// <summary>The file store answers existence without any service, which is why it is asked
    /// first. Fail state: a probe that needs the keyring cannot answer when the keyring is the thing
    /// that is missing.</summary>
    [Fact]
    public void FileStore_AnswersExistenceForItsOwnEntries()
    {
        var store = NewStore();
        Assert.Equal(SecretExistence.Absent, store.Exists(KeyType));

        store.Store(KeyType, "present", "x");
        Assert.Equal(SecretExistence.Present, store.Exists(KeyType));
    }

    /// <summary>The salt sidecar must not be mistaken for a secret. Fail state: a store that has
    /// only ever written its salt reports Present, and the mint guard refuses a first identity.</summary>
    [Fact]
    public void TheSaltFileIsNotMistakenForAnEntry()
    {
        var store = NewStore();
        store.Store(KeyType, "x", "y");
        store.Delete(KeyType, "x");

        Assert.True(File.Exists(Path.Combine(_dir, ".kdf-salt")));
        Assert.Equal(SecretExistence.Absent, store.Exists(KeyType));
    }

    internal sealed class RecordingSink : IDiagnosticSink
    {
        private readonly List<string> _lines = new();
        private readonly List<string> _messages = new();

        public void Capture(DiagnosticEvent evt)
        {
            lock (_lines)
            {
                _lines.Add(evt.ErrorClass);
                _lines.Add(evt.Component);
                _lines.Add(evt.Underlying ?? string.Empty);
                _lines.AddRange(evt.Fingerprint);
                if (evt.Tags is not null) _lines.AddRange(evt.Tags.Select(kv => kv.Key + "=" + kv.Value));
                if (evt.Extra is not null) _lines.AddRange(evt.Extra.Select(kv => kv.Key + "=" + kv.Value));
            }
        }

        public void Breadcrumb(string category, string message, DiagnosticSeverity level,
                               IReadOnlyDictionary<string, string> data)
        {
            lock (_lines)
            {
                _messages.Add(message);
                _lines.Add(category);
                _lines.Add(message);
                _lines.AddRange(data.Select(kv => kv.Key + "=" + kv.Value));
            }
        }

        public void DiscardBuffered() { }

        public IReadOnlyList<string> Messages() { lock (_lines) return _messages.ToList(); }
        public string Everything() { lock (_lines) return string.Join('\n', _lines); }
    }
}
