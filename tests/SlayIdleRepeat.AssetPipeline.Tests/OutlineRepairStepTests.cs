using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

public sealed class OutlineRepairStepTests
{
    /// <summary>
    /// This case's own tolerance for the synthetic fixture, not a calibration of
    /// <see cref="ThresholdKeys.OutlineColourTolerance"/>: leaves room for an implementation that
    /// blends a closed pixel with its neighbours, well short of the background colour it replaced.
    /// </summary>
    private const double RepairedColourTolerance = 16d;

    /// <summary>
    /// Rasterising a circle discretises the constructed three-pixel width by up to half a pixel;
    /// this is that measurement slack, not a statement about the authorised width band (QA item 3
    /// owns that).
    /// </summary>
    private const double WidthMeasurementTolerance = 0.5d;

    [Fact]
    public void Run_closes_a_known_break_in_the_outline()
    {
        var fixture = SyntheticAsset.ChibiWithOutlineGap();
        fixture.GapPixels.Count.ShouldBeGreaterThan(
            3, "an empty gap would make the ShouldAllBe below vacuously true");

        var result = new OutlineRepairStep().Run(Input(fixture));

        var distances = Pixels.ColoursAt(result.Image, fixture.GapPixels)
            .Select(colour => Pixels.RgbDistance(colour, Doc15Authorised.OutlineColour))
            .ToArray();
        distances.Length.ShouldBe(fixture.GapPixels.Count);
        distances.ShouldAllBe(distance => distance <= RepairedColourTolerance);
    }

    /// <summary>A step that repainted the whole outline would also pass the case above, quietly thickening every asset.</summary>
    [Fact]
    public void Run_leaves_outline_pixels_far_from_the_break_exactly_as_they_were()
    {
        var fixture = SyntheticAsset.ChibiWithOutlineGap();
        fixture.OutlinePixelsFarFromGap.Count.ShouldBeGreaterThan(
            50, "an empty set would make the comparison below vacuously true");
        var before = Pixels.ColoursAt(fixture.Image, fixture.OutlinePixelsFarFromGap);

        var result = new OutlineRepairStep().Run(Input(fixture));

        Pixels.ColoursAt(result.Image, fixture.OutlinePixelsFarFromGap).ShouldBe(before);
    }

    /// <summary>The step measures width but does not judge it; grading it would be marking its own homework.</summary>
    [Fact]
    public void Run_measures_the_outline_width_it_found()
    {
        var fixture = SyntheticAsset.Chibi();

        var result = new OutlineRepairStep().Run(Input(fixture));

        result.Measurements.ShouldNotBeEmpty();
        var width = result.Measurements
            .Single(measurement => measurement.Key == OutlineRepairStep.OutlineWidthMeasurement);
        width.Value.ShouldBe(SyntheticAsset.OutlineWidth, WidthMeasurementTolerance);
    }

    private static AssetStepInput Input(SyntheticFixture fixture) => new(
        fixture.Image,
        TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
        StatedThresholds.ForSyntheticFixtures());
}
