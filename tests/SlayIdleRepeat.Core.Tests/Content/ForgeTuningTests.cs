using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The forge reader: the three free operations' numbers, and the two holes it refuses rather than
/// fills.
/// </summary>
public sealed class ForgeTuningTests
{
    private static ForgeTuning Shipped { get; } = ForgeTuning.Read(ForgeDocuments.Shipped);

    /// <summary>The fusion's shape, read rather than compiled in.</summary>
    [Fact]
    public void The_reader_answers_the_fusion_shape_the_document_authors()
    {
        Shipped.MergeInputCount.ShouldBe(ForgeDocuments.ShippedMergeInputCount);
        Shipped.MergeDustSubstituteMaxInputs.ShouldBe(ForgeDocuments.ShippedDustSubstituteMaxInputs);
    }

    /// <summary>A retuned input count reaches the reader rather than a compiled-in three.</summary>
    [Fact]
    public void A_retuned_input_count_is_read_back_rather_than_assumed()
    {
        ForgeTuning.Read(ForgeDocuments.With(inputCount: ContentValue.Number(5)))
            .MergeInputCount.ShouldBe(5);
    }

    /// <summary>The Crown price is keyed on the OUTPUT band.</summary>
    /// <param name="rarity">The band the fusion lands on.</param>
    /// <param name="price">What it costs.</param>
    [Theory]
    [InlineData(Rarity.B, 120)]
    [InlineData(Rarity.A, 600)]
    [InlineData(Rarity.S, 3000)]
    [InlineData(Rarity.SS, 15000)]
    public void The_reader_answers_the_Crown_price_of_each_output_band(Rarity rarity, long price)
    {
        Shipped.MergeCrownCost(rarity).ShouldBe(price);
    }

    /// <summary>
    /// Nothing fuses ONTO the bottom band, so the map deliberately carries no row for it and the
    /// reader refuses rather than answering a price nobody set.
    /// </summary>
    [Fact]
    public void The_bottom_band_is_not_priced_as_an_output_and_is_refused()
    {
        Should.Throw<InvalidTunableException>(() => _ = Shipped.MergeCrownCost(Rarity.C));
    }

    /// <summary>The dust price is keyed on the INPUT band.</summary>
    /// <param name="rarity">The band the items being fused share.</param>
    /// <param name="price">What a substituted slot costs.</param>
    [Theory]
    [InlineData(Rarity.C, 50)]
    [InlineData(Rarity.B, 200)]
    [InlineData(Rarity.A, 800)]
    [InlineData(Rarity.S, 3200)]
    public void The_reader_answers_the_dust_price_of_each_input_band(Rarity rarity, long price)
    {
        Shipped.MergeDustSubstituteCost(rarity).ShouldBe(price);
    }

    /// <summary>
    /// 🔒 <c>SS = n/a</c> is an AUTHORED ABSENCE, not a missing number. Nothing merges out of the
    /// top rung, so the document holds a deliberate null there and the reader refuses it in the
    /// content vocabulary for exactly that — never a zero, and never a plausible extrapolation of
    /// the ×4 ladder below it.
    /// </summary>
    [Fact]
    public void The_top_bands_dust_price_is_an_authored_absence_and_is_refused_as_one()
    {
        Should.Throw<UnauthorisedTunableException>(
            () => _ = Shipped.MergeDustSubstituteCost(Rarity.SS));
    }

    /// <summary>
    /// The two refusals are DIFFERENT: a band the map never mentions is a hole in the document, a
    /// band holding an authored null is a decision. A reader that collapsed them would report a
    /// re-authored map as a design decision.
    /// </summary>
    [Fact]
    public void An_omitted_band_and_an_authored_null_band_are_refused_differently()
    {
        var omitted = ForgeTuning.Read(ForgeDocuments.With(
            dustSubstituteCost: ForgeDocuments.Prices(("C", 50))));

        Should.Throw<InvalidTunableException>(() => _ = omitted.MergeDustSubstituteCost(Rarity.SS));
        Should.Throw<UnauthorisedTunableException>(
            () => _ = Shipped.MergeDustSubstituteCost(Rarity.SS));
    }

    /// <summary>The enhancement bounds and the per-level bonus, read rather than compiled in.</summary>
    [Fact]
    public void The_reader_answers_the_enhancement_bounds_the_document_authors()
    {
        Shipped.MinEnhanceLevel.ShouldBe(ForgeDocuments.ShippedMinEnhanceLevel);
        Shipped.MaxEnhanceLevel.ShouldBe(ForgeDocuments.ShippedMaxEnhanceLevel);
        Shipped.StatBonusPerLevel.ShouldBe(ForgeDocuments.ShippedStatBonusPerLevel);
    }

    /// <summary>Every rung of the stone ladder is reachable and carries the authored figure.</summary>
    /// <param name="level">The level the attempt reaches for.</param>
    /// <param name="stones">What it costs.</param>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(5, 8)]
    [InlineData(6, 12)]
    [InlineData(11, 55)]
    [InlineData(15, 200)]
    public void Every_rung_of_the_stone_ladder_carries_the_authored_cost(int level, long stones)
    {
        Shipped.EnhanceStoneCost(level).ShouldBe(stones);
    }

