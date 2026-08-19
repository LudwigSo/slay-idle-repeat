using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>REVIVE: once per run, restores 50% Max HP and re-opens the fight that killed the hero.</summary>
public sealed class ReviveTests
{
    private static CommandResult Revive(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new ReviveCommand(), TileWorlds.Context);

    [Fact]
    public void A_hero_who_is_not_dead_cannot_revive()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 100, hasPendingTile: true));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_dead_hero_with_no_pending_fight_cannot_revive()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 0, hasPendingTile: false));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_first_revive_heals_to_half_max_hp_and_reopens_the_battle()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 0, maxHp: 100, hasPendingTile: true));

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(50);
        result.NewState.Run!.Phase.ShouldBe(RunPhase.BattlePending);
        result.NewState.Run!.AdUseCount("AD_REVIVE").ShouldBe(1);
    }

    [Fact]
    public void A_second_revive_in_the_same_run_is_refused()
    {
        var alreadyUsed = RunEndWorlds.InProgress(
            currentHp: 0, hasPendingTile: true,
            adUses: RunSnapshots.AdUses(("AD_REVIVE", 1)));

        var result = Revive(alreadyUsed);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.CAP_REACHED);
    }
}
