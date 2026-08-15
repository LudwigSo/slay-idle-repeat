using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>SHOP_REFRESH: dispatched through the production table, and refused today.</summary>
public sealed class ShopRefreshTests
{
    [Fact]
    public void A_refresh_is_rejected_as_illegal_state_today()
    {
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            Worlds.InARun(), new ShopRefreshCommand(), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A rejected command's state is the caller's own slice, unchanged.</summary>
    [Fact]
    public void A_rejection_leaves_the_run_untouched()
    {
        var state = Worlds.InARun();

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new ShopRefreshCommand(), Worlds.Context);

        result.NewState.ShouldBeSameAs(state);
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
