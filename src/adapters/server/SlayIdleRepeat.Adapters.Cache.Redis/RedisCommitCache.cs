using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The write-behind cache population that rides after an authoritative commit, never inside it.</summary>
/// <remarks>
/// <para>
/// The inner unit of work commits first and is the only thing that decides whether the command
/// happened. Only then are the run-state and record entries populated, best effort: every
/// non-cancellation cache fault is absorbed into the counter, because a cache that cannot be
/// written is a slower next read and never a failed command.
/// </para>
/// <para>
/// The sequence counter is not populated here, by the same rule its sibling decorators keep: a
/// stale cached counter after a lost write would pass the gate for a sequence the store of record
/// already consumed, which is a double-apply.
/// </para>
/// </remarks>
public sealed class RedisCommitCache : IUnitOfWork
{
    private readonly IVolatileByteCache _cache;
    private readonly IUnitOfWork _inner;
    private readonly CacheFailureCounter _failures;
    private readonly TimeSpan _ttl;

    /// <summary>Builds the population layer over the authoritative unit of work.</summary>
    /// <param name="cache">The volatile byte surface.</param>
    /// <param name="inner">The unit of work that actually commits.</param>
    /// <param name="failures">Where absorbed cache failures are counted.</param>
    /// <param name="ttl">How long a populated entry lives. Positive.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ttl"/> is zero or negative.</exception>
    public RedisCommitCache(
        IVolatileByteCache cache, IUnitOfWork inner, CacheFailureCounter failures, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(failures);

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl), ttl, "A non-positive lifetime would expire every populated entry at birth.");
        }

        _cache = cache;
        _inner = inner;
        _failures = failures;
        _ttl = ttl;
    }

    /// <inheritdoc/>
    public Task CommitAsync(CommandCommit commit, CancellationToken ct) =>
        throw new NotImplementedException(
            "Write-behind population is not implemented yet: commit through the inner unit of work "
            + "first, then populate the run-state and record entries, absorbing every "
            + "non-cancellation cache fault into the counter.");
}
