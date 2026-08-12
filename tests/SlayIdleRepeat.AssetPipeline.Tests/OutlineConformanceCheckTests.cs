using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C13 — `15` Part F item 3: <em>"Outline continuous, uniform width, colour #231A2E"</em>.
/// </summary>
/// <remarks>
/// Half of this item's numbers are `15` §A3's own — the colour and the 3-4 px at 512 px band — and
/// half are holes. The cases below therefore split three ways: the authorised half is asserted
/// against <see cref="Doc15Authorised"/>, the unauthorised half against a value this suite states,
/// and leaving either hole open must yield <see cref="QaVerdict.Uncalibrated"/>.
/// </remarks>
public sealed class OutlineConformanceCheckTests
{
    /// <summary>Every hole item 3 reaches into, one theory case each.</summary>
    public static TheoryData<string> EveryThresholdItem3Needs() => new()
    {
        ThresholdKeys.OutlineColourTolerance,
        ThresholdKeys.OutlineWidthUniformityTolerance,
    };

    /// <summary>
    /// 🔒 The fixture's ring is <see cref="SyntheticAsset.OutlineBoxWidth"/> px on a
    /// <see cref="SyntheticAsset.OutlineBoxCanvas"/> px canvas, which `15` §A3's proportional band
    /// contains exactly. This case asserts that first, so the pass below is a statement about §A3
    /// rather than about whatever band the check happened to compute.
    /// </summary>
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

    /// <summary>
    /// 🔒 Pure black is `15` §A3's named counter-example: <em>"Colour #231A2E (never pure
    /// black)"</em>. It is 63.4 RGB units away, which no plausible antialiasing tolerance covers.
    /// </summary>
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
        outcome.Measurements
            .Single(m => m.Key == OutlineConformanceCheck.OutlineColourDistanceMeasurement)
            .Value.ShouldBeGreaterThan(Pixels.RgbDistance(SKColors.Black, Doc15Authorised.OutlineColour) - 1d);
    }

    /// <summary>
    /// 🔒 "Continuous" is the item's first word and it is measured, not assumed: a gap punched in
    /// the ring lets a flood started at the frame edge reach the interior, and the leak count is how
    /// many interior pixels it reached.
    /// </summary>
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
        outcome.Measurements
            .Single(m => m.Key == OutlineConformanceCheck.OutlineLeakMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    /// <summary>
    /// 🔒 The ring is the right colour and continuous, and four times too thick for `15` §A3's band
    /// at this canvas. Only the width claim can fail, so the reason has one honest thing to name.
    /// </summary>
    [Fact]
    public void Evaluate_fails_naming_the_measured_width_when_the_ring_is_outside_15_A3s_band()
    {
        var fixture = SyntheticAsset.OutlinedBox(SyntheticAsset.OutlineBoxWidthTooWide);

        var outcome = new OutlineConformanceCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(3);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(
            OutlineConformanceCheck.OutlineWidthMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == OutlineConformanceCheck.OutlineWidthMeasurement)
            .Value.ShouldBe(SyntheticAsset.OutlineBoxWidthTooWide, 0.5d);
    }

    /// <summary>
    /// 🔒 <b>The most important assertion in this suite.</b> Neither of these two numbers is in
    /// `15`, and a check that quietly passed without them would report a clean batch that nobody
    /// had graded. Not <see cref="QaVerdict.Pass"/>, not <see cref="QaVerdict.Fail"/>, and the
    /// reason names <em>which</em> hole stopped it.
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
