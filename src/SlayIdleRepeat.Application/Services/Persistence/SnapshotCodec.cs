using System.Text.Json;
using System.Text.Json.Serialization;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Persistence;

/// <summary>The value stored under a player's key: the player, and whatever run the player is in.</summary>
/// <param name="Player">The player's persisted row. Never absent — a slice always names a player.</param>
/// <param name="Run">The run the player is in, or <c>null</c> when they are outside one.</param>
/// <remarks>
/// The pair travels under one key so one accepted command is one write. Two keys for the two
/// aggregates would leave a crash between them holding a player that disagrees with its own run, and
/// no ordering of the two writes repairs that.
/// </remarks>
public sealed record StoredSlice(PlayerSnapshot Player, RunSnapshot? Run);

/// <summary>Snapshots to UTF-8 JSON bytes and back — the serializer the byte-oriented cache leaves above it.</summary>
/// <remarks>
/// <para>
/// <c>System.Text.Json</c> over the snapshot records themselves. The snapshots are positional records
/// with exactly one public constructor, which is the shape the serializer binds without any mapping
/// layer, and the shape adding a field to would be visible here as well as everywhere else.
/// </para>
/// <para>
/// Nothing here carries a version number of its own. Each snapshot already carries the one the
/// aggregate's own rehydration checks, and a second version on the envelope would be a second
/// migration story to keep in step with the first.
/// </para>
/// <para>
/// Malformed bytes throw rather than answering with an empty or partial state: a row that cannot be
/// read is not a player who has nothing.
/// </para>
/// </remarks>
public static class SnapshotCodec
{
    /// <summary>Encodes a stored slice as UTF-8 JSON.</summary>
    /// <param name="slice">The pair to encode.</param>
    /// <returns>The UTF-8 bytes to store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="slice"/> is null.</exception>
    public static byte[] EncodeSlice(StoredSlice slice)
    {
        ArgumentNullException.ThrowIfNull(slice);

        return JsonSerializer.SerializeToUtf8Bytes(slice, Options);
    }

    /// <summary>Decodes a stored slice from UTF-8 JSON.</summary>
    /// <param name="utf8">The bytes previously written by <see cref="EncodeSlice"/>.</param>
    /// <returns>The decoded pair.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// The bytes are not a stored slice — malformed, absent, or holding a value one of these records
    /// refuses.
    /// </exception>
    public static StoredSlice DecodeSlice(ReadOnlySpan<byte> utf8) =>
        Decode<StoredSlice>(utf8, nameof(StoredSlice));

