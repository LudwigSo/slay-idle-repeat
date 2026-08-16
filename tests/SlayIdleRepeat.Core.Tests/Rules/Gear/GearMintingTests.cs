using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// Building one item of an already-decided band: the base item, the quality scalar and the affixes,
/// in that fixed order.
/// </summary>
/// <remarks>
/// <b>Draw order is the item's identity.</b> Base item, then quality, then affixes — so the same seed
/// at the same stream position mints the same item on the client and on the server, and a forced band
/// and a natural one leave the stream in the same place. That ordering claim is what
/// <see cref="Two_bands_drawn_from_one_seed_pick_the_same_base_item_and_the_same_quality"/> exists to
/// pin: it is invisible in any single mint.
/// </remarks>
public sealed class GearMintingTests
{
    /// <summary>An arbitrary but fixed seed. Nothing here depends on which item it produces.</summary>
    private const ulong Seed = 0x5A1D_5EED_0417UL;

    /// <summary>How many seeds every sweep in this file runs over.</summary>
    private const int SweepWidth = 96;

    /// <summary>A minted item carries the identity, chapter and band it was minted with.</summary>
    [Fact]
    public void A_minted_item_carries_the_identity_chapter_and_band_it_was_minted_with()
    {
        var item = Mint(new GearInstanceId("gi_0042"), chapterOrigin: 6, rarity: Rarity.A, Rng());

        item.InstanceId.ShouldBe(new GearInstanceId("gi_0042"));
        item.ChapterOrigin.ShouldBe(6);
        item.Rarity.ShouldBe(Rarity.A);
        item.EnhanceLevel.ShouldBe(0);
        item.EnhanceFailures.ShouldBe(0);
        item.Locked.ShouldBeFalse("a freshly rolled item is not locked; the player locks it");
    }

    /// <summary>The base item is one of the catalogue's own rows, whole.</summary>
    /// <remarks>
    /// Id, slot and family together: a mint that drew a family and looked its slot up separately
    /// could produce a blade worn on the feet.
    /// </remarks>
    [Fact]
    public void The_base_item_is_one_of_the_catalogues_own_rows()
    {
        var catalogue = Catalogue();
        var minted = 0;

        foreach (var seed in Seeds())
        {
            var item = Mint(new GearInstanceId("gi"), 1, Rarity.B, At(seed));
            var definition = catalogue.Definition(item.Family);

            item.DefId.ShouldBe(definition.DefId);
            item.Slot.ShouldBe(definition.Slot);
            minted++;
        }

        minted.ShouldBe(SweepWidth, "the assertions above live inside a loop");
    }

    /// <summary>Every one of the twenty-four base items is reachable.</summary>
    /// <remarks>
    /// Nothing in the design set weights one of the twenty-four over another for a drop, and a mint
    /// that could not produce some of them would make those items unobtainable without any test in
    /// this file going red.
    /// </remarks>
    [Fact]
    public void Every_base_item_is_reachable()
    {
        Enumerable.Range(1, 600)
            .Select(seed => Mint(
                new GearInstanceId("gi"), 1, Rarity.C, At((ulong)seed)).Family)
            .Distinct()
            .Count()
            .ShouldBe(
                Enum.GetValues<GearFamily>().Length,
                "the base item is drawn uniformly across the catalogue; a row nothing can reach is an " +
                "item that exists only in the content file.");
    }

    /// <summary>Quality lands inside the authored range and is already rounded.</summary>
    /// <remarks>
    /// Rounded at the mint because this is where the draw becomes state: the instance refuses an
    /// unrounded quality, which is what keeps the rounding from drifting to a caller that no longer
    /// has the draw in front of it.
    /// </remarks>
    [Fact]
    public void Quality_lands_inside_the_authored_range_and_is_already_rounded()
    {
        var quality = Drops().Quality;
        var minted = 0;

        foreach (var seed in Seeds())
        {
            var rolled = Mint(new GearInstanceId("gi"), 1, Rarity.C, At(seed)).Quality;

            rolled.ShouldBeInRange(quality.Minimum, quality.Maximum);
            DeterminismRounding.IsRounded(rolled).ShouldBeTrue(
                $"seed {seed} rolled {rolled}, which persisted state cannot carry");
            minted++;
        }

        minted.ShouldBe(SweepWidth);
    }

    /// <summary>Quality is drawn rather than fixed: the sweep produces many different values.</summary>
    /// <remarks>The floor under the range assertion, which a mint answering a constant would pass.</remarks>
    [Fact]
    public void Quality_is_drawn_rather_than_fixed()
    {
        Seeds()
            .Select(seed => Mint(new GearInstanceId("gi"), 1, Rarity.C, At(seed)).Quality)
            .Distinct()
            .Count()
            .ShouldBeGreaterThan(SweepWidth / 2);
    }

