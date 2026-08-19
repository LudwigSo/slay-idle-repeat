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
