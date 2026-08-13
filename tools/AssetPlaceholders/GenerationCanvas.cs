using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// The working canvas a placeholder is drawn on, before `15` §B4 step 5 resamples it down to the
/// register's delivery size.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Ruling <c>CON_DELIVERY_ASPECT</c> (M8, 2026-08-13): the generation canvas takes the
/// DELIVERY aspect, authorised by `15` §B0.</b> M8-06 found that a square canvas cannot reach §C's
/// non-square delivery sizes (mounts 512×384, backdrops 1080×1440) without a non-uniform resample,
/// and no section of `15` authorises letterboxing, padding or a crop. The resolution is that §C's
/// "1024×1024" is the baseline <em>resolution</em>, not a universal aspect: §B0 already says
/// <em>"Lock <c>--ar</c> … per category and record them"</em>, and <c>--ar</c> is precisely
/// Midjourney's aspect control. Reading §C as forcing square would make §B0's <c>--ar</c> dead text.
/// </para>
/// <para>
/// 🔒 <b>The canvas is strictly larger than the delivery size, on purpose.</b> M8-06 built step 5
/// against synthetic fixtures and never once resampled for real — an identity resize exercises
/// nothing. Every placeholder therefore arrives on a canvas that step 5 must genuinely reduce.
/// </para>
/// <para>
/// 🔒 <b>The aspect is exact, not approximate.</b> The delivery size is reduced by its greatest
/// common divisor and multiplied by a whole number, so
/// <c>canvas.Width × delivery.Height == canvas.Height × delivery.Width</c> holds in integer
/// arithmetic — which is the comparison <see cref="AssetPipeline.ResizeStep"/> makes before it
/// decides whether to emit <c>CON_DELIVERY_ASPECT</c>. An "almost right" aspect would make every
/// asset in the batch report a doc contradiction that this generator, not `15`, had caused.
/// </para>
/// </remarks>
public static class GenerationCanvas
{
    /// <summary>
    /// `15` §C: <em>"Generation resolution 1024×1024"</em> — the baseline long edge.
    /// </summary>
    public const int BaselineLongEdge = 1024;

    /// <summary>
    /// `15` §C: <em>"(upscale to 2048 for bosses and backgrounds)"</em> — the upscaled long edge.
    /// </summary>
    public const int UpscaledLongEdge = 2048;

    /// <summary>
    /// The delivery long edge at or above which §C's upscaled canvas is used.
    /// </summary>
    /// <remarks>
    /// 🔒 Derived, not invented. §C names bosses and backgrounds for the 2048 canvas and delivers
    /// exactly those two categories at a long edge of 1024 or more (bosses 1024×1024, battle
    /// backdrops 1080×1440); every other row in §C's delivery table is 640 or smaller. Keying on the
    /// delivery size rather than on a hardcoded list of §E-sections means the rule stays true if a
    /// third category ever delivers that large, and it never disagrees with the register.
    /// </remarks>
    public const int UpscaleAtDeliveryLongEdge = BaselineLongEdge;

    /// <summary>The working canvas for one delivery size.</summary>
    /// <param name="delivery">The `15` §C delivery size the register carries for the row.</param>
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

        // Strictly larger in both axes: without this a delivery size at or above the baseline would
        // land on a canvas of its own size and step 5 would be an identity resample — the exact
        // thing this canvas exists to avoid.
        while (aspectWidth * multiple <= delivery.Width || aspectHeight * multiple <= delivery.Height)
        {
            multiple++;
        }

        // Even in both axes. `15` §B4 step 2 centres by integer division of the slack, so an odd
        // slack puts the subject half a pixel off centre and Part F item 7 measures exactly that.
        // Doubling the multiple keeps the aspect exact, which halving or rounding would not.
        if ((aspectWidth * multiple % 2) != 0 || (aspectHeight * multiple % 2) != 0)
        {
            multiple *= 2;
        }

        return new PixelSize(aspectWidth * multiple, aspectHeight * multiple);
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
