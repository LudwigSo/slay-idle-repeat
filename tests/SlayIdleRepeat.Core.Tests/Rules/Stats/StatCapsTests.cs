using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>The six stat ceilings, and their application.</summary>
public sealed class StatCapsTests
{
    [Theory]
    [InlineData(StatId.CRIT, 0.75)]
    [InlineData(StatId.LIFESTEAL, 0.40)]
    [InlineData(StatId.DODGE, 0.50)]
    [InlineData(StatId.BLOCK, 0.60)]
    [InlineData(StatId.PEN, 0.70)]
    public void A_stat_above_its_ceiling_is_bound_to_it(StatId stat, double cap)
    {
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
    /// `16` D46's one authored floor binds: <c>DR_PCT</c>, the damage-taken multiplier, never
    /// aggregates below 0.4 — the old 0.60 reduction cap re-expressed as <c>1.0 − 0.6</c>.
    /// </summary>
    [Fact]
    public void DR_PCT_is_floored_at_the_authored_damage_taken_floor()
    {
        StatFixtures.Caps().Apply(StatId.DR_PCT, 0.2).ShouldBe(0.4);
        StatFixtures.Caps().Apply(StatId.DR_PCT, 0.4).ShouldBe(0.4, "the floor itself is reachable");
        StatFixtures.Caps().Apply(StatId.DR_PCT, 0.55).ShouldBe(0.55, "above the floor nothing happens");
        StatFixtures.Caps().Apply(StatId.DR_PCT, 1.7).ShouldBe(1.7, "no ceiling remains on the multiplier");
    }

    /// <summary>
    /// No other stat gained a floor: `16` D46 authors exactly one, and inventing a lower clamp for
    /// the rest would be an unauthorised rule. Recorded as a case so the absence stays a decision.
    /// </summary>
    [Fact]
    public void A_negative_value_is_not_clamped_because_05_authorises_no_floor()
    {
        StatFixtures.Caps().Apply(StatId.BLOCK, -0.25).ShouldBe(-0.25);
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

    /// <summary>The <c>STAT_CAP_OVERRIDE</c> mechanism — the table can be rewritten.</summary>
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
}