    /// <summary>An item rolls exactly the number of affixes its band authors.</summary>
    /// <remarks>
    /// The top band is absent from this theory on purpose — see
    /// <see cref="The_shipped_affix_pool_cannot_fill_a_top_band_boots_item"/>, which states why.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C)]
    [InlineData(Rarity.B)]
    [InlineData(Rarity.A)]
    [InlineData(Rarity.S)]
    public void An_item_rolls_exactly_the_number_of_affixes_its_band_authors(Rarity rarity)
    {
        var drops = Drops();
        var expected = drops.Band(rarity).AffixCount;
        var minted = 0;

        foreach (var seed in Seeds())
        {
            var item = Mint(new GearInstanceId("gi"), 1, rarity, At(seed));

            item.Affixes.Count.ShouldBe(expected);
            minted++;
        }

        minted.ShouldBe(SweepWidth);
    }

    /// <summary>Every rolled affix is one the item's own slot and band are eligible for.</summary>
    [Theory]
    [InlineData(Rarity.B)]
    [InlineData(Rarity.A)]
    [InlineData(Rarity.S)]
    public void Every_rolled_affix_is_eligible_for_the_items_own_slot(Rarity rarity)
    {
        var drops = Drops();
        var checkedAffixes = 0;

        foreach (var seed in Seeds())
        {
            var item = Mint(new GearInstanceId("gi"), 1, rarity, At(seed));
            var eligible = drops.EligibleAffixes(item.Slot, rarity)
                .Select(affix => affix.AffixId)
                .ToArray();

            foreach (var rolled in item.Affixes)
            {
                eligible.ShouldContain(
                    rolled.AffixId,
                    $"a {rarity} {item.Slot} item rolled {rolled.AffixId}, which the pool never offers " +
                    "on that slot — a stat the item screen cannot explain.");
                checkedAffixes++;
            }
        }

        checkedAffixes.ShouldBe(SweepWidth * drops.Band(rarity).AffixCount);
    }

    /// <summary>No affix repeats on one item.</summary>
    [Fact]
    public void No_affix_repeats_on_one_item()
    {
        foreach (var seed in Seeds())
        {
            Mint(new GearInstanceId("gi"), 1, Rarity.S, At(seed))
                .Affixes.Select(affix => affix.AffixId).ShouldBeUnique();
        }
    }

    // ---------------------------------------------------------------- determinism and draw order

    /// <summary>The same seed at the same position mints an identical item.</summary>
    /// <remarks>
    /// Compared whole, which the hand-written value equality on <c>GearInstance</c> makes meaningful:
    /// the affixes are compared element by element rather than by list reference.
    /// </remarks>
    [Fact]
    public void The_same_seed_at_the_same_position_mints_an_identical_item()
    {
        Mint(new GearInstanceId("gi_0001"), 4, Rarity.S, Rng())
            .ShouldBe(Mint(new GearInstanceId("gi_0001"), 4, Rarity.S, Rng()));
    }

    /// <summary>A resumed stream mints a different item, and advances from where it stood.</summary>
    [Fact]
    public void A_resumed_stream_mints_from_where_it_stood()
    {
        var draws = DeterministicRng.OpenAt(Seed, RngStreams.Drops, 0);

        var first = Mint(new GearInstanceId("gi_0001"), 1, Rarity.B, draws);
        var atFour = draws.Position;
        var second = Mint(new GearInstanceId("gi_0001"), 1, Rarity.B, draws);

        atFour.ShouldBe(4UL, "one draw for the base item, one for quality, two for the single affix");
        draws.Position.ShouldBe(8UL);
        second.ShouldNotBe(first, "a second mint on the same stream is a second, independent item");
    }

    /// <summary>
    /// 🔒 Draw order is base item, then quality, then affixes: two different bands drawn from one seed
    /// pick the same base item and the same quality, and differ only from the affixes on.
    /// </summary>
    /// <remarks>
    /// Invisible in any single mint, and the property the whole ordering exists for. A mint that drew
    /// the affixes first — or that drew a band-dependent number of indices before the quality — would
    /// give two bands different base items from one seed, and a replay that re-decided a band would
    /// land on a different item entirely.
    /// </remarks>
    [Fact]
    public void Two_bands_drawn_from_one_seed_pick_the_same_base_item_and_the_same_quality()
    {
        var checkedSeeds = 0;

        foreach (var seed in Seeds())
        {
            var bottom = Mint(new GearInstanceId("gi"), 1, Rarity.C, At(seed));
            var second = Mint(new GearInstanceId("gi"), 1, Rarity.B, At(seed));

            second.DefId.ShouldBe(bottom.DefId);
            second.Family.ShouldBe(bottom.Family);
            second.Slot.ShouldBe(bottom.Slot);
            second.Quality.ShouldBe(
                bottom.Quality,
                "quality is drawn before the affixes, so the number of affixes the band asks for " +
                "cannot move it.");

            bottom.Affixes.ShouldBeEmpty();
            second.Affixes.Count.ShouldBe(1, "and the bands do differ from the affixes on");
            checkedSeeds++;
        }

        checkedSeeds.ShouldBe(SweepWidth);
    }

