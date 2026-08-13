using Shouldly;
using SkiaSharp;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C3 — `15` §B4 step 2: <em>"Trim to content -&gt; then pad to the target canvas with the subject
/// centered"</em>, honouring `15` §C's two pivots.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>"The target canvas" is the WORKING canvas — the `15` §C generation canvas the image
/// arrived on — not the §C delivery size.</b> §B4 lists step 2 and step 5 ("Resize -&gt; to the spec
/// size in the manifest") separately; under the other reading step 5 is an identity resample on
/// every asset and §B4's own step 5 is dead text. These cases therefore assert against the source
/// image's own dimensions and never against <c>spec.TargetSize</c>.
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
    /// 🔒 <b>The case that replaces "content larger than the target canvas fails loudly".</b> That
    /// contract no longer exists, and it was wrong: it measured the trimmed content against the
    /// <em>delivery</em> canvas and refused an ordinary `15` §C generation — a subject spanning most
    /// of the generation frame, for a row that delivers smaller — before step 5, the step whose
    /// entire job is that downscale, ever ran. Content trimmed out of an image can never exceed that
    /// image, so padding back to the image's own dimensions has no oversize case at all. Here the
    /// subject is 20x10 against an 8x8 delivery size: this used to throw, and must now re-frame.
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

        // The delivery size really is smaller than the content, so the old guard's condition holds
        // on this input and only the ruling stops it throwing.
        spec.TargetSize.Width.ShouldBeLessThan(ContentWidth);
        result.Image.Width.ShouldBe(WorkingCanvas);
        result.Image.Height.ShouldBe(WorkingCanvas);
        result.Image.Width.ShouldNotBe(spec.TargetSize.Width);
        result.Outcome.ShouldBe(StepOutcome.Applied);
        Pixels.OpaqueBounds(result.Image).ShouldBe(new SKRectI(22, 27, 42, 37));
    }

    /// <summary>
    /// 🔒 An image `15` §B4 step 1 keyed away entirely has no subject to centre, and inventing a
    /// placement for nothing would hand step 5 an empty frame that looks like a clean asset.
    /// </summary>
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
