using System.Text.Json;
using System.Text.Json.Serialization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>
/// <see cref="GearInstanceId"/> on the wire: the bare identifier string, both directions.
/// </summary>
/// <remarks>
/// Without this the id would ride as <c>{"value": "..."}</c> — the record's shape rather than the
/// identifier — which is not what 14 §2.3's payload sketches (<c>{ itemId, gearSlot }</c>) spell.
/// The constructor's own guard runs on the read path, so a blank or whitespace id surfaces as the
/// <see cref="ArgumentException"/> the codec turns into <c>MALFORMED_COMMAND</c>.
/// </remarks>
internal sealed class GearInstanceIdJsonConverter : JsonConverter<GearInstanceId>
{
    /// <inheritdoc/>
    public override GearInstanceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new GearInstanceId(reader.GetString()!)
            : throw new JsonException("A gear instance id is a JSON string.");

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, GearInstanceId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);

    /// <inheritdoc/>
    /// <remarks>For <c>IReadOnlyDictionary&lt;GearSlot, GearInstanceId&gt;</c>-shaped members, should one ever key on the id instead.</remarks>
    public override GearInstanceId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString()!);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, GearInstanceId value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Value);
}
