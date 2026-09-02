using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
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

    // ------------------------------------------------ available at all times

    /// <summary>
    /// 🔒 The state that used to refuse. A battle is open, and quitting mid-fight is the single
    /// most likely moment a player wants out — the fight is going badly and the alternative is
    /// watching it lose. Nothing here is paid for the abandoned battle: the run banks only what it
    /// had banked before <c>START_BATTLE</c>.
    /// </summary>
    [Fact]
    public void A_battle_pending_run_can_be_abandoned_mid_fight()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Abandon(opened);

        result.Accepted.ShouldBeTrue(
            "ABANDON_RUN is legal from every state a LIVE run can stand in; a run stuck inside a " +
            "fight it cannot win with no way out is the wedge this exemption exists to prevent.");
        result.NewState.Run!.Phase.ShouldBe(RunPhase.Ended);
    }

    /// <summary>
    /// The battle is closed before the run is, so the persisted row never carries a phase no legal
    /// sequence could have produced. Asserted through the accepted state rather than the handler,
    /// since <see cref="RunPhase.Ended"/> is what the row ends up holding either way — what this
    /// pins is that <c>Run.ExitBattle</c> ran at all, which its own throw makes observable.
    /// </summary>
    [Fact]
    public void Abandoning_mid_fight_closes_the_battle_rather_than_stranding_it()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        var result = Abandon(opened);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.HasPendingTile.ShouldBeFalse(
            "the tile the abandoned fight was standing on is cleared with it.");
    }

    /// <summary>
    /// A draft is open. <c>GameRules.Execute</c>'s draft gate admits only the three draft commands,
    /// and ABANDON_RUN, which is exempted with it.
    /// </summary>
    [Fact]
    public void A_run_with_an_open_draft_can_be_abandoned()
    {
        var drafting = DraftWorlds.DraftPendingOn();

        var result = Abandon(drafting);

        result.Accepted.ShouldBeTrue(
            "a draft the player does not want to answer must not be able to hold the run open.");
        result.NewState.Run!.Phase.ShouldBe(RunPhase.Ended);
        result.NewState.Run.DraftPending.ShouldBeFalse();
    }

    /// <summary>Paused at a junction: <c>CHOOSE_FORK</c> is the only other legal move.</summary>
    [Fact]
    public void A_run_paused_at_a_junction_can_be_abandoned()
    {
        var paused = RunEndWorlds.PausedAtFork();

        var result = Abandon(paused);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.PendingFork.ShouldBeNull(
            "a paused movement is closed with the run, not persisted onto a finished one.");
    }

    /// <summary>Standing on an unresolved tile — the ordinary mid-board state.</summary>
    [Theory]
    [InlineData(TileKind.Enemy)]
    [InlineData(TileKind.Shop)]
    [InlineData(TileKind.Event)]
    [InlineData(TileKind.Campfire)]
    [InlineData(TileKind.Minigame)]
    [InlineData(TileKind.DiceForge)]
    [InlineData(TileKind.Shrine)]
    public void A_run_standing_on_any_unresolved_tile_can_be_abandoned(TileKind kind)
    {
        var result = Abandon(TileWorlds.OnTile(kind));

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Phase.ShouldBe(RunPhase.Ended);
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
