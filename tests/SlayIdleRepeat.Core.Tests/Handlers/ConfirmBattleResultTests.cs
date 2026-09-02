using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;
using SlayIdleRepeat.Core.Tests.Rules.Combat;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>CONFIRM_BATTLE_RESULT, driven through the production dispatch table by GameRules.Apply.</summary>
public sealed class ConfirmBattleResultTests
{
    private static CommandResult Confirm(WorldSlice state, string logHash) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new ConfirmBattleResultCommand(logHash, Won: true), TileWorlds.Context);

    /// <summary>The fixture hero wins every fight this suite asserts a payout for.</summary>
    /// <remarks>
    /// The server recomputes the fight (14 §9), so a fixture loadout falling below ChapterPowerTarget
    /// silently turns the payout cases into loss cases. All three kinds are probed because which arm
    /// discriminates is a fact about current tuning (today: Elite). Gold is the observable rather than
    /// the phase, because the phase returns to InProgress on a loss too.
    /// </remarks>
    [Theory]
    [InlineData(TileKind.Enemy)]
    [InlineData(TileKind.Elite)]
    [InlineData(TileKind.Boss)]
    [InlineData(TileKind.MiniBoss)]
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

    [Fact]
    public void A_run_with_no_battle_open_is_rejected()
    {
        var result = Confirm(TileWorlds.OnNoTile(), "123");

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_pending_fight_tile_with_no_open_battle_is_rejected()
    {
        var result = Confirm(TileWorlds.OnTile(TileKind.Enemy), "123");

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

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

    [Fact]
    public void Closing_a_battle_returns_the_phase_to_InProgress()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.Phase.ShouldBe(RunPhase.InProgress);
    }

    [Fact]
    public void Closing_a_battle_clears_the_pending_tile()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.HasPendingTile.ShouldBeFalse();
    }

    [Fact]
    public void Closing_a_battle_marks_a_draft_pending()
    {
        var opened = TileWorlds.OnTile(TileKind.Elite, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.ToSnapshot().DraftPending.ShouldBeTrue();
    }

    /// <summary>
    /// The boss is the exception, and the only one: it is the last fight of the run, so there is no
    /// run left to spend a perk in. A draft left pending here also gates every other run command,
    /// so the run would end behind an offer nobody wants.
    /// </summary>
    [Fact]
    public void Killing_the_boss_opens_no_draft()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.BossDefeated.ShouldBeTrue("the premise: the boss actually died.");
        result.NewState.Run.ToSnapshot().DraftPending.ShouldBeFalse();
    }

    /// <summary>
    /// A mini-boss pays an elite's rewards exactly — no new payout numbers. Stated against the
    /// elite arm rather than against literals, so an M6 retune of the elite row moves both together
    /// instead of turning this into a transcription of today's tuning.
    /// </summary>
    [Fact]
    public void A_miniboss_win_pays_exactly_what_an_elite_win_pays()
    {
        var miniBoss = Confirm(
            TileWorlds.OnTile(TileKind.MiniBoss, gold: 0, phase: RunPhase.BattlePending), "1");
        var elite = Confirm(
            TileWorlds.OnTile(TileKind.Elite, gold: 0, phase: RunPhase.BattlePending), "1");

        elite.NewState.Run!.Gold.ShouldBeGreaterThan(
            0L, "the elite arm is the reference; a reference that paid nothing compares nothing.");
        elite.NewState.Run.BankedLegendXp.ShouldBeGreaterThan(
            0L, "same reason: two zeroes agree about nothing.");
        GearGrants(elite).ShouldBeGreaterThan(
            0, "and a reference that granted no gear would compare no gear.");

        miniBoss.NewState.Run!.Gold.ShouldBe(
            elite.NewState.Run.Gold, "elite Gold-per-kill, scaled by the same run modifiers.");
        miniBoss.NewState.Run.BankedLegendXp.ShouldBe(
            elite.NewState.Run.BankedLegendXp, "the elite Legend XP source, not the normal one.");
        miniBoss.NewState.Run.BankedSoulShards.ShouldBe(
            0L, "Soul Shards are the boss kill's alone.");
        GearGrants(miniBoss).ShouldBe(
            GearGrants(elite), "the elite guaranteed gear drop, at the elite count.");
    }

    private static int GearGrants(CommandResult result) =>
        result.Events.Count(domainEvent => domainEvent is SlayIdleRepeat.Core.Events.GearGranted);

    /// <summary>
    /// Chapter 1, NORMAL tier, a normal Enemy kill: Gold = 40 * G(1) = 40; Legend XP =
    /// 25 * 1.55^0 * 1.0 (NORMAL tier) * 1 (NORMAL_ENEMY_KILL) = 25.
    /// </summary>
    [Fact]
    public void Winning_a_battle_pays_gold_and_banks_legend_xp()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, gold: 250, currentHp: 60, phase: RunPhase.BattlePending);

        var result = Confirm(opened, "1");

        result.NewState.Run!.Gold.ShouldBe(290);
        // At the ceiling rather than somewhere interesting because the over-par fixture hero ends the
        // fight far above the run's Max HP of 100; a wounded-survivor case needs `05` §9's near-par harness.
        result.NewState.Run.CurrentHp.ShouldBe(
            result.NewState.Run.MaxHp,
            "a won fight writes its own ending HP, clamped into the run's range.");
        result.NewState.Run.BankedLegendXp.ShouldBe(25);
        result.NewState.Run.BankedSoulShards.ShouldBe(0);
        result.Events.ShouldNotBeEmpty();
    }

    [Fact]
    public void Losing_a_battle_sets_HP_to_zero_and_pays_nothing()
    {
        // An Elite fought bare-handed: Won: false is only the client's claim and the recomputation
        // overrules it, so a losing case must hand over a fight the hero genuinely loses — and a
        // Legend-20 hero beats a chapter-1 ordinary Enemy with no gear at all.
        var opened = TileWorlds.OnTile(
            TileKind.Elite, gold: 250, currentHp: 60, phase: RunPhase.BattlePending, geared: false);

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            opened, new ConfirmBattleResultCommand("1", Won: false), TileWorlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(250);
        result.NewState.Run.CurrentHp.ShouldBe(0);
        result.NewState.Run.BankedLegendXp.ShouldBe(0);
        result.NewState.Run.Phase.ShouldBe(RunPhase.InProgress);
        result.NewState.Run.HasPendingTile.ShouldBeTrue("a loss leaves the fight pending for REVIVE");
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
        result.NewState.Run.BankedSoulShards.ShouldBe(15 + 450);
    }

    [Fact]
    public void First_clear_bonus_does_not_repeat_for_an_already_cleared_chapter_tier()
    {
        var opened = TileWorlds.OnTile(TileKind.Boss, phase: RunPhase.BattlePending);

        // The already-cleared player is built on the geared row: a bare row would lose the recomputed
        // boss fight, making this pass or fail on whether a loss banks Soul Shards instead.
        var clearedPlayerSnapshot = RunBattleWorlds.FarAboveParRow(
            SlayIdleRepeat.Core.Tests.Model.PlayerSnapshots.Counters(("1:NORMAL", 1)));
        var clearedPlayer = SlayIdleRepeat.Core.Model.Player.Rehydrate(
            clearedPlayerSnapshot, TileWorlds.Context.Content).Value;
        var world = opened with { Player = clearedPlayer };

        var result = Confirm(world, "1");

        result.NewState.Run!.BankedSoulShards.ShouldBe(15);
    }

    [Fact]
    public void Confirming_twice_is_rejected()
    {
        var opened = TileWorlds.OnTile(TileKind.Enemy, phase: RunPhase.BattlePending);

        var closed = Confirm(opened, "1").NewState;

        Confirm(closed, "2").Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

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
        closed.NewState.Run.HasPendingTile.ShouldBeFalse();
    }
}
