using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C2 — `15` §B4 step 1: <em>"Background removal -&gt; true alpha, no halo (matte decontamination
/// on)"</em>.
/// </summary>
public sealed class BackgroundRemovalStepTests
{
    /// <summary>
    /// How much closer to `15` §A3's outline colour the decontaminated edge must come, in RGB units.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>This is the case's own stated tolerance for a synthetic fixture, not a calibration of
    /// <see cref="ThresholdKeys.MatteDecontaminationStrength"/>.</b> The fixture blends its edge a
    /// stated 50% toward white, which moves it roughly 190 RGB units away from #231A2E; asking for
    /// 8 units back is a floor that a step doing nothing cannot clear and that a step doing
    /// something imperfectly still can. Nothing about real generated art is claimed by it.
    /// </remarks>
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

    /// <summary>
    /// 🔒 `15` §A3 asks for "no halo", and the only way to say that mechanically about a stated
    /// fixture is that the contaminated pixels moved back toward the colour they were painted.
    /// Measured against the input's own distance, so the case cannot pass by the step producing a
    /// different wrong answer.
    /// </summary>
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

        // Step 1 reads no size and no pivot; the spec is here because every step takes one. The row
        // is real so nothing about it is invented.
        TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
        StatedThresholds.ForSyntheticFixtures());
}
