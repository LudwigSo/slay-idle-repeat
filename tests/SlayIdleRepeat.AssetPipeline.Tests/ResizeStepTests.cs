using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C6 — `15` §B4 step 5: <em>"Resize -&gt; to the spec size in the manifest (Lanczos, then sharpen
/// 0.4)"</em>.
/// </summary>
public sealed class ResizeStepTests
{
    private const int TargetEdge = 32;

    [Fact]
    public void Run_produces_exactly_the_delivery_size_the_manifest_states()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var spec = TestSpecs.WithTargetSize(ManifestRows.NonBiomeUiIcon, TargetEdge, TargetEdge);

        var result = new ResizeStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Image.Width.ShouldBe(spec.TargetSize.Width);
        result.Image.Height.ShouldBe(spec.TargetSize.Height);
    }

    /// <summary>
    /// 🔒 Sharpening the alpha channel re-creates precisely the semi-transparent fringe `15` §B4
    /// step 1 exists to remove — and `15` Part F item 6 then fails the asset for it. The claim is
    /// bit-level, so it is asserted over the raw alpha bytes of the resample alone, which is the
    /// only image that can say what the sharpen was supposed to leave untouched.
    /// </summary>
    [Fact]
    public void Run_sharpens_rgb_and_leaves_every_alpha_byte_exactly_as_the_resample_left_it()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var spec = TestSpecs.WithTargetSize(ManifestRows.NonBiomeUiIcon, TargetEdge, TargetEdge);
        var step = new ResizeStep();

        var resampled = step.Resample(fixture.Image, spec.TargetSize);
        var result = step.Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        Pixels.AlphaBytes(resampled).Length.ShouldBe(
            TargetEdge * TargetEdge, "an empty buffer would make the comparison below vacuous");
        Pixels.AlphaBytes(result.Image).ShouldBe(Pixels.AlphaBytes(resampled));
        Pixels.RgbBytes(result.Image).ShouldNotBe(
            Pixels.RgbBytes(resampled), "a sharpen of amount 0.4 that changed no colour did not run");
    }

    /// <summary>
    /// 🔒 There is no Lanczos resampler in SkiaSharp and shelling out to one is forbidden, so the
    /// step takes a documented deviation rather than quietly resampling differently from what
    /// `15` §B4 says.
    /// </summary>
    [Fact]
    public void Run_declares_that_15_B4_step_5s_Lanczos_resampler_was_not_available()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var spec = TestSpecs.WithTargetSize(ManifestRows.NonBiomeUiIcon, TargetEdge, TargetEdge);

        var result = new ResizeStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Deviations.ShouldNotBeEmpty();
        var deviation = result.Deviations
            .Single(declared => declared.Id == ResizeStep.LanczosDeviationId);
        deviation.Requirement.ShouldContain("Lanczos", Case.Sensitive);
    }
}
