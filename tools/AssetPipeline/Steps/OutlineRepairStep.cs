namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 4: <em>"Outline repair -&gt; ensure the outline is continuous and uniform
/// width"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Builds the outline mask (within <see cref="ThresholdKeys.OutlineColourTolerance"/> of
/// <see cref="Doc15Authorised.OutlineColourHex"/>), closes gaps up to
/// <see cref="ThresholdKeys.OutlineGapClosureRadius"/> morphologically, and <b>measures</b> the
/// resulting width as a <see cref="StepMeasurement"/>.
/// </para>
/// <para>
/// 🔒 This step repairs <em>continuity</em>. <em>Width conformance</em> against `15` §A3's 3-4 px
/// band is QA item 3's job, and the two are kept apart on purpose: a step that both changed the
/// width and judged it would be marking its own homework.
/// </para>
/// <para>
/// 🔒 <b>"Continuous" is an enclosure claim, so the repair is one too.</b> The closing proposes
/// pixels; only the connected groups that restore an enclosure the break had opened are painted. A
/// group whose removal changes nothing was never a break — it is a concavity the disc rounded off,
/// or a shape smaller than the disc that the closing filled in — and painting it would thicken the
/// art rather than repair it. On an outline that is already continuous the step is a no-op, which is
/// what keeps a closure radius large relative to the subject from quietly redrawing it.
/// </para>
/// <para>
/// 🔒 <b>The bridge is then widened to the band's own width</b>, because §B4 asks for continuous
/// <em>and uniform width</em> and a closing leaves the bridge thinner than the band it joins: the
/// disc reaches a break in a thin band obliquely, and shaves the outer layers of the bridge off. The
/// widening is measured from the outline's own mean width and is confined to the neighbourhood of
/// the bridge, so it restores what the break took and touches nothing else.
/// </para>
/// </remarks>
public sealed class OutlineRepairStep : IAssetStep
{
    /// <inheritdoc/>
    public int Number => 4;

    /// <inheritdoc/>
    public string Id => "outline-repair";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 4";

    /// <summary>The measurement key this step records the measured outline width under.</summary>
    public const string OutlineWidthMeasurement = "outlineWidthPx";

    /// <summary>The measurement key for how many pixels closing a break added.</summary>
    public const string RepairedPixelMeasurement = "outlinePixelsRepaired";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tolerance = input.Thresholds.RequireNumber(ThresholdKeys.OutlineColourTolerance);
        var closureRadius = input.Thresholds.RequireNumber(ThresholdKeys.OutlineGapClosureRadius);

        var image = Raster.From(input.Image);
        var outline = OutlineMask(image, tolerance);

        // The width the repair restores its bridge to is the band's own, taken before the repair:
        // measuring it afterwards would measure the bridge the repair had just drawn.
        var width = Math.Round(MeanWidth(outline), MidpointRounding.AwayFromZero);
        var repaired = CloseBreaks(outline, closureRadius, width);

        foreach (var (x, y) in repaired.Pixels())
        {
            image.SetColour(x, y, Doc15Authorised.OutlineColour);
            outline[x, y] = true;
        }

