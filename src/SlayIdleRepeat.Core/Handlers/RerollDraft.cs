using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 M3-06, `14` §2.3 — the <c>REROLL_DRAFT</c> handler: redraws the perk draft (`06` §1's reroll
/// economy).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The cost is charged before anything is drawn</b>, on <c>Handlers.EventChoose</c>'s
/// precedent: affordability is checked first, so an unaffordable reroll costs the player nothing
/// and — because a rejected command's <c>RunRngScope</c> is discarded with the clone — consumes no
/// `14` §8.1 draw index either.
/// </para>
/// <para>
/// 🔒 <b>Rerolling means advancing the <c>draft</c> stream past the current options, not storing a
/// new set.</b> The three options a player sees are always "whatever
/// <c>PickPerk.GenerateCurrentOptions</c> derives from the run's committed <c>draft</c>-stream
/// position right now" (see <c>Handlers.PickPerk</c>'s remarks) — so a reroll's whole job is to move
/// that position forward, which drawing a (discarded) set of options already does. The draft stays
/// pending: this command never calls <c>Run.ClearDraftPending</c>.
/// </para>
/// <para>
/// ⚠️ <b>No reroll-count cap is enforced here, deliberately.</b> `06` §4's own text keys the reroll
/// economy on Gold, not a per-draft count, and no other document authors one; inventing a cap would
/// be exactly the plausible-looking hole steering S6 forbids.
/// </para>
/// </remarks>
internal static class RerollDraft
{
    /// <summary>The `30` §7 attribution token a reroll's cost is logged under.</summary>
    internal const string CostReason = "draft_reroll_cost";

    /// <summary>`06` §1 — applies <c>REROLL_DRAFT</c>.</summary>
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
        _ = PickPerk.GenerateCurrentOptions(input, run);

        return HandlerResult.Accept(cost);
    }
}
