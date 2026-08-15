using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>The small, closed set of numeric constants the pipeline treats as authorised.</summary>
/// <remarks>
/// Everything else the steps need is either read per row from the manifest (delivery size, pivot,
/// atlas, biome palette) or is a hole in <see cref="ThresholdSet"/> whose value is <c>null</c>.
/// </remarks>
public static class Doc15Authorised
{
    /// <summary>The outline colour (never pure black).</summary>
    public const string OutlineColourHex = "#231A2E";

    public static SKColor OutlineColour { get; } = new(0x23, 0x1A, 0x2E);

    /// <summary>Lower bound of the outline-weight band, at the reference canvas.</summary>
    public const double OutlineWidthMinAtReferenceCanvas = 3d;

    /// <summary>Upper bound of the outline-weight band, at the reference canvas.</summary>
    public const double OutlineWidthMaxAtReferenceCanvas = 4d;

    /// <summary>The canvas width the outline-weight band is stated against.</summary>
    public const int OutlineWidthReferenceCanvas = 512;

    /// <summary>Baseline generation long edge.</summary>
    public const int GenerationLongEdge = 1024;

    /// <summary>The upscaled generation long edge used for bosses and backgrounds.</summary>
    /// <remarks>
    /// Same number as <see cref="MaxSingleTextureWidth"/> but a different meaning — a delivered
    /// texture cap vs. a generation size — kept as separate constants so one can change without
    /// silently moving the other.
    /// </remarks>
    public const int GenerationUpscaledLongEdge = 2048;

    /// <summary>Max single texture width.</summary>
    public const int MaxSingleTextureWidth = 2048;

    /// <summary>Max single texture height.</summary>
    public const int MaxSingleTextureHeight = 2048;

    /// <summary>
    /// The resize step's sharpen amount. The amount is authorised; the radius/sigma is not, and
    /// lives as an uncalibrated threshold instead.
    /// </summary>
    public const double SharpenAmount = 0.4d;

    /// <summary>The silhouette mask size in pixels.</summary>
    public const int SilhouetteMaskSize = 64;

    /// <summary>The size a detail must remain readable at.</summary>
    /// <remarks>
    /// Same number as <see cref="SilhouetteMaskSize"/> but a different meaning — kept as a separate
    /// constant so one can change without silently moving the other.
    /// </remarks>
    public const int DetailBudgetSize = 64;

    /// <summary>
    /// The authorised outline-width band at an arbitrary canvas width, scaled proportionally from
    /// <see cref="OutlineWidthReferenceCanvas"/>.
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

/// <summary>The two pivot values in use: characters use bottom-center, icons use center.</summary>
/// <remarks>
/// <see cref="AssetSpec.Resolve"/> refuses both an absent pivot and an unrecognised one rather
/// than inventing a third convention.
/// </remarks>
public static class Doc15Pivots
{
    public const string Center = "center";

    public const string BottomCenter = "bottom-center";

    /// <summary>Both authorised pivots, for a caller that wants to validate against the closed set.</summary>
    public static IReadOnlyList<string> All { get; } = [Center, BottomCenter];
}
