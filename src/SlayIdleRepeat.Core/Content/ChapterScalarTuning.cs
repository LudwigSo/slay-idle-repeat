using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §7a — the two chapter growth scalars every in-run income table is a multiple of, read out
/// of <c>tuning/currencies.json#/chapterScalars</c>: <c>G(c) = goldGrowth^(c-1)</c> and
/// <c>M(c) = metaGrowth^(c-1)</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One reader for both scalars rather than a copy per consumer.</b> M3-03's treasure tile, its
/// cache tile and its event cards all scale by <c>M(c)</c>, and the alternative the task brief
/// offered — a private <c>MetaScalar</c> helper duplicated on each tuning reader — would put the
/// <see cref="Math.Pow"/> call and the rounding decision in three places that must agree. `21` §3.1
/// makes the numbers data; this makes the <em>formula</em> single-sourced too. It lives in
/// <c>Content/</c> beside the other readers, not in <c>Rules/Economy/</c>, so that a content reader
/// never has to depend on a rules namespace to read its own document.
/// </para>
/// <para>
/// ⚠️ <b>Only <c>NEAREST_INTEGER</c> is implemented, and the mode is read rather than assumed.</b>
/// <c>#/chapterScalars/roundingMode</c> is authored as <c>NEAREST_INTEGER</c> and nothing else is
/// authored anywhere; a reader that silently accepted another mode would round by a rule the data
/// does not describe, so any other value is refused as an <see cref="InvalidTunableException"/>
/// rather than quietly treated as this one (steering <b>S6</b>).
/// </para>
/// <para>
/// ⚠️ <b><c>adBundleScalar</c> is deliberately not read here.</b> It is the <em>linear</em>
/// <c>1 + 0.35·highestChapterCleared</c> form of `12` §5 / `03` §6.1, a different formula over a
/// different input, and <see cref="MinigameRewardTuning"/> already owns it. Folding two unrelated
/// scaling laws into one type because they share a JSON object would make the object the abstraction.
/// </para>
/// </remarks>
internal sealed class ChapterScalarTuning
{
    /// <summary>The document `03` §7a's chapter scalars live in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string ScalarsPointer = DocumentPath + "#/chapterScalars";

    /// <summary>`03` §7a — <c>G(c) = goldGrowth^(c-1)</c>'s base. 1.55 as shipped.</summary>
    internal const string GoldGrowthReference = ScalarsPointer + "/goldGrowth";

    /// <summary>`03` §7a — <c>M(c) = metaGrowth^(c-1)</c>'s base. 1.35 as shipped.</summary>
    internal const string MetaGrowthReference = ScalarsPointer + "/metaGrowth";

    /// <summary>`03` §7a — how a scaled amount is turned back into a whole currency unit.</summary>
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
    /// 🔒 `03` §7a — <c>G(c) = goldGrowth^(c-1)</c>, the Gold/XP/shop-price growth curve, <b>as the
    /// real multiplier it is</b>.
    /// </summary>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <remarks>
    /// ⚠️ <b>Unrounded, and that is the whole point of the type.</b> See <see cref="ScaleGold"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal double GoldScalar(int chapterId) => Scalar(_goldGrowth, chapterId);

    /// <summary>
    /// 🔒 `03` §7a — <c>M(c) = metaGrowth^(c-1)</c>, the material/meta growth curve, unrounded.
    /// </summary>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <remarks>
    /// Chapter 1 answers <c>1</c> exactly (<c>metaGrowth^0</c>), which is why every in-run income
    /// table can be authored at its chapter-1 value and multiplied.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal double MetaScalar(int chapterId) => Scalar(_metaGrowth, chapterId);

    /// <summary>
    /// 🔒 `03` §7a — scales a chapter-1 authored Gold amount to <paramref name="chapterId"/> and
    /// rounds the <b>product</b> back to a whole currency unit.
    /// </summary>
    /// <inheritdoc cref="ScaleMeta"/>
    internal long ScaleGold(long chapter1Amount, int chapterId) =>
        Scale(chapter1Amount, Scalar(_goldGrowth, chapterId), chapterId);

    /// <summary>
    /// 🔒 `03` §7a — scales a chapter-1 authored meta-currency amount by <c>M(c)</c> and rounds the
    /// <b>product</b> back to a whole currency unit.
    /// </summary>
    /// <param name="chapter1Amount">The amount as the income table authors it, at its chapter-1 value.</param>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The rounding happens HERE, on the scaled amount, and never on the scalar.</b> `03` §7a
    /// makes the in-run amounts <em>multiples of</em> <c>M(c)</c> rounded to a whole currency unit —
    /// the scalar itself is a real number and quantising it first destroys the curve rather than the
    /// payout. ⚠️ This is not a hypothetical: with the shipped <c>metaGrowth: 1.35</c>, rounding the
    /// scalar gives <c>round(1.35) = 1</c>, so <b>chapter 2 would pay exactly what chapter 1 pays</b>
    /// — no growth at all — and chapter 3 would then jump by 100%. Gold is worse: <c>round(1.55) = 2</c>
    /// doubles chapter 2 instead of raising it by 55%. Every in-run income table is a multiple of one
    /// of these two scalars, so the quantisation was the whole economy curve, not a rounding detail.
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="MidpointRounding.AwayFromZero"/>, matching
    /// <see cref="MinigameRewardTuning"/>'s own chapter-scaled payout</b> — the one existing reader
    /// that already scales an authored chapter-1 currency amount by a growth curve, and therefore the
    /// precedent that governs. ⚠️ It is <b>not</b> <c>ShopPricing</c>'s <see cref="MidpointRounding.ToEven"/>:
    /// the two genuinely differ, and this remark says so rather than claiming a single economy-wide
    /// convention that does not exist. Both are `03` §7a's <c>NEAREST_INTEGER</c>; they part company
    /// only at an exact <c>.5</c>, and a payout rounds the player's way there.
    /// </para>
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

        // 03 §7a's curve is exponential, so a large authored base at a high chapter genuinely can
        // leave 64-bit range — and a bare (long) cast of an out-of-range double is UNDEFINED in an
        // unchecked context (it yields long.MinValue on x64), which would turn an overflowing GRANT
        // into a debt. Refused loudly instead: an amount this size is an authoring defect.
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
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
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

        // ⚠️ IsFinite FIRST, and it is a CONSISTENCY guard rather than a live one: ContentValue
        // backs every number with a decimal, so ReadDouble cannot hand back a NaN or an infinity
        // today and this arm is unreachable. It is written anyway because CacheTuning, TreasureTuning
        // and EventCatalogue all guard in exactly this order — a reader here that checked only the
        // sign would read as the one that had thought about it least, and NaN failing every
        // comparison is precisely the trap a lone `<= 0.0` walks into if that backing ever widens.
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
