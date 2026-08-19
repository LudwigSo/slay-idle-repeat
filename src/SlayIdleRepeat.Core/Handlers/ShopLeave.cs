using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SHOP_LEAVE</c> handler: closes the shop the run is standing at and clears the tile, so the
/// run can roll again.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The tile's clearing step, and the reason the Shop tile needs a command of its own.</b> Every
/// other tile finishes on the command that resolves it. A shop does not: the player buys, refreshes,
/// buys again, and only they know when they are done — so a shop tile that cleared itself on arrival
/// (which is what it used to do) could never sell anything, and one that never cleared would end the
/// run.
/// </para>
/// <para>
/// Idempotent by construction: a resend after the shop has closed finds no pending shop tile and is
/// refused, which is a rejection rather than a second close, and closing is the last thing that can
/// happen at a shop anyway.
/// </para>
/// </remarks>
internal static class ShopLeave
{
    /// <summary>Applies <c>SHOP_LEAVE</c>.</summary>
    /// <param name="command">The no-payload request.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when the run is not standing on a pending Shop
    /// tile; otherwise accepted, with the visit closed and the tile cleared.
    /// </returns>
    internal static HandlerResult Handle(ShopLeaveCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile || (TileKind)run.PendingTileKindValue != TileKind.Shop)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // Both, and in this order: the visit's offer, purchase mask and refresh count go together
        // (Run.CloseShop), and then the tile itself, which is what lets ROLL_DICE fire again.
        run.CloseShop();
        run.ClearPendingTile();

        return HandlerResult.Accept();
    }
}
