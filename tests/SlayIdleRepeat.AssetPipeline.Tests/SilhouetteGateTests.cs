using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C14 — `15` §A4: <em>"fill it 100% black, scale to 64 px. If you cannot tell which character it
/// is, regenerate it."</em>
/// </summary>
/// <remarks>
/// 🔒 The first sentence is mechanised exactly and the second is not mechanised at all. These cases
/// pin the first, and pin that every result says so about the second — including on the results
/// where every measurement passed, which is where the omission would be least visible.
/// </remarks>
public sealed class SilhouetteGateTests
{
    /// <summary>The `15` §D1 category the registry cases key on.</summary>
    private const string CharacterCategory = "chr";

    /// <summary>Every cutoff `15` §A4 leaves to somebody else, one theory case each.</summary>
    public static TheoryData<string> EverySilhouetteThreshold() => new()
    {
        ThresholdKeys.SilhouetteMinCoverageRatio,
        ThresholdKeys.SilhouetteMinBoundingBoxFill,
        ThresholdKeys.SilhouetteMaxComponentCount,
        ThresholdKeys.SilhouetteMinDistinguishability,
    };

    [Fact]
    public void Render_produces_a_mask_at_exactly_the_64_px_15_A4_states()
    {
        var oversized = SyntheticAsset.SolidBlock(128, 96);

        var mask = SilhouetteGate.Render(oversized);

        mask.Width.ShouldBe(Doc15Authorised.SilhouetteMaskSize);
        mask.Height.ShouldBe(Doc15Authorised.SilhouetteMaskSize);
        Doc15Authorised.SilhouetteMaskSize.ShouldBe(64);
    }

    /// <summary>
    /// 🔒 "Fill it 100% black" means two colours and no third. A mask carrying an antialiased grey
    /// would make every measurement below depend on where the implementation drew its threshold.
    /// </summary>
    [Fact]
    public void Render_maps_every_pixel_to_either_opaque_black_or_fully_transparent()
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var mask = SilhouetteGate.Render(fixture.Image);

