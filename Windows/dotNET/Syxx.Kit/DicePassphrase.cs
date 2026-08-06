using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Syxx.Kit;

/// <summary>How a passphrase came to exist. Kept on the value itself, because a UI that says
/// "you rolled these dice" when a PRNG chose them is the fabrication class this codebase
/// legislates against (spine #4). There is deliberately no "unknown" member: every construction
/// path in this type knows which of the two it is.</summary>
public enum PassphraseOrigin
{
    /// <summary>Chosen by <see cref="RandomNumberGenerator"/> on this machine.</summary>
    GeneratedByCsprng,

    /// <summary>Transcribed from physical dice the person rolled themselves, as
    /// <c>eff.org/dice</c> describes. Syxx did not choose these words.</summary>
    EnteredFromPhysicalDice,
}

/// <summary>Raised when dice digits handed to <see cref="DicePassphrase.FromPhysicalDiceRolls"/>
/// are not a well-formed set of rolls, or when a passphrase is accepted without the written-down
/// acknowledgement.</summary>
public sealed class DicePassphraseException : Exception
{
    public DicePassphraseException(string message) : base(message) { }
}

/// <summary>One word and the four-digit index it is published under.</summary>
/// <param name="Index">Four characters, each <c>1</c>–<c>6</c> — the dice roll that selects it.</param>
public readonly record struct DiceWord(string Index, string Word);

/// <summary>
/// EFF short wordlist #1 passphrase generation for the witness signing key at rest (§8).
///
/// <para>WHAT THIS IS FOR. On Linux the key at rest is wrapped with material derived from the
/// machine alone — no user secret takes part (P-04). This type produces that missing secret.
/// On <b>Linux it is load-bearing</b>: it replaces machine-derived material and is the only thing
/// between someone who has obtained the keystore file and a forged signing identity. On
/// <b>macOS it is additive</b>: the Keychain already gates the key on login, so a passphrase adds a
/// second factor rather than supplying the first. Do not let copy on either platform imply they are
/// equivalent.</para>
///
/// <para>WHAT IT IS WORTH, STATED RATHER THAN ASSERTED. <see cref="EntropyBits"/> is computed from
/// <see cref="PassphraseWordCount"/> and the real wordlist length, never hardcoded, and is meant to
/// be rendered beside the words. At the shipped three words that is
/// <c>log2(1296) x 3 = 31.0 bits</c>. The number is displayed so the app states what the passphrase
/// is worth instead of claiming it is strong.</para>
///
/// <para>THE WORD COUNT IS ONE CONSTANT. <see cref="PassphraseWordCount"/> is the single place the
/// count is written on this platform, and <c>DicePassphrase.passphraseWordCount</c> is its Swift
/// twin. Changing it changes generation, dice-entry validation, and the displayed entropy together.</para>
///
/// <para>THERE IS NO RECOVERY. A lost passphrase makes the wrapped key unrecoverable and every
/// previously sealed testimony unverifiable. There is no escrow and no hook for one — an escrow
/// would weaken the exact property this feature creates. <see cref="Generate"/> and
/// <see cref="FromPhysicalDiceRolls"/> therefore return a <see cref="DicePassphraseOffer"/>, which
/// carries <see cref="DicePassphraseOffer.NoRecoveryWarning"/> and will not produce the usable
/// secret until <see cref="DicePassphraseOffer.Accept"/> is called with the written-down
/// acknowledgement.</para>
///
/// <para>NEVER LOGGED. Nothing in this file passes a word, an index, or a phrase to
/// <see cref="SyxxDiagnostics"/>, and nothing here writes to a process argument list. Callers must
/// take the passphrase from a prompt, from <c>SYXX_WITNESS_PASSPHRASE</c>, or from a
/// <c>--passphrase-file</c> — never from a positional or <c>--passphrase</c> argument, because
/// <c>/proc/&lt;pid&gt;/cmdline</c> is world-readable on stock Ubuntu (C-11).</para>
/// </summary>
public static class DicePassphrase
{
    /// <summary>★ Owner decision, 2026-08-04: three words. The owner was shown the entropy analysis
    /// and reaffirmed three. This constant — and its Swift twin <c>passphraseWordCount</c> — is the
    /// one place the count is written, so raising it later is a one-line change on each platform.</summary>
    public const int PassphraseWordCount = 3;

