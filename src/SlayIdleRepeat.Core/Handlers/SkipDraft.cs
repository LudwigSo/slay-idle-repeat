using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SKIP_DRAFT</c> handler: takes none of the offered perks.
/// </summary>
/// <remarks>
/// Pays the player, closes the draft, and draws nothing — unlike <c>PICK_PERK</c>/
/// <c>REROLL_DRAFT</c>, a skip never needs to know what the three options were.
/// </remarks>
internal static class SkipDraft
{
    /// <summary>Income-attribution token a skip's Gold reward is logged under.</summary>
    internal const string RewardReason = "draft_skip_reward";

    /// <summary>Applies <c>SKIP_DRAFT</c>.</summary>
    /// <returns><see cref="RejectionReason.ILLEGAL_STATE"/> when no draft is pending; otherwise accepted.</returns>
    internal static HandlerResult Handle(SkipDraftCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.DraftPending)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var economy = DraftEconomyTuning.Read(input.Context.Content);
        var reward = run.MoveCurrency(CurrencyId.GOLD, economy.SkipGoldReward, RewardReason);

        run.ClearDraftPending();

        return HandlerResult.Accept(reward);
    }
}
