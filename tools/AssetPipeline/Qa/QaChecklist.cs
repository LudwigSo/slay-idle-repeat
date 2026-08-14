using SlayIdleRepeat.AssetPipeline.Qa.Checks;

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

    /// <summary>A checklist over a caller's own eleven items, in `15` Part F order.</summary>
    /// <remarks>
    /// 🔒 The seam M8-10 needs to substitute or wrap an item without forking this type, and the
    /// only way <see cref="Graded"/>'s assumption-A5 guard can be exercised against a check that
    /// breaks it. The same S3 floor applies: eleven items, numbered 1 to 11, in order.
    /// </remarks>
    /// <param name="items">The eleven checks, in `15` Part F order.</param>
    public QaChecklist(IReadOnlyList<IQaCheck> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = [.. items];

        var numbers = Items.Select(item => item.ItemNumber).ToArray();
        if (Items.Count != Doc15PartF.ItemCount
            || !numbers.SequenceEqual(Enumerable.Range(1, Doc15PartF.ItemCount)))
        {
            throw new InvalidOperationException(
                $"`15` Part F lists {Doc15PartF.ItemCount} items, numbered 1 to " +
                $"{Doc15PartF.ItemCount} in its own order, and this checklist holds " +
                $"[{string.Join(", ", numbers)}]. A checklist that can silently shrink or reorder " +
                "passes forever (steering rule S3).");
        }
    }

    /// <summary>The eleven checks, in `15` Part F order. Index 0 is item 1.</summary>
    public IReadOnlyList<IQaCheck> Items { get; }

    /// <summary>How many items a machine decides outright. `15` Part F: two.</summary>
    public int MechanicalCount => OfClassification(QaClassification.Mechanical).Count;

    /// <summary>How many a machine decides once somebody states a threshold. `15` Part F: four.</summary>
    public int UncalibratedThresholdCount =>
        OfClassification(QaClassification.MechanicalUncalibratedThreshold).Count;

    /// <summary>How many no machine decides. `15` Part F: five.</summary>
    public int HumanCount => OfClassification(QaClassification.Human).Count;

    /// <summary>
    /// Every named gap the checklist carries, in item order — the five human items' and item 1's.
    /// </summary>
    /// <remarks>
    /// 🔒 First-class data, not commentary. This is the list M8-10 prints beside a batch report so
    /// that "the pipeline accepted 942 assets" can never be read as "942 assets passed Part F".
    /// </remarks>
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

    /// <summary>
    /// Runs one item and enforces assumption A5 on what it returned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Assumption A5, as structure rather than as five coincidences.</b> A5 says the human
    /// half of `15` Part F is declared as a named, visible gap and never dressed up as a machine
    /// verdict. Today the five <see cref="QaClassification.Human"/> items each happen to return
    /// <see cref="QaVerdict.HumanGapOnly"/> from a single statement, and only a per-item test pins
    /// it — so an <see cref="IQaCheck"/> M8-10 writes could return <see cref="QaVerdict.Pass"/> from
    /// a <see cref="QaClassification.Human"/> classification and nothing in this type would object.
    /// That is the exact failure A5 exists to prevent: a batch accepted by machinery alone.
    /// </para>
    /// <para>
    /// It throws rather than downgrading the verdict. A check that claims a human judgement is
    /// wrong about what it is, and quietly rewriting its answer would leave a report that looks
    /// correct and a check that is not.
    /// </para>
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
