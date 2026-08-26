using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The hot cache over the authoritative idempotency store — records only, sequences never.</summary>
/// <remarks>
/// Outcome records are safe to cache: a miss falls back to the store of record and a hit replays
/// bytes that can never change. The sequence counter is NOT cached, deliberately — a stale cached
/// counter after a lost write-behind would pass the gate for a sequence the store of record already
/// consumed, which is a double-apply. <see cref="ReadLastSequenceAsync"/> and
/// <see cref="OpenScopeAsync"/> therefore delegate straight through.
/// </remarks>
public sealed class RedisIdempotencyCache : IIdempotencyStore
{
    /// <summary>Builds the cache layer over the authoritative store.</summary>
    /// <param name="cache">The volatile byte surface.</param>
    /// <param name="inner">The authoritative store underneath.</param>
    /// <param name="failures">Where absorbed cache failures are counted.</param>
    public RedisIdempotencyCache(IVolatileByteCache cache, IIdempotencyStore inner, CacheWriteFailureCounter failures) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task RecordAsync(
        IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");
}
