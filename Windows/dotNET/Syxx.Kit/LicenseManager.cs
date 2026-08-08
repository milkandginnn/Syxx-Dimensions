using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Syxx.Kit
{
    public class KeygenMeta
    {
        [JsonPropertyName("constant")]
        public string Constant { get; set; } = "";

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }

    public class KeygenValidationResponse
    {
        [JsonPropertyName("meta")]
        public KeygenMeta Meta { get; set; } = new KeygenMeta();
    }

    /// A typed outcome for license validation, distinguishing "the key is invalid" from "we
    /// couldn't reach the validation server" — collapsing both to false makes a censored/blocked
    /// network indistinguishable from a bad key, silently locking users out of detection
    /// features exactly when the network is the thing worth investigating (W23).
    public enum LicenseValidationResult
    {
        Valid,
        Invalid,
        Timeout,
        NetworkError
    }

    /// <summary>
    /// Manages license key persistence, trial state, and online validation.
    ///
    /// SECRET STORAGE (removed direct DPAPI dependency — see PlatformSecretStore):
    ///   On Windows: uses <see cref="DpapiSecretStore"/> (Win32 DPAPI via crypt32.dll).
    ///   On Linux:   uses <see cref="LinuxSecretStore"/> (libsecret P/Invoke + AES-256-GCM
    ///               encrypted file fallback at ~/.syxx/keystore/).
    ///   Other:      uses <see cref="LinuxSecretStore"/> file-only fallback.
    ///
    /// The store is injected via <see cref="SetSecretStore"/> or lazily initialised from
    /// <see cref="PlatformSecretStore.Create"/>. Existing Windows config files with legacy
    /// DPAPI-base64 blobs are read transparently via a backward-compatibility path.
    /// </summary>
    public static class LicenseManager
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(8);

        // Reference token prefix — used on ALL platforms for new secrets written through
        // the ISecretStore abstraction. Legacy Windows config files may still contain
        // DPAPI-base64 blobs (handled by the backward-compat path in Unprotect).
        private const string RefTokenPrefix = "__syxx_ls:";

        /// <summary>The injected secret store. If null, lazily created via
        /// <see cref="PlatformSecretStore.Create"/> on first use.</summary>
        private static ISecretStore? _store;

        /// <summary>
        /// Injects the secret store implementation (for DI or testing).
        /// Call once at application startup, before any license operations.
        /// </summary>
        public static void SetSecretStore(ISecretStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        private static ISecretStore SecretStore
        {
            get
            {
                if (_store is null)
                {
                    _store = PlatformSecretStore.Create();
                }
                return _store;
            }
        }

        private static string ConfigPath
        {
            get
            {
                // D109: use LocalApplicationData instead of Roaming ApplicationData.
                // The license config should not be copied to a profile server on domain-joined
                // machines with roaming profiles.
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "Syxx", "license.json");
            }
        }

        private static Dictionary<string, string> LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string content = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(content) ?? new Dictionary<string, string>();
                }
            }
            catch { }
            return new Dictionary<string, string>();
        }

        private static void SaveConfig(Dictionary<string, string> dict)
        {
            try
            {
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // E087: restrict the config directory to owner-only on Linux.
                if (dir != null) SyxxPaths.EnsurePrivateDir(dir);
                string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                // E087: restrict license config to owner-only on Linux.
                SyxxPaths.RestrictToOwner(ConfigPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Syxx LicenseManager: failed to save config: {ex.Message}");
            }
        }

        // MARK: - Credential storage (W20)

        /// <summary>
        /// Encrypts <paramref name="plaintext"/> for storage in the config dictionary.
        ///
        /// On all platforms (via <see cref="ISecretStore"/>): stores the secret under
        /// a randomly-generated keyId and writes a reference token <c>__syxx_ls:&lt;keyId&gt;</c>
        /// into the config. The actual encrypted secret lives in the platform-appropriate
        /// store (DPAPI on Windows, libsecret/AES-GCM on Linux).
        ///
        /// Backward compatibility: existing Windows config files with DPAPI-base64 blobs
        /// (written by the old inline-DPAPI code) are still readable via <see cref="Unprotect"/>.
        /// </summary>
        private static string Protect(string plaintext)
        {
            var keyId = Guid.NewGuid().ToString("N");
            SecretStore.Store("license", keyId, plaintext);
            return RefTokenPrefix + keyId;
        }

        /// <summary>
        /// Decrypts <paramref name="stored"/> previously produced by <see cref="Protect"/>.
        ///
        /// Handles:
        ///   <c>__syxx_ls:&lt;keyId&gt;</c> — reference token (new format, all platforms).
        ///   Base64 string                  — legacy DPAPI blob (Windows only, backward compat).
        ///
        /// Returns <c>null</c> on any failure.
        /// </summary>
        private static string? Unprotect(string stored)
        {
            if (stored.StartsWith(RefTokenPrefix, StringComparison.Ordinal))
            {
                var keyId = stored.Substring(RefTokenPrefix.Length);
                return SecretStore.Retrieve("license", keyId);
            }

            // Legacy Windows DPAPI-base64 blob (backward compat).
            // Only attempted on Windows; falls through to null on other platforms.
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var encrypted = Convert.FromBase64String(stored);
#pragma warning disable CA1416 // DpapiSecretStore.DecryptLegacy is [SupportedOSPlatform("windows")]
                    return DpapiSecretStore.DecryptLegacy(encrypted, DpapiSecretStore.LegacyLicenseEntropy);
#pragma warning restore CA1416
                }
                // E001: surface DPAPI failures to diagnostics instead of silently returning null.
                // Keystore corruption or OS permission errors must be distinguishable from "no key stored".
                catch (Exception ex)
                {
                    SyxxTracer.Log("license.unprotect.failed", $"DPAPI legacy decryption failed: {ex.GetType().Name}");
                    return null;
                }
            }

            return null;
        }

        /// Returns the locally stored license key, decrypted via DPAPI (bound to this Windows
        /// user account), instead of the plaintext it used to be stored as.
        public static string? GetStoredLicenseKey()
        {
            var dict = LoadConfig();
            if (dict.TryGetValue("key", out string? encrypted))
            {
                return Unprotect(encrypted);
            }
            return null;
        }

        /// Saves the license key via the platform secret store (DPAPI on Windows,
        /// libsecret/AES-GCM on Linux).
        public static void SaveLicenseKey(string key)
        {
            var dict = LoadConfig();
            dict["key"] = Protect(key);
            SaveConfig(dict);
        }

        // MARK: - Trial state (W21)

        /// D054: merged trialMaxTime into the HMAC-signed TrialState so the max-seen clock
        /// value travels with the first-launch value in one signed blob. An attacker who
        /// modifies either field invalidates the signature.
        private class TrialState
        {
            public long FirstLaunchEpochMillis { get; set; }
            /// D054: the highest clock value seen during trial, signed alongside FirstLaunchEpochMillis.
            /// Previously stored as a separate unsigned DPAPI blob ("trialMaxTime"), which allowed
            /// an attacker to lower it and roll the clock back to extend the trial undetected.
            public long MaxSeenMillis { get; set; }
            /// E205: nullable — when the machine fingerprint is unavailable, no signature is
            /// computed and the trial state is treated as unsigned (untrusted).
            public string? Signature { get; set; }
        }

        // E044: fully async — the old GetAwaiter().GetResult() form ran synchronously on the WinUI
        // UI thread (IsFeatureUnlockedAsync executes up to its first await on the caller's thread)
        // and froze the GUI for up to the 1.5s network timeout on every entitlement check.
        private static async Task<(long ServerFirstLaunch, long ServerTime)?> SyncTrialAnchorWithServerAsync(string fingerprint)
        {
            try
            {
                var body = JsonSerializer.Serialize(new Dictionary<string, string> { { "fingerprint", fingerprint } });
                using var content = new StringContent(body, Encoding.UTF8, "application/json");

                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                var response = await _httpClient.PostAsync("https://syxx.app/api/trial/anchor", content, cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(responseJson);
                    long serverLaunch = doc.RootElement.GetProperty("firstLaunch").GetInt64();
                    long serverTime = doc.RootElement.GetProperty("serverTime").GetInt64();
                    return (serverLaunch, serverTime);
                }
            }
            // E002: log network failures instead of silently swallowing them.
            catch (Exception ex)
            {
                SyxxTracer.Log("trial.sync.failed", $"Trial anchor sync failed: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        /// D054: sign both FirstLaunchEpochMillis and MaxSeenMillis so neither can be
        /// modified independently. The signature covers both fields separated by a pipe.
        ///
        /// E205: refuses to sign when the machine fingerprint is unavailable — an empty-string
        /// HMAC key is trivially forgeable (any attacker can reproduce it). Returns null so callers
        /// treat the trial state as unsigned rather than trusting a zero-entropy signature.
        ///
        /// E205 (extended): the HMAC key also mixes in installation-specific random bytes
        /// generated on first install and persisted via the platform secret store. This ensures
        /// that even if the fingerprint is guessable or shared across VMs, the HMAC key contains
        /// entropy unique to THIS installation that an attacker cannot reproduce without access
        /// to the encrypted store.
        private static string? TrialSignature(long firstLaunchMillis, long maxSeenMillis = 0)
        {
            var fp = GetMachineFingerprint();
            if (fp.Quality == FingerprintQuality.Unavailable)
                return null;

            // E205: mix installation-specific random bytes into the HMAC key so it is never
            // derived from the fingerprint alone. GetInstallationSalt() generates and persists
            // 32 random bytes on first call; subsequent calls return the same bytes.
            var installSalt = GetInstallationSalt();
            var hmacKey = fp.Value + ":" + installSalt;

            return ComputeHmacSHA256(
                firstLaunchMillis.ToString(CultureInfo.InvariantCulture) + "|" +
                maxSeenMillis.ToString(CultureInfo.InvariantCulture),
                hmacKey);
        }

        /// E205: retrieves or creates a 32-byte random salt unique to this installation.
        /// The salt is persisted via the platform secret store so it survives across sessions
        /// but cannot be reconstructed by an attacker who only knows the machine fingerprint.
        /// This prevents HMAC key forgery even when the fingerprint is guessable (e.g. VMs
        /// sharing a MachineGuid, or an attacker who has enumerated the registry value).
        private static string GetInstallationSalt()
        {
            const string saltKeyType = "trial-salt";
            const string saltKeyId = "v1";

            try
            {
                var existing = SecretStore.Retrieve(saltKeyType, saltKeyId);
                if (!string.IsNullOrEmpty(existing))
                    return existing;

                // Generate 32 cryptographically-random bytes, hex-encoded (64 chars).
                var bytes = new byte[32];
                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(bytes);
                var salt = Convert.ToHexString(bytes).ToLowerInvariant();

                SecretStore.Store(saltKeyType, saltKeyId, salt);
                return salt;
            }
            catch (Exception ex)
            {
                // If the secret store is broken, log and return a fallback that at least
                // prevents an empty HMAC key. This is not forgeable across installs because
                // it includes the current timestamp — but trial state signed with it will
                // not survive a store repair. The alternative (empty key) is worse.
                SyxxTracer.Log("trial.salt.unavailable",
                    $"Installation salt store unavailable: {ex.GetType().Name}. " +
                    "Trial state signed with ephemeral fallback salt.");
                return $"ephemeral:{DateTimeOffset.UtcNow.Ticks}";
            }
        }

        private static string ComputeHmacSHA256(string message, string key)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var messageBytes = Encoding.UTF8.GetBytes(message);
            using var hmac = new HMACSHA256(keyBytes);
            return Convert.ToHexString(hmac.ComputeHash(messageBytes)).ToLower();
        }

        /// Retrieves or initializes the first-launch timestamp. The stored record is signed with
        /// a key derived from this device's fingerprint and stored via the platform secret store;
        /// a record that fails signature verification (hand-edited or copied from a different
        /// machine) is treated as absent rather than trusted, so the config file can no longer
        /// be trivially edited to extend or reset the trial window.
        ///
        /// Anchors the launch date on the server if online (KTRU-03), and returns DateTime.MinValue
        /// if clock regression is detected (KTRU-04).
        /// Blocking wrapper retained ONLY for the [Obsolete] sync IsFeatureUnlocked path, which has
        /// no production callers. Production code (GUI + CLI) reaches the async version below.
        public static DateTime GetFirstLaunchDate()
            => GetFirstLaunchDateAsync().GetAwaiter().GetResult();

        public static async Task<DateTime> GetFirstLaunchDateAsync()
        {
            var dict = LoadConfig();
            long? localLaunchMillis = null;
            long localMaxSeenMillis = 0;
            if (dict.TryGetValue("trialState", out string? encrypted))
            {
                var json = Unprotect(encrypted);
                if (json != null)
                {
                    try
                    {
                        var state = JsonSerializer.Deserialize<TrialState>(json);
                        if (state != null)
                        {
                            // D054: verify signature covers both FirstLaunchEpochMillis and MaxSeenMillis.
                            // Backward compatibility: old records only signed FirstLaunchEpochMillis,
                            // so we accept either signature format during the transition.
                            // E205: reject unsigned state — a null or empty stored signature with a
                            // null expected signature (fingerprint unavailable) must NOT pass.
                            if (!string.IsNullOrEmpty(state.Signature))
                            {
                                var expectedSig = TrialSignature(state.FirstLaunchEpochMillis, state.MaxSeenMillis);
                                var legacySig = TrialSignature(state.FirstLaunchEpochMillis, 0);
                                if (state.Signature == expectedSig || state.Signature == legacySig)
                                {
                                    localLaunchMillis = state.FirstLaunchEpochMillis;
                                    localMaxSeenMillis = state.MaxSeenMillis;
                                }
                                else
                                {
                                    // D369: log fingerprint mismatch — typically caused by Windows
                                    // reinstall changing MachineGuid, which resets trial without explanation.
                                    SyxxTracer.Log("trial.signature.mismatch",
                                        "Trial signature does not match current machine fingerprint. " +
                                        "This typically occurs after a Windows reinstall or hardware change.");
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            // D054: backward compatibility — read the old unsigned trialMaxTime if present and
            // use it to initialize MaxSeenMillis in the signed TrialState. This is a one-time
            // migration; subsequent writes will include MaxSeenMillis in the signature.
            if (localMaxSeenMillis == 0 && dict.TryGetValue("trialMaxTime", out string? encryptedMax))
            {
                var maxStr = Unprotect(encryptedMax);
                if (maxStr != null && long.TryParse(maxStr, out long parsedMax))
                    localMaxSeenMillis = parsedMax;
            }

            string fingerprint = GetSystemFingerprint();
            long finalLaunchMillis;

            var online = await SyncTrialAnchorWithServerAsync(fingerprint).ConfigureAwait(false);
            if (online.HasValue)
            {
                long serverLaunch = online.Value.ServerFirstLaunch * 1000;
                long serverTime = online.Value.ServerTime * 1000;

                // D059: never overwrite an earlier local anchor with a later server timestamp.
                // Going offline creates a local anchor; the server has never seen the
                // fingerprint, so it returns now() as firstLaunch. Taking min(local, server)
                // prevents the offline-then-online path from resetting the trial window.
                finalLaunchMillis = localLaunchMillis.HasValue
                    ? Math.Min(localLaunchMillis.Value, serverLaunch)
                    : serverLaunch;

                long nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (nowMillis < serverLaunch || (serverTime - nowMillis) > 300000)
                {
                    return DateTime.MinValue;
                }
            }
            else
            {
                if (localLaunchMillis.HasValue)
                {
                    finalLaunchMillis = localLaunchMillis.Value;
                }
                else
                {
                    finalLaunchMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }

            // E205: only persist trial state when we can sign it — a stored unsigned blob
            // would pass verification on next read when the fingerprint is still unavailable,
            // making the trial trivially forgeable. When we cannot sign, the trial state lives
            // only for this session (the caller still gets the correct first-launch date).
            var signature = TrialSignature(finalLaunchMillis, localMaxSeenMillis);
            if (signature is not null)
            {
                var newState = new TrialState
                {
                    FirstLaunchEpochMillis = finalLaunchMillis,
                    // D054: preserve the existing MaxSeenMillis from the old trialMaxTime field
                    // (backward compatibility) or from the deserialized TrialState.
                    MaxSeenMillis = localMaxSeenMillis,
                    Signature = signature
                };
                dict["trialState"] = Protect(JsonSerializer.Serialize(newState));
                SaveConfig(dict);
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(finalLaunchMillis).UtcDateTime;
        }

        // D028: deleted dead [Obsolete] IsFeatureUnlocked(bool) method — a latent footgun that
        // trusted a caller-supplied entitlement bool. macOS already removed this overload.

        // MARK: - Fingerprint (W22)

        /// Generates a privacy-preserving device fingerprint: a salted hash of the raw
        /// MachineGuid, not the MachineGuid itself. The raw identifier is stable and could be
        /// correlated with other services; hashing it with an app-specific salt means only Syxx
        /// can recognize repeat installs on the same device, without exposing the identifier.
        public static string GetSystemFingerprint() => GetMachineFingerprint().Value;

        /// The fingerprint WITH its provenance (see MachineFingerprint). Callers deciding whether to
        /// node-lock must use this, not the bare string: `Quality` is the difference between "this
        /// identifies the hardware" and "this identifies one OS install".
        ///
        /// The hostname fallback that used to live here is GONE. It made every Linux box fingerprint
        /// as its hostname (the registry read always fails off Windows), so two machines named
        /// "ubuntu" were one machine to a licence server and renaming a host burned an activation.
        /// When we cannot identify the machine we now say so.
        public static MachineFingerprint GetMachineFingerprint()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    var guid = key?.GetValue("MachineGuid")?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(guid))
                        return MachineFingerprints.From(guid, FingerprintQuality.Hardware, "win:MachineGuid");
                }
                catch { }
                return MachineFingerprint.Unavailable;
            }

            if (OperatingSystem.IsLinux()) return MachineFingerprints.Linux();

            // macOS: the SyxxKit twin owns IOPlatformUUID; this .NET Kit does not run the macOS GUI.
            return MachineFingerprint.Unavailable;
        }

        // MARK: - Validation (W23)

        // D066: Pinned P-256 public key (SubjectPublicKeyInfo DER, hex-encoded, 91 bytes = 182 hex chars)
        // from Keygen.sh. Must match the macOS twin (LicenseManager.swift keygenPublicKeyDERHex).
        // Empty means signature verification is unavailable — meta.valid cannot be trusted.
        // A TLS interceptor or compromised trust root could otherwise forge license grants or revocations.
        private static readonly string KeygenPublicKeyDerHex = "3059301306072a8648ce3d020106082a8648ce3d030107034200043495cab670d545c30a9f6a3cce56658883ffbe96a48c03a09efbc3350ed23e513c9064ea53b32b354202fae899e50ab30262788441c7f95bbd3779215da6a78e";

        /// D066: Verifies the P-256 ECDSA signature on a Keygen.sh validate-key response.
        /// Returns true only when the signature is cryptographically valid against the pinned
        /// public key. Returns false when the key is not configured, the signature header is
        /// missing/malformed, or verification fails — caller must NOT trust meta.valid.
        private static bool VerifyKeygenSignature(byte[] body, HttpResponseMessage httpResponse,
                                                   string requestPath, string requestMethod)
        {
            if (string.IsNullOrEmpty(KeygenPublicKeyDerHex)) return false;

            // Parse the Keygen-Signature header.
            if (!httpResponse.Headers.TryGetValues("Keygen-Signature", out var sigValues))
                return false;
            string sigHeader = sigValues.First();
            var sigParams = new Dictionary<string, string>();
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(sigHeader, "([^=,\"]+)=\"([^\"]*)\""))
            {
                sigParams[match.Groups[1].Value] = match.Groups[2].Value;
            }
            if (!sigParams.TryGetValue("signature", out var encSig) || string.IsNullOrEmpty(encSig))
                return false;
            byte[] sigBytes;
            try { sigBytes = Convert.FromBase64String(encSig); }
            catch { return false; }

            // Date header freshness: +/- 5 minutes to prevent replay of old signed responses.
            if (!httpResponse.Headers.TryGetValues("Date", out var dateValues)) return false;
            if (!DateTimeOffset.TryParse(dateValues.First(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var respDate))
                return false;
            if (Math.Abs((respDate - DateTimeOffset.UtcNow).TotalMinutes) > 5) return false;

            // Compute SHA-256 digest of response body.
            byte[] bodyDigest = SHA256.HashData(body);
            string encDigest = Convert.ToBase64String(bodyDigest);

            // Reconstruct signing data matching Keygen's format.
            string signingData = $"(request-target): {requestMethod.ToLowerInvariant()} {requestPath}\n" +
                                 $"host: api.keygen.sh\n" +
                                 $"date: {dateValues.First()}\n" +
                                 $"digest: sha-256={encDigest}";

            // Load the pinned P-256 SubjectPublicKeyInfo DER and verify with ECDSA-SHA256.
            // sigBytes from Keygen are DER-encoded ECDSA (Rfc3279DerSequence format).
            try
            {
                byte[] derBytes = Convert.FromHexString(KeygenPublicKeyDerHex);
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(derBytes, out _);
                return ecdsa.VerifyData(
                    Encoding.UTF8.GetBytes(signingData),
                    sigBytes,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);
            }
            catch
            {
                return false;
            }
        }

        /// Validates a license key using Keygen.sh API, returning a typed result so a network
        /// failure (e.g. on a censored/filtered connection) is distinguishable from an invalid key.
        public static async Task<LicenseValidationResult> ValidateLicenseDetailedAsync(string key, string accountID)
        {
            if (string.IsNullOrEmpty(accountID))
            {
                return LicenseValidationResult.NetworkError;
            }

            try
            {
                // validate-key is Keygen's by-key route; plain .../actions/validate is not a real
                // endpoint (404 NOT_FOUND) and made every license read as invalid. No fingerprint
                // scope: a scope requires the machine to be pre-activated, so a fresh key would fail.
                string url = $"https://api.keygen.sh/v1/accounts/{accountID}/licenses/actions/validate-key";

                var payload = new
                {
                    meta = new
                    {
                        key = key
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/vnd.api+json");

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.api+json"));

                using var cts = new CancellationTokenSource(ValidationTimeout);
                var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    byte[] resBytes = await response.Content.ReadAsByteArrayAsync();
                    string resJson = Encoding.UTF8.GetString(resBytes);
                    var validation = JsonSerializer.Deserialize<KeygenValidationResponse>(resJson);
                    // D066: verify the Ed25519 response signature before trusting meta.valid.
                    // An unverified meta.valid is indistinguishable from a forged body.
                    if (validation?.Meta.Valid == true)
                    {
                        string requestPath = $"/v1/accounts/{accountID}/licenses/actions/validate-key";
                        if (!VerifyKeygenSignature(resBytes, response, requestPath, "post"))
                        {
                            SyxxDiagnostics.Breadcrumb(category: "license.validation",
                                message: "Keygen signature unverifiable — refusing to trust meta.valid",
                                level: DiagnosticSeverity.Warning, data: new Dictionary<string, string>());
                            return LicenseValidationResult.NetworkError;
                        }
                    }
                    return (validation?.Meta.Valid ?? false) ? LicenseValidationResult.Valid : LicenseValidationResult.Invalid;
                }
                // B187: 401/404 are UNAUTHENTICATED statuses (classic captive-portal/interception
                // response), not a definite "key is bad" — treat as transient so offline grace applies.
                return LicenseValidationResult.NetworkError;
            }
            catch (OperationCanceledException)
            {
                return LicenseValidationResult.Timeout;
            }
            catch (Exception ex)
            {
                SyxxDiagnostics.Breadcrumb(category: "license.validation", message: "License validation failed", level: DiagnosticSeverity.Warning, data: new Dictionary<string, string> { ["error"] = ex.GetType().Name });
                return LicenseValidationResult.NetworkError;
            }
        }

        // E050: ValidateLicenseAsync (bool wrapper) removed. All callers now use
        // ValidateLicenseDetailedAsync so Timeout/NetworkError are never collapsed to "invalid."

        /// <summary>
        /// Returns the epoch milliseconds of the last successful online license validation, or null.
        /// Used by callers outside LicenseManager (e.g., AppViewModel.CheckLicenseAsync catch block)
        /// to check offline grace without accessing the private config.
        /// </summary>
        public static long? GetLastValidValidationEpoch()
        {
            try
            {
                var dict = LoadConfig();
                if (dict.TryGetValue("lastValidValidation", out string? encryptedLast))
                {
                    var lastStr = Unprotect(encryptedLast);
                    if (lastStr != null &&
                        long.TryParse(lastStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lastValidMs))
                        return lastValidMs;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Pure E-7 offline-grace predicate, exposed for tests. Returns true when
        /// <paramref name="nowMs"/> is within <paramref name="graceWindowMs"/> of
        /// <paramref name="lastValidMs"/>. A null <paramref name="lastValidMs"/> returns false.
        ///
        /// Mutation: removing the <c>&lt;= graceWindowMs</c> check returns false for any lastValidMs
        /// — tests that assert true when within the window will RED.</summary>
        public static bool OfflineGraceApplies(long nowMs, long? lastValidMs, long graceWindowMs = 30L * 24 * 60 * 60 * 1000)
        {
            if (lastValidMs is null) return false;
            return nowMs - lastValidMs.Value <= graceWindowMs;
        }

        /// <summary>Internal feature-unlock check — validates stored license key with Keygen,
        /// then falls back to trial mode. Never trusts a caller-supplied boolean (D003 / KTRU-01).
        ///
        /// E-7 offline grace: a paying subscriber whose network blocks Keygen (the very condition Syxx
        /// investigates) stays unlocked for 30 days from their last successful online validation.
        /// This distinguishes "subscription lapsed" (Invalid) from "can't reach server" (Timeout /
        /// NetworkError). A censored network must not silently demote Pro to Locked.</summary>
        public static async Task<(bool Unlocked, string Message)> IsFeatureUnlockedAsync()
        {
            // 30 days in milliseconds for E-7 offline grace window.
            const long OfflineGraceMs = 30L * 24 * 60 * 60 * 1000;

            string? storedKey = GetStoredLicenseKey();
            if (!string.IsNullOrEmpty(storedKey))
            {
                var result = await ValidateLicenseDetailedAsync(storedKey, SyxxConfig.AccountID);
                if (result == LicenseValidationResult.Valid)
                {
                    // Record this successful validation epoch for E-7 offline grace.
                    long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var configForWrite = LoadConfig();
                    configForWrite["lastValidValidation"] = Protect(nowMs.ToString(CultureInfo.InvariantCulture));
                    SaveConfig(configForWrite);
                    // E156: the entitlement message is a status, not a storefront. Prices belong
                    // on the purchase cards (the single price surface), not in the confirmation line.
                    return (true, "🟢 Unlocked via Active Pro Subscription");
                }
                else if (result == LicenseValidationResult.Invalid)
                {
                    // Key is definitively bad — do NOT grant offline grace, fall through to trial.
                }
                else
                {
                    // Timeout or NetworkError — could be the filtered network this app investigates.
                    // Check offline grace window before falling through to the trial/locked path.
                    var configForGrace = LoadConfig();
                    if (configForGrace.TryGetValue("lastValidValidation", out string? encryptedLast))
                    {
                        var lastStr = Unprotect(encryptedLast);
                        if (lastStr != null &&
                            long.TryParse(lastStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lastValidMs))
                        {
                            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            if (nowMs - lastValidMs <= OfflineGraceMs)
                            {
                                // Within 30 days of last successful validation — stay unlocked.
                                // OUTPUT REQUIRES USER APPROVAL (AGENTS.md:42): pending approval for exact wording.
                                return (true, "🟢 Unlocked via Pro Subscription (offline — last verified within 30 days)");
                            }
                        }
                    }
                    // No recent validation record, or > 30 days ago — fall through.
                }
            }

            long nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var dict = LoadConfig();

            // D054: read MaxSeenMillis from the signed TrialState instead of the old unsigned
            // trialMaxTime field. The signature covers both FirstLaunchEpochMillis and
            // MaxSeenMillis, so an attacker cannot lower MaxSeenMillis and roll the clock back.
            long maxSeenMillis = 0;
            if (dict.TryGetValue("trialState", out string? encryptedState))
            {
                var json = Unprotect(encryptedState);
                if (json != null)
                {
                    try
                    {
                        var state = JsonSerializer.Deserialize<TrialState>(json);
                        if (state != null)
                        {
                            var expectedSig = TrialSignature(state.FirstLaunchEpochMillis, state.MaxSeenMillis);
                            var legacySig = TrialSignature(state.FirstLaunchEpochMillis, 0);
                            if (state.Signature == expectedSig || state.Signature == legacySig)
                            {
                                maxSeenMillis = state.MaxSeenMillis;
                            }
                        }
                    }
                    catch { }
                }
            }

            if (nowMillis < maxSeenMillis)
                return (false, "Locked (Clock regression detected). Please restore your system clock to proceed.");

            if (nowMillis > maxSeenMillis)
            {
                // D054: update MaxSeenMillis in the signed TrialState instead of the old
                // unsigned trialMaxTime field.
                long firstLaunchMillis = 0;
                if (dict.TryGetValue("trialState", out string? existingState))
                {
                    var existingJson = Unprotect(existingState);
                    if (existingJson != null)
                    {
                        try
                        {
                            var existing = JsonSerializer.Deserialize<TrialState>(existingJson);
                            if (existing != null) firstLaunchMillis = existing.FirstLaunchEpochMillis;
                        }
                        catch { }
                    }
                }
                var updatedState = new TrialState
                {
                    FirstLaunchEpochMillis = firstLaunchMillis,
                    MaxSeenMillis = nowMillis,
                    Signature = TrialSignature(firstLaunchMillis, nowMillis)
                };
                dict["trialState"] = Protect(JsonSerializer.Serialize(updatedState));
                SaveConfig(dict);
            }

            DateTime firstLaunch = await GetFirstLaunchDateAsync();
            if (firstLaunch == DateTime.MinValue)
                return (false, "🔒 Locked (Clock regression detected). Please restore your system clock to proceed.");

            TimeSpan elapsed = DateTime.UtcNow - firstLaunch;
            TimeSpan trialDuration = TimeSpan.FromDays(3);

            if (elapsed.TotalMilliseconds < 0)
                return (false, "🔒 Locked (Clock regression detected). Please restore your system clock to proceed.");
            if (elapsed < trialDuration)
            {
                int remainingHours = (int)(trialDuration - elapsed).TotalHours;
                return (true, $"⏳ Unlocked via Free Trial ({remainingHours} hours remaining)");
            }

            return (false, "🔒 Locked (Trial Expired). Requires a $3.95/month subscription to cover hosting costs.");
        }
    }
}
