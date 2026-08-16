using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// The two stats a gear instance contributes, derived from its stored fields — never read back from
/// storage, because they are never stored.
/// </summary>
/// <remarks>
/// <b>Two kinds of stat, and the difference is load-bearing.</b> A flat stat is a multiple of item
/// power and climbs with the chapter curve; a percent stat is read straight out of the per-rarity
/// table and climbs with rarity alone, because it feeds a capped percentage that would otherwise push
/// every build against the cap by mid-game. The slot table says which is which by authoring a null
/// coefficient for the percent ones, and the two must never be added into the same accumulator.
/// </remarks>
public sealed class GearStatDerivationTests
{
    /// <summary>A flat stat doubles from chapter to chapter, because the par column does.</summary>
    /// <remarks>
    /// Exactly twice rather than merely larger: item power is a fixed fraction of the chapter's par,
    /// and the shipped par column doubles, so anything but a factor of two means a second scaling
    /// crept in between the two.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C)]
    [InlineData(Rarity.SS)]
    public void A_flat_stat_scales_with_the_chapter_the_item_came_from(Rarity rarity)
    {
        var first = Primary(Item(GearSlot.WEAPON, GearFamily.BLADE, rarity, chapterOrigin: 1));
        var second = Primary(Item(GearSlot.WEAPON, GearFamily.BLADE, rarity, chapterOrigin: 2));

        first.IsPercent.ShouldBeFalse("ATK is authored with a flat coefficient");
        second.Value.ShouldBe(
            DeterminismRounding.Round(first.Value * 2.0),
            "an item keeps the power it dropped with, and chapter 2's par is exactly twice chapter 1's.");
    }

    /// <summary>A percent stat is identical at every chapter, at the same band and quality.</summary>
    /// <remarks>
    /// It feeds a capped percentage. Letting it inflate across chapters would push every build
    /// against the cap by mid-game, which is why the slot table authors a null coefficient for it
    /// rather than a small one.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C)]
    [InlineData(Rarity.SS)]
    public void A_percent_stat_does_not_scale_with_the_chapter(Rarity rarity)
    {
        var first = Primary(Item(GearSlot.RING, GearFamily.BAND, rarity, chapterOrigin: 1));
        var last = Primary(Item(GearSlot.RING, GearFamily.BAND, rarity, chapterOrigin: 8));

        first.IsPercent.ShouldBeTrue("crit chance is authored with a null coefficient");
        last.Value.ShouldBe(
            first.Value,
            "a chapter-8 ring and a chapter-1 ring of the same band carry the same crit chance; " +
            "eight chapters of doubling would be a hundred and twenty-eight times the cap.");
    }

    /// <summary>A percent stat does still climb with the band.</summary>
    /// <remarks>
    /// The negative control on the case above: a derivation that answered a constant for percent
    /// stats would satisfy it while making the whole rarity ladder meaningless for half the slots.
    /// </remarks>
    [Fact]
    public void A_percent_stat_climbs_with_the_band()
    {
        var bottom = Primary(Item(GearSlot.RING, GearFamily.BAND, Rarity.C, 1));
        var top = Primary(Item(GearSlot.RING, GearFamily.BAND, Rarity.SS, 1));

        (top.Value > bottom.Value).ShouldBeTrue(
            $"crit chance runs from {bottom.Value} at C to {top.Value} at SS");
    }

    /// <summary>
    /// Quality is asymmetric: at the top of the range the primary stat is at ×1.10 and the secondary
    /// at ×1.15; at the bottom they are ×0.90 and ×0.85.
    /// </summary>
    /// <remarks>
    /// The asymmetry is what makes two S-rarity blades different items, and what lets the UI show one
    /// honest quality bar for both stats. A symmetric pair would make the bar mean the same thing
    /// twice.
    /// </remarks>
    [Theory]
    [InlineData(0.0, 16.2, 46.75)]
    [InlineData(0.5, 18.0, 55.0)]
    [InlineData(1.0, 19.8, 63.25)]
    public void Quality_spans_further_on_the_secondary_stat_than_on_the_primary(
        double quality, double primary, double secondary)
    {
        var item = Item(GearSlot.HELMET, GearFamily.HOOD, Rarity.C, 1, quality);

        Primary(item).Value.ShouldBe(
            primary, "chapter 1 at C is 100 item power; DEF's coefficient is 0.18, spanning 0.90–1.10");
        Secondary(item).Value.ShouldBe(
            secondary, "and Max HP's is 0.55, spanning 0.85–1.15 — further, on purpose");
    }

