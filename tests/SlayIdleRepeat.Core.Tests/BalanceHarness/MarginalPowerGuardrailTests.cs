using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 `05` §9 guardrail 6 — the structural zero, the ranking, and the synthetic archetype sets that
/// show the guardrail can both pass and fail.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class MarginalPowerGuardrailTests
{
    [Fact]
    public void HEAL_PCT_and_THORNS_move_PowerIndex_by_exactly_zero_in_every_archetype()
    {
        // 🔴 THE load-bearing case, and it is deliberately NOT a statement about a step size. `29`
        // §2.3's closed form has no term for either stat, so varying them over two enormously
        // different values must leave PowerIndex bit-identical. If this ever fails, the model grew a
        // term and guardrail 6's verdict needs re-deriving rather than re-reading.
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
        // Without this, the case above would also pass for a PowerIndex that ignored its stat block
        // entirely. Two stats, one from each factor of the geometric mean.
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

        // ARCH_CRIT holds LIFESTEAL, BLOCK, DR_PCT and THORNS at 0 — those rows are the labelled
        // absolute probe; ATK at 150 is 1% of its own value.
        ranking.Entries.Single(e => e.Stat == StatId.LIFESTEAL).UsedAbsoluteProbe.ShouldBeTrue();
        ranking.Entries.Single(e => e.Stat == StatId.LIFESTEAL).Step
            .ShouldBe(MarginalPowerGuardrail.AbsoluteProbeForZero);

        ranking.Entries.Single(e => e.Stat == StatId.ATK).UsedAbsoluteProbe.ShouldBeFalse();
        ranking.Entries.Single(e => e.Stat == StatId.ATK).Step.ShouldBe(150.0 * 0.01, tolerance: 1e-12);
    }

    [Fact]
    public void The_guardrail_FAILS_on_the_shipped_archetypes_naming_all_NINE_uncovered_stats()
    {
        // 🔴 NINE, not two — and the difference is the finding. Asserting only that the summary
        // mentions HEAL_PCT and THORNS is satisfied by a result that named two stats, or nine, or all
        // fourteen; it cannot tell the structural zero (the two `29` §2.3 has no term for) apart from
        // the seven the RELATIVE STEP additionally suppresses, which is a different defect with a
        // different remedy. Seven_stats_can_never_reach_the_top_three_under_a_pure_relative_step below
        // derives exactly that set, so this case pins the whole list rather than a sample of it.
        var result = MarginalPowerGuardrail.Evaluate(
            ShippedHarness.Content, ShippedHarness.Runner.Calibration.Archetypes, level: 40);

        result.Verdict.ShouldBe(GuardrailVerdict.Fail);
        result.SubjectCount.ShouldBe(14 * 5);

        result.Summary.ShouldContain(
            "9 of 14 stats are top-3 in no archetype: " +
            "DEF, CRIT, CDMG, DODGE, BLOCK, PEN, DMG_PCT, HEAL_PCT, THORNS",
            Case.Sensitive,
            "the whole breach list, in StatIds.Combat order");

        // 🔒 And WHICH cause the report reached. The two branches are not interchangeable: one says
        // the verdict is step-independent, the other says the step is implicated and the ranking needs
        // reading. On the shipped archetypes it must be the second, because DEF and friends are not
        // stats `29` §2.3 is blind to — they are stats the +1% relative step cannot lift.
        result.Summary.ShouldContain(
            "the step definition is implicated", Case.Sensitive,
            "not the step-independent CAUSE branch, which would understate the finding");
        result.Summary.ShouldNotContain("CAUSE: `29` §2.3's closed form", Case.Sensitive);
    }

    [Fact]
    public void A_synthetic_set_in_which_a_named_stat_IS_top_three_reports_it_as_covered()
    {
        // 🔴 The discriminating control the steering asks for. DODGE is top-3 in NO shipped archetype;
        // a synthetic archetype that puts it there must make the guardrail report it as covered, which
        // shows the per-stat verdict tracks the ranking rather than being hard-coded.
        //
        // ⚠️ What actually puts DODGE in the top 3 is worth stating, because it is the harness step's
        // distortion rather than anything about dodge: an archetype holding DODGE at 0 gets the
        // labelled +0.01 ABSOLUTE probe, and 1/(1 - 0.01) beats the exactly +1% that a relative step
        // buys on ATK. Every other stat here is held above 0 so that it takes the relative step.
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
        // 🔴 A structural property of the +1% relative step, and the reason guardrail 6 is unpassable
        // under it for more stats than the two the model cannot see. `29` §2.1's power is
        // sqrt(EffectiveHP × DPS); MAX_HP, ATK and ASPD each enter exactly once and multiplicatively,
        // so d ln P / d ln x = 1/2 for all three, whatever the statline says. They tie, exactly, in
        // every archetype.
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
        // 🔴 The consequence, and a finding in its own right. `29` §2.1's power is
        // sqrt(EffectiveHP × DPS), so d ln P / d ln x is half the elasticity of x in that product:
        //
        //   MAX_HP, ATK, ASPD   enter multiplicatively            -> elasticity exactly 1
        //   DEF, CRIT, CDMG,    enter through a term (1 ± w·x)    -> elasticity strictly below 1
        //   BLOCK, PEN,         for every value below `05` §1's cap
        //   LIFESTEAL, DMG_PCT
        //
        // So with no stat held at 0 — no labelled absolute probe anywhere — those seven are
        // STRUCTURALLY unable to enter the top 3 of any archetype whatsoever. Guardrail 6 is therefore
        // unpassable under this step for more stats than the two `29` §2.3 has no term for, and the
        // report says so.
        //
        // Three very differently shaped statlines, because one would look like a coincidence of the
        // numbers rather than a property of the formula.
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
    public void DR_PCT_is_the_one_ratio_stat_that_CAN_out_rank_them_and_only_near_its_cap()
    {
        // The discriminating half of the case above — without it, "seven stats never reach the top 3"
        // would also be satisfied by a ranking that ignored every ratio stat. DR_PCT's elasticity is
        // DR/(1 - DR), which passes 1 at DR = 0.5 and reaches 1.5 at `05` §1's 0.6 cap. So it displaces
        // one of the multiplicative three at 0.59 and does not at 0.2.
        var high = MarginalPowerGuardrail.Rank(
            ShippedHarness.Content,
            new BuildArchetype("ARCH_DR_HIGH", AllNonZero().With(StatId.DR_PCT, 0.59)),
            level: 40);

        var low = MarginalPowerGuardrail.Rank(
            ShippedHarness.Content,
            new BuildArchetype("ARCH_DR_LOW", AllNonZero().With(StatId.DR_PCT, 0.2)),
            level: 40);

        high.Entries.Take(3).Select(e => e.Stat).ShouldContain(StatId.DR_PCT);
        low.Entries.Take(3).Select(e => e.Stat).ShouldNotContain(StatId.DR_PCT);
        low.Entries.Take(3).Select(e => e.Stat)
            .ShouldBe(new[] { StatId.MAX_HP, StatId.ATK, StatId.ASPD }, ignoreOrder: true);
    }

    [Fact]
    public void A_stat_at_its_authored_cap_has_exactly_zero_marginal_power()
    {
        // `05` §1.1's caps are applied before the power model reads a stat, so a build already at the
        // 0.75 CRIT cap gains nothing from more crit — `29` §2.3 says so in as many words. The step
        // therefore measures 0, which is a different zero from HEAL_PCT's and must not be confused
        // with it: this one moves the moment the stat drops below the cap.
        var atCap = AllNonZero().With(StatId.CRIT, 0.75).With(StatId.CDMG, 2.0);
        var belowCap = AllNonZero().With(StatId.CRIT, 0.5).With(StatId.CDMG, 2.0);

        Marginal(atCap, StatId.CRIT).ShouldBe(0.0);
        Marginal(belowCap, StatId.CRIT).ShouldBeGreaterThan(0.0);

        // ...and it still cannot out-rank ATK, which is the point of the case above.
        Marginal(belowCap, StatId.CRIT).ShouldBeLessThan(Marginal(belowCap, StatId.ATK));
    }

    /// <summary>A statline with every one of the fourteen strictly above zero.</summary>
    /// <remarks>
    /// Every value is under `05` §1's cap for its stat, so nothing is clamped away before the
    /// derivative is taken and the ranking is about the formula rather than about the caps.
    /// </remarks>
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

    /// <summary>
    /// A second, very differently shaped statline — a wall with every ratio just under its cap.
    /// </summary>
    /// <remarks>
    /// <c>DR_PCT</c> is held at 0.45 rather than at its 0.6 cap: above 0.5 its elasticity passes 1 and
    /// it legitimately displaces one of the multiplicative three, which is its own case below.
    /// </remarks>
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
