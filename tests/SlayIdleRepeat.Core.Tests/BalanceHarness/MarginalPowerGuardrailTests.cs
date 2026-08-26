using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// Guardrail 6: the structural zero, the ranking, and the synthetic archetype sets that show the
/// guardrail can both pass and fail.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class MarginalPowerGuardrailTests
{
    [Fact]
    public void HEAL_PCT_and_THORNS_move_PowerIndex_by_exactly_zero_in_every_archetype()
    {
        // The closed form has no term for either stat, so varying them across enormously different
        // values must leave PowerIndex bit-identical.
        foreach (var archetype in ShippedHarness.Runner.Calibration.Archetypes)
        {
            foreach (var stat in MarginalPowerGuardrail.StructurallyInvisibleStats)
            {
                var low = Power(archetype.Stats.With(stat, 0.0));
                var high = Power(archetype.Stats.With(stat, 25.0));
                var absurd = Power(archetype.Stats.With(stat, 10_000.0));

                low.ShouldBe(high);
                low.ShouldBe(absurd);
            }
        }
    }

    [Fact]
    public void The_negative_control_a_stat_the_model_DOES_see_moves_PowerIndex()
    {
        // Without this, the case above would also pass for a PowerIndex that ignored its stat block entirely.
        var stats = ShippedHarness.Runner.Calibration.Archetype("ARCH_CRIT").Stats;

        Power(stats.With(StatId.ATK, stats[StatId.ATK] * 2.0)).ShouldBeGreaterThan(Power(stats));
        Power(stats.With(StatId.MAX_HP, stats[StatId.MAX_HP] * 2.0)).ShouldBeGreaterThan(Power(stats));
        Power(stats.With(StatId.DEF, stats[StatId.DEF] * 2.0)).ShouldBeGreaterThan(Power(stats));
    }

    [Fact]
    public void The_two_invisible_stats_rank_last_in_every_archetype_whatever_the_step()
    {
        foreach (var archetype in ShippedHarness.Runner.Calibration.Archetypes)
        {
            var ranking = MarginalPowerGuardrail.Rank(ShippedHarness.Content, archetype, level: 40);

            ranking.Entries.Count.ShouldBe(14);
            ranking.Entries.Take(MarginalPowerGuardrail.TopN)
                .ShouldNotContain(e => MarginalPowerGuardrail.StructurallyInvisibleStats.Contains(e.Stat));

            foreach (var stat in MarginalPowerGuardrail.StructurallyInvisibleStats)
            {
                ranking.Entries.Single(e => e.Stat == stat).MarginalPower.ShouldBe(0.0);
            }
        }
    }

    [Fact]
    public void The_zero_held_stats_use_the_labelled_absolute_probe_and_the_others_do_not()
    {
        var archetype = ShippedHarness.Runner.Calibration.Archetype("ARCH_CRIT");
        var ranking = MarginalPowerGuardrail.Rank(ShippedHarness.Content, archetype, level: 40);

        // ARCH_CRIT holds LIFESTEAL at 0, so that row takes the labelled absolute probe; ATK at 150
        // takes a relative one (1% of its own value).
        ranking.Entries.Single(e => e.Stat == StatId.LIFESTEAL).UsedAbsoluteProbe.ShouldBeTrue();
        ranking.Entries.Single(e => e.Stat == StatId.LIFESTEAL).Step
            .ShouldBe(MarginalPowerGuardrail.AbsoluteProbeForZero);

        ranking.Entries.Single(e => e.Stat == StatId.ATK).UsedAbsoluteProbe.ShouldBeFalse();
        ranking.Entries.Single(e => e.Stat == StatId.ATK).Step.ShouldBe(150.0 * 0.01, tolerance: 1e-12);
    }

    [Fact]
    public void The_guardrail_FAILS_on_the_shipped_archetypes_naming_all_TEN_uncovered_stats()
    {
        // Ten, not two — the two structural zeros plus the eight the harness definitions additionally
        // suppress, which is a different defect. Pins the whole list rather than a sample of it.
        // ASPD joined the list with `16` D46: DR_PCT's improving-direction probe (÷1% of the
        // damage-taken multiplier) strictly out-lifts the ×1.01 the multiplicative trio takes, so the
        // top three is DR_PCT plus two of the exactly-tied {MAX_HP, ATK, ASPD} — and the deterministic
        // StatId tie-break always drops ASPD. A tie-break artifact of the harness, not a fact about
        // ASPD, and the summary's step-implicated branch says so.
        var result = MarginalPowerGuardrail.Evaluate(
            ShippedHarness.Content, ShippedHarness.Runner.Calibration.Archetypes, level: 40);

        result.Verdict.ShouldBe(GuardrailVerdict.Fail);
        result.SubjectCount.ShouldBe(14 * 5);

        result.Summary.ShouldContain(
            "10 of 14 stats are top-3 in no archetype: " +
            "DEF, ASPD, CRIT, CDMG, DODGE, BLOCK, PEN, DMG_PCT, HEAL_PCT, THORNS",
            Case.Sensitive,
            "the whole breach list, in StatIds.Combat order");

        // The two cause branches are not interchangeable: DEF and friends are not stats the model is
        // blind to — they are stats the +1% relative step cannot lift.
        result.Summary.ShouldContain(
            "the step definition is implicated", Case.Sensitive,
            "not the step-independent CAUSE branch, which would understate the finding");
        result.Summary.ShouldNotContain("CAUSE: `29` §2.3's closed form", Case.Sensitive);
    }

    [Fact]
    public void A_synthetic_set_in_which_a_named_stat_IS_top_three_reports_it_as_covered()
    {
        // DODGE is top-3 in no shipped archetype; a synthetic archetype that puts it there must make
        // the guardrail report it as covered, showing the per-stat verdict tracks the ranking rather
        // than being hard-coded. What puts it there is the harness step's own distortion: held at 0 it
        // gets the labelled absolute probe, which beats the relative step every other stat takes.
        var dodgy = new BuildArchetype("ARCH_SYNTHETIC_DODGE", NoZeroesExcept(StatId.DODGE));

        var ranking = MarginalPowerGuardrail.Rank(ShippedHarness.Content, dodgy, level: 40);
        ranking.Entries.Single(e => e.Stat == StatId.DODGE).UsedAbsoluteProbe.ShouldBeTrue();
        ranking.Entries.Take(3).Select(e => e.Stat).ShouldContain(StatId.DODGE);

        var result = MarginalPowerGuardrail.Evaluate(
            ShippedHarness.Content,
            [.. ShippedHarness.Runner.Calibration.Archetypes, dodgy],
            level: 40);
        string.Join("\n", result.Details).ShouldContain("ok       DODGE     top-3 in ARCH_SYNTHETIC_DODGE");

        // And the other half: without that archetype, DODGE is top-3 nowhere.
        var without = MarginalPowerGuardrail.Evaluate(
            ShippedHarness.Content, ShippedHarness.Runner.Calibration.Archetypes, level: 40);
        string.Join("\n", without.Details).ShouldContain("BREACH     DODGE     top-3 in NO archetype");
    }

    [Fact]
    public void The_relative_step_gives_MAX_HP_ATK_and_ASPD_an_identical_marginal_power()
    {
        // Power is sqrt(EffectiveHP x DPS); MAX_HP, ATK and ASPD each enter exactly once and
        // multiplicatively, so their elasticity is identical and they tie exactly in every archetype.
        foreach (var archetype in ShippedHarness.Runner.Calibration.Archetypes)
        {
            var ranking = MarginalPowerGuardrail.Rank(ShippedHarness.Content, archetype, level: 40);

            var maxHp = ranking.Entries.Single(e => e.Stat == StatId.MAX_HP).MarginalPower;
            var atk = ranking.Entries.Single(e => e.Stat == StatId.ATK).MarginalPower;
            var aspd = ranking.Entries.Single(e => e.Stat == StatId.ASPD).MarginalPower;

            // Within the 4-dp rounding PowerIndex applies, which is the only thing that separates them.
            atk.ShouldBe(maxHp, tolerance: 0.001);
            aspd.ShouldBe(maxHp, tolerance: 0.001);
            maxHp.ShouldBeGreaterThan(0.0);
        }
    }

    [Fact]
    public void Seven_stats_can_never_reach_the_top_three_under_a_pure_relative_step()
    {
        // MAX_HP, ATK and ASPD enter power multiplicatively (elasticity exactly 1); DEF, CRIT, CDMG,
        // BLOCK, PEN, LIFESTEAL and DMG_PCT enter through a (1 ± w*x) term (elasticity strictly below
        // 1 below their cap). So with no stat held at 0, those seven can never enter the top 3.
        //
        // Three very differently shaped statlines, so this isn't a coincidence of one set of numbers.
        var neverTopThree = new[]
        {
            StatId.DEF, StatId.CRIT, StatId.CDMG, StatId.LIFESTEAL,
            StatId.DODGE, StatId.BLOCK, StatId.PEN, StatId.DMG_PCT,
        };

        foreach (var line in new[] { AllNonZero(), AllNonZeroButBrittle(), AllNonZeroButGlassCannon() })
        {
            var ranking = MarginalPowerGuardrail.Rank(
                ShippedHarness.Content, new BuildArchetype("ARCH_PROBE", line), level: 40);

            ranking.Entries.ShouldAllBe(e => !e.UsedAbsoluteProbe);
            ranking.Entries.Count.ShouldBe(14);

            var topThree = ranking.Entries.Take(3).Select(e => e.Stat).ToArray();
            topThree.ShouldNotBeEmpty();
            foreach (var stat in neverTopThree)
            {
                topThree.ShouldNotContain(stat);
            }
        }
    }

    [Fact]
    public void DR_PCT_out_ranks_the_multiplicative_three_above_its_floor_and_scores_zero_at_it()
    {
        // Without this, "seven stats never reach the top 3" would also be satisfied by a ranking
        // that ignored every ratio stat. Under `16` D46 the probe steps DR_PCT down — its improving
        // direction — so shrinking the damage-taken divisor by 1% lifts EffectiveHP by ~1.0101%,
        // strictly more than the trio's ×1.01: DR_PCT ranks top-3 anywhere above its 0.4 floor. AT
        // the floor the downward step clamps away and its marginal power is exactly zero.
        var high = MarginalPowerGuardrail.Rank(
            ShippedHarness.Content,
            new BuildArchetype("ARCH_DR_HIGH", AllNonZero().With(StatId.DR_PCT, 0.59)),
            level: 40);

        var atFloor = MarginalPowerGuardrail.Rank(
            ShippedHarness.Content,
            new BuildArchetype("ARCH_DR_FLOOR", AllNonZero().With(StatId.DR_PCT, 0.4)),
            level: 40);

        high.Entries.Take(3).Select(e => e.Stat).ShouldContain(StatId.DR_PCT);
        atFloor.Entries.Single(e => e.Stat == StatId.DR_PCT).MarginalPower.ShouldBe(
            0.0, "both sides of the probe read the floored 0.4, like a stat at its cap");
        atFloor.Entries.Take(3).Select(e => e.Stat)
            .ShouldBe(new[] { StatId.MAX_HP, StatId.ATK, StatId.ASPD }, ignoreOrder: true);
    }

    [Fact]
    public void A_stat_at_its_authored_cap_has_exactly_zero_marginal_power()
    {
        // Caps are applied before the power model reads a stat, so a build at the CRIT cap gains
        // nothing from more crit. This zero is different from HEAL_PCT's: it moves once the stat
        // drops below the cap.
        var atCap = AllNonZero().With(StatId.CRIT, 0.75).With(StatId.CDMG, 2.0);
        var belowCap = AllNonZero().With(StatId.CRIT, 0.5).With(StatId.CDMG, 2.0);

        Marginal(atCap, StatId.CRIT).ShouldBe(0.0);
        Marginal(belowCap, StatId.CRIT).ShouldBeGreaterThan(0.0);

        // ...and it still cannot out-rank ATK, which is the point of the case above.
        Marginal(belowCap, StatId.CRIT).ShouldBeLessThan(Marginal(belowCap, StatId.ATK));
    }

    /// <summary>
    /// A statline with every one of the fourteen strictly above zero and under its cap, so nothing
    /// is clamped away and the ranking is about the formula rather than the caps.
    /// </summary>
    private static StatLine AllNonZero() => StatLine.From(new Dictionary<StatId, double>
    {
        [StatId.MAX_HP] = 1000,
        [StatId.ATK] = 100,
        [StatId.DEF] = 60,
        [StatId.ASPD] = 1.0,
        [StatId.CRIT] = 0.2,
        [StatId.CDMG] = 0.6,
        [StatId.LIFESTEAL] = 0.2,
        [StatId.DODGE] = 0.2,
        [StatId.BLOCK] = 0.2,
        [StatId.PEN] = 0.2,
        [StatId.DMG_PCT] = 0.2,
        [StatId.DR_PCT] = 0.2,
        [StatId.HEAL_PCT] = 1.0,
        [StatId.THORNS] = 0.2,
    });

    /// <summary>A second, very differently shaped statline — a wall with every ratio just under its cap.</summary>
    private static StatLine AllNonZeroButBrittle() => AllNonZero()
        .With(StatId.MAX_HP, 40_000)
        .With(StatId.ATK, 3)
        .With(StatId.DEF, 9_000)
        .With(StatId.ASPD, 3.0)
        .With(StatId.CRIT, 0.74)
        .With(StatId.CDMG, 4.0)
        .With(StatId.LIFESTEAL, 0.39)
        .With(StatId.DODGE, 0.49)
        .With(StatId.BLOCK, 0.59)
        .With(StatId.PEN, 0.69)
        .With(StatId.DMG_PCT, 3.0)
        .With(StatId.DR_PCT, 0.45);

    /// <summary>A third shape — all damage, no defence.</summary>
    private static StatLine AllNonZeroButGlassCannon() => AllNonZero()
        .With(StatId.MAX_HP, 120)
        .With(StatId.ATK, 9_000)
        .With(StatId.DEF, 1)
        .With(StatId.ASPD, 0.4)
        .With(StatId.CRIT, 0.6)
        .With(StatId.CDMG, 8.0)
        .With(StatId.LIFESTEAL, 0.05)
        .With(StatId.DODGE, 0.05)
        .With(StatId.BLOCK, 0.05)
        .With(StatId.PEN, 0.05)
        .With(StatId.DMG_PCT, 0.05)
        .With(StatId.DR_PCT, 0.05);

    /// <summary>
    /// <see cref="AllNonZero"/> with exactly one stat forced to 0, so exactly one row of the ranking
    /// takes the labelled absolute probe.
    /// </summary>
    private static StatLine NoZeroesExcept(StatId zeroed) => AllNonZero().With(zeroed, 0.0);

    private static double Marginal(StatLine line, StatId stat) =>
        MarginalPowerGuardrail
            .Rank(ShippedHarness.Content, new BuildArchetype("ARCH_PROBE", line), level: 40)
            .Entries.Single(e => e.Stat == stat)
            .MarginalPower;

    private static double Power(StatLine stats) =>
        PowerCalculator.PowerIndex(stats.ToActorStats(), 40, ShippedHarness.Content);
}
