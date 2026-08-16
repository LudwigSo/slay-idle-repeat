using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// The scalar every rolled item's stats are a multiple of: the chapter's par power, times the
/// authored fraction one item carries, times the rarity band's multiplier.
/// </summary>
/// <remarks>
/// Three numbers and a product. Both inputs are authored and neither is restated in the rules layer,
/// so the cases below read them back out of the tuning rather than spelling them — a constant here
/// would be a second, frozen answer that no re-tune could reach.
/// </remarks>
public sealed class ItemPowerTests
{
    /// <summary>The product of the three factors, with nothing else folded in.</summary>
    [Theory]
    [InlineData(1000.0, 0.1, 1.0, 100.0)]
    [InlineData(2000.0, 0.5, 2.0, 2000.0)]
    [InlineData(1.0, 1.0, 1.0, 1.0)]
    public void Item_power_is_the_product_of_its_three_factors(
        double par, double coefficient, double multiplier, double expected)
    {
        DeterminismRounding.Round(ItemPower.For(par, coefficient, multiplier)).ShouldBe(expected);
    }

    /// <summary>Real cells, driven from the authored tables rather than from literals.</summary>
    /// <remarks>
    /// The expected figures are the only literals: a case that computed both sides from the tuning
    /// would agree with itself whatever the tuning said.
    /// </remarks>
    [Theory]
    [InlineData(1, Rarity.C, 100.0)]
    [InlineData(1, Rarity.SS, 600.0)]
    [InlineData(5, Rarity.S, 6080.0)]
    [InlineData(8, Rarity.A, 30720.0)]
    public void Item_power_at_an_authored_chapter_and_band(int chapter, Rarity rarity, double expected)
    {
        var par = ParPowerTuning.Read(GearDocuments.Shipped);
        var drops = DropsTuning.Read(GearDocuments.Shipped);

        DeterminismRounding.Round(ItemPower.For(
                par.ChapterPowerTarget(chapter),
                drops.ItemPowerCoefficient,
                drops.Band(rarity).StatMultiplier))
            .ShouldBe(
                expected,
                $"chapter {chapter} at {rarity}: the par table's own cell, times the authored " +
                "item-power fraction, times the band's own multiplier.");
    }

    /// <summary>Item power climbs with the chapter and with the band, and with nothing else.</summary>
    /// <remarks>
    /// The two monotonicities the item screen rests on. Stated as a property because the four cells
    /// above are single points, and a formula that had swapped two factors would satisfy them.
    /// </remarks>
    [Fact]
    public void Item_power_climbs_with_both_the_chapter_and_the_band()
    {
        var par = ParPowerTuning.Read(GearDocuments.Shipped);
        var drops = DropsTuning.Read(GearDocuments.Shipped);

        double Power(int chapter, Rarity rarity) => ItemPower.For(
            par.ChapterPowerTarget(chapter),
            drops.ItemPowerCoefficient,
            drops.Band(rarity).StatMultiplier);

        (Power(2, Rarity.C) / Power(1, Rarity.C)).ShouldBe(
            2.0, 1e-9, "the par column doubles from chapter to chapter, and item power is a fraction of it");
        (Power(1, Rarity.SS) / Power(1, Rarity.C)).ShouldBe(
            (double)GearDocuments.ShippedStatMultiplierSs, 1e-9);
    }

    /// <summary>Every factor must be a positive finite number, and each refusal names its own.</summary>
    /// <remarks>
    /// A zero or negative factor makes every stat on the item zero or negative; a non-finite one
    /// makes the item unpersistable. Named per parameter because a single guard on the first factor
    /// would satisfy a bare type assertion on all three.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_factor_that_is_not_a_positive_finite_number_is_refused(double bad)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ItemPower.For(bad, 0.1, 1.0))
            .ParamName.ShouldBe("chapterPowerTarget");
        Should.Throw<ArgumentOutOfRangeException>(() => ItemPower.For(1000.0, bad, 1.0))
            .ParamName.ShouldBe("itemPowerCoefficient");
        Should.Throw<ArgumentOutOfRangeException>(() => ItemPower.For(1000.0, 0.1, bad))
            .ParamName.ShouldBe("bandStatMultiplier");
    }

    /// <summary>The refusal says why, not merely that a number was out of range.</summary>
    [Fact]
    public void The_refusal_says_what_a_bad_factor_would_do_to_the_item()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ItemPower.For(0.0, 0.1, 1.0))
            .Message.ShouldContain("three positive finite factors", Case.Sensitive);
    }
}
