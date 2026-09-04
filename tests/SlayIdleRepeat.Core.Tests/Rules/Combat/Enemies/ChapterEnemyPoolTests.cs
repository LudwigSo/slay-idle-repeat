using Shouldly;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;

/// <summary>One draw by weight from a chapter's pool, over the closed archetype set.</summary>
public sealed class ChapterEnemyPoolTests
{
    private const ulong BattleSeed = 0xC0FFEE_1234_5678UL;

    /// <summary><c>05</c> §6.2's elite power multiplier, which chapters 2-8 carry verbatim.</summary>
    private const double Standard = EnemyFixtures.StandardElitePowerMultiplier;

    [Fact]
    public void One_draw_from_a_pool_consumes_exactly_one_draw_index()
    {
        var rng = new DeterministicRng(BattleSeed, RngStreams.Combat);
        var pool = EnemyFixtures.ChapterOnePool();

        rng.Position.ShouldBe(0UL);
        pool.Draw(rng);
        rng.Position.ShouldBe(1UL);
        pool.Draw(rng);
        rng.Position.ShouldBe(2UL);
    }

    /// <summary>
    /// The draw is a pure function of the seed and the stream — the property PvP fairness and
    /// client/server parity both rest on.
    /// </summary>
    [Fact]
    public void The_same_battle_seed_draws_the_same_sequence()
    {
        var pool = EnemyFixtures.ChapterOnePool();

        var first = Sequence();
        var second = Sequence();

        second.ShouldBe(first);

        IReadOnlyList<EnemyArchetype> Sequence()
        {
            var rng = new DeterministicRng(BattleSeed, RngStreams.Combat);
            return Enumerable.Range(0, 40).Select(_ => pool.Draw(rng)).ToArray();
        }
    }

    /// <summary>A zero weight is never drawn, however many draws are taken.</summary>
    [Fact]
    public void A_zero_weight_archetype_is_never_drawn()
    {
        var rng = new DeterministicRng(BattleSeed, RngStreams.Combat);
        var pool = EnemyFixtures.ChapterOnePool();

        pool.WeightOf(EnemyArchetype.REAVER).ShouldBe(0.0, "05 §6.4 — Chapter 1 has no REAVER");
        pool.WeightOf(EnemyArchetype.LEECH).ShouldBe(0.0, "05 §6.4 — nor any LEECH");

        var drawn = Enumerable.Range(0, 2000).Select(_ => pool.Draw(rng)).ToArray();

        drawn.ShouldNotContain(EnemyArchetype.REAVER);
        drawn.ShouldNotContain(EnemyArchetype.LEECH);
        drawn.Distinct().Count().ShouldBe(6, "the other six shapes all have a positive weight in Chapter 1");
    }

    /// <summary>
    /// The pool is stated over the whole archetype set, so a shape that disappears from a row is
    /// a construction failure rather than an implicit zero.
    /// </summary>
    [Fact]
    public void An_archetype_missing_from_a_row_fails_rather_than_becoming_an_implicit_zero()
    {
        var withoutReaver = new List<ArchetypeWeight>
        {
            new(EnemyArchetype.GRUNT, 40), new(EnemyArchetype.SWARM, 20),
            new(EnemyArchetype.BRUTE, 15), new(EnemyArchetype.SKIRMISHER, 10),
            new(EnemyArchetype.WARDEN, 5), new(EnemyArchetype.CASTER, 5),
            new(EnemyArchetype.LEECH, 5),
        };

        var thrown = Should.Throw<ArgumentException>(
            () => ChapterEnemyPool.From(1, withoutReaver, new List<string> { "EL_A", "EL_B" }, Standard));

        thrown.ParamName.ShouldBe("weights");
        thrown.Message.ShouldContain("REAVER", Case.Sensitive);
        thrown.Message.ShouldContain("design statements, not incidental", Case.Sensitive);
    }

    [Fact]
    public void A_repeated_archetype_in_one_row_is_two_pools_and_fails()
    {
        var doubled = Enum.GetValues<EnemyArchetype>()
            .Select(a => new ArchetypeWeight(a, 10.0))
            .Append(new ArchetypeWeight(EnemyArchetype.GRUNT, 30.0))
            .ToList();

        var thrown = Should.Throw<ArgumentException>(
            () => ChapterEnemyPool.From(1, doubled, new List<string> { "EL_A", "EL_B" }, Standard));

        thrown.ParamName.ShouldBe("weights");
        thrown.Message.ShouldContain("GRUNT", Case.Sensitive);
    }

