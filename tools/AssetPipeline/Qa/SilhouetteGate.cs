using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// The four numbers `15` §A4's mechanical floor is made of.
/// </summary>
/// <remarks>
/// 🔒 Four numbers, four thresholds, and not one of them authorised by `15`. §A4 states a test and
/// a bar (<em>"If you cannot tell which character it is"</em>) and no cutoffs at all, so all four
/// live in <see cref="ThresholdSet"/> as null and <see cref="SilhouetteGate.Evaluate"/> throws
/// naming whichever it reaches first.
/// </remarks>
/// <param name="CoverageRatio">
/// Share of the 64x64 frame the silhouette occupies, 0-1. A subject that shrank to nothing after
/// the §B4 resize reads here.
/// </param>
/// <param name="BoundingBoxFill">
/// Share of its own bounding box the silhouette fills, 0-1. Separates a solid readable shape from a
/// sparse one that merely spans the frame.
/// </param>
/// <param name="ConnectedComponentCount">
/// How many 8-connected blobs the mask holds. A character that fell apart into detached limbs
/// during background removal reads here.
/// </param>
/// <param name="Distinguishability">
/// Distance from the nearest already-accepted silhouette in the same category, normalised to 0-1:
/// <see cref="SilhouetteGate.MinimumDistinguishability"/> for an identical one,
/// <see cref="SilhouetteGate.MaximumDistinguishability"/> when the category holds none or the two
/// masks share no pixel.
/// </param>
public sealed record SilhouetteMeasurement(
    double CoverageRatio,
    double BoundingBoxFill,
    int ConnectedComponentCount,
    double Distinguishability);

/// <summary>What the `15` §A4 gate concluded, and what it did not conclude.</summary>
/// <remarks>
/// 🔒 <see cref="HumanGap"/> is non-nullable on purpose. It is carried on every result, including
/// every passing one — the case where it would be most tempting to omit it and most misleading to.
/// <see cref="MechanicalPass"/> means the silhouette cleared a floor somebody stated; it never
/// means §A4 passed.
/// </remarks>
/// <param name="MechanicalPass">True when all four measurements are within their stated cutoffs.</param>
/// <param name="Measurement">The four quantities, whatever the verdict.</param>
/// <param name="Measurements">The same four as <see cref="StepMeasurement"/>s, for a QA outcome.</param>
/// <param name="Reason">
/// Which measurement tripped and its value, or the empty string when none did.
/// </param>
/// <param name="HumanGap">`15` §A4's actual test, which this gate does not perform.</param>
public sealed record SilhouetteResult(
    bool MechanicalPass,
    SilhouetteMeasurement Measurement,
    IReadOnlyList<StepMeasurement> Measurements,
    string Reason,
    string HumanGap);

/// <summary>
/// `15` §A4: <em>"fill it 100% black, scale to 64 px"</em> — and the mechanical floor under the
/// sentence that follows it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This gate is not §A4.</b> §A4's acceptance test is
/// <em>"If you cannot tell which character it is, regenerate it"</em>, and no code in this project
/// decides that. What is mechanised is the fill-and-scale (which is stated exactly, and which this
/// does exactly) plus four measurements that catch silhouettes nobody would need to squint at.
/// Every <see cref="SilhouetteResult"/> carries the difference in writing.
/// </para>
/// <para>
/// 🔒 <b>The gate throws where a check would not.</b> Asked for a cutoff nobody has stated,
/// <see cref="Evaluate"/> raises <see cref="UncalibratedThresholdException"/> naming the key — it
/// is a measuring instrument and an instrument with no scale is broken, not permissive. `15` Part F
/// item 1 is the seam that turns that into a <see cref="QaVerdict.Uncalibrated"/> outcome so one
/// hole does not abort a batch.
/// </para>
/// </remarks>
public static class SilhouetteGate
{
    /// <summary>The measurement key for <see cref="SilhouetteMeasurement.CoverageRatio"/>.</summary>
    public const string CoverageRatioMeasurement = "silhouetteCoverageRatio";

    /// <summary>The measurement key for <see cref="SilhouetteMeasurement.BoundingBoxFill"/>.</summary>
    public const string BoundingBoxFillMeasurement = "silhouetteBoundingBoxFill";

    /// <summary>The measurement key for <see cref="SilhouetteMeasurement.ConnectedComponentCount"/>.</summary>
    public const string ComponentCountMeasurement = "silhouetteComponentCount";

    /// <summary>The measurement key for <see cref="SilhouetteMeasurement.Distinguishability"/>.</summary>
    public const string DistinguishabilityMeasurement = "silhouetteDistinguishability";

    /// <summary>
    /// What <see cref="SilhouetteMeasurement.Distinguishability"/> reads for a silhouette identical
    /// to one already accepted in its category: nothing tells them apart.
    /// </summary>
    /// <remarks>
    /// Not a threshold and not an S6 hole — it is the bottom of the scale the measurement is
    /// normalised onto, the way 0 is the bottom of a ratio. What counts as <em>enough</em>
    /// distinguishability is the hole, and it is
    /// <see cref="ThresholdKeys.SilhouetteMinDistinguishability"/>.
    /// </remarks>
    public const double MinimumDistinguishability = 0d;

    /// <summary>
    /// The top of the same scale: the category holds nothing to be confused with, or the two masks
    /// share no pixel at all.
    /// </summary>
    public const double MaximumDistinguishability = 1d;

    /// <summary>
    /// `15` §A4's mask: every pixel with a non-zero alpha becomes opaque black, everything else
    /// becomes fully transparent, and the result is exactly
    /// <see cref="Doc15Authorised.SilhouetteMaskSize"/> square.
    /// </summary>
    /// <remarks>
    /// 🔒 Fill first, scale second. §A4's own order is "fill it 100% black, scale to 64 px", and it
    /// is the order that answers the question: scaling a coloured asset down and only then filling
    /// it black would let a resampler's antialiasing decide the silhouette's edge.
    /// </remarks>
    /// <param name="image">Any bitmap. Only its alpha channel is read.</param>
    /// <returns>A new 64x64 mask. The input is unchanged.</returns>
    public static SKBitmap Render(SKBitmap image) => throw new NotImplementedException();

    /// <summary>Measures the four quantities of one rendered mask.</summary>
    /// <param name="mask">A 64x64 mask from <see cref="Render"/>. Any other size is a loud failure.</param>
    /// <param name="category">The `15` §D1 category the registry is consulted for.</param>
    /// <param name="registry">The silhouettes already accepted. May be empty.</param>
    public static SilhouetteMeasurement Measure(
        SKBitmap mask, string category, SilhouetteRegistry registry) =>
        throw new NotImplementedException();

    /// <summary>Renders, measures, and grades against the four stated cutoffs.</summary>
    /// <param name="image">The processed asset.</param>
    /// <param name="category">The `15` §D1 category the registry is consulted for.</param>
    /// <param name="registry">The silhouettes already accepted. May be empty.</param>
    /// <param name="thresholds">
    /// The threshold set. Any of the four silhouette keys being null raises
    /// <see cref="UncalibratedThresholdException"/> naming it.
    /// </param>
    public static SilhouetteResult Evaluate(
        SKBitmap image,
        string category,
        SilhouetteRegistry registry,
        ThresholdSet thresholds) =>
        throw new NotImplementedException();
}
