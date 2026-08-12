using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 3: <em>"Palette quantise -&gt; to the biome palette + neutrals (biome assets
/// only)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Biome assets only.</b> A row with <see cref="AssetSpec.IsBiomeScoped"/> false comes back
/// <see cref="StepOutcome.SkippedNotApplicable"/> with the reason stated, and its bitmap must come
/// out <b>pixel-identical</b> to the one that went in. Getting that wrong silently recolours the
/// whole UI kit and the currency icons, none of which name a biome.
/// </para>
/// <para>
/// Where it does apply: a nearest-colour snap to the six `15` §A5 hues plus the §A3 outline colour
/// plus <see cref="ThresholdKeys.PaletteNeutrals"/>, within
/// <see cref="ThresholdKeys.PaletteMatchTolerance"/>. Both of those are uncalibrated — §A5 says
/// "+ neutrals" and never enumerates them — so both throw when unstated.
/// </para>
/// <para>
/// 🔒 A pixel further from every authorised colour than the tolerance is left exactly as it is. The
/// tolerance is what separates "antialiasing of an on-palette colour" from "a colour that is not on
/// the palette at all", and snapping the second kind anyway would quietly repaint art the palette
/// does not cover — `15` Part F item 5 is where an off-palette pixel gets reported, not here.
/// </para>
/// </remarks>
public sealed class PaletteQuantiseStep : IAssetStep
{
    /// <summary>The measurement key for how many pixels were snapped to an authorised colour.</summary>
    public const string SnappedPixelMeasurement = "pixelsSnapped";

    /// <summary>The measurement key for how many pixels no authorised colour was near enough to.</summary>
    public const string OffPalettePixelMeasurement = "pixelsLeftOffPalette";

    /// <inheritdoc/>
    public int Number => 3;

    /// <inheritdoc/>
    public string Id => "palette-quantise";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 3";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var spec = input.Spec;
        if (!spec.IsBiomeScoped)
        {
            return new AssetStepResult(
                Number,
                Id,
                StepOutcome.SkippedNotApplicable,
                input.Image,
                $"`15` §B4 step 3 is biome assets only, and '{spec.Id}' ({spec.Section}) names no " +
                "biome and carries no `15` §A5 palette. Quantising it would recolour it to a " +
                "palette the doc never gave it.",
                [],
                []);
        }

        var tolerance = input.Thresholds.RequireNumber(ThresholdKeys.PaletteMatchTolerance);
        var authorised = AuthorisedColours(spec, input.Thresholds);

        var image = Raster.From(input.Image);
        var snapped = 0;
        var offPalette = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var colour = image.ColourAt(x, y);
                if (colour.Alpha == 0)
                {
                    continue;
                }

                var nearest = Nearest(authorised, colour);
                if (nearest.Distance > tolerance)
                {
                    offPalette++;
                    continue;
                }

                image.SetRgb(x, y, nearest.Colour.Red, nearest.Colour.Green, nearest.Colour.Blue);
                snapped++;
            }
        }

        return new AssetStepResult(
            Number,
            Id,
            StepOutcome.Applied,
            image.ToBitmap(),
            string.Empty,
            [
                new StepMeasurement(SnappedPixelMeasurement, snapped, "count", DocReference),
                new StepMeasurement(OffPalettePixelMeasurement, offPalette, "count", DocReference),
            ],
            []);
    }

    /// <summary>
    /// The biome's six `15` §A5 hues, the §A3 outline colour, and the unenumerated neutrals the
    /// caller has stated.
    /// </summary>
    /// <param name="spec">The biome-scoped spec.</param>
    /// <param name="thresholds">The threshold set. The neutrals are uncalibrated and throw.</param>
    private static IReadOnlyList<SKColor> AuthorisedColours(AssetSpec spec, ThresholdSet thresholds)
    {
        var palette = spec.PaletteColours
            ?? throw new InvalidOperationException(
                $"'{spec.Id}' reports as biome-scoped and carries no `15` §A5 palette.");

        var neutrals = thresholds.RequireColours(ThresholdKeys.PaletteNeutrals);

        return
        [
            .. palette.Hues.Select(Parse),
            Doc15Authorised.OutlineColour,
            .. neutrals.Select(Parse),
        ];
    }

    /// <summary>Parses a hex colour, or fails loudly naming the text that is not one.</summary>
    /// <param name="hex">A hex colour, as the manifest and the threshold file spell them.</param>
    private static SKColor Parse(string hex) => SKColor.TryParse(hex, out var colour)
        ? colour
        : throw new InvalidOperationException(
            $"'{hex}' is not a colour `15` §A5's palette or the stated neutrals can be read from.");

    /// <summary>The nearest authorised colour in RGB, and how far away it is.</summary>
    /// <param name="authorised">The closed set of colours §B4 step 3 may produce.</param>
    /// <param name="colour">The pixel's colour.</param>
    private static (SKColor Colour, double Distance) Nearest(
        IReadOnlyList<SKColor> authorised, SKColor colour)
    {
        var best = authorised[0];
        var bestDistance = Raster.RgbDistance(colour, best);

        for (var index = 1; index < authorised.Count; index++)
        {
            var distance = Raster.RgbDistance(colour, authorised[index]);
            if (distance < bestDistance)
            {
                best = authorised[index];
                bestDistance = distance;
            }
        }

        return (best, bestDistance);
    }
}
