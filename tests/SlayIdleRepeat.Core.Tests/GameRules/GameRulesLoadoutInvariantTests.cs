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
/// 🔴 The exit-side half of the equipped-item invariant, driven through <c>GameRules.Execute</c>:
/// a command that destroys an item without taking it off the hero fails <b>on that command</b>,
/// rather than being accepted, persisted, and bricking the account from the next one onwards.
/// </summary>
/// <remarks>
/// <para>
/// Driven over a hand-built dispatch table rather than a real command, so the invariant is pinned
/// independently of whichever handlers happen to destroy an item today. The table seam exists for
/// exactly this: <c>Execute</c> is internal <em>"so the domain test suite can drive it against
/// shapes never committed to production"</em>.
/// </para>
/// <para>
/// 🔴 <b>This file used to claim "no shipped handler can break the pairing", and that claim was
/// false when it was written.</b> M4-04's <c>MERGE</c> and <c>SALVAGE</c> both destroyed items
/// through <c>Inventory.Remove</c> without clearing the slot, leaving <c>Player.DiscardItem</c> with
/// no production caller at all. Nothing caught it because <c>EQUIP</c> was deferred for the whole of
/// M4, so no command could fill a slot and no fixture reached the state; M7-00d then wired
/// <c>EQUIP</c> and made it reachable. Fixed in the M4 review. The real handler cases live beside
/// their commands — <c>MergeTests.Merging_away_a_worn_input_takes_it_off_the_hero</c> and
/// <c>SalvageTests.Salvaging_a_worn_item_takes_it_off_the_hero</c> — because a claim about what
/// production handlers do belongs where those handlers are tested, not here.
/// </para>
/// <para>
/// The claim is about WHEN the failure happens. <c>Player.Rehydrate</c> already refuses such a row on
/// the way IN, and <c>GameRules.Clone</c> runs that on every command — so with the exit-side check
/// removed, the offending command still returns success and its state is still handed back to be
/// persisted, and only the NEXT command throws. By then the corrupt row is the stored one.
/// </para>
/// </remarks>
public sealed class GameRulesLoadoutInvariantTests
{
    private const string Worn = "GI_WORN";

    private static readonly InventoryTuning Stock =
        InventoryTuning.Read(InventoryDocuments.Shipped);

    /// <summary>
    /// 🔴 A handler that empties the stock and leaves the slot behind fails the command it rode on.
    /// </summary>
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
    /// The negative control, and the one that makes the case above mean something: the SAME removal
    /// through <c>Player.DiscardItem</c> is accepted, because it takes the item off the hero too.
    /// </summary>
    /// <remarks>
    /// Without this, the rule above would hold just as well over a check that refused every command
    /// touching the stock at all — which would make the forge unimplementable rather than safe.
    /// </remarks>
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
    /// Destroying an item the hero is <b>not</b> wearing leaves the loadout alone.
    /// </summary>
    /// <remarks>
    /// The second negative control: a seam that unequipped indiscriminately would pass the case
    /// above while stripping the hero every time anything was salvaged.
    /// </remarks>
    [Fact]
    public void Destroying_an_unworn_item_leaves_the_hero_dressed()
    {
        var player = Worlds.Rehydrated(PlayerSnapshots.With(
            inventory: new InventorySnapshot(
                0,
                [Inventories.Persist(Inventories.Item(Worn)), Inventories.Persist(Inventories.Item("GI_SPARE"))],
                []),
            loadout: new LoadoutSnapshot(PlayerSnapshots.Gear((GearSlot.WEAPON, Worn)))));

        player.DiscardItem(new GearInstanceId("GI_SPARE"), Stock).ShouldBeTrue();

        player.Loadout.TryGet(GearSlot.WEAPON, out var item).ShouldBeTrue();
        item.Value.ShouldBe(Worn);
    }

    /// <summary>Discarding an item the player does not own changes nothing and says so.</summary>
    [Fact]
    public void Discarding_an_item_the_player_does_not_own_answers_false()
    {
        var player = Worlds.Rehydrated(PlayerSnapshots.Valid);

        player.DiscardItem(new GearInstanceId("GI_GONE"), Stock).ShouldBeFalse();
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