    /// <summary>SHA-256 of EFF short wordlist #1 exactly as published: 1,296 lines of
    /// <c>NNNN&lt;TAB&gt;word</c>, LF endings, trailing newline, 13,660 bytes. Fetched 2026-08-04
    /// from <c>https://www.eff.org/files/2016/09/08/eff_short_wordlist_1.txt</c>.
    ///
    /// <para><see cref="VerifyWordlistIntegrity"/> reconstructs that byte sequence from the embedded
    /// words and compares. Because the reconstruction regenerates the indices from position, the
    /// comparison proves the words AND their order AND the count — a truncated or reordered list
    /// fails, rather than silently shrinking the keyspace.</para></summary>
    public const string WordlistSha256 =
        "8f5ca830b8bffb6fe39c9736c024a00a6a6411adb3f83a9be8bfeeb6e067ae69";

    /// <summary>Six faces per die, four dice per word — so 6^4 = 1,296 words.</summary>
    private const int DiceFaces = 6;
    private const int DigitsPerWord = 4;

    private static readonly string[] Words = ParseWordlist();

    private static string[] ParseWordlist() =>
        DicePassphraseWordlist.SpaceSeparated.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The number of words actually embedded. 1,296 when intact.</summary>
    public static int WordlistCount => Words.Length;

    /// <summary>Entropy of one generated passphrase, in bits: <c>log2(WordlistCount) x
    /// PassphraseWordCount</c>. Computed, never hardcoded — if either input changes, this follows.
    /// Render it beside the words.</summary>
    public static double EntropyBits => Math.Log2(Words.Length) * PassphraseWordCount;

    /// <summary>Entropy formatted for display, e.g. <c>"31.0 bits"</c>. Invariant culture, because
    /// this is a technical quantity that appears in copy and in reports alike.</summary>
    public static string EntropyDisplay =>
        string.Create(CultureInfo.InvariantCulture, $"{EntropyBits:F1} bits");

    /// <summary>The four-digit published index of the word at zero-based <paramref name="position"/>.
    /// The index is the four base-6 digits of the position, each digit plus one: position 0 is
    /// <c>1111</c>, position 1295 is <c>6666</c>. Verified against the published file for all 1,296
    /// entries, which is why the indices are derived rather than stored.</summary>
    internal static string IndexForPosition(int position)
    {
        Span<char> digits = stackalloc char[DigitsPerWord];
        for (int i = DigitsPerWord - 1; i >= 0; i--)
        {
            digits[i] = (char)('0' + (position % DiceFaces) + 1);
            position /= DiceFaces;
        }
        return new string(digits);
    }

    /// <summary>Zero-based position of a four-digit roll, or <c>-1</c> when the roll is not four
    /// characters of <c>1</c>–<c>6</c>. Inverse of <see cref="IndexForPosition"/>.</summary>
    internal static int PositionForIndex(string index)
    {
        if (index is null || index.Length != DigitsPerWord) return -1;
        int position = 0;
        foreach (var ch in index)
        {
            if (ch < '1' || ch > '6') return -1;
            position = (position * DiceFaces) + (ch - '1');
        }
        return position;
    }

    /// <summary>Rebuilds EFF short wordlist #1 byte-for-byte as published, from the embedded words.
    /// Used only by <see cref="VerifyWordlistIntegrity"/> and its test.</summary>
    internal static string CanonicalWordlistText()
    {
        var sb = new StringBuilder(16 * 1024);
        for (int i = 0; i < Words.Length; i++)
        {
            sb.Append(IndexForPosition(i)).Append('\t').Append(Words[i]).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Whether the embedded wordlist still hashes to <see cref="WordlistSha256"/>.
    /// Called on every generation: a wordlist that has been truncated or reordered must fail loudly
    /// rather than quietly produce a passphrase from a smaller keyspace.</summary>
    public static bool VerifyWordlistIntegrity()
    {
        var actual = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(CanonicalWordlistText())));
        return string.Equals(actual, WordlistSha256, StringComparison.Ordinal);
    }

