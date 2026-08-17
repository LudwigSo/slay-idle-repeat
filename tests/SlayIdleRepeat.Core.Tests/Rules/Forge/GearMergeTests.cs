using Shouldly;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Forge;

/// <summary>
/// The fusion rule: which selections are legal, and what the item a legal one produces carries.
/// </summary>
public sealed class GearMergeTests
{
    /// <summary>Three matching items of a band below the top are a legal fusion.</summary>
    [Fact]
    public void A_matching_triple_below_the_top_band_is_a_legal_fusion()
    {
        GearMerge.Refusal(Forges.Triple(), dustSubstituted: false, Forges.Tuning).ShouldBeNull();
    }

    /// <summary>
    /// 🔒 Each refusal is named, not merely counted. Six independent rules can refuse one selection
    /// and four map onto one wire rejection, so a case asserting only "it was refused" could not
    /// tell them apart and five of them could be broken with it green (steering S2).
    /// </summary>
    /// <param name="count">How many real items the selection names.</param>
    /// <param name="dust">Whether dust fills a slot.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    public void A_selection_that_does_not_add_up_to_the_authored_input_count_is_refused(
        int count, bool dust)
    {
        var inputs = Forges.Triple().Take(count).ToArray();

        if (count == 4)
        {
            inputs = [.. inputs, Inventories.Item("merge_d")];
        }

        GearMerge.Refusal(inputs, dust, Forges.Tuning)
            .ShouldBe(MergeRefusal.WRONG_INPUT_COUNT);
    }

    /// <summary>Dust may fill no more slots than the document lets it.</summary>
    [Fact]
    public void Dust_may_not_fill_a_slot_the_document_does_not_let_it_fill()
    {
        var noDust = ForgeTuningFor(dustSubstituteMaxInputs: 0);

        GearMerge.Refusal(Forges.Triple().Take(2).ToArray(), dustSubstituted: true, noDust)
            .ShouldBe(MergeRefusal.DUST_SUBSTITUTION_NOT_ALLOWED);
    }

    /// <summary>One item named twice would fuse an item with itself and consume it once.</summary>
    [Fact]
    public void An_instance_named_twice_is_refused_as_a_duplicate()
    {
        var triple = Forges.Triple();

        GearMerge.Refusal([triple[0], triple[1], triple[1]], dustSubstituted: false, Forges.Tuning)
            .ShouldBe(MergeRefusal.DUPLICATE_INPUT);
    }

    /// <summary>The inputs must all be the same base item.</summary>
    [Fact]
    public void A_selection_spanning_two_base_items_is_refused_as_mismatched()
    {
        var triple = Forges.Triple();

        GearMerge.Refusal(
                [triple[0], triple[1], Inventories.Item("merge_c", GearFamily.AXE)],
                dustSubstituted: false,
                Forges.Tuning)
            .ShouldBe(MergeRefusal.MISMATCHED_ITEM);
    }

    /// <summary>The inputs must all be of one band.</summary>
    [Fact]
    public void A_selection_spanning_two_bands_is_refused_as_mismatched()
    {
        var triple = Forges.Triple();

        GearMerge.Refusal(
                [triple[0], triple[1], Inventories.Item("merge_c", rarity: Rarity.B)],
                dustSubstituted: false,
                Forges.Tuning)
            .ShouldBe(MergeRefusal.MISMATCHED_RARITY);
    }

    /// <summary>The inputs must all stand at one enhancement level.</summary>
    [Fact]
    public void A_selection_spanning_two_enhancement_levels_is_refused_as_mismatched()
    {
        var triple = Forges.Triple();

        GearMerge.Refusal(
                [triple[0], triple[1], Inventories.Item("merge_c", enhanceLevel: 1)],
                dustSubstituted: false,
                Forges.Tuning)
            .ShouldBe(MergeRefusal.MISMATCHED_ENHANCE_LEVEL);
    }

    /// <summary>Nothing merges out of the top of the ladder, so there is no band to fuse onto.</summary>
    [Fact]
    public void A_triple_at_the_top_band_has_no_band_to_fuse_onto()
    {
        GearMerge.Refusal(Forges.Triple(Rarity.SS), dustSubstituted: false, Forges.Tuning)
            .ShouldBe(MergeRefusal.NO_HIGHER_RARITY);
    }

    /// <summary>Every rung below the top fuses onto the one above it.</summary>
    /// <param name="input">The band the inputs share.</param>
    /// <param name="output">The band the fusion lands on.</param>
    [Theory]
    [InlineData(Rarity.C, Rarity.B)]
    [InlineData(Rarity.B, Rarity.A)]
    [InlineData(Rarity.A, Rarity.S)]
    [InlineData(Rarity.S, Rarity.SS)]
    public void A_fusion_lands_on_the_rung_above_its_inputs(Rarity input, Rarity output)
    {
        GearMerge.Fuse(Forges.Triple(input), Forges.Drops, Forges.Draws())
            .Rarity.ShouldBe(output);
    }

    /// <summary>
    /// 🔒 The output takes the HIGHEST quality of its inputs, and the fixture makes that
    /// discriminating: the maximum is neither the first input's nor the last's, so a rule reading
    /// either position by accident answers a different number.
    /// </summary>
    [Fact]
    public void The_output_takes_the_highest_quality_of_its_inputs()
    {
        var inputs = new[]
        {
            Inventories.Item("merge_a", quality: 0.2),
            Inventories.Item("merge_b", quality: 0.9),
            Inventories.Item("merge_c", quality: 0.4),
        };

        GearMerge.Fuse(inputs, Forges.Drops, Forges.Draws()).Quality.ShouldBe(0.9);
    }

    /// <summary>
    /// 🔒 The output takes the highest chapter of origin, and this is the load-bearing one: item
    /// power doubles per chapter, so without the maximum two fusions of what look like the same
    /// three items could legally differ by a factor of sixteen.
    /// </summary>
    [Fact]
    public void The_output_takes_the_highest_chapter_of_origin_of_its_inputs()
    {
        var inputs = new[]
        {
            Inventories.Item("merge_a", chapterOrigin: 2),
            Inventories.Item("merge_b", chapterOrigin: 6),
            Inventories.Item("merge_c", chapterOrigin: 3),
        };

        GearMerge.Fuse(inputs, Forges.Drops, Forges.Draws()).ChapterOrigin.ShouldBe(6);
    }

    /// <summary>
    /// 🔒 The mercy counter is inherited at its highest, so a player chasing an enhancement is never
    /// punished for merging mid-chase.
    /// </summary>
    [Fact]
    public void The_output_inherits_the_highest_mercy_counter_of_its_inputs()
    {
        var inputs = new[]
        {
            Inventories.Item("merge_a", enhanceFailures: 1),
            Inventories.Item("merge_b", enhanceFailures: 5),
            Inventories.Item("merge_c", enhanceFailures: 2),
        };

        GearMerge.Fuse(inputs, Forges.Drops, Forges.Draws()).EnhanceFailures.ShouldBe(5);
    }

    /// <summary>The shared enhancement level survives the fusion — the input rule already requires one.</summary>
    [Fact]
    public void The_output_keeps_the_enhancement_level_its_inputs_shared()
    {
        GearMerge.Fuse(Forges.Triple(enhanceLevel: 4), Forges.Drops, Forges.Draws())
            .EnhanceLevel.ShouldBe(4);
    }

    /// <summary>The output is the same base item its inputs were, worn in the same slot.</summary>
    [Fact]
    public void The_output_is_the_same_base_item_as_its_inputs()
    {
        var inputs = Forges.Triple(family: GearFamily.AXE);

        var fused = GearMerge.Fuse(inputs, Forges.Drops, Forges.Draws());

        fused.DefId.ShouldBe(inputs[0].DefId);
        fused.Family.ShouldBe(GearFamily.AXE);
        fused.Slot.ShouldBe(inputs[0].Slot);
    }

    /// <summary>
    /// The output keeps the first named input's identity — three go in, one comes out, and the
    /// domain has no way to mint a new identity because identity is a port.
    /// </summary>
    [Fact]
    public void The_output_keeps_the_first_inputs_identity()
    {
        var inputs = Forges.Triple();

        GearMerge.Fuse(inputs, Forges.Drops, Forges.Draws())
            .InstanceId.ShouldBe(inputs[0].InstanceId);
    }

    /// <summary>
    /// The affixes are re-rolled at the NEW band's count, not carried over. The bottom band rolls
    /// none and the one above it rolls some, so the count moving is the observable half.
    /// </summary>
    [Fact]
    public void The_output_re_rolls_its_affixes_at_the_new_bands_count()
    {
        var inputs = Forges.Triple();

        inputs[0].Affixes.ShouldBeEmpty("the bottom band rolls no affixes");

        var fused = GearMerge.Fuse(inputs, Forges.Drops, Forges.Draws());

        fused.Affixes.Count.ShouldBe(
            Forges.Drops.Band(Rarity.B).AffixCount,
            "the fusion rolls what its OUTPUT band authors, not what its inputs carried");
    }

    /// <summary>
    /// 🔒 A dust-filled slot contributes to none of the maxima — the rule takes the maximum over the
    /// REAL inputs, and a fusion of two ordinary items must not inherit anything from the slot dust
    /// paid for.
    /// </summary>
    /// <param name="highestFirst">
    /// Whether the higher-valued item is named first. Both orders are run because a two-item
    /// selection has no middle: descending alone cannot tell "the maximum" from "the first named",
    /// and ascending alone cannot tell it from "the last named". Together they can.
    /// </param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_dust_filled_slot_contributes_no_quality_and_no_chapter(bool highestFirst)
    {
        var higher = Inventories.Item("merge_a", quality: 0.4, chapterOrigin: 3, enhanceFailures: 2);
        var lower = Inventories.Item("merge_b", quality: 0.2, chapterOrigin: 2, enhanceFailures: 1);

        var pair = highestFirst ? new[] { higher, lower } : [lower, higher];

        GearMerge.Refusal(pair, dustSubstituted: true, Forges.Tuning).ShouldBeNull();

        var fused = GearMerge.Fuse(pair, Forges.Drops, Forges.Draws());

        fused.Quality.ShouldBe(0.4);
        fused.ChapterOrigin.ShouldBe(3);
        fused.EnhanceFailures.ShouldBe(2);
    }

    /// <summary>The same selection and the same seed fuse the same item, affixes included.</summary>
    [Fact]
    public void A_fusion_is_the_same_item_on_a_second_pass_over_the_same_seed()
    {
        var first = GearMerge.Fuse(Forges.Triple(Rarity.A), Forges.Drops, Forges.Draws());
        var second = GearMerge.Fuse(Forges.Triple(Rarity.A), Forges.Drops, Forges.Draws());

        second.ShouldBe(first);
    }

    /// <summary>Two seeds roll different affixes, so the re-roll is a draw rather than a constant.</summary>
    [Fact]
    public void Two_seeds_roll_different_affixes_onto_the_same_selection()
    {
        var first = GearMerge.Fuse(Forges.Triple(Rarity.A), Forges.Drops, Forges.Draws(1));
        var second = GearMerge.Fuse(Forges.Triple(Rarity.A), Forges.Drops, Forges.Draws(2));

        second.ShouldNotBe(first);
    }

    /// <summary>A caller that skipped the refusal check and fused at the top band is a defect, not a rejection.</summary>
    [Fact]
    public void Fusing_at_the_top_band_is_refused_loudly()
    {
        var act = () => GearMerge.Fuse(Forges.Triple(Rarity.SS), Forges.Drops, Forges.Draws());

        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("NO_HIGHER_RARITY");
    }

    /// <summary>A fusion of nothing has no base item, no band and no identity to give its output.</summary>
    [Fact]
    public void Fusing_an_empty_selection_is_refused_loudly()
    {
        var act = () => GearMerge.Fuse([], Forges.Drops, Forges.Draws());

        Should.Throw<ArgumentException>(act);
    }

    private static Core.Content.ForgeTuning ForgeTuningFor(int dustSubstituteMaxInputs) =>
        Core.Content.ForgeTuning.Read(ForgeDocuments.With(
            dustSubstituteMaxInputs: Core.Content.ContentValue.Number(dustSubstituteMaxInputs)));
}
