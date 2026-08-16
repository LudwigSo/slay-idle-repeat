using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary><c>ENHANCE</c> end to end: the precondition, the price, and what the attempt leaves.</summary>
public sealed class EnhanceTests
{
    private static GameContext Context => Worlds.Drawing(ForgeWorlds.Seed);

    private static EnhanceCommand Command(string id = "blade") => new(new GearInstanceId(id));

    /// <summary>An attempt inside the certain band raises the level and charges its stones.</summary>
    [Fact]
    public void An_attempt_inside_the_certain_band_raises_the_level_and_charges_its_stones()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade", enhanceLevel: 0));

        var result = GameRules.Apply(world, Command(), Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Inventory.Stored[0].EnhanceLevel.ShouldBe(1);
        ForgeWorlds.Moved(result.Events, CurrencyId.ENHANCE_STONES).ShouldBe(-2);
    }

    /// <summary>Each level costs what the ladder authors for it, not a flat price.</summary>
    /// <param name="level">The level the item stands at.</param>
    /// <param name="stones">The stones the attempt costs.</param>
    [Theory]
    [InlineData(0, 2)]
    [InlineData(4, 8)]
    [InlineData(5, 12)]
    [InlineData(14, 200)]
    public void An_attempt_charges_the_stones_its_own_level_authors(int level, long stones)
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade", enhanceLevel: level));

        var result = GameRules.Apply(world, Command(), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.ENHANCE_STONES).ShouldBe(-stones);
    }

    /// <summary>
    /// 🔒 A LOCKED item can be enhanced. The lock excludes an item from auto-salvage and from merge
    /// selection — the two operations that consume it — and enhancement consumes nothing but stones
    /// and can neither destroy nor downgrade it.
    /// </summary>
    [Fact]
    public void A_locked_item_can_still_be_enhanced()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade", locked: true));

        var result = GameRules.Apply(world, Command(), Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Inventory.Stored[0].EnhanceLevel.ShouldBe(1);
        result.NewState.Player.Inventory.Stored[0].Locked.ShouldBeTrue();
    }

    /// <summary>An item the player does not own is refused as such.</summary>
    [Fact]
    public void An_item_the_player_does_not_own_is_refused_as_not_owned()
    {
        GameRules.Apply(ForgeWorlds.Holding(Inventories.Item("blade")), Command("other"), Context)
            .Rejection.ShouldBe(RejectionReason.NOT_OWNED);
    }

    /// <summary>An item waiting in overflow is not acted on.</summary>
    [Fact]
    public void An_item_waiting_in_overflow_is_refused_as_inventory_full()
    {
        GameRules.Apply(
                ForgeWorlds.Overflowing(Inventories.Item("held")), Command("held"), Context)
            .Rejection.ShouldBe(RejectionReason.INVENTORY_FULL);
    }

    /// <summary>
    /// An item at the ceiling is refused as a cap, distinct from the shortfall below and from the
    /// illegal-state catch-all — three different things for a player to do about it.
    /// </summary>
    [Fact]
    public void An_item_at_the_ceiling_is_refused_as_a_cap()
    {
        GameRules.Apply(
                ForgeWorlds.Holding(Inventories.Item("blade", enhanceLevel: 15)), Command(), Context)
            .Rejection.ShouldBe(RejectionReason.CAP_REACHED);
    }

    /// <summary>A wallet that does not cover the stones is a shortfall.</summary>
    [Fact]
    public void A_wallet_that_does_not_cover_the_stones_is_refused_as_a_shortfall()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.ENHANCE_STONES, 1)),
            Inventories.Item("blade"));

        GameRules.Apply(world, Command(), Context).Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    /// <summary>🔒 A refused attempt charges nothing and moves no item.</summary>
    /// <remarks>
    /// 🔴 The expected bytes are taken BEFORE <c>Apply</c>. Reading them off <c>world</c> afterwards
    /// would compare the slice against itself — <c>Apply</c> hands the caller's own slice back on a
    /// rejection — so the assertion would hold however badly the handler had written it.
    /// </remarks>
    [Fact]
    public void A_refused_attempt_leaves_the_stock_and_the_wallet_exactly_where_they_stood()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.ENHANCE_STONES, 1)), Inventories.Item("blade"));

        var before = ForgeWorlds.StockBytes(world);

        var result = GameRules.Apply(world, Command(), Context);

        result.Accepted.ShouldBeFalse();
        result.Events.ShouldBeEmpty();
        ForgeWorlds.StockBytes(result.NewState).ShouldBe(before);
        world.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(1);
    }

    /// <summary>
    /// 🔒 The stones are charged whether or not the attempt lands, and a failure leaves the level
    /// standing. Found by walking seeds rather than assumed, so the case fails loudly if the
    /// hardest level never fails.
    /// </summary>
    [Fact]
    public void A_failed_attempt_charges_its_stones_and_leaves_the_level_standing()
    {
        for (var seed = 1UL; seed < 200UL; seed++)
        {
            var result = GameRules.Apply(
                ForgeWorlds.Holding(Inventories.Item("blade", enhanceLevel: 14)),
                Command(),
                Worlds.Drawing(seed));

            var item = result.NewState.Player.Inventory.Stored[0];

            if (item.EnhanceLevel != 14)
            {
                continue;
            }

            result.Accepted.ShouldBeTrue();
            item.EnhanceFailures.ShouldBe(1);
            ForgeWorlds.Moved(result.Events, CurrencyId.ENHANCE_STONES).ShouldBe(-200);
            return;
        }

        throw new InvalidOperationException(
            "No seed under two hundred failed an attempt at the hardest level, whose authored chance " +
            "is one in four — so this case has been asserting over a success. Either the draw is not " +
            "reaching the handler or the ladder has moved.");
    }

    /// <summary>The item keeps its identity across an attempt, so the stock does not grow.</summary>
    [Fact]
    public void An_attempt_keeps_the_items_identity_and_leaves_the_stock_the_same_size()
    {
        var world = ForgeWorlds.Holding(
            Inventories.Item("blade"), Inventories.Item("spectator", rarity: Rarity.S));

        var result = GameRules.Apply(world, Command(), Context);

        result.NewState.Player.Inventory.Stored.Count.ShouldBe(2);
        result.NewState.Player.Inventory.Stored[0].InstanceId.ShouldBe(new GearInstanceId("blade"));
    }
}