    // ---------------------------------------------------------------- the carried-forward content gap

    /// <summary>
    /// 🔴 The shipped affix pool cannot fill a top-band boots item: the slot authorises three affixes
    /// and the band rolls four, so the mint is refused rather than answered with three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>This is a content gap, not a rule defect, and it is reachable in play.</b> The last
    /// chapter band drops the top rarity at five per cent and the base item is drawn uniformly across
    /// twenty-four rows, four of which are boots — so roughly one top-band drop in six lands on a slot
    /// the pool cannot fill and the drop throws.
    /// </para>
    /// <para>
    /// The refusal itself is right: rolling three affixes instead of four would hide a pool that
    /// cannot fill the band behind an item that merely looks unlucky. What is missing is a fourth
    /// boots-eligible affix in <c>tuning/drops.json</c>, which is an authoring decision rather than
    /// something a test may invent. <b>Carried forward: the affix pool needs a fourth boots row, or
    /// the top band's affix count needs a slot-aware cap.</b> When either lands, this case goes red
    /// and the top band joins the two theories above.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shipped_affix_pool_cannot_fill_a_top_band_boots_item()
    {
        var drops = Drops();
        var catalogue = Catalogue();

        drops.EligibleAffixes(GearSlot.BOOTS, Rarity.SS).Count.ShouldBe(
            3, "the shipped pool offers attack speed, defence and dodge on boots and nothing else");
        drops.Band(Rarity.SS).AffixCount.ShouldBe(4);

        var refusedSlots = new List<GearSlot>();
        var minted = 0;

        foreach (var seed in Seeds())
        {
            var slot = catalogue.Definitions[At(seed).Range(0, catalogue.Definitions.Count)].Slot;

            try
            {
                Mint(new GearInstanceId("gi"), 8, Rarity.SS, At(seed)).Affixes.Count.ShouldBe(4);
                minted++;
            }
            catch (InvalidTunableException)
            {
                refusedSlots.Add(slot);
            }
        }

        minted.ShouldBeGreaterThan(0, "every other slot fills the top band from its own pool");
        refusedSlots.ShouldNotBeEmpty(
            "the sweep has to actually reach a boots item, or this case would pass by never testing " +
            "the gap at all.");
        refusedSlots.Distinct().ShouldBe(
            new[] { GearSlot.BOOTS },
            "boots is the only slot the shipped pool cannot fill at the top band; a second slot " +
            "appearing here is a new gap rather than this one.");
    }

    // ---------------------------------------------------------------- refusals

    /// <summary>Every reference argument is required, and each refusal names its own.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => GearMinting.Mint(
                new GearInstanceId("gi"), null!, Drops(), 1, Rarity.C, Rng()))
            .ParamName.ShouldBe("catalogue");
        Should.Throw<ArgumentNullException>(() => GearMinting.Mint(
                new GearInstanceId("gi"), Catalogue(), null!, 1, Rarity.C, Rng()))
            .ParamName.ShouldBe("drops");
        Should.Throw<ArgumentNullException>(() => GearMinting.Mint(
                new GearInstanceId("gi"), Catalogue(), Drops(), 1, Rarity.C, null!))
            .ParamName.ShouldBe("draws");
    }

    /// <summary>A chapter of origin below the first chapter is refused by the instance it would build.</summary>
    [Fact]
    public void A_chapter_of_origin_below_the_first_chapter_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => Mint(new GearInstanceId("gi"), 0, Rarity.C, Rng()))
            .ParamName.ShouldBe("chapterOrigin");
    }

    /// <summary>A band the ladder has no row for is refused rather than minted with no affixes.</summary>
    [Fact]
    public void A_band_the_ladder_has_no_row_for_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => Mint(new GearInstanceId("gi"), 1, (Rarity)9, Rng()))
            .Reference.ShouldBe(DropsTuning.RaritiesReference);
    }

    private static GearInstance Mint(
        GearInstanceId instanceId, int chapterOrigin, Rarity rarity, DeterministicRng draws) =>
        GearMinting.Mint(instanceId, Catalogue(), Drops(), chapterOrigin, rarity, draws);

    private static GearCatalogue Catalogue() => GearCatalogue.Read(GearDocuments.Shipped);

    private static DropsTuning Drops() => DropsTuning.Read(GearDocuments.Shipped);

    private static DeterministicRng Rng() => At(Seed);

    private static DeterministicRng At(ulong seed) =>
        DeterministicRng.OpenAt(seed, RngStreams.Drops, 0);

    private static IEnumerable<ulong> Seeds() =>
        Enumerable.Range(1, SweepWidth).Select(seed => (ulong)seed);
}
