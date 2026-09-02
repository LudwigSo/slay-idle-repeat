using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// The per-run ad-use counters' guards. Counting behaviour is covered at the <c>Apply</c> seam
/// (<c>ReviveTests</c>, <c>BeginSessionDrawSeamTests</c>); the caps themselves belong to the
/// handler that reads the content tuning, not to the aggregate.
/// </summary>
public sealed class RunAdUseTests
{
    private static Run WithAdUses(params (string Placement, long Uses)[] uses) =>
        Run.Rehydrate(RunSnapshots.With(adUses: RunSnapshots.AdUses(uses))).Value;

    /// <summary>
    /// The parameter is asserted, not just the type: <see cref="ArgumentOutOfRangeException"/>
    /// derives from <see cref="ArgumentException"/>, so the type alone is also satisfied by the
    /// amount guard.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_blank_placement_key_is_refused(string placementId)
    {
        var run = WithAdUses();

        Should.Throw<ArgumentException>(() => run.CountAdUse(placementId, 1))
              .ParamName.ShouldBe("placementId");

        Should.Throw<ArgumentException>(() => run.AdUseCount(placementId))
              .ParamName.ShouldBe("placementId");
    }

    [Fact]
    public void A_negative_amount_is_refused()
    {
        var run = WithAdUses(("AD_SHOP_REFRESH", 2));

        var act = () => run.CountAdUse("AD_SHOP_REFRESH", -1);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .ParamName.ShouldBe(
                  "amount",
                  "the other guard on this method throws over the key, and the exception type " +
                  "alone does not say which refusal this is.");

        run.AdUseCount("AD_SHOP_REFRESH").ShouldBe(2, "a refused advance changes nothing");
    }

    [Fact]
    public void An_overflowing_advance_is_refused()
    {
        var run = WithAdUses(("AD_DOUBLE_CHEST", long.MaxValue));

        var act = () => run.CountAdUse("AD_DOUBLE_CHEST", 1);

        Should.Throw<ArgumentOutOfRangeException>(act)
              .Message.ShouldMatchWildcard("*overflows a 64-bit count*");

        run.AdUseCount("AD_DOUBLE_CHEST").ShouldBe(long.MaxValue);
    }

    [Fact]
    public void The_exposed_map_is_a_live_view_and_cannot_be_mutated_through_its_reference()
    {
        var run = WithAdUses(("AD_CAMPFIRE_HEAL", 1));
        var exposed = run.AdUses;

        Should.Throw<NotSupportedException>(
            () => ((IDictionary<string, long>)exposed)["AD_CAMPFIRE_HEAL"] = 999);

        run.CountAdUse("AD_CAMPFIRE_HEAL", 1);

        exposed["AD_CAMPFIRE_HEAL"].ShouldBe(
            2,
            "the ad counters are mutated in place, so the view a caller took earlier reflects the " +
            "increment — the opposite of RngStreamPositions, which is replaced wholesale.");
    }
}
