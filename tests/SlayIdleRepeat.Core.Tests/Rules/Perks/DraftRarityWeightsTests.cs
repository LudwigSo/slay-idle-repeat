using System.Collections.Generic;
using System.Linq;
using Shouldly;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Rules.Board;
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

    // ------------------------------------------------------------------ the epic+ table, keyed on the mini-boss

    /// <summary>
    /// The epic+ band is the mini-boss's, at both of the stages a mini-boss stands on, and it holds
    /// two rows: a table with four would let a mini-boss draft a Common.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_miniboss_draws_Epic_60_and_Legendary_40_with_no_Common_or_Rare(int stage)
    {
        var table = DraftRarityWeights.For(stage, TileKind.MiniBoss);

        table.Count.ShouldBe(2);
        WeightOf(PerkRarity.Epic, table).ShouldBe(60.0);
        WeightOf(PerkRarity.Legendary, table).ShouldBe(40.0);
    }

    /// <summary>
    /// A boss opens no draft at all, so there is no boss table to fall through to. Refused rather
    /// than answered with a stage table: a silent fallthrough would hand a boss kill a Common-heavy
    /// offer the moment some caller asked for one again.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_boss_battle_has_no_table_at_all(int stage)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DraftRarityWeights.For(stage, TileKind.Boss));
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
