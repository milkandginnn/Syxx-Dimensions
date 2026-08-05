namespace Syxx.Kit;

/// <summary>
/// What a store established about whether ANY secret of a given keyType is held — the answer to
/// <see cref="ISecretStore.Exists"/>. Four states, because the two ways of "not knowing" lead to
/// different caller decisions and collapsing them is how G-02 survived:
///   <see cref="Present"/>     — at least one secret of this keyType is held. Established, not inferred.
///   <see cref="Absent"/>      — the store looked at every backend it has and positively established
///                               that none is held. This is the only answer that licenses a caller to
///                               mint a replacement identity.
///   <see cref="Unknown"/>     — a backend that may be holding one could not be interrogated (locked
///                               keyring, IO error, no enumeration API for what it wrote). NOT "none".
///   <see cref="Unsupported"/> — this store does not implement the probe at all, so it carries no
///                               evidence in either direction. Distinct from <see cref="Unknown"/>:
///                               nothing was attempted, so there is no outage to report or retry.
/// </summary>
public enum SecretExistence
{
    Present,
    Absent,
    Unknown,
    Unsupported
}

/// <summary>
/// Platform-abstraction for secret storage. Each implementation binds credentials to the
/// current user/machine using the platform's native secure storage:
///
///   Windows – DPAPI (CryptProtectData / CryptUnprotectData) with AES-256-GCM at rest
///             under the current Windows user account, storing cipher blobs in
///             <c>~/.syxx/keystore/</c>.
///
///   Linux   – libsecret (GNOME Keyring / KDE Wallet) via P/Invoke, falling back to an
///             AES-256-GCM encrypted file store at <c>~/.syxx/keystore/</c> with a key
///             derived from machine-specific data via PBKDF2.
///
/// Design contract (AGENTS.md TRI-STATE, NEVER BOOLEAN):
///   Store   – best-effort. A failure to persist (keyring unavailable, disk full) is silent;
///             the caller must verify via Retrieve. <see cref="WitnessKeyStore.SaveKey"/> does
///             exactly that before it writes its reference token (G-01).
///   Retrieve – returns null when the secret is missing, unreadable, or tampered. The caller
///             distinguishes "never stored" from "unreachable" by checking the oracle that
///             produced the keyId.
///   Exists  – tri-state (plus "cannot answer") probe over a whole keyType, for the case where the
///             oracle holding the keyId has itself been lost. Never throws.
///   Delete  – best-effort. Never throws.
/// </summary>
public interface ISecretStore
{
    /// <summary>Persists <paramref name="secret"/> under the composite key
    /// (<paramref name="keyType"/>, <paramref name="keyId"/>).</summary>
    void Store(string keyType, string keyId, string secret);

    /// <summary>Retrieves a secret previously stored with <see cref="Store"/>.
    /// Returns <c>null</c> on any failure (missing, corrupt, tampered, unreachable).</summary>
    string? Retrieve(string keyType, string keyId);

    /// <summary>Removes a stored secret. Best-effort; never throws.</summary>
    void Delete(string keyType, string keyId);

    /// <summary>
    /// Whether ANY secret is held under <paramref name="keyType"/>, without knowing a keyId.
    ///
    /// Exists for the case where the record holding the keyId is gone: <see cref="WitnessKeyStore"/>
    /// keeps its keyId only in <c>witness.key</c>, under a different root from the secret itself, so
    /// losing that file leaves <see cref="Retrieve"/> with nothing to ask for while the identity is
    /// still sitting in the store. Asked before minting a replacement key, this is what turns "the
    /// token file is missing" into either proven absence or an honest Undetermined (G-02).
    ///
    /// Implementations must never throw and must never report <see cref="SecretExistence.Absent"/>
    /// for a backend they could not actually inspect — return <see cref="SecretExistence.Unknown"/>
    /// there. Absent is a claim; the caller is licensed to mint over an identity on the strength
    /// of it.
    ///
    /// The default implementation returns <see cref="SecretExistence.Unsupported"/>: a store that has
    /// not implemented the probe says so, rather than reporting an emptiness it never checked. Callers
    /// must treat Unsupported as "no evidence", not as Absent.
    /// </summary>
    SecretExistence Exists(string keyType) => SecretExistence.Unsupported;
}
