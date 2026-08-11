using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace Syxx.Kit;

public sealed partial class LinuxSecretStore
{
    // ── Key derivation ────────────────────────────────────────────────────────────

    /// <summary>Argon2id cost. One lane is not a choice — libsodium's <c>crypto_pwhash</c> exposes no
    /// parallelism, and NSec rejects any other value. 64 MiB over 3 passes is roughly a fifth of a
    /// second on a desktop and is memory-hard enough that a GPU attacker loses most of its advantage.
    /// It buys two to four orders of magnitude, which is worth having and is NOT enough on its own to
    /// rescue a low-entropy passphrase — see <see cref="DicePassphrase.EntropyBits"/>, which is why
    /// that number is displayed rather than implied.</summary>
    private const int Argon2MemoryKiB = 64 * 1024;
    private const int Argon2Passes = 3;

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
                MemorySize = Argon2MemoryKiB,
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
}
