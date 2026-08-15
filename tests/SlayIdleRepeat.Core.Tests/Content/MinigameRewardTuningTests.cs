using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// <c>MinigameRewardTuning</c>: the tier counts, the chapter-scaling formula, and the refusals over
/// <c>tuning/currencies.json</c>'s <c>minigameRewards</c> block.
/// </summary>
public sealed class MinigameRewardTuningTests
{
    private static readonly MinigameRewardTuning Tuning = MinigameRewardTuning.Read(TuningDocuments.Shipped);

    // ------------------------------------------------------------------ tier counts

    /// <summary>The four minigames genuinely have different tier counts; this is not one shape times four.</summary>
    [Theory]
    [InlineData(MinigameCatalogue.ChestPick, 3)]
    [InlineData(MinigameCatalogue.TimingBar, 4)]
    [InlineData(MinigameCatalogue.DiceDuel, 3)]
    [InlineData(MinigameCatalogue.MemoryRune, 3)]
    public void Each_minigames_tier_count_matches_03_section_6_1s_table(string minigameId, int expectedTierCount)
    {
        Tuning.TierCount(minigameId).ShouldBe(expectedTierCount);
    }

    /// <summary>An id outside the four is refused, naming the id.</summary>
    [Fact]
    public void An_unknown_minigame_id_is_refused()
    {
        Should.Throw<ArgumentException>(() => Tuning.TierCount("MG_NOT_REAL"))
            .Message.ShouldContain("MG_NOT_REAL", Case.Sensitive);
    }

    // ------------------------------------------------------------------ chapter-1 base rewards

    /// <summary>Chapter 1's scalar is 1, so the base row comes back untouched.</summary>
    [Fact]
    public void Chapter_1_MG_CHEST_PICK_gold_tier_pays_the_authored_base_row()
    {
        var reward = Tuning.RewardFor(MinigameCatalogue.ChestPick, tier: 2, chapterId: 1);

        reward.Gold.ShouldBe(500L);
        reward.Crowns.ShouldBe(60L);
        reward.BeastFeed.ShouldBe(15L);
        reward.EnhanceStones.ShouldBe(0L);
        reward.RerollCharges.ShouldBe(0L);
    }

    /// <summary>The one row that authors a reroll charge — read, not applied by this type.</summary>
    [Fact]
    public void MG_DICE_DUELs_2_0_row_carries_one_reroll_charge()
    {
        Tuning.RewardFor(MinigameCatalogue.DiceDuel, tier: 2, chapterId: 1).RerollCharges.ShouldBe(1L);
    }

    // ------------------------------------------------------------------ the chapter-scaling formula

    /// <summary><c>1 + adBundleScalar × (chapter − 1)</c>, applied to every column.</summary>
    [Theory]
    [InlineData(1, 400L, 30L)]  // scalar 1.0
    [InlineData(2, 540L, 41L)]  // scalar 1.35: 400*1.35=540, 30*1.35=40.5 -> 41 (away from zero)
    [InlineData(5, 960L, 72L)]  // scalar 2.4: 400*2.4=960, 30*2.4=72
    public void The_scaling_formula_applies_to_every_currency_column(int chapterId, long expectedGold, long expectedCrowns)
    {
        // MG_TIMING_BAR tier 2 ("2 hits"): 400 Gold + 30 Crowns.
        var reward = Tuning.RewardFor(MinigameCatalogue.TimingBar, tier: 2, chapterId: chapterId);

        reward.Gold.ShouldBe(expectedGold);
        reward.Crowns.ShouldBe(expectedCrowns);
    }

    /// <summary>A zero column stays zero at every chapter — scaling never manufactures a reward.</summary>
    [Fact]
    public void A_zero_column_stays_zero_at_every_chapter()
    {
        Tuning.RewardFor(MinigameCatalogue.TimingBar, tier: 0, chapterId: 8).Crowns.ShouldBe(0L);
    }

    // ------------------------------------------------------------------ refusals

    /// <summary>A tier past the row count is refused, isolated from the chapter guard below.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_tier_outside_MG_CHEST_PICKs_three_rows_is_refused(int tier)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Tuning.RewardFor(MinigameCatalogue.ChestPick, tier, chapterId: 1));
    }

    /// <summary>…and the negative control: every one of MG_CHEST_PICK's three tiers IS valid.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_tier_of_MG_CHEST_PICKs_three_rows_is_accepted(int tier)
    {
        Should.NotThrow(() => Tuning.RewardFor(MinigameCatalogue.ChestPick, tier, chapterId: 1));
    }

    /// <summary>A chapter below 1 is refused — the formula's own floor.</summary>
    [Fact]
    public void A_chapter_below_one_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => Tuning.RewardFor(MinigameCatalogue.ChestPick, tier: 0, chapterId: 0));
    }
}
