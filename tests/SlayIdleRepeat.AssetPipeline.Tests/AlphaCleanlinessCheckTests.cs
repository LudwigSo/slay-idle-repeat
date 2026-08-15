using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>Validates that alpha edges are clean — no white/black halo, no semi-transparent fringe.</summary>
public sealed class AlphaCleanlinessCheckTests
{
    private static readonly string[] ThresholdsItem6Needs =
    [
        ThresholdKeys.HaloMaxFringeRatio,
        ThresholdKeys.HaloMaxLuminanceDeviation,
    ];

    public static TheoryData<string> EveryThresholdItem6Needs()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdsItem6Needs)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>Ratios are 0-1 by construction, so this forgives every fringe amount.</summary>
    private const double EveryFringeAllowed = 1d;

    /// <summary>Eight-bit channels span 255, so this forgives every luminance deviation.</summary>
    private const double EveryHaloAllowed = 256d;

    [Fact]
    public void Evaluate_passes_an_asset_whose_every_pixel_is_fully_opaque_or_fully_transparent()
    {
        var fixture = SyntheticAsset.ChibiCutOut();
        Pixels.AlphaBytes(fixture.Image)
            .Count(alpha => alpha is not 0 and not 255)
            .ShouldBe(0, "the passing fixture must carry no partial alpha at all");

        var outcome = new AlphaCleanlinessCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(6);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.Measurements
            .Single(m => m.Key == AlphaCleanlinessCheck.FringeRatioMeasurement)
            .Value.ShouldBe(0d);
    }

    [Fact]
    public void Evaluate_fails_naming_the_fringe_ratio_when_the_edge_is_semi_transparent()
    {
        var fixture = SyntheticAsset.ChibiCutOutWithFringe();
        fixture.HaloedEdgePixels.Count.ShouldBeGreaterThan(
            50, "an unfringed fixture would make this case vacuous");

        var outcome = new AlphaCleanlinessCheck().Evaluate(
            Subject(
                fixture.Image,
                QaThresholds.All().With(ThresholdKeys.HaloMaxLuminanceDeviation, EveryHaloAllowed)));

        outcome.ItemNumber.ShouldBe(6);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            AlphaCleanlinessCheck.FringeRatioMeasurement, Case.Sensitive);

        outcome.Reason.ShouldNotContain(
            AlphaCleanlinessCheck.FringeLuminanceDeviationMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == AlphaCleanlinessCheck.FringeRatioMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    [Fact]
    public void Evaluate_fails_naming_the_luminance_deviation_when_the_fringe_runs_white()
    {
        var fixture = SyntheticAsset.ChibiCutOutWithFringe();

        var outcome = new AlphaCleanlinessCheck().Evaluate(
            Subject(
                fixture.Image,
                QaThresholds.All().With(ThresholdKeys.HaloMaxFringeRatio, EveryFringeAllowed)));

        outcome.ItemNumber.ShouldBe(6);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            AlphaCleanlinessCheck.FringeLuminanceDeviationMeasurement, Case.Sensitive);

        outcome.Reason.ShouldNotContain(
            AlphaCleanlinessCheck.FringeRatioMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == AlphaCleanlinessCheck.FringeLuminanceDeviationMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    [Theory]
    [MemberData(nameof(EveryThresholdItem6Needs))]
    public void Evaluate_reports_Uncalibrated_naming_the_key_when_one_hole_is_left_open(string key)
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var outcome = new AlphaCleanlinessCheck()
            .Evaluate(Subject(fixture.Image, QaThresholds.Except(key)));

        outcome.ItemNumber.ShouldBe(6);
        outcome.Verdict.ShouldBe(QaVerdict.Uncalibrated);
        outcome.Reason.ShouldContain(key, Case.Sensitive);

        foreach (var stated in ThresholdsItem6Needs.Where(other => !string.Equals(other, key, StringComparison.Ordinal)))
        {
            outcome.Reason.ShouldNotContain(stated, Case.Sensitive);
        }
    }

    private static QaSubject Subject(SKBitmap image, ThresholdSet? thresholds = null) =>
        QaSubjects.For(
            image,
            ManifestRows.NonBiomeUiIcon,
            TestSpecs.WithTargetSize(
                ManifestRows.NonBiomeUiIcon, SyntheticAsset.Canvas, SyntheticAsset.Canvas),
            thresholds);
}
