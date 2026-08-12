using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C7 — `15` §B4 step 6: <em>"Export -&gt; PNG-32, then compress with pngquant (quality
/// 80-95)"</em>.
/// </summary>
public sealed class ExportStepTests
{
    /// <summary>
    /// 🔒 `15` §C's delivery format is "PNG-32 straight alpha". Straight, not premultiplied: a
    /// premultiplied round trip quantises colour in low-alpha pixels, which is the halo step 1
    /// spent its whole run removing. The probe pixel is exactly #80FF0000 and must come back
    /// exactly #80FF0000.
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
    /// 🔒 pngquant is a native binary and this toolchain has none, so the lossy half of step 6 does
    /// not run as written. The step says so on the result rather than reporting a clean pass over
    /// an uncompressed file — the difference is what stops "step 6 succeeded" from meaning two
    /// different things in a batch report.
    /// </summary>
    [Fact]
    public void Run_declares_that_15_B4_step_6s_pngquant_stage_did_not_run()
    {
        var (image, _, _) = SyntheticAsset.SemiTransparentProbe();

        var result = new ExportStep().Run(Input(image));

        result.Deviations.ShouldNotBeEmpty();
        var deviation = result.Deviations
            .Single(declared => declared.Id == ExportStep.PngquantDeviationId);
        var text = $"{deviation.Requirement} {deviation.Taken} {deviation.Why}";
        text.ShouldContain("pngquant", Case.Sensitive);
    }

    private static AssetStepInput Input(SkiaSharp.SKBitmap image) => new(
        image,

        // Step 6 encodes what it is handed; the row's 96x96 delivery size rides along unused, the
        // same way it does for step 1.
        TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon),
        StatedThresholds.ForSyntheticFixtures());
}
