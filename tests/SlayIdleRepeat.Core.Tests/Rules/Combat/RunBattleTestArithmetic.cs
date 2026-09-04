using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The tile's enemy power and the tier's ordinal, transcribed from the authored curve rather than
/// taken from the production seam.
/// </summary>
/// <remarks>
/// Deliberately a second transcription, not a call into <c>EnemyPowerFormula</c>: a helper that
/// asked the production formula would agree with the seam by construction, even when the seam
/// double-applies a multiplier — the exact shape the comparison cases exist to catch.
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

    /// <summary>
    /// The boss's stage multiplier. The boss belongs to no stage, and its node's linear index has
    /// already multiplied the target by <c>2.47</c> before this applies — which is why it is
    /// <c>1.35</c> and not <c>02</c> §4.3's <c>2.20</c>. See
    /// <c>EnemyPowerFormula.BossMultiplier</c>.
    /// </summary>
    private const double Boss = 1.35;

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

    /// <summary>A build's effects as roster holdings, each keeping its collected instance id.</summary>
    internal static IReadOnlyList<HeldEffect> Holdings(HeroBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var held = new HeldEffect[build.Collected.Count];

        for (var i = 0; i < held.Length; i++)
        {
            held[i] = new HeldEffect(build.Collected[i].Effect, build.Collected[i].Instance);
        }

        return held;
    }

    /// <summary>The same holdings with every STANDING modifier struck out.</summary>
    /// <remarks>
    /// The standing test is transcribed here, not borrowed: an effect stands when its trigger is
    /// absent OR authored <c>ALWAYS</c>, and asking the production helper for that reading would
    /// make the comparison agree with the seam about the one thing the case is asking about.
    /// </remarks>
    internal static IReadOnlyList<HeldEffect> HoldingsWithoutStandingModifiers(HeroBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        return Holdings(build).Where(held => !Stands(held.Effect)).ToArray();
    }

    /// <summary>The effects of a build that stand — the half a fight has to re-aggregate every pass.</summary>
    internal static IReadOnlyList<EffectDefinition> StandingEffects(HeroBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);

        return build.Effects.Where(Stands).ToArray();
    }

    /// <inheritdoc cref="HoldingsWithoutStandingModifiers"/>
    private static bool Stands(EffectDefinition effect) =>
        effect.Trigger is null || effect.Trigger.Kind == TriggerKind.ALWAYS;

    /// <summary>
    /// Every stat of a block, added up — pins only a DIRECTION between two builds, never a value.
    /// </summary>
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
