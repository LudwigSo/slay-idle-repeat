using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

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
    /// Sharpening the alpha channel would re-create the semi-transparent fringe that background
    /// removal exists to remove, so this is asserted bit-level over the raw alpha bytes.
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

    /// <summary>SkiaSharp has no Lanczos resampler and shelling out to one is forbidden, so the step declares a deviation.</summary>
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

    /// <summary>
    /// Generation is on a square canvas but mounts deliver at 512x384, with no authorised way to
    /// reconcile them, so the step resamples non-uniformly and reports the contradiction as data
    /// rather than silently inventing a convention.
    /// </summary>
    [Fact]
    public void Run_declares_the_15_C_delivery_aspect_contradiction_naming_both_sizes()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var spec = TestSpecs.FromRow(ManifestRows.NonSquareDeliveryMount);

        spec.TargetSize.Width.ShouldNotBe(spec.TargetSize.Height);
        fixture.Image.Width.ShouldBe(fixture.Image.Height);

        var result = new ResizeStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        var contradiction = result.Contradictions
            .ShouldHaveSingleItem();
        contradiction.Id.ShouldBe(ResizeStep.DeliveryAspectContradictionId);
        contradiction.Detail.ShouldContain(
            $"{fixture.Image.Width}×{fixture.Image.Height}", Case.Sensitive);
        contradiction.Detail.ShouldContain(spec.TargetSize.ToString(), Case.Sensitive);
        contradiction.Detail.ShouldContain("512×384", Case.Sensitive);
        contradiction.Detail.ShouldContain("1080×1440", Case.Sensitive);
    }

    /// <summary>Firing the contradiction on a square-to-square resample would bury the rows that actually disagree.</summary>
    [Fact]
    public void Run_declares_no_contradiction_when_the_delivery_size_keeps_the_generation_aspect()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        var spec = TestSpecs.WithTargetSize(ManifestRows.NonBiomeUiIcon, TargetEdge, TargetEdge);

        var result = new ResizeStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Contradictions.ShouldBeEmpty();

        result.Deviations.Select(declared => declared.Id).ToArray()
            .ShouldBe([ResizeStep.LanczosDeviationId]);
    }
}