    /// <summary>
    /// 🔒 The success ladder is the two published band endpoints spread linearly and evenly across
    /// each band's five levels. The second band is non-integral by construction and is not rounded
    /// to look tidy — a ladder that had been would answer 0.44 and 0.31 at these rows.
    /// </summary>
    /// <param name="level">The level the attempt reaches for.</param>
    /// <param name="rate">Its unmodified chance.</param>
    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(5, 1.0)]
    [InlineData(6, 0.85)]
    [InlineData(7, 0.8)]
    [InlineData(8, 0.75)]
    [InlineData(9, 0.7)]
    [InlineData(10, 0.65)]
    [InlineData(11, 0.5)]
    [InlineData(12, 0.4375)]
    [InlineData(13, 0.375)]
    [InlineData(14, 0.3125)]
    [InlineData(15, 0.25)]
    public void Every_rung_of_the_success_ladder_carries_the_interpolated_chance(
        int level, double rate)
    {
        Shipped.EnhanceSuccessRate(level).ShouldBe(rate);
    }

    /// <summary>A level no attempt reaches has neither a cost nor a chance.</summary>
    /// <param name="level">The level asked about.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public void A_level_no_attempt_reaches_has_no_cost_and_no_chance(int level)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _ = Shipped.EnhanceStoneCost(level));
        Should.Throw<ArgumentOutOfRangeException>(() => _ = Shipped.EnhanceSuccessRate(level));
    }

    /// <summary>The stones an item's level cost is the running total of the rungs below it.</summary>
    /// <param name="level">The level the item stands at.</param>
    /// <param name="invested">What getting there cost.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(5, 23)]
    [InlineData(15, 713)]
    public void The_stones_invested_are_the_running_total_of_the_rungs_below(int level, long invested)
    {
        Shipped.EnhanceStonesInvested(level).ShouldBe(invested);
    }

    /// <summary>The salvage numbers, read rather than compiled in.</summary>
    [Fact]
    public void The_reader_answers_the_salvage_numbers_the_document_authors()
    {
        Shipped.DustPerEnhanceLevel.ShouldBe(ForgeDocuments.ShippedDustPerEnhanceLevel);
        Shipped.StoneRefundShare.ShouldBe(ForgeDocuments.ShippedStoneRefundShare);
        Shipped.SalvageBaseDust(Rarity.A).ShouldBe(160);
    }

    /// <summary>
    /// 🔒 An unauthored success ladder is refused rather than filled. This is the hole the document
    /// carried while the interpolation was unstated, and a reader that defaulted it would have been
    /// choosing the shape of the ramp — the one thing the null existed to stop.
    /// </summary>
    [Fact]
    public void An_unauthored_success_ladder_is_refused_rather_than_filled()
    {
        var act = () => ForgeTuning.Read(
            ForgeDocuments.With(perLevelSuccessRate: ContentValue.Unauthorised));

        Should.Throw<UnauthorisedTunableException>(act);
    }

    /// <summary>A ladder of the wrong length cannot price every level, and is refused.</summary>
    [Fact]
    public void A_success_ladder_that_does_not_cover_every_level_is_refused()
    {
        var act = () => ForgeTuning.Read(
            ForgeDocuments.With(perLevelSuccessRate: ForgeDocuments.Rates(1.0, 0.5)));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>A zero chance would make a level unreachable however many attempts were paid for.</summary>
    [Fact]
    public void A_zero_success_chance_is_refused()
    {
        var rates = ForgeDocuments.ShippedSuccessRates.ToArray();
        rates[14] = 0.0;

        var act = () => ForgeTuning.Read(
            ForgeDocuments.With(perLevelSuccessRate: ForgeDocuments.Rates(rates)));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>A fusion of fewer than two items is a rename, not a fusion.</summary>
    [Fact]
    public void A_fusion_of_fewer_than_two_inputs_is_refused()
    {
        var act = () => ForgeTuning.Read(ForgeDocuments.With(inputCount: ContentValue.Number(1)));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>
    /// Dust filling every slot would mint an item out of dust alone, with no input to take a quality
    /// or a chapter of origin from.
    /// </summary>
    [Fact]
    public void Dust_filling_every_slot_is_refused()
    {
        var act = () => ForgeTuning.Read(
            ForgeDocuments.With(dustSubstituteMaxInputs: ContentValue.Number(3)));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>A ceiling at or below the floor leaves nothing enhanceable at all.</summary>
    [Fact]
    public void A_ceiling_at_or_below_the_floor_is_refused()
    {
        var act = () => ForgeTuning.Read(ForgeDocuments.With(maxLevel: ContentValue.Number(0)));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>A refund share above one would turn the game's largest sink into a source.</summary>
    [Fact]
    public void A_refund_share_above_one_is_refused()
    {
        var act = () => ForgeTuning.Read(
            ForgeDocuments.With(stoneRefundShare: ContentValue.Number(1.5m)));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>A price map that prices no band leaves every operation it governs unreachable.</summary>
    [Fact]
    public void A_price_map_that_prices_no_band_is_refused()
    {
        var act = () => ForgeTuning.Read(ForgeDocuments.With(crownCost: ContentValue.EmptyObject));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>A negative price would pay the player to perform the operation.</summary>
    [Fact]
    public void A_negative_price_is_refused()
    {
        var act = () => ForgeTuning.Read(
            ForgeDocuments.With(crownCost: ForgeDocuments.Prices(("B", -1))));

        Should.Throw<InvalidTunableException>(act);
    }

    /// <summary>The missing-document door is a different failure from every hole above it.</summary>
    [Fact]
    public void A_content_set_with_no_forge_document_is_refused_as_missing()
    {
        var act = () => ForgeTuning.Read(ForgeDocuments.Without());

        Should.Throw<MissingContentException>(act);
    }

    /// <summary>The reader refuses a null content set rather than reading a default one.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        var act = () => ForgeTuning.Read(null!);

        Should.Throw<ArgumentNullException>(act);
    }
}