    /// <summary>Derived values are rounded to the assembly's determinism precision.</summary>
    /// <remarks>
    /// The quality scale is a product of authored decimals, so an unrounded derivation lands on a
    /// value the canonical state writer refuses and the item screen renders to fifteen places.
    /// </remarks>
    [Fact]
    public void A_derived_value_is_rounded_to_the_assemblys_precision()
    {
        var item = Item(GearSlot.RING, GearFamily.BAND, Rarity.C, 1, quality: 0.3333);

        DeterminismRounding.IsRounded(Primary(item).Value).ShouldBeTrue();
        Primary(item).Value.ShouldBe(
            0.0145,
            "0.015 at C, times a primary scale of 0.9 + 0.2 × 0.3333 — which is 0.0144999… before " +
            "the rounding rule is applied.");
    }

    /// <summary>Which primary stat each slot carries, and whether it is a percentage.</summary>
    /// <remarks>
    /// <c>IsPercent</c> is true exactly where the slot table authors a null coefficient. The two
    /// kinds are never added into the same accumulator, so a stat mislabelled here would either
    /// vanish from a total or be added to one it does not belong in.
    /// </remarks>
    [Theory]
    [InlineData(GearSlot.WEAPON, GearFamily.BLADE, "ATK", false)]
    [InlineData(GearSlot.HELMET, GearFamily.HOOD, "DEF", false)]
    [InlineData(GearSlot.ARMOR, GearFamily.LEATHERS, "MAX_HP", false)]
    [InlineData(GearSlot.BOOTS, GearFamily.TREADS, "ASPD", true)]
    [InlineData(GearSlot.RING, GearFamily.BAND, "CRIT", true)]
    [InlineData(GearSlot.AMULET, GearFamily.PENDANT, "MAX_HP", false)]
    public void The_primary_stat_of_a_slot_is_the_one_the_table_authors(
        GearSlot slot, GearFamily family, string stat, bool percent)
    {
        var derived = Primary(Item(slot, family, Rarity.A, 3));

        derived.Stat.ShouldBe(stat);
        derived.IsPercent.ShouldBe(percent);
    }

    /// <summary>Which secondary stat each slot carries, and whether it is a percentage.</summary>
    [Theory]
    [InlineData(GearSlot.WEAPON, GearFamily.BLADE, "CRIT", true)]
    [InlineData(GearSlot.HELMET, GearFamily.HOOD, "MAX_HP", false)]
    [InlineData(GearSlot.ARMOR, GearFamily.LEATHERS, "DEF", false)]
    [InlineData(GearSlot.BOOTS, GearFamily.TREADS, "DODGE", true)]
    [InlineData(GearSlot.RING, GearFamily.BAND, "PEN", true)]
    [InlineData(GearSlot.AMULET, GearFamily.PENDANT, "LIFESTEAL", true)]
    public void The_secondary_stat_of_a_slot_is_the_one_the_table_authors(
        GearSlot slot, GearFamily family, string stat, bool percent)
    {
        var derived = Secondary(Item(slot, family, Rarity.A, 3));

        derived.Stat.ShouldBe(stat);
        derived.IsPercent.ShouldBe(percent);
    }

    /// <summary>A percent stat reads the per-rarity cell rather than a fraction of item power.</summary>
    /// <remarks>
    /// The two derivations agree on the shape of an answer and on nothing else, so the value is
    /// checked against the table the null coefficient points at.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C, 0.015)]
    [InlineData(Rarity.A, 0.04)]
    [InlineData(Rarity.SS, 0.08)]
    public void A_percent_stat_is_the_per_rarity_cell_at_a_neutral_quality(Rarity rarity, double cell)
    {
        Primary(Item(GearSlot.RING, GearFamily.BAND, rarity, 5, quality: 0.5)).Value.ShouldBe(
            cell,
            "a quality of 0.5 puts the primary scale at exactly 1.0, so the derived value is the " +
            "authored cell itself.");
    }

