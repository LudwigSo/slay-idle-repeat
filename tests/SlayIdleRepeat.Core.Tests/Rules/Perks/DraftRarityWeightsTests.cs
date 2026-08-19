using System.Collections.Generic;
using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Rules.Perks;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Perks;

/// <summary>
/// <c>DraftRarityWeights.For</c>, the <c>RarityWeights(stage, isElite, isBoss)</c> table. Internal
/// seam by necessity: a draft exposes a weight only as a rate across many seeds, so no public entry
/// point can pin the 06 §4 numbers exactly.
/// </summary>
public sealed class DraftRarityWeightsTests
{
    private static double WeightOf(PerkRarity rarity, IReadOnlyList<(PerkRarity Rarity, double Weight)> table) =>
        table.Single(r => r.Rarity == rarity).Weight;

    // ------------------------------------------------------------------ the three stage tables, verbatim

    [Fact]
    public void Stage_1_normal_matches_06_SS4_exactly()
    {
        var table = DraftRarityWeights.For(stage: 1, isElite: false, isBoss: false);

        table.Count.ShouldBe(4);
        WeightOf(PerkRarity.Common, table).ShouldBe(62.0);
        WeightOf(PerkRarity.Rare, table).ShouldBe(30.0);
        WeightOf(PerkRarity.Epic, table).ShouldBe(7.0);
        WeightOf(PerkRarity.Legendary, table).ShouldBe(1.0);
    }

    [Fact]
    public void Stage_2_normal_matches_06_SS4_exactly()
    {
        var table = DraftRarityWeights.For(stage: 2, isElite: false, isBoss: false);

        WeightOf(PerkRarity.Common, table).ShouldBe(48.0);
        WeightOf(PerkRarity.Rare, table).ShouldBe(36.0);
        WeightOf(PerkRarity.Epic, table).ShouldBe(13.0);
        WeightOf(PerkRarity.Legendary, table).ShouldBe(3.0);
    }

    [Fact]
    public void Stage_3_normal_matches_06_SS4_exactly()
    {
        var table = DraftRarityWeights.For(stage: 3, isElite: false, isBoss: false);

        WeightOf(PerkRarity.Common, table).ShouldBe(34.0);
        WeightOf(PerkRarity.Rare, table).ShouldBe(40.0);
        WeightOf(PerkRarity.Epic, table).ShouldBe(20.0);
        WeightOf(PerkRarity.Legendary, table).ShouldBe(6.0);
    }

    // ------------------------------------------------------------------ the elite shift

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Elite_halves_Common_and_doubles_Legendary_leaving_Rare_and_Epic_at_the_stage_base(int stage)
    {
        var normal = DraftRarityWeights.For(stage, isElite: false, isBoss: false);
        var elite = DraftRarityWeights.For(stage, isElite: true, isBoss: false);

        WeightOf(PerkRarity.Common, elite).ShouldBe(WeightOf(PerkRarity.Common, normal) / 2.0);
        WeightOf(PerkRarity.Legendary, elite).ShouldBe(WeightOf(PerkRarity.Legendary, normal) * 2.0);
        WeightOf(PerkRarity.Rare, elite).ShouldBe(WeightOf(PerkRarity.Rare, normal));
        WeightOf(PerkRarity.Epic, elite).ShouldBe(WeightOf(PerkRarity.Epic, normal));
    }

    // ------------------------------------------------------------------ the boss table

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Boss_is_Epic_55_Legendary_45_with_no_Common_or_Rare_regardless_of_stage(int stage)
    {
        var table = DraftRarityWeights.For(stage, isElite: false, isBoss: true);

        table.Count.ShouldBe(2, "no Commons or Rares — a boss table with four rows would let a boss draft a Common");
        WeightOf(PerkRarity.Epic, table).ShouldBe(55.0);
        WeightOf(PerkRarity.Legendary, table).ShouldBe(45.0);
    }

    [Fact]
    public void Boss_ignores_isElite_and_stage_alike()
    {
        var plainBoss = DraftRarityWeights.For(stage: 1, isElite: false, isBoss: true);
        var eliteBoss = DraftRarityWeights.For(stage: 3, isElite: true, isBoss: true);

        plainBoss.ShouldBe(eliteBoss);
    }

    // ------------------------------------------------------------------ the guard, mutated on purpose (S1)

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void A_stage_outside_1_2_3_is_refused_when_not_a_boss_draw(int stage)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DraftRarityWeights.For(stage, isElite: false, isBoss: false));
    }
}
