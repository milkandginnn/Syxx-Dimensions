using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests;

/// <summary>
/// §8 — the EFF dice passphrase, the user secret the file-store KDF entirely lacked (P-04).
///
/// Each test names the state it fails in. The wordlist tests exist because a silently smaller
/// keyspace is invisible at runtime: a truncated list still produces plausible-looking words.
/// </summary>
public sealed class DicePassphraseTests
{
    // ── The embedded wordlist ─────────────────────────────────────────────────────

    /// <summary>§8 point 1. Fail state: a truncated or reordered list still generates passphrases,
    /// from a smaller keyspace, with nothing on screen different.</summary>
    [Fact]
    public void Wordlist_HasExactlyTheEffShortListCount()
    {
        Assert.Equal(1296, DicePassphrase.WordlistCount);
    }

    /// <summary>The pin is over the published file reconstructed from the embedded words, so it
    /// proves the words, their order and the count at once. Fail state: any of the three drifts and
    /// the generator keeps working.</summary>
    [Fact]
    public void Wordlist_MatchesThePinnedSha256OfTheCanonicalEffFile()
    {
        Assert.True(DicePassphrase.VerifyWordlistIntegrity(),
            "the embedded wordlist no longer hashes to the pinned EFF short wordlist #1 SHA-256");
    }

