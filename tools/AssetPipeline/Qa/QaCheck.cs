namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>One of the eleven items on `15` Part F's quality-assurance checklist.</summary>
/// <remarks>
/// <para>
/// 🔒 Independently constructible and independently runnable, exactly like the `15` §B4 steps: a
/// reviewer arguing about item 5 must be able to run item 5 alone, on one asset, without the other
/// ten and without the pipeline.
/// </para>
/// <para>
/// 🔒 <b>A check does not throw for an uncalibrated threshold.</b> It returns
/// <see cref="QaVerdict.Uncalibrated"/> with the <see cref="ThresholdKeys"/> constant in the
/// reason, so that one hole in <c>assets/pipeline/thresholds.json</c> does not abort a 942-asset
/// batch and hide the other ten items' findings. The `15` §A4 gate underneath item 1 <em>does</em>
/// throw — see <see cref="SilhouetteGate"/> — and item 1 is the seam that turns that into a
/// verdict.
/// </para>
/// </remarks>
public interface IQaCheck
{
    /// <summary>The `15` Part F ordinal, 1-11, in the order Part F lists the items.</summary>
    int ItemNumber { get; }

    /// <summary>
    /// Part F's own line for this item, verbatim — see <see cref="Doc15PartF"/> for what "verbatim"
    /// means down to the dash characters.
    /// </summary>
    string ChecklistText { get; }

    /// <summary>Whether a machine can decide this item at all. See <see cref="QaClassification"/>.</summary>
    QaClassification Classification { get; }

    /// <summary>The section of `15` the item's substance comes from, e.g. <c>15 §A3</c>.</summary>
    string DocReference { get; }

    /// <summary>
    /// The judgement this project does not make, or null where there is none.
    /// </summary>
    /// <remarks>
    /// 🔒 Non-null for every <see cref="QaClassification.Human"/> item, and also for item 1: `15`
    /// §A4's acceptance test is <em>"If you cannot tell which character it is, regenerate it"</em>,
    /// which four pixel measurements do not perform. A mechanical pass on item 1 is never a report
    /// that §A4 passed.
    /// </remarks>
    string? HumanGap { get; }

    /// <summary>Runs this item alone against one asset.</summary>
    /// <param name="subject">The processed asset and everything needed to judge it.</param>
    QaOutcome Evaluate(QaSubject subject);
}
