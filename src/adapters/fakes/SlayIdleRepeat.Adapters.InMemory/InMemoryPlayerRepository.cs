using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IPlayerRepository"/> — the fake half of the port's 23 §5 A5 pair.</summary>
public sealed class InMemoryPlayerRepository : IPlayerRepository
{
    /// <inheritdoc/>
    public Task<PlayerProfile?> GetAsync(PlayerId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task SaveAsync(PlayerProfile profile, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task<PlayerId> CreateAnonymousAsync(PlayerProfile initial, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");
}
