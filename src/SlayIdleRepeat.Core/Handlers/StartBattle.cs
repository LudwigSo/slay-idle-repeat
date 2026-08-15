using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>START_BATTLE</c> handler: opens the fight the run is standing on.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in <c>Core</c> can build the hero's <c>ActorStats</c> yet, so this handler cannot run the
/// actual simulation — the client simulates locally from a server-issued seed instead. This
/// handler's job is narrower: commit the battle seed and record that a battle is open.
/// <see cref="Rng.RunRngScope.BeginBattle"/> derives the seed and advances the combat stream by
/// exactly one; the seed itself is recoverable from the accepted run snapshot without a domain event.
/// </para>
/// <para>
/// A battle can only open against a pending Enemy/Elite/Boss tile. The phase gate that refuses a
/// second START_BATTLE while one is open lives in <c>GameRules.Execute</c>, so this handler does not
/// re-check the phase itself.
/// </para>
/// </remarks>
internal static class StartBattle
{
    /// <summary>Applies <c>START_BATTLE</c>.</summary>
    internal static HandlerResult Handle(StartBattleCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var kind = (TileKind)run.PendingTileKindValue;

        if (kind is not (TileKind.Enemy or TileKind.Elite or TileKind.Boss))
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Derives this battle's seed and advances the combat stream; the seed itself is not read
        // here since deriving it is what commits the draw.
        _ = input.Rng.BeginBattle();

        run.EnterBattle();

        return HandlerResult.Accept();
    }
}
