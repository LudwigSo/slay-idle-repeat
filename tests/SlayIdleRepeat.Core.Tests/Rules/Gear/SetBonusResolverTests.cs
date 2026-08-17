using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// Which sets a loadout is wearing and which of their breakpoints it has reached: derived from the
/// family axis the catalogue authors, never from a stored set id.
/// </summary>
/// <remarks>
/// <b>SS only.</b> A set piece is a top-band item; the same family at any lower band contributes
/// nothing, which is what makes a full set an endgame goal rather than something a mid-game player
/// assembles by accident. The lower bands are counted out by the engine that states the rule rather
/// than filtered by its callers.
/// </remarks>
public sealed class SetBonusResolverTests
{
    /// <summary>The balanced families, one per slot — the set a full loadout can complete.</summary>
    private static readonly GearFamily[] Balanced =
    [
        GearFamily.BLADE, GearFamily.HOOD, GearFamily.LEATHERS,
        GearFamily.TREADS, GearFamily.BAND, GearFamily.PENDANT,
    ];

    /// <summary>A loadout with no top-band piece activates no set at all.</summary>
    /// <remarks>
    /// Six pieces of one axis, one per slot, at the band immediately below the set band: the loadout
    /// would be a complete set if the rule were "matching families" rather than "matching families at
    /// the top band".
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C)]
    [InlineData(Rarity.B)]
    [InlineData(Rarity.A)]
    [InlineData(Rarity.S)]
    public void A_full_loadout_below_the_set_band_activates_no_set(Rarity rarity)
    {
        Resolve(Balanced.Select(family => Item(family, rarity)).ToArray()).ShouldBeEmpty(
            $"{rarity} is not the set band, and six matching pieces of it are still six ordinary items");
    }

    /// <summary>Six top-band pieces of one axis meet every breakpoint, ascending.</summary>
    /// <remarks>
    /// Every breakpoint at or below the count, not only the highest: the tiers escalate rather than
    /// replace, so a six-piece set is still granting its two- and four-piece bonuses.
    /// </remarks>
    [Fact]
    public void Six_top_band_pieces_of_one_axis_meet_every_breakpoint()
    {
        var active = Resolve(Balanced.Select(family => Item(family, SetBonusResolver.SetBand)).ToArray());

        active.Count.ShouldBe(1);
        active[0].Set.ShouldBe(GearFamilyAxis.BALANCED);
        active[0].Pieces.ShouldBe(6);
        active[0].BreakpointsMet.ShouldBe(
            GearDocuments.ShippedSetBreakpoints,
            "the tiers escalate rather than replace; answering only the six-piece tier would silently " +
            "take the two- and four-piece bonuses away from a completed set.");
    }

    /// <summary>Fewer pieces meet fewer breakpoints, and the ladder is walked from the bottom.</summary>
    [Theory]
    [InlineData(1, new int[0])]
    [InlineData(2, new[] { 2 })]
    [InlineData(3, new[] { 2 })]
    [InlineData(4, new[] { 2, 4 })]
    [InlineData(5, new[] { 2, 4 })]
    [InlineData(6, new[] { 2, 4, 6 })]
    public void A_partial_set_meets_exactly_the_breakpoints_its_piece_count_reaches(
        int pieces, int[] met)
    {
        var active = Resolve(Balanced.Take(pieces).Select(family => Item(family, Rarity.SS)).ToArray());

        active.Count.ShouldBe(1, "one axis is worn, so one set is reported");
        active[0].Pieces.ShouldBe(pieces);
        active[0].BreakpointsMet.ShouldBe(met);
    }

    /// <summary>A single top-band piece is reported as worn even though it grants nothing yet.</summary>
    /// <remarks>
    /// Reported rather than omitted: the client shows "1/2" progress towards the first tier, and a set
    /// that vanished below its own first breakpoint would leave the player with nothing to count
    /// towards.
    /// </remarks>
    [Fact]
    public void A_single_top_band_piece_is_reported_as_worn_with_no_breakpoint_met()
    {
        var active = Resolve([Item(GearFamily.BLADE, Rarity.SS)]);

        active.Count.ShouldBe(1);
        active[0].Pieces.ShouldBe(1);
        active[0].BreakpointsMet.ShouldBeEmpty();
    }

    /// <summary>A mixed loadout reports each axis separately, in family-axis order.</summary>
    /// <remarks>
    /// Separately, because the sets do not pool: three balanced pieces and three heavy ones is two
    /// sets at three pieces each, not one set at six. Order is asserted as well as membership, so the
    /// client can render the same list twice and get the same rows.
    /// </remarks>
    [Fact]
    public void A_mixed_loadout_reports_each_axis_separately_in_axis_order()
    {
        var active = Resolve(
        [
            Item(GearFamily.BLADE, Rarity.SS),
            Item(GearFamily.HOOD, Rarity.SS),
            Item(GearFamily.PLATE, Rarity.SS),
            Item(GearFamily.GREAVES, Rarity.SS),
            Item(GearFamily.SIGNET, Rarity.SS),
            Item(GearFamily.IDOL, Rarity.SS),
        ]);

        active.Select(set => set.Set).ShouldBe(
            new[] { GearFamilyAxis.BALANCED, GearFamilyAxis.HEAVY, GearFamilyAxis.AGILE });
        active.Single(set => set.Set == GearFamilyAxis.BALANCED).Pieces.ShouldBe(2);
        active.Single(set => set.Set == GearFamilyAxis.HEAVY).Pieces.ShouldBe(3);
        active.Single(set => set.Set == GearFamilyAxis.AGILE).BreakpointsMet.ShouldBeEmpty(
            "one agile piece reaches no tier, while the balanced pair reaches the first");
        active.Single(set => set.Set == GearFamilyAxis.BALANCED).BreakpointsMet.ShouldBe(new[] { 2 });
    }

    /// <summary>An axis nothing is worn of is not reported at all.</summary>
    /// <remarks>The negative control on the mixed case: absent, not present with zero pieces.</remarks>
    [Fact]
    public void An_axis_nothing_is_worn_of_is_not_reported()
    {
        Resolve([Item(GearFamily.BLADE, Rarity.SS)])
            .Select(set => set.Set)
            .ShouldNotContain(GearFamilyAxis.CASTER);
    }

    /// <summary>A piece's set is the axis the catalogue authors for its family.</summary>
    /// <remarks>
    /// Derived rather than stored: a set id on the instance would be the same value written twice,
    /// and the two spellings would eventually disagree.
    /// </remarks>
    [Theory]
    [InlineData(GearFamily.BLADE, GearFamilyAxis.BALANCED)]
    [InlineData(GearFamily.PLATE, GearFamilyAxis.HEAVY)]
    [InlineData(GearFamily.CIRCLET, GearFamilyAxis.CASTER)]
    [InlineData(GearFamily.SANDALS, GearFamilyAxis.AGILE)]
    public void A_pieces_set_is_the_axis_the_catalogue_authors_for_its_family(
        GearFamily family, GearFamilyAxis axis)
    {
        Resolve([Item(family, Rarity.SS)])[0].Set.ShouldBe(axis);
    }

    /// <summary>A loadout mixing bands counts only the top-band pieces.</summary>
    /// <remarks>
    /// The discriminating case: the same six families are equipped either way, and only the bands
    /// differ. A resolver that counted every matching family would answer six pieces here.
    /// </remarks>
    [Fact]
    public void A_loadout_mixing_bands_counts_only_the_top_band_pieces()
    {
        var active = Resolve(
        [
            Item(GearFamily.BLADE, Rarity.SS),
            Item(GearFamily.HOOD, Rarity.SS),
            Item(GearFamily.LEATHERS, Rarity.S),
            Item(GearFamily.TREADS, Rarity.A),
            Item(GearFamily.BAND, Rarity.SS),
            Item(GearFamily.PENDANT, Rarity.C),
        ]);

        active.Count.ShouldBe(1);
        active[0].Pieces.ShouldBe(3);
        active[0].BreakpointsMet.ShouldBe(new[] { 2 });
    }

    /// <summary>An empty loadout activates nothing.</summary>
    [Fact]
    public void An_empty_loadout_activates_nothing()
    {
        Resolve([]).ShouldBeEmpty();
    }

    /// <summary>Every reference argument is required, and each refusal names its own.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        var catalogue = GearCatalogue.Read(GearDocuments.Shipped);
        var drops = DropsTuning.Read(GearDocuments.Shipped);

        Should.Throw<ArgumentNullException>(() => SetBonusResolver.Resolve(null!, drops, []))
            .ParamName.ShouldBe("catalogue");
        Should.Throw<ArgumentNullException>(() => SetBonusResolver.Resolve(catalogue, null!, []))
            .ParamName.ShouldBe("drops");
        Should.Throw<ArgumentNullException>(() => SetBonusResolver.Resolve(catalogue, drops, null!))
            .ParamName.ShouldBe("equipped");
    }

    private static IReadOnlyList<ActiveSet> Resolve(IReadOnlyList<GearInstance> equipped) =>
        SetBonusResolver.Resolve(
            GearCatalogue.Read(GearDocuments.Shipped),
            DropsTuning.Read(GearDocuments.Shipped),
            equipped);

    private static GearInstance Item(GearFamily family, Rarity rarity) => new(
        new GearInstanceId("gi_" + family),
        "GEAR_TEST_" + family,
        GearCatalogue.Read(GearDocuments.Shipped).Definition(family).Slot,
        family,
        rarity,
        1,
        0.5,
        0,
        0,
        [],
        locked: false);
}
