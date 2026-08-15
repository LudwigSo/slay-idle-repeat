using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>Fully mechanical check, so there is no <see cref="QaVerdict.Uncalibrated"/> case here and there must never be one.</summary>
public sealed class CanvasAndPivotCheckTests
{
    private const int Canvas = 64;
    private const int ContentWidth = 32;
    private const int ContentHeight = 24;

    /// <summary>A canvas that is neither the target nor a plausible rounding of it.</summary>
    private const int WrongCanvas = 48;

    [Fact]
    public void Evaluate_passes_an_asset_at_the_manifests_size_with_its_content_on_the_pivot()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            Canvas, ContentWidth, ContentHeight, Doc15Pivots.BottomCenter);
        var subject = Subject(fixture.Image, Doc15Pivots.BottomCenter);

        var outcome = new CanvasAndPivotCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(7);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.HumanGap.ShouldBeNull();
    }

    /// <summary>The delivery size is an exact integer, not a target to land near.</summary>
    [Fact]
    public void Evaluate_fails_naming_the_canvas_it_measured_when_the_size_is_not_the_manifests()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            WrongCanvas, ContentWidth, ContentHeight, Doc15Pivots.BottomCenter);
        var subject = Subject(fixture.Image, Doc15Pivots.BottomCenter);

        var outcome = new CanvasAndPivotCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(7);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(CanvasAndPivotCheck.CanvasWidthMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == CanvasAndPivotCheck.CanvasWidthMeasurement)
            .Value.ShouldBe(WrongCanvas);
    }

    /// <summary>The canvas is right and only the content position is wrong, so a size-only check would wave this through.</summary>
    [Fact]
    public void Evaluate_fails_naming_the_pivot_offset_when_a_bottom_centre_subject_is_centred()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            Canvas, ContentWidth, ContentHeight, Doc15Pivots.Center);
        var subject = Subject(fixture.Image, Doc15Pivots.BottomCenter);

        var outcome = new CanvasAndPivotCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(7);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(CanvasAndPivotCheck.PivotOffsetMeasurement, Case.Sensitive);
        outcome.Measurements
            .Single(m => m.Key == CanvasAndPivotCheck.PivotOffsetMeasurement)
            .Value.ShouldBeGreaterThan(0d);
    }

    /// <summary>Without this the previous case would also pass an implementation demanding bottom alignment of everything.</summary>
    [Fact]
    public void Evaluate_passes_a_centred_subject_when_the_row_declares_the_center_pivot()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            Canvas, ContentWidth, ContentHeight, Doc15Pivots.Center);
        var subject = Subject(fixture.Image, Doc15Pivots.Center);

        var outcome = new CanvasAndPivotCheck().Evaluate(subject);

        outcome.Verdict.ShouldBe(QaVerdict.Pass);
    }

    /// <summary>
    /// Content whose dimension has the opposite parity to the canvas: every other fixture here uses
    /// even dimensions that centre exactly, missing this case.
    /// </summary>
    /// <remarks>
    /// A 31 px subject on a 64 px canvas cannot be centred: 33 px of padding does not halve.
    /// <c>TrimToCanvasStep</c> pads with integer division, so the odd pixel goes right/bottom and
    /// the content lands at 16, not 16.5 — the check must expect the same integer origin, not a
    /// floating-point one, or it rejects correct assets on odd-sized content.
    /// </remarks>
    [Theory]
    [InlineData(Doc15Pivots.Center)]
    [InlineData(Doc15Pivots.BottomCenter)]
    public void Evaluate_passes_odd_parity_content_centred_the_way_step_2_centres_it(string pivot)
    {
        const int OddContentWidth = 31;
        const int OddContentHeight = 25;

        ((Canvas - OddContentWidth) % 2).ShouldBe(1);
        ((Canvas - OddContentHeight) % 2).ShouldBe(1);

        var fixture = SyntheticAsset.PivotedSubject(Canvas, OddContentWidth, OddContentHeight, pivot);
        var subject = Subject(fixture.Image, pivot);

        var outcome = new CanvasAndPivotCheck().Evaluate(subject);

        outcome.ItemNumber.ShouldBe(7);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.Measurements
            .Single(m => m.Key == CanvasAndPivotCheck.PivotOffsetMeasurement)
            .Value.ShouldBe(0d);
    }

    /// <summary>This check reads nothing out of <see cref="ThresholdSet"/>, so an uncalibrated set must not change its answer.</summary>
    [Fact]
    public void Evaluate_still_concludes_with_an_entirely_uncalibrated_threshold_set()
    {
        var fixture = SyntheticAsset.PivotedSubject(
            Canvas, ContentWidth, ContentHeight, Doc15Pivots.BottomCenter);
        var subject = QaSubjects.For(
            fixture.Image,
            ManifestRows.NonBiomeUiIcon,
            TestSpecs.WithTargetSizeAndPivot(
                ManifestRows.NonBiomeUiIcon, Canvas, Canvas, Doc15Pivots.BottomCenter),
            ThresholdSet.Uncalibrated());

        var outcome = new CanvasAndPivotCheck().Evaluate(subject);

        outcome.Verdict.ShouldBe(QaVerdict.Pass);
    }

    private static QaSubject Subject(SkiaSharp.SKBitmap image, string pivot) => QaSubjects.For(
        image,
        ManifestRows.NonBiomeUiIcon,
        TestSpecs.WithTargetSizeAndPivot(ManifestRows.NonBiomeUiIcon, Canvas, Canvas, pivot));
}
