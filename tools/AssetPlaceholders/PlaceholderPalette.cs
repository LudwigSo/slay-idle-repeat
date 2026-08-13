using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// The four colours one placeholder is drawn from.
/// </summary>
/// <param name="Fill">The body of the card.</param>
/// <param name="Cross">The diagonal cross that says "this is not art".</param>
/// <param name="Stamp">The id stamp.</param>
/// <param name="Outline">`15` §A3's outline colour. The same for every asset, biome or not.</param>
public sealed record PlaceholderColours(SKColor Fill, SKColor Cross, SKColor Stamp, SKColor Outline);

/// <summary>
/// Chooses a placeholder's colours: the row's `15` §A5 biome palette where it has one, and a flat
/// neutral where it does not.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Biome rows use the register's own palette so the atlases are visually coherent</b> — a
/// biome atlas full of identical grey boxes tells a reader nothing about whether the eight biome
/// batches were assembled correctly, and `15` §B4 step 3 is a no-op on a colour it does not
/// recognise, so a grey placeholder would leave the quantiser untested on every one of the 192
/// biome-scoped rows.
/// </para>
/// <para>
/// 🔒 <b>The choices are constrained by two measurable distances, not by taste.</b> `15` §B4 step 4
/// reads every visible pixel within <see cref="AssetPipeline.ThresholdKeys.OutlineColourTolerance"/>
/// of <c>#231A2E</c> as outline. So the fill, the cross and the stamp must all sit <em>far</em> from
/// the outline colour, or the whole card joins the outline mask and step 4 spends its run closing
/// concavities in a shape that is not an outline. See
/// <see cref="PlaceholderThresholds.OutlineColourTolerance"/> for the arithmetic that fixes the
/// tolerance between those two bands.
/// </para>
/// <para>
/// 🔒 §A5's <c>shadow</c> and <c>sky</c> hues are deliberately <b>not</b> used. Three of the eight
/// are within 40 channel units of <c>#231A2E</c> (Sunken Crypt's shadow <c>#233A45</c>, Astral
/// Spire's sky <c>#141028</c>), so a card drawn in them would read as outline. <c>accent</c> and
/// <c>glow</c> are light in all eight biomes by §A5's own construction.
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
    /// <param name="palette">The row's `15` §A5 palette, or null where the row names no biome.</param>
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
    /// 🔒 The same metric <c>Raster.RgbDistance</c> uses, because the numbers this generator reasons
    /// about are the numbers the pipeline's steps will measure. <c>Raster</c> is internal to
    /// <c>SlayIdleRepeat.AssetPipeline</c>, so it cannot be called from here; the formula is three
    /// subtractions and a square root, and the suite pins that the two agree by driving a real
    /// placeholder through the real steps rather than by trusting this comment.
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
