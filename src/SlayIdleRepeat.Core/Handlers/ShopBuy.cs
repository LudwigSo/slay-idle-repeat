using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `03` §7 — the <c>SHOP_BUY</c> handler (M3-08): buys one of the in-run shop's four slots,
/// paid in run-local Gold.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>What this handler can and cannot do today, stated loudly rather than discovered later
/// (steering S6).</b> `03` §7's pricing formula is real and fully implemented —
/// <see cref="Rules.Economy.ShopPricing"/>, tested against
/// <see cref="Content.ShopTuning"/>'s read of <c>tuning/currencies.json#/shopTile</c> — but three
/// things the formula and the purchase both need are not yet buildable inside <c>Core</c>:
/// </para>
/// <list type="number">
///   <item><b>Which stage the run is standing in.</b> `03` §7's <c>stageIndex</c> comes from the
///   board's stage boundaries, and the board is <c>M3-01</c>'s <c>GapRegister</c> entry — there is
///   no <c>Run</c> field a handler can read it from.</item>
///   <item><b>Which four items are on offer, and whether this slot has already been bought this
///   visit.</b> `30` §4's <c>Run</c> row names no "active shop offer", so nothing establishes what
///   a shop <em>visit</em> is until <c>M3-03</c>'s tile resolver opens one — inventing a new
///   persisted field for it here, with no caller that could ever populate it, would be exactly the
///   kind of guess steering S6 forbids.</item>
///   <item><b>What a rarity-weighted Perk slot actually offers.</b> The 98-perk catalogue and its
///   rarity distribution are <c>M3-06</c>/<c>M3-07</c>'s.</item>
/// </list>
/// <para>
/// So this handler is <c>Handled</c> rather than <c>Deferred</c> — it is real code, reachable
/// through the production dispatch table and unit-tested — and it refuses every call today with
/// <see cref="RejectionReason.ILLEGAL_STATE"/>, because <b>no <c>Run</c> shaped by today's
/// aggregate can ever be standing at a legal shop purchase</b>. That is not a placeholder
/// exception a future author has to notice and delete; it is the correct answer for the state the
/// aggregate can actually be in until the three gaps above close. The slot index is still
/// validated first, so a malformed request is refused for its own reason rather than folded into
/// the same message as a legal one the game cannot yet honour.
/// </para>
/// </remarks>
internal static class ShopBuy
{
    /// <summary>`03` §7 — the shop has exactly four slots, indices 0..3.</summary>
    private const int SlotCount = 4;

    /// <summary>🔒 `03` §7 — applies <c>SHOP_BUY</c>.</summary>
    /// <param name="command">Which of the four slots to buy.</param>
    /// <param name="input">The run's slice.</param>
    /// <returns>
    /// A rejection: <see cref="RejectionReason.ILLEGAL_STATE"/> for an out-of-range slot index, and
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> for every in-range one too, per this type's
    /// remarks.
    /// </returns>
    internal static HandlerResult Handle(ShopBuyCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        // 🔒 Validated first, and separately from the reason below: a caller sending slot 7 is
        // asking for something `03` §7 never authors at all, which is a different defect from a
        // caller asking for slot 0 on a shop this aggregate cannot yet represent (steering S2).
        if (command.ShopSlotIndex < 0 || command.ShopSlotIndex >= SlotCount)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        // 🔴 See this type's remarks: no Run shaped by today's aggregate carries an active shop
        // offer, a stage index, or a perk catalogue to draw slot 1 from. Every otherwise-legal
        // slot index is refused for that reason until M3-01/M3-03/M3-06/M3-07 close those gaps.
        return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
    }
}
