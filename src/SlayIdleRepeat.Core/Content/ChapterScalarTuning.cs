using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The two chapter growth scalars every in-run income table is a multiple of, read out of
/// <c>tuning/currencies.json#/chapterScalars</c>: <c>G(c) = goldGrowth^(c-1)</c> and
/// <c>M(c) = metaGrowth^(c-1)</c>.
/// </summary>
/// <remarks>
/// <para>
/// One reader for both scalars rather than a copy per consumer, so the <see cref="Math.Pow"/> call
/// and the rounding decision live in one place rather than several that must agree.
/// </para>
/// <para>
/// Only <c>NEAREST_INTEGER</c> is implemented, and the mode is read rather than assumed: any other
/// value is refused rather than quietly treated as this one.
/// </para>
/// <para>
/// <c>adBundleScalar</c> is deliberately not read here — it is a different, linear formula over a
/// different input, and <see cref="MinigameRewardTuning"/> already owns it.
/// </para>
/// </remarks>
internal sealed class ChapterScalarTuning
{
    /// <summary>The document the chapter scalars live in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string ScalarsPointer = DocumentPath + "#/chapterScalars";

    /// <summary><c>G(c) = goldGrowth^(c-1)</c>'s base. 1.55 as shipped.</summary>
    internal const string GoldGrowthReference = ScalarsPointer + "/goldGrowth";

    /// <summary><c>M(c) = metaGrowth^(c-1)</c>'s base. 1.35 as shipped.</summary>
    internal const string MetaGrowthReference = ScalarsPointer + "/metaGrowth";

    /// <summary>How a scaled amount is turned back into a whole currency unit.</summary>
    internal const string RoundingModeReference = ScalarsPointer + "/roundingMode";

    /// <summary>The one rounding mode this reader implements.</summary>
    internal const string NearestInteger = "NEAREST_INTEGER";

    private readonly double _goldGrowth;
    private readonly double _metaGrowth;

    private ChapterScalarTuning(double goldGrowth, double metaGrowth)
    {
        _goldGrowth = goldGrowth;
        _metaGrowth = metaGrowth;
    }

    /// <summary>
    /// <c>G(c) = goldGrowth^(c-1)</c>, the Gold/XP/shop-price growth curve, as the real multiplier
    /// it is — unrounded (see <see cref="ScaleGold"/>).
    /// </summary>
    /// <param name="chapterId">The chapter, from 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal double GoldScalar(int chapterId) => Scalar(_goldGrowth, chapterId);

    /// <summary><c>M(c) = metaGrowth^(c-1)</c>, the material/meta growth curve, unrounded.</summary>
    /// <param name="chapterId">The chapter, from 1.</param>
    /// <remarks>
    /// Chapter 1 answers <c>1</c> exactly (<c>metaGrowth^0</c>), which is why every in-run income
    /// table can be authored at its chapter-1 value and multiplied.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal double MetaScalar(int chapterId) => Scalar(_metaGrowth, chapterId);

    /// <summary>
    /// Scales a chapter-1 authored Gold amount to <paramref name="chapterId"/> and rounds the
    /// product back to a whole currency unit.
    /// </summary>
    /// <inheritdoc cref="ScaleMeta"/>
    internal long ScaleGold(long chapter1Amount, int chapterId) =>
        Scale(chapter1Amount, Scalar(_goldGrowth, chapterId), chapterId);

    /// <summary>
    /// Scales a chapter-1 authored meta-currency amount by <c>M(c)</c> and rounds the product back
    /// to a whole currency unit.
    /// </summary>
    /// <param name="chapter1Amount">The amount as the income table authors it, at its chapter-1 value.</param>
    /// <param name="chapterId">The chapter, from 1.</param>
    /// <remarks>
    /// The rounding happens on the scaled amount, never on the scalar: quantising the scalar first
    /// destroys the curve rather than the payout. With the shipped <c>metaGrowth: 1.35</c>, rounding
    /// the scalar itself would give <c>round(1.35) = 1</c>, so chapter 2 would pay exactly what
    /// chapter 1 pays. Uses <see cref="MidpointRounding.AwayFromZero"/>, matching
    /// <see cref="MinigameRewardTuning"/>'s payout rounding (not <c>ShopPricing</c>'s
    /// <see cref="MidpointRounding.ToEven"/> — the two genuinely differ).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="chapterId"/> is below 1, or the scaled amount leaves 64-bit range.
    /// </exception>
    internal long ScaleMeta(long chapter1Amount, int chapterId) =>
        Scale(chapter1Amount, Scalar(_metaGrowth, chapterId), chapterId);

    private static double Scalar(double growth, int chapterId)
    {
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId),
                chapterId,
                "02 §1 runs chapters from 1 and chapter.schema.json sets \"minimum\": 1; " +
                Text(chapterId) + " is below the floor the exponent (chapter - 1) is defined over. " +
                "⚠️ There is deliberately no upper bound: content/chapters/ holds chapters 1-2 " +
                "today and 3-8 are M11-02's, so a ceiling here would be a content bound in code " +
                "(21 §3.1).");
        }

        return Math.Pow(growth, chapterId - 1);
    }

    /// <summary>
    /// Multiplies first and rounds second — see <see cref="ScaleMeta"/> for why that order is the
    /// entire correctness of this type.
    /// </summary>
    private static long Scale(long chapter1Amount, double scalar, int chapterId)
    {
        var scaled = Math.Round(chapter1Amount * scalar, MidpointRounding.AwayFromZero);

        // A bare (long) cast of an out-of-range double is UNDEFINED in an unchecked context (it
        // yields long.MinValue on x64), which would turn an overflowing grant into a debt.
        if (!double.IsFinite(scaled) || scaled is < long.MinValue or > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapter1Amount),
                chapter1Amount,
                "Scaling " + Text(chapter1Amount) + " to chapter " + Text(chapterId) + " gives " +
                Text(scaled) + ", which is outside a 64-bit currency amount. 03 §7a's growth curve " +
                "is exponential, so this is an authored base far too large for the curve rather " +
                "than an arithmetic accident.");
        }

        return (long)scaled;
    }

    /// <summary>Reads the chapter-scalar block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static ChapterScalarTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var roundingMode = content.ReadText(RoundingModeReference);
        if (!string.Equals(roundingMode, NearestInteger, StringComparison.Ordinal))
        {
            throw new InvalidTunableException(
                RoundingModeReference,
                "This reader implements exactly one rounding mode, '" + NearestInteger + "', which " +
                "is what 03 §7a authors; this document authors '" + roundingMode + "'. No other " +
                "mode is implemented, and rounding by NEAREST_INTEGER anyway would apply a rule the " +
                "data does not describe to every currency amount in the run.");
        }

        return new ChapterScalarTuning(
            ReadGrowth(content, GoldGrowthReference),
            ReadGrowth(content, MetaGrowthReference));
    }

    private static double ReadGrowth(ContentSnapshot content, string reference)
    {
        var growth = content.ReadDouble(reference);

        // IsFinite first: ReadDouble cannot yield NaN/infinity today, but a lone `<= 0.0` would be
        // the trap if that backing ever widens, since NaN fails every comparison.
        if (!double.IsFinite(growth) || growth <= 0.0)
        {
            throw new InvalidTunableException(
                reference,
                "A growth base is a finite number above zero; anything else makes every chapter's " +
                "scalar zero, negative, oscillating or not a number at all. 03 §7a authors 1.55 " +
                "(gold) and 1.35 (meta); this document authors " + Text(growth) + ".");
        }

        return growth;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
