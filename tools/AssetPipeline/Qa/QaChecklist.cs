namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// `15` Part F's eleven items, in Part F's order, as runnable checks.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Eleven, and the count is asserted.</b> A checklist that can silently shrink passes forever
/// (steering rule S3), so <see cref="Doc15PartF.ItemCount"/> is a constant, the item numbers must be
/// exactly 1 to 11 with no gaps, and <c>QaChecklistTests</c> floors both.
/// </para>
/// <para>
/// 🔒 <b>The classification split is a deliverable.</b> 2 <see cref="QaClassification.Mechanical"/>,
/// 4 <see cref="QaClassification.MechanicalUncalibratedThreshold"/>, 5
/// <see cref="QaClassification.Human"/>. <see cref="MechanicalCount"/>,
/// <see cref="UncalibratedThresholdCount"/> and <see cref="HumanCount"/> exist so the split is data
/// a caller can print and a test can pin, rather than a claim living in a design document.
/// </para>
/// <para>
/// 🔒 <b>The unit is one asset.</b> Part F gates a <em>batch</em>, and a batch's decision is the
/// worst of its assets' — M8-10 composes that. Grading one asset is where the eleven items actually
/// bite, so that is what this evaluates, and <see cref="QaBatchResult"/> is the shape both levels
/// share.
/// </para>
/// </remarks>
public sealed class QaChecklist
{
    /// <summary>The eleven items, wired in `15` Part F order.</summary>
    public QaChecklist()
    {
    }

    /// <summary>The eleven checks, in `15` Part F order. Index 0 is item 1.</summary>
    public IReadOnlyList<IQaCheck> Items => throw new NotImplementedException();

    /// <summary>How many items a machine decides outright. `15` Part F: two.</summary>
    public int MechanicalCount => throw new NotImplementedException();

    /// <summary>How many a machine decides once somebody states a threshold. `15` Part F: four.</summary>
    public int UncalibratedThresholdCount => throw new NotImplementedException();

    /// <summary>How many no machine decides. `15` Part F: five.</summary>
    public int HumanCount => throw new NotImplementedException();

    /// <summary>
    /// Every named gap the checklist carries, in item order — the five human items' and item 1's.
    /// </summary>
    /// <remarks>
    /// 🔒 First-class data, not commentary. This is the list M8-10 prints beside a batch report so
    /// that "the pipeline accepted 942 assets" can never be read as "942 assets passed Part F".
    /// </remarks>
    public IReadOnlyList<string> HumanGaps => throw new NotImplementedException();

    /// <summary>The items of one classification, in item order.</summary>
    /// <param name="classification">The classification to filter by.</param>
    public IReadOnlyList<IQaCheck> OfClassification(QaClassification classification) =>
        throw new NotImplementedException();

    /// <summary>Runs all eleven items over one asset.</summary>
    /// <param name="subject">The processed asset and everything needed to judge it.</param>
    public QaBatchResult Evaluate(QaSubject subject) => throw new NotImplementedException();
}
