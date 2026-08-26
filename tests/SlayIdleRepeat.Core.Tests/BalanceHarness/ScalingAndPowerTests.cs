using Shouldly;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>The derived <c>K_POWER</c> constant and the scaling rule.</summary>
[Collection(WallClockSensitive.Name)]
public sealed class ScalingAndPowerTests
{
    [Fact]
    public void K_POWER_is_derived_from_the_reference_build_and_matches_29_2_1_s_expected_magnitude()
    {
        var kPower = ShippedHarness.Runner.KPower;

        // The definition: PlayerPower(referenceParBuild) := 1000, so K_POWER = 1000 / PowerIndex.
        // Restated here from the two authored inputs rather than taken from the type under test.
        var calibration = CalibrationBuilds.Read(ShippedHarness.Content);
        var index = PowerCalculator.PowerIndex(
            calibration.ReferenceParBuild.ToActorStats(), 10, ShippedHarness.Content);

        kPower.ReferenceLevel.ShouldBe(10);
        kPower.ReferencePowerIndex.ShouldBe(index);
        kPower.Value.ShouldBe(1000.0 / index, tolerance: 1e-12);

        // A transcription check on every weight PowerIndex reads, not a tight assertion — the band is
        // wide on purpose but still discriminating, since getting a weight wrong moves this by a
        // factor, not a percent.
        kPower.Value.ShouldBeInRange(4.0, 7.0);
    }

    [Fact]
    public void K_POWER_cancels_out_of_the_target_it_produces()
    {
        // PowerIndex targets are ratios, so the constant is irrelevant to every guardrail.
        var runner = ShippedHarness.Runner;

        runner.KPower.TargetPowerIndex(1000.0)
            .ShouldBe(runner.KPower.ReferencePowerIndex, tolerance: 1e-9);

        // Four times the power is four times the index, exactly.
        runner.KPower.TargetPowerIndex(4000.0)
            .ShouldBe(4.0 * runner.KPower.ReferencePowerIndex, tolerance: 1e-9);
    }

    [Fact]
    public void Compute_still_throws_on_the_shipped_data_and_that_is_why_PowerIndex_is_used()
    {
        // tuning/power_model.json#/kPower is authored null and PowerCalculator.Compute throws by
        // design. If it stopped throwing, someone filled the hole and the derivation needs revisiting.
        var stats = CalibrationBuilds.Read(ShippedHarness.Content).ReferenceParBuild.ToActorStats();

        // The identity of the refusal, not merely that something threw — Compute has several
        // documented failure paths, and this claims the narrower one: the authored hole is still a hole.
        Should.Throw<UnauthorisedTunableException>(
                () => PowerCalculator.Compute(stats, 10, ShippedHarness.Content))
            .Reference.ShouldBe("tuning/power_model.json#/kPower");

        Should.NotThrow(() => PowerCalculator.PowerIndex(stats, 10, ShippedHarness.Content));
    }

    [Theory]
    [InlineData(1, "ARCH_CRIT")]
    [InlineData(4, "ARCH_TANK_THORNS")]
    [InlineData(8, "ARCH_PET")]
    public void The_scaling_rule_places_a_loadout_within_the_authored_tolerance(int chapter, string archetypeId)
    {
        var runner = ShippedHarness.Runner;
        var archetype = runner.Calibration.Archetype(archetypeId);

        foreach (var tier in Tiers.All)
        {
            var scaled = runner.ParHero(chapter, tier, archetype.Stats);

            // The authored tolerance (0.1%) with room for the 4-dp rounding of s afterward.
            scaled.AchievedRatio.ShouldBeInRange(0.99, 1.01);
            scaled.Level.ShouldBe(runner.Enemies.Level(chapter, tier));

            // "round s to 4 dp", literally.
            scaled.Scalar.ShouldBe(Math.Round(scaled.Scalar, 4));
        }
    }

