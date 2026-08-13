using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C13 — `15` Part F item 7: <em>"Correct canvas size and pivot per §C"</em>. Fully mechanical, so
/// there is no <see cref="QaVerdict.Uncalibrated"/> case here and there must never be one.
/// </summary>
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

    /// <summary>
    /// 🔒 `15` §C's delivery size is an exact integer, not a target to land near: an atlas built on
    /// a 48 px sprite where the metadata says 64 misplaces every reference to it.
    /// </summary>
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

    /// <summary>
    /// 🔒 The canvas is right and the content is in the wrong place — the failure a check that only
    /// compared sizes would wave through, and the one that lands as a hero floating above the board.
    /// </summary>
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

    /// <summary>
    /// 🔒 The pivot check must be able to accept <c>center</c> too. Without this the previous case
    /// would also pass an implementation that simply demanded bottom alignment of everything, and
    /// 362 of the 974 shipped rows are <c>center</c>.
    /// </summary>
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
    /// 🔒 Content whose dimension has the OPPOSITE PARITY to the canvas — the case every other
    /// fixture here misses, because 32 and 24 on a 64 px canvas are all even and centre exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A 31 px subject on a 64 px canvas cannot be centred: 33 px of padding does not halve.
    /// <c>TrimToCanvasStep</c> pads with integer division, so the odd pixel goes right and bottom
    /// and the content lands at 16, not 16.5. Item 7 briefly computed the expected origin in
    /// floating point and demanded 16.5, which no image the pipeline itself produces can satisfy —
    /// so item 7 was unsatisfiable for roughly half of the 974 shipped rows, on assets that were
    /// in fact correct. The two must agree, and this case is what holds them together.
    /// </para>
    /// <para>
    /// Both pivots are driven because they divide on different axes: <c>center</c> halves the
    /// padding vertically as well, while <c>bottom-center</c> subtracts and only the horizontal
    /// axis can disagree.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(Doc15Pivots.Center)]
    [InlineData(Doc15Pivots.BottomCenter)]
    public void Evaluate_passes_odd_parity_content_centred_the_way_step_2_centres_it(string pivot)
    {
        const int OddContentWidth = 31;
        const int OddContentHeight = 25;

        // The premise of the case: both dimensions must genuinely fail to halve against this
        // canvas, or it degenerates into a duplicate of the even-parity cases above.
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

    /// <summary>
    /// 🔒 Item 7 reads nothing out of <see cref="ThresholdSet"/>, so an entirely uncalibrated set
    /// must not change its answer. A mechanical item that went <see cref="QaVerdict.Uncalibrated"/>
    /// would make the two items that can actually conclude something depend on a calibration nobody
    /// owes.
    /// </summary>
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
