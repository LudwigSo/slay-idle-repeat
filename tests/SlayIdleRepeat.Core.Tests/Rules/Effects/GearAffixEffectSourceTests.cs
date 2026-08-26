using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects;

/// <summary>
/// The <c>AFFIXES</c> source's null-pair branch: a rolled affix whose pool row authors no stat and
/// no op contributes nothing and breaks nothing.
/// </summary>
/// <remarks>
/// The shipped pool no longer carries a null pair — the damage-vs-Elites affix was its one until
/// the conditional bucket made it authorable — so this branch would otherwise be exercised by no
/// content at all while the gear gap register's data arm still depends on it. Synthetic pool, so
/// the pin survives the shipped pool changing again.
/// </remarks>
public sealed class GearAffixEffectSourceTests
{
    /// <summary>A roll of a null-pair affix is skipped by the source, not thrown on and not invented.</summary>
    [Fact]
    public void A_rolled_null_pair_affix_contributes_no_effect()
    {
        var pool = ContentValue.Array(
            GearDocuments.ShippedAffixes
                .Select(GearDocuments.AffixRow)
                .Append(GearDocuments.AffixRow(
                    new AuthoredAffix("AFX_TEST_NULL_PAIR", null, null, 0.1m, 0.2m, ["RING"], null))));

        var drops = DropsTuning.Read(GearDocuments.With(affixes: pool));

        var ring = Inventories.Item(
            "ring", GearFamily.BAND, Rarity.S, affixes: [new GearAffixRoll("AFX_TEST_NULL_PAIR", 0.15)]);

        new GearAffixEffectSource(drops, [ring]).Effects.ShouldBeEmpty(
            "the pool authors neither a stat nor an op for the roll, so there is nothing to " +
            "synthesise — and dropping it silently HERE is correct because the pool already said " +
            "so loudly");
    }

    /// <summary>The control: a stat-writing roll from the same pool synthesises its one effect.</summary>
    [Fact]
    public void A_stat_writing_roll_synthesises_exactly_one_effect()
    {
        var drops = DropsTuning.Read(GearDocuments.Shipped);

        var ring = Inventories.Item(
            "ring", GearFamily.BAND, Rarity.S, affixes: [new GearAffixRoll("AFX_CRIT_CHANCE", 0.05)]);

        var effect = new GearAffixEffectSource(drops, [ring]).Effects.ShouldHaveSingleItem().Effect;

        effect.Op.ShouldBe(EffectOp.STAT_ADD_FLAT);
        effect.Value.ShouldBe(0.05);
    }
}