    /// <summary>Enhancement is not folded into the derivation.</summary>
    /// <remarks>
    /// The level is stored on the instance and the ladder that prices it belongs to the forge; this
    /// derivation answers what the item rolled, not what it has been upgraded to. A caller that needs
    /// the enhanced figure composes the two rather than finding one silently folded in.
    /// </remarks>
    [Fact]
    public void Enhancement_is_not_folded_into_the_derivation()
    {
        var unenhanced = Item(GearSlot.WEAPON, GearFamily.BLADE, Rarity.A, 3);
        var enhanced = Item(GearSlot.WEAPON, GearFamily.BLADE, Rarity.A, 3, enhanceLevel: 12);

        Primary(enhanced).Value.ShouldBe(Primary(unenhanced).Value);
    }

    /// <summary>An item from a chapter the par table has no row for is refused.</summary>
    [Fact]
    public void An_item_from_a_chapter_the_par_table_has_no_row_for_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => Primary(Item(GearSlot.WEAPON, GearFamily.BLADE, Rarity.A, chapterOrigin: 9)))
            .Reference.ShouldBe(ParPowerTuning.ParPowerReference);
    }

    /// <summary>A percent stat needs no par table row at all, so it answers for any chapter.</summary>
    /// <remarks>
    /// The discriminating half of the case above: a derivation that read the par table before
    /// deciding the stat's kind would refuse a chapter-9 ring too, and percent stats would silently
    /// inherit a dependency they do not have.
    /// </remarks>
    [Fact]
    public void A_percent_stat_answers_for_a_chapter_the_par_table_has_no_row_for()
    {
        Primary(Item(GearSlot.RING, GearFamily.BAND, Rarity.A, chapterOrigin: 9, quality: 0.5))
            .Value.ShouldBe(0.04);
    }

    /// <summary>Every argument is required, and each refusal names its own.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        var par = ParPowerTuning.Read(GearDocuments.Shipped);
        var drops = DropsTuning.Read(GearDocuments.Shipped);
        var item = Item(GearSlot.WEAPON, GearFamily.BLADE, Rarity.A, 1);

        Should.Throw<ArgumentNullException>(() => GearStatDerivation.Primary(null!, drops, item))
            .ParamName.ShouldBe("par");
        Should.Throw<ArgumentNullException>(() => GearStatDerivation.Primary(par, null!, item))
            .ParamName.ShouldBe("drops");
        Should.Throw<ArgumentNullException>(() => GearStatDerivation.Primary(par, drops, null!))
            .ParamName.ShouldBe("item");
        Should.Throw<ArgumentNullException>(() => GearStatDerivation.Secondary(null!, drops, item))
            .ParamName.ShouldBe("par");
        Should.Throw<ArgumentNullException>(() => GearStatDerivation.Secondary(par, null!, item))
            .ParamName.ShouldBe("drops");
        Should.Throw<ArgumentNullException>(() => GearStatDerivation.Secondary(par, drops, null!))
            .ParamName.ShouldBe("item");
    }

    private static DerivedGearStat Primary(GearInstance item) => GearStatDerivation.Primary(
        ParPowerTuning.Read(GearDocuments.Shipped), DropsTuning.Read(GearDocuments.Shipped), item);

    private static DerivedGearStat Secondary(GearInstance item) => GearStatDerivation.Secondary(
        ParPowerTuning.Read(GearDocuments.Shipped), DropsTuning.Read(GearDocuments.Shipped), item);

    private static GearInstance Item(
        GearSlot slot,
        GearFamily family,
        Rarity rarity,
        int chapterOrigin,
        double quality = 0.5,
        int enhanceLevel = 0) =>
        new(
            new GearInstanceId("gi_0001"),
            "GEAR_TEST_ITEM",
            slot,
            family,
            rarity,
            chapterOrigin,
            quality,
            enhanceLevel,
            0,
            [],
            locked: false);
}
