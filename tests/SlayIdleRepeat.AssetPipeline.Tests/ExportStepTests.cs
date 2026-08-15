using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

public sealed class ExportStepTests
{
    /// <summary>
    /// Alpha must stay straight, not premultiplied: a premultiplied round trip quantises colour in
    /// low-alpha pixels, undoing the halo removal from step 1.
    /// </summary>
    [Fact]
    public void Run_emits_a_png_whose_straight_alpha_survives_a_round_trip_bit_for_bit()
    {
        var (image, probe, colour) = SyntheticAsset.SemiTransparentProbe();
        colour.Alpha.ShouldBe((byte)0x80, "the probe must be semi-transparent or it proves nothing");

        var result = new ExportStep().Run(Input(image));

        result.EncodedPng.ShouldNotBeNull();
        result.EncodedPng.Length.ShouldBeGreaterThan(0);
        using var decoded = Pixels.DecodePngAsStraightAlpha(result.EncodedPng);
        decoded.Width.ShouldBe(image.Width);
        decoded.Height.ShouldBe(image.Height);
        decoded.GetPixel(probe.X, probe.Y).ShouldBe(colour);
    }

    /// <summary>
    /// pngquant is a native binary this toolchain lacks, so the lossy half of step 6 does not run;
    /// the step must declare that deviation rather than silently reporting a clean pass.
    /// </summary>
    [Fact]
    public void Run_declares_that_15_B4_step_6s_pngquant_stage_did_not_run()
    {
        var (image, _, _) = SyntheticAsset.SemiTransparentProbe();
        var uncalibrated = ThresholdSet.Uncalibrated();
        uncalibrated.IsCalibrated(ThresholdKeys.ExportColourBudget).ShouldBeFalse(
            "the deviation is about the compression half not running, so the case is vacuous " +
            "against a set that would let it run");
        uncalibrated.IsCalibrated(ThresholdKeys.ExportMaxMeanError).ShouldBeFalse();

        var result = new ExportStep().Run(Input(image, uncalibrated));

        result.EncodedPng.ShouldNotBeNull(
            "`15` §B4 step 6's authorised, lossless half runs whatever the compression half does");
        result.Deviations.ShouldNotBeEmpty();
        var deviation = result.Deviations
            .Single(declared => declared.Id == ExportStep.PngquantDeviationId);
        var text = $"{deviation.Requirement} {deviation.Taken} {deviation.Why}";
        text.ShouldContain("pngquant", Case.Sensitive);
    }

    private static AssetStepInput Input(
        SkiaSharp.SKBitmap image, ThresholdSet? thresholds = null) => new(
        image,
        TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
        thresholds ?? StatedThresholds.ForSyntheticFixtures());
}
