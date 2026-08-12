namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>
/// `15` Part F item 3: <em>"Outline continuous, uniform width, colour #231A2E"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Three claims, three measurements, each able to fail on its own:
/// </para>
/// <list type="bullet">
///   <item><b>continuous</b> — <see cref="OutlineLeakMeasurement"/>: flood the non-outline pixels
///   inward from the frame edge; any interior pixel the flood reaches means the outline does not
///   enclose the subject. Zero is continuous.</item>
///   <item><b>uniform width</b> — <see cref="OutlineWidthMeasurement"/> and
///   <see cref="OutlineWidthSpreadMeasurement"/>, graded against `15` §A3's 3-4 px at 512 px
///   canvas, scaled proportionally to this asset's canvas
///   (<see cref="Doc15Authorised.OutlineWidthBandFor"/>) and widened by
///   <see cref="ThresholdKeys.OutlineWidthUniformityTolerance"/>.</item>
///   <item><b>colour</b> — <see cref="OutlineColourDistanceMeasurement"/>: mean RGB distance from
///   <see cref="Doc15Authorised.OutlineColourHex"/>, against
///   <see cref="ThresholdKeys.OutlineColourTolerance"/>.</item>
/// </list>
/// <para>
/// 🔒 <b>Half of this item's numbers are authorised and half are not, and the two halves are kept
/// visibly apart.</b> §A3 states the colour and the 3-4 px band, so both are
/// <see cref="Doc15Authorised"/> constants. It states nothing about how far antialiasing may drag a
/// pixel off that colour, nor how much the width may vary around the band, so both of those are
/// null thresholds and this check reports <see cref="QaVerdict.Uncalibrated"/> without them.
/// </para>
/// <para>
/// 🔒 <b>Order: colour, then continuity, then width</b> — the first failure is the reported one.
/// Not Part F's word order, and for a reason: the width and leak measurements are taken over the
/// colour-seeded outline mask, so an outline painted the wrong colour yields an empty mask and a
/// measured width of zero. Reporting "width 0" for a black outline would name a symptom of the
/// colour failure as though it were a second, independent defect (steering rule S2). The colour
/// measurement itself is taken over the <em>geometric</em> boundary band — the visible pixels
/// adjacent to the alpha boundary — precisely so that it can be far from #231A2E and say so.
/// </para>
/// <para>
/// 🔒 Width <em>conformance</em> lives here and not in `15` §B4 step 4, which repairs continuity.
/// A step that both changed the width and graded it would be marking its own homework.
/// </para>
/// </remarks>
public sealed class OutlineConformanceCheck : IQaCheck
{
    /// <summary>Interior pixels reachable from the frame edge without crossing the outline.</summary>
    public const string OutlineLeakMeasurement = "outlineLeakPixelCount";

    /// <summary>The outline's mean width, in pixels.</summary>
    public const string OutlineWidthMeasurement = "outlineWidthPx";

    /// <summary>The spread between the outline's thinnest and thickest place, in pixels.</summary>
    public const string OutlineWidthSpreadMeasurement = "outlineWidthSpreadPx";

    /// <summary>Mean RGB distance of the outline pixels from `15` §A3's #231A2E.</summary>
    public const string OutlineColourDistanceMeasurement = "outlineColourMeanDistance";

    /// <inheritdoc/>
    public int ItemNumber => 3;

    /// <inheritdoc/>
    public string ChecklistText => Doc15PartF.Item3;

    /// <inheritdoc/>
    public QaClassification Classification => QaClassification.MechanicalUncalibratedThreshold;

    /// <inheritdoc/>
    public string DocReference => "15 §A3";

    /// <inheritdoc/>
    public string? HumanGap => null;

    /// <inheritdoc/>
    public QaOutcome Evaluate(QaSubject subject) => throw new NotImplementedException();
}
