using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>REROLL_DRAFT</c> handler: redraws the perk draft.
/// </summary>
/// <remarks>
/// <para>
/// The cost is charged before anything is drawn: an unaffordable reroll costs the player nothing and
/// consumes no RNG draw index either.
/// </para>
/// <para>
/// Rerolling means advancing the <c>draft</c> stream past the current options, not storing a new
/// set. The three options a player sees are always whatever <c>PickPerk.GenerateCurrentOptions</c>
/// derives from the run's committed draft-stream position right now, so a reroll's whole job is to
/// move that position forward — drawing a (discarded) set of options already does that. The draft
/// stays pending.
/// </para>
/// <para>
/// No reroll-count cap is enforced here: the reroll economy is keyed on Gold, not a per-draft count.
/// </para>
/// </remarks>
internal static class RerollDraft
{
    /// <summary>Income-attribution token a reroll's cost is logged under.</summary>
    internal const string CostReason = "draft_reroll_cost";

    /// <summary>Applies <c>REROLL_DRAFT</c>.</summary>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no draft is pending;
    /// <see cref="RejectionReason.INSUFFICIENT_FUNDS"/> when the run cannot afford the reroll;
    /// otherwise accepted, with the cost row.
    /// </returns>
    internal static HandlerResult Handle(RerollDraftCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.DraftPending)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var economy = DraftEconomyTuning.Read(input.Context.Content);

        if (run.BalanceOf(CurrencyId.GOLD) < economy.RerollGoldCost)
        {
            return HandlerResult.Reject(RejectionReason.INSUFFICIENT_FUNDS);
        }

        var cost = run.MoveCurrency(CurrencyId.GOLD, -economy.RerollGoldCost, CostReason);

        // Discarded on purpose — see this type's remarks. Consuming the draws is the reroll.
        _ = PickPerk.GenerateCurrentOptions(input, run, out _);

        return HandlerResult.Accept(cost);
    }
}
