using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using NSec.Cryptography;

namespace Syxx.Kit
{
    public class WitnessSnapshot
    {
        /// Signed payload schema version. Currently 2.
        public int FormatVersion { get; set; } = 2;

        /// <summary>
        /// Display mirror of the signed <c>Evidence.Timestamp</c>. [B251] Encoded as Foundation's
        /// seconds-since-2001 double so macOS can read a Windows-signed snapshot; ISO-8601 is still
        /// accepted on read for snapshots written by older Windows builds.
        /// </summary>
        [JsonConverter(typeof(AppleReferenceDateJsonConverter))]
        public DateTime Timestamp { get; set; }

        /// Optional absolute expiry date baked into the signed payload.
        [JsonConverter(typeof(NullableAppleReferenceDateJsonConverter))]
        public DateTime? ExpiresAt { get; set; }
        public string TargetDomain { get; set; } = string.Empty;
        public ComparisonResult Comparison { get; set; } = new ComparisonResult();
        /// The canonical, cross-platform byte-stable content the signature actually covers (E011).
        public SignedEvidencePayload Evidence { get; set; } = new SignedEvidencePayload();
        public string Signature { get; set; } = string.Empty;
        /// Base64-encoded Ed25519 public key — anyone can verify without holding the private key.
        public string PublicKey { get; set; } = string.Empty;
        /// Key identifier (SHA-256 hash of the public key) for rotation support.
        public string KeyId { get; set; } = string.Empty;
    }

    public class WitnessSignerException : Exception
    {
        public WitnessSignerException(string message) : base(message) { }
    }

    public static class WitnessSigner
    {
        private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

        /// Raw byte length of an Ed25519 private key.
        private const int Ed25519PrivateKeyBytes = 32;

        /// <summary>
        /// Decodes and validates a base64-encoded Ed25519 private key, returning the live key.
        ///
        /// SINGLE definition of "is this usable signing key material", shared by the signing paths
        /// and by <see cref="WitnessKeyStore.SaveKey"/>, so anything accepted at save time is
        /// guaranteed to be accepted at sign time. Splitting those two checks is what allowed a
        /// 6-byte string that happened to be valid base64 to be persisted as a signing key on the
        /// macOS twin and only fail, opaquely, at signing time months later.
        ///
        /// Parity with macOS <c>WitnessSigner.validatedPrivateKey(fromBase64:)</c>: every rejection
        /// is a <see cref="WitnessSignerException"/>. Previously a non-base64 key escaped as a raw
        /// <c>FormatException</c> and a wrong-length key as an NSec exception, neither of which the
        /// macOS side can produce, so the two platforms failed differently on identical input.
        /// </summary>
        /// <exception cref="WitnessSignerException">The input is not usable Ed25519 key material.</exception>
        internal static Key ImportValidatedPrivateKey(string privateKeyBase64)
        {
            if (string.IsNullOrEmpty(privateKeyBase64))
            {
                throw new WitnessSignerException("No signing key was supplied.");
            }

            // Fail closed: TryFromBase64String rejects any non-alphabet byte, then the length check
            // rejects anything that is not exactly 32 raw bytes, then NSec's own import is the final
            // acceptance test, so this validator can never be more permissive than the signer.
            var buffer = new byte[((privateKeyBase64.Length + 3) / 4) * 3];
            if (!Convert.TryFromBase64String(privateKeyBase64, buffer, out int written)
                || written != Ed25519PrivateKeyBytes)
            {
                throw new WitnessSignerException(InvalidPrivateKeyMessage);
            }

            try
            {
                return Key.Import(Algorithm, buffer.AsSpan(0, written), KeyBlobFormat.RawPrivateKey, new KeyCreationParameters
                {
                    ExportPolicy = KeyExportPolicies.AllowPlaintextExport
                });
            }
            catch (Exception ex) when (ex is not WitnessSignerException)
            {
                throw new WitnessSignerException(InvalidPrivateKeyMessage);
            }
        }

