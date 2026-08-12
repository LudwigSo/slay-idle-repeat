using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// 🔒 `05` §1 — the six stat ceilings, and `18` §8 step 9's application of them.
/// </summary>
public sealed class StatCapsTests
{
    /// <summary>
    /// The six `05` §1 caps a nobody-has-overridden-anything table binds — and, just as importantly,
    /// the eight it does not.
    /// </summary>
    [Fact]
    public void Exactly_the_six_stats_05_section_1_caps_carry_a_ceiling()
    {
        StatFixtures.Caps().Capped.ShouldBe(
        [
            StatId.CRIT, StatId.LIFESTEAL, StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DR_PCT,
        ]);

        foreach (var uncapped in (StatId[])
        [
            StatId.MAX_HP, StatId.ATK, StatId.DEF, StatId.ASPD,
            StatId.CDMG, StatId.DMG_PCT, StatId.HEAL_PCT, StatId.THORNS,
        ])
        {
            StatFixtures.Caps().Maximum(uncapped).ShouldBeNull(
                $"05 §1's Notes column caps {uncapped} nowhere, and absence is the only 'uncapped' 05 authorises");
        }
    }

    [Theory]
    [InlineData(StatId.CRIT, 0.75)]
    [InlineData(StatId.LIFESTEAL, 0.40)]
    [InlineData(StatId.DODGE, 0.50)]
    [InlineData(StatId.BLOCK, 0.60)]
    [InlineData(StatId.PEN, 0.70)]
    [InlineData(StatId.DR_PCT, 0.60)]
    public void A_stat_above_its_ceiling_is_bound_to_it(StatId stat, double cap)
    {
        StatFixtures.Caps().Maximum(stat).ShouldBe(cap);
        StatFixtures.Caps().Apply(stat, cap + 0.5).ShouldBe(cap);
        StatFixtures.Caps().Apply(stat, cap).ShouldBe(cap, "the cap itself is reachable");
        StatFixtures.Caps().Apply(stat, cap - 0.1).ShouldBe(cap - 0.1, "below the cap nothing happens");
    }

    [Fact]
    public void An_uncapped_stat_passes_through_however_large()
    {
        StatFixtures.Caps().Apply(StatId.ATK, 1_000_000.0).ShouldBe(1_000_000.0);
        StatCaps.None.Apply(StatId.CRIT, 40.0).ShouldBe(40.0);
    }

    /// <summary>
    /// 🔒 There is deliberately no floor. `18` §8 step 9 says "apply caps" — one direction — and
    /// inventing a lower clamp would be a rule the design has not authorised (`16` R6). Recorded as
    /// a case so the absence is a decision on the record rather than an oversight.
    /// </summary>
    [Fact]
    public void A_negative_value_is_not_clamped_because_05_authorises_no_floor()
    {
        StatFixtures.Caps().Apply(StatId.DR_PCT, -0.25).ShouldBe(-0.25);
        StatFixtures.Caps().Apply(StatId.CRIT, -1.0).ShouldBe(-1.0);
    }

    [Fact]
    public void A_cap_on_a_non_combat_stat_is_refused()
    {
        var thrown = Should.Throw<ArgumentException>(
            () => StatCaps.From(new Dictionary<StatId, double> { [StatId.GOLD_PCT] = 0.5 }));

        thrown.Message.ShouldContain("GOLD_PCT", Case.Sensitive);
    }

    [Fact]
    public void A_cap_that_is_not_itself_rounded_is_refused()
    {
        var thrown = Should.Throw<ArgumentException>(
            () => StatCaps.From(new Dictionary<StatId, double> { [StatId.CRIT] = 0.750_05 }));

        thrown.Message.ShouldContain("05 §1.1", Case.Sensitive);
    }

    /// <summary>`18` §2.1's <c>STAT_CAP_OVERRIDE</c> mechanism — the table can be rewritten.</summary>
    [Fact]
    public void With_raises_a_ceiling_without_touching_the_others()
    {
        var raised = StatFixtures.Caps().With(StatId.CRIT, 0.90);

        raised.Maximum(StatId.CRIT).ShouldBe(0.90);
        raised.Maximum(StatId.DODGE).ShouldBe(0.50);
        StatFixtures.Caps().Maximum(StatId.CRIT).ShouldBe(0.75, "the original table is not mutated");
    }

    [Fact]
    public void With_can_cap_a_stat_05_leaves_uncapped()
    {
        StatFixtures.Caps().With(StatId.THORNS, 2.0).Maximum(StatId.THORNS).ShouldBe(2.0);
    }

    [Fact]
    public void None_caps_nothing()
    {
        StatCaps.None.Capped.ShouldBeEmpty();
    }
}
