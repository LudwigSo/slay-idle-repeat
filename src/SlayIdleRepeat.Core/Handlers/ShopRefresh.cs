using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SHOP_REFRESH</c> handler: restocks the in-run shop under its refresh economy (1 free
/// refresh per visit, then <c>AD_SHOP_REFRESH</c>, 2/run, then unavailable).
/// </summary>
/// <remarks>
/// Refuses every call today for the same reason as <see cref="ShopBuy"/>: the run has nowhere yet to
/// track an active shop offer or refreshes used this visit, so there is nothing to legally refresh.
/// </remarks>
internal static class ShopRefresh
{
    /// <summary>Applies <c>SHOP_REFRESH</c>.</summary>
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
