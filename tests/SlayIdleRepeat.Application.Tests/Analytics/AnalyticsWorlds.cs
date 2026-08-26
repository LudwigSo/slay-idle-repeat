using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Application.Tests.Analytics;

/// <summary>
/// Real accepted commands packaged as the batches the analytics seam receives — every one produced
/// by <c>GameRules.Apply</c>, never assembled from invented events.
/// </summary>
/// <remarks>
/// The two terminal-state fixtures stand a real run at Victory by editing its snapshot the way the
/// domain's own handler suites do: the interesting precondition (a dead Boss, a held die) is hours
/// of play away, and the command that is the subject still goes through the rules.
/// </remarks>
internal static class AnalyticsWorlds
{
    /// <summary>Sends <paramref name="command"/> through the harness and packages its delivery.</summary>
    internal static DispatchedEvents Sent(InMemoryGame game, PlayerId player, GameCommand command)
    {
        var result = game.Send(player, command);

        return Batch(player, command, result);
    }

    /// <summary>A victory <c>END_RUN</c>'s delivery: the Boss is dead, so the command is accepted.</summary>
    /// <param name="bankedSoulShards">What the run banked, so a case can force a currency payout.</param>
    internal static DispatchedEvents VictoryEndRun(long bankedSoulShards = 0)
    {
        var (game, player) = Worlds.InARun();
        var slice = game.State(player);
        var victory = new WorldSlice(
            slice.Player,
            Rehydrated(slice.Run!.ToSnapshot() with
            {
                BossDefeated = true,
                BankedSoulShards = bankedSoulShards,
            }));

        return Applied(game, player, victory, new EndRunCommand());
    }

    /// <summary>A <c>USE_FIXED_DIE</c> delivery: the run holds one die of <paramref name="pips"/> and spends it.</summary>
    internal static DispatchedEvents FixedDieWalk(int pips)
    {
        var (game, player) = Worlds.InARun();
        var slice = game.State(player);
        var holding = new WorldSlice(
            slice.Player,
            Rehydrated(slice.Run!.ToSnapshot() with
            {
                FixedDice = new Dictionary<int, int> { [pips] = 1 },
            }));

        return Applied(game, player, holding, new UseFixedDieCommand(pips));
    }

    private static DispatchedEvents Applied(
        InMemoryGame game, PlayerId player, WorldSlice slice, GameCommand command)
    {
        var result = GameRules.Apply(slice, command, Worlds.Context(game));

        return Batch(player, command, result);
    }

    private static DispatchedEvents Batch(PlayerId player, GameCommand command, CommandResult result)
    {
        if (!result.Accepted)
        {
            throw new InvalidOperationException(
                command.GetType().Name + " was refused " + result.Rejection + ", so this fixture never " +
                "reached the state the cases built on it assume. Fix the fixture; do not weaken the cases.");
        }

        return new DispatchedEvents(player, command, result.NewState, result.Events);
    }

    private static RunAggregate Rehydrated(RunSnapshot snapshot)
    {
        var run = RunAggregate.Rehydrate(snapshot);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException("The fixture RunSnapshot does not rehydrate: " + run.Error);
    }
}
