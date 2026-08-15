using SkiaSharp;
using Shouldly;
using SlayIdleRepeat.AssetPipeline.Qa;
using SlayIdleRepeat.AssetPipeline.Qa.Checks;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// Item 1 is mechanised and also owes a human gap, so these cases assert both halves every time: the
/// verdict the four measurements support, and the human sentence that no measurement performs.
/// </summary>
public sealed class SilhouetteCheckTests
{
    /// <summary>How many blobs make a silhouette that has fallen apart, past the stated ceiling.</summary>
    private const int TooManyComponents = 5;

    private static readonly string[] ThresholdsItem1Needs =
    [
        ThresholdKeys.SilhouetteMinCoverageRatio,
        ThresholdKeys.SilhouetteMinBoundingBoxFill,
        ThresholdKeys.SilhouetteMaxComponentCount,
        ThresholdKeys.SilhouetteMinDistinguishability,
    ];

    private static readonly string[] EverySilhouetteMeasurement =
    [
        SilhouetteGate.CoverageRatioMeasurement,
        SilhouetteGate.BoundingBoxFillMeasurement,
        SilhouetteGate.ComponentCountMeasurement,
        SilhouetteGate.DistinguishabilityMeasurement,
    ];

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

    /// <summary>On the passing case, where omitting the gap would be least noticeable, the outcome still carries the human sentence.</summary>
    [Fact]
    public void Evaluate_carries_15_A4s_human_gap_even_when_every_measurement_passes()
    {
        var fixture = SyntheticAsset.ChibiCutOut();

        var outcome = new SilhouetteCheck().Evaluate(Subject(fixture.Image));

        outcome.Verdict.ShouldBe(QaVerdict.Pass);
        outcome.HumanGap.ShouldNotBeNull();
        outcome.HumanGap.ShouldContain(Doc15PartF.SilhouetteAcceptanceSentence, Case.Sensitive);
    }

    /// <summary>The fixture is one solid blob filling its own bounding box entirely, so only the coverage cutoff can trip.</summary>
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

    /// <summary>The fixture clears coverage and bounding-box fill, so the component ceiling is the only cutoff that can name it.</summary>
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

    /// <summary>Even a failing outcome carries the gap, since a report of four cutoffs alone would invite regenerating until they clear.</summary>
    [Fact]
    public void Evaluate_carries_15_A4s_human_gap_on_a_failing_outcome_too()
    {
        var (image, _) = SyntheticAsset.SeparatedBlobs(1);

        var outcome = new SilhouetteCheck().Evaluate(Subject(image));

        outcome.Verdict.ShouldBe(QaVerdict.Fail);
        outcome.HumanGap.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>One uncalibrated cutoff must stop item 1 from concluding without aborting the rest of the batch.</summary>
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

        foreach (var stated in ThresholdsItem1Needs.Where(other => !string.Equals(other, key, StringComparison.Ordinal)))
        {
            outcome.Reason.ShouldNotContain(stated, Case.Sensitive);
        }
    }

    /// <summary>Asserts the reason names the one measurement the fixture was built to trip and none of the three it clears.</summary>
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

    /// <summary>An uncalibrated item 1 still owes the human gap, so calibration alone can't look like the only blocker.</summary>
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
