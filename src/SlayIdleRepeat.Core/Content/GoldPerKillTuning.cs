using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 M3-13, `03` §7a.1 — <c>GoldPerKill(c) = base * G(c)</c>, read out of
/// <c>tuning/currencies.json#/inRunIncome/goldPerKill</c>.
/// </summary>
/// <remarks>
/// ⚠️ Gold is paid <b>immediately</b> on the kill, into <c>Run.Gold</c> — it is the run-local wallet
/// that "vanishes at run end" (`03` §7a.1: <em>"No Gold is paid for run victory"</em>), unlike Legend
/// XP and Soul Shards, which are banked and pass through the run-end <c>CompletionMultiplier</c>. See
/// <see cref="RunXpTuning"/>.
/// </remarks>
internal sealed class GoldPerKillTuning
{
    /// <summary>The document `03` §7a.1's goldPerKill block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string GoldPerKillPointer = DocumentPath + "#/inRunIncome/goldPerKill";

    /// <summary>`03` §7a.1 — the chapter-1 Gold paid for a normal enemy kill. 40 as shipped.</summary>
    internal const string BaseReference = GoldPerKillPointer + "/base";

    /// <summary>`03` §7a.1 — the Elite multiplier over <see cref="BaseReference"/>. 3x as shipped.</summary>
    internal const string EliteMultiplierReference = GoldPerKillPointer + "/eliteMultiplier";

    /// <summary>`03` §7a.1 — the Boss multiplier over <see cref="BaseReference"/>. 10x as shipped.</summary>
    internal const string BossMultiplierReference = GoldPerKillPointer + "/bossMultiplier";

    private readonly long _base;
    private readonly long _eliteMultiplier;
    private readonly long _bossMultiplier;

    private GoldPerKillTuning(long baseAmount, long eliteMultiplier, long bossMultiplier)
    {
        _base = baseAmount;
        _eliteMultiplier = eliteMultiplier;
        _bossMultiplier = bossMultiplier;
    }

    /// <summary>`03` §7a.1 — Gold for a normal enemy kill at <paramref name="chapterId"/>, scaled by <c>G(c)</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal long ForNormalKill(ChapterScalarTuning scalars, int chapterId) =>
        Scale(scalars, _base, chapterId);

    /// <summary>`03` §7a.1 — Gold for an Elite kill at <paramref name="chapterId"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal long ForEliteKill(ChapterScalarTuning scalars, int chapterId) =>
        Scale(scalars, _base * _eliteMultiplier, chapterId);

    /// <summary>`03` §7a.1 — Gold for a Boss kill at <paramref name="chapterId"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal long ForBossKill(ChapterScalarTuning scalars, int chapterId) =>
        Scale(scalars, _base * _bossMultiplier, chapterId);

    private static long Scale(ChapterScalarTuning scalars, long chapter1Amount, int chapterId)
    {
        ArgumentNullException.ThrowIfNull(scalars);

        return scalars.ScaleGold(chapter1Amount, chapterId);
    }

    /// <summary>Reads the goldPerKill block. Throws rather than defaulting on anything unusable.</summary>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static GoldPerKillTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var baseAmount = content.ReadInt64(BaseReference);
        if (baseAmount < 0)
        {
            throw new InvalidTunableException(
                BaseReference,
                "GoldPerKill's base must not be negative. 03 §7a.1 authors 40; this document " +
                "authors " + Text(baseAmount) + ".");
        }

        var eliteMultiplier = content.ReadInt64(EliteMultiplierReference);
        if (eliteMultiplier < 1)
        {
            throw new InvalidTunableException(
                EliteMultiplierReference,
                "An Elite must pay at least as much as a normal kill. 03 §7a.1 authors 3x; this " +
                "document authors " + Text(eliteMultiplier) + "x.");
        }

        var bossMultiplier = content.ReadInt64(BossMultiplierReference);
        if (bossMultiplier < 1)
        {
            throw new InvalidTunableException(
                BossMultiplierReference,
                "A Boss must pay at least as much as a normal kill. 03 §7a.1 authors 10x; this " +
                "document authors " + Text(bossMultiplier) + "x.");
        }

        return new GoldPerKillTuning(baseAmount, eliteMultiplier, bossMultiplier);
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