    /// <summary>
    /// Generates a passphrase with the system CSPRNG.
    ///
    /// <para>Selection is <see cref="RandomNumberGenerator.GetInt32(int,int)"/>, which draws a
    /// masked random value and REJECTS anything at or above the bound before returning — uniform
    /// over all <see cref="WordlistCount"/> indices. Modulo over a raw byte is not used anywhere
    /// here: <c>256 mod 1296</c> aside, any modulo reduction of a power-of-two range by 1,296 biases
    /// toward low indices, which would be entropy loss on the one operation whose entire job is
    /// entropy.</para>
    /// </summary>
    /// <exception cref="DicePassphraseException">The embedded wordlist failed its integrity pin.</exception>
    public static DicePassphraseOffer Generate()
    {
        RequireIntactWordlist();

        var picked = new DiceWord[PassphraseWordCount];
        for (int i = 0; i < picked.Length; i++)
        {
            int position = RandomNumberGenerator.GetInt32(0, Words.Length);
            picked[i] = new DiceWord(IndexForPosition(position), Words[position]);
        }

        return new DicePassphraseOffer(picked, PassphraseOrigin.GeneratedByCsprng);
    }

    /// <summary>
    /// Builds a passphrase from dice the person rolled themselves — the physical procedure
    /// <c>eff.org/dice</c> actually describes. The result is labelled
    /// <see cref="PassphraseOrigin.EnteredFromPhysicalDice"/>, so nothing downstream can claim a
    /// PRNG choice was a roll or the reverse.
    /// </summary>
    /// <param name="rolls">One entry per word, each four characters of <c>1</c>–<c>6</c>.</param>
    /// <exception cref="DicePassphraseException">Wrong number of rolls, or a roll that is not four
    /// digits of 1–6. Nothing partial is returned — a mistyped roll must be corrected, never
    /// silently substituted.</exception>
    public static DicePassphraseOffer FromPhysicalDiceRolls(IReadOnlyList<string> rolls)
    {
        RequireIntactWordlist();
        ArgumentNullException.ThrowIfNull(rolls);

        if (rolls.Count != PassphraseWordCount)
        {
            throw new DicePassphraseException(string.Create(CultureInfo.InvariantCulture,
                $"{PassphraseWordCount} rolls are needed, one per word, and {rolls.Count} were entered."));
        }

        var picked = new DiceWord[PassphraseWordCount];
        for (int i = 0; i < rolls.Count; i++)
        {
            var roll = rolls[i]?.Trim() ?? string.Empty;
            int position = PositionForIndex(roll);
            if (position < 0)
            {
                throw new DicePassphraseException(string.Create(CultureInfo.InvariantCulture,
                    $"Roll {i + 1} is not four dice digits. Enter the four numbers you rolled, each 1 to 6."));
            }
            picked[i] = new DiceWord(IndexForPosition(position), Words[position]);
        }

        return new DicePassphraseOffer(picked, PassphraseOrigin.EnteredFromPhysicalDice);
    }

    /// <summary>The word at a published index, for a UI that wants to echo the word as each roll is
    /// typed. Returns <c>null</c> for a malformed index rather than guessing.</summary>
    public static string? WordForIndex(string index)
    {
        int position = PositionForIndex(index);
        return position < 0 || position >= Words.Length ? null : Words[position];
    }

    private static void RequireIntactWordlist()
    {
        if (Words.Length == 0 || !VerifyWordlistIntegrity())
        {
            // No breadcrumb: this fires before any passphrase exists, and the message deliberately
            // carries no word content. A build that reaches here has a corrupted wordlist and must
            // not produce a passphrase from whatever survived.
            throw new DicePassphraseException(
                "The built-in word list failed its integrity check, so no passphrase was generated. "
                + "This build is damaged — reinstall Syxx rather than continuing.");
        }
    }
}

