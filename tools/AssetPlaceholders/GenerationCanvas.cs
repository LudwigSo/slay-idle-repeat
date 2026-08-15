using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// The working canvas a placeholder is drawn on, before it is resampled down to the register's
/// delivery size.
/// </summary>
/// <remarks>
/// <para>
/// The canvas takes the delivery aspect ratio, not a fixed square: deliveries are non-square
/// (mounts 512×384, backdrops 1080×1440), and no uniform resample reaches them from a square canvas.
/// </para>
/// <para>
/// The canvas is strictly larger than the delivery size, on purpose, so the downstream resample is
/// a real reduction rather than an identity resize.
/// </para>
/// <para>
/// The aspect is exact, not approximate: the delivery size is reduced by its greatest common
/// divisor and multiplied by a whole number, so
/// <c>canvas.Width × delivery.Height == canvas.Height × delivery.Width</c> holds in integer
/// arithmetic — the comparison <see cref="AssetPipeline.ResizeStep"/> makes before deciding whether
/// to emit <c>CON_DELIVERY_ASPECT</c>.
/// </para>
/// </remarks>
public static class GenerationCanvas
{
    /// <summary>The baseline generation long edge.</summary>
    /// <remarks>An alias of <see cref="Doc15Authorised.GenerationLongEdge"/>, not a second copy of the number.</remarks>
    public const int BaselineLongEdge = Doc15Authorised.GenerationLongEdge;

    /// <summary>The upscaled generation long edge, used for bosses and backgrounds.</summary>
    /// <remarks>An alias of <see cref="Doc15Authorised.GenerationUpscaledLongEdge"/>.</remarks>
    public const int UpscaledLongEdge = Doc15Authorised.GenerationUpscaledLongEdge;

    /// <summary>
    /// The delivery long edge at or above which the upscaled canvas is used.
    /// </summary>
    /// <remarks>
    /// Keyed on the delivery size rather than a hardcoded list of categories, so the rule stays true
    /// if a new category ever delivers that large.
    /// </remarks>
    public const int UpscaleAtDeliveryLongEdge = BaselineLongEdge;

    /// <summary>The working canvas for one delivery size.</summary>
    /// <param name="delivery">The delivery size the register carries for the row.</param>
    /// <returns>A canvas of the same exact aspect, strictly larger in both axes, both edges even.</returns>
    public static PixelSize For(PixelSize delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (delivery.Width <= 0 || delivery.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delivery),
                delivery,
                "A delivery size of zero or less has no aspect to generate at. `15` §C states a " +
                "positive size for every row it covers.");
        }

        var divisor = GreatestCommonDivisor(delivery.Width, delivery.Height);
        var aspectWidth = delivery.Width / divisor;
        var aspectHeight = delivery.Height / divisor;

        var longEdge = Math.Max(delivery.Width, delivery.Height) >= UpscaleAtDeliveryLongEdge
            ? UpscaledLongEdge
            : BaselineLongEdge;

        var multiple = Math.Max(1, longEdge / Math.Max(aspectWidth, aspectHeight));

        // Compared in `long`: at a large coprime aspect, `aspectWidth * multiple` in `int` wraps
        // negative on the second iteration and the loop spins through hundreds of millions of
        // iterations as an apparent hang with no message.
        while ((long)aspectWidth * multiple <= delivery.Width
               || (long)aspectHeight * multiple <= delivery.Height)
        {
            multiple++;
        }

        // Even in both axes: centring divides the slack by two, so an odd slack would put the
        // subject half a pixel off centre. Doubling the multiple keeps the aspect exact.
        if ((aspectWidth * multiple % 2) != 0 || (aspectHeight * multiple % 2) != 0)
        {
            multiple *= 2;
        }

        var width = (long)aspectWidth * multiple;
        var height = (long)aspectHeight * multiple;

        // A delivery size large enough to overflow this multiplication would wrap to a negative
        // edge and hand Skia a nonsense allocation, so it is refused explicitly instead.
        if (width > int.MaxValue || height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delivery),
                delivery,
                $"A canvas of the same exact aspect, strictly larger than {delivery}, is " +
                $"{width}×{height} — outside a 32-bit pixel size. `15` §C states nothing this " +
                "large, so the register the caller read is not §C's.");
        }

        return new PixelSize((int)width, (int)height);
    }

    /// <summary>Euclid's algorithm, on two positive integers.</summary>
    /// <param name="first">One value.</param>
    /// <param name="second">The other.</param>
    private static int GreatestCommonDivisor(int first, int second)
    {
        while (second != 0)
        {
            (first, second) = (second, first % second);
        }

        return first;
    }
}
