using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>Background removal: true alpha, no halo (matte decontamination on).</summary>
/// <remarks>
/// <para>
/// A border-seeded flood fill against the sampled border colour, within
/// <see cref="ThresholdKeys.BackgroundKeyTolerance"/>, then matte decontamination — un-mixing the
/// background colour back out of the partially transparent edge pixels — at
/// <see cref="ThresholdKeys.MatteDecontaminationStrength"/>.
/// </para>
/// <para>
/// Both thresholds are uncalibrated, so asking for either without a stated value throws
/// <see cref="UncalibratedThresholdException"/> rather than picking something that looks right.
/// </para>
/// <para>
/// Decontamination is spatial, not algebraic: the textbook un-mix <c>F = (C - (1-α)·B) / α</c>
/// assumes the contaminant <em>is</em> the key colour B, and a real generated halo is not — it is
/// the light matte the subject was composited over, which survives the key precisely because it is
/// nowhere near B. So the fringe is reconstructed from the clean foreground beneath it instead, at
/// the stated strength, which also removes a key-coloured fringe since that is contamination too.
/// </para>
/// <para>
/// The fringe is two layers, one per side of the boundary: a composited edge contaminates the
/// background side and the subject side alike, so the band is the outermost retained layer and the
/// layer beneath it. That is a structural choice about where a boundary is, not a calibrated width.
/// </para>
/// </remarks>
public sealed class BackgroundRemovalStep : IAssetStep
{
    /// <summary>The measurement key for how many pixels the key turned transparent.</summary>
    public const string KeyedPixelMeasurement = "backgroundPixelsKeyed";

    /// <summary>The measurement key for how many fringe pixels were decontaminated.</summary>
    public const string DecontaminatedPixelMeasurement = "fringePixelsDecontaminated";

    /// <inheritdoc/>
    public int Number => 1;

    /// <inheritdoc/>
    public string Id => "background-removal";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 1";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tolerance = input.Thresholds.RequireNumber(ThresholdKeys.BackgroundKeyTolerance);
        var strength = input.Thresholds.RequireNumber(ThresholdKeys.MatteDecontaminationStrength);

        var image = Raster.From(input.Image);
        var background = KeyOut(image, tolerance);
        var keyed = background.Count;

        var decontaminated = Decontaminate(image, background, strength);

