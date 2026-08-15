using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// The four colours one placeholder is drawn from.
/// </summary>
/// <param name="Fill">The body of the card.</param>
/// <param name="Cross">The diagonal cross that says "this is not art".</param>
/// <param name="Stamp">The id stamp.</param>
/// <param name="Outline">The outline colour. The same for every asset, biome or not.</param>
public sealed record PlaceholderColours(SKColor Fill, SKColor Cross, SKColor Stamp, SKColor Outline);

/// <summary>
/// Chooses a placeholder's colours: the row's biome palette where it has one, and a flat neutral
/// where it does not.
/// </summary>
/// <remarks>
/// <para>
/// Biome rows use the register's own palette so the atlases are visually coherent — a biome atlas
/// full of identical grey boxes would leave the palette quantiser step untested on every biome row.
/// </para>
/// <para>
/// The choices are constrained by two measurable distances, not by taste: the pipeline's outline
/// detection reads any pixel within tolerance of the outline colour as outline, so the fill, cross
/// and stamp must all sit far enough from it that the whole card doesn't join the outline mask. See
/// <see cref="PlaceholderThresholds.OutlineColourTolerance"/> for the arithmetic that fixes the
/// tolerance between those two bands.
/// </para>
/// <para>
/// The <c>shadow</c> and <c>sky</c> biome hues are deliberately not used, since some sit close
/// enough to the outline colour that a card drawn in them would read as outline.
/// </para>
/// </remarks>
public static class PlaceholderPalette
{
    /// <summary>The card body for a row that names no biome.</summary>
    public const string NeutralFillHex = "#8A8A96";

    /// <summary>The cross for a row that names no biome.</summary>
    public const string NeutralCrossHex = "#6E6E7A";

    /// <summary>The id stamp for a row that names no biome.</summary>
    public const string NeutralStampHex = "#F2F2F6";

    /// <summary>The colours for one row.</summary>
    /// <param name="palette">The row's palette, or null where the row names no biome.</param>
    public static PlaceholderColours For(Palette? palette)
    {
        var outline = Parse(AssetPipeline.Doc15Authorised.OutlineColourHex);

        return palette is null
            ? new PlaceholderColours(
                Parse(NeutralFillHex), Parse(NeutralCrossHex), Parse(NeutralStampHex), outline)
            : new PlaceholderColours(
                Parse(palette.Base), Parse(palette.Accent), Parse(palette.Glow), outline);
    }

    /// <summary>Euclidean distance between two colours in RGB, ignoring alpha.</summary>
    /// <remarks>
    /// The same metric the pipeline's own outline step uses (that code is internal and cannot be
    /// called from here), so the numbers this generator reasons about match what the pipeline
    /// measures. Public because the test suite needs it to re-derive the tolerance arithmetic.
    /// </remarks>
    /// <param name="first">One colour.</param>
    /// <param name="second">The other.</param>
    public static double RgbDistance(SKColor first, SKColor second)
    {
        double red = first.Red - second.Red;
        double green = first.Green - second.Green;
        double blue = first.Blue - second.Blue;
        return Math.Sqrt((red * red) + (green * green) + (blue * blue));
    }

    /// <summary>Blends two colours, keeping the first's alpha.</summary>
    /// <param name="from">The colour blended away from.</param>
    /// <param name="to">The colour blended toward.</param>
    /// <param name="amount">How far to blend, 0 (not at all) to 1 (all the way).</param>
    public static SKColor Blend(SKColor from, SKColor to, double amount) => new(
        Channel(from.Red + ((to.Red - from.Red) * amount)),
        Channel(from.Green + ((to.Green - from.Green) * amount)),
        Channel(from.Blue + ((to.Blue - from.Blue) * amount)),
        from.Alpha);

    private static byte Channel(double value) =>
        (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0d, 255d);

    private static SKColor Parse(string hex) => SKColor.TryParse(hex, out var colour)
        ? colour
        : throw new InvalidOperationException(
            $"'{hex}' is not a hex colour. `15` §A5 spells every palette hue as one, and " +
            "this generator's neutrals are spelled the same way.");
}
