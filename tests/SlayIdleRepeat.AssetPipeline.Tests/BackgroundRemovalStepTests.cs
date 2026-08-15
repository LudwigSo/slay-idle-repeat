using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

public sealed class BackgroundRemovalStepTests
{
    /// <summary>
    /// This case's own tolerance for the synthetic fixture, not a calibration of
    /// <see cref="ThresholdKeys.MatteDecontaminationStrength"/>: the fixture's edge is blended 50%
    /// toward white (~190 RGB units off), and 8 units back is a floor a no-op step cannot clear.
    /// </summary>
    private const double MinimumHaloImprovement = 8d;

    [Fact]
    public void Run_turns_the_keyed_background_fully_transparent()
    {
        var fixture = SyntheticAsset.ChibiWithHalo();
        fixture.BackgroundPixelsAwayFromEdge.Count.ShouldBeGreaterThan(
            500, "an empty set would make the ShouldAllBe below vacuously true");

        var result = new BackgroundRemovalStep().Run(Input(fixture));

        Pixels.AlphaAt(result.Image, fixture.BackgroundPixelsAwayFromEdge)
            .ShouldAllBe(alpha => alpha == 0);
    }

    [Fact]
    public void Run_leaves_the_subject_fully_opaque()
    {
        var fixture = SyntheticAsset.ChibiWithHalo();
        fixture.InteriorPixels.Count.ShouldBeGreaterThan(
            300, "an empty set would make the ShouldAllBe below vacuously true");

        var result = new BackgroundRemovalStep().Run(Input(fixture));

        Pixels.AlphaAt(result.Image, fixture.InteriorPixels).ShouldAllBe(alpha => alpha == 255);
    }

    /// <summary>Measured against the input's own distance, so the case cannot pass on a different wrong answer.</summary>
    [Fact]
    public void Run_decontaminates_the_haloed_edge_back_toward_the_15_A3_outline_colour()
    {
        var fixture = SyntheticAsset.ChibiWithHalo();
        fixture.HaloedEdgePixels.Count.ShouldBeGreaterThan(
            20, "the mean below would otherwise be taken over almost nothing");
        var before = Pixels.MeanRgbDistance(
            fixture.Image, fixture.HaloedEdgePixels, fixture.OutlineColour);

        var result = new BackgroundRemovalStep().Run(Input(fixture));

        var after = Pixels.MeanRgbDistance(
            result.Image, fixture.HaloedEdgePixels, fixture.OutlineColour);
        after.ShouldBeLessThan(before - MinimumHaloImprovement);
    }

    private static AssetStepInput Input(SyntheticFixture fixture) => new(
        fixture.Image,
        TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
        StatedThresholds.ForSyntheticFixtures());
}
