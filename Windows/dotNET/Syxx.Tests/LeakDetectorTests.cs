using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NSec.Cryptography;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    public class LeakResultReconciliationTests
    {
        [Fact]
        public void ConfirmedRequiresAttestedResolver()
        {
            var ok = LeakDetector.ReconcileResult(true, new List<string> { "9.9.9.9" });
            Assert.Equal(LeakResult.Confirmed, ok.Result);
            Assert.Null(ok.Reason);
            // A "leak" with no attested resolver cannot be attributed -> inconclusive, not confirmed.
            var bad = LeakDetector.ReconcileResult(true, new List<string>());
            Assert.Equal(LeakResult.Inconclusive, bad.Result);
            Assert.NotNull(bad.Reason);
        }

        [Fact]
        public void NoLeakMustNotAttestResolvers()
        {
            var ok = LeakDetector.ReconcileResult(false, new List<string>());
            Assert.Equal(LeakResult.NotDetected, ok.Result);
            // "No leak" yet resolvers were observed is internally inconsistent -> inconclusive.
            var bad = LeakDetector.ReconcileResult(false, new List<string> { "8.8.8.8" });
            Assert.Equal(LeakResult.Inconclusive, bad.Result);
            Assert.NotNull(bad.Reason);
        }
    }

    // D003 collector TLS transport pinning — mirrors the macOS SyxxKitTests D003 subset.
    public class CollectorPinningTests
    {
        // Same self-signed EC P-256 leaf as the macOS fixture; reference pin computed independently
        // with openssl (pin-sha256). Proves the SPKI hash is byte-identical across platforms — the
        // owner supplies ONE pin value for both.
        private const string CertDerBase64 =
            "MIIBfDCCASOgAwIBAgIUTRwDvN3VdQMU+Vgu/3G+NDSnXvAwCgYIKoZIzj0EAwIwFDESMBAGA1UEAwwJbG9jYWxob3N0MB4XDTI2MDcxMTA1Mzk0N1oXDTI2MDcxMzA1Mzk0N1owFDESMBAGA1UEAwwJbG9jYWxob3N0MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnDqyTcvfozZQLCIGJPfuNp8TvBoeC9GtV732MxjUiORG6Tn8e4Lq5RNv09LBBEzRqewDcavyeuzPcYJGw1+t1qNTMFEwHQYDVR0OBBYEFIyTOb3tNTI/9HRcTy8hz+X01XecMB8GA1UdIwQYMBaAFIyTOb3tNTI/9HRcTy8hz+X01XecMA8GA1UdEwEB/wQFMAMBAf8wCgYIKoZIzj0EAwIDRwAwRAIgZ94hEPzAJIf7TvC9FGIF8nW3HWwV22FkelWD9/re8bICIDKN3rUFOCrm35JDQ58hIAHxLvaNF2+1msCqXkuuVJ1k";
        private const string ReferencePin = "FfEkilG5Q/bHox+eHUsh67GrlG2i/dtxUUDFWa4sQXY=";

        [Fact]
        public void SpkiPinMatchesReferencePin()
        {
            var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(CertDerBase64));
            Assert.Equal(ReferencePin, CollectorPinning.SpkiPin(cert));
        }

        [Fact]
        public void AcceptIsFailClosed()
        {
            var good = ReferencePin;
            var other = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
            var configured = new HashSet<string> { good };
            // PKI must pass first — an untrusted chain is refused regardless of pins.
            Assert.False(CollectorPinning.Accept(false, good, configured));
            // Unconfigured: standard validation only.
            Assert.True(CollectorPinning.Accept(true, good, new HashSet<string>()));
            // Configured + matching leaf -> accept.
            Assert.True(CollectorPinning.Accept(true, good, new HashSet<string> { good, other }));
            // Configured + NON-matching leaf -> refuse (fail-closed). THE mutation guard.
            Assert.False(CollectorPinning.Accept(true, other, configured));
            // Configured but leaf pin could not be computed -> refuse, never accept.
            Assert.False(CollectorPinning.Accept(true, null, configured));
        }

        [Fact]
        public void IsTransportIdentityFailureDetectsAuthException()
        {
            var pinFail = new HttpRequestException("handshake",
                new AuthenticationException("The remote certificate is invalid."));
            Assert.True(LeakDetector.IsTransportIdentityFailure(pinFail));
            // A plain HTTP error (e.g. non-2xx) is NOT a transport-identity failure.
            Assert.False(LeakDetector.IsTransportIdentityFailure(new HttpRequestException("HTTP 500")));
            Assert.False(LeakDetector.IsTransportIdentityFailure(new TimeoutException()));
        }
    }

    public class LeakDetectorTrustTests
    {
        [Fact]
        public void CheckTrustSet_KeyInSet_ReturnsTrusted()
        {
            var key = Key.Create(SignatureAlgorithm.Ed25519);
            var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var trustSet = new HashSet<string> { pubB64, "some-other-key" };

            var result = LeakDetector.CheckTrustSet(pubB64, trustSet);

            Assert.True(result, "A key present in the trust set must be trusted");
        }

        [Fact]
        public void CheckTrustSet_KeyNotInSet_ReturnsUntrusted()
        {
            var key = Key.Create(SignatureAlgorithm.Ed25519);
            var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var otherKey = Key.Create(SignatureAlgorithm.Ed25519);
            var otherPubB64 = Convert.ToBase64String(otherKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var trustSet = new HashSet<string> { otherPubB64 };

            var result = LeakDetector.CheckTrustSet(pubB64, trustSet);

            Assert.False(result, "A key NOT in the trust set must be untrusted");
        }

        [Fact]
        public void CheckTrustSet_NullPublicKey_ReturnsNull()
        {
            var trustSet = new HashSet<string> { "some-key" };

            var result = LeakDetector.CheckTrustSet(null, trustSet);

            Assert.True(result is null, "A null public key must return null (check not performed)");
        }

        [Fact]
        public void CheckTrustSet_NullTrustSet_ReturnsNull()
        {
            var result = LeakDetector.CheckTrustSet("some-key", null);

            Assert.True(result is null, "A null trust set must return null (check not performed)");
        }

        [Fact]
        public void CheckTrustSet_EmptyTrustSet_ReturnsNull()
        {
            var emptySet = new HashSet<string>();

            var result = LeakDetector.CheckTrustSet("some-key", emptySet);

            Assert.True(result is null, "An empty trust set must return null (check not performed)");
        }

        [Fact]
        public void Verify_ThenCheckTrustSet_EndToEnd()
        {
            // Generate a real Ed25519 keypair and sign a known payload.
            var key = Key.Create(SignatureAlgorithm.Ed25519);
            var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var payload = System.Text.Encoding.UTF8.GetBytes("D011-test-payload");
            var sigB64 = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, payload));
            var payloadB64 = Convert.ToBase64String(payload);

            var result = new LeakTestResult
            {
                Result = LeakResult.NotDetected,
                Signature = sigB64,
                PublicKey = pubB64,
                SignedPayload = payloadB64
            };

            // Crypto verification must pass
            Assert.True(LeakDetector.Verify(result), "A genuine signature must verify");

            // Trust set containing the key → trusted
            var trustedSet = new HashSet<string> { pubB64, "some-other-key" };
            Assert.True(LeakDetector.CheckTrustSet(pubB64, trustedSet),
                "A verified signer whose key is in the trust set must be trusted");

            // Trust set NOT containing the key → not trusted
            var otherKey = Key.Create(SignatureAlgorithm.Ed25519);
            var otherPubB64 = Convert.ToBase64String(otherKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));
            var untrustedSet = new HashSet<string> { otherPubB64 };
            Assert.False(LeakDetector.CheckTrustSet(pubB64, untrustedSet),
                "A verified signer whose key is NOT in the trust set must be untrusted");
        }
    }

    // B091 — result-bearing fields must come from the SIGNED payload, never the outer JSON;
    // C031 — an expected-resolver baseline keeps the user's own resolver from reading as a leak.
    public class LeakSignedAttestationTests
    {
        private static string PayloadB64(string json) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        [Fact]
        public void ParseIsStrict_MissingFieldsParseToNull()
        {
            Assert.NotNull(LeakDetector.ParseSignedAttestation(
                PayloadB64("{\"leaked\":true,\"observedResolvers\":[\"9.9.9.9\"]}")));
            // Failure cases: a payload missing either result-bearing field must NOT default.
            Assert.Null(LeakDetector.ParseSignedAttestation(PayloadB64("{\"observedResolvers\":[]}")));
            Assert.Null(LeakDetector.ParseSignedAttestation(PayloadB64("{\"leaked\":false}")));
            Assert.Null(LeakDetector.ParseSignedAttestation(PayloadB64("not json")));
            Assert.Null(LeakDetector.ParseSignedAttestation("!!!not-base64!!!"));
        }

        [Fact]
        public void ValidSignature_ResultComesFromSignedFields_NotOuter()
        {
            // Signed payload says LEAKED via 9.9.9.9; a forging relay flipped the outer JSON to
            // "no leak". The result must follow the SIGNED fields... but an outer/signed mismatch
            // is itself proof of tampering in transit, so the honest answer is Inconclusive —
            // never the relay's "no leak".
            var signed = PayloadB64("{\"leaked\":true,\"observedResolvers\":[\"9.9.9.9\"]}");
            var (result, observed, _) = LeakDetector.DeriveResult(
                sigValid: true, signed, outerLeaked: false, outerObserved: new List<string>(),
                expectedResolvers: null);
            Assert.Equal(LeakResult.Inconclusive, result);
            // The displayed resolver set must be the SIGNED one, not the relay's stripped list.
            Assert.Equal(new List<string> { "9.9.9.9" }, observed);
        }

        [Fact]
        public void ValidSignature_MatchingOuter_DerivesFromSigned()
        {
            var signed = PayloadB64("{\"leaked\":true,\"observedResolvers\":[\"9.9.9.9\"]}");
            var (result, observed, _) = LeakDetector.DeriveResult(
                true, signed, outerLeaked: true, outerObserved: new List<string> { "9.9.9.9" },
                expectedResolvers: null);
            Assert.Equal(LeakResult.Confirmed, result);
            Assert.Equal(new List<string> { "9.9.9.9" }, observed);
        }

        [Fact]
        public void ValidSignature_UnparseablePayload_IsInconclusive()
        {
            var (result, _, _) = LeakDetector.DeriveResult(
                true, PayloadB64("{\"unrelated\":1}"), outerLeaked: true,
                outerObserved: new List<string> { "9.9.9.9" }, expectedResolvers: null);
            Assert.Equal(LeakResult.Inconclusive, result);
        }

        [Fact]
        public void InvalidSignature_IsInconclusive_NeverOuterResult()
        {
            var (result, _, _) = LeakDetector.DeriveResult(
                sigValid: false, signedPayloadBase64: null, outerLeaked: false,
                outerObserved: new List<string>(), expectedResolvers: null);
            Assert.Equal(LeakResult.Inconclusive, result);
        }

        [Fact]
        public void C031_BaselineResolversDoNotReadAsLeak()
        {
            var baseline = new HashSet<string> { "10.64.0.1", "10.64.0.2" };
            // All observed resolvers are the expected (e.g. VPN provider) egress → expected
            // behavior, not a leak.
            var (r1, _) = LeakDetector.ReconcileResult(
                leaked: true, new List<string> { "10.64.0.1" }, baseline);
            Assert.Equal(LeakResult.NotDetected, r1);

            // Failure case: ANY resolver outside the baseline is still a confirmed leak — the
            // baseline must not blind the detector.
            var (r2, _) = LeakDetector.ReconcileResult(
                leaked: true, new List<string> { "10.64.0.1", "203.0.113.7" }, baseline);
            Assert.Equal(LeakResult.Confirmed, r2);

            // No baseline → original semantics (any observation is a leak).
            var (r3, _) = LeakDetector.ReconcileResult(
                leaked: true, new List<string> { "10.64.0.1" }, null);
            Assert.Equal(LeakResult.Confirmed, r3);
        }
    }
}
