using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The keys this adapter's cache entries live under, and the only place their spelling is decided.</summary>
/// <remarks>
/// The same one-decider pattern as the application's <c>SliceKeys</c>: every id involved is already
/// closed to <c>A-Z a-z 0-9 . _ -</c> by its own type, so the <c>:</c> separators cannot collide
/// with id text and two different identities cannot mangle to one key.
/// </remarks>
public static class RedisKeys
{
    /// <summary>The key one run's hot snapshot is cached under.</summary>
    /// <param name="run">The run.</param>
    public static string ForRunState(RunId run) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <summary>The key one recorded command outcome is cached under.</summary>
    /// <param name="scope">The record's sequencing domain.</param>
    /// <param name="commandId">The record's idempotency key.</param>
    public static string ForRecord(IdempotencyScope scope, CommandId commandId) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");
}
