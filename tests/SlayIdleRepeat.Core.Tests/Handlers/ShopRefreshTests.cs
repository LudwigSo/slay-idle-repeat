using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// SHOP_REFRESH: `03` §7's refresh economy — one free per visit, then two ad-gated per run, then
/// unavailable.
/// </summary>
public sealed class ShopRefreshTests
{
    private static WorldSlice AtAShop() =>
        SlayIdleRepeat.Core.GameRules.Apply(
            TileWorlds.OnTile(TileKind.Shop, gold: 100_000),
            new ResolveTileCommand(),
            TileWorlds.Context).NewState;

    private static CommandResult Refresh(WorldSlice state) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new ShopRefreshCommand(), TileWorlds.Context);

    [Fact]
    public void A_refresh_away_from_a_shop_is_rejected()
    {
        var result = Refresh(TileWorlds.OnNoTile());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A restock draws the NEXT block of the shop stream, not a re-roll of the same one.</summary>
    /// <remarks>
    /// The position is what this asserts, not the rows: two blocks of a deterministic stream can
    /// legitimately draw the same four ids, so an assertion that the offer CHANGED would be flaky by
    /// construction. What must never happen is the stream standing still, which would hand the
    /// player back the offer they had just rejected.
    /// </remarks>
    [Fact]
    public void A_refresh_advances_the_shop_stream_by_one_offer()
    {
        var open = AtAShop();
        var before = open.Run!.ShopOfferDraw!.Value;

        var refreshed = Refresh(open);

        refreshed.Accepted.ShouldBeTrue();
        refreshed.NewState.Run!.ShopOfferDraw!.Value.ShouldBe(before + RunShopOffer.DrawsPerOffer);
        refreshed.NewState.Run.StreamPosition(RngStreams.Shop)
            .ShouldBe(before + (2 * (ulong)RunShopOffer.DrawsPerOffer));
    }

    /// <summary>A refresh clears the purchase mask, because the offer those bits described is gone.</summary>
    [Fact]
    public void A_refresh_reopens_every_slot()
    {
        var open = AtAShop();

        var bought = SlayIdleRepeat.Core.GameRules.Apply(
            open, new ShopBuyCommand(RunShopOffer.RunBuffSlot), TileWorlds.Context);

        bought.Accepted.ShouldBeTrue();
        bought.NewState.Run!.IsShopSlotPurchased(RunShopOffer.RunBuffSlot).ShouldBeTrue();

        var refreshed = Refresh(bought.NewState);

        refreshed.NewState.Run!.IsShopSlotPurchased(RunShopOffer.RunBuffSlot).ShouldBeFalse(
            "a mark that outlived its offer would grey out an unrelated slot of the new one.");
    }

    /// <summary>
    /// 🔒 The cap: one free refresh, then <c>AD_SHOP_REFRESH</c> twice per RUN, then
    /// <see cref="RejectionReason.CAP_REACHED"/>.
    /// </summary>
    /// <remarks>
    /// The fourth is the assertion that matters. Without a cap the shop is re-rollable until it
    /// offers a Legendary, which is the whole reason `03` §7 counts refreshes at all.
    /// </remarks>
    [Fact]
    public void A_visit_gets_one_free_refresh_then_two_ad_refreshes_then_no_more()
    {
        var state = AtAShop();

        for (var refresh = 1; refresh <= 1 + SlayIdleRepeat.Core.Handlers.ShopRefresh.AdRefreshesPerRun; refresh++)
        {
            var result = Refresh(state);

            result.Accepted.ShouldBeTrue(
                "refresh " + refresh + " was refused " + result.Rejection +
                "; one free plus " + SlayIdleRepeat.Core.Handlers.ShopRefresh.AdRefreshesPerRun + " ad-gated are authored.");

            state = result.NewState;
        }

        var overCap = Refresh(state);

        overCap.Accepted.ShouldBeFalse();
        overCap.Rejection.ShouldBe(RejectionReason.CAP_REACHED);
    }

    /// <summary>
    /// 🔒 The free refresh is per VISIT and the ad refreshes are per RUN, so the second shop of a run
    /// is free again — and only once.
    /// </summary>
    /// <remarks>
    /// The two counters are the same number for one visit and diverge from the second, which is the
    /// only shape that tells them apart. Holding both in one counter would make the second shop
    /// either free forever or never free.
    /// </remarks>
    [Fact]
    public void The_free_refresh_returns_at_the_next_shop_but_the_ad_allowance_does_not()
    {
        var state = AtAShop();

        for (var refresh = 0; refresh < 1 + SlayIdleRepeat.Core.Handlers.ShopRefresh.AdRefreshesPerRun; refresh++)
        {
            state = Refresh(state).NewState;
        }

        // Leave, then walk into the next shop — the same run, a fresh visit.
        var left = SlayIdleRepeat.Core.GameRules.Apply(
            state, new ShopLeaveCommand(), TileWorlds.Context).NewState;

        left.Run!.ArriveAtTile((int)TileKind.Shop, linearIndex: 21, stage: 2);

        var second = SlayIdleRepeat.Core.GameRules.Apply(
            left, new ResolveTileCommand(), TileWorlds.Context).NewState;

        var free = Refresh(second);

        free.Accepted.ShouldBeTrue(
            "the free refresh is per VISIT and this is a new one — it was refused " + free.Rejection);

        var afterFree = Refresh(free.NewState);

        afterFree.Accepted.ShouldBeFalse(
            "…and the ad allowance is per RUN, so it is already spent. A second free refresh here " +
            "would mean the two counters had been collapsed into one.");
        afterFree.Rejection.ShouldBe(RejectionReason.CAP_REACHED);
    }

    /// <summary>A run-less slice still throws, the same guard every other run command hits.</summary>
    [Fact]
    public void A_run_less_slice_throws_rather_than_rejects()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(
                    Worlds.OutsideARun(), new ShopRefreshCommand(), Worlds.Context))
            .Message.ShouldContain("SHOP_REFRESH", Case.Sensitive);
    }
}
