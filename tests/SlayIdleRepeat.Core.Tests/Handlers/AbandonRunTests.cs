using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>ABANDON_RUN closes the run at the 0.10 ABANDON multiplier, regardless of HP or Boss state.</summary>
public sealed class AbandonRunTests
{
    private static CommandResult Abandon(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new AbandonRunCommand(), TileWorlds.Context);

    /// <summary>Banked 100 Legend XP * ABANDON (0.10) = 10, exactly.</summary>
    [Fact]
    public void An_unhurt_run_with_the_boss_alive_can_still_abandon_at_ten_percent()
    {
        var world = RunEndWorlds.InProgress(bossDefeated: false, currentHp: 100, bankedLegendXp: 100);
        var before = world.Player.LegendXp;

        var result = Abandon(world);

        result.Accepted.ShouldBeTrue();
        (result.NewState.Player.LegendXp - before).ShouldBe(10);
        result.NewState.Run!.Phase.ShouldBe(RunPhase.Ended);
    }

    /// <summary>Abandoning pays the same 0.10 whether or not the Boss is already dead — no Victory bonus is banked.</summary>
    [Fact]
    public void Abandoning_a_victorious_run_still_pays_only_ten_percent()
    {
        var world = RunEndWorlds.InProgress(bossDefeated: true, bankedLegendXp: 100);
        var before = world.Player.LegendXp;

        var result = Abandon(world);

        // Negative control: proves AbandonRun does not fall through to EndRun's Victory branch.
        (result.NewState.Player.LegendXp - before).ShouldBe(10);
    }

    [Fact]
    public void A_dead_run_can_be_abandoned_too()
    {
        var world = RunEndWorlds.InProgress(currentHp: 0, bankedSoulShards: 100);

        var result = Abandon(world);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.SOUL_SHARDS).ShouldBe(10);
    }

    [Fact]
    public void A_battle_pending_run_is_rejected()
    {
        var opened = TileWorlds.OnTile(SlayIdleRepeat.Core.Rules.Board.TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Abandon(opened);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void Abandoning_an_already_ended_run_is_rejected()
    {
        var ended = Abandon(RunEndWorlds.InProgress()).NewState;

        var result = Abandon(ended);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.RUN_ALREADY_ENDED);
    }
}
