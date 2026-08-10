using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// <summary>
    /// SPKI certificate-pinning support for the leak-collector transport (D003).
    ///
    /// The pin is the base64-encoded SHA-256 of the leaf certificate's DER-encoded
    /// SubjectPublicKeyInfo — identical across platforms to macOS's SecCertificate SPKI hash and to
    /// <c>openssl x509 -pubkey | openssl pkey -pubin -outform der | openssl dgst -sha256 -binary | base64</c>
    /// (RFC 7469 "pin-sha256"). So the owner computes ONE pin value that works on Windows and macOS.
    /// </summary>
    public static class CollectorPinning
    {
        // These two strings are USER-VISIBLE and were approved by the user (2026-07-11).
        /// <summary>Reason surfaced when the collector's TLS identity could not be verified.</summary>
        public const string PinFailureReason =
            "Collector identity could not be verified (certificate pinning failed); this leak result cannot be trusted.";
        /// <summary>One-time caveat surfaced when no pins are configured (transparently unpinned).</summary>
        public const string UnconfiguredCaveat =
            "Collector-identity pinning is not configured; the collector connection is unpinned (standard TLS validation only).";

        /// <summary>
        /// The configured pin set (primary + backup, to survive certificate rotation), from the
        /// <c>SYXX_COLLECTOR_PINS</c> environment variable (the same pattern as
        /// <see cref="SyxxConfig.DimensionBaseURL"/>). Comma-, space-, or newline-separated. An
        /// empty set means unpinned.
        ///
        /// OWNER DELIVERABLE: the actual pin value(s) for the real collector are a deployment input
        /// (like <c>SYXX_COLLECTOR_PRIVATE_KEY</c>) and are intentionally NOT hardcoded here.
        /// </summary>
        public static ISet<string> ConfiguredPins()
        {
            // Read from persisted file first (Windows equivalent of UserDefaults),
            // then fall back to the environment variable — matching the Swift twin's
            // three-tier fallback (UserDefaults → env var).
            string raw = PersistedCollectorPins() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                raw = Environment.GetEnvironmentVariable("SYXX_COLLECTOR_PINS") ?? string.Empty;
            var parts = raw.Split(new[] { ',', ' ', '\n', '\t', '\r' },
                                  StringSplitOptions.RemoveEmptyEntries);
            return new HashSet<string>(parts);
        }

        /// <summary>
        /// Returns the persisted collector pins file contents, or null if none exists.
        /// Twin of the Swift UserDefaults-based config read in LeakDetector.swift.
        /// </summary>
        private static string? PersistedCollectorPins()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var pinFile = System.IO.Path.Combine(appData, "Syxx", "collector_spki_pins.txt");
                return System.IO.File.Exists(pinFile) ? System.IO.File.ReadAllText(pinFile) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// WS3-A.3 / D011: the configured Ed25519 signer trust set (base64 raw public keys), from
        /// <c>SYXX_COLLECTOR_TRUSTED_KEYS</c> — same deployment pattern as
        /// <see cref="ConfiguredPins"/>. Auto-loaded by <see cref="LeakDetector.RunCollectorTestAsync"/>
        /// when the caller passes no explicit set, so the signer-trust check engages in the shipped
        /// GUI and CLI without per-call-site wiring. Empty set = no trust restriction (any
        /// cryptographically valid signer accepted; SignatureKeyTrusted stays null).
        /// </summary>
        public static ISet<string> ConfiguredTrustedSigningKeys()
        {
            string raw = Environment.GetEnvironmentVariable("SYXX_COLLECTOR_TRUSTED_KEYS") ?? string.Empty;

            // WS3-02: fall back to persisted synced key if manual config is empty
            if (string.IsNullOrEmpty(raw))
            {
                raw = PersistedTrustedSigningKey() ?? string.Empty;
            }

            var parts = raw.Split(new[] { ',', ' ', '\n', '\t', '\r' },
                                  StringSplitOptions.RemoveEmptyEntries);
            return new HashSet<string>(parts);
        }

        // MARK: - WS3-02: Trust store sync from /api/pubkey

        /// <summary>
        /// Fetches the collector's public key from the well-known endpoint and persists it.
        /// </summary>
        /// <param name="collectorBaseURL">The base URL of the collector server.</param>
        /// <returns>The fetched public key, or null if the fetch failed.</returns>
        public static async Task<string?> SyncTrustedSigningKeyAsync(Uri collectorBaseURL)
        {
            try
            {
                using var client = LeakDetector.MakeCollectorClient(CollectorPinning.ConfiguredPins());
                var pubkeyUrl = new Uri(collectorBaseURL, "api/pubkey");
                var response = await client.GetAsync(pubkeyUrl);
                response.EnsureSuccessStatusCode();

                var json = await JsonSerializer.DeserializeAsync<JsonElement>(await response.Content.ReadAsStreamAsync());
                var publicKey = json.GetProperty("publicKey").GetString();
                var keyId = json.GetProperty("keyId").GetString();
                var rotationTimestamp = json.GetProperty("rotationTimestamp").GetInt32();

                if (publicKey == null || keyId == null)
                    return null;

                // B185: Validate the fetched public key is a valid 32-byte Ed25519 key
                // and that keyId matches SHA-256(key)[0:8] hex prefix.
                byte[] keyBytes;
                try
                {
                    keyBytes = Convert.FromBase64String(publicKey);
                }
                catch (FormatException)
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: public key is not valid base64, discarding");
                    return null;
                }
                if (keyBytes.Length != 32)
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: public key is {keyBytes.Length} bytes (expected 32), discarding");
                    return null;
                }
                var keyHash = System.Security.Cryptography.SHA256.HashData(keyBytes);
                var expectedKeyId = Convert.ToHexString(keyHash, 0, 4).ToLowerInvariant(); // first 8 hex chars = 4 bytes
                if (!string.Equals(keyId, expectedKeyId, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: keyId mismatch — got {keyId}, expected {expectedKeyId}, discarding");
                    return null;
                }

                // Persist to registry for offline use (Windows equivalent of UserDefaults)
                // For now, we'll use a simple file-based approach since registry access requires more setup
                // In production, this should use proper Windows registry or secure storage
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var configDir = System.IO.Path.Combine(appData, "Syxx");
                System.IO.Directory.CreateDirectory(configDir);
                var keyFile = System.IO.Path.Combine(configDir, "collector_trusted_key.txt");

                // M4: Key rotation surfacing — emit a diagnostic breadcrumb if the key changed.
                if (System.IO.File.Exists(keyFile))
                {
                    var previousKey = System.IO.File.ReadAllText(keyFile).Trim();
                    if (!string.Equals(previousKey, publicKey, StringComparison.Ordinal))
                    {
                        var previousPrefix = previousKey.Length > 8 ? previousKey[..8] : previousKey;
                        System.Diagnostics.Debug.WriteLine($"[Syxx] SyncTrustedSigningKey: trusted key rotated (previous keyId prefix: {previousPrefix}, new keyId: {keyId})");
                    }
                }
                System.IO.File.WriteAllText(keyFile, publicKey);

                return publicKey;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the persisted trusted signing key, or null if none exists.
        /// </summary>
        public static string? PersistedTrustedSigningKey()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var keyFile = System.IO.Path.Combine(appData, "Syxx", "collector_trusted_key.txt");
                if (System.IO.File.Exists(keyFile))
                {
                    return System.IO.File.ReadAllText(keyFile);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The pin (base64 SHA-256 of DER SubjectPublicKeyInfo) for a leaf certificate. Returns
        /// <c>null</c> on any failure — the caller treats that fail-closed as a mismatch.
        /// </summary>
        public static string? SpkiPin(X509Certificate2 cert)
        {
            try
            {
                byte[] spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
                return Convert.ToBase64String(SHA256.HashData(spki));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Pure, offline-testable fail-closed decision.
        /// <list type="bullet">
        /// <item>PKI chain validation MUST pass first (pinning is <em>in addition to</em> PKI, never
        /// a replacement — a self-signed impostor is rejected even if the attacker knew a pin).</item>
        /// <item>No pins configured → accept via standard validation.</item>
        /// <item>Pins configured → accept only if the leaf pin is in the set; otherwise refuse.</item>
        /// </list>
        /// </summary>
        public static bool Accept(bool chainOk, string? leafPin, ISet<string> pins)
        {
            if (!chainOk) return false;
            if (pins.Count == 0) return true;
            return leafPin != null && pins.Contains(leafPin);
        }
    }
}
