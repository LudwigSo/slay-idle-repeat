using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// Stores and restores <see cref="StateMirror"/> through the byte cache, gzipped at the boundary.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 The serializer lives entirely ABOVE the port, which stays byte-oriented: a generic read would
/// put serialization inside the port and leave each implementation free to store something else.
/// </para>
/// <para>
/// 🔒 A restore fills the mirror only by handing it an answer shaped exactly like a server's, so a
/// hit can carry nothing a server could not have said — and it is overwritten by the first real
/// answer, because a reconnect owes a resync before anything may be believed.
/// </para>
/// </remarks>
public sealed class MirrorCache
{
    /// <summary>The one key the mirror lives under, inside the port's closed ordinal key space.</summary>
    public const string MirrorKey = "mirror.serverState";

    /// <summary>Wires the round-trip over the byte cache it stores through.</summary>
    /// <param name="cache">The on-device byte cache.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    public MirrorCache(ILocalCachePort cache) => ArgumentNullException.ThrowIfNull(cache);

    /// <summary>Fills the mirror from the last thing the server said, if anything is stored.</summary>
    /// <param name="mirror">The mirror to fill.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>True when a stored answer was applied.</returns>
    /// <remarks>A malformed or unknown-version blob is a MISS, never a fault.</remarks>
    public Task<bool> RestoreAsync(StateMirror mirror, CancellationToken ct) =>
        throw new NotImplementedException();

    /// <summary>Stores what the mirror currently holds.</summary>
    /// <param name="mirror">The mirror to read.</param>
    /// <param name="ct">Cancellation.</param>
    public Task PersistAsync(StateMirror mirror, CancellationToken ct) =>
        throw new NotImplementedException();
}