        return new AssetStepResult(
            Number,
            Id,
            StepOutcome.Applied,
            image.ToBitmap(),
            string.Empty,
            [
                new StepMeasurement(KeyedPixelMeasurement, keyed, "count", DocReference),
                new StepMeasurement(
                    DecontaminatedPixelMeasurement, decontaminated, "count", DocReference),
            ],
            []);
    }

    /// <summary>
    /// Flood-fills the background from the border and writes it fully transparent, in place.
    /// </summary>
    /// <param name="image">The image to key. Mutated.</param>
    /// <param name="tolerance">How close to the sampled border colour counts as background.</param>
    /// <returns>The mask of everything that became transparent.</returns>
    private static PixelMask KeyOut(Raster image, double tolerance)
    {
        var key = SampleBorderColour(image);
        var candidates = new PixelMask(image.Width, image.Height);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                candidates[x, y] = image.AlphaAt(x, y) == 0
                                   || (key is not null
                                       && Raster.RgbDistance(image.ColourAt(x, y), key.Value) <= tolerance);
            }
        }

        var background = candidates.FloodFromBorder();
        foreach (var (x, y) in background.Pixels())
        {
            image.SetColour(x, y, SKColors.Transparent);
        }

        return background;
    }

    /// <summary>
    /// The most common opaque colour on the image's border, or null when the border is already
    /// fully transparent and there is nothing to key against.
    /// </summary>
    /// <remarks>
    /// Ties break toward the smallest packed value so the same image always samples the same
    /// colour; a hash-order tie-break would make the whole step non-deterministic.
    /// </remarks>
    /// <param name="image">The image to sample.</param>
    private static SKColor? SampleBorderColour(Raster image)
    {
        var counts = new Dictionary<uint, int>();

        void Sample(int x, int y)
        {
            var colour = image.ColourAt(x, y);
            if (colour.Alpha == 0)
            {
                return;
            }

            var packed = (uint)colour;
            counts[packed] = counts.TryGetValue(packed, out var seen) ? seen + 1 : 1;
        }

        for (var x = 0; x < image.Width; x++)
        {
            Sample(x, 0);
            Sample(x, image.Height - 1);
        }

        for (var y = 0; y < image.Height; y++)
        {
            Sample(0, y);
            Sample(image.Width - 1, y);
        }

        if (counts.Count == 0)
        {
            return null;
        }

        var best = counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .First();

        return (SKColor)best.Key;
    }

    /// <summary>
    /// Pulls the two fringe layers back toward the clean foreground beneath them, in place.
    /// </summary>
    /// <param name="image">The keyed image. Mutated.</param>
    /// <param name="background">What the key turned transparent.</param>
    /// <param name="strength">How far to pull, 0 (not at all) to 1 (all the way).</param>
    /// <returns>How many pixels were changed.</returns>
    private static int Decontaminate(Raster image, PixelMask background, double strength)
    {
        var retained = background.Complement();
        var outerFringe = LayerTouching(retained, background);
        var innerFringe = LayerTouching(retained.Except(outerFringe), outerFringe);
        var clean = retained.Except(outerFringe).Except(innerFringe);

        // Inner first, then outer against the inner layer's repaired colours: the outermost layer
        // is the most contaminated, so referencing it before it is itself clean would carry the
        // halo one pixel inward instead of removing it.
        return PullToward(image, innerFringe, clean, strength)
               + PullToward(image, outerFringe, innerFringe, strength);
    }

    /// <summary>The pixels of one mask that are 4-adjacent to another.</summary>
    /// <param name="candidates">Where the layer may be drawn from.</param>
    /// <param name="neighbour">What the layer must touch.</param>
    private static PixelMask LayerTouching(PixelMask candidates, PixelMask neighbour)
    {
        var layer = new PixelMask(candidates.Width, candidates.Height);
        foreach (var (x, y) in candidates.Pixels())
        {
            layer[x, y] = neighbour[x - 1, y]
                          || neighbour[x + 1, y]
                          || neighbour[x, y - 1]
                          || neighbour[x, y + 1];
        }

        return layer;
    }

    /// <summary>
    /// Blends every pixel of a layer toward the mean colour of its 8-neighbours in a reference set.
    /// </summary>
    /// <param name="image">The image to correct. Mutated.</param>
    /// <param name="layer">The contaminated pixels.</param>
    /// <param name="reference">The pixels to reconstruct them from.</param>
    /// <param name="strength">How far to pull, 0 (not at all) to 1 (all the way).</param>
    /// <returns>How many pixels were changed.</returns>
    private static int PullToward(Raster image, PixelMask layer, PixelMask reference, double strength)
    {
        var corrected = 0;

        foreach (var (x, y) in layer.Pixels())
        {
            var red = 0d;
            var green = 0d;
            var blue = 0d;
            var seen = 0;

            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (!reference[x + offsetX, y + offsetY])
                    {
                        continue;
                    }

                    var neighbour = image.ColourAt(x + offsetX, y + offsetY);
                    red += neighbour.Red;
                    green += neighbour.Green;
                    blue += neighbour.Blue;
                    seen++;
                }
            }

            if (seen == 0)
            {
                // Nothing clean to reconstruct from. Inventing a colour here would be worse than
                // leaving the pixel as the generator produced it.
                continue;
            }

            var colour = image.ColourAt(x, y);
            image.SetRgb(
                x,
                y,
                Raster.ToChannel(colour.Red + (((red / seen) - colour.Red) * strength)),
                Raster.ToChannel(colour.Green + (((green / seen) - colour.Green) * strength)),
                Raster.ToChannel(colour.Blue + (((blue / seen) - colour.Blue) * strength)));
            corrected++;
        }

        return corrected;
    }
}