    [Fact]
    public void Only_maxHp_atk_and_def_are_scaled_and_the_ratio_stats_are_untouched()
    {
        var runner = ShippedHarness.Runner;
        var archetype = runner.Calibration.Archetype("ARCH_CRIT");
        var scaled = runner.ParHero(5, Tier.MYTHIC, archetype.Stats);

        scaled.Scalar.ShouldBeGreaterThan(2.0);

        foreach (var stat in StatIds.Combat)
        {
            if (stat is StatId.MAX_HP or StatId.ATK or StatId.DEF)
            {
                scaled.Stats[stat].ShouldBe(
                    Math.Round(archetype.Stats[stat] * scaled.Scalar, 4), tolerance: 1e-9);
            }
            else
            {
                // The discriminating half: a scaler that multiplied everything would fail here, and
                // the scalar above is large enough that it could not pass by coincidence.
                scaled.Stats[stat].ShouldBe(archetype.Stats[stat]);
            }
        }
    }

    [Fact]
    public void PowerIndex_is_strictly_increasing_in_the_scalar_which_is_what_makes_bisection_sound()
    {
        var archetype = ShippedHarness.Runner.Calibration.Archetype("ARCH_DOT");
        var scaled = new[] { 0.25, 0.5, 1.0, 2.0, 4.0, 16.0 }
            .Select(s => PowerCalculator.PowerIndex(
                archetype.Stats.ScaledBy(s, [StatId.MAX_HP, StatId.ATK, StatId.DEF]).ToActorStats(),
                40,
                ShippedHarness.Content))
            .ToArray();

        scaled.Length.ShouldBe(6);
        for (var i = 1; i < scaled.Length; i++)
        {
            scaled[i].ShouldBeGreaterThan(scaled[i - 1]);
        }
    }

    [Fact]
    public void The_evaluation_level_changes_the_power_a_statline_scores()
    {
        // The mitigation denominator carries the attacker level, so a level-blind scaling rule would
        // place a hero at the wrong power for every chapter but one.
        var archetype = ShippedHarness.Runner.Calibration.Archetype("ARCH_CRIT");
        var stats = archetype.Stats.ToActorStats();

        var atTen = PowerCalculator.PowerIndex(stats, 10, ShippedHarness.Content);
        var atHundred = PowerCalculator.PowerIndex(stats, 100, ShippedHarness.Content);

        atHundred.ShouldBeGreaterThan(atTen);
        (atHundred / atTen).ShouldBeGreaterThan(1.05);
    }

    /// <summary>
    /// `16` D46: <c>Dps</c> consumes <c>DMG%</c> bare. 1.15 against the 1.0 identity scales DPS by
    /// exactly 1.15; the additive reading scales it by 2.15 / 2.0 = 1.075.
    /// </summary>
    [Fact]
    public void Dps_consumes_DMG_PCT_bare()
    {
        var baseline = PowerCalculator.Dps(PowerBlock((StatId.DMG_PCT, 1.0)), 10, ShippedHarness.Content);
        var boosted = PowerCalculator.Dps(PowerBlock((StatId.DMG_PCT, 1.15)), 10, ShippedHarness.Content);

        (boosted / baseline).ShouldBe(1.15, tolerance: 1e-6);
    }

    /// <summary>
    /// `16` D46: <c>EffectiveHp</c> divides by the damage-taken multiplier itself. Doubling the
    /// multiplier (0.4 → 0.8) halves survivability; the subtractive reading with the old 0.6 cap
    /// answers 1.5 for the same pair.
    /// </summary>
    [Fact]
    public void EffectiveHp_divides_by_the_bare_damage_taken_multiplier()
    {
        var atFloor = PowerCalculator.EffectiveHp(PowerBlock((StatId.DR_PCT, 0.4)), ShippedHarness.Content);
        var doubled = PowerCalculator.EffectiveHp(PowerBlock((StatId.DR_PCT, 0.8)), ShippedHarness.Content);

        (doubled / atFloor).ShouldBe(0.5, tolerance: 1e-6);
    }

