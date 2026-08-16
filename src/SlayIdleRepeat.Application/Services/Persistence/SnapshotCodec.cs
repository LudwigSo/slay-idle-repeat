using SlayIdleRepeat.Core.Model.Snapshots;

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
    public static byte[] EncodeSlice(StoredSlice slice) => throw new NotImplementedException();

    /// <summary>Decodes a stored slice from UTF-8 JSON.</summary>
    /// <param name="utf8">The bytes previously written by <see cref="EncodeSlice"/>.</param>
    /// <returns>The decoded pair.</returns>
    /// <exception cref="System.Text.Json.JsonException">The bytes are not a stored slice.</exception>
    public static StoredSlice DecodeSlice(ReadOnlySpan<byte> utf8) => throw new NotImplementedException();

    /// <summary>Encodes one run's row as UTF-8 JSON — what the archive holds.</summary>
    /// <param name="run">The run's persisted row.</param>
    /// <returns>The UTF-8 bytes to store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    public static byte[] EncodeRun(RunSnapshot run) => throw new NotImplementedException();

    /// <summary>Decodes one run's row from UTF-8 JSON.</summary>
    /// <param name="utf8">The bytes previously written by <see cref="EncodeRun"/>.</param>
    /// <returns>The decoded row.</returns>
    /// <exception cref="System.Text.Json.JsonException">The bytes are not a run row.</exception>
    public static RunSnapshot DecodeRun(ReadOnlySpan<byte> utf8) => throw new NotImplementedException();
}
