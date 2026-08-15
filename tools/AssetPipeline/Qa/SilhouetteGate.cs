using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>The four numbers the silhouette gate's mechanical floor is made of.</summary>
/// <remarks>
/// None of the four cutoffs is authorised anywhere; all four live in <see cref="ThresholdSet"/> as
/// null and <see cref="SilhouetteGate.Evaluate"/> throws naming whichever it reaches first.
/// </remarks>
/// <param name="CoverageRatio">
/// Share of the 64x64 frame the silhouette occupies, 0-1. A subject that shrank to nothing after
/// the resize reads here.
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

/// <summary>What the silhouette gate concluded, and what it did not conclude.</summary>
/// <remarks>
/// <see cref="HumanGap"/> is non-nullable on purpose, carried on every result including passing
/// ones: <see cref="MechanicalPass"/> means the silhouette cleared a floor somebody stated, never
/// that the human acceptance test passed.
/// </remarks>
/// <param name="MechanicalPass">True when all four measurements are within their stated cutoffs.</param>
/// <param name="Measurement">The four quantities, whatever the verdict.</param>
/// <param name="Measurements">The same four as <see cref="StepMeasurement"/>s, for a QA outcome.</param>
/// <param name="Reason">
/// Which measurement tripped and its value, or the empty string when none did.
/// </param>
/// <param name="HumanGap">The actual acceptance test, which this gate does not perform.</param>
public sealed record SilhouetteResult(
    bool MechanicalPass,
    SilhouetteMeasurement Measurement,
    IReadOnlyList<StepMeasurement> Measurements,
    string Reason,
    string HumanGap);

