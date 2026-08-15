using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 M3-13, `14` §2.3, `02` §6 — <c>REVIVE</c>: once per run, restores 50% Max HP and re-opens the
/// fight that killed the hero.
/// </summary>
public sealed class ReviveTests
{
    private static CommandResult Revive(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new ReviveCommand(), TileWorlds.Context);

    /// <summary>🔒 A live hero has nothing to revive from.</summary>
    [Fact]
    public void A_hero_who_is_not_dead_cannot_revive()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 100, hasPendingTile: true));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>🔒 A dead hero with no pending fight has nothing for REVIVE to restart.</summary>
    [Fact]
    public void A_dead_hero_with_no_pending_fight_cannot_revive()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 0, hasPendingTile: false));

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>🔒 Heals to 50% of Max HP (100 -> 50) and re-opens the same pending fight.</summary>
    [Fact]
    public void A_first_revive_heals_to_half_max_hp_and_reopens_the_battle()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 0, maxHp: 100, hasPendingTile: true));

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(50);
        result.NewState.Run!.Phase.ShouldBe(RunPhase.BattlePending);
        result.NewState.Run!.AdUseCount("AD_REVIVE").ShouldBe(1);
    }

    /// <summary>🔒 A second revive in the same run is refused — once per run, hard (02 §6).</summary>
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

    /// <summary>🔒 Negative control: a revive never restores full HP — only the authored 50% share.</summary>
    [Fact]
    public void A_revive_does_not_restore_full_hp()
    {
        var result = Revive(RunEndWorlds.InProgress(currentHp: 0, maxHp: 100, hasPendingTile: true));

        result.NewState.Run!.CurrentHp.ShouldNotBe(100);
        result.NewState.Run!.CurrentHp.ShouldBe(50);
    }
}
