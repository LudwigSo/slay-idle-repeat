namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>This adapter's at-rest compression: gzip in, gzip out, byte-identical round-trips.</summary>
/// <remarks>Adapter-internal on purpose: the port speaks the log's own bytes, and where they are compressed is a backing detail.</remarks>
public static class BattleLogCompression
{
    /// <summary>The bytes as stored: gzip of the log.</summary>
    /// <param name="log">The log's bytes. May be empty.</param>
    public static byte[] Compress(ReadOnlyMemory<byte> log) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <summary>The log's bytes back from storage.</summary>
    /// <param name="stored">Bytes previously produced by <see cref="Compress"/>.</param>
    public static byte[] Decompress(ReadOnlyMemory<byte> stored) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");
}
