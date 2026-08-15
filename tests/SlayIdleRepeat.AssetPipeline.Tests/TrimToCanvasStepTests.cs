using Shouldly;
using SkiaSharp;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <remarks>
/// <para>
/// "The target canvas" means the working canvas the image arrived on, not the delivery size:
/// reading it as the delivery size would make the resize step an identity resample on every asset.
/// These cases therefore assert against the source image's own dimensions, never against
/// <c>spec.TargetSize</c>.
/// </para>
/// <para>
/// The arithmetic is stated, not derived: a 20x10 subject on the fixture's 64x64 frame leaves 44 px
/// of horizontal slack and 54 px of vertical slack, both even, so there is no rounding rule to
/// guess at. Centred puts it at (22, 27); bottom-aligned puts it at (22, 54).
/// </para>
/// </remarks>
public sealed class TrimToCanvasStepTests
{
    private const int ContentWidth = 20;
    private const int ContentHeight = 10;

    /// <summary>The working canvas every case here runs on — <see cref="SyntheticAsset"/>'s frame.</summary>
    private const int WorkingCanvas = SyntheticAsset.Canvas;

    /// <summary>
    /// A delivery size deliberately smaller than <see cref="ContentWidth"/>x<see cref="ContentHeight"/>,
    /// so a step that padded to the delivery canvas could not possibly produce these expectations.
    /// </summary>
    private const int DeliveryEdge = 8;

    [Fact]
    public void Run_centres_the_trimmed_content_for_the_15_C_center_pivot()
    {
        var fixture = SyntheticAsset.OffCentreSubject(ContentWidth, ContentHeight);
        var spec = TestSpecs.WithTargetSizeAndPivot(
            ManifestRows.NonBiomeUiIcon, DeliveryEdge, DeliveryEdge, Doc15Pivots.Center);

        var result = new TrimToCanvasStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Image.Width.ShouldBe(WorkingCanvas);
        result.Image.Height.ShouldBe(WorkingCanvas);
        Pixels.OpaqueBounds(result.Image).ShouldBe(new SKRectI(22, 27, 42, 37));
    }

    [Fact]
    public void Run_bottom_aligns_the_trimmed_content_for_the_15_C_bottom_center_pivot()
    {
        var fixture = SyntheticAsset.OffCentreSubject(ContentWidth, ContentHeight);
        var spec = TestSpecs.WithTargetSizeAndPivot(
            ManifestRows.NonBiomeUiIcon, DeliveryEdge, DeliveryEdge, Doc15Pivots.BottomCenter);

        var result = new TrimToCanvasStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Image.Width.ShouldBe(WorkingCanvas);
        result.Image.Height.ShouldBe(WorkingCanvas);
        Pixels.OpaqueBounds(result.Image).ShouldBe(new SKRectI(22, 54, 42, WorkingCanvas));
    }

    /// <summary>
    /// Replaces a prior contract of "content larger than the target canvas fails loudly", which was
    /// wrong: it measured trimmed content against the delivery canvas and refused an ordinary
    /// generation before the resize step (whose job is exactly that downscale) ever ran. Content
    /// trimmed out of an image can never exceed that image, so padding back to its own dimensions
    /// has no oversize case at all.
    /// </summary>
    [Fact]
    public void Run_pads_to_the_working_canvas_even_when_the_content_dwarfs_the_15_C_delivery_size()
    {
        var fixture = SyntheticAsset.OffCentreSubject(ContentWidth, ContentHeight);
        var spec = TestSpecs.WithTargetSizeAndPivot(
            ManifestRows.NonBiomeUiIcon, DeliveryEdge, DeliveryEdge, Doc15Pivots.Center);
        var step = new TrimToCanvasStep();

        var result = step.Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        spec.TargetSize.Width.ShouldBeLessThan(ContentWidth);
        result.Image.Width.ShouldBe(WorkingCanvas);
        result.Image.Height.ShouldBe(WorkingCanvas);
        result.Image.Width.ShouldNotBe(spec.TargetSize.Width);
        result.Outcome.ShouldBe(StepOutcome.Applied);
        Pixels.OpaqueBounds(result.Image).ShouldBe(new SKRectI(22, 27, 42, 37));
    }

    /// <summary>An image keyed away entirely has no subject to centre; inventing a placement for nothing would look like a clean asset.</summary>
    [Fact]
    public void Run_refuses_an_image_with_no_visible_pixel_naming_the_asset()
    {
        var blank = SyntheticAsset.NewBitmap(WorkingCanvas, WorkingCanvas);
        var spec = TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon);
        var step = new TrimToCanvasStep();
        var input = new AssetStepInput(blank, spec, StatedThresholds.ForSyntheticFixtures());

        var exception = Should.Throw<InvalidOperationException>(() => step.Run(input));

        exception.Message.ShouldContain(spec.Id, Case.Sensitive);
    }
}
