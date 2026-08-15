using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// 🔒 `03` §7 — the <c>SHOP_REFRESH</c> handler (M3-08): restocks the in-run shop under its
/// refresh economy (1 free refresh per visit, then <c>AD_SHOP_REFRESH</c>, 2/run, then
/// unavailable).
/// </summary>
/// <remarks>
/// 🔴 Refuses every call today, for exactly the reason <see cref="ShopBuy"/>'s remarks state at
/// length: `30` §4's <c>Run</c> row names no "active shop offer" or "refreshes used this visit",
/// so nothing establishes what a shop <em>visit</em> is until <c>M3-03</c>'s tile resolver opens
/// one. A refresh with nothing to refresh is not a state this handler can legally act on, so it is
/// <see cref="RejectionReason.ILLEGAL_STATE"/> — real, dispatched, unit-tested code, not a
/// placeholder a future author has to notice and delete.
/// </remarks>
internal static class ShopRefresh
{
    /// <summary>🔒 `03` §7 — applies <c>SHOP_REFRESH</c>.</summary>
    /// <param name="command">The no-payload refresh request.</param>
    /// <param name="input">The run's slice.</param>
    /// <returns><see cref="RejectionReason.ILLEGAL_STATE"/>, per this type's remarks.</returns>
    internal static HandlerResult Handle(ShopRefreshCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
    }
}
