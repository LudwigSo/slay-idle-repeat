using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Persistence;
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
    private readonly IVolatileByteCache _cache;
    private readonly IRunStateStore _inner;
    private readonly CacheFailureCounter _failures;

    /// <summary>Builds the cache layer over the authoritative store.</summary>
    /// <param name="cache">The volatile byte surface.</param>
    /// <param name="inner">The authoritative store underneath.</param>
    /// <param name="failures">Where absorbed cache failures are counted.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public RedisRunStateCache(IVolatileByteCache cache, IRunStateStore inner, CacheFailureCounter failures)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(failures);

        _cache = cache;
        _inner = inner;
        _failures = failures;
    }

    /// <inheritdoc/>
    public async Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct)
    {
        var key = RedisKeys.ForRunState(id);
        var cacheAnswered = true;

        try
        {
            if (await _cache.GetAsync(key, ct).ConfigureAwait(false) is { } cached)
            {
                return SnapshotCodec.DecodeRun(cached);
            }
        }
        catch (Exception failure) when (IsAbsorbable(failure))
        {
            // One count per absorbed read: the repopulate below is skipped rather than attempted
            // against an endpoint that just failed, which would double the count and the latency.
            _failures.Increment();
            cacheAnswered = false;
        }

        var authoritative = await _inner.GetAsync(id, ct).ConfigureAwait(false);

        if (authoritative is not null && cacheAnswered)
        {
            // Best-effort repopulate, or every read after a flush pays the fallback forever. The
            // true lifetime is the authority's; this entry rides a short one until the next save
            // restamps it.
            await TrySetAsync(key, SnapshotCodec.EncodeRun(authoritative), RepopulateTtl, ct)
                .ConfigureAwait(false);
        }

        return authoritative;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);

        // The authority first: a command that reached the cache but not the row would be progress
        // the next flush deletes.
        await _inner.SaveAsync(state, ttl, ct).ConfigureAwait(false);

        await TrySetAsync(RedisKeys.ForRunState(state.Id), SnapshotCodec.EncodeRun(state), ttl, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(RunId id, CancellationToken ct)
    {
        await _inner.DeleteAsync(id, ct).ConfigureAwait(false);

        try
        {
            await _cache.DeleteAsync(RedisKeys.ForRunState(id), ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsAbsorbable(failure))
        {
            // The stale entry this leaves is bounded by its own lifetime — the accepted cost of
            // never failing a command on a cache.
            _failures.Increment();
        }
    }

    /// <summary>What a repopulated entry rides with when no save has stated a lifetime: the configured window's shape, conservatively short.</summary>
    private static readonly TimeSpan RepopulateTtl = TimeSpan.FromHours(1);

    private async Task TrySetAsync(string key, byte[] value, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(key, value, ttl, ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (IsAbsorbable(failure))
        {
            _failures.Increment();
        }
    }

    /// <summary>Everything except cancellation — the caller's own stop signal is never a cache failure.</summary>
    internal static bool IsAbsorbable(Exception failure) => failure is not OperationCanceledException;
}
