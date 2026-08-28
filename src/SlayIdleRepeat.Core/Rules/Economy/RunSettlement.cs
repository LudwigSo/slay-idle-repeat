using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Economy;

/// <summary>
/// Closing a run that ended in a Victory or a Death: which completion row it pays, the banked
/// rewards through that row's multiplier, and the run moved to its terminal phase.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Here rather than in the handler, because two callers close a run the same way.</b>
/// <c>END_RUN</c> closes the run the player asked to close, and the catch-up closes one whose window
/// lapsed while nobody was playing it. Two implementations would be two completion multipliers, and
/// the run a player walked away from would pay a different rate than the identical run they came
/// back to finish.
/// </para>
/// <para>
/// What is deliberately NOT here: the Victory bonus banked before the payout, and the session floor.
/// Both are things only a run the player actually finished is owed, and folding them in would pay
/// them to a run nobody was in.
/// </para>
/// </remarks>
internal static class RunSettlement
{
    /// <summary>Income-attribution reason for a run-end Soul Shard payout, from either caller.</summary>
    internal const string PayoutReason = "run_end_payout";

    /// <summary>Which completion row this run pays, read off the run itself.</summary>
    /// <param name="run">The run being closed.</param>
    /// <remarks>
    /// The pending tile is the stage the run reached, and it is still readable at a death because a
    /// loss leaves the tile in place. <c>Run.PendingTileStage</c> throws when nothing is pending, so
    /// the question is asked before the value is taken rather than beside it.
    /// </remarks>
    internal static RunCompletionOutcome OutcomeOf(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return RunRewardMath.OutcomeFor(run.BossDefeated, run.HasPendingTile ? run.PendingTileStage : null);
    }

    /// <summary>Pays the run's banked rewards at its completion rate and closes it.</summary>
    /// <param name="player">Who the payout lands on.</param>
    /// <param name="run">The run being closed.</param>
    /// <param name="outcome">The completion row, from <see cref="OutcomeOf"/>.</param>
    /// <param name="content">The version-stamped content snapshot the command is reading.</param>
    /// <param name="events">The command's events so far; the Soul Shard movement is appended.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    internal static void Settle(
        Player player,
        Run run,
        RunCompletionOutcome outcome,
        ContentSnapshot content,
        List<DomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(events);

        var payout = RunRewardMath.FinalPayoutFor(
            run.BankedLegendXp, run.BankedSoulShards, outcome, watchedAd: false, content);

        if (payout.LegendXp != 0)
        {
            player.GrantLegendXp(payout.LegendXp);
        }

        if (payout.SoulShards != 0)
        {
            events.Add(player.MoveCurrency(CurrencyId.SOUL_SHARDS, payout.SoulShards, PayoutReason));
        }

        run.EndRun();
    }
}
