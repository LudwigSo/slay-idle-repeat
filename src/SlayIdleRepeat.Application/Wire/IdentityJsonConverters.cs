using System.Text.Json;
using System.Text.Json.Serialization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary><see cref="PlayerId"/> on the wire: the bare identifier string.</summary>
/// <remarks>Same argument as <see cref="GearInstanceIdJsonConverter"/>: 14 §2.3 spells ids as strings, not as the record shape that guards them.</remarks>
internal sealed class PlayerIdJsonConverter : JsonConverter<PlayerId>
{
    /// <inheritdoc/>
    public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new PlayerId(reader.GetString()!)
            : throw new JsonException("A player id is a JSON string.");

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary><see cref="RunId"/> on the wire: the bare identifier string.</summary>
internal sealed class RunIdJsonConverter : JsonConverter<RunId>
{
    /// <inheritdoc/>
    public override RunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new RunId(reader.GetString()!)
            : throw new JsonException("A run id is a JSON string.");

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RunId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
