using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core;

/// <summary>The aggregates one command may read or write. <c>Apply</c> never receives "the world", only these.</summary>
/// <remarks>
/// <para>
/// Lives in the <c>SlayIdleRepeat.Core</c> root rather than under <c>Core/Model/</c>: it's not an
/// aggregate, it's <c>Apply</c>'s parameter, and the Application layer must be able to construct one
/// on every command — a constraint that conflicts with the architecture rule forbidding a public
/// constructor on any public <c>Core/Model/</c> type.
/// </para>
/// <para>
/// A pair of references, not a copy: constructing one does not clone the aggregates.
/// <c>GameRules.Apply</c> is what clones, on the way in.
/// </para>
/// </remarks>
/// <param name="Player">The player the command is applied to. Never null: Run is modelled as a child of Player.</param>
/// <param name="Run">
/// The run in flight, or <c>null</c> outside a run. A <c>null</c> here for a run command is a
/// loading defect, not a rejection.
/// </param>
public sealed record WorldSlice(Player Player, Run? Run)
{
    private readonly Player _player = RequirePlayer(Player);

    private readonly Run? _run = RequireOwnedRun(Player, Run);

    /// <inheritdoc cref="WorldSlice(Player, Run)" path="/param[@name='Player']"/>
    public Player Player
    {
        get => _player;
        init => _player = RequirePlayer(value);
    }

    /// <inheritdoc cref="WorldSlice(Player, Run)" path="/param[@name='Run']"/>
    public Run? Run
    {
        get => _run;

        // Reads the field, not the Player property: on the `with` path, the copy constructor copies
        // fields before calling init setters, so the parameter isn't yet the slice's player.
        init => _run = RequireOwnedRun(_player, value);
    }

    /// <summary>The guard behind <see cref="Player"/>, in the <c>init</c> accessor so it also runs on the <c>with</c> path.</summary>
    private static Player RequirePlayer(Player player) =>
        player ?? throw new ArgumentNullException(
            nameof(Player),
            "A WorldSlice always names a player. 30 §4 models Run as a CHILD of Player — every run " +
            "mutation also touches player state (rewards, XP, pity) — so there is no command in the " +
            "game that touches a run and no player. A null here is the Application layer loading the " +
            "wrong slice (30 §4.1), not a state the domain can be in.");

    /// <summary>Enforces that the run in a slice belongs to the player in that slice.</summary>
    /// <remarks>
    /// A cross-player run is not a state the domain can be in, so it throws rather than rejecting —
    /// a caller that built this slice is miswired, not a player being refused.
    /// </remarks>
    private static Run? RequireOwnedRun(Player player, Run? run) =>
        run is null || run.PlayerId == player.Id
            ? run
            : throw new ArgumentException(
                "The run in this slice belongs to " + run.PlayerId + ", not to " + player.Id + ". " +
                "30 §4 models Run as a CHILD of Player — single writer, owned by exactly one player " +
                "— so a slice pairing one player's run with another player is the Application layer " +
                "loading the wrong slice (30 §4.1). Apply would otherwise move Gold, write HP, fold " +
                "RNG stream positions and stamp the run, and return the result as if it belonged to " +
                "the player named here.",
                nameof(Run));
}