        /// <summary>
        /// Throws unless <paramref name="privateKeyBase64"/> is usable Ed25519 signing key material.
        /// Same contract as <see cref="ImportValidatedPrivateKey"/>; use when the key is not needed.
        /// </summary>
        /// <exception cref="WitnessSignerException">The input is not usable Ed25519 key material.</exception>
        public static void ValidatePrivateKey(string privateKeyBase64)
        {
            using var key = ImportValidatedPrivateKey(privateKeyBase64);
        }

        /// States WHAT failed (established by the guard) and never WHY. We cannot know whether a key
        /// is bad because it was typed as a passphrase, truncated, or pasted from the wrong place,
        /// so no cause and no cause-dependent remedy is asserted. Byte-identical to the macOS
        /// <c>WitnessSignerError.invalidPrivateKey</c> description.
        internal const string InvalidPrivateKeyMessage =
            "The signing key is not valid Ed25519 key material. An Ed25519 private key is exactly 32 bytes, written as base64.";

        /// Generates a new Ed25519 keypair.
        /// Returns (privateKeyBase64, publicKeyBase64).
        public static (string PrivateKey, string PublicKey) GenerateKeyPair()
        {
            using var key = Key.Create(Algorithm, new KeyCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport
            });
            var privRaw = key.Export(KeyBlobFormat.RawPrivateKey);
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            return (Convert.ToBase64String(privRaw), Convert.ToBase64String(pubRaw));
        }

        /// Creates and signs a witness snapshot. `privateKeyBase64` must be a base64-encoded
        /// Ed25519 private key (32 raw bytes). Callers must generate a keypair with
        /// <see cref="GenerateKeyPair"/> and store the private key securely.
        public static WitnessSnapshot CreateSignedSnapshot(string domain, ComparisonResult comparison, string privateKeyBase64, DateTime? expiresAt = null)
            => CreateSignedSnapshotAt(domain, comparison, privateKeyBase64, expiresAt, timestampOverride: null);

