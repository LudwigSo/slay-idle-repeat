using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// The outline colour and width band are authorised values (asserted against
/// <see cref="Doc15Authorised"/>); colour/width uniformity tolerance are not, and leaving either
/// open must yield <see cref="QaVerdict.Uncalibrated"/>.
/// </summary>
public sealed class OutlineConformanceCheckTests
{
    private static readonly string[] ThresholdsItem3Needs =
    [
        ThresholdKeys.OutlineColourTolerance,
        ThresholdKeys.OutlineWidthUniformityTolerance,
    ];

    public static TheoryData<string> EveryThresholdItem3Needs()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdsItem3Needs)
        {
            data.Add(key);
        }

        return data;
    }

    /// <summary>Asserts the fixture's ring width sits inside the authorised band, so the pass below is a real claim, not a tautology.</summary>
    [Fact]
    public void The_conforming_fixtures_ring_is_inside_15_A3s_proportional_band()
    {
        var (min, max) = Doc15Authorised.OutlineWidthBandFor(SyntheticAsset.OutlineBoxCanvas);

        ((double)SyntheticAsset.OutlineBoxWidth).ShouldBeGreaterThanOrEqualTo(min);
        ((double)SyntheticAsset.OutlineBoxWidth).ShouldBeLessThanOrEqualTo(max);
        ((double)SyntheticAsset.OutlineBoxWidthTooWide).ShouldBeGreaterThan(max);
    }

    [Fact]
    public void Evaluate_passes_a_continuous_ring_of_uniform_width_in_15_A3s_colour()
    {
        var fixture = SyntheticAsset.OutlinedBox(SyntheticAsset.OutlineBoxWidth);

        var outcome = new OutlineConformanceCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(3);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.HumanGap.ShouldBeNull();
    }

    /// <summary>Pure black is 63.4 RGB units from the authorised colour, which no plausible antialiasing tolerance covers.</summary>
    [Fact]
    public void Evaluate_fails_naming_the_colour_distance_when_the_outline_is_pure_black()
    {
        var fixture = SyntheticAsset.OutlinedBox(
            SyntheticAsset.OutlineBoxWidth, outlineColour: SKColors.Black);

        var outcome = new OutlineConformanceCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(3);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            OutlineConformanceCheck.OutlineColourDistanceMeasurement, Case.Sensitive);

        // Width/leak measurements are taken over the colour-seeded mask, which a black ring empties,
        // so they must not also report as failures here.
        outcome.Reason.ShouldNotContain(
            OutlineConformanceCheck.OutlineWidthMeasurement, Case.Sensitive);
        outcome.Reason.ShouldNotContain(
            OutlineConformanceCheck.OutlineLeakMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == OutlineConformanceCheck.OutlineColourDistanceMeasurement)
            .Value.ShouldBeGreaterThan(Pixels.RgbDistance(SKColors.Black, Doc15Authorised.OutlineColour) - 1d);
    }

    /// <summary>A gap in the ring lets a flood from the frame edge reach the interior; the leak count is how many pixels it reached.</summary>
    [Fact]
    public void Evaluate_fails_naming_the_leak_count_when_the_outline_does_not_enclose_the_subject()
    {
        var fixture = SyntheticAsset.OutlinedBox(
            SyntheticAsset.OutlineBoxWidth, gapLength: SyntheticAsset.OutlineBoxGapLength);
        fixture.GapPixels.Count.ShouldBe(
            SyntheticAsset.OutlineBoxGapLength * SyntheticAsset.OutlineBoxWidth,
            "a fixture whose gap was never punched would make this case vacuous");

        var outcome = new OutlineConformanceCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(3);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            OutlineConformanceCheck.OutlineLeakMeasurement, Case.Sensitive);

        outcome.Reason.ShouldNotContain(
            OutlineConformanceCheck.OutlineWidthMeasurement, Case.Sensitive);
        outcome.Reason.ShouldNotContain(
            OutlineConformanceCheck.OutlineColourDistanceMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == OutlineConformanceCheck.OutlineLeakMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    /// <summary>The ring is the right colour and continuous, but too thick for the band at this canvas size.</summary>
    [Fact]
    public void Evaluate_fails_naming_the_measured_width_when_the_ring_is_outside_15_A3s_band()
    {
        var fixture = SyntheticAsset.OutlinedBox(SyntheticAsset.OutlineBoxWidthTooWide);

        var outcome = new OutlineConformanceCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(3);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            OutlineConformanceCheck.OutlineWidthMeasurement, Case.Sensitive);

        outcome.Reason.ShouldNotContain(
            OutlineConformanceCheck.OutlineColourDistanceMeasurement, Case.Sensitive);
        outcome.Reason.ShouldNotContain(
            OutlineConformanceCheck.OutlineLeakMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == OutlineConformanceCheck.OutlineWidthMeasurement)
            .Value.ShouldBe(SyntheticAsset.OutlineBoxWidthTooWide, 0.5d);
    }

    /// <summary>
    /// Neither tolerance is authorised, so a check that quietly passed without them would report a
    /// clean batch that nobody had graded; the reason must name which hole stopped it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryThresholdItem3Needs))]
    public void Evaluate_reports_Uncalibrated_naming_the_key_when_one_hole_is_left_open(string key)
    {
        var fixture = SyntheticAsset.OutlinedBox(SyntheticAsset.OutlineBoxWidth);

        var outcome = new OutlineConformanceCheck()
            .Evaluate(Subject(fixture.Image, QaThresholds.Except(key)));

        outcome.ItemNumber.ShouldBe(3);
        outcome.Verdict.ShouldBe(QaVerdict.Uncalibrated);
        outcome.Reason.ShouldContain(key, Case.Sensitive);

        foreach (var stated in ThresholdsItem3Needs.Where(other => !string.Equals(other, key, StringComparison.Ordinal)))
        {
            outcome.Reason.ShouldNotContain(stated, Case.Sensitive);
        }
    }

    private static QaSubject Subject(SKBitmap image, ThresholdSet? thresholds = null) =>
        QaSubjects.For(
            image,
            ManifestRows.NonBiomeUiIcon,
            TestSpecs.WithTargetSize(
                ManifestRows.NonBiomeUiIcon,
                SyntheticAsset.OutlineBoxCanvas,
                SyntheticAsset.OutlineBoxCanvas),
            thresholds);
}
