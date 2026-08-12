using Shouldly;
using SkiaSharp;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C3 — `15` §B4 step 2: <em>"Trim to content -&gt; then pad to the target canvas with the subject
/// centered"</em>, honouring `15` §C's two pivots.
/// </summary>
/// <remarks>
/// The arithmetic is stated, not derived: a 20x10 subject on a 40x30 canvas leaves 20 px of
/// horizontal slack and 20 px of vertical slack, both even, so there is no rounding rule to guess
/// at. Centred puts it at (10, 10); bottom-aligned puts it at (10, 20).
/// </remarks>
public sealed class TrimToCanvasStepTests
{
    private const int ContentWidth = 20;
    private const int ContentHeight = 10;
    private const int TargetWidth = 40;
    private const int TargetHeight = 30;

    [Fact]
    public void Run_centres_the_trimmed_content_for_the_15_C_center_pivot()
    {
        var fixture = SyntheticAsset.OffCentreSubject(ContentWidth, ContentHeight);
        var spec = TestSpecs.WithTargetSizeAndPivot(
            ManifestRows.NonBiomeUiIcon, TargetWidth, TargetHeight, Doc15Pivots.Center);

        var result = new TrimToCanvasStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Image.Width.ShouldBe(TargetWidth);
        result.Image.Height.ShouldBe(TargetHeight);
        Pixels.OpaqueBounds(result.Image).ShouldBe(new SKRectI(10, 10, 30, 20));
    }

    [Fact]
    public void Run_bottom_aligns_the_trimmed_content_for_the_15_C_bottom_center_pivot()
    {
        var fixture = SyntheticAsset.OffCentreSubject(ContentWidth, ContentHeight);
        var spec = TestSpecs.WithTargetSizeAndPivot(
            ManifestRows.NonBiomeUiIcon, TargetWidth, TargetHeight, Doc15Pivots.BottomCenter);

        var result = new TrimToCanvasStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Image.Width.ShouldBe(TargetWidth);
        result.Image.Height.ShouldBe(TargetHeight);
        Pixels.OpaqueBounds(result.Image).ShouldBe(new SKRectI(10, 20, 30, 30));
    }

    /// <summary>
    /// 🔒 A crop here would delete art nobody asked to delete, and `15` §B4 puts size changes in
    /// step 5, not step 2. The message must name the asset and both sizes — a bare "content too
    /// large" over a 942-asset batch says nothing about which asset or by how much. The sizes are
    /// spelled the way M8-09's <c>PixelSize.ToString()</c> spells them.
    /// </summary>
    [Fact]
    public void Run_refuses_content_larger_than_the_target_canvas_naming_the_asset_and_both_sizes()
    {
        var fixture = SyntheticAsset.OffCentreSubject(ContentWidth, ContentHeight);
        var spec = TestSpecs.WithTargetSizeAndPivot(
            ManifestRows.NonBiomeUiIcon, 8, 8, Doc15Pivots.Center);
        var step = new TrimToCanvasStep();
        var input = new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures());

        var exception = Should.Throw<InvalidOperationException>(() => step.Run(input));

        exception.Message.ShouldMatchWildcard($"*{spec.Id}*{ContentWidth}×{ContentHeight}*8×8*");
    }
}
