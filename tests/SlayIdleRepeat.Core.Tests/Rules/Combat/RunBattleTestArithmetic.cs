using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The tile's enemy power and the tier's ordinal, transcribed here from the authored curve rather
/// than taken from the production seam.
/// </summary>
/// <remarks>
/// 🔒 <b>Deliberately a second transcription, not a call into <c>EnemyPowerFormula</c>.</b> The
/// hazard case compares the seam's fight against one composed here, and a helper that asked the
/// production formula for the number would agree with the seam by construction — it would still
/// agree if the seam read the Heroic column and applied <c>TierMult</c> on top of it, which is the
/// exact double-application shape the case exists to catch, one layer down from the hero.
/// <para>
/// The four stage multipliers, the per-node growth term and the three tier multipliers are
/// transcribed from the enemy-power curve; the chapter target is read out of the par table's Normal
/// column, because that column is the only one the curve's <c>ChapterPowerTarget(c)</c> term names.
/// </para>
/// </remarks>
internal static class RunBattleTestArithmetic
{
    /// <summary>The per-node linear growth coefficient.</summary>
    private const double PerNodeGrowth = 0.035;

    /// <summary>The stage multipliers, in stage order, with the boss's held apart.</summary>
    private const double Stage1 = 1.00;

    /// <inheritdoc cref="Stage1"/>
    private const double Stage2 = 1.15;

    /// <inheritdoc cref="Stage1"/>
    private const double Stage3 = 1.35;

    /// <summary>The boss's stage multiplier. The boss belongs to no stage.</summary>
    private const double Boss = 2.20;

    /// <summary>The enemy power one tile of one run is fought at.</summary>
    internal static double EnemyPower(RunSnapshot run, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);

        return ParPowerTuning.Read(content).ChapterPowerTarget(run.ChapterId)
               * TierMultiplier(run.Tier)
               * (1.0 + (PerNodeGrowth * run.PendingTileLinearIndex))
               * StageMultiplier(run.PendingTileStage);
    }

    /// <summary>The tier's ordinal in the enemy level table's authored order.</summary>
    internal static int TierOrdinal(DifficultyTier tier) => tier switch
    {
        DifficultyTier.NORMAL => 0,
        DifficultyTier.HEROIC => 1,
        DifficultyTier.MYTHIC => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Not one of the three tiers."),
    };

    /// <summary>Every stat of a block, added up — a single number two builds can be ordered by.</summary>
    /// <remarks>
    /// Crude on purpose. It is used only to pin a DIRECTION (a doubled loadout is the bigger block),
    /// never a value, so a weighting scheme here would be inventing a power model beside the real
    /// one.
    /// </remarks>
    internal static double Sum(ActorStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var total = 0.0;

        foreach (var (_, value) in stats.Values)
        {
            total += value;
        }

        return total;
    }

    private static double TierMultiplier(DifficultyTier tier) => tier switch
    {
        DifficultyTier.NORMAL => 1.0,
        DifficultyTier.HEROIC => 4.0,
        DifficultyTier.MYTHIC => 16.0,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Not one of the three tiers."),
    };

    private static double StageMultiplier(int stage) => stage switch
    {
        1 => Stage1,
        2 => Stage2,
        3 => Stage3,
        BoardGraph.BossStage => Boss,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Not one of the four stages."),
    };
}
