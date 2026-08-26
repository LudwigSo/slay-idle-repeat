using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IPlayerRepository"/> — the fake half of the port's 23 §5 A5 pair.</summary>
/// <remarks>
/// Rows are held as the canonical codec bytes and re-decoded per read, the way the real backing
/// stores them: an aliased in-memory object would let a caller's later mutation change "committed"
/// state, and byte storage is what makes the shared suite's byte-identity cases mean the same thing
/// against both implementations.
/// </remarks>
public sealed class InMemoryPlayerRepository : IPlayerRepository
{
    private readonly ConcurrentDictionary<string, byte[]> _rows = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<PlayerProfile?> GetAsync(PlayerId id, CancellationToken ct)
    {
        RequireId(id);
        ct.ThrowIfCancellationRequested();

        if (!_rows.TryGetValue(id.Value, out var row))
        {
            return Task.FromResult<PlayerProfile?>(null);
        }

        var stored = SnapshotCodec.DecodeSlice(row);

        return Task.FromResult<PlayerProfile?>(new PlayerProfile(stored.Player, stored.Run));
    }

    /// <inheritdoc/>
    public Task SaveAsync(PlayerProfile profile, CancellationToken ct)
    {
        var (id, row) = EncodedRowOf(profile);
        ct.ThrowIfCancellationRequested();

        _rows[id] = row;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<PlayerId> CreateAnonymousAsync(PlayerProfile initial, CancellationToken ct)
    {
        var (id, row) = EncodedRowOf(initial);
        ct.ThrowIfCancellationRequested();

        return _rows.TryAdd(id, row)
            ? Task.FromResult(initial.Player.Id)
            : throw new InvalidOperationException(
                "A row already exists for player " + initial.Player.Id + ". Server-minted ids never " +
                "collide, so this is a miswired caller — overwriting would hand one player another's " +
                "account.");
    }

    private static (string Id, byte[] Row) EncodedRowOf(PlayerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        RequireId(profile.Player.Id);

        return (profile.Player.Id.Value,
            SnapshotCodec.EncodeSlice(new StoredSlice(profile.Player, profile.ActiveRun)));
    }

    private static void RequireId(PlayerId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "default(PlayerId) carries no text — a row keyed on it would pool every such " +
                "caller's state into one row.",
                nameof(id));
        }
    }
}
