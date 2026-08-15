using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary><c>EnemyLevel(c, t) = BaseEnemyLevel(c) + TierLevelBonus(t)</c>.</summary>
public sealed class EnemyLevelTableTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 15)]
    [InlineData(3, 20)]
    [InlineData(4, 30)]
    [InlineData(5, 40)]
    [InlineData(6, 50)]
    [InlineData(7, 60)]
    [InlineData(8, 80)]
    public void BaseEnemyLevel_is_05_section_6_0s_chapter_table(int chapter, int expected) =>
        EnemyFixtures.Levels().BaseLevel(chapter).ShouldBe(expected);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    public void TierLevelBonus_is_05_section_6_0s_tier_table(int tierOrdinal, int expected) =>
        EnemyFixtures.Levels().TierBonus(tierOrdinal).ShouldBe(expected);

    /// <summary>The two tables composed, at the corners the design actually ships.</summary>
    [Theory]
    [InlineData(1, 0, 10)]
    [InlineData(1, 2, 30)]
    [InlineData(8, 0, 80)]
    [InlineData(8, 2, 100)]
    [InlineData(5, 1, 50)]
    public void EnemyLevel_is_the_sum_of_the_two_tables(int chapter, int tierOrdinal, int expected) =>
        EnemyFixtures.Levels().Of(chapter, tierOrdinal).ShouldBe(expected);

    /// <summary>
    /// All enemies, Elites, Guardians and bosses in a (chapter, tier) share this level — there is
    /// one table and nothing takes an archetype.
    /// </summary>
    [Fact]
    public void The_level_depends_on_the_chapter_and_the_tier_and_on_nothing_else()
    {
        typeof(EnemyLevelTable)
            .GetMethod("Of", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ShouldBe(new[] { typeof(int), typeof(int) },
                "05 §6.0: all enemies, Elites, Guardians and bosses in a (chapter, tier) share this " +
                "level. Two ints in, one int out — an archetype or an elite flag that could change " +
                "the answer would have to be a parameter, and adding one is what this pins.");

        EnemyFixtures.Levels().Of(3, 1).ShouldBe(30, "05 §6.0 — Ch3 base 20 plus Heroic +10");
    }

    [Fact]
    public void A_chapter_with_no_authored_base_level_is_a_construction_failure()
    {
        var incomplete = new Dictionary<int, int> { [1] = 10, [2] = 15 };

        var thrown = Should.Throw<ArgumentException>(
            () => EnemyLevelTable.From(incomplete, new List<int> { 0, 10, 20 }));

        thrown.ParamName.ShouldBe("baseByChapter");
        thrown.Message.ShouldContain("3, 4, 5, 6, 7, 8", Case.Sensitive);
    }

    [Fact]
    public void A_ninth_chapter_is_a_construction_failure_rather_than_a_silently_extra_row()
    {
        var extra = Enumerable.Range(1, 9).ToDictionary(c => c, c => c * 10);

        var thrown = Should.Throw<ArgumentException>(
            () => EnemyLevelTable.From(extra, new List<int> { 0, 10, 20 }));

        thrown.ParamName.ShouldBe("baseByChapter");
        thrown.Message.ShouldContain("A ninth chapter is a design decision", Case.Sensitive);
    }

    [Fact]
    public void A_tier_bonus_row_that_is_lost_is_a_construction_failure()
    {
        var thrown = Should.Throw<ArgumentException>(() => EnemyLevelTable.From(
            Enumerable.Range(1, 8).ToDictionary(c => c, _ => 10),
            new List<int> { 0, 10 }));

        thrown.ParamName.ShouldBe("tierBonus");
        thrown.Message.ShouldContain("NORMAL, HEROIC, MYTHIC", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void A_chapter_outside_05_section_6s_range_fails_rather_than_returning_a_level(int chapter) =>
        Should.Throw<ArgumentOutOfRangeException>(() => EnemyFixtures.Levels().Of(chapter, 0))
              .ParamName.ShouldBe("chapter");

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_tier_ordinal_outside_the_authored_three_fails(int tierOrdinal) =>
        Should.Throw<ArgumentOutOfRangeException>(() => EnemyFixtures.Levels().Of(1, tierOrdinal))
              .ParamName.ShouldBe("tierOrdinal");

    /// <summary>
    /// The floor under the tier vocabulary: every rule above indexes
    /// <see cref="EnemyLevelTable.Ordinals"/>; an empty or shortened list would make the ordinal
    /// tests quantify over nothing while still passing.
    /// </summary>
    [Fact]
    public void The_three_tier_names_are_the_ones_05_section_6_0_lists()
    {
        EnemyLevelTable.Ordinals.ShouldBe(new[] { "NORMAL", "HEROIC", "MYTHIC" });

        EnemyLevelTable.FirstChapter.ShouldBe(1);
        EnemyLevelTable.LastChapter.ShouldBe(8);
    }
}
