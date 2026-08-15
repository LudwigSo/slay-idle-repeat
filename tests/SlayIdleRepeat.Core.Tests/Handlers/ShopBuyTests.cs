using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 `03` §7 / M3-08 — <c>SHOP_BUY</c>: dispatched through the production table, and refused for
/// every slot index today. See <c>Handlers.ShopBuy</c>'s remarks for why every otherwise-legal
/// call is <see cref="RejectionReason.ILLEGAL_STATE"/> until M3-01/M3-03/M3-06/M3-07 land the
/// board's stage boundaries, the shop-visit state and the perk catalogue.
/// </summary>
public sealed class ShopBuyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_in_range_slot_is_rejected_as_illegal_state_today(int slotIndex)
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(), new ShopBuyCommand(slotIndex), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(1000)]
    public void An_out_of_range_slot_index_is_rejected_too(int slotIndex)
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(), new ShopBuyCommand(slotIndex), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>🔒 P4: a rejected command's state is the caller's own slice, unchanged.</summary>
    [Fact]
    public void A_rejection_leaves_the_run_untouched()
    {
        var state = Worlds.InARun();
        var goldBefore = state.Run!.Gold;

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new ShopBuyCommand(3), Worlds.Context);

        result.NewState.ShouldBeSameAs(state);
        result.NewState.Run!.Gold.ShouldBe(goldBefore);
    }

    /// <summary>A run-less slice still throws, the same guard every other run command hits.</summary>
    [Fact]
    public void A_run_less_slice_throws_rather_than_rejects()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(
                    Worlds.OutsideARun(), new ShopBuyCommand(0), Worlds.Context))
            .Message.ShouldContain("SHOP_BUY", Case.Sensitive);
    }
}
