using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>The in-memory <see cref="IRunStateStore"/> — expiry is real here, against the injected clock.</summary>
public sealed class InMemoryRunStateStore : IRunStateStore
{
    /// <summary>Builds a store whose sliding lifetimes are measured on <paramref name="clock"/>.</summary>
    /// <param name="clock">The clock expiry is measured against — an adjustable one in tests.</param>
    public InMemoryRunStateStore(IClockPort clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
    }

    /// <inheritdoc/>
    public Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");

    /// <inheritdoc/>
    public Task DeleteAsync(RunId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements this fake.");
}
