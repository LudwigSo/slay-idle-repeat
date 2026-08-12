using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C13 — `15` Part F item 1: <em>"Silhouette test passed at 64 px (characters)"</em>.
/// </summary>
/// <remarks>
/// 🔒 Item 1 is the one item that is mechanised <b>and</b> owes a human gap, so these cases assert
/// both halves every time: the verdict the four measurements support, and the sentence of `15` §A4
/// that no measurement performs. A pass here must never be readable as "§A4 passed".
/// </remarks>
public sealed class SilhouetteCheckTests
{
    /// <summary>How many blobs make a silhouette that has fallen apart, past the stated ceiling.</summary>
    private const int TooManyComponents = 5;

    /// <summary>Every hole item 1 reaches into.</summary>
    private static readonly string[] ThresholdsItem1Needs =
    [
        ThresholdKeys.SilhouetteMinCoverageRatio,
        ThresholdKeys.SilhouetteMinBoundingBoxFill,
        ThresholdKeys.SilhouetteMaxComponentCount,
        ThresholdKeys.SilhouetteMinDistinguishability,
    ];

    /// <summary>
    /// The `15` §A4 measurements a fixture built to trip exactly one of them must NOT be reported
    /// against — held here so each violation case can state the three that stayed inside their
    /// stated cutoffs.
    /// </summary>
    private static readonly string[] EverySilhouetteMeasurement =
    [
        SilhouetteGate.CoverageRatioMeasurement,
        SilhouetteGate.BoundingBoxFillMeasurement,
        SilhouetteGate.ComponentCountMeasurement,
        SilhouetteGate.DistinguishabilityMeasurement,
    ];

    /// <summary>Every hole item 1 reaches into, one theory case each.</summary>
    public static TheoryData<string> EveryThresholdItem1Needs()
    {
        var data = new TheoryData<string>();
        foreach (var key in ThresholdsItem1Needs)
        {
            data.Add(key);
        }

        return data;
    }

    [Fact]
    public void Evaluate_passes_a_solid_single_blob_silhouette_that_clears_every_stated_cutoff()
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var outcome = new SilhouetteCheck().Evaluate(Subject(fixture.Image));

