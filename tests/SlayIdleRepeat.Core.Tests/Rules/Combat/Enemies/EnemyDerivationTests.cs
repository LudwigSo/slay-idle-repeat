using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary><c>EnemyStats(power, archetype)</c> — the claim that the derivation is total, checked term by term.</summary>
public sealed class EnemyDerivationTests
{
    /// <summary>The four Power-derived terms, at a Power the arithmetic is legible at.</summary>
    [Fact]
    public void The_four_power_derived_terms_are_05_section_6s_formula()
    {
        var stats = EnemyDerivation.Derive(1000.0, EnemyFixtures.Row(EnemyArchetype.GRUNT), EnemyFixtures.Constants());

        stats[StatId.MAX_HP].ShouldBe(600.0, "05 §6 — 1000 * 0.60 * 1.00");
        stats[StatId.ATK].ShouldBe(45.0, "05 §6 — 1000 * 0.045 * 1.00");
        stats[StatId.DEF].ShouldBe(30.0, "05 §6 — 1000 * 0.030 * 1.00");
        stats[StatId.ASPD].ShouldBe(1.0, "05 §6 — 1.00 * 1.00");
    }

    /// <summary>
    /// The four secondaries are the archetype's own values, not scaled by Power — which is what
    /// makes a <c>REAVER</c> a crit spiker at every chapter rather than only at the last.
    /// </summary>
    [Fact]
    public void The_four_secondaries_are_taken_from_the_row_and_never_scaled_by_power()
    {
        var reaver = EnemyFixtures.Row(EnemyArchetype.REAVER);

        var weak = EnemyDerivation.Derive(1000.0, reaver, EnemyFixtures.Constants());
        var strong = EnemyDerivation.Derive(128000.0, reaver, EnemyFixtures.Constants());

        foreach (var stat in new[] { StatId.CRIT, StatId.CDMG, StatId.DODGE, StatId.LIFESTEAL })
        {
            strong[stat].ShouldBe(weak[stat], $"05 §6.1 states {stat} as the stat, not as a coefficient");
        }

        weak[StatId.CRIT].ShouldBe(0.30, "05 §6.1 — REAVER crit, preserved exactly from prose");
        weak[StatId.CDMG].ShouldBe(1.20, "05 §6.1 — REAVER critDamage, preserved exactly from prose");
    }

    /// <summary>The six invariant stats, including the one that matters: <c>HEAL%</c> is 1.0, and every heal is multiplied by it.</summary>
    [Fact]
    public void The_six_stats_05_section_6_fixes_are_the_same_for_every_archetype()
    {
        foreach (var row in EnemyFixtures.Archetypes)
        {
            var stats = EnemyDerivation.Derive(4000.0, row, EnemyFixtures.Constants());

            stats[StatId.BLOCK].ShouldBe(0.0, $"05 §6 — BLOCK is 0.00 for {row.Id}");
            stats[StatId.PEN].ShouldBe(0.0, $"05 §6 — PEN is 0.00 for {row.Id}");
            stats[StatId.DMG_PCT].ShouldBe(1.0, $"05 §6 / 16 D46 — DMG% is the bare multiplier identity 1.00 for {row.Id}");
            stats[StatId.DR_PCT].ShouldBe(1.0, $"05 §6 / 16 D46 — DR% is the damage-taken identity 1.00 for {row.Id}");
            stats[StatId.HEAL_PCT].ShouldBe(1.0, $"05 §6 — HEAL% is 1.00 for {row.Id}");
            stats[StatId.THORNS].ShouldBe(0.0, $"05 §6 — THORN is 0.00 for {row.Id}");
        }

        EnemyFixtures.Archetypes.Count.ShouldBe(8, "05 §6.1's table has eight rows and this loop must cover them");
    }

    /// <summary>A fixed stat that disappeared from the data is a failure, not a zero.</summary>
    /// <remarks>
    /// The <c>ParamName</c> is what pins which rule fired: <see cref="ActorStats.From"/>'s own
    /// missing-stat message carries the same stat name and the same sentence, so deleting
    /// <see cref="EnemyDerivation"/>'s guard entirely would leave this green on <c>ActorStats</c>'s
    /// throw. The document path appears in this message only.
    /// </remarks>
    [Fact]
    public void A_fixed_stat_missing_from_the_data_fails_rather_than_defaulting_to_zero()
    {
        var thrown = Should.Throw<ArgumentException>(() => EnemyDerivation.Derive(
            1000.0,
            EnemyFixtures.Row(EnemyArchetype.GRUNT),
            EnemyFixtures.ConstantsWithout(StatId.HEAL_PCT)));

        thrown.ParamName.ShouldBe("constants", "ActorStats.From's own guard throws nameof(values)");
        thrown.Message.ShouldContain("content/enemies/enemies.json", Case.Sensitive);
        thrown.Message.ShouldContain("HEAL_PCT", Case.Sensitive);
        thrown.Message.ShouldContain("an unstated stat is a bug, not a zero", Case.Sensitive);
    }

