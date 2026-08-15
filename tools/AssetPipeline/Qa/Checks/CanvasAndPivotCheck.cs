using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>Checklist item 7: correct canvas size and pivot.</summary>
/// <remarks>
/// <para>
/// Fully mechanical, and the only item with no tolerance: the canvas is an exact integer comparison
/// against the manifest's delivery size, and the pivot is verified against the content's alpha
/// bounding box — <see cref="Doc15Pivots.Center"/> means centred in both axes,
/// <see cref="Doc15Pivots.BottomCenter"/> means centred horizontally and sitting on the bottom edge.
/// </para>
/// <para>
/// Canvas is checked before pivot, and the first failure is the reported one: a pivot offset
/// measured against a wrong-sized canvas is a consequence of that, not a second defect.
/// </para>
/// <para>
/// Pivot is checked against the <em>content</em>, not a declaration — a declaration that disagrees
/// with where the subject actually sits is the failure this item exists to catch: it lands as a
/// limb sunk into the board or a hero floating above it.
/// </para>
/// </remarks>
public sealed class CanvasAndPivotCheck : IQaCheck
{
    /// <summary>The delivered canvas width, in pixels.</summary>
    public const string CanvasWidthMeasurement = "canvasWidthPx";

    /// <summary>The delivered canvas height, in pixels.</summary>
    public const string CanvasHeightMeasurement = "canvasHeightPx";

    /// <summary>
    /// How far the content's bounding box sits from where the row's pivot puts it, in pixels.
    /// Zero when the content is exactly where the pivot says.
    /// </summary>
    public const string PivotOffsetMeasurement = "pivotOffsetPx";

    /// <inheritdoc/>
    public int ItemNumber => 7;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item7;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.Mechanical;

    /// <inheritdoc/>
    public string DocReference => "15 §C";

    /// <inheritdoc/>
    public string? HumanGap => null;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var image = Raster.From(subject.Image);
        var target = subject.Spec.TargetSize;
        var content = image.OpaqueBounds();
        var offset = PivotOffset(image, content, subject.Spec.Pivot);
        var measurements = new StepMeasurement[]
        {
            new(CanvasWidthMeasurement, image.Width, "px", DocReference),
            new(CanvasHeightMeasurement, image.Height, "px", DocReference),
            new(PivotOffsetMeasurement, offset, "px", DocReference),
        };

        // Canvas is checked first: a pivot offset against an already-wrong-sized canvas is a
        // consequence of that, not a second defect.
        if (image.Width != target.Width)
        {
            return Failed(
                measurements,
                $"{CanvasWidthMeasurement} is {image.Width} and the register delivers " +
                $"'{subject.Spec.Id}' at {target.Width}. `15` §C's delivery size is exact.");
        }

        if (image.Height != target.Height)
        {
            return Failed(
                measurements,
                $"{CanvasHeightMeasurement} is {image.Height} and the register delivers " +
                $"'{subject.Spec.Id}' at {target.Height}. `15` §C's delivery size is exact.");
        }

        if (IsEmpty(content))
        {
            return Failed(
                measurements,
                $"'{subject.Spec.Id}' carries no visible pixel at all, so there is no content for " +
                $"`15` §C's '{subject.Spec.Pivot}' pivot to sit on.");
        }

        return offset == 0d
            ? new QaOutcome(
                QaVerdict.Pass,
                ItemNumber,
                $"{image.Width}×{image.Height} as the register delivers it, with the content " +
                $"exactly where `15` §C's '{subject.Spec.Pivot}' pivot puts it.",
                measurements,
                HumanGap: null)
            : Failed(
                measurements,
                $"{PivotOffsetMeasurement} is {QaEvidence.Number(offset)}: the content sits that " +
                $"far from where `15` §C's '{subject.Spec.Pivot}' pivot puts it on a " +
                $"{image.Width}×{image.Height} canvas.");
    }

    /// <summary>
    /// How far the content's bounding box sits from where the row's pivot puts it, as the distance
    /// between where its top-left corner is and where it would be.
    /// </summary>
    /// <remarks>
    /// The centring tie is broken the same way <see cref="TrimToCanvasStep"/> breaks it — integer
    /// division, so the odd pixel goes to the right and to the bottom. An asset whose content is one
    /// pixel narrower or shorter than the canvas in parity cannot be centred exactly, so expecting a
    /// half-pixel here would make this item unsatisfiable for every such row.
    /// </remarks>
    /// <param name="image">The processed image.</param>
    /// <param name="content">The content's bounding box.</param>
    /// <param name="pivot">One of <see cref="Doc15Pivots.All"/>.</param>
    private static double PivotOffset(Raster image, SKRectI content, string pivot)
    {
        if (IsEmpty(content))
        {
            return 0d;
        }

        // Horizontally centred for both pivots; only the vertical rule differs.
        var expectedLeft = (image.Width - content.Width) / 2;
        var expectedTop = pivot switch
        {
            Doc15Pivots.Center => (image.Height - content.Height) / 2,
            Doc15Pivots.BottomCenter => image.Height - content.Height,
            _ => throw new InvalidOperationException(
                $"`15` §C authorises {string.Join(" and ", Doc15Pivots.All)} and nothing else, and " +
                $"this row declares '{pivot}'. There is no third convention to measure against."),
        };

        return Math.Sqrt(
            Math.Pow(content.Left - expectedLeft, 2d) + Math.Pow(content.Top - expectedTop, 2d));
    }

    /// <summary>True when a bounding box holds no pixel.</summary>
    /// <param name="content">The content's bounding box.</param>
    private static bool IsEmpty(SKRectI content) => content.Width <= 0 || content.Height <= 0;

    /// <summary>A failing outcome carrying every measurement that was taken.</summary>
    /// <param name="measurements">The three measurements.</param>
    /// <param name="reason">Which claim broke, and what it measured.</param>
    private QaOutcome Failed(IReadOnlyList<StepMeasurement> measurements, string reason) =>
        new(QaVerdict.Fail, ItemNumber, reason, measurements, HumanGap: null);
}
