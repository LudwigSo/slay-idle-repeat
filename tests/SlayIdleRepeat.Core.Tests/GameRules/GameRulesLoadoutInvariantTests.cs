using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔴 The exit-side half of the equipped-item invariant, driven through <c>GameRules.Execute</c>
/// over a hand-built table so the invariant is pinned independently of whichever handlers destroy
/// an item today (the real handler cases live beside their commands, in <c>MergeTests</c> and
/// <c>SalvageTests</c>).
/// </summary>
/// <remarks>
/// The claim is about WHEN the failure happens: <c>Player.Rehydrate</c> refuses such a row on the
/// way IN, so without the exit-side check the offending command still returns success, its state is
/// persisted, and only the NEXT command throws — by then the corrupt row is the stored one.
/// </remarks>
public sealed class GameRulesLoadoutInvariantTests
{
    private const string Worn = "GI_WORN";

    private static readonly InventoryTuning Stock =
        InventoryTuning.Read(InventoryDocuments.Shipped);

    [Fact]
    public void A_command_that_destroys_an_equipped_item_without_unequipping_it_fails_that_command()
    {
        var thrown = Should.Throw<InvalidOperationException>(() => SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable(RemoveFromStockOnly),
            Wearing(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context));

        thrown.Message.ShouldContain(Worn);
        thrown.Message.ShouldContain("Loadout.WithoutItem");
    }

    /// <summary>
    /// The negative control: without it, the rule above would hold just as well over a check that
    /// refused every command touching the stock at all.
    /// </summary>
    [Fact]
    public void The_same_removal_through_the_seam_is_accepted_and_takes_the_item_off_the_hero()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable(DiscardThroughTheSeam),
            Wearing(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Loadout.EquippedCount.ShouldBe(0);
        result.NewState.Player.Inventory.Stored.ShouldBeEmpty();
    }

    /// <summary>
    /// The second negative control: a seam that unequipped indiscriminately would pass the case
    /// above while stripping the hero every time anything was salvaged.
    /// </summary>
    [Fact]
    public void Destroying_an_unworn_item_leaves_the_hero_dressed()
    {
        var state = new WorldSlice(
            Worlds.Rehydrated(PlayerSnapshots.With(
                inventory: new InventorySnapshot(
                    0,
                    [Inventories.Persist(Inventories.Item(Worn)), Inventories.Persist(Inventories.Item("GI_SPARE"))],
                    []),
                loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((GearSlot.WEAPON, Worn))))),
            null);

        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                input.Player.DiscardItem(new GearInstanceId("GI_SPARE"), Stock).ShouldBeTrue();

                return HandlerResult.Accept();
            }),
            state,
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out var item).ShouldBeTrue();
        item.Value.ShouldBe(Worn);
        result.NewState.Player.Inventory.Stored.ShouldHaveSingleItem();
    }

    /// <summary>Discarding an item the player does not own changes nothing and says so.</summary>
    [Fact]
    public void Discarding_an_item_the_player_does_not_own_answers_false()
    {
        var result = SlayIdleRepeat.Core.GameRules.Execute(
            Worlds.MetaTable((_, input) =>
            {
                input.Player.DiscardItem(new GearInstanceId("GI_GONE"), Stock).ShouldBeFalse();

                return HandlerResult.Accept();
            }),
            Worlds.OutsideARun(),
            new Worlds.MetaFixtureCommand(),
            Worlds.Context);

        result.Accepted.ShouldBeTrue();
    }

    /// <summary>A slice whose player wears one item and holds exactly that item in stock.</summary>
    private static WorldSlice Wearing() =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(
                inventory: new InventorySnapshot(0, [Inventories.Persist(Inventories.Item(Worn))], []),
                loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((GearSlot.WEAPON, Worn))))),
            null);

    /// <summary>The wrong way: the item leaves the stock and the slot still names it.</summary>
    private static HandlerResult RemoveFromStockOnly(Worlds.MetaFixtureCommand command, HandlerInput input)
    {
        input.Player.Inventory.Remove(new GearInstanceId(Worn), Stock);

        return HandlerResult.Accept();
    }

    /// <summary>The right way: one call, both halves.</summary>
    private static HandlerResult DiscardThroughTheSeam(Worlds.MetaFixtureCommand command, HandlerInput input)
    {
        input.Player.DiscardItem(new GearInstanceId(Worn), Stock);

        return HandlerResult.Accept();
    }
}
