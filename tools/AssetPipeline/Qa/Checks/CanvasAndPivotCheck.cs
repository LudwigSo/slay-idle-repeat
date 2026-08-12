namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 7: <em>"Correct canvas size and pivot per §C"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Fully mechanical, and the only item with no tolerance of any kind.</b> The canvas is an
/// exact integer comparison against the manifest's delivery size — 511 px is wrong, not nearly
/// right — and the pivot is verified against the content's alpha bounding box:
/// <see cref="Doc15Pivots.Center"/> means the content is centred in both axes,
/// <see cref="Doc15Pivots.BottomCenter"/> means centred horizontally and sitting on the bottom
/// edge. Both come from the row, so there is nothing here for anybody to calibrate.
/// </para>
/// <para>
/// 🔒 <b>Order: canvas, then pivot</b> — the first failure is the reported one. A pivot offset
/// measured against a canvas that is already the wrong size is not a second defect, it is a
/// consequence of the first, and reporting both would send a reviewer looking for two problems.
/// </para>
/// <para>
/// 🔒 Pivot is checked against the <em>content</em>, not against a declaration. `15` §C says the
/// pivot is "declared in the atlas metadata", and a declaration that disagrees with where the
/// subject actually sits is the failure this item exists to catch: it lands as a limb sunk into the
/// board or a hero floating above it, on every screen the asset appears on.
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
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