        outcome.ItemNumber.ShouldBe(1);
        outcome.Verdict.ShouldBe(QaVerdict.Pass);
    }

    /// <summary>
    /// 🔒 <b>The A5 assertion for item 1.</b> On the passing case — the one where omitting the gap
    /// would be least noticeable and most misleading — the outcome still carries `15` §A4's own
    /// sentence, so no report can turn four cleared cutoffs into "§A4 passed".
    /// </summary>
    [Fact]
    public void Evaluate_carries_15_A4s_human_gap_even_when_every_measurement_passes()
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var outcome = new SilhouetteCheck().Evaluate(Subject(fixture.Image));

        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.HumanGap.ShouldNotBeNull();
        outcome.HumanGap.ShouldContain(Doc15PartF.SilhouetteAcceptanceSentence, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A subject that survived the pipeline as 1.6% of its frame is unreadable at any size. Only
    /// the coverage cutoff can trip on this fixture: it is one solid blob filling its own bounding
    /// box entirely, so the other three are clear.
    /// </summary>
    [Fact]
    public void Evaluate_fails_naming_the_coverage_ratio_when_the_subject_has_all_but_vanished()
    {
        var (image, opaquePixels) = SyntheticAsset.SeparatedBlobs(1);
        opaquePixels.ShouldBe(SyntheticAsset.BlobSize * SyntheticAsset.BlobSize);

        var outcome = new SilhouetteCheck().Evaluate(Subject(image));

        outcome.ItemNumber.ShouldBe(1);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(SilhouetteGate.CoverageRatioMeasurement, Case.Sensitive);
        ShouldNameNoOtherMeasurement(outcome, SilhouetteGate.CoverageRatioMeasurement);
        outcome.Measurements
            .Single(m => m.Key == SilhouetteGate.CoverageRatioMeasurement)
            .Value.ShouldBeLessThan(0.05d);
    }

    /// <summary>
    /// 🔒 A character whose limbs detached during background removal reads as several blobs. This
    /// fixture clears coverage and bounding-box fill, so the component ceiling is the only cutoff
    /// that can name it.
    /// </summary>
    [Fact]
    public void Evaluate_fails_naming_the_component_count_when_the_silhouette_has_fallen_apart()
    {
        var (image, _) = SyntheticAsset.SeparatedBlobs(TooManyComponents);

        var outcome = new SilhouetteCheck().Evaluate(Subject(image));

        outcome.ItemNumber.ShouldBe(1);
        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.Reason.ShouldContain(SilhouetteGate.ComponentCountMeasurement, Case.Sensitive);
        ShouldNameNoOtherMeasurement(outcome, SilhouetteGate.ComponentCountMeasurement);
        outcome.Measurements
            .Single(m => m.Key == SilhouetteGate.ComponentCountMeasurement)
            .Value.ShouldBe(TooManyComponents);
    }

    /// <summary>
    /// 🔒 Even a failing outcome carries the gap. A rejected asset is exactly when a reviewer reads
    /// the report, and a report that named four cutoffs without saying what they are a floor under
    /// would invite regenerating until the numbers cleared.
    /// </summary>
    [Fact]
    public void Evaluate_carries_15_A4s_human_gap_on_a_failing_outcome_too()
    {
        var (image, _) = SyntheticAsset.SeparatedBlobs(1);

        var outcome = new SilhouetteCheck().Evaluate(Subject(image));

        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.HumanGap.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 🔒 The S6 assertion for item 1, and the reason <see cref="SilhouetteGate"/> throws while this
    /// check does not: one uncalibrated cutoff must stop item 1 from concluding without aborting
    /// the other ten items of a 942-asset batch.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryThresholdItem1Needs))]
    public void Evaluate_reports_Uncalibrated_naming_the_key_when_one_hole_is_left_open(string key)
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var outcome = new SilhouetteCheck()
            .Evaluate(Subject(fixture.Image, QaThresholds.Except(key)));

        outcome.ItemNumber.ShouldBe(1);
        outcome.Verdict.ShouldBe(QaVerdict.Uncalibrated);
        outcome.Reason.ShouldContain(key, Case.Sensitive);

        // 🔒 Steering rule S2. A reason that listed all four of `15` §A4's cutoffs would satisfy
        // the assertion above for every one of these four cases and would still leave a reviewer
        // unable to tell which of the four to go and measure. The other three are stated in this
        // set, so naming one is naming a hole that is not open.
        foreach (var stated in ThresholdsItem1Needs.Where(other => !string.Equals(other, key, StringComparison.Ordinal)))
        {
            outcome.Reason.ShouldNotContain(stated, Case.Sensitive);
        }
    }

    /// <summary>
    /// 🔒 Asserts the reason names the one `15` §A4 measurement the fixture was built to trip and
    /// none of the three it clears. Without it a reason that recited all four would satisfy every
    /// violation case in this file, and "the check named the coverage ratio" would stop being
    /// evidence that the coverage ratio is what failed (steering rule S2).
    /// </summary>
    /// <param name="outcome">The outcome under test.</param>
    /// <param name="tripped">The measurement the fixture violates by construction.</param>
    private static void ShouldNameNoOtherMeasurement(QaOutcome outcome, string tripped)
    {
        foreach (var clear in EverySilhouetteMeasurement.Where(
                     other => !string.Equals(other, tripped, StringComparison.Ordinal)))
        {
            outcome.Reason.ShouldNotContain(clear, Case.Sensitive);
        }
    }

    /// <summary>
    /// 🔒 An uncalibrated item 1 still owes the human gap. Reporting "no threshold" without
    /// reporting "and §A4 was never performed either" would leave a reader thinking calibration is
    /// the only thing standing between the batch and acceptance.
    /// </summary>
    [Fact]
    public void Evaluate_carries_15_A4s_human_gap_on_an_uncalibrated_outcome_too()
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var outcome = new SilhouetteCheck().Evaluate(
            Subject(fixture.Image, QaThresholds.Except(ThresholdKeys.SilhouetteMinCoverageRatio)));

        outcome.Verdict.ShouldBe(QaVerdict.Uncalibrated);
        outcome.HumanGap.ShouldNotBeNullOrWhiteSpace();
    }

    private static QaSubject Subject(SKBitmap image, ThresholdSet? thresholds = null) =>
        QaSubjects.For(
            image,
            ManifestRows.BiomeScopedCharacter,
            TestSpecs.WithTargetSize(
                ManifestRows.BiomeScopedCharacter, SyntheticAsset.Canvas, SyntheticAsset.Canvas),
            thresholds);
}
