using System;
using System.Collections.Generic;
using System.IO;
using NSec.Cryptography;

namespace Syxx.Kit
{
    /// <summary>
    /// WARP_MANIFEST_V1 §7 — out-of-band pinned trust set for the signed content-integrity manifest.
    ///
    /// Twin of macOS <c>WarpManifestPinning</c> and a sibling of <see cref="WitnessPinning"/>: the
    /// trusted signing key(s) are loaded OUT OF BAND (env var + a persisted key-file), NEVER from
    /// <c>/api/pubkey</c> at runtime. <c>/api/pubkey</c> travels the same channel an attacker may
    /// control, so it can never be the trust root — a valid signature by a non-pinned key is
    /// <c>UntrustedSigner</c> (→ INCONCLUSIVE), never a match (the D011 rule already enforced for
    /// Witness/Glutton: valid-but-untrusted must not render green).
    ///
    /// A pinned entry binds a <c>key_id</c> (the server's <c>leak.collector_key_id()</c> — base64 of
    /// the raw public key's first 8 bytes) to the raw Ed25519 public-key bytes verified against.
    ///
    /// OWNER DELIVERABLE: distribute/bake the pinned manifest key(s) into clients exactly as Witness
    /// keys are. Nothing is hardcoded here.
    /// </summary>
    public static class WarpManifestPinning
    {
        private const string EnvKey = "SYXX_WARP_MANIFEST_KEYS";

        /// <summary>
        /// Returns the pinned trust set as <c>key_id -&gt; raw Ed25519 public-key bytes</c>. Sources,
        /// in order: the <c>SYXX_WARP_MANIFEST_KEYS</c> environment variable, else a persisted key
        /// file at <c>%LocalAppData%/Syxx/warp_manifest_trusted_keys.txt</c>. An EMPTY map means no
        /// trust root is configured — every signer is then <c>UntrustedSigner</c> (INCONCLUSIVE),
        /// never trusted.
        /// </summary>
        public static IReadOnlyDictionary<string, byte[]> ConfiguredTrustedManifestKeys()
        {
            string raw = Environment.GetEnvironmentVariable(EnvKey) ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
            {
                raw = PersistedTrustedManifestKeys() ?? string.Empty;
            }
            return Parse(raw);
        }

        /// <summary>Returns the persisted trusted-key file contents, or null if none exists.</summary>
        public static string? PersistedTrustedManifestKeys()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var keyFile = Path.Combine(appData, "Syxx", "warp_manifest_trusted_keys.txt");
                return File.Exists(keyFile) ? File.ReadAllText(keyFile) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Pure parser (testable, no I/O). Accepts, comma/space/newline/tab separated:
        /// <list type="bullet">
        /// <item>a 64-char lowercase-hex <c>key_id</c> (the raw 32-byte Ed25519 public key,
        /// hex-encoded) — the canonical corpus/production form; the public key IS
        /// <c>hexdecode(key_id)</c>.</item>
        /// <item><c>keyid=base64pubkey</c> / <c>keyid:base64pubkey</c> — explicit pin (key_id
        /// verbatim, base64 key).</item>
        /// <item><c>base64pubkey</c> — a bare raw public key; its key_id is DERIVED as the lowercase
        /// hex of the raw public key, matching the server's <c>key_id</c> scheme.</item>
        /// </list>
        /// Entries whose public-key component is not a valid 32-byte Ed25519 key are dropped
        /// (fail-closed: an unusable pin is not a trust anchor).
        /// </summary>
        public static IReadOnlyDictionary<string, byte[]> Parse(string raw)
        {
            var outMap = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) return outMap;
            var parts = raw.Split(new[] { ',', ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in parts)
            {
                int sep = token.IndexOfAny(new[] { '=', ':' });
                if (sep >= 0)
                {
                    string keyId = token.Substring(0, sep);
                    var pub = ValidPubKeyFromBase64(token.Substring(sep + 1));
                    if (pub != null) outMap[keyId] = pub;
                    continue;
                }

                // Bare 64-char hex key_id (the public key hex-encoded).
                var fromHex = ValidPubKeyFromHex(token);
                if (fromHex != null) { outMap[token.ToLowerInvariant()] = fromHex; continue; }

                // Bare base64 public key → derive key_id from the raw bytes.
                var fromB64 = ValidPubKeyFromBase64(token);
                if (fromB64 != null) outMap[DeriveKeyId(fromB64)] = fromB64;
            }
            return outMap;
        }

        /// <summary>The server's <c>key_id</c> derivation: lowercase hex of the raw 32-byte Ed25519 public key.</summary>
        public static string DeriveKeyId(byte[] rawPublicKey)
            => Convert.ToHexString(rawPublicKey).ToLowerInvariant();

        private static byte[]? ValidPubKeyFromBase64(string token)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(token); }
            catch { return null; }
            return ValidPubKey(bytes);
        }

        private static byte[]? ValidPubKeyFromHex(string token)
        {
            if (token.Length != 64) return null;
            byte[] bytes;
            try { bytes = Convert.FromHexString(token); }
            catch { return null; }
            return ValidPubKey(bytes);
        }

        private static byte[]? ValidPubKey(byte[] bytes)
        {
            if (bytes.Length != 32) return null;
            try { _ = PublicKey.Import(SignatureAlgorithm.Ed25519, bytes, KeyBlobFormat.RawPublicKey); }
            catch { return null; }
            return bytes;
        }
    }
}
