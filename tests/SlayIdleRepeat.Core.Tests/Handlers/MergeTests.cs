using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>MERGE</c> end to end: the identities, the legality, the price, and what the stock holds
/// afterwards.
/// </summary>
public sealed class MergeTests
{
    private static GameContext Context => Worlds.Drawing(ForgeWorlds.Seed);

    private static MergeCommand Command(params string[] ids) =>
        new(ids.Select(id => new GearInstanceId(id)).ToArray(), dustSubstituted: false);

    private static GearInstance[] Triple(
        Rarity rarity = Rarity.C, int enhanceLevel = 0, bool locked = false) =>
    [
        Inventories.Item("a", rarity: rarity, enhanceLevel: enhanceLevel, locked: locked),
        Inventories.Item("b", rarity: rarity, enhanceLevel: enhanceLevel),
        Inventories.Item("c", rarity: rarity, enhanceLevel: enhanceLevel),
    ];

    /// <summary>Three matching items become one of the next band, and the other two are gone.</summary>
    [Fact]
    public void A_legal_fusion_leaves_one_item_of_the_next_band_where_three_stood()
    {
        var result = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeTrue();

        var stock = result.NewState.Player.Inventory.Stored;

        stock.Count.ShouldBe(1);
        stock[0].InstanceId.ShouldBe(new GearInstanceId("a"));
        stock[0].Rarity.ShouldBe(Rarity.B);
    }

