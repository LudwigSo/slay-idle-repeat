namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>Checklist item 1: silhouette test passed at 64 px (characters).</summary>
/// <remarks>
/// <para>
/// Delegates to <see cref="SilhouetteGate"/> and turns its four measurements into a verdict. The
/// four cutoffs are uncalibrated, so a threshold set that states none of them yields
/// <see cref="QaVerdict.Uncalibrated"/> naming the key — never a pass.
/// </para>
/// <para>
/// Classified with a human gap even though it is mechanised: four pixel measurements do not
/// perform the actual acceptance test, they catch silhouettes so degenerate that nobody would need
/// to try. <see cref="HumanGap"/> is non-null on every outcome, including the passing ones, so no
/// report can claim the human test passed on the strength of a machine.
/// </para>
/// </remarks>
public sealed class SilhouetteCheck : IQaCheck
{
    /// <summary>The judgement this project does not make.</summary>
    public const string SilhouetteHumanGap =
        "15 §A4's acceptance test is a human judgement: \"" +
        Doc15PartF.SilhouetteAcceptanceSentence +
        "\" Nothing here performs it. The four measurements are a mechanical floor beneath §A4 — " +
        "they catch a silhouette that has collapsed, split apart or duplicated one already " +
        "accepted. A pass means the silhouette cleared that floor; it never means §A4 passed.";

    /// <inheritdoc/>
    public int ItemNumber => 1;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item1;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.MechanicalUncalibratedThreshold;

    /// <inheritdoc/>
    public string DocReference => "15 §A4";

    /// <inheritdoc/>
    public string? HumanGap => SilhouetteHumanGap;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var category = AssetNaming.CategoryOf(subject.Asset.Id);

        try
        {
            var result = SilhouetteGate.Evaluate(
                subject.Image, category, subject.Registry, subject.Thresholds);

            return new QaOutcome(
                result.MechanicalPass ? QaVerdict.Pass : QaVerdict.Fail,
                ItemNumber,
                result.MechanicalPass ? ClearedTheFloor : result.Reason,
                result.Measurements,
                SilhouetteHumanGap);
        }
        catch (UncalibratedThresholdException uncalibrated)
        {
            // The gate throws and this item reports, so one open hole doesn't abort the rest of a
            // batch. Measurements travel anyway, since they need no cutoff to be taken.
            return QaEvidence.Uncalibrated(
                ItemNumber, uncalibrated, MeasuredWithoutGrading(subject, category), SilhouetteHumanGap);
        }
    }

    /// <summary>
    /// What a mechanical pass says, phrased so that it cannot be quoted as "§A4 passed".
    /// </summary>
    private const string ClearedTheFloor =
        "Every stated cutoff was cleared. That is the mechanical floor beneath `15` §A4, not §A4's " +
        "own test — see the human gap.";

    /// <summary>
    /// The four silhouette quantities, taken without grading them, so an uncalibrated outcome still
    /// hands a reviewer the numbers a cutoff would have been compared against.
    /// </summary>
    /// <param name="subject">The asset under judgement.</param>
    /// <param name="category">The category the registry is consulted for.</param>
    private static IReadOnlyList<StepMeasurement> MeasuredWithoutGrading(
        QaSubject subject, string category)
    {
        using var mask = SilhouetteGate.Render(subject.Image);
        var measurement = SilhouetteGate.Measure(mask, category, subject.Registry);

        return
        [
            new StepMeasurement(
                SilhouetteGate.CoverageRatioMeasurement,
                measurement.CoverageRatio, "ratio", "15 §A4"),
            new StepMeasurement(
                SilhouetteGate.BoundingBoxFillMeasurement,
                measurement.BoundingBoxFill, "ratio", "15 §A4"),
            new StepMeasurement(
                SilhouetteGate.ComponentCountMeasurement,
                measurement.ConnectedComponentCount, "count", "15 §A4"),
            new StepMeasurement(
                SilhouetteGate.DistinguishabilityMeasurement,
                measurement.Distinguishability, "ratio", "15 §A4"),
        ];
    }
}