    /// <summary>Independent recomputation of the pin, so the pin constant itself is checked rather
    /// than merely agreed with.</summary>
    [Fact]
    public void Wordlist_CanonicalTextHashesToThePublishedDigest()
    {
        var text = typeof(DicePassphrase)
            .GetMethod("CanonicalWordlistText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null) as string;

        var hex = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text!)));
        Assert.Equal("8f5ca830b8bffb6fe39c9736c024a00a6a6411adb3f83a9be8bfeeb6e067ae69", hex);
    }

    /// <summary>Every index is four dice digits, 1111 through 6666, with no gaps. Fail state: an
    /// index outside 1-6 means a roll a person physically cannot produce.</summary>
    [Fact]
    public void Wordlist_EveryIndexIsFourDigitsOfOneToSix_AndAllAreDistinct()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < DicePassphrase.WordlistCount; i++)
        {
            var index = InvokeIndexForPosition(i);
            Assert.Equal(4, index.Length);
            Assert.All(index, ch => Assert.InRange(ch, '1', '6'));
            Assert.True(seen.Add(index), $"index {index} appeared twice");
        }
        Assert.Equal(1296, seen.Count);
    }

    /// <summary>The published endpoints, as a cheap tripwire on the whole list.</summary>
    [Fact]
    public void Wordlist_FirstAndLastEntriesMatchThePublishedFile()
    {
        Assert.Equal("acid", DicePassphrase.WordForIndex("1111"));
        Assert.Equal("zoom", DicePassphrase.WordForIndex("6666"));
    }

    [Fact]
    public void WordForIndex_RejectsMalformedIndices_RatherThanGuessing()
    {
        Assert.Null(DicePassphrase.WordForIndex("1117"));   // 7 is not a die face
        Assert.Null(DicePassphrase.WordForIndex("111"));    // too short
        Assert.Null(DicePassphrase.WordForIndex("11110"));  // too long
        Assert.Null(DicePassphrase.WordForIndex("0111"));   // 0 is not a die face
    }

    // ── Entropy is computed and displayed, not asserted ───────────────────────────

    /// <summary>★ Owner decision: three words. Fail state: the count drifts and the displayed
    /// entropy no longer describes what is generated.</summary>
    [Fact]
    public void WordCount_IsTheOwnerDecidedThree()
    {
        Assert.Equal(3, DicePassphrase.PassphraseWordCount);
        Assert.Equal(3, DicePassphrase.Generate().Words.Count);
    }

    /// <summary>§8: the entropy is computed from the constant and the real wordlist length, never
    /// hardcoded. log2(1296) x 3 = 31.0 bits. Fail state: a hardcoded "31.0" survives a change to
    /// the word count and the UI then states a number that is not true of what it generated.</summary>
    [Fact]
    public void EntropyBits_IsComputedFromTheWordCountAndTheRealWordlistLength()
    {
        var expected = Math.Log2(DicePassphrase.WordlistCount) * DicePassphrase.PassphraseWordCount;
        Assert.Equal(expected, DicePassphrase.EntropyBits, precision: 10);
        Assert.Equal(31.0, DicePassphrase.EntropyBits, precision: 1);
        Assert.Equal("31.0 bits", DicePassphrase.EntropyDisplay);
    }

    // ── Generation: CSPRNG, uniform, no modulo bias ───────────────────────────────

    /// <summary>§8 point 2. Fail state: modulo over a raw byte biases toward low indices — entropy
    /// loss on the one operation whose entire job is entropy. A biased generator would show up as a
    /// chi-squared far above the bound, and as unreachable high indices.</summary>
    [Fact]
    public void Generate_IsUniformOverAllWordlistIndices()
    {
        const int words = 1296;
        const int draws = words * 40;   // ~51,840 samples, 40 expected per bucket

        var counts = new int[words];
        int drawn = 0;
        while (drawn < draws)
        {
            foreach (var w in DicePassphrase.Generate().Words)
            {
                counts[PositionOf(w.Index)]++;
                drawn++;
            }
        }

        // Every index must be reachable. A modulo reduction of a 2^k range by 1296 leaves the top of
        // the range over-represented and never under-represents to zero, but a wordlist accidentally
        // bounded to 1024 or 256 would leave whole tails at zero.
        Assert.DoesNotContain(0, counts);

        double expected = (double)drawn / words;
        double chiSquared = counts.Sum(c => (c - expected) * (c - expected) / expected);

        // df = 1295. Upper 0.9999 critical value is about 1517; a uniform generator clears this
        // essentially always, while a byte-modulo generator (which makes indices 0..975 about 25%
        // more likely than 976..1295) produces a statistic in the tens of thousands.
        Assert.True(chiSquared < 1600,
            $"index distribution is not uniform: chi-squared {chiSquared:F1} over {drawn} draws");
    }

    /// <summary>Generation is labelled as machine-chosen. Fail state: a UI says "you rolled these"
    /// when a PRNG chose — the fabrication class this codebase legislates against.</summary>
    [Fact]
    public void Generate_IsLabelledAsChosenByTheMachine()
    {
        var offer = DicePassphrase.Generate();
        Assert.Equal(PassphraseOrigin.GeneratedByCsprng, offer.Origin);
        Assert.Contains("Syxx chose", offer.OriginDisplay, StringComparison.Ordinal);
    }

    // ── Physical dice entry ───────────────────────────────────────────────────────

    /// <summary>§8 point 3: eff.org/dice describes a PHYSICAL procedure, so the physical path must
    /// exist and must be distinguishable in the result.</summary>
    [Fact]
    public void FromPhysicalDiceRolls_ResolvesThePublishedWords_AndSaysThePersonRolledThem()
    {
        var offer = DicePassphrase.FromPhysicalDiceRolls(new[] { "1111", "6666", "1111" });

        Assert.Equal(new[] { "acid", "zoom", "acid" }, offer.Words.Select(w => w.Word));
        Assert.Equal(PassphraseOrigin.EnteredFromPhysicalDice, offer.Origin);
        Assert.Contains("your own dice", offer.OriginDisplay, StringComparison.Ordinal);
        Assert.Equal("acid zoom acid", offer.Phrase);
    }

    /// <summary>Fail state: a mistyped roll is silently substituted and the person's written-down
    /// passphrase no longer matches what was used.</summary>
    [Theory]
    [InlineData("1117")]   // 7 is not a die face
    [InlineData("0111")]   // 0 is not a die face
    [InlineData("111")]    // three dice
    [InlineData("")]
    [InlineData("abcd")]
    public void FromPhysicalDiceRolls_RejectsAnyMalformedRoll(string bad)
    {
        var ex = Assert.Throws<DicePassphraseException>(
            () => DicePassphrase.FromPhysicalDiceRolls(new[] { "1111", bad, "1111" }));
        Assert.Contains("four", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromPhysicalDiceRolls_RejectsTheWrongNumberOfRolls()
    {
        Assert.Throws<DicePassphraseException>(
            () => DicePassphrase.FromPhysicalDiceRolls(new[] { "1111", "2222" }));
        Assert.Throws<DicePassphraseException>(
            () => DicePassphrase.FromPhysicalDiceRolls(new[] { "1111", "2222", "3333", "4444" }));
    }

    // ── No recovery: the acknowledgement gate ─────────────────────────────────────

    /// <summary>§8 point 7 + owner decision 15 (no escrow). Fail state: a UI wraps the signing key
    /// with a passphrase nobody was told they can never recover, and one lost passphrase later every
    /// sealed testimony is unverifiable with no route back.</summary>
    [Fact]
    public void Accept_RefusesWithoutTheWrittenDownAcknowledgement()
    {
        var offer = DicePassphrase.Generate();
        var ex = Assert.Throws<DicePassphraseException>(() => offer.Accept(writtenDownAcknowledged: false));
        Assert.Contains("written down", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Accept_WithAcknowledgement_YieldsThePhraseAndKeepsTheOrigin()
    {
        var offer = DicePassphrase.FromPhysicalDiceRolls(new[] { "1111", "1111", "1111" });
        var accepted = offer.Accept(writtenDownAcknowledged: true);

        Assert.Equal("acid acid acid", accepted.Phrase);
        Assert.Equal(PassphraseOrigin.EnteredFromPhysicalDice, accepted.Origin);
    }

    /// <summary>The warning must state the consequence, not merely caution. Fail state: soft copy
    /// ("keep it safe") that does not tell the person their archive stops verifying.</summary>
    [Fact]
    public void NoRecoveryWarning_StatesThatSealedTestimonyBecomesUnverifiable()
    {
        Assert.Contains("no recovery", DicePassphraseOffer.NoRecoveryWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verify", DicePassphraseOffer.NoRecoveryWarning, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Owner decision 15: no escrow, and no hook for one. Fail state: a well-meaning later
    /// change adds a recovery path and quietly removes the property this feature creates.</summary>
    [Fact]
    public void NoEscrowApiExists()
    {
        // The constant NoRecoveryWarning is the user-facing warning STRING this feature exists to
        // show — it contains "Recovery" by design and is not an API. Excluding it by exact name
        // keeps the guard on anything that could actually escrow or recover a passphrase, which is
        // what owner decision 15 forbids. Widening past this exclusion would ban the warning text.
        var names = typeof(DicePassphrase).GetMembers()
            .Concat(typeof(DicePassphraseOffer).GetMembers())
            .Select(m => m.Name)
            .Where(n => n != nameof(DicePassphraseOffer.NoRecoveryWarning))
            .ToList();

        Assert.DoesNotContain(names, n =>
            n.Contains("Escrow", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Recover", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Backup", StringComparison.OrdinalIgnoreCase));
    }

    // ── C-11: the passphrase never reaches diagnostics ────────────────────────────

    /// <summary>§8 point 4. Fail state: a breadcrumb or a captured event carries the words, and the
    /// passphrase leaves the machine through the telemetry pipe the user consented to for scan
    /// failures. Drives every public entry point on the type, including the failing ones.</summary>
    [Fact]
    public void NothingInThePassphrasePathReachesSyxxDiagnostics()
    {
        var sink = new CapturingSink();
        SyxxDiagnostics.Install(sink);
        try
        {
            var generated = DicePassphrase.Generate();
            var accepted = generated.Accept(writtenDownAcknowledged: true);

            var rolled = DicePassphrase.FromPhysicalDiceRolls(new[] { "1111", "6666", "3333" });
            _ = rolled.Accept(writtenDownAcknowledged: true);
            _ = DicePassphrase.EntropyDisplay;
            _ = DicePassphrase.WordForIndex("1111");
            try { _ = generated.Accept(writtenDownAcknowledged: false); } catch (DicePassphraseException) { }
            try { DicePassphrase.FromPhysicalDiceRolls(new[] { "9999", "9999", "9999" }); }
            catch (DicePassphraseException) { }

            var haystack = sink.Everything();
            foreach (var word in generated.Words.Select(w => w.Word)
                         .Concat(rolled.Words.Select(w => w.Word))
                         .Concat(new[] { accepted.Phrase, rolled.Phrase }))
            {
                Assert.DoesNotContain(word, haystack, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            SyxxDiagnostics.Uninstall();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static string InvokeIndexForPosition(int position) =>
        (string)typeof(DicePassphrase)
            .GetMethod("IndexForPosition",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { position })!;

    private static int PositionOf(string index)
    {
        int p = 0;
        foreach (var ch in index) p = (p * 6) + (ch - '1');
        return p;
    }

    /// <summary>Records everything a sink is handed, so a test can assert on what would actually
    /// have been transmitted rather than on what a call site intended.</summary>
    internal sealed class CapturingSink : IDiagnosticSink
    {
        private readonly List<string> _lines = new();

        public void Capture(DiagnosticEvent evt)
        {
            lock (_lines)
            {
                _lines.Add(evt.ErrorClass);
                _lines.Add(evt.Component);
                _lines.Add(evt.Underlying ?? string.Empty);
                _lines.AddRange(evt.Fingerprint);
                if (evt.Tags is not null) _lines.AddRange(evt.Tags.Select(kv => kv.Key + "=" + kv.Value));
                if (evt.Extra is not null) _lines.AddRange(evt.Extra.Select(kv => kv.Key + "=" + kv.Value));
            }
        }

        public void Breadcrumb(string category, string message, DiagnosticSeverity level,
                               IReadOnlyDictionary<string, string> data)
        {
            lock (_lines)
            {
                _lines.Add(category);
                _lines.Add(message);
                _lines.AddRange(data.Select(kv => kv.Key + "=" + kv.Value));
            }
        }

        public void DiscardBuffered() { }

        public string Everything()
        {
            lock (_lines) return string.Join('\n', _lines);
        }
    }
}
