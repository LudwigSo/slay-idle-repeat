using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;

namespace SlayIdleRepeat.Core.Handlers;

/// <summary>
/// The <c>SHOP_REFRESH</c> handler: restocks the in-run shop under `03` §7's refresh economy — one
/// free refresh per visit, then <c>AD_SHOP_REFRESH</c> (2 per run), then unavailable.
/// </summary>
/// <remarks>
/// <para>
/// The refresh advances the <c>shop</c> stream by exactly one offer's worth of draws
/// (<see cref="RunShopOffer.DrawsPerOffer"/>) and records the new position, so the restocked shop is
/// the NEXT block of the run's own sequence rather than a re-roll of the same one. The purchase mask
/// is cleared with it — the offer behind those bits is gone.
/// </para>
/// <para>
/// 🔒 <b>An ad refresh is counted against the run, a free one against the visit.</b> The free
/// allowance is "per shop visit" and resets when the run walks into the next shop; the ad allowance
/// is "2/run" and does not. They are two different counters because they are two different periods,
/// and holding both in one would make the second shop of a run either free again or never free.
/// </para>
/// <para>
/// ⚠️ The ad refresh is counted here but NOT gated on a watched ad: <c>ShopRefreshCommand</c> carries
/// no payload that could name one, and no ad-attribution record reaches the domain
/// (<c>CLAIM_AD_REWARD</c> is still deferred). So today the ad allowance is simply two further free
/// refreshes. What is real is the CAP — a player cannot refresh more than the document allows — and
/// that is the half that keeps the shop from being re-rollable until it offers a Legendary.
/// </para>
/// </remarks>
internal static class ShopRefresh
{
    /// <summary>The ad placement `03` §7 names for the paid refreshes, counted on the run.</summary>
    internal const string PlacementId = "AD_SHOP_REFRESH";

    /// <summary>How many ad-gated refreshes a run gets, after the free one per visit. `03` §7's "2/run".</summary>
    internal const int AdRefreshesPerRun = 2;

    /// <summary>Applies <c>SHOP_REFRESH</c>.</summary>
    /// <param name="command">The no-payload refresh request.</param>
    /// <param name="input">The cloned, already-caught-up, in-run slice.</param>
    /// <returns>
    /// <see cref="RejectionReason.ILLEGAL_STATE"/> when no shop is open;
    /// <see cref="RejectionReason.CAP_REACHED"/> when this visit's free refresh and the run's ad
    /// refreshes are all spent; otherwise accepted, with the shop restocked.
    /// </returns>
    internal static HandlerResult Handle(ShopRefreshCommand command, HandlerInput input)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(input);

        var run = input.Run;

        if (!run.HasPendingTile ||
            (TileKind)run.PendingTileKindValue != TileKind.Shop ||
            !run.HasOpenShop)
        {
            return HandlerResult.Reject(RejectionReason.ILLEGAL_STATE);
        }

        var free = ShopTuning.Read(input.Context.Content).FreeRefreshesPerVisit;
        var usesFreeRefresh = run.ShopRefreshesUsedThisVisit < free;

        if (!usesFreeRefresh && run.AdUseCount(PlacementId) >= AdRefreshesPerRun)
        {
            return HandlerResult.Reject(RejectionReason.CAP_REACHED);
        }

        if (!usesFreeRefresh)
        {
            run.CountAdUse(PlacementId, 1);
        }

        run.StockShop(ShopStocking.Draw(input), countsAsRefresh: true);

        return HandlerResult.Accept();
    }
}
