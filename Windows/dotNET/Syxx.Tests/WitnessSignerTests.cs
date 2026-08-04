using System;
using System.Collections.Generic;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    public class WitnessSignerTests
    {
        private static ComparisonResult MakeComparison(string domain = "example.com", bool redirected = false)
        {
            var record = new DNSRecord { Name = domain, Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            return new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                PublicResolverResults = new Dictionary<string, List<DNSRecord>> { { "Cloudflare", new List<DNSRecord> { record } } },
                Result = redirected ? VantageComparisonResult.RedirectionDetected : VantageComparisonResult.Consistent
            };
        }

        private static (string PrivateKey, string PublicKey) TestKeyPair => WitnessSigner.GenerateKeyPair();

        [Fact]
        public void VerifiesWithCorrectKey()
        {
            var (priv, pub) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snapshot));
            Assert.Equal(pub, snapshot.PublicKey);
            Assert.False(string.IsNullOrEmpty(snapshot.KeyId));
        }

        [Fact]
        public void RejectsEmptyKeyOnCreate()
        {
            Assert.Throws<WitnessSignerException>(() => WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), ""));
        }

        [Fact]
        public void RejectsEmptyPublicKey()
        {
            // A bare snapshot fails for many reasons (over-determined). Start from a VALID signed
            // snapshot and blank ONLY the public key, so the property tested is "an otherwise-valid
            // snapshot with no public key must not verify" — parity with the macOS test.
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            snapshot.PublicKey = "";
            // an otherwise-valid snapshot with an empty public key must be rejected
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snapshot));
        }

        [Fact]
        public void RejectsTamperedComparison()
        {
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(redirected: false), priv);
            snapshot.Comparison = MakeComparison(redirected: true); // attacker flips the result after signing
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snapshot));
        }

        // [B037] The display-integrity guard must also cover per-resolver outcomes. A tampered display
        // that keeps the aggregate result/answer-sets identical (extra injected resolver, same rdata,
        // so ComparisonResult is unchanged) but alters resolverQueryOutcomes must be rejected.
        [Fact]
        public void RejectsTamperedResolverOutcomes()
        {
            var (priv, _) = TestKeyPair;
            var record = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            var ok = new ResolverQueryOutcome { Status = DNSResponseStatus.Success, Records = new List<DNSRecord> { record } };
            // C# Build() reads PublicResolverOutcomes (not the raw ...Results), so populate it.
            var original = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                LocalOutcome = ok,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome> { { "Cloudflare", ok } },
                Result = VantageComparisonResult.Consistent
            };
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", original, priv);
            // Inject an extra resolver with the SAME rdata: authoritative set + result are unchanged
            // (so ComparisonResult still matches), but resolverQueryOutcomes gains an entry.
            snapshot.Comparison = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                LocalOutcome = ok,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
                {
                    { "Cloudflare", ok },
                    { "EvilInjectedResolver", ok }
                },
                Result = VantageComparisonResult.Consistent
            };
            // a tampered displayed resolver set must not verify, even when the result is unchanged
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snapshot));
        }

        [Fact]
        public void RejectsTamperedDomain()
        {
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            snapshot.TargetDomain = "attacker-controlled.com";
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snapshot));
        }

        // B085 — a snapshot signed with a FUTURE timestamp defeats every max-age freshness check
        // by construction; an honest signer cannot produce one, so it must be rejected outright.
        [Fact]
        public void RejectsFutureDatedSnapshot()
        {
            var (priv, _) = TestKeyPair;
            var future = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", MakeComparison(), priv, expiresAt: null,
                timestampOverride: DateTime.UtcNow.AddHours(1));
            Assert.Equal(SnapshotVerification.FutureDated, WitnessSigner.VerifySnapshotDetailed(future));
            // a future-dated snapshot must not verify even with a valid signature
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(future));
        }

        // B085 failure case for the failure case: honest clock drift within the skew tolerance
        // must NOT be rejected — the guard must not over-block real snapshots.
        [Fact]
        public void AcceptsSnapshotWithinClockSkewTolerance()
        {
            var (priv, _) = TestKeyPair;
            var slightlyAhead = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", MakeComparison(), priv, expiresAt: null,
                timestampOverride: DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(slightlyAhead));
        }

        // E021 — expired vs stale vs untrusted vs invalid-signature are forensically DIFFERENT
        // failures and must stay distinct; collapsing them into one bool false was the defect.
        [Fact]
        public void DetailedVerificationPreservesDistinctFailureStates()
        {
            var (priv, pub) = TestKeyPair;

            var expired = WitnessSigner.CreateSignedSnapshot(
                "example.com", MakeComparison(), priv, expiresAt: DateTime.UtcNow.AddMinutes(-5));
            Assert.Equal(SnapshotVerification.Expired, WitnessSigner.VerifySnapshotDetailed(expired));

            var stale = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", MakeComparison(), priv, expiresAt: null,
                timestampOverride: DateTime.UtcNow.AddHours(-2));
            Assert.Equal(SnapshotVerification.Stale,
                WitnessSigner.VerifySnapshotDetailed(stale, maxAge: TimeSpan.FromHours(1)));

            var untrusted = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            var (otherPriv, otherPub) = WitnessSigner.GenerateKeyPair();
            _ = otherPriv;
            Assert.Equal(SnapshotVerification.UntrustedSigner,
                WitnessSigner.VerifySnapshotDetailed(untrusted, trustedPublicKeys: new List<string> { otherPub }));
            Assert.Equal(SnapshotVerification.Valid,
                WitnessSigner.VerifySnapshotDetailed(untrusted, trustedPublicKeys: new List<string> { pub }));

            var forged = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            var sigBytes = Convert.FromBase64String(forged.Signature);
            sigBytes[0] ^= 0xFF;
            forged.Signature = Convert.ToBase64String(sigBytes);
            Assert.Equal(SnapshotVerification.InvalidSignature, WitnessSigner.VerifySnapshotDetailed(forged));
        }

        // E020 — format v3 removes free-text errorMessage from the signed canonical bytes.
        // The verifier must accept BOTH v2 (legacy) and v3 (migration is verifier-first: signers
        // flip to v3 only after both platforms' verifiers ship).
        [Fact]
        public void FormatV3ExcludesErrorTextAndStillVerifies()
        {
            var (priv, _) = TestKeyPair;
            var record = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            var failed = new ResolverQueryOutcome
            {
                Status = DNSResponseStatus.Timeout,
                Records = new List<DNSRecord>(),
                ErrorMessage = "platform-specific exception text that must not be signed"
            };
            var comparison = new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { record },
                LocalOutcome = failed,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome> { { "Cloudflare", failed } },
                Result = VantageComparisonResult.Consistent
            };

            var v3 = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", comparison, priv, expiresAt: null, timestampOverride: null,
                formatVersion: SignedEvidencePayload.ErrorMessageFreeFormatVersion);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(v3));

            // The defect under test: error text present in the wire outcome must NOT reach the
            // signed bytes in v3. If Build() regresses to including it, this fails.
            var signedJson = System.Text.Encoding.UTF8.GetString(v3.Evidence.CanonicalBytes());
            Assert.DoesNotContain("platform-specific exception text", signedJson);

            // Failure case: the same comparison signed as v2 DOES carry the text (legacy shape,
            // proving the v3 assertion above is not vacuous), and still verifies.
            var v2 = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", comparison, priv, expiresAt: null, timestampOverride: null, formatVersion: 2);
            var v2Json = System.Text.Encoding.UTF8.GetString(v2.Evidence.CanonicalBytes());
            Assert.Contains("platform-specific exception text", v2Json);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(v2));

            // An unknown future version is refused, not guessed at.
            var v9 = WitnessSigner.CreateSignedSnapshotAt(
                "example.com", comparison, priv, expiresAt: null, timestampOverride: null, formatVersion: 9);
            Assert.Equal(SnapshotVerification.UnsupportedFormat, WitnessSigner.VerifySnapshotDetailed(v9));
        }

        [Fact]
        public void DomainWithDelimiterDoesNotCollide()
        {
            // Regression test — structured JSON payload means delimiter chars cannot collide.
            var (priv, _) = TestKeyPair;
            var comparisonA = MakeComparison("evil|{\"IsRedirectionDetected\":true}");
            var comparisonB = MakeComparison("evil");
            var snapshotA = WitnessSigner.CreateSignedSnapshot("evil|{\"IsRedirectionDetected\":true}", comparisonA, priv);

            var crossed = new WitnessSnapshot
            {
                FormatVersion = 2,
                Timestamp = snapshotA.Timestamp,
                TargetDomain = "evil",
                Comparison = comparisonB,
                Evidence = snapshotA.Evidence,
                Signature = snapshotA.Signature,
                PublicKey = snapshotA.PublicKey,
                KeyId = snapshotA.KeyId
            };
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(crossed));
        }

        // [GV-A/A′/B] Combined-state vectors (Phase 0). Every case above exercises ONE failure in
        // isolation, so the crypto-LAST ordering is invisible. These prove the invariant: a
        // non-crypto verdict (Expired/UntrustedSigner) must NEVER be returned ahead of the
        // crypto/display failure it would mask. RED on the current crypto-LAST Kit; GREEN after
        // Phase 1a. See docs/PROPOSAL-witness-crypto-first-2026-08-03.md §4.

        [Fact]
        public void GVA_ForgedAndExpired_IsInvalidSignature_NotExpired()
        {
            // GV-A: forged signature AND a past ExpiresAt. Crypto-first => InvalidSignature.
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot(
                "example.com", MakeComparison(), priv, expiresAt: DateTime.UtcNow.AddMinutes(-5));
            var sigBytes = Convert.FromBase64String(snapshot.Signature);
            sigBytes[0] ^= 0xFF;
            snapshot.Signature = Convert.ToBase64String(sigBytes);
            var v = WitnessSigner.VerifySnapshotDetailed(snapshot);
            Assert.Equal(SnapshotVerification.InvalidSignature, v);
            Assert.NotEqual(SnapshotVerification.Expired, v);
        }

        [Fact]
        public void GVAPrime_ForgedAndUntrusted_IsInvalidSignature_NotUntrusted()
        {
            // GV-A′: forged signature signed by a key OUTSIDE the trust set. Crypto-first =>
            // InvalidSignature; a trust-before-crypto verifier returns UntrustedSigner and masks it.
            var (priv, _) = TestKeyPair;
            var (otherPriv, otherPub) = WitnessSigner.GenerateKeyPair();
            _ = otherPriv;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            var sigBytes = Convert.FromBase64String(snapshot.Signature);
            sigBytes[0] ^= 0xFF;
            snapshot.Signature = Convert.ToBase64String(sigBytes);
            var v = WitnessSigner.VerifySnapshotDetailed(
                snapshot, trustedPublicKeys: new List<string> { otherPub });
            Assert.Equal(SnapshotVerification.InvalidSignature, v);
            Assert.NotEqual(SnapshotVerification.UntrustedSigner, v);
        }

        [Fact]
        public void GVB_DisplayTamperedAndExpired_IsDisplayMismatch_NotExpired()
        {
            // GV-B: honest signature + past ExpiresAt + display timestamp edited post-sign.
            // Display-integrity is checked before time => DisplayMismatch, not the masking Expired.
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot(
                "example.com", MakeComparison(), priv, expiresAt: DateTime.UtcNow.AddMinutes(-5));
            snapshot.Timestamp = snapshot.Timestamp.AddMinutes(1);   // display-only edit
            var v = WitnessSigner.VerifySnapshotDetailed(snapshot);
            Assert.Equal(SnapshotVerification.DisplayMismatch, v);
            Assert.NotEqual(SnapshotVerification.Expired, v);
        }

        // [B099] A valid snapshot RELABELED with a different keyId must be rejected: the verifier
        // recomputes the keyId from the VERIFIED public key and compares (WitnessSigner.cs step 3).
        // macOS has always done this (WitnessSigner.swift:293); Phase 1a ports it to Windows/Linux.
        // Without the recompute a relabeled-but-valid snapshot reads Valid — this is the §2 mutation guard.
        [Fact]
        public void RelabeledKeyIdOnAnOtherwiseValidSnapshotIsDisplayMismatch()
        {
            var (priv, _) = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshotDetailed(snapshot));
            // Flip the first hex char of the 16-char keyId so it no longer matches SHA256(pubKey)[..8].
            var original = snapshot.KeyId;
            snapshot.KeyId = (original[0] == '0' ? '1' : '0') + original.Substring(1);
            Assert.NotEqual(original, snapshot.KeyId);
            Assert.Equal(SnapshotVerification.DisplayMismatch, WitnessSigner.VerifySnapshotDetailed(snapshot));
        }

        [Fact]
        public void KeyPairGeneration()
        {
            var (priv1, pub1) = WitnessSigner.GenerateKeyPair();
            var (priv2, pub2) = WitnessSigner.GenerateKeyPair();
            Assert.False(string.IsNullOrEmpty(priv1));
            Assert.False(string.IsNullOrEmpty(pub1));
            Assert.NotEqual(priv1, priv2);
            Assert.NotEqual(pub1, pub2);
            // Distinct non-empty strings don't prove a WORKING Ed25519 pair. Round-trip: a snapshot
            // signed with priv1 verifies, and the same snapshot with pub2 swapped in must NOT.
            var snap = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), priv1);
            // a freshly generated pair must sign+verify
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snap));
            snap.PublicKey = pub2;
            // a snapshot verified under a DIFFERENT generated public key must be rejected
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snap));
        }

        [Fact]
        public void ThirdPartyCannotForge()
        {
            // Third party with only the public key can verify but cannot forge.
            var (priv, _) = TestKeyPair;
            var (attackerPriv, _) = TestKeyPair;
            var comparison = MakeComparison();
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", comparison, priv);
            Assert.Equal(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(snapshot));

            // Attacker signs with their own key but stamps target's public key
            var forged = WitnessSigner.CreateSignedSnapshot("evil.com", comparison, attackerPriv);
            var forgedSnapshot = new WitnessSnapshot
            {
                FormatVersion = 2,
                Timestamp = forged.Timestamp,
                TargetDomain = "evil.com",
                Comparison = forged.Comparison,
                Evidence = forged.Evidence,
                Signature = forged.Signature,
                PublicKey = snapshot.PublicKey,
                KeyId = snapshot.KeyId
            };
            Assert.NotEqual(SnapshotVerification.Valid, WitnessSigner.VerifySnapshot(forgedSnapshot));
        }
    }
}
