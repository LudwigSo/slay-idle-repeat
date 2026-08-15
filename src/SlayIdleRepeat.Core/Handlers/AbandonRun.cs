using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-13, `14` §2.3 — the <c>ABANDON_RUN</c> handler: closes the run with `02` §5.2's
/// <c>ABANDON</c> multiplier (0.10) applied to whatever was banked, no matter the run's HP or
/// whether the Boss is dead.
/// </summary>
/// <remarks>
/// ⚠️ <b>"No gear drops are kept" is a no-op today, and that is a scope boundary, not a lost rule.</b>
/// There is no gear system in this milestone — <c>GapRegister</c>'s inventory/gear-instance entries
/// are M4's — so there is nothing for this handler to strip. The 0.10 multiplier on
/// <c>BankedRewards</c> is the whole of what M3-13 can enforce; the day gear exists, its own task
/// reads this handler's remarks and adds the strip.
/// </remarks>
internal static class AbandonRun
{
    /// <summary>🔒 `02` §5.2 — applies <c>ABANDON_RUN</c>.</summary>
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

    /// <summary>The `21` §8.3 income-attribution reason the abandon-run payout carries.</summary>
    private const string PayoutReason = "run_abandon_payout";
}
