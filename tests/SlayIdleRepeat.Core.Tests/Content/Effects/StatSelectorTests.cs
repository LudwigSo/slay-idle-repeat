using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// 🔒 <c>ALL_COMBAT</c> is a stat <b>selector</b>, not a stat — the `18` §9.1 ruling, as a type.
/// </summary>
public sealed class StatSelectorTests
{
    [Fact]
    public void ALL_COMBAT_is_not_a_member_of_the_stat_enum()
    {
        Enum.GetNames<StatId>().ShouldNotContain(
            "ALL_COMBAT",
            "a stat has a base value, a cap and a row in the aggregation; ALL_COMBAT has none of " +
            "the three, and the 14 combat members here are exactly the actor stat block");

        Enum.GetNames<StatId>().ShouldNotContain("HIGHEST_PCT_BONUS");
    }

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
    /// <c>HIGHEST_PCT_BONUS</c> is only knowable at copy time (`18` §2.4). Expanding it throws
    /// rather than returning an empty list — an effect that silently selected no stats would apply
    /// to nothing and still report success.
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
    /// 🔒 `18` §9.1's <c>CP_GLASS_HEART</c>, as pure data and as two effects — the second exists
    /// precisely because the group selector cannot set one stat.
    /// </summary>
    /// <remarks>
    /// <em>"A pre-agreed downgrade to ×1.6 must be a one-number edit in data, never a code
    /// change."</em> Nothing in <c>SlayIdleRepeat.Core.Content.Effects</c> knows the number 2.0;
    /// this case supplies it, which is the demonstration.
    /// </remarks>
    [Fact]
    public void CP_GLASS_HEART_is_two_effects_and_the_multiplier_is_data()
    {
        var doubled = new EffectDefinition
        {
            Id = "CP_GLASS_HEART_MULT",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.AllCombat,
            Value = 2.0,
        };

        var oneHp = new EffectDefinition
        {
            Id = "CP_GLASS_HEART_SET_HP",
            Op = EffectOp.STAT_SET,
            Stat = StatSelector.Of(StatId.MAX_HP),
            Value = 1.0,
            ValueMode = ValueMode.FLAT,
        };

        doubled.Stat!.Value.Expand().Count.ShouldBe(14);
        oneHp.Stat!.Value.Expand().ShouldBe([StatId.MAX_HP]);

        // The downgrade 18 §9.1 pre-agrees is a `with` on the data, not a branch anywhere.
        (doubled with { Value = 1.6 }).Value.ShouldBe(1.6);
    }

    [Fact]
    public void Every_stat_is_classified_combat_or_non_combat()
    {
        var all = StatIds.All;

        all.Count.ShouldBe(26);
        StatIds.Combat.Concat(StatIds.NonCombat).OrderBy(s => (int)s).ShouldBe(all.OrderBy(s => (int)s));
        StatIds.Combat.ShouldNotContain(s => StatIds.NonCombat.Contains(s));
    }

    [Fact]
    public void A_value_that_is_not_a_declared_stat_is_rejected_rather_than_classified()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => StatIds.IsCombat((StatId)999));
    }
}
