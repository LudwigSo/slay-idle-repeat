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
    private const string Prefix = "sir:";

    /// <summary>The key one run's hot snapshot is cached under.</summary>
    /// <param name="run">The run.</param>
    /// <exception cref="ArgumentException"><paramref name="run"/> is a default struct with no text.</exception>
    public static string ForRunState(RunId run) =>
        Prefix + "run:" + RequireText(run.Value, nameof(run));

    /// <summary>The key one recorded command outcome is cached under.</summary>
    /// <param name="scope">The record's sequencing domain.</param>
    /// <param name="commandId">The record's idempotency key.</param>
    /// <exception cref="ArgumentException"><paramref name="commandId"/> is a default struct with no text.</exception>
    public static string ForRecord(IdempotencyScope scope, CommandId commandId)
    {
        var command = RequireText(commandId.Value, nameof(commandId));

        return scope.Kind == IdempotencyScopeKind.Run
            ? Prefix + "idem:run:" + scope.Player.Value + ":" + scope.Run!.Value.Value + ":" + command
            : Prefix + "idem:player:" + scope.Player.Value + ":" + command;
    }

    private static string RequireText(string? value, string parameterName) =>
        value ?? throw new ArgumentException(
            "This id carries no text (a default struct) — a key built on it would pool every such " +
            "caller's entries under one name.",
            parameterName);
}