    /// <summary>Every term is rounded to 4 dp. A block whose terms are not rounded cannot be built at all — <see cref="ActorStats"/> refuses it.</summary>
    /// <remarks>
    /// The Power is chosen so that <c>power × 0.045 × 0.85</c> has a fifth decimal place:
    /// <c>1234.5678 × 0.045 × 0.85 = 47.2209...</c>, which rounds and would not otherwise be
    /// representable as a 4-dp value.
    /// </remarks>
    [Fact]
    public void Every_derived_term_is_rounded_to_four_decimal_places()
    {
        var stats = EnemyDerivation.Derive(
            1234.5678, EnemyFixtures.Row(EnemyArchetype.SKIRMISHER), EnemyFixtures.Constants());

        // The loop below cannot fail on its own, and that is recorded rather than pretended
        // otherwise: ActorStats.From already refuses an unrounded value, so a derivation that
        // stopped rounding would throw out of Derive rather than reach here. It is kept as the
        // statement of the claim, floored so it cannot also quantify over nothing.
        stats.Values.Count().ShouldBe(14, "05 §1's actor block is fourteen stats wide");

        foreach (var (stat, value) in stats.Values)
        {
            StatRounding.IsRounded(value).ShouldBeTrue($"05 §6 rounds every term to 4 dp, and {stat} is {value}");
        }

        stats[StatId.ATK].ShouldBe(Math.Round(1234.5678 * 0.045 * 0.85, 4));
        stats[StatId.ATK].ShouldNotBe(1234.5678 * 0.045 * 0.85, "the unrounded product would defeat the point");
    }

    /// <summary><c>Elite = base archetype x 2.2 power</c>, and the elite multiplier is the only thing it multiplies by.</summary>
    [Fact]
    public void An_elite_is_its_base_archetypes_statline_at_2_2_power()
    {
        var brute = EnemyFixtures.Row(EnemyArchetype.BRUTE);

        var elitePower = EnemyDerivation.ElitePower(1000.0, 2.2);
        elitePower.ShouldBe(2200.0);

        var normal = EnemyDerivation.Derive(1000.0, brute, EnemyFixtures.Constants());
        var elite = EnemyDerivation.Derive(elitePower, brute, EnemyFixtures.Constants());

        // Literals, not `normal[stat] * 2.2`. The production order is round-the-power-then-derive
        // and the derived-then-multiplied order agrees only because BRUTE's coefficients happen to
        // be exact; asserting the second order would drift from the first for an archetype whose
        // product has a fifth decimal place, with no bug present.
        elite[StatId.MAX_HP].ShouldBe(2640.0, "2200 * 0.60 * 2.00");
        elite[StatId.ATK].ShouldBe(133.65, "2200 * 0.045 * 1.35");
        elite[StatId.DEF].ShouldBe(79.2, "2200 * 0.030 * 1.20");

        elite[StatId.MAX_HP].ShouldBeGreaterThan(normal[StatId.MAX_HP]);

        elite[StatId.ASPD].ShouldBe(normal[StatId.ASPD], "05 §6 derives ASPD from the archetype, not from Power");
        elite[StatId.CRIT].ShouldBe(normal[StatId.CRIT], "05 §6.2 multiplies POWER, not the secondaries");
    }

    /// <summary>
    /// A boss's Power already includes its stage multiplier and must not be multiplied again. This
    /// is stated as an absence: the derivation takes Power as a parameter and has no stage term to
    /// apply.
    /// </summary>
    [Fact]
    public void The_derivation_takes_power_as_given_and_has_no_second_stage_multiplier()
    {
        var row = EnemyFixtures.Row(EnemyArchetype.BRUTE);
        var bossPower = 1000.0 * 2.20;

        var derived = EnemyDerivation.Derive(bossPower, row, EnemyFixtures.Constants());

        derived[StatId.MAX_HP].ShouldBe(Math.Round(bossPower * 0.60 * row.HpCoef, 4),
            "05 §6.3 — the 2.20 is already in EnemyPower(i); the derivation applies no stage term of its own");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1.0)]
    public void A_power_that_is_not_a_non_negative_finite_number_fails_at_the_derivation(double power)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => EnemyDerivation.Derive(
            power, EnemyFixtures.Row(EnemyArchetype.GRUNT), EnemyFixtures.Constants()));

        thrown.ParamName.ShouldBe("power");
        thrown.Message.ShouldContain("EnemyPower", Case.Sensitive);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.2)]
    [InlineData(double.NaN)]
    public void An_elite_power_multiplier_that_is_not_positive_and_finite_fails(double multiplier)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EnemyDerivation.ElitePower(1000.0, multiplier));

        thrown.ParamName.ShouldBe("powerMultiplier");
    }
}
