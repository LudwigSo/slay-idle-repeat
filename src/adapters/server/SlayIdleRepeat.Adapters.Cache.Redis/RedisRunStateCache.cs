using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The hot cache over the authoritative run store — a decorator, never a second system of record.</summary>
/// <remarks>
/// Reads answer from the cache and fall back to — and best-effort repopulate from — the inner
/// store. Writes commit to the inner store FIRST; the cache write behind it may fail and the
/// command still happened, so the failure is absorbed and counted. Flushing the cache costs
/// latency, never progress.
/// </remarks>
public sealed class RedisRunStateCache : IRunStateStore
{
    /// <summary>Builds the cache layer over the authoritative store.</summary>
    /// <param name="cache">The volatile byte surface.</param>
    /// <param name="inner">The authoritative store underneath.</param>
    /// <param name="failures">Where absorbed cache failures are counted.</param>
    public RedisRunStateCache(IVolatileByteCache cache, IRunStateStore inner, CacheWriteFailureCounter failures) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task DeleteAsync(RunId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");
}
