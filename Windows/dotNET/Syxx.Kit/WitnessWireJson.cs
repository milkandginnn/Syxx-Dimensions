// C002 — module header: the Foundation/.NET date-encoding mismatch and the ULP-nudging workaround.
//
// WHY THE WIRE FORMAT IS A DOUBLE. The DISPLAY mirror of a witness snapshot writes DateTime as a
// bare JSON number (AppleReferenceDateJsonConverter). macOS writes Foundation's
// timeIntervalSinceReferenceDate — seconds since 2001-01-01Z as an IEEE-754 binary64 — and Swift's
// JSONDecoder reads exactly that; ISO-8601 strings, .NET's natural encoding, are rejected outright
// by Swift. The two runtimes share IEEE-754 binary64 add/multiply, so the double itself is
// portable across the Foundation/.NET boundary.
//
// WHY THE DOUBLE IS NOT ENOUGH. A DateTime carries 100 ns resolution, but a binary64 at the
// magnitudes involved (~8e8 s since 2001; one ULP ≈ 1.2e-4 ms) cannot represent every instant.
// D083 requires the display timestamp to TRUNCATE to exactly the signed evidence millisecond on
// both platforms (ToUnixTimeMilliseconds() on Windows, UInt64(timeIntervalSince1970 * 1000) on
// macOS), and both sides truncate. A naive double conversion lands within ~4e-4 ms of the instant,
// so roughly one snapshot in a thousand would truncate to the adjacent millisecond and read as
// DISPLAY MISMATCH — an honest snapshot failing its own verification on a timing coin-flip.
// ToAppleReferenceSeconds() therefore walks the candidate's binary64 ULPs (each step moves the
// represented instant by well under a microsecond) until macOS's truncation reproduces the signed
// millisecond exactly. This is inherently fragile precision-critical code; the guards inside
// ToAppleReferenceSeconds keep the walk from ever emitting an invalid (non-finite) date.
//
// FOLLOW-UP (deliberately NOT implemented here): the robust protocol-level fix is to stop shipping
// dates as doubles and write an integer milliseconds-since-epoch on the wire — both platforms
// already compute that value exactly (SignedEvidencePayload carries integer epoch-milliseconds
// today, so the value is a known, portable quantity). That is a wire-format change (macOS
// JSONEncoder/JSONDecoder date strategy plus the Windows reader/writer) and is out of scope for
// this change; nothing in this file alters the bytes emitted today.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Syxx.Kit
{
    /// <summary>
    /// [B251] JSON converters that pin the DISPLAY half of a witness snapshot to the macOS wire
    /// format, so evidence sealed on one platform can be read by the other.
    /// </summary>
    /// <remarks>
    /// macOS is the gold standard (AGENTS.md), and already-signed snapshots in the field carry the
    /// Swift encodings. Nothing here changes what macOS writes; these converters teach the .NET
    /// reader to accept it and teach the .NET writer to emit it.
    ///
    /// Scope note: only the DISPLAY mirror is affected. The SIGNED bytes are
    /// <see cref="SignedEvidencePayload.CanonicalBytes"/>, which contains no enums and no dates
    /// (the result is a plain string, timestamps are integer epoch-milliseconds). No previously
    /// issued signature is invalidated by anything in this file.
    ///
    /// Both converters are TOLERANT READERS and STRICT WRITERS: they accept the macOS encoding and
    /// the legacy .NET encoding that older Windows builds emitted, and they always write the macOS
    /// encoding. Anything they cannot map is rejected with a <see cref="JsonException"/> — never
    /// silently defaulted, because a defaulted status ("success") or a defaulted date would turn a
    /// broken read into fabricated evidence.
    /// </remarks>
    public static class WitnessWireJson
    {
        /// <summary>Seconds between the Unix epoch and Foundation's reference date (2001-01-01Z).</summary>
        internal const double AppleToUnixEpochOffsetSeconds = 978307200.0;

        internal static readonly DateTime AppleReferenceDate =
            new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// [B251] Reads/writes <see cref="DNSResponseStatus"/> using the Swift <c>rawValue</c> strings
    /// that macOS emits (DNSResolver.swift:96-107) — <c>success</c>, <c>noData</c>, <c>nxDomain</c>,
    /// <c>servFail</c>, <c>refused</c>, <c>timeout</c>, <c>truncated</c>, <c>responseIdMismatch</c>,
    /// <c>malformedResponse</c>, <c>transportError</c>.
    /// </summary>
    /// <remarks>
    /// Without this, <c>JsonSerializer.Deserialize&lt;WitnessSnapshot&gt;</c> threw
    /// "The JSON value could not be converted to Syxx.Kit.DNSResponseStatus. Path:
    /// $.comparison.dohOutcomes.Cloudflare.status" on every macOS-produced snapshot, and .NET wrote
    /// bare ordinals (<c>"status": 0</c>) that Swift's String-raw-valued enum cannot decode.
    ///
    /// The name table is written out by hand rather than derived from
    /// <c>JsonNamingPolicy.CamelCase</c>: the mapping is a WIRE CONTRACT with another
    /// implementation, so it must be pinned by an explicit table that a test can diff against the
    /// Swift rawValues, not by a naming heuristic that could silently re-spell a member if the CLR
    /// identifier is ever renamed.
    /// </remarks>
    public sealed class DNSResponseStatusJsonConverter : JsonConverter<DNSResponseStatus>
    {
        /// The wire spelling of each member. MUST stay byte-identical to the Swift rawValues in
        /// macOS/Sources/SyxxKit/DNSResolver.swift:96-107.
        internal static readonly IReadOnlyDictionary<DNSResponseStatus, string> WireNames =
            new Dictionary<DNSResponseStatus, string>
            {
                [DNSResponseStatus.Success] = "success",
                [DNSResponseStatus.NoData] = "noData",
                [DNSResponseStatus.NXDomain] = "nxDomain",
                [DNSResponseStatus.ServFail] = "servFail",
                [DNSResponseStatus.Refused] = "refused",
                [DNSResponseStatus.Timeout] = "timeout",
                [DNSResponseStatus.Truncated] = "truncated",
                [DNSResponseStatus.ResponseIdMismatch] = "responseIdMismatch",
                [DNSResponseStatus.MalformedResponse] = "malformedResponse",
                [DNSResponseStatus.TransportError] = "transportError",
                [DNSResponseStatus.RcodeOther] = "rcodeOther",
            };

        private static readonly Dictionary<string, DNSResponseStatus> ByWireName = BuildReverse();

        private static Dictionary<string, DNSResponseStatus> BuildReverse()
        {
            // Case-insensitive on READ so a snapshot written by an older build that used
            // JsonStringEnumConverter's PascalCase ("TransportError") is still readable. The member
            // names are distinct case-insensitively, so this cannot conflate two statuses.
            var map = new Dictionary<string, DNSResponseStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in WireNames)
            {
                map[kv.Value] = kv.Key;
                map[kv.Key.ToString()] = kv.Key;
            }
            return map;
        }

        public override DNSResponseStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    {
                        string? raw = reader.GetString();
                        if (raw is not null && ByWireName.TryGetValue(raw, out var mapped))
                            return mapped;
                        // Fail closed. An unknown status is NOT "success" and is not "timeout"; we do
                        // not know what happened, so the whole snapshot read fails and the caller
                        // reports malformed input rather than verifying fabricated evidence.
                        throw new JsonException(
                            $"Unrecognised DNS response status '{raw}'. Expected one of: {string.Join(", ", WireNames.Values)}.");
                    }
                case JsonTokenType.Number:
                    {
                        // Back-compat: Windows builds before B251 wrote the bare CLR ordinal. The
                        // declaration order of DNSResponseStatus (DNSResolver.cs:78-90) matches the
                        // Swift declaration order and is pinned by DNSResponseStatusWireTests.
                        if (reader.TryGetInt32(out int ordinal)
                            && Enum.IsDefined(typeof(DNSResponseStatus), ordinal))
                        {
                            return (DNSResponseStatus)ordinal;
                        }
                        throw new JsonException("Numeric DNS response status is outside the defined range.");
                    }
                default:
                    throw new JsonException(
                        $"DNS response status must be a string or a number, got {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, DNSResponseStatus value, JsonSerializerOptions options)
        {
            if (!WireNames.TryGetValue(value, out var wire))
            {
                // A member added to the CLR enum without a wire spelling would otherwise be written
                // as something macOS cannot decode. Refuse rather than emit unreadable evidence.
                throw new JsonException($"DNS response status {value} has no cross-platform wire spelling.");
            }
            writer.WriteStringValue(wire);
        }
    }

    /// <summary>
    /// [B251] Reads/writes a witness snapshot's display <see cref="DateTime"/> as Foundation's
    /// <c>timeIntervalSinceReferenceDate</c> — the seconds-since-2001-01-01Z double that Swift's
    /// default <c>JSONEncoder</c>/<c>JSONDecoder</c> uses for <c>Date</c> (macOS
    /// SyxxWitness.swift:77-81 encodes with no custom date strategy).
    /// </summary>
    /// <remarks>
    /// Windows previously wrote ISO-8601 ("2026-07-29T16:38:13.644356Z"), which Swift's decoder
    /// rejects outright — <c>The data couldn't be read because it isn't in the correct format</c> —
    /// so no Windows-signed snapshot could ever be verified on macOS. Reads still accept ISO-8601
    /// so snapshots written by older Windows builds remain readable.
    ///
    /// PRECISION IS LOAD-BEARING, not cosmetic. The D083 display-integrity guard requires
    /// <c>new DateTimeOffset(snapshot.Timestamp).ToUnixTimeMilliseconds() ==
    /// snapshot.Evidence.Timestamp</c> and macOS requires the mirror-image
    /// <c>UInt64(snapshot.timestamp.timeIntervalSince1970 * 1000) == evidence.timestamp</c>. Both
    /// sides TRUNCATE. A naive double conversion lands within ~4e-4 ms of the true instant, so
    /// roughly one snapshot in a thousand would truncate to the adjacent millisecond and be
    /// rejected as a DISPLAY MISMATCH — an honest snapshot failing its own verification on a timing
    /// coin-flip, which is the same defect class as D102. Both directions below are therefore
    /// constructed to be exact:
    ///  - READ derives the millisecond with the identical double arithmetic macOS performs and then
    ///    builds the DateTime from that millisecond plus the sub-millisecond remainder, so
    ///    ToUnixTimeMilliseconds() reproduces it by construction.
    ///  - WRITE emits the double, then simulates macOS's truncation on the emitted value and nudges
    ///    by single ULPs (at most ~60 ns, far below the instrument's resolution) until the
    ///    simulation agrees. IEEE-754 binary64 add/multiply are correctly rounded and identical on
    ///    both runtimes, so the simulation is exact rather than approximate.
    /// </remarks>
    public sealed class AppleReferenceDateJsonConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => ReadCore(ref reader);

        internal static DateTime ReadCore(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return FromAppleReferenceSeconds(reader.GetDouble());
                case JsonTokenType.String:
                    {
                        // Legacy Windows encoding (ISO-8601). Round-trip kind so the value stays UTC.
                        string? raw = reader.GetString();
                        if (raw is not null && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var parsed))
                        {
                            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
                        }
                        throw new JsonException($"Timestamp '{raw}' is neither a Foundation reference-date interval nor an ISO-8601 date.");
                    }
                default:
                    throw new JsonException($"Timestamp must be a number or a string, got {reader.TokenType}.");
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
            => writer.WriteNumberValue(ToAppleReferenceSeconds(value));

        /// <summary>
        /// Converts Foundation's <c>timeIntervalSinceReferenceDate</c> to a UTC
        /// <see cref="DateTime"/> whose <c>ToUnixTimeMilliseconds()</c> equals, exactly, the
        /// millisecond macOS signed for the same double.
        /// </summary>
        internal static DateTime FromAppleReferenceSeconds(double secondsSinceReferenceDate)
        {
            if (!double.IsFinite(secondsSinceReferenceDate))
                throw new JsonException("Timestamp is not a finite number of seconds.");

            // The identical double operations Foundation + the macOS signer perform.
            double unixSeconds = secondsSinceReferenceDate + WitnessWireJson.AppleToUnixEpochOffsetSeconds;
            double unixMillisExact = unixSeconds * 1000.0;

            // macOS converts with UInt64(...), which traps on a negative value: a pre-1970 witness
            // timestamp is not something macOS can sign or verify, so accepting it here would only
            // manufacture a snapshot the other platform must reject. Fail closed.
            if (unixMillisExact < 0 || unixMillisExact > 253_402_300_799_999.0)
                throw new JsonException("Timestamp is outside the range a witness snapshot can represent.");

            long unixMillis = (long)unixMillisExact;                       // truncation, as macOS does
            double remainderMillis = unixMillisExact - unixMillis;          // in [0, 1)
            long subMillisTicks = (long)(remainderMillis * TimeSpan.TicksPerMillisecond);
            if (subMillisTicks >= TimeSpan.TicksPerMillisecond) subMillisTicks = TimeSpan.TicksPerMillisecond - 1;
            if (subMillisTicks < 0) subMillisTicks = 0;

            long ticks = DateTime.UnixEpoch.Ticks
                       + unixMillis * TimeSpan.TicksPerMillisecond
                       + subMillisTicks;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Converts a <see cref="DateTime"/> to the <c>timeIntervalSinceReferenceDate</c> double
        /// macOS expects, guaranteeing that macOS's own truncation of that double reproduces the
        /// millisecond this snapshot was signed with.
        /// </summary>
        internal static double ToAppleReferenceSeconds(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            long signedMillis = new DateTimeOffset(utc).ToUnixTimeMilliseconds();

            double candidate = (utc.Ticks - WitnessWireJson.AppleReferenceDate.Ticks) / (double)TimeSpan.TicksPerSecond;

            // [C002] Defensive: every representable DateTime yields a finite candidate, but a NaN
            // must never reach the nudge loop below — Int64BitsToDouble can fabricate NaN/±∞ from
            // bit patterns, and feeding that to MacOsTruncatedUnixMillis gives an implementation-
            // defined double→long cast (long.MinValue on .NET) that could either silently mis-encode
            // or, on a runtime that casts NaN to 0, masquerade as the epoch's signedMilliseconds==0.
            // Fail loudly rather than emit an invalid date.
            if (!double.IsFinite(candidate))
                throw new JsonException("Could not encode the display timestamp: the computed reference-date interval is not finite.");

            if (MacOsTruncatedUnixMillis(candidate) == signedMillis)
                return candidate;

            // Nudge by single ULPs toward the interior of the signed millisecond. Each step moves
            // the represented instant by well under a microsecond; the loop is bounded because the
            // millisecond is ~4 orders of magnitude wider than one ULP at this magnitude.
            long bits = BitConverter.DoubleToInt64Bits(candidate);
            long direction = MacOsTruncatedUnixMillis(candidate) < signedMillis ? 1 : -1;
            for (int i = 0; i < 64; i++)
            {
                bits += direction;
                double nudged = BitConverter.Int64BitsToDouble(bits);
                // [C002] Guard: a step that lands on NaN or ±∞ (reachable only by overflowing the
                // bit pattern) must never be compared or returned, so bail to the fail-closed path
                // below instead of letting a runtime-specific NaN→long cast fabricate a match. The
                // last finite value is never emitted either — see the refusal after the loop.
                if (!double.IsFinite(nudged))
                    break;
                if (MacOsTruncatedUnixMillis(nudged) == signedMillis)
                    return nudged;
            }

            // Unreachable in practice for any representable DateTime (the initial candidate is
            // within a few ULPs of agreement, and a millisecond is thousands of ULPs wide at every
            // magnitude a DateTime can express); reachable only if a nudge step produced a
            // non-finite value. Refuse rather than fall back to the non-nudged candidate — a
            // disagreeing millisecond is the D102 defect class and would make an honest snapshot
            // read as tampered on the other platform.
            throw new JsonException("Could not encode the display timestamp without losing millisecond agreement with the signed evidence.");
        }

        /// <summary>Simulates macOS's <c>UInt64(date.timeIntervalSince1970 * 1000)</c> exactly.</summary>
        internal static long MacOsTruncatedUnixMillis(double secondsSinceReferenceDate)
            => (long)((secondsSinceReferenceDate + WitnessWireJson.AppleToUnixEpochOffsetSeconds) * 1000.0);
    }

    /// <summary>[B251] Nullable twin of <see cref="AppleReferenceDateJsonConverter"/>.</summary>
    public sealed class NullableAppleReferenceDateJsonConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return AppleReferenceDateJsonConverter.ReadCore(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteNumberValue(AppleReferenceDateJsonConverter.ToAppleReferenceSeconds(value.Value));
        }
    }
}
