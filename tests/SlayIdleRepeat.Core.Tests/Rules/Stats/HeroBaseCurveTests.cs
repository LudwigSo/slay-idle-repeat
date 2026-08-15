using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>The hero base stat curve.</summary>
/// <remarks>
/// Numbers come from <see cref="StatFixtures.HeroCurve"/>, which restates what
/// <c>game-data/content/combat_caps.json</c> holds; agreement with the shipped file itself is
/// asserted in <c>SlayIdleRepeat.Application.Tests.Content.CombatCapsDataTests</c>.
/// </remarks>
public sealed class HeroBaseCurveTests
{
    /// <summary>The three level-scaled curves, at both ends of the authored range and the middle.</summary>
    [Theory]
    [InlineData(1, 295.0, 36.0, 18.0)]
    [InlineData(60, 2950.0, 390.0, 195.0)]
    [InlineData(200, 9250.0, 1230.0, 615.0)]
    public void The_three_level_scaled_stats_follow_05_section_2(int level, double maxHp, double atk, double def)
    {
        var stats = StatFixtures.HeroCurve().At(level);

        stats[StatId.MAX_HP].ShouldBe(maxHp, $"MaxHP = 250 + 45 * {level}");
        stats[StatId.ATK].ShouldBe(atk, $"ATK = 30 + 6 * {level}");
        stats[StatId.DEF].ShouldBe(def, $"DEF = 15 + 3 * {level}");
    }

    /// <summary>
    /// Every level-independent constant, checked at two different levels — a stat that had
    /// accidentally picked up a per-level term would be invisible at one.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void The_eleven_level_independent_stats_are_the_constants_05_section_2_states(int level)
    {
        var stats = StatFixtures.HeroCurve().At(level);

        stats[StatId.ASPD].ShouldBe(1.00, "ASPD = 1.00, attacks per second");
        stats[StatId.CRIT].ShouldBe(0.05, "CRIT = 0.05");
        stats[StatId.CDMG].ShouldBe(0.50, "CDMG = 0.50, i.e. a x1.5 crit");
        stats[StatId.LIFESTEAL].ShouldBe(0.00, "LS = 0.00");
        stats[StatId.DODGE].ShouldBe(0.02, "DODGE = 0.02");
        stats[StatId.BLOCK].ShouldBe(0.00, "BLOCK = 0.00");
        stats[StatId.PEN].ShouldBe(0.00, "PEN = 0.00");
        stats[StatId.DMG_PCT].ShouldBe(0.00, "DMG% = 0.00");
        stats[StatId.DR_PCT].ShouldBe(0.00, "DR% = 0.00");
        stats[StatId.THORNS].ShouldBe(0.00, "THORN = 0.00");
    }

    /// <summary>
    /// The one row that defaults to a multiplier rather than zero: HEAL_PCT = 1.00, so lifesteal
    /// and heals apply with no modifier.
    /// </summary>
    /// <remarks>
    /// A silent 0 here would not fail loudly: every heal, lifesteal tick and REGEN in the game
    /// would quietly do nothing while the suite stayed green. It's the one stat where "unstated
    /// means zero" would be worse than a crash.
    /// </remarks>
    [Fact]
    public void HEAL_PCT_starts_at_one_because_a_zero_would_disable_every_heal_in_the_game()
    {
        StatFixtures.HeroCurve().At(1)[StatId.HEAL_PCT].ShouldBe(1.00);
        StatFixtures.HeroCurve().At(200)[StatId.HEAL_PCT].ShouldBe(1.00);

        StatFixtures.HeroCurve().At(1)[StatId.HEAL_PCT].ShouldNotBe(0.0);
    }

