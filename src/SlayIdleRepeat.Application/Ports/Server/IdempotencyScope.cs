using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>Which of the two sequencing domains a scope names.</summary>
public enum IdempotencyScopeKind
{
    /// <summary>One run's domain: sequence monotone per run from 1, command ids scoped to the run.</summary>
    Run = 0,

    /// <summary>One player's lifetime domain: sequence monotone per player, command ids scoped to the player.</summary>
    Player = 1,
}

/// <summary>One sequencing-and-idempotency domain: a player's lifetime counter, or one of their runs.</summary>
/// <remarks>
/// <para>
/// The widening the <c>23</c> §4.2 sketch could not express: a command id alone cannot key a record
/// when the same id is legal in two domains with two different lifetimes. Every read and write on
/// <see cref="IIdempotencyStore"/> names the domain first.
/// </para>
/// <para>
/// Structured rather than a composite string so a store can reach the row the counter lives on —
/// the player's row for the lifetime counter, the run's row for the run counter — without parsing
/// anything.
/// </para>
/// </remarks>
public readonly record struct IdempotencyScope
{
    private IdempotencyScope(IdempotencyScopeKind kind, PlayerId player, RunId? run)
    {
        Kind = kind;
        Player = player;
        Run = run;
    }

    /// <summary>Which domain this is.</summary>
    public IdempotencyScopeKind Kind { get; }

    /// <summary>The player whose command this domain sequences. Every scope names one.</summary>
    public PlayerId Player { get; }

    /// <summary>The run, on a <see cref="IdempotencyScopeKind.Run"/> scope; <c>null</c> on a player scope.</summary>
    public RunId? Run { get; }

    /// <summary>The player's lifetime domain.</summary>
    /// <param name="player">The player.</param>
    public static IdempotencyScope ForPlayer(PlayerId player) =>
        new(IdempotencyScopeKind.Player, Require(player), run: null);

    /// <summary>One run's domain.</summary>
    /// <param name="player">The run's owner.</param>
    /// <param name="run">The run.</param>
    public static IdempotencyScope ForRun(PlayerId player, RunId run) =>
        run.Value is null or ""
            ? throw new ArgumentException(
                "A run scope names a run, and this run id carries no text. A scope over a blank run " +
                "would pool every such run's records into one domain.",
                nameof(run))
            : new(IdempotencyScopeKind.Run, Require(player), run);

    private static PlayerId Require(PlayerId player) =>
        player.Value is null or ""
            ? throw new ArgumentException(
                "Every scope names a player, and this player id carries no text. A scope over a blank " +
                "player would pool every such player's records into one domain.",
                nameof(player))
            : player;
}