    /// <summary>Encodes one run's row as UTF-8 JSON — what the archive holds.</summary>
    /// <param name="run">The run's persisted row.</param>
    /// <returns>The UTF-8 bytes to store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    public static byte[] EncodeRun(RunSnapshot run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return JsonSerializer.SerializeToUtf8Bytes(run, Options);
    }

    /// <summary>Decodes one run's row from UTF-8 JSON.</summary>
    /// <param name="utf8">The bytes previously written by <see cref="EncodeRun"/>.</param>
    /// <returns>The decoded row.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// The bytes are not a run row — malformed, absent, or holding a value one of these records refuses.
    /// </exception>
    public static RunSnapshot DecodeRun(ReadOnlySpan<byte> utf8) =>
        Decode<RunSnapshot>(utf8, nameof(RunSnapshot));

    /// <summary>Every decode, with one answer for every way a row can fail to be one.</summary>
    /// <remarks>
    /// The value types in these rows validate in their own constructors and refuse with an argument
    /// failure, which is the same type the store raises for a caller's unstorable id. Left as it comes,
    /// a caller reading the type alone cannot tell "this row is corrupt" from "you asked with the wrong
    /// identity" — one is data to quarantine and the other is a bug in the call.
    /// </remarks>
    private static T Decode<T>(ReadOnlySpan<byte> utf8, string expected)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(utf8, Options) ?? throw NoRoot(expected);
        }
        catch (ArgumentException refused)
        {
            throw new JsonException(
                "These bytes are not a readable " + expected + ": " + refused.Message, refused);
        }
    }

    /// <summary>The one settings object every call here shares.</summary>
    /// <remarks>
    /// <para>
    /// Built once because building one per call rebuilds the whole reflection-driven contract cache
    /// for these records on every command, which is the difference between a cheap write and the most
    /// expensive step of a command.
    /// </para>
    /// <para>
    /// The five converters are named one by one rather than covered by a rule over shapes. Each is
    /// there for the same measured reason and no other: the serializer builds a value type through
    /// its implicit parameterless constructor, and these five declare their members with a getter and
    /// no setter, so every one of them would otherwise come back as the type's zeroed default — an id
    /// with no text, a wallet with no energy, an affix with no roll — and the loud failure that
    /// follows would name the row rather than the codec. Every other member of these snapshots is a
    /// primitive, an enum, a collection, or a record class the serializer binds through its one
    /// public constructor, and needs nothing here.
    /// </para>
    /// </remarks>
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters =
        {
            new PlayerIdConverter(),
            new RunIdConverter(),
            new GearInstanceIdConverter(),
            new EnergyBanksConverter(),
            new GearAffixRollConverter(),
        },
    };

    private static JsonException NoRoot(string expected) =>
        new(
            "These bytes decoded to nothing where a " + expected + " was expected. A row that reads as " +
            "an absent value is not a player who has nothing: answering with null here would hand the " +
            "caller a fresh account and overwrite the real one on the next command.");

    /// <summary>The text of an identifier written as a bare JSON string.</summary>
    private static string IdTextOf(ref Utf8JsonReader reader, string expected) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException(
                "A " + expected + " is stored as a JSON string and this row holds a " +
                reader.TokenType + " where one belongs.");

    private static T RowOf<T>(ref Utf8JsonReader reader, JsonSerializerOptions options, string expected)
        where T : class =>
        JsonSerializer.Deserialize<T>(ref reader, options)
        ?? throw new JsonException("This row holds nothing where a " + expected + " belongs.");

    private sealed class PlayerIdConverter : JsonConverter<PlayerId>
    {
        public override PlayerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(IdTextOf(ref reader, nameof(PlayerId)));

        public override void Write(Utf8JsonWriter writer, PlayerId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class RunIdConverter : JsonConverter<RunId>
    {
        public override RunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(IdTextOf(ref reader, nameof(RunId)));

        public override void Write(Utf8JsonWriter writer, RunId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value.Value);
        }
    }

    private sealed class GearInstanceIdConverter : JsonConverter<GearInstanceId>
    {
        public override GearInstanceId Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(IdTextOf(ref reader, nameof(GearInstanceId)));

        public override void Write(Utf8JsonWriter writer, GearInstanceId value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value.Value);
        }
    }

    /// <summary>The two Energy banks in the shape the serializer can build unaided.</summary>
    private sealed record EnergyRow(int Energy, int Reserve);

    private sealed class EnergyBanksConverter : JsonConverter<EnergyBanks>
    {
        public override EnergyBanks Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var row = RowOf<EnergyRow>(ref reader, options, nameof(EnergyBanks));

            return new EnergyBanks(row.Energy, row.Reserve);
        }

        public override void Write(Utf8JsonWriter writer, EnergyBanks value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new EnergyRow(value.Energy, value.Reserve), options);
    }

    /// <inheritdoc cref="EnergyRow"/>
    private sealed record AffixRow(string AffixId, double Value);

    private sealed class GearAffixRollConverter : JsonConverter<GearAffixRoll>
    {
        public override GearAffixRoll Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var row = RowOf<AffixRow>(ref reader, options, nameof(GearAffixRoll));

            return new GearAffixRoll(row.AffixId, row.Value);
        }

        public override void Write(Utf8JsonWriter writer, GearAffixRoll value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new AffixRow(value.AffixId, value.Value), options);
    }
}
