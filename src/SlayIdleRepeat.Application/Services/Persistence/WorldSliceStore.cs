using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Persistence;

/// <summary>
/// Loads and commits the aggregates one command reads or writes, over the byte cache the in-process
/// host gives this layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Commit order is the whole of this type's design.</b> A finished run is copied to its own
/// archive row <i>first</i>, and the player row — which carries the run inline — is written
/// <i>last</i>. A crash between the two leaves an archived copy of a run the committed state does
/// not yet call finished, and the retried command rewrites the same bytes. The reverse order loses
/// the archive for good, with the run already over and nothing left to copy.
/// </para>
/// <para>
/// A finished run's archive row is written once and never overwritten or deleted here. It is keyed by
/// the run's own identity, which the domain already keeps distinct, so a later run cannot displace an
/// earlier one's finished row. Nothing expires it either: expiry belongs to a store that can expire
/// keys, not to a use case.
/// </para>
/// <para>
/// A player with no stored row throws rather than answering with a blank player. Account creation is
/// somewhere else's; a caller here has been handed an identity the game already issued, so a miss is
/// a miswired host and not a state to invent a value for.
/// </para>
/// </remarks>
public sealed class WorldSliceStore
{
    /// <summary>Builds a store over one byte cache.</summary>
    /// <param name="cache">The cache the rows live in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    public WorldSliceStore(ILocalCachePort cache) => throw new NotImplementedException();

    /// <summary>The aggregates a command for this player may read or write.</summary>
    /// <param name="player">The player whose row to read.</param>
    /// <param name="content">The content set the player is rehydrated against.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The player, and whatever run their row carries.</returns>
    /// <exception cref="InvalidOperationException">
    /// No row is stored for this player, or the stored row does not rehydrate — a corrupt or
    /// unreadable row fails loudly here rather than several rules deeper.
    /// </exception>
    public Task<WorldSlice> LoadAsync(PlayerId player, ContentSnapshot content, CancellationToken ct) =>
        throw new NotImplementedException();

    /// <summary>Commits a slice: the finished-run archive first, the player row last.</summary>
    /// <param name="slice">The state to commit, exactly as the domain produced it.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="slice"/> is null.</exception>
    public Task SaveAsync(WorldSlice slice, CancellationToken ct) => throw new NotImplementedException();

    /// <summary>The player's stored rows, without rehydrating anything — the read side's door.</summary>
    /// <param name="player">The player whose row to read.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The stored pair, or <c>null</c> when nothing is stored for this player.</returns>
    public Task<StoredSlice?> ReadSnapshotsAsync(PlayerId player, CancellationToken ct) =>
        throw new NotImplementedException();

    /// <summary>The archived row of a run that has finished.</summary>
    /// <param name="run">The run to read.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The archived row, or <c>null</c> when no run was ever archived under that identity.</returns>
    public Task<RunSnapshot?> ReadArchivedRunAsync(RunId run, CancellationToken ct) =>
        throw new NotImplementedException();
}
