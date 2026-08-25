using SlayIdleRepeat.AssetPipeline.Qa.Checks;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>The eleven quality-assurance checklist items, in order, as runnable checks.</summary>
/// <remarks>
/// <para>
/// Eleven, and the count is asserted: a checklist that can silently shrink passes forever, so the
/// item numbers must be exactly 1 to 11 with no gaps.
/// </para>
/// <para>
/// The unit is one asset, not a batch. A batch's decision is the worst of its assets', composed
/// elsewhere; grading one asset is where the eleven items actually bite.
/// </para>
/// </remarks>
public sealed class QaChecklist
{
    public QaChecklist()
        : this(
        [
            new SilhouetteCheck(),
            new ReadabilityCheck(),
            new OutlineConformanceCheck(),
            new KeyLightCheck(),
            new PaletteConformanceCheck(),
            new AlphaCleanlinessCheck(),
            new CanvasAndPivotCheck(),
            new WatermarkCheck(),
            new ProportionsCheck(),
            new NamingAndAtlasCheck(),
            new StyleDriftCheck(),
        ])
    {
    }

    /// <summary>A checklist over a caller's own eleven items, in order.</summary>
    /// <remarks>A seam for substituting or wrapping an item without forking this type.</remarks>
    /// <param name="items">The eleven checks, in order.</param>
    public QaChecklist(IReadOnlyList<IQaCheck> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = [.. items];

        var numbers = Items.Select(item => item.ItemNumber).ToArray();
        if (Items.Count != Doc15PartF.SupersededItemCount
            || !numbers.SequenceEqual(Enumerable.Range(1, Doc15PartF.SupersededItemCount)))
        {
            throw new InvalidOperationException(
                $"`15` Part F lists {Doc15PartF.SupersededItemCount} items, numbered 1 to " +
                $"{Doc15PartF.SupersededItemCount} in its own order, and this checklist holds " +
                $"[{string.Join(", ", numbers)}]. A checklist that can silently shrink or reorder " +
                "passes forever (steering rule S3).");
        }
    }

    /// <summary>The eleven checks, in order. Index 0 is item 1.</summary>
    public IReadOnlyList<IQaCheck> Items { get; }

    /// <summary>How many items a machine decides outright.</summary>
    public int MechanicalCount => OfClassification(QaClassification.Mechanical).Count;

    /// <summary>How many a machine decides once somebody states a threshold.</summary>
    public int UncalibratedThresholdCount =>
        OfClassification(QaClassification.MechanicalUncalibratedThreshold).Count;

    /// <summary>How many no machine decides.</summary>
    public int HumanCount => OfClassification(QaClassification.Human).Count;

    /// <summary>Every named gap the checklist carries, in item order.</summary>
    public IReadOnlyList<string> HumanGaps =>
    [
        .. Items.Where(item => item.HumanGap is not null).Select(item => item.HumanGap!),
    ];

    /// <summary>The items of one classification, in item order.</summary>
    /// <param name="classification">The classification to filter by.</param>
    public IReadOnlyList<IQaCheck> OfClassification(QaClassification classification) =>
        [.. Items.Where(item => item.Classification == classification)];

    /// <summary>Runs all eleven items over one asset.</summary>
    /// <param name="subject">The processed asset and everything needed to judge it.</param>
    public QaBatchResult Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new QaBatchResult(subject.Asset.Id, [.. Items.Select(item => Graded(item, subject))]);
    }

    /// <summary>Runs one item and enforces that a human item never claims a machine verdict.</summary>
    /// <remarks>
    /// Throws rather than downgrading the verdict: a check that claims a human judgement is wrong
    /// about what it is, and quietly rewriting its answer would leave a report that looks correct
    /// and a check that is not.
    /// </remarks>
    /// <param name="item">The item to run.</param>
    /// <param name="subject">The processed asset and everything needed to judge it.</param>
    private static QaOutcome Graded(IQaCheck item, QaSubject subject)
    {
        var outcome = item.Evaluate(subject);

        if (item.Classification == QaClassification.Human
            && outcome.Verdict != QaVerdict.HumanGapOnly)
        {
            throw new InvalidOperationException(
                $"`15` Part F item {item.ItemNumber} is classified " +
                $"{nameof(QaClassification)}.{item.Classification} and returned " +
                $"{nameof(QaVerdict)}.{outcome.Verdict}. Assumption A5 permits a human item exactly " +
                $"one verdict, {nameof(QaVerdict)}.{QaVerdict.HumanGapOnly}: the mechanical half of " +
                "the checklist is built and the human half is declared as a named, visible gap, so " +
                "a check that concludes a human item is a heuristic wearing the checklist's " +
                "clothes and would let a batch be accepted by machinery alone.");
        }

        return outcome;
    }
}