        var colours = Pixels.AllPixels(mask).Distinct().ToArray();
        colours.Length.ShouldBeInRange(1, 2);
        colours.ShouldAllBe(colour =>
            colour == SKColors.Black || colour.Alpha == 0);
    }

    /// <summary>
    /// 🔒 A known-geometry fixture with an exact answer: 1024 of the frame's 4096 pixels are opaque,
    /// the input is already 64x64 so nothing is resampled, and the mask must therefore hold exactly
    /// 1024 black pixels. An off-by-a-resampler implementation cannot pass this.
    /// </summary>
    [Fact]
    public void Render_maps_a_known_geometrys_alpha_exactly_pixel_for_pixel()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            Doc15Authorised.SilhouetteMaskSize, 32, 32, Doc15Pivots.Center);
        const int expectedOpaque = 32 * 32;

        var mask = SilhouetteGate.Render(fixture.Image);

        Pixels.AlphaBytes(mask).Count(alpha => alpha == 255).ShouldBe(expectedOpaque);
        Pixels.OpaqueBounds(mask).ShouldBe(fixture.ContentBounds);
    }

    /// <summary>
    /// 🔒 Only alpha decides. A fully opaque frame painted in the biome's own colours is entirely
    /// silhouette, and a renderer keyed on luminance instead would report a hole where the light
    /// hues are.
    /// </summary>
    [Fact]
    public void Render_reads_alpha_and_not_colour()
    {
        var opaque = SyntheticAsset.SolidBlock(
            Doc15Authorised.SilhouetteMaskSize, Doc15Authorised.SilhouetteMaskSize);

        var mask = SilhouetteGate.Render(opaque);

        var alphas = Pixels.AlphaBytes(mask);
        alphas.Length.ShouldBe(
            Doc15Authorised.SilhouetteMaskSize * Doc15Authorised.SilhouetteMaskSize);
        alphas.ShouldAllBe(alpha => alpha == 255);
    }

    [Fact]
    public void Measure_reports_the_four_quantities_15_A4s_floor_is_made_of()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            Doc15Authorised.SilhouetteMaskSize, 32, 32, Doc15Pivots.Center);
        var mask = SilhouetteGate.Render(fixture.Image);

        var measurement = SilhouetteGate.Measure(mask, CharacterCategory, SilhouetteRegistry.Empty);

        measurement.CoverageRatio.ShouldBe(0.25d, 0.001d);
        measurement.BoundingBoxFill.ShouldBe(1d, 0.001d);
        measurement.ConnectedComponentCount.ShouldBe(1);
        measurement.Distinguishability.ShouldBe(SilhouetteGate.MaximumDistinguishability);
    }

    /// <summary>
    /// 🔒 A rectangle fills its own bounding box completely and a chibi does not. Without the second
    /// half, an implementation returning a constant 1 would pass the case above.
    /// </summary>
    [Fact]
    public void Measure_reports_a_bounding_box_fill_below_one_for_a_shape_that_is_not_a_rectangle()
    {
        var mask = SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image);

        var measurement = SilhouetteGate.Measure(mask, CharacterCategory, SilhouetteRegistry.Empty);

        measurement.BoundingBoxFill.ShouldBeLessThan(1d);
        measurement.BoundingBoxFill.ShouldBeGreaterThan(0d);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Measure_counts_one_component_per_separated_blob(int blobs)
    {
        var (image, _) = SyntheticAsset.SeparatedBlobs(blobs);
        var mask = SilhouetteGate.Render(image);

        var measurement = SilhouetteGate.Measure(mask, CharacterCategory, SilhouetteRegistry.Empty);

        measurement.ConnectedComponentCount.ShouldBe(blobs);
    }

    /// <summary>
    /// 🔒 `15` §A4's bar is about telling one asset apart from another, so the bottom of the scale is
    /// a silhouette identical to one already accepted: nothing distinguishes it at all.
    /// </summary>
    [Fact]
    public void Measure_reports_the_minimum_distinguishability_against_an_identical_accepted_mask()
    {
        var mask = SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image);
        var registry = SilhouetteRegistry.Empty.Accept(
            ManifestRows.BiomeScopedCharacter, SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image));
        registry.Count.ShouldBe(1);

        var measurement = SilhouetteGate.Measure(mask, CharacterCategory, registry);

        measurement.Distinguishability.ShouldBe(SilhouetteGate.MinimumDistinguishability);
    }

    /// <summary>
    /// 🔒 And the other end: a silhouette sharing almost nothing with the accepted one reads higher.
    /// A measurement that returned the minimum for everything would pass the case above.
    /// </summary>
    [Fact]
    public void Measure_reports_a_larger_distinguishability_against_a_very_different_accepted_mask()
    {
        var (blobs, _) = SyntheticAsset.SeparatedBlobs(1);
        var registry = SilhouetteRegistry.Empty.Accept(
            ManifestRows.BiomeScopedCharacter, SilhouetteGate.Render(blobs));
        var mask = SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image);

        var measurement = SilhouetteGate.Measure(mask, CharacterCategory, registry);

        measurement.Distinguishability.ShouldBeGreaterThan(SilhouetteGate.MinimumDistinguishability);
    }

    /// <summary>
    /// 🔒 `15` Part F item 11 and §A4 both say "in the same category". A registry holding only icons
    /// tells a character nothing, so a character measured against it is as distinguishable as a
    /// first asset.
    /// </summary>
    [Fact]
    public void Measure_ignores_accepted_silhouettes_from_a_different_15_D1_category()
    {
        var registry = SilhouetteRegistry.Empty.Accept(
            ManifestRows.NonBiomeUiIcon, SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image));
        registry.Count.ShouldBe(1);
        var mask = SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image);

        var measurement = SilhouetteGate.Measure(mask, CharacterCategory, registry);

        measurement.Distinguishability.ShouldBe(SilhouetteGate.MaximumDistinguishability);
    }

    /// <summary>
    /// 🔒 The gate is a measuring instrument, and an instrument with no scale is broken rather than
    /// permissive. It throws naming the key; `15` Part F item 1 is the seam that turns that into a
    /// verdict so one hole cannot abort a 942-asset batch.
    /// </summary>
    [Theory]
    [MemberData(nameof(EverySilhouetteThreshold))]
    public void Evaluate_throws_naming_the_key_when_one_of_15_A4s_four_cutoffs_is_null(string key)
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var exception = Should.Throw<UncalibratedThresholdException>(() => SilhouetteGate.Evaluate(
            fixture.Image, CharacterCategory, SilhouetteRegistry.Empty, QaThresholds.Except(key)));

        exception.Key.ShouldBe(key);
        exception.Message.ShouldContain(key, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <b>Asserted on a passing fixture on purpose.</b> This is the case where carrying `15`
    /// §A4's sentence looks redundant and matters most: four cleared cutoffs are not the test the
    /// doc describes, and the result says so in its own field rather than in a comment.
    /// </summary>
    [Fact]
    public void Evaluate_carries_the_human_gap_even_when_every_measurement_passes()
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var result = SilhouetteGate.Evaluate(
            fixture.Image, CharacterCategory, SilhouetteRegistry.Empty, QaThresholds.All());

        result.MechanicalPass.ShouldBeTrue();
        result.HumanGap.ShouldNotBeNullOrWhiteSpace();
        result.HumanGap.ShouldContain(Doc15PartF.SilhouetteAcceptanceSentence, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Every measurement travels with the result whatever the verdict, so a reviewer can tell a
    /// near miss from a wide one without rerunning the gate.
    /// </summary>
    [Fact]
    public void Evaluate_carries_all_four_measurements_whatever_it_concluded()
    {
        var (image, _) = SyntheticAsset.SeparatedBlobs(1);

        var result = SilhouetteGate.Evaluate(
            image, CharacterCategory, SilhouetteRegistry.Empty, QaThresholds.All());

        result.MechanicalPass.ShouldBeFalse();
        result.Measurements.Select(m => m.Key).ToArray().ShouldBe(
            [
                SilhouetteGate.CoverageRatioMeasurement,
                SilhouetteGate.BoundingBoxFillMeasurement,
                SilhouetteGate.ComponentCountMeasurement,
                SilhouetteGate.DistinguishabilityMeasurement,
            ],
            ignoreOrder: true);
        result.HumanGap.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_empty_registry_holds_nothing_in_any_category()
    {
        SilhouetteRegistry.Empty.Count.ShouldBe(0);
        SilhouetteRegistry.Empty.Categories.ShouldBeEmpty();
        SilhouetteRegistry.Empty.InCategory(CharacterCategory).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 Immutable, like <see cref="ThresholdSet"/>: accepting a silhouette must not widen the set
    /// somebody else is being judged against as a side effect.
    /// </summary>
    [Fact]
    public void Accept_returns_a_new_registry_and_leaves_the_original_alone()
    {
        var mask = SilhouetteGate.Render(SyntheticAsset.ChibiCutOut().Image);
        var original = SilhouetteRegistry.Empty;

        var extended = original.Accept(ManifestRows.BiomeScopedCharacter, mask);

        original.Count.ShouldBe(0);
        extended.Count.ShouldBe(1);
        extended.InCategory(CharacterCategory).Single().AssetId
            .ShouldBe(ManifestRows.BiomeScopedCharacter);
    }
}
