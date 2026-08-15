namespace SlayIdleRepeat.AssetPipeline.Qa.Checks;

/// <summary>Checklist item 3: outline continuous, uniform width, colour #231A2E.</summary>
/// <remarks>
/// <para>
/// Three claims, three measurements, each able to fail on its own:
/// </para>
/// <list type="bullet">
///   <item><b>continuous</b> — <see cref="OutlineLeakMeasurement"/>: flood the non-outline pixels
///   inward from the frame edge; any interior pixel the flood reaches means the outline does not
///   enclose the subject. Zero is continuous.</item>
///   <item><b>uniform width</b> — <see cref="OutlineWidthMeasurement"/> and
///   <see cref="OutlineWidthSpreadMeasurement"/>, graded against the authorised band at this
///   asset's canvas (<see cref="Doc15Authorised.OutlineWidthBandFor"/>) and widened by
///   <see cref="ThresholdKeys.OutlineWidthUniformityTolerance"/>.</item>
///   <item><b>colour</b> — <see cref="OutlineColourDistanceMeasurement"/>: mean RGB distance from
///   <see cref="Doc15Authorised.OutlineColourHex"/>, against
///   <see cref="ThresholdKeys.OutlineColourTolerance"/>.</item>
/// </list>
/// <para>
/// Colour is checked before continuity, before width, and the first failure is the reported one —
/// not the order the claims are listed above: the width and leak measurements are taken over the
/// colour-seeded outline mask, so an outline painted the wrong colour yields an empty mask and a
/// measured width of zero, which would otherwise read as a second, independent defect. The colour
/// measurement itself is taken over the <em>geometric</em> boundary band, precisely so it can be
/// far from the target colour and say so.
/// </para>
/// <para>
/// Width <em>conformance</em> lives here, not in the repair step that fixes continuity — a step
/// that both changed the width and graded it would be marking its own homework.
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

    /// <summary>Mean RGB distance of the outline pixels from the target colour.</summary>
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
    public QaOutcome Evaluate(QaSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var image = Raster.From(subject.Image);
        var (bandMin, bandMax) = Doc15Authorised.OutlineWidthBandFor(image.Width);
        var visible = VisibleMask(image);
        var colourDistance = BoundaryColourDistance(image, visible, bandMax);

        try
        {
            var colourTolerance =
                subject.Thresholds.RequireNumber(ThresholdKeys.OutlineColourTolerance);
            var uniformity =
                subject.Thresholds.RequireNumber(ThresholdKeys.OutlineWidthUniformityTolerance);

            var outline = OutlineMask(image, colourTolerance);
            var leak = LeakCount(visible, outline);
            var (width, spread) = WidthOf(outline);
            IReadOnlyList<StepMeasurement> measurements =
            [
                new StepMeasurement(OutlineLeakMeasurement, leak, "count", DocReference),
                new StepMeasurement(OutlineWidthMeasurement, width, "px", DocReference),
                new StepMeasurement(OutlineWidthSpreadMeasurement, spread, "px", DocReference),
                new StepMeasurement(
                    OutlineColourDistanceMeasurement, colourDistance, "rgb", DocReference),
            ];

            return FirstFailure(measurements, colourDistance, colourTolerance, leak, width, spread,
                       bandMin, bandMax, uniformity)
                   ?? new QaOutcome(
                       QaVerdict.Pass,
                       ItemNumber,
                       $"The outline is `15` §A3's {Doc15Authorised.OutlineColourHex}, it encloses " +
                       $"the subject, and it measures {QaEvidence.Number(width)} px against §A3's " +
                       $"{QaEvidence.Number(bandMin)}-{QaEvidence.Number(bandMax)} px band at this " +
                       "canvas.",
                       measurements,
                       HumanGap: null);
        }
        catch (UncalibratedThresholdException uncalibrated)
        {
            // The colour distance needs no tolerance to be taken, so it travels even here: it is
            // what a reviewer would look at first, and it is the one number this item can always
            // produce.
            return QaEvidence.Uncalibrated(
                ItemNumber,
                uncalibrated,
                [
                    new StepMeasurement(
                        OutlineColourDistanceMeasurement, colourDistance, "rgb", DocReference),
                ]);
        }
    }

    /// <summary>
    /// Which of item 3's three claims broke, in the order colour, continuity, width — or null when
    /// none did.
    /// </summary>
    /// <param name="measurements">Everything measured, carried on whatever comes back.</param>
    /// <param name="colourDistance">Mean distance of the boundary band from #231A2E.</param>
    /// <param name="colourTolerance">How far antialiasing may drag a pixel off that colour.</param>
    /// <param name="leak">Interior pixels a flood from the frame edge reached.</param>
    /// <param name="width">The outline's measured width.</param>
    /// <param name="spread">The gap between its thinnest and thickest place.</param>
    /// <param name="bandMin">The authorised band at this canvas, lower bound.</param>
    /// <param name="bandMax">The authorised band at this canvas, upper bound.</param>
    /// <param name="uniformity">The stated slack around that band.</param>
    private QaOutcome? FirstFailure(
        IReadOnlyList<StepMeasurement> measurements,
        double colourDistance,
        double colourTolerance,
        int leak,
        double width,
        double spread,
        double bandMin,
        double bandMax,
        double uniformity)
    {
        if (colourDistance > colourTolerance)
        {
            return Failed(
                measurements,
                $"{OutlineColourDistanceMeasurement} is {QaEvidence.Number(colourDistance)}, past " +
                $"the stated tolerance of {QaEvidence.Number(colourTolerance)}: the boundary band " +
                $"is not `15` §A3's {Doc15Authorised.OutlineColourHex}.");
        }

        if (leak > 0)
        {
            return Failed(
                measurements,
                $"{OutlineLeakMeasurement} is {leak}: that many visible interior pixels are " +
                "reachable from the frame edge without crossing the outline, so it does not " +
                "enclose the subject.");
        }

        // A band, not a single value, and the tolerance widens it on both sides. Both the width
        // and the spread are graded against that one widened band.
        var floor = bandMin - uniformity;
        var ceiling = bandMax + uniformity;

        if (width < floor || width > ceiling)
        {
            return Failed(
                measurements,
                $"{OutlineWidthMeasurement} is {QaEvidence.Number(width)}, outside `15` §A3's " +
                $"{QaEvidence.Number(bandMin)}-{QaEvidence.Number(bandMax)} px band at this canvas " +
                $"widened to {QaEvidence.Number(floor)}-{QaEvidence.Number(ceiling)} by the stated " +
                "slack.");
        }

        return spread > ceiling - floor
            ? Failed(
                measurements,
                $"{OutlineWidthSpreadMeasurement} is {QaEvidence.Number(spread)}: the outline's " +
                "thinnest and thickest places differ by more than the whole " +
                $"{QaEvidence.Number(ceiling - floor)} px the widened `15` §A3 band permits, so it " +
                "is not of uniform width.")
            : null;
    }

    /// <summary>Every pixel with a non-zero alpha.</summary>
    /// <param name="image">The processed image.</param>
    private static PixelMask VisibleMask(Raster image)
    {
        var visible = new PixelMask(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                visible[x, y] = image.AlphaAt(x, y) > 0;
            }
        }

        return visible;
    }

    /// <summary>Every visible pixel within the tolerance of the target outline colour.</summary>
    /// <param name="image">The processed image.</param>
    /// <param name="tolerance">How far from #231A2E still reads as outline.</param>
    private static PixelMask OutlineMask(Raster image, double tolerance)
    {
        var outline = new PixelMask(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                outline[x, y] = image.AlphaAt(x, y) > 0
                                && Raster.RgbDistance(
                                       image.ColourAt(x, y), Doc15Authorised.OutlineColour)
                                   <= tolerance;
            }
        }

        return outline;
    }

    /// <summary>
    /// The mean distance from the target colour over the <em>geometric</em> boundary band — the
    /// visible pixels lying within the band width of the edge of the subject.
    /// </summary>
    /// <remarks>
    /// Geometric, not colour-seeded, so it can be far from the target colour and say so: a band
    /// selected by nearness to the outline colour could only ever measure as near to it. The
    /// outermost visible layer is always included, because at small canvases the band is under a
    /// pixel wide and a boundary of no pixels would make any claim true.
    /// </remarks>
    /// <param name="image">The processed image.</param>
    /// <param name="visible">Every visible pixel.</param>
    /// <param name="bandMax">The authorised band at this canvas, upper bound.</param>
    private static double BoundaryColourDistance(Raster image, PixelMask visible, double bandMax)
    {
        var depth = DepthInside(visible);
        var band = Math.Max(bandMax, 1d);
        var total = 0d;
        var counted = 0;

        foreach (var (x, y) in visible.Pixels())
        {
            if (depth[(y * visible.Width) + x] > band)
            {
                continue;
            }

            total += Raster.RgbDistance(image.ColourAt(x, y), Doc15Authorised.OutlineColour);
            counted++;
        }

        return counted == 0 ? 0d : total / counted;
    }

    /// <summary>
    /// How many visible pixels outside the outline a flood from the frame edge reaches — zero when
    /// the outline encloses the subject.
    /// </summary>
    /// <param name="visible">Every visible pixel.</param>
    /// <param name="outline">Every outline pixel.</param>
    private static int LeakCount(PixelMask visible, PixelMask outline)
    {
        var reached = outline.Complement().FloodFromBorder();
        var leaked = 0;

        foreach (var (x, y) in visible.Except(outline).Pixels())
        {
            leaked += reached[x, y] ? 1 : 0;
        }

        return leaked;
    }

    /// <summary>
    /// The outline's width and the gap between its thinnest and thickest place, both in pixels.
    /// </summary>
    /// <remarks>
    /// The width comes from the mean depth of the band: in a band of width <c>w</c> the distance to
    /// the nearer edge averages <c>w/4</c>, and pixel centres sit half a pixel inside the edge they
    /// are measured from. The spread comes from the largest disc that fits at each pixel — twice its
    /// depth — because "thinnest and thickest place" is a local question the mean cannot answer.
    /// </remarks>
    /// <param name="outline">Every outline pixel. An empty mask measures zero.</param>
    private static (double Width, double Spread) WidthOf(PixelMask outline)
    {
        var depth = DepthInside(outline);
        var total = 0d;
        var counted = 0;
        var thinnest = double.PositiveInfinity;
        var thickest = 0d;

        foreach (var (x, y) in outline.Pixels())
        {
            var inside = depth[(y * outline.Width) + x];
            total += inside;
            counted++;
            thinnest = Math.Min(thinnest, inside);
            thickest = Math.Max(thickest, inside);
        }

        return counted == 0
            ? (0d, 0d)
            : ((4d * ((total / counted) - 0.5d)), 2d * (thickest - thinnest));
    }

    /// <summary>
    /// Each pixel's Euclidean distance to the nearest pixel the mask does not hold, counting
    /// everything beyond the frame as not held.
    /// </summary>
    /// <remarks>
    /// The frame edge is outside: an outline that runs along the edge of its canvas is bounded there
    /// as surely as it is by the subject, and measuring its depth only inward would report it twice
    /// as thick as it is.
    /// </remarks>
    /// <param name="mask">The set to measure inside.</param>
    private static double[] DepthInside(PixelMask mask)
    {
        var padded = new PixelMask(mask.Width + 2, mask.Height + 2);
        for (var y = 0; y < padded.Height; y++)
        {
            for (var x = 0; x < padded.Width; x++)
            {
                padded[x, y] = x == 0
                               || y == 0
                               || x == padded.Width - 1
                               || y == padded.Height - 1
                               || !mask[x - 1, y - 1];
            }
        }

        var paddedDistance = padded.DistanceToSet();
        var depth = new double[mask.Width * mask.Height];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                depth[(y * mask.Width) + x] = paddedDistance[((y + 1) * padded.Width) + x + 1];
            }
        }

        return depth;
    }

    /// <summary>A failing outcome carrying every measurement that was taken.</summary>
    /// <param name="measurements">The four measurements.</param>
    /// <param name="reason">Which claim broke.</param>
    private QaOutcome Failed(IReadOnlyList<StepMeasurement> measurements, string reason) =>
        new(QaVerdict.Fail, ItemNumber, reason, measurements, HumanGap: null);
}