    /// <summary>
    /// The defaults read back as a set rather than stat-by-stat: exactly one stat is 1.0, three
    /// scale with level, and the remaining ten are zero at level 1.
    /// </summary>
    /// <remarks>
    /// Stated as a shape so a stat picking up a wrong default fails here even if the per-stat
    /// cases above were edited to match it — nothing in <see cref="HeroBaseCurve"/> otherwise
    /// constrains what the fourteen values are, only that there are fourteen of them.
    /// </remarks>
    [Fact]
    public void At_level_one_exactly_one_stat_defaults_to_a_multiplier_and_ten_default_to_zero()
    {
        var stats = StatFixtures.HeroCurve().At(1);

        stats.Values.Where(v => v.Value == 0.0).Select(v => v.Key).ShouldBe(
            [
                StatId.LIFESTEAL, StatId.BLOCK, StatId.PEN,
                StatId.DMG_PCT, StatId.DR_PCT, StatId.THORNS,
            ],
            ignoreOrder: true);

        stats.Values.Where(v => v.Value == 1.0).Select(v => v.Key).ShouldBe(
            [StatId.ASPD, StatId.HEAL_PCT],
            ignoreOrder: true,
            "05 §2's only two unit defaults: one attack per second, and a x1 healing multiplier");

        stats.Values.Count().ShouldBe(14);
    }

    [Fact]
    public void A_curve_missing_a_combat_stat_is_refused()
    {
        foreach (var omitted in StatIds.Combat)
        {
            var rows = StatIds.Combat
                .Where(s => s != omitted)
                .ToDictionary(s => s, _ => (0.0, 0.0));

            Should.Throw<ArgumentException>(() => HeroBaseCurve.From(rows, 1, 200))
                  .Message.ShouldContain(omitted.ToString(), Case.Sensitive);
        }
    }

    [Fact]
    public void A_curve_naming_a_non_combat_stat_is_refused()
    {
        var rows = StatIds.Combat.ToDictionary(s => s, _ => (0.0, 0.0));
        rows[StatId.GOLD_PCT] = (0.0, 0.0);

        Should.Throw<ArgumentException>(() => HeroBaseCurve.From(rows, 1, 200))
              .Message.ShouldContain("GOLD_PCT", Case.Sensitive);
    }

    /// <summary>
    /// Enforced, not clamped: the authored range is 1..200, and answering for level 0 or 201
    /// would be inventing a number.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void A_level_outside_05_section_2s_range_throws_rather_than_clamping(int level)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => StatFixtures.HeroCurve().At(level));

        thrown.Message.ShouldContain("05 §2", Case.Sensitive);
        thrown.Message.ShouldContain("Clamping", Case.Sensitive);
    }

    [Fact]
    public void The_authored_range_is_the_one_05_section_2_states()
    {
        StatFixtures.HeroCurve().MinimumLevel.ShouldBe(1);
        StatFixtures.HeroCurve().MaximumLevel.ShouldBe(200);
    }

    /// <summary>The curve is an accumulation point, so it rounds.</summary>
    [Fact]
    public void The_curve_rounds_at_four_decimal_places()
    {
        var rows = StatIds.Combat.ToDictionary(s => s, _ => (0.0, 0.0));
        rows[StatId.ATK] = (0.0, 0.000_012_5);

        var curve = HeroBaseCurve.From(rows, 1, 200);

        curve.At(1)[StatId.ATK].ShouldBe(0.0, "0.0000125 rounds to 0.0000 at 4 dp");
        curve.At(8)[StatId.ATK].ShouldBe(0.0001, "0.0001 exactly");
    }

    [Fact]
    public void A_curve_with_a_non_finite_coefficient_is_refused()
    {
        var rows = StatIds.Combat.ToDictionary(s => s, _ => (0.0, 0.0));
        rows[StatId.ATK] = (double.PositiveInfinity, 0.0);

        Should.Throw<ArgumentException>(() => HeroBaseCurve.From(rows, 1, 200));
    }

    [Fact]
    public void A_level_range_that_is_not_a_range_is_refused()
    {
        var rows = StatIds.Combat.ToDictionary(s => s, _ => (0.0, 0.0));

        Should.Throw<ArgumentException>(() => HeroBaseCurve.From(rows, 0, 200));
        Should.Throw<ArgumentException>(() => HeroBaseCurve.From(rows, 200, 1));
    }
}
