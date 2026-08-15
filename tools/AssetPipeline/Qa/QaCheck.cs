namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>One item on the quality-assurance checklist.</summary>
/// <remarks>
/// <para>
/// Independently constructible and runnable: a reviewer arguing about one item must be able to run
/// it alone, on one asset, without the others and without the pipeline.
/// </para>
/// <para>
/// A check does not throw for an uncalibrated threshold — it returns
/// <see cref="QaVerdict.Uncalibrated"/> with the <see cref="ThresholdKeys"/> constant in the
/// reason, so one uncalibrated threshold does not abort a whole batch and hide the other items'
/// findings. The silhouette gate underneath item 1 does throw — see <see cref="SilhouetteGate"/> —
/// and item 1 is the seam that turns that into a verdict.
/// </para>
/// </remarks>
public interface IQaCheck
{
    /// <summary>The checklist ordinal, 1-11.</summary>
    int ItemNumber { get; }

    /// <summary>The checklist's own line for this item, verbatim.</summary>
    string ChecklistText { get; }

    /// <summary>Whether a machine can decide this item at all. See <see cref="QaClassification"/>.</summary>
    QaClassification Classification { get; }

    /// <summary>Where the item's substance comes from.</summary>
    string DocReference { get; }

    /// <summary>The judgement this project does not make, or null where there is none.</summary>
    /// <remarks>
    /// Non-null for every <see cref="QaClassification.Human"/> item, and also for item 1: a
    /// mechanical pass there is never a report that the human acceptance test passed.
    /// </remarks>
    string? HumanGap { get; }

    /// <summary>Runs this item alone against one asset.</summary>
    /// <param name="subject">The processed asset and everything needed to judge it.</param>
    QaOutcome Evaluate(QaSubject subject);
}
