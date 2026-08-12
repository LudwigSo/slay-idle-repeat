using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// The numbers `15` actually authorises, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Steering rule S6: <em>"If the design docs do not authorise a number, leave it absent and
/// greppable (null), and say so."</em> This type is the other half of that rule — the small,
/// closed set of values a doc DOES state, each carrying the section it came from, so that a reader
/// can tell an authorised constant from an invented one at a glance. Everything else the seven
/// steps need is either read per row from the manifest (delivery size, pivot, atlas, biome
/// palette) or is a hole in <see cref="ThresholdSet"/> whose value is <c>null</c>.
/// </para>
/// <para>
/// Do not add a number here without quoting the sentence of `15` that states it.
/// </para>
/// </remarks>
public static class Doc15Authorised
{
    /// <summary>
    /// `15` §A3: the outline colour, "never pure black".
    /// </summary>
    public const string OutlineColourHex = "#231A2E";

    /// <summary>`15` §A3's outline colour as a Skia colour.</summary>
    public static SKColor OutlineColour { get; } = new(0x23, 0x1A, 0x2E);

    /// <summary>
    /// `15` §A3: outline weight is "3-4 px at 512 px canvas, scaled proportionally" — this is the
    /// lower bound of the band, at the reference canvas.
    /// </summary>
    public const double OutlineWidthMinAtReferenceCanvas = 3d;

    /// <summary>`15` §A3: the upper bound of the outline-weight band, at the reference canvas.</summary>
    public const double OutlineWidthMaxAtReferenceCanvas = 4d;

    /// <summary>`15` §A3: the canvas width the 3-4 px band is stated against.</summary>
    public const int OutlineWidthReferenceCanvas = 512;

    /// <summary>`15` §C: "max single texture 2048x2048" — the width half.</summary>
    public const int MaxSingleTextureWidth = 2048;

    /// <summary>`15` §C: "max single texture 2048x2048" — the height half.</summary>
    public const int MaxSingleTextureHeight = 2048;

    /// <summary>
    /// `15` §B4 step 5: "Resize -> to the spec size in the manifest (Lanczos, then sharpen 0.4)".
    /// The amount is authorised; the radius/sigma is not, and lives as an uncalibrated threshold.
    /// </summary>
    public const double SharpenAmount = 0.4d;

    /// <summary>
    /// `15` §A4: "fill it 100% black, <b>scale to 64 px</b>". The size of the silhouette mask —
    /// stated by the doc, so it is a constant here and not a threshold.
    /// </summary>
    public const int SilhouetteMaskSize = 64;

    /// <summary>
    /// The authorised outline-width band at an arbitrary canvas width, scaled proportionally from
    /// the §A3 statement at <see cref="OutlineWidthReferenceCanvas"/>.
    /// </summary>
    /// <param name="canvasWidth">The canvas width in pixels.</param>
    /// <returns>The inclusive minimum and maximum outline width in pixels.</returns>
    public static (double Min, double Max) OutlineWidthBandFor(int canvasWidth)
    {
        if (canvasWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(canvasWidth),
                canvasWidth,
                "`15` §A3 scales its 3-4 px band proportionally with the canvas, and a canvas of " +
                "no width scales it to nothing.");
        }

        var scale = canvasWidth / (double)OutlineWidthReferenceCanvas;
        return (OutlineWidthMinAtReferenceCanvas * scale, OutlineWidthMaxAtReferenceCanvas * scale);
    }
}

/// <summary>
/// The two pivot values `15` §C authorises: "Characters: bottom-center. Icons: center."
/// </summary>
/// <remarks>
/// 🔒 §C names these two and nothing else, and 284 of the 974 manifest rows carry no pivot at all.
/// <see cref="AssetSpec.Resolve"/> refuses both an absent pivot and an unrecognised one rather
/// than inventing a third convention.
/// </remarks>
public static class Doc15Pivots
{
    /// <summary>`15` §C: "Icons: center."</summary>
    public const string Center = "center";

    /// <summary>`15` §C: "Characters: bottom-center."</summary>
    public const string BottomCenter = "bottom-center";

    /// <summary>Both authorised pivots, for a caller that wants to validate against the closed set.</summary>
    public static IReadOnlyList<string> All { get; } = [Center, BottomCenter];
}
