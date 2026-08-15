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

    /// <summary>`03` §7a — <c>G(c)</c>, the Gold/XP/shop-price growth curve, rounded to a whole unit.</summary>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal long GoldScalar(int chapterId) => Scalar(_goldGrowth, chapterId);

    /// <summary>
    /// `03` §7a — <c>M(c)</c>, the material/meta growth curve, rounded to a whole unit.
    /// </summary>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <remarks>
    /// Chapter 1 answers <c>1</c> exactly (<c>metaGrowth^0</c>), which is why every in-run income
    /// table can be authored at its chapter-1 value and multiplied.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal long MetaScalar(int chapterId) => Scalar(_metaGrowth, chapterId);

    private static long Scalar(double growth, int chapterId)
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

        // MidpointRounding.ToEven is Math.Round's default and the same mode ShopPricing already
        // rounds its prices under — one rounding convention for the whole economy, not two.
        return (long)Math.Round(Math.Pow(growth, chapterId - 1), MidpointRounding.ToEven);
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

        if (growth <= 0.0)
        {
            throw new InvalidTunableException(
                reference,
                "A growth base of zero or below makes every chapter's scalar zero, negative or " +
                "oscillating. 03 §7a authors 1.55 (gold) and 1.35 (meta); this document authors " +
                Text(growth) + ".");
        }

        return growth;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
