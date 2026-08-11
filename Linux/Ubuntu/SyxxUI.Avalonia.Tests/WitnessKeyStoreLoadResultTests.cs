using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// Coverage for the three decisions <see cref="WitnessKeyStore"/> makes about a persisted identity:
    /// whether a key is <see cref="KeyPresence.Present"/>, positively
    /// <see cref="KeyPresence.Absent"/>, or <see cref="KeyPresence.Undetermined"/>, and whether
    /// <see cref="WitnessKeyStore.SaveKey"/> may report success. The caller mints a new identity on
    /// Absent and refuses on Undetermined, so every collapse of the second into the first destroys a
    /// signing identity and silently invalidates every testimony sealed before it.
    ///
    /// <para>G-05, why this file was rewritten: the presence switch had ZERO coverage. The tests that
    /// claimed it injected a stub past the code under test, and the tests here could not drive the
    /// states without writing the developer's real <c>~/.local/share/Syxx/witness.key</c> and
    /// restoring it afterwards. Both are gone, and the states are driven through the real code path
    /// rather than around it.</para>
    ///
    /// <para>FILESYSTEM SAFETY, stated exactly. Two test-only seams redirect the two places this code
    /// persists anything: <c>WitnessKeyStore.SetKeyPathForTesting</c> for the token file and
    /// <c>LinuxSecretStore.SetFallbackDirectoryForTesting</c> for the AES-GCM file store. Both point
    /// into a per-test temp directory and both are restored in <see cref="Dispose"/>, so no test here
    /// touches the real <c>~/.local/share/Syxx/witness.key</c> or the real <c>~/.syxx/keystore</c>, and
    /// a killed process cannot leave an orphan in a user's store. Nothing in this file reaches a real
    /// OS keyring either: the only test that instantiates a real <see cref="LinuxSecretStore"/> asserts
    /// on the file-store backend, and never uses the <c>witness</c> keyType.</para>
    /// </summary>
    public sealed class WitnessKeyStoreLoadResultTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _keyPath;

        public WitnessKeyStoreLoadResultTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), "syxx-witness-keystore-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _keyPath = Path.Combine(_tempDir, "witness.key");
            WitnessKeyStore.SetKeyPathForTesting(_keyPath);
            // The AES-GCM file store is the second thing this code persists to. Redirect it as well,
            // so LinuxSecretStore_Exists_... cannot write into a real user's ~/.syxx/keystore.
            LinuxSecretStore.SetFallbackDirectoryForTesting(Path.Combine(_tempDir, "keystore"));
        }

        public void Dispose()
        {
            WitnessKeyStore.SetKeyPathForTesting(null);
            LinuxSecretStore.SetFallbackDirectoryForTesting(null);
            // The injected store is static too. Hand the real platform store back rather than leaving
            // a fake installed for whatever test class runs next (the leak G-05 flagged).
            WitnessKeyStore.SetSecretStore(PlatformSecretStore.Create());
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }

        // ── Present ────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void LoadKeyResult_TokenPlusRetrievableKey_IsPresentWithTheKey()
        {
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);
            WitnessKeyStore.SaveKey(priv);   // writes the token file; the fake store holds the secret

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Present, result.Presence);
            Assert.Equal(priv, result.Key);
            Assert.Equal(priv, WitnessKeyStore.LoadKey());   // legacy surface still returns the key
            Assert.True(WitnessKeyStore.HasKey);
        }

        // ── Absent — the only state that licenses minting a new identity ───────────────────────────

        [Fact]
        public void LoadKeyResult_NoTokenFileAndStoreProvesNoSecret_IsAbsent()
        {
            // The mint path. Both oracles agree nothing was ever established here, so a first run
            // must still be able to create an identity — a fix for G-02 that blocked this would make
            // the Witness toolset unusable on a fresh install.
            Assert.False(File.Exists(_keyPath));
            WitnessKeyStore.SetSecretStore(new FakeStore());   // empty store answers Absent

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Absent, result.Presence);
            Assert.Null(result.Key);
            Assert.Null(WitnessKeyStore.LoadKey());
            Assert.False(WitnessKeyStore.HasKey);
        }

        [Fact]
        public void LoadKeyResult_EmptyTokenFileAndStoreProvesNoSecret_IsAbsent()
        {
            // An interrupted/blank write with nothing in the store behind it: still safe to mint.
            File.WriteAllBytes(_keyPath, Array.Empty<byte>());
            WitnessKeyStore.SetSecretStore(new FakeStore());

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Absent, result.Presence);
            Assert.False(WitnessKeyStore.HasKey);
        }

        // ── G-02: a lost token file is not proof the identity is gone ─────────────────────────────

        [Fact]
        public void LoadKeyResult_NoTokenFileButStoreHoldsAWitnessSecret_IsUndetermined_NeverAbsent()
        {
            // G-02 REGRESSION GUARD, and the case the plan names "missing token file + populated store
            // must NOT mint". The token and the secret resolve from independent roots
            // (LocalApplicationData vs the keyring / $HOME/.syxx/keystore), so XDG divergence between a
            // desktop, snap/flatpak and plain-shell launch loses the token while the secret survives.
            // Reporting Absent here mints a second identity under a fresh GUID and every testimony
            // sealed before it starts verifying against a different public key — with a successful seal
            // on screen and nothing else.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);
            WitnessKeyStore.SaveKey(priv);         // identity genuinely established

            File.Delete(_keyPath);                 // ...and now the token file is gone

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Undetermined, result.Presence);
            Assert.Null(result.Key);
            Assert.Equal(SecretExistence.Present, store.Exists("witness"));   // the secret is still there
        }

        [Fact]
        public void LoadKeyResult_EmptyTokenFileButStoreHoldsAWitnessSecret_IsUndetermined()
        {
            // Same reasoning as a missing file: a blank token file is proof about the file, not about
            // the store's contents.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);
            WitnessKeyStore.SaveKey(priv);
            File.WriteAllBytes(_keyPath, Array.Empty<byte>());

            Assert.Equal(KeyPresence.Undetermined, WitnessKeyStore.LoadKeyResult().Presence);
        }

        [Fact]
        public void LoadKeyResult_NoTokenFileAndExistenceProbeThrows_IsUndetermined()
        {
            // The probe itself failed, so we know nothing about the store AND have no token file. The
            // one thing that must not happen is calling that "no key".
            var store = new FakeStore { ThrowOnExists = true };
            WitnessKeyStore.SetSecretStore(store);

            Assert.Equal(KeyPresence.Undetermined, WitnessKeyStore.LoadKeyResult().Presence);
        }

        [Theory]
        [InlineData(SecretExistence.Unknown)]
        [InlineData(SecretExistence.Unsupported)]
        public void LoadKeyResult_NoTokenFileAndStoreCannotAnswer_IsAbsent_ByDocumentedFallback(
            SecretExistence answer)
        {
            // CHARACTERIZATION OF A STATED RESIDUAL, not an endorsement. Neither answer is evidence of
            // absence, and strict tri-state reasoning would say Undetermined. It is Absent because a
            // store that cannot enumerate what it wrote — Linux libsecret via the bound P/Invoke API,
            // see LinuxSecretStore.Exists — would otherwise make first-run minting impossible on every
            // GNOME/KDE desktop and leave no route to a first identity at all. WitnessKeyStore records
            // the fallback as a Warning breadcrumb instead of taking it silently. If a future change
            // gives those stores a real enumeration path, this test is the thing that should be
            // revisited: tighten the behaviour, do not soften the assertion.
            WitnessKeyStore.SetSecretStore(new FakeStore { ExistenceOverride = answer });

            Assert.Equal(KeyPresence.Absent, WitnessKeyStore.LoadKeyResult().Presence);
        }

        [Fact]
        public void LoadKeyResult_StoreWithoutTheExistenceProbe_IsAbsent_AndStillCompiles()
        {
            // The default ISecretStore.Exists implementation reports Unsupported, so a store predating
            // the probe keeps working and keeps its old answer. The assertion that matters here is that
            // this file compiles at all: making Exists a required member would break every existing
            // ISecretStore implementation, including ones this seat does not own.
            WitnessKeyStore.SetSecretStore(new LegacyStore());

            Assert.Equal(KeyPresence.Absent, WitnessKeyStore.LoadKeyResult().Presence);
        }

        // ── Undetermined — a key exists but the store cannot hand it back ─────────────────────────

        [Fact]
        public void LoadKeyResult_TokenPresentButStoreReturnsNull_IsUndetermined()
        {
            // RG-2 REGRESSION GUARD. The token file PROVES a key was established; a null store result
            // means the keyring is merely unreachable right now — the honest answer is Undetermined,
            // never Absent. If this reverts to Absent, the caller mints over the identity again.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);
            WitnessKeyStore.SaveKey(priv);   // identity genuinely persisted
            store.ReturnNull = true;         // now simulate the store going unreachable

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Undetermined, result.Presence);
            Assert.Null(result.Key);
            // The legacy LoadKey() surface is byte-unchanged: Undetermined still reads as null. The
            // tri-state is what the caller consults to avoid overwriting.
            Assert.Null(WitnessKeyStore.LoadKey());
        }

        [Fact]
        public void LoadKeyResult_TokenPresentButRetrieveThrows_IsUndetermined()
        {
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);
            WitnessKeyStore.SaveKey(priv);
            store.ThrowOnRetrieve = true;

            Assert.Equal(KeyPresence.Undetermined, WitnessKeyStore.LoadKeyResult().Presence);
        }

        [Fact]
        public void LoadKeyResult_MalformedNonEmptyFile_IsUndetermined_NeverAbsent()
        {
            // A non-empty file we cannot parse is existing content we must NOT overwrite. On every
            // platform this is Undetermined: off-Windows it is unrecognized content; on Windows the
            // legacy DPAPI decrypt of garbage returns null → also Undetermined.
            File.WriteAllText(_keyPath, "not-a-valid-token-or-blob");
            WitnessKeyStore.SetSecretStore(new FakeStore());

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Undetermined, result.Presence);
            Assert.Null(result.Key);
        }

        [Fact]
        public void LoadKeyResult_TokenPrefixButUnparseableRemainder_IsUndetermined()
        {
            // Starts with '_' but is not a full reference token — unparseable, existing, non-empty.
            File.WriteAllText(_keyPath, "_not_the_ref_prefix");
            WitnessKeyStore.SetSecretStore(new FakeStore());

            Assert.Equal(KeyPresence.Undetermined, WitnessKeyStore.LoadKeyResult().Presence);
        }

        // ── G-02a: an unreadable key path is not an absent key ────────────────────────────────────

        [Fact]
        public void LoadKeyResult_KeyPathCannotBeOpened_IsUndetermined_AndHasKeyStaysTrue()
        {
            // G-02a. File.Exists returns false on permission denial and IO failure exactly as it does
            // for a missing file, so the old code read those errors as "no key was ever established"
            // and the caller minted over the identity. A directory standing where the key file belongs
            // reproduces that class of failure portably: the guarded open throws
            // UnauthorizedAccessException, where File.Exists would simply have said false.
            var asDirectory = Path.Combine(_tempDir, "witness-as-directory");
            Directory.CreateDirectory(asDirectory);
            WitnessKeyStore.SetKeyPathForTesting(asDirectory);
            WitnessKeyStore.SetSecretStore(new FakeStore());

            var result = WitnessKeyStore.LoadKeyResult();

            Assert.Equal(KeyPresence.Undetermined, result.Presence);
            Assert.Null(result.Key);
            // HasKey is a bool and cannot say Undetermined, so it must not claim the key is gone.
            Assert.True(WitnessKeyStore.HasKey);
        }

        [Fact]
        public void HasKey_IsFalseOnlyWhenAbsenceIsProven()
        {
            WitnessKeyStore.SetSecretStore(new FakeStore());

            Assert.False(WitnessKeyStore.HasKey);                       // no file

            File.WriteAllBytes(_keyPath, Array.Empty<byte>());
            Assert.False(WitnessKeyStore.HasKey);                       // empty file

            File.WriteAllText(_keyPath, "__syxx_ls:deadbeef");
            Assert.True(WitnessKeyStore.HasKey);                        // a token is there
        }

        // ── G-01: SaveKey must not report success on an unverified store write ────────────────────

        [Fact]
        public void SaveKey_StoreSilentlyDroppedTheWrite_ThrowsAndWritesNoTokenFile()
        {
            // G-01 REGRESSION GUARD. ISecretStore.Store is best-effort and every Linux backend swallows
            // its own failures, so on a locked keyring the old code wrote a token file pointing at a
            // secret that was never stored: the seal in progress succeeded with a key nothing could
            // ever read back, and every launch after it was permanently Undetermined.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore { DropWrites = true };
            WitnessKeyStore.SetSecretStore(store);

            var ex = Assert.Throws<InvalidOperationException>(() => WitnessKeyStore.SaveKey(priv));

            Assert.False(File.Exists(_keyPath), "no token file may exist for a secret that was not stored");
            Assert.False(WitnessKeyStore.HasKey);
            Assert.Equal(KeyPresence.Absent, WitnessKeyStore.LoadKeyResult().Presence);
            // The message is user-visible through ErrorPresenter.Compose, and it must not invent a
            // cause it has not established.
            Assert.Equal(WitnessKeyStore.StoreWriteUnverifiedMessage, ex.Message);
            foreach (var forbidden in new[] { "disk full", "corrupt", "you " })
            {
                Assert.DoesNotContain(forbidden, ex.Message, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void SaveKey_StoreReturnsSomethingElse_Throws()
        {
            // Not just null: a store that hands back a DIFFERENT value has not persisted this key
            // either, and a seal made with the key we think we saved would verify against nothing.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var (other, _) = WitnessSigner.GenerateKeyPair();
            WitnessKeyStore.SetSecretStore(new FakeStore { SubstituteValue = other });

            Assert.Throws<InvalidOperationException>(() => WitnessKeyStore.SaveKey(priv));
            Assert.False(File.Exists(_keyPath));
        }

        [Fact]
        public void SaveKey_ReadBackThrows_ThrowsAndKeepsTheOriginalException()
        {
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            WitnessKeyStore.SetSecretStore(new FakeStore { ThrowOnRetrieve = true });

            var ex = Assert.Throws<InvalidOperationException>(() => WitnessKeyStore.SaveKey(priv));

            Assert.False(File.Exists(_keyPath));
            // The technical cause is preserved for the diagnostic sink rather than discarded.
            Assert.NotNull(ex.InnerException);
        }

        [Fact]
        public void SaveKey_FailedWrite_LeavesThePreviousIdentityIntact()
        {
            // The property that makes verify-before-token-write worth having: a failed save is a no-op
            // on the established identity, not a half-rotation.
            var (first, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);
            WitnessKeyStore.SaveKey(first);
            var tokenBefore = File.ReadAllBytes(_keyPath);

            var (second, _) = WitnessSigner.GenerateKeyPair();
            store.DropWrites = true;
            Assert.Throws<InvalidOperationException>(() => WitnessKeyStore.SaveKey(second));

            store.DropWrites = false;
            Assert.Equal(tokenBefore, File.ReadAllBytes(_keyPath));
            var result = WitnessKeyStore.LoadKeyResult();
            Assert.Equal(KeyPresence.Present, result.Presence);
            Assert.Equal(first, result.Key);
        }

        [Fact]
        public void SaveKey_FailedWrite_DiscardsTheUnreferencedSecret()
        {
            // No token file points at the GUID that was just minted, so anything left behind is an
            // orphan accumulating in the keyring on every failed save.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore { SubstituteValue = "not-the-key-you-stored" };
            WitnessKeyStore.SetSecretStore(store);

            Assert.Throws<InvalidOperationException>(() => WitnessKeyStore.SaveKey(priv));

            Assert.Single(store.Deleted);
            Assert.Equal(SecretExistence.Absent, store.Exists("witness"));
        }

        [Fact]
        public void SaveKey_VerifiedWrite_WritesTheTokenAndLoadsBack()
        {
            // The control: the read-back must not reject a store that genuinely persisted the key.
            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var store = new FakeStore();
            WitnessKeyStore.SetSecretStore(store);

            WitnessKeyStore.SaveKey(priv);

            Assert.True(File.Exists(_keyPath));
            Assert.StartsWith("__syxx_ls:", File.ReadAllText(_keyPath), StringComparison.Ordinal);
            Assert.Equal(priv, WitnessKeyStore.LoadKey());
            Assert.Empty(store.Deleted);
        }

        // ── The real Linux/fallback store's existence probe ───────────────────────────────────────

        [Fact]
        public void LinuxSecretStore_Exists_NeverClaimsAbsenceItCannotEstablish()
        {
            // Runs against the real store type, with its file backend redirected into this test's temp
            // directory by the ctor — no real ~/.syxx/keystore, no orphan if the process is killed.
            // On this host (no libsecret, no secret-tool) the file store is the only backend, so a
            // keyType never written is definitively Absent. Where a keyring backend IS present the
            // answer must degrade to Unknown and never stay Absent — that is the invariant this asserts,
            // and Present is impossible either way for a fresh GUID keyType.
            var store = new LinuxSecretStore();
            var keyType = "syxxtest-" + Guid.NewGuid().ToString("N");

            var before = store.Exists(keyType);
            Assert.NotEqual(SecretExistence.Present, before);
            Assert.Contains(before, new[] { SecretExistence.Absent, SecretExistence.Unknown });

            try
            {
                // Proves the file-store enumeration is genuinely definitive — that backend is what
                // carries this fix on Windows, on macOS, and on every Linux box whose keyring is locked
                // or absent, which is the common case G-02 is about.
                store.Store(keyType, "id1", "secret-value");
                Assert.Equal(SecretExistence.Present, store.Exists(keyType));
            }
            finally
            {
                store.Delete(keyType, "id1");
            }

            Assert.NotEqual(SecretExistence.Present, store.Exists(keyType));
        }

        // Deliberately NOT added here: an end-to-end "save through the real LinuxSecretStore, delete the
        // token file, assert Undetermined". It passes on this host and on any CI leg without libsecret,
        // and goes RED on a GNOME/KDE developer box — where the secret lands in the keyring, Exists
        // answers Unknown, and the documented fallback returns Absent. Writing it as an if/else over the
        // two environments would assert less than the fake-store tests above already do. The gap it
        // would paper over is the open half of G-02, not something a test can close.

        // ── Fakes ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>In-memory <see cref="ISecretStore"/> with one knob per failure mode a real store
        /// has: <see cref="ReturnNull"/> and <see cref="ThrowOnRetrieve"/> for an unreachable keyring,
        /// <see cref="DropWrites"/> for the silent persist failure of G-01,
        /// <see cref="SubstituteValue"/> for a write that landed as something else, and
        /// <see cref="ExistenceOverride"/> / <see cref="ThrowOnExists"/> for stores that cannot answer
        /// the G-02 existence probe.</summary>
        private sealed class FakeStore : ISecretStore
        {
            private readonly Dictionary<string, string> _m = new();

            public bool ReturnNull;
            public bool ThrowOnRetrieve;
            public bool DropWrites;
            public bool ThrowOnExists;
            public string? SubstituteValue;
            public SecretExistence? ExistenceOverride;

            public List<string> Deleted { get; } = new();

            private static string K(string t, string i) => t + " " + i;

            public void Store(string keyType, string keyId, string secret)
            {
                if (DropWrites) return;   // exactly what LinuxSecretStore does on a locked keyring
                _m[K(keyType, keyId)] = SubstituteValue ?? secret;
            }

            public string? Retrieve(string keyType, string keyId)
            {
                if (ThrowOnRetrieve) throw new InvalidOperationException("store unreachable");
                if (ReturnNull) return null;
                return _m.TryGetValue(K(keyType, keyId), out var v) ? v : null;
            }

            public void Delete(string keyType, string keyId)
            {
                Deleted.Add(K(keyType, keyId));
                _m.Remove(K(keyType, keyId));
            }

            public SecretExistence Exists(string keyType)
            {
                if (ThrowOnExists) throw new InvalidOperationException("store unreachable");
                if (ExistenceOverride is { } forced) return forced;
                return _m.Keys.Any(k => k.StartsWith(keyType + " ", StringComparison.Ordinal))
                    ? SecretExistence.Present
                    : SecretExistence.Absent;
            }
        }

        /// <summary>A store written before the existence probe existed: implements only the original
        /// three members and inherits the default <see cref="SecretExistence.Unsupported"/>.</summary>
        private sealed class LegacyStore : ISecretStore
        {
            public void Store(string keyType, string keyId, string secret) { }
            public string? Retrieve(string keyType, string keyId) => null;
            public void Delete(string keyType, string keyId) { }
        }
    }
}
