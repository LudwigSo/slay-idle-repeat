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

    // ------------------------------------------------------------- the fixture's own premise

    /// <summary>
    /// 🔒 <b>The fixture hero WINS every fight this suite asserts a payout for, and it is asserted
    /// rather than assumed.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>Every payout case below is conditional on this, and none of them would fail if it stopped
    /// being true.</b> Since <c>CONFIRM_BATTLE_RESULT</c> recomputes the fight (<c>14</c> §9), the server
    /// decides whether a battle was won — so a fixture whose hero loses does not break the payout tests,
    /// it makes them assert that a loss pays nothing. Twenty green tests, none of them testing the arm
    /// M7-06c changed. This case turns that silence into one loud failure.
    /// </para>
    /// <para>
    /// 🔒 <b>All three tile kinds, because only one of them discriminates and it is not the one you
    /// would guess.</b> The first draft asserted the Enemy fight alone — and PASSED with the loadout
    /// stripped to bare, because a Legend-20 hero beats a chapter-1 ordinary enemy with no gear at all.
    /// It was a cannot-fail pin guarding against cannot-fail pins. ⚠️ <b>Re-probed after widening, and
    /// measured:</b> swapping <c>WornLoadout</c> for <c>BareLoadout</c> fails the <b>Elite</b> arm and
    /// leaves Enemy and Boss green. So Elite is the arm carrying this case today. All three are kept
    /// anyway — which arm discriminates is a fact about current tuning, and M6 will move it; a probe
    /// narrowed to today's discriminator would go quiet the moment that changed.
    /// </para>
    /// <para>
    /// ⚠️ <b>The drift this exists to catch is real rather than hypothetical.</b> "Geared enough to win"
    /// is measured against <c>ChapterPowerTarget</c>, which M6 exists to retune. This suite fought
    /// bare-handed until M7-06c, and the sweep establishing that levelling could not fix it (Legend Level
    /// 1 → 20 → 60 → 120 moved 39 whole-suite failures to 34) is recorded in <c>TileWorlds</c>.
    /// </para>
    /// <para>
    /// Asserted through the HANDLER rather than by calling the simulation directly: what matters is not
    /// that some fight is winnable but that the fight <em>this fixture</em> hands the handler is. Gold is
    /// the observable rather than the phase, because the phase returns to <c>InProgress</c> on a loss too.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(TileKind.Enemy)]
    [InlineData(TileKind.Elite)]
    [InlineData(TileKind.Boss)]
    public void The_fixture_hero_wins_every_fight_this_suite_pays_out_for(TileKind kind)
    {
        var opened = TileWorlds.OnTile(kind, gold: 0, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.Accepted.ShouldBeTrue("a well-formed confirmation is accepted whatever the outcome was");
        result.NewState.Run!.Gold.ShouldBeGreaterThan(
            0L,
            $"the fixture hero LOST its {kind} fight, so every payout case in this file that uses it is " +
            "now asserting that a loss pays nothing — they will all stay green while testing none of " +
            "the win arm. Either TileWorlds' fixture loadout has fallen behind ChapterPowerTarget (M6 " +
            "retunes it) or the run no longer freezes that loadout. Fix the fixture; do not relax this.");
    }

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
