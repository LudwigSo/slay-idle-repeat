namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 2: <em>"Readable at the smallest in-game display size"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Human.</b> "Readable" is not a predicate over pixels. `15` §A3's detail budget says
/// <em>"If a detail is not readable at 64 px, remove it"</em> — a sentence addressed to an artist,
/// and one that presupposes somebody deciding what counts as a detail.
/// </para>
/// <para>
/// What this emits is <see cref="ContrastRetentionMeasurement"/>: how much of the asset's contrast
/// survives a downscale to the smallest size the manifest ships it at. It is evidence for a
/// reviewer sorting a batch, and it gates nothing — the verdict is always
/// <see cref="QaVerdict.HumanGapOnly"/>.
/// </para>
/// </remarks>
public sealed class ReadabilityCheck : IQaCheck
{
    /// <summary>
    /// Share of the asset's full-size luminance spread that survives the downscale, 0-1. Evidence,
    /// not a gate — there is no threshold for it and there must not be one.
    /// </summary>
    public const string ContrastRetentionMeasurement = "contrastRetentionRatio";

    /// <summary>The judgement this project does not make.</summary>
    public const string ReadabilityHumanGap =
        "\"Readable\" is not a predicate over pixels, and 15 §A3's detail budget (\"If a detail " +
        "is not readable at 64 px, remove it\") is addressed to an artist. The contrast-retention " +
        "measurement is evidence for a reviewer, not a verdict: a human must look at the asset at " +
        "its smallest in-game size and say.";

    /// <inheritdoc/>
    public int ItemNumber => 2;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item2;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Human;

    /// <inheritdoc/>
    public string DocReference => "15 §A3 detail budget";

    /// <inheritdoc/>
    public string? HumanGap => ReadabilityHumanGap;

    /// <summary>
    /// `15` §A3's detail budget: <em>"If a detail is not readable at 64 px, remove it."</em> The one
    /// size the doc itself attaches to readability, and the size this measurement downscales to.
    /// </summary>
    /// <remarks>
    /// 🔒 An authorised number, not an invented one — so it lives in <see cref="Doc15Authorised"/>
    /// with the sentence that states it, and this is an alias rather than a second copy. It is the
    /// size a <em>detail</em> is judged at, not "the smallest in-game display size", which `15` never
    /// states per row. The measurement is therefore evidence about detail survival and not an answer
    /// to item 2.
    /// </remarks>
    private const int DetailBudgetSize = Doc15Authorised.DetailBudgetSize;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var image = Raster.From(subject.Image);
        var full = SpreadAtFullSize(image);
        var reduced = SpreadAtDetailBudget(image);
        var retention = full <= 0d ? 0d : Math.Min(1d, reduced / full);

        return new QaOutcome(
            QaVerdict.HumanGapOnly,
            ItemNumber,
            $"Measured only: {QaEvidence.Number(retention)} of this asset's visible luminance " +
            $"spread survives a downscale to `15` §A3's {DetailBudgetSize} px detail budget. " +
            "Nothing was decided from it.",
            [
                new StepMeasurement(
                    ContrastRetentionMeasurement, retention, "ratio", DocReference),
            ],
            ReadabilityHumanGap);
    }

    /// <summary>The luminance spread across every visible pixel, as delivered.</summary>
    /// <param name="image">The processed image.</param>
    private static double SpreadAtFullSize(Raster image)
    {
        var luminances = new List<double>(image.PixelCount);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.AlphaAt(x, y) > 0)
                {
                    luminances.Add(QaEvidence.Luminance(image.ColourAt(x, y)));
                }
            }
        }

        return Spread(luminances);
    }

    /// <summary>
    /// The luminance spread after a box downscale to `15` §A3's detail budget, over the cells that
    /// hold any visible pixel at all.
    /// </summary>
    /// <remarks>
    /// A box average rather than a resampler: this measures how much contrast neighbouring pixels
    /// lose to each other when they land in the same screen pixel, and a sharpening kernel would
    /// measure the kernel instead. An asset already at or below the budget loses nothing, and reads
    /// as full retention rather than as a problem.
    /// </remarks>
    /// <param name="image">The processed image.</param>
    private static double SpreadAtDetailBudget(Raster image)
    {
        var longest = Math.Max(image.Width, image.Height);
        var scale = Math.Min(1d, DetailBudgetSize / (double)longest);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale, MidpointRounding.AwayFromZero));
        var cells = new List<double>(width * height);

        for (var y = 0; y < height; y++)
        {
            var top = y * image.Height / height;
            var bottom = Math.Max(top + 1, (y + 1) * image.Height / height);
            for (var x = 0; x < width; x++)
            {
                var left = x * image.Width / width;
                var right = Math.Max(left + 1, (x + 1) * image.Width / width);
                var cell = CellLuminance(image, left, top, right, bottom);
                if (cell is not null)
                {
                    cells.Add(cell.Value);
                }
            }
        }

        return Spread(cells);
    }

    /// <summary>
    /// The mean luminance of one downscaled cell's visible pixels, or null where it holds none.
    /// </summary>
    /// <param name="image">The processed image.</param>
    /// <param name="left">The cell's left edge, inclusive.</param>
    /// <param name="top">The cell's top edge, inclusive.</param>
    /// <param name="right">The cell's right edge, exclusive.</param>
    /// <param name="bottom">The cell's bottom edge, exclusive.</param>
    private static double? CellLuminance(Raster image, int left, int top, int right, int bottom)
    {
        var total = 0d;
        var visible = 0;

        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (image.AlphaAt(x, y) == 0)
                {
                    continue;
                }

                total += QaEvidence.Luminance(image.ColourAt(x, y));
                visible++;
            }
        }

        return visible == 0 ? null : total / visible;
    }

    /// <summary>The distance between the brightest and darkest of a set of luminances.</summary>
    /// <param name="luminances">The luminances. An empty set spreads over nothing.</param>
    private static double Spread(IReadOnlyList<double> luminances) =>
        luminances.Count == 0 ? 0d : luminances.Max() - luminances.Min();
}
