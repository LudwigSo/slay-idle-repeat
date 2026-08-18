using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>
/// Turning a stored row back into the aggregate it describes, for a public entry point that was
/// handed the row.
/// </summary>
/// <remarks>
/// <para>
/// The validating factory pair is the only construction path, and it answers with a
/// <c>Result</c> — which is the right shape for a persistence adapter deciding what to do about a
/// corrupt row, and the wrong shape for a projection whose caller handed it the row a moment ago.
/// This turns the failure into an argument fault naming the parameter, so a public projection can
/// take rows without either swallowing a fault or inventing a second result vocabulary for it.
/// </para>
/// <para>
/// ⚠️ Deliberately not a way to <em>obtain</em> an aggregate from outside: it is <c>internal</c>, and
/// what the entry points using it hand back is a read-only projection, never the aggregate itself.
/// </para>
/// </remarks>
internal static class RowDoor
{
    /// <summary>The player a row describes.</summary>
    /// <param name="row">The stored row.</param>
    /// <param name="content">The content set the aggregate validates against.</param>
    /// <param name="parameter">The caller's parameter name, for the fault.</param>
    /// <exception cref="ArgumentException">The row does not rehydrate.</exception>
    internal static PlayerAggregate Player(PlayerSnapshot row, Content.ContentSnapshot content, string parameter)
    {
        var player = PlayerAggregate.Rehydrate(row, content);

        return player.IsSuccess
            ? player.Value
            : throw new ArgumentException(
                "This player row does not rehydrate, so there is no hero to build from it: " +
                player.Error,
                parameter);
    }

    /// <summary>The run a row describes.</summary>
    /// <param name="row">The stored row.</param>
    /// <param name="parameter">The caller's parameter name, for the fault.</param>
    /// <exception cref="ArgumentException">The row does not rehydrate.</exception>
    internal static RunAggregate Run(RunSnapshot row, string parameter)
    {
        var run = RunAggregate.Rehydrate(row);

        return run.IsSuccess
            ? run.Value
            : throw new ArgumentException(
                "This run row does not rehydrate, so there is no run standing in a battle: " + run.Error,
                parameter);
    }
}
