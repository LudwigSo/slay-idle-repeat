using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C13 — `15` Part F item 6:
/// <em>"Alpha is clean — no white/black halo, no semi-transparent fringe"</em>.
/// </summary>
public sealed class AlphaCleanlinessCheckTests
{
    /// <summary>Every hole item 6 reaches into.</summary>
    private static readonly string[] ThresholdsItem6Needs =
    [
        ThresholdKeys.HaloMaxFringeRatio,
        ThresholdKeys.HaloMaxLuminanceDeviation,
    ];

    /// <summary>Every hole item 6 reaches into, one theory case each.</summary>
    public static TheoryData<string> EveryThresholdItem6Needs()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdsItem6Needs)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>
    /// A fringe ratio nothing can exceed, stated so that a case aimed at the luminance half cannot
    /// be answered by the ratio half. Not a calibration — a ratio is 0-1 by construction.
    /// </summary>
    private const double EveryFringeAllowed = 1d;

    /// <summary>
    /// A luminance deviation nothing can exceed: eight-bit channels span 255, so no deviation
    /// reaches 256. Stated for the mirror-image reason.
    /// </summary>
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

    /// <summary>
    /// 🔒 The fringe half alone: every halo is forgiven, so only the amount of partial alpha can
    /// trip the check and the reason has exactly one honest thing to name.
    /// </summary>
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

        // 🔒 Every halo is forgiven in this set, so the luminance half cannot have tripped and a
        // reason that named it would be reporting a claim that held (steering rule S2).
        outcome.Reason.ShouldNotContain(
            AlphaCleanlinessCheck.FringeLuminanceDeviationMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == AlphaCleanlinessCheck.FringeRatioMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// 🔒 The halo half alone: every amount of fringe is forgiven, so only how white it is can trip
    /// the check. Without this case an implementation that never looked at colour at all would pass
    /// both halves of the item on the strength of the ratio.
    /// </summary>
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

        // 🔒 Every amount of fringe is forgiven in this set, so the ratio half cannot have tripped
        // and a reason that named it would be reporting a claim that held (steering rule S2).
        outcome.Reason.ShouldNotContain(
            AlphaCleanlinessCheck.FringeRatioMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == AlphaCleanlinessCheck.FringeLuminanceDeviationMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// 🔒 The S6 assertion for item 6. `15` says nothing about how much fringe is acceptable or how
    /// white it may run — "clean" is the whole of the doc's guidance — so both numbers are holes and
    /// neither may be filled in silently.
    /// </summary>
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

        // 🔒 Steering rule S2. A reason naming both of item 6's keys would satisfy the assertion
        // above for both cases; the other key is stated here, so naming it names a hole that is
        // not open.
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