    /// <summary>
    /// 🔴 Merging away an item the hero is <b>wearing</b> takes it off the hero in the same command.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The consumed inputs are the ones at risk, not the surviving one.</b> A fusion keeps the
    /// first input's identity — <c>Inventory.Replace</c> writes the output over <c>a</c>'s slot — so
    /// a hero wearing <c>a</c> is unharmed and a hero wearing <c>b</c> or <c>c</c> is left naming an
    /// item that no longer exists. This case wears <c>b</c> for that reason: wearing <c>a</c> would
    /// pass against the defect.
    /// </para>
    /// <para>
    /// ⚠️ <b>What a failure looks like is a throw, not a bad answer.</b>
    /// <c>Player.RequireLoadoutResolves</c> runs after the handler and throws
    /// <see cref="InvalidOperationException"/> out of <c>Apply</c>, because a slot naming a destroyed
    /// item is a handler defect rather than an illegal player action.
    /// </para>
    /// </remarks>
    [Fact]
    public void Merging_away_a_worn_input_takes_it_off_the_hero()
    {
        var world = ForgeWorlds.Wearing((GearSlot.WEAPON, "b"), Triple());

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeTrue(
            "fusing away an item the hero happens to be wearing is a legal action. A throw out of " +
            "Apply means the input was destroyed with the weapon slot still naming it.");

        result.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out _).ShouldBeFalse(
            "the consumed input was destroyed and the weapon slot still names it, so the hero is " +
            "wearing an item the stock no longer holds.");
    }

    /// <summary>
    /// 🔒 The whole stock is compared by canonical bytes, not by record equality: a fusion changes
    /// a collection, and a synthesized <c>Equals</c> compares one by reference (steering S17).
    /// </summary>
    [Fact]
    public void The_stock_after_a_fusion_is_exactly_the_output_beside_what_was_untouched()
    {
        var before = ForgeWorlds.Holding(
            [.. Triple(), Inventories.Item("spectator", rarity: Rarity.S)]);

        var after = GameRules.Apply(before, Command("a", "b", "c"), Context);

        var expected = ForgeWorlds.Holding(
            Inventories.Item(
                "a",
                rarity: Rarity.B,
                affixes: after.NewState.Player.Inventory.Stored[0].Affixes),
            Inventories.Item("spectator", rarity: Rarity.S));

        ForgeWorlds.StockBytes(after.NewState).ShouldBe(ForgeWorlds.StockBytes(expected));
    }

    /// <summary>The fusion charges the Crown price of the band it lands on.</summary>
    /// <param name="rarity">The band the inputs share.</param>
    /// <param name="crowns">The price of the band it lands on.</param>
    [Theory]
    [InlineData(Rarity.C, 120)]
    [InlineData(Rarity.B, 600)]
    [InlineData(Rarity.A, 3000)]
    [InlineData(Rarity.S, 15000)]
    public void A_fusion_charges_the_Crown_price_of_the_band_it_lands_on(Rarity rarity, long crowns)
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple(rarity)), Command("a", "b", "c"), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.CROWNS).ShouldBe(-crowns);
    }

    /// <summary>A fusion without dust moves no dust at all.</summary>
    [Fact]
    public void A_fusion_without_dust_moves_no_dust()
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(0);
    }

    /// <summary>A dust-filled slot charges the dust price of the INPUT band, on top of the Crowns.</summary>
    [Fact]
    public void A_dust_filled_slot_charges_the_dust_price_of_the_input_band()
    {
        var world = ForgeWorlds.Holding(
            Inventories.Item("a", rarity: Rarity.A),
            Inventories.Item("b", rarity: Rarity.A));

        var command = new MergeCommand(
            [new GearInstanceId("a"), new GearInstanceId("b")], dustSubstituted: true);

        var result = GameRules.Apply(world, command, Context);

        result.Accepted.ShouldBeTrue();
        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(-800);
        ForgeWorlds.Moved(result.Events, CurrencyId.CROWNS).ShouldBe(-3000);
        result.NewState.Player.Inventory.Stored.Count.ShouldBe(1);
    }

    /// <summary>An identity the player does not own is a rejection about the request, not the selection.</summary>
    [Fact]
    public void An_identity_the_player_does_not_own_is_refused_as_not_owned()
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple()), Command("a", "b", "nobody"), Context);

        result.Rejection.ShouldBe(RejectionReason.NOT_OWNED);
    }

    /// <summary>🔒 A locked item is excluded from merge selection, which is what the lock is for.</summary>
    [Fact]
    public void A_locked_input_is_refused()
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple(locked: true)), Command("a", "b", "c"), Context);

        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 The first production emitter of <c>INVENTORY_FULL</c>. An item waiting in overflow is not
    /// acted on: it is neither unknown nor locked, and collapsing it onto either would show the
    /// player the wrong thing to do about it.
    /// </summary>
    [Fact]
    public void An_input_waiting_in_overflow_is_refused_as_inventory_full()
    {
        var world = ForgeWorlds.Overflowing(Inventories.Item("held"));

        var result = GameRules.Apply(world, Command("held"), Context);

        result.Rejection.ShouldBe(RejectionReason.INVENTORY_FULL);
    }

    /// <summary>Every way the selection itself is illegal reaches the player as one domain reason.</summary>
    /// <param name="ids">The identities the client named.</param>
    [Theory]
    [InlineData("a,b")]
    [InlineData("a,b,b")]
    [InlineData("a,b,c,d")]
    public void A_selection_that_is_not_a_legal_fusion_is_refused(string ids)
    {
        var world = ForgeWorlds.Holding([.. Triple(), Inventories.Item("d")]);

        GameRules.Apply(world, Command(ids.Split(',')), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A wallet that does not cover the price is a shortfall, not an illegal selection.</summary>
    [Fact]
    public void A_wallet_that_does_not_cover_the_price_is_refused_as_a_shortfall()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.CROWNS, 119)), Triple());

        GameRules.Apply(world, Command("a", "b", "c"), Context)
            .Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    /// <summary>Dust the player does not hold is the same shortfall, on the other column.</summary>
    [Fact]
    public void Dust_the_player_does_not_hold_is_refused_as_a_shortfall()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.CROWNS, 1_000_000), (CurrencyId.MERGE_DUST, 49)),
            Inventories.Item("a"),
            Inventories.Item("b"));

        var command = new MergeCommand(
            [new GearInstanceId("a"), new GearInstanceId("b")], dustSubstituted: true);

        GameRules.Apply(world, command, Context).Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    /// <summary>🔒 A refused fusion charges nothing and consumes nothing.</summary>
    /// <remarks>
    /// 🔴 The expected bytes are taken BEFORE <c>Apply</c>. Reading them off <c>world</c> afterwards
    /// would compare the slice against itself — <c>Apply</c> hands the caller's own slice back on a
    /// rejection — so the assertion would hold however badly the handler had written it.
    /// </remarks>
    [Fact]
    public void A_refused_fusion_leaves_the_stock_and_the_wallet_exactly_where_they_stood()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.CROWNS, 119)), Triple());

        var before = ForgeWorlds.StockBytes(world);

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeFalse();
        result.Events.ShouldBeEmpty();
        ForgeWorlds.StockBytes(result.NewState).ShouldBe(before);
        world.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(119);
    }

    /// <summary>Nothing fuses out of the top band, however well funded the player is.</summary>
    [Fact]
    public void A_fusion_at_the_top_band_is_refused()
    {
        GameRules.Apply(
                ForgeWorlds.Holding(Triple(Rarity.SS)), Command("a", "b", "c"), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 The fused item stays IN THE STOCK even when the stock is full and overflow is waiting.
    /// Removing all three inputs and filing the output afterwards is not the same edit: the three
    /// opened slots reclaim from overflow first, and the item the player just paid for would land in
    /// overflow itself. The fixture holds three waiting items precisely so the two spellings part.
    /// </summary>
    [Fact]
    public void A_fusion_out_of_a_full_stock_leaves_its_output_stored_rather_than_in_overflow()
    {
        var world = ForgeWorlds.FullWithOverflow(held: 3, Triple());

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeTrue();

        var stock = result.NewState.Player.Inventory;

        stock.Stored.ShouldContain(item => item.InstanceId == new GearInstanceId("a"));
        stock.Held.ShouldNotContain(item => item.InstanceId == new GearInstanceId("a"));
        stock.Stored.Count.ShouldBe(Inventories.Tuning.CapacityAt(0));
        stock.Held.Count.ShouldBe(1, "two slots opened and two of the three waiting items took them");
    }

    /// <summary>The same command over the same seed produces byte-identical state.</summary>
    [Fact]
    public void The_same_fusion_over_the_same_seed_produces_the_same_stock()
    {
        var first = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);
        var second = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        ForgeWorlds.StockBytes(second.NewState).ShouldBe(ForgeWorlds.StockBytes(first.NewState));
    }
}
