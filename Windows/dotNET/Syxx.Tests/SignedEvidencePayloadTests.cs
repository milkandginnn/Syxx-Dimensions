using System;
using System.Text;
using NSec.Cryptography;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// E011 — the marquee cross-platform trust bar (C# twin of the Swift E011ByteParityTests).
    /// The canonical signed evidence must serialize to byte-identical UTF-8 on Windows and macOS,
    /// so an Ed25519 signature made on one platform verifies on the other.
    /// </summary>
    public class SignedEvidencePayloadTests
    {
        // The exact canonical bytes both platforms must emit — identical literal to the Swift test.
        // Keys are fully sorted at every level (authoritativeSet, conclusion, vantageSet).
        private const string ExpectedCanonicalJson =
            "{\"comparisonResult\":{\"authoritativeSet\":[\"2606:2800::1\",\"93.184.216.34\"],\"conclusion\":\"diverges\",\"vantageSet\":[\"10.0.0.1\"]},\"formatVersion\":2,\"queryId\":\"münchen/a+b.example\",\"resolverQueryOutcomes\":[{\"resolverId\":\"1.1.1.1\",\"success\":true},{\"errorMessage\":\"timeout\",\"resolverId\":\"Cloudflare\",\"success\":false}],\"timestamp\":1704067200000}";

        // A Swift-produced Ed25519 public key + signature over the canonical bytes above (fixed key).
        // Ed25519 signing is deterministic (RFC 8032), so this literal verifies over exactly these
        // bytes and no others; verifying THIS Swift signature over C#'s independently-built bytes
        // proves both byte-parity and Swift→Windows cross-verification in one assertion: if C#'s
        // bytes differed by a single byte, the Swift signature would fail to verify. Regenerated
        // 2026-08-01 over the corrected (fully-sorted) bytes; the public key is unchanged.
        private const string SwiftPubKeyB64 = "IVL40Zt5HSRFMkLhXy6rbLfP+ntqXtMAl5YOBpiB2xI=";
        private const string SwiftSignatureB64 =
            "geqJGAQLxxQRJJ7P0QBvO0b/3nY78W6FASwtCnXByLTDmLWmXWpIRmqpffC1hblZUmZFd3TnRiNa77b99jk1Dw==";

        private static SignedEvidencePayload MakeFixturePayload() => new()
        {
            FormatVersion = 2,
            Timestamp = 1704067200000UL,
            QueryId = "münchen/a+b.example",
            ResolverQueryOutcomes = new()
            {
                new ResolverQueryOutcomeCanonical { ResolverId = "1.1.1.1", Success = true },
                new ResolverQueryOutcomeCanonical { ResolverId = "Cloudflare", Success = false, ErrorMessage = "timeout" }
            },
            ComparisonResult = new ComparisonResultCanonical
            {
                Result = "diverges",
                AuthoritativeSet = new() { "2606:2800::1", "93.184.216.34" },
                VantageSet = new() { "10.0.0.1" }
            },
            ExpiresAt = null,
            DohOutcomes = null
        };

        [Fact]
        public void CanonicalBytesMatchCrossPlatformFixture()
        {
            var bytes = MakeFixturePayload().CanonicalBytes();
            Assert.Equal(ExpectedCanonicalJson, Encoding.UTF8.GetString(bytes));
        }

        [Fact]
        public void VerifiesSwiftProducedSignatureOverOwnBytes()
        {
            var bytes = MakeFixturePayload().CanonicalBytes();
            var algorithm = SignatureAlgorithm.Ed25519;
            var pub = PublicKey.Import(algorithm, Convert.FromBase64String(SwiftPubKeyB64), KeyBlobFormat.RawPublicKey);
            var sig = Convert.FromBase64String(SwiftSignatureB64);
            Assert.True(algorithm.Verify(pub, bytes, sig));
        }

        [Fact]
        public void SetOrderingIsDeterministic()
        {
            var a = SignedEvidencePayload.SortedUnique(new[] { "9.9.9.9", "1.1.1.1", "8.8.8.8" });
            var b = SignedEvidencePayload.SortedUnique(new[] { "8.8.8.8", "1.1.1.1", "9.9.9.9" });
            Assert.Equal(a, b);
            Assert.Equal(new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" }, a);
        }

        // --- Failure cases — these MUST go RED if canonical serialization or Ed25519 integrity breaks. ---

        /// <summary>Flipping the result must change the signed bytes AND invalidate a signature made
        /// over the original. Red if the serializer drops `result` or verification is slack.</summary>
        [Fact]
        public void TamperedResultBreaksSignature()
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            using var key = Key.Create(algorithm);
            var originalBytes = MakeFixturePayload().CanonicalBytes();
            var sig = algorithm.Sign(key, originalBytes);
            Assert.True(algorithm.Verify(key.PublicKey, originalBytes, sig));   // sanity

            var tampered = MakeFixturePayload();
            tampered.ComparisonResult.Result = "matches";
            var tamperedBytes = tampered.CanonicalBytes();
            Assert.NotEqual(originalBytes, tamperedBytes);
            Assert.False(algorithm.Verify(key.PublicKey, tamperedBytes, sig));
        }

        /// <summary>Swapping one authoritative-set IP must change the signed bytes.</summary>
        [Fact]
        public void TamperedAnswerSetChangesBytes()
        {
            var originalBytes = MakeFixturePayload().CanonicalBytes();
            var tampered = MakeFixturePayload();
            tampered.ComparisonResult.AuthoritativeSet = new() { "2606:2800::1", "6.6.6.6" };
            Assert.NotEqual(originalBytes, tampered.CanonicalBytes());
        }

        /// <summary>A forged (non-signature) blob must be rejected.</summary>
        [Fact]
        public void ForgedSignatureIsRejected()
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            var bytes = MakeFixturePayload().CanonicalBytes();
            var pub = PublicKey.Import(algorithm, Convert.FromBase64String(SwiftPubKeyB64), KeyBlobFormat.RawPublicKey);
            var forged = new byte[64]; forged[0] = 0x01;
            Assert.False(algorithm.Verify(pub, bytes, forged));
        }

        /// <summary>A genuine signature verified under a DIFFERENT key must be rejected.</summary>
        [Fact]
        public void SignatureUnderWrongKeyIsRejected()
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            var bytes = MakeFixturePayload().CanonicalBytes();
            using var key = Key.Create(algorithm);
            var sig = algorithm.Sign(key, bytes);
            using var wrongKey = Key.Create(algorithm);
            Assert.False(algorithm.Verify(wrongKey.PublicKey, bytes, sig));
        }

        // --- B252: v4 canonical DoH signing anchors (twin of the Swift E011ByteParityTests block). ---

        /// <summary>The v4 golden literal — identical to the Swift test's literal. Exercises the
        /// B252 additions: canonical <c>dohOutcomes</c> (typed success, resolverId-sorted,
        /// errorMessage-free) and <c>perResolverAnswers</c> (sorted records), plus v3 semantics.</summary>
        private const string ExpectedV4CanonicalJson =
            "{\"comparisonResult\":{\"authoritativeSet\":[\"93.184.216.34\"],\"authoritativeTtls\":[216],\"authoritativeTypes\":[1],\"conclusion\":\"matches\",\"vantageSet\":[\"93.184.216.34\"]},\"dohOutcomes\":{\"outcomes\":[{\"resolverId\":\"Cloudflare\",\"success\":true},{\"resolverId\":\"Google\",\"success\":false}]},\"formatVersion\":4,\"perResolverAnswers\":[{\"records\":[{\"name\":\"example.com\",\"rdata\":\"93.184.216.34\",\"ttl\":300,\"type\":1}],\"resolverId\":\"1.1.1.1\"},{\"records\":[{\"name\":\"example.com\",\"rdata\":\"93.184.216.34\",\"ttl\":300,\"type\":1}],\"resolverId\":\"10.0.0.1\"},{\"records\":[{\"name\":\"example.com\",\"rdata\":\"93.184.216.34\",\"ttl\":216,\"type\":1}],\"resolverId\":\"Cloudflare\"}],\"queryId\":\"münchen/a+b.example\",\"resolverQueryOutcomes\":[{\"resolverId\":\"1.1.1.1\",\"success\":true},{\"resolverId\":\"10.0.0.1\",\"success\":true},{\"resolverId\":\"Cloudflare\",\"success\":true}],\"timestamp\":1704067200000}";

        // B252: signatures over the v4 canonical bytes (fixed 32*0x42 seed, same keypair — the
        // derived public key matches SwiftPubKeyB64). Both platforms' signatures are pinned so each
        // suite proves the OTHER platform's signature verifies over ITS OWN independently-built bytes.
        private const string V4SwiftSignatureB64 =
            "8YjrILke7PagrzwE0CovFvj4KWzx81kJ2ioNl2WsMDXBlsquprHyDjCqcln/6TvqnHQPFEYmuvwzzaMEk6diCQ==";
        private const string V4CSharpSignatureB64 =
            "lzilx0FgTfuDAUWTIffMi+l3h8yV1bxw/rjYb3N4ViucJTpTn4Z7r+19EMnD+5ioEVEhUEhMzIiDBrfD7inyDg==";

        private static SignedEvidencePayload MakeV4FixturePayload() => new()
        {
            FormatVersion = SignedEvidencePayload.CurrentFormatVersion,
            Timestamp = 1704067200000UL,
            QueryId = "münchen/a+b.example",
            ResolverQueryOutcomes = new()
            {
                new ResolverQueryOutcomeCanonical { ResolverId = "1.1.1.1", Success = true },
                new ResolverQueryOutcomeCanonical { ResolverId = "10.0.0.1", Success = true },
                new ResolverQueryOutcomeCanonical { ResolverId = "Cloudflare", Success = true }
            },
            ComparisonResult = new ComparisonResultCanonical
            {
                Result = "matches",
                AuthoritativeSet = new() { "93.184.216.34" },
                VantageSet = new() { "93.184.216.34" },
                AuthoritativeTtls = new() { 216 },
                AuthoritativeTypes = new() { 1 }
            },
            ExpiresAt = null,
            DohOutcomes = new DoHOutcomesCanonical
            {
                Outcomes = new()
                {
                    new ResolverQueryOutcomeCanonical { ResolverId = "Cloudflare", Success = true },
                    new ResolverQueryOutcomeCanonical { ResolverId = "Google", Success = false }
                }
            },
            PerResolverAnswers = new()
            {
                new ResolverAnswerRecordsCanonical
                {
                    ResolverId = "1.1.1.1",
                    Records = new() { new CanonicalRecord { Name = "example.com", Type = 1, Ttl = 300, Rdata = "93.184.216.34" } }
                },
                new ResolverAnswerRecordsCanonical
                {
                    ResolverId = "10.0.0.1",
                    Records = new() { new CanonicalRecord { Name = "example.com", Type = 1, Ttl = 300, Rdata = "93.184.216.34" } }
                },
                new ResolverAnswerRecordsCanonical
                {
                    ResolverId = "Cloudflare",
                    Records = new() { new CanonicalRecord { Name = "example.com", Type = 1, Ttl = 216, Rdata = "93.184.216.34" } }
                }
            }
        };

        [Fact]
        public void V4CanonicalBytesMatchCrossPlatformFixture()
        {
            var bytes = MakeV4FixturePayload().CanonicalBytes();
            Assert.Equal(ExpectedV4CanonicalJson, Encoding.UTF8.GetString(bytes));
        }

        /// <summary>Swift-produced v4 signature must verify over C#'s independently-built bytes —
        /// proves byte-parity of the new canonical fields AND Swift→Windows cross-verification.</summary>
        [Fact]
        public void V4VerifiesSwiftProducedSignatureOverOwnBytes()
        {
            var bytes = MakeV4FixturePayload().CanonicalBytes();
            var algorithm = SignatureAlgorithm.Ed25519;
            var pub = PublicKey.Import(algorithm, Convert.FromBase64String(SwiftPubKeyB64), KeyBlobFormat.RawPublicKey);
            var sig = Convert.FromBase64String(V4SwiftSignatureB64);
            Assert.True(algorithm.Verify(pub, bytes, sig));
        }

        /// <summary>The C# v4 signature over the byte-identical canonical bytes verifies (and is
        /// pinned so the Swift suite can prove Windows→macOS verification against it).</summary>
        [Fact]
        public void V4CSharpSignatureVerifiesOverOwnBytes()
        {
            var bytes = MakeV4FixturePayload().CanonicalBytes();
            var algorithm = SignatureAlgorithm.Ed25519;
            var pub = PublicKey.Import(algorithm, Convert.FromBase64String(SwiftPubKeyB64), KeyBlobFormat.RawPublicKey);
            var sig = Convert.FromBase64String(V4CSharpSignatureB64);
            Assert.True(algorithm.Verify(pub, bytes, sig));
        }

        /// <summary>B252 marquee failure case: rewriting the CANONICAL DoH evidence (signed at v4)
        /// must invalidate the signature — flipped Google outcome (transportError → success).</summary>
        [Fact]
        public void V4TamperingCanonicalDoHEvidenceIsRejected()
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            using var key = Key.Create(algorithm);
            var original = MakeV4FixturePayload();
            var originalBytes = original.CanonicalBytes();
            var sig = algorithm.Sign(key, originalBytes);
            Assert.True(algorithm.Verify(key.PublicKey, originalBytes, sig));   // sanity

            original.DohOutcomes!.Outcomes[1] = new ResolverQueryOutcomeCanonical { ResolverId = "Google", Success = true };
            var tamperedBytes = original.CanonicalBytes();
            Assert.NotEqual(originalBytes, tamperedBytes);
            Assert.False(algorithm.Verify(key.PublicKey, tamperedBytes, sig));
        }

        /// <summary>B098/B252 failure case: swapping answer records between resolvers (equal
        /// aggregate answers, changed ownership) must invalidate the signature.</summary>
        [Fact]
        public void V4TamperingCanonicalPerResolverAnswersIsRejected()
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            using var key = Key.Create(algorithm);
            var original = MakeV4FixturePayload();
            var originalBytes = original.CanonicalBytes();
            var sig = algorithm.Sign(key, originalBytes);
            Assert.True(algorithm.Verify(key.PublicKey, originalBytes, sig));   // sanity

            original.PerResolverAnswers![0].Records[0].Ttl = 216;   // attacker moves Cloudflare's TTL into 1.1.1.1's record
            original.PerResolverAnswers![2].Records[0].Ttl = 300;
            var tamperedBytes = original.CanonicalBytes();
            Assert.NotEqual(originalBytes, tamperedBytes);
            Assert.False(algorithm.Verify(key.PublicKey, tamperedBytes, sig));
        }
    }
}
