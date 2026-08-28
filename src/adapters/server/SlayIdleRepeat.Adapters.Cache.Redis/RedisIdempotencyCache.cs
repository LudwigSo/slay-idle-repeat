using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The hot cache over the authoritative idempotency store — records only, sequences never.</summary>
/// <remarks>
/// Outcome records are safe to cache: a miss falls back to the store of record and a hit replays
/// bytes that can never change. The sequence counter is NOT cached, deliberately — a stale cached
/// counter after a lost write-behind would pass the gate for a sequence the store of record already
/// consumed, which is a double-apply. <see cref="ReadLastSequenceAsync"/>,
/// <see cref="OpenScopeAsync"/> and <see cref="ReadOutcomesAfterAsync"/> therefore delegate straight
/// through.
/// </remarks>
public sealed class RedisIdempotencyCache : IIdempotencyStore
{
    private readonly IVolatileByteCache _cache;
    private readonly IIdempotencyStore _inner;
    private readonly CacheFailureCounter _failures;

    /// <summary>Builds the cache layer over the authoritative store.</summary>
    /// <param name="cache">The volatile byte surface.</param>
    /// <param name="inner">The authoritative store underneath.</param>
    /// <param name="failures">Where absorbed cache failures are counted.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public RedisIdempotencyCache(IVolatileByteCache cache, IIdempotencyStore inner, CacheFailureCounter failures)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(failures);

        _cache = cache;
        _inner = inner;
        _failures = failures;
    }

    /// <inheritdoc/>
    public async Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct)
    {
        var key = RedisKeys.ForRecord(scope, commandId);
        var cacheAnswered = true;

        try
        {
            if (await _cache.GetAsync(key, ct).ConfigureAwait(false) is { } cached)
            {
                return RedisRecordCodec.Decode(cached);
            }
        }
        catch (Exception failure) when (RedisRunStateCache.IsAbsorbable(failure))
        {
            // One count per absorbed read; the repopulate is skipped rather than attempted against
            // an endpoint that just failed.
            _failures.Increment();
            cacheAnswered = false;
        }

        var authoritative = await _inner.GetRecordedOutcomeAsync(scope, commandId, ct).ConfigureAwait(false);

        if (authoritative is not null && cacheAnswered)
        {
            await TrySetAsync(key, authoritative, RepopulateTtl, ct).ConfigureAwait(false);
        }

        return authoritative;
    }

    /// <inheritdoc/>
    public async Task RecordAsync(
        IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        // The authority first — the atomic record-plus-advance is its; the cache holds a copy of
        // bytes that can never change.
        await _inner.RecordAsync(scope, outcome, ttl, ct).ConfigureAwait(false);

        // A run record's bytes never change but its EXISTENCE follows the run row, which can end
        // early — so its cache entry never promises longer than the short window, or a cache-on
        // process would replay a record its authority already answers null for.
        var cacheTtl = scope.Kind == IdempotencyScopeKind.Run ? RepopulateTtl : ttl;

        await TrySetAsync(RedisKeys.ForRecord(scope, outcome.CommandId), outcome, cacheTtl, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Uncached, straight through, for the same reason the counter is: a key-value surface cannot
    /// enumerate a scope by sequence, and a cache is rebuildable — never a system of record — so it
    /// has no standing to say what a client missed. Answering from here would let an evicted entry
    /// read as "nothing was recorded above that sequence", which is the one lie this contract's
    /// empty list must never carry.
    /// </remarks>
    public Task<IReadOnlyList<RecordedCommandOutcome>> ReadOutcomesAfterAsync(
        IdempotencyScope scope, long sinceSequence, CancellationToken ct) =>
        _inner.ReadOutcomesAfterAsync(scope, sinceSequence, ct);

    /// <inheritdoc/>
    public Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct) =>
        _inner.ReadLastSequenceAsync(scope, ct);

    /// <inheritdoc/>
    public Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct) =>
        _inner.OpenScopeAsync(scope, ct);

    /// <summary>What a repopulated entry rides with — its true remaining lifetime is the authority's business.</summary>
    /// <remarks>
    /// Internal because the commit-time population layer keeps the same rule for a run-scoped
    /// record, and the two answers to "how long may a cached record promise to exist" have to be
    /// one answer.
    /// </remarks>
    internal static readonly TimeSpan RepopulateTtl = TimeSpan.FromHours(1);

    private async Task TrySetAsync(
        string key, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(key, RedisRecordCodec.Encode(outcome), ttl, ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (RedisRunStateCache.IsAbsorbable(failure))
        {
            _failures.Increment();
        }
    }
}
