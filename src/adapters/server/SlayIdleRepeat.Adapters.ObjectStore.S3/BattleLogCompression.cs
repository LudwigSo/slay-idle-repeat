using System.IO.Compression;

namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>This adapter's at-rest compression: gzip in, gzip out, byte-identical round-trips.</summary>
/// <remarks>Adapter-internal on purpose: the port speaks the log's own bytes, and where they are compressed is a backing detail.</remarks>
public static class BattleLogCompression
{
    /// <summary>The bytes as stored: gzip of the log.</summary>
    /// <param name="log">The log's bytes. May be empty.</param>
    public static byte[] Compress(ReadOnlyMemory<byte> log)
    {
        using var stored = new MemoryStream();

        using (var gzip = new GZipStream(stored, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(log.Span);
        }

        return stored.ToArray();
    }

    /// <summary>The log's bytes back from storage.</summary>
    /// <param name="stored">Bytes previously produced by <see cref="Compress"/>.</param>
    /// <exception cref="InvalidDataException">The bytes are not gzip — a corrupt object, refused loudly rather than replayed as garbage.</exception>
    public static byte[] Decompress(ReadOnlyMemory<byte> stored)
    {
        using var source = new MemoryStream(stored.ToArray(), writable: false);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var log = new MemoryStream();

        gzip.CopyTo(log);

        return log.ToArray();
    }
}