    /// <summary>
    /// The 0.4 damage-taken floor (`05` §1's 0.6 cap re-expressed) binds inside the power model
    /// too: 0.2 scores exactly what 0.4 scores, and 0.5 — above the floor — scores less.
    /// </summary>
    [Fact]
    public void The_damage_taken_floor_binds_inside_EffectiveHp()
    {
        var below = PowerCalculator.EffectiveHp(PowerBlock((StatId.DR_PCT, 0.2)), ShippedHarness.Content);
        var atFloor = PowerCalculator.EffectiveHp(PowerBlock((StatId.DR_PCT, 0.4)), ShippedHarness.Content);
        var above = PowerCalculator.EffectiveHp(PowerBlock((StatId.DR_PCT, 0.5)), ShippedHarness.Content);

        below.ShouldBe(atFloor, "0.2 floors to 0.4 before the model reads it");
        above.ShouldBeLessThan(atFloor, "0.5 is above the floor and passes through — the negative control");
    }

    /// <summary>A minimal but complete block for the two D46 pins: identity DMG%/DR% unless named.</summary>
    private static ActorStats PowerBlock(params (StatId Stat, double Value)[] overrides)
    {
        var values = new List<(StatId, double)>
        {
            (StatId.MAX_HP, 1000.0),
            (StatId.ATK, 100.0),
            (StatId.DEF, 100.0),
            (StatId.ASPD, 1.0),
            (StatId.HEAL_PCT, 1.0),
            (StatId.DMG_PCT, 1.0),
            (StatId.DR_PCT, 1.0),
        };
        values.AddRange(overrides.Select(o => (o.Stat, o.Value)));

        return Rules.Stats.StatFixtures.Block(values.ToArray());
    }

    [Fact]
    public void A_scalar_that_is_not_finite_and_positive_is_refused()
    {
        var stats = ShippedHarness.Runner.Calibration.Archetype("ARCH_PET").Stats;

        Should.Throw<ArgumentOutOfRangeException>(() => stats.ScaledBy(0.0, [StatId.ATK]));
        Should.Throw<ArgumentOutOfRangeException>(() => stats.ScaledBy(-1.0, [StatId.ATK]));
        Should.Throw<ArgumentOutOfRangeException>(() => stats.ScaledBy(double.NaN, [StatId.ATK]));
    }

    [Fact]
    public void Every_stat_line_the_harness_builds_is_accepted_by_ActorStats_From()
    {
        // ActorStats.From requires every value to be rounded to 4 dp and rejects a negative zero.
        // The bisection produces arbitrary doubles, so this is the property that keeps a 40-minute
        // sweep from dying on an ArgumentException in cell 97.
        var runner = ShippedHarness.Runner;

        foreach (var archetype in runner.Calibration.Archetypes)
        {
            foreach (var chapter in new[] { 1, 8 })
            {
                foreach (var tier in Tiers.All)
                {
                    Should.NotThrow(() => runner.ParHero(chapter, tier, archetype.Stats).Stats.ToActorStats());
                }
            }
        }
    }

    [Fact]
    public void The_harness_rounding_is_the_expression_DeterminismRounding_uses()
    {
        HarnessRounding.Decimals.ShouldBe(4);
        HarnessRounding.Round(1.00004999).ShouldBe(1.0);
        HarnessRounding.Round(1.23456789).ShouldBe(1.2346);

        // MidpointRounding.ToEven, probed on exact binary midpoints (5/32, 21/32) so the tie is real
        // rather than a decimal-literal artefact. AwayFromZero rounds up on the same inputs.
        HarnessRounding.Round(0.15625).ShouldBe(0.1562);
        Math.Round(0.15625, 4, MidpointRounding.AwayFromZero).ShouldBe(0.1563);

        HarnessRounding.Round(0.65625).ShouldBe(0.6562);
        Math.Round(0.65625, 4, MidpointRounding.AwayFromZero).ShouldBe(0.6563);

        // The -0.0 normalisation, which is the clause ActorStats.From rejects without.
        var negativeZero = HarnessRounding.Round(-0.00004);
        negativeZero.ShouldBe(0.0);
        double.IsNegative(negativeZero).ShouldBeFalse();

        // The negative control: without the "+ 0.0" the same input keeps its sign bit.
        double.IsNegative(Math.Round(-0.00004, 4, MidpointRounding.ToEven)).ShouldBeTrue();
    }
}
