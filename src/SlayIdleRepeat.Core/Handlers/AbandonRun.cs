using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>ABANDON_RUN</c> handler: closes the run early, applying a 0.10 multiplier to whatever
/// rewards were banked, regardless of HP or Boss status.
/// </summary>
/// <remarks>
/// Gear drops are not stripped because no gear system exists yet; that lands with whichever task
/// adds gear.
/// </remarks>
internal static class AbandonRun
{
    /// <summary>Applies <c>ABANDON_RUN</c>.</summary>
    internal static HandlerResult Handle(AbandonRunCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (run.Phase != RunPhase.InProgress)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var payout = RunRewardMath.FinalPayoutFor(
            run.BankedLegendXp,
            run.BankedSoulShards,
            RunCompletionOutcome.Abandon,
            watchedAd: false,
            input.Context.Content);

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

    /// <summary>Income-attribution reason for the abandon-run payout.</summary>
    private const string PayoutReason = "run_abandon_payout";
}
