using System.Text.Json;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>Recorded command outcomes to UTF-8 JSON bytes and back — this adapter's one value codec.</summary>
/// <remarks>
/// A private row shape rather than serialising the record directly: <c>CommandId</c> declares its
/// property with a getter the serializer cannot rebuild, and a converter roster here would be a
/// second registration of knowledge the row shape states in one line.
/// </remarks>
public static class RedisRecordCodec
{
    private sealed record Row(
        string CommandId,
        long Sequence,
        string CommandType,
        string PayloadJson,
        string ResponseBody,
        string? OpensScope);

    /// <summary>Encodes a record as UTF-8 JSON.</summary>
    /// <param name="record">The record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is null.</exception>
    public static byte[] Encode(RecordedCommandOutcome record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return JsonSerializer.SerializeToUtf8Bytes(new Row(
            record.CommandId.Value, record.Sequence, record.CommandType,
            record.PayloadJson, record.ResponseBody, record.OpensScope));
    }

    /// <summary>Decodes a record from UTF-8 JSON.</summary>
    /// <param name="utf8">Bytes previously written by <see cref="Encode"/>.</param>
    /// <exception cref="JsonException">The bytes are not a readable record — a corrupt cache entry, which the decorators treat as a miss.</exception>
    public static RecordedCommandOutcome Decode(ReadOnlySpan<byte> utf8)
    {
        var row = JsonSerializer.Deserialize<Row>(utf8)
            ?? throw new JsonException("These bytes decoded to nothing where a cached record was expected.");

        return new RecordedCommandOutcome(
            new CommandId(row.CommandId), row.Sequence, row.CommandType,
            row.PayloadJson, row.ResponseBody, row.OpensScope);
    }
}
