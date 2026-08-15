using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Board.Resolution;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>CAMPFIRE_CHOOSE</c> handler: one of the campfire's three fixed options.
/// </summary>
/// <remarks>
/// Only option 0 (rest) is implemented. Upgrading a perk's tier and gaining reroll charges are
/// refused as <see cref="RejectionReason.ILLEGAL_STATE"/> rather than accepted as no-ops, since
/// neither drafted perks nor a reroll-charge count are tracked anywhere yet — accepting would tell
/// the client an option was applied when nothing happened. The pending tile is left in place on a
/// refusal, so the player can still choose rest.
/// </remarks>
internal static class CampfireChoose
{
    /// <summary>The campfire's rest option: heal a share of Max HP.</summary>
    internal const int RestChoiceIndex = 0;

    /// <summary>Upgrade one drafted perk's tier. Not yet tracked anywhere in this codebase.</summary>
    internal const int UpgradePerkChoiceIndex = 1;

    /// <summary>Gain 2 Reroll Charges. Not yet tracked anywhere in this codebase.</summary>
    internal const int RerollChargesChoiceIndex = 2;

    /// <summary>Applies <c>CAMPFIRE_CHOOSE</c>.</summary>
    /// <param name="command">Which of the three options.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no campfire is pending, for either of the
    /// two unimplemented options, or for an index outside the three; otherwise the rest is applied
    /// and the tile cleared.
    /// </returns>
    internal static HandlerResult Handle(CampfireChooseCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile || (TileKind)run.PendingTileKindValue != TileKind.Campfire)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        if (command.ChoiceIndex is UpgradePerkChoiceIndex or RerollChargesChoiceIndex or not RestChoiceIndex)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var result = CampfireResolver.Heal(input);

        run.ClearPendingTile();

        return result;
    }
}