        return new AssetStepResult(
            Number,
            Id,
            StepOutcome.Applied,
            image.ToBitmap(),
            string.Empty,
            [
                new StepMeasurement(OutlineWidthMeasurement, MeanWidth(outline), "px", "15 §A3"),
                new StepMeasurement(
                    RepairedPixelMeasurement, repaired.Count, "count", DocReference),
            ],
            []);
    }

    /// <summary>Every visible pixel within the tolerance of `15` §A3's outline colour.</summary>
    /// <param name="image">The image to read.</param>
    /// <param name="tolerance">How far from #231A2E still reads as outline.</param>
    private static PixelMask OutlineMask(Raster image, double tolerance)
    {
        var mask = new PixelMask(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                mask[x, y] = image.AlphaAt(x, y) > 0
                             && Raster.RgbDistance(image.ColourAt(x, y), Doc15Authorised.OutlineColour)
                             <= tolerance;
            }
        }

        return mask;
    }

    /// <summary>
    /// The pixels that have to be painted for the outline to enclose again, at the band's own width.
    /// </summary>
    /// <param name="outline">The outline mask.</param>
    /// <param name="radius">The largest gap treated as a break rather than a design feature.</param>
    /// <param name="width">The band's measured width, in whole pixels.</param>
    private static PixelMask CloseBreaks(PixelMask outline, double radius, double width)
    {
        var bridge = LoadBearing(outline, outline.Close(radius).Except(outline));
        return bridge.Count == 0 ? bridge : Widen(outline, bridge, width);
    }

    /// <summary>
    /// The connected groups of a proposed repair without which the outline stops enclosing.
    /// </summary>
    /// <param name="outline">The outline mask.</param>
    /// <param name="proposed">What the closing would add.</param>
    private static PixelMask LoadBearing(PixelMask outline, PixelMask proposed)
    {
        var bridge = new PixelMask(outline.Width, outline.Height);
        var sealedOff = Enclosed(outline, proposed);
        if (sealedOff.Count == 0)
        {
            // Even with everything painted the outline encloses nothing, so nothing on offer is
            // repairing a break.
            return bridge;
        }

        foreach (var group in Groups(proposed))
        {
            // A group can only be holding a break shut if it touches what is shut away: the path
            // that re-opens has to run from the edge of the image, through the group, into the
            // sealed region. Cheap, and it spares the flood fill below for the many small groups a
            // closing produces around every concavity in a real asset.
            if (!Touches(group, sealedOff))
            {
                continue;
            }

            if (!sealedOff.Except(Enclosed(outline, proposed.Except(group))).Pixels().Any())
            {
                // Everything stays shut away without this group, so it was never holding a break
                // shut: it is a rounded concavity, or a shape the disc simply filled in.
                continue;
            }

            foreach (var (x, y) in group.Pixels())
            {
                bridge[x, y] = true;
            }
        }

        return bridge;
    }

    /// <summary>
    /// Grows a bridge out to the band's own width, so the repair does not leave the outline thin
    /// where it crossed the break.
    /// </summary>
    /// <remarks>
    /// The band is "everything within the measured width of what the sealed outline encloses", which
    /// is what a uniform-width outline around a subject <em>is</em>. Confined to the bridge's own
    /// neighbourhood: away from the break the outline is whatever the artist drew, and §A3 width
    /// conformance is `15` Part F item 3's judgement to make, not this step's.
    /// </remarks>
    /// <param name="outline">The outline mask.</param>
    /// <param name="bridge">The load-bearing repair.</param>
    /// <param name="width">The band's measured width, in whole pixels.</param>
    private static PixelMask Widen(PixelMask outline, PixelMask bridge, double width)
    {
        var free = outline.Complement();
        var outside = free.Except(bridge).FloodFromBorder();

        // Only the bridge's outward face is band; whatever of it lies behind that face is subject
        // the closing happened to swallow, and measuring the band from there would leave the
        // repair a layer short of the width it is restoring.
        var face = new PixelMask(outline.Width, outline.Height);
        foreach (var (x, y) in bridge.Pixels())
        {
            face[x, y] = outside[x - 1, y] || outside[x + 1, y] || outside[x, y - 1] || outside[x, y + 1];
        }

        var subject = free.Except(outside).Except(face);
        var toSubject = subject.DistanceToSet();
        var nearBridge = bridge.Dilate(width);
        var widened = new PixelMask(outline.Width, outline.Height);

        for (var y = 0; y < outline.Height; y++)
        {
            for (var x = 0; x < outline.Width; x++)
            {
                var distance = toSubject[(y * outline.Width) + x];
                widened[x, y] = bridge[x, y]
                                || (nearBridge[x, y]
                                    && !outline[x, y]
                                    && !subject[x, y]
                                    && distance <= width);
            }
        }

        return widened;
    }

    /// <summary>True when any pixel of one mask is 4-adjacent to the other.</summary>
    /// <param name="group">The mask to test.</param>
    /// <param name="other">The mask to test against.</param>
    private static bool Touches(PixelMask group, PixelMask other) =>
        group.Pixels().Any(pixel =>
            other[pixel.X - 1, pixel.Y]
            || other[pixel.X + 1, pixel.Y]
            || other[pixel.X, pixel.Y - 1]
            || other[pixel.X, pixel.Y + 1]);

    /// <summary>What the outline plus a set of repairs shuts away from the edge of the image.</summary>
    /// <param name="outline">The outline mask.</param>
    /// <param name="repairs">The pixels treated as painted.</param>
    private static PixelMask Enclosed(PixelMask outline, PixelMask repairs)
    {
        var free = outline.Complement().Except(repairs);
        return free.Except(free.FloodFromBorder());
    }

    /// <summary>The mask's 8-connected components, in row-major discovery order.</summary>
    /// <param name="mask">The mask to group.</param>
    private static IReadOnlyList<PixelMask> Groups(PixelMask mask)
    {
        var assigned = new PixelMask(mask.Width, mask.Height);
        var groups = new List<PixelMask>();

        foreach (var (startX, startY) in mask.Pixels())
        {
            if (assigned[startX, startY])
            {
                continue;
            }

            var group = new PixelMask(mask.Width, mask.Height);
            var queue = new Queue<(int X, int Y)>();
            assigned[startX, startY] = true;
            group[startX, startY] = true;
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
                        if (!mask[nextX, nextY] || assigned[nextX, nextY])
                        {
                            continue;
                        }

                        assigned[nextX, nextY] = true;
                        group[nextX, nextY] = true;
                        queue.Enqueue((nextX, nextY));
                    }
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    /// <summary>
    /// The mean width of a band, from the Euclidean distance of each of its pixels to the nearest
    /// pixel outside it.
    /// </summary>
    /// <remarks>
    /// For a band of width <c>w</c> the distance to the nearer edge averages <c>w/4</c>, so the
    /// width is four times the mean. Pixel centres sit half a pixel inside the edge they are
    /// measured from, which the half-pixel term removes.
    /// </remarks>
    /// <param name="band">The mask to measure. An empty one measures zero.</param>
    private static double MeanWidth(PixelMask band)
    {
        var distance = band.Complement().DistanceToSet();
        var total = 0d;
        var counted = 0;

        foreach (var (x, y) in band.Pixels())
        {
            var depth = distance[(y * band.Width) + x];
            if (double.IsPositiveInfinity(depth))
            {
                // Every pixel is band: there is no outside to measure a width against.
                continue;
            }

            total += depth - 0.5d;
            counted++;
        }

        return counted == 0 ? 0d : 4d * (total / counted);
    }
}
