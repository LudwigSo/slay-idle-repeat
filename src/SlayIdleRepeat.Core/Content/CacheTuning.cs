using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 `03` §7a.4 — <c>TILE_CACHE</c>'s Beast Feed payout and its Pet Egg rate, read out of
/// <c>tuning/currencies.json#/inRunIncome/cache</c>.
/// </summary>
/// <remarks>
/// §7a.4: <em>"CacheFeed(c) = base · M(c). On an egg hit the cache pays one Pet Egg instead of the
/// Feed. The egg rate is flat across chapters."</em> The <c>M(c)</c> curve is
/// <see cref="ChapterScalarTuning"/>'s; the egg rate is deliberately <em>not</em> scaled here,
/// because the document says it does not scale.
/// </remarks>
internal sealed class CacheTuning
{
    /// <summary>The document `03` §7a.4's cache block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string CachePointer = DocumentPath + "#/inRunIncome/cache";

    /// <summary>`03` §7a.4 — the chapter-1 Beast Feed payout. 25 as shipped.</summary>
    internal const string BeastFeedBaseReference = CachePointer + "/beastFeedBase";

    /// <summary>`03` §7a.4 — the flat Pet Egg rate. 0.06 as shipped.</summary>
    internal const string EggChanceReference = CachePointer + "/eggChance";

    private CacheTuning(long beastFeedBase, double eggChance)
    {
        BeastFeedBase = beastFeedBase;
        EggChance = eggChance;
    }

    /// <summary>`03` §7a.4 — the Beast Feed payout before <c>M(c)</c>.</summary>
    internal long BeastFeedBase { get; }

    /// <summary>
    /// `03` §7a.4 — the probability a cache pays a Pet Egg instead of Feed, in <c>[0,1]</c>. Flat
    /// across chapters.
    /// </summary>
    internal double EggChance { get; }

    /// <summary>Reads the cache block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static CacheTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var beastFeedBase = content.ReadInt64(BeastFeedBaseReference);
        if (beastFeedBase < 0)
        {
            throw new InvalidTunableException(
                BeastFeedBaseReference,
                "A cache pays out; it never charges. 03 §7a.4 authors 25; this document authors " +
                Text(beastFeedBase) + ".");
        }

        var eggChance = content.ReadDouble(EggChanceReference);
        if (!double.IsFinite(eggChance) || eggChance is < 0.0 or > 1.0)
        {
            throw new InvalidTunableException(
                EggChanceReference,
                "An egg rate is a probability in [0,1]. 03 §7a.4 authors 0.06; this document " +
                "authors " + Text(eggChance) + ". ⚠️ 0 and 1 are both accepted: a rate of 0 is how " +
                "content switches the egg off, and 1 is how a test pins the egg branch.");
        }

        return new CacheTuning(beastFeedBase, eggChance);
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
