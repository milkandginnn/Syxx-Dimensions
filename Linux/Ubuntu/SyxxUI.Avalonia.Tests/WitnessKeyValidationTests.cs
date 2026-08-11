using System;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// Windows twin of macOS <c>Tests/SyxxKitTests/WitnessKeyValidationTests.swift</c>.
    ///
    /// Two defects, one root. On macOS a 6-byte string (<c>lklklklk</c> — valid base64, 6 bytes,
    /// where Ed25519 needs 32) was stored as a signing key with no validation and only failed at
    /// signing time, months later, as an unreadable error. Windows had the same unvalidated
    /// <c>WitnessKeyStore.SaveKey</c>, plus a parity gap of its own: unusable key material escaped
    /// the signer as a raw <c>FormatException</c> (non-base64) or an NSec exception (wrong length),
    /// neither of which macOS can produce, so the two platforms failed differently on identical
    /// input.
    ///
    /// FILESYSTEM SAFETY: every <c>SaveKey</c> case below is a REJECTION case, and rejection happens
    /// before any secret-store or file write. Nothing here reads, writes, or deletes the real
    /// <c>witness.key</c>. The "accepts a real key" direction is asserted through the pure validator
    /// instead, deliberately.
    /// </summary>
    public class WitnessKeyValidationTests
    {
        /// The exact 6-byte string found in the macOS owner's keychain: valid base64, wrong length.
        private const string SixByteKey = "lklklklk";

        private static string Base64Of(int byteCount, byte fill = 0x41)
            => Convert.ToBase64String(Enumerable.Repeat(fill, byteCount).ToArray());

        private static ComparisonResult MakeComparison()
        {
            var record = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            return new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new() { record },
                PublicResolverResults = new() { ["Cloudflare"] = new() { record } },
                Result = VantageComparisonResult.Consistent
            };
        }

        // ---- The validator itself -------------------------------------------------------------

        [Fact]
        public void ValidatePrivateKey_AcceptsAFreshlyGeneratedKeypair()
        {
            // Not over-strict: a validator that rejected everything would pass every rejection test
            // below while breaking the product outright.
            for (int i = 0; i < 8; i++)
            {
                var (priv, _) = WitnessSigner.GenerateKeyPair();
                WitnessSigner.ValidatePrivateKey(priv);   // must not throw
            }
        }

        [Fact]
        public void ValidatePrivateKey_RejectsTheSixByteKey()
        {
            var ex = Assert.Throws<WitnessSignerException>(() => WitnessSigner.ValidatePrivateKey(SixByteKey));
            Assert.Equal(WitnessSigner.InvalidPrivateKeyMessage, ex.Message);
        }

        [Theory]
        [InlineData("", "empty")]
        [InlineData(null, "null")]
        public void ValidatePrivateKey_RejectsAbsentKey(string? key, string label)
        {
            var ex = Assert.Throws<WitnessSignerException>(() => WitnessSigner.ValidatePrivateKey(key!));
            Assert.False(string.IsNullOrWhiteSpace(ex.Message), $"{label}: message must say what failed");
        }

        /// Adversarial shapes. The off-by-one lengths are the interesting ones: 31 and 33 bytes are
        /// both well-formed base64 and both unusable as Ed25519 private keys.
        [Fact]
        public void ValidatePrivateKey_RejectsAdversarialShapes()
        {
            var valid32 = Base64Of(32);
            var cases = new (string Input, string Label)[]
            {
                (Base64Of(31), "31 bytes"),
                (Base64Of(33), "33 bytes"),
                (Base64Of(0), "zero bytes"),
                ("not base64 at all!", "non-base64 characters"),
                ("lklklkl", "base64 with invalid padding"),
                (valid32[..^1], "truncated base64"),
                ("\0", "NUL byte"),
                (new string('A', 100_000), "oversized input"),
            };

            foreach (var (input, label) in cases)
            {
                // The assertion is on the TYPE as much as the throw: a raw FormatException or an
                // NSec exception escaping here is the cross-platform divergence this closes.
                Assert.Throws<WitnessSignerException>(() => WitnessSigner.ValidatePrivateKey(input));
            }

            // The control: exactly 32 bytes, cleanly encoded, is accepted.
            WitnessSigner.ValidatePrivateKey(valid32);
        }

        /// The invariant the shared validator exists to guarantee: save-time acceptance and
        /// sign-time acceptance cannot drift apart.
        [Fact]
        public void ValidatorAndSignerAgree()
        {
            var comparison = MakeComparison();
            var candidates = new[]
            {
                WitnessSigner.GenerateKeyPair().PrivateKey,
                Base64Of(32, 0x00),
                Base64Of(32, 0xFF),
                SixByteKey,
                "",
                "not base64 at all!",
                Base64Of(31),
            };

            foreach (var candidate in candidates)
            {
                bool validatorAccepts = Try(() => WitnessSigner.ValidatePrivateKey(candidate));
                bool signerAccepts = Try(() => WitnessSigner.CreateSignedSnapshot("example.com", comparison, candidate));
                Assert.True(validatorAccepts == signerAccepts,
                    $"validator and signer disagree on '{Preview(candidate)}' — this is the deferred-failure defect");
            }

            static bool Try(Action a) { try { a(); return true; } catch (Exception) { return false; } }
            static string Preview(string s) => s.Length <= 12 ? s : s[..12] + "...";
        }

        /// CHARACTERIZATION, NOT ENDORSEMENT, and the macOS twin of the same test. Both platforms'
        /// base64 decoders skip whitespace between base64 characters, so a key pasted with a wrapped
        /// line break still decodes to the same 32 bytes. Left as-is rather than tightened: the
        /// validator and the signer share one decoder, so no key can be saved that cannot later
        /// sign, and tightening would newly reject a real working key someone pasted with a line
        /// break. This test exists so a future runtime change is discovered here, not in the field.
        [Fact]
        public void ValidatePrivateKey_EmbeddedWhitespaceIsSkippedByTheRuntime()
        {
            var wrapped = Base64Of(32).Replace("A", "A\n");
            var ex = Record.Exception(() =>
            {
                WitnessSigner.ValidatePrivateKey(wrapped);
                WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), wrapped);
            });
            Assert.Null(ex);
        }

        // ---- SaveKey rejects at the point of entry --------------------------------------------

        /// Every case is a rejection, so SaveKey throws before touching the secret store or the
        /// filesystem. If the validation is removed, these go red by NOT throwing (and only then
        /// would a write occur).
        [Fact]
        public void SaveKey_RejectsUnusableKeyMaterialBeforeStoringIt()
        {
            var ex = Assert.Throws<WitnessSignerException>(() => WitnessKeyStore.SaveKey(SixByteKey));
            Assert.Equal(WitnessSigner.InvalidPrivateKeyMessage, ex.Message);

            Assert.Throws<WitnessSignerException>(() => WitnessKeyStore.SaveKey("not base64 at all!"));
            Assert.Throws<WitnessSignerException>(() => WitnessKeyStore.SaveKey(Base64Of(31)));
            Assert.Throws<WitnessSignerException>(() => WitnessKeyStore.SaveKey(""));
        }

        /// The invalid-key message must convey WHAT failed and must not assert a cause we have not
        /// established (we cannot know whether the key was typed as a passphrase, truncated, or
        /// pasted from the wrong place). Byte-identical to the macOS
        /// <c>WitnessSignerError.invalidPrivateKey</c> description, so both platforms say the same
        /// thing about the same failure.
        [Fact]
        public void InvalidPrivateKeyMessage_NamesEd25519KeyMaterialAndAssertsNoCause()
        {
            var text = WitnessSigner.InvalidPrivateKeyMessage;
            Assert.Contains("Ed25519", text);
            Assert.Contains("32", text);
            foreach (var forbidden in new[] { "password", "passphrase", "because", "you typed", "you pasted" })
            {
                Assert.DoesNotContain(forbidden, text.ToLowerInvariant());
            }
            Assert.Equal(
                "The signing key is not valid Ed25519 key material. An Ed25519 private key is exactly 32 bytes, written as base64.",
                text);
        }
    }
}
