using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C5 — `15` §B4 step 4: <em>"Outline repair -&gt; ensure the outline is continuous and uniform
/// width"</em>.
/// </summary>
public sealed class OutlineRepairStepTests
{
    /// <summary>
    /// How far a repaired pixel may sit from `15` §A3's #231A2E, in RGB units.
    /// </summary>
    /// <remarks>
    /// 🔒 The case's own stated tolerance for a synthetic fixture, not a calibration of
    /// <see cref="ThresholdKeys.OutlineColourTolerance"/>. The fixture is painted without
    /// antialiasing, so a morphological close that paints the outline colour lands exactly on it;
    /// 16 leaves room for an implementation that blends a closed pixel with its neighbours, and is
    /// still nowhere near the 228 units that separate #231A2E from the background it replaced.
    /// </remarks>
    private const double RepairedColourTolerance = 16d;

    /// <summary>
    /// How far the measured outline width may sit from the fixture's constructed three pixels.
    /// </summary>
    /// <remarks>
    /// 🔒 The case's own stated tolerance. The outline is a radial band of exactly three pixels by
    /// construction, but any measurement over a rasterised circle discretises; half a pixel is the
    /// most that can cost, and it is not a statement about `15` §A3's 3-4 px band, which QA item 3
    /// owns.
    /// </remarks>
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

    /// <summary>
    /// 🔒 Step 4 repairs a break. A step that repainted the whole outline would also pass the case
    /// above, and would have quietly thickened every asset in the batch.
    /// </summary>
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

    /// <summary>
    /// 🔒 The step <em>measures</em> width and does not judge it. Conformance against `15` §A3's
    /// 3-4 px band belongs to QA item 3; a step that both changed the width and graded it would be
    /// marking its own homework.
    /// </summary>
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