        /// Internal test seam: identical to <see cref="CreateSignedSnapshot"/> but allows pinning
        /// the signed timestamp so freshness rules (B085 future-dated, stale) can be exercised
        /// against honestly-signed snapshots. Never used by production callers.
        internal static WitnessSnapshot CreateSignedSnapshotAt(string domain, ComparisonResult comparison, string privateKeyBase64, DateTime? expiresAt, DateTime? timestampOverride, int formatVersion = SignedEvidencePayload.CurrentFormatVersion)
        {
            using var key = ImportValidatedPrivateKey(privateKeyBase64);

            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            var pubKeyBase64 = Convert.ToBase64String(pubRaw);
            var keyId = Convert.ToHexString(SHA256.HashData(pubRaw)).ToLower()[..16];

            var timestamp = timestampOverride ?? DateTime.UtcNow;
            ulong epochMillis = (ulong)new DateTimeOffset(timestamp.ToUniversalTime()).ToUnixTimeMilliseconds();
            ulong? expiresAtMillis = expiresAt.HasValue
                ? (ulong)new DateTimeOffset(expiresAt.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                : (ulong?)null;
            var evidence = SignedEvidencePayload.Build(domain, comparison, epochMillis, expiresAtMillis, formatVersion);
            var payloadBytes = evidence.CanonicalBytes();
            var signature = Algorithm.Sign(key, payloadBytes);
            var signatureBase64 = Convert.ToBase64String(signature);

            return new WitnessSnapshot
            {
                FormatVersion = formatVersion,
                Timestamp = timestamp,
                ExpiresAt = expiresAt,
                TargetDomain = domain,
                Comparison = comparison,
                Evidence = evidence,
                Signature = signatureBase64,
                PublicKey = pubKeyBase64,
                KeyId = keyId
            };
        }

        /// Ph6.1/B098: sign the decisive-act catalog membership + version so evidence bundles can
        /// prove which catalog produced a decisive result (AMEND-O2 forensic reproducibility).
        /// B227: marked [Obsolete] — no caller exists, and the signed type is a Phase 3 compile shim.
        /// Do not wire until Phase 3.6 re-derives the WitnessSigner surface over a real type.
        [Obsolete("SignDecisiveActs has no caller and signs a stub type. Await Phase 3.6 re-derivation.")]
        public static ValidatedDecisiveActs SignDecisiveActs(ValidatedDecisiveActs acts, string privateKeyBase64)
        {
            using var key = ImportValidatedPrivateKey(privateKeyBase64);
            var signature = Algorithm.Sign(key, acts.CanonicalBytes());
            return acts with
            {
                Signature = Convert.ToBase64String(signature),
                PublicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)),
            };
        }

        /// Ph6.1/B098: verify a signed decisive-act catalog. An UNSIGNED catalog never verifies —
        /// absence of a signature is absence of provenance, not implicit trust. When a trust set
        /// is supplied, the signer must be a member.
        /// B227: marked [Obsolete] — no caller exists, and the signed type is a Phase 3 compile shim.
        [Obsolete("VerifyDecisiveActs has no caller and verifies a stub type. Await Phase 3.6 re-derivation.")]
        public static bool VerifyDecisiveActs(ValidatedDecisiveActs acts, List<string>? trustedPublicKeys = null)
        {
            if (string.IsNullOrEmpty(acts.Signature) || string.IsNullOrEmpty(acts.PublicKey))
            {
                return false;
            }
            // B211: auto-load trust set when caller doesn't supply one.
            trustedPublicKeys ??= WitnessPinning.ConfiguredTrustedWitnessKeys().ToList();
            if (trustedPublicKeys is { Count: > 0 } && !trustedPublicKeys.Contains(acts.PublicKey))
            {
                return false;
            }
            try
            {
                var publicKey = PublicKey.Import(Algorithm, Convert.FromBase64String(acts.PublicKey), KeyBlobFormat.RawPublicKey);
                return Algorithm.Verify(publicKey, acts.CanonicalBytes(), Convert.FromBase64String(acts.Signature));
            }
            catch
            {
                return false;
            }
        }

        /// Verifies a witness snapshot using the Ed25519 public key embedded in the snapshot.
        /// No secret key is needed — any third party can verify with the public key alone.
        /// Optionally rejects snapshots older than <paramref name="maxAge"/> and snapshots not
        /// signed by a <paramref name="trustedPublicKeys"/> member.
        /// D050: now returns <see cref="SnapshotVerification"/> directly (never a bare bool) so
        /// callers can distinguish expired, stale, untrusted, and forged results — matching macOS
        /// <c>verifySnapshot</c> which always returns the tri-state enum. Previously was a boolean
        /// convenience wrapper over <see cref="VerifySnapshotDetailed"/>.
        public static SnapshotVerification VerifySnapshot(WitnessSnapshot snapshot, TimeSpan? maxAge = null, List<string>? trustedPublicKeys = null)
            => VerifySnapshotDetailed(snapshot, maxAge, trustedPublicKeys);

        /// B085 clock-skew tolerance: a snapshot may carry a signed timestamp up to this far ahead
        /// of local now (honest clock drift) before it is rejected as future-dated. Matches the
        /// forensic stance that a snapshot "from the future" cannot have been produced by an honest
        /// signer and defeats max-age freshness by construction.
        internal const ulong FutureSkewToleranceMillis = 5 * 60 * 1000;

        /// E021: full verification outcome. Callers that need only pass/fail use
        /// <see cref="VerifySnapshot"/>; surfaces that must distinguish expired vs untrusted vs
        /// invalid (WS3-C.4) consume this and MUST NOT collapse non-Valid states into one message.
        public static SnapshotVerification VerifySnapshotDetailed(
            WitnessSnapshot snapshot, TimeSpan? maxAge = null, List<string>? trustedPublicKeys = null)
        {
            try
            {
                // B211: auto-load trust set when caller doesn't supply one, matching the pattern
                // LeakDetector.RunCollectorTestAsync uses (LeakDetector.cs:329). Without this,
                // the trust check is inert in every production caller (CLI + GUI).
                trustedPublicKeys ??= WitnessPinning.ConfiguredTrustedWitnessKeys().ToList();

                // v2 = legacy (errorMessage inside signed bytes); v3 = E020 (error text removed);
                // v4 = B252 current (errorMessage-free + canonical dohOutcomes + perResolverAnswers).
                // Verifier accepts all three so the platforms can migrate signer-side independently.
                if (snapshot.FormatVersion != 2 &&
                    snapshot.FormatVersion != SignedEvidencePayload.ErrorMessageFreeFormatVersion &&
                    snapshot.FormatVersion != SignedEvidencePayload.CurrentFormatVersion)
                {
                    return SnapshotVerification.UnsupportedFormat;
                }

                // B184: cryptographic verification MUST precede time-based and trust checks. A wholly
                // fabricated snapshot with a past ExpiresAt previously returned Expired — asserting the
                // file is an authentic-but-lapsed document. No verdict before evidence: the signature
                // must be valid before any claim about the snapshot's age or trust anchor. Windows and
                // Linux (via SyxxUI.Avalonia's ProjectReference to this Kit) now honor this order,
                // matching the macOS reference (WitnessSigner.swift:268-288).

                // 1. Parse public key + signature + canonical payload. An empty PublicKey and an
                // empty canonical payload (C070) both fold into MalformedInput here — matching macOS
                // WitnessSigner.swift:274-283.
                if (string.IsNullOrEmpty(snapshot.PublicKey))
                {
                    return SnapshotVerification.MalformedInput;
                }
                var pubKeyData = Convert.FromBase64String(snapshot.PublicKey);
                var publicKey = PublicKey.Import(Algorithm, pubKeyData, KeyBlobFormat.RawPublicKey);
                var sigData = Convert.FromBase64String(snapshot.Signature);
                var payloadBytes = snapshot.Evidence.CanonicalBytes();
                // C070: never verify against empty/zero-length bytes — macOS parity guard
                // matching `guard let payload = try? evidence.canonicalBytes() else { return .malformedInput }`.
                if (payloadBytes.Length == 0)
                {
                    return SnapshotVerification.MalformedInput;
                }

                // 2. Verify the Ed25519 signature — the load-bearing check, moved ahead of every
                // time/trust/display verdict so a forged snapshot is never masked as expired/stale/
                // untrusted. This is the C175 fix.
                if (!Algorithm.Verify(publicKey, payloadBytes, sigData))
                {
                    return SnapshotVerification.InvalidSignature;
                }

                // 3. B099: derive the key ID from the VERIFIED public key and reject a serialized
                // mismatch. An attacker cannot relabel a valid snapshot with a different keyId without
                // this catching it. Byte-identical to the sign side (WitnessSigner.cs:142) and to
                // macOS WitnessSigner.swift:180/293 (SHA-256 of the raw public key, first 8 bytes,
                // lowercase hex = 16 chars).
                var recomputedKeyId = Convert.ToHexString(SHA256.HashData(pubKeyData))[..16].ToLowerInvariant();
                if (!string.Equals(snapshot.KeyId, recomputedKeyId, StringComparison.Ordinal))
                {
                    return SnapshotVerification.DisplayMismatch;
                }

                // 4. Display-integrity guard: the cross-platform-stable projection of the display
                // fields (target domain, result, answer sets — all bools/strings that round-trip)
                // must still match the signed evidence, so a tampered Comparison/TargetDomain is
                // rejected even though those richer wire fields are not themselves signed.
                var rebuilt = SignedEvidencePayload.Build(
                    snapshot.TargetDomain, snapshot.Comparison, snapshot.Evidence.Timestamp, snapshot.Evidence.ExpiresAt,
                    snapshot.Evidence.FormatVersion);
                // D083: also validate the DISPLAY timestamp, expiresAt and formatVersion against the
                // signed payload fields. These are sourced from JSON and can be edited independently
                // of the signed bytes — without this check, editing Timestamp leaves Syxx printing
                // "VALID" beside a fabricated date. Millisecond equality (truncated, see D102).
                // D102: project the display DateTimes to epoch millis using the SAME truncating
                // conversion the signer used (ToUnixTimeMilliseconds, WitnessSigner.cs:80/82), NOT
                // Math.Round. Rounding here diverged from the signer's truncation whenever the
                // sub-millisecond tick fraction was >= 0.5 ms (~59% of DateTime.UtcNow samples),
                // yielding signedTs+1 and tripping the D083 equality guard below — so an honestly
                // signed snapshot was rejected as DisplayMismatch on a timing-dependent basis.
                ulong displayTimestampMillis =
                    (ulong)new DateTimeOffset(snapshot.Timestamp.ToUniversalTime()).ToUnixTimeMilliseconds();
                ulong? displayExpiresAtMillis = snapshot.ExpiresAt.HasValue
                    ? (ulong?)new DateTimeOffset(snapshot.ExpiresAt.Value.ToUniversalTime()).ToUnixTimeMilliseconds()
                    : null;
                if (rebuilt.QueryId != snapshot.Evidence.QueryId ||
                    !rebuilt.ComparisonResult.CanonicalEquals(snapshot.Evidence.ComparisonResult) ||
                    // [B037] per-resolver outcomes are signed evidence too — a tampered displayed
                    // resolver set (same aggregate result) must not verify as authentic.
                    !ResolverQueryOutcomeCanonical.SequenceCanonicalEquals(
                        rebuilt.ResolverQueryOutcomes, snapshot.Evidence.ResolverQueryOutcomes) ||
                    // [B098/B252] per-resolver answer records bind answer ownership to resolvers —
                    // a display-only rewrite of who answered what must not verify as authentic.
                    !ResolverAnswerRecordsCanonical.SequenceCanonicalEquals(
                        rebuilt.PerResolverAnswers, snapshot.Evidence.PerResolverAnswers) ||
                    // [B252] canonical DoH evidence is compared only at v4+: pre-v4 evidence never
                    // carried dohOutcomes, so comparing here would reject honest legacy snapshots.
                    (snapshot.FormatVersion >= SignedEvidencePayload.CurrentFormatVersion &&
                     !SignedEvidencePayload.DoHOutcomesCanonicalEquals(rebuilt.DohOutcomes, snapshot.Evidence.DohOutcomes)) ||
                    // [D083] display timestamp must agree with signed timestamp to millisecond precision.
                    displayTimestampMillis != snapshot.Evidence.Timestamp ||
                    // [D083] display expiresAt must agree (both null, or both matching ms value).
                    displayExpiresAtMillis != snapshot.Evidence.ExpiresAt ||
                    // [D083] formatVersion on the envelope must match the signed payload's version.
                    snapshot.FormatVersion != snapshot.Evidence.FormatVersion)
                {
                    return SnapshotVerification.DisplayMismatch;
                }

                // 5. Trust-set check (D011 + B211) — the signature is valid but the signer may not be
                // one the caller trusts. Empty/unconfigured set = no restriction (fail-open, until
                // Phase 1b; see the proposal §3).
                if (trustedPublicKeys != null && trustedPublicKeys.Count > 0 && !trustedPublicKeys.Contains(snapshot.PublicKey))
                {
                    return SnapshotVerification.UntrustedSigner;
                }

                // 6. Time-based checks — only meaningful on a cryptographically verified snapshot.
                // Checked against the SIGNED evidence (integer epoch millis that round-trip
                // cross-platform), not the display Timestamp/ExpiresAt mirrors.
                ulong nowMillis = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (snapshot.Evidence.ExpiresAt.HasValue && nowMillis > snapshot.Evidence.ExpiresAt.Value)
                {
                    return SnapshotVerification.Expired;
                }

                if (maxAge.HasValue)
                {
                    ulong maxAgeMillis = (ulong)Math.Max(0, maxAge.Value.TotalMilliseconds);
                    if (nowMillis > snapshot.Evidence.Timestamp && (nowMillis - snapshot.Evidence.Timestamp) > maxAgeMillis)
                    {
                        return SnapshotVerification.Stale;
                    }
                }

                // B085: a future-dated snapshot (signed timestamp ahead of local now beyond skew
                // tolerance) is rejected outright — it would otherwise bypass every max-age
                // freshness check forever.
                if (snapshot.Evidence.Timestamp > nowMillis &&
                    (snapshot.Evidence.Timestamp - nowMillis) > FutureSkewToleranceMillis)
                {
                    return SnapshotVerification.FutureDated;
                }

                return SnapshotVerification.Valid;
            }
            catch (Exception ex)
            {
                SyxxDiagnostics.Breadcrumb(category: "witness.verify", message: "Witness verification threw", level: DiagnosticSeverity.Warning, data: new Dictionary<string, string> { ["error"] = ex.GetType().Name });
                return SnapshotVerification.MalformedInput;
            }
        }
    }

    /// <summary>
    /// B211: auto-loaded trust set for witness snapshot signers, mirroring the pattern
    /// <see cref="LeakDetector.CollectorPinning"/> uses for collector signing keys. Loads from
    /// <c>SYXX_WITNESS_TRUSTED_KEYS</c> (env var) with fallback to a persisted key file at
    /// <c>~/.syxx/witness_trusted_keys.txt</c>. Empty set = no trust restriction (any
    /// cryptographically valid signer accepted; trust posture rendered as "unverified").
    /// </summary>
    public static class WitnessPinning
    {
        /// <summary>
        /// The configured Ed25519 signer trust set (base64 raw public keys) for witness snapshots.
        /// Comma-, space-, or newline-separated. An empty set means no trust restriction.
        /// </summary>
        public static ISet<string> ConfiguredTrustedWitnessKeys()
        {
            string raw = Environment.GetEnvironmentVariable("SYXX_WITNESS_TRUSTED_KEYS") ?? string.Empty;

            if (string.IsNullOrEmpty(raw))
            {
                raw = PersistedTrustedWitnessKey() ?? string.Empty;
            }

            var parts = raw.Split(new[] { ',', ' ', '\n', '\t', '\r' },
                                  StringSplitOptions.RemoveEmptyEntries);
            return new HashSet<string>(parts);
        }

        /// <summary>
        /// Returns the persisted trusted witness key, or null if none exists.
        /// </summary>
        public static string? PersistedTrustedWitnessKey()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var keyFile = System.IO.Path.Combine(appData, "Syxx", "witness_trusted_keys.txt");
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
    }

    /// <summary>
    /// E021: the distinct, non-collapsible outcomes of witness-snapshot verification. Everything
    /// except <see cref="Valid"/> fails verification, but the REASONS are forensically different
    /// (an expired honest snapshot is not a forged one) and surfaces must render them distinctly.
    /// </summary>
    public enum SnapshotVerification
    {
        Valid,
        /// <summary>Missing public key, undecodable base64, or a thrown parse failure.</summary>
        MalformedInput,
        /// <summary>Snapshot format version this verifier does not understand.</summary>
        UnsupportedFormat,
        /// <summary>The signed ExpiresAt has passed.</summary>
        Expired,
        /// <summary>Older than the caller-supplied max age.</summary>
        Stale,
        /// <summary>B085: signed timestamp is ahead of local now beyond skew tolerance.</summary>
        FutureDated,
        /// <summary>D011: signature verifies but the signer is not in the caller's trust set.</summary>
        UntrustedSigner,
        /// <summary>Display fields do not match the signed evidence (tampered presentation).</summary>
        DisplayMismatch,
        /// <summary>The Ed25519 signature check itself failed.</summary>
        InvalidSignature,
    }
}
