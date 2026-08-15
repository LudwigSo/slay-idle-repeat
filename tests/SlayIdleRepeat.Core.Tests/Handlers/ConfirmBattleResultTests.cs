using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>CONFIRM_BATTLE_RESULT, driven through the production dispatch table by GameRules.Apply.</summary>
public sealed class ConfirmBattleResultTests
{
    private static CommandResult Confirm(WorldSlice state, string logHash) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new ConfirmBattleResultCommand(logHash, Won: true), TileWorlds.Context);

    // ------------------------------------------------------------------ the gate

    /// <summary>No battle is open on a run standing on no tile at all.</summary>
    [Fact]
    public void A_run_with_no_battle_open_is_rejected()
    {
        var result = Confirm(TileWorlds.OnNoTile(), "123");

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A run standing on a pending fight tile that never opened a battle is still not open.</summary>
    [Fact]
    public void A_pending_fight_tile_with_no_open_battle_is_rejected()
    {
        var result = Confirm(TileWorlds.OnTile(TileKind.Enemy), "123");

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ LogHash format

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("0x1A")]
    public void A_malformed_LogHash_is_rejected(string logHash)
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, logHash);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("18446744073709551615")] // ulong.MaxValue
    [InlineData("123456789")]
    public void A_well_formed_LogHash_is_accepted(string logHash)
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, logHash);

        result.Accepted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ closing the fight

    /// <summary>Closing a battle moves the phase back to InProgress.</summary>
    [Fact]
    public void Closing_a_battle_returns_the_phase_to_InProgress()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
    }

    /// <summary>…and clears the pending fight tile.</summary>
    [Fact]
    public void Closing_a_battle_clears_the_pending_tile()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.HasPendingTile.ShouldBeFalse();
    }

    /// <summary>Closing a battle marks a draft as pending.</summary>
    [Fact]
    public void Closing_a_battle_marks_a_draft_pending()
    {
        var opened = TileWorlds.OnTile(TileKind.Elite, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.ToSnapshot().DraftPending.ShouldBeTrue();
    }

    /// <summary>
    /// A WON battle pays Gold immediately and banks Legend XP; HP is untouched.
    /// Chapter 1, NORMAL tier, a normal Enemy kill: Gold = 40 * G(1) = 40; Legend XP =
    /// 25 * 1.55^0 * 1.0 (NORMAL tier) * 1 (NORMAL_ENEMY_KILL) = 25.
    /// </summary>
    [Fact]
    public void Winning_a_battle_pays_gold_and_banks_legend_xp()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, gold: 250, currentHp: 60, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.Gold.ShouldBe(290);
        result.NewState.Run!.CurrentHp.ShouldBe(60);
        result.NewState.Run!.BankedLegendXp.ShouldBe(25);
        result.NewState.Run!.BankedSoulShards.ShouldBe(0);
        result.Events.ShouldNotBeEmpty();
    }

    /// <summary>A LOST battle sets HP to zero, pays nothing, and leaves the tile pending for a revive.</summary>
    [Fact]
    public void Losing_a_battle_sets_HP_to_zero_and_pays_nothing()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, gold: 250, currentHp: 60, phase: RunPhase.BattlePending);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            opened, new ConfirmBattleResultCommand("1", Won: false), TileWorlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(250);
        result.NewState.Run!.CurrentHp.ShouldBe(0);
        result.NewState.Run!.BankedLegendXp.ShouldBe(0);
        result.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
        result.NewState.Run!.HasPendingTile.ShouldBeTrue("a loss leaves the fight pending for REVIVE");
    }

    /// <summary>
    /// A Boss kill's banked Soul Shards = per-chapter Boss rate (15 at chapter 1 NORMAL) plus the
    /// 450 first-clear grant, the first time this (Chapter, Tier) is cleared.
    /// </summary>
    [Fact]
    public void Killing_the_boss_marks_it_defeated_and_banks_soul_shards()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.BossDefeated.ShouldBeTrue();
        result.NewState.Run!.BankedSoulShards.ShouldBe(15 + 450);
    }

    /// <summary>
    /// The first-clear grant is one-time: a player who has already cleared this (Chapter, Tier) pair
    /// banks only the per-kill Boss Soul Shards, not the 450 again.
    /// </summary>
    [Fact]
    public void First_clear_bonus_does_not_repeat_for_an_already_cleared_chapter_tier()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        // Rehydrate a player who has already cleared chapter 1 NORMAL.
        var clearedPlayerSnapshot = SlayIdleRepeat.Core.Tests.Model.PlayerSnapshots.With(
            clearedChapterTiers: SlayIdleRepeat.Core.Tests.Model.PlayerSnapshots.Counters(("1:NORMAL", 1)));
        var clearedPlayer = SlayIdleRepeat.Core.Model.Player.Rehydrate(
            clearedPlayerSnapshot, TileWorlds.Context.Content).Value;
        var world = opened with { Player = clearedPlayer };

        var result = Confirm(world, "1");

        result.NewState.Run!.BankedSoulShards.ShouldBe(15);
    }

    /// <summary>A second CONFIRM_BATTLE_RESULT after the first closed it is refused — nothing is open.</summary>
    [Fact]
    public void Confirming_twice_is_rejected()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var closed = Confirm(opened, "1").NewState;

        Confirm(closed, "2").Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ the round trip

    /// <summary>The full loop: RESOLVE_TILE acknowledges a fight tile, START_BATTLE opens it, CONFIRM_BATTLE_RESULT closes it.</summary>
    [Fact]
    public void A_fight_resolves_across_three_commands()
    {
        var arrived = TileWorlds.OnTile(TileKind.Enemy);

        var acknowledged = SlayIdleRepeat.Core.GameRules.Apply(
            arrived, new ResolveTileCommand(), TileWorlds.Context);
        acknowledged.Accepted.ShouldBeTrue();
        acknowledged.NewState.Run!.HasPendingTile.ShouldBeTrue("RESOLVE_TILE must not consume a fight tile");

        var opened = SlayIdleRepeat.Core.GameRules.Apply(
            acknowledged.NewState, new StartBattleCommand(), TileWorlds.Context);
        opened.Accepted.ShouldBeTrue();
        opened.NewState.Run!.Phase.ShouldBe(RunPhase.BattlePending);

        var closed = Confirm(opened.NewState, "1");
        closed.Accepted.ShouldBeTrue();
        closed.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
        closed.NewState.Run!.HasPendingTile.ShouldBeFalse();
    }
}
