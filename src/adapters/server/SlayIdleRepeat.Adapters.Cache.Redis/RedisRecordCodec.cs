using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>Recorded command outcomes to UTF-8 JSON bytes and back — this adapter's one value codec.</summary>
public static class RedisRecordCodec
{
    /// <summary>Encodes a record as UTF-8 JSON.</summary>
    /// <param name="record">The record.</param>
    public static byte[] Encode(RecordedCommandOutcome record) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <summary>Decodes a record from UTF-8 JSON.</summary>
    /// <param name="utf8">Bytes previously written by <see cref="Encode"/>.</param>
    public static RecordedCommandOutcome Decode(ReadOnlySpan<byte> utf8) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");
}
