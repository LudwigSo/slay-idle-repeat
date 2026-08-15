using Shouldly;
using SlayIdleRepeat.AssetPipeline;
using Xunit;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The split between what this generator states so its pipeline can run, and what it refuses to
/// state so QA stays honest.
/// </summary>
/// <remarks>
/// Nine of the seventeen holes are inputs a pipeline step throws without; the other eight are
/// acceptance cutoffs, and turning one of those into a number would turn an <c>Uncalibrated</c>
/// verdict into a <c>Pass</c> nobody measured. The two sets are pinned <b>by name</b>, not by count.
/// </remarks>
public sealed class PlaceholderThresholdsTests
{
    /// <summary>The nine keys the pipeline's steps cannot run without, and which this generator states.</summary>
    private static readonly string[] StatedByThePipeline =
    [
        ThresholdKeys.BackgroundKeyTolerance,
        ThresholdKeys.MatteDecontaminationStrength,
        ThresholdKeys.OutlineColourTolerance,
        ThresholdKeys.OutlineGapClosureRadius,
        ThresholdKeys.PaletteMatchTolerance,
        ThresholdKeys.PaletteNeutrals,
        ThresholdKeys.ResizeSharpenRadius,
        ThresholdKeys.ExportColourBudget,
        ThresholdKeys.ExportMaxMeanError,
    ];

    [Fact]
    public void The_pipeline_set_states_exactly_the_nine_processing_values_and_no_acceptance_cutoff()
    {
        var thresholds = PlaceholderThresholds.ForPipeline();

        ThresholdSet.Keys.Count.ShouldBe(17, "M8-06 ships seventeen holes; this split is over all of them.");

        var stated = ThresholdSet.Keys.Where(thresholds.IsCalibrated).ToArray();
        var open = ThresholdSet.Keys.Where(key => !thresholds.IsCalibrated(key)).ToArray();

        stated.ShouldBe(StatedByThePipeline, ignoreOrder: true);

        // Named, not counted: the eight below are QA's own cutoffs and must stay null in both sets.
        open.ShouldBe(
            [
                ThresholdKeys.OutlineWidthUniformityTolerance,
                ThresholdKeys.HaloMaxFringeRatio,
                ThresholdKeys.HaloMaxLuminanceDeviation,
                ThresholdKeys.SilhouetteMinCoverageRatio,
                ThresholdKeys.SilhouetteMinBoundingBoxFill,
                ThresholdKeys.SilhouetteMaxComponentCount,
                ThresholdKeys.SilhouetteMinDistinguishability,
                ThresholdKeys.WatermarkCornerOpacityCeiling,
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_quality_assurance_set_is_the_shipped_register_with_every_key_still_null()
    {
        var thresholds = PlaceholderThresholds.ForQualityAssurance(PlaceholderFiles.ThresholdsJson());

        ThresholdSet.Keys.Count.ShouldBeGreaterThanOrEqualTo(17);

        var calibrated = ThresholdSet.Keys.Where(thresholds.IsCalibrated).ToArray();

        calibrated.ShouldBeEmpty(
            "`15` Part F is graded against the shipped register verbatim. A key stated here would " +
            "let this generator grade its own output against its own working values — which is the " +
            $"S6 violation the null register exists to prevent. Stated: [{string.Join(", ", calibrated)}].");
    }

    [Fact]
    public void The_neutral_list_is_stated_empty_rather_than_invented()
    {
        // The palette spec never enumerates the neutrals, so this generator writes none — a fact
        // about the batch, not a claim about what the real neutrals are.
        PlaceholderThresholds.PaletteNeutrals.ShouldBeEmpty();

        PlaceholderThresholds.ForPipeline()
            .RequireColours(ThresholdKeys.PaletteNeutrals)
            .ShouldBeEmpty();
    }

    [Fact]
    public void An_acceptance_cutoff_read_off_the_pipeline_set_still_throws_naming_its_key()
    {
        // The pipeline set is not a back door: reading a Part F cutoff off it fails the same way
        // reading it off the shipped register does, and the message names which hole stopped it.
        var thrown = Should.Throw<UncalibratedThresholdException>(
            () => PlaceholderThresholds.ForPipeline()
                .RequireNumber(ThresholdKeys.SilhouetteMinCoverageRatio));

        thrown.Key.ShouldBe(ThresholdKeys.SilhouetteMinCoverageRatio);
        thrown.Message.ShouldContain("steering rule S6", Case.Sensitive);
    }
}
