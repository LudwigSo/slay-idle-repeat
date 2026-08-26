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
    /// <summary>The key of one run's sequencing domain: <c>run:&lt;playerId&gt;:&lt;runId&gt;</c>.</summary>
    /// <param name="player">The run's owner.</param>
    /// <param name="run">The run.</param>
    public static string ForRun(PlayerId player, RunId run) =>
        throw new NotImplementedException("M5-05 phase 3 implements the scope spelling.");

    /// <summary>The key of a player's lifetime sequencing domain: <c>player:&lt;playerId&gt;</c>.</summary>
    /// <param name="player">The player.</param>
    public static string ForPlayer(PlayerId player) =>
        throw new NotImplementedException("M5-05 phase 3 implements the scope spelling.");

    /// <summary>The typed domain a scope key names.</summary>
    /// <param name="scope">A key previously produced by <see cref="ForRun"/> or <see cref="ForPlayer"/>.</param>
    /// <exception cref="ArgumentException">The text is not a scope key this class ever produced.</exception>
    public static IdempotencyScope Resolve(string scope) =>
        throw new NotImplementedException("M5-05 phase 3 implements the scope spelling.");
}