/// <summary>Fills a silhouette 100% black, scales it to a fixed size, and floors it mechanically.</summary>
/// <remarks>
/// <para>
/// This gate is not the human acceptance test: no code here decides whether a character is
/// recognisable. What is mechanised is the fill-and-scale plus four measurements that catch
/// silhouettes nobody would need to squint at. Every <see cref="SilhouetteResult"/> says so.
/// </para>
/// <para>
/// Asked for a cutoff nobody has stated, <see cref="Evaluate"/> raises
/// <see cref="UncalibratedThresholdException"/> naming the key — a measuring instrument with no
/// scale is broken, not permissive. The checklist item calling this turns that into a
/// <see cref="QaVerdict.Uncalibrated"/> outcome so one hole does not abort a batch.
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
    /// Not itself a threshold — it is the bottom of the scale the measurement is normalised onto.
    /// What counts as <em>enough</em> distinguishability is <see cref="ThresholdKeys.SilhouetteMinDistinguishability"/>.
    /// </remarks>
    public const double MinimumDistinguishability = 0d;

    /// <summary>
    /// The top of the same scale: the category holds nothing to be confused with, or the two masks
    /// share no pixel at all.
    /// </summary>
    public const double MaximumDistinguishability = 1d;

    /// <summary>The actual acceptance test, which this gate does not perform and every result says so.</summary>
    public const string HumanGap =
        "`15` §A4's acceptance test is a human judgement: \"" +
        Doc15PartF.SilhouetteAcceptanceSentence +
        "\" This gate performs §A4's first sentence exactly — fill it 100% black, scale to 64 px — " +
        "and then measures four quantities beneath the second one. A mechanical pass means the " +
        "silhouette cleared a floor somebody stated; it never means §A4 passed.";

    /// <summary>The section every silhouette measurement serves.</summary>
    private const string SilhouetteDocReference = "15 §A4";

    /// <summary>
    /// The silhouette mask: every pixel with a non-zero alpha becomes opaque black, everything else
    /// becomes fully transparent, and the result is exactly
    /// <see cref="Doc15Authorised.SilhouetteMaskSize"/> square.
    /// </summary>
    /// <remarks>
    /// Fill first, scale second: scaling a coloured asset down and only then filling it black would
    /// let a resampler's antialiasing decide the silhouette's edge.
    /// </remarks>
    /// <param name="image">Any bitmap. Only its alpha channel is read.</param>
    /// <returns>A new 64x64 mask. The input is unchanged.</returns>
    public static SKBitmap Render(SKBitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var source = Raster.From(image);
        var size = Doc15Authorised.SilhouetteMaskSize;
        var mask = Raster.Blank(size, size);

        for (var y = 0; y < size; y++)
        {
            var (top, bottom) = Footprint(y, source.Height, size);
            for (var x = 0; x < size; x++)
            {
                var (left, right) = Footprint(x, source.Width, size);
                if (AnyVisible(source, left, top, right, bottom))
                {
                    mask.SetColour(x, y, SKColors.Black);
                }
            }
        }

        return mask.ToBitmap();
    }

    /// <summary>Measures the four quantities of one rendered mask.</summary>
    /// <param name="mask">A 64x64 mask from <see cref="Render"/>. Any other size is a loud failure.</param>
    /// <param name="category">The category the registry is consulted for.</param>
    /// <param name="registry">The silhouettes already accepted. May be empty.</param>
    public static SilhouetteMeasurement Measure(
        SKBitmap mask, string category, SilhouetteRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(registry);

        var filled = MaskOf(mask);
        var bounds = BoundsOf(filled);
        var boxArea = (long)bounds.Width * bounds.Height;

        return new SilhouetteMeasurement(
            filled.Count / (double)(Doc15Authorised.SilhouetteMaskSize * Doc15Authorised.SilhouetteMaskSize),
            boxArea == 0 ? 0d : filled.Count / (double)boxArea,
            ComponentCount(filled),
            DistanceFromAccepted(filled, category, registry));
    }

    /// <summary>Renders, measures, and grades against the four stated cutoffs.</summary>
    /// <param name="image">The processed asset.</param>
    /// <param name="category">The category the registry is consulted for.</param>
    /// <param name="registry">The silhouettes already accepted. May be empty.</param>
    /// <param name="thresholds">
    /// The threshold set. Any of the four silhouette keys being null raises
    /// <see cref="UncalibratedThresholdException"/> naming it.
    /// </param>
    public static SilhouetteResult Evaluate(
        SKBitmap image,
        string category,
        SilhouetteRegistry registry,
        ThresholdSet thresholds)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(thresholds);

        // All four thresholds are read before a single pixel is graded, so which hole gets reported
        // does not depend on the asset.
        var minCoverage = thresholds.RequireNumber(ThresholdKeys.SilhouetteMinCoverageRatio);
        var minBoundingBoxFill = thresholds.RequireNumber(ThresholdKeys.SilhouetteMinBoundingBoxFill);
        var maxComponents = thresholds.RequireNumber(ThresholdKeys.SilhouetteMaxComponentCount);
        var minDistinguishability =
            thresholds.RequireNumber(ThresholdKeys.SilhouetteMinDistinguishability);

        using var mask = Render(image);
        var measurement = Measure(mask, category, registry);
        var reason = FirstFailure(
            measurement, minCoverage, minBoundingBoxFill, maxComponents, minDistinguishability);

        return new SilhouetteResult(
            reason.Length == 0,
            measurement,
            MeasurementsOf(measurement),
            reason,
            HumanGap);
    }

    /// <summary>
    /// Which cutoff the silhouette missed and by how much, or the empty string when it missed none.
    /// </summary>
    /// <remarks>
    /// Reports only the first miss so a reviewer knows which one to look at; the other three travel
    /// as <see cref="SilhouetteResult.Measurements"/>.
    /// </remarks>
    /// <param name="measurement">The four quantities.</param>
    /// <param name="minCoverage">The stated minimum coverage ratio.</param>
    /// <param name="minBoundingBoxFill">The stated minimum bounding-box fill.</param>
    /// <param name="maxComponents">The stated maximum 8-connected component count.</param>
    /// <param name="minDistinguishability">The stated minimum distance from an accepted silhouette.</param>
    private static string FirstFailure(
        SilhouetteMeasurement measurement,
        double minCoverage,
        double minBoundingBoxFill,
        double maxComponents,
        double minDistinguishability)
    {
        if (measurement.CoverageRatio < minCoverage)
        {
            return $"{CoverageRatioMeasurement} is " +
                   $"{QaEvidence.Number(measurement.CoverageRatio)}, below the stated minimum of " +
                   $"{QaEvidence.Number(minCoverage)}.";
        }

        if (measurement.BoundingBoxFill < minBoundingBoxFill)
        {
            return $"{BoundingBoxFillMeasurement} is " +
                   $"{QaEvidence.Number(measurement.BoundingBoxFill)}, below the stated minimum of " +
                   $"{QaEvidence.Number(minBoundingBoxFill)}.";
        }

        if (measurement.ConnectedComponentCount > maxComponents)
        {
            return $"{ComponentCountMeasurement} is {measurement.ConnectedComponentCount}, above " +
                   $"the stated maximum of {QaEvidence.Number(maxComponents)}: the silhouette has " +
                   "fallen apart.";
        }

        return measurement.Distinguishability < minDistinguishability
            ? $"{DistinguishabilityMeasurement} is " +
              $"{QaEvidence.Number(measurement.Distinguishability)}, below the stated minimum of " +
              $"{QaEvidence.Number(minDistinguishability)}: something already accepted in this " +
              "category looks like this."
            : string.Empty;
    }

    /// <summary>The four quantities as measurements, in the order this type declares them.</summary>
    /// <param name="measurement">The four quantities.</param>
    private static IReadOnlyList<StepMeasurement> MeasurementsOf(SilhouetteMeasurement measurement) =>
    [
        new StepMeasurement(
            CoverageRatioMeasurement, measurement.CoverageRatio, "ratio", SilhouetteDocReference),
        new StepMeasurement(
            BoundingBoxFillMeasurement, measurement.BoundingBoxFill, "ratio", SilhouetteDocReference),
        new StepMeasurement(
            ComponentCountMeasurement, measurement.ConnectedComponentCount, "count", SilhouetteDocReference),
        new StepMeasurement(
            DistinguishabilityMeasurement, measurement.Distinguishability, "ratio", SilhouetteDocReference),
    ];

    /// <summary>The mask as a pixel set, refusing anything not the expected size.</summary>
    /// <param name="mask">A mask from <see cref="Render"/>.</param>
    private static PixelMask MaskOf(SKBitmap mask)
    {
        var size = Doc15Authorised.SilhouetteMaskSize;
        if (mask.Width != size || mask.Height != size)
        {
            throw new InvalidOperationException(
                $"`15` §A4 scales the silhouette to {size} px and this mask is {mask.Width}×" +
                $"{mask.Height}. Every measurement here is a share of that frame, so measuring " +
                "another size would report ratios against a denominator the doc does not state.");
        }

        var raster = Raster.From(mask);
        var filled = new PixelMask(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                filled[x, y] = raster.AlphaAt(x, y) > 0;
            }
        }

        return filled;
    }

    /// <summary>The bounding box of a mask: left/top inclusive, right/bottom exclusive.</summary>
    /// <param name="filled">The mask to bound. An empty one bounds to nothing.</param>
    private static SKRectI BoundsOf(PixelMask filled)
    {
        var left = filled.Width;
        var top = filled.Height;
        var right = 0;
        var bottom = 0;

        foreach (var (x, y) in filled.Pixels())
        {
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
        }

        return right == 0 && bottom == 0 ? SKRectI.Empty : new SKRectI(left, top, right, bottom);
    }

    /// <summary>How many 8-connected blobs a mask holds.</summary>
    /// <remarks>
    /// 8-connected because a character whose arm meets its body only diagonally is one character,
    /// not two: 4-connectivity would report a limb as detached on a shape a person reads as whole.
    /// </remarks>
    /// <param name="filled">The mask to count.</param>
    private static int ComponentCount(PixelMask filled)
    {
        var seen = new PixelMask(filled.Width, filled.Height);
        var components = 0;

        foreach (var (startX, startY) in filled.Pixels())
        {
            if (seen[startX, startY])
            {
                continue;
            }

            components++;
            seen[startX, startY] = true;
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue((startX, startY));

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        var nextX = x + offsetX;
                        var nextY = y + offsetY;
                        if (!filled[nextX, nextY] || seen[nextX, nextY])
                        {
                            continue;
                        }

                        seen[nextX, nextY] = true;
                        queue.Enqueue((nextX, nextY));
                    }
                }
            }
        }

        return components;
    }

    /// <summary>
    /// The distance from the nearest already-accepted silhouette in the same category, as the
    /// Jaccard distance between the two masks: <c>1 - |A ∩ B| / |A ∪ B|</c>.
    /// </summary>
    /// <remarks>
    /// Jaccard rather than a pixel-difference count because it is already normalised onto the 0-1
    /// scale <see cref="SilhouetteMeasurement.Distinguishability"/> is defined on; a raw pixel count
    /// would make a small silhouette look distinguishable from everything simply by being small.
    /// </remarks>
    /// <param name="filled">The silhouette being measured.</param>
    /// <param name="category">The category to compare within.</param>
    /// <param name="registry">The silhouettes already accepted.</param>
    private static double DistanceFromAccepted(
        PixelMask filled, string category, SilhouetteRegistry registry)
    {
        var nearest = MaximumDistinguishability;
        var compared = 0;

        foreach (var entry in registry.InCategory(category))
        {
            nearest = Math.Min(nearest, JaccardDistance(filled, MaskOf(entry.Mask)));
            compared++;
        }

        // Nothing accepted in this category yet: the first asset of a category has nothing to be
        // confused with, which is the top of the scale rather than a hole in it.
        return compared == 0 ? MaximumDistinguishability : nearest;
    }

    /// <summary>One minus the intersection over the union of two same-sized masks.</summary>
    /// <param name="first">One mask.</param>
    /// <param name="second">The other.</param>
    private static double JaccardDistance(PixelMask first, PixelMask second)
    {
        var shared = 0;
        var either = 0;

        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                var inFirst = first[x, y];
                var inSecond = second[x, y];
                shared += inFirst && inSecond ? 1 : 0;
                either += inFirst || inSecond ? 1 : 0;
            }
        }

        // Two empty masks are the same shape, and the same shape is the bottom of the scale.
        return either == 0
            ? MinimumDistinguishability
            : MaximumDistinguishability - (shared / (double)either);
    }

    /// <summary>
    /// The source pixels one target pixel of the 64 px mask covers: left inclusive, right exclusive,
    /// never empty.
    /// </summary>
    /// <param name="target">The target coordinate.</param>
    /// <param name="sourceExtent">The source width or height.</param>
    /// <param name="targetExtent">The target width or height.</param>
    private static (int From, int To) Footprint(int target, int sourceExtent, int targetExtent)
    {
        var from = (int)((long)target * sourceExtent / targetExtent);
        var to = (int)((long)(target + 1) * sourceExtent / targetExtent);
        return (Math.Min(from, sourceExtent - 1), Math.Clamp(to, from + 1, sourceExtent));
    }

    /// <summary>True when anything inside a footprint is visible.</summary>
    /// <remarks>
    /// A union, not an average with a cutoff: any ink counts as silhouette, so a footprint that is
    /// mostly transparent but has one opaque pixel still reads as filled after the downscale.
    /// </remarks>
    /// <param name="source">The source image.</param>
    /// <param name="left">The footprint's left edge, inclusive.</param>
    /// <param name="top">The footprint's top edge, inclusive.</param>
    /// <param name="right">The footprint's right edge, exclusive.</param>
    /// <param name="bottom">The footprint's bottom edge, exclusive.</param>
    private static bool AnyVisible(Raster source, int left, int top, int right, int bottom)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (source.AlphaAt(x, y) > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
