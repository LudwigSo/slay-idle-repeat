using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SHOP_BUY</c> handler: buys one of the in-run shop's four slots, paid in run-local Gold.
/// </summary>
/// <remarks>
/// <para>
/// The pricing formula is fully implemented, but the run has nowhere yet to track which stage it's
/// in, what's on offer this visit, or what a rarity-weighted Perk slot contains — none of that state
/// exists on <c>Run</c> yet. So this handler is real, dispatched, unit-tested code that refuses every
/// call with <see cref="RejectionReason.ILLEGAL_STATE"/> until those gaps close: no <c>Run</c> shaped
/// by today's aggregate can ever be standing at a legal purchase. The slot index is still validated
/// first, so a malformed request is refused for its own reason.
/// </para>
/// <para>
/// Because a visit can therefore buy nothing, it is <c>RESOLVE_TILE</c> that clears a shop tile and
/// lets the run walk away. The commit that makes a purchase legal here owes the shop its own
/// clearing step and has to revisit that branch.
/// </para>
/// </remarks>
internal static class ShopBuy
{
    /// <summary>The shop has exactly four slots, indices 0..3.</summary>
    private const int SlotCount = 4;

    /// <summary>Applies <c>SHOP_BUY</c>.</summary>
    /// <param name="command">Which of the four slots to buy.</param>
    /// <param name="input">The run's slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> for an out-of-range slot index, and for every
    /// in-range one too, per this type's remarks.
    /// </returns>
    internal static HandlerResult Handle(ShopBuyCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        if (command.ShopSlotIndex < 0 || command.ShopSlotIndex >= SlotCount)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // See this type's remarks: no Run shaped by today's aggregate carries an active shop offer.
        return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
    }
}
