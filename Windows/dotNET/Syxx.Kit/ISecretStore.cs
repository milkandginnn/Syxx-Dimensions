namespace Syxx.Kit;

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
///             the caller must verify via Retrieve.
///   Retrieve – returns null when the secret is missing, unreadable, or tampered. The caller
///             distinguishes "never stored" from "unreachable" by checking the oracle that
///             produced the keyId.
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
}
