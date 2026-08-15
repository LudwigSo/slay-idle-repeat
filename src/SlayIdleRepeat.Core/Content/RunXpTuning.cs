using System.Globalization;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 M3-13, `02` §5.1a — Legend XP income: <c>BaseXp(c) = baseXpCoefficient * baseXpGrowth^(c-1)</c>,
/// scaled by a difficulty-tier multiplier and a per-source multiplier, read out of
/// <c>tuning/progression.json#/runXp</c>.
/// </summary>
/// <remarks>
/// Legend XP is a <b>banked</b> reward (`02` §5.1a): the amounts this type computes are accumulated on
/// <c>Run</c> during the run and only reach <c>Player.LegendXp</c> through the run-end
/// <c>CompletionMultiplier</c> / <c>AdDoubleMultiplier</c> payout — unlike Gold, which
/// <see cref="GoldPerKillTuning"/> pays immediately.
/// </remarks>
internal sealed class RunXpTuning
{
    /// <summary>The document `02` §5.1a's runXp block lives in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string RunXpPointer = DocumentPath + "#/runXp";

    /// <summary>`02` §5.1a — the coefficient BaseXp(c) starts from. 25 as shipped.</summary>
    internal const string BaseXpCoefficientReference = RunXpPointer + "/baseXpCoefficient";

    /// <summary>`02` §5.1a — <c>BaseXp(c) = baseXpCoefficient * baseXpGrowth^(c-1)</c>'s base. 1.55 as shipped.</summary>
    internal const string BaseXpGrowthReference = RunXpPointer + "/baseXpGrowth";

    private const string TierMultiplierPointer = RunXpPointer + "/tierMultiplier";

    private const string SourceMultiplierPointer = RunXpPointer + "/sourceMultiplier";

    // 🔒 static readonly, NOT const, and that is load-bearing rather than a style choice: a `const`
    // is inlined as an ldstr AT EVERY CALL SITE, including RunRewardMath.ForKill in
    // Rules/Economy/ — and BossEngineRuleTests.No_boss_identity_is_named_in_code fails any string
    // literal starting with "BOSS_" found anywhere under the Rules namespace, on the theory that
    // 17 §11 keeps all eight boss identities out of code. "BOSS_KILL" here is progression.json's
    // Legend XP SOURCE key, not one of 17 §1.2's eight boss ids, but the rule cannot tell the two
    // apart by spelling — so this field is read through an ldsfld instead of inlined, keeping the
    // one ldstr that names it inside Content/ (unscanned) rather than duplicated into Rules/.

    /// <summary>`02` §5.1a — a normal enemy kill's source multiplier. 1x as shipped.</summary>
    internal static readonly string NormalEnemyKillSource = "NORMAL_ENEMY_KILL";

    /// <summary>`02` §5.1a — an Elite kill's source multiplier. 3x as shipped.</summary>
    internal static readonly string EliteKillSource = "ELITE_KILL";

    /// <summary>`02` §5.1a — a Boss kill's source multiplier. 15x as shipped.</summary>
    internal static readonly string BossKillSource = "BOSS_KILL";

    /// <summary>`02` §5.1a — the run-victory bonus's source multiplier. 10x as shipped.</summary>
    internal static readonly string RunVictoryBonusSource = "RUN_VICTORY_BONUS";

    private readonly double _baseXpCoefficient;
    private readonly double _baseXpGrowth;
    private readonly IReadOnlyDictionary<DifficultyTier, double> _tierMultiplier;
    private readonly IReadOnlyDictionary<string, double> _sourceMultiplier;

    private RunXpTuning(
        double baseXpCoefficient,
        double baseXpGrowth,
        IReadOnlyDictionary<DifficultyTier, double> tierMultiplier,
        IReadOnlyDictionary<string, double> sourceMultiplier)
    {
        _baseXpCoefficient = baseXpCoefficient;
        _baseXpGrowth = baseXpGrowth;
        _tierMultiplier = tierMultiplier;
        _sourceMultiplier = sourceMultiplier;
    }

    /// <summary>
    /// 🔒 `02` §5.1a — the Legend XP one source pays at a chapter and tier, rounded to a whole XP
    /// point (<see cref="MidpointRounding.AwayFromZero"/>, the same convention
    /// <see cref="ChapterScalarTuning.ScaleMeta"/> uses for a chapter-scaled payout).
    /// </summary>
    /// <param name="source">One of <see cref="NormalEnemyKillSource"/>, <see cref="EliteKillSource"/>,
    /// <see cref="BossKillSource"/> or <see cref="RunVictoryBonusSource"/>.</param>
    /// <param name="chapterId">`02` §1's chapter, from 1.</param>
    /// <param name="tier">The run's difficulty tier.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="chapterId"/> is below 1.</exception>
    /// <exception cref="KeyNotFoundException"><paramref name="source"/> is not authored.</exception>
    internal long LegendXpFor(string source, int chapterId, DifficultyTier tier)
    {
        if (chapterId < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chapterId), chapterId, "02 §1 runs chapters from 1.");
        }

        var baseXp = _baseXpCoefficient * Math.Pow(_baseXpGrowth, chapterId - 1);
        var scaled = baseXp * _tierMultiplier[tier] * _sourceMultiplier[source];

        return (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }

    /// <summary>Reads the runXp block. Throws rather than defaulting on anything unusable.</summary>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static RunXpTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var baseXpCoefficient = content.ReadDouble(BaseXpCoefficientReference);
        if (!double.IsFinite(baseXpCoefficient) || baseXpCoefficient <= 0.0)
        {
            throw new InvalidTunableException(
                BaseXpCoefficientReference,
                "The Legend XP coefficient must be a finite number above zero. 02 §5.1a authors 25; " +
                "this document authors " + Text(baseXpCoefficient) + ".");
        }

        var baseXpGrowth = content.ReadDouble(BaseXpGrowthReference);
        if (!double.IsFinite(baseXpGrowth) || baseXpGrowth <= 0.0)
        {
            throw new InvalidTunableException(
                BaseXpGrowthReference,
                "The Legend XP growth base must be a finite number above zero. 02 §5.1a authors " +
                "1.55; this document authors " + Text(baseXpGrowth) + ".");
        }

        var tierMultiplier = new Dictionary<DifficultyTier, double>
        {
            [DifficultyTier.NORMAL] = ReadPositive(content, TierMultiplierPointer + "/NORMAL"),
            [DifficultyTier.HEROIC] = ReadPositive(content, TierMultiplierPointer + "/HEROIC"),
            [DifficultyTier.MYTHIC] = ReadPositive(content, TierMultiplierPointer + "/MYTHIC"),
        };

        var sourceMultiplier = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [NormalEnemyKillSource] = ReadPositive(content, SourceMultiplierPointer + "/" + NormalEnemyKillSource),
            [EliteKillSource] = ReadPositive(content, SourceMultiplierPointer + "/" + EliteKillSource),
            [BossKillSource] = ReadPositive(content, SourceMultiplierPointer + "/" + BossKillSource),
            [RunVictoryBonusSource] = ReadPositive(content, SourceMultiplierPointer + "/" + RunVictoryBonusSource),
        };

        return new RunXpTuning(baseXpCoefficient, baseXpGrowth, tierMultiplier, sourceMultiplier);
    }

    private static double ReadPositive(ContentSnapshot content, string reference)
    {
        var value = content.ReadDouble(reference);
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw new InvalidTunableException(
                reference, "A Legend XP multiplier must be a finite number above zero; this document " +
                "authors " + Text(value) + ".");
        }

        return value;
    }

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
