using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>The store of run rows — the authoritative row plus whatever hot cache sits in front of it.</summary>
/// <remarks>
/// <para>
/// One port for both layers, composed as a decorator at the root: the cache implementation takes an
/// inner <see cref="IRunStateStore"/> and answers reads from memory, falling back to — and
/// repopulating from — the authoritative row underneath. A caller cannot tell which layer answered,
/// which is the whole claim "flushing the cache costs latency, never progress" rests on.
/// </para>
/// <para>
/// <paramref name="ttl"/> on <see cref="SaveAsync"/> is the run's sliding lifetime, restamped on
/// every save: measured from the last accepted command, so an active run never expires under the
/// player. Expiry is enforced where expiry is real — a store reading an expired row answers
/// <c>null</c>; nothing here schedules deletion, because no retention rule is authored.
/// </para>
/// </remarks>
public interface IRunStateStore
{
    /// <summary>The run's stored snapshot, or <c>null</c> when none is stored or its lifetime has passed.</summary>
    /// <param name="id">The run.</param>
    /// <param name="ct">Cancellation.</param>
    Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct);

    /// <summary>Commits a run's snapshot and restamps its sliding lifetime.</summary>
    /// <param name="state">The run's snapshot, exactly as the domain produced it.</param>
    /// <param name="ttl">The sliding lifetime, measured from now. Positive.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is zero or negative — a row born expired is a delete spelled confusingly.</exception>
    Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct);

    /// <summary>Removes a run's row. Removing an absent row is a no-op, not a fault.</summary>
    /// <param name="id">The run.</param>
    /// <param name="ct">Cancellation.</param>
    Task DeleteAsync(RunId id, CancellationToken ct);
}