/// <summary>
/// A generated-but-not-yet-accepted passphrase.
///
/// <para>WHY THIS TYPE EXISTS. §8 point 7: there is no recovery, and the API must make the UI say so
/// before the passphrase is used to wrap a key. The words are readable here because a UI has to
/// display them; what is gated is the step that matters — <see cref="Accept"/> is the only route to
/// an <see cref="AcceptedDicePassphrase"/>, the only type the keystore will take, and it refuses
/// unless the caller passes the acknowledgement. A UI physically cannot wrap a key without having
/// had somewhere to show <see cref="NoRecoveryWarning"/>.</para>
/// </summary>
public sealed class DicePassphraseOffer
{
    internal DicePassphraseOffer(IReadOnlyList<DiceWord> words, PassphraseOrigin origin)
    {
        Words = words;
        Origin = origin;
    }

    /// <summary>The chosen words with their dice indices, in order.</summary>
    public IReadOnlyList<DiceWord> Words { get; }

    /// <summary>Whether Syxx chose these words or the person rolled them. Render this — never
    /// describe a CSPRNG result as a dice roll.</summary>
    public PassphraseOrigin Origin { get; }

    /// <summary>Entropy of this passphrase in bits, computed from the wordlist and the word count.
    /// Show it beside the words.</summary>
    public double EntropyBits => DicePassphrase.EntropyBits;

    /// <summary>Entropy formatted for display, e.g. <c>"31.0 bits"</c>.</summary>
    public string EntropyDisplay => DicePassphrase.EntropyDisplay;

    /// <summary>User-facing sentence naming who chose the words. Honest in both directions.</summary>
    public string OriginDisplay => Origin == PassphraseOrigin.GeneratedByCsprng
        ? "Syxx chose these words using this machine's secure random generator."
        : "You chose these words with your own dice. Syxx did not pick them.";

    /// <summary>The no-recovery warning a UI must show before <see cref="Accept"/>. Stated as an
    /// outcome, not a caution: the consequence is that sealed testimony stops verifying.</summary>
    public const string NoRecoveryWarning =
        "Write this down now and keep it somewhere safe. There is no recovery and no backup copy: "
        + "if you lose this passphrase your signing key is gone for good, and every testimony you "
        + "have already sealed becomes impossible to verify.";

    /// <summary>The passphrase as typed and stored: words joined by single spaces, lower case.</summary>
    public string Phrase => string.Join(' ', Words.Select(static w => w.Word));

    /// <summary>
    /// Confirms the person has written the passphrase down, and returns the value the keystore
    /// will accept.
    /// </summary>
    /// <param name="writtenDownAcknowledged">Must be <c>true</c>, and must come from an actual
    /// affirmative act by the person — a ticked box, a typed confirmation. Passing a hardcoded
    /// <c>true</c> to satisfy the compiler defeats the only guard standing between a user and a
    /// permanently unverifiable archive.</param>
    /// <exception cref="DicePassphraseException"><paramref name="writtenDownAcknowledged"/> is false.</exception>
    public AcceptedDicePassphrase Accept(bool writtenDownAcknowledged)
    {
        if (!writtenDownAcknowledged)
        {
            throw new DicePassphraseException(
                "The passphrase was not confirmed as written down, so nothing was changed. "
                + "Confirm you have written it down before Syxx wraps your signing key with it.");
        }
        return new AcceptedDicePassphrase(Phrase, Origin);
    }
}

/// <summary>A passphrase whose no-recovery consequence has been acknowledged. The only form the
/// keystore accepts, so the acknowledgement cannot be routed around.</summary>
/// <param name="Phrase">The words, space-joined, exactly as they must be typed back.</param>
/// <param name="Origin">Who chose the words.</param>
public readonly record struct AcceptedDicePassphrase(string Phrase, PassphraseOrigin Origin);
