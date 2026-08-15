using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// The pure reward-banking and run-end payout formulas: Gold per kill (immediate), Legend XP and
/// Soul Shards per kill (banked), and the run-end
/// <c>FinalPayout = BankedRewards * CompletionMultiplier * AdDoubleMultiplier</c>.
/// </summary>
/// <remarks>
/// Keeps computation off the aggregates: <c>Handlers.ConfirmBattleResult</c> calls
/// <see cref="ForKill"/> and hands the two immediate/banked halves to <c>Run.MoveCurrency</c> and
/// <c>Run.BankRewards</c>; <c>Handlers.EndRun</c>/<c>Handlers.AbandonRun</c> call
/// <see cref="FinalPayoutFor"/> and hand the result to <c>Player.GrantLegendXp</c> and
/// <c>Player.MoveCurrency</c>.
/// </remarks>
internal static class RunRewardMath
{
    /// <summary>
    /// The reward one kill pays: immediate Gold, banked Legend XP, and (Boss only) banked Soul
    /// Shards.
    /// </summary>
    /// <param name="kind">The tile kind fought — <see cref="TileKind.Enemy"/>, <see cref="TileKind.Elite"/> or <see cref="TileKind.Boss"/>.</param>
    /// <param name="chapterId">The chapter, from 1.</param>
    /// <param name="tier">The run's difficulty tier.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not Enemy, Elite or Boss.</exception>
    internal static KillReward ForKill(TileKind kind, int chapterId, DifficultyTier tier, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var scalars = ChapterScalarTuning.Read(content);
        var goldPerKill = GoldPerKillTuning.Read(content);
        var runXp = RunXpTuning.Read(content);

        return kind switch
        {
            TileKind.Enemy => new KillReward(
                goldPerKill.ForNormalKill(scalars, chapterId),
                runXp.LegendXpFor(RunXpTuning.NormalEnemyKillSource, chapterId, tier),
                SoulShards: 0),

            TileKind.Elite => new KillReward(
                goldPerKill.ForEliteKill(scalars, chapterId),
                runXp.LegendXpFor(RunXpTuning.EliteKillSource, chapterId, tier),
                SoulShards: 0),

            TileKind.Boss => new KillReward(
                goldPerKill.ForBossKill(scalars, chapterId),
                runXp.LegendXpFor(RunXpTuning.BossKillSource, chapterId, tier),
                SoulShardTuning.Read(content).BossKillShards(chapterId, tier)),

            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "A kill reward is only defined for Enemy, Elite or Boss tiles."),
        };
    }

    /// <summary>The banked Legend XP a run's Victory bonus adds, before <see cref="FinalPayoutFor"/>.</summary>
    internal static long VictoryBonus(int chapterId, DifficultyTier tier, ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return RunXpTuning.Read(content).LegendXpFor(RunXpTuning.RunVictoryBonusSource, chapterId, tier);
    }

    /// <summary>The one-time Soul Shard grant for a Chapter/Tier's first clear.</summary>
    internal static long FirstClearBonus(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return SoulShardTuning.Read(content).FirstClearShards();
    }

    /// <summary>
    /// <c>FinalPayout = BankedRewards * CompletionMultiplier * AdDoubleMultiplier</c>, rounded to a
    /// whole unit (<see cref="MidpointRounding.AwayFromZero"/>).
    /// </summary>
    /// <param name="bankedLegendXp">The run's accumulated banked Legend XP.</param>
    /// <param name="bankedSoulShards">The run's accumulated banked Soul Shards.</param>
    /// <param name="outcome">How the run ended.</param>
    /// <param name="watchedAd">Whether the run-end rewarded ad was watched.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    internal static FinalPayout FinalPayoutFor(
        long bankedLegendXp,
        long bankedSoulShards,
        RunCompletionOutcome outcome,
        bool watchedAd,
        ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var payout = RunPayoutTuning.Read(content);
        var factor = payout.CompletionMultiplier(outcome) * payout.AdDoubleMultiplier(watchedAd);

        return new FinalPayout(Scale(bankedLegendXp, factor), Scale(bankedSoulShards, factor));
    }

    private static long Scale(long banked, double factor)
    {
        var scaled = Math.Round(banked * factor, MidpointRounding.AwayFromZero);

        return scaled switch
        {
            < 0 => 0, // A CompletionMultiplier/AdDoubleMultiplier product is never negative; guarded anyway against a silent negative payout.
            > long.MaxValue => long.MaxValue,
            _ => (long)scaled,
        };
    }
}

/// <summary>One kill's reward — the immediate Gold and the banked Legend XP/Soul Shards.</summary>
/// <param name="Gold">Paid immediately into <c>Run.Gold</c>.</param>
/// <param name="LegendXp">Banked on <c>Run</c>, subject to <see cref="RunRewardMath.FinalPayoutFor"/> at run end.</param>
/// <param name="SoulShards">Banked on <c>Run</c>; zero for every kind but <see cref="TileKind.Boss"/>.</param>
internal readonly record struct KillReward(long Gold, long LegendXp, long SoulShards);

/// <summary>The run-end payout: banked rewards after <c>CompletionMultiplier * AdDoubleMultiplier</c>.</summary>
internal readonly record struct FinalPayout(long LegendXp, long SoulShards);
