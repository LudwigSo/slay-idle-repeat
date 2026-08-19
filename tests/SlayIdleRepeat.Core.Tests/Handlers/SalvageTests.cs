using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary><c>SALVAGE</c> end to end: the batch's preconditions, its payout, and its all-or-nothing rule.</summary>
public sealed class SalvageTests
{
    private static GameContext Context => Worlds.Context;

    private static SalvageCommand Command(params string[] ids) =>
        new(ids.Select(id => new GearInstanceId(id)).ToArray());

    /// <summary>One item pays its band's dust and leaves the stock empty.</summary>
    [Fact]
    public void One_item_pays_its_bands_dust_and_leaves_the_stock_empty()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade", rarity: Rarity.A));

        var result = GameRules.Apply(world, Command("blade"), Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Inventory.Stored.ShouldBeEmpty();
        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(160);
    }

    /// <summary>An enhanced item additionally refunds a share of what its level cost.</summary>
    [Fact]
    public void An_enhanced_item_refunds_a_share_of_the_stones_its_level_cost()
    {
        var world = ForgeWorlds.Holding(
            Inventories.Item("blade", rarity: Rarity.A, enhanceLevel: 10));

        var result = GameRules.Apply(world, Command("blade"), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(400);
        ForgeWorlds.Moved(result.Events, CurrencyId.ENHANCE_STONES).ShouldBe(85);
    }

    /// <summary>A batch pays the sum of its items and destroys all of them.</summary>
    [Fact]
    public void A_batch_pays_the_sum_of_its_items()
    {
        var world = ForgeWorlds.Holding(
            Inventories.Item("a", rarity: Rarity.C),
            Inventories.Item("b", rarity: Rarity.B),
            Inventories.Item("c", rarity: Rarity.SS));

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(10 + 40 + 2560);
        result.NewState.Player.Inventory.Stored.ShouldBeEmpty();
    }

    /// <summary>An unenhanced batch refunds no stones, so no zero-delta row is emitted for them.</summary>
    [Fact]
    public void An_unenhanced_batch_emits_no_stone_row_at_all()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade"));

        var result = GameRules.Apply(world, Command("blade"), Context);

        result.Events.OfType<Core.Events.CurrencyChanged>()
            .ShouldNotContain(e => e.Id == CurrencyId.ENHANCE_STONES);
    }

    /// <summary>🔒 A locked item is excluded from salvage, which is what the lock is for.</summary>
    [Fact]
    public void A_locked_item_is_refused()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade", locked: true));

        GameRules.Apply(world, Command("blade"), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 A batch naming one locked item destroys none of the others — salvage cannot be undone, so
    /// a partial application is worse than a refusal.
    /// </summary>
    /// <remarks>
    /// What this pins is <c>Apply</c>'s discard, not the handler's ordering: mutation showed that
    /// moving the removals inside the validation pass stays green, because the handler mutates a
    /// clone that a refusal throws away.
    /// </remarks>
    [Fact]
    public void A_batch_naming_one_locked_item_destroys_none_of_the_others()
    {
        var world = ForgeWorlds.Holding(
            Inventories.Item("loose"),
            Inventories.Item("locked", locked: true),
            Inventories.Item("also_loose"));

        // Taken BEFORE Apply: a rejection hands the caller's own slice back, so bytes read off
        // `world` afterwards would compare the slice against itself.
        var before = ForgeWorlds.StockBytes(world);

        var result = GameRules.Apply(world, Command("loose", "locked", "also_loose"), Context);

        result.Accepted.ShouldBeFalse();
        result.Events.ShouldBeEmpty();
        ForgeWorlds.StockBytes(result.NewState).ShouldBe(before);
    }

    /// <summary>An identity the player does not own refuses the batch before anything is destroyed.</summary>
    [Fact]
    public void An_identity_the_player_does_not_own_refuses_the_whole_batch()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("a"), Inventories.Item("b"));

        // Taken BEFORE Apply, for the reason recorded on the locked-item case above.
        var before = ForgeWorlds.StockBytes(world);

        var result = GameRules.Apply(world, Command("a", "nobody"), Context);

        result.Rejection.ShouldBe(RejectionReason.NOT_OWNED);
        ForgeWorlds.StockBytes(result.NewState).ShouldBe(before);
    }

    /// <summary>
    /// 🔴 Salvaging an item the hero is <b>wearing</b> takes it off the hero in the same command.
    /// The failure this pins is Apply throwing: destroying a worn item without clearing its slot
    /// leaves a state <c>Player.RequireLoadoutResolves</c> refuses by exception, on a command that
    /// was entirely legal.
    /// </summary>
    [Fact]
    public void Salvaging_a_worn_item_takes_it_off_the_hero()
    {
        var world = ForgeWorlds.Wearing(
            (GearSlot.WEAPON, "blade"), Inventories.Item("blade", rarity: Rarity.A));

        var result = GameRules.Apply(world, Command("blade"), Context);

        result.Accepted.ShouldBeTrue(
            "salvaging an item the hero happens to be wearing is a legal action. A refusal here " +
            "means the handler grew a guard instead of clearing the slot; a throw out of Apply " +
            "means it destroyed the item and left the slot naming it.");

        result.NewState.Player.Inventory.Stored.ShouldBeEmpty();

        result.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out _).ShouldBeFalse(
            "the item was destroyed and the weapon slot still names it, so the hero is wearing " +
            "something that no longer exists — the state RequireLoadoutResolves throws on.");
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
    /// One identity named twice would be paid for twice and destroyed once, so the batch is refused
    /// rather than quietly de-duplicated.
    /// </summary>
    [Fact]
    public void An_identity_named_twice_is_refused()
    {
        var world = ForgeWorlds.Holding(Inventories.Item("blade"));

        GameRules.Apply(world, Command("blade", "blade"), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A batch naming nothing is a command with no effect, refused rather than accepted.</summary>
    [Fact]
    public void A_batch_naming_nothing_is_refused()
    {
        GameRules.Apply(ForgeWorlds.Holding(Inventories.Item("blade")), Command(), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// Salvaging out of a full stock pulls the overflow item back in — the reclaim the container
    /// already owns, reached for the first time by a production command.
    /// </summary>
    [Fact]
    public void Salvaging_out_of_a_full_stock_reclaims_the_item_waiting_for_space()
    {
        var world = ForgeWorlds.Overflowing(Inventories.Item("held"));
        var first = world.Player.Inventory.Stored[0].InstanceId;

        var result = GameRules.Apply(world, Command(first.Value), Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Inventory.Held.ShouldBeEmpty();
        result.NewState.Player.Inventory.Stored
            .ShouldContain(item => item.InstanceId == new GearInstanceId("held"));
    }
}