    /// <summary>
    /// A negative weight is not "never drawn": <c>WeightedPick</c> skips a non-positive row, so a
    /// negative total shifts every other row's share instead.
    /// </summary>
    [Fact]
    public void A_negative_weight_fails_rather_than_reading_as_never_drawn()
    {
        var negative = Enum.GetValues<EnemyArchetype>()
            .Select(a => new ArchetypeWeight(a, a == EnemyArchetype.REAVER ? -5.0 : 15.0))
            .ToList();

        var thrown = Should.Throw<ArgumentException>(
            () => ChapterEnemyPool.From(1, negative, new List<string> { "EL_A", "EL_B" }, Standard));

        thrown.ParamName.ShouldBe("weights");
        thrown.Message.ShouldContain("negative or not finite", Case.Sensitive);
    }

    [Fact]
    public void A_row_of_nothing_but_zeros_can_draw_nothing_and_fails()
    {
        var zeroed = Enum.GetValues<EnemyArchetype>().Select(a => new ArchetypeWeight(a, 0.0)).ToList();

        var thrown = Should.Throw<ArgumentException>(
            () => ChapterEnemyPool.From(1, zeroed, new List<string> { "EL_A", "EL_B" }, Standard));

        thrown.ParamName.ShouldBe("weights");
        thrown.Message.ShouldContain("draw nothing at all", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void A_chapter_outside_05_section_6s_range_fails(int chapter) =>
        Should.Throw<ArgumentOutOfRangeException>(() => ChapterEnemyPool.From(
            chapter,
            Enum.GetValues<EnemyArchetype>().Select(a => new ArchetypeWeight(a, 10.0)).ToList(),
            new List<string> { "EL_A", "EL_B" }, Standard))
            .ParamName.ShouldBe("chapter");

    /// <summary>
    /// Elites come only from the chapter's <c>elitePool</c>, so an empty one is a chapter that can
    /// present no elite and a repeat is one elite drawn twice as often as the other.
    /// </summary>
    [Fact]
    public void An_empty_or_repeating_elite_pool_fails_rather_than_producing_a_chapter_with_no_elite()
    {
        var weights = Enum.GetValues<EnemyArchetype>()
            .Select(a => new ArchetypeWeight(a, 12.5))
            .ToList();

        var empty = Should.Throw<ArgumentException>(
            () => ChapterEnemyPool.From(1, weights, new List<string>(), Standard));
        empty.ParamName.ShouldBe("elitePool");
        empty.Message.ShouldContain("could present none", Case.Sensitive);

        var repeated = Should.Throw<ArgumentException>(() => ChapterEnemyPool.From(
            1, weights, new List<string> { "EL_THORN_SENTINEL", "EL_THORN_SENTINEL" }, Standard));
        repeated.ParamName.ShouldBe("elitePool");
        repeated.Message.ShouldContain("EL_THORN_SENTINEL", Case.Sensitive);
    }

    /// <summary>The row totals 100, and the shipped row is asserted, not a synthetic one.</summary>
    [Fact]
    public void Chapter_ones_weights_total_one_hundred()
    {
        var pool = EnemyFixtures.ChapterOnePool();

        pool.TotalWeight.ShouldBe(100.0);
        pool.Weights.Count.ShouldBe(8, "05 §6.1's eight shapes are the closed set every row is stated over");
        pool.ElitePool.ShouldBe(new[] { "EL_THORN_SENTINEL", "EL_MOSSBACK_ALPHA" });
    }

    /// <summary>
    /// The elite power multiplier is the chapter's own, not one global figure: chapter 1 authors a
    /// gentler one than 05 §6.2's 2.2 because a mini-boss takes the elite power path and chapter 1's
    /// two mini-bosses are unskippable by a hero with no gear.
    /// </summary>
    [Fact]
    public void The_elite_power_multiplier_is_the_chapters_own()
    {
        EnemyFixtures.ChapterOnePool().ElitePowerMultiplier
            .ShouldBe(EnemyFixtures.ChapterOneElitePowerMultiplier);

        ChapterEnemyPool.From(
                2,
                Enum.GetValues<EnemyArchetype>().Select(a => new ArchetypeWeight(a, 12.5)).ToList(),
                new List<string> { "EL_A", "EL_B" },
                Standard)
            .ElitePowerMultiplier.ShouldBe(Standard);
    }

    /// <summary>
    /// A chapter may be authored gentler than 2.2 but never authored away: a zero would field an
    /// elite whose every derived stat is nothing.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.4)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_elite_power_multiplier_that_is_not_positive_and_finite_fails(double multiplier)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => ChapterEnemyPool.From(
            1,
            Enum.GetValues<EnemyArchetype>().Select(a => new ArchetypeWeight(a, 12.5)).ToList(),
            new List<string> { "EL_A", "EL_B" },
            multiplier));

        thrown.ParamName.ShouldBe("elitePowerMultiplier");
        thrown.Message.ShouldContain("positive and finite", Case.Sensitive);
    }
}
