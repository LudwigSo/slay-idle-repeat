using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 M3-13, `10` §2 — the Soul Shard sources this milestone pays: a Boss kill
/// (<c>BOSS_KILL_*_PER_CHAPTER</c>) and a first Chapter/Tier clear, read out of
/// <c>tuning/currencies.json#/soulShards/sources</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><c>_PER_CHAPTER</c> is a linear multiple of the chapter, not <c>ChapterScalarTuning</c>'s
/// exponential <c>G(c)</c>/<c>M(c)</c>.</b> The three keys are named after the chapter directly — no
/// growth base is authored alongside them — so <see cref="BossKillShards"/> reads them as
/// <c>perChapterValue(tier) * chapterId</c>. This is a documented reading of an ambiguous key name,
/// not a transcribed formula: no design doc excerpt in this repository spells out the boss-kill Soul
/// Shard curve beyond the three per-tier constants and the "per chapter" name.
/// </para>
/// <para>
/// ⚠️ <b>The first-clear grant amount is a judgement call, not an authored number.</b> `02` §5.3
/// gives only <c>FIRST_CLEAR_MIN</c> (100) and <c>FIRST_CLEAR_MAX</c> (800); nothing in this
/// repository picks a point between them. <see cref="FirstClearShards"/> answers the midpoint, 450,
/// which this reader validates falls within the authored bounds so a future data edit narrowing the
/// range fails loudly instead of silently granting outside it.
/// </para>
/// </remarks>
internal sealed class SoulShardTuning
{
    /// <summary>The document `10` §2's soulShards block lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string SourcesPointer = DocumentPath + "#/soulShards/sources";

    /// <summary>`10` §2 — Normal-tier Boss-kill Soul Shards, per chapter. 15 as shipped.</summary>
    internal const string BossKillNormalReference = SourcesPointer + "/BOSS_KILL_NORMAL_PER_CHAPTER";

    /// <summary>`10` §2 — Heroic-tier Boss-kill Soul Shards, per chapter. 40 as shipped.</summary>
    internal const string BossKillHeroicReference = SourcesPointer + "/BOSS_KILL_HEROIC_PER_CHAPTER";

    /// <summary>`10` §2 — Mythic-tier Boss-kill Soul Shards, per chapter. 100 as shipped.</summary>
    internal const string BossKillMythicReference = SourcesPointer + "/BOSS_KILL_MYTHIC_PER_CHAPTER";

    /// <summary>`02` §5.3 — the floor of the first-clear grant range. 100 as shipped.</summary>
    internal const string FirstClearMinReference = SourcesPointer + "/FIRST_CLEAR_MIN";

    /// <summary>`02` §5.3 — the ceiling of the first-clear grant range. 800 as shipped.</summary>
    internal const string FirstClearMaxReference = SourcesPointer + "/FIRST_CLEAR_MAX";

    /// <summary>
    /// 🔒 The fixed first-clear grant this reader pays — the midpoint of
    /// [<see cref="FirstClearMinReference"/>, <see cref="FirstClearMaxReference"/>] as shipped
    /// (100..800). See this type's remarks for why a single fixed value stands in for a range no
    /// document narrows further.
    /// </summary>
    internal const long FirstClearGrant = 450;

    private readonly IReadOnlyDictionary<DifficultyTier, long> _bossKillPerChapter;

    private SoulShardTuning(IReadOnlyDictionary<DifficultyTier, long> bossKillPerChapter)
    {
        _bossKillPerChapter = bossKillPerChapter;
    }

    /// <summary>`10` §2 — the Soul Shards a Boss kill pays at <paramref name="chapterId"/> and <paramref name="tier"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    internal long BossKillShards(int chapterId, DifficultyTier tier)
    {
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId), chapterId, "02 §1 runs chapters from 1.");
        }

        return _bossKillPerChapter[tier] * chapterId;
    }

    /// <summary>`02` §5.3 — the one-time Soul Shard grant for a Chapter/Tier's first clear.</summary>
    internal long FirstClearShards() => FirstClearGrant;

    /// <summary>Reads the soulShards sources block. Throws rather than defaulting on anything unusable.</summary>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static SoulShardTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var perChapter = new Dictionary<DifficultyTier, long>
        {
            [DifficultyTier.NORMAL] = ReadNonNegative(content, BossKillNormalReference),
            [DifficultyTier.HEROIC] = ReadNonNegative(content, BossKillHeroicReference),
            [DifficultyTier.MYTHIC] = ReadNonNegative(content, BossKillMythicReference),
        };

        var min = content.ReadInt64(FirstClearMinReference);
        var max = content.ReadInt64(FirstClearMaxReference);

        if (min < 0 || max < min)
        {
            throw new InvalidTunableException(
                FirstClearMinReference,
                "The first-clear range must be a non-negative, non-decreasing [MIN, MAX]. This " +
                "document authors [" + Text(min) + ", " + Text(max) + "].");
        }

        if (FirstClearGrant < min || FirstClearGrant > max)
        {
            throw new InvalidTunableException(
                FirstClearMinReference,
                "SoulShardTuning.FirstClearGrant (" + Text(FirstClearGrant) + ") falls outside this " +
                "document's authored [" + Text(min) + ", " + Text(max) + "] range. The grant is a " +
                "fixed midpoint of the SHIPPED 100..800 range; a data edit that narrows the range " +
                "past it must be reconciled by hand, not silently clamped.");
        }

        return new SoulShardTuning(perChapter);
    }

    private static long ReadNonNegative(ContentSnapshot content, string reference)
    {
        var value = content.ReadInt64(reference);
        if (value < 0)
        {
            throw new InvalidTunableException(
                reference, "A Boss-kill Soul Shard rate must not be negative; this document authors " +
                Text(value) + ".");
        }

        return value;
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
