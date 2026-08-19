using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// END_RUN banks the run's rewards and closes it, for the two terminal states — Victory (Boss dead)
/// and Death (HP 0).
/// </summary>
public sealed class EndRunTests
{
    private static CommandResult End(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new EndRunCommand(), TileWorlds.Context);

    [Fact]
    public void An_unhurt_run_with_the_boss_alive_is_rejected()
    {
        var result = End(RunEndWorlds.InProgress(bossDefeated: false, currentHp: 100));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_battle_pending_run_is_rejected()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = End(opened);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// Chapter 1, NORMAL: Victory bonus = 25 * 1.55^0 * 1.0 * 10 = 250. Banked 100 + 250 = 350,
    /// times CompletionMultiplier.VICTORY (1.0) = 350, exactly.
    /// </summary>
    [Fact]
    public void Victory_banks_the_run_bonus_and_pays_the_full_amount()
    {
        var world = RunEndWorlds.InProgress(bossDefeated: true, bankedLegendXp: 100, bankedSoulShards: 50);
        var before = world.Player.LegendXp;

        var result = End(world);

        result.Accepted.ShouldBeTrue();
        (result.NewState.Player.LegendXp - before).ShouldBe(350);
        result.NewState.Player.BalanceOf(CurrencyId.SOUL_SHARDS).ShouldBe(50);
        result.NewState.Run!.Phase.ShouldBe(RunPhase.Ended);
    }

    [Fact]
    public void A_stage_3_death_pays_sixty_percent()
    {
        var world = RunEndWorlds.InProgress(
            currentHp: 0, bankedLegendXp: 100, hasPendingTile: true, pendingTileStage: 3);
        var before = world.Player.LegendXp;

        var result = End(world);

        (result.NewState.Player.LegendXp - before).ShouldBe(60);
    }

    /// <summary>Stage 1 is the floor: death still pays, never zero.</summary>
    [Fact]
    public void A_stage_1_death_pays_twentyfive_percent_never_zero()
    {
        var world = RunEndWorlds.InProgress(
            currentHp: 0, bankedLegendXp: 100, hasPendingTile: true, pendingTileStage: 1);
        var before = world.Player.LegendXp;

        var result = End(world);

        (result.NewState.Player.LegendXp - before).ShouldBe(25);
    }

    /// <summary>A death fighting the Boss has no stage of its own.</summary>
    [Fact]
    public void A_death_to_the_boss_pays_the_stage_3_rate()
    {
        var world = RunEndWorlds.InProgress(
            currentHp: 0, bankedLegendXp: 100, hasPendingTile: false, pendingTileStage: 0);
        var before = world.Player.LegendXp;

        var result = End(world);

        (result.NewState.Player.LegendXp - before).ShouldBe(60);
    }

    [Fact]
    public void Ending_an_already_ended_run_is_rejected()
    {
        var ended = End(RunEndWorlds.InProgress(bossDefeated: true)).NewState;

        var result = End(ended);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.RUN_ALREADY_ENDED);
    }
}
