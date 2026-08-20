using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary><c>ALL_COMBAT</c> is a stat <b>selector</b>, not a stat.</summary>
public sealed class StatSelectorTests
{
    [Fact]
    public void ALL_COMBAT_expands_to_the_fourteen_combat_stats_and_no_others()
    {
        StatSelector.AllCombat.Expand().ShouldBe(
        [
            StatId.MAX_HP, StatId.ATK, StatId.DEF, StatId.ASPD, StatId.CRIT, StatId.CDMG,
            StatId.LIFESTEAL, StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DMG_PCT,
            StatId.DR_PCT, StatId.HEAL_PCT, StatId.THORNS,
        ]);

        StatSelector.AllCombat.Expand().ShouldNotContain(StatId.GOLD_PCT);
    }

    [Fact]
    public void A_single_selector_expands_to_that_one_stat()
    {
        StatSelector.Of(StatId.CRIT).Expand().ShouldBe([StatId.CRIT]);
        StatSelector.Of(StatId.CRIT).Stat.ShouldBe(StatId.CRIT);
        StatSelector.Of(StatId.CRIT).Kind.ShouldBe(StatSelectorKind.SINGLE);
    }

    /// <summary>
    /// <c>HIGHEST_PCT_BONUS</c> is only knowable at copy time. Expanding it throws rather than
    /// returning an empty list — an effect that silently selected no stats would apply to nothing
    /// and still report success.
    /// </summary>
    [Fact]
    public void HIGHEST_PCT_BONUS_cannot_be_expanded_before_evaluation()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => StatSelector.HighestPctBonus.Expand());

        thrown.Message.ShouldContain("18 §2.4", Case.Sensitive);
        thrown.Message.ShouldContain("copy time", Case.Sensitive);
    }

    [Fact]
    public void A_default_selector_names_nothing_and_says_so()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => default(StatSelector).Expand());

        thrown.Message.ShouldContain("names no stat", Case.Sensitive);
    }

    [Fact]
    public void A_selector_renders_as_the_token_the_DSL_writes()
    {
        StatSelector.Of(StatId.MAX_HP).ToString().ShouldBe("MAX_HP");
        StatSelector.AllCombat.ToString().ShouldBe("ALL_COMBAT");
        StatSelector.HighestPctBonus.ToString().ShouldBe("HIGHEST_PCT_BONUS");
    }

    /// <summary>
    /// Stated against a literal list: only naming the eleven non-combat stats can catch a stat that
    /// moved sides, which silently changes what <c>ALL_COMBAT</c> selects.
    /// </summary>
    [Fact]
    public void The_non_combat_stats_are_exactly_the_eleven_outside_the_actor_stat_block()
    {
        StatIds.NonCombat.ShouldBe(
        [
            StatId.GOLD_PCT, StatId.CROWNS_PCT, StatId.DROP_CHANCE, StatId.RARITY_SHIFT,
            StatId.ENERGY_REGEN_PCT, StatId.PET_AURA_PCT,
            StatId.TILE_PREVIEW, StatId.SHOP_PRICE_PCT, StatId.XP_PCT,
            StatId.BEAST_FEED_PCT, StatId.STONE_PCT,
        ]);
    }

    [Fact]
    public void A_value_that_is_not_a_declared_stat_is_rejected_rather_than_classified()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StatIds.IsCombat((StatId)999));
    }
}
