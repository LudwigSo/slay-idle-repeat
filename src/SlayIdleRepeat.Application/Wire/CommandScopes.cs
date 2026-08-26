using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>The spelling of a ledger scope key, and its mapping to the typed sequencing domain — decided once.</summary>
/// <remarks>
/// The gateway builds every scope key through here, and the durable ledger resolves the same keys
/// back into <see cref="IdempotencyScope"/>s — so the spelling and the parse cannot drift, and the
/// <c>:</c> separators are safe because both id types refuse text containing one.
/// </remarks>
public static class CommandScopes
{
    private const string RunPrefix = "run:";
    private const string PlayerPrefix = "player:";

    /// <summary>The key of one run's sequencing domain: <c>run:&lt;playerId&gt;:&lt;runId&gt;</c>.</summary>
    /// <param name="player">The run's owner.</param>
    /// <param name="run">The run.</param>
    public static string ForRun(PlayerId player, RunId run)
    {
        // Constructing the typed scope first runs its blank-id guards, so the string can only ever
        // spell a domain the resolver will accept back.
        _ = IdempotencyScope.ForRun(player, run);

        return RunPrefix + player.Value + ":" + run.Value;
    }

    /// <summary>The key of a player's lifetime sequencing domain: <c>player:&lt;playerId&gt;</c>.</summary>
    /// <param name="player">The player.</param>
    public static string ForPlayer(PlayerId player)
    {
        _ = IdempotencyScope.ForPlayer(player);

        return PlayerPrefix + player.Value;
    }

    /// <summary>The typed domain a scope key names.</summary>
    /// <param name="scope">A key previously produced by <see cref="ForRun"/> or <see cref="ForPlayer"/>.</param>
    /// <exception cref="ArgumentException">The text is not a scope key this class ever produced.</exception>
    public static IdempotencyScope Resolve(string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.StartsWith(PlayerPrefix, StringComparison.Ordinal))
        {
            var player = scope[PlayerPrefix.Length..];

            if (player.Length > 0)
            {
                return IdempotencyScope.ForPlayer(new PlayerId(player));
            }
        }
        else if (scope.StartsWith(RunPrefix, StringComparison.Ordinal))
        {
            var body = scope[RunPrefix.Length..];

            // Ids cannot contain ':' (their spelling is closed), so the first one is the split.
            var split = body.IndexOf(':', StringComparison.Ordinal);

            if (split > 0 && split < body.Length - 1)
            {
                return IdempotencyScope.ForRun(
                    new PlayerId(body[..split]), new RunId(body[(split + 1)..]));
            }
        }

        throw new ArgumentException(
            "'" + scope + "' is not a scope key this repository ever spelled ('player:<id>' or " +
            "'run:<playerId>:<runId>'). Resolving it by guess would file the record where no " +
            "reader will ever look for it.",
            nameof(scope));
    }
}
