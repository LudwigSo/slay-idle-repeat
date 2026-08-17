using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Forge;

/// <summary>What breaking an item down pays: Merge Dust, and a share of the stones its level cost.</summary>
public sealed class GearSalvageTests
{
    /// <summary>
    /// The dust formula, at every band. An unenhanced item is worth its band's base exactly, so a
    /// rule that folded the level bonus in unconditionally would be visible at every row.
    /// </summary>
    /// <param name="rarity">The item's band.</param>
    /// <param name="baseDust">What an unenhanced one of that band is worth.</param>
    [Theory]
    [InlineData(Rarity.C, 10)]
    [InlineData(Rarity.B, 40)]
    [InlineData(Rarity.A, 160)]
    [InlineData(Rarity.S, 640)]
    [InlineData(Rarity.SS, 2560)]
    public void An_unenhanced_item_breaks_down_into_its_bands_base_dust(Rarity rarity, long baseDust)
    {
        GearSalvage.Payout(Inventories.Item("blade", rarity: rarity), Forges.Tuning)
            .Dust.ShouldBe(baseDust);
    }

    /// <summary>
    /// Each level adds fifteen percent of the base. The rows discriminate: at the bottom band a
    /// level is worth 1.5 dust, so the rounding is visible, and at the top band it is worth 384,
    /// where it could not be.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The <c>B</c>/+9 row is the one that catches a bare floor.</b> A share authored at two
    /// decimal places is not exact in binary, so a payout whose real value is the whole number 94
    /// computes as 93.999999999999986 and a floor alone answers 93 — a defect invisible at every
    /// other row here, because the rest either land on a genuine half or come out above the whole
    /// number rather than below it.
    /// </remarks>
    /// <param name="rarity">The item's band.</param>
    /// <param name="level">The level it stands at.</param>
    /// <param name="dust">What it breaks down into.</param>
    [Theory]
    [InlineData(Rarity.C, 1, 11)]
    [InlineData(Rarity.C, 3, 14)]
    [InlineData(Rarity.C, 15, 32)]
    [InlineData(Rarity.B, 9, 94)]
    [InlineData(Rarity.A, 10, 400)]
    [InlineData(Rarity.SS, 15, 8320)]
    public void Each_enhancement_level_adds_a_share_of_the_base_dust(
        Rarity rarity, int level, long dust)
    {
        GearSalvage.Payout(
                Inventories.Item("blade", rarity: rarity, enhanceLevel: level), Forges.Tuning)
            .Dust.ShouldBe(dust);
    }

    /// <summary>
    /// 🔴 The dust rounds DOWN, and the fixture is chosen so the two directions disagree: a bottom
    /// band item at +1 is worth 11.5, which rounds to 12 and floors to 11.
    /// </summary>
    [Fact]
    public void A_fractional_dust_value_rounds_down_rather_than_to_nearest()
    {
        GearSalvage.Dust(baseDust: 10, enhanceLevel: 1, perLevelShare: 0.15).ShouldBe(11);
    }

    /// <summary>An unenhanced item has had nothing spent on it, so there is nothing to refund.</summary>
    [Fact]
    public void An_unenhanced_item_refunds_no_stones()
    {
        GearSalvage.Payout(Inventories.Item("blade"), Forges.Tuning).Stones.ShouldBe(0);
    }

    /// <summary>
    /// The refund is a share of what the item's level cost. The rows are the running totals of the
    /// authored ladder, so a rule that summed one rung too many or too few misses every one.
    /// </summary>
    /// <param name="level">The level the item stands at.</param>
    /// <param name="stones">The stones the refund pays.</param>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 13)]
    [InlineData(10, 85)]
    [InlineData(15, 427)]
    public void The_refund_is_a_share_of_the_stones_the_items_level_cost(int level, long stones)
    {
        GearSalvage.Payout(Inventories.Item("blade", enhanceLevel: level), Forges.Tuning)
            .Stones.ShouldBe(stones);
    }

    /// <summary>
    /// 🔴 The refund rounds DOWN too, and the fixture discriminates: sixty percent of five is three
    /// exactly, of six is 3.6, which floors to 3 and rounds to 4.
    /// </summary>
    [Fact]
    public void A_fractional_refund_rounds_down_rather_than_to_nearest()
    {
        GearSalvage.Stones(invested: 5, refundShare: 0.6).ShouldBe(3);
        GearSalvage.Stones(invested: 6, refundShare: 0.6).ShouldBe(3);
    }

    /// <summary>A share above one would pay back more stones than the item ever cost.</summary>
    [Fact]
    public void A_refund_share_above_one_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => _ = GearSalvage.Stones(invested: 100, refundShare: 1.5));
    }

    /// <summary>
    /// The stones-invested figure counts only what the item's LEVEL cost — failures consume stones
    /// too, and nothing on the item records how many, so this is the figure stored state supports.
    /// </summary>
    [Fact]
    public void The_refund_counts_the_levels_cost_and_not_the_failures_along_the_way()
    {
        var unlucky = Inventories.Item("blade", enhanceLevel: 5, enhanceFailures: 9);
        var lucky = Inventories.Item("blade", enhanceLevel: 5);

        GearSalvage.Payout(unlucky, Forges.Tuning).Stones
            .ShouldBe(GearSalvage.Payout(lucky, Forges.Tuning).Stones);
    }

    /// <summary>The lock does not change what an item is worth — it changes whether it may be salvaged at all.</summary>
    [Fact]
    public void A_locked_item_is_worth_exactly_what_an_unlocked_one_is()
    {
        GearSalvage.Payout(Inventories.Item("blade", locked: true), Forges.Tuning)
            .ShouldBe(GearSalvage.Payout(Inventories.Item("blade"), Forges.Tuning));
    }

    /// <summary>A band the document values nothing for has no payout to answer with.</summary>
    [Fact]
    public void A_band_the_document_does_not_value_is_refused_rather_than_answered()
    {
        var partial = ForgeTuning.Read(Content.ForgeDocuments.With(
            baseDustByRarity: Content.ForgeDocuments.Prices(("C", 10))));

        Should.Throw<InvalidTunableException>(() => _ = partial.SalvageBaseDust(Rarity.SS));
    }
}
