using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>END_RUN</c> handler: banks the run's rewards and closes it, for the two natural terminal
/// states — Victory (the Boss is dead) and Death (the hero's HP is 0 and there is no revive left).
/// </summary>
/// <remarks>
/// <para>
/// Refused outside Victory or Death — "stopped early, unhurt, boss alive" is
/// <see cref="AbandonRunCommand"/>'s case instead.
/// </para>
/// <para>
/// A death fighting the Boss is scored as a Stage 3 death, since the Boss node has no stage of its
/// own; otherwise the stage comes from <c>Run.PendingTileStage</c>, still readable because a loss in
/// <c>Handlers.ConfirmBattleResult</c> leaves the pending tile in place.
/// </para>
/// <para>
/// No ad-double path is reachable yet: <see cref="EndRunCommand"/> carries no payload to signal a
/// watched ad, so this always pays with <c>watchedAd: false</c>.
/// </para>
/// </remarks>
internal static class EndRun
{
    /// <summary>Applies <c>END_RUN</c>.</summary>
    internal static HandlerResult Handle(EndRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.Phase != RunPhase.InProgress)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (!run.BossDefeated && run.CurrentHp != 0)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var outcome = run.BossDefeated
            ? RunCompletionOutcome.Victory
            : DeathOutcomeFor(run);

        if (outcome == RunCompletionOutcome.Victory)
        {
            run.BankRewards(
                RunRewardMath.VictoryBonus(run.ChapterId, run.Tier, input.Context.Content), soulShards: 0);
        }

        var payout = RunRewardMath.FinalPayoutFor(
            run.BankedLegendXp, run.BankedSoulShards, outcome, watchedAd: false, input.Context.Content);

        var events = new List<DomainEvent>();

        if (payout.LegendXp != 0)
        {
            input.Player.GrantLegendXp(payout.LegendXp);
        }

        if (payout.SoulShards != 0)
        {
            events.Add(input.Player.MoveCurrency(CurrencyId.SOUL_SHARDS, payout.SoulShards, PayoutReason));
        }

        run.EndRun();

        return HandlerResult.Accept(events);
    }

    /// <summary>Which Death row a dead-but-not-victorious run pays.</summary>
    private static RunCompletionOutcome DeathOutcomeFor(Run run)
    {
        if (!run.HasPendingTile)
        {
            // Defensive: ConfirmBattleResult's loss branch always leaves the tile pending.
            return RunCompletionOutcome.Stage3Death;
        }

        return run.PendingTileStage switch
        {
            1 => RunCompletionOutcome.Stage1Death,
            2 => RunCompletionOutcome.Stage2Death,
            3 => RunCompletionOutcome.Stage3Death,

            // The Boss node belongs to no stage; treat as Stage 3.
            _ => RunCompletionOutcome.Stage3Death,
        };
    }

    /// <summary>Income-attribution reason for the run-end Soul Shard payout.</summary>
    private const string PayoutReason = "run_end_payout";
}
