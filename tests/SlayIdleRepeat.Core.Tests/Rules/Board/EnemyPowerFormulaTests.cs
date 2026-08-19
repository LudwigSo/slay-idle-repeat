using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Board;

/// <summary>
/// Tests <see cref="EnemyPowerFormula"/>:
/// <c>ChapterPowerTarget(c) · TierMult(t) · (1 + 0.035·i) · StageMult(s)</c>.
/// </summary>
/// <remarks>
/// Internal seam by necessity: the formula's product reaches a caller only folded into a fight's
/// enemy stats, so no command or simulator output can isolate one term. Every case below fixes
/// three of the four inputs and moves one, so a failure names which term broke. The chapter power
/// target is 1000 throughout — an arbitrary round test value, since <c>ChapterPowerTarget(c)</c>
/// is authored nowhere and is a parameter for exactly that reason.
/// </remarks>
public sealed class EnemyPowerFormulaTests
{
    private const double Target = 1000.0;

    /// <summary>The boss belongs to no stage and is carried as this value.</summary>
    private const int BossStage = BoardGraph.BossStage;

    /// <summary>The four stage multipliers, at the node where every other term is 1.</summary>
    /// <remarks>
    /// Linear index 0 makes the growth term exactly <c>1 + 0.035·0 = 1</c>, and NORMAL makes the tier
    /// term exactly 1 — so the answer IS the stage multiplier times the target, and nothing else can
    /// be hiding in it.
    /// </remarks>
    [Theory]
    [InlineData(1, 1000.0)]
    [InlineData(2, 1150.0)]
    [InlineData(3, 1350.0)]
    [InlineData(BossStage, 2200.0)]
    public void Each_stage_multiplier_is_02_section_4_3s(int stage, double expected)
    {
        EnemyPowerFormula.Compute(Target, DifficultyTier.NORMAL, linearIndex: 0, stage)
            .ShouldBe(expected, tolerance: 1e-9);
    }

    /// <summary>The three tier multipliers, at the node where every other term is 1.</summary>
    [Theory]
    [InlineData(DifficultyTier.NORMAL, 1000.0)]
    [InlineData(DifficultyTier.HEROIC, 4000.0)]
    [InlineData(DifficultyTier.MYTHIC, 16000.0)]
    public void Each_tier_multiplier_is_02_section_4_3s(DifficultyTier tier, double expected)
    {
        EnemyPowerFormula.Compute(Target, tier, linearIndex: 0, stage: 1)
            .ShouldBe(expected, tolerance: 1e-9);
    }

    /// <summary>The linear-index term moves the answer, and moves it by exactly <c>0.035</c> per node.</summary>
    /// <remarks>
    /// This is why it's a <c>[Theory]</c> over several indices rather than one case: a formula that
    /// ignored the index entirely would still satisfy a suite that only checked index 0.
    /// </remarks>
    [Theory]
    [InlineData(0, 1000.0)]
    [InlineData(1, 1035.0)]
    [InlineData(10, 1350.0)]
    [InlineData(42, 2470.0)]
    public void The_linear_index_term_grows_by_three_and_a_half_percent_per_node(
        int linearIndex, double expected)
    {
        EnemyPowerFormula.Compute(Target, DifficultyTier.NORMAL, linearIndex, stage: 1)
            .ShouldBe(expected, tolerance: 1e-9);
    }

    /// <summary>Stated as a relation rather than a table: a later node is always strictly stronger.</summary>
    [Fact]
    public void A_later_node_is_strictly_stronger_than_an_earlier_one()
    {
        var early = EnemyPowerFormula.Compute(Target, DifficultyTier.MYTHIC, linearIndex: 3, stage: 2);
        var late = EnemyPowerFormula.Compute(Target, DifficultyTier.MYTHIC, linearIndex: 4, stage: 2);

        late.ShouldBeGreaterThan(early);
    }

    /// <summary>All four terms compose — the boss of a Mythic run at the last spine index.</summary>
    /// <remarks>
    /// Written as the product of the four named factors rather than as a single literal, so that a
    /// failure says which factor moved instead of "2470 became 2469".
    /// </remarks>
    [Fact]
    public void The_four_terms_compose()
    {
        const double tierMult = 16.0;
        const double stageMult = 2.20;
        const double growth = 1.0 + (0.035 * 42);

        EnemyPowerFormula.Compute(Target, DifficultyTier.MYTHIC, linearIndex: 42, stage: BossStage)
            .ShouldBe(Target * tierMult * growth * stageMult, tolerance: 1e-9);
    }

    /// <summary>Nothing is rounded — the formula is a pure double and callers round if they need to.</summary>
    [Fact]
    public void The_result_is_not_rounded()
    {
        EnemyPowerFormula.Compute(1.0, DifficultyTier.NORMAL, linearIndex: 1, stage: 2)
            .ShouldBe(1.035 * 1.15, tolerance: 1e-12);
    }

    /// <summary>Linear index runs from 0 upwards.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_linear_index_is_refused(int linearIndex)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            EnemyPowerFormula.Compute(Target, DifficultyTier.NORMAL, linearIndex, stage: 1));
    }

    /// <summary>Only three stages plus the boss are valid.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void A_stage_outside_the_four_is_refused(int stage)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            EnemyPowerFormula.Compute(Target, DifficultyTier.NORMAL, linearIndex: 0, stage));
    }

    /// <summary>
    /// <see cref="DifficultyTier"/> has no zero member, so an uninitialised field is refused
    /// rather than silently treated as NORMAL.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void An_undefined_tier_is_refused(int tier)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            EnemyPowerFormula.Compute(Target, (DifficultyTier)tier, linearIndex: 0, stage: 1));
    }

    /// <summary>…and the negative control: index 0 and each of the four stages ARE accepted.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(BossStage)]
    public void Every_authored_stage_is_accepted(int stage)
    {
        Should.NotThrow(() =>
            EnemyPowerFormula.Compute(Target, DifficultyTier.NORMAL, linearIndex: 0, stage));
    }
}
