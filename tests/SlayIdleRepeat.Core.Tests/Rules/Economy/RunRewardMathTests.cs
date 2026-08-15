using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// 🔒 M3-13, `02` §5 / `03` §7a.1 — the pure reward-banking and run-end payout formulas, pinned to
/// exact hand-computed values (not just "greater than before").
/// </summary>
public sealed class RunRewardMathTests
{
    private static readonly ContentSnapshot Content = InRunIncomeDocuments.Shipped;

    // ------------------------------------------------------------------ ForKill: Gold + Legend XP

    /// <summary>Chapter 1 NORMAL: Gold = 40 * G(1) = 40; Legend XP = 25 * 1.55^0 * 1.0 * 1 = 25.</summary>
    [Fact]
    public void A_normal_kill_at_chapter_1_normal_tier_pays_forty_gold_and_twentyfive_xp()
    {
        var reward = RunRewardMath.ForKill(TileKind.Enemy, 1, DifficultyTier.NORMAL, Content);

        reward.Gold.ShouldBe(40);
        reward.LegendXp.ShouldBe(25);
        reward.SoulShards.ShouldBe(0);
    }

    /// <summary>Elite: Gold = 40*3 = 120; Legend XP = 25 * 3 = 75 (03 §7a.1 / 02 §5.1a).</summary>
    [Fact]
    public void An_elite_kill_pays_triple_gold_and_triple_xp()
    {
        var reward = RunRewardMath.ForKill(TileKind.Elite, 1, DifficultyTier.NORMAL, Content);

        reward.Gold.ShouldBe(120);
        reward.LegendXp.ShouldBe(75);
    }

    /// <summary>Boss: Gold = 40*10 = 400; Legend XP = 25*15 = 375; Soul Shards = 15*chapter(1) = 15.</summary>
    [Fact]
    public void A_boss_kill_pays_ten_times_gold_fifteen_times_xp_and_soul_shards()
    {
        var reward = RunRewardMath.ForKill(TileKind.Boss, 1, DifficultyTier.NORMAL, Content);

        reward.Gold.ShouldBe(400);
        reward.LegendXp.ShouldBe(375);
        reward.SoulShards.ShouldBe(15);
    }

    /// <summary>🔒 Chapter 2 NORMAL: Gold = 40 * 1.55 = 62 (rounded); Legend XP = 25 * 1.55 = 38.75 -> 39.</summary>
    [Fact]
    public void Chapter_2_scales_gold_and_xp_by_the_growth_curve()
    {
        var reward = RunRewardMath.ForKill(TileKind.Enemy, 2, DifficultyTier.NORMAL, Content);

        reward.Gold.ShouldBe(62);
        reward.LegendXp.ShouldBe(39);
    }

    /// <summary>
    /// 🔒 HEROIC applies TierXpMult 1.6: Legend XP = 25 * 1.0 (Enemy) * 1.6 = 40. NORMAL-tier tests
    /// alone cannot catch a dropped tier multiplier, because NORMAL's is 1.0 — this is the probe
    /// that can.
    /// </summary>
    [Fact]
    public void Heroic_tier_applies_its_own_multiplier_to_legend_xp()
    {
        var reward = RunRewardMath.ForKill(TileKind.Enemy, 1, DifficultyTier.HEROIC, Content);

        reward.LegendXp.ShouldBe(40);
    }

    /// <summary>🔒 MYTHIC applies TierXpMult 2.5: Legend XP = 25 * 1.0 (Enemy) * 2.5 = 63 (rounded).</summary>
    [Fact]
    public void Mythic_tier_applies_its_own_multiplier_to_legend_xp()
    {
        var reward = RunRewardMath.ForKill(TileKind.Enemy, 1, DifficultyTier.MYTHIC, Content);

        reward.LegendXp.ShouldBe(63);
    }

    /// <summary>🔒 Negative control: a non-combat tile kind has no kill reward to compute.</summary>
    [Fact]
    public void A_non_kill_tile_kind_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => RunRewardMath.ForKill(TileKind.Shop, 1, DifficultyTier.NORMAL, Content));
    }

    // ------------------------------------------------------------------ VictoryBonus / FirstClearBonus

    /// <summary>Chapter 1 NORMAL Victory bonus = 25 * 1.55^0 * 1.0 * 10 = 250.</summary>
    [Fact]
    public void The_victory_bonus_at_chapter_1_normal_is_two_hundred_fifty()
    {
        RunRewardMath.VictoryBonus(1, DifficultyTier.NORMAL, Content).ShouldBe(250);
    }

    /// <summary>🔒 The chosen first-clear grant (the midpoint of the shipped 100..800 range).</summary>
    [Fact]
    public void The_first_clear_bonus_is_the_authored_midpoint()
    {
        RunRewardMath.FirstClearBonus(Content).ShouldBe(450);
    }

    // ------------------------------------------------------------------ FinalPayoutFor: CompletionMultiplier

    [Theory]
    [InlineData(RunCompletionOutcome.Victory, 1000)]
    [InlineData(RunCompletionOutcome.Stage3Death, 600)]
    [InlineData(RunCompletionOutcome.Stage2Death, 400)]
    [InlineData(RunCompletionOutcome.Stage1Death, 250)]
    [InlineData(RunCompletionOutcome.Abandon, 100)]
    internal void FinalPayout_applies_the_exact_completion_multiplier(
        RunCompletionOutcome outcome, long expectedXp)
    {
        RunRewardMath.FinalPayoutFor(1000, 0, outcome, false, Content).LegendXp.ShouldBe(expectedXp);
    }

    /// <summary>🔒 Negative control: death still pays — even the harshest death row is never zero.</summary>
    [Fact]
    public void Even_a_stage_1_death_pays_something_never_zero()
    {
        var payout = RunRewardMath.FinalPayoutFor(10, 0, RunCompletionOutcome.Stage1Death, watchedAd: false, Content);

        payout.LegendXp.ShouldBeGreaterThan(0);
    }

    /// <summary>🔒 AdDoubleMultiplier: watching the ad doubles the payout (transcribed as 2.0).</summary>
    [Fact]
    public void Watching_the_ad_doubles_the_payout()
    {
        var doubled = RunRewardMath.FinalPayoutFor(
            1000, 100, RunCompletionOutcome.Victory, watchedAd: true, Content);

        doubled.LegendXp.ShouldBe(2000);
        doubled.SoulShards.ShouldBe(200);
    }
}
