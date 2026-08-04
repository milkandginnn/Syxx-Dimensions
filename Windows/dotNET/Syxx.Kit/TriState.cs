using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Syxx.Kit
{
    /// <summary>Canonical result tri-state, shared across the Syxx kits (macOS, Windows,
    /// Server). The three raw values ("success", "failure", "inconclusive") are the ONLY
    /// wire-format strings the client kits accept; any other spelling breaks byte-identical
    /// JSON parity with the macOS SyxxKit twin and the Python server.
    ///
    /// <c>Inconclusive</c> is NOT a negative result: it means the check could not reach a
    /// conclusion (timeout, network error, missing data, ...). Surfaces that display it must
    /// NEVER read it as "the condition did not hold" — that is what <c>Failure</c> is for.
    ///
    /// Distinct from <c>ConditionState</c> (Yes/No/Unknown), which describes network
    /// conditions that can genuinely be unknown (VPN active, captive portal intercepting,
    /// DNS encrypted).</summary>
    [JsonConverter(typeof(TriStateJsonConverter))]
    public enum TriState
    {
        Success,
        Failure,
        Inconclusive,
    }

    /// <summary>Explicit JSON converter so the wire format is the lowercase strings
    /// "success"/"failure"/"inconclusive" — byte-identical with the Swift raw values
    /// and the Python <c>TriState.value</c>. Default System.Text.Json enum serialisation
    /// would emit PascalCase member names, which would silently break cross-platform
    /// parity.</summary>
    public sealed class TriStateJsonConverter : JsonConverter<TriState>
    {
        public override TriState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetString() switch
            {
                "success" => TriState.Success,
                "failure" => TriState.Failure,
                "inconclusive" => TriState.Inconclusive,
                var s => throw new JsonException($"unknown TriState '{s}'"),
            };

        public override void Write(Utf8JsonWriter writer, TriState value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                TriState.Success => "success",
                TriState.Failure => "failure",
                TriState.Inconclusive => "inconclusive",
                _ => throw new JsonException($"unhandled TriState '{value}'"),
            });
    }
}
