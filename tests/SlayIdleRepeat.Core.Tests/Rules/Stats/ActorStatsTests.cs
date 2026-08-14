using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔒 `05` §2 — <em>"every actor — hero and enemy alike — carries a complete 14-stat block with
/// these defaults. <b>An unstated stat is a bug, not a zero.</b>"</em>
/// </summary>
public sealed class ActorStatsTests
{
    [Fact]
    public void The_block_is_exactly_the_fourteen_combat_stats_of_05_section_1()
    {
        ActorStats.Required.ShouldBe(
        [
            StatId.MAX_HP, StatId.ATK, StatId.DEF, StatId.ASPD, StatId.CRIT, StatId.CDMG,
            StatId.LIFESTEAL, StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DMG_PCT,
            StatId.DR_PCT, StatId.HEAL_PCT, StatId.THORNS,
        ]);

        ActorStats.Required.Count.ShouldBe(14);
    }

    /// <summary>
    /// 🔒 The completeness rule is stated over <see cref="StatIds.Combat"/> at run time, not over a
    /// fourteen-parameter constructor.
    /// </summary>
    /// <remarks>
    /// That is what makes "an unstated stat is a bug" survive a <em>fifteenth</em> combat stat: a
    /// fixed-arity constructor would keep compiling on the commit that adds one and keep returning a
    /// block with a silent hole. The case cannot add an enum member, so it asserts the property that
    /// would make it bite — the required set is the classification itself.
    /// </remarks>
    [Fact]
    public void Every_stat_StatIds_declares_combat_must_be_supplied_or_construction_fails()
    {
        ActorStats.Required.ShouldBe(StatIds.Combat, "the rule is the classification, not a copy of it");

        foreach (var omitted in StatIds.Combat)
        {
            var partial = StatIds.Combat.Where(s => s != omitted).ToDictionary(s => s, _ => 0.0);

            var thrown = Should.Throw<ArgumentException>(() => ActorStats.From(partial));

            thrown.Message.ShouldContain(omitted.ToString(), Case.Sensitive);
            thrown.Message.ShouldContain("an unstated stat is a bug, not a zero", Case.Sensitive);
        }
    }

    /// <summary>
    /// 🔒 There is no partially-populated block to construct: no public constructor, and no
    /// <c>default</c> because it is a reference type.
    /// </summary>
    [Fact]
    public void There_is_no_way_to_construct_a_block_that_bypasses_the_completeness_check()
    {
        typeof(ActorStats).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .ShouldBeEmpty("a public constructor would be a route past From's completeness check");

        typeof(ActorStats).IsClass.ShouldBeTrue(
            "a struct would give every caller a zero-initialised block for free — the exact 'unstated " +
            "stat silently reads as zero' that 05 §2 forbids");

        typeof(ActorStats).IsSealed.ShouldBeTrue("a subclass could add a second way in");
    }

    [Fact]
    public void A_stat_that_is_not_a_combat_stat_is_rejected_rather_than_dropped()
    {
        var withGold = StatIds.Combat.ToDictionary(s => s, _ => 0.0);
        withGold[StatId.GOLD_PCT] = 0.25;

        var thrown = Should.Throw<ArgumentException>(() => ActorStats.From(withGold));

        thrown.Message.ShouldContain("GOLD_PCT", Case.Sensitive);
        thrown.Message.ShouldContain("26 stats wide", Case.Sensitive);
    }

    [Fact]
    public void An_undeclared_enum_value_is_named_by_its_number_rather_than_crashing_the_message()
    {
        var withGarbage = StatIds.Combat.ToDictionary(s => s, _ => 0.0);
        withGarbage[(StatId)999] = 1.0;

        Should.Throw<ArgumentException>(() => ActorStats.From(withGarbage))
              .Message.ShouldContain("(999)", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `05` §1.1's rounding rule, guarded at the block rather than only at the writer. A value
    /// arriving unrounded means an accumulation point upstream is missing its <c>Math.Round(x, 4)</c>.
    /// </summary>
    [Fact]
    public void A_value_that_is_not_rounded_to_four_places_is_refused()
    {
        var unrounded = StatIds.Combat.ToDictionary(s => s, _ => 0.0);
        unrounded[StatId.ATK] = 1.234_56;

        Should.Throw<ArgumentException>(() => ActorStats.From(unrounded))
              .Message.ShouldContain("05 §1.1", Case.Sensitive);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_value_that_is_not_finite_is_refused(double value)
    {
        var broken = StatIds.Combat.ToDictionary(s => s, _ => 0.0);
        broken[StatId.MAX_HP] = value;

        Should.Throw<ArgumentException>(() => ActorStats.From(broken));
    }

    [Fact]
    public void A_negative_zero_is_refused_because_CanonicalStateWriter_refuses_to_encode_one()
    {
        var negativeZero = StatIds.Combat.ToDictionary(s => s, _ => 0.0);
        negativeZero[StatId.THORNS] = -0.0;

        Should.Throw<ArgumentException>(() => ActorStats.From(negativeZero));
    }

    [Fact]
    public void Reading_a_stat_the_block_does_not_hold_throws_rather_than_answering_zero()
    {
        var block = StatFixtures.Zeroed();

        Should.Throw<ArgumentOutOfRangeException>(() => _ = block[StatId.GOLD_PCT]);
    }

    [Fact]
    public void With_replaces_one_stat_and_leaves_the_rest()
    {
        var block = StatFixtures.Block((StatId.ATK, 30.0), (StatId.MAX_HP, 295.0));

        var raised = block.With(StatId.ATK, 42.0);

        raised[StatId.ATK].ShouldBe(42.0);
        raised[StatId.MAX_HP].ShouldBe(295.0);
        block[StatId.ATK].ShouldBe(30.0, "the original block is not mutated");
    }

    [Fact]
    public void With_applies_the_same_rounding_guard_as_From()
    {
        Should.Throw<ArgumentException>(() => StatFixtures.Zeroed().With(StatId.ATK, 1.234_56));
        Should.Throw<ArgumentOutOfRangeException>(() => StatFixtures.Zeroed().With(StatId.GOLD_PCT, 1.0));
    }

    [Fact]
    public void Two_blocks_with_the_same_values_are_equal()
    {
        StatFixtures.Block((StatId.ATK, 30.0)).ShouldBe(StatFixtures.Block((StatId.ATK, 30.0)));
        StatFixtures.Block((StatId.ATK, 30.0)).ShouldNotBe(StatFixtures.Block((StatId.ATK, 31.0)));

        StatFixtures.Block((StatId.ATK, 30.0)).GetHashCode()
            .ShouldBe(StatFixtures.Block((StatId.ATK, 30.0)).GetHashCode());
    }

    [Fact]
    public void Values_reads_back_in_05_section_1_table_order()
    {
        StatFixtures.Zeroed().Values.Select(v => v.Key).ShouldBe(ActorStats.Required);
    }
}
